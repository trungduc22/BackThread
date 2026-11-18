using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using BackThread.Audio;
using BackThread.Transport;
using BackThread.Worker;
using BackThread.DeviceTests;

// This runner uses production transport/audio/worker code. Simulator mode is explicit and never counts as phone acceptance.
string? Value(string option) { int i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool simulated = args.Contains("--simulator");
string output = Path.GetFullPath(Value("--output") ?? "artifacts/device-acceptance.json");
var checks = new List<object>(); var measurements = new Dictionary<string, object?>();
bool connected = false, passed = false; string? error = null;
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(6));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
CancellationToken ct = stop.Token;
void Check(string name, bool condition) { checks.Add(new { name, passed = condition }); if (!condition) throw new InvalidDataException(name); Console.WriteLine("PASS " + name); }
try
{
    byte[] key = Authentication.LoadKey(Value("--key-file") ?? throw new ArgumentException("--key-file is required"));
    int port = int.Parse(Value("--port") ?? (simulated ? "23461" : "23451"));
    if (port is < 1024 or > 65535) throw new ArgumentException("Invalid port");
    int seconds = int.Parse(Value("--seconds") ?? "3");
    if (seconds is < 1 or > 180) throw new ArgumentException("Seconds must be 1..180");
    bool fixture = args.Contains("--fixture"), loopback = args.Contains("--loopback");
    float gain=float.Parse(Value("--gain")??"0.08",System.Globalization.CultureInfo.InvariantCulture);
    if(!float.IsFinite(gain)||gain<0||gain>0.3f)throw new ArgumentException("Queue gain must be 0..0.3");
    if (fixture && loopback || fixture && seconds > 5) throw new ArgumentException("Choose one audio source; generated tone is limited to five seconds");
    measurements["audioSource"] = fixture ? "quiet fixture" : loopback ? "WASAPI loopback" : "off";
    measurements["requestedQueueGain"] = gain;
    async Task<Stream> Connect()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        if (simulated)
        {
            var client = new TcpClient { NoDelay = true };
            try { await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); return client.GetStream(); }
            catch { client.Dispose(); throw; }
        }
        var mux = new UsbMux(); var devices = await mux.ListAsync(timeout.Token);
        measurements["physicalUsbDeviceCount"] = devices.Count;
        if (devices.Count != 1) throw new IOException("Exactly one physical USB device is required");
        return await mux.ConnectAsync(devices[0].Id, port, timeout.Token);
    }
    using (var first = await Connect()) { await Authentication.ClientAsync(first, key, ct); connected = true; }
    Check("authenticated session", connected);
    bool rejected = false;
    using (var wrong = await Connect())
    {
        byte[] wrongKey = (byte[])key.Clone(); wrongKey[0] ^= 1;
        try { await Authentication.ClientAsync(wrong, wrongKey, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
    }
    Check("wrong-key rejection", rejected);
    using (var malformed = await Connect())
    {
        await Authentication.ClientAsync(malformed, key, ct);
        var header = Wire.Header(new(LogicalChannel.Control, 1, 0, [])); header[8] = 2;
        await malformed.WriteAsync(header, ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(3));
        byte[] one = new byte[1];
        bool closed;
        try { closed = await malformed.ReadAsync(one, limit.Token) == 0; } catch (IOException) { closed = true; }
        Check("unsupported frame version closes session", closed);
    }
    {
    using var stream = await Connect(); await Authentication.ClientAsync(stream, key, ct);
    await using var connection = new Connection(stream);
    var worker = new RemoteWorker(connection);
    using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
    Task reader = connection.RunAsync((frame, message, _) => { worker.Receive(frame, message); return ValueTask.CompletedTask; }, session.Token);
    try
    {
        var hello = (await connection.RequestAsync(LogicalChannel.Control, "hello", new { host = "BackThread.DeviceTests", protocol = 1 }, ct)).Body;
        Check("peer simulation classification matches requested transport", hello.GetProperty("simulated").GetBoolean() == simulated);
        measurements["hello"] = hello.Clone();
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(250, ct);
            var pong = await connection.RequestAsync(LogicalChannel.Control, "ping", new { }, ct);
            Check("heartbeat " + (i + 1), pong.Type == "pong");
        }
        await connection.RequestAsync(LogicalChannel.Media, "media.state", new { session = (object?)null }, ct);
        await connection.RequestAsync(LogicalChannel.Control, "action.registry", new { actions = Array.Empty<object>() }, ct);
        string eventId = Guid.NewGuid().ToString("N");
        var eventBody = new { id = eventId, source = "BackThread acceptance", title = "Integration check", body = "BackThread-owned test event", priority = 0, expiresUtc = DateTimeOffset.UtcNow.AddSeconds(30), sensitive = false };
        var accepted = await connection.RequestAsync(LogicalChannel.Notifications, "event.publish", eventBody, ct);
        Check("agent accepts unexpired event", accepted.Body.GetProperty("accepted").GetBoolean());
        var duplicate = await connection.RequestAsync(LogicalChannel.Notifications, "event.publish", eventBody, ct);
        Check("agent rejects duplicate event", !duplicate.Body.GetProperty("accepted").GetBoolean());
        // Accepted messages establish transport/agent state, not visible UIKit rendering or successful Windows media control.
        async Task<JobState> Job(string kind, string name, CancellationToken? jobToken = null)
        {
            CancellationToken jobCt=jobToken??ct;
            string id = Guid.NewGuid().ToString("N");
            await worker.SubmitAsync(new(id, kind, name), jobCt);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                var state = (await worker.QueryAsync(id, jobCt)).Body.Deserialize<JobState>(Message.Json)!;
                if (state.State is "completed" or "failed" or "cancelled" or "interrupted") { Check("job completed: " + kind + "/" + name, state.State == "completed"); return state; }
                await Task.Delay(100, jobCt);
            }
            throw new TimeoutException("Agent job deadline exceeded");
        }
        measurements["memoryBefore"] = (await Job("diagnostic", "memory")).Result;
        async Task Audio()
        {
            if (!fixture && !loopback) return;
            // Queue gain is deliberately small; never raise the phone's system output volume.
            if (!simulated) await connection.RequestAsync(LogicalChannel.Control, "audio.volume", new { value = gain }, ct);
            var bridge = new LoopbackBridge();
            var memorySamples = new List<object>();
            double nextMemory=5;
            try
            {
                var window=await AudioMeasurementWindow.RunAsync(TimeSpan.FromSeconds(seconds),
                    (started,token)=>bridge.RunAsync(connection,fixture,token,started),
                    async (elapsed,token)=>{
                        var stats=(await connection.RequestAsync(LogicalChannel.Diagnostics,"audio.stats",new{},token)).Body.Clone();
                        if(elapsed.TotalSeconds>=nextMemory&&seconds-elapsed.TotalSeconds>1){var memory=(await Job("diagnostic","memory",token)).Result;token.ThrowIfCancellationRequested();memorySamples.Add(new{elapsedSeconds=elapsed.TotalSeconds,memory});nextMemory=elapsed.TotalSeconds+5;}
                        return stats;
                    },ct);
                var samples=window.Samples.Select(s=>s.Value).ToList();
                measurements["audioSampleTimesSeconds"]=window.Samples.Select(s=>s.ElapsedSeconds).ToArray();
                measurements["audioWindowSeconds"]=window.RequestedSeconds;
                measurements["audioMeasurementScope"]="Only replies completed during active PCM pacing; shutdown tail excluded";
                measurements["audio"] = samples;
                measurements["memoryDuringAudio"] = memorySamples;
                measurements["senderFrames"] = bridge.FramesSent; measurements["senderDroppedFrames"] = connection.DroppedAudio;
                measurements["captureDevice"] = bridge.DeviceName; measurements["captureError"] = bridge.LastError;
                measurements["senderTiming"]=new
                {
                    queueMaximumMilliseconds=connection.AudioQueueMaximumMilliseconds,
                    writeMaximumMilliseconds=connection.AudioWriteMaximumMilliseconds,slowWrites=connection.AudioSlowWrites,
                    pacingMaximumLatenessMilliseconds=bridge.PacingMaximumLatenessMilliseconds,
                    pacingLateFrames=bridge.PacingLateFrames,pacingResets=bridge.PacingResets,
                    captureDroppedBytes=bridge.Ring.DroppedBytes,captureSilenceBytes=bridge.Ring.SilenceBytes
                };
                Check("PCM frames sent", bridge.FramesSent > 0);
                Check("statistics sampled while source was active",samples.Count>0);
                Check("PCM bytes received", samples[^1].GetProperty("bytes").GetInt64() > 0);
                if (!simulated)
                {
                    Check("AudioQueue callbacks executed", samples[^1].GetProperty("callbacks").GetInt64() > 0);
                    Check("AudioQueue status is noErr", samples[^1].GetProperty("lastOSStatus").GetInt32() == 0);
                    Check("AudioQueue running and primed", samples[^1].GetProperty("running").GetBoolean() && samples[^1].GetProperty("primed").GetBoolean());
                    Check("built-in speaker route", samples[^1].GetProperty("routeTypes").EnumerateArray().Any(p => p.GetString() == "Speaker"));
                    Check("AudioQueue reports hardware running", samples[^1].GetProperty("queueRunning").GetInt32() != 0);
                    Check("AudioQueue diagnostic getters succeed", samples[^1].GetProperty("queueRunningStatus").GetInt32()==0&&samples[^1].GetProperty("queueGainStatus").GetInt32()==0);
                    if (fixture)
                    {
                        Check("fixture input has nonzero PCM", samples[^1].GetProperty("inputPeak").GetInt32() > 0);
                        Check("fixture rendered buffers have nonzero PCM", samples[^1].GetProperty("renderPeak").GetInt32() > 0);
                    }
                    if(args.Contains("--require-nonzero"))Check("known WASAPI signal rendered",samples[^1].GetProperty("inputPeak").GetInt32()>0&&samples[^1].GetProperty("renderPeak").GetInt32()>0);
                }
                measurements["zeroLossInSampledWindow"]=connection.DroppedAudio==0&&samples.All(s=>s.GetProperty("gaps").GetInt64()==0&&s.GetProperty("droppedBytes").GetInt64()==0&&s.GetProperty("silenceBytes").GetInt64()==0);
            }
            finally
            {
                await connection.RequestAsync(LogicalChannel.Control, "audio.stop", new { }, ct);
            }
        }
        async Task Files()
        {
            if (!args.Contains("--worker")) return;
            string name = "acceptance-" + Guid.NewGuid().ToString("N") + ".dat";
            string directory = Path.GetFullPath("artifacts/device-fixtures"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            byte[] bytes = new byte[1024 * 1024]; RandomNumberGenerator.Fill(bytes); await File.WriteAllBytesAsync(path, bytes, ct);
            string expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            try
            {
                measurements["agentTestFile"] = name;
                await worker.UploadAsync(path, name, ct);
                var hashed = await Job("sha256", name);
                var value = JsonSerializer.SerializeToElement(hashed.Result, Message.Json);
                Check("whole file SHA-256", value.GetProperty("sha256").GetString() == expected);
                byte[] received = await worker.ReadArtifactAsync(name, 32768, 32768, ct);
                Check("artifact range contents", received.AsSpan().SequenceEqual(bytes.AsSpan(32768, 32768)));
                measurements["agentTestFile"] = name; // No file-delete protocol exists; remove only this exact app-owned fixture when cleaning up.
            }
            finally { File.Delete(path); }
        }
        await Task.WhenAll(Audio(), Files());
        measurements["memoryAfter"] = (await Job("diagnostic", "memory")).Result;
        Check("control still responds after workload", (await connection.RequestAsync(LogicalChannel.Control, "ping", new { }, ct)).Type == "pong");
    }
    finally
    {
        session.Cancel();
        try { await reader; } catch (Exception) when (session.IsCancellationRequested) { }
    }
    }
    using var reconnect = await Connect(); await Authentication.ClientAsync(reconnect, key, ct);
    Check("new socket authenticates after prior session closes", true);
    passed = true;
}
catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; Console.Error.WriteLine(error); }
finally
{
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { timestampUtc = DateTimeOffset.UtcNow, simulated, transport = simulated ? "localhost" : "usb", connected, passed, speakerAudibilityVerified = false, listeningCheck = "not established by this automated runner; record the human result separately", UIKitRenderingVerified = false, physicalUnplugTested = false, checks, measurements, error }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Report: " + output);
}
return passed ? 0 : connected ? 1 : 2;
