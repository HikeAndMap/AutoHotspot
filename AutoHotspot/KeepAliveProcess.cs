using System.Diagnostics;

namespace AutoHotspot;

/// <summary>Starts, stops and detects the background keep-alive instance from the dialog.</summary>
internal static class KeepAliveProcess
{
    public static bool IsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(Program.KeepAliveMutexName, out Mutex? mutex))
                return false;

            using (mutex)
            {
                // Merely opening the mutex doesn't prove an owner exists, so try to take it.
                if (!TryAcquire(mutex))
                    return true;
                mutex.ReleaseMutex();
                return false;
            }
        }
    }

    /// <summary>Takes the mutex without waiting. An abandoned mutex (owner crashed) counts as acquired.</summary>
    public static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    public static void Start()
    {
        if (IsRunning)
            return;

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Program.StartupArgument)
        {
            UseShellExecute = false,
        });
    }

    /// <summary>Asks the running instance to exit and waits for it. Returns true when none is running any more.</summary>
    public static bool Stop(TimeSpan timeout)
    {
        if (EventWaitHandle.TryOpenExisting(Program.StopEventName, out EventWaitHandle? stopEvent))
        {
            using (stopEvent)
                stopEvent.Set();
        }

        var stopwatch = Stopwatch.StartNew();
        while (IsRunning)
        {
            if (stopwatch.Elapsed > timeout)
                return false;
            Thread.Sleep(100);
        }

        return true;
    }
}
