using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The player's class component (the desired class, the round's readiness),
/// the per-frame condition countdown, the class models' precache, and the
/// composite's TF block in its declared order.
/// </summary>
public sealed class TFPlayerClassTests
{
    private static (PlayerRig Rig, TFPlayerClass Class, TFPlayerConditions Conditions, TFPlayerConditionTicker Ticker) Make()
    {
        var rig = new PlayerRig();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var cls = new TFPlayerClass(rig.Carriers, rig.Items);
        var ticker = new TFPlayerConditionTicker(rig.Carriers, conditions, rig.Engine);
        rig.Extra.Add(cls);
        rig.Extra.Add(ticker);
        rig.Join();
        return (rig, cls, conditions, ticker);
    }

    [Fact]
    public void A_player_is_ready_once_they_have_chosen_a_class()
    {
        var (rig, cls, _, _) = Make();
        var readiness = new TFPlayerReadiness(rig.Carriers);
        Assert.False(readiness.HasChosenClass(PlayerRig.Player));

        cls.Desired = TFClass.Medic;

        Assert.True(readiness.HasChosenClass(PlayerRig.Player));
    }

    [Fact]
    public void The_class_component_is_the_players_part_until_they_leave()
    {
        var (rig, cls, _, _) = Make();
        Assert.Same(cls, rig.Carriers.Part<TFPlayerClass>(PlayerRig.Index));

        rig.Leave();

        Assert.Null(rig.Carriers.Part<TFPlayerClass>(PlayerRig.Index));
    }

    [Fact]
    public void The_composite_ticks_the_countdown_and_an_expiry_leaves_the_carrier()
    {
        var (rig, _, conditions, _) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Burning, 1f);

        Frame(rig, 10f);
        Frame(rig, 10.5f);
        Assert.True(rig.State.InCond(TFCond.Burning));

        Frame(rig, 11.25f);

        Assert.False(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void A_permanent_condition_outlives_the_countdown()
    {
        var (rig, _, conditions, ticker) = Make();
        conditions.Add(PlayerRig.Player, TFCond.Zoomed);

        ticker.Tick(1f);
        ticker.Tick(100f);

        Assert.True(rig.State.InCond(TFCond.Zoomed));
    }

    /// <summary>ConditionThink's ground-contact arm (tf_player_shared.cpp:3124-3128):
    /// landing ends the airborne conditions; LostFooting is timed, not landed out.</summary>
    [Fact]
    public void Landing_ends_the_knocked_into_air_and_air_current_conditions()
    {
        var (rig, _, conditions, ticker) = Make();
        conditions.Add(PlayerRig.Player, TFCond.KnockedIntoAir);
        conditions.Add(PlayerRig.Player, TFCond.AirCurrent);
        conditions.Add(PlayerRig.Player, TFCond.LostFooting, 5f);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.OnGround);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        ticker.Tick(10f);

        Assert.False(rig.State.InCond(TFCond.KnockedIntoAir));
        Assert.False(rig.State.InCond(TFCond.AirCurrent));
        Assert.True(rig.State.InCond(TFCond.LostFooting));
    }

    [Fact]
    public void A_knocked_into_air_player_still_in_the_air_keeps_the_conditions()
    {
        var (rig, _, conditions, ticker) = Make();
        conditions.Add(PlayerRig.Player, TFCond.KnockedIntoAir);
        conditions.Add(PlayerRig.Player, TFCond.AirCurrent);
        rig.Engine.Returns("Networking.EntityGetState", EntityState.None);
        rig.Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);

        ticker.Tick(10f);

        Assert.True(rig.State.InCond(TFCond.KnockedIntoAir));
        Assert.True(rig.State.InCond(TFCond.AirCurrent));
    }

    [Fact]
    public void The_tf_block_runs_carrier_class_countdown_stun_override()
    {
        var rig = new PlayerRig();
        var source = new TFPlayerComponents(rig.Engine, new SingleEntity(rig.Entity), rig.Carriers, rig.Log, rig.Items,
                                            new TFPlayerConditions(rig.Carriers, rig.Engine),
                                            new TF2.Tests.Entities.Items.FakeMapPlayers(), new TF2.Tests.Rules.FakeClock(), rig.Engine,
                                            rig.Schema, new PoollessGiver(), rig.Engine, rig.Engine);

        var block = source.Create(PlayerRig.Player).ToList();

        Assert.Collection(block,
            c => Assert.IsType<TFPlayerCarrier>(c),
            c => Assert.IsType<TFPlayerClass>(c),
            c => Assert.IsType<TFPlayerConditionTicker>(c),
            c => Assert.IsType<TFBlastJumpState>(c),
            c => Assert.IsType<TFTauntThink>(c),
            c => Assert.IsType<TFStunStack>(c),
            c => Assert.IsType<TFRespawnOverride>(c),
            c => Assert.IsType<TFPlayerAttributes>(c),
            c => Assert.IsType<TFAmmoPoolMirror>(c),
            c => Assert.IsType<TFWeaponClipMirror>(c));
        Assert.Equal(block.Select(c => c.Order).Order(), block.Select(c => c.Order));
    }

    [Fact]
    public void Every_class_model_and_hands_are_precached_at_level_init()
    {
        var rig = new PlayerRig();
        rig.Engine.Returns("Precache.PrecacheModel", 1);
        IGameLifecycle models = new TFClassModels(rig.Classes, rig.Engine);

        models.OnLevelInitPreEntity("ctf_2fort");

        var precached = rig.Engine.AllOf("Precache.PrecacheModel").Select(c => c.ArgAs<string>(0)).ToHashSet();
        Assert.Contains("models/player/scout.mdl", precached);
        Assert.Contains("models/player/heavy.mdl", precached);
        Assert.Contains("models/weapons/c_models/c_scout_arms.mdl", precached);
        Assert.True(precached.Count >= 18, $"{precached.Count} models precached for nine classes");
    }

    private static void Frame(PlayerRig rig, float now)
    {
        rig.Engine.Returns("Clock.Now", now);
        ((IFrameLifecycle)rig.Composites).OnThink(0.015f);
    }

    private sealed class SingleEntity(TFPlayerDataEntity entity) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => h == PlayerRig.CarrierHandle ? entity : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == TFEntityNames.PlayerData;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}
