using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Combat;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using RevengePurse = TF2.Shared.Player.TFRevengeCrits;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Manmelter (<c>CTFFlareGun_Revenge</c>, <c>tf_weapon_flaregun.cpp:386-632</c>):
/// the vacuum's frames on the held secondary, the shot that spends a banked
/// crit, and the beam that puts a burning teammate out for one.
/// </summary>
public sealed class TFManmelterFacts
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string Manmelter = "tf_weapon_flaregun_revenge";

    private static int _nextDef = 5600;

    /// <summary>An item holding one attribute under the named hook class (the shell facts' shape).</summary>
    private static IAttributeProvider ItemWith(string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>A world over the extras, whose owner carries a predicted carrier: the count the shot spends lives there.</summary>
    private static TFWeaponWorld World(TFPlayerNetState carrier)
    {
        var data = new FakePredictedData { Carrier = carrier };
        carrier.BindLocal();
        return new(data, files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));
    }

    [Fact]
    public void The_manmelter_row_is_the_projectiles()
    {
        var world = World(new TFPlayerNetState());
        Assert.Equal("tf2.projectile", world.Row(Manmelter).Behaviour);
    }

    [Fact]
    public void The_held_trigger_asks_the_vacuum_and_stamps_the_half_second()
    {
        var carrier = new TFPlayerNetState();
        var world = World(carrier);
        var (w, vm) = world.Armed(Manmelter, clip: -1, reserve: 20);

        // The command's own frame tail reads its buttons already (RunCommand
        // stamps LastButtons before the tail), so the first held frame is
        // both the charge's start and the refire's stamp (:497, :515).
        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, Attack2, 10.015f);

        var beam = Assert.Single(world.Effects.ExtinguishBeams);
        Assert.Equal(Manmelter, beam);
        Assert.Equal(10.5f, w.NextSecondaryAttack, 4);
        Assert.True(w.BehaviourTimeA > 0f);
    }

    [Fact]
    public void The_vacuums_clock_holds_the_next_ask_half_a_second()
    {
        var carrier = new TFPlayerNetState();
        var world = World(carrier);
        var (w, vm) = world.Armed(Manmelter, clip: -1, reserve: 20);

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, Attack2, 10.015f);
        world.Run(w, vm, Attack2, 10.03f);
        world.Run(w, vm, Attack2, 10.5f);

        Assert.Single(world.Effects.ExtinguishBeams);

        // Past the stamp (10.015 + 0.5), the held beam asks again — the stamp
        // the ask's yes alone earns (:547).
        world.Run(w, vm, Attack2, 10.6f);

        Assert.Equal(2, world.Effects.ExtinguishBeams.Count);
    }

    [Fact]
    public void The_released_trigger_ends_the_charge()
    {
        var carrier = new TFPlayerNetState();
        var world = World(carrier);
        var (w, vm) = world.Armed(Manmelter, clip: -1, reserve: 20);

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, Attack2, 10.015f);
        Assert.True(w.BehaviourTimeA > 0f);

        // The release reads a command late, as every held-test here does.
        world.Run(w, vm, 0, 10.03f);
        world.Run(w, vm, 0, 10.045f);

        Assert.Equal(0f, w.BehaviourTimeA, 4);
    }

    [Fact]
    public void The_shot_spends_a_banked_crit_and_the_last_drops_the_condition()
    {
        var carrier = new TFPlayerNetState();
        var world = World(carrier);
        carrier.RevengeCrits = 2;
        var conds = new TFConditions(authoritative: true);
        world.Owners.Conditions[TFWeaponWorld.Owner] = conds;
        conds.Add(TFCond.Critboosted);
        var (w, vm) = world.Armed(Manmelter, clip: -1, reserve: 20);

        world.Run(w, vm, Attack, 10f);

        Assert.Equal(1, carrier.RevengeCrits);
        Assert.True(conds.Has(TFCond.Critboosted));

        // TimeFireDelay 2.0: the next shot is the fixture's own delay away.
        world.Run(w, vm, Attack, 12.6f);

        Assert.Equal(0, carrier.RevengeCrits);
        Assert.False(conds.Has(TFCond.Critboosted));
    }

    [Fact]
    public void The_energy_weapon_spends_no_ammo()
    {
        var carrier = new TFPlayerNetState();
        var world = World(carrier);
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith(TFProjectileBehaviour.NoAmmoAttribute,
                                                           AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Manmelter, clip: 4, reserve: 20);

        world.Run(w, vm, Attack, 10f);

        Assert.Single(world.Effects.Projectiles);
        Assert.Equal(4, w.Clip);
        Assert.Equal(20, w.Reserve);
    }

    [Fact]
    public void The_held_press_puts_the_burning_teammate_out_through_the_real_beam()
    {
        var rig = new FlameRig();
        BeamWorld(rig, Burner());
        rig.Engine.ReturnsEachCall("GameEvents.EventCreate", () => new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Owners.Items[TFWeaponWorld.Owner] = rig.AttributeItem("extinguish_restores_health",
                                                                  AttributeDescriptionFormat.Additive, 20f);

        // The press a player presses, the beam the server speaks: the world's
        // parts speak through the real effects composite over the rig's
        // TFFlameEffects, whose engine stubs answer as the live side does —
        // the run reaches the real ExtinguishBeam, burner, grant, condition
        // and heal in one.
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()),
                                      effects: OwnedEffects.Of(flames: rig.Effects));
        var (w, vm) = world.Armed(Manmelter, clip: -1, reserve: 20);

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, Attack2, 10.015f);

        Assert.Equal((FlameRig.Victim, TFCond.Burning), Assert.Single(rig.Conditions.Removed));
        Assert.Equal(1, rig.Carriers.Get(TFWeaponWorld.Owner)?.RevengeCrits);
        Assert.Contains((FlameRig.Player, TFCond.Critboosted), rig.Conditions.Added.Select(a => (a.Player, a.Cond)));
        var healed = Assert.Single(rig.Health.HealthTaken);
        Assert.Equal((FlameRig.Player, 20), (healed.Who, healed.Amount));
    }

    // ---- the beam (the server's arm, TFFlameEffects.ExtinguishBeam) ---------

    private static WeaponAttackContext BeamAttack() => new(default!, null!, null!, 100f, true,
        null!, new WeaponCommand(1, 5, TFWeaponWorld.Owner));

    /// <summary>The Manmelter's script, from the extras set (the fixture lives beside its README).</summary>
    private static TFWeaponScript ManmelterScript()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get(Manmelter)
            ?? throw new InvalidOperationException("no script " + Manmelter);
    }

    /// <summary>
    /// A real player_data carrier under the owner, attached as the composite
    /// does (the tray facts' shape), with the index read per handle so the
    /// burn registry's keys are the entries.
    /// </summary>
    private static void AttachCarrier(FlameRig rig)
    {
        var carrierHandle = new EntityHandle(0x0004_0020u);
        rig.Engine.Returns("Entities.Create", carrierHandle);
        rig.Engine.ReturnsEachCall("Entities.GetIndex", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h ? h.EntryIndex : -1);
        var data = new TFPlayerDataEntity(rig.Engine);
        data.State.BindLocal();
        rig.Directory.Holds(carrierHandle, data);
        new TFPlayerCarrier(rig.Engine, rig.Directory, rig.Carriers, rig.Log).Attach(FlameRig.Player);
    }

    /// <summary>The victim's burn, attached and set from <paramref name="burner"/>: the vacuum's read.</summary>
    private static TFAfterburn IgniteVictim(FlameRig rig, EntityHandle burner)
    {
        var slice = (TFAfterburn)Assert.Single(rig.Afterburns.Create(FlameRig.Victim));
        slice.Attach(FlameRig.Victim);
        slice.Burn.Ignite(100f, alive: true, TFClass.Pyro, default, burner,
                          TFWeaponId.Flamethrower, "tf_weapon_flamethrower");
        return slice;
    }

    private static EntityHandle Burner() => new(0x0001_0000u | 7);

    /// <summary>
    /// The beam's world: the carrier attached, the victim's burn set from
    /// <paramref name="burner"/>, the hull on the victim, and the teams — the
    /// victim and the blaster Red, the burner Blue.
    /// </summary>
    private static void BeamWorld(FlameRig rig, EntityHandle burner)
    {
        AttachCarrier(rig);
        IgniteVictim(rig, burner);
        rig.Engine.Returns("WorldQuery.TraceHull", new HullResult(false, false, FlameRig.Victim));
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h
                ? h == Burner() ? 3 : 2
                : 2);
        rig.Conditions.Grant(FlameRig.Victim, TFCond.Burning);
    }

    [Fact]
    public void The_vacuum_puts_a_burning_teammate_out_and_banks_the_enemy_burners_crit()
    {
        var rig = new FlameRig();
        BeamWorld(rig, Burner());

        Assert.True(rig.Effects.ExtinguishBeam(BeamAttack(), ManmelterScript()));

        Assert.Equal((FlameRig.Victim, TFCond.Burning), Assert.Single(rig.Conditions.Removed));
        Assert.Equal(1, rig.Carriers.Get(TFWeaponWorld.Owner)?.RevengeCrits);
        Assert.Contains((FlameRig.Player, TFCond.Critboosted), rig.Conditions.Added.Select(a => (a.Player, a.Cond)));
    }

    [Fact]
    public void The_vacuums_grant_is_the_setters_clamp()
    {
        var rig = new FlameRig();
        BeamWorld(rig, Burner());
        rig.Carriers.Get(TFWeaponWorld.Owner)!.RevengeCrits = RevengePurse.MaxCrits;

        Assert.True(rig.Effects.ExtinguishBeam(BeamAttack(), ManmelterScript()));

        Assert.Equal(RevengePurse.MaxCrits, rig.Carriers.Get(TFWeaponWorld.Owner)?.RevengeCrits);
    }

    [Fact]
    public void A_teammates_burn_banks_nothing()
    {
        var rig = new FlameRig();
        var teammateBurner = new EntityHandle(0x0001_0000u | 9);
        BeamWorld(rig, teammateBurner);

        Assert.True(rig.Effects.ExtinguishBeam(BeamAttack(), ManmelterScript()));

        var carrier = rig.Carriers.Get(TFWeaponWorld.Owner);
        Assert.Equal(0, carrier?.RevengeCrits);
        Assert.DoesNotContain((FlameRig.Player, TFCond.Critboosted), rig.Conditions.Added.Select(a => (a.Player, a.Cond)));
    }

    [Fact]
    public void An_enemys_burn_is_no_vacuum()
    {
        var rig = new FlameRig();
        rig.Engine.Returns("WorldQuery.TraceHull", new HullResult(false, false, FlameRig.Victim));
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () =>
            rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == FlameRig.Victim ? 3 : 2);

        Assert.False(rig.Effects.ExtinguishBeam(BeamAttack(), ManmelterScript()));
        Assert.Empty(rig.Conditions.Removed);
    }

    [Fact]
    public void A_teammate_standing_unburnt_is_no_vacuum()
    {
        var rig = new FlameRig();
        rig.Engine.Returns("WorldQuery.TraceHull", new HullResult(false, false, FlameRig.Victim));
        rig.Engine.Returns("Entities.IsPlayer", true);
        rig.Engine.Returns("Players.GetTeam", 2);

        Assert.False(rig.Effects.ExtinguishBeam(BeamAttack(), ManmelterScript()));
        Assert.Empty(rig.Conditions.Removed);
    }

    [Fact]
    public void The_enemys_burn_pays_the_extinguish_heal()
    {
        var rig = new FlameRig();
        BeamWorld(rig, Burner());
        rig.Engine.ReturnsEachCall("GameEvents.EventCreate", () => new IntPtr(7));
        rig.Engine.Returns("GameEvents.EventFire", true);
        rig.Owners.Items[TFWeaponWorld.Owner] = rig.AttributeItem("extinguish_restores_health",
                                                                  AttributeDescriptionFormat.Additive, 20f);

        Assert.True(rig.Effects.ExtinguishBeam(BeamAttack(), ManmelterScript()));

        var healed = Assert.Single(rig.Health.HealthTaken);
        Assert.Equal((FlameRig.Player, 20), (healed.Who, healed.Amount));

        var created = rig.Engine.Last("GameEvents.EventCreate");
        Assert.NotNull(created);
        Assert.Equal("player_healonhit", created!.ArgAs<string>(0));
        var sets = rig.Engine.Calls.Where(c => c.Member == "GameEvents.EventSetInt").ToList();
        // -1: a bare hook item names no defindex, the airblast heal's own
        // answer for one.
        Assert.Equal(
            new (string, int)[] { ("amount", 20), ("entindex", 3), ("weapon_def_index", -1) },
            sets.Select(c => (c.ArgAs<string>(1), c.ArgAs<int>(2))).ToArray());
    }
}
