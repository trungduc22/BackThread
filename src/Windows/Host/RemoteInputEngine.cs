using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace BackThread.Host;

public sealed record RemoteInputConfig
{
    /// <summary>Disabled by default; requires explicit opt-in plus per-device permission.</summary>
    public bool Enabled { get; init; }
    public bool AllowTouchpad { get; init; }
    public bool AllowDrawingPad { get; init; }
    public double TouchpadSensitivity { get; init; } = 1.4;
    public bool TouchpadAcceleration { get; init; } = true;
    public double ScrollSensitivity { get; init; } = 1.0;
    public int TargetMonitorIndex { get; init; }
    /// <summary>fit | fill | stretch</summary>
    public string DrawingAspectMode { get; init; } = "fit";
    public double CalMinX { get; init; }
    public double CalMinY { get; init; }
    public double CalMaxX { get; init; } = 1.0;
    public double CalMaxY { get; init; } = 1.0;
    public bool PalmRejection { get; init; } = true;
    public double PalmContactRadiusThreshold { get; init; } = 32.0;
    public int IdleButtonReleaseMs { get; init; } = 5000;
}

public sealed record MonitorDescriptor(
    int Index,
    string Name,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary);

public sealed record TouchpadEvent(
    string Kind,
    double Dx = 0,
    double Dy = 0,
    double ScrollX = 0,
    double ScrollY = 0,
    long? ClientTimestampMs = null);

public sealed record DrawingPadEvent(
    string Kind,
    double NormX,
    double NormY,
    double DeviceAspect = 0,
    bool Portrait = false,
    double ContactRadius = 0,
    bool HasRealPressure = false,
    double? Pressure = null,
    double? TiltX = null,
    double? TiltY = null,
    int? PenButtons = null,
    long? ClientTimestampMs = null);

public sealed record InputDispatchResult(
    bool Accepted,
    string? Reason = null,
    int? MappedX = null,
    int? MappedY = null,
    bool PalmRejected = false);

public sealed record RemoteInputMetrics(
    bool Enabled,
    bool AllowTouchpad,
    bool AllowDrawingPad,
    bool EmergencyStopped,
    bool LeftButtonHeld,
    long TotalEvents,
    long TouchpadEvents,
    long DrawingEvents,
    long DroppedEvents,
    long RejectedUnauthorized,
    long PalmRejectedEvents,
    double AvgLatencyMs,
    double P95LatencyMs,
    double AvgJitterPx,
    int TargetMonitorIndex,
    string DrawingAspectMode);

/// <summary>
/// Strictly mouse-only pointer injection abstraction. Never exposes keyboard/shell injection or secure-desktop elevation.
/// </summary>
public interface IRemoteInputSink
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();
    void MoveRelative(int dx, int dy);
    void MoveAbsolute(int screenX, int screenY, MonitorDescriptor virtualDesktop);
    void LeftDown();
    void LeftUp();
    void RightClick();
    void MiddleClick();
    void Scroll(int wheelDeltaY, int wheelDeltaX);
}

/// <summary>
/// Win32 SendInput mouse-only backend (zero WinUI/NativeShellKit dependencies).
/// </summary>
public sealed class Win32RemoteInputSink : IRemoteInputSink
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        if (!OperatingSystem.IsWindows())
            return [new MonitorDescriptor(0, "VirtualPrimary", 0, 0, 1920, 1080, true)];

        var list = new List<MonitorDescriptor>();
        try
        {
            int idx = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfoW(hMonitor, ref info))
                {
                    int w = Math.Max(1, info.rcMonitor.Right - info.rcMonitor.Left);
                    int h = Math.Max(1, info.rcMonitor.Bottom - info.rcMonitor.Top);
                    bool primary = (info.dwFlags & 1) != 0;
                    string name = string.IsNullOrWhiteSpace(info.szDevice) ? $"Display {idx + 1}" : info.szDevice;
                    list.Add(new MonitorDescriptor(idx++, name, info.rcMonitor.Left, info.rcMonitor.Top, w, h, primary));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        if (list.Count == 0)
            list.Add(new MonitorDescriptor(0, "Primary", 0, 0, 1920, 1080, true));
        return list;
    }

    public MonitorDescriptor GetVirtualDesktop()
    {
        if (!OperatingSystem.IsWindows())
            return new MonitorDescriptor(0, "VirtualDesktop", 0, 0, 1920, 1080, true);
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        return new MonitorDescriptor(0, "VirtualDesktop", left, top, width, height, true);
    }

    public void MoveRelative(int dx, int dy)
    {
        if (!OperatingSystem.IsWindows() || (dx == 0 && dy == 0)) return;
        SendMouse(dx, dy, 0, MOUSEEVENTF_MOVE);
    }

    public void MoveAbsolute(int screenX, int screenY, MonitorDescriptor virtualDesktop)
    {
        if (!OperatingSystem.IsWindows()) return;
        var vd = virtualDesktop.Width > 0 && virtualDesktop.Height > 0 ? virtualDesktop : GetVirtualDesktop();
        int normX = (int)Math.Clamp(Math.Round((screenX - vd.Left) * 65535.0 / Math.Max(1, vd.Width - 1)), 0, 65535);
        int normY = (int)Math.Clamp(Math.Round((screenY - vd.Top) * 65535.0 / Math.Max(1, vd.Height - 1)), 0, 65535);
        SendMouse(normX, normY, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
    }

    public void LeftDown() { if (OperatingSystem.IsWindows()) SendMouse(0, 0, 0, MOUSEEVENTF_LEFTDOWN); }
    public void LeftUp() { if (OperatingSystem.IsWindows()) SendMouse(0, 0, 0, MOUSEEVENTF_LEFTUP); }

    public void RightClick()
    {
        if (!OperatingSystem.IsWindows()) return;
        SendMouse(0, 0, 0, MOUSEEVENTF_RIGHTDOWN);
        SendMouse(0, 0, 0, MOUSEEVENTF_RIGHTUP);
    }

    public void MiddleClick()
    {
        if (!OperatingSystem.IsWindows()) return;
        SendMouse(0, 0, 0, MOUSEEVENTF_MIDDLEDOWN);
        SendMouse(0, 0, 0, MOUSEEVENTF_MIDDLEUP);
    }

    public void Scroll(int wheelDeltaY, int wheelDeltaX)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (wheelDeltaY != 0) SendMouse(0, 0, (uint)wheelDeltaY, MOUSEEVENTF_WHEEL);
        if (wheelDeltaX != 0) SendMouse(0, 0, (uint)wheelDeltaX, MOUSEEVENTF_HWHEEL);
    }

    private static void SendMouse(int dx, int dy, uint mouseData, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = mouseData, dwFlags = flags }
        };
        _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
        // Padding to match union size with KEYBDINPUT/HARDWAREINPUT on x64.
        public nint pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);
}

/// <summary>
/// Adaptive One-Euro low-jitter filter for smooth low-speed drawing and instant high-speed strokes.
/// </summary>
internal sealed class OneEuroFilter2D(double minCutoff = 1.2, double beta = 0.04, double dCutoff = 1.0)
{
    private bool hasState;
    private double xPrev, yPrev, dxPrev, dyPrev;
    private double lastTimeSec;

    public void Reset() => hasState = false;

    public (double X, double Y) Filter(double x, double y, double timeSec)
    {
        if (!hasState)
        {
            hasState = true;
            xPrev = x; yPrev = y; dxPrev = 0; dyPrev = 0; lastTimeSec = timeSec;
            return (x, y);
        }

        double dt = Math.Clamp(timeSec - lastTimeSec, 1e-4, 0.25);
        lastTimeSec = timeSec;

        double dx = (x - xPrev) / dt;
        double dy = (y - yPrev) / dt;
        double alphaD = Alpha(dt, dCutoff);
        double edx = alphaD * dx + (1.0 - alphaD) * dxPrev;
        double edy = alphaD * dy + (1.0 - alphaD) * dyPrev;
        dxPrev = edx; dyPrev = edy;

        double speed = Math.Sqrt(edx * edx + edy * edy);
        double cutoff = minCutoff + beta * speed;
        double alpha = Alpha(dt, cutoff);

        double xf = alpha * x + (1.0 - alpha) * xPrev;
        double yf = alpha * y + (1.0 - alpha) * yPrev;
        xPrev = xf; yPrev = yf;
        return (xf, yf);
    }

    private static double Alpha(double dt, double cutoff)
    {
        double tau = 1.0 / (2.0 * Math.PI * Math.Max(0.01, cutoff));
        return 1.0 / (1.0 + tau / dt);
    }
}

/// <summary>
/// Precision Touchpad (Mode 1) + Drawing Pad (Mode 2) + honest pen metadata (Mode 3) engine with strict
/// default-OFF safety gates, emergency disconnect, held-button cleanup, and latency/jitter telemetry.
/// </summary>
public sealed class RemoteInputEngine
{
    private readonly Lock gate = new();
    private readonly IRemoteInputSink sink;
    private readonly OneEuroFilter2D drawFilter = new();
    private readonly double[] latencyRing = new double[128];
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private RemoteInputConfig config;
    private bool emergencyStopped;
    private bool leftButtonHeld;
    private bool strokeActive;
    private double remX, remY;
    private double lastRawX, lastRawY;
    private bool hasLastDrawSample;

    private long totalEvents, touchpadEvents, drawingEvents, droppedEvents, rejectedUnauthorized, palmRejectedEvents;
    private int latencyCount, latencyIndex;
    private double jitterSum;
    private long jitterSamples;

    public RemoteInputEngine(RemoteInputConfig? initialConfig = null, IRemoteInputSink? sink = null)
    {
        config = initialConfig ?? new RemoteInputConfig();
        this.sink = sink ?? new Win32RemoteInputSink();
    }

    public RemoteInputConfig Config { get { lock (gate) return config; } }
    public IReadOnlyList<MonitorDescriptor> GetMonitors() => sink.GetMonitors();

    public RemoteInputConfig UpdateConfig(RemoteInputConfig updated, bool clearEmergencyStop = false)
    {
        lock (gate)
        {
            double sens = double.IsFinite(updated.TouchpadSensitivity) ? Math.Clamp(updated.TouchpadSensitivity, 0.2, 5.0) : 1.4;
            double scroll = double.IsFinite(updated.ScrollSensitivity) ? Math.Clamp(updated.ScrollSensitivity, 0.2, 5.0) : 1.0;
            string aspect = updated.DrawingAspectMode is "fit" or "fill" or "stretch" ? updated.DrawingAspectMode : "fit";
            double minX = Math.Clamp(updated.CalMinX, 0.0, 0.9);
            double minY = Math.Clamp(updated.CalMinY, 0.0, 0.9);
            double maxX = Math.Clamp(updated.CalMaxX, minX + 0.1, 1.0);
            double maxY = Math.Clamp(updated.CalMaxY, minY + 0.1, 1.0);

            config = updated with
            {
                TouchpadSensitivity = sens,
                ScrollSensitivity = scroll,
                DrawingAspectMode = aspect,
                CalMinX = minX,
                CalMinY = minY,
                CalMaxX = maxX,
                CalMaxY = maxY,
                TargetMonitorIndex = Math.Max(0, updated.TargetMonitorIndex)
            };
            if (clearEmergencyStop) emergencyStopped = false;
            if (!config.Enabled) ReleaseHeldButtonsLocked();
            return config;
        }
    }

    /// <summary>Immediately disables all remote input and releases any held mouse buttons.</summary>
    public void EmergencyDisconnect()
    {
        lock (gate)
        {
            emergencyStopped = true;
            config = config with { Enabled = false };
            ReleaseHeldButtonsLocked();
            drawFilter.Reset();
            remX = 0; remY = 0;
        }
    }

    /// <summary>Called on transport disconnect or session switch so a drag/stroke never gets stuck down.</summary>
    public void ReleaseHeldButtons()
    {
        lock (gate)
        {
            ReleaseHeldButtonsLocked();
            drawFilter.Reset();
            remX = 0; remY = 0;
        }
    }

    private void ReleaseHeldButtonsLocked()
    {
        if (leftButtonHeld)
        {
            sink.LeftUp();
            leftButtonHeld = false;
        }
        strokeActive = false;
        hasLastDrawSample = false;
    }

    public InputDispatchResult ProcessTouchpad(TouchpadEvent ev, bool deviceAuthorized)
    {
        long startTicks = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (emergencyStopped || !config.Enabled || !config.AllowTouchpad || !deviceAuthorized)
            {
                rejectedUnauthorized++;
                return new(false, emergencyStopped ? "emergency-stopped" : "touchpad-not-authorized");
            }

            if (!double.IsFinite(ev.Dx) || !double.IsFinite(ev.Dy) || !double.IsFinite(ev.ScrollX) || !double.IsFinite(ev.ScrollY))
            {
                droppedEvents++;
                return new(false, "invalid-coordinates");
            }

            totalEvents++;
            touchpadEvents++;

            switch (ev.Kind.ToLowerInvariant())
            {
                case "move":
                {
                    double speed = Math.Sqrt(ev.Dx * ev.Dx + ev.Dy * ev.Dy);
                    double accel = config.TouchpadAcceleration ? 1.0 + Math.Min(2.2, speed * 0.045) : 1.0;
                    double scaledX = ev.Dx * config.TouchpadSensitivity * accel + remX;
                    double scaledY = ev.Dy * config.TouchpadSensitivity * accel + remY;
                    int ix = (int)Math.Truncate(scaledX);
                    int iy = (int)Math.Truncate(scaledY);
                    remX = scaledX - ix;
                    remY = scaledY - iy;
                    if (ix != 0 || iy != 0) sink.MoveRelative(ix, iy);
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true, MappedX: ix, MappedY: iy);
                }
                case "tap":
                case "left_click":
                    sink.LeftDown();
                    sink.LeftUp();
                    leftButtonHeld = false;
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true);
                case "double_tap":
                    sink.LeftDown(); sink.LeftUp();
                    sink.LeftDown(); sink.LeftUp();
                    leftButtonHeld = false;
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true);
                case "right_click":
                    sink.RightClick();
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true);
                case "middle_click":
                    sink.MiddleClick();
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true);
                case "drag_start":
                case "left_down":
                    if (!leftButtonHeld) { sink.LeftDown(); leftButtonHeld = true; }
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true);
                case "drag_end":
                case "left_up":
                    if (leftButtonHeld) { sink.LeftUp(); leftButtonHeld = false; }
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true);
                case "scroll":
                {
                    int sy = (int)Math.Round(ev.ScrollY * 120.0 * config.ScrollSensitivity);
                    int sx = (int)Math.Round(ev.ScrollX * 120.0 * config.ScrollSensitivity);
                    if (sy != 0 || sx != 0) sink.Scroll(sy, sx);
                    RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                    return new(true, MappedX: sx, MappedY: sy);
                }
                default:
                    droppedEvents++;
                    return new(false, "unsupported-touchpad-kind");
            }
        }
    }

    public InputDispatchResult ProcessDrawingPad(DrawingPadEvent ev, bool deviceAuthorized)
    {
        long startTicks = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (emergencyStopped || !config.Enabled || !config.AllowDrawingPad || !deviceAuthorized)
            {
                rejectedUnauthorized++;
                return new(false, emergencyStopped ? "emergency-stopped" : "drawing-pad-not-authorized");
            }

            if (!double.IsFinite(ev.NormX) || !double.IsFinite(ev.NormY))
            {
                droppedEvents++;
                return new(false, "invalid-coordinates");
            }

            // Optional palm rejection: ignore large-contact touches when not explicitly continuing an active stroke.
            if (config.PalmRejection && ev.ContactRadius > config.PalmContactRadiusThreshold && !ev.HasRealPressure)
            {
                palmRejectedEvents++;
                return new(false, "palm-rejected", PalmRejected: true);
            }

            string kind = ev.Kind.ToLowerInvariant();
            if (kind == "stroke_end")
            {
                ReleaseHeldButtonsLocked();
                drawFilter.Reset();
                totalEvents++;
                drawingEvents++;
                RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
                return new(true);
            }

            var monitors = sink.GetMonitors();
            var target = monitors[Math.Clamp(config.TargetMonitorIndex, 0, monitors.Count - 1)];
            var virtualDesktop = ComputeVirtualBounds(monitors);

            // 1. Calibration sub-rect normalization
            double u = Math.Clamp((ev.NormX - config.CalMinX) / Math.Max(0.05, config.CalMaxX - config.CalMinX), 0.0, 1.0);
            double v = Math.Clamp((ev.NormY - config.CalMinY) / Math.Max(0.05, config.CalMaxY - config.CalMinY), 0.0, 1.0);

            // 2. Portrait/landscape orientation + aspect-ratio mapping
            double padAspect = ev.DeviceAspect > 0.1
                ? (ev.Portrait ? (1.0 / ev.DeviceAspect) : ev.DeviceAspect)
                : (double)target.Width / Math.Max(1, target.Height);
            double monAspect = (double)target.Width / Math.Max(1, target.Height);

            double mapWidth = target.Width;
            double mapHeight = target.Height;
            double offsetX = target.Left;
            double offsetY = target.Top;

            if (config.DrawingAspectMode == "fit" && Math.Abs(padAspect - monAspect) > 0.01)
            {
                if (padAspect > monAspect)
                {
                    mapHeight = target.Width / padAspect;
                    offsetY = target.Top + (target.Height - mapHeight) * 0.5;
                }
                else
                {
                    mapWidth = target.Height * padAspect;
                    offsetX = target.Left + (target.Width - mapWidth) * 0.5;
                }
            }
            else if (config.DrawingAspectMode == "fill" && Math.Abs(padAspect - monAspect) > 0.01)
            {
                if (padAspect > monAspect)
                {
                    double visibleU = monAspect / padAspect;
                    u = Math.Clamp((u - (1.0 - visibleU) * 0.5) / visibleU, 0.0, 1.0);
                }
                else
                {
                    double visibleV = padAspect / monAspect;
                    v = Math.Clamp((v - (1.0 - visibleV) * 0.5) / visibleV, 0.0, 1.0);
                }
            }

            double rawX = offsetX + u * (mapWidth - 1);
            double rawY = offsetY + v * (mapHeight - 1);

            if (kind == "stroke_start")
                drawFilter.Reset();

            var (fx, fy) = drawFilter.Filter(rawX, rawY, clock.Elapsed.TotalSeconds);
            if (hasLastDrawSample)
            {
                double rawStep = double.Hypot(rawX - lastRawX, rawY - lastRawY);
                double filtDiff = double.Hypot(fx - rawX, fy - rawY);
                if (rawStep < 6.0) { jitterSum += filtDiff; jitterSamples++; }
            }
            lastRawX = rawX; lastRawY = rawY; hasLastDrawSample = true;

            int screenX = (int)Math.Clamp(Math.Round(fx), target.Left, target.Left + target.Width - 1);
            int screenY = (int)Math.Clamp(Math.Round(fy), target.Top, target.Top + target.Height - 1);

            totalEvents++;
            drawingEvents++;

            switch (kind)
            {
                case "hover":
                    sink.MoveAbsolute(screenX, screenY, virtualDesktop);
                    break;
                case "stroke_start":
                    sink.MoveAbsolute(screenX, screenY, virtualDesktop);
                    if (!leftButtonHeld) { sink.LeftDown(); leftButtonHeld = true; }
                    strokeActive = true;
                    break;
                case "stroke_move":
                    sink.MoveAbsolute(screenX, screenY, virtualDesktop);
                    if (strokeActive && !leftButtonHeld) { sink.LeftDown(); leftButtonHeld = true; }
                    break;
                default:
                    droppedEvents++;
                    return new(false, "unsupported-drawing-kind");
            }

            RecordLatencyLocked(startTicks, ev.ClientTimestampMs);
            return new(true, MappedX: screenX, MappedY: screenY);
        }
    }

    public RemoteInputMetrics GetMetrics()
    {
        lock (gate)
        {
            double avgLat = 0, p95Lat = 0;
            if (latencyCount > 0)
            {
                var sample = new double[latencyCount];
                Array.Copy(latencyRing, sample, latencyCount);
                Array.Sort(sample);
                avgLat = sample.Average();
                p95Lat = sample[Math.Min(sample.Length - 1, (int)Math.Ceiling(sample.Length * 0.95) - 1)];
            }
            double avgJitter = jitterSamples > 0 ? jitterSum / jitterSamples : 0;
            return new RemoteInputMetrics(
                Enabled: config.Enabled,
                AllowTouchpad: config.AllowTouchpad,
                AllowDrawingPad: config.AllowDrawingPad,
                EmergencyStopped: emergencyStopped,
                LeftButtonHeld: leftButtonHeld,
                TotalEvents: totalEvents,
                TouchpadEvents: touchpadEvents,
                DrawingEvents: drawingEvents,
                DroppedEvents: droppedEvents,
                RejectedUnauthorized: rejectedUnauthorized,
                PalmRejectedEvents: palmRejectedEvents,
                AvgLatencyMs: Math.Round(avgLat, 3),
                P95LatencyMs: Math.Round(p95Lat, 3),
                AvgJitterPx: Math.Round(avgJitter, 3),
                TargetMonitorIndex: config.TargetMonitorIndex,
                DrawingAspectMode: config.DrawingAspectMode);
        }
    }

    private void RecordLatencyLocked(long startTicks, long? clientTimestampMs)
    {
        double procMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        double metricMs = procMs;
        if (clientTimestampMs is long cts)
        {
            double e2e = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - cts;
            if (e2e is >= 0 and <= 5000) metricMs = e2e;
        }
        latencyRing[latencyIndex] = metricMs;
        latencyIndex = (latencyIndex + 1) % latencyRing.Length;
        if (latencyCount < latencyRing.Length) latencyCount++;
    }

    private static MonitorDescriptor ComputeVirtualBounds(IReadOnlyList<MonitorDescriptor> monitors)
    {
        int left = monitors.Min(m => m.Left);
        int top = monitors.Min(m => m.Top);
        int right = monitors.Max(m => m.Left + m.Width);
        int bottom = monitors.Max(m => m.Top + m.Height);
        return new MonitorDescriptor(0, "VirtualDesktop", left, top, Math.Max(1, right - left), Math.Max(1, bottom - top), true);
    }
}
