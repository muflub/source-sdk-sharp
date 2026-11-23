using System.Collections.Immutable;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Furniture;

/// <summary>What breaking a crate or a barrel gives (the user, 2026-11-15: "crates and barrels should be breakable for rng chance of gld/items/bad stuff").</summary>
public enum BreakOutcome
{
    /// <summary>Splinters and nothing else: the most common.</summary>
    Nothing,

    /// <summary>A little Australium on the floor, each player's own.</summary>
    Australium,

    /// <summary>A consumable of the level's band, each player's own.</summary>
    Potion,

    /// <summary>An item of the breakables' loot table, each player's own (R4).</summary>
    Item,

    /// <summary>It goes off as the furniture's barrel does.</summary>
    Blast,

    /// <summary>A splash that puts a status on every hero near it.</summary>
    Cloud,

    /// <summary>Robots of the level's templates, woken beside it and hunting the hero who broke it.</summary>
    Ambush,
}

/// <summary>The splash a <see cref="BreakOutcome.Cloud"/> throws: the condition word (robot_conditions.json) it puts on each hero within <see cref="Radius"/>, for <see cref="Seconds"/>.</summary>
public sealed record BreakCloud(string Condition, double Seconds, double Radius);

/// <summary>An <see cref="BreakOutcome.Ambush"/>: between <see cref="Min"/> and <see cref="Max"/> robots, each hunting its breaker for <see cref="HuntSeconds"/>.</summary>
public sealed record BreakAmbush(int Min, int Max, double HuntSeconds);

/// <summary>
/// furniture.json's <c>breakables</c>: the kinds (a crate, a barrel), the outcome weights and the
/// cap on the bad ones' share, the loot table an item rolls from, the share of a kill's Australium
/// curve a pile is, the explosive row a blast goes off as, the cloud and the ambush.
/// </summary>
public sealed record BreakableSettings(
    IReadOnlyList<string> Kinds,
    ImmutableDictionary<BreakOutcome, double> Outcomes,
    double BadCapPct,
    string LootTable,
    double AustraliumScale,
    string Blast,
    BreakCloud Cloud,
    BreakAmbush Ambush)
{
    /// <summary>The outcomes that hurt the party.</summary>
    public static bool Bad(BreakOutcome outcome) => outcome is BreakOutcome.Blast or BreakOutcome.Cloud or BreakOutcome.Ambush;

    /// <summary>Whether an outcome hands each player a share of his own (Australium, a potion, an item).</summary>
    public static bool PerPlayer(BreakOutcome outcome) => outcome is BreakOutcome.Australium or BreakOutcome.Potion or BreakOutcome.Item;

    public double Weight(BreakOutcome outcome) => Outcomes.GetValueOrDefault(outcome);
}

/// <summary>One break's roll: what it gives, and the stream its shares and its ambush are rolled from.</summary>
public sealed record BreakRoll(string Marker, BreakOutcome Outcome);

/// <summary>
/// Crates and barrels (plan_tf2_rpg 2l, beside R24's containers and explosive barrels): any hit
/// breaks one, and a break rolls ONCE, from the level seed and the marker it stood on, so a replay
/// of the level gives the same answer. The roll picks an outcome by the table's weights; the shares
/// of an Australium, potion or item outcome are each player's own (R4), rolled by the loot rules
/// from <see cref="Request"/>; an ambush's robots are drawn from the depth's templates.
/// </summary>
public sealed class BreakRules
{
    private readonly DescentContent _content;

    public BreakRules(DescentContent content) => _content = content;

    private BreakableSettings S => _content.Furniture.Breakables;

    /// <summary>The outcome of breaking the breakable on <paramref name="marker"/> on the level of <paramref name="levelSeed"/>.</summary>
    public BreakRoll Roll(ulong levelSeed, string marker)
    {
        var r = Seeded.From(levelSeed, "break", marker);
        var all = Enum.GetValues<BreakOutcome>();
        return new BreakRoll(marker, all[r.Pick([.. all.Select(S.Weight)])]);
    }

    /// <summary>
    /// The outcome of breaking hosted breakable <paramref name="breakableSeq"/> (the breakables
    /// manager's slot) on the instance of <paramref name="instanceSeed"/> at <paramref name="depth"/>
    /// (Host.Contracts 1.2 <c>BreakableDrops</c>): keyed on those three alone, so every hero the host
    /// asks for agrees on it, and the game carries out the same outcome (a blast, a cloud, an ambush).
    /// The depth is the levels table's (one below 1 reads depth 1, as every loot roll does), so a
    /// hub's depth 0 and its level's 1 agree.
    /// </summary>
    public BreakOutcome Hosted(ulong instanceSeed, uint breakableSeq, int depth)
    {
        var r = Seeded.From(instanceSeed, "break_hosted", breakableSeq, Math.Clamp(depth, 1, _content.Levels.Rows.Count));
        var all = Enum.GetValues<BreakOutcome>();
        return all[r.Pick([.. all.Select(S.Weight)])];
    }

    /// <summary>The name a hosted breakable goes by in its share's roll: its number on the level.</summary>
    public static string HostedName(uint breakableSeq) => "breakable:" + breakableSeq.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// What one opener's share of hosted breakable <paramref name="breakableSeq"/> is rolled from: the
    /// breakables' table, for the opener as the host knows him (his sheet's name and class), seeded
    /// from the instance seed and the breakable's number, as the unhosted share is from the level
    /// seed, the marker and the player.
    /// </summary>
    public LootRequest HostedRequest(ulong instanceSeed, uint breakableSeq, string openerName, string openerClass)
    {
        string name = HostedName(breakableSeq), player = openerName + "/" + openerClass;
        return new(S.LootTable, name, player, Seeded.From(instanceSeed, "break_share", name, player).NextULong());
    }

    /// <summary>What the loot rules roll <paramref name="player"/>'s share of a break from: the breakables' table, seeded by the level, the marker and the player.</summary>
    public LootRequest Request(ulong levelSeed, string marker, string player) =>
        new(S.LootTable, marker, player, Seeded.From(levelSeed, "break_share", marker, player).NextULong());

    /// <summary>
    /// The robots an ambush wakes at <paramref name="depth"/>: between the table's least and most,
    /// each a template of the depth's pack table by its weight, from the level seed and the marker.
    /// </summary>
    public ImmutableArray<string> Ambush(ulong levelSeed, string marker, int depth)
    {
        var rows = _content.Levels.Rows;
        var templates = rows[Math.Clamp(depth, 1, rows.Count) - 1].Templates;
        var r = Seeded.From(levelSeed, "break_ambush", marker);
        int count = S.Ambush.Min + r.Next(S.Ambush.Max - S.Ambush.Min + 1);
        var weights = templates.Select(t => t.Weight).ToList();
        var picked = ImmutableArray.CreateBuilder<string>(count);
        for (int i = 0; i < count; i++)
            picked.Add(templates[r.Pick(weights)].Template);
        return picked.MoveToImmutable();
    }
}
