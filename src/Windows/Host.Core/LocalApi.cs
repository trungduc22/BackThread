using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BackThread.Events;
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
    public void Stop() => app?.Lifetime.StopApplication();
    public async Task RunAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(LocalApi).Assembly.FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => { o.Listen(IPAddress.Loopback, port, l => l.Protocols = HttpProtocols.Http1); o.Limits.MaxRequestBodySize = 65536; o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5); o.Limits.MaxConcurrentConnections = 16; });
        await using var application = builder.Build(); app = application;
        string bearer = Convert.ToHexString(key).ToLowerInvariant();
        application.Use(async (context, next) =>
        {
            string actual = context.Request.Headers.Authorization.ToString(); string expected = "Bearer " + bearer;
            if (context.Request.Headers.ContainsKey("Origin") || actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected))) { context.Response.StatusCode = 401; return; }
            if (!await capacity.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            try { await next(context); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                if (!context.Response.HasStarted) { context.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : ex is IOException ? 503 : 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }, Message.Json, context.RequestAborted); }
            }
            finally { capacity.Release(); }
        });
        application.MapGet("/v1/status", () => host.Status);
        application.MapGet("/v1/metrics", () => host.Metrics);
        application.MapGet("/v1/apps",async()=>await host.Apps.WireCatalogAsync());
        application.MapGet("/v1/wifi",()=>host.WifiStatus);
        application.MapPost("/v1/wifi/enable",async(HttpContext c)=>{var b=await Body<JsonElement>(c);return await host.EnableWifiAsync(b.GetProperty("address").GetString()!,b.GetProperty("minutes").GetInt32(),b.TryGetProperty("fallback",out var fallback)&&fallback.GetBoolean(),c.RequestAborted);});
        application.MapPost("/v1/wifi/disable",async(HttpContext c)=>{await host.DisableWifiAsync(c.RequestAborted);return new{success=true};});
        application.MapPost("/v1/apps/launch",async(HttpContext c)=>await host.Apps.LaunchRequestAsync(await Body<JsonElement>(c),c.RequestAborted));
        application.MapPost("/v1/ui",async(HttpContext c)=>
        {
            var result=await host.InspectUiAsync(await Body<JsonElement>(c),c.RequestAborted);
            await c.Response.WriteAsJsonAsync(result,Message.Json,c.RequestAborted);
        });
        application.MapPost("/v1/jobs", async (HttpContext c) => (await host.Worker.SubmitAsync(await Body<JobSpec>(c), c.RequestAborted)).Body);
        application.MapGet("/v1/jobs/{id}", async (string id, HttpContext c) => (await host.Worker.QueryAsync(id, c.RequestAborted)).Body);
        application.MapDelete("/v1/jobs/{id}", async (string id, HttpContext c) => (await host.Worker.CancelAsync(id, c.RequestAborted)).Body);
        application.MapPost("/v1/archives", async (HttpContext c) => { var b = await Body<ArchiveRequest>(c); return (await host.Worker.UploadAsync(b.Path, b.Name, c.RequestAborted)).Body; });
        application.MapGet("/v1/artifacts/{name}", async (string name, long? offset, int? count, HttpContext c) => Results.Bytes(await host.Worker.ReadArtifactAsync(name, offset ?? 0, count ?? 32768, c.RequestAborted), "application/octet-stream"));
        application.MapPost("/v1/events", async (HttpContext c) => await host.PublishAsync(await Body<CompanionEvent>(c), c.RequestAborted));
        application.MapGet("/v1/events", () => host.History());
        application.MapPost("/v1/actions/{id}", async (string id, HttpContext c) => await host.ActionAsync(id, c.RequestAborted));
        application.MapPost("/v1/handoff", async (HttpContext c) => { var b = await Body<HandoffRequest>(c); return await host.HandoffAsync(b.Url, c.RequestAborted); });
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
        try { await application.WaitForShutdownAsync(ct); } finally { await application.StopAsync(CancellationToken.None); app = null; }
    }
    private static async Task<T> Body<T>(HttpContext c) => await c.Request.ReadFromJsonAsync<T>(Message.Json, c.RequestAborted) ?? throw new InvalidDataException("Request body required");
    private sealed record ArchiveRequest(string Path, string Name);
    private sealed record HandoffRequest(string Url);
}
