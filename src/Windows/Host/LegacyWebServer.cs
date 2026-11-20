using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BackThread.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace BackThread.Host;

/// <summary>
/// Optional, TLS-only lightweight legacy web companion server for older touchscreens (iOS 8-11 Safari,
/// older iPads, legacy Android/Windows Phone browsers). Never downgrades to plaintext HTTP for remote control.
/// Serves minimal HTML/CSS and strict ES5 JavaScript (&lt;15 KB total, zero frameworks/CDNs) with WebSocket
/// transport plus bounded XHR polling fallback and mandatory device-specific bearer authentication.
/// </summary>
public sealed class LegacyWebServer
{
    private readonly HostRuntime host;
    private readonly X509Certificate2 certificate;
    private readonly IPAddress bindAddress;
    private readonly int requestedPort;
    private WebApplication? app;
    private readonly TaskCompletionSource<int> boundPort = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LegacyWebServer(HostRuntime host, X509Certificate2 certificate, int port = 23454, IPAddress? bindAddress = null)
    {
        this.host = host;
        this.certificate = certificate;
        requestedPort = port;
        this.bindAddress = bindAddress ?? IPAddress.Any;
    }

    public Task<int> BoundPortAsync => boundPort.Task;
    public void Stop() => app?.Lifetime.StopApplication();

    public async Task RunAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LegacyWebServer).Assembly.FullName
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(bindAddress, requestedPort, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                listen.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                });
            });
            o.Limits.MaxRequestBodySize = 16384;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            o.Limits.MaxConcurrentConnections = 16;
        });

        await using var application = builder.Build();
        app = application;
        application.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        application.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' wss: https:; frame-ancestors 'none'";
            context.Response.Headers["Cache-Control"] = "no-store";
            await next(context);
        });

        application.MapGet("/", () => Results.Content(IndexHtml, "text/html; charset=utf-8"));
        application.MapGet("/app.js", () => Results.Content(AppJsEs5, "application/javascript; charset=utf-8"));

        // Keyless endpoint 1: single-use pairing code exchange over TLS -> device-specific bearer token
        application.MapPost("/web/api/pair/claim", async (HttpContext c) =>
        {
            using var doc = await JsonDocument.ParseAsync(c.Request.Body, cancellationToken: c.RequestAborted);
            string? code = doc.RootElement.TryGetProperty("code", out var ce) ? ce.GetString() : null;
            string deviceId = doc.RootElement.TryGetProperty("deviceId", out var de) && !string.IsNullOrWhiteSpace(de.GetString())
                ? de.GetString()!.Trim()
                : "web-" + Guid.NewGuid().ToString("N")[..12];
            string deviceName = doc.RootElement.TryGetProperty("deviceName", out var ne) && !string.IsNullOrWhiteSpace(ne.GetString())
                ? ne.GetString()!.Trim()
                : "Legacy Web Browser";

            if (!host.Pairing.Claim(code))
                return Results.Unauthorized();

            var issued = host.Devices.IssueDeviceKey(deviceId, deviceName, "LegacyWebTls");
            string tokenHex = Convert.ToHexString(issued.Key).ToLowerInvariant();
            CryptographicOperations.ZeroMemory(issued.Key);
            return Results.Ok(new
            {
                deviceId,
                deviceName,
                token = tokenHex,
                keyVersion = issued.Record.KeyVersion,
                allowTouchpad = issued.Record.AllowTouchpad,
                allowDrawingPad = issued.Record.AllowDrawingPad
            });
        });

        application.MapGet("/web/api/capabilities", (HttpContext c) =>
        {
            string? deviceId = AuthenticateDevice(c);
            if (deviceId is null) return Results.Unauthorized();
            var perms = host.Devices.GetInputPermissions(deviceId);
            var metrics = host.Input.GetMetrics();
            return Results.Ok(new
            {
                protocol = "legacy-web-v1",
                deviceId,
                tlsRequired = true,
                allowTouchpad = metrics.Enabled && metrics.AllowTouchpad && perms.AllowTouchpad,
                allowDrawingPad = metrics.Enabled && metrics.AllowDrawingPad && perms.AllowDrawingPad,
                monitors = host.Input.GetMonitors(),
                input = metrics
            });
        });

        application.MapGet("/web/api/status", (HttpContext c) =>
        {
            string? deviceId = AuthenticateDevice(c);
            if (deviceId is null) return Results.Unauthorized();
            var tel = host.LatestTelemetry;
            var media = host.Router.CurrentState;
            return Results.Ok(new
            {
                deviceId,
                hostState = host.Status.State,
                cpuPercent = tel.CpuUsagePercent,
                memoryPercent = tel.MemoryUsedPercent,
                mediaTitle = media.Title,
                mediaOwner = media.Owner.ToString(),
                input = host.Input.GetMetrics()
            });
        });

        application.MapPost("/web/api/input", async (HttpContext c) =>
        {
            string? deviceId = AuthenticateDevice(c);
            if (deviceId is null) return Results.Unauthorized();
            using var doc = await JsonDocument.ParseAsync(c.Request.Body, cancellationToken: c.RequestAborted);
            var res = DispatchInputJson(deviceId, doc.RootElement);
            return res.Accepted ? Results.Ok(res) : Results.Json(res, statusCode: StatusCodes.Status403Forbidden);
        });

        application.MapPost("/web/api/input/emergency-stop", (HttpContext c) =>
        {
            string? deviceId = AuthenticateDevice(c);
            if (deviceId is null) return Results.Unauthorized();
            host.Input.EmergencyDisconnect();
            return Results.Ok(new { emergencyStopped = true });
        });

        application.MapPost("/web/api/media/command", async (HttpContext c) =>
        {
            string? deviceId = AuthenticateDevice(c);
            if (deviceId is null) return Results.Unauthorized();
            using var doc = await JsonDocument.ParseAsync(c.Request.Body, cancellationToken: c.RequestAborted);
            string cmd = doc.RootElement.TryGetProperty("command", out var ce) ? (ce.GetString() ?? "").Trim().ToLowerInvariant() : "";
            if (cmd is not ("toggle" or "play" or "pause" or "next" or "previous"))
                return Results.BadRequest(new { error = "Command not in allowlist" });
            bool ok = await host.MediaCommandAsync(cmd, null, c.RequestAborted);
            return Results.Ok(new { ok, command = cmd });
        });

        // Authenticated WebSocket endpoint (credentials sent in first JSON frame, never in query string)
        application.Map("/web/ws", async (HttpContext c) =>
        {
            if (!c.WebSockets.IsWebSocketRequest)
            {
                c.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var ws = await c.WebSockets.AcceptWebSocketAsync();
            await HandleWebSocketAsync(ws, c.RequestAborted);
        });

        await application.StartAsync(ct);
        int actualPort = requestedPort;
        string? addr = application.Urls.FirstOrDefault();
        if (addr is not null && Uri.TryCreate(addr, UriKind.Absolute, out var uri))
            actualPort = uri.Port;
        boundPort.TrySetResult(actualPort);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        finally
        {
            try { await application.StopAsync(CancellationToken.None); } catch { }
        }
    }

    private string? AuthenticateDevice(HttpContext c)
    {
        if (!c.Request.IsHttps) return null;
        string deviceId = c.Request.Headers["X-BackThread-Device"].ToString().Trim();
        string auth = c.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(deviceId) || !auth.StartsWith("Bearer ", StringComparison.Ordinal))
            return null;
        string providedHex = auth["Bearer ".Length..].Trim();
        return VerifyDeviceToken(deviceId, providedHex) ? deviceId : null;
    }

    private bool VerifyDeviceToken(string deviceId, string providedHex)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || providedHex.Length != 64) return false;
        byte[]? expected = host.Devices.TryGetDeviceKey(deviceId);
        if (expected is null) return false;
        try
        {
            byte[] given = Convert.FromHexString(providedHex);
            return given.Length == 32 && CryptographicOperations.FixedTimeEquals(given, expected);
        }
        catch { return false; }
        finally { CryptographicOperations.ZeroMemory(expected); }
    }

    private InputDispatchResult DispatchInputJson(string deviceId, JsonElement root)
    {
        string mode = root.TryGetProperty("mode", out var me) ? (me.GetString() ?? "") : "";
        var perms = host.Devices.GetInputPermissions(deviceId);
        if (string.Equals(mode, "emergency_stop", StringComparison.OrdinalIgnoreCase))
        {
            host.Input.EmergencyDisconnect();
            return new(true, "emergency-stopped");
        }
        if (string.Equals(mode, "touchpad", StringComparison.OrdinalIgnoreCase))
        {
            var ev = new TouchpadEvent(
                Kind: root.TryGetProperty("kind", out var k) ? (k.GetString() ?? "move") : "move",
                Dx: root.TryGetProperty("dx", out var dx) && dx.TryGetDouble(out var dxv) ? dxv : 0,
                Dy: root.TryGetProperty("dy", out var dy) && dy.TryGetDouble(out var dyv) ? dyv : 0,
                ScrollX: root.TryGetProperty("scrollX", out var sx) && sx.TryGetDouble(out var sxv) ? sxv : 0,
                ScrollY: root.TryGetProperty("scrollY", out var sy) && sy.TryGetDouble(out var syv) ? syv : 0,
                ClientTimestampMs: root.TryGetProperty("ts", out var ts) && ts.TryGetInt64(out var tsv) ? tsv : null);
            return host.Input.ProcessTouchpad(ev, perms.AllowTouchpad);
        }
        if (string.Equals(mode, "draw", StringComparison.OrdinalIgnoreCase))
        {
            var ev = new DrawingPadEvent(
                Kind: root.TryGetProperty("kind", out var k) ? (k.GetString() ?? "stroke_move") : "stroke_move",
                NormX: root.TryGetProperty("x", out var x) && x.TryGetDouble(out var xv) ? xv : 0,
                NormY: root.TryGetProperty("y", out var y) && y.TryGetDouble(out var yv) ? yv : 0,
                DeviceAspect: root.TryGetProperty("aspect", out var a) && a.TryGetDouble(out var av) ? av : 0,
                Portrait: root.TryGetProperty("portrait", out var p) && p.ValueKind == JsonValueKind.True,
                ContactRadius: root.TryGetProperty("radius", out var r) && r.TryGetDouble(out var rv) ? rv : 0,
                HasRealPressure: root.TryGetProperty("hasRealPressure", out var hp) && hp.ValueKind == JsonValueKind.True,
                Pressure: root.TryGetProperty("pressure", out var pr) && pr.TryGetDouble(out var prv) ? prv : null,
                ClientTimestampMs: root.TryGetProperty("ts", out var ts) && ts.TryGetInt64(out var tsv) ? tsv : null);
            return host.Input.ProcessDrawingPad(ev, perms.AllowDrawingPad);
        }
        return new(false, "unsupported-mode");
    }

    private async Task HandleWebSocketAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        string? authedDeviceId = null;
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var recv = await ws.ReceiveAsync(buffer, ct);
                if (recv.MessageType == WebSocketMessageType.Close) break;
                if (recv.MessageType != WebSocketMessageType.Text || !recv.EndOfMessage) continue;

                using var doc = JsonDocument.Parse(buffer.AsMemory(0, recv.Count));
                var root = doc.RootElement;
                string type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";

                if (authedDeviceId is null)
                {
                    if (type != "auth")
                    {
                        await SendWsJsonAsync(ws, new { type = "error", error = "auth-required" }, ct);
                        await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth required", ct);
                        return;
                    }
                    string devId = root.TryGetProperty("deviceId", out var d) ? (d.GetString() ?? "").Trim() : "";
                    string tok = root.TryGetProperty("token", out var tk) ? (tk.GetString() ?? "").Trim() : "";
                    if (!VerifyDeviceToken(devId, tok))
                    {
                        await SendWsJsonAsync(ws, new { type = "error", error = "unauthorized" }, ct);
                        await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", ct);
                        return;
                    }
                    authedDeviceId = devId;
                    var perms = host.Devices.GetInputPermissions(devId);
                    await SendWsJsonAsync(ws, new { type = "auth.ok", deviceId = devId, allowTouchpad = perms.AllowTouchpad, allowDrawingPad = perms.AllowDrawingPad }, ct);
                    continue;
                }

                if (type == "ping")
                {
                    await SendWsJsonAsync(ws, new { type = "pong", ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, ct);
                }
                else if (type == "input")
                {
                    var res = DispatchInputJson(authedDeviceId, root);
                    if (!res.Accepted)
                        await SendWsJsonAsync(ws, new { type = "input.rejected", reason = res.Reason }, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            host.Input.ReleaseHeldButtons();
        }
    }

    private static Task SendWsJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Message.Json);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public const string IndexHtml = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no">
<title>BackThread Companion</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
html,body{margin:0;padding:0;height:100%;background:#111418;color:#e8eef5;font-family:-apple-system,Helvetica,Arial,sans-serif;overflow:hidden}
body.oled{background:#000000;color:#d6dce4}
.bar{display:flex;align-items:center;justify-content:space-between;padding:8px 10px;border-bottom:1px solid #262c34;font-size:13px}
.tabs button,.controls button{background:#1e252e;color:#e8eef5;border:1px solid #323c4a;border-radius:6px;padding:7px 10px;margin-right:4px;font-size:13px}
.tabs button.active{background:#0f766e;border-color:#14b8a6}
button.danger{background:#991b1b;border-color:#ef4444;color:#fff;font-weight:bold}
.pane{display:none;height:calc(100% - 48px);padding:10px}
.pane.active{display:block}
#pad,#drawCanvas{width:100%;height:74%;background:#181e26;border:1px solid #2d3748;border-radius:10px;touch-action:none;display:block}
body.oled #pad,body.oled #drawCanvas{background:#050505;border-color:#1c1c1c}
.row{margin-top:8px;display:flex;align-items:center;gap:6px;flex-wrap:wrap}
input{background:#181e26;color:#fff;border:1px solid #334155;border-radius:6px;padding:7px;font-size:14px}
.note{font-size:12px;color:#94a3b8;margin-top:6px}
</style>
</head>
<body>
<div class="bar">
  <div class="tabs">
    <button id="tPair" class="active" type="button">Pair</button>
    <button id="tPad" type="button">Touchpad</button>
    <button id="tDraw" type="button">Draw</button>
    <button id="tMedia" type="button">Status</button>
  </div>
  <div>
    <span id="connState">Offline</span>
    <button id="btnStop" class="danger" type="button">STOP</button>
  </div>
</div>

<div id="pPair" class="pane active">
  <div class="row">
    <input id="inCode" type="text" placeholder="One-time pairing code" autocomplete="off">
    <input id="inName" type="text" placeholder="Device name" value="Legacy Tablet">
    <button id="btnPair" type="button">Pair over TLS</button>
  </div>
  <div id="secNotice" class="note">TLS + explicit PC authorization required for Touchpad and Drawing Pad.</div>
</div>

<div id="pPad" class="pane">
  <div id="pad"></div>
  <div class="row controls">
    <button id="btnLeft" type="button">Left Click</button>
    <button id="btnRight" type="button">Right Click</button>
    <button id="btnDrag" type="button">Drag: Off</button>
    <button id="btnOled" type="button">OLED Black</button>
  </div>
  <div id="padNote" class="note">1-finger move/tap, 2-finger scroll, double-tap.</div>
</div>

<div id="pDraw" class="pane">
  <canvas id="drawCanvas" width="600" height="400"></canvas>
  <div class="row controls">
    <button id="btnClear" type="button">Clear Preview</button>
    <span id="drawNote" class="note">Absolute calibrated mapping (fit aspect).</span>
  </div>
</div>

<div id="pMedia" class="pane">
  <div id="statusBox" class="note">Waiting for telemetry...</div>
  <div class="row controls">
    <button id="mPrev" type="button">Prev</button>
    <button id="mToggle" type="button">Play / Pause</button>
    <button id="mNext" type="button">Next</button>
  </div>
</div>

<script src="/app.js"></script>
</body>
</html>
""";

    // Strict ES5 JavaScript for iOS 8-11 Safari, legacy Android WebView, and Windows Phone browsers.
    public const string AppJsEs5 = """
(function () {
  "use strict";
  var state = {
    deviceId: "",
    token: "",
    ws: null,
    wsReady: false,
    reconnectMs: 1000,
    dragHeld: false,
    lastX: 0,
    lastY: 0,
    lastTapAt: 0,
    touchMoved: false,
    twoFinger: false,
    allowTouchpad: false,
    allowDrawingPad: false
  };

  function el(id) { return document.getElementById(id); }
  function setText(id, txt) { var n = el(id); if (n) { n.textContent = String(txt); } }

  function isSecureTransport() {
    return window.location && window.location.protocol === "https:";
  }

  function xhrJson(method, url, body, cb) {
    var req = new XMLHttpRequest();
    req.open(method, url, true);
    req.setRequestHeader("Content-Type", "application/json");
    if (state.token && state.deviceId) {
      req.setRequestHeader("Authorization", "Bearer " + state.token);
      req.setRequestHeader("X-BackThread-Device", state.deviceId);
    }
    req.onreadystatechange = function () {
      if (req.readyState !== 4) { return; }
      var parsed = null;
      try { parsed = req.responseText ? JSON.parse(req.responseText) : null; } catch (e) {}
      cb(req.status, parsed);
    };
    req.send(body ? JSON.stringify(body) : null);
  }

  function showTab(name) {
    var map = { pair: ["tPair", "pPair"], pad: ["tPad", "pPad"], draw: ["tDraw", "pDraw"], media: ["tMedia", "pMedia"] };
    var k;
    for (k in map) {
      if (!map.hasOwnProperty(k)) { continue; }
      el(map[k][0]).className = (k === name) ? "active" : "";
      el(map[k][1]).className = (k === name) ? "pane active" : "pane";
    }
  }

  function sendInput(payload) {
    if (!isSecureTransport() || !state.token) { return; }
    payload.type = "input";
    payload.ts = (new Date()).getTime();
    if (state.ws && state.wsReady && state.ws.readyState === 1) {
      try { state.ws.send(JSON.stringify(payload)); return; } catch (e) {}
    }
    xhrJson("POST", "/web/api/input", payload, function () {});
  }

  function connectWs() {
    if (!isSecureTransport() || !state.token || typeof window.WebSocket !== "function") {
      setText("connState", state.token ? "HTTPS Poll" : "Offline");
      return;
    }
    try {
      var wsUrl = "wss://" + window.location.host + "/web/ws";
      var ws = new WebSocket(wsUrl);
      state.ws = ws;
      ws.onopen = function () {
        ws.send(JSON.stringify({ type: "auth", deviceId: state.deviceId, token: state.token }));
      };
      ws.onmessage = function (ev) {
        var msg = null;
        try { msg = JSON.parse(ev.data); } catch (e) { return; }
        if (msg.type === "auth.ok") {
          state.wsReady = true;
          state.reconnectMs = 1000;
          state.allowTouchpad = !!msg.allowTouchpad;
          state.allowDrawingPad = !!msg.allowDrawingPad;
          setText("connState", "TLS WS");
        }
      };
      ws.onclose = function () {
        state.wsReady = false;
        setText("connState", "Reconnecting");
        window.setTimeout(connectWs, state.reconnectMs);
        state.reconnectMs = Math.min(10000, state.reconnectMs * 2);
      };
    } catch (e) {
      setText("connState", "HTTPS Poll");
    }
  }

  function pollStatus() {
    if (!isSecureTransport() || !state.token) { return; }
    xhrJson("GET", "/web/api/status", null, function (code, data) {
      if (code === 200 && data) {
        setText("statusBox", "Host: " + data.hostState + " | CPU: " + (data.cpuPercent || 0) + "% | RAM: " + (data.memoryPercent || 0) + "% | Media: " + (data.mediaTitle || "Idle"));
      }
    });
  }

  function bindTouchpad() {
    var pad = el("pad");
    if (!pad) { return; }
    pad.addEventListener("touchstart", function (e) {
      e.preventDefault();
      state.touchMoved = false;
      state.twoFinger = e.touches.length >= 2;
      state.lastX = e.touches[0].clientX;
      state.lastY = e.touches[0].clientY;
    }, false);
    pad.addEventListener("touchmove", function (e) {
      e.preventDefault();
      if (!e.touches.length) { return; }
      var x = e.touches[0].clientX;
      var y = e.touches[0].clientY;
      var dx = x - state.lastX;
      var dy = y - state.lastY;
      state.lastX = x;
      state.lastY = y;
      if (Math.abs(dx) > 1 || Math.abs(dy) > 1) { state.touchMoved = true; }
      if (state.twoFinger || e.touches.length >= 2) {
        sendInput({ mode: "touchpad", kind: "scroll", scrollX: dx * 0.04, scrollY: -dy * 0.04 });
      } else {
        sendInput({ mode: "touchpad", kind: "move", dx: dx, dy: dy });
      }
    }, false);
    pad.addEventListener("touchend", function (e) {
      e.preventDefault();
      if (!state.touchMoved) {
        var now = (new Date()).getTime();
        if (state.twoFinger) {
          sendInput({ mode: "touchpad", kind: "right_click" });
        } else if (now - state.lastTapAt < 300) {
          sendInput({ mode: "touchpad", kind: "double_tap" });
        } else {
          sendInput({ mode: "touchpad", kind: "tap" });
        }
        state.lastTapAt = now;
      }
      state.twoFinger = false;
    }, false);
  }

  function bindDrawingPad() {
    var canvas = el("drawCanvas");
    if (!canvas) { return; }
    var ctx = canvas.getContext ? canvas.getContext("2d") : null;
    function normCoords(touch) {
      var rect = canvas.getBoundingClientRect();
      var nx = (touch.clientX - rect.left) / Math.max(1, rect.width);
      var ny = (touch.clientY - rect.top) / Math.max(1, rect.height);
      return { x: Math.max(0, Math.min(1, nx)), y: Math.max(0, Math.min(1, ny)), aspect: rect.width / Math.max(1, rect.height), radius: touch.radiusX || 0 };
    }
    canvas.addEventListener("touchstart", function (e) {
      e.preventDefault();
      if (!e.touches.length) { return; }
      var p = normCoords(e.touches[0]);
      if (ctx) { ctx.strokeStyle = "#14b8a6"; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(p.x * canvas.width, p.y * canvas.height); }
      sendInput({ mode: "draw", kind: "stroke_start", x: p.x, y: p.y, aspect: p.aspect, portrait: window.innerHeight > window.innerWidth, radius: p.radius, hasRealPressure: false });
    }, false);
    canvas.addEventListener("touchmove", function (e) {
      e.preventDefault();
      if (!e.touches.length) { return; }
      var p = normCoords(e.touches[0]);
      if (ctx) { ctx.lineTo(p.x * canvas.width, p.y * canvas.height); ctx.stroke(); }
      sendInput({ mode: "draw", kind: "stroke_move", x: p.x, y: p.y, aspect: p.aspect, portrait: window.innerHeight > window.innerWidth, radius: p.radius, hasRealPressure: false });
    }, false);
    canvas.addEventListener("touchend", function (e) {
      e.preventDefault();
      sendInput({ mode: "draw", kind: "stroke_end", x: 0, y: 0 });
    }, false);
    el("btnClear").onclick = function () {
      if (ctx) { ctx.clearRect(0, 0, canvas.width, canvas.height); }
    };
  }

  function init() {
    if (!isSecureTransport()) {
      setText("secNotice", "Insecure HTTP detected. Remote control is disabled; use HTTPS.");
    }
    el("tPair").onclick = function () { showTab("pair"); };
    el("tPad").onclick = function () { showTab("pad"); };
    el("tDraw").onclick = function () { showTab("draw"); };
    el("tMedia").onclick = function () { showTab("media"); pollStatus(); };
    el("btnOled").onclick = function () {
      document.body.className = (document.body.className === "oled") ? "" : "oled";
    };
    el("btnLeft").onclick = function () { sendInput({ mode: "touchpad", kind: "tap" }); };
    el("btnRight").onclick = function () { sendInput({ mode: "touchpad", kind: "right_click" }); };
    el("btnDrag").onclick = function () {
      state.dragHeld = !state.dragHeld;
      setText("btnDrag", state.dragHeld ? "Drag: On" : "Drag: Off");
      sendInput({ mode: "touchpad", kind: state.dragHeld ? "drag_start" : "drag_end" });
    };
    el("btnStop").onclick = function () {
      state.dragHeld = false;
      setText("btnDrag", "Drag: Off");
      xhrJson("POST", "/web/api/input/emergency-stop", {}, function () { setText("connState", "STOPPED"); });
    };
    el("mPrev").onclick = function () { xhrJson("POST", "/web/api/media/command", { command: "previous" }, function () {}); };
    el("mToggle").onclick = function () { xhrJson("POST", "/web/api/media/command", { command: "toggle" }, function () {}); };
    el("mNext").onclick = function () { xhrJson("POST", "/web/api/media/command", { command: "next" }, function () {}); };
    el("btnPair").onclick = function () {
      if (!isSecureTransport()) { setText("secNotice", "Pairing requires HTTPS."); return; }
      var code = (el("inCode").value || "").replace(/^\s+|\s+$/g, "");
      var name = (el("inName").value || "Legacy Browser").replace(/^\s+|\s+$/g, "");
      xhrJson("POST", "/web/api/pair/claim", { code: code, deviceName: name }, function (status, data) {
        if (status === 200 && data && data.token) {
          state.deviceId = data.deviceId;
          state.token = data.token;
          setText("secNotice", "Paired as " + data.deviceId + ". Enable Touchpad/Draw permission on PC.");
          connectWs();
          showTab("pad");
        } else {
          setText("secNotice", "Pairing failed (" + status + "). Check one-time code.");
        }
      });
    };
    bindTouchpad();
    bindDrawingPad();
    window.setInterval(pollStatus, 5000);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init, false);
  } else {
    init();
  }
})();
""";
}
