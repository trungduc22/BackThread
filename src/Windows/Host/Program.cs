using System.Text.Json;
using BackThread.Host;
using BackThread.Audio;
using BackThread.Media;
using BackThread.Transport;

string? Value(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
try
{
    if (Value("--init-key") is { } path) { Authentication.CreateKey(path); Console.WriteLine("Created a session key. Provision the same file to the agent through an authorized local method; keep it private."); return 0; }
    using var stop = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    if (args.Contains("--diagnose"))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var mux = new UsbMux(); var devices = await mux.ListAsync(timeout.Token);
        foreach (var device in devices)
        {
            bool paired = await mux.HasPairRecordAsync(device.Serial, timeout.Token); string port;
            try { using var stream = await mux.ConnectAsync(device.Id, 23451, timeout.Token); port = "open (authentication not tested)"; } catch (IOException ex) { port = ex.Message; }
            Console.WriteLine(JsonSerializer.Serialize(new { deviceId = device.Id, connection = device.ConnectionType, localPairRecord = paired, trustValidated = false, backThreadPort = port }, Message.Json));
        }
        if (devices.Count == 0) Console.WriteLine("No USB devices detected"); return 0;
    }
    if (args.Contains("--media-probe")) { var media = new MediaSessions(); await media.InitializeAsync(); var s = await media.SnapshotAsync(); Console.WriteLine(JsonSerializer.Serialize(new { apiAvailable = true, activeSession = s != null, source = s?.Source, playbackState = s?.State, hasArtwork = s?.ArtworkBase64 != null }, Message.Json)); return 0; }
    if (args.Contains("--audio-probe")) { using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15)); Console.WriteLine(JsonSerializer.Serialize(await LoopbackDiagnostics.ProbeAsync(limit.Token), Message.Json)); return 0; }
    if (args.Contains("--media-self-test")) { using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15)); Console.WriteLine(JsonSerializer.Serialize(await MediaDiagnostics.ProbeAsync(limit.Token), Message.Json)); return 0; }
    var runtime = new HostRuntime(HostConfig.Load(Value("--config") ?? "config/local.json")); runtime.Log += s => Console.WriteLine(DateTimeOffset.Now.ToString("O") + " " + s);
    await runtime.RunAsync(stop.Token); return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
