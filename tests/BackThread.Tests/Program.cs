using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackThread.Audio;
using BackThread.Events;
using BackThread.Host;
using BackThread.Media;
using BackThread.Simulator;
using BackThread.Tests;
using BackThread.Transport;
using BackThread.Worker;
using BackThread.DeviceTests;

string root = Path.Combine(Path.GetTempPath(), "BackThread-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
using var global = new CancellationTokenSource(TimeSpan.FromMinutes(10)); CancellationToken ct = global.Token;
byte[] key = RandomNumberGenerator.GetBytes(32);
var results = new List<object>(); int failed = 0;
async Task Test(string name, Func<Task> action)
{
    string? filter = Environment.GetEnvironmentVariable("BACKTHREAD_TEST_FILTER");
    if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
    var watch = Stopwatch.StartNew();
    try { await action(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true, milliseconds = watch.ElapsedMilliseconds }); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); results.Add(new { name, passed = false, milliseconds = watch.ElapsedMilliseconds, error = ex.ToString() }); }
}
await Test("wire.fragmentation-and-json", async () =>
{
    var f = new Frame(LogicalChannel.Jobs, 123, 987654321, Message.Create("job.query", new { jobId = "abc" }, "request").Encode());
    using var memory = new MemoryStream(); await Wire.WriteAsync(memory, f, ct); using var fragmented = new FragmentedStream(memory.ToArray()); var read = await Wire.ReadAsync(fragmented, ct);
    Check.Equal(f.Sequence, read.Sequence); Check.Equal(f.TimestampUs, read.TimestampUs); Check.Equal("abc", Message.Decode(read.Payload).Body.GetProperty("jobId").GetString());
});
await Test("wire.async-write-retains-packet-until-completion", async () =>
{
    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var first=new DelayedWriteStream(gate.Task);using var other=new MemoryStream();
    var a=new Frame(LogicalChannel.Audio,1,123,Enumerable.Repeat((byte)0x19,1920).ToArray());
    var b=new Frame(LogicalChannel.Audio,2,456,Enumerable.Repeat((byte)0x73,1920).ToArray());
    Task writing=Wire.WriteAsync(first,a,ct).AsTask();await first.Entered.Task.WaitAsync(ct);
    try{await Wire.WriteAsync(other,b,ct);}finally{gate.TrySetResult();}
    await writing;first.Position=0;other.Position=0;
    Check.True(first.ToArray().AsSpan(0,24).SequenceEqual(Convert.FromHexString("800700004254483101000200010000007B00000000000000")),"Wire header changed from the native-compatible format");
    var one=await Wire.ReadAsync(first,ct);var two=await Wire.ReadAsync(other,ct);
    Check.True(one.Payload.SequenceEqual(a.Payload));Check.True(two.Payload.SequenceEqual(b.Payload));
    Check.Equal(a.Sequence,one.Sequence);Check.Equal(b.TimestampUs,two.TimestampUs);
});
await Test("wire.network-writer-continues-with-paused-caller", async()=>
{
    var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread=new Thread(()=>{
        var context=new PausedSynchronizationContext();Connection? connection=null;
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try{
            using var client=new TcpClient{NoDelay=true};client.Connect(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port);
            using var peer=listener.AcceptTcpClient();peer.ReceiveTimeout=1000;
            SynchronizationContext.SetSynchronizationContext(context);
            connection=new Connection(client.GetStream());
            connection.SendFrameAsync(LogicalChannel.Audio,new byte[1920],0,ct).GetAwaiter().GetResult();
            byte[] received=new byte[24+1920];peer.GetStream().ReadExactly(received);
            Check.Equal((byte)'B',received[4]);Check.Equal(1920u,BinaryPrimitives.ReadUInt32LittleEndian(received));
            completed.TrySetResult();
        }catch(Exception ex){completed.TrySetException(ex);}
        finally{
            SynchronizationContext.SetSynchronizationContext(null);context.Release();
            if(connection!=null)connection.DisposeAsync().AsTask().GetAwaiter().GetResult();listener.Stop();
        }
    }){IsBackground=true};thread.Start();await completed.Task.WaitAsync(ct);thread.Join();
});
await Test("transport.audio-queue-wait-includes-blocked-writer", async()=>
{
    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var output=new DelayedWriteStream(gate.Task);
    await using var connection=new Connection(output);
    try
    {
        await connection.SendFrameAsync(LogicalChannel.Files,[],0,ct);
        await output.Entered.Task.WaitAsync(ct);
        await connection.SendFrameAsync(LogicalChannel.Audio,new byte[1920],0,ct);
        await Task.Delay(40,ct);
        gate.TrySetResult();
        await Check.Eventually(()=>Task.FromResult(connection.AudioQueueMaximumMilliseconds>=30));
        Check.Equal(0L,connection.DroppedAudio);
    }
    finally {gate.TrySetResult();}
});
await Test("config.audio-gain-keeps-system-volume-separate", async()=>
{
    string path=Path.Combine(root,"gain.json");await File.WriteAllTextAsync(path,"{}",ct);Check.Equal(1f,HostConfig.Load(path).AudioGain);
    foreach(float gain in new[]{0f,.25f,1f}){await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{audioGain=gain},Message.Json),ct);Check.Equal(gain,HostConfig.Load(path).AudioGain);}
    foreach(float gain in new[]{-.1f,1.1f}){await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{audioGain=gain},Message.Json),ct);await Check.Throws<InvalidDataException>(()=>{HostConfig.Load(path);return Task.CompletedTask;});}
});
await Test("wire.reject-size-version-channel-truncation", async () =>
{
    var h = Wire.Header(new(LogicalChannel.Control, 1, 0, [])); BinaryPrimitives.WriteUInt32LittleEndian(h, uint.MaxValue);
    await Check.Throws<InvalidDataException>(async () => await Wire.ReadAsync(new MemoryStream(h), ct));
    h = Wire.Header(new(LogicalChannel.Control, 1, 0, [])); h[8] = 2; await Check.Throws<InvalidDataException>(async () => await Wire.ReadAsync(new MemoryStream(h), ct));
    h = Wire.Header(new(LogicalChannel.Control, 1, 0, [])); h[10] = 99; await Check.Throws<InvalidDataException>(async () => await Wire.ReadAsync(new MemoryStream(h), ct));
    await Check.Throws<EndOfStreamException>(async () => await Wire.ReadAsync(new MemoryStream(new byte[5]), ct));
    await Check.Throws<InvalidDataException>(() => { Message.Decode(Encoding.UTF8.GetBytes("{\"type\":\"\",\"body\":{}}")); return Task.CompletedTask; });
});
await Test("pcm.clipping-nan-and-bounded-ring", () =>
{
    byte[] output = new byte[12]; PcmRing.FloatToPcm16([-2, -1, 0, 1, 2, float.NaN], output);
    Check.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(output)); Check.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(6))); Check.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(10)));
    var ring = new PcmRing(16); ring.Write(Enumerable.Range(0, 12).Select(i => (byte)i).ToArray()); byte[] first = new byte[8]; ring.Read(first); ring.Write(Enumerable.Range(12, 20).Select(i => (byte)i).ToArray());
    byte[] rest = new byte[20]; Check.Equal(16, ring.Read(rest)); Check.True(rest[..16].SequenceEqual(Enumerable.Range(16, 16).Select(i => (byte)i))); Check.True(rest[16..].All(b => b == 0)); Check.Equal(8L, ring.DroppedBytes); return Task.CompletedTask;
});
await Test("storage.path-traversal-and-reserved-names", async () =>
{
    var storage = new Storage(Path.Combine(root, "safe"));
    foreach (string path in new[] { "../secret", "C:\\secret", "a/b", ".hidden", "CON", "aux.txt", "LPT1.bin", "a.", "a:b" }) await Check.Throws<InvalidDataException>(() => { storage.Resolve(path); return Task.CompletedTask; });
});
await Test("validation.null-json-fields", async () =>
{
    var store = new Storage(Path.Combine(root, "nulls"));
    await Check.Throws<InvalidDataException>(async () => await store.BeginAsync(new(null!, "a.bin", 0, new string('0', 64)), ct));
    await using var jobs = new JobScheduler(store, Path.Combine(root, "nulls-jobs.json"));
    await Check.Throws<InvalidDataException>(() => { jobs.Submit(new(null!, "sha256", "a.bin")); return Task.CompletedTask; });
    Check.True(new EventRouter().Accept(new(null!, "source", "t", "b", 1, DateTimeOffset.UtcNow.AddMinutes(1))) == null);
});
await Test("storage.reserves-quota-before-reception", async () =>
{
    var store = new Storage(Path.Combine(root, "quota"), 10); string sha = new('a', 64);
    await store.BeginAsync(new(Guid.NewGuid().ToString("N"), "first.bin", 6, sha), ct);
    await Check.Throws<IOException>(async () => await store.BeginAsync(new(Guid.NewGuid().ToString("N"), "second.bin", 5, sha), ct));
});
await Test("events.privacy-expiry-dedup-flood-bound", () =>
{
    var router = new EventRouter(); var now = DateTimeOffset.UtcNow; var e = new CompanionEvent("a", "app", "Sensitive title", "secret", 2, now.AddMinutes(1), true);
    var accepted = router.Accept(e, now)!; Check.Equal("Private notification", accepted.Title); Check.True(router.Accept(e, now) == null); Check.True(router.Accept(e with { Id = "expired", ExpiresUtc = now }, now) == null);
    for (int i = 0; i < 1000; i++) router.Accept(e with { Id = "e" + i }, now); Check.True(router.History().Length <= 10); return Task.CompletedTask;
});
await Test("handoff.requires-connectivity-and-direct-resource", () =>
{
    var provider = new DirectMediaProvider(); Check.True(!provider.Evaluate(new("https://example.com/audio.mp3"), false).Supported); Check.True(!provider.Evaluate(new("https://youtube.com/watch?v=abc"), true).Supported); Check.True(!provider.Evaluate(new("http://example.com/a.mp3"), true).Supported); Check.True(provider.Evaluate(new("https://example.com/a.mp3"), true).Supported); return Task.CompletedTask;
});
await Test("auth.wrong-key-rejected-and-recovery", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "auth")); server.Start();
    await Check.Throws<Exception>(async () => { await using var bad = await Session.Open(server.Port, RandomNumberGenerator.GetBytes(32), ct); });
    await using var good = await Session.Open(server.Port, key, ct); var hello = await good.Connection.RequestAsync(LogicalChannel.Control, "hello", new { }, ct); Check.True(hello.Body.GetProperty("simulated").GetBoolean());
});
await Test("auth.replay-rejected", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "replay")); server.Start(); byte[] captured;
    using (var first = new TcpClient())
    {
        await first.ConnectAsync(IPAddress.Loopback, server.Port, ct); var stream = first.GetStream(); var challenge = Message.Decode((await Wire.ReadAsync(stream, ct)).Payload);
        string serverNonce = challenge.Body.GetProperty("serverNonce").GetString()!, clientNonce = new('a', 64);
        captured = Message.Create("auth.response", new { clientNonce, proof = Authentication.Proof(key, "host", serverNonce, clientNonce) }).Encode();
        await Wire.WriteAsync(stream, new(LogicalChannel.Control, 0, 0, captured), ct); Check.Equal("auth.ok", Message.Decode((await Wire.ReadAsync(stream, ct)).Payload).Type);
    }
    using (var second = new TcpClient())
    {
        await second.ConnectAsync(IPAddress.Loopback, server.Port, ct); var stream = second.GetStream(); await Wire.ReadAsync(stream, ct);
        await Wire.WriteAsync(stream, new(LogicalChannel.Control, 0, 0, captured), ct); await Check.Throws<EndOfStreamException>(async () => await Wire.ReadAsync(stream, ct));
    }
    await using var good = await Session.Open(server.Port, key, ct); Check.Equal("pong", (await good.Connection.RequestAsync(LogicalChannel.Control, "ping", new { }, ct)).Type);
});
await Test("wire.malformed-socket-and-reconnect", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "malformed")); server.Start();
    using (var client = new TcpClient())
    {
        await client.ConnectAsync(IPAddress.Loopback, server.Port, ct); var stream = client.GetStream(); await Authentication.ClientAsync(stream, key, ct);
        byte[] header = Wire.Header(new(LogicalChannel.Audio, 1, 0, [])); BinaryPrimitives.WriteUInt32LittleEndian(header, 0x7fffffff); await stream.WriteAsync(header, ct);
        await Check.Throws<EndOfStreamException>(async () => await Wire.ReadAsync(stream, ct));
    }
    await using var good = await Session.Open(server.Port, key, ct); Check.Equal("ok", (await good.Connection.RequestAsync(LogicalChannel.Control, "hello", new { }, ct)).Type);
});
await Test("transport.audio-backpressure-is-bounded", async () =>
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    try
    {
        using var client = new TcpClient { SendBufferSize = 1024 }; await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, ct);
        using var peer = await listener.AcceptTcpClientAsync(ct); peer.ReceiveBufferSize = 1024;
        await using var c = new Connection(client.GetStream());
        for (int i = 0; i < 1000; i++) await c.SendFrameAsync(LogicalChannel.Audio, new byte[1920], (ulong)i * 10000, ct);
        Check.True(c.DroppedAudio > 0, "Slow consumer did not trigger audio eviction");
    }
    finally { listener.Stop(); }
});
await Test("simulator.slow-file-io-keeps-control-responsive",async()=>
{
    await using var server=new AgentServer(0,key,Path.Combine(root,"slow-file")){FileIoDelayMs=3000};
    server.Start();await using var session=await Session.Open(server.Port,key,ct);
    string path=Path.Combine(root,"slow-file.dat");byte[] data=RandomNumberGenerator.GetBytes(8192);await File.WriteAllBytesAsync(path,data,ct);
    Task<Message> upload=session.Worker.UploadAsync(path,"fixture.dat",ct);
    try
    {
        await Check.Eventually(()=>Task.FromResult(server.FileOperationsInFlight>0));
        Check.True(!upload.IsCompleted,"Injected file delay was not exercised");
        var pong=await session.Connection.RequestAsync(LogicalChannel.Control,"ping",new{},ct,TimeSpan.FromSeconds(1));
        Check.Equal("pong",pong.Type);
    }
    finally {server.FileIoDelayMs=0;await upload;}
    Check.True((await session.Worker.ReadArtifactAsync("fixture.dat",0,data.Length,ct)).SequenceEqual(data));
});
await Test("integration.heartbeat-audio-order-and-content", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "audio")); server.Start(); await using var s = await Session.Open(server.Port, key, ct);
    await s.Connection.RequestAsync(LogicalChannel.Control, "audio.start", new { sampleRate = 48000, channels = 2, bits = 16 }, ct);
    using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    for (int i = 0; i < 100; i++) { var pcm = PcmRing.Sine(i * 480); expected.AppendData(pcm); await s.Connection.SendFrameAsync(LogicalChannel.Audio, pcm, (ulong)i * 10000, ct); await Task.Delay(10, ct); }
    await Check.Eventually(async () => (await s.Connection.RequestAsync(LogicalChannel.Diagnostics, "audio.stats", new { }, ct)).Body.GetProperty("packets").GetInt64() == 100);
    var stats = (await s.Connection.RequestAsync(LogicalChannel.Diagnostics, "audio.stats", new { }, ct)).Body;
    Check.Equal(Convert.ToHexString(expected.GetHashAndReset()).ToLowerInvariant(), stats.GetProperty("sha256").GetString()); Check.Equal(0L, stats.GetProperty("gaps").GetInt64());
    Check.Equal("pong", (await s.Connection.RequestAsync(LogicalChannel.Control, "ping", new { }, ct)).Type);
});
await Test("integration.transfer-hash-text-download-and-failed-job", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "files")); server.Start(); await using var s = await Session.Open(server.Port, key, ct);
    string path = Path.Combine(root, "text.txt"); await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat("hello world\n", 10000)), ct);
    string expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))).ToLowerInvariant();
    var upload = await s.Worker.UploadAsync(path, "backup.txt", ct); Check.Equal(expected, upload.Body.GetProperty("sha256").GetString());
    await s.Worker.SubmitAsync(new("hash", "sha256", "backup.txt"), ct); await Check.Eventually(async () => (await s.Worker.QueryAsync("hash", ct)).Body.GetProperty("state").GetString() == "completed");
    Check.Equal(expected, (await s.Worker.QueryAsync("hash", ct)).Body.GetProperty("result").GetProperty("sha256").GetString());
    var bytes = await s.Worker.ReadArtifactAsync("backup.txt", 10, 32768, ct); Check.True(bytes.SequenceEqual((await File.ReadAllBytesAsync(path, ct)).AsSpan(10, 32768).ToArray()));
    await s.Worker.SubmitAsync(new("text", "text", "backup.txt"), ct); await Check.Eventually(async () => (await s.Worker.QueryAsync("text", ct)).Body.GetProperty("state").GetString() == "completed");
    var text = (await s.Worker.QueryAsync("text", ct)).Body.GetProperty("result"); Check.Equal(10000L, text.GetProperty("lines").GetInt64()); Check.Equal(20000L, text.GetProperty("words").GetInt64());
    await s.Worker.SubmitAsync(new("missing", "sha256", "missing.bin"), ct); await Check.Eventually(async () => (await s.Worker.QueryAsync("missing", ct)).Body.GetProperty("state").GetString() == "failed");
    await s.Worker.SubmitAsync(new("verify", "verify", "backup.txt", new string('0', 64)), ct); await Check.Eventually(async () => (await s.Worker.QueryAsync("verify", ct)).Body.GetProperty("state").GetString() == "failed");
});
await Test("worker.rejects-corrupt-artifact-ranges-and-recovers", async () =>
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var peer = Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync(ct); var stream = socket.GetStream();
        await Authentication.ServerAsync(stream, key, ct);
        for (int i = 0; i < 4; i++)
        {
            var request = Message.Decode((await Wire.ReadAsync(stream, ct)).Payload);
            string transfer = request.Body.GetProperty("transferId").GetString()!;
            byte[] data = i == 1 ? [1, 2, 3, 4, 5] : [1, 2, 3, 4];
            await Wire.WriteAsync(stream, new(LogicalChannel.Files, (uint)i, 0, Storage.Chunk(transfer, i == 0 ? 9 : 8, data)), ct);
            await Wire.WriteAsync(stream, new(LogicalChannel.Jobs, (uint)i, 0, Message.Create("ok", new { length = i == 2 ? 5 : data.Length }, replyTo: request.Id).Encode()), ct);
        }
    }, ct);
    try
    {
        await using var session = await Session.Open(port, key, ct);
        for (int i = 0; i < 3; i++) await Check.Throws<InvalidDataException>(() => session.Worker.ReadArtifactAsync("fixture.dat", 8, 4, ct));
        Check.True((await session.Worker.ReadArtifactAsync("fixture.dat", 8, 4, ct)).SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        await peer;
    }
    finally { listener.Stop(); }
});
await Test("storage.resume-duplicate-incomplete-and-checksum", async () =>
{
    var store = new Storage(Path.Combine(root, "resume")); byte[] data = RandomNumberGenerator.GetBytes(90000); string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), id = Guid.NewGuid().ToString("N"); var spec = new TransferSpec(id, "data.bin", data.Length, sha);
    Check.Equal(0L, await store.BeginAsync(spec, ct)); await store.AppendAsync(Storage.Chunk(id, 0, data.AsSpan(0, 32768)), ct);
    store = new(Path.Combine(root, "resume")); Check.Equal(32768L, await store.BeginAsync(spec, ct)); Check.Equal(32768L, (await store.AppendAsync(Storage.Chunk(id, 0, data.AsSpan(0, 32768)), ct)).Offset);
    await Check.Throws<InvalidDataException>(async () => await store.CommitAsync(id, ct));
    await Check.Throws<InvalidDataException>(async () => await store.BeginAsync(spec with { Name = "other.bin" }, ct));
    await Check.Throws<InvalidDataException>(async () => await store.AppendAsync(Storage.Chunk(id, 40000, data.AsSpan(0, 4)), ct));
    await store.AppendAsync(Storage.Chunk(id, 32768, data.AsSpan(32768, 32768)), ct); await store.AppendAsync(Storage.Chunk(id, 65536, data.AsSpan(65536)), ct); await store.CommitAsync(id, ct);
    Check.True((await File.ReadAllBytesAsync(store.Resolve("data.bin"), ct)).SequenceEqual(data));
});
await Test("worker.cancel-low-memory-duplicate-and-restart", async () =>
{
    var store = new Storage(Path.Combine(root, "jobs")); await File.WriteAllBytesAsync(store.Resolve("large.bin"), new byte[8 * 1024 * 1024], ct); string journal = Path.Combine(root, "jobs-state.json");
    await using (var jobs = new JobScheduler(store, journal))
    {
        jobs.MemoryPressure = true; await Check.Throws<IOException>(() => { jobs.Submit(new("pressure", "sha256", "large.bin")); return Task.CompletedTask; }); jobs.MemoryPressure = false;
        jobs.Changed += state => { if (state.State == "running") jobs.MemoryPressure = true; };
        jobs.Submit(new("cancel", "sha256", "large.bin")); Check.Equal("cancel", jobs.Submit(new("cancel", "sha256", "large.bin")).JobId);
        await Check.Throws<InvalidDataException>(() => { jobs.Submit(new("cancel", "metadata", "large.bin")); return Task.CompletedTask; });
        Check.True(jobs.Cancel("cancel")); await Check.Eventually(() => Task.FromResult(jobs.Query("cancel")?.State == "cancelled"));
    }
    await using (var recovered = new JobScheduler(store, journal)) Check.Equal("cancelled", recovered.Query("cancel")?.State);
    await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new[] { new JobState("stale", "running", .4) }), ct);
    await using var restart = new JobScheduler(store, journal); Check.Equal("interrupted", restart.Query("stale")?.State);
});
await Test("integration.socket-reset-reconnect-and-format-error", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "reconnect")); server.Start();
    await using (var s = await Session.Open(server.Port, key, ct))
    {
        await Check.Throws<IOException>(async () => await s.Connection.RequestAsync(LogicalChannel.Control, "audio.start", new { sampleRate = 44100, channels = 2, bits = 16 }, ct));
        await Check.Throws<Exception>(async () => await s.Connection.RequestAsync(LogicalChannel.Control, "fault.disconnect", new { }, ct));
    }
    await using var again = await Session.Open(server.Port, key, ct); Check.Equal("ok", (await again.Connection.RequestAsync(LogicalChannel.Control, "hello", new { }, ct)).Type);
});
await Test("integration.notification-routing", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "events")); server.Start(); await using var s = await Session.Open(server.Port, key, ct);
    var e = new CompanionEvent("private", "test", "hidden", "secret", 2, DateTimeOffset.UtcNow.AddMinutes(1), true);
    Check.True((await s.Connection.RequestAsync(LogicalChannel.Notifications, "event.publish", e, ct)).Body.GetProperty("accepted").GetBoolean());
    var events = (await s.Connection.RequestAsync(LogicalChannel.Notifications, "event.history", new { }, ct)).Body.GetProperty("events"); Check.Equal("Private notification", events[0].GetProperty("title").GetString());
});

await Test("acceptance.audio-window-excludes-slow-shutdown-tail", async () =>
{
    int reads=0;bool sourceActive=false;Task? lateReply=null;var watch=Stopwatch.StartNew();
    var result=await AudioMeasurementWindow.RunAsync(TimeSpan.FromMilliseconds(600),
        async(started,token)=>{sourceActive=true;started();try{await Task.Delay(Timeout.Infinite,token);}finally{sourceActive=false;}},
        async(_,token)=>{
            int read=++reads;
            if(read==1)await Task.Delay(10,token);
            else {lateReply=Task.Delay(800);await lateReply;} // emulate a reply that ignores cancellation
            return sourceActive;
        },ct,TimeSpan.FromMilliseconds(100));
    Check.Equal(1,result.Samples.Count);Check.True(result.Samples.All(s=>s.Value&&s.ElapsedSeconds<.6));
    Check.True(!sourceActive,"Source still active after its deadline");
    Check.True(watch.Elapsed<TimeSpan.FromSeconds(2),"Late stats extended the source deadline");
    if(lateReply!=null)await lateReply;
});
await Test("acceptance.audio-window-rejects-source-failure", async () =>
{
    await Check.Throws<IOException>(async()=>await AudioMeasurementWindow.RunAsync(TimeSpan.FromSeconds(1),
        (_,_)=>Task.FromException(new IOException("source startup failed")),(_,_)=>Task.FromResult(1),ct));
    await Check.Throws<IOException>(async()=>await AudioMeasurementWindow.RunAsync(TimeSpan.FromSeconds(1),
        (started,_)=>{started();return Task.CompletedTask;},(_,_)=>Task.FromResult(1),ct));
});
await Test("host.receiver-audio-error-preserves-session", async () =>
{
    foreach(bool failStartup in new[]{false,true}) {
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
    int port=((IPEndPoint)listener.LocalEndpoint).Port;
    var reservation=new TcpListener(IPAddress.Loopback,0);reservation.Start();int apiPort=((IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();
    using var peerStop=CancellationTokenSource.CreateLinkedTokenSource(ct);int connections=0;
    Task peer=Task.Run(async()=>{
        try{
            using var client=await listener.AcceptTcpClientAsync(peerStop.Token);connections++;
            await Authentication.ServerAsync(client.GetStream(),key,peerStop.Token);
            await using var connection=new Connection(client.GetStream());
            await connection.RunAsync(async(frame,message,token)=>{
                if(message?.Id is not{} id)return;
                if(failStartup&&message.Type=="audio.start"){await connection.SendAsync(frame.Channel,Message.Create("error",new{error="injected receiver failure"},replyTo:id),token);return;}
                object body=message.Type=="hello"?new{agent="BackThread test peer",simulated=true}:message.Type=="job.query"?new{jobId="after-audio-error",state="completed",progress=1}:new{received=true};
                await connection.SendAsync(frame.Channel,Message.Create("ok",body,replyTo:id),token);
                if(!failStartup&&message.Type=="action.registry")await connection.SendAsync(LogicalChannel.Diagnostics,Message.Create("audio.error",new{error="injected receiver failure",lastOSStatus=-1}),token);
            },peerStop.Token);
        }catch(Exception)when(peerStop.IsCancellationRequested){}
    },ct);
    string keyPath=Path.Combine(root,"audio-error-host.key");await File.WriteAllTextAsync(keyPath,Convert.ToHexString(key),ct);
    var host=new HostRuntime(new HostConfig{KeyFile=keyPath,Transport="localhost",DevicePort=port,ApiPort=apiPort,StreamAudio=failStartup,FixtureAudio=true});
    using var hostStop=CancellationTokenSource.CreateLinkedTokenSource(ct);Task running=host.RunAsync(hostStop.Token);
    try{
        await Check.Eventually(()=>{var state=JsonSerializer.SerializeToElement(host.Status,Message.Json);return Task.FromResult(state.GetProperty("state").GetString()=="connected"&&state.GetProperty("audioError").GetString()=="injected receiver failure");});
        Check.Equal("completed",(await host.Worker.QueryAsync("after-audio-error",ct)).Body.GetProperty("state").GetString());
        await Task.Delay(3200,ct); // let the ordinary heartbeat use the same session too
        Check.Equal(1,connections);Check.True(!running.IsCompleted,"Host ended after audio-only failure");
    }finally{hostStop.Cancel();peerStop.Cancel();listener.Stop();await running;await peer;}
    }
});
await Test("host.local-api-authentication-and-auto-reconnect", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "host")) { DisconnectAfterAudioPackets = 40 }; server.Start();
    var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); int apiPort = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
    string keyPath = Path.Combine(root, "host.key"); await File.WriteAllTextAsync(keyPath, Convert.ToHexString(key), ct);
    var host = new HostRuntime(new HostConfig { KeyFile = keyPath, Transport = "localhost", DevicePort = server.Port, ApiPort = apiPort, StreamAudio = true, FixtureAudio = true });
    using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(ct); var running = host.RunAsync(hostStop.Token);
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}"), Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        await Check.Eventually(async () => { try { return (await http.GetAsync("/v1/status", ct)).StatusCode == HttpStatusCode.Unauthorized; } catch (HttpRequestException) { return false; } });
        http.DefaultRequestHeaders.Authorization = new("Bearer", Convert.ToHexString(key).ToLowerInvariant());
        await Check.Eventually(async () => { var response = await http.GetAsync("/v1/status", ct); return response.IsSuccessStatusCode; });
        using var denied = await http.PostAsync("/v1/actions/not-authorized", new StringContent("{}", Encoding.UTF8, "application/json"), ct); Check.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        http.DefaultRequestHeaders.Add("Origin", "https://example.com"); Check.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/v1/status", ct)).StatusCode); http.DefaultRequestHeaders.Remove("Origin");
        await Check.Eventually(() => Task.FromResult(server.AuthenticatedConnections >= 2), 10000);
        Check.True(!running.IsFaulted, "Host failed while reconnecting");
    }
    finally { hostStop.Cancel(); await running; }
});

if (Environment.GetEnvironmentVariable("BACKTHREAD_WINDOWS_PROBES") == "1") await Test("windows.actual-loopback-through-protocol", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "windows-audio")); server.Start(); await using var s = await Session.Open(server.Port, key, ct);
    using var audioStop = CancellationTokenSource.CreateLinkedTokenSource(ct); var bridge = new LoopbackBridge(); var stream = bridge.RunAsync(s.Connection, false, audioStop.Token);
    try { await LoopbackDiagnostics.ProbeAsync(ct); await Task.Delay(200, ct); }
    finally { audioStop.Cancel(); try { await stream; } catch (OperationCanceledException) { } }
    var stats = (await s.Connection.RequestAsync(LogicalChannel.Diagnostics, "audio.stats", new { }, ct)).Body;
    long bytes = stats.GetProperty("bytes").GetInt64(); Check.True(bytes > 96000, "Insufficient real captured PCM reached simulator");
    string silent = Convert.ToHexString(SHA256.HashData(new byte[checked((int)bytes)])).ToLowerInvariant();
    Check.True(stats.GetProperty("sha256").GetString() != silent, "Only silence reached the protocol receiver");
    Check.True(bridge.DeviceName != null && bridge.LastError is null, "WASAPI bridge capture reported an error");
    Console.WriteLine("WINDOWS_BRIDGE " + JsonSerializer.Serialize(new { realWindowsCapture = true, simulatedReceiver = true, physicalIPhoneTest = false, receiver = stats }, Message.Json));
});

// Explicit opt-in sustained run. Uses the real protocol and worker concurrently; no physical playback claim.
int duration = int.TryParse(Environment.GetEnvironmentVariable("BACKTHREAD_SOAK_SECONDS"), out int seconds) && seconds > 0 ? Math.Clamp(seconds, 1, 3600) : 0;
if (duration > 0) await Test("soak.concurrent-audio-worker", async () =>
{
    await using var server = new AgentServer(0, key, Path.Combine(root, "soak")); server.Start(); await using var s = await Session.Open(server.Port, key, ct);
    using var audioStop = CancellationTokenSource.CreateLinkedTokenSource(ct); var audio = new LoopbackBridge(); var stream = audio.RunAsync(s.Connection, true, audioStop.Token);
    string file = Path.Combine(root, "soak.dat"); await File.WriteAllBytesAsync(file, RandomNumberGenerator.GetBytes(4 * 1024 * 1024), ct);
    var watch = Stopwatch.StartNew(); var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; long startMemory = process.WorkingSet64; int jobs = 0; long maximumBuffer = 0;
    var lossSamples=new List<object>();long lastSilence=0,lastDrops=0,lastGaps=0,lastSenderDrops=0;
    JsonElement final;
    try {
    while (watch.Elapsed.TotalSeconds < duration)
    {
        await s.Worker.UploadAsync(file, "backup.dat", ct); string id = "soak" + jobs++; await s.Worker.SubmitAsync(new(id, "sha256", "backup.dat"), ct);
        await Check.Eventually(async () => (await s.Worker.QueryAsync(id, ct)).Body.GetProperty("state").GetString() == "completed");
        var stats = (await s.Connection.RequestAsync(LogicalChannel.Diagnostics, "audio.stats", new { }, ct)).Body; maximumBuffer = Math.Max(maximumBuffer, stats.GetProperty("bufferedBytes").GetInt64());
        long silence=stats.GetProperty("silenceBytes").GetInt64(),drops=stats.GetProperty("droppedBytes").GetInt64(),gaps=stats.GetProperty("gaps").GetInt64(),senderDrops=s.Connection.DroppedAudio;
        if(lossSamples.Count<32&&(silence!=lastSilence||drops!=lastDrops||gaps!=lastGaps||senderDrops!=lastSenderDrops))lossSamples.Add(new{elapsedSeconds=watch.Elapsed.TotalSeconds,receiver=stats.Clone(),senderDrops});
        lastSilence=silence;lastDrops=drops;lastGaps=gaps;lastSenderDrops=senderDrops;
        await Task.Delay(250, ct);
    }
    final = (await s.Connection.RequestAsync(LogicalChannel.Diagnostics, "audio.stats", new { }, ct)).Body.Clone();
    } finally { audioStop.Cancel();try{await stream;}catch(OperationCanceledException)when(audioStop.IsCancellationRequested){} }
    process.Refresh(); Console.WriteLine("METRIC " + JsonSerializer.Serialize(new { durationSeconds = watch.Elapsed.TotalSeconds, finalStatsTakenBeforeSourceStop=true, jobs, maximumBuffer, startMemory, endMemory = process.WorkingSet64, cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds, senderDropped = s.Connection.DroppedAudio,
        sender=new {queueMaximumMilliseconds=s.Connection.AudioQueueMaximumMilliseconds,writeMaximumMilliseconds=s.Connection.AudioWriteMaximumMilliseconds,slowWrites=s.Connection.AudioSlowWrites,pacingMaximumLatenessMilliseconds=audio.PacingMaximumLatenessMilliseconds,pacingLateFrames=audio.PacingLateFrames,pacingResets=audio.PacingResets,captureDroppedBytes=audio.Ring.DroppedBytes,captureSilenceBytes=audio.Ring.SilenceBytes},
        receiver = final,lossSamples }, Message.Json));
    Check.True(maximumBuffer <= 19200);
    Check.Equal(0L, final.GetProperty("droppedBytes").GetInt64());
    Check.Equal(0L, final.GetProperty("gaps").GetInt64());
    Check.Equal(0L, final.GetProperty("silenceBytes").GetInt64());
    Check.Equal(0L, s.Connection.DroppedAudio);
});

string report = Path.GetFullPath(Environment.GetEnvironmentVariable("BACKTHREAD_TEST_REPORT") ?? "artifacts/test-results.json"); Directory.CreateDirectory(Path.GetDirectoryName(report)!); await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { timestampUtc = DateTimeOffset.UtcNow, simulated = true, results, failed }, new JsonSerializerOptions { WriteIndented = true }), ct);
Console.WriteLine($"{results.Count - failed}/{results.Count} passed; report: {report}");
// Test directories contain only generated fixtures; keep them for diagnosis on failure.
if (failed == 0) Directory.Delete(root, true);
return failed == 0 ? 0 : 1;
