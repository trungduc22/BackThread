using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackThread.Audio;
using BackThread.Host;
using BackThread.Simulator;
using BackThread.Transport;
using BackThread.Worker;

namespace BackThread.Tests;

internal static class UpgradeFeaturesTests
{
    public static async Task RunAllAsync(
        Func<string, Func<Task>, Task> test,
        string root,
        byte[] key,
        CancellationToken ct)
    {
        await test("lan.cert-pinning-and-qr-pairing-claim", async () =>
        {
            string certPath = Path.Combine(root, "lan-test", "host.pfx");
            using var cert = LanCertificateStore.LoadOrCreate(certPath, "TestPC");
            using var reloaded = LanCertificateStore.LoadOrCreate(certPath, "TestPC");
            string fp = LanCertificateStore.GetFingerprint(cert);
            Check.Equal(64, fp.Length);
            Check.Equal(fp, LanCertificateStore.GetFingerprint(reloaded));
            string checkCode = LanCertificateStore.GetHumanCheckCode(fp);
            Check.Equal(9, checkCode.Length);
            Check.Equal(' ', checkCode[4]);

            var pairing = new QrPairingService();
            var devices = new DeviceRegistry(Path.Combine(root, "lan-test", "devices.json"));
            await using var server = new LanTlsServer(cert, key, pairing, port: 0, IPAddress.Loopback, "TestPC", devices);
            server.Start();

            var snap = pairing.CreateSession("TestPC", fp, server.Port, ["192.168.1.50", "100.77.1.2"]);
            Check.True(snap.PairingUri.StartsWith("backthread://pair#v=1&", StringComparison.Ordinal), "Expected backthread://pair fragment URI");
            Check.True(!snap.PairingUri.Contains(Convert.ToHexString(key), StringComparison.OrdinalIgnoreCase), "Session key MUST NEVER appear in QR URI");
            Check.True(snap.SvgMarkup.Contains("<svg", StringComparison.OrdinalIgnoreCase), "QR SVG markup missing");
            Check.True(QrPairingService.RenderPng(snap.PairingUri).Length > 100, "QR PNG bytes empty");

            // 1. Wrong fingerprint must fail TLS pinning verification
            bool pinRejected = false;
            try
            {
                var (badTcp, badSsl) = await LanTlsServer.ConnectPinnedClientAsync("127.0.0.1", server.Port, new string('0', 64), ct);
                badSsl.Dispose();
                badTcp.Dispose();
            }
            catch (AuthenticationException) { pinRejected = true; }
            Check.True(pinRejected, "Mismatched TLS SHA-256 pin must be rejected");

            // 2. Valid pin + single-use pair.claim returns session key once
            {
                var (tcp, ssl) = await LanTlsServer.ConnectPinnedClientAsync("127.0.0.1", server.Port, fp, ct);
                using (tcp) using (ssl)
                {
                    await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                        Message.Create("pair.claim", new { code = snap.OneTimeCode, deviceId = "iphone-15-pro", deviceName = "Duc iPhone" }, id: "p1").Encode()), ct);
                    var replyFrame = await Wire.ReadAsync(ssl, ct);
                    var reply = Message.Decode(replyFrame.Payload);
                    Check.Equal("pair.ok", reply.Type);
                    Check.Equal(Convert.ToHexString(key).ToLowerInvariant(), reply.Body.GetProperty("sessionKey").GetString());
                    Check.Equal(checkCode, reply.Body.GetProperty("checkCode").GetString());
                }
            }

            // 3. Replaying the same one-time code must fail
            {
                var (tcp, ssl) = await LanTlsServer.ConnectPinnedClientAsync("127.0.0.1", server.Port, fp, ct);
                using (tcp) using (ssl)
                {
                    await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                        Message.Create("pair.claim", new { code = snap.OneTimeCode, deviceId = "attacker", deviceName = "Bad" }, id: "p2").Encode()), ct);
                    var reply = Message.Decode((await Wire.ReadAsync(ssl, ct)).Payload);
                    Check.Equal("error", reply.Type);
                }
            }

            // 4. 10 bad attempts lock out active pairing code
            var (lockCode, _, _) = pairing.Start(1_000_000);
            for (int i = 0; i < QrPairingService.MaxFailures; i++)
                Check.True(!pairing.Claim("wrong-code-" + i, 1_001_000));
            Check.True(!pairing.Claim(lockCode, 1_001_000), "Pairing code should be invalidated after MaxFailures");

            // 5. Revoked device rejected on auth.challenge
            Check.True(devices.Revoke("iphone-15-pro"));
            {
                var (tcp, ssl) = await LanTlsServer.ConnectPinnedClientAsync("127.0.0.1", server.Port, fp, ct);
                using (tcp) using (ssl)
                {
                    byte[] nonce = RandomNumberGenerator.GetBytes(32);
                    await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                        Message.Create("auth.challenge", new { serverNonce = Convert.ToBase64String(nonce), deviceId = "iphone-15-pro", deviceName = "Duc iPhone" }, id: "a1").Encode()), ct);
                    var reply = Message.Decode((await Wire.ReadAsync(ssl, ct)).Payload);
                    Check.Equal("error", reply.Type);
                }
            }
        });

        await test("lan.nic-filtering-and-mdns-packet", async () =>
        {
            Check.True(LanNetworkDiscovery.ShouldSkipAdapter("vEthernet (WSL)", hasIpv4Gateway: false), "WSL vEthernet without gateway should be skipped");
            Check.True(LanNetworkDiscovery.ShouldSkipAdapter("VMware Virtual Ethernet Adapter", hasIpv4Gateway: false), "VMware adapter without gateway should be skipped");
            Check.True(!LanNetworkDiscovery.ShouldSkipAdapter("vEthernet (External Switch)", hasIpv4Gateway: true), "Hyper-V External vSwitch WITH gateway must be preserved");
            Check.True(!LanNetworkDiscovery.ShouldSkipAdapter("Intel(R) Wi-Fi 6E AX211", hasIpv4Gateway: true), "Physical Wi-Fi adapter must be preserved");

            Check.True(LanNetworkDiscovery.IsPrivateLanIpv4(IPAddress.Parse("192.168.1.42")));
            Check.True(LanNetworkDiscovery.IsPrivateLanIpv4(IPAddress.Parse("10.0.0.5")));
            Check.True(LanNetworkDiscovery.IsPrivateLanIpv4(IPAddress.Parse("172.20.1.9")));
            Check.True(!LanNetworkDiscovery.IsPrivateLanIpv4(IPAddress.Parse("8.8.8.8")));

            Check.True(LanNetworkDiscovery.IsTailscaleAddress(IPAddress.Parse("100.64.0.1")));
            Check.True(LanNetworkDiscovery.IsTailscaleAddress(IPAddress.Parse("100.127.255.254")));
            Check.True(!LanNetworkDiscovery.IsTailscaleAddress(IPAddress.Parse("100.128.0.1")));

            Check.Equal(TransportRouteKind.Tailscale, LanNetworkDiscovery.ClassifyRemoteAddress(IPAddress.Parse("100.88.12.3")));
            Check.Equal(TransportRouteKind.WifiLan, LanNetworkDiscovery.ClassifyRemoteAddress(IPAddress.Parse("192.168.1.99")));
            Check.Equal(TransportRouteKind.Localhost, LanNetworkDiscovery.ClassifyRemoteAddress(IPAddress.Loopback));

            byte[] mdnsPkt = LanNetworkDiscovery.BuildMdnsAnnouncementPacket("MyPC", "MyPC", 23453, "0123456789abcdef", "0123456789abcdef", IPAddress.Parse("192.168.1.25"));
            Check.True(mdnsPkt.Length > 60, "mDNS announcement packet too short");
            string ascii = Encoding.ASCII.GetString(mdnsPkt);
            Check.True(ascii.Contains("_backthread", StringComparison.Ordinal), "mDNS packet missing _backthread service");
            Check.True(ascii.Contains("pc=0123456789abcdef", StringComparison.Ordinal), "mDNS TXT missing pcId");
            await Task.CompletedTask;
        });

        await test("audio.adaptive-wifi-20ms-and-per-app-mixer", async () =>
        {
            var wifiProfile = AdaptiveAudioProfile.ForRoute(TransportRouteKind.WifiLan);
            Check.Equal(20, wifiProfile.PacketMs);
            Check.Equal(3840, wifiProfile.BytesPerPacket);
            Check.Equal(200, wifiProfile.RingCapacityMs);

            var usbProfile = AdaptiveAudioProfile.ForRoute(TransportRouteKind.Usb);
            Check.Equal(10, usbProfile.PacketMs);
            Check.Equal(1920, usbProfile.BytesPerPacket);
            Check.Equal(100, usbProfile.RingCapacityMs);

            await using var server = new AgentServer(0, key, Path.Combine(root, "wifi-audio"));
            server.Start();
            await using var s = await Session.Open(server.Port, key, ct);
            using var audioStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var bridge = new LoopbackBridge();
            var stream = bridge.RunAsync(s.Connection, fixture: true, audioStop.Token, audioProfile: wifiProfile);
            try { await Task.Delay(250, ct); }
            finally
            {
                audioStop.Cancel();
                try { await stream; } catch (OperationCanceledException) { }
            }
            var stats = (await s.Connection.RequestAsync(LogicalChannel.Diagnostics, "audio.stats", new { }, ct)).Body;
            long packets = stats.GetProperty("packets").GetInt64();
            long bytes = stats.GetProperty("bytes").GetInt64();
            Check.True(packets >= 5, "Expected at least 5 20ms PCM packets");
            Check.Equal(packets * 3840, bytes);
            Check.Equal(20, bridge.ActivePacketMs);
        });

        await test("worker.expanded-jobs-and-offload-coordinator", async () =>
        {
            string workerRoot = Path.Combine(root, "worker-v2");
            var storage = new Storage(Path.Combine(workerRoot, "files"));
            await using var jobs = new JobScheduler(storage, Path.Combine(workerRoot, "jobs.json"));

            // Seed a compressible log file into storage
            string sampleText = string.Join('\n', Enumerable.Range(1, 300).Select(i =>
                i % 30 == 0 ? $"2026-11-19 ERROR subsystem=net failed attempt {i}" : $"2026-11-19 INFO heartbeat ok {i}"));
            byte[] rawBytes = Encoding.UTF8.GetBytes(sampleText);
            string rawSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
            string samplePath = storage.Resolve("sample.log");
            await File.WriteAllBytesAsync(samplePath, rawBytes, ct);

            // 1. compress -> decompress lossless round-trip
            jobs.Submit(new JobSpec("cmp-1", "compress", "sample.log"));
            await Check.Eventually(() => Task.FromResult(jobs.Query("cmp-1")?.State == "completed"));
            var cmpState = jobs.Query("cmp-1")!;
            var cmpJson = JsonSerializer.SerializeToElement(cmpState.Result, Message.Json);
            Check.Equal("sample.log.gz", cmpJson.GetProperty("outputName").GetString());
            Check.True(File.Exists(storage.Resolve("sample.log.gz")));
            Check.True(new FileInfo(storage.Resolve("sample.log.gz")).Length < rawBytes.Length, "Compressed artifact should be smaller");

            jobs.Submit(new JobSpec("dec-1", "decompress", "sample.log.gz", Parameters: new Dictionary<string, string> { ["outputName"] = "restored.log" }));
            await Check.Eventually(() => Task.FromResult(jobs.Query("dec-1")?.State == "completed"));
            byte[] restoredBytes = await File.ReadAllBytesAsync(storage.Resolve("restored.log"), ct);
            Check.Equal(rawSha, Convert.ToHexString(SHA256.HashData(restoredBytes)).ToLowerInvariant());

            // 2. log.scan finds exact 10 ERROR lines
            jobs.Submit(new JobSpec("scan-1", "log.scan", "sample.log", Parameters: new Dictionary<string, string> { ["pattern"] = "ERROR" }));
            await Check.Eventually(() => Task.FromResult(jobs.Query("scan-1")?.State == "completed"));
            var scanJson = JsonSerializer.SerializeToElement(jobs.Query("scan-1")!.Result, Message.Json);
            Check.Equal(10, scanJson.GetProperty("matchedLines").GetInt32());

            // 3. OffloadCoordinator cost-benefit, capability tier, thermal/battery guard, Auto-PiP advisor
            var coord = new OffloadCoordinator();
            var policy = new OffloadPolicyConfig();
            var usbRoute = new ActiveRouteSnapshot(TransportRouteKind.Usb, "serial", "dev1", 2.0, AdaptiveAudioProfile.ForRoute(TransportRouteKind.Usb), 0, 1);
            var neuralPhone = new PairedDeviceRecord(
                "dev1", "iPhone 13", "15.8", DeviceCapabilityTier.Neural, 4096, 6, true,
                ThermalState: "nominal", BatteryLevel: 80, BatteryCharging: true,
                SupportedJobs: ["sha256", "compress", "decompress", "log.scan", "ocr.extract", "speech.transcribe"]);
            var compactPhone = new PairedDeviceRecord(
                "dev2", "iPhone 6", "12.5.8", DeviceCapabilityTier.Compact, 1024, 2, false,
                ThermalState: "nominal", BatteryLevel: 80, BatteryCharging: true,
                SupportedJobs: ["sha256", "compress", "decompress", "log.scan"]);

            // Small payload on idle PC stays on PC
            var dSmall = coord.Evaluate(new OffloadCandidate("compress", 16 * 1024), new HostLoadMetrics(15, 40, PowerProfile.Balanced), neuralPhone, usbRoute, policy);
            Check.True(!dSmall.ShouldOffload && dSmall.Target == "pc", "Tiny payload on idle PC should remain local");

            // Large payload under high PC CPU offloads to iPhone
            var dHeavy = coord.Evaluate(new OffloadCandidate("compress", 8 * 1024 * 1024), new HostLoadMetrics(88, 70, PowerProfile.Eco), neuralPhone, usbRoute, policy);
            Check.True(dHeavy.ShouldOffload && dHeavy.Target == "iphone", "Heavy payload on pressured PC should offload to iPhone");

            // iOS 15 Vision OCR routes to iOS 15 Neural phone, NOT iOS 12 compact phone
            var dOcr15 = coord.Evaluate(new OffloadCandidate("ocr.extract", 512 * 1024), new HostLoadMetrics(20, 40, PowerProfile.Balanced), neuralPhone, usbRoute, policy);
            Check.True(dOcr15.ShouldOffload, "iOS 15 Neural iPhone should accept ocr.extract");
            var dOcr12 = coord.Evaluate(new OffloadCandidate("ocr.extract", 512 * 1024), new HostLoadMetrics(85, 80, PowerProfile.Eco), compactPhone, usbRoute, policy);
            Check.True(!dOcr12.ShouldOffload, "iOS 12 iPhone without ocr.extract capability must not receive OCR");

            // Thermal serious or battery <20% unplugged pauses offloading
            var hotPhone = neuralPhone with { ThermalState = "serious" };
            var dHot = coord.Evaluate(new OffloadCandidate("compress", 8 * 1024 * 1024), new HostLoadMetrics(90, 85, PowerProfile.Eco), hotPhone, usbRoute, policy);
            Check.True(!dHot.ShouldOffload, "Serious thermal state must pause offloading");

            var lowBatPhone = neuralPhone with { BatteryLevel = 15, BatteryCharging = false };
            var dLow = coord.Evaluate(new OffloadCandidate("compress", 8 * 1024 * 1024), new HostLoadMetrics(90, 85, PowerProfile.Eco), lowBatPhone, usbRoute, policy);
            Check.True(!dLow.ShouldOffload, "Unplugged battery <20% must pause offloading");

            // Auto-PiP advisor fires only after AutoPipSustainSeconds (12s)
            var highVideo = new HostLoadMetrics(82, 55, PowerProfile.Balanced, VideoPlayingOnPc: true);
            Check.True(!coord.UpdateAutoPipAdvisor(highVideo, policy, nowUnixMs: 1000));
            Check.True(!coord.UpdateAutoPipAdvisor(highVideo, policy, nowUnixMs: 10_000));
            Check.True(coord.UpdateAutoPipAdvisor(highVideo, policy, nowUnixMs: 13_500), "Auto-PiP should recommend after 12s sustained CPU>=70%");
            Check.True(!coord.UpdateAutoPipAdvisor(highVideo with { CpuUsagePercent = 30 }, policy, nowUnixMs: 14_000));
        });

        await test("rules.hysteresis-cooldown-and-while-active-undo", async () =>
        {
            var engine = new AutomationRuleEngine();
            var rules = new[]
            {
                new AutomationRuleConfig
                {
                    Id = "r-cpu",
                    Name = "CPU Spike",
                    When = "cpu-above",
                    Threshold = 80,
                    ForSec = 15,
                    CooldownMin = 5,
                    Then = "goto",
                    TargetPage = "telemetry",
                    WhileActive = true
                }
            };

            var high = new RuleEvaluationInput(92, 50, false, 80, false);
            var low = new RuleEvaluationInput(25, 50, false, 80, false);
            var clock = new DateTime(2026, 11, 19, 14, 0, 0);

            Check.Equal(0, engine.Evaluate(rules, high, clock, nowTickMs: 10_000).Count);
            Check.Equal(0, engine.Evaluate(rules, high, clock, nowTickMs: 20_000).Count); // 10s < 15s
            var fired = engine.Evaluate(rules, high, clock, nowTickMs: 25_000); // 15s!
            Check.Equal(1, fired.Count);
            Check.Equal("goto", fired[0].ActionKind);
            Check.Equal("telemetry", fired[0].TargetPage);

            // Brief 2s dip does NOT release (4s release hysteresis)
            Check.Equal(0, engine.Evaluate(rules, low, clock, nowTickMs: 26_000).Count);
            Check.Equal(0, engine.Evaluate(rules, low, clock, nowTickMs: 28_000).Count);

            // Sustained 4s low releases and emits whileActive undo
            var undone = engine.Evaluate(rules, low, clock, nowTickMs: 30_500);
            Check.Equal(1, undone.Count);
            Check.Equal("undo", undone[0].ActionKind);

            // Immediate re-spike within 5-minute cooldown does NOT refire
            Check.Equal(0, engine.Evaluate(rules, high, clock, nowTickMs: 40_000).Count);
            Check.Equal(0, engine.Evaluate(rules, high, clock, nowTickMs: 60_000).Count);

            // Overnight schedule test (22:00-07:00)
            var nightRule = new AutomationRuleConfig { Id = "night", During = "22:00-07:00", Days = "thu" };
            Check.True(AutomationRuleEngine.IsScheduled(nightRule, new DateTime(2026, 11, 19, 23, 30, 0))); // Thu 23:30
            Check.True(AutomationRuleEngine.IsScheduled(nightRule, new DateTime(2026, 11, 19, 2, 15, 0)));  // Thu 02:15
            Check.True(!AutomationRuleEngine.IsScheduled(nightRule, new DateTime(2026, 11, 19, 15, 0, 0))); // Thu 15:00
            await Task.CompletedTask;
        });

        await test("chargeguard.cycle-and-10min-silent-failsafe", async () =>
        {
            var cfg = new ChargeGuardConfig { Enabled = true, MinPercent = 50, MaxPercent = 80, Method = "action", OnActionId = "plug.on", OffActionId = "plug.off" };
            var calls = new List<string>();
            using var guard = new ChargeGuardService();

            // 82% while charging -> switch OFF
            guard.RecordPhoneBattery(82, charging: true, "nominal", nowUnixMs: 100_000);
            var a1 = await guard.EvaluateAndApplyAsync(cfg, (id, _) => { calls.Add(id); return Task.CompletedTask; }, ct, nowUnixMs: 100_500);
            Check.Equal("off", a1);
            Check.Equal("plug.off", calls[^1]);

            // 65% unplugged -> stay off (no action)
            guard.RecordPhoneBattery(65, charging: false, "nominal", nowUnixMs: 120_000);
            var a2 = await guard.EvaluateAndApplyAsync(cfg, (id, _) => { calls.Add(id); return Task.CompletedTask; }, ct, nowUnixMs: 120_500);
            Check.True(a2 is null, "Should hold between 50% and 80%");

            // 48% unplugged -> switch ON
            guard.RecordPhoneBattery(48, charging: false, "nominal", nowUnixMs: 140_000);
            var a3 = await guard.EvaluateAndApplyAsync(cfg, (id, _) => { calls.Add(id); return Task.CompletedTask; }, ct, nowUnixMs: 140_500);
            Check.Equal("on", a3);
            Check.Equal("plug.on", calls[^1]);

            // Turn back OFF at 85%, then simulate >10 minutes of phone silence -> FAIL-SAFE MUST TURN ON!
            guard.RecordPhoneBattery(85, charging: true, "nominal", nowUnixMs: 200_000);
            var a4 = await guard.EvaluateAndApplyAsync(cfg, (id, _) => { calls.Add(id); return Task.CompletedTask; }, ct, nowUnixMs: 200_500);
            Check.Equal("off", a4);

            var failsafe = await guard.EvaluateAndApplyAsync(cfg, (id, _) => { calls.Add(id); return Task.CompletedTask; }, ct, nowUnixMs: 200_000 + ChargeGuardService.SilentFailsafeMs + 5000);
            Check.Equal("on", failsafe);
            Check.Equal("plug.on", calls[^1]);
        });

        await test("feedproxy.ssrf-hardening", async () =>
        {
            // Strictly public IP checks
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("127.0.0.1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("10.0.0.1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("172.16.5.1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("192.168.1.1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("169.254.169.254")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("100.64.0.1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("::1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("fc00::1")));
            Check.True(!SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("fe80::1")));
            Check.True(SafeFeedProxy.IsStrictlyPublicAddress(IPAddress.Parse("1.1.1.1")));

            // URL allowlist & scheme checks
            Check.True(!SafeFeedProxy.TryValidateUrl("http://api.open-meteo.com/v1/forecast", null, out _), "HTTP must be rejected");
            Check.True(!SafeFeedProxy.TryValidateUrl("https://api.open-meteo.com:8443/v1/forecast", null, out _), "Non-default port must be rejected");
            Check.True(!SafeFeedProxy.TryValidateUrl("https://user:pass@api.open-meteo.com/v1/forecast", null, out _), "Userinfo must be rejected");
            Check.True(!SafeFeedProxy.TryValidateUrl("https://1.1.1.1/test", null, out _), "Raw IP literal must be rejected");
            Check.True(!SafeFeedProxy.TryValidateUrl("https://evil.example.com/feed", null, out _), "Unlisted host must be rejected");
            Check.True(SafeFeedProxy.TryValidateUrl("https://api.open-meteo.com/v1/forecast?latitude=21&longitude=105", null, out _));

            var cfg = new FeedProxyConfig { Enabled = true };

            // Redirect response blocked
            using var redirectClient = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://169.254.169.254/") } }));
            var rRedir = await SafeFeedProxy.FetchAsync("https://api.open-meteo.com/v1/forecast", cfg, ct, redirectClient);
            Check.Equal(502, rRedir.StatusCode);
            Check.True(rRedir.Error is not null);

            // Allowlisted response succeeds cleanly
            using var okClient = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"temp\":26.5}", Encoding.UTF8, "application/json")
            }));
            var rOk = await SafeFeedProxy.FetchAsync("https://api.open-meteo.com/v1/forecast", cfg, ct, okClient);
            Check.Equal(200, rOk.StatusCode);
            Check.True(rOk.Error is null);
            Check.Equal("{\"temp\":26.5}", Encoding.UTF8.GetString(rOk.Body));
        });

        await test("host.wifi-tls-end-to-end-and-new-api-routes", async () =>
        {
            int apiPort = ReservePort();
            int lanPort = ReservePort();
            string keyPath = Path.Combine(root, "wifi-e2e.key");
            await File.WriteAllTextAsync(keyPath, Convert.ToHexString(key), ct);

            var mixer = new MockPerAppAudioMixer();
            var cfg = new HostConfig
            {
                KeyFile = keyPath,
                Transport = "wifi",
                ApiPort = apiPort,
                PowerProfile = PowerProfile.Eco,
                StreamAudio = false,
                Lan = new LanConfig
                {
                    Enabled = true,
                    Port = lanPort,
                    BindAddress = "127.0.0.1",
                    CertificatePath = Path.Combine(root, "wifi-e2e.pfx"),
                    DeviceRegistryPath = Path.Combine(root, "wifi-e2e-devices.json"),
                    AdvertiseMdns = false
                }
            };

            using var host = new HostRuntime(cfg, audioMixer: mixer);
            using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var hostTask = host.RunAsync(hostStop.Token);

            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}"), Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", Convert.ToHexString(key).ToLowerInvariant());

            using var phoneStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? phoneLoop = null;
            try
            {
                // Wait for LocalApi + LanTlsServer to come up
                await Check.Eventually(async () =>
                {
                    try
                    {
                        var st = await http.GetFromJsonAsync<JsonElement>("/v1/lan/status", ct);
                        return st.GetProperty("enabled").GetBoolean();
                    }
                    catch { return false; }
                });

                // Start QR pairing via HTTP API
                using var pairRes = await http.PostAsync("/v1/lan/pair/start", new StringContent("{}", Encoding.UTF8, "application/json"), ct);
                pairRes.EnsureSuccessStatusCode();
                var pairJson = await pairRes.Content.ReadFromJsonAsync<JsonElement>(Message.Json, ct);
                string fp = pairJson.GetProperty("fingerprint").GetString()!;
                string code = pairJson.GetProperty("oneTimeCode").GetString()!;

                // Simulated iPhone claims session key over pinned TLS
                string claimedKeyHex;
                {
                    var (tcp, ssl) = await LanTlsServer.ConnectPinnedClientAsync("127.0.0.1", lanPort, fp, ct);
                    using (tcp) using (ssl)
                    {
                        await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                            Message.Create("pair.claim", new { code, deviceId = "iphone15-e2e", deviceName = "iPhone 15 Lab" }, id: "claim1").Encode()), ct);
                        var rep = Message.Decode((await Wire.ReadAsync(ssl, ct)).Payload);
                        Check.Equal("pair.ok", rep.Type);
                        claimedKeyHex = rep.Body.GetProperty("sessionKey").GetString()!;
                    }
                }
                byte[] claimedKey = Convert.FromHexString(claimedKeyHex);

                // Simulated iPhone reconnects over pinned TLS and runs BTH1 mutual HMAC + hello v2 + phone.health + mixer check
                var (ptcp, pssl) = await LanTlsServer.ConnectPinnedClientAsync("127.0.0.1", lanPort, fp, ct);
                phoneLoop = Task.Run(async () =>
                {
                    using (ptcp) using (pssl)
                    {
                        await Authentication.ServerAsync(pssl, claimedKey, phoneStop.Token);
                        await using var conn = new Connection(pssl);
                        var read = conn.RunAsync(async (frame, msg, token) =>
                        {
                            if (msg is null) return;
                            if (msg.Type == "hello")
                            {
                                await conn.SendAsync(frame.Channel, Message.Create("ok", new
                                {
                                    agent = "BackThread.iOS.Sim",
                                    deviceName = "iPhone 15 Lab",
                                    iosVersion = "15.8",
                                    ramMb = 4096,
                                    cpuCores = 6,
                                    hasNeuralEngine = true,
                                    thermalState = "nominal",
                                    batteryLevel = 76,
                                    batteryCharging = false,
                                    supportedJobs = new[] { "sha256", "compress", "ocr.extract" },
                                    capabilities = new[] { "wifi-tls", "sync", "adaptive-audio" }
                                }, replyTo: msg.Id), token);
                            }
                            else if (msg.Type is "action.registry" or "media.sync" or "ping" or "audio.volume")
                            {
                                await conn.SendAsync(frame.Channel, Message.Create("ok", new { ok = true }, replyTo: msg.Id), token);
                            }
                        }, phoneStop.Token);

                        await conn.SendAsync(LogicalChannel.Diagnostics, Message.Create("phone.health", new { batteryLevel = 74, batteryCharging = false, thermalState = "nominal" }), phoneStop.Token);
                        await conn.SendAsync(LogicalChannel.Control, Message.Create("client.viewport", new { tab = "media", awake = true, screenDimmed = false }), phoneStop.Token);
                        await read;
                    }
                }, CancellationToken.None);

                await Check.Eventually(async () =>
                {
                    var st = await http.GetFromJsonAsync<JsonElement>("/v1/status", ct);
                    return st.GetProperty("state").GetString() == "connected"
                        && st.GetProperty("viewportTab").GetString() == "media";
                }, 8000);

                // Verify new HTTP API routes
                var mixerList = await http.GetFromJsonAsync<JsonElement>("/v1/audio/mixer", ct);
                Check.Equal(1, mixerList.GetProperty("sessions").GetArrayLength());

                using var setMix = await http.PostAsJsonAsync("/v1/audio/mixer", new { processId = 4242, volume = 0.4f, muted = true }, ct);
                setMix.EnsureSuccessStatusCode();
                Check.True(mixer.Sessions[0].Muted && Math.Abs(mixer.Sessions[0].VolumeScalar - 0.4f) < 0.01f);

                using var profRes = await http.PostAsJsonAsync("/v1/power-profile", new { profile = "Performance" }, ct);
                profRes.EnsureSuccessStatusCode();
                Check.Equal(PowerProfile.Performance, host.PowerProfile);

                var offload = await http.GetFromJsonAsync<JsonElement>("/v1/offload/status", ct);
                Check.True(offload.GetProperty("phoneEligibleForCompute").GetBoolean());
                Check.Equal("Neural", offload.GetProperty("deviceTier").GetString());

                var devicesJson = await http.GetFromJsonAsync<JsonElement>("/v1/lan/devices", ct);
                Check.True(devicesJson.GetProperty("devices").GetArrayLength() >= 1);
            }
            finally
            {
                phoneStop.Cancel();
                hostStop.Cancel();
                if (phoneLoop is not null) try { await phoneLoop; } catch { }
                try { await hostTask; } catch { }
            }
        });
    }

    private static int ReservePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class MockPerAppAudioMixer : IPerAppAudioMixer
    {
        public List<PerAppAudioSession> Sessions { get; } =
        [
            new(4242, "sess-1", "spotify", "Spotify", 0.8f, false, "Active")
        ];

        public IReadOnlyList<PerAppAudioSession> ListSessions() => Sessions.ToList();

        public PerAppAudioSession? SetSessionVolume(int processId, float? volumeScalar, bool? muted)
        {
            int idx = Sessions.FindIndex(s => s.ProcessId == processId);
            if (idx < 0) return null;
            var cur = Sessions[idx];
            var next = cur with
            {
                VolumeScalar = volumeScalar ?? cur.VolumeScalar,
                Muted = muted ?? cur.Muted
            };
            Sessions[idx] = next;
            return next;
        }
    }
}
