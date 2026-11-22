using SourceSharp.Abi;

using TF2.Client.Ui;

namespace TF2.Descent.Client.Ui;

/// <summary>
/// No map info after the message of the day (user 2026-11-15: "there is a
/// menu that pops up after the motd"): TF2's text window's OK raises its map
/// info menu, whose briefing is TF2's game type and whose CONTINUE leads to
/// the team menu. A hero is placed on the heroes' team as his sheet's class
/// the moment he is in the game (the server's HeroPlacement), so closing the
/// message leaves him in the level with nothing else to answer.
/// </summary>
[SideService(Side.Client)]
public sealed class HeroMapInfoGate : ITFMapInfoGate, IOrderedHook
{
    /// <summary>The one member of the chain, at TF2 Descent's first mode stage (the server's ModStages.First + 100).</summary>
    public const int Order = 1_000_000 + 100;

    public int HookOrder => Order;

    public bool RaisesMapInfo() => false;
}
