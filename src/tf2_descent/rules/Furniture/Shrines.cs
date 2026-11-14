using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Furniture;

/// <summary>A shrine on the level: its marker, the effect rolled for it at load, and whether it has been used.</summary>
public sealed record ShrineState(string Marker, string Effect, bool Used = false);

/// <summary>A spell raised by a shrine for the level: <paramref name="Ranks"/> more of <paramref name="Spell"/>.</summary>
public sealed record SpellBoost(string Spell, int Ranks);

/// <summary>
/// A shrine used: the shrine after it, the effect, who takes it (the user, or the whole
/// party in the instance when the effect says so), the message to show, whether it
/// refills or reveals the map, and the spell it raised for each taker (none for a taker
/// who knows no spell).
/// </summary>
public sealed record ShrineUse(
    ShrineState Shrine,
    ShrineEffect Effect,
    ImmutableArray<string> Takers,
    string MessageToken,
    bool PartyWide,
    ImmutableSortedDictionary<string, SpellBoost> Spells,
    ShrineRefusal? Refused = null)
{
    public bool Ok => Refused is null;
}

public enum ShrineRefusal
{
    AlreadyUsed,
}

/// <summary>A shrine effect a hero carries until it leaves the level.</summary>
public sealed record ActiveShrine(string Effect, IReadOnlyList<StatAmount> Stats, SpellBoost? Spell);

/// <summary>What shrine effects one hero carries on this level.</summary>
public sealed record ShrineBuffs(ImmutableArray<ActiveShrine> Active)
{
    public static ShrineBuffs None { get; } = new([]);
}

/// <summary>
/// Shrines (plan_tf2_rpg 2l): one use each; the effect was rolled at load
/// (<see cref="FurnishingRules"/>) and is told by a cryptic message, never by name. An
/// effect that lasts the level is carried in <see cref="ShrineBuffs"/> and gone when its
/// taker leaves the level; an instant one (a refill, the map revealed) is done on use.
/// </summary>
public sealed class ShrineRules
{
    private readonly DescentContent _content;

    public ShrineRules(DescentContent content) => _content = content;

    private FurnitureSettings S => _content.Furniture;

    /// <summary>
    /// <paramref name="user"/> uses the shrine. <paramref name="party"/> is every party member
    /// in the instance (the user among them); <paramref name="spellsKnown"/> gives each one's
    /// known spells, from which a spell shrine raises one, chosen from the level seed.
    /// </summary>
    public ShrineUse Use(ShrineState shrine, string user, IReadOnlyList<string> party,
        Func<string, IReadOnlyCollection<string>> spellsKnown, ulong levelSeed)
    {
        var effect = S.Shrine(shrine.Effect);
        if (shrine.Used)
            return new ShrineUse(shrine, effect, [], effect.MessageToken, effect.PartyWide,
                ImmutableSortedDictionary<string, SpellBoost>.Empty, ShrineRefusal.AlreadyUsed);

        var takers = effect.PartyWide
            ? party.Append(user).Distinct(StringComparer.Ordinal).ToImmutableArray()
            : [user];
        var spells = ImmutableSortedDictionary.CreateBuilder<string, SpellBoost>(StringComparer.Ordinal);
        if (effect.SpellRanks > 0)
            foreach (var taker in takers)
            {
                var known = spellsKnown(taker).Order(StringComparer.Ordinal).ToList();
                if (known.Count == 0) continue;
                var roll = Seeded.From(levelSeed, "shrine-spell", shrine.Marker, taker);
                spells[taker] = new SpellBoost(known[roll.Next(known.Count)], effect.SpellRanks);
            }
        return new ShrineUse(shrine with { Used = true }, effect, takers, effect.MessageToken, effect.PartyWide, spells.ToImmutable());
    }

    /// <summary>One taker's buffs after a use: a level-long effect joins them; an instant one does not.</summary>
    public static ShrineBuffs Take(ShrineBuffs buffs, ShrineUse use, string taker)
    {
        if (!use.Ok || !use.Takers.Contains(taker) || use.Effect.Lasts != ShrineLasts.Level) return buffs;
        use.Spells.TryGetValue(taker, out var spell);
        return new ShrineBuffs(buffs.Active.Add(new ActiveShrine(use.Effect.Id, use.Effect.Stats, spell)));
    }

    /// <summary>Leaving the level ends every level-long shrine effect.</summary>
    public static ShrineBuffs LeaveLevel(ShrineBuffs buffs) => ShrineBuffs.None;

    /// <summary>The buffs as the hero sheet reads them: each stat from a Buff source named for its shrine.</summary>
    public static IReadOnlyList<Contribution> Contributions(ShrineBuffs buffs) =>
        [.. buffs.Active.SelectMany(a => a.Stats.Select(s =>
            new Contribution(s.Stat, s.Amount, new ContributionSource(SourceKind.Buff, "shrine:" + a.Effect))))];

    /// <summary>The spell ranks the buffs add, by spell.</summary>
    public static IReadOnlyDictionary<string, int> SpellRanks(ShrineBuffs buffs) =>
        buffs.Active.Where(a => a.Spell is not null)
            .GroupBy(a => a.Spell!.Spell, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Spell!.Ranks), StringComparer.Ordinal);
}
