using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace AutoHotspot;

/// <summary>An IPv4 subnet (network address plus prefix length). Pure value type: no network or registry access.</summary>
internal readonly record struct Ipv4Subnet
{
    public uint Network { get; }
    public int PrefixLength { get; }

    public Ipv4Subnet(uint address, int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(prefixLength));

        PrefixLength = prefixLength;
        Network = address & MaskOf(prefixLength);
    }

    public static Ipv4Subnet From(IPAddress address, int prefixLength) => new(ToUInt(address), prefixLength);

    /// <summary>Parses "a.b.c.d/n" (the address part may have host bits set).</summary>
    public static bool TryParse(string text, out Ipv4Subnet subnet)
    {
        subnet = default;
        string[] parts = text.Split('/');
        if (parts.Length != 2
            || !TryParseAddress(parts[0], out IPAddress? address)
            || !int.TryParse(parts[1], out int prefix)
            || prefix is < 0 or > 32)
            return false;

        subnet = From(address, prefix);
        return true;
    }

    /// <summary>Strict dotted-quad IPv4 parse (IPAddress.TryParse alone also accepts shorthand such as "10.1").</summary>
    public static bool TryParseAddress(string text, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        if (text.Split('.').Length != 4
            || !IPAddress.TryParse(text, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork
            || parsed.ToString() != text)
            return false;

        address = parsed;
        return true;
    }

    public static uint MaskOf(int prefixLength) => prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);

    public static uint ToUInt(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        if (b.Length != 4)
            throw new ArgumentException("Not an IPv4 address.", nameof(address));
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    public static IPAddress ToAddress(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    public uint Mask => MaskOf(PrefixLength);

    public bool Contains(uint address) => (address & Mask) == Network;

    /// <summary>Subnets are aligned blocks, so two overlap exactly when the larger one contains the other's network address.</summary>
    public bool Overlaps(Ipv4Subnet other)
    {
        int shorter = Math.Min(PrefixLength, other.PrefixLength);
        uint mask = MaskOf(shorter);
        return (Network & mask) == (other.Network & mask);
    }

    /// <summary>The first host address of the subnet (network address + 1), as used for the hotspot's own address.</summary>
    public IPAddress FirstHost => ToAddress(Network + 1);

    public override string ToString() => $"{ToAddress(Network)}/{PrefixLength}";
}
