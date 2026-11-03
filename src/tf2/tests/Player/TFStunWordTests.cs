using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The stun stack's carrier words — the legacy <c>m_iStunFlags</c> the
/// <c>UpdateLegacyStunSystem</c> mirror copies for the network
/// (tf_player_shared.cpp:7441-7453) and the movement's stun arm reads. The
/// stack's own behaviour is the trigger lane's (TriggerStunTests); these hold
/// the mirror's copy alone — and stock leaves the value words stale behind a
/// cleared index (:3185, :1025), which every reader gates on first
/// (:9938-9975), so the stale word is inert.
/// </summary>
public sealed class TFStunWordTests
{
    private static (PlayerRig Rig, TFStunStack Stack) Make()
    {
        var rig = new PlayerRig();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers);
        var stack = new TFStunStack(rig.Carriers, players, rig.Engine);
        rig.Extra.Add(stack);
        rig.Join();
        return (rig, stack);
    }

    [Fact]
    public void The_active_stuns_flags_word_rides_the_carrier()
    {
        var (rig, stack) = Make();

        stack.Stun(PlayerRig.Player, 3f, 0.5f, TFStunFlags.LoserState | TFStunFlags.ByTrigger, EntityHandle.None);

        Assert.Equal((int)(TFStunFlags.LoserState | TFStunFlags.ByTrigger),
                     rig.Carriers.Get(PlayerRig.Index)!.StunFlags);
    }

    [Fact]
    public void The_word_goes_stale_when_the_last_stun_expires()
    {
        var (rig, stack) = Make();
        stack.Stun(PlayerRig.Player, 3f, 0.5f, TFStunFlags.LoserState, EntityHandle.None);

        stack.Tick(4f);   // past the 3 s expiry

        // Stock's UpdateLegacyStunSystem clears m_iStunIndex (:3185) and copies
        // the VALUES only behind an active entry (:7446-7453): the word goes
        // stale, as stock's does, and every reader gates on the index and the
        // condition first (:9938-9975) — so the stale word is inert (the speed
        // and movement facts hold that arm).
        var state = rig.Carriers.Get(PlayerRig.Index)!;
        Assert.Equal(-1, state.StunIndex);
        Assert.Equal((int)TFStunFlags.LoserState, state.StunFlags);
    }

    [Fact]
    public void The_word_is_stocked_zero_without_a_stun()
    {
        var (rig, _) = Make();

        Assert.Equal(0, rig.Carriers.Get(PlayerRig.Index)!.StunFlags);
    }
}
