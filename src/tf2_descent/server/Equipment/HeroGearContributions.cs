using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server.Gear;

/// <summary>
/// What a hero's gear adds to his sheet (<see cref="IHeroContributionSource"/>): the rules'
/// contributions of every equipped item, the nine slots and armour among them
/// (<see cref="TF2.Descent.Rules.Items.Equipment.Resolve"/>), each carrying its item's state,
/// so an inactive or broken item's count for nothing. The requirement resolution is the
/// rules', over the sheet the hero is recomputed with.
///
/// <para>A recompute is also the moment a sheet change (a level-up, a point spent) can wake
/// or put to sleep a weapon's rolled attributes: when the hero's weapon slots would now be
/// answered otherwise than the kit was built from, the settle is asked to rebuild it after
/// this frame (<see cref="HeroKitSettle.Pending"/>).</para>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroGearContributions(HeroEquipment equipment, HeroKitSettle settle) : IHeroContributionSource, IOrderedHook
{
    /// <summary>In TF2 Descent's equipment block.</summary>
    public const int Order = ModStages.First + 401;

    public int HookOrder => Order;

    public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet)
    {
        if (equipment.HeroOf(playerIndex) is not { } key || equipment.Ledger.Find(key) is not { } hero)
            return [];
        var gear = equipment.Resolve(playerIndex, key, hero, sheet);
        settle.Check(playerIndex);
        return gear.Result.Contributions;
    }
}
