using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// The level a hero's armour is measured against (Appendix C.2), from the horde (HO8): a lent
/// robot body's attacker level is its roster row's monster level; anything else (a trap, the
/// world, a parked body, a dead robot's, another player) is level 1. Nothing hurts a hero as a
/// robot until HO7's attacks; the hero damage layer asks it of every hit already.
/// </summary>
[SideService(Side.Server, As = [typeof(IAttackerLevel)])]
public sealed class RobotAttackerLevel(IHordeCurrent horde, IEntityDirectory directory) : IAttackerLevel
{
    /// <summary>The level of anything that is not a robot.</summary>
    public const int Default = 1;

    public int LevelOf(EntityHandle attacker)
    {
        if (attacker == EntityHandle.None || horde.Current?.Roster is not { } roster
            || directory.Get(attacker) is not RobotBody { Parked: false } body || !roster.IsLive(body.Robot)
            || roster.State(body.Robot.Index) != RobotState.Embodied)
            return Default;
        return Math.Max(Default, roster.MonsterLevel(body.Robot.Index));
    }
}
