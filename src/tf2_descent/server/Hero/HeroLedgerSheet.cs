using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// The item ledger's copy of a hero's sheet follows the live one
/// (<see cref="IItemLedger.SetSheet"/>: capacity and equip requirements read
/// it): each time the hero's stats are derived again (a level-up, a
/// <c>tf2_descent_setlevel</c>, a spend, the sheet's arrival), the sheet goes
/// to the ledger under the hero's ledger key (<see cref="HeroKeys"/>). A hero
/// the ledger has not loaded, or one with no key yet, is skipped.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroLedgerSheet(HeroKeys keys, IItemLedger ledger) : IHeroStatsWatcher, IOrderedHook
{
    /// <summary>In TF2 Descent's hero block, after the TF attributes (<see cref="HeroAttributes.Order"/>).</summary>
    public const int Order = ModStages.First + 320;

    public int HookOrder => Order;

    public void StatsChanged(HeroComponent hero)
    {
        if (hero.Sheet is not { } sheet || keys.Of(hero.PlayerIndex) is not { } key || ledger.Find(key) is not { } held)
            return;
        if (!ReferenceEquals(held.Sheet, sheet))
            ledger.SetSheet(key, sheet);
    }

    public void HeroLeft(int playerIndex)
    {
    }
}
