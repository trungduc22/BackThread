using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

namespace BackThread.Transport;

public sealed record WifiEnrollment(string Address,int Port,string SessionId,string CertificateSha256,int TtlSeconds,bool PreferUsb);
/// <summary>Explicit, session-scoped TLS listener. One selected local interface, no firewall or trust-store changes.</summary>
public sealed class TemporaryWifiTransport(byte[] key,bool allowLoopbackForTests=false) : ICompanionTransport,IDisposable
{
    private sealed class Session : IDisposable
    {
        public readonly CancellationTokenSource Stop=new();
        public readonly Channel<TransportLease> Ready=Channel.CreateBounded<TransportLease>(1);
        public readonly object Gate=new();
        public required TcpListener Listener;public required X509Certificate2 Certificate;public required WifiEnrollment Enrollment;
        public long Started=Stopwatch.GetTimestamp();public Stream? Active;public Task? Serving;
        public string Stage="awaiting-tcp", FailureStage="", FailureKind="";
        public int TcpAccepted, TlsCompleted, EnrollmentAccepted, Authenticated;
        public void Dispose(){Stop.Cancel();Listener.Stop();lock(Gate){Active?.Dispose();Active=null;}Ready.Writer.TryComplete();Certificate.Dispose();}
    }
    private readonly object gate=new();
    private Session? current;private string lastState="Off";
    public string LastFailure {get;private set;}="";
    public bool Enabled{get{lock(gate)return current is not null&&!current.Stop.IsCancellationRequested;}}
    public bool PreferUsb{get{lock(gate)return current?.Enrollment.PreferUsb??true;}}
    public event Action? Changed;
    public object Status
    {
        get
        {
            lock (gate)
            {
                var s = current;
                if (s is null) return new { enabled = false, state = lastState, remainingSeconds = 0, untilDisconnected = false, preferUsb = true };
                lock (s.Gate) return new
                {
                    enabled = true, state = s.Active is null ? "Waiting for iPhone" : "TLS authenticated",
                    remainingSeconds = s.Enrollment.TtlSeconds == 0 ? 0 : Math.Max(0, s.Enrollment.TtlSeconds - Stopwatch.GetElapsedTime(s.Started).TotalSeconds),
                    untilDisconnected = s.Enrollment.TtlSeconds == 0, preferUsb = s.Enrollment.PreferUsb,
                    stage = s.Stage, failureStage = s.FailureStage, failureKind = s.FailureKind,
                    tcpAccepted = s.TcpAccepted, tlsCompleted = s.TlsCompleted, enrollmentAccepted = s.EnrollmentAccepted, authenticated = s.Authenticated
                };
            }
        }
    }
    public WifiEnrollment Enable(string address,TimeSpan lifetime,bool preferUsb=true)
    {
        if(lifetime<TimeSpan.Zero||lifetime>TimeSpan.FromMinutes(30))throw new ArgumentOutOfRangeException(nameof(lifetime));
        if(!IPAddress.TryParse(address,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||IPAddress.Any.Equals(ip)||IPAddress.Broadcast.Equals(ip))throw new ArgumentException("Choose one local private IPv4 interface");
        var bytes=ip.GetAddressBytes();bool local=IPAddress.IsLoopback(ip);
        bool privateAddress=bytes[0]==10||bytes[0]==172&&bytes[1] is >=16 and <=31||bytes[0]==192&&bytes[1]==168||bytes[0]==169&&bytes[1]==254;
        if(!(allowLoopbackForTests&&local)&&(!privateAddress||!NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Any(a=>a.Address.Equals(ip))))throw new ArgumentException("The Wi-Fi listener must use a selected private address assigned to this PC");
        Disable();
        using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=BackThread temporary companion",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        // Windows 10 Schannel cannot use the ephemeral key returned by CreateSelfSigned.
        // Import in the current user's temporary key container, never a certificate trust store.
        byte[] pfx=generated.Export(X509ContentType.Pfx);X509Certificate2 certificate;
        try{certificate=new X509Certificate2(pfx,(string?)null,X509KeyStorageFlags.UserKeySet);}
        finally{CryptographicOperations.ZeroMemory(pfx);}
        var listener=new TcpListener(ip,0);try{listener.Start(2);}catch{certificate.Dispose();throw;}
        var enrollment=new WifiEnrollment(ip.ToString(),((IPEndPoint)listener.LocalEndpoint).Port,Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant(),(int)Math.Ceiling(lifetime.TotalSeconds),preferUsb);
        var s=new Session{Certificate=certificate,Listener=listener,Enrollment=enrollment};lock(gate){current=s;lastState="Waiting for iPhone";}
        s.Serving=ServeAsync(s);if(lifetime>TimeSpan.Zero)_=ExpireAsync(s,lifetime);Changed?.Invoke();return enrollment;
    }
    private async Task ExpireAsync(Session session,TimeSpan lifetime){try{await Task.Delay(lifetime,session.Stop.Token);lock(gate){if(current!=session)return;current=null;lastState="Session expired";}session.Dispose();Changed?.Invoke();}catch(OperationCanceledException)when(session.Stop.IsCancellationRequested){}}
    private async Task ServeAsync(Session session)
    {
        var attempts=new Queue<long>();
        while(!session.Stop.IsCancellationRequested)
        {
            TcpClient? client=null;SslStream? secure=null;
            string stage="awaiting-tcp";
            try
            {
                client=await session.Listener.AcceptTcpClientAsync(session.Stop.Token);client.NoDelay=true;
                while(attempts.Count>0&&Stopwatch.GetElapsedTime(attempts.Peek())>TimeSpan.FromMinutes(1))attempts.Dequeue();
                bool occupied;lock(session.Gate)occupied=session.Active is not null;
                if(attempts.Count>=8||occupied){client.Dispose();continue;}attempts.Enqueue(Stopwatch.GetTimestamp());
                stage="tls";lock(session.Gate){session.Stage=stage;session.TcpAccepted++;}
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(session.Stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var cancel=timeout.Token.Register(client.Dispose);
                secure=new SslStream(client.GetStream(),false);
                await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=session.Certificate,EnabledSslProtocols=SslProtocols.Tls12,ClientCertificateRequired=false,CertificateRevocationCheckMode=X509RevocationMode.NoCheck},timeout.Token);
                stage="enrollment";lock(session.Gate){session.Stage=stage;session.TlsCompleted++;}
                var join=await Wire.ReadAsync(secure,timeout.Token);var message=Message.Decode(join.Payload);
                if(join.Channel!=LogicalChannel.Control||message.Type!="wifi.join"||!message.Body.TryGetProperty("sessionId",out var id)||id.GetString()!=session.Enrollment.SessionId)throw new UnauthorizedAccessException("Temporary session identity rejected");
                stage="hmac";lock(session.Gate){session.Stage=stage;session.EnrollmentAccepted++;}
                await Authentication.ClientAsync(secure,key,timeout.Token);
                stage="handover";lock(session.Gate){session.Stage=stage;session.Authenticated++;}
                SslStream accepted=secure;
                var owned=new LeaseStream(accepted,()=>{lock(session.Gate){session.Active=null;session.Stage="awaiting-tcp";}});
                lock(session.Gate){session.Stop.Token.ThrowIfCancellationRequested();session.Active=owned;session.Stage="authenticated";session.FailureStage=session.FailureKind="";}
                if(!session.Ready.Writer.TryWrite(new(owned,"wifi",true))){owned.Dispose();throw new IOException("Another Wi-Fi handover is pending");}
                secure=null;client=null;Changed?.Invoke();
            }
            catch(Exception)when(session.Stop.IsCancellationRequested){break;}
            catch(Exception ex)when(ex is IOException or AuthenticationException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or SocketException or ArgumentException){
                lock(session.Gate){session.Stage="awaiting-tcp";session.FailureStage=stage;session.FailureKind=ex.GetType().Name;}
                LastFailure=stage+": "+ex.GetType().Name;
            }
            finally{secure?.Dispose();client?.Dispose();}
        }
    }
    public async Task<TransportLease> ConnectAsync(CancellationToken ct)
    {
        Session s;lock(gate)s=current??throw new IOException("Temporary Wi-Fi is not authorized");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct,s.Stop.Token);
        try{return await s.Ready.Reader.ReadAsync(timeout.Token);}catch(ChannelClosedException ex){throw new IOException("Temporary Wi-Fi session ended",ex);}
    }
    public void Disable(){Session? old;lock(gate){old=current;current=null;lastState="Off";}old?.Dispose();Changed?.Invoke();}
    public void Dispose()=>Disable();
    private sealed class LeaseStream(Stream inner,Action closed) : Stream
    {
        private int disposed;
        public override bool CanRead=>inner.CanRead;public override bool CanWrite=>inner.CanWrite;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>inner.ReadAsync(buffer,ct);
        public override void Write(byte[] buffer,int offset,int count)=>inner.Write(buffer,offset,count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)=>inner.WriteAsync(buffer,ct);
        public override void Flush()=>inner.Flush();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(Interlocked.Exchange(ref disposed,1)==0){if(disposing)inner.Dispose();closed();}base.Dispose(disposing);}
    }
}
