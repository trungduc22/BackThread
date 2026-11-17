using BackThread.Simulator;
using BackThread.Transport;

string? Value(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
if (Value("--key-file") is not { } keyFile) { Console.Error.WriteLine("Usage: BackThread.Simulator --key-file <32-byte hex key> [--port 23451] [--state state/simulator] [--delay-ms 0] [--disconnect-after 0]"); return 2; }
using var stop = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
await using var server = new AgentServer(int.Parse(Value("--port") ?? "23451"), Authentication.LoadKey(keyFile), Value("--state") ?? "state/simulator") { DelayMs = int.Parse(Value("--delay-ms") ?? "0"), DisconnectAfterAudioPackets = int.Parse(Value("--disconnect-after") ?? "0") };
server.Log += Console.WriteLine; server.Start(); Console.WriteLine($"SIMULATOR: listening only on 127.0.0.1:{server.Port}; no iPhone playback implied");
try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
return 0;
