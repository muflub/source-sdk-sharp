using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// The boss depths (5, 10, 15) a character has killed the boss of: what opens
/// the camps the town's cage offers (plan_descent_mine_levels M10, D4, read by
/// <see cref="TravelService"/> through <c>TownDescents.Destinations</c>).
/// Nothing records a boss kill yet (D4 extends the sheet), so the one
/// implementation, <see cref="NoBossKills"/>, answers none.
/// </summary>
public interface IBossKills
{
    /// <summary>The boss depths whose boss the character <paramref name="characterId"/> has killed; empty for none.</summary>
    IReadOnlyCollection<int> Depths(string characterId);
}

/// <summary>Until a boss kill is recorded (D4): no character has killed one.</summary>
[SideService(Side.Server, As = [typeof(IBossKills)])]
public sealed class NoBossKills : IBossKills
{
    public IReadOnlyCollection<int> Depths(string characterId) => [];
}
