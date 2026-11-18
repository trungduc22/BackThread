using System.Text.Json.Serialization;

namespace BackThread.Media;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlaybackMode
{
    Auto,
    DirectInternet,
    BufferedUsb,
    LivePcm,
    RemoteControl
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlaybackOwner
{
    WindowsOwned,
    TransferPending,
    iPhoneOwned,
    TransferFailed,
    ReturningToWindows
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HandoffStage
{
    Idle,
    Requested,
    Validating,
    Connecting,
    Buffering,
    Ready,
    Playing,
    Confirmed,
    Failed,
    Cancelled,
    Returned
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioFocusPolicy
{
    Auto,
    IndependentOnly,
    LivePcmOnly,
    IndependentPrioritized,
    LivePcmPrioritized,
    KeepVideoMuteAudio,
    DuckVideo
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioOutputMode
{
    PcOnly,
    PhoneOnly,
    Both
}

public sealed record AudioControlState(
    AudioOutputMode Mode,
    float PcVolumeScalar,
    bool PcMuted,
    bool PcMuteSavedByBackThread,
    bool PcEndpointAvailable,
    string ActiveVolumeLayer,
    float PhoneAppGain,
    float PhonePlayerVolume,
    string CaptureSignalStatus,
    string? DeviceName = null,
    string? Warning = null);

public sealed record MediaCapabilities(
    bool SupportsDirectInternet,
    bool SupportsBufferedUsb,
    bool SupportsLivePcm,
    bool SupportsRemoteControl,
    bool SupportsVideo,
    bool SupportsSeek,
    bool IsLiveStream,
    bool RequiresInteractiveAuth,
    bool DrmProtected,
    bool TransfersPlaybackToPhone,
    bool SurvivesUsbDisconnect,
    bool RequiresDeviceInternet,
    bool BackgroundSupported,
    PlaybackMode RecommendedMode,
    string Provider,
    string Reason,
    string[] Limitations)
{
    public static MediaCapabilities DirectUrl(string mimeType, bool supportsVideo, bool isLive, bool deviceHasInternet, string reason) => new(
        SupportsDirectInternet: deviceHasInternet,
        SupportsBufferedUsb: false,
        SupportsLivePcm: true,
        SupportsRemoteControl: false,
        SupportsVideo: supportsVideo,
        SupportsSeek: !isLive,
        IsLiveStream: isLive,
        RequiresInteractiveAuth: false,
        DrmProtected: false,
        TransfersPlaybackToPhone: deviceHasInternet,
        SurvivesUsbDisconnect: true,
        RequiresDeviceInternet: true,
        BackgroundSupported: !supportsVideo,
        RecommendedMode: deviceHasInternet ? PlaybackMode.DirectInternet : PlaybackMode.LivePcm,
        Provider: "direct",
        Reason: reason,
        Limitations: deviceHasInternet ? [] : ["Device internet connectivity must be enabled for independent Mode A playback"]);

    public static MediaCapabilities LocalFile(string mimeType, bool supportsVideo) => new(
        SupportsDirectInternet: false,
        SupportsBufferedUsb: true,
        SupportsLivePcm: true,
        SupportsRemoteControl: false,
        SupportsVideo: supportsVideo,
        SupportsSeek: true,
        IsLiveStream: false,
        RequiresInteractiveAuth: false,
        DrmProtected: false,
        TransfersPlaybackToPhone: true,
        SurvivesUsbDisconnect: false,
        RequiresDeviceInternet: false,
        BackgroundSupported: !supportsVideo,
        RecommendedMode: PlaybackMode.BufferedUsb,
        Provider: "buffered-usb",
        Reason: $"Local encoded media ({mimeType}) streamed over USB buffer to iOS AVPlayer",
        Limitations: ["Requires active USB connection while buffering new chunks"]);
}

public sealed record MediaSubmissionRequest(
    string? Url = null,
    string? FilePath = null,
    PlaybackMode Mode = PlaybackMode.Auto,
    AudioFocusPolicy FocusPolicy = AudioFocusPolicy.Auto,
    string? Title = null,
    string? Artist = null,
    string? QualityCap = null,
    bool? KeepScreenAwake = null,
    bool AutoPauseWindowsOnConfirm = true,
    double StartPositionSeconds = 0);

public sealed record MediaSessionState(
    string SessionId,
    long Generation,
    PlaybackMode Mode,
    PlaybackOwner Owner,
    HandoffStage Stage,
    AudioFocusPolicy FocusPolicy,
    string Provider,
    string Title,
    string Artist,
    string Album,
    string? Url,
    string? LocalFilePath,
    string? StreamId,
    string? MimeType,
    double PositionSeconds,
    double DurationSeconds,
    double BufferedSeconds,
    bool IsPlaying,
    bool ReadyForDisplay,
    int VideoWidth,
    int VideoHeight,
    string QualityCap,
    bool KeepScreenAwake,
    bool UsbConnected,
    bool WindowsPausedByHandoff,
    bool ShouldStreamLivePcm,
    float IndependentVolume,
    string? WindowsSource,
    string? Error,
    MediaCapabilities? Capabilities,
    DateTimeOffset UpdatedUtc)
{
    public static MediaSessionState Initial(bool usbConnected = false) => new(
        SessionId: "",
        Generation: 0,
        Mode: PlaybackMode.LivePcm,
        Owner: PlaybackOwner.WindowsOwned,
        Stage: HandoffStage.Idle,
        FocusPolicy: AudioFocusPolicy.Auto,
        Provider: "windows-smtc",
        Title: "",
        Artist: "",
        Album: "",
        Url: null,
        LocalFilePath: null,
        StreamId: null,
        MimeType: null,
        PositionSeconds: 0,
        DurationSeconds: 0,
        BufferedSeconds: 0,
        IsPlaying: false,
        ReadyForDisplay: false,
        VideoWidth: 0,
        VideoHeight: 0,
        QualityCap: "auto",
        KeepScreenAwake: false,
        UsbConnected: usbConnected,
        WindowsPausedByHandoff: false,
        ShouldStreamLivePcm: true,
        IndependentVolume: 1f,
        WindowsSource: null,
        Error: null,
        Capabilities: null,
        UpdatedUtc: DateTimeOffset.UtcNow);
}

public sealed record HandoffStatusUpdate(
    string SessionId,
    long Generation,
    string Stage,
    double PositionSeconds,
    double DurationSeconds,
    double BufferedSeconds,
    bool IsPlaying,
    bool ReadyForDisplay = false,
    int VideoWidth = 0,
    int VideoHeight = 0,
    string? Error = null,
    string? Reason = null,
    bool FallbackSuggested = false);

public sealed record AgentMediaSync(
    string? SessionId,
    long Generation,
    string? Mode,
    bool IndependentActive,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    double BufferedSeconds,
    string? Url,
    string? Title);
