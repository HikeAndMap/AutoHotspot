using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace AutoHotspot;

internal enum NetworkSourceKind
{
    /// <summary>The address of a network adapter.</summary>
    Adapter,

    /// <summary>An entry of the IPv4 routing table.</summary>
    Route,
}

/// <summary>An IPv4 subnet that something other than the hotspot already uses on this PC.</summary>
/// <param name="Address">For adapters, the adapter's own address (not just the subnet).</param>
internal sealed record OccupiedNetwork(Ipv4Subnet Subnet, string InterfaceName, NetworkSourceKind Kind, IPAddress? Address = null)
{
    public string Describe() => Kind == NetworkSourceKind.Adapter
        ? $"\"{InterfaceName}\" ({Address}/{Subnet.PrefixLength})"
        : $"a route to {Subnet} on \"{InterfaceName}\"";
}

/// <summary>The IPv4 address the hotspot's virtual adapter actually has right now.</summary>
internal sealed record HotspotAdapterState(string Name, IPAddress Address, int PrefixLength)
{
    public Ipv4Subnet Subnet => Ipv4Subnet.From(Address, PrefixLength);
}

internal sealed record HotspotConflict(Ipv4Subnet HotspotSubnet, OccupiedNetwork Other)
{
    public string Key => $"conflict|{HotspotSubnet}|{Other.InterfaceName}";

    public string Describe() => $"The hotspot range {HotspotSubnet} overlaps {Other.Describe()}.";
}

/// <summary>One thing worth telling the user about. <paramref name="Key"/> identifies it so it is only announced once.</summary>
internal sealed record HotspotIpIssue(string Key, string Message);

internal sealed record HotspotIpReport(
    IPAddress ConfiguredAddress,
    HotspotAdapterState? Actual,
    IReadOnlyList<HotspotConflict> Conflicts)
{
    public Ipv4Subnet ConfiguredSubnet => Ipv4Subnet.From(ConfiguredAddress, 24);

    /// <summary>The registry says one address while the running hotspot adapter still has another.</summary>
    public bool AddressMismatch => Actual != null && !Actual.Address.Equals(ConfiguredAddress);

    public IReadOnlyList<HotspotIpIssue> Issues
    {
        get
        {
            var issues = new List<HotspotIpIssue>();
            if (AddressMismatch)
            {
                issues.Add(new HotspotIpIssue($"mismatch|{ConfiguredAddress}|{Actual!.Address}",
                    $"The hotspot is set to {ConfiguredAddress}, but its adapter still uses {Actual.Address}. Windows applies the new address when the hotspot service restarts."));
            }
            issues.AddRange(Conflicts.Select(c => new HotspotIpIssue(c.Key, c.Describe())));
            return issues;
        }
    }
}

/// <summary>
/// The decisions behind the hotspot IP feature, kept free of registry, adapter and routing-table
/// access so they can be unit tested: what counts as a conflict, and which new range to propose.
/// </summary>
internal static class HotspotIpLogic
{
    /// <summary>Microsoft's built-in hotspot address when nothing is configured.</summary>
    public static readonly IPAddress DefaultHotspotAddress = IPAddress.Parse("192.168.137.1");

    /// <summary>
    /// Whether an adapter address or route can collide with the hotspot. Ignores the default route
    /// and other very short prefixes (VPN "catch-all" routes), host routes, loopback, link-local
    /// (169.254/16: no real network) and multicast/broadcast.
    /// </summary>
    public static bool IsRelevant(OccupiedNetwork network)
    {
        Ipv4Subnet s = network.Subnet;
        if (s.PrefixLength >= 8 && new Ipv4Subnet(0x7F000000, 8).Contains(s.Network)) // 127.0.0.0/8
            return false;
        if (s.PrefixLength >= 16 && new Ipv4Subnet(0xA9FE0000, 16).Contains(s.Network)) // 169.254.0.0/16
            return false;
        if (s.Network >= 0xE0000000) // 224.0.0.0 and above: multicast, reserved, broadcast
            return false;

        if (network.Kind == NetworkSourceKind.Route && (s.PrefixLength < 8 || s.PrefixLength == 32))
            return false;
        if (s.PrefixLength == 0)
            return false;

        return true;
    }

    /// <summary>
    /// Compares the hotspot's configured range and (when it differs) the range its adapter actually
    /// has against everything else on the PC. One conflict is reported per hotspot range and
    /// interface, with the interface's own address preferred over its routes.
    /// </summary>
    public static HotspotIpReport Analyze(IPAddress configured, HotspotAdapterState? actual, IEnumerable<OccupiedNetwork> others)
    {
        var hotspotRanges = new List<Ipv4Subnet> { Ipv4Subnet.From(configured, 24) };
        if (actual != null && !hotspotRanges.Contains(actual.Subnet))
            hotspotRanges.Add(actual.Subnet);

        List<OccupiedNetwork> relevant = others
            .Where(IsRelevant)
            .OrderBy(o => o.Kind) // adapters before routes
            .ToList();

        var conflicts = new List<HotspotConflict>();
        foreach (Ipv4Subnet range in hotspotRanges)
        {
            var reportedInterfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OccupiedNetwork other in relevant)
            {
                if (range.Overlaps(other.Subnet) && reportedInterfaces.Add(other.InterfaceName))
                    conflicts.Add(new HotspotConflict(range, other));
            }
        }

        return new HotspotIpReport(configured, actual, conflicts);
    }

    /// <summary>Private /24 ranges to try, in order: 192.168.50-99.x first, then 10.77.50-99.x and 172.30.50-99.x.</summary>
    public static IEnumerable<Ipv4Subnet> CandidateSubnets()
    {
        for (uint third = 50; third <= 99; third++)
            yield return new Ipv4Subnet((192u << 24) | (168u << 16) | (third << 8), 24);
        for (uint third = 50; third <= 99; third++)
            yield return new Ipv4Subnet((10u << 24) | (77u << 16) | (third << 8), 24);
        for (uint third = 50; third <= 99; third++)
            yield return new Ipv4Subnet((172u << 24) | (30u << 16) | (third << 8), 24);
    }

    /// <summary>
    /// The first candidate /24 that overlaps nothing in <paramref name="occupied"/> and none of
    /// <paramref name="exclude"/> (the hotspot's current ranges, so a change really changes it).
    /// Null when every candidate is taken.
    /// </summary>
    public static Ipv4Subnet? PickFreeSubnet(IEnumerable<OccupiedNetwork> occupied, IEnumerable<Ipv4Subnet> exclude)
    {
        List<Ipv4Subnet> taken = occupied.Where(IsRelevant).Select(o => o.Subnet).Concat(exclude).ToList();
        foreach (Ipv4Subnet candidate in CandidateSubnets())
        {
            if (!taken.Any(t => t.Overlaps(candidate)))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The range to offer the user. When the only problem is that the registry already holds a
    /// good address that was never applied, that address is offered (applying it just restarts the
    /// hotspot service). Otherwise a new free range is picked.
    /// </summary>
    public static Ipv4Subnet? ProposeSubnet(HotspotIpReport report, IEnumerable<OccupiedNetwork> others)
    {
        bool configuredIsFree = report.Conflicts.All(c => c.HotspotSubnet != report.ConfiguredSubnet);
        if (report.AddressMismatch && configuredIsFree)
            return report.ConfiguredSubnet;

        var exclude = new List<Ipv4Subnet> { report.ConfiguredSubnet };
        if (report.Actual != null)
            exclude.Add(report.Actual.Subnet);
        return PickFreeSubnet(others, exclude);
    }

    /// <summary>
    /// Whether <paramref name="text"/> is an address the elevated "set hotspot IP" mode may write:
    /// a strict dotted-quad, in a private range, with a usable host part (the hotspot mask is /24).
    /// </summary>
    public static bool TryValidateHotspotAddress(string text, [NotNullWhen(true)] out IPAddress? address)
    {
        if (!Ipv4Subnet.TryParseAddress(text, out address))
            return false;

        uint value = Ipv4Subnet.ToUInt(address);
        bool isPrivate = new Ipv4Subnet(0x0A000000, 8).Contains(value)       // 10.0.0.0/8
                         || new Ipv4Subnet(0xAC100000, 12).Contains(value)   // 172.16.0.0/12
                         || new Ipv4Subnet(0xC0A80000, 16).Contains(value);  // 192.168.0.0/16
        uint host = value & 0xFF;
        if (!isPrivate || host is 0 or 255)
        {
            address = null;
            return false;
        }
        return true;
    }
}
