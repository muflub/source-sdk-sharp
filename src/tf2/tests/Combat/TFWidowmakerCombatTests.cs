using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Tests.Buildings;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// The Widowmaker's damage-layer arms: the sentry's mark
/// (<c>CTFPlayer::ModifyDamageInfo</c>, tf_player_shared.cpp:10185-10204) and
/// the on-hit refund (<c>CTFWeaponBase::ApplyOnHitAttributes</c>' first arm,
/// tf_weaponbase.cpp:5059-5079).
/// </summary>
public sealed class TFWidowmakerCombatTests
{
    /// <summary>A built sentry whose aim is on an enemy in sight, its builder with it.</summary>
    private static (BuildingsRig Buildings, TFSentryGun Sentry, EntityHandle Builder, EntityHandle Enemy) AimedSentry()
    {
        var buildings = new BuildingsRig();
        buildings.StubWorld();
        EntityHandle builder = buildings.AddBuilder(TFTeam.Red);
        var sentry = buildings.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        buildings.Clock.Now = 100f;
        sentry.StartBuilding();
        buildings.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();   // the base think's last tick: constructs to full and goes active

        EntityHandle enemy = buildings.AddBuilder(TFTeam.Blue);
        buildings.Centers[enemy] = new Vector(300f, 0f, TFSentryGun.EyeHeightForLevel(1));
        buildings.FillsSphere(enemy);
        sentry.OnThink("SentryThink");   // the find
        return (buildings, sentry, builder, enemy);
    }

    /// <summary>The combat rig the aimed sentry stands on: the builder an
    /// engineer whose held item is <paramref name="item"/>, the enemy a body.</summary>
    private static CombatRig RigFor(TFSentryGun sentry, EntityHandle builder, EntityHandle enemy, bool marked)
    {
        var rig = new CombatRig();
        rig.Players.Add(builder.EntryIndex, TFClass.Engineer);
        rig.Players.Add(enemy.EntryIndex, TFClass.Scout);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () =>
            rig.Engine.Calls.Count > 0
            && rig.Engine.Calls[^1].Args.Length > 0
            && rig.Engine.Calls[^1].Args[0] is EntityHandle h
            && (h == CombatRig.Victim || h == CombatRig.Attacker || h == CombatRig.Third
                || h == builder || h == enemy));
        rig.Owners.Held[builder.EntryIndex] = "tf_weapon_shotgun_primary";
        if (marked)
            rig.ItemOn(builder, Mark, AttributeDescriptionFormat.Percentage, 1.5f);
        rig.Entities.Placed[sentry.Handle] = sentry;
        return rig;
    }

    /// <summary>The mark, as stock prices it: half again the bullet.</summary>
    private const string Mark = "mult_dmg_bullet_vs_sentry_target";

    [Fact]
    public void TheHeldMarkHitsTheSentrysTargetForMore()
    {
        var (_, sentry, builder, enemy) = AimedSentry();
        CombatRig rig = RigFor(sentry, builder, enemy, marked: true);

        // The aim holds, so the twenty lands forty-five: the shotgun's
        // short-range band at contact (the 1.5 of :483) and the half again
        // the item's mark prices the builder's own target at.
        Assert.Equal(45f, rig.Hit(20f, DamageType.Bullet, attacker: builder, victim: enemy), 3);
    }

    [Fact]
    public void TheMarkWaitsForTheAim()
    {
        var (_, sentry, builder, enemy) = AimedSentry();
        CombatRig rig = RigFor(sentry, builder, enemy, marked: true);
        rig.Entities.Placed.Clear();   // no sentry the layer can see

        // Same mark, no sentry on the board: the band's thirty alone.
        Assert.Equal(30f, rig.Hit(20f, DamageType.Bullet, attacker: builder, victim: enemy), 3);
    }

    [Fact]
    public void TheHitPaysTheItemsShareIntoTheMetal()
    {
        var rig = new CombatRig();
        rig.Owners.Held[CombatRig.Attacker.EntryIndex] = "tf_weapon_pistol";
        rig.Owners.Items[CombatRig.Attacker.EntryIndex] =
            TFWidowmakerTests.Widowed(("add_onhit_addammo", 60f));
        rig.Engine.Ignores("Players.GiveAmmo");

        Assert.Equal(20f, rig.Hit(20f, DamageType.Bullet), 4);

        // Sixty percent of the twenty the hit was, into the pool the item's
        // metal arm names: the grant the next refusal reads.
        var grant = Assert.Single(rig.Engine.AllOf("Players.GiveAmmo"));
        Assert.Equal(CombatRig.Attacker, grant.ArgAs<EntityHandle>(0));
        Assert.Equal(12, grant.ArgAs<int>(1));
        Assert.Equal("TF_AMMO_METAL", grant.ArgAs<string>(2));
    }

    [Fact]
    public void NoItemPaysNothing()
    {
        var rig = new CombatRig();
        rig.Engine.Ignores("Players.GiveAmmo");

        Assert.Equal(20f, rig.Hit(20f, DamageType.Bullet), 4);
        Assert.Empty(rig.Engine.AllOf("Players.GiveAmmo"));
    }

    [Fact]
    public void ADisguisedUnstealthedSpyPaysNothing()
    {
        var rig = new CombatRig();
        rig.Players.Add(CombatRig.Victim.EntryIndex, TFClass.Spy).DisguiseTeam = (int)TFTeam.Blue;
        rig.Conditions.Add(CombatRig.Victim, TFCond.Disguised);
        rig.Owners.Held[CombatRig.Attacker.EntryIndex] = "tf_weapon_pistol";
        rig.Owners.Items[CombatRig.Attacker.EntryIndex] =
            TFWidowmakerTests.Widowed(("add_onhit_addammo", 60f));
        rig.Engine.Ignores("Players.GiveAmmo");

        Assert.Equal(20f, rig.Hit(20f, DamageType.Bullet), 4);
        Assert.Empty(rig.Engine.AllOf("Players.GiveAmmo"));
    }
}
