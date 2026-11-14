using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Triggers;
using TF2.Shared;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Entities.Triggers;

/// <summary>
/// trigger_add_tf_player_condition, trigger_remove_tf_player_condition and
/// trigger_ignite (stage 2.4): their <c>StartTouch</c>/<c>EndTouch</c> and
/// <c>BurnEntities</c> (tf_triggers.cpp).
/// </summary>
public sealed class TFTriggerTests
{
    private static MapItemRig Rig(Func<EntityHandle, bool>? isPlayer = null)
    {
        var rig = new MapItemRig();
        isPlayer ??= _ => true;
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () => isPlayer((EntityHandle)rig.Engine.Last("Entities.IsPlayer")!.Value.Arg(0)!))
                  .Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f));
        return rig;
    }

    private static string Num(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Entering_adds_the_condition_for_its_duration()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerAddTFPlayerCondition>(TriggerAddTFPlayerCondition.MapClass,
            ("condition", ((int)TFCond.SpeedBoost).ToString()), ("duration", Num(5f)));
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);

        Assert.Contains(TFCond.SpeedBoost, p.Conditions);
        Assert.Equal(5f, p.Durations[TFCond.SpeedBoost]);

        // Leaving does not take a timed condition away.
        rig.Registry.EndTouch(trigger.Handle, player);
        Assert.Contains(TFCond.SpeedBoost, p.Conditions);
    }

    [Fact]
    public void A_permanent_condition_lasts_while_inside()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerAddTFPlayerCondition>(TriggerAddTFPlayerCondition.MapClass,
            ("condition", ((int)TFCond.Critboosted).ToString()), ("duration", "-1"));
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);
        Assert.Contains(TFCond.Critboosted, p.Conditions);

        rig.Registry.EndTouch(trigger.Handle, player);
        Assert.DoesNotContain(TFCond.Critboosted, p.Conditions);
    }

    [Fact]
    public void Each_entry_adds_it_whatever_the_volumes_wait()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerAddTFPlayerCondition>(TriggerAddTFPlayerCondition.MapClass,
            ("condition", ((int)TFCond.SpeedBoost).ToString()), ("duration", "5"), ("wait", "10"));
        var (first, a) = rig.Players.Add();
        var (second, b) = rig.Players.Add();

        rig.Touch(trigger, first);
        rig.Touch(trigger, second);

        Assert.Contains(TFCond.SpeedBoost, a.Conditions);
        Assert.Contains(TFCond.SpeedBoost, b.Conditions);
    }

    [Fact]
    public void An_invalid_condition_adds_nothing()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerAddTFPlayerCondition>(TriggerAddTFPlayerCondition.MapClass, ("condition", "-1"));
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);

        Assert.Empty(p.Conditions);
    }

    [Fact]
    public void A_disabled_trigger_adds_nothing()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerAddTFPlayerCondition>(TriggerAddTFPlayerCondition.MapClass,
            ("condition", ((int)TFCond.SpeedBoost).ToString()), ("StartDisabled", "1"));
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);

        Assert.Empty(p.Conditions);
    }

    [Fact]
    public void Entering_removes_the_condition()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerRemoveTFPlayerCondition>(TriggerRemoveTFPlayerCondition.MapClass,
            ("condition", ((int)TFCond.Burning).ToString()));
        var (player, p) = rig.Players.Add();
        p.Conditions.Add(TFCond.Burning);
        p.Conditions.Add(TFCond.SpeedBoost);

        rig.Touch(trigger, player);

        Assert.Equal([TFCond.SpeedBoost], p.Conditions);
    }

    [Fact]
    public void An_invalid_condition_removes_every_condition()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerRemoveTFPlayerCondition>(TriggerRemoveTFPlayerCondition.MapClass, ("condition", "-1"));
        var (player, p) = rig.Players.Add();
        p.Conditions.Add(TFCond.Burning);
        p.Conditions.Add(TFCond.SpeedBoost);

        rig.Touch(trigger, player);

        Assert.Empty(p.Conditions);
    }

    [Fact]
    public void Ignite_sets_an_entering_player_alight_for_the_burn_duration()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass, ("burn_duration", "4"));
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);

        Assert.Equal([4f], p.Burns);
        Assert.True(trigger.Burning);
        Assert.Equal(TriggerIgnite.BurnInterval, trigger.NextThinkDelay());
    }

    [Fact]
    public void Ignite_spares_the_invulnerable_and_the_already_burning()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass);
        var (uber, u) = rig.Players.Add();
        var (burning, b) = rig.Players.Add();
        u.Conditions.Add(TFCond.Invulnerable);
        b.Conditions.Add(TFCond.Burning);

        rig.Touch(trigger, uber);
        rig.Touch(trigger, burning);

        Assert.Empty(u.Burns);
        Assert.Empty(b.Burns);
    }

    [Fact]
    public void Ignite_burns_a_percent_of_max_health_per_second_of_the_think()
    {
        var rig = Rig();
        rig.Engine.Ignores("Damage.TakeDamage");
        var trigger = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass, ("damage_percent_per_second", "20"));
        var (player, p) = rig.Players.Add();
        p.MaxHealth = 150;

        rig.Clock.Now = 10f;
        rig.Touch(trigger, player);
        rig.Clock.Now = 10.5f;
        trigger.OnThink();

        // 0.5 s * 20 % * 150 = 15 of DMG_BURN, by the trigger.
        var hit = rig.Engine.Last("Damage.TakeDamage")!.Value;
        Assert.Equal(player, hit.Arg(0));
        Assert.Equal(trigger.Handle, hit.Arg(1));
        Assert.Equal(15f, (float)hit.Arg(2)!, 3);
        Assert.Equal(SourceSharp.Abi.DamageType.Burn, hit.Arg(3));
        Assert.Equal(TriggerIgnite.BurnInterval, trigger.NextThinkDelay(), 3);
    }

    [Fact]
    public void Ignite_stops_thinking_once_the_volume_is_empty()
    {
        var rig = Rig();
        rig.Engine.Ignores("Damage.TakeDamage");
        var trigger = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass);
        var (player, _) = rig.Players.Add();

        rig.Touch(trigger, player);
        rig.Registry.EndTouch(trigger.Handle, player);
        trigger.OnThink();

        Assert.False(trigger.Burning);
        Assert.Equal(0, rig.Engine.CountOf("Damage.TakeDamage"));
    }

    [Fact]
    public void Ignite_removes_a_non_player_only_when_the_map_lets_one_in()
    {
        var prop = new EntityHandle(0x0070_0001u);
        var rig = Rig(h => h != prop);
        var unflagged = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass);
        var everything = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass, ("spawnflags", "64"));
        rig.Engine.Returns("Entities.GetSpawnFlags", 64);

        rig.Touch(everything, prop);
        Assert.Equal([prop], rig.Engine.AllOf("Entities.Remove").Select(c => (EntityHandle)c.Arg(0)!));

        rig.Engine.Returns("Entities.GetSpawnFlags", 0);
        rig.Touch(unflagged, prop);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    // ---- the particle arms (row 55, tf_triggers.cpp:645-679, 719-798) -------

    [Fact]
    public void Ignite_dispatches_its_named_particle_at_the_toucher_before_the_sound()
    {
        var rig = Rig();
        rig.Engine.Returns("Precache.ParticleSystemIndex", 7);
        var trigger = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass,
            ("ignite_particle_name", "explosion"), ("ignite_sound_name", "Fire.Ignite"));
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);

        var (name, data, filter, calls) = Assert.Single(rig.Temps.Dispatched);
        Assert.Equal("ParticleEffect", name);
        // The world-position overload: no entity rides the record (tf_triggers.cpp:651).
        Assert.Equal(7, data.HitBox);
        Assert.Equal(0, data.EntIndex);
        Assert.Equal(0, data.Flags);
        Assert.Equal(0, data.DamageType);
        Assert.Equal(new Vector(1f, 2f, 3f), data.Origin);
        Assert.Equal(new Vector(1f, 2f, 3f), data.Start);
        Assert.Equal(TeFilterKind.Pas, filter.Kind);

        // Stock's order: the particle, THEN the sound (:645-665). The engine
        // had `calls` whole calls when the dispatch went out, so the call at
        // that index is the one that followed it.
        Assert.Equal("Sound.EmitSoundAtPosition", rig.Engine.Calls[calls].Member);
        Assert.NotEmpty(p.Burns);
    }

    [Fact]
    public void Ignite_without_a_particle_name_dispatches_nothing()
    {
        var rig = Rig();
        var trigger = rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass);
        var (player, p) = rig.Players.Add();

        rig.Touch(trigger, player);

        Assert.Empty(rig.Temps.Dispatched);
        Assert.NotEmpty(p.Burns);
    }

    [Fact]
    public void Ignite_precaches_its_named_particle_on_spawn()
    {
        var rig = Rig();

        rig.Spawn<TriggerIgnite>(TriggerIgnite.MapClass, ("ignite_particle_name", "explosion"));

        Assert.Equal(["explosion"],
            rig.Engine.AllOf("Precache.PrecacheParticleSystem").Select(c => (string)c.Arg(0)!));
    }

    [Fact]
    public void Entering_trigger_particle_dresses_the_toucher()
    {
        var rig = Rig();
        rig.Engine.Returns("Precache.ParticleSystemIndex", 4);
        var trigger = rig.Spawn<TriggerParticle>(TriggerParticle.MapClass, ("particle_name", "explosion"));
        var (player, _) = rig.Players.Add();

        rig.Touch(trigger, player);

        var (name, data, filter, _) = Assert.Single(rig.Temps.Dispatched);
        Assert.Equal("ParticleEffect", name);
        Assert.Equal(4, data.HitBox);
        Assert.Equal(player.EntryIndex, data.EntIndex);
        Assert.Equal(1, data.Flags);
        // The constructor's PATTACH_ABSORIGIN default, no attachment named: the
        // PAS filter of the origin (particle_parse.cpp:328-338).
        Assert.Equal(0, data.DamageType);
        Assert.Equal(-1, data.AttachmentIndex);
        Assert.Equal(TeFilterKind.Pas, filter.Kind);
    }

    [Fact]
    public void Trigger_particle_follows_the_named_attachment_to_everyone()
    {
        var rig = Rig();
        rig.Engine.Returns("Precache.ParticleSystemIndex", 4)
                  .Returns("Entities.LookupAttachmentAny", 3);
        var trigger = rig.Spawn<TriggerParticle>(TriggerParticle.MapClass,
            ("particle_name", "explosion"), ("attachment_name", "muzzle"), ("attachment_type", "4"));
        var (player, _) = rig.Players.Add();

        rig.Touch(trigger, player);

        var (name, data, filter, _) = Assert.Single(rig.Temps.Dispatched);
        Assert.Equal(4, data.DamageType);
        Assert.Equal(3, data.AttachmentIndex);
        // The follow attach types take the broadcast filter (particle_parse.cpp:329-334).
        Assert.Equal(TeFilterKind.Broadcast, filter.Kind);
    }

    [Fact]
    public void A_missing_precache_dispatches_the_error_system()
    {
        var rig = Rig();
        rig.Engine.Returns("Precache.ParticleSystemIndex", 0);
        var trigger = rig.Spawn<TriggerParticle>(TriggerParticle.MapClass);
        var (player, _) = rig.Players.Add();

        rig.Touch(trigger, player);

        var (_, data, _, _) = Assert.Single(rig.Temps.Dispatched);
        Assert.Equal(0, data.HitBox);
    }

    [Fact]
    public void Trigger_particle_precaches_its_system_on_spawn()
    {
        var rig = Rig();

        rig.Spawn<TriggerParticle>(TriggerParticle.MapClass, ("particle_name", "explosion"));

        Assert.Equal(["explosion"],
            rig.Engine.AllOf("Precache.PrecacheParticleSystem").Select(c => (string)c.Arg(0)!));
    }

    // ---- trigger_ignite_arrows (tf_triggers.cpp:173-232) ---------------------

    /// <summary>The rig of the bow facts: a world whose one carrier holds a
    /// compound bow, an eye at the origin plane looking along
    /// <paramref name="eyeAngles"/>, and the trigger's origin ten east.</summary>
    private static (MapItemRig Rig, EntityHandle Bow) BowWorld(Vector eyeAngles)
    {
        var rig = new MapItemRig();
        rig.Registry.RegisterAll(typeof(SsWeapon).Assembly);
        rig.Engine.Returns("Entities.IsPlayer", true)
                  .Returns("Entities.GetOrigin", new Vector(10f, 0f, 0f))      // the trigger's origin
                  .Returns("Entities.GetEyePosition", Vector.Zero)             // the eye
                  .Returns("Entities.GetEyeAngles", eyeAngles)
                  .Returns("Players.GetActiveWeapon", "tf_weapon_compound_bow");
        var bow = rig.NewHandle();
        rig.Engine.Returns("Players.GetActiveWeaponEntity", bow);
        rig.Registry.Spawn(bow, EntityNames.SsWeapon);
        return (rig, bow);
    }

    /// <summary>The Touch order of stock (:202-231): a Sniper facing the
    /// trigger origin with the bow held lights the arrow (:230).</summary>
    [Fact]
    public void Entering_as_a_facing_sniper_lights_the_held_bows_arrow()
    {
        // Straight ahead (:216-223): forward (1,0,0) against the eye-to-origin
        // direction (1,0,0), dot 1 over the 0.95 threshold.
        var (rig, bowHandle) = BowWorld(new Vector(0f, 0f, 0f));
        var trigger = rig.Spawn<TriggerIgniteArrows>(TriggerIgniteArrows.MapClass);
        var (player, _) = rig.Players.Add(playerClass: TFClass.Sniper);

        rig.Touch(trigger, player);

        Assert.True(((SsWeapon)rig.Registry.Get(bowHandle)!).State.ArrowAlight);
    }

    /// <summary>The dot gate (:222): the same bow held by a Sniper whose back
    /// is turned answers nothing — the arrow stays dark.</summary>
    [Fact]
    public void Entering_while_facing_away_leaves_the_arrow_dark()
    {
        // Facing west while the origin lies east: forward (-1,0,0) against
        // (1,0,0), dot -1 under the threshold.
        var (rig, bowHandle) = BowWorld(new Vector(0f, 180f, 0f));
        var trigger = rig.Spawn<TriggerIgniteArrows>(TriggerIgniteArrows.MapClass);
        var (player, _) = rig.Players.Add(playerClass: TFClass.Sniper);

        rig.Touch(trigger, player);

        Assert.False(((SsWeapon)rig.Registry.Get(bowHandle)!).State.ArrowAlight);
    }

    /// <summary>The class gate (:212-214): a Soldier facing the bow-held
    /// origin still gets no lit arrow.</summary>
    [Fact]
    public void Entering_as_a_non_sniper_leaves_the_arrow_dark()
    {
        var (rig, bowHandle) = BowWorld(new Vector(0f, 0f, 0f)); // the facing geometry
        var trigger = rig.Spawn<TriggerIgniteArrows>(TriggerIgniteArrows.MapClass);
        var (player, _) = rig.Players.Add(); // the rig's default Soldier

        rig.Touch(trigger, player);

        Assert.False(((SsWeapon)rig.Registry.Get(bowHandle)!).State.ArrowAlight);
    }
}
