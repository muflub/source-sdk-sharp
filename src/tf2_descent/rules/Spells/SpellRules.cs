using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;

namespace TF2.Descent.Rules.Spells;

/// <summary>Why reading a tome was refused.</summary>
public enum TomeRefusal
{
    UnknownSpell,
    /// <summary>The spell is at its max rank: the tome is refused and kept.</summary>
    AtMaxRank,
}

/// <summary>A tome read: the sheet after it, whether the tome was used up, and why it was refused.</summary>
public sealed record TomeResult(HeroSheet Sheet, bool Consumed, TomeRefusal? Refusal)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// A scroll used: how many are left in the stack, whether one was used up (always, once, when
/// the stack had one), whether the spell went off (not when the cast was interrupted), and
/// the spell at the scroll's rank.
/// </summary>
public sealed record ScrollResult(int Remaining, bool Consumed, bool Cast, ResolvedSkill? Spell);

/// <summary>Scrolls added to a stack: the stack after it, and what did not fit.</summary>
public readonly record struct ScrollStack(int Count, int Overflow);

/// <summary>
/// Tomes and scrolls (plan_tf2_rpg 2e, R27; RU6 step 5), pure over the content. A tome is a
/// consumable: the first copy read teaches its spell at rank 1, each further copy adds a
/// rank up to the cap, and one more is refused and kept. Each rank has a Smarts requirement
/// read from Now: below it the spell is inactive, never forgotten. Skill points and respecs
/// never touch spell ranks. A scroll casts one spell once at a fixed rank, with no Smarts
/// requirement and no mana, and is used up exactly once even when the cast is interrupted.
/// </summary>
public sealed class SpellRules
{
    private readonly DescentContent _content;
    private readonly SkillRules _skills;
    private readonly CastRules _casting;

    public SpellRules(DescentContent content, SkillRules skills, CastRules casting)
    {
        _content = content;
        _skills = skills;
        _casting = casting;
    }

    private SkillSettings S => _content.SkillSettings;

    public SpellDef? Find(string id) => _content.Spells.TryGet(id, out var s) ? s : null;

    public SpellDef Spell(string id) => Find(id) ?? throw new ArgumentException($"'{id}' is not a spell", nameof(id));

    /// <summary>The rank the hero has read the spell to (0: never read).</summary>
    public static int Rank(HeroSheet sheet, string id) => sheet.SpellRanks.TryGetValue(id, out var r) ? r : 0;

    /// <summary>Reads one tome of <paramref name="spellId"/>: teaches rank 1, else adds a rank; at the cap it is refused and not used up.</summary>
    public TomeResult ReadTome(HeroSheet sheet, string spellId)
    {
        if (Find(spellId) is not { } spell) return new(sheet, false, TomeRefusal.UnknownSpell);
        int rank = Rank(sheet, spellId);
        if (rank >= spell.MaxRank) return new(sheet, false, TomeRefusal.AtMaxRank);
        return new(sheet with { SpellRanks = sheet.SpellRanks.SetItem(spellId, rank + 1) }, true, null);
    }

    /// <summary>The Smarts rank <paramref name="rank"/> of the spell needs.</summary>
    public double SmartsRequired(string spellId, int rank) => Spell(spellId).Smarts.At(rank);

    /// <summary>Whether the hero can cast the spell: it has been read, and Smarts Now meets its rank's requirement.</summary>
    public bool Active(HeroSheet sheet, HeroStats stats, string spellId)
    {
        int rank = Rank(sheet, spellId);
        return rank > 0 && stats.Now(S.SpellAttribute) >= SmartsRequired(spellId, rank);
    }

    /// <summary>The spell at <paramref name="rank"/>, every number resolved; its damage reads the spell attribute's Now.</summary>
    public ResolvedSkill Resolve(HeroSheet sheet, HeroStats stats, string spellId, int rank)
    {
        var spell = Spell(spellId);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        return _skills.ResolveAt(spell.Id, spell.Effect, [], spell.ManaCost, spell.Cooldown, Rank(sheet, spellId), rank,
            sheet, stats, S.SpellAttribute);
    }

    /// <summary>
    /// Casts a spell the hero has read, at its rank: refused when unknown, never read, inactive
    /// (Smarts below its rank's requirement), on cooldown, or short of mana.
    /// </summary>
    public CastResult Cast(HeroSheet sheet, HeroStats stats, CasterState state, string spellId)
    {
        if (Find(spellId) is null) return new(state, CastRefusal.UnknownSkill, null, 0, [], []);
        int rank = Rank(sheet, spellId);
        if (rank <= 0) return new(state, CastRefusal.NotLearned, null, 0, [], []);
        if (!Active(sheet, stats, spellId)) return new(state, CastRefusal.Inactive, null, 0, [], []);
        if (state.CooldownLeft(spellId) > 0) return new(state, CastRefusal.OnCooldown, null, 0, [], []);
        var resolved = Resolve(sheet, stats, spellId, rank);
        double cost = _casting.ManaCost(resolved, stats, 0);
        if (state.Mana < cost) return new(state, CastRefusal.NoMana, null, 0, [], []);
        var after = state with { Mana = state.Mana - cost };
        if (resolved.Cooldown > 0)
            after = after with { ReadyAt = after.ReadyAt.SetItem(spellId, state.Now + resolved.Cooldown) };
        return new(after, null, resolved, cost, [], []);
    }

    /// <summary>
    /// Uses one scroll of a stack of <paramref name="held"/>: the spell at the scroll's
    /// <paramref name="rank"/>, with no Smarts requirement, no mana and no cooldown. The
    /// scroll is used up exactly once whether or not the cast was
    /// <paramref name="interrupted"/>; an empty stack uses nothing.
    /// </summary>
    public ScrollResult UseScroll(HeroSheet sheet, HeroStats stats, int held, string spellId, int rank, bool interrupted)
    {
        var spell = Spell(spellId);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rank, spell.MaxRank);
        if (held <= 0) return new(0, false, false, null);
        return new(held - 1, true, !interrupted, interrupted ? null : Resolve(sheet, stats, spellId, rank));
    }

    /// <summary>Adds scrolls to a stack, which holds at most the content's scroll stack (10); the rest overflow.</summary>
    public ScrollStack Stack(int held, int adding)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(held);
        ArgumentOutOfRangeException.ThrowIfNegative(adding);
        int count = Math.Min(S.ScrollStack, held + adding);
        return new ScrollStack(count, held + adding - count);
    }
}
