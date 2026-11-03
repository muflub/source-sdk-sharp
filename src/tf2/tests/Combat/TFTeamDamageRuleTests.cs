using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Tests.Buildings;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// CTFGameRules::FPlayerCanTakeDamage over CTeamplayRules' (tf_gamerules.cpp:9808,
/// teamplay_gamerules.cpp:361): no teammate damage with mp_friendlyfire 0, one's own
/// always, and a building hurts its builder and the enemy only.
/// </summary>
public sealed class TFTeamDamageRuleTests
{
    private static readonly EntityHandle Victim = new(0x0001_0001u);
    private static readonly EntityHandle Mate = new(0x0001_0002u);
    private static readonly EntityHandle Enemy = new(0x0001_0003u);
    private static readonly EntityHandle Trigger = new(0x0001_0040u);

    private static ConVarRegistry ConVars()
    {
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(MpFriendlyFireConVar).Assembly);
        return convars;
    }

    private static (TFTeamDamageRule Rule, ConVarRegistry ConVars) Players()
    {
        var engine = TestServices.Fake();
        engine.ReturnsEachCall("Players.GetTeam", () => engine.Calls[^1].Arg(0) is EntityHandle h && h == Enemy
                                                        ? (int)TFTeam.Blue : (int)TFTeam.Red);
        engine.ReturnsEachCall("Entities.IsPlayer", () => engine.Calls[^1].Arg(0) is EntityHandle h && h != Trigger);
        engine.Returns("Entities.IsValid", true);
        var convars = ConVars();
        return (new TFTeamDamageRule(convars, engine, engine, new NoEntities()), convars);
    }

    private static DamageContext Hit(EntityHandle victim, EntityHandle attacker) => new(victim, attacker, 50f, 0);

    [Fact]
    public void A_teammates_hit_is_refused_with_friendly_fire_off()
    {
        var (rule, _) = Players();

        Assert.Equal(Decision.No, rule.Decide(Hit(Victim, Mate)));
    }

    [Fact]
    public void A_teammates_hit_lands_with_friendly_fire_on()
    {
        var (rule, convars) = Players();
        convars.Get<MpFriendlyFireConVar>()!.NotifyChanged("1", "0");

        Assert.Equal(Decision.UseStockRule, rule.Decide(Hit(Victim, Mate)));
    }

    [Fact]
    public void An_enemys_hit_and_ones_own_land()
    {
        var (rule, _) = Players();

        Assert.Equal(Decision.UseStockRule, rule.Decide(Hit(Victim, Enemy)));
        Assert.Equal(Decision.UseStockRule, rule.Decide(Hit(Victim, Victim)));
    }

    [Fact]
    public void A_same_team_non_player_is_no_teammate()
    {
        var (rule, _) = Players();

        Assert.Equal(Decision.UseStockRule, rule.Decide(Hit(Victim, Trigger)));
    }

    [Fact]
    public void A_building_hurts_its_builder_and_the_enemy_never_their_teammates()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        EntityHandle mate = rig.AddBuilder(TFTeam.Red);
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => rig.Engine.Calls[^1].Arg(0) is EntityHandle h && h == enemy
                                                            ? (int)TFTeam.Blue : (int)TFTeam.Red);
        var rule = new TFTeamDamageRule(ConVars(), rig.Engine, rig.Engine, rig.Registry);

        Assert.Equal(Decision.UseStockRule, rule.Decide(Hit(builder, sentry.Handle)));
        Assert.Equal(Decision.UseStockRule, rule.Decide(Hit(enemy, sentry.Handle)));
        Assert.Equal(Decision.No, rule.Decide(Hit(mate, sentry.Handle)));
    }
}
