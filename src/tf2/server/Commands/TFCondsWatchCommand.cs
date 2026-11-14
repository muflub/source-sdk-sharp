using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Commands;

/// <summary>
/// The ticking half of <c>tf2_conds_watch</c>: one step the command hands in,
/// run once a frame until its seconds are up or the step says the player is
/// gone. A holder service beside the other frame thinkers — the command
/// itself is built fresh per console line, so the countdown cannot live on it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFCondsWatch : IFrameLifecycle
{
    /// <summary>The watch's frame slot: a stage of its own in TF2's thousand.</summary>
    public const int Order = ModStages.First - 1_000 + 600;

    private Func<bool>? _step;
    private float _secondsLeft;

    /// <summary>The feed runs at <see cref="TFObjectives.Order"/>, the autobalance at the default; neither ties this.</summary>
    int IFrameLifecycle.FrameOrder => Order;

    /// <summary>Arm the watch: <paramref name="step"/> runs once a frame for <paramref name="seconds"/>.</summary>
    public void Start(float seconds, Func<bool> step)
    {
        _step = step;
        _secondsLeft = seconds;
    }

    public void OnThink(float frameTime)
    {
        if (_step is not { } step)
            return;

        _secondsLeft -= frameTime;
        if (!step() || _secondsLeft <= 0f)
            _step = null;
    }
}

/// <summary>
/// <c>tf2_conds_watch &lt;player&gt; &lt;seconds&gt;</c>: the
/// <see cref="TFCondsCommand"/> line of one player once a server tick for a
/// few seconds, stopping at the tick the player is no longer alive — the
/// falling-edge reads (a jump's z through its arc, a cloak's fade) a lane
/// needs across a window rather than at one instant. Behind
/// <c>sv_cheats</c> as <c>tf2_conds</c> itself is.
/// </summary>
[ConCommand("tf2_conds_watch", Help = "Repeat the tf2_conds line for a player every tick. tf2_conds_watch <player> <seconds>")]
public sealed class TFCondsWatchCommand(ILog log, SourceSharp.Shared.SvCheatsConVar cheats, IEnginePlayers players,
                                        IEngineEntities entities, ITFPlayerData playerData, TFCondsWatch watch)
{
    /// <summary>The named player, watched for <paramref name="seconds"/>, dying included.</summary>
    public void Execute(int index, float seconds)
    {
        if (!cheats.Value)
        {
            log.Warning("[TF2/Conds] tf2_conds_watch runs only under sv_cheats\n");
            return;
        }

        EntityHandle player = players.GetHandle(index);
        if (!entities.IsValid(player))
        {
            log.Warning($"[TF2/Conds] tf2_conds_watch has no player {index}\n");
            return;
        }

        watch.Start(seconds, () => TFCondsCommand.Print(log, players, entities, playerData, player, index));
    }
}
