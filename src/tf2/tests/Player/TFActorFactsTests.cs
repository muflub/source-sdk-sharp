using System.Reflection;

using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;

using TF2.Tests.Spy;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="ITFActorFacts"/>: what a client is sent about a player and would
/// draw, answered read-only by player index from the networked carrier and the
/// roster — the disguise as rendered with the borrowed index (stock's
/// <c>m_hDisguiseTarget</c>, whose name the target id prints for an enemy
/// viewer, tf_hud_target_id.cpp:248), the invisibility level as the client
/// draws it, and nothing a client is not sent.
/// </summary>
public sealed class TFActorFactsTests
{
    /// <summary>The roster every fact answers through: these handles are on Red.</summary>
    internal sealed class ActorRoster(params EntityHandle[] red) : ITeamRoundRoster
    {
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => [.. red];
        public TFTeam TeamOf(EntityHandle player) => red.Contains(player) ? TFTeam.Red : TFTeam.Unassigned;
        public bool IsAlive(EntityHandle player) => true;
        public float DeathTime(EntityHandle player) => -1f;
        public bool IsReadyToPlay(EntityHandle player) => true;
        public void ForceRespawn(EntityHandle player) { }
        public bool ChangeTeam(EntityHandle player, TFTeam team) => false;
        public string Name(EntityHandle player) => "player";
        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    private static TFServerActorFacts SpyService(SpyRig rig)
        => new(rig.States, new ActorRoster(SpyRig.Spy), rig.Engine, rig.Clock);

    private static TFServerActorFacts PlayerService(PlayerRig rig)
    {
        rig.Engine.Returns("Clock.Now", 0f);
        return new TFServerActorFacts(rig.Carriers, new ActorRoster(PlayerRig.Player), rig.Engine, rig.Engine);
    }

    /// <summary>The disguised spy's facts carry the disguise fields and the victim's index.</summary>
    [Fact]
    public void ADisguisedSpyReportsTheDisguiseAndTheBorrowedIndex()
    {
        var rig = new SpyRig();
        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));

        Assert.True(SpyService(rig).TryRead(SpyRig.Spy.EntryIndex, out TFActorFacts facts));
        Assert.Equal(TFClass.HeavyWeapons, facts.DisguiseClass);
        Assert.Equal(TFTeam.Blue, facts.DisguiseTeam);
        Assert.Equal(SpyRig.Victim.EntryIndex, facts.DisguiseTargetIndex);
        Assert.True(facts.Disguised);
    }

    /// <summary>The cloak's fade is what the client draws: half-way at the
    /// fade's half-time, fully invisible when the change completes.</summary>
    [Fact]
    public void ACloakedSpysPercentInvisibleRises()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 100f;
        rig.Conditions.Add(SpyRig.Spy, TFCond.Stealthed, provider: SpyRig.Spy);
        rig.State.InvisChangeCompleteTime = 101f;   // cloaked at 100 over the invis time's 1.0 s
        rig.Clock.Now = 100.5f;

        Assert.True(SpyService(rig).TryRead(SpyRig.Spy.EntryIndex, out TFActorFacts half));
        Assert.Equal(0.5f, half.PercentInvisible, 4);

        rig.Clock.Now = 101f;
        Assert.True(SpyService(rig).TryRead(SpyRig.Spy.EntryIndex, out TFActorFacts full));
        Assert.Equal(1f, full.PercentInvisible, 4);
    }

    /// <summary>A player who never disguised: the civilian disguise with no
    /// borrowed name, and the render flags clear.</summary>
    [Fact]
    public void AnUndisguisedPlayerReportsNoDisguise()
    {
        var rig = new PlayerRig().As(TFClass.Scout);

        Assert.True(PlayerService(rig).TryRead(PlayerRig.Index, out TFActorFacts facts));
        Assert.Equal(TFClass.Scout, facts.Class);
        Assert.Equal(TFTeam.Red, facts.Team);
        Assert.Equal(TFClass.Civilian, facts.DisguiseClass);
        Assert.Equal(TFTeam.Unassigned, facts.DisguiseTeam);
        Assert.Equal(0, facts.DisguiseTargetIndex);
        Assert.False(facts.Disguised);
        Assert.False(facts.Cloaked);
        Assert.Equal(0f, facts.PercentInvisible, 4);
    }

    /// <summary>A slot with no carrier answers false.</summary>
    /// <summary>A medic's charge is the invulnerable condition
    /// (g_MedigunEffects' first row, tf_weapon_medigun.cpp:51), which stock's
    /// IsInvulnerable reads (tf_player_shared.cpp:8099); the medigun resists
    /// are the Vaccinator's, a charge type of their own.</summary>
    [Fact]
    public void AnUberchargedPlayerReportsUbered()
    {
        var rig = new SpyRig();
        rig.Conditions.Add(SpyRig.Spy, TFCond.Invulnerable);

        Assert.True(SpyService(rig).TryRead(SpyRig.Spy.EntryIndex, out TFActorFacts facts));
        Assert.True(facts.Ubered);
    }

    [Fact]
    public void TryReadIsFalseForAnEmptySlot()
    {
        var rig = new PlayerRig();

        Assert.False(PlayerService(rig).TryRead(PlayerRig.Index, out _));
    }

    /// <summary>The record carries what a client is sent and rendered; the
    /// server-only numbers (health, clip, reserve ammo) are not members.</summary>
    [Fact]
    public void TFActorFactsCarriesNoHealthOrClip()
    {
        HashSet<string> names = [];
        foreach (PropertyInfo property in typeof(TFActorFacts).GetProperties())
            names.Add(property.Name);

        foreach (string name in new[]
                 {
                     "Class", "Team", "DisguiseClass", "DisguiseTeam", "DisguiseTargetIndex",
                     "PercentInvisible", "AirDash", "Burning", "Stunned", "Ubered", "Disguised", "Cloaked", "Taunting",
                 })
            Assert.Contains(name, names);

        foreach (string name in new[] { "Health", "Clip", "Reserve", "Ammo" })
            Assert.DoesNotContain(name, names);
    }
}
