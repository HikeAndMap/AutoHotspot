using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace AutoHotspot;

/// <summary>
/// The windowless background mode started at sign-in. Every few seconds it makes sure Wi-Fi and
/// the Mobile hotspot are on, so the hotspot comes up as soon as the adapters and the network are
/// ready, and comes back within seconds whenever Windows switches it off.
/// </summary>
internal sealed class KeepAliveContext : ApplicationContext
{
    private static readonly TimeSpan AttemptInterval = TimeSpan.FromSeconds(5);

    private readonly NotifyIcon trayIcon;
    private readonly EventWaitHandle stopEvent;
    private readonly Timer tickTimer;

    private DateTime nextAttemptUtc = DateTime.MinValue;
    private bool attemptRunning;
    private bool? lastIsOn;
    private string? lastMessage;
    private bool waitingNotificationShown;
    private bool startedNotificationShown;

    public KeepAliveContext()
    {
        stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.StopEventName);
        stopEvent.Reset(); // this instance owns the keep-alive mutex, so any earlier stop request is stale

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AutoHotspot", null, (_, _) => OpenDialog());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Stop until next sign-in", null, (_, _) => Stop("stopped from the tray menu"));

        trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = "AutoHotspot: starting",
            ContextMenuStrip = menu,
            Visible = true,
        };
        trayIcon.DoubleClick += (_, _) => OpenDialog();

        Log.Write($"Keep-alive started (process {Environment.ProcessId}).");

        // Ticks every second so a stop request from the dialog is noticed quickly;
        // hotspot attempts only happen every AttemptInterval.
        tickTimer = new Timer { Interval = 1000 };
        tickTimer.Tick += OnTick;
        tickTimer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (stopEvent.WaitOne(0))
        {
            Stop("disabled from the AutoHotspot dialog");
            return;
        }

        if (attemptRunning || DateTime.UtcNow < nextAttemptUtc)
            return;

        attemptRunning = true;
        try
        {
            HotspotAttempt attempt = await Task.Run(() =>
            {
                try
                {
                    return HotspotController.EnsureOn();
                }
                catch (Exception ex)
                {
                    // Adapters that are still initializing can throw COM errors; just try again later.
                    return new HotspotAttempt(false, $"Error: {ex.Message}");
                }
            });
            HandleAttempt(attempt);
        }
        finally
        {
            attemptRunning = false;
            nextAttemptUtc = DateTime.UtcNow + AttemptInterval;
        }
    }

    private void HandleAttempt(HotspotAttempt attempt)
    {
        if (attempt.IsOn != lastIsOn || attempt.Message != lastMessage)
        {
            Log.Write(attempt.Message);
            SetTrayText(attempt.IsOn ? "AutoHotspot: hotspot is on" : $"AutoHotspot: waiting. {attempt.Message}");
        }
        lastIsOn = attempt.IsOn;
        lastMessage = attempt.Message;

        if (!attempt.IsOn && !waitingNotificationShown)
        {
            waitingNotificationShown = true;
            trayIcon.ShowBalloonTip(10000, "Mobile hotspot isn't on yet",
                $"{attempt.Message} AutoHotspot keeps trying in the background.", ToolTipIcon.Info);
        }
        else if (attempt.IsOn && waitingNotificationShown && !startedNotificationShown)
        {
            startedNotificationShown = true;
            trayIcon.ShowBalloonTip(10000, "Mobile hotspot is on", attempt.Message, ToolTipIcon.Info);
        }
    }

    private void SetTrayText(string text)
    {
        const int maxLength = 127; // NotifyIcon.Text limit
        trayIcon.Text = text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    private static void OpenDialog()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
    }

    private static void OpenLog()
    {
        if (File.Exists(Log.FilePath))
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
    }

    private void Stop(string reason)
    {
        Log.Write($"Keep-alive stopped: {reason}.");
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        tickTimer.Stop();
        trayIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tickTimer.Dispose();
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();
            stopEvent.Dispose();
        }
        base.Dispose(disposing);
    }
}
