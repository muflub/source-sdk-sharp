using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Server.Rules;

using Xunit;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The body a death leaves keeps the team and class it died on — stock's
/// ragdoll <c>m_iTeam</c>/<c>m_iClass</c> (tf_player.cpp:15625-15626), the
/// disguise included (:15870-15879) — so a corpse never changes colour or
/// model when its owner switches sides after dying (ruled 2026-11-02).
/// </summary>
public sealed class TFRagdollTeamTests
{
    private static readonly EntityHandle Player = new(0x0100_0001u);
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

    /// <summary>
    /// The roster seam, over the engine team store the corpse path reads: its
    /// <see cref="ChangeTeam"/> lands in the store, the chain the real
    /// adapter takes (TeamRoundAdapters.ChangeTeam → Teams.Join →
    /// IEnginePlayers's store).
    /// </summary>
    private sealed class Roster(RecordingEngine engine) : ITeamRoundRoster
    {
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => [];
        public TFTeam TeamOf(EntityHandle player) => (TFTeam)engine.Answer<int>("Players.GetTeam");
        public bool IsAlive(EntityHandle player) => false;
        public float DeathTime(EntityHandle player) => -1;
        public bool IsReadyToPlay(EntityHandle player) => false;
        public void ForceRespawn(EntityHandle player) { }

        public bool ChangeTeam(EntityHandle player, TFTeam team)
        {
            engine.Returns("Players.GetTeam", (int)team);
            return true;
        }

        public string Name(EntityHandle player) => string.Empty;
        public int EntIndex(EntityHandle player) => 3;
    }

    private static (RecordingEngine Engine, SsRagdoll Body, PlayerData Data, Roster Roster) Rig(int team)
    {
        var engine = Mock();
        engine.Returns("Players.GetTeam", team);
        var body = new SsRagdoll(engine) { Handle = Body };
        body.BindEngine(engine);
        body.State.BindLocal();
        var roster = new Roster(engine);
        return (engine, body, new PlayerData(), roster);
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

    private static void Die(TFRagdollTeam hook) =>
        hook.OnPlayerDied(new PlayerDeath(Player, 3, EntityHandle.None, default(Vector), Body));

    [Fact]
    public void ABodyCarriesTheTeamItDiedOn()
    {
        var (engine, body, data, roster) = Rig((int)TFTeam.Red);
        data.Players[3] = Carrier();
        data.Players[3].PlayerClass = (int)TFClass.Scout;

        Die(new TFRagdollTeam(new Directory(body), data, engine));

        Assert.Equal((int)TFTeam.Red, body.State.Team);
        Assert.Equal((int)TFClass.Scout, body.State.Class);
    }

    [Fact]
    public void ADisguisedSpysRealBodyIsTheSpys()
    {
        var (engine, body, data, roster) = Rig((int)TFTeam.Red);
        var spy = Carrier(TFCond.Disguised);
        spy.PlayerClass = (int)TFClass.Spy;
        spy.DisguiseTeam = (int)TFTeam.Blue;
        spy.DisguiseClass = (int)TFClass.HeavyWeapons;
        data.Players[3] = spy;

        Die(new TFRagdollTeam(new Directory(body), data, engine));

        // A real death's ragdoll is the player's own (tf_player.cpp:15625-15626);
        // only the Dead Ringer's fake corpse wears a disguise (:15870-15879).
        Assert.Equal((int)TFTeam.Red, body.State.Team);
        Assert.Equal((int)TFClass.Spy, body.State.Class);
    }

    [Fact]
    public void AnOwnersLaterTeamChangeLeavesTheBodyAlone()
    {
        var (engine, body, data, roster) = Rig((int)TFTeam.Red);
        data.Players[3] = Carrier();
        data.Players[3].PlayerClass = (int)TFClass.Scout;
        var hook = new TFRagdollTeam(new Directory(body), data, engine);
        Die(hook);
        Assert.Equal((int)TFTeam.Red, body.State.Team);

        // The owner switches sides after the death, the way the balance does
        // — and the corpse the death left stays the corpse it was.
        Assert.True(roster.ChangeTeam(Player, TFTeam.Blue));
        Assert.Equal(TFTeam.Blue, roster.TeamOf(Player));
        Assert.Equal((int)TFTeam.Red, body.State.Team);
        Assert.Equal((int)TFClass.Scout, body.State.Class);
    }
}
