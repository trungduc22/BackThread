using System.Net;
using System.Net.Sockets;

namespace BackThread.Transport;

public sealed record TransportLease(Stream Stream,string Name,bool Authenticated=false);
public interface ICompanionTransport { Task<TransportLease> ConnectAsync(CancellationToken ct); }
public sealed class UsbTransport(int port,string? serial=null) : ICompanionTransport
{
    public async Task<TransportLease> ConnectAsync(CancellationToken ct)
    {
        var mux=new UsbMux();var devices=await mux.ListAsync(ct);
        if(serial is null&&devices.Count>1)throw new IOException("Multiple USB devices; configure the intended device");
        var device=devices.FirstOrDefault(d=>serial is null||d.Serial==serial)??throw new IOException("Configured USB iPhone not detected");
        return new(await mux.ConnectAsync(device.Id,port,ct),"usb");
    }
}
public sealed class SimulatorTransport(int port) : ICompanionTransport
{
    public async Task<TransportLease> ConnectAsync(CancellationToken ct){var client=new TcpClient{NoDelay=true};try{await client.ConnectAsync(IPAddress.Loopback,port,ct);return new(client.GetStream(),"localhost");}catch{client.Dispose();throw;}}
}
