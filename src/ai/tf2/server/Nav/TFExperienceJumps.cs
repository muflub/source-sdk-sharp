using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Player;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// What the jump watch reads of a TF2 player (lane G6a, plan_bots_teams_tf2_part_g G.6). Its body: the state the command
/// loop published after the command before (<see cref="SsPlayer.State"/>: fields of a managed object, no engine call but
/// one for a landing's ground). Its blasts: the blast jump state the mod's blast applier sets (<see cref="TFBlastJumpState"/>):
/// its own rocket (<see cref="TFBlastJump.RocketJumped"/>, named by every self push of a soldier's, grounded or not), its own
/// sticky (<see cref="TFBlastJump.StickyJumped"/>), another's blast on its rise (<see cref="TFBlastJump.EnemyBlastedMe"/>);
/// the state lasts until the landing think clears it, so a read in the air sees it. Asked by the experience's jump feed
/// alone, for persons' jumps (and the stand-in's): nothing here reaches a bot's view.
/// </summary>
[SideService(Side.Server, As = [typeof(IExperienceJumps)])]
public sealed class TFExperienceJumps(TFPlayerCarriers carriers, IEntityDirectory directory, IEnginePlayers players) : IExperienceJumps
{
    public bool Read(EntityHandle player, bool askGround, out ShownJumpBody body)
    {
        if (directory.Get(player) is not SsPlayer p)
        {
            body = default;
            return false;
        }

        PlayerNetState s = p.State;
        Vector o = s.Origin, v = s.Velocity;
        bool onPlayer = false;
        if (askGround && p.GroundIndex is int ground and >= 1 and < VisionPass.Slots)
            onPlayer = players.GetHandle(ground) != EntityHandle.None;
        body = new ShownJumpBody(new AiVector(o.X, o.Y, o.Z), new AiVector(v.X, v.Y, v.Z), (s.Flags & PlayerStances.FlOnGround) != 0, s.Ducked,
                                 s.WaterLevel != 0, s.MoveType == (byte)MoveType.Walk, onPlayer);
        return true;
    }

    public JumpBlast BlastOf(EntityHandle player) => Of(carriers.Part<TFBlastJumpState>(player.EntryIndex)?.State ?? TFBlastJump.None);

    /// <summary>The watch's blasts of the mod's blast jump state.</summary>
    public static JumpBlast Of(TFBlastJump state)
        => ((state & TFBlastJump.RocketJumped) != 0 ? JumpBlast.Own : JumpBlast.None)
           | ((state & TFBlastJump.StickyJumped) != 0 ? JumpBlast.OwnSticky : JumpBlast.None)
           | ((state & TFBlastJump.EnemyBlastedMe) != 0 ? JumpBlast.Other : JumpBlast.None);
}
