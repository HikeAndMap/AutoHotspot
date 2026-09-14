namespace AutoHotspot;

internal static class Program
{
    /// <summary>Command-line switch used by the sign-in "Run" entry: run the keep-alive loop without a window.</summary>
    public const string StartupArgument = "--startup";

    /// <summary>Held by the one running keep-alive instance.</summary>
    public const string KeepAliveMutexName = @"Local\AutoHotspot.KeepAlive";

    /// <summary>Signalled by the dialog to ask a running keep-alive instance to exit.</summary>
    public const string StopEventName = @"Local\AutoHotspot.Stop";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => string.Equals(a, StartupArgument, StringComparison.OrdinalIgnoreCase)))
        {
            RunKeepAlive();
            return;
        }

        Application.Run(new FormMain());
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
