using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFDeathNotice"/>: the <c>player_death</c> fields
/// <c>CTFGameRules::DeathNotice</c> sends, from the killing hit the layer
/// recorded.
/// </summary>
public sealed class TFDeathNoticeTests
{
    internal sealed class Fields : IDeathNoticeFields
    {
        public Dictionary<string, object> Set { get; } = new();
        public void SetInt(string key, int value) => Set[key] = value;
        public void SetBool(string key, bool value) => Set[key] = value;
        public void SetString(string key, string value) => Set[key] = value;
    }

    private static Dictionary<string, object> Fill(CombatRig rig, EntityHandle attacker, EntityHandle? inflictor = null,
                                                   string coreWeapon = "tf_weapon_scattergun")
    {
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock, rig.Carriers, rig.Level,
                                       new TFDominations(rig.Carriers), rig.Owners, new AttributeHooks());
        var fields = new Fields();
        EntityHandle scorer = attacker == CombatRig.Attacker || attacker == CombatRig.Third || attacker == CombatRig.Victim
            ? attacker : EntityHandle.None;
        notice.Fill(new DeathNoticeFacts(CombatRig.Victim, attacker, scorer, inflictor ?? attacker, coreWeapon), fields);
        return fields.Set;
    }

    [Fact]
    public void A_scattergun_kill_is_scattergun_with_its_bits_and_no_crit()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun", AttackerAt = new Vector(512f, 0f, 0f) };
        rig.Hit(6f, DamageType.Bullet | TFDamageBits.UseDistanceMod);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal("scattergun", f["weapon"]);
        Assert.Equal((int)TFWeaponId.Scattergun, f["weaponid"]);
        Assert.Equal(100 + CombatRig.Attacker.EntryIndex, f["attacker"]);
        // The record carries the hit's bits, the weapon's own
        // (g_aWeaponDamageTypes: bullet, buckshot, the distance mod).
        Assert.Equal((int)TFWeaponDamageTypes.Of(TFWeaponId.Scattergun), f["damagebits"]);
        Assert.Equal((int)TFCritType.None, f["crit_type"]);
        Assert.Equal(-1, f["assister"]);
        Assert.Equal(CombatRig.Victim.EntryIndex, f["victim_entindex"]);
    }

    [Fact]
    public void A_crit_kill_carries_the_crit_flag_and_type()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_scattergun" };
        rig.Hit(6f, DamageType.Bullet | TFDamageBits.UseDistanceMod, critical: true);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal((int)TFCritType.Full, f["crit_type"]);
        Assert.True(((int)f["damagebits"] & (int)TFDamageBits.Critical) != 0);
    }

    [Fact]
    public void A_headshot_kill_is_the_sniper_rifles_with_the_headshot_custom_kind()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_sniperrifle" };
        rig.AttackerState.SetConditions(TFCondMask.Of(TFCond.Zoomed));
        rig.Hit(50f, DamageType.Bullet, HitGroup.Head);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal(("sniperrifle", (int)TFDamageCustom.Headshot), ((string)f["weapon"], (int)f["customkill"]));
    }

    /// <summary>The recorder's death (tf_gamerules.cpp:12333-12336): a kill carrying
    /// the sapper-recorder kind is named for its recorder, the kind the sapper's
    /// drain stamps when the builder's item degenerates.</summary>
    [Fact]
    public void A_recorder_death_names_the_recorder()
    {
        var rig = new CombatRig();
        using (rig.Scopes.Push(TFDamageCustom.SapperRecorderDeath, TFWeaponId.None, ""))
            rig.Hit(20f, DamageType.Crush);

        Assert.Equal("recorder", Fill(rig, CombatRig.Attacker)["weapon"]);
    }

    [Fact]
    public void The_second_recent_enemy_damager_assists()
    {
        var rig = new CombatRig();
        rig.Hit(10f, DamageType.Bullet, attacker: CombatRig.Third);
        rig.Clock.Now += 1f;
        rig.Hit(10f, DamageType.Bullet);

        Assert.Equal(100 + CombatRig.Third.EntryIndex, Fill(rig, CombatRig.Attacker)["assister"]);

        rig.Clock.Now += TFCombatDefs.TimeAssistKill + 0.5f;
        Assert.Equal(-1, Fill(rig, CombatRig.Attacker)["assister"]);
    }

    [Fact]
    public void An_afterburn_kill_is_the_flamethrowers_whatever_the_attacker_holds()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_shotgun_pyro" };
        using (rig.Scopes.Push(TFDamageCustom.Burning, TFWeaponId.Flamethrower, "tf_weapon_flamethrower"))
            rig.Hit(4f, TFBurn.TickDamageType);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal(("flamethrower", (int)TFWeaponId.Flamethrower, (int)TFDamageCustom.Burning),
                     ((string)f["weapon"], (int)f["weaponid"], (int)f["customkill"]));
    }

    [Fact]
    public void A_suicide_names_the_world_and_credits_a_recent_enemy()
    {
        var rig = new CombatRig();
        rig.Engine.Returns("Entities.GetClassName", "player");
        var alone = Fill(rig, CombatRig.Victim);
        // The scorer inflicted it on himself: the name stays "world" (:12447-12457).
        Assert.Equal((100 + CombatRig.Victim.EntryIndex, "world"), ((int)alone["attacker"], (string)alone["weapon"]));

        // An enemy hit within TF_TIME_SUICIDE_KILL_CREDIT: the kill is his, by the
        // victim as inflictor, so the name is the inflictor's class.
        rig.Hit(10f, DamageType.Bullet);
        var credited = Fill(rig, CombatRig.Victim);
        Assert.Equal((100 + CombatRig.Attacker.EntryIndex, "player"), ((int)credited["attacker"], (string)credited["weapon"]));
    }

    [Fact]
    public void A_self_kill_is_stamped_the_suicide_custom_kind()
    {
        // CommitSuicide flags its own damage info TF_DMG_CUSTOM_SUICIDE
        // (tf_player.cpp:10218-10224, player.cpp:5448-5458): the event
        // carries it though no hit recorded a kind.
        var rig = new CombatRig();
        rig.Engine.Returns("Entities.GetClassName", "player");
        var alone = Fill(rig, CombatRig.Victim);
        Assert.Equal((int)TFDamageCustom.Suicide, alone["customkill"]);
    }

    [Fact]
    public void A_world_death_keeps_whatever_its_hit_recorded()
    {
        // Only a self-inflicted death is the suicide kind: the world's
        // inflictor is not the victim.
        var rig = new CombatRig();
        rig.Engine.Returns("Entities.GetClassName", "trigger_hurt");
        var f = Fill(rig, EntityHandle.None, CombatRig.Third);
        Assert.Equal((int)TFDamageCustom.None, f["customkill"]);
    }

    [Theory]
    [InlineData("tf_weapon_scattergun", "scattergun")]
    [InlineData("tf_weapon_grenade_pipebomb", "pipebomb")]
    [InlineData("tf_projectile_sentryrocket", "obj_sentrygun3")]
    [InlineData("tf_projectile_healing_bolt", "crusaders_crossbow")]
    [InlineData("func_door", "door")]
    [InlineData("trigger_hurt", "trigger_hurt")]
    public void The_weapon_name_loses_stocks_prefixes(string name, string expected)
        => Assert.Equal(expected, TFDeathNotice.StockName(name));

    /// <summary>The victim's blast jump component, as the rig's players have it.</summary>
    private static TFBlastJumpState Jump(CombatRig rig, TFBlastJump state)
    {
        var jump = new TFBlastJumpState(rig.Carriers, new TFPlayerConditions(rig.Carriers, rig.Engine, new TF2.Tests.Rules.ListHooks<ITFConditionHook>(), rig.Engine), rig.Engine,
                                        new NoEntities(), rig.Engine, rig.Engine, rig.Engine);
        jump.Attach(CombatRig.Victim);
        if (state != TFBlastJump.None)
        {
            // Set() fires the jump's own event; the record is what the word reads.
            rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(7));
            rig.Engine.Returns("GameEvents.EventFire", true);
            jump.Set(state);
        }
        return jump;
    }

    /// <summary>The death's <c>rocket_jump</c> word (tf_gamerules.cpp:13116): the victim's
    /// <c>RocketJumped</c> state at the death.</summary>
    [Fact]
    public void A_rocket_jump_victim_dies_with_the_rocket_jump_word()
    {
        var rig = new CombatRig();
        Jump(rig, TFBlastJump.RocketJumped);
        rig.Hit(6f, DamageType.Bullet);

        Assert.True((bool)Fill(rig, CombatRig.Attacker)["rocket_jump"]);
    }

    [Fact]
    public void A_sticky_jump_victim_dies_without_the_rocket_jump_word()
    {
        var rig = new CombatRig();
        Jump(rig, TFBlastJump.StickyJumped);
        rig.Hit(6f, DamageType.Bullet);

        Assert.False((bool)Fill(rig, CombatRig.Attacker)["rocket_jump"]);
    }

    [Fact]
    public void A_hadouken_kill_is_the_pyro_taunt_whatever_is_held()
    {
        // The taunt attack's scope carries the held weapon; the kill name is
        // the taunt's own (tf_gamerules.cpp:12209-12211).
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_shotgun_pyro" };
        using (rig.Scopes.Push(TFDamageCustom.TauntatkHadouken, TFWeaponId.ShotgunPyro, "tf_weapon_shotgun_pyro"))
            rig.Hit(50f, DamageType.Burn);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal(("taunt_pyro", (int)TFWeaponId.None, (int)TFDamageCustom.TauntatkHadouken),
                     ((string)f["weapon"], (int)f["weaponid"], (int)f["customkill"]));
    }

    [Fact]
    public void A_high_noon_kill_is_the_heavy_taunt_whatever_is_held()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_shotgun_heavy" };
        using (rig.Scopes.Push(TFDamageCustom.TauntatkHighNoon, TFWeaponId.ShotgunHwg, "tf_weapon_shotgun_hwg"))
            rig.Hit(50f, DamageType.Bullet);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal("taunt_heavy", f["weapon"]);
    }

    [Fact]
    public void A_fencing_kill_is_the_spy_taunt_whatever_is_held()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_knife" };
        using (rig.Scopes.Push(TFDamageCustom.TauntatkFencing, TFWeaponId.Knife, "tf_weapon_knife"))
            rig.Hit(25f, DamageType.Slash);

        var f = Fill(rig, CombatRig.Attacker);

        Assert.Equal("taunt_spy", f["weapon"]);
    }

    /// <summary>The taunt kills' names (tf_gamerules.cpp:12209-12258): each
    /// taunt's custom kind names its own taunt weapon, whatever the killer
    /// holds; the soldier's taunt is the plain soldier's, stock's lumbricus
    /// half waiting on the wearable schema (the Worms Gear hat).</summary>
    [Theory]
    [InlineData(TFDamageCustom.TauntatkHadouken, "taunt_pyro")]                // :12209-12211
    [InlineData(TFDamageCustom.TauntatkHighNoon, "taunt_heavy")]              // :12213-12215
    [InlineData(TFDamageCustom.TauntatkGrandSlam, "taunt_scout")]             // :12217-12219
    [InlineData(TFDamageCustom.TauntatkBarbarianSwing, "taunt_demoman")]      // :12221-12223
    [InlineData(TFDamageCustom.TauntatkUberslice, "taunt_medic")]             // :12225-12227
    [InlineData(TFDamageCustom.TauntatkFencing, "taunt_spy")]                 // :12229-12231
    [InlineData(TFDamageCustom.TauntatkArrowStab, "taunt_sniper")]            // :12233-12235
    [InlineData(TFDamageCustom.TauntatkGrenade, "taunt_soldier")]             // :12237-12246
    [InlineData(TFDamageCustom.TauntatkEngineerGuitarSmash, "taunt_guitar_kill")]     // :12248-12250
    [InlineData(TFDamageCustom.TauntatkAllclassGuitarRiff, "taunt_guitar_riff_kill")] // :12252-12254
    [InlineData(TFDamageCustom.TauntatkEngineerArmKill, "robot_arm_blender_kill")]    // :12256-12258
    public void Each_taunt_kill_names_its_taunt_whatever_is_held(TFDamageCustom custom, string expected)
    {
        // The scope carries a held weapon; the kill name is the taunt's own,
        // not the held gun's stripped name.
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_shotgun_pyro" };
        using (rig.Scopes.Push(custom, TFWeaponId.None, ""))
            rig.Hit(50f, DamageType.Bullet);

        Assert.Equal(expected, Fill(rig, CombatRig.Attacker)["weapon"]);
    }

    [Theory]
    [InlineData("koth_cachoeira", "helicopter")]
    [InlineData("koth_megaton", "megaton")]
    [InlineData("dm_lockdown", "world")]
    public void A_trigger_kill_is_the_maps_hazard_or_the_world_left(string map, string expected)
    {
        // The trigger arm is the map's hazard (tf_gamerules.cpp:12429-12439);
        // elsewhere it leaves the name the chain began with.
        var rig = new CombatRig();
        rig.Level.Map = map;
        using (rig.Scopes.Push(TFDamageCustom.TriggerHurt, TFWeaponId.None, ""))
            rig.Hit(50f, DamageType.Crush);

        var f = Fill(rig, CombatRig.Attacker);
        Assert.Equal((expected, (int)TFWeaponId.None), ((string)f["weapon"], (int)f["weaponid"]));
    }

    [Fact]
    public void A_plain_kill_still_names_the_weapon_that_fired_it()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_pistol" };
        rig.Hit(6f, DamageType.Bullet);

        Assert.Equal("pistol", Fill(rig, CombatRig.Attacker)["weapon"]);
    }

    [Fact]
    public void A_victim_who_never_jumped_dies_without_the_rocket_jump_word()
    {
        var rig = new CombatRig();
        rig.Hit(6f, DamageType.Bullet);

        Assert.False((bool)Fill(rig, CombatRig.Attacker)["rocket_jump"]);
    }

    // ---- the silent killer (set_silent_killer, tf_weaponbase.cpp:6184-6192) ----

    /// <summary>Builds the notice over the rig with the owners and the hook walk
    /// armed, as <see cref="Fill"/> does but with the two walks the silent-kill
    /// ask needs (tf_gamerules.cpp:12833-12839).</summary>
    private static Dictionary<string, object> FillSilent(CombatRig rig)
    {
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock, rig.Carriers, rig.Level,
                                       new TFDominations(rig.Carriers), rig.Owners, new AttributeHooks());
        var fields = new Fields();
        notice.Fill(new DeathNoticeFacts(CombatRig.Victim, CombatRig.Attacker, CombatRig.Attacker, CombatRig.Attacker,
                                         "tf_weapon_knife"), fields);
        return fields.Set;
    }

    /// <summary>IsSilentKiller: the scorer's active weapon holding set_silent_killer
    /// one, with the backstab's custom kind on the kill, makes the notice silent
    /// (tf_gamerules.cpp:12833-12839, the item walk tf_weaponbase.cpp:6188).</summary>
    [Fact]
    public void A_backstab_by_a_silent_knife_leaves_the_notice_silent()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_knife" };
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_knife");
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(30062, "silent killer", "set_silent_killer",
                                                                AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("s")), 1f));
        rig.Owners.Items[CombatRig.Attacker.EntryIndex] = item;
        using (rig.Scopes.Push(TFDamageCustom.Backstab, TFWeaponId.Knife, "tf_weapon_knife"))
            rig.Hit(25f, DamageType.Slash);

        Assert.True((bool)FillSilent(rig)["silent_kill"]);
    }

    /// <summary>A silent knife that does not carry the attribute (the plain knife)
    /// keeps the notice: the walk answers no without the item (:6188).</summary>
    [Fact]
    public void A_backstab_by_a_plain_knife_keeps_the_notice_loud()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_knife" };
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_knife");
        using (rig.Scopes.Push(TFDamageCustom.Backstab, TFWeaponId.Knife, "tf_weapon_knife"))
            rig.Hit(25f, DamageType.Slash);

        Assert.False((bool)FillSilent(rig)["silent_kill"]);
    }

    /// <summary>The gate before the walk (:12833): a non-backstab kill stays
    /// audible however silent the killer's knife is.</summary>
    [Fact]
    public void A_plain_hit_by_a_silent_knife_keeps_the_notice_loud()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_knife" };
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_knife");
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(30062, "silent killer", "set_silent_killer",
                                                                AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                                                false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("s")), 1f));
        rig.Owners.Items[CombatRig.Attacker.EntryIndex] = item;
        rig.Hit(25f, DamageType.Slash);

        Assert.False((bool)FillSilent(rig)["silent_kill"]);
    }

    // ---- the gilded death (TF_DEATH_AUSTRALIUM, tf_gamerules.cpp:13092-13108) ----

    /// <summary>The notice over the rig with the owners and the attribute walk
    /// armed, as <see cref="FillSilent"/> does but for the plain kill's name.</summary>
    private static Dictionary<string, object> FillGilded(CombatRig rig)
    {
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock, rig.Carriers, rig.Level,
                                       new TFDominations(rig.Carriers), rig.Owners, new AttributeHooks());
        var fields = new Fields();
        notice.Fill(new DeathNoticeFacts(CombatRig.Victim, CombatRig.Attacker, CombatRig.Attacker, CombatRig.Attacker,
                                         "tf_weapon_wrench"), fields);
        return fields.Set;
    }

    /// <summary>IsAustraliumItem on the killing weapon's item: the Golden
    /// Wrench's set_turn_to_gold gilds the death flag (:13102-13104).</summary>
    [Fact]
    public void A_kill_by_a_gold_turning_wrench_gilds_the_death_flag()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_wrench" };
        rig.AttackerItem("set_turn_to_gold", AttributeDescriptionFormat.Additive, 1f);
        rig.Hit(60f, DamageType.Crush);

        Assert.True(((int)FillGilded(rig)["death_flags"] & TFDeathNotice.Australium) != 0);
    }

    /// <summary>The australium item wear gilds too, the same ask's other arm
    /// (the Golden Frying Pan's; :13098-13100).</summary>
    [Fact]
    public void A_kill_by_an_australium_item_gilds_the_death_flag()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_wrench" };
        rig.AttackerItem("is_australium_item", AttributeDescriptionFormat.Additive, 1f);
        rig.Hit(60f, DamageType.Crush);

        Assert.True(((int)FillGilded(rig)["death_flags"] & TFDeathNotice.Australium) != 0);
    }

    /// <summary>A plain wrench keeps the flag down: neither wear, no gold.</summary>
    [Fact]
    public void A_kill_by_a_plain_wrench_keeps_the_death_flag_ungilded()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_wrench" };
        rig.Hit(60f, DamageType.Crush);

        Assert.Equal(0, (int)FillGilded(rig)["death_flags"]);
    }

    // ---- the wrangler's kills (TF_DMG_CUSTOM_PLAYER_SENTRY,
    // tf_gamerules.cpp:12296-12311) ----

    /// <summary>The plain pointer's kill is the wrangler's, whatever sentry
    /// fired: the custom's own name (:12310).</summary>
    [Fact]
    public void A_wrangled_sentry_kill_is_the_wranglers_kill()
    {
        var rig = new CombatRig();
        using (rig.Scopes.Push(TFDamageCustom.PlayerSentry, TFWeaponId.None, ""))
            rig.Hit(8f, DamageType.Bullet);

        Assert.Equal("wrangler_kill", Fill(rig, CombatRig.Attacker)["weapon"]);
    }

    /// <summary>A pointer wearing <c>is_giger_counter</c> — the Giger
    /// Counter's — names the counter (:12298-12307), the scorer's item
    /// through the owners.</summary>
    [Fact]
    public void A_wrangled_sentry_kill_by_a_giger_counter_names_the_counter()
    {
        var rig = new CombatRig();
        rig.Owners.Held[CombatRig.Attacker.EntryIndex] = "tf_weapon_laser_pointer";
        rig.AttackerItem("is_giger_counter", AttributeDescriptionFormat.Additive, 1f);
        using (rig.Scopes.Push(TFDamageCustom.PlayerSentry, TFWeaponId.None, ""))
            rig.Hit(8f, DamageType.Bullet);

        Assert.Equal("giger_counter", FillGilded(rig)["weapon"]);
    }

    /// <summary>The plain sentry kill: the shell's scope names no weapon (stock's
    /// <c>FireBullets</c> info holds none), so the notice falls to the inflictor
    /// arm and names the gun from the sentry's own class — neither the empty
    /// scope name nor the builder's held shotgun wins
    /// (<c>GetKillingWeaponName</c>, tf_gamerules.cpp:12108-12300).</summary>
    [Fact]
    public void A_plain_sentry_kill_names_the_sentry_from_its_inflictor()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_shotgun_primary" };
        rig.Engine.Returns("Entities.GetClassName", "obj_sentrygun");
        var gun = new EntityHandle(0x0001_0009u);
        using (rig.Inflictors.Push(gun, default))
        using (rig.Scopes.PushNoLauncher(TFDamageCustom.None, TFWeaponId.None, ""))
            rig.Hit(30f, DamageType.Bullet);

        Assert.Equal("obj_sentrygun", Fill(rig, CombatRig.Attacker, gun)["weapon"]);
    }

    // ---- the fish kill (TF_DMG_CUSTOM_FISH_KILL, tf_player.cpp:9738-9759) ----

    /// <summary>The notice over the rig with the owners and the attribute walk
    /// armed, the fish ask reading the killing item through them.</summary>
    private static Dictionary<string, object> FillFish(CombatRig rig)
    {
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock, rig.Carriers, rig.Level,
                                       new TFDominations(rig.Carriers), rig.Owners, new AttributeHooks());
        var fields = new Fields();
        notice.Fill(new DeathNoticeFacts(CombatRig.Victim, CombatRig.Attacker, CombatRig.Attacker, CombatRig.Attacker,
                                         "tf_weapon_scattergun"), fields);
        return fields.Set;
    }

    /// <summary>The Unarmed Combat's killing blow is TF_DMG_CUSTOM_FISH_KILL
    /// and the fish notice (:9745, :9758), the fish the feed's weapon.</summary>
    [Fact]
    public void A_fish_kill_carries_the_fish_custom_and_the_fish_notice()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_bat_fish" };
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Hit(200f, DamageType.Club | TFDamageBits.Melee);

        var f = FillFish(rig);
        Assert.Equal("fish_notice", f["weapon"]);
        Assert.Equal((int)TFDamageCustom.FishKill, (int)f["customkill"]);
        Assert.Equal((int)TFWeaponId.BatFish, (int)f["weaponid"]);
    }

    /// <summary>The bat wearing fish_damage_override — the arm's fish — moves
    /// the notice to fish_notice__arm (:9752-9755, the ON_OTHER walk over the
    /// weapon that dealt the blow).</summary>
    [Fact]
    public void The_fish_override_moves_the_kill_notice_to_the_arm()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_bat_fish" };
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.AttackerItem("fish_damage_override", AttributeDescriptionFormat.Additive, 1f);
        rig.Hit(200f, DamageType.Club | TFDamageBits.Melee);

        Assert.Equal("fish_notice__arm", FillFish(rig)["weapon"]);
    }

    /// <summary>A fish hit the victim survives carries no custom kind: stock's
    /// arm waits for the health gone (:9743).</summary>
    [Fact]
    public void A_fish_hit_the_victim_survives_keeps_no_custom()
    {
        var rig = new CombatRig { AttackerWeapon = "tf_weapon_bat_fish" };
        rig.Engine.Returns("Entities.GetHealth", 100);
        rig.Hit(5f, DamageType.Club | TFDamageBits.Melee);

        Assert.Equal((int)TFDamageCustom.None, (int)FillFish(rig)["customkill"]);
    }
}
