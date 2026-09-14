using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace AutoHotspot;

/// <summary>Outcome of one attempt to get the hotspot running.</summary>
/// <param name="IsOn">True when the hotspot is confirmed on after this attempt.</param>
/// <param name="Message">Human-readable description of the state or of why it isn't on yet.</param>
internal sealed record HotspotAttempt(bool IsOn, string Message);

/// <summary>
/// Switches the Wi-Fi radio and the Windows Mobile hotspot on through the Windows Runtime APIs.
/// All methods block on WinRT async calls, so call them from a background thread (Task.Run),
/// never from the UI thread.
/// </summary>
internal static class HotspotController
{
    /// <summary>
    /// Makes sure Wi-Fi is on, then makes sure the hotspot is on. Safe to call repeatedly:
    /// when everything is already on it only reads state.
    /// </summary>
    public static HotspotAttempt EnsureOn()
    {
        string? wifiProblem = EnsureWifiRadioOn();
        if (wifiProblem != null)
            return new HotspotAttempt(false, wifiProblem);

        ConnectionProfile? profile = FindShareableProfile(out string? profileProblem);
        if (profile == null)
            return new HotspotAttempt(false, profileProblem ?? "No network connection to share yet.");

        NetworkOperatorTetheringManager manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
        switch (manager.TetheringOperationalState)
        {
            case TetheringOperationalState.On:
                return new HotspotAttempt(true, $"Hotspot is on, sharing \"{profile.ProfileName}\".");
            case TetheringOperationalState.InTransition:
                return new HotspotAttempt(false, "Hotspot is switching state, waiting.");
        }

        NetworkOperatorTetheringOperationResult result = manager.StartTetheringAsync().AsTask().GetAwaiter().GetResult();
        if (result.Status == TetheringOperationStatus.Success)
            return new HotspotAttempt(true, $"Hotspot started, sharing \"{profile.ProfileName}\".");

        string detail = string.IsNullOrWhiteSpace(result.AdditionalErrorMessage) ? "" : $" ({result.AdditionalErrorMessage})";
        return new HotspotAttempt(false, $"Starting the hotspot failed: {result.Status}{detail}.");
    }

    /// <summary>Reads the current hotspot state without changing anything.</summary>
    public static TetheringOperationalState? ReadState()
    {
        ConnectionProfile? profile = FindShareableProfile(out _);
        if (profile == null)
            return null;
        return NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile).TetheringOperationalState;
    }

    /// <summary>Turns every Wi-Fi radio on. Returns null on success, otherwise the reason it can't be done yet.</summary>
    private static string? EnsureWifiRadioOn()
    {
        RadioAccessStatus access = Radio.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
        if (access != RadioAccessStatus.Allowed)
            return $"Windows does not allow this app to control radios ({access}). Check Settings > Privacy & security > Radios.";

        IReadOnlyList<Radio> radios = Radio.GetRadiosAsync().AsTask().GetAwaiter().GetResult();
        List<Radio> wifiRadios = radios.Where(r => r.Kind == RadioKind.WiFi).ToList();
        if (wifiRadios.Count == 0)
            return "No Wi-Fi adapter found yet.";

        foreach (Radio radio in wifiRadios)
        {
            if (radio.State == RadioState.On)
                continue;

            RadioAccessStatus setResult = radio.SetStateAsync(RadioState.On).AsTask().GetAwaiter().GetResult();
            if (setResult != RadioAccessStatus.Allowed)
                return $"Could not turn Wi-Fi on ({setResult}).";
        }

        return null;
    }

    /// <summary>
    /// Picks the connection the hotspot should share: the internet connection when there is one,
    /// otherwise any other connected network that Windows allows tethering from.
    /// </summary>
    private static ConnectionProfile? FindShareableProfile(out string? problem)
    {
        problem = null;
        var candidates = new List<ConnectionProfile>();

        ConnectionProfile? internet = NetworkInformation.GetInternetConnectionProfile();
        if (internet != null)
            candidates.Add(internet);

        foreach (ConnectionProfile p in NetworkInformation.GetConnectionProfiles())
        {
            if (p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None
                && !candidates.Any(c => c.ProfileName == p.ProfileName))
                candidates.Add(p);
        }

        if (candidates.Count == 0)
        {
            problem = "No network connection to share yet.";
            return null;
        }

        foreach (ConnectionProfile candidate in candidates)
        {
            TetheringCapability capability = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(candidate);
            if (capability == TetheringCapability.Enabled)
                return candidate;
            problem ??= $"Windows does not allow sharing \"{candidate.ProfileName}\" ({capability}).";
        }

        return null;
    }
}
