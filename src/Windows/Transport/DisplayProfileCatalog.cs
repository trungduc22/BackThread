using System.Text.Json.Serialization;

namespace BackThread.Transport;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayPanelKind
{
    Unknown,
    Lcd,
    Oled
}

public sealed record DisplayProfile(
    string ModelIdentifier,
    string MarketingName,
    int NativeWidth,
    int NativeHeight,
    int LogicalWidth,
    int LogicalHeight,
    double Scale,
    DisplayPanelKind PanelKind,
    bool IsOverride = false)
{
    public double AspectRatio => Math.Round((double)Math.Max(NativeWidth, NativeHeight) / Math.Max(1, Math.Min(NativeWidth, NativeHeight)), 4);
    /// <summary>OLED burn-in protections are ONLY recommended on verified OLED panels (or explicit user override).</summary>
    public bool RecommendOledGuard => PanelKind == DisplayPanelKind.Oled;
}

public sealed record DisplayUserOverride(
    DisplayPanelKind? PanelKindOverride = null,
    bool? EnableOledTrueBlack = null,
    double? OledBrightnessScale = null,
    bool? EnablePixelShift = null);

/// <summary>
/// Deterministic Apple hardware display catalog. Unknown models default to DisplayPanelKind.Unknown (never assuming OLED on old/LCD hardware)
/// and always respect user overrides (e.g., aftermarket replacement displays).
/// </summary>
public static class DisplayProfileCatalog
{
    private static readonly Dictionary<string, DisplayProfile> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // Classic LCD iPhones (including iPhone 6 iOS 12.5.8 target)
        ["iPhone7,2"] = new("iPhone7,2", "iPhone 6", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone7,1"] = new("iPhone7,1", "iPhone 6 Plus", 1080, 1920, 414, 736, 3.0, DisplayPanelKind.Lcd),
        ["iPhone8,1"] = new("iPhone8,1", "iPhone 6s", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone8,2"] = new("iPhone8,2", "iPhone 6s Plus", 1080, 1920, 414, 736, 3.0, DisplayPanelKind.Lcd),
        ["iPhone8,4"] = new("iPhone8,4", "iPhone SE (1st gen)", 640, 1136, 320, 568, 2.0, DisplayPanelKind.Lcd),
        ["iPhone9,1"] = new("iPhone9,1", "iPhone 7", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone9,3"] = new("iPhone9,3", "iPhone 7", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone10,1"] = new("iPhone10,1", "iPhone 8", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone10,4"] = new("iPhone10,4", "iPhone 8", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone11,8"] = new("iPhone11,8", "iPhone XR", 828, 1792, 414, 896, 2.0, DisplayPanelKind.Lcd),
        ["iPhone12,1"] = new("iPhone12,1", "iPhone 11", 828, 1792, 414, 896, 2.0, DisplayPanelKind.Lcd),
        ["iPhone12,8"] = new("iPhone12,8", "iPhone SE (2nd gen)", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),
        ["iPhone14,6"] = new("iPhone14,6", "iPhone SE (3rd gen)", 750, 1334, 375, 667, 2.0, DisplayPanelKind.Lcd),

        // OLED iPhones
        ["iPhone10,3"] = new("iPhone10,3", "iPhone X", 1125, 2436, 375, 812, 3.0, DisplayPanelKind.Oled),
        ["iPhone10,6"] = new("iPhone10,6", "iPhone X", 1125, 2436, 375, 812, 3.0, DisplayPanelKind.Oled),
        ["iPhone11,2"] = new("iPhone11,2", "iPhone XS", 1125, 2436, 375, 812, 3.0, DisplayPanelKind.Oled),
        ["iPhone11,6"] = new("iPhone11,6", "iPhone XS Max", 1242, 2688, 414, 896, 3.0, DisplayPanelKind.Oled),
        ["iPhone12,3"] = new("iPhone12,3", "iPhone 11 Pro", 1125, 2436, 375, 812, 3.0, DisplayPanelKind.Oled),
        ["iPhone12,5"] = new("iPhone12,5", "iPhone 11 Pro Max", 1242, 2688, 414, 896, 3.0, DisplayPanelKind.Oled),
        ["iPhone13,1"] = new("iPhone13,1", "iPhone 12 mini", 1080, 2340, 360, 780, 3.0, DisplayPanelKind.Oled),
        ["iPhone13,2"] = new("iPhone13,2", "iPhone 12", 1170, 2532, 390, 844, 3.0, DisplayPanelKind.Oled),
        ["iPhone13,3"] = new("iPhone13,3", "iPhone 12 Pro", 1170, 2532, 390, 844, 3.0, DisplayPanelKind.Oled),
        ["iPhone13,4"] = new("iPhone13,4", "iPhone 12 Pro Max", 1284, 2778, 428, 926, 3.0, DisplayPanelKind.Oled),
        ["iPhone14,2"] = new("iPhone14,2", "iPhone 13 Pro", 1170, 2532, 390, 844, 3.0, DisplayPanelKind.Oled),
        ["iPhone14,3"] = new("iPhone14,3", "iPhone 13 Pro Max", 1284, 2778, 428, 926, 3.0, DisplayPanelKind.Oled),
        ["iPhone14,5"] = new("iPhone14,5", "iPhone 13", 1170, 2532, 390, 844, 3.0, DisplayPanelKind.Oled),
        ["iPhone15,2"] = new("iPhone15,2", "iPhone 14 Pro", 1179, 2556, 393, 852, 3.0, DisplayPanelKind.Oled),
        ["iPhone15,3"] = new("iPhone15,3", "iPhone 14 Pro Max", 1290, 2796, 430, 932, 3.0, DisplayPanelKind.Oled),
        ["iPhone15,4"] = new("iPhone15,4", "iPhone 15", 1179, 2556, 393, 852, 3.0, DisplayPanelKind.Oled),
        ["iPhone16,1"] = new("iPhone16,1", "iPhone 15 Pro", 1179, 2556, 393, 852, 3.0, DisplayPanelKind.Oled),
        ["iPhone16,2"] = new("iPhone16,2", "iPhone 15 Pro Max", 1290, 2796, 430, 932, 3.0, DisplayPanelKind.Oled),

        // Common iPads (LCD unless verified OLED M4 Pro)
        ["iPad7,5"] = new("iPad7,5", "iPad (6th gen)", 1536, 2048, 768, 1024, 2.0, DisplayPanelKind.Lcd),
        ["iPad11,3"] = new("iPad11,3", "iPad Air (3rd gen)", 1668, 2224, 834, 1112, 2.0, DisplayPanelKind.Lcd),
        ["iPad13,4"] = new("iPad13,4", "iPad Pro 11-inch (3rd gen)", 1668, 2388, 834, 1194, 2.0, DisplayPanelKind.Lcd),
        ["iPad16,3"] = new("iPad16,3", "iPad Pro 11-inch (M4)", 1668, 2420, 834, 1210, 2.0, DisplayPanelKind.Oled),
        ["iPad16,5"] = new("iPad16,5", "iPad Pro 13-inch (M4)", 2064, 2752, 1032, 1376, 2.0, DisplayPanelKind.Oled),
    };

    public static DisplayProfile Resolve(string? modelIdentifier, int? reportedNativeWidth = null, int? reportedNativeHeight = null, double? reportedScale = null, DisplayUserOverride? userOverride = null)
    {
        DisplayProfile baseProfile;
        if (!string.IsNullOrWhiteSpace(modelIdentifier) && Known.TryGetValue(modelIdentifier.Trim(), out var found))
        {
            baseProfile = found;
        }
        else
        {
            int nw = Math.Max(320, reportedNativeWidth ?? 750);
            int nh = Math.Max(320, reportedNativeHeight ?? 1334);
            double scale = reportedScale is > 0 and <= 4 ? reportedScale.Value : 2.0;
            baseProfile = new DisplayProfile(
                ModelIdentifier: string.IsNullOrWhiteSpace(modelIdentifier) ? "Unknown" : modelIdentifier.Trim(),
                MarketingName: string.IsNullOrWhiteSpace(modelIdentifier) ? "Generic Device" : modelIdentifier.Trim(),
                NativeWidth: nw,
                NativeHeight: nh,
                LogicalWidth: (int)Math.Round(nw / scale),
                LogicalHeight: (int)Math.Round(nh / scale),
                Scale: scale,
                PanelKind: DisplayPanelKind.Unknown);
        }

        if (userOverride?.PanelKindOverride is DisplayPanelKind overrideKind)
            return baseProfile with { PanelKind = overrideKind, IsOverride = true };
        return baseProfile;
    }
}
