using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BackThread.Daemon;

/// <summary>
/// Minimal hidden Win32 window + message loop on a dedicated STA thread. It gives
/// <c>NativeTrayService</c> an owner HWND (tray callbacks, TaskbarCreated recovery) without loading
/// WinForms, WPF, or WinUI into the background daemon.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayMessageWindow : IDisposable
{
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_APP = 0x8000;
    private const uint WM_APP_SHUTDOWN = WM_APP + 0x51;
    private const int SW_HIDE = 0;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private readonly string className = "BackThreadDaemonTray_" + Environment.ProcessId;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WndProc? wndProc;
    private Thread? thread;
    private Action? cleanupOnWindowThread;
    private nint hwnd;
    private bool disposed;

    public nint Handle => hwnd;
    public Task Completion => exited.Task;

    /// <summary>
    /// Starts the window thread. <paramref name="initializeOnWindowThread"/> runs on the STA thread once the HWND exists
    /// (create the tray there) and may return a cleanup action that also runs on that thread before the window is destroyed.
    /// </summary>
    public Task<nint> StartAsync(Func<nint, Action?> initializeOnWindowThread)
    {
        ArgumentNullException.ThrowIfNull(initializeOnWindowThread);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (thread is not null)
            return ready.Task;

        thread = new Thread(() => Run(initializeOnWindowThread))
        {
            IsBackground = true,
            Name = "BackThread.TrayMessageWindow"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    /// <summary>Hides the owner window again (NativeTrayService restores its owner on left-click).</summary>
    public void Hide()
    {
        if (hwnd != nint.Zero)
            ShowWindow(hwnd, SW_HIDE);
    }

    private void Run(Func<nint, Action?> initialize)
    {
        try
        {
            wndProc = WindowProc;
            nint hInstance = GetModuleHandleW(null);
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = hInstance,
                lpszClassName = className
            };
            if (RegisterClassExW(ref wc) == 0)
                throw new InvalidOperationException($"RegisterClassExW failed (Win32Error={Marshal.GetLastWin32Error()})");

            // A real (hidden, zero-size, tool) top-level window: message-only windows do not receive the
            // broadcast TaskbarCreated message needed to restore the tray icon after Explorer restarts.
            hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, className, "BackThread Daemon", WS_POPUP,
                0, 0, 0, 0, nint.Zero, nint.Zero, hInstance, nint.Zero);
            if (hwnd == nint.Zero)
                throw new InvalidOperationException($"CreateWindowExW failed (Win32Error={Marshal.GetLastWin32Error()})");

            cleanupOnWindowThread = initialize(hwnd);
            ready.TrySetResult(hwnd);

            while (GetMessageW(out var msg, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
        finally
        {
            UnregisterClassW(className, GetModuleHandleW(null));
            exited.TrySetResult();
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_APP_SHUTDOWN:
            case WM_CLOSE:
                try { cleanupOnWindowThread?.Invoke(); } catch { /* tray removal is best effort */ }
                cleanupOnWindowThread = null;
                DestroyWindow(hWnd);
                return nint.Zero;
            case WM_DESTROY:
                hwnd = nint.Zero;
                PostQuitMessage(0);
                return nint.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        nint target = hwnd;
        if (target != nint.Zero && PostMessageW(target, WM_APP_SHUTDOWN, nint.Zero, nint.Zero))
            exited.Task.Wait(TimeSpan.FromSeconds(2));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int cmd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);
}
