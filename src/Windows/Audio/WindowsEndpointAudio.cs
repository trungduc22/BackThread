using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;

namespace BackThread.Audio;

public sealed record EndpointVolumeSnapshot(
    bool Available,
    string? DeviceName,
    float VolumeScalar,
    bool Muted,
    bool MuteSavedByBackThread,
    string? Error = null);

public interface IMultimediaAudioController : IDisposable
{
    EndpointVolumeSnapshot GetState();
    EndpointVolumeSnapshot SetVolumeScalar(float scalar);
    EndpointVolumeSnapshot EnsureMutedForPhoneOnly();
    EndpointVolumeSnapshot RestoreMuteIfSaved();
}

/// <summary>
/// Controls the active Windows default multimedia render endpoint volume and mute state
/// without changing the default output device. Preserves original mute state and always
/// restores it when leaving PhoneOnly mode, disconnecting, or disposing.
/// </summary>
public sealed class WindowsEndpointAudio : IMultimediaAudioController
{
    private readonly object gate = new();
    private bool muteSaved;
    private bool originalMuteState;

    public EndpointVolumeSnapshot GetState()
    {
        lock (gate)
        {
            if (!OperatingSystem.IsWindows())
                return new(false, null, 1f, false, muteSaved, "Windows CoreAudio requires Windows");
            return ReadWindowsState(muteSaved);
        }
    }

    public EndpointVolumeSnapshot SetVolumeScalar(float scalar)
    {
        float clamped = Math.Clamp(float.IsFinite(scalar) ? scalar : 1f, 0f, 1f);
        lock (gate)
        {
            if (!OperatingSystem.IsWindows())
                return new(false, null, clamped, false, muteSaved, "Windows CoreAudio requires Windows");
            return WriteWindowsVolume(clamped, muteSaved);
        }
    }

    public EndpointVolumeSnapshot EnsureMutedForPhoneOnly()
    {
        lock (gate)
        {
            if (!OperatingSystem.IsWindows())
                return new(false, null, 1f, false, muteSaved, "Windows CoreAudio requires Windows");
            return MuteWindowsIfNeeded();
        }
    }

    public EndpointVolumeSnapshot RestoreMuteIfSaved()
    {
        lock (gate)
        {
            if (!muteSaved)
                return OperatingSystem.IsWindows()
                    ? ReadWindowsState(false)
                    : new(false, null, 1f, false, false, null);

            bool priorMute = originalMuteState;
            muteSaved = false;
            if (!OperatingSystem.IsWindows())
                return new(false, null, 1f, priorMute, false, null);

            return RestoreWindowsMute(priorMute);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EndpointVolumeSnapshot ReadWindowsState(bool savedFlag)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new(
                true,
                device.FriendlyName,
                Math.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar, 0f, 1f),
                device.AudioEndpointVolume.Mute,
                savedFlag);
        }
        catch (Exception ex)
        {
            return new(false, null, 1f, false, savedFlag, ex.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EndpointVolumeSnapshot WriteWindowsVolume(float clamped, bool savedFlag)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = clamped;
            return new(
                true,
                device.FriendlyName,
                Math.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar, 0f, 1f),
                device.AudioEndpointVolume.Mute,
                savedFlag);
        }
        catch (Exception ex)
        {
            return new(false, null, clamped, false, savedFlag, ex.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private EndpointVolumeSnapshot MuteWindowsIfNeeded()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (!muteSaved)
            {
                originalMuteState = device.AudioEndpointVolume.Mute;
                muteSaved = true;
            }
            if (!device.AudioEndpointVolume.Mute)
                device.AudioEndpointVolume.Mute = true;

            return new(
                true,
                device.FriendlyName,
                Math.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar, 0f, 1f),
                device.AudioEndpointVolume.Mute,
                muteSaved);
        }
        catch (Exception ex)
        {
            return new(false, null, 1f, false, muteSaved, ex.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EndpointVolumeSnapshot RestoreWindowsMute(bool priorMute)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = priorMute;
            return new(
                true,
                device.FriendlyName,
                Math.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar, 0f, 1f),
                device.AudioEndpointVolume.Mute,
                false);
        }
        catch (Exception ex)
        {
            return new(false, null, 1f, priorMute, false, ex.Message);
        }
    }

    public void Dispose()
    {
        RestoreMuteIfSaved();
    }
}
