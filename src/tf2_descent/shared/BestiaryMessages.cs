using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// WHAT A HERO MAY KNOW OF A ROBOT (plan_descent_logic UI6, ruling L6, default
// E7). The robot body's own networked state goes to every client and carries
// only what anyone may see: its template, level, scale, a life FRACTION for the
// bar, the champion flag and the status bits. What the bestiary gates goes to
// one hero alone, decided on the server by the rules' BestiaryRules.View for
// that hero's kills of the template:
//
//   bestiary    his kill count of each template (the count the bar shows);
//               sent at spawn (a resync) and when a kill changes a count
//   robot_view  one lent body as he may see it: its life numbers (life, at
//               most) once the template's life is revealed, its rolled
//               resistances, immunity and champion affixes once resistances
//               are, its temperament style once the style is. A field he has
//               not earned is NOT IN THE BYTES: an array of length 0, an empty
//               string. Sent when a body is lent, when what he may see of it
//               changes (a revealed life falls, a threshold is crossed), and
//               with Template 0 when the body he was told of is parked.
//   robot_immune  a hit of his that the robot was immune to: the body's entity
//               index and the damage type, so IMMUNE shows over that robot
//
// Rows of the rules' tables are named by index (robots.json, champions.json,
// damage_types.json, status.json); the client embeds the same files, and the
// fingerprint each message carries (RobotContentKeys) is what it checks them
// against. Every payload fits one 255-byte user message at its fullest.
//=============================================================================

/// <summary>Which parts of a template's entry a hero may see (the rules' <c>BestiaryReveal</c>, as bits).</summary>
[Flags]
public enum RevealBits : byte
{
    None = 0,
    Count = 1,
    Life = 2,
    Resistances = 4,
    Style = 8,
}

/// <summary>What the bestiary message does to the client's counts.</summary>
public enum BestiaryPhase : byte
{
    /// <summary>The rows sent replace those templates' counts; the others stay.</summary>
    Update,

    /// <summary>Forget every count: the rows sent are the hero's whole bestiary.</summary>
    Resync,
}

/// <summary>A hero's kill counts as they reach his client: template rows of robots.json and their counts.</summary>
public sealed record BestiaryView(uint Fingerprint, BestiaryPhase Phase, IReadOnlyList<(int Template, int Kills)> Rows);

public interface IBestiaryHook
{
    void OnBestiary(BestiaryView bestiary);
}

/// <summary>The bestiary on the wire (<c>bestiary</c>): to the hero alone.</summary>
public partial struct BestiaryCounts
{
    /// <summary>The most templates one message carries; a longer resync goes in several (the first a <see cref="BestiaryPhase.Resync"/>).</summary>
    public const int MaxRows = 40;

    public partial uint Fingerprint { get; set; }
    public partial byte Phase { get; set; }

    /// <summary>Each row's template: its index in robots.json.</summary>
    [UserMessageArray(MaxCount = MaxRows)]
    public partial byte[] Templates { get; set; }

    [UserMessageArray(MaxCount = MaxRows)]
    public partial int[] Kills { get; set; }

    public BestiaryView View()
    {
        var templates = Templates ?? [];
        var kills = Kills ?? [];
        var rows = new (int, int)[Math.Min(templates.Length, kills.Length)];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = (templates[i], kills[i]);
        return new BestiaryView(Fingerprint, (BestiaryPhase)Phase, rows);
    }
}

[UserMessage(Name)]
public sealed class BestiaryMessage(IHooks<IBestiaryHook>? hooks) : IUserMessage<BestiaryCounts>
{
    public BestiaryMessage() : this(null) { }

    public const string Name = "bestiary";

    public void OnReceived(BestiaryCounts message)
    {
        var view = message.View();
        hooks?.Each(nameof(IBestiaryHook.OnBestiary), h => h.OnBestiary(view));
    }
}

/// <summary>
/// One robot body as one hero may see it, read off the wire. A part the
/// bestiary hides is absent: <see cref="Life"/> and <see cref="MaxLife"/> are
/// null below the life threshold, <see cref="Resist"/> (percent per damage
/// type, in damage_types.json's order), <see cref="Immunity"/> and
/// <see cref="Affixes"/> (champions.json rows) below the resistances', and
/// <see cref="Style"/> below the style's. <see cref="Template"/> is the
/// body's robots.json row plus one, as its net state names it; 0 says the
/// body was parked and the client forgets it.
/// </summary>
public sealed record RobotView(
    int Entity, int Serial, int Template, uint Fingerprint, RevealBits Reveal,
    int? Life, int? MaxLife, IReadOnlyList<int>? Resist, int? Immunity, IReadOnlyList<int>? Affixes, string? Style)
{
    public bool Gone => Template == 0;
}

public interface IRobotViewHook
{
    void OnRobotView(RobotView view);

    /// <summary>A hit of the local hero on the robot in <paramref name="entity"/> did nothing: it is immune to <paramref name="damageType"/> (damage_types.json's row).</summary>
    void OnRobotImmune(int entity, int damageType);
}

/// <summary>A robot view on the wire (<c>robot_view</c>): to one hero alone.</summary>
public partial struct RobotViewWire
{
    /// <summary>The most damage types a view carries resistances for.</summary>
    public const int MaxTypes = 8;

    /// <summary>The most champion affixes a view names.</summary>
    public const int MaxAffixes = 8;

    /// <summary>The longest style a view carries (two of the temperament table's words).</summary>
    public const int MaxStyle = 48;

    public partial short Entity { get; set; }

    /// <summary>The lend's serial, so a view of a body lent again is told from the last.</summary>
    public partial int Serial { get; set; }

    public partial byte Template { get; set; }
    public partial uint Fingerprint { get; set; }
    public partial byte Reveal { get; set; }

    /// <summary>Life now and at most: two values once the life is revealed, else none.</summary>
    [UserMessageArray(MaxCount = 2)]
    public partial int[] Life { get; set; }

    /// <summary>Percent per damage type once resistances are revealed, else none.</summary>
    [UserMessageArray(MaxCount = MaxTypes)]
    public partial short[] Resist { get; set; }

    /// <summary>The damage type it is immune to, once resistances are revealed and it has one, else none.</summary>
    [UserMessageArray(MaxCount = 1)]
    public partial byte[] Immune { get; set; }

    /// <summary>Its champion affixes (champions.json rows) once resistances are revealed, else none.</summary>
    [UserMessageArray(MaxCount = MaxAffixes)]
    public partial byte[] Affixes { get; set; }

    /// <summary>Its temperament style once the style is revealed, else empty.</summary>
    [UserMessageString(MaxLength = MaxStyle)]
    public partial string Style { get; set; }

    public RobotView View()
    {
        var life = Life ?? [];
        var resist = Resist ?? [];
        var immune = Immune ?? [];
        var affixes = Affixes ?? [];
        var reveal = (RevealBits)Reveal;
        return new RobotView(
            Entity, Serial, Template, Fingerprint, reveal,
            life.Length >= 2 ? life[0] : null, life.Length >= 2 ? life[1] : null,
            (reveal & RevealBits.Resistances) != 0 ? Array.ConvertAll(resist, r => (int)r) : null,
            immune.Length > 0 ? immune[0] : null,
            (reveal & RevealBits.Resistances) != 0 ? Array.ConvertAll(affixes, a => (int)a) : null,
            string.IsNullOrEmpty(Style) ? null : Style);
    }
}

[UserMessage(Name)]
public sealed class RobotViewMessage(IHooks<IRobotViewHook>? hooks) : IUserMessage<RobotViewWire>
{
    public RobotViewMessage() : this(null) { }

    public const string Name = "robot_view";

    public void OnReceived(RobotViewWire message)
    {
        var view = message.View();
        hooks?.Each(nameof(IRobotViewHook.OnRobotView), h => h.OnRobotView(view));
    }
}

/// <summary>IMMUNE on the wire (<c>robot_immune</c>): to the hero whose hit it was.</summary>
public partial struct RobotImmune
{
    public partial short Entity { get; set; }

    /// <summary>The damage type's row in damage_types.json; 255 for one the table lacks.</summary>
    public partial byte DamageType { get; set; }
}

[UserMessage(Name)]
public sealed class RobotImmuneMessage(IHooks<IRobotViewHook>? hooks) : IUserMessage<RobotImmune>
{
    public RobotImmuneMessage() : this(null) { }

    public const string Name = "robot_immune";

    public void OnReceived(RobotImmune message)
    {
        int entity = message.Entity, type = message.DamageType;
        hooks?.Each(nameof(IRobotViewHook.OnRobotImmune), h => h.OnRobotImmune(entity, type));
    }
}

/// <summary>
/// The fingerprint of the robot rows the bestiary's messages name by index:
/// robots.json, champions.json, damage_types.json and status.json's ids, in
/// table order (<see cref="ContentFingerprint"/>). The server computes it from
/// the rules' tables, the client from the bytes it embeds.
/// </summary>
public static class RobotContentKeys
{
    public static uint Fingerprint(IEnumerable<string> robots, IEnumerable<string> champions, IEnumerable<string> damageTypes,
                                   IEnumerable<string> statuses) =>
        ContentFingerprint.Of(robots, champions, damageTypes, statuses);
}
