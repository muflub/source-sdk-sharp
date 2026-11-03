using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The death that says whether its body arrived burning — stock's
/// <c>DT_TFRagdoll</c> networks <c>m_bBurning</c> (c_tf_player.cpp:518-542)
/// and the CLIENT ragdoll acts on it, stamping its own burn clock and
/// starting the corpse's particle (c_tf_player.cpp:928-932). The server
/// copies nothing else: the clock is the client's, not the wire's.
/// </summary>
public sealed class TFBurnRagdollTests
{
    private static readonly EntityHandle Body = new(0x0100_0002u);

    private sealed class Directory(ScriptedEntity? body) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => handle == Body ? body : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == EntityNames.Hl2mpRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class PlayerData : ITFPlayerData
    {
        public readonly Dictionary<int, TFPlayerNetState> Players = [];

        public TFPlayerNetState? Predicted(int playerIndex) => Players.GetValueOrDefault(playerIndex);

        public TFPlayerNetState? Networked(int playerIndex) => Players.GetValueOrDefault(playerIndex);
    }

    private static TFPlayerNetState Carrier(params TFCond[] conds)
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        TFCondMask mask = default;
        foreach (TFCond cond in conds)
            mask |= TFCondMask.Of(cond);
        TFPlayerNetStateViews.SetConditions(state, mask);
        return state;
    }

    private static SsRagdoll Corpse()
    {
        var engine = Mock();
        var body = new SsRagdoll(engine) { Handle = Body };
        body.BindEngine(engine);
        body.State.BindLocal();
        return body;
    }

    [Fact]
    public void A_player_who_died_alight_leaves_the_body_burning()
    {
        var body = Corpse();
        var data = new PlayerData();
        data.Players[3] = Carrier(TFCond.Burning);
        var hook = new TFBurnRagdoll(new Directory(body), data);

        hook.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), Body));

        Assert.True(body.State.Burning);
    }

    [Fact]
    public void A_death_without_a_burn_leaves_the_body_cold()
    {
        var body = Corpse();
        var data = new PlayerData();
        data.Players[3] = Carrier();
        var hook = new TFBurnRagdoll(new Directory(body), data);

        hook.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), Body));

        Assert.False(body.State.Burning);

        // A body the death never made (or none at all) has nothing to write.
        var noBody = new TFBurnRagdoll(new Directory(null), data);
        noBody.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), EntityHandle.None));
    }
}
