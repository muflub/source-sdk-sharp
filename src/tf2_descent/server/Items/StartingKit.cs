using SourceSharp.Abi;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Server.Items;

/// <summary>
/// What a new hero of the in-memory ledger starts with, so there is something to equip and
/// drink (HE0): the rules' starting kit for the class (starting_kits.json,
/// <see cref="TF2DescentRules.StartingKit"/>), its gear bases in the backpack and its potions
/// on the belt, each a Stock item rolled through the rules with a forced base.
/// </summary>
[SideService(Side.Server)]
public sealed class StartingKit(TF2DescentRules rules)
{
    /// <summary>The source the kit is rolled under: a craft's (identified, never cursed or promoted).</summary>
    public const string Source = StartingKitRow.Source;

    /// <summary>The backpack's bases for a class: its stock weapons; none for a class the kit does not name.</summary>
    public IReadOnlyList<string> WeaponsOf(string classId) => rules.StartingKit(classId).Backpack;

    /// <summary>The belt's potions for a class, by belt slot.</summary>
    public IReadOnlyList<string> PotionsOf(string classId) => rules.StartingKit(classId).Belt;
}
