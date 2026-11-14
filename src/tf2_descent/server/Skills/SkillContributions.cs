using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server.Skills;

/// <summary>A timed buff a hero stands in: an aura's or a buff's stats, from a skill, until a tick.</summary>
public sealed record HeroBuff(string SourceId, int FromIndex, IReadOnlyList<Contribution> Stats, int EndTick);

/// <summary>
/// The timed buffs on each hero (auras, buffs, a triggered effect's
/// duration): one a source on a hero, a recast replacing it, gone at its end
/// tick. What <see cref="SkillContributions"/> adds to the hero's stats.
/// </summary>
[SideService(Side.Server)]
public sealed class SkillBuffs
{
    private readonly Dictionary<int, List<HeroBuff>> _byIndex = new();

    public IReadOnlyList<HeroBuff> Of(int playerIndex) =>
        _byIndex.TryGetValue(playerIndex, out var list) ? list : [];

    /// <summary>The buff on the hero; one from the same source is replaced.</summary>
    public void Add(int playerIndex, HeroBuff buff)
    {
        if (!_byIndex.TryGetValue(playerIndex, out var list))
            _byIndex[playerIndex] = list = [];
        list.RemoveAll(b => b.SourceId == buff.SourceId);
        list.Add(buff);
    }

    /// <summary>The buffs whose end tick has come go; whether any did.</summary>
    public bool Expire(int playerIndex, int tick) =>
        _byIndex.TryGetValue(playerIndex, out var list) && list.RemoveAll(b => b.EndTick <= tick) > 0;

    /// <summary>The buffs from <paramref name="sourceId"/> go from every hero (an Order ended by another); the heroes that lost one.</summary>
    public IReadOnlyList<int> End(string sourceId)
    {
        var lost = new List<int>();
        foreach (var (index, list) in _byIndex)
            if (list.RemoveAll(b => b.SourceId == sourceId) > 0)
                lost.Add(index);
        return lost;
    }

    public void Clear(int playerIndex) => _byIndex.Remove(playerIndex);
}

/// <summary>
/// The skills' share of a hero's stats (plan_descent_logic HE6), on the
/// hero's contribution chain: the passive upgrades held
/// (<see cref="SkillRules.Contributions"/>), the upgrades scoped to the weapon
/// family in the hands (<see cref="SkillRules.WeaponContributions"/>, R17),
/// the timed buffs the hero stands in (<see cref="SkillBuffs"/>), and what a
/// held trigger or a toggle grants while it holds (BlastJumping, life
/// missing, no damage taken, a toggle on: <see cref="SkillCaster.HeldStats"/>).
/// Gear's bonus ranks are not read: nothing hands them to the runtime yet
/// (<see cref="SkillBonuses.None"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class SkillContributions(TF2DescentRules rules, SkillCasters casters, SkillBuffs buffs)
    : IHeroContributionSource, IOrderedHook
{
    /// <summary>In TF2 Descent's skills block.</summary>
    public const int Order = ModStages.First + 610;

    public int HookOrder => Order;

    public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet)
    {
        var list = new List<Contribution>(rules.Skills.Contributions(sheet, SkillBonuses.None));
        var caster = casters.Of(playerIndex);
        if (caster?.Family is { } family)
            list.AddRange(rules.Skills.WeaponContributions(sheet, SkillBonuses.None, family));
        foreach (var buff in buffs.Of(playerIndex))
            list.AddRange(buff.Stats);
        if (caster is not null)
            list.AddRange(caster.HeldStats);
        return list;
    }
}
