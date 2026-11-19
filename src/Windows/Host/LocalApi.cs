using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BackThread.Events;
using BackThread.Media;
using BackThread.Transport;
using BackThread.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackThread.Host;

public sealed class LocalApi(HostRuntime host, int port, byte[] key)
{
    private WebApplication? app;
    private readonly SemaphoreSlim capacity = new(8, 8);
    private readonly SemaphoreSlim subscriptions = new(4, 4);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ready => ready.Task;
    public void Stop() => app?.Lifetime.StopApplication();

    public async Task RunAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(LocalApi).Assembly.FullName });
        builder.Logging.ClearProviders();
        bool useHiberDeskPipe = OperatingSystem.IsWindows() && host.Config.EnableHiberDeskBridge;
        if (useHiberDeskPipe)
            builder.WebHost.UseNamedPipes(options => options.CurrentUserOnly = true);
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http1);
            if (useHiberDeskPipe)
                o.ListenNamedPipe(HiberDeskApi.PipeName(port), l => l.Protocols = HttpProtocols.Http1);
            o.Limits.MaxRequestBodySize = 65536;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            o.Limits.MaxConcurrentConnections = 16;
        });
        await using var application = builder.Build(); app = application;
        string bearer = Convert.ToHexString(key).ToLowerInvariant();
        string? hiberDeskBearer = useHiberDeskPipe ? HiberDeskApi.Bearer(key) : null;
        application.Use(async (context, next) =>
        {
            // The pipe transport has no IP endpoint. The only other listener is IPv4 loopback.
            bool isHiberDesk = useHiberDeskPipe && context.Connection.LocalIpAddress is null;
            // Browser OAuth redirect callback for Spotify PKCE validates CSRF state directly
            // and never exposes Host state or accepts arbitrary cross-origin Origin requests.
            bool isSpotifyCallback = !isHiberDesk && HttpMethods.IsGet(context.Request.Method)
                && string.Equals(context.Request.Path.Value, "/v1/spotify/callback", StringComparison.Ordinal);

            if (!isSpotifyCallback)
            {
                string actual = context.Request.Headers.Authorization.ToString();
                string expected = "Bearer " + (isHiberDesk ? hiberDeskBearer : bearer);
                if (context.Request.Headers.ContainsKey("Origin")
                    || actual.Length != expected.Length
                    || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
                {
                    context.Response.StatusCode = 401;
                    return;
                }
            }
            if (isHiberDesk)
            {
                context.Response.Headers[HiberDeskApi.VersionHeader] = HiberDeskApi.Version;
                if (!HiberDeskApi.Allows(context.Request)) { context.Response.StatusCode = 403; return; }
            }
            if (!await capacity.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            try { await next(context); }
            catch (SpotifyApiException ex)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = ex.StatusCode is >= 400 and <= 599 ? ex.StatusCode : 502;
                    await context.Response.WriteAsJsonAsync(new { error = ex.Message, code = ex.Code }, Message.Json, context.RequestAborted);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : ex is IOException ? 503 : 400;
                    await context.Response.WriteAsJsonAsync(new { error = ex.Message }, Message.Json, context.RequestAborted);
                }
            }
            finally { capacity.Release(); }
        });

        application.MapGet("/v1/status", (HttpContext c) => useHiberDeskPipe && c.Connection.LocalIpAddress is null
            ? host.HiberDeskStatus() : host.Status);
        application.MapGet("/v1/telemetry", () => host.LatestTelemetry);
        application.MapGet("/v1/audio/state", () => host.CurrentAudioControlState());
        application.MapPost("/v1/audio/mode", async (HttpContext c) =>
        {
            var b = await Body<AudioOutputModeRequest>(c);
            return await host.SetAudioOutputModeAsync(b.Mode, c.RequestAborted);
        });
        application.MapPost("/v1/audio/volume", async (HttpContext c) =>
        {
            var b = await Body<AudioVolumeRequest>(c);
            return await host.SetVolumeAsync(b.Value, b.Layer, c.RequestAborted);
        });
        application.MapPost("/v1/jobs", async (HttpContext c) => (await host.Worker.SubmitAsync(await Body<JobSpec>(c), c.RequestAborted)).Body);
        application.MapGet("/v1/jobs/{id}", async (string id, HttpContext c) => (await host.Worker.QueryAsync(id, c.RequestAborted)).Body);
        application.MapDelete("/v1/jobs/{id}", async (string id, HttpContext c) => (await host.Worker.CancelAsync(id, c.RequestAborted)).Body);
        application.MapPost("/v1/archives", async (HttpContext c) => { var b = await Body<ArchiveRequest>(c); return (await host.Worker.UploadAsync(b.Path, b.Name, c.RequestAborted)).Body; });
        application.MapGet("/v1/artifacts/{name}", async (string name, long? offset, int? count, HttpContext c) => Results.Bytes(await host.Worker.ReadArtifactAsync(name, offset ?? 0, count ?? 32768, c.RequestAborted), "application/octet-stream"));
        application.MapPost("/v1/events", async (HttpContext c) =>
        {
            var notification = await Body<CompanionEvent>(c);
            return useHiberDeskPipe && c.Connection.LocalIpAddress is null
                ? await host.PublishForHiberDeskAsync(notification, c.RequestAborted)
                : await host.PublishAsync(notification, c.RequestAborted);
        });
        application.MapGet("/v1/events", () => host.History());
        application.MapGet("/v1/notifications/status", () => new { status = host.NotificationAccessStatus() });
        application.MapGet("/v1/notifications/diagnostics", () => host.NotificationDiagnostics());
        application.MapPost("/v1/notifications/request-access", async () => new { status = await host.RequestNotificationAccessAsync() });
        application.MapPost("/v1/actions/{id}", async (string id, HttpContext c) => await host.ActionAsync(id, c.RequestAborted));
        application.MapPost("/v1/handoff", async (HttpContext c) => { var b = await Body<HandoffRequest>(c); return await host.HandoffAsync(b.Url, c.RequestAborted); });

        // Hybrid MediaRouter & External PiP endpoints
        application.MapGet("/v1/media/status", () => host.Router.CurrentState);
        application.MapGet("/v1/media/session", () => host.Router.CurrentState);
        application.MapPost("/v1/media/capabilities", async (HttpContext c) =>
        {
            var request = await Body<MediaSubmissionRequest>(c);
            return await host.InspectCapabilitiesAsync(request, c.RequestAborted);
        });
        application.MapPost("/v1/media/submit", async (HttpContext c) =>
        {
            var request = await Body<MediaSubmissionRequest>(c);
            if (useHiberDeskPipe && c.Connection.LocalIpAddress is null && !host.Config.PauseWindowsOnConfirmedHandoff)
                request = request with { AutoPauseWindowsOnConfirm = false };
            return await host.SubmitMediaAsync(request, c.RequestAborted);
        });
        application.MapPost("/v1/media/cancel", async (HttpContext c) =>
        {
            var b = await OptionalBody<ReasonRequest>(c);
            return await host.CancelMediaHandoffAsync(b?.Reason, c.RequestAborted);
        });
        application.MapPost("/v1/media/return", async (HttpContext c) =>
        {
            var b = await OptionalBody<ReturnRequest>(c);
            return await host.ReturnToWindowsAsync(b?.ResumeWindows ?? true, c.RequestAborted);
        });
        application.MapPost("/v1/media/command", async (HttpContext c) =>
        {
            var b = await Body<MediaCommandRequest>(c);
            return await host.MediaCommandAsync(b.Command, b.Seconds ?? 0, b.QualityCap, b.KeepScreenAwake, b.Target, c.RequestAborted);
        });
        application.MapPost("/v1/media/focus", async (HttpContext c) =>
        {
            var b = await Body<FocusRequest>(c);
            return await host.SetFocusPolicyAsync(b.Policy, c.RequestAborted);
        });
        application.MapPost("/v1/media/mode", async (HttpContext c) =>
        {
            var b = await Body<ModeSelectRequest>(c);
            return await host.SubmitMediaAsync(new MediaSubmissionRequest(Url: b.Url, FilePath: b.FilePath, Mode: b.Mode), c.RequestAborted);
        });
        application.MapGet("/v1/media/events", async (HttpContext context) =>
        {
            if (!await subscriptions.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            var queue = Channel.CreateBounded<MediaSessionState>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });
            void Update(MediaSessionState s) => queue.Writer.TryWrite(s);
            host.MediaStateChanged += Update;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                await context.Response.WriteAsync(": connected\n\nevent: media\ndata: " + JsonSerializer.Serialize(host.Router.CurrentState, Message.Json) + "\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await foreach (var s in queue.Reader.ReadAllAsync(context.RequestAborted))
                {
                    await context.Response.WriteAsync("event: media\ndata: " + JsonSerializer.Serialize(s, Message.Json) + "\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally { host.MediaStateChanged -= Update; subscriptions.Release(); }
        });

        // Spotify Web API OAuth PKCE & remote control endpoints
        application.MapGet("/v1/spotify/status", async (HttpContext c) =>
        {
            if (!host.Spotify.IsAuthenticated)
                return Results.Ok(new { connected = false, capabilities = SpotifyProvider.BuildCapabilities() });
            var playback = await host.Spotify.GetPlaybackStateAsync(c.RequestAborted);
            return Results.Ok(new { connected = true, playback, capabilities = SpotifyProvider.BuildCapabilities() });
        });
        application.MapPost("/v1/spotify/auth/start", async (HttpContext c) =>
        {
            var b = await OptionalBody<SpotifyAuthStartRequest>(c);
            string clientId = b?.ClientId ?? host.Config.SpotifyClientId ?? throw new InvalidDataException("Provide Spotify clientId or set spotifyClientId in config");
            return Results.Ok(host.Spotify.BeginAuthorization(clientId, b?.RedirectUri ?? host.Config.SpotifyRedirectUri));
        });
        application.MapGet("/v1/spotify/callback", async (string? code, string? state, string? error, HttpContext c) =>
        {
            if (!string.IsNullOrEmpty(error))
                return Results.Content($"<html><body><h3>Spotify authorization denied: {WebUtility.HtmlEncode(error)}</h3></body></html>", "text/html");
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Results.BadRequest(new { error = "Missing code or state" });
            await host.Spotify.CompleteAuthorizationAsync(code, state, c.RequestAborted);
            return Results.Content("<html><body style='font-family:sans-serif;background:#111;color:#eee'><h3>Spotify connected to BackThread.</h3><p>You may close this tab and return to BackThread.</p></body></html>", "text/html");
        });
        application.MapPost("/v1/spotify/disconnect", () => { host.Spotify.Disconnect(); return Results.Ok(new { connected = false }); });
        application.MapGet("/v1/spotify/devices", async (HttpContext c) =>
        {
            return new { devices = await host.Spotify.GetDevicesAsync(c.RequestAborted) };
        });
        application.MapGet("/v1/spotify/playlists", async (HttpContext c) =>
        {
            return new { playlists = await host.Spotify.GetPlaylistsAsync(20, c.RequestAborted) };
        });
        application.MapPost("/v1/spotify/play", async (HttpContext c) =>
        {
            var b = await OptionalBody<SpotifyPlayRequest>(c);
            await host.Spotify.PlayAsync(b?.Uri, b?.DeviceId, c.RequestAborted);
            return Results.Ok(new { ok = true });
        });
        application.MapPost("/v1/spotify/pause", async (HttpContext c) => { await host.Spotify.PauseAsync(c.RequestAborted); return Results.Ok(new { ok = true }); });
        application.MapPost("/v1/spotify/next", async (HttpContext c) => { await host.Spotify.NextAsync(c.RequestAborted); return Results.Ok(new { ok = true }); });
        application.MapPost("/v1/spotify/previous", async (HttpContext c) => { await host.Spotify.PreviousAsync(c.RequestAborted); return Results.Ok(new { ok = true }); });
        application.MapPost("/v1/spotify/seek", async (HttpContext c) => { var b = await Body<SeekRequest>(c); await host.Spotify.SeekAsync(b.Seconds, c.RequestAborted); return Results.Ok(new { ok = true }); });
        application.MapPost("/v1/spotify/transfer", async (HttpContext c) => { var b = await Body<SpotifyTransferRequest>(c); await host.Spotify.TransferPlaybackAsync(b.DeviceId, b.Play ?? true, c.RequestAborted); return Results.Ok(new { ok = true }); });
        application.MapPost("/v1/spotify/queue", async (HttpContext c) => { var b = await Body<SpotifyQueueRequest>(c); await host.Spotify.AddToQueueAsync(b.Uri, c.RequestAborted); return Results.Ok(new { ok = true }); });

        application.MapGet("/v1/progress", async (HttpContext context) =>
        {
            if (!await subscriptions.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            var queue = Channel.CreateBounded<JobState>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest });
            void Update(JobState s) => queue.Writer.TryWrite(s);
            host.JobProgress += Update; context.Response.ContentType = "text/event-stream"; context.Response.Headers.CacheControl = "no-store";
            try
            {
                await context.Response.WriteAsync(": connected\n\n", context.RequestAborted); await context.Response.Body.FlushAsync(context.RequestAborted);
                await foreach (var s in queue.Reader.ReadAllAsync(context.RequestAborted)) { await context.Response.WriteAsync("event: progress\ndata: " + JsonSerializer.Serialize(s, Message.Json) + "\n\n", context.RequestAborted); await context.Response.Body.FlushAsync(context.RequestAborted); }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally { host.JobProgress -= Update; subscriptions.Release(); }
        });

        await application.StartAsync(ct);
        ready.TrySetResult();
        try { await application.WaitForShutdownAsync(ct); } finally { await application.StopAsync(CancellationToken.None); app = null; }
    }

    private static async Task<T> Body<T>(HttpContext c) => await c.Request.ReadFromJsonAsync<T>(Message.Json, c.RequestAborted) ?? throw new InvalidDataException("Request body required");
    private static async Task<T?> OptionalBody<T>(HttpContext c) where T : class
    {
        if (c.Request.ContentLength is 0 or null && !c.Request.Headers.ContainsKey("Transfer-Encoding")) return null;
        try { return await c.Request.ReadFromJsonAsync<T>(Message.Json, c.RequestAborted); }
        catch (JsonException) { return null; }
    }

    private sealed record ArchiveRequest(string Path, string Name);
    private sealed record HandoffRequest(string Url);
    private sealed record ReasonRequest(string? Reason);
    private sealed record ReturnRequest(bool? ResumeWindows);
    private sealed record MediaCommandRequest(string Command, double? Seconds, string? QualityCap, bool? KeepScreenAwake, string? Target);
    private sealed record FocusRequest(AudioFocusPolicy Policy);
    private sealed record AudioOutputModeRequest(AudioOutputMode Mode);
    private sealed record AudioVolumeRequest(float Value, string? Layer = null);
    private sealed record ModeSelectRequest(PlaybackMode Mode, string? Url = null, string? FilePath = null);
    private sealed record SpotifyAuthStartRequest(string? ClientId, string? RedirectUri);
    private sealed record SpotifyPlayRequest(string? Uri, string? DeviceId);
    private sealed record SeekRequest(double Seconds);
    private sealed record SpotifyTransferRequest(string DeviceId, bool? Play);
    private sealed record SpotifyQueueRequest(string Uri);
}
