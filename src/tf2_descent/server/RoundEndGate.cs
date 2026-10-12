using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server;

/// <summary>
/// TF2Descent has no round end. Its progression is level hops, and a hosted
/// process never changes map: the host refuses a second Booting from a live
/// instance, and the level change's disconnects release every player's lease
/// (k3s run, 2026-10-11). So the core's round end, from a frag or time limit,
/// game_end or cs_sv_end_round, is ignored here entirely: no scoreboard, no
/// frozen players, no level change.
/// </summary>
[SideService(Side.Server)]
public sealed class RoundEndGate : IRoundEndGate
{
    public bool MayEndRound() => false;
}
