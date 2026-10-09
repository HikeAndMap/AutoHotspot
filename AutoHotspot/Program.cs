namespace AutoHotspot;

internal static class Program
{
    /// <summary>Command-line switch used by the sign-in "Run" entry: run the keep-alive loop without a window.</summary>
    public const string StartupArgument = "--startup";

    /// <summary>Command-line switch of the elevated mode that applies a new hotspot IP: <c>--set-hotspot-ip a.b.c.d</c>.</summary>
    public const string SetHotspotIpArgument = "--set-hotspot-ip";

    /// <summary>Held by the one running keep-alive instance.</summary>
    public const string KeepAliveMutexName = @"Local\AutoHotspot.KeepAlive";

    /// <summary>Signalled by the dialog to ask a running keep-alive instance to exit.</summary>
    public const string StopEventName = @"Local\AutoHotspot.Stop";

    /// <summary>Signalled by the elevated hotspot IP change while it restarts the hotspot, so the keep-alive instance doesn't fight it.</summary>
    public const string PauseEventName = @"Local\AutoHotspot.Pause";

    /// <summary>Held by the elevated hotspot IP change so only one runs at a time.</summary>
    public const string ChangeIpMutexName = @"Local\AutoHotspot.ChangeIp";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], SetHotspotIpArgument, StringComparison.OrdinalIgnoreCase))
            return HotspotIpChanger.RunElevatedMode(args);

        ApplicationConfiguration.Initialize();

        if (args.Any(a => string.Equals(a, StartupArgument, StringComparison.OrdinalIgnoreCase)))
        {
            RunKeepAlive();
            return 0;
        }

        Application.Run(new FormMain());
        return 0;
    }

    private static void RunKeepAlive()
    {
        using var mutex = new Mutex(false, KeepAliveMutexName);
        if (!KeepAliveProcess.TryAcquire(mutex))
            return; // another keep-alive instance is already running

        try
        {
            Application.Run(new KeepAliveContext());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
