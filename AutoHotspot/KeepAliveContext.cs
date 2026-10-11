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
    private readonly EventWaitHandle pauseEvent;
    private readonly EventWaitHandle manualStopEvent;
    private readonly HotspotIpMonitor ipMonitor;
    private readonly Timer tickTimer;
    private readonly ToolStripMenuItem startItem;
    private readonly ToolStripMenuItem stopItem;

    private DateTime nextAttemptUtc = DateTime.MinValue;
    private bool attemptRunning;
    private bool manuallyStopped;
    private bool switching;
    private bool? lastIsOn;
    private string? lastMessage;
    private bool waitingNotificationShown;
    private bool startedNotificationShown;
    private bool balloonOffersIpChange;
    private DateTime? pausedSinceUtc;

    public KeepAliveContext()
    {
        stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.StopEventName);
        stopEvent.Reset(); // this instance owns the keep-alive mutex, so any earlier stop request is stale
        pauseEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.PauseEventName);
        pauseEvent.Reset();
        manualStopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.ManualStopEventName);
        manualStopEvent.Reset();

        startItem = new ToolStripMenuItem("Start hotspot", null, (_, _) => StartHotspot());
        stopItem = new ToolStripMenuItem("Stop hotspot (no automatic restart)", null, (_, _) => StopHotspot());

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AutoHotspot", null, (_, _) => OpenDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Change hotspot IP...", null, (_, _) => ChangeHotspotIp());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit until next sign-in", null, (_, _) => Stop("exited from the tray menu"));
        menu.Opening += (_, _) =>
        {
            startItem.Enabled = manuallyStopped && !switching;
            stopItem.Enabled = !manuallyStopped && !switching;
        };

        trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = "AutoHotspot: starting",
            ContextMenuStrip = menu,
            Visible = true,
        };
        trayIcon.DoubleClick += (_, _) => OpenDialog();
        trayIcon.BalloonTipClicked += (_, _) =>
        {
            if (balloonOffersIpChange)
                ChangeHotspotIp();
        };

        Log.Write($"Keep-alive started (process {Environment.ProcessId}).");

        // Ticks every second so a stop request from the dialog is noticed quickly;
        // hotspot attempts only happen every AttemptInterval.
        tickTimer = new Timer { Interval = 1000 };
        tickTimer.Tick += OnTick;
        tickTimer.Start();

        ipMonitor = new HotspotIpMonitor(IsPaused);
        ipMonitor.NewIssues += OnNewHotspotIpIssues;
        ipMonitor.Start();
    }

    /// <summary>
    /// True while an elevated hotspot IP change is restarting the hotspot. A change that never
    /// clears the signal (for example because it was killed) stops pausing us after a few minutes.
    /// </summary>
    private bool IsPaused()
    {
        if (!pauseEvent.WaitOne(0))
        {
            pausedSinceUtc = null;
            return false;
        }

        pausedSinceUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - pausedSinceUtc > TimeSpan.FromMinutes(3))
        {
            pauseEvent.Reset();
            pausedSinceUtc = null;
            Log.Write("A hotspot IP change did not finish in 3 minutes; resuming the keep-alive loop.");
            return false;
        }
        return true;
    }

    private void OnNewHotspotIpIssues(IReadOnlyList<HotspotIpIssue> issues)
    {
        const string suffix = " Click to change the hotspot IP.";
        string text = string.Join(" ", issues.Select(i => i.Message));
        if (text.Length > 250 - suffix.Length) // balloon text limit is 255 characters
            text = text[..(250 - suffix.Length - 3)] + "...";
        text += suffix;
        balloonOffersIpChange = true;
        trayIcon.ShowBalloonTip(15000, "Mobile hotspot IP problem", text, ToolTipIcon.Warning);
    }

    private async void ChangeHotspotIp()
    {
        try
        {
            await HotspotIpChangeFlow.RunAsync(null);
        }
        catch (Exception ex)
        {
            Log.Write($"Changing the hotspot IP failed: {ex.Message}");
        }
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (stopEvent.WaitOne(0))
        {
            Stop("disabled from the AutoHotspot dialog");
            return;
        }

        if (manuallyStopped || attemptRunning || DateTime.UtcNow < nextAttemptUtc || IsPaused())
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

    /// <summary>
    /// Switches the hotspot off and stops the keep-alive loop from switching it back on, until
    /// <see cref="StartHotspot"/> or the next sign-in. The tray icon stays.
    /// </summary>
    private async void StopHotspot()
    {
        if (manuallyStopped || switching)
            return;

        switching = true;
        manuallyStopped = true;
        manualStopEvent.Set();
        Log.Write("Hotspot stopped from the tray menu; it is not restarted automatically until Start is chosen.");
        SetTrayText("AutoHotspot: stopped. Right-click > Start hotspot to turn it back on");
        try
        {
            // An attempt that is already running could switch the hotspot on right after we switch it off.
            while (attemptRunning)
                await Task.Delay(100);

            await Task.Run(() => HotspotController.StopIfOn(TimeSpan.FromSeconds(15)));
        }
        catch (Exception ex)
        {
            Log.Write($"Stopping the hotspot failed: {ex.Message}");
            balloonOffersIpChange = false;
            trayIcon.ShowBalloonTip(10000, "Could not stop the Mobile hotspot", ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            switching = false;
        }
    }

    /// <summary>Lifts a manual stop: the keep-alive loop switches the hotspot on right away and keeps it on again.</summary>
    private void StartHotspot()
    {
        if (!manuallyStopped || switching)
            return;

        manuallyStopped = false;
        manualStopEvent.Reset();
        lastIsOn = null;
        lastMessage = null;
        nextAttemptUtc = DateTime.MinValue;
        Log.Write("Hotspot started from the tray menu; keeping it on again.");
        SetTrayText("AutoHotspot: starting");
    }

    private void HandleAttempt(HotspotAttempt attempt)
    {
        if (manuallyStopped)
            return; // StopHotspot waits for this attempt and then switches the hotspot off

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
            balloonOffersIpChange = false;
            trayIcon.ShowBalloonTip(10000, "Mobile hotspot isn't on yet",
                $"{attempt.Message} AutoHotspot keeps trying in the background.", ToolTipIcon.Info);
        }
        else if (attempt.IsOn && waitingNotificationShown && !startedNotificationShown)
        {
            startedNotificationShown = true;
            balloonOffersIpChange = false;
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
        ipMonitor.Dispose();
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
            pauseEvent.Dispose();
            manualStopEvent.Dispose();
        }
        base.Dispose(disposing);
    }
}
