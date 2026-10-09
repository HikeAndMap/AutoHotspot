using System.Net;
using System.Security.Principal;

namespace AutoHotspot;

/// <summary>Exit codes of the elevated "set hotspot IP" mode, which the unelevated instance turns into a message.</summary>
internal enum ChangeIpOutcome
{
    /// <summary>Applied, and the hotspot adapter now has the new address.</summary>
    Applied = 0,

    /// <summary>Nothing was changed, or a step failed and the previous settings were restored. Details are in the log.</summary>
    Failed = 1,

    InvalidArgument = 2,
    NowOverlaps = 3,
    AlreadyRunning = 4,
    NotElevated = 5,

    /// <summary>Saved and the hotspot was restarted, but the adapter still shows the old address.</summary>
    AdapterStillOld = 10,

    /// <summary>Saved, but the hotspot is not on, so the adapter's address could not be checked.</summary>
    NotVerified = 11,
}

/// <summary>
/// The elevated half of "Change IP": runs as <c>AutoHotspot.exe --set-hotspot-ip a.b.c.d</c> after a
/// UAC prompt. Stops the hotspot and the SharedAccess service, writes ScopeAddress and
/// ScopeAddressBackup, starts both again and re-reads the adapter's real address. If a step
/// fails the old registry values are restored and the service and hotspot are brought back.
/// </summary>
internal static class HotspotIpChanger
{
    private const string ServiceName = "SharedAccess";
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HotspotTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AdapterTimeout = TimeSpan.FromSeconds(20);

    public static int RunElevatedMode(string[] args)
    {
        ChangeIpOutcome outcome;
        try
        {
            outcome = Run(args);
        }
        catch (Exception ex)
        {
            Log.Write($"Hotspot IP change failed unexpectedly: {ex.Message}");
            outcome = ChangeIpOutcome.Failed;
        }
        Log.Write($"Hotspot IP change finished: {outcome}.");
        return (int)outcome;
    }

    private static ChangeIpOutcome Run(string[] args)
    {
        if (args.Length != 2 || !HotspotIpLogic.TryValidateHotspotAddress(args[1], out IPAddress? newAddress))
        {
            Log.Write("Hotspot IP change refused: the address argument is missing or not a usable private address.");
            return ChangeIpOutcome.InvalidArgument;
        }

        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Log.Write("Hotspot IP change refused: not running elevated.");
                return ChangeIpOutcome.NotElevated;
            }
        }

        using var mutex = new Mutex(false, Program.ChangeIpMutexName);
        if (!KeepAliveProcess.TryAcquire(mutex))
        {
            Log.Write("Hotspot IP change refused: another change is already running.");
            return ChangeIpOutcome.AlreadyRunning;
        }

        try
        {
            // Re-check with fresh data: the PC may have changed since the user confirmed.
            NetworkSnapshot snapshot = HotspotNetwork.Read();
            Ipv4Subnet target = Ipv4Subnet.From(newAddress, 24);
            OccupiedNetwork? overlap = snapshot.Others.Where(HotspotIpLogic.IsRelevant).FirstOrDefault(o => target.Overlaps(o.Subnet));
            if (overlap != null)
            {
                Log.Write($"Hotspot IP change refused: {target} now overlaps {overlap.Describe()}.");
                return ChangeIpOutcome.NowOverlaps;
            }

            // Both the dialog's background instance and this process would otherwise fight over the hotspot.
            using EventWaitHandle? pause = OpenPauseEvent();
            pause?.Set();
            try
            {
                return Task.Run(() => Apply(newAddress)).GetAwaiter().GetResult();
            }
            finally
            {
                pause?.Reset();
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static EventWaitHandle? OpenPauseEvent()
    {
        try
        {
            return EventWaitHandle.TryOpenExisting(Program.PauseEventName, out EventWaitHandle? pause) ? pause : null;
        }
        catch (UnauthorizedAccessException)
        {
            // Elevated as a different administrator account than the one that runs the keep-alive loop.
            Log.Write("The keep-alive loop could not be paused (access denied); it may restart the hotspot during the change.");
            return null;
        }
    }

    private static ChangeIpOutcome Apply(IPAddress newAddress)
    {
        string newText = newAddress.ToString();
        string? oldScope = HotspotScopeRegistry.ReadRaw(HotspotScopeRegistry.ScopeAddressName);
        string? oldBackup = HotspotScopeRegistry.ReadRaw(HotspotScopeRegistry.ScopeAddressBackupName);
        Log.Write($"Changing the hotspot IP to {newText} (was ScopeAddress {oldScope ?? "<not set>"}, ScopeAddressBackup {oldBackup ?? "<not set>"}).");

        var service = new WindowsService(ServiceName);
        bool hotspotWasOn = false;
        bool registryTouched = false;

        try
        {
            hotspotWasOn = HotspotController.StopIfOn(HotspotTimeout);
            Log.Write(hotspotWasOn ? "Hotspot stopped." : "Hotspot was not on.");

            StopService(service);
            Log.Write("SharedAccess service stopped.");

            registryTouched = true;
            HotspotScopeRegistry.WriteRaw(HotspotScopeRegistry.ScopeAddressName, newText);
            HotspotScopeRegistry.WriteRaw(HotspotScopeRegistry.ScopeAddressBackupName, newText);
            Log.Write("ScopeAddress and ScopeAddressBackup written.");

            StartService(service);
            Log.Write("SharedAccess service started.");

            if (hotspotWasOn)
            {
                HotspotController.StartAndWait(HotspotTimeout);
                Log.Write("Hotspot started.");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Hotspot IP change failed: {ex.Message}. Restoring the previous settings.");
            Rollback(service, oldScope, oldBackup, registryTouched, hotspotWasOn);
            return ChangeIpOutcome.Failed;
        }

        return Verify(newAddress, hotspotWasOn);
    }

    /// <summary>Re-reads the hotspot adapter's real address and reports whether the new one took effect.</summary>
    private static ChangeIpOutcome Verify(IPAddress newAddress, bool hotspotWasOn)
    {
        if (!hotspotWasOn)
        {
            Log.Write("The hotspot was not on, so the adapter address was not checked.");
            return ChangeIpOutcome.NotVerified;
        }

        DateTime deadline = DateTime.UtcNow + AdapterTimeout;
        HotspotAdapterState? actual;
        do
        {
            actual = HotspotNetwork.Read().Actual;
            if (actual != null && actual.Address.Equals(newAddress))
            {
                Log.Write($"The hotspot adapter \"{actual.Name}\" now has {actual.Address}.");
                return ChangeIpOutcome.Applied;
            }
            Thread.Sleep(1000);
        }
        while (DateTime.UtcNow < deadline);

        if (actual == null)
        {
            Log.Write("The hotspot adapter has no address yet, so the change could not be confirmed.");
            return ChangeIpOutcome.NotVerified;
        }

        Log.Write($"The hotspot adapter \"{actual.Name}\" still has {actual.Address}; a restart of Windows may be necessary.");
        return ChangeIpOutcome.AdapterStillOld;
    }

    /// <summary>
    /// Best effort, every step on its own: when the registry was touched, restore the values and
    /// restart the service so it reads them; then make sure the service and the hotspot are running.
    /// </summary>
    private static void Rollback(WindowsService service, string? oldScope, string? oldBackup, bool registryTouched, bool hotspotWasOn)
    {
        if (registryTouched)
        {
            Try("stop the service for the rollback", () => StopService(service));
            Try("restore ScopeAddress", () => HotspotScopeRegistry.WriteRaw(HotspotScopeRegistry.ScopeAddressName, oldScope));
            Try("restore ScopeAddressBackup", () => HotspotScopeRegistry.WriteRaw(HotspotScopeRegistry.ScopeAddressBackupName, oldBackup));
        }
        Try("start the service again", () => StartService(service));
        if (hotspotWasOn)
            Try("start the hotspot again", () => HotspotController.StartAndWait(HotspotTimeout));
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Write($"Rollback: could not {what}: {ex.Message}");
        }
    }

    private static void StopService(WindowsService service)
    {
        if (service.Status == ServiceState.Stopped)
            return;

        if (service.Status != ServiceState.StopPending)
            service.Stop();
        service.WaitForStatus(ServiceState.Stopped, ServiceTimeout);
    }

    private static void StartService(WindowsService service)
    {
        if (service.Status == ServiceState.Running)
            return;

        if (service.Status != ServiceState.StartPending)
            service.Start();
        service.WaitForStatus(ServiceState.Running, ServiceTimeout);
    }
}
