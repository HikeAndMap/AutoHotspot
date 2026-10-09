using System.ComponentModel;
using System.Diagnostics;

namespace AutoHotspot;

/// <summary>
/// The user-facing "Change IP..." flow: work out a free range, ask the user to confirm it, run the
/// elevated mode (one UAC prompt) and report what happened. Used by the dialog and the tray icon.
/// </summary>
internal static class HotspotIpChangeFlow
{
    private const int ErrorCancelled = 1223; // the user declined the UAC prompt
    private const string Caption = "AutoHotspot: hotspot IP";

    private static int running;

    public const string ItNote =
        "192.168.137.x is the range Microsoft reserves for the Mobile hotspot, so your network or IT should not use it. " +
        "Changing the hotspot IP is a workaround.";

    public static async Task RunAsync(IWin32Window? owner)
    {
        if (Interlocked.Exchange(ref running, 1) == 1)
            return;

        Form? hiddenOwner = null;
        try
        {
            if (owner == null)
                owner = hiddenOwner = CreateHiddenOwner();

            NetworkSnapshot snapshot;
            try
            {
                snapshot = await Task.Run(HotspotNetwork.Read);
            }
            catch (Exception ex)
            {
                Log.Write($"Reading the network state failed: {ex.Message}");
                MessageBox.Show(owner, $"Could not read the network state:\n\n{ex.Message}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            HotspotIpReport report = snapshot.Analyze();
            Ipv4Subnet? proposal = HotspotIpLogic.ProposeSubnet(report, snapshot.Others);
            if (proposal == null)
            {
                MessageBox.Show(owner, "Every range AutoHotspot knows how to pick is already in use on this PC, so it can't suggest a new hotspot IP.\n\n" + ItNote,
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string address = proposal.Value.FirstHost.ToString();
            if (MessageBox.Show(owner, BuildConfirmation(report, proposal.Value), Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Log.Write($"User confirmed changing the hotspot IP to {address}.");
            ChangeIpOutcome? outcome = await Task.Run(() => LaunchElevated(address));
            Report(owner, outcome, address, report.Actual?.Address.ToString() ?? report.ConfiguredAddress.ToString());
        }
        finally
        {
            hiddenOwner?.Dispose();
            Interlocked.Exchange(ref running, 0);
        }
    }

    public static string BuildConfirmation(HotspotIpReport report, Ipv4Subnet proposal)
    {
        var text = new System.Text.StringBuilder();
        IReadOnlyList<HotspotIpIssue> issues = report.Issues;
        if (issues.Count == 0)
            text.AppendLine("No hotspot IP problem was found right now.").AppendLine();
        else
        {
            foreach (HotspotIpIssue issue in issues)
                text.AppendLine("- " + issue.Message);
            text.AppendLine();
        }

        bool isConfiguredAddress = proposal == report.ConfiguredSubnet;
        text.AppendLine(isConfiguredAddress
            ? $"The address already set in Windows ({proposal.FirstHost}, range {proposal}) is free, so it only needs to be applied."
            : $"Suggested hotspot address: {proposal.FirstHost} (range {proposal}). Nothing else on this PC uses it.");
        text.AppendLine();
        text.AppendLine("Windows will ask for permission (UAC). The hotspot is then switched off for a few seconds, its sharing service restarts and the hotspot comes back on; connected devices reconnect on their own.");
        text.AppendLine();
        text.AppendLine(ItNote);
        text.AppendLine();
        text.Append("Change the hotspot IP now?");
        return text.ToString();
    }

    /// <summary>Starts the elevated mode and waits for it. Null when the user declined the UAC prompt.</summary>
    private static ChangeIpOutcome? LaunchElevated(string address)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!, $"{Program.SetHotspotIpArgument} {address}")
        {
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process == null)
                return ChangeIpOutcome.Failed;
            process.WaitForExit();
            return Enum.IsDefined(typeof(ChangeIpOutcome), process.ExitCode) ? (ChangeIpOutcome)process.ExitCode : ChangeIpOutcome.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            Log.Write("The permission prompt (UAC) was declined; the hotspot IP was not changed.");
            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"Could not start the elevated hotspot IP change: {ex.Message}");
            return ChangeIpOutcome.Failed;
        }
    }

    private static void Report(IWin32Window owner, ChangeIpOutcome? outcome, string address, string oldAddress)
    {
        (string message, MessageBoxIcon icon) = outcome switch
        {
            null => ("Nothing was changed, because the permission prompt was declined.", MessageBoxIcon.Information),
            ChangeIpOutcome.Applied => ($"The hotspot now uses {address}, and it is back on.", MessageBoxIcon.Information),
            ChangeIpOutcome.AdapterStillOld => ($"The new address {address} is saved and the hotspot was restarted, but Windows still shows {oldAddress} on the hotspot adapter. Restarting the PC may be necessary to apply it.", MessageBoxIcon.Warning),
            ChangeIpOutcome.NotVerified => ($"The new address {address} is saved. The hotspot isn't on right now, so the adapter's address couldn't be checked. It applies when the hotspot starts.", MessageBoxIcon.Information),
            ChangeIpOutcome.NowOverlaps => ($"{address} is now used by something else on this PC, so nothing was changed. Try again to get a new suggestion.", MessageBoxIcon.Warning),
            ChangeIpOutcome.AlreadyRunning => ("Another hotspot IP change is already running.", MessageBoxIcon.Warning),
            ChangeIpOutcome.NotElevated => ("The change needs administrator rights, which weren't granted. Nothing was changed.", MessageBoxIcon.Warning),
            _ => ("The hotspot IP could not be changed. The previous settings were restored and the hotspot was switched back on. The log has the details.", MessageBoxIcon.Error),
        };
        MessageBox.Show(owner, message, Caption, MessageBoxButtons.OK, icon);
    }

    /// <summary>A tiny topmost, invisible owner so dialogs opened from the tray don't end up behind other windows.</summary>
    private static Form CreateHiddenOwner()
    {
        var form = new Form
        {
            TopMost = true,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
        };
        form.Show();
        return form;
    }
}
