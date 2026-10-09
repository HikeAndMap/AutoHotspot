using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoHotspot;

/// <summary>What this PC looks like from the hotspot's point of view, read in one go.</summary>
/// <param name="Configured">The hotspot address from the registry (or Microsoft's default when none is set).</param>
/// <param name="Actual">The address on the hotspot's virtual adapter, or null when the hotspot is off.</param>
/// <param name="Others">Every adapter address and route that is not the hotspot adapter itself.</param>
internal sealed record NetworkSnapshot(IPAddress Configured, HotspotAdapterState? Actual, IReadOnlyList<OccupiedNetwork> Others)
{
    public HotspotIpReport Analyze() => HotspotIpLogic.Analyze(Configured, Actual, Others);
}

/// <summary>
/// Reads (never writes) the hotspot's configured address, the address on its virtual adapter, and
/// every other IPv4 subnet in use on the PC. The decisions made with this data are in HotspotIpLogic.
/// </summary>
internal static class HotspotNetwork
{
    private const string HotspotAdapterDescription = "Wi-Fi Direct Virtual Adapter";

    public static NetworkSnapshot Read()
    {
        IPAddress configured = HotspotScopeRegistry.ReadScopeAddress() ?? HotspotIpLogic.DefaultHotspotAddress;

        var others = new List<OccupiedNetwork>();
        var hotspotCandidates = new List<HotspotAdapterState>();
        var hotspotIndexes = new HashSet<int>();
        var namesByIndex = new Dictionary<int, string>();

        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            int? index = TryGetIpv4Index(nic);
            bool isHotspotAdapter = nic.Description.Contains(HotspotAdapterDescription, StringComparison.OrdinalIgnoreCase);
            if (index is int i)
            {
                namesByIndex[i] = nic.Name;
                if (isHotspotAdapter)
                    hotspotIndexes.Add(i);
            }

            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.PrefixLength is < 1 or > 32)
                    continue;

                if (isHotspotAdapter)
                {
                    hotspotCandidates.Add(new HotspotAdapterState(nic.Name, ua.Address, ua.PrefixLength));
                }
                else
                {
                    others.Add(new OccupiedNetwork(Ipv4Subnet.From(ua.Address, ua.PrefixLength), nic.Name,
                        NetworkSourceKind.Adapter, ua.Address));
                }
            }
        }

        foreach (IpForwardRoute route in RoutingTable.Read())
        {
            if (hotspotIndexes.Contains(route.InterfaceIndex))
                continue;

            string name = namesByIndex.TryGetValue(route.InterfaceIndex, out string? n) ? n : $"interface {route.InterfaceIndex}";
            others.Add(new OccupiedNetwork(route.Subnet, name, NetworkSourceKind.Route));
        }

        return new NetworkSnapshot(configured, PickHotspotAdapter(hotspotCandidates, configured), others);
    }

    /// <summary>
    /// The hotspot adapter's real address: a non-link-local address on a Wi-Fi Direct virtual
    /// adapter that is up, preferring the one that matches the configured address.
    /// </summary>
    private static HotspotAdapterState? PickHotspotAdapter(List<HotspotAdapterState> candidates, IPAddress configured)
    {
        List<HotspotAdapterState> real = candidates
            .Where(c => !IsLinkLocal(c.Address))
            .ToList();
        return real.FirstOrDefault(c => c.Address.Equals(configured)) ?? real.FirstOrDefault();
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    private static int? TryGetIpv4Index(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GetIPv4Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
            return null; // adapter has no IPv4 stack
        }
    }
}

/// <summary>The hotspot (Internet Connection Sharing) address settings in HKLM.</summary>
internal static class HotspotScopeRegistry
{
    public const string KeyPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters";
    public const string ScopeAddressName = "ScopeAddress";
    public const string ScopeAddressBackupName = "ScopeAddressBackup";

    public static IPAddress? ReadScopeAddress()
    {
        string? raw = ReadRaw(ScopeAddressName);
        return raw != null && Ipv4Subnet.TryParseAddress(raw.Trim(), out IPAddress? address) ? address : null;
    }

    /// <summary>The raw string value, or null when the value does not exist.</summary>
    public static string? ReadRaw(string valueName)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? key = baseKey.OpenSubKey(KeyPath);
        return key?.GetValue(valueName) as string;
    }

    /// <summary>Writes (or, for null, deletes) a value. Needs administrator rights.</summary>
    public static void WriteRaw(string valueName, string? value)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey key = baseKey.CreateSubKey(KeyPath, writable: true);
        if (value == null)
            key.DeleteValue(valueName, throwOnMissingValue: false);
        else
            key.SetValue(valueName, value, RegistryValueKind.String);
    }
}

internal readonly record struct IpForwardRoute(Ipv4Subnet Subnet, int InterfaceIndex);

/// <summary>The IPv4 routing table through GetIpForwardTable (iphlpapi).</summary>
internal static class RoutingTable
{
    private const int ErrorInsufficientBuffer = 122;
    private const int RowSize = 56;        // MIB_IPFORWARDROW: 14 DWORDs
    private const int InterfaceIndexOffset = 16;
    private const int TypeOffset = 20;
    private const uint RouteTypeInvalid = 2;

    [DllImport("iphlpapi.dll")]
    private static extern int GetIpForwardTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order);

    public static IReadOnlyList<IpForwardRoute> Read()
    {
        int size = 0;
        int result = GetIpForwardTable(IntPtr.Zero, ref size, false);
        if (result != ErrorInsufficientBuffer && result != 0)
            throw new InvalidOperationException($"GetIpForwardTable failed ({result}).");

        for (int attempt = 0; attempt < 5; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetIpForwardTable(buffer, ref size, false);
                if (result == ErrorInsufficientBuffer)
                    continue; // table grew between the two calls

                if (result != 0)
                    throw new InvalidOperationException($"GetIpForwardTable failed ({result}).");

                return Parse(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("The routing table kept changing while it was being read.");
    }

    private static List<IpForwardRoute> Parse(IntPtr buffer)
    {
        int count = Marshal.ReadInt32(buffer);
        var routes = new List<IpForwardRoute>(count);
        for (int i = 0; i < count; i++)
        {
            IntPtr row = buffer + 4 + i * RowSize;
            if ((uint)Marshal.ReadInt32(row, TypeOffset) == RouteTypeInvalid)
                continue;

            uint destination = ReadNetworkOrder(row, 0);
            uint mask = ReadNetworkOrder(row, 4);
            int index = Marshal.ReadInt32(row, InterfaceIndexOffset);
            routes.Add(new IpForwardRoute(new Ipv4Subnet(destination, BitOperations.PopCount(mask)), index));
        }
        return routes;
    }

    /// <summary>Addresses in the table are stored with the first octet in the first byte.</summary>
    private static uint ReadNetworkOrder(IntPtr row, int offset)
    {
        return ((uint)Marshal.ReadByte(row, offset) << 24) | ((uint)Marshal.ReadByte(row, offset + 1) << 16)
             | ((uint)Marshal.ReadByte(row, offset + 2) << 8) | Marshal.ReadByte(row, offset + 3);
    }
}
