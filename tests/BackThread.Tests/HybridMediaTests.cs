using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackThread.Events;
using BackThread.Host;
using BackThread.Media;
using BackThread.Simulator;
using BackThread.Transport;

namespace BackThread.Tests;

internal static class HybridMediaTests
{
    private sealed class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    public static async Task RunAllAsync(
        Func<string, Func<Task>, Task> test,
        string root,
        byte[] key,
        CancellationToken ct)
    {
        await test("media.router-confirmed-handoff-pauses-windows-once-and-rejects-stale", async () =>
        {
            await using var server = new AgentServer(0, key, Path.Combine(root, "router-confirm")) { SimulateHandoffStepDelayMs = 15 };
            server.Start();
            await using var s = await Session.Open(server.Port, key, ct);
            int pauseCalls = 0, resumeCalls = 0;
            double? resumedPos = null;
            using var router = new MediaRouter(deviceHasInternet: true)
            {
                PauseWindowsMediaAsync = _ => { Interlocked.Increment(ref pauseCalls); return Task.CompletedTask; },
                ResumeWindowsMediaAsync = (_, pos) => { Interlocked.Increment(ref resumeCalls); resumedPos = pos; return Task.CompletedTask; }
            };
            s.OnMessage = async (_, m, token) =>
            {
                if (m?.Type == "media.handoff.status")
                {
                    var upd = m.Body.Deserialize<HandoffStatusUpdate>(Message.Json);
                    if (upd != null) await router.ProcessHandoffStatusAsync(upd, token);
                }
            };

            var submit = await router.SubmitAsync(new MediaSubmissionRequest(Url: "https://cdn.example.com/clip.mp4", Title: "Clip", StartPositionSeconds: 12), s.Connection, ct);
            Check.Equal(PlaybackOwner.TransferPending, submit.Owner);
            Check.Equal(0, pauseCalls);

            await Check.Eventually(() => Task.FromResult(router.CurrentState.Owner == PlaybackOwner.iPhoneOwned && router.CurrentState.Stage == HandoffStage.Confirmed));
            Check.Equal(1, pauseCalls);
            Check.True(router.CurrentState.WindowsPausedByHandoff);
            Check.True(router.ShouldSuppressWindowsMediaPush);
            Check.True(!router.ShouldStreamLivePcm);

            // Stale updates from old session/generation must be ignored without resetting state or double-pausing.
            await router.ProcessHandoffStatusAsync(new HandoffStatusUpdate("wrong", router.CurrentState.Generation, "failed", 0, 0, 0, false, Error: "stale"), ct);
            await router.ProcessHandoffStatusAsync(new HandoffStatusUpdate(router.CurrentState.SessionId, router.CurrentState.Generation - 1, "failed", 0, 0, 0, false, Error: "stale"), ct);
            await router.ProcessHandoffStatusAsync(new HandoffStatusUpdate(router.CurrentState.SessionId, router.CurrentState.Generation, "confirmed", 44, 180, 10, true), ct);
            Check.Equal(PlaybackOwner.iPhoneOwned, router.CurrentState.Owner);
            Check.Equal(1, pauseCalls);

            var returned = await router.ReturnToWindowsAsync(s.Connection, resumeWindows: true, ct);
            Check.Equal(PlaybackOwner.WindowsOwned, returned.Owner);
            Check.True(returned.ShouldStreamLivePcm);
            Check.Equal(1, resumeCalls);
            Check.True(resumedPos is > 10);
        });

        await test("media.router-transfer-failure-keeps-windows-playing", async () =>
        {
            await using var server = new AgentServer(0, key, Path.Combine(root, "router-fail")) { SimulateHandoffShouldFail = true, SimulateHandoffStepDelayMs = 10 };
            server.Start();
            await using var s = await Session.Open(server.Port, key, ct);
            int pauseCalls = 0;
            using var router = new MediaRouter(deviceHasInternet: true)
            {
                PauseWindowsMediaAsync = _ => { Interlocked.Increment(ref pauseCalls); return Task.CompletedTask; }
            };
            s.OnMessage = async (_, m, token) =>
            {
                if (m?.Type == "media.handoff.status")
                {
                    var upd = m.Body.Deserialize<HandoffStatusUpdate>(Message.Json);
                    if (upd != null) await router.ProcessHandoffStatusAsync(upd, token);
                }
            };

            await router.SubmitAsync(new MediaSubmissionRequest(Url: "https://cdn.example.com/broken.mp4"), s.Connection, ct);
            await Check.Eventually(() => Task.FromResult(router.CurrentState.Owner == PlaybackOwner.TransferFailed));
            Check.Equal(HandoffStage.Failed, router.CurrentState.Stage);
            Check.Equal(0, pauseCalls);
            Check.True(!router.CurrentState.WindowsPausedByHandoff);
            Check.True(router.CurrentState.ShouldStreamLivePcm);
        });

        await test("media.router-focus-and-usb-disconnect-sync", async () =>
        {
            await using var server = new AgentServer(0, key, Path.Combine(root, "usb-sync")) { SimulateHandoffStepDelayMs = 10 };
            server.Start();
            int freePort;
            {
                var r = new TcpListener(IPAddress.Loopback, 0); r.Start(); freePort = ((IPEndPoint)r.LocalEndpoint).Port; r.Stop();
            }
            string keyPath = Path.Combine(root, "usb-sync.key");
            await File.WriteAllTextAsync(keyPath, Convert.ToHexString(key), ct);
            var cfg = new HostConfig
            {
                KeyFile = keyPath,
                Transport = "localhost",
                DevicePort = server.Port,
                ApiPort = freePort,
                DeviceHasInternet = true,
                StreamAudio = false,
                CollectWindowsNotifications = false
            };

            using (var host = new HostRuntime(cfg))
            {
                using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var running = host.RunAsync(hostStop.Token);
                try
                {
                    await Check.Eventually(() => Task.FromResult(JsonSerializer.SerializeToElement(host.Status, Message.Json).GetProperty("state").GetString() == "connected"));
                    await host.SubmitMediaAsync(new MediaSubmissionRequest(Url: "https://cdn.example.com/show.mp4", Title: "Show"), ct);
                    await Check.Eventually(() => Task.FromResult(host.Router.CurrentState.Owner == PlaybackOwner.iPhoneOwned));

                    // Audio focus arbitration updates both host live PCM flag and device policy.
                    var muted = await host.SetFocusPolicyAsync(AudioFocusPolicy.KeepVideoMuteAudio, ct);
                    Check.True(muted.ShouldStreamLivePcm);
                    Check.True(server.LastMuteVideoAudio);
                    var indOnly = await host.SetFocusPolicyAsync(AudioFocusPolicy.IndependentOnly, ct);
                    Check.True(!indOnly.ShouldStreamLivePcm);
                    Check.Equal(1f, indOnly.IndependentVolume);

                    // Simulated USB disconnect must keep iPhoneOwned state alive even after transport drops.
                    host.Router.OnTransportDisconnected();
                    Check.Equal(PlaybackOwner.iPhoneOwned, host.Router.CurrentState.Owner);
                    Check.True(!host.Router.CurrentState.UsbConnected);
                }
                finally
                {
                    hostStop.Cancel();
                    await running;
                }
            }

            // Fresh host connection reconciles independent playback over media.sync without restarting playback.
            using var host2 = new HostRuntime(cfg);
            using var host2Stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var running2 = host2.RunAsync(host2Stop.Token);
            try
            {
                await Check.Eventually(() => Task.FromResult(
                    JsonSerializer.SerializeToElement(host2.Status, Message.Json).GetProperty("state").GetString() == "connected"
                    && host2.Router.CurrentState.Owner == PlaybackOwner.iPhoneOwned));
                Check.True(host2.Router.CurrentState.UsbConnected);
                Check.Equal("Show", host2.Router.CurrentState.Title);
            }
            finally
            {
                host2Stop.Cancel();
                await running2;
            }
        });

        await test("media.buffered-usb-mode-b-chunks-and-validation", async () =>
        {
            string badExt = Path.Combine(root, "movie.mkv");
            await File.WriteAllBytesAsync(badExt, [1, 2, 3, 4], ct);
            Check.True(!BufferedMediaSource.CanServeFile(badExt, out _, out _, out _));
            Check.True(!BufferedMediaSource.CanServeFile("relative.mp4", out _, out _, out _));

            string mp4Path = Path.Combine(root, "local-sample.mp4");
            byte[] mediaBytes = RandomNumberGenerator.GetBytes(160 * 1024);
            await File.WriteAllBytesAsync(mp4Path, mediaBytes, ct);
            string expectedSha = Convert.ToHexString(SHA256.HashData(mediaBytes)).ToLowerInvariant();

            await using var server = new AgentServer(0, key, Path.Combine(root, "mode-b")) { SimulateHandoffStepDelayMs = 10 };
            server.Start();
            await using var s = await Session.Open(server.Port, key, ct);
            using var router = new MediaRouter(deviceHasInternet: false);
            s.OnMessage = async (_, m, token) =>
            {
                if (m?.Type == "media.handoff.status")
                {
                    var upd = m.Body.Deserialize<HandoffStatusUpdate>(Message.Json);
                    if (upd != null) await router.ProcessHandoffStatusAsync(upd, token);
                }
                else if (m?.Type == "media.buffered.request")
                {
                    string sid = m.Body.GetProperty("streamId").GetString()!;
                    long gen = m.Body.GetProperty("generation").GetInt64();
                    long off = m.Body.GetProperty("offset").GetInt64();
                    int len = m.Body.GetProperty("length").GetInt32();
                    await router.ServeBufferedChunkAsync(sid, gen, off, len, s.Connection, token);
                }
            };

            var pending = await router.SubmitAsync(new MediaSubmissionRequest(FilePath: mp4Path, Mode: PlaybackMode.BufferedUsb), s.Connection, ct);
            Check.Equal(PlaybackMode.BufferedUsb, pending.Mode);
            await Check.Eventually(() => Task.FromResult(router.CurrentState.Owner == PlaybackOwner.iPhoneOwned && router.CurrentState.Stage == HandoffStage.Confirmed));
            Check.Equal(mediaBytes.LongLength, server.LastBufferedBytesReceived);
            Check.Equal(expectedSha, server.LastBufferedSha256);
        });

        await test("media.prober-and-youtube-oembed-adapter", async () =>
        {
            using var handler = new MockHttpHandler(req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("youtube.com/oembed"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"title\":\"Systems Engineering Talk\",\"author_name\":\"Channel Name\",\"thumbnail_url\":\"https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg\"}", Encoding.UTF8, "application/json")
                    };
                }
                if (req.Method == HttpMethod.Head && req.RequestUri.AbsolutePath == "/video-endpoint")
                {
                    var res = new HttpResponseMessage(HttpStatusCode.OK);
                    res.Content = new ByteArrayContent([]);
                    res.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
                    return res;
                }
                if (req.RequestUri.AbsolutePath == "/head-blocked")
                {
                    if (req.Method == HttpMethod.Head) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                    var res = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([0]) };
                    res.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var prober = new HttpMediaUrlProber(handler);
            var direct = new DirectMediaProvider(prober);
            var headResolved = await direct.EvaluateAsync(new Uri("https://cdn.example.org/video-endpoint"), true, ct);
            Check.True(headResolved.Supported);
            Check.Equal("video/mp4", headResolved.MimeType);

            var rangeFallback = await direct.EvaluateAsync(new Uri("https://cdn.example.org/head-blocked"), true, ct);
            Check.True(rangeFallback.Supported);
            Check.Equal("audio/mpeg", rangeFallback.MimeType);

            Check.True(YouTubeProvider.TryParseVideoId(new Uri("https://youtu.be/dQw4w9WgXcQ?t=1m30s"), out string id1, out int start1, out bool music1));
            Check.Equal("dQw4w9WgXcQ", id1); Check.Equal(90, start1); Check.True(!music1);
            Check.True(YouTubeProvider.TryParseVideoId(new Uri("https://music.youtube.com/watch?v=abcDEF12345&t=42s"), out string id2, out int start2, out bool music2));
            Check.Equal("abcDEF12345", id2); Check.Equal(42, start2); Check.True(music2);

            using var yt = new YouTubeProvider(handler);
            var meta = await yt.FetchMetadataAsync(new Uri("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=15"), ct);
            Check.Equal("Systems Engineering Talk", meta!.Title);
            Check.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?playsinline=1&rel=0&start=15", meta.EmbedUrl);

            using var routerDefault = new MediaRouter(youtubeProvider: yt, allowExperimentalYouTubeEmbed: false);
            var evalDefault = await routerDefault.EvaluateCapabilitiesAsync(new MediaSubmissionRequest(Url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ"), ct);
            Check.Equal(PlaybackMode.RemoteControl, evalDefault.SelectedMode);
            Check.True(!evalDefault.Capabilities.SupportsDirectInternet);

            using var routerEmbed = new MediaRouter(youtubeProvider: yt, allowExperimentalYouTubeEmbed: true);
            var evalEmbed = await routerEmbed.EvaluateCapabilitiesAsync(new MediaSubmissionRequest(Url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ"), ct);
            Check.True(evalEmbed.IsExperimentalEmbed);
            Check.Equal(PlaybackMode.DirectInternet, evalEmbed.SelectedMode);
        });

        await test("spotify.pkce-refresh-commands-and-error-mapping", async () =>
        {
            int tokenCalls = 0;
            bool forceUnauthorizedOnce = false;
            string? lastCommandPath = null;
            using var handler = new MockHttpHandler(req =>
            {
                string path = req.RequestUri!.PathAndQuery;
                if (path == "/api/token")
                {
                    tokenCalls++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"access_token\":\"tok_{tokenCalls}\",\"refresh_token\":\"ref_{tokenCalls}\",\"expires_in\":3600,\"scope\":\"user-read-playback-state\"}}", Encoding.UTF8, "application/json")
                    };
                }
                if (path == "/v1/me/player")
                {
                    if (forceUnauthorizedOnce)
                    {
                        forceUnauthorizedOnce = false;
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"is_playing\":true,\"progress_ms\":45000,\"device\":{\"id\":\"win1\",\"name\":\"Windows PC\"},\"item\":{\"name\":\"Midnight\",\"uri\":\"spotify:track:123\",\"duration_ms\":210000,\"artists\":[{\"name\":\"Band\"}],\"album\":{\"name\":\"Album\",\"images\":[{\"url\":\"https://i.scdn.co/image/1\"}]}}}", Encoding.UTF8, "application/json")
                    };
                }
                if (path == "/v1/me/player/devices")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"devices\":[{\"id\":\"win1\",\"name\":\"Windows PC\",\"type\":\"Computer\",\"is_active\":true,\"volume_percent\":70}]}", Encoding.UTF8, "application/json")
                    };
                }
                if (path.StartsWith("/v1/me/playlists"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"items\":[{\"id\":\"pl1\",\"name\":\"Coding Flow\",\"uri\":\"spotify:playlist:pl1\",\"tracks\":{\"total\":24}}]}", Encoding.UTF8, "application/json")
                    };
                }
                if (path.StartsWith("/v1/me/player/pause"))
                    return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":\"premium required\"}") };
                if (path.StartsWith("/v1/me/player/next"))
                    return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"no active device\"}") };
                if (path.StartsWith("/v1/me/player/previous"))
                {
                    var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
                    r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                    return r;
                }
                lastCommandPath = path;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

            var store = new InMemorySpotifyTokenStore();
            using var sp = new SpotifyProvider(store, handler, "client-123");
            var start = sp.BeginAuthorization("client-123", "http://127.0.0.1:48292/v1/spotify/callback");
            Check.True(start.AuthorizeUrl.Contains("code_challenge_method=S256"));

            await Check.Throws<UnauthorizedAccessException>(() => sp.CompleteAuthorizationAsync("abc", "tampered-state", ct));
            var tok = await sp.CompleteAuthorizationAsync("abc", start.State, ct);
            Check.Equal("tok_1", tok.AccessToken);

            var state = await sp.GetPlaybackStateAsync(ct);
            Check.Equal("Midnight", state!.Title);
            Check.Equal("Band", state.Artist);
            Check.Equal(1, (await sp.GetDevicesAsync(ct)).Length);
            Check.Equal(1, (await sp.GetPlaylistsAsync(10, ct)).Length);

            await sp.PlayAsync("spotify:track:123", "win1", ct);
            Check.True(lastCommandPath!.Contains("/v1/me/player/play?device_id=win1"));
            await sp.SeekAsync(30.5, ct);
            Check.Equal("/v1/me/player/seek?position_ms=30500", lastCommandPath);

            // Auto refresh on 401
            forceUnauthorizedOnce = true;
            var refreshedState = await sp.GetPlaybackStateAsync(ct);
            Check.Equal("Midnight", refreshedState!.Title);
            Check.Equal("tok_2", store.Load()!.AccessToken);

            // Typed error mapping
            try { await sp.PauseAsync(ct); throw new Exception("Expected 403"); } catch (SpotifyApiException ex) { Check.Equal(403, ex.StatusCode); }
            try { await sp.NextAsync(ct); throw new Exception("Expected 404"); } catch (SpotifyApiException ex) { Check.Equal(404, ex.StatusCode); }
            try { await sp.PreviousAsync(ct); throw new Exception("Expected 429"); } catch (SpotifyApiException ex) { Check.Equal(429, ex.StatusCode); }
        });

        await test("events.rich-priorities-and-privacy-masking", () =>
        {
            var er = new EventRouter { HideSensitive = true };
            var now = DateTimeOffset.UtcNow;
            var crit = er.Accept(new("id-crit", "SecurityApp", "CRITICAL ALERT", "Token expiring", 3, now.AddMinutes(5), Sensitive: false, AppName: "Auth", Category: "security"), now);
            Check.True(crit != null && crit.FullscreenEligible == true && crit.AppName == "Auth");

            var priv = er.Accept(new("id-priv", "Mail", "Secret Subject", "Private body", 2, now.AddMinutes(5), Sensitive: true, AppName: "Mail", Category: "email", IconBase64: "AQID"), now);
            Check.Equal("Private notification", priv!.Title);
            Check.True(priv.IconBase64 == null && priv.Category == null);

            Check.True(er.Accept(new("id-bad", "App", "T", "B", 2, now.AddMinutes(5), IconBase64: new string('A', 25000)), now) == null);
            return Task.CompletedTask;
        });

        await test("host.local-api-media-spotify-and-native-messaging", async () =>
        {
            await using var server = new AgentServer(0, key, Path.Combine(root, "api-full")) { SimulateHandoffStepDelayMs = 10 };
            server.Start();
            int apiPort;
            {
                var r = new TcpListener(IPAddress.Loopback, 0); r.Start(); apiPort = ((IPEndPoint)r.LocalEndpoint).Port; r.Stop();
            }
            string keyPath = Path.Combine(root, "api-full.key");
            await File.WriteAllTextAsync(keyPath, Convert.ToHexString(key), ct);

            using var mockHttp = new MockHttpHandler(req =>
            {
                if (req.RequestUri!.PathAndQuery == "/api/token")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"sp_tok\",\"refresh_token\":\"sp_ref\",\"expires_in\":3600,\"scope\":\"user-read-playback-state\"}", Encoding.UTF8, "application/json") };
                if (req.RequestUri.PathAndQuery == "/v1/me/player")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"is_playing\":false,\"progress_ms\":1000,\"item\":{\"name\":\"Song\",\"duration_ms\":60000,\"artists\":[{\"name\":\"Art\"}],\"album\":{\"name\":\"Alb\",\"images\":[]}},\"device\":{\"id\":\"d1\",\"name\":\"PC\"}}", Encoding.UTF8, "application/json") };
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

            var cfg = new HostConfig
            {
                KeyFile = keyPath,
                Transport = "localhost",
                DevicePort = server.Port,
                ApiPort = apiPort,
                DeviceHasInternet = true,
                StreamAudio = false,
                CollectWindowsNotifications = false,
                SpotifyClientId = "cid-1",
                SpotifyRedirectUri = $"http://127.0.0.1:{apiPort}/v1/spotify/callback"
            };
            using var host = new HostRuntime(cfg, new InMemorySpotifyTokenStore(), mockHttp);
            using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var running = host.RunAsync(hostStop.Token);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}"), Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Authorization = new("Bearer", Convert.ToHexString(key).ToLowerInvariant());

            try
            {
                await Check.Eventually(async () =>
                {
                    try
                    {
                        var st = await client.GetFromJsonAsync<JsonElement>("/v1/status", ct);
                        return st.GetProperty("state").GetString() == "connected";
                    }
                    catch { return false; }
                });

                // Origin bypass check remains enforced on new endpoints too
                using var evilReq = new HttpRequestMessage(HttpMethod.Get, "/v1/media/status");
                evilReq.Headers.Add("Origin", "https://evil.example");
                Check.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(evilReq, ct)).StatusCode);

                // SSE stream receives initial state cleanly
                {
                    using var sseReq = new HttpRequestMessage(HttpMethod.Get, "/v1/media/events");
                    using var sseRes = await client.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead, ct);
                    sseRes.EnsureSuccessStatusCode();
                    using var sseReader = new StreamReader(await sseRes.Content.ReadAsStreamAsync(ct));
                    string? firstLine = await sseReader.ReadLineAsync(ct);
                    Check.True(firstLine != null && firstLine.StartsWith(": connected"));
                }

                // Spotify PKCE flow through LocalApi
                var authStart = await (await client.PostAsJsonAsync("/v1/spotify/auth/start", new { }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
                string oauthState = authStart.GetProperty("state").GetString()!;
                using var bareClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}") };
                Check.Equal(HttpStatusCode.Forbidden, (await bareClient.GetAsync("/v1/spotify/callback?code=abc&state=bad", ct)).StatusCode);
                Check.Equal(HttpStatusCode.OK, (await bareClient.GetAsync($"/v1/spotify/callback?code=abc&state={oauthState}", ct)).StatusCode);
                var spStatus = await client.GetFromJsonAsync<JsonElement>("/v1/spotify/status", ct);
                Check.True(spStatus.GetProperty("connected").GetBoolean());
                Check.Equal("Song", spStatus.GetProperty("playback").GetProperty("title").GetString());

                // NativeMessagingHost stdio round-trip against live LocalApi
                using var stdin = new MemoryStream();
                using var stdout = new MemoryStream();
                await NativeMessagingHost.WriteNativeMessageAsync(stdin, new { action = "submitMedia", url = "https://cdn.example.com/native.mp4", mode = "Auto" }, ct);
                stdin.Position = 0;
                int rc = await NativeMessagingHost.RunStdioAsync(cfg, stdin, stdout, ct: ct);
                Check.Equal(0, rc);
                stdout.Position = 0;
                byte[] lenBytes = new byte[4];
                await stdout.ReadExactlyAsync(lenBytes, ct);
                int replyLen = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
                byte[] replyBytes = new byte[replyLen];
                await stdout.ReadExactlyAsync(replyBytes, ct);
                var nmReply = JsonDocument.Parse(replyBytes).RootElement;
                Check.True(nmReply.GetProperty("ok").GetBoolean());

                await Check.Eventually(() => Task.FromResult(host.Router.CurrentState.Owner == PlaybackOwner.iPhoneOwned));
                await host.ReturnToWindowsAsync(false, ct);
                Check.Equal(PlaybackOwner.WindowsOwned, host.Router.CurrentState.Owner);
            }
            finally
            {
                hostStop.Cancel();
                await running;
            }
        });
    }
}
