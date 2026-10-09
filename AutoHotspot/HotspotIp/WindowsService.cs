using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoHotspot;

internal enum ServiceState
{
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7,
}

/// <summary>
/// Minimal Service Control Manager wrapper (query, start, stop). The ServiceController class is
/// not part of the shared framework, and this project uses no NuGet packages.
/// </summary>
internal sealed class WindowsService
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ControlStop = 0x00000001;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(IntPtr service, int argCount, IntPtr args);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    private readonly string name;

    public WindowsService(string name)
    {
        this.name = name;
    }

    public ServiceState Status => WithService(ServiceQueryStatus, handle => Query(handle));

    public void Start() => _ = WithService(ServiceStart, handle =>
    {
        if (!StartService(handle, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() != ErrorServiceAlreadyRunning)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return 0;
    });

    public void Stop() => _ = WithService(ServiceStop, handle =>
    {
        if (!ControlService(handle, ControlStop, out _) && Marshal.GetLastWin32Error() != ErrorServiceNotActive)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return 0;
    });

    /// <summary>Polls until the service reports <paramref name="state"/>; throws a TimeoutException otherwise.</summary>
    public void WaitForStatus(ServiceState state, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (Status != state)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The {name} service did not reach the {state} state in {timeout.TotalSeconds:0} seconds.");
            Thread.Sleep(250);
        }
    }

    private static ServiceState Query(IntPtr service)
    {
        if (!QueryServiceStatus(service, out ServiceStatus status))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (ServiceState)status.CurrentState;
    }

    private T WithService<T>(uint access, Func<IntPtr, T> action)
    {
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IntPtr service = OpenService(manager, name, access);
            if (service == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                return action(service);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }
}
