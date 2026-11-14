using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>A victim that is not a player; whether it runs the damage rules is the fact's to flip.</summary>
internal sealed class OtherVictim : ScriptedEntity
{
    public bool Runs { get; set; } = true;

    public override bool RunsDamageRules => Runs;
}

/// <summary>
/// <see cref="TFDamageLayer"/> over a victim that is not a player and runs the
/// damage rules (<see cref="ScriptedEntity.RunsDamageRules"/>): stock's
/// <c>CTFBaseBoss::OnTakeDamage</c> calling <c>ApplyOnDamageModifyRules</c>
/// (<c>tf_base_boss.cpp:442</c>) with no <c>pVictim</c>. The attacker's half of
/// the rules reaches it with the number the same hit gives a player who wears
/// no condition; the arms stock keeps behind <c>if ( pVictim )</c> do not; and
/// a victim that did not opt in takes the hit as dealt. Each fact hits the
/// rig's player and the entity from the same attacker at the same range (the
/// rig answers every centre but the attacker's with the origin).
/// </summary>
public sealed class TFDamageLayerOtherVictimTests
{
    private static readonly EntityHandle Other = new(0x0001_0040u);

    private const string RocketLauncher = "tf_weapon_rocketlauncher";
    private const float Rocket = 90f;

    private static (CombatRig Rig, OtherVictim Entity) Rig(float attackerX = 512f, string weapon = RocketLauncher)
    {
        var rig = new CombatRig { AttackerWeapon = weapon, AttackerAt = new Vector(attackerX, 0f, 0f) };
        var entity = new OtherVictim { Handle = Other };
        rig.Entities.Placed[Other] = entity;
        return (rig, entity);
    }

    //-------------------------------------------------------------------------
    // The attacker's half reaches it, with a player's number.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_critical_rocket_hits_it_as_hard_as_it_hits_a_player()
    {
        var (rig, _) = Rig();

        float player = rig.Hit(Rocket, DamageType.Blast, critical: true);
        float entity = rig.Hit(Rocket, DamageType.Blast, critical: true, victim: Other);

        Assert.Equal(270f, player, 3);      // TF_DAMAGE_CRIT_MULTIPLIER 3, and a crit takes no fall-off
        Assert.Equal(player, entity, 3);
    }

    [Theory]
    [InlineData(128f)]      // ramp-up: the rocket launchers' halved near bonus
    [InlineData(1500f)]     // fall-off: the band's floor
    public void A_rockets_range_reaches_it_as_it_reaches_a_player(float distance)
    {
        var (rig, _) = Rig(attackerX: distance);

        float player = rig.Hit(Rocket, DamageType.Blast);
        float entity = rig.Hit(Rocket, DamageType.Blast, victim: Other);

        Assert.True(MathF.Abs(player - Rocket) > 1f, $"the range moved the player's number off the damage as dealt ({player})");
        Assert.Equal(player, entity, 3);
    }

    [Fact]
    public void A_rocket_falls_off_at_long_range_on_it()
    {
        var (near, _) = Rig(attackerX: 512f);
        var (far, _) = Rig(attackerX: 1500f);

        float atOptimum = near.Hit(Rocket, DamageType.Blast, victim: Other);
        float atRange = far.Hit(Rocket, DamageType.Blast, victim: Other);

        Assert.Equal(Rocket, atOptimum, 3);
        Assert.True(atRange < atOptimum * 0.6f, $"long range takes about half of the rocket ({atRange} of {atOptimum})");
    }

    /// <summary>
    /// <c>mult_dmg</c> is the weapon's own (<c>GetProjectileDamage</c>, our
    /// <c>TFWeaponHooks.ModifyDamage</c>): it is in the shot before any victim
    /// sees it, so it reaches this victim through the same rules as a
    /// player's hit does.
    /// </summary>
    [Fact]
    public void An_attackers_damage_bonus_reaches_it_as_it_reaches_a_player()
    {
        const string scattergun = "tf_weapon_scattergun";
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(scattergun, clip: 6, reserve: 32);
        ShotDamage plain = world.Behaviour(scattergun).ModifyShot(world.Attack(w, vm, 10f, TFWeaponWorld.GunSeed(200, 10000)), default);

        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(9001, "damage bonus", "mult_dmg", AttributeDescriptionFormat.Percentage,
                                                               AttributeEffectType.Positive, false, false, AttributeValueType.Default,
                                                               MergedKeys.Block("x")), 1.5f));
        world.Owners.Items[TFWeaponWorld.Owner] = item;
        ShotDamage bonus = world.Behaviour(scattergun).ModifyShot(world.Attack(w, vm, 10f, TFWeaponWorld.GunSeed(200, 10000)), default);
        Assert.Equal((6f, 9f), (plain.Damage, bonus.Damage));

        var (rig, _) = Rig(attackerX: 256f, weapon: scattergun);
        float player = rig.Hit(bonus.Damage, DamageType.Bullet);
        float entity = rig.Hit(bonus.Damage, DamageType.Bullet, victim: Other);
        float entityPlain = rig.Hit(plain.Damage, DamageType.Bullet, victim: Other);

        Assert.Equal(player, entity, 3);
        Assert.Equal(entityPlain * 1.5f, entity, 3);
    }

    [Fact]
    public void A_buffed_attackers_mini_crit_reaches_it_as_it_reaches_a_player()
    {
        var (rig, _) = Rig();
        rig.AttackerState.SetConditions(TFCondMask.Of(TFCond.Offensebuff));

        float player = rig.Hit(Rocket, DamageType.Blast);
        float entity = rig.Hit(Rocket, DamageType.Blast, victim: Other);

        Assert.Equal(Rocket * TFDamage.MinicritMultiplier, player, 3);
        Assert.Equal(player, entity, 3);
    }

    /// <summary>
    /// Stock asks <c>mult_dmg_vs_players</c> of the weapon for any victim that
    /// reaches the modify rules (<c>tf_gamerules.cpp:6394-6398</c>, behind
    /// <c>if ( pWeapon )</c> alone), the boss included.
    /// </summary>
    [Fact]
    public void The_weapons_vs_players_multiplier_reaches_it_as_stock_asks_it_of_any_victim()
    {
        var (rig, _) = Rig();
        rig.AttackerItem("mult_dmg_vs_players", AttributeDescriptionFormat.Percentage, 2f);

        float player = rig.Hit(Rocket, DamageType.Blast);
        float entity = rig.Hit(Rocket, DamageType.Blast, victim: Other);

        Assert.Equal(180f, player, 3);
        Assert.Equal(player, entity, 3);
    }

    //-------------------------------------------------------------------------
    // The opt-in.
    //-------------------------------------------------------------------------

    /// <summary>The opt-in is what matters: the same entity, the same critical rocket, the flag flipped.</summary>
    [Fact]
    public void A_victim_that_does_not_run_the_rules_takes_the_damage_as_dealt()
    {
        var (rig, entity) = Rig();

        float runs = rig.Hit(Rocket, DamageType.Blast, critical: true, victim: Other);
        entity.Runs = false;
        float plain = rig.Hit(Rocket, DamageType.Blast, critical: true, victim: Other);

        Assert.Equal((270f, Rocket), (runs, plain));
    }

    /// <summary>
    /// A building keeps its own damage rule (<c>mult_dmg_vs_buildings</c> in
    /// <c>TFBaseObject.OnDamaged</c>, which <c>TFBaseObjectTests</c> pins): none
    /// opts in, so none of this reaches one.
    /// </summary>
    [Fact]
    public void No_building_runs_the_rules()
    {
        Type[] buildings = typeof(TFBaseObject).Assembly.GetTypes()
            .Where(t => typeof(TFBaseObject).IsAssignableFrom(t)).ToArray();
        string[] optedIn = buildings
            .Where(t => EntityOpMask.Overrides(t, "get_" + nameof(ScriptedEntity.RunsDamageRules)))
            .Select(t => t.Name).ToArray();

        Assert.Contains(typeof(TFSentryGun), buildings);       // the walk reached the sentry
        Assert.True(optedIn.Length == 0, "buildings that run the damage rules: " + string.Join(", ", optedIn));
    }

    //-------------------------------------------------------------------------
    // The victim's half does not.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>or_crit_vs_not_playercond</c> stands inside stock's
    /// <c>if ( pVictim )</c> (<c>tf_gamerules.cpp:5959-6042</c>): a player who
    /// is not in the listed condition is crit, and a victim that is not a
    /// player has no conditions to be "not in".
    /// </summary>
    [Fact]
    public void A_crit_against_players_not_in_a_condition_does_not_crit_it()
    {
        var (rig, _) = Rig();
        rig.AttackerItem("or_crit_vs_not_playercond", AttributeDescriptionFormat.Or, 1f);     // bit 0: burning

        float player = rig.Hit(Rocket, DamageType.Blast);
        float entity = rig.Hit(Rocket, DamageType.Blast, victim: Other);

        Assert.Equal((270f, Rocket), (player, entity));
    }

    /// <summary>
    /// <c>mult_dmg_vs_nonburning</c> is asked behind
    /// <c>if ( pVictim &amp;&amp; !InCond( TF_COND_BURNING ) )</c>
    /// (<c>tf_gamerules.cpp:6413-6431</c>): a player's, never this victim's.
    /// </summary>
    [Fact]
    public void The_non_burning_multiplier_does_not_scale_it()
    {
        var (rig, _) = Rig();
        rig.AttackerItem("mult_dmg_vs_nonburning", AttributeDescriptionFormat.Percentage, 0.5f);

        float player = rig.Hit(Rocket, DamageType.Blast);
        float entity = rig.Hit(Rocket, DamageType.Blast, victim: Other);

        Assert.Equal((45f, Rocket), (player, entity));
    }

    /// <summary>
    /// The books are the player's: the hit record a kill is read from and the
    /// attacker's ranged-damage stat (<c>Event_PlayerDamage</c>, the tail of
    /// <c>CTFPlayer::OnTakeDamage</c>) take nothing from a hit on it.
    /// </summary>
    [Fact]
    public void A_hit_on_it_writes_no_player_record_and_no_player_damage_stat()
    {
        var (rig, _) = Rig();

        rig.Hit(Rocket, DamageType.Blast, victim: Other);
        int afterEntity = rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged);
        rig.Hit(Rocket, DamageType.Blast);
        int afterPlayer = rig.Stats.Round(CombatRig.Attacker.EntryIndex, TFStat.DamageRanged);

        Assert.Null(rig.Records.LastHit(Other.EntryIndex));
        Assert.NotNull(rig.Records.LastHit(CombatRig.Victim.EntryIndex));     // the control: a player's hit is booked
        Assert.Equal((0, 90), (afterEntity, afterPlayer));
    }
}
