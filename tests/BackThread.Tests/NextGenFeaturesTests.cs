using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BackThread.Host;
using BackThread.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace BackThread.Tests;

internal static class NextGenFeaturesTests
{
    private sealed class RecordingInputSink : IRemoteInputSink
    {
        public List<MonitorDescriptor> Monitors { get; } =
        [
            new(0, "Primary-1080p", 0, 0, 1920, 1080, true),
            new(1, "Secondary-1440p", 1920, 0, 2560, 1440, false)
        ];

        public int TotalRelX, TotalRelY, LeftDowns, LeftUps, RightClicks, MiddleClicks, ScrollY, ScrollX;
        public List<(int X, int Y)> AbsoluteMoves { get; } = [];

        public IReadOnlyList<MonitorDescriptor> GetMonitors() => Monitors;
        public void MoveRelative(int dx, int dy) { TotalRelX += dx; TotalRelY += dy; }
        public void MoveAbsolute(int screenX, int screenY, MonitorDescriptor vd) => AbsoluteMoves.Add((screenX, screenY));
        public void LeftDown() => LeftDowns++;
        public void LeftUp() => LeftUps++;
        public void RightClick() => RightClicks++;
        public void MiddleClick() => MiddleClicks++;
        public void Scroll(int wheelDeltaY, int wheelDeltaX) { ScrollY += wheelDeltaY; ScrollX += wheelDeltaX; }
    }

    public static async Task RunAllAsync(Func<string, Func<Task>, Task> test, string root, byte[] key, CancellationToken ct)
    {
        await test("input.touchpad-drawing-safety-and-latency-metrics", () =>
        {
            var sink = new RecordingInputSink();
            var engine = new RemoteInputEngine(new RemoteInputConfig(), sink);

            // 1. Input MUST be disabled by default even if deviceAuthorized=true
            var blocked = engine.ProcessTouchpad(new TouchpadEvent("move", Dx: 10, Dy: 5), deviceAuthorized: true);
            Check.True(!blocked.Accepted);
            Check.Equal("touchpad-not-authorized", blocked.Reason);

            // 2. Enable touchpad only -> touchpad works, drawing pad stays separately blocked
            engine.UpdateConfig(new RemoteInputConfig { Enabled = true, AllowTouchpad = true, AllowDrawingPad = false, TouchpadSensitivity = 1.5, TouchpadAcceleration = false });
            Check.True(!engine.ProcessTouchpad(new TouchpadEvent("move", Dx: 4, Dy: 2), deviceAuthorized: false).Accepted);
            Check.True(!engine.ProcessDrawingPad(new DrawingPadEvent("stroke_start", 0.5, 0.5), deviceAuthorized: true).Accepted);

            // Precision sub-pixel accumulation + gestures
            for (int i = 0; i < 4; i++)
                Check.True(engine.ProcessTouchpad(new TouchpadEvent("move", Dx: 0.5, Dy: 0.5), deviceAuthorized: true).Accepted);
            Check.Equal(3, sink.TotalRelX); // 4 * 0.5 * 1.5 = 3.0
            Check.True(engine.ProcessTouchpad(new TouchpadEvent("tap"), deviceAuthorized: true).Accepted);
            Check.True(engine.ProcessTouchpad(new TouchpadEvent("double_tap"), deviceAuthorized: true).Accepted);
            Check.True(engine.ProcessTouchpad(new TouchpadEvent("right_click"), deviceAuthorized: true).Accepted);
            Check.True(engine.ProcessTouchpad(new TouchpadEvent("scroll", ScrollY: 2.0, ScrollX: -1.0), deviceAuthorized: true).Accepted);
            Check.Equal(1, sink.RightClicks);
            Check.Equal(240, sink.ScrollY);
            Check.Equal(-120, sink.ScrollX);

            // Drag + emergency disconnect MUST immediately release held button & lock input
            Check.True(engine.ProcessTouchpad(new TouchpadEvent("drag_start"), deviceAuthorized: true).Accepted);
            Check.True(engine.GetMetrics().LeftButtonHeld);
            engine.EmergencyDisconnect();
            var afterStop = engine.GetMetrics();
            Check.True(afterStop.EmergencyStopped);
            Check.True(!afterStop.Enabled);
            Check.True(!afterStop.LeftButtonHeld);
            Check.Equal(sink.LeftDowns, sink.LeftUps);
            Check.True(!engine.ProcessTouchpad(new TouchpadEvent("tap"), deviceAuthorized: true).Accepted);

            // 3. Re-enable with Drawing Pad on secondary monitor, aspect fit, calibration & palm rejection
            engine.UpdateConfig(new RemoteInputConfig
            {
                Enabled = true,
                AllowTouchpad = true,
                AllowDrawingPad = true,
                TargetMonitorIndex = 1,
                DrawingAspectMode = "fit",
                CalMinX = 0.1,
                CalMaxX = 0.9,
                CalMinY = 0.1,
                CalMaxY = 0.9,
                PalmRejection = true,
                PalmContactRadiusThreshold = 30.0
            }, clearEmergencyStop: true);

            // Palm contact rejected
            var palm = engine.ProcessDrawingPad(new DrawingPadEvent("stroke_start", 0.5, 0.5, ContactRadius: 45.0, HasRealPressure: false), deviceAuthorized: true);
            Check.True(!palm.Accepted && palm.PalmRejected);

            // Valid stroke mapped into Monitor 1 (Left=1920, Width=2560)
            var startRes = engine.ProcessDrawingPad(new DrawingPadEvent("stroke_start", 0.5, 0.5, DeviceAspect: 16.0 / 9.0, HasRealPressure: false), deviceAuthorized: true);
            Check.True(startRes.Accepted);
            Check.True(startRes.MappedX is >= 1920 and < 4480);

            // 500-event latency & jitter micro-benchmark
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 500; i++)
            {
                double nx = 0.2 + (i / 500.0) * 0.6;
                double ny = 0.5 + Math.Sin(i * 0.1) * 0.01;
                engine.ProcessDrawingPad(new DrawingPadEvent("stroke_move", nx, ny, DeviceAspect: 16.0 / 9.0), deviceAuthorized: true);
            }
            sw.Stop();
            engine.ProcessDrawingPad(new DrawingPadEvent("stroke_end", 0, 0), deviceAuthorized: true);
            var metrics = engine.GetMetrics();
            Check.True(metrics.DrawingEvents >= 502);
            Check.Equal(0L, metrics.DroppedEvents);
            Check.True(metrics.PalmRejectedEvents >= 1);
            Check.True(!metrics.LeftButtonHeld);
            Console.WriteLine($"METRIC remote-input-500events elapsedMs={sw.Elapsed.TotalMilliseconds:F2} avgLatencyMs={metrics.AvgLatencyMs:F3} p95LatencyMs={metrics.P95LatencyMs:F3} avgJitterPx={metrics.AvgJitterPx:F3}");
            return Task.CompletedTask;
        });

        await test("display.profile-catalog-lcd-oled-and-overrides", () =>
        {
            var ip6 = DisplayProfileCatalog.Resolve("iPhone7,2");
            Check.Equal(DisplayPanelKind.Lcd, ip6.PanelKind);
            Check.True(!ip6.RecommendOledGuard);
            Check.Equal(750, ip6.NativeWidth);

            var ip15Pro = DisplayProfileCatalog.Resolve("iPhone16,1");
            Check.Equal(DisplayPanelKind.Oled, ip15Pro.PanelKind);
            Check.True(ip15Pro.RecommendOledGuard);

            // Unknown device must NOT assume OLED
            var unknown = DisplayProfileCatalog.Resolve("OldAndroidTablet-X", 1024, 768, 1.0);
            Check.Equal(DisplayPanelKind.Unknown, unknown.PanelKind);
            Check.True(!unknown.RecommendOledGuard);

            // Aftermarket LCD screen override on OLED model disables OLED guard
            var replaced = DisplayProfileCatalog.Resolve("iPhone16,1", userOverride: new DisplayUserOverride(PanelKindOverride: DisplayPanelKind.Lcd));
            Check.Equal(DisplayPanelKind.Lcd, replaced.PanelKind);
            Check.True(replaced.IsOverride);
            Check.True(!replaced.RecommendOledGuard);
            return Task.CompletedTask;
        });

        await test("legacyweb.tls-pairing-ws-input-and-emergency-stop", async () =>
        {
            var sink = new RecordingInputSink();
            string keyPath = Path.Combine(root, "legacyweb.key");
            await File.WriteAllTextAsync(keyPath, Convert.ToHexString(key), ct);
            var cert = LanCertificateStore.LoadOrCreate(Path.Combine(root, "legacyweb.pfx"), "WebTestPC");
            string fp = LanCertificateStore.GetFingerprint(cert);

            var cfg = new HostConfig
            {
                KeyFile = keyPath,
                Transport = "localhost",
                EnableHttpApi = false,
                StreamAudio = false,
                RemoteInput = new RemoteInputConfig { Enabled = true, AllowTouchpad = true, AllowDrawingPad = true }
            };
            using var host = new HostRuntime(cfg, inputSink: sink);
            var web = new LegacyWebServer(host, cert, port: 0, bindAddress: IPAddress.Loopback);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var run = web.RunAsync(stop.Token);
            try
            {
                int port = await web.BoundPortAsync.WaitAsync(TimeSpan.FromSeconds(8), ct);
                using var handler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    SslOptions =
                    {
                        RemoteCertificateValidationCallback = (_, c, _, _) =>
                        {
                            if (c is null) return false;
                            using var x = new X509Certificate2(c);
                            return string.Equals(x.GetCertHashString(HashAlgorithmName.SHA256), fp, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                };
                using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };

                // 1. Minimal HTML + ES5 JS bundle check (< 15 KB total)
                string html = await http.GetStringAsync("/", ct);
                string js = await http.GetStringAsync("/app.js", ct);
                Check.True(html.Contains("BackThread Companion", StringComparison.Ordinal));
                Check.True(js.Length + html.Length < 15 * 1024, $"Expected compact legacy web bundle <15KB, got {js.Length + html.Length}");
                Check.True(!js.Contains("=>", StringComparison.Ordinal), "Legacy app.js must remain ES5 (no arrow functions)");
                Check.True(!js.Contains("const ", StringComparison.Ordinal) && !js.Contains("let ", StringComparison.Ordinal), "Legacy app.js must remain ES5 (no const/let)");

                // 2. Unauthenticated status MUST return 401
                using var unauth = await http.GetAsync("/web/api/status", ct);
                Check.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

                // 3. Pair via single-use code over TLS
                var (code, _, _) = host.Pairing.Start();
                using var claimResp = await http.PostAsJsonAsync("/web/api/pair/claim", new { code, deviceId = "ipad-ios9", deviceName = "iPad Air iOS 9" }, ct);
                claimResp.EnsureSuccessStatusCode();
                var claimJson = await claimResp.Content.ReadFromJsonAsync<JsonElement>(Message.Json, ct);
                string token = claimJson.GetProperty("token").GetString()!;
                Check.Equal(64, token.Length);

                // 4. Input rejected until PC grants per-device touchpad permission
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                http.DefaultRequestHeaders.Add("X-BackThread-Device", "ipad-ios9");
                using var deniedInput = await http.PostAsJsonAsync("/web/api/input", new { mode = "touchpad", kind = "move", dx = 5, dy = 5 }, ct);
                Check.Equal(HttpStatusCode.Forbidden, deniedInput.StatusCode);

                // 5. Grant touchpad + drawing pad permission on PC, then stream over TLS WebSocket
                host.SetDeviceInputPermissions("ipad-ios9", allowTouchpad: true, allowDrawingPad: true);
                using var ws = new ClientWebSocket();
                ws.Options.RemoteCertificateValidationCallback = (_, c, _, _) =>
                {
                    if (c is null) return false;
                    using var x = new X509Certificate2(c);
                    return string.Equals(x.GetCertHashString(HashAlgorithmName.SHA256), fp, StringComparison.OrdinalIgnoreCase);
                };
                await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/web/ws"), ct);
                await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "auth", deviceId = "ipad-ios9", token })), WebSocketMessageType.Text, true, ct);
                var buf = new byte[1024];
                var authRecv = await ws.ReceiveAsync(buf, ct);
                string authText = Encoding.UTF8.GetString(buf, 0, authRecv.Count);
                Check.True(authText.Contains("auth.ok", StringComparison.Ordinal));

                var wsSw = Stopwatch.StartNew();
                for (int i = 0; i < 50; i++)
                {
                    long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    byte[] frame = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "input", mode = "touchpad", kind = "move", dx = 2, dy = 1, ts }));
                    await ws.SendAsync(frame, WebSocketMessageType.Text, true, ct);
                }
                await ws.SendAsync(Encoding.UTF8.GetBytes("""{"type":"ping"}"""), WebSocketMessageType.Text, true, ct);
                var pong = await ws.ReceiveAsync(buf, ct);
                wsSw.Stop();
                Check.True(Encoding.UTF8.GetString(buf, 0, pong.Count).Contains("pong", StringComparison.Ordinal));
                Check.True(sink.TotalRelX > 0);
                Console.WriteLine($"METRIC legacyweb-ws-50moves elapsedMs={wsSw.Elapsed.TotalMilliseconds:F2} avgPerEventMs={wsSw.Elapsed.TotalMilliseconds / 50.0:F3}");

                // 6. Emergency stop immediately blocks subsequent input
                using var stopResp = await http.PostAsync("/web/api/input/emergency-stop", null, ct);
                stopResp.EnsureSuccessStatusCode();
                Check.True(host.Input.GetMetrics().EmergencyStopped);
            }
            finally
            {
                stop.Cancel();
                web.Stop();
                try { await run; } catch (OperationCanceledException) { }
            }
        });

        await test("phonebox.adapter-qr-pinning-claim-and-allowlist", async () =>
        {
            var cert = LanCertificateStore.LoadOrCreate(Path.Combine(root, "phonebox-mock.pfx"), "PhoneBoxMock");
            string fp = LanCertificateStore.GetFingerprint(cert);
            const string oneTimeCode = "pb-one-time-1234";
            const string expectedToken = "phonebox-paired-token-xyz";
            bool claimed = false;

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Listen(IPAddress.Loopback, 0, l =>
                {
                    l.Protocols = HttpProtocols.Http1;
                    l.UseHttps(h => { h.ServerCertificate = cert; h.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13; });
                });
            });
            await using var mockPb = builder.Build();
            mockPb.MapPost("/api/pair/claim", async (HttpContext c) =>
            {
                if (c.Request.Headers.ContainsKey("Origin")) return Results.Unauthorized();
                using var doc = await JsonDocument.ParseAsync(c.Request.Body, cancellationToken: c.RequestAborted);
                string? code = doc.RootElement.TryGetProperty("code", out var ce) ? ce.GetString() : null;
                if (claimed || code != oneTimeCode) return Results.Unauthorized();
                claimed = true;
                return Results.Ok(new { token = expectedToken });
            });
            mockPb.MapGet("/api/status", (HttpContext c) =>
            {
                if (c.Request.Headers.Authorization.ToString() != "Bearer " + expectedToken) return Results.Unauthorized();
                return Results.Ok(new { ok = true, host = "PhoneBoxMock", version = "0.9.21" });
            });
            await mockPb.StartAsync(ct);
            int port = new Uri(mockPb.Urls.First()).Port;
            try
            {
                string qr = $"https://ersinkoc.github.io/phonebox/pair/#v=1&pc=OfficePC&fp={fp}&port={port}&h=127.0.0.1&c={oneTimeCode}";
                var proposal = PhoneBoxAdapter.ParsePairingUri(qr);
                Check.True(proposal is not null);
                Check.Equal("OfficePC", proposal!.PcName);
                Check.Equal(port, proposal.Port);

                // Wrong TLS fingerprint MUST fail
                using (var badPin = new PhoneBoxAdapter("127.0.0.1", port, new string('a', 64)))
                {
                    bool failed = false;
                    try { await badPin.PairAsync(proposal, ct); }
                    catch (Exception ex) when (ex is HttpRequestException or AuthenticationException) { failed = true; }
                    Check.True(failed, "PhoneBoxAdapter must reject mismatched certificate fingerprint");
                }

                // Valid pin + claim + read-only status + allowlist check
                using var adapter = new PhoneBoxAdapter("127.0.0.1", port, proposal.FingerprintHex);
                var session = await adapter.PairAsync(proposal, ct);
                Check.Equal(expectedToken, session.BearerToken);
                Check.True(!session.SupportsBackThreadPcm && !session.SupportsBackThreadWorkers);

                var status = await adapter.GetReadOnlyAsync("/api/status", ct);
                Check.Equal("PhoneBoxMock", status.GetProperty("host").GetString());

                // Disallowed path (/api/settings) MUST be blocked client-side before sending
                bool disallowedBlocked = false;
                try { await adapter.GetReadOnlyAsync("/api/settings", ct); }
                catch (InvalidOperationException) { disallowedBlocked = true; }
                Check.True(disallowedBlocked, "Disallowed PhoneBox LAN paths must be rejected");
            }
            finally
            {
                await mockPb.StopAsync(CancellationToken.None);
            }
        });
    }
}
