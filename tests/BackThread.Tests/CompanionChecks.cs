using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using BackThread.Host;
using BackThread.Transport;

namespace BackThread.Tests;

internal static class CompanionChecks
{
    public static async Task Run(Func<string,Func<Task>,Task> test,string root,byte[] key,CancellationToken ct)
    {
        await test("apps.id-only-launch-fixed-arguments-and-feedback",async()=>
        {
            string dotnet=Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")??Path.GetDirectoryName(Environment.ProcessPath)!,"dotnet.exe");
            string fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../BackThread.LaunchFixture/bin/Release/net8.0/BackThread.LaunchFixture.dll"));
            Check.True(File.Exists(fixture),"Launch fixture missing: "+fixture);string marker=Path.Combine(root,"launch-result.txt"),literal="literal & ; $(not-a-command) tiếng Việt";
            var apps=new AppCatalog(Path.Combine(root,"apps.json"));apps.Pin(new("fixture","BackThread test app","exe",dotnet,[fixture,marker,literal]));
            await Check.Throws<UnauthorizedAccessException>(async()=>await apps.LaunchRequestAsync(JsonSerializer.SerializeToElement(new{appId="fixture",arguments=new[]{"injected"}},Message.Json),ct));
            await Check.Throws<UnauthorizedAccessException>(async()=>await apps.LaunchRequestAsync(JsonSerializer.SerializeToElement(new{appId="unknown"},Message.Json),ct));
            Check.True(!File.Exists(marker));
            var result=JsonSerializer.SerializeToElement(await apps.LaunchRequestAsync(JsonSerializer.SerializeToElement(new{appId="fixture"},Message.Json),ct),Message.Json);
            Check.True(result.GetProperty("started").GetBoolean());await Check.Eventually(()=>Task.FromResult(File.Exists(marker)));Check.Equal(literal,await File.ReadAllTextAsync(marker,ct));
            string wire=JsonSerializer.Serialize(await apps.WireCatalogAsync(),Message.Json);Check.True(!wire.Contains(dotnet,StringComparison.Ordinal)&&!wire.Contains("arguments",StringComparison.Ordinal));
            var restored=new AppCatalog(Path.Combine(root,"apps.json"));Check.Equal(1,restored.Entries.Length);restored.Remove("fixture");Check.Equal(0,restored.Entries.Length);
        });
        await test("apps.invalid-paths-order-icons-and-corrupt-catalog",async()=>
        {
            var apps=new AppCatalog(Path.Combine(root,"validation-apps.json"));
            foreach(string target in new[]{"relative.exe","\\\\server\\share\\app.exe",Path.Combine(root,"missing.exe")})
                await Check.Throws<Exception>(()=>{apps.Pin(new("bad","Bad","exe",target,[]));return Task.CompletedTask;});
            await Check.Throws<InvalidDataException>(()=>{apps.Pin(new("bad","Bad","packaged","unknown!id",[],"not-png"));return Task.CompletedTask;});
            await Check.Throws<InvalidDataException>(()=>{apps.Reorder(["unknown"]);return Task.CompletedTask;});
            string path=Path.Combine(root,"corrupt-apps.json");await File.WriteAllTextAsync(path,"{not-json}",ct);var broken=new AppCatalog(path);Check.True(broken.LoadError is not null);Check.Equal(0,(await broken.WireCatalogAsync()).Length);Check.Equal("{not-json}",await File.ReadAllTextAsync(path,ct));
        });
        await test("wifi.explicit-bind-disable-and-expiry",async()=>
        {
            using var normal=new TemporaryWifiTransport(key);
            foreach(string address in new[]{"0.0.0.0","8.8.8.8","127.0.0.1"})await Check.Throws<ArgumentException>(()=>{normal.Enable(address,TimeSpan.FromMinutes(15));return Task.CompletedTask;});
            Check.True(!normal.Enabled);
            using var wifi=new TemporaryWifiTransport(key,true);var session=wifi.Enable("127.0.0.1",TimeSpan.FromMilliseconds(300));Check.True(wifi.Enabled);
            await Check.Eventually(()=>Task.FromResult(!wifi.Enabled));
            Check.Equal("Session expired",JsonSerializer.SerializeToElement(wifi.Status,Message.Json).GetProperty("state").GetString());
            await Check.Throws<SocketException>(async()=>{using var tcp=new TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,session.Port,ct);});
            wifi.Enable("127.0.0.1",TimeSpan.Zero);Check.True(wifi.Enabled);wifi.Disable();Check.True(!wifi.Enabled);
        });
        await test("wifi.tls-pin-hmac-framing-and-single-session",async()=>
        {
            using var wifi=new TemporaryWifiTransport(key,true);var enrollment=wifi.Enable("127.0.0.1",TimeSpan.FromSeconds(20));
            using var tcp=new TcpClient{NoDelay=true};await tcp.ConnectAsync(IPAddress.Loopback,enrollment.Port,ct);
            SslStream tls;try{tls=await TlsClient(tcp,enrollment.CertificateSha256,ct);}catch(Exception ex){throw new IOException("TLS server failure: "+wifi.LastFailure,ex);}
            using var tlsLifetime=tls;await Wire.WriteAsync(tls,new(LogicalChannel.Control,0,0,Message.Create("wifi.join",new{sessionId=enrollment.SessionId}).Encode()),ct);
            Task auth=Authentication.ServerAsync(tls,key,ct);var lease=await wifi.ConnectAsync(ct);await auth;
            Check.True(lease.Authenticated&&lease.Name=="wifi");
            await Wire.WriteAsync(lease.Stream,new(LogicalChannel.Control,4,0,Message.Create("ping").Encode()),ct);
            Check.Equal("ping",Message.Decode((await Wire.ReadAsync(tls,ct)).Payload).Type);
            using var duplicateWait=CancellationTokenSource.CreateLinkedTokenSource(ct);duplicateWait.CancelAfter(200);
            await Check.Throws<OperationCanceledException>(async()=>await wifi.ConnectAsync(duplicateWait.Token));
            wifi.Disable();using var closed=CancellationTokenSource.CreateLinkedTokenSource(ct);closed.CancelAfter(2000);
            await Check.Throws<IOException>(async()=>await Wire.ReadAsync(tls,closed.Token));lease.Stream.Dispose();
        });
        await test("wifi.rejects-wrong-pin-session-and-key",async()=>
        {
            using var wifi=new TemporaryWifiTransport(key,true);var enrollment=wifi.Enable("127.0.0.1",TimeSpan.FromSeconds(30));
            using(var tcp=new TcpClient()){await tcp.ConnectAsync(IPAddress.Loopback,enrollment.Port,ct);await Check.Throws<AuthenticationException>(async()=>{using var ignored=await TlsClient(tcp,new string('0',64),ct);});}
            using(var tcp=new TcpClient()){await tcp.ConnectAsync(IPAddress.Loopback,enrollment.Port,ct);using var tls=await TlsClient(tcp,enrollment.CertificateSha256,ct);await Wire.WriteAsync(tls,new(LogicalChannel.Control,0,0,Message.Create("wifi.join",new{sessionId="old-session"}).Encode()),ct);await Check.Throws<IOException>(async()=>await Wire.ReadAsync(tls,ct));}
            using(var tcp=new TcpClient()){await tcp.ConnectAsync(IPAddress.Loopback,enrollment.Port,ct);using var tls=await TlsClient(tcp,enrollment.CertificateSha256,ct);await Wire.WriteAsync(tls,new(LogicalChannel.Control,0,0,Message.Create("wifi.join",new{sessionId=enrollment.SessionId}).Encode()),ct);byte[] wrong=(byte[])key.Clone();wrong[0]^=1;await Check.Throws<UnauthorizedAccessException>(async()=>await Authentication.ServerAsync(tls,wrong,ct));}
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(250);await Check.Throws<OperationCanceledException>(async()=>await wifi.ConnectAsync(timeout.Token));Check.True(wifi.Enabled);
        });
    }
    private static async Task<SslStream> TlsClient(TcpClient tcp,string pin,CancellationToken ct)
    {
        var tls=new SslStream(tcp.GetStream(),false,(_,certificate,_,_)=>certificate is not null&&CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()),Convert.FromHexString(pin)));
        try{await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="BackThread temporary companion",EnabledSslProtocols=SslProtocols.Tls12,CertificateRevocationCheckMode=System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck},ct);return tls;}catch{tls.Dispose();throw;}
    }
}
