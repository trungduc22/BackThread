using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace BackThread.Transport;

/// <summary>
/// Smart local NIC discovery (skipping host-only virtual adapters while preserving external Hyper-V switches with gateways,
/// prioritizing private LAN IPv4 addresses ahead of Tailscale 100.64.0.0/10) plus lightweight mDNS (_backthread._tcp.local.) advertiser.
/// </summary>
public static class LanNetworkDiscovery
{
    public const string MdnsServiceType = "_backthread._tcp.local";
    public static readonly IPAddress MdnsMulticastAddress = IPAddress.Parse("224.0.0.251");
    public const int MdnsPort = 5353;

    public static IReadOnlyList<string> GetReachableAddresses()
    {
        var lan = new List<string>();
        var tailscale = new List<string>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                string desc = ni.Description + " " + ni.Name;
                bool isTailscale = desc.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);
                var props = ni.GetIPProperties();
                bool hasIpv4Gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                if (!isTailscale && ShouldSkipAdapter(desc, hasIpv4Gateway))
                    continue;

                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IsTailscaleAddress(ua.Address))
                        tailscale.Add(ua.Address.ToString());
                    else if (IsPrivateLanIpv4(ua.Address))
                        lan.Add(ua.Address.ToString());
                }
            }
        }
        catch
        {
            // Return whatever was discovered cleanly.
        }

        return lan.Concat(tailscale).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Virtual adapters (Hyper-V, WSL, VMware, VirtualBox, Bluetooth PAN) are skipped unless they own an IPv4 default gateway
    /// (for example a Hyper-V External vSwitch bridging the physical NIC).
    /// </summary>
    public static bool ShouldSkipAdapter(string descriptionAndName, bool hasIpv4Gateway)
        => !hasIpv4Gateway && (
            descriptionAndName.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("WSL", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("VMware", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("VBox", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("Docker", StringComparison.OrdinalIgnoreCase)
            || descriptionAndName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase));

    public static bool IsPrivateLanIpv4(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    public static bool IsTailscaleAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    public static TransportRouteKind ClassifyRemoteAddress(IPAddress? address)
    {
        if (address is null) return TransportRouteKind.WifiLan;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return TransportRouteKind.Localhost;
        if (IsTailscaleAddress(address)) return TransportRouteKind.Tailscale;
        return TransportRouteKind.WifiLan;
    }

    /// <summary>Builds a minimal RFC 6762/6763 mDNS announcement packet for _backthread._tcp.local.</summary>
    public static byte[] BuildMdnsAnnouncementPacket(string instanceName, string hostLabel, int port, string pcId, string fingerprintPrefix, IPAddress? ipv4)
    {
        string safeInstance = SanitizeDnsLabel(instanceName);
        string safeHost = SanitizeDnsLabel(hostLabel);
        string fullService = MdnsServiceType;
        string fullInstance = $"{safeInstance}.{fullService}";
        string fullHost = $"{safeHost}.local";

        using var ms = new MemoryStream(512);
        // Header: ID=0, Flags=0x8400 (Response + Authoritative), QDCOUNT=0, ANCOUNT=3 or 4, NSCOUNT=0, ARCOUNT=0
        ushort anCount = ipv4 is not null && ipv4.AddressFamily == AddressFamily.InterNetwork ? (ushort)4 : (ushort)3;
        WriteU16(ms, 0);
        WriteU16(ms, 0x8400);
        WriteU16(ms, 0);
        WriteU16(ms, anCount);
        WriteU16(ms, 0);
        WriteU16(ms, 0);

        // 1. PTR: _backthread._tcp.local -> <instance>._backthread._tcp.local
        WriteName(ms, fullService);
        WriteU16(ms, 12); // PTR
        WriteU16(ms, 1);  // IN
        WriteU32(ms, 120);
        byte[] ptrRdata = EncodeNameBytes(fullInstance);
        WriteU16(ms, (ushort)ptrRdata.Length);
        ms.Write(ptrRdata);

        // 2. SRV: <instance>._backthread._tcp.local -> port + <host>.local
        WriteName(ms, fullInstance);
        WriteU16(ms, 33); // SRV
        WriteU16(ms, 0x8001); // IN + cache-flush
        WriteU32(ms, 120);
        byte[] targetBytes = EncodeNameBytes(fullHost);
        WriteU16(ms, (ushort)(6 + targetBytes.Length));
        WriteU16(ms, 0); // priority
        WriteU16(ms, 0); // weight
        WriteU16(ms, (ushort)port);
        ms.Write(targetBytes);

        // 3. TXT: v=1, pcId=..., fp=...
        WriteName(ms, fullInstance);
        WriteU16(ms, 16); // TXT
        WriteU16(ms, 0x8001);
        WriteU32(ms, 120);
        byte[] txtRdata = EncodeTxtRecords([
            "v=1",
            $"pc={pcId}",
            $"fp={fingerprintPrefix}",
            $"port={port}"
        ]);
        WriteU16(ms, (ushort)txtRdata.Length);
        ms.Write(txtRdata);

        // 4. A record (if private IPv4 known)
        if (ipv4 is not null && ipv4.AddressFamily == AddressFamily.InterNetwork)
        {
            WriteName(ms, fullHost);
            WriteU16(ms, 1); // A
            WriteU16(ms, 0x8001);
            WriteU32(ms, 120);
            WriteU16(ms, 4);
            ms.Write(ipv4.GetAddressBytes());
        }

        return ms.ToArray();
    }

    private static string SanitizeDnsLabel(string raw)
    {
        var sb = new StringBuilder(Math.Min(raw.Length, 48));
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
            if (sb.Length >= 48) break;
        }
        return sb.Length > 0 ? sb.ToString() : "BackThread-PC";
    }

    private static byte[] EncodeNameBytes(string dottedName)
    {
        using var ms = new MemoryStream();
        WriteName(ms, dottedName);
        return ms.ToArray();
    }

    private static void WriteName(Stream s, string dottedName)
    {
        foreach (string part in dottedName.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(part);
            int len = Math.Min(bytes.Length, 63);
            s.WriteByte((byte)len);
            s.Write(bytes, 0, len);
        }
        s.WriteByte(0);
    }

    private static byte[] EncodeTxtRecords(string[] entries)
    {
        using var ms = new MemoryStream();
        foreach (string e in entries)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(e);
            int len = Math.Min(bytes.Length, 255);
            ms.WriteByte((byte)len);
            ms.Write(bytes, 0, len);
        }
        return ms.ToArray();
    }

    private static void WriteU16(Stream s, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        s.Write(b);
    }
}

/// <summary>
/// Single-socket low-overhead mDNS announcer for _backthread._tcp.local. so paired iPhones
/// automatically follow DHCP IP changes on the same LAN without re-pairing.
/// </summary>
public sealed class MdnsAdvertiser : IAsyncDisposable
{
    private readonly string instanceName;
    private readonly int port;
    private readonly string pcId;
    private readonly string fingerprintPrefix;
    private readonly CancellationTokenSource stop = new();
    private Task? loop;

    public MdnsAdvertiser(string instanceName, int port, string pcId, string fingerprint)
    {
        this.instanceName = instanceName;
        this.port = port;
        this.pcId = pcId;
        this.fingerprintPrefix = fingerprint.Length >= 16 ? fingerprint[..16] : fingerprint;
    }

    public void Start()
    {
        if (loop is not null) return;
        loop = Task.Run(() => RunAsync(stop.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            udp.MulticastLoopback = false;
            var target = new IPEndPoint(LanNetworkDiscovery.MdnsMulticastAddress, LanNetworkDiscovery.MdnsPort);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var hosts = LanNetworkDiscovery.GetReachableAddresses();
                    IPAddress? primaryLan = hosts
                        .Select(h => IPAddress.TryParse(h, out var ip) ? ip : null)
                        .FirstOrDefault(ip => ip is not null && LanNetworkDiscovery.IsPrivateLanIpv4(ip));

                    byte[] packet = LanNetworkDiscovery.BuildMdnsAnnouncementPacket(
                        instanceName,
                        Environment.MachineName,
                        port,
                        pcId,
                        fingerprintPrefix,
                        primaryLan);
                    await udp.SendAsync(packet, target, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Network changes or firewall policies must never fail the host.
                }

                await Task.Delay(TimeSpan.FromSeconds(45), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        {
            // Multicast unavailable in this environment; direct IP/Tailscale still works.
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        if (loop is not null)
        {
            try { await loop; } catch { }
        }
        stop.Dispose();
    }
}
