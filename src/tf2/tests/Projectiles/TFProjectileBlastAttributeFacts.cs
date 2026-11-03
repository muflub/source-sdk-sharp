using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Movement;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The blast attributes of <c>CTFGameRules::RadiusDamage</c> and
/// <c>CTFPlayer::ApplyPushFromDamage</c>, read off the firing weapon's item:
/// <c>mult_dmg_falloff</c> (CalculateFalloff, tf_gamerules.cpp:5741-5744),
/// the own-pass arm's <c>add_health_on_radius_damage</c> with its
/// <c>player_healonhit</c> event (:5681-5703), and the self arms
/// <c>no_self_blast_dmg</c>, <c>blast_dmg_to_self</c>
/// (ApplyAndScaleDamage, :7476-7487) and <c>mult_dmgself_push_force</c>
/// (ApplyPushFromDamage, tf_player.cpp:10237-10239).
/// </summary>
public class TFProjectileBlastAttributeFacts
{
    private static TFBlast RocketBlast()
        => TFBlast.Of(90f, TFProjectileDefs.RocketRadius, TFWeaponDamageTypes.Of(TFWeaponId.Rocketlauncher));

    /// <summary>A victim at 60 units: the half-falloff rocket takes 71.5 of 90.</summary>
    private static void VictimAtSixty(ProjectileRig rig)
    {
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        // The victim is not a player, so stock scores it at the trace's end.
        rig.Engine.Returns("WorldQuery.TraceLine",
                           new TraceResult(true, 1f, new Vector(60f, 0f, 0f), default, EntityHandle.None));
    }

    /// <summary>
    /// The attacker, 100 out and the only one there — stock's own pass runs
    /// while the attacker is still at the blast, which is the one fact here
    /// that can jump.
    /// </summary>
    private static void SelfPassArmed(ProjectileRig rig)
    {
        rig.Engine.FillsSphere();
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(100f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(100f, 0f, 0f));
    }

    private static void ApplyRocket(ProjectileRig rig, float selfRadius = 0f, float selfBaseDamage = 0f)
        => rig.Blast.Apply(new Vector(0f, 0f, 0f), RocketBlast(), ProjectileRig.Player,
                           ProjectileRig.ProjectileHandle, ignored: ProjectileRig.ProjectileHandle,
                           TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher", critical: false,
                           selfRadius: selfRadius, selfBaseDamage: selfBaseDamage);

    private static float SeenDamage(ProjectileRig rig)
        => EngineCallFacts.ArgAs<float>(rig.Engine.Last("Damage.TakeDamageAnswered"), 2);

    [Fact]
    public void TheMultDmgFalloffWeaponKeepsTheEdgeDamageAtFull()
    {
        // CalculateFalloff (:5741-5744): flFalloffMod starts at 1, the hook
        // reads the item, and anything but 1 is ADDED (stock's quirk). The
        // rocket launcher's half falloff plus a 0.5 mod is a 1.0: the damage
        // no longer falls off at all, and the 71.5 at 60 of 146 is the full 90.
        var rig = new ProjectileRig();
        VictimAtSixty(rig);
        rig.Owners.Item = rig.AttributeItem("mult_dmg_falloff", AttributeDescriptionFormat.Percentage, 0.5f);

        ApplyRocket(rig);

        Assert.Equal(90f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void TheBlastDmgToSelfAttributeScalesTheOwnPass()
    {
        // ApplyAndScaleDamage (:7485): blast_dmg_to_self multiplies what the
        // own pass delivers. At 100 of the 121 jump radius the 90 has fallen
        // to 52.8; half of that is the 26.4.
        var rig = new ProjectileRig();
        SelfPassArmed(rig);
        rig.Owners.Item = rig.AttributeItem("blast_dmg_to_self", AttributeDescriptionFormat.Percentage, 0.5f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(26.4f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void TheNoSelfBlastDamageAttributeZeroesTheOwnPassEntirely()
    {
        // ApplyAndScaleDamage (:7476-7483): iNoSelfBlastDamage == 2 (all)
        // zeroes the damage before blast_dmg_to_self can scale it — and the
        // push rides the damage, so nothing moves either.
        var rig = new ProjectileRig();
        SelfPassArmed(rig);
        rig.Owners.Item = rig.AttributeItem("no_self_blast_dmg", AttributeDescriptionFormat.Additive, 2f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamageAnswered"));
        Assert.Equal(0, rig.Engine.CountOf("Entities.ApplyVelocityImpulse"));
    }

    [Fact]
    public void TheNoSelfBlastDamageSingleOnlySparesThePracticeSticky()
    {
        // :7479-7483: == 1 zeroes only the practice sticky (custom
        // TF_DMG_CUSTOM_PRACTICE_STICKY). This port has no practice mode —
        // the blasts carry custom None — so == 1 spares a rocket's own pass,
        // which is what the 52.8 (90 at 100 of 121) here holds.
        var rig = new ProjectileRig();
        SelfPassArmed(rig);
        rig.Owners.Item = rig.AttributeItem("no_self_blast_dmg", AttributeDescriptionFormat.Additive, 1f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(52.8f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void TheSelfPushForceAttributeScalesOnlyTheJump()
    {
        // ApplyPushFromDamage's self branch (tf_player.cpp:10237-10239):
        // mult_dmgself_push_force multiplies the self impulse whole. Same rig
        // without the item first, so the scale is measured against the
        // impulse the fact's own blast actually produced.
        Vector Impulse(ProjectileRig rig)
            => rig.Engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1);

        var bare = new ProjectileRig();
        SelfPassArmed(bare);
        bare.Engine.Returns("Entities.GetBounds",
                            (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, TFViewVectors.Params.HullMax.Z)));
        ApplyRocket(bare, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);
        var plain = Impulse(bare);

        var rig = new ProjectileRig();
        SelfPassArmed(rig);
        rig.Engine.Returns("Entities.GetBounds",
                           (new Vector(-24f, -24f, 0f), new Vector(24f, 24f, TFViewVectors.Params.HullMax.Z)));
        rig.Owners.Item = rig.AttributeItem("mult_dmgself_push_force", AttributeDescriptionFormat.Percentage, 0.5f);
        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(plain * 0.5f, Impulse(rig));
    }

    [Fact]
    public void TheRocketJumpDmgReductionScalesASelfBlastThatHitNoEnemy()
    {
        // ApplyAndScaleDamage (:7449-7458): a self blast — attacker == victim,
        // the blast bit set, no TAUNTATK_GRENADE custom — whose
        // DamagedOtherPlayers is still 0 reads rocket_jump_dmg_reduction off
        // the attacker's own manager. At 100 of the 121 jump radius the 90
        // has fallen to 52.8; half of that is the 26.4.
        var rig = new ProjectileRig();
        SelfPassArmed(rig);
        rig.Owners.Manager = rig.AttributeItem("rocket_jump_dmg_reduction", AttributeDescriptionFormat.Percentage, 0.5f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(26.4f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void ASelfBlastAfterAnEnemyWasHurtKeepsItsReduction()
    {
        // :7451 — the gate is stock's DamagedOtherPlayers == 0, stamped with
        // iDamageEnemies only when an own pass is armed (:5673). The sweep
        // here hurts an enemy player first, so the armed manager's 0.5 must
        // NOT reach the 52.8 own pass that follows it.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim, ProjectileRig.Player);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is SourceSharp.Abi.EntityHandle h && h == ProjectileRig.Victim ? 3 : 2);
        rig.Engine.ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
            rig.Engine.Calls[^1].Args[0] is SourceSharp.Abi.EntityHandle h && h == ProjectileRig.Victim
                ? new Vector(60f, 0f, 0f) : new Vector(100f, 0f, 0f));
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () =>
            rig.Engine.Calls[^1].Args[0] is SourceSharp.Abi.EntityHandle h && h == ProjectileRig.Victim
                ? new Vector(60f, 0f, 0f) : new Vector(100f, 0f, 0f));
        rig.Owners.Manager = rig.AttributeItem("rocket_jump_dmg_reduction", AttributeDescriptionFormat.Percentage, 0.5f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(52.8f, SeenDamage(rig), 1f);
    }

    [Fact]
    public void TheDamageForceReductionScalesThePushFromAnotherHand()
    {
        // ApplyPushFromDamage's other-than-self arm (tf_player.cpp:10465-10467):
        // the victim's own manager's damage_force_reduction scales vecForce.
        // Same rig without the manager first, so the scale is measured
        // against the impulse the fact's own blast actually produced.
        Vector Impulse(ProjectileRig rig)
            => rig.Engine.Last("Entities.ApplyVelocityImpulse")!.ArgAs<Vector>(1);

        var bare = new ProjectileRig();
        VictimAtSixty(bare);
        bare.Engine.Returns("Entities.IsPlayer", true);
        ApplyRocket(bare);
        var plain = Impulse(bare);

        var rig = new ProjectileRig();
        VictimAtSixty(rig);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Owners.Manager = rig.AttributeItem("damage_force_reduction", AttributeDescriptionFormat.Percentage, 0.5f);
        ApplyRocket(rig);

        Assert.Equal(plain * 0.5f, Impulse(rig));
    }

    [Fact]
    public void TheRadiusDamageHealScalesWithTheDamageDoneAndFiresPlayerHealonhit()
    {
        // The own-pass arm (tf_gamerules.cpp:5681-5703): add_health_on_radius_damage
        // on the weapon, scaled by nDamageDealt / flBaseDamage — the enemy
        // takes the blast's full 90, so the scale is 1 and the whole 10 lands.
        // Stock heals before the second sweep and, when anything healed,
        // fires player_healonhit {amount, entindex, weapon_def_index}.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is SourceSharp.Abi.EntityHandle h && h == ProjectileRig.Victim ? 3 : 2);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.ReturnsEachCall("Damage.TakeDamageAnswered", () => 90); // full: the scale reads as one hit
        rig.Engine.Returns("Entities.GetIndex", 3); // the attacker's edict index
        rig.Engine.ReturnsEachCall("GameEvents.EventCreate", () => new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Owners.Item = rig.AttributeItem("add_health_on_radius_damage", AttributeDescriptionFormat.Additive, 10f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        var created = rig.Engine.Last("GameEvents.EventCreate");
        Assert.NotNull(created);
        Assert.Equal("player_healonhit", created!.ArgAs<string>(0));
        var sets = rig.Engine.Calls.Where(c => c.Member == "GameEvents.EventSetInt").ToList();
        Assert.Equal(
            new (string, int)[] { ("amount", 10), ("entindex", 3), ("weapon_def_index", -1) },
            sets.Select(c => (c.ArgAs<string>(1), c.ArgAs<int>(2))).ToArray());
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    [Fact]
    public void AHealScaledIntoNothingFiresNoEvent()
    {
        // :5691 and :5696-5703: the scale truncates the attribute
        // (iModHealthOnHit = (int)(fl * flScale)) — a mod of 10 against the 1
        // the rig's default answers on 90 base is (int)(10/90) = 0, healed is
        // 0, and no player_healonhit goes out.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is SourceSharp.Abi.EntityHandle h && h == ProjectileRig.Victim ? 3 : 2);
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Engine.ReturnsEachCall("Damage.TakeDamageAnswered", () => 1); // scale = 1/90
        rig.Owners.Item = rig.AttributeItem("add_health_on_radius_damage", AttributeDescriptionFormat.Additive, 10f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    [Fact]
    public void TeammateDamageHealsNothing()
    {
        // :5676-5679: only victims NOT InSameTeam with the attacker feed
        // nDamageDealt. The blast still hurts the teammate — it just earns
        // the shooter no health and no event.
        var rig = new ProjectileRig();
        rig.Engine.FillsSphere(ProjectileRig.Victim);
        rig.Engine.Returns("Entities.IsPlayer", true); // same team as the attacker by the rig's default
        rig.Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(60f, 0f, 0f));
        rig.Engine.Returns("Entities.GetOrigin", new Vector(60f, 0f, 0f));
        rig.Owners.Item = rig.AttributeItem("add_health_on_radius_damage", AttributeDescriptionFormat.Additive, 10f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 90f);

        Assert.Equal(2, rig.Engine.CountOf("Damage.TakeDamageAnswered")); // the teammate and the own pass
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }

    [Fact]
    public void TheHealArmIsGatedOnEnemyDamageAndBaseDamage()
    {
        // :5668: `if (info.flRJRadius && pAttacker)` around
        // `if (nDamageDealt > 0)` — and the scale divides by the base damage,
        // so an unarmed own pass (no radius, no base) can neither heal nor
        // divide by zero.
        var rig = new ProjectileRig();
        SelfPassArmed(rig);
        rig.Owners.Item = rig.AttributeItem("add_health_on_radius_damage", AttributeDescriptionFormat.Additive, 10f);

        ApplyRocket(rig, selfRadius: TFProjectileDefs.RocketRadiusForRocketJumps, selfBaseDamage: 0f);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));

        ApplyRocket(rig); // no own pass armed at all
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));
    }
}
