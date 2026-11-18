using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BackThread.Transport;

namespace BackThread.Host;

/// <summary>
/// Chromium Native Messaging stdio host bridge.
/// Reads length-prefixed JSON from stdin, authenticates locally against BackThread Host's loopback HTTP API
/// using the local session key (never exposing Bearer secrets or Origin bypass to browser pages),
/// and writes a length-prefixed JSON response to stdout.
/// </summary>
public static class NativeMessagingHost
{
    public const int MaxMessageBytes = 65536;

    public static async Task<int> RunStdioAsync(HostConfig config, Stream? input = null, Stream? output = null, HttpMessageHandler? httpHandler = null, CancellationToken ct = default)
    {
        input ??= Console.OpenStandardInput();
        output ??= Console.OpenStandardOutput();
        byte[] key = Authentication.LoadKey(config.KeyFile);
        string bearer = Convert.ToHexString(key).ToLowerInvariant();
        using var http = new HttpClient(httpHandler ?? new HttpClientHandler(), disposeHandler: httpHandler == null)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{config.ApiPort}"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        while (!ct.IsCancellationRequested)
        {
            byte[] lenBuf = new byte[4];
            int headerRead = await ReadExactAsync(input, lenBuf, ct);
            if (headerRead == 0) break;
            if (headerRead < 4) return 1;
            int length = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
            if (length <= 0 || length > MaxMessageBytes)
            {
                await WriteNativeMessageAsync(output, new { ok = false, error = "Invalid native message size" }, ct);
                return 1;
            }
            byte[] msgBytes = new byte[length];
            if (await ReadExactAsync(input, msgBytes, ct) != length) return 1;

            object reply;
            try
            {
                using var doc = JsonDocument.Parse(msgBytes);
                reply = await DispatchAsync(doc.RootElement, http, ct);
            }
            catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException or TaskCanceledException)
            {
                reply = new { ok = false, error = ex.Message };
            }
            await WriteNativeMessageAsync(output, reply, ct);
        }
        return 0;
    }

    public static async Task<object> DispatchAsync(JsonElement request, HttpClient http, CancellationToken ct)
    {
        string action = request.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        switch (action)
        {
            case "status":
                return await ForwardAsync(HttpMethod.Get, "/v1/status", null, http, ct);
            case "inspect":
                return await ForwardAsync(HttpMethod.Post, "/v1/media/capabilities", request, http, ct);
            case "submitMedia":
                return await ForwardAsync(HttpMethod.Post, "/v1/media/submit", request, http, ct);
            case "returnToWindows":
                return await ForwardAsync(HttpMethod.Post, "/v1/media/return", request, http, ct);
            default:
                return new { ok = false, error = "Unsupported native messaging action" };
        }
    }

    private static async Task<object> ForwardAsync(HttpMethod method, string path, JsonElement? body, HttpClient http, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body.HasValue)
            req.Content = new StringContent(body.Value.GetRawText(), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct);
        string json = await res.Content.ReadAsStringAsync(ct);
        JsonElement parsed = string.IsNullOrWhiteSpace(json) ? JsonSerializer.SerializeToElement(new { }, Message.Json) : JsonDocument.Parse(json).RootElement.Clone();
        return new { ok = res.IsSuccessStatusCode, statusCode = (int)res.StatusCode, data = parsed };
    }

    public static async Task WriteNativeMessageAsync(Stream output, object payload, CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, Message.Json);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, json.Length);
        await output.WriteAsync(header, ct);
        await output.WriteAsync(json, ct);
        await output.FlushAsync(ct);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (n == 0) return offset;
            offset += n;
        }
        return offset;
    }
}
