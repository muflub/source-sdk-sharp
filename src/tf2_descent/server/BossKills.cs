using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// The boss depths (5, 10, 15) a character has killed the boss of: what opens
/// the camps the town's cage offers (plan_descent_mine_levels M10, D4, read by
/// <see cref="TravelService"/> through <c>TownDescents.Destinations</c>).
/// HO13 records a boss kill on the hero's sheet (<c>Horde.Boss.BossFallen</c>) and reads it
/// back here (<c>Horde.Boss.SheetBossKills</c>).
/// </summary>
public interface IBossKills
{
    /// <summary>The boss depths whose boss the character <paramref name="characterId"/> has killed; empty for none.</summary>
    IReadOnlyCollection<int> Depths(string characterId);

    /// <summary>The boss depths whose boss the hero in client slot <paramref name="player"/> has killed (unhosted: his own sheet); empty for none.</summary>
    IReadOnlyCollection<int> DepthsOf(int player);
}

