using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Xml.Linq;

namespace BackThread.Transport;

public sealed record UsbDevice(int Id, string Serial, string ConnectionType);

/// <summary>Independent XML-plist usbmux protocol client, compatible with Apple Mobile Device Service.</summary>
public sealed class UsbMux
{
    public const int ServicePort = 27015;
    private static async Task<TcpClient> OpenAsync(CancellationToken ct)
    {
        var c = new TcpClient { NoDelay = true };
        try { await c.ConnectAsync(IPAddress.Loopback, ServicePort, ct); return c; }
        catch { c.Dispose(); throw; }
    }
    private static XElement Value(object value) => value switch
    {
        int i => new("integer", i), bool b => new(b ? "true" : "false"), _ => new("string", value.ToString())
    };
    private static async Task<XElement> ExchangeAsync(Stream stream, Dictionary<string, object> fields, CancellationToken ct)
    {
        fields["ClientVersionString"] = "BackThread/0.1"; fields["ProgName"] = "BackThread"; fields["kLibUSBMuxVersion"] = 3;
        var dict = new XElement("dict"); foreach (var (key, value) in fields) { dict.Add(new XElement("key", key)); dict.Add(Value(value)); }
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(new XDocument(new XElement("plist", new XAttribute("version", "1.0"), dict)).ToString());
        byte[] header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(16 + payload.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 8); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(payload, ct);
        await stream.ReadExactlyAsync(header, ct);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size < 16 || size > 1024 * 1024 || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) != 8) throw new InvalidDataException("Invalid usbmux reply");
        payload = new byte[size - 16]; await stream.ReadExactlyAsync(payload, ct);
        using var reader = XmlReader.Create(new MemoryStream(payload), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        return XDocument.Load(reader).Root?.Element("dict") ?? throw new InvalidDataException("Invalid usbmux plist");
    }
    private static XElement? Get(XElement dict, string key)
        => dict.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault();
    public async Task<IReadOnlyList<UsbDevice>> ListAsync(CancellationToken ct)
    {
        using var c = await OpenAsync(ct);
        var reply = await ExchangeAsync(c.GetStream(), new() { ["MessageType"] = "ListDevices" }, ct);
        return Get(reply, "DeviceList")?.Elements("dict").Select(d =>
        {
            var p = Get(d, "Properties") ?? d;
            return new UsbDevice(int.Parse(Get(d, "DeviceID")!.Value, System.Globalization.CultureInfo.InvariantCulture), Get(p, "SerialNumber")?.Value ?? "", Get(p, "ConnectionType")?.Value ?? "USB");
        }).Where(d => d.ConnectionType == "USB").ToArray() ?? [];
    }
    public async Task<bool> HasPairRecordAsync(string serial, CancellationToken ct)
    {
        using var c = await OpenAsync(ct);
        return Get(await ExchangeAsync(c.GetStream(), new() { ["MessageType"] = "ReadPairRecord", ["PairRecordID"] = serial }, ct), "PairRecordData") is not null;
    }
    public async Task<Stream> ConnectAsync(int deviceId, int port, CancellationToken ct)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var c = await OpenAsync(ct);
        try
        {
            var stream = c.GetStream();
            int networkPort = (ushort)IPAddress.HostToNetworkOrder((short)port);
            var result = await ExchangeAsync(stream, new() { ["MessageType"] = "Connect", ["DeviceID"] = deviceId, ["PortNumber"] = networkPort }, ct);
            if (Get(result, "Number")?.Value != "0") throw new IOException($"USB port {port} unavailable (usbmux result {Get(result, "Number")?.Value ?? "unknown"})");
            return stream; // NetworkStream owns the underlying socket.
        }
        catch { c.Dispose(); throw; }
    }
}
