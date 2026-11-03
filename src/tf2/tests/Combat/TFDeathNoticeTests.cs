using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
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
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock);
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

    [Theory]
    [InlineData("tf_weapon_scattergun", "scattergun")]
    [InlineData("tf_weapon_grenade_pipebomb", "pipebomb")]
    [InlineData("tf_projectile_sentryrocket", "obj_sentrygun3")]
    [InlineData("tf_projectile_healing_bolt", "crusaders_crossbow")]
    [InlineData("func_door", "door")]
    [InlineData("trigger_hurt", "trigger_hurt")]
    public void The_weapon_name_loses_stocks_prefixes(string name, string expected)
        => Assert.Equal(expected, TFDeathNotice.StockName(name));
}
