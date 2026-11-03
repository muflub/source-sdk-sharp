using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFClassDataParser"/>: <c>TFPlayerClassData_t::ParseData</c>
/// (tf_classdata.cpp) over the shipped class scripts (decrypted fixtures,
/// TestData/playerclasses/README) and over synthetic ones for the arms the
/// shipped files never take. Each fixture value is also found in the
/// fixture's own text, so a fixture edited under a fact fails it.
/// </summary>
public sealed class TFClassDataTests
{
    internal static string Fixture(string rawName) =>
        File.ReadAllText(RepoLayout.Repo("src", "tf2", "tests", "TestData", "playerclasses", rawName + ".txt"));

    internal static KeyValuesText Block(string text)
    {
        var document = KeyValuesText.Parse(text);
        Assert.NotEmpty(document.Children);
        return document.Children[0];
    }

    private static TFClassData Parse(TFClass c, string text, RecordingLog? log = null) =>
        TFClassDataParser.Parse(c, Block(text), log ?? new RecordingLog(), "test/" + c);

    private static void AssertFixtureSays(string text, string key, string value) =>
        Assert.Matches(new Regex("^\\s*\"" + Regex.Escape(key) + "\"\\s+\"" + Regex.Escape(value) + "\"", RegexOptions.Multiline), text);

    [Theory]
    [InlineData("scout", TFClass.Scout, 400, 125)]
    [InlineData("soldier", TFClass.Soldier, 240, 200)]
    [InlineData("heavyweapons", TFClass.HeavyWeapons, 230, 300)]
    [InlineData("demoman", TFClass.Demoman, 280, 175)]
    public void A_shipped_script_gives_its_speed_and_health(string raw, TFClass c, int speed, int health)
    {
        string text = Fixture(raw);
        AssertFixtureSays(text, "speed_max", speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AssertFixtureSays(text, "health_max", health.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var data = Parse(c, text);

        Assert.True(data.IsParsed);
        Assert.Equal(c, data.Class);
        Assert.Equal(raw, data.Name);
        Assert.Equal(speed, data.MaxSpeed);
        Assert.Equal(health, data.MaxHealth);
        Assert.Equal(0, data.MaxArmor);
    }

    [Fact]
    public void The_weapon_keys_resolve_to_weapon_ids_and_the_unset_slots_are_none()
    {
        string text = Fixture("scout");
        AssertFixtureSays(text, "weapon3", "TF_WEAPON_SCATTERGUN");

        var data = Parse(TFClass.Scout, text);

        Assert.Equal(
            [TFWeaponId.Bat, TFWeaponId.PistolScout, TFWeaponId.Scattergun, TFWeaponId.None, TFWeaponId.None, TFWeaponId.None],
            data.Weapons.ToArray());
        Assert.Equal([TFWeaponId.GrenadeCaltrop, TFWeaponId.GrenadeConcussion], data.Grenades.ToArray());
    }

    [Theory]
    [InlineData("scout", TFClass.Scout, 32, 36, 100, 1, 1)]
    [InlineData("soldier", TFClass.Soldier, 20, 32, 100, 1, 1)]
    [InlineData("heavyweapons", TFClass.HeavyWeapons, 200, 32, 100, 1, 1)]
    [InlineData("engineer", TFClass.Engineer, 32, 200, 200, 0, 0)]
    public void The_AmmoMax_block_gives_the_per_class_maxima(string raw, TFClass c, int primary, int secondary, int metal, int g1, int g2)
    {
        string text = Fixture(raw);
        AssertFixtureSays(text, "tf_ammo_secondary", secondary.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var data = Parse(c, text);

        Assert.Equal(primary, data.MaxAmmo(TFAmmo.Primary));
        Assert.Equal(secondary, data.MaxAmmo(TFAmmo.Secondary));
        Assert.Equal(metal, data.MaxAmmo(TFAmmo.Metal));
        Assert.Equal(g1, data.MaxAmmo(TFAmmo.Grenades1));
        Assert.Equal(g2, data.MaxAmmo(TFAmmo.Grenades2));
        // No script names grenades3: GetInt's default. Index 0 stays TF_AMMO_DUMMY.
        Assert.Equal(0, data.MaxAmmo(TFAmmo.Grenades3));
        Assert.Equal(0, data.MaxAmmo(TFAmmo.Dummy));
        Assert.Equal((int)TFAmmo.Count, data.AmmoMax.Length);
    }

    [Fact]
    public void The_ammo_keys_are_matched_without_case()
    {
        // The shipped files spell them "tf_ammo_primary"; g_aAmmoNames says TF_AMMO_PRIMARY.
        var data = Parse(TFClass.Scout, "PlayerClass { AmmoMax { \"TF_AMMO_PRIMARY\" \"7\" \"tf_ammo_metal\" \"9\" } }");

        Assert.Equal(7, data.MaxAmmo(TFAmmo.Primary));
        Assert.Equal(9, data.MaxAmmo(TFAmmo.Metal));
    }

    [Fact]
    public void Without_an_AmmoMax_block_every_maximum_is_zero()
    {
        var data = Parse(TFClass.Scout, "PlayerClass { \"speed_max\" \"400\" \"health_max\" \"125\" }");

        Assert.All(data.AmmoMax, max => Assert.Equal(0, max));
    }

    [Fact]
    public void An_out_of_range_ammo_index_is_refused()
    {
        var data = Parse(TFClass.Scout, Fixture("scout"));

        Assert.Throws<ArgumentOutOfRangeException>(() => data.MaxAmmo(TFAmmo.Count));
    }

    [Fact]
    public void The_buildables_resolve_case_insensitively_and_an_unknown_one_is_OBJ_LAST()
    {
        string engineer = Fixture("engineer");
        AssertFixtureSays(engineer, "buildable1", "OBJ_SENTRYGUN");
        Assert.Equal(
            [TFObjectType.Sentrygun, TFObjectType.Dispenser, TFObjectType.Teleporter, TFObjectType.Last, TFObjectType.Last, TFObjectType.Last],
            Parse(TFClass.Engineer, engineer).Buildables.ToArray());

        var odd = Parse(TFClass.Engineer, "PlayerClass { \"buildable1\" \"obj_dispenser\" \"buildable2\" \"OBJ_NOTHING\" \"buildable6\" \"OBJ_TELEPORTER\" }");
        Assert.Equal(
            [TFObjectType.Dispenser, TFObjectType.Last, TFObjectType.Last, TFObjectType.Last, TFObjectType.Last, TFObjectType.Teleporter],
            odd.Buildables.ToArray());
    }

    [Fact]
    public void Weapon_names_that_are_not_ids_read_as_none()
    {
        var data = Parse(TFClass.Civilian, "PlayerClass { \"weapon1\" \"TF_WEAPON_BAT\" \"weapon2\" \"\" \"weapon3\" \"TF_WEAPON_BOGUS\" }");

        Assert.Equal(TFWeaponId.Bat, data.Weapons[0]);
        Assert.Equal(TFWeaponId.None, data.Weapons[1]);
        Assert.Equal(TFWeaponId.None, data.Weapons[2]);
    }

    [Fact]
    public void The_models_names_and_camera_offset_are_read()
    {
        var data = Parse(TFClass.Scout, Fixture("scout"));

        Assert.Equal("models/player/scout.mdl", data.Model);
        Assert.Equal("models/player/hwm/scout.mdl", data.ModelHwm);
        Assert.Equal("models/weapons/c_models/c_scout_arms.mdl", data.ModelHands);
        Assert.Equal("TF_Class_Name_Scout", data.LocalizeName);
        Assert.Equal(new Vector(85f, 25f, 0f), data.ThirdPersonOffset);
    }

    [Fact]
    public void The_death_sounds_are_the_script_s_or_stock_s_defaults()
    {
        var scout = Parse(TFClass.Scout, Fixture("scout"));
        Assert.Equal("Scout.Death", scout.DeathSound(TFDeathSound.Generic));
        Assert.Equal("Scout.CritDeath", scout.DeathSound(TFDeathSound.Crit));
        Assert.Equal("Scout.MeleeDeath", scout.DeathSound(TFDeathSound.Melee));
        Assert.Equal("Scout.ExplosionDeath", scout.DeathSound(TFDeathSound.Explosion));

        var bare = Parse(TFClass.Scout, "PlayerClass { \"speed_max\" \"1\" \"health_max\" \"1\" }");
        Assert.Equal(["Player.Death", "TFPlayer.CritDeath", "Player.MeleeDeath", "Player.ExplosionDeath"], bare.DeathSounds.ToArray());
    }

    [Fact]
    public void The_animation_flags_are_set_by_a_positive_int()
    {
        var on = Parse(TFClass.Scout, "PlayerClass { \"DontDoAirwalk\" \"1\" \"dontdonewjump\" \"2\" }");
        Assert.True(on.DontDoAirwalk);
        Assert.True(on.DontDoNewJump);

        var off = Parse(TFClass.Scout, "PlayerClass { \"DontDoAirwalk\" \"0\" \"DontDoNewJump\" \"-1\" }");
        Assert.False(off.DontDoAirwalk);
        Assert.False(off.DontDoNewJump);
    }

    [Fact]
    public void An_unknown_key_is_ignored()
    {
        const string Body = "\"speed_max\" \"300\" \"health_max\" \"150\" \"weapon1\" \"TF_WEAPON_BONESAW\" AmmoMax { \"tf_ammo_primary\" \"150\" }";
        var plain = Parse(TFClass.Medic, "PlayerClass { " + Body + " }");
        var extra = Parse(TFClass.Medic, "PlayerClass { \"bogus\" \"1\" Unknown { \"x\" \"y\" } " + Body + " }");

        Assert.Equal(300f, plain.MaxSpeed);
        Assert.Equal(plain.MaxSpeed, extra.MaxSpeed);
        Assert.Equal(plain.MaxHealth, extra.MaxHealth);
        Assert.Equal(plain.Weapons.ToArray(), extra.Weapons.ToArray());
        Assert.Equal(plain.AmmoMax.ToArray(), extra.AmmoMax.ToArray());
    }

    [Fact]
    public void A_script_without_speed_max_reads_zero_and_says_so()
    {
        var log = new RecordingLog();

        var data = Parse(TFClass.Pyro, "PlayerClass { \"health_max\" \"175\" }", log);

        Assert.Equal(0f, data.MaxSpeed);
        Assert.Equal(175, data.MaxHealth);
        Assert.Single(log.Warnings());
        Assert.Contains("\"speed_max\"", log.Warnings()[0]);
        Assert.Contains("test/Pyro", log.Warnings()[0]);
    }

    [Fact]
    public void A_shipped_script_logs_no_warning()
    {
        var log = new RecordingLog();

        Parse(TFClass.Scout, Fixture("scout"), log);

        Assert.Empty(log.Warnings());
    }

    [Fact]
    public void A_string_longer_than_stock_s_buffer_is_clipped_to_127_bytes()
    {
        string longModel = new('m', 200);

        var data = Parse(TFClass.Scout, "PlayerClass { \"model\" \"" + longModel + "\" \"name\" \"" + new string('n', 127) + "\" }");

        Assert.Equal(127, data.Model.Length);
        Assert.Equal(127, data.Name.Length);
        Assert.Equal("abc", TFClassDataParser.Clip("abc"));
        Assert.Equal(126, TFClassDataParser.Clip(new string('x', 126)).Length);
    }

    [Fact]
    public void Missing_is_what_stock_s_constructor_leaves()
    {
        var missing = TFClassData.Missing(TFClass.Spy);

        Assert.False(missing.IsParsed);
        Assert.Equal(TFClass.Spy, missing.Class);
        Assert.Equal(string.Empty, missing.Name);
        Assert.Equal(0f, missing.MaxSpeed);
        Assert.Equal(TFClassLimits.WeaponCount, missing.Weapons.Length);
        Assert.All(missing.Weapons, w => Assert.Equal(TFWeaponId.None, w));
        Assert.All(missing.Grenades, w => Assert.Equal(TFWeaponId.None, w));
        Assert.Equal(TFClassLimits.BlueprintCount, missing.Buildables.Length);
        Assert.All(missing.Buildables, b => Assert.Equal(TFObjectType.Last, b));
        Assert.All(missing.AmmoMax, a => Assert.Equal(0, a));
        Assert.All(missing.DeathSounds, s => Assert.Equal(string.Empty, s));
    }

    [Fact]
    public void Undefined_has_the_scout_model_and_its_own_name()
    {
        var undefined = TFClassData.Undefined;

        Assert.False(undefined.IsParsed);
        Assert.Equal(TFClass.Undefined, undefined.Class);
        Assert.Equal("undefined", undefined.Name);
        Assert.Equal("undefined", undefined.LocalizeName);
        Assert.Equal("models/player/scout.mdl", undefined.Model);
    }
}

/// <summary>The shared <see cref="RecordingLog"/>'s lines split by kind, as the TF2 facts read them.</summary>
internal static class RecordingLogViews
{
    public static List<string> Messages(this RecordingLog log)
        => log.Lines.Where(l => !l.Warning).Select(l => l.Text).ToList();

    public static List<string> Warnings(this RecordingLog log)
        => log.Lines.Where(l => l.Warning).Select(l => l.Text).ToList();
}
