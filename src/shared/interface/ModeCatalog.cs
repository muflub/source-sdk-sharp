namespace SourceSharp.Abi;

/// <summary>What kind of value a game-mode setting holds.</summary>
public enum SettingKind : byte
{
    Bool,
    Int,
    Float,

    /// <summary>One of the setting's named <see cref="SettingSpec.Choices"/>.</summary>
    Choice,
}

/// <summary>When a written setting takes effect (plan_bots_teams_tf2 D.2).</summary>
public enum SettingApplies : byte
{
    /// <summary>At once: the convar is written when the change is made.</summary>
    Now,

    /// <summary>At the next round's setup: the write waits for the mode's next round.</summary>
    NextRound,

    /// <summary>At the next level: the write waits for the next map.</summary>
    NextMap,
}

/// <summary>One value a setting offers, and what the menus call it (a token or a plain number).</summary>
public readonly record struct SettingChoice(string Value, string Label);

/// <summary>
/// One setting of a game mode: the convar that stores it, its kind and range,
/// the values the menus offer, when a change takes effect, and who may make it.
/// </summary>
public sealed record SettingSpec(
    string Key,
    SettingKind Kind,
    IReadOnlyList<SettingChoice> Choices,
    string Default,
    SettingApplies Applies = SettingApplies.Now,
    bool Votable = true,
    bool AdminOnly = false,
    float Min = 0f,
    float Max = 0f);

/// <summary>
/// One game mode as the menus see it: its id (<c>sv_mode</c>'s), its title
/// token, whether it plays with teams, its settings, and the entity classes a
/// map must hold one of to play it (empty: any map).
/// </summary>
public sealed record ModeSpec(
    string Id,
    string Title,
    bool Teamplay,
    IReadOnlyList<SettingSpec> Settings,
    IReadOnlyList<string> RequiresAnyOf);

/// <summary>Whether a map can play a mode, and why not when it cannot.</summary>
public readonly record struct MapSupport(bool Supported, string Reason);
