using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A game-mode setting changed through the one validated path votes and the
/// admin menu share (plan_bots_teams_tf2 D.2, D.3): only a setting of the mode
/// playing now, only to a value its row allows, and when its row says it
/// applies. A <see cref="SettingApplies.Now"/> write goes at once; a
/// <see cref="SettingApplies.NextRound"/> one waits for the next round's setup
/// (<see cref="GameModes"/>), a <see cref="SettingApplies.NextMap"/> one for
/// the next level. The convars are the store: nothing else is written.
/// </summary>
public sealed class ModeSettingWrites(GameModeCatalog catalog, IEngineConsole console, ILog log)
{
    private readonly Dictionary<string, (SettingApplies Applies, string Value)> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The writes waiting for a round or a level, by convar.</summary>
    public IReadOnlyDictionary<string, (SettingApplies Applies, string Value)> Pending => _pending;

    /// <summary>The value a setting's convar holds now; null when the game declares no such convar.</summary>
    public string? Current(string key) => console.FindConVarValue(key);

    /// <summary>Changes the active mode's setting <paramref name="key"/> to <paramref name="value"/>; null when done or queued, else why not.</summary>
    public string? Write(string key, string value)
    {
        if (catalog.ActiveSetting(key) is not { } setting)
            return $"'{catalog.Active?.Id ?? "no mode"}' has no setting {key}";
        if (!Allows(setting, value))
            return $"{value} is not a value {key} takes";
        if (Current(key) is null)
            return $"this game has no {key}";

        if (setting.Applies == SettingApplies.Now)
        {
            _pending.Remove(key);
            Run(key, value);
            return null;
        }
        _pending[key] = (setting.Applies, value);
        string when = setting.Applies == SettingApplies.NextRound ? "the next round" : "the next map";
        log.Msg($"[SourceSharp/Settings] {key} {value} waits for {when}\n");
        return null;
    }

    /// <summary>A round is being set up: the writes that wait for one go now.</summary>
    public void ApplyRound() => Apply(SettingApplies.NextRound);

    /// <summary>A level is starting: every waiting write goes now.</summary>
    public void ApplyLevel() => Apply(SettingApplies.NextMap);

    private void Apply(SettingApplies upTo)
    {
        foreach (var (key, (applies, value)) in _pending.Where(p => p.Value.Applies <= upTo).ToList())
        {
            _pending.Remove(key);
            Run(key, value);
        }
    }

    private void Run(string key, string value)
    {
        log.Msg($"[SourceSharp/Settings] {key} = {value}\n");
        console.ExecuteServerCommand($"{key} {value}\n");
    }

    /// <summary>
    /// Whether a row takes <paramref name="value"/>: one of its choices when it
    /// names them, else a number within its range (a bool is 0 or 1).
    /// </summary>
    public static bool Allows(SettingSpec setting, string value)
    {
        if (setting.Choices.Count > 0)
            return setting.Choices.Any(c => SameValue(c.Value, value));
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            return false;
        if (setting.Kind == SettingKind.Int && number != MathF.Floor(number))
            return false;
        return setting.Max <= setting.Min || (number >= setting.Min && number <= setting.Max);
    }

    /// <summary>Convars read back as they were written: "20" and "20.0" are one value.</summary>
    public static bool SameValue(string a, string b)
        => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
           && float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ? x == y : a == b;
}

/// <summary><c>ss_setting &lt;convar&gt; &lt;value&gt;</c>: a passing vote's command, and the console's way to the same path.</summary>
[ConCommand(Name, Help = "ss_setting <convar> <value>: change a setting of the mode playing, when its row says it applies")]
public sealed class ModeSettingCommand(ILog log, IServiceLocator services)
{
    public const string Name = "ss_setting";

    public void Execute(string key = "", string value = "")
    {
        // Resolved at execute: the command is bound at convar registration, before the catalog.
        if (services.Resolve<ModeSettingWrites>() is not { } writes)
            return;
        if (key.Length == 0 || value.Length == 0)
        {
            log.Msg($"[SourceSharp/Settings] usage: {Name} <convar> <value>\n");
            return;
        }
        if (writes.Write(key, value) is { } why)
            log.Warning($"[SourceSharp/Settings] {Name} {key} {value} refused: {why}\n");
    }
}
