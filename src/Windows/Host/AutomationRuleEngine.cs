using System.Text.Json;

namespace BackThread.Host;

public sealed record AutomationRuleConfig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    /// <summary>cpu-above | ram-above | media-playing | battery-below | route-wifi</summary>
    public string When { get; init; } = "cpu-above";
    public double Threshold { get; init; } = 85;
    public int ForSec { get; init; }
    public int CooldownMin { get; init; } = 5;
    /// <summary>Optional comma-separated days: mon,tue,wed,thu,fri,sat,sun</summary>
    public string? Days { get; init; }
    /// <summary>Optional HH:mm-HH:mm local time window (supports overnight windows like 22:00-07:00).</summary>
    public string? During { get; init; }
    /// <summary>goto | notify | ambient | offload.suggest</summary>
    public string Then { get; init; } = "notify";
    public string? TargetPage { get; init; }
    public string? Text { get; init; }
    public int Priority { get; init; } = 1;
    public bool WhileActive { get; init; } = true;
}

public sealed record RuleEvaluationInput(
    double? CpuUsagePercent,
    double? MemoryUsedPercent,
    bool MediaPlaying,
    int? PhoneBatteryPercent,
    bool OnWifiRoute);

public sealed record RuleActionCommand(
    string RuleId,
    string RuleName,
    string ActionKind, // "goto" | "notify" | "ambient" | "offload.suggest" | "undo"
    string? TargetPage,
    string Detail,
    int Priority,
    bool HoldWhileActive);

/// <summary>
/// Event-evaluated automation rule engine with sustain hysteresis, release hysteresis (4s),
/// cooldown gating, day/time scheduling, and whileActive auto-undo. Zero dedicated background thread required.
/// </summary>
public sealed class AutomationRuleEngine
{
    public const int DefaultCpuSustainSec = 15;
    public const int ReleaseHysteresisSec = 4;

    private sealed class RuleState(AutomationRuleConfig rule)
    {
        public AutomationRuleConfig Rule { get; } = rule;
        public bool Active;
        public long TrueSinceMs;
        public long FalseSinceMs;
    }

    private readonly Lock gate = new();
    private readonly Dictionary<string, RuleState> states = [];
    private readonly Dictionary<string, long> lastFired = [];
    private long pausedUntilUnixMs;
    private string[] activeRuleIds = [];

    public IReadOnlyList<string> ActiveRuleIds => activeRuleIds;

    public long? PausedUntilUnixMs
    {
        get
        {
            long p = Interlocked.Read(ref pausedUntilUnixMs);
            return p > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ? p : null;
        }
    }

    public void Pause(int minutes, long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long until = minutes <= 0 ? 0 : now + Math.Min(minutes, 24 * 60) * 60_000L;
        Interlocked.Exchange(ref pausedUntilUnixMs, until);
    }

    public IReadOnlyList<RuleActionCommand> Evaluate(
        IReadOnlyList<AutomationRuleConfig> rules,
        RuleEvaluationInput input,
        DateTime? localClock = null,
        long? nowTickMs = null)
    {
        long now = nowTickMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DateTime clock = localClock ?? DateTime.Now;
        var emitted = new List<RuleActionCommand>();

        lock (gate)
        {
            bool paused = Interlocked.Read(ref pausedUntilUnixMs) > now;
            var enabledMap = new Dictionary<string, AutomationRuleConfig>();
            foreach (var r in rules)
            {
                if (!r.Enabled || string.IsNullOrWhiteSpace(r.Id)) continue;
                enabledMap[JsonSerializer.Serialize(r)] = r;
            }

            foreach (var key in states.Keys.Where(k => paused || !enabledMap.ContainsKey(k)).ToList())
            {
                var st = states[key];
                if (st.Active && st.Rule.WhileActive)
                {
                    emitted.Add(new RuleActionCommand(st.Rule.Id, st.Rule.Name, "undo", st.Rule.TargetPage, "Rule ended or paused", st.Rule.Priority, false));
                    lastFired.Remove(st.Rule.Id);
                }
                states.Remove(key);
            }

            if (paused)
            {
                activeRuleIds = [];
                return emitted;
            }

            var active = new List<string>();
            foreach (var (key, rule) in enabledMap)
            {
                if (!states.TryGetValue(key, out var st))
                    states[key] = st = new RuleState(rule);

                var (holds, detail) = IsScheduled(rule, clock) ? EvaluateCondition(rule, input) : (false, "");
                if (holds)
                {
                    st.FalseSinceMs = 0;
                    if (st.TrueSinceMs == 0) st.TrueSinceMs = now;
                }
                else
                {
                    st.TrueSinceMs = 0;
                    if (st.FalseSinceMs == 0) st.FalseSinceMs = now;
                }

                int sustainSec = rule.ForSec > 0 ? rule.ForSec : (rule.When == "cpu-above" ? DefaultCpuSustainSec : 0);
                bool sustained = holds && (now - st.TrueSinceMs) >= sustainSec * 1000L;
                bool cooled = !lastFired.TryGetValue(rule.Id, out var prev) || (now - prev) >= rule.CooldownMin * 60_000L;

                if (sustained && !st.Active && cooled)
                {
                    st.Active = true;
                    lastFired[rule.Id] = now;
                    string text = string.IsNullOrWhiteSpace(rule.Text) ? detail : rule.Text.Replace("{detail}", detail, StringComparison.Ordinal);
                    emitted.Add(new RuleActionCommand(rule.Id, string.IsNullOrWhiteSpace(rule.Name) ? rule.Id : rule.Name, rule.Then, rule.TargetPage, text, rule.Priority, rule.WhileActive));
                }
                else if (!holds && st.Active && (now - st.FalseSinceMs) >= ReleaseHysteresisSec * 1000L)
                {
                    st.Active = false;
                    if (rule.WhileActive)
                        emitted.Add(new RuleActionCommand(rule.Id, string.IsNullOrWhiteSpace(rule.Name) ? rule.Id : rule.Name, "undo", rule.TargetPage, "Condition cleared", rule.Priority, false));
                }

                if (st.Active) active.Add(rule.Id);
            }

            activeRuleIds = [.. active];
            return emitted;
        }
    }

    private static (bool Holds, string Detail) EvaluateCondition(AutomationRuleConfig rule, RuleEvaluationInput input) => rule.When switch
    {
        "cpu-above" => input.CpuUsagePercent is double cpu && cpu >= rule.Threshold
            ? (true, $"PC CPU at {cpu:F0}% (threshold {rule.Threshold:F0}%)")
            : (false, ""),
        "ram-above" => input.MemoryUsedPercent is double ram && ram >= rule.Threshold
            ? (true, $"PC RAM at {ram:F0}% (threshold {rule.Threshold:F0}%)")
            : (false, ""),
        "media-playing" => input.MediaPlaying ? (true, "Media playback started on PC") : (false, ""),
        "battery-below" => input.PhoneBatteryPercent is int bat && bat <= rule.Threshold
            ? (true, $"iPhone battery at {bat}%")
            : (false, ""),
        "route-wifi" => input.OnWifiRoute ? (true, "Connected over Wi-Fi/Tailscale") : (false, ""),
        _ => (false, "")
    };

    private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    public static bool IsScheduled(AutomationRuleConfig rule, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(rule.Days))
        {
            string today = DayNames[(int)now.DayOfWeek];
            var allowed = rule.Days.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!allowed.Contains(today, StringComparer.OrdinalIgnoreCase)) return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.During))
        {
            var parts = rule.During.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TimeSpan.TryParse(parts[0], out var start) && TimeSpan.TryParse(parts[1], out var end))
            {
                var tod = now.TimeOfDay;
                if (start <= end) { if (tod < start || tod > end) return false; }
                else { if (tod < start && tod > end) return false; } // overnight window
            }
        }
        return true;
    }
}
