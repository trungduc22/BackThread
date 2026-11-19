using System.Diagnostics;
using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BackThread.Audio;

public sealed record PerAppAudioSession(
    int ProcessId,
    string SessionId,
    string ProcessName,
    string DisplayName,
    float VolumeScalar,
    bool Muted,
    string State);

public interface IPerAppAudioMixer
{
    IReadOnlyList<PerAppAudioSession> ListSessions();
    PerAppAudioSession? SetSessionVolume(int processId, float? volumeScalar, bool? muted);
}

/// <summary>
/// Per-application Windows Core Audio mixer built on IAudioSessionManager2 via NAudio.
/// Demand-driven: only enumerates sessions when requested by the iPhone Media tab or PC App.
/// </summary>
public sealed class WindowsPerAppAudioMixer : IPerAppAudioMixer
{
    public IReadOnlyList<PerAppAudioSession> ListSessions()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        return ListWindowsSessions();
    }

    public PerAppAudioSession? SetSessionVolume(int processId, float? volumeScalar, bool? muted)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
            return null;
        return SetWindowsSessionVolume(processId, volumeScalar, muted);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<PerAppAudioSession> ListWindowsSessions()
    {
        var list = new List<PerAppAudioSession>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            var byPid = new Dictionary<int, PerAppAudioSession>();

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
                int pid = (int)s.GetProcessID;
                if (pid <= 0) continue;

                string procName = TryProcessName(pid);
                string display = string.IsNullOrWhiteSpace(s.DisplayName) || s.DisplayName.StartsWith("@", StringComparison.Ordinal)
                    ? procName
                    : s.DisplayName.Trim();

                var item = new PerAppAudioSession(
                    ProcessId: pid,
                    SessionId: s.GetSessionIdentifier ?? pid.ToString(),
                    ProcessName: procName,
                    DisplayName: display,
                    VolumeScalar: Math.Clamp(s.SimpleAudioVolume.Volume, 0f, 1f),
                    Muted: s.SimpleAudioVolume.Mute,
                    State: s.State == AudioSessionState.AudioSessionStateActive ? "Active" : "Inactive");

                if (!byPid.TryGetValue(pid, out var existing) || item.State == "Active")
                    byPid[pid] = item;
            }

            list.AddRange(byPid.Values.OrderByDescending(x => x.State == "Active").ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            // Device unavailable or headless session without audio endpoint.
        }
        return list;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PerAppAudioSession? SetWindowsSessionVolume(int processId, float? volumeScalar, bool? muted)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            PerAppAudioSession? updated = null;

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if ((int)s.GetProcessID != processId) continue;

                if (volumeScalar is float v && float.IsFinite(v))
                {
                    float clamped = Math.Clamp(v, 0f, 1f);
                    s.SimpleAudioVolume.Volume = clamped;
                    if (clamped > 0f && muted is null)
                        s.SimpleAudioVolume.Mute = false;
                }
                if (muted is bool m)
                    s.SimpleAudioVolume.Mute = m;

                string procName = TryProcessName(processId);
                string display = string.IsNullOrWhiteSpace(s.DisplayName) || s.DisplayName.StartsWith("@", StringComparison.Ordinal)
                    ? procName
                    : s.DisplayName.Trim();

                updated = new PerAppAudioSession(
                    ProcessId: processId,
                    SessionId: s.GetSessionIdentifier ?? processId.ToString(),
                    ProcessName: procName,
                    DisplayName: display,
                    VolumeScalar: Math.Clamp(s.SimpleAudioVolume.Volume, 0f, 1f),
                    Muted: s.SimpleAudioVolume.Mute,
                    State: s.State == AudioSessionState.AudioSessionStateActive ? "Active" : "Inactive");
            }
            return updated;
        }
        catch
        {
            return null;
        }
    }

    private static string TryProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return $"PID {pid}";
        }
    }
}
