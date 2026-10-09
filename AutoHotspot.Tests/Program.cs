using System.Net;
using AutoHotspot;

// Minimal test runner: each Test(...) call runs one case, failures are collected and the exit code is non-zero if any failed.
int failures = 0;
int total = 0;

void Test(string name, Action body)
{
    total++;
    try
    {
        body();
        Console.WriteLine($"  ok    {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"  FAIL  {name}: {ex.Message}");
    }
}

void True(bool condition, string what = "expected true")
{
    if (!condition)
        throw new Exception(what);
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"expected <{expected}> but was <{actual}>");
}

Ipv4Subnet Net(string cidr)
{
    True(Ipv4Subnet.TryParse(cidr, out Ipv4Subnet s), $"bad test subnet {cidr}");
    return s;
}

OccupiedNetwork Adapter(string name, string address, int prefix) =>
    new(Ipv4Subnet.From(IPAddress.Parse(address), prefix), name, NetworkSourceKind.Adapter, IPAddress.Parse(address));

OccupiedNetwork Route(string name, string cidr) => new(Net(cidr), name, NetworkSourceKind.Route);

HotspotAdapterState Hotspot(string address, int prefix = 24) => new("Local Area Connection* 10", IPAddress.Parse(address), prefix);

Console.WriteLine("Ipv4Subnet");
Test("masks host bits off the network address", () => Equal("192.168.137.0/24", Ipv4Subnet.From(IPAddress.Parse("192.168.137.77"), 24).ToString()));
Test("/0 and /32 masks", () =>
{
    Equal(0u, Ipv4Subnet.MaskOf(0));
    Equal(uint.MaxValue, Ipv4Subnet.MaskOf(32));
    Equal("0.0.0.0/0", new Ipv4Subnet(0xC0A80101, 0).ToString());
});
Test("overlap: same subnet", () => True(Net("192.168.137.0/24").Overlaps(Net("192.168.137.0/24"))));
Test("overlap: adjacent /24s do not overlap", () => True(!Net("192.168.137.0/24").Overlaps(Net("192.168.138.0/24"))));
Test("overlap: bigger subnet contains smaller (both ways)", () =>
{
    True(Net("192.168.0.0/16").Overlaps(Net("192.168.137.0/24")));
    True(Net("192.168.137.0/24").Overlaps(Net("192.168.0.0/16")));
});
Test("overlap: /25 inside /24", () => True(Net("192.168.137.128/25").Overlaps(Net("192.168.137.0/24"))));
Test("overlap: 10/8 vs 172.16/12 do not overlap", () => True(!Net("10.0.0.0/8").Overlaps(Net("172.16.0.0/12"))));
Test("overlap: 0.0.0.0/0 overlaps everything", () => True(Net("0.0.0.0/0").Overlaps(Net("192.168.137.0/24"))));
Test("TryParse rejects junk", () =>
{
    True(!Ipv4Subnet.TryParse("192.168.137.0", out _));
    True(!Ipv4Subnet.TryParse("192.168.137.0/33", out _));
    True(!Ipv4Subnet.TryParse("192.168.137/24", out _));
    True(!Ipv4Subnet.TryParse("abc/24", out _));
});
Test("FirstHost is network + 1", () => Equal("192.168.50.1", Net("192.168.50.0/24").FirstHost.ToString()));

Console.WriteLine("Relevance filter");
Test("default route, loopback, link-local, multicast, broadcast and host routes are ignored", () =>
{
    True(!HotspotIpLogic.IsRelevant(Route("x", "0.0.0.0/0")));
    True(!HotspotIpLogic.IsRelevant(Route("x", "0.0.0.0/1")));
    True(!HotspotIpLogic.IsRelevant(Route("x", "127.0.0.0/8")));
    True(!HotspotIpLogic.IsRelevant(Route("x", "169.254.0.0/16")));
    True(!HotspotIpLogic.IsRelevant(Adapter("Wi-Fi", "169.254.12.34", 16)));
    True(!HotspotIpLogic.IsRelevant(Route("x", "224.0.0.0/4")));
    True(!HotspotIpLogic.IsRelevant(Route("x", "255.255.255.255/32")));
    True(!HotspotIpLogic.IsRelevant(Route("x", "192.168.137.20/32")));
});
Test("normal LAN, VPN and wide private routes are relevant", () =>
{
    True(HotspotIpLogic.IsRelevant(Adapter("Ethernet", "192.168.15.17", 24)));
    True(HotspotIpLogic.IsRelevant(Route("VPN", "192.168.0.0/16")));
    True(HotspotIpLogic.IsRelevant(Route("VPN", "10.0.0.0/8")));
});

Console.WriteLine("Analyze");
Test("no conflict: the default hotspot next to a 192.168.15.x uplink", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), Hotspot("192.168.137.1"),
        new[] { Adapter("MITD-OnBoard", "192.168.15.17", 24), Adapter("Wi-Fi", "169.254.10.20", 16) });
    Equal(0, r.Conflicts.Count);
    True(!r.AddressMismatch);
    Equal(0, r.Issues.Count);
});
Test("conflict: IT assigned 192.168.137.x to an adapter", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), Hotspot("192.168.137.1"),
        new[] { Adapter("Ethernet", "192.168.137.20", 24) });
    Equal(1, r.Conflicts.Count);
    Equal("Ethernet", r.Conflicts[0].Other.InterfaceName);
    Equal("192.168.137.0/24", r.Conflicts[0].HotspotSubnet.ToString());
    True(r.Conflicts[0].Describe().Contains("\"Ethernet\" (192.168.137.20/24)"), r.Conflicts[0].Describe());
});
Test("conflict: a VPN route covers the hotspot range", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), null,
        new[] { Route("WireGuard", "192.168.0.0/16") });
    Equal(1, r.Conflicts.Count);
    True(r.Conflicts[0].Describe().Contains("route to 192.168.0.0/16"), r.Conflicts[0].Describe());
});
Test("an interface is reported once, preferring its adapter address over its routes", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), null,
        new[] { Route("Ethernet", "192.168.137.0/24"), Adapter("Ethernet", "192.168.137.20", 24) });
    Equal(1, r.Conflicts.Count);
    Equal(NetworkSourceKind.Adapter, r.Conflicts[0].Other.Kind);
});
Test("catch-all VPN routes (0.0.0.0/1, 128.0.0.0/1) are not conflicts", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), null,
        new[] { Route("VPN", "0.0.0.0/1"), Route("VPN", "128.0.0.0/1"), Route("VPN", "0.0.0.0/0") });
    Equal(0, r.Conflicts.Count);
});
Test("the user's case: registry 192.168.50.1, adapter still 192.168.137.1, PC on 192.168.137.x", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.50.1"), Hotspot("192.168.137.1"),
        new[] { Adapter("Ethernet", "192.168.137.20", 24), Adapter("MITD-OnBoard", "192.168.15.17", 24) });
    True(r.AddressMismatch, "mismatch expected");
    Equal(1, r.Conflicts.Count); // the actual 192.168.137.0/24 range overlaps; the configured 192.168.50.0/24 does not
    Equal("192.168.137.0/24", r.Conflicts[0].HotspotSubnet.ToString());
    Equal(2, r.Issues.Count);
    True(r.Issues[0].Message.Contains("192.168.50.1") && r.Issues[0].Message.Contains("192.168.137.1"), r.Issues[0].Message);
});
Test("mismatch only: nothing conflicts", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.50.1"), Hotspot("192.168.137.1"),
        new[] { Adapter("MITD-OnBoard", "192.168.15.17", 24) });
    True(r.AddressMismatch);
    Equal(0, r.Conflicts.Count);
    Equal(1, r.Issues.Count);
});
Test("hotspot off (no actual adapter): configured range is still checked, no mismatch", () =>
{
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), null,
        new[] { Adapter("Ethernet", "192.168.137.20", 24) });
    True(!r.AddressMismatch);
    Equal(1, r.Conflicts.Count);
});
Test("issue keys are stable, so the same problem is announced once", () =>
{
    var others = new[] { Adapter("Ethernet", "192.168.137.20", 24) };
    HotspotIpReport a = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), Hotspot("192.168.137.1"), others);
    HotspotIpReport b = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), Hotspot("192.168.137.1"), others);
    Equal(a.Issues[0].Key, b.Issues[0].Key);
});

Console.WriteLine("Picking a new range");
Test("picks 192.168.50.0/24 when it is free", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(new[] { Adapter("MITD-OnBoard", "192.168.15.17", 24) }, new[] { Net("192.168.137.0/24") });
    Equal("192.168.50.0/24", pick!.Value.ToString());
});
Test("skips ranges used by adapters", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(new[] { Adapter("a", "192.168.50.7", 24), Adapter("b", "192.168.51.7", 24) }, Array.Empty<Ipv4Subnet>());
    Equal("192.168.52.0/24", pick!.Value.ToString());
});
Test("skips ranges covered by wider routes and subnets", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(new[] { Route("VPN", "192.168.48.0/21"), Adapter("b", "192.168.56.1", 22) }, Array.Empty<Ipv4Subnet>());
    Equal("192.168.60.0/24", pick!.Value.ToString());
});
Test("a 192.168.0.0/16 VPN route moves the pick to 10.77.x", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(new[] { Route("VPN", "192.168.0.0/16") }, Array.Empty<Ipv4Subnet>());
    Equal("10.77.50.0/24", pick!.Value.ToString());
});
Test("a 10.0.0.0/8 route as well moves it on to 172.30.x", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(new[] { Route("VPN", "192.168.0.0/16"), Route("VPN", "10.0.0.0/8") }, Array.Empty<Ipv4Subnet>());
    Equal("172.30.50.0/24", pick!.Value.ToString());
});
Test("returns null when every candidate is taken", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(
        new[] { Route("VPN", "192.168.0.0/16"), Route("VPN", "10.0.0.0/8"), Route("VPN", "172.16.0.0/12") }, Array.Empty<Ipv4Subnet>());
    True(pick == null);
});
Test("ignored networks (default route, link-local) do not block candidates", () =>
{
    Ipv4Subnet? pick = HotspotIpLogic.PickFreeSubnet(new[] { Route("x", "0.0.0.0/0"), Adapter("Wi-Fi", "169.254.1.2", 16) }, Array.Empty<Ipv4Subnet>());
    Equal("192.168.50.0/24", pick!.Value.ToString());
});
Test("candidates are all private /24s and none is 192.168.137.0/24", () =>
{
    foreach (Ipv4Subnet c in HotspotIpLogic.CandidateSubnets())
    {
        Equal(24, c.PrefixLength);
        True(HotspotIpLogic.TryValidateHotspotAddress(c.FirstHost.ToString(), out _), $"{c} is not a valid hotspot range");
        True(!c.Overlaps(Net("192.168.137.0/24")), $"{c} overlaps the reserved hotspot range");
    }
});

Console.WriteLine("Proposal");
Test("conflict: proposes a new range that differs from the current one", () =>
{
    var others = new[] { Adapter("Ethernet", "192.168.137.20", 24), Adapter("MITD-OnBoard", "192.168.15.17", 24) };
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.137.1"), Hotspot("192.168.137.1"), others);
    Equal("192.168.50.0/24", HotspotIpLogic.ProposeSubnet(r, others)!.Value.ToString());
});
Test("user's case: the free configured 192.168.50.1 is proposed (just apply it)", () =>
{
    var others = new[] { Adapter("Ethernet", "192.168.137.20", 24), Adapter("MITD-OnBoard", "192.168.15.17", 24) };
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.50.1"), Hotspot("192.168.137.1"), others);
    Equal("192.168.50.0/24", HotspotIpLogic.ProposeSubnet(r, others)!.Value.ToString());
});
Test("mismatch where the configured range also conflicts: picks something else", () =>
{
    var others = new[] { Adapter("Ethernet", "192.168.50.20", 24) };
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.50.1"), Hotspot("192.168.137.1"), others);
    Equal("192.168.51.0/24", HotspotIpLogic.ProposeSubnet(r, others)!.Value.ToString());
});
Test("manual change with no problem still proposes a different range than the current one", () =>
{
    var others = new[] { Adapter("MITD-OnBoard", "192.168.15.17", 24) };
    HotspotIpReport r = HotspotIpLogic.Analyze(IPAddress.Parse("192.168.50.1"), Hotspot("192.168.50.1"), others);
    Equal("192.168.51.0/24", HotspotIpLogic.ProposeSubnet(r, others)!.Value.ToString());
});

Console.WriteLine("Address validation (elevated mode argument)");
Test("accepts private host addresses", () =>
{
    True(HotspotIpLogic.TryValidateHotspotAddress("192.168.50.1", out _));
    True(HotspotIpLogic.TryValidateHotspotAddress("10.77.50.1", out _));
    True(HotspotIpLogic.TryValidateHotspotAddress("172.30.50.1", out _));
    True(HotspotIpLogic.TryValidateHotspotAddress("172.16.0.1", out _));
});
Test("rejects public, malformed, network and broadcast addresses", () =>
{
    foreach (string bad in new[] { "8.8.8.8", "172.32.0.1", "192.169.1.1", "192.168.50.0", "192.168.50.255", "192.168.50", "192.168.50.1/24",
                                   "192.168.050.1", "::1", "", " 192.168.50.1", "192.168.50.1 ", "256.1.1.1", "192.168.50.1; calc" })
        True(!HotspotIpLogic.TryValidateHotspotAddress(bad, out _), $"'{bad}' should be rejected");
});

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"All {total} tests passed." : $"{failures} of {total} tests FAILED.");
return failures == 0 ? 0 : 1;
