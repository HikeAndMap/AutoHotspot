using System.Diagnostics;
using Windows.Networking.NetworkOperators;

namespace AutoHotspot;

/// <summary>The dialog shown when AutoHotspot is started normally: enable/disable plus live status.</summary>
public partial class FormMain : Form
{
    private bool loading;
    private bool statusRefreshRunning;

    public FormMain()
    {
        InitializeComponent();
        Icon = AppIcon.Load(new Size(32, 32));
    }

    private void FormMain_Load(object? sender, EventArgs e)
    {
        if (AutostartRegistry.RepairPathIfEnabled())
            Log.Write($"Sign-in entry updated to {AutostartRegistry.ExpectedCommand}.");

        loading = true;
        toggleRowEnabled.Checked = AutostartRegistry.IsEnabled;
        loading = false;

        timerStatus.Start();
        timerStatus_Tick(this, EventArgs.Empty);
    }

    private void toggleRowEnabled_CheckedChanged(object? sender, EventArgs e)
    {
        if (loading)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            if (toggleRowEnabled.Checked)
            {
                AutostartRegistry.Enable();
                Log.Write($"Enabled: sign-in entry set to {AutostartRegistry.ExpectedCommand}.");
                KeepAliveProcess.Start();
            }
            else
            {
                AutostartRegistry.Disable();
                Log.Write("Disabled: sign-in entry removed.");
                if (!KeepAliveProcess.Stop(TimeSpan.FromSeconds(5)))
                {
                    MessageBox.Show(this, "The sign-in entry was removed, but the background instance did not stop. It will not start again at the next sign-in.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not change the setting:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            loading = true;
            toggleRowEnabled.Checked = AutostartRegistry.IsEnabled;
            loading = false;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        timerStatus_Tick(this, EventArgs.Empty);
    }

    private async void timerStatus_Tick(object? sender, EventArgs e)
    {
        bool keepAliveRunning = KeepAliveProcess.IsRunning;
        labelKeepAliveValue.Text = keepAliveRunning ? "Running, keeping the hotspot on" : "Not running";
        buttonStartKeepAlive.Visible = toggleRowEnabled.Checked && !keepAliveRunning;

        if (statusRefreshRunning)
            return;
        statusRefreshRunning = true;
        try
        {
            string hotspotText = await Task.Run(() =>
            {
                try
                {
                    return HotspotController.ReadState() switch
                    {
                        null => "Off (no network connection to share)",
                        TetheringOperationalState.On => "On",
                        TetheringOperationalState.Off => "Off",
                        TetheringOperationalState.InTransition => "Switching...",
                        _ => "Unknown",
                    };
                }
                catch (Exception ex)
                {
                    return $"Unknown ({ex.Message})";
                }
            });

            if (!IsDisposed)
                labelHotspotValue.Text = hotspotText;
        }
        finally
        {
            statusRefreshRunning = false;
        }
    }

    private void buttonStartKeepAlive_Click(object? sender, EventArgs e)
    {
        KeepAliveProcess.Start();
        timerStatus_Tick(this, EventArgs.Empty);
    }

    private void linkLabelHotspotSettings_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:network-mobilehotspot") { UseShellExecute = true });
    }

    private void linkLabelOpenLog_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (File.Exists(Log.FilePath))
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        else
            MessageBox.Show(this, "Nothing has been logged yet.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void buttonClose_Click(object? sender, EventArgs e)
    {
        Close();
    }
}
