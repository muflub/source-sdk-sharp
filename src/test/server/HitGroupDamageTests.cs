using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>What a hit on each part of a player is worth (CBasePlayer::TraceAttack, cfg/skill.cfg).</summary>
public class HitGroupDamageTests
{
    private sealed class Owners(params IHitGroupDamageOwner[] owners) : IHooks<IHitGroupDamageOwner>
    {
        public IReadOnlyList<IHitGroupDamageOwner> All => owners;
        public void Each(string what, Action<IHitGroupDamageOwner> call) { foreach (var o in owners) call(o); }
    }

    private sealed class Owner : IHitGroupDamageOwner;

    private static ConVarRegistry ConVars()
    {
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(HitGroupDamage).Assembly);
        return convars;
    }

    private static DamageContext Hit(HitGroup group) => new(EntityHandle.None, EntityHandle.None, 10f, 0, group);

    [Theory]
    [InlineData(HitGroup.Head, 30f)]        // mod_hl2mp's skill.cfg: sk_player_head "3"
    [InlineData(HitGroup.Chest, 10f)]
    [InlineData(HitGroup.Stomach, 10f)]
    [InlineData(HitGroup.LeftArm, 10f)]
    [InlineData(HitGroup.RightArm, 10f)]
    [InlineData(HitGroup.LeftLeg, 10f)]
    [InlineData(HitGroup.RightLeg, 10f)]
    [InlineData(HitGroup.Generic, 10f)]     // a blast, a fall: no trace, no group
    [InlineData(HitGroup.Gear, 10f)]
    public void A_hit_is_worth_what_stocks_config_says(HitGroup group, float damage)
    {
        var scale = new HitGroupDamage(ConVars());
        DamageContext hit = Hit(group);

        Assert.Equal(damage, scale.Modify(in hit, 10f));
    }

    [Fact]
    public void The_running_damage_is_what_scales()
    {
        var scale = new HitGroupDamage(ConVars());
        DamageContext hit = Hit(HitGroup.Head);

        Assert.Equal(21f, scale.Modify(in hit, 7f));
    }

    [Fact]
    public void A_server_can_change_a_multiplier()
    {
        var convars = ConVars();
        var scale = new HitGroupDamage(convars);
        convars.Get<PlayerLegDamageConVar>()!.NotifyChanged("0.5", "1");
        DamageContext hit = Hit(HitGroup.LeftLeg);

        Assert.Equal(5f, scale.Modify(in hit, 10f));
    }

    /// <summary>A class game's locations are its own: stock's CTFPlayer::TraceAttack never multiplies.</summary>
    [Fact]
    public void A_mod_that_owns_hit_locations_turns_it_off()
    {
        var scale = new HitGroupDamage(ConVars(), new Owners(new Owner()));
        DamageContext hit = Hit(HitGroup.Head);

        Assert.Equal(10f, scale.Modify(in hit, 10f));
    }

    /// <summary>The whole route: the player's own damage path asks the rules, which ask this first.</summary>
    [Fact]
    public void A_headshot_takes_three_times_the_health()
    {
        var log = new RecordingLog();
        var convars = ConVars();
        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GiveItem", "Players.GiveAmmo")
                            .Returns("Players.GetTeam", 0)
                            .Returns("Players.RespawnDelay", 3f);
        var rules = new GameRules(log, convars, host, players, Fake().Returns("Entities.IsValid", true),
                                  new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog()),
                                  new PlayerDeathEvent(),
                                  damageModifiers: new Modifiers(new HitGroupDamage(convars)));
        var attacker = new EntityHandle(0x0002_0001u);

        int head = PlayerDamageRulesTests.Player(rules).OnDamagedAt(attacker, 5f, DamageType.Bullet, default, default, HitGroup.Head);
        int chest = PlayerDamageRulesTests.Player(rules).OnDamagedAt(attacker, 5f, DamageType.Bullet, default, default, HitGroup.Chest);

        Assert.Equal(15, head);
        Assert.Equal(5, chest);
    }

    private sealed class Modifiers(params IDamageModifier[] modifiers) : IHooks<IDamageModifier>
    {
        public IReadOnlyList<IDamageModifier> All => modifiers;
        public void Each(string what, Action<IDamageModifier> call) { foreach (var m in modifiers) call(m); }
    }
}
