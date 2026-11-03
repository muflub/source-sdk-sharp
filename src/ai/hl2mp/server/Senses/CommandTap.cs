using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// Every player's usercmd into the recording (plan_hl2mp_ai §3.4.4): the
/// core's <see cref="IPlayerCommandObserver"/>, called for humans' commands
/// and bots' alike from the one command loop, so a person and a bot are
/// recorded by the same tap. Each command carries the server's tick; the
/// recording names the actor (a person's anonymised by the recorder). Costs
/// one check a command while nothing records.
/// </summary>
[SideService(Side.Server, As = [typeof(IPlayerCommandObserver)])]
public sealed class CommandTap(Hl2mpAiSession ai, Hl2mpBots bots, IEngineEntities entities, IEngineFrame frame) : IPlayerCommandObserver
{
    /// <summary>Commands written since the tap was made.</summary>
    public long Written { get; private set; }

    public void OnCommand(EntityHandle player, in UserCmd cmd)
    {
        if (!ai.Recording.Recording || player == EntityHandle.None)
            return;
        int slot = entities.GetIndex(player);
        if (slot is < 1 or >= VisionPass.Slots)
            return;
        ActorId actor = ActorOf(bots, slot, player);
        ai.Recording.UserCmd(new AiUserCmd(actor, new Tick(frame.TickCount()), cmd.CommandNumber, cmd.TickCount,
                                           new AiAngles(cmd.Pitch, cmd.Yaw, cmd.Roll), cmd.ForwardMove, cmd.SideMove, cmd.UpMove,
                                           cmd.Buttons, (byte)cmd.Impulse, cmd.WeaponSelect, 0, 0,
                                           (short)Math.Clamp(cmd.MouseDx, short.MinValue, short.MaxValue),
                                           (short)Math.Clamp(cmd.MouseDy, short.MinValue, short.MaxValue)));
        Written++;
    }

    /// <summary>The actor of the player in <paramref name="slot"/>: the bot there, else a person's.</summary>
    public static ActorId ActorOf(Hl2mpBots bots, int slot, EntityHandle player)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (b.Id.Slot == slot)
                return b.Id;
        }

        return ActorDirectory.HumanActor(slot, player);
    }
}
