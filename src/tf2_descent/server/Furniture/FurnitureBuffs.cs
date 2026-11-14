using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Mode;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// The shrine effects each hero carries on this level (the rules'
/// <see cref="ShrineBuffs"/>, by the ledger's name for him): a level-long
/// effect joins at its use and every one ends when the level does
/// (<see cref="ShrineRules.LeaveLevel"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureBuffs
{
    private readonly Dictionary<string, ShrineBuffs> _byHero = new(StringComparer.Ordinal);

    public ShrineBuffs Of(string hero) => _byHero.GetValueOrDefault(hero) ?? ShrineBuffs.None;

    public void Set(string hero, ShrineBuffs buffs) => _byHero[hero] = buffs;

    /// <summary>The level ends: every hero leaves it, and his shrine effects with it.</summary>
    public void LeaveLevel()
    {
        foreach (var hero in _byHero.Keys.ToList())
            _byHero[hero] = ShrineRules.LeaveLevel(_byHero[hero]);
    }

    /// <summary>Heroes carrying any shrine effect.</summary>
    public int Carrying => _byHero.Values.Count(b => !b.Active.IsEmpty);
}

/// <summary>
/// The shrines' share of a hero's stats (plan_descent_logic WO3, 2l), on the
/// hero's contribution chain beside his gear and skills: each level-long
/// effect he carries as the rules read it (<see cref="ShrineRules.Contributions"/>:
/// a Buff source named for its shrine), so the sheet, the stats print and the
/// HUD see it as they see a skill's buff.
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureContributions(FurnitureBuffs buffs, IFurnitureHeroes heroes) : IHeroContributionSource, IOrderedHook
{
    /// <summary>In TF2 Descent's furniture block.</summary>
    public const int Order = ModStages.First + 851;

    public int HookOrder => Order;

    public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) =>
        heroes.KeyOf(playerIndex) is { } hero ? ShrineRules.Contributions(buffs.Of(hero)) : [];
}

/// <summary>
/// Whether the furniture's TF2 blast is being delivered now: that pass is
/// for what is not a player, so <see cref="FurnitureBlastRule"/> refuses a
/// hero inside it (his hit is the rules' own, dealt after it).
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureBlastScope
{
    private int _depth;

    public bool Active => _depth > 0;

    public void Enter() => _depth++;

    public void Leave() => _depth = Math.Max(0, _depth - 1);
}

/// <summary>
/// No hero is hurt by the furniture's TF2 blast (<see cref="IFurnitureWorld.Blast"/>):
/// an explosive's blast reaches robots through TF2's applier, credited to the
/// hero who set it off, and heroes through the furniture's own pass at the
/// rules' damage with the explosive as attacker (a hero would otherwise be
/// spared by friendly fire, or take his own blast as a rocket jump).
/// </summary>
[SideService(Side.Server)]
public sealed class FurnitureBlastRule(IDescentModeState mode, FurnitureBlastScope scope, IEngineEntities entities)
    : IDamageRule, IOrderedHook
{
    /// <summary>In TF2 Descent's furniture block, after the mode's friendly fire (First + 100).</summary>
    public const int Order = ModStages.First + 852;

    public int HookOrder => Order;

    public Decision Decide(in DamageContext hit) =>
        mode.Active && scope.Active && entities.IsPlayer(hit.Victim) ? Decision.No : Decision.UseStockRule;
}
