using System.Net.NetworkInformation;
using Timer = System.Windows.Forms.Timer;

namespace AutoHotspot;

/// <summary>
/// Checks the hotspot IP at startup and again whenever Windows reports an address change
/// (NetworkChange.NetworkAddressChanged), never on a timer loop. Bursts of events are folded into
/// one check shortly after the last one, and each distinct issue is announced only once.
/// </summary>
internal sealed class HotspotIpMonitor : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private readonly Func<bool> isPaused;
    private readonly Timer debounceTimer = new();
    private readonly HashSet<string> announced = new();
    private readonly Control marshaller = new();
    private bool checkRunning;
    private bool disposed;

    public HotspotIpMonitor(Func<bool> isPaused)
    {
        this.isPaused = isPaused;
        _ = marshaller.Handle; // create the window on the UI thread so events from other threads can be marshalled to it
        debounceTimer.Tick += async (_, _) => await CheckAsync();
    }

    /// <summary>Raised on the UI thread with the issues that have not been announced before.</summary>
    public event Action<IReadOnlyList<HotspotIpIssue>>? NewIssues;

    /// <summary>The most recent check, or null before the first one finished.</summary>
    public HotspotIpReport? Latest { get; private set; }

    public void Start()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        Schedule();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        // Raised on a thread-pool thread.
        if (!disposed && marshaller.IsHandleCreated)
            marshaller.BeginInvoke(Schedule);
    }

    private void Schedule()
    {
        if (disposed)
            return;
        debounceTimer.Stop();
        debounceTimer.Interval = (int)Debounce.TotalMilliseconds;
        debounceTimer.Start();
    }

    private async Task CheckAsync()
    {
        debounceTimer.Stop();
        if (disposed)
            return;

        // The elevated change restarts the hotspot on purpose; look again once it is done.
        if (isPaused() || checkRunning)
        {
            Schedule();
            return;
        }

        checkRunning = true;
        try
        {
            HotspotIpReport report = await Task.Run(() => HotspotNetwork.Read().Analyze());
            if (disposed)
                return;

            Latest = report;
            List<HotspotIpIssue> fresh = report.Issues.Where(i => announced.Add(i.Key)).ToList();
            if (fresh.Count == 0)
                return;

            foreach (HotspotIpIssue issue in fresh)
                Log.Write("Hotspot IP: " + issue.Message);
            NewIssues?.Invoke(fresh);
        }
        catch (Exception ex)
        {
            Log.Write($"Hotspot IP check failed: {ex.Message}");
        }
        finally
        {
            checkRunning = false;
        }
    }

    public void Dispose()
    {
        disposed = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        debounceTimer.Dispose();
        marshaller.Dispose();
    }
}
