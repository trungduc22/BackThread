using System.Runtime.InteropServices;

namespace BackThread.Audio;

/// <summary>Scoped multimedia timer request; released on every stream shutdown. No persistent OS setting.</summary>
internal sealed class AudioTimerScope : IDisposable
{
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint timeEndPeriod(uint milliseconds);
    private readonly bool active = OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0;
    public void Dispose() { if (active && OperatingSystem.IsWindows()) timeEndPeriod(1); }
}
