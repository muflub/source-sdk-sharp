using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFWeaponScriptParser"/> (<c>CTFWeaponInfo::Parse</c>) over the
/// shipped scripts, and <see cref="TFWeaponScripts"/> reading them sealed.
/// Each milestone row pins its values against the fixture text first, so a
/// fixture edit or a parse that reads the wrong key both go red.
/// </summary>
public sealed class TFWeaponScriptTests
{
    // name, TimeFireDelay, Damage, BulletsPerShot (0 = key absent), clip_size (null = key absent), primary_ammo (null = absent)
    [Theory]
    [InlineData("tf_weapon_scattergun", "0.625", 6, 10, "6", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_shotgun_primary", "0.625", 6, 10, "6", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_shotgun_soldier", "0.625", 6, 10, "6", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_shotgun_hwg", "0.625", 6, 10, "6", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_shotgun_pyro", "0.625", 6, 10, "6", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_pistol", "0.15", 15, 1, "12", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_pistol_scout", "0.15", 15, 1, "12", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_smg", "0.1", 8, 1, "25", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_revolver", "0.5", 40, 1, "6", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_minigun", "0.1", 9, 4, "-1", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_sniperrifle", "1.5", 4, 1, "-1", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_syringegun_medic", "0.1", 10, 1, "40", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_rocketlauncher", "0.8", 90, 1, "4", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_grenadelauncher", "0.6", 100, 1, "4", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_pipebomblauncher", "0.6", 120, 1, "8", "TF_AMMO_SECONDARY")]
    [InlineData("tf_weapon_flamethrower", "0.04", 170, 1, "-1", "TF_AMMO_PRIMARY")]
    [InlineData("tf_weapon_medigun", "0.5", 24, 0, null, "None")]
    [InlineData("tf_weapon_bat", "0.5", 35, 0, null, "None")]
    [InlineData("tf_weapon_bottle", "0.8", 65, 0, null, "None")]
    [InlineData("tf_weapon_fireaxe", "0.8", 65, 0, null, "None")]
    [InlineData("tf_weapon_shovel", "0.8", 65, 0, null, "None")]
    [InlineData("tf_weapon_fists", "0.8", 65, 0, null, "None")]
    [InlineData("tf_weapon_wrench", "0.8", 65, 0, "-1", "None")]
    [InlineData("tf_weapon_bonesaw", "0.8", 65, 0, null, "None")]
    [InlineData("tf_weapon_club", "0.8", 65, 0, null, "None")]
    [InlineData("tf_weapon_knife", "0.8", 40, 0, null, "None")]
    public void A_milestone_script_gives_its_fire_delay_damage_pellets_clip_and_ammo(
        string name, string fireDelay, int damage, int bullets, string? clip, string ammo)
    {
        string text = WeaponFixtures.Text(name);
        WeaponFixtures.Says(text, "TimeFireDelay", fireDelay);
        WeaponFixtures.Says(text, "Damage", damage.ToString(CultureInfo.InvariantCulture));
        if (bullets > 0)
            WeaponFixtures.Says(text, "BulletsPerShot", bullets.ToString(CultureInfo.InvariantCulture));
        else
            WeaponFixtures.Lacks(text, "BulletsPerShot");
        if (clip is null)
            WeaponFixtures.Lacks(text, "clip_size");
        else
            WeaponFixtures.Says(text, "clip_size", clip);
        WeaponFixtures.Says(text, "primary_ammo", ammo);

        var script = WeaponFixtures.Parse(name);

        Assert.Equal(name, script.Name);
        Assert.Equal(float.Parse(fireDelay, CultureInfo.InvariantCulture), script.Primary.TimeFireDelay);
        Assert.Equal(damage, script.Primary.Damage);
        Assert.Equal(bullets, script.Primary.BulletsPerShot);
        Assert.Equal(clip is null ? WeaponScript.NoClip : int.Parse(clip, CultureInfo.InvariantCulture), script.Base.MaxClip1);
        Assert.Equal(ammo == "None" ? "" : ammo, script.Base.Ammo1);
    }

    [Theory]
    [InlineData("tf_weapon_builder", TFWeaponCategory.Building)]
    [InlineData("tf_weapon_sapper", TFWeaponCategory.Building)]
    [InlineData("tf_weapon_pda_engineer_build", TFWeaponCategory.Pda)]
    [InlineData("tf_weapon_pda_engineer_destroy", TFWeaponCategory.Pda)]
    [InlineData("tf_weapon_pda_spy", TFWeaponCategory.Pda)]
    [InlineData("tf_weapon_invis", TFWeaponCategory.Item1)]
    [InlineData("tf_weapon_medigun", TFWeaponCategory.Secondary)]
    [InlineData("tf_weapon_knife", TFWeaponCategory.Melee)]
    [InlineData("tf_weapon_minigun", TFWeaponCategory.Primary)]
    public void A_script_names_its_weapon_type(string name, TFWeaponCategory category)
    {
        string word = category switch
        {
            TFWeaponCategory.Building => "building",
            TFWeaponCategory.Pda => "pda",
            TFWeaponCategory.Item1 => "item1",
            TFWeaponCategory.Secondary => "secondary",
            TFWeaponCategory.Melee => "melee",
            _ => "primary",
        };
        WeaponFixtures.Says(WeaponFixtures.Text(name), "WeaponType", word);

        Assert.Equal(category, WeaponFixtures.Parse(name).Category);
    }

    [Fact]
    public void Every_fixture_parses_and_names_a_weapon_type()
    {
        var names = WeaponFixtures.Names().ToArray();
        Assert.Equal(32, names.Length);
        foreach (string name in names)
        {
            var script = WeaponFixtures.Parse(name);
            Assert.Equal(name, script.Name);
            Assert.NotEqual(WeaponScript.PrintNameMissing, script.Base.PrintName);
        }
    }

    [Fact]
    public void Rapid_fire_crits_are_the_scripts_flag()
    {
        foreach (string name in new[] { "tf_weapon_minigun", "tf_weapon_smg", "tf_weapon_pistol", "tf_weapon_flamethrower", "tf_weapon_syringegun_medic" })
        {
            WeaponFixtures.Says(WeaponFixtures.Text(name), "UseRapidFireCrits", "1");
            Assert.True(WeaponFixtures.Parse(name).Primary.UseRapidFireCrits, name);
        }

        WeaponFixtures.Lacks(WeaponFixtures.Text("tf_weapon_scattergun"), "UseRapidFireCrits");
        Assert.False(WeaponFixtures.Parse("tf_weapon_scattergun").Primary.UseRapidFireCrits);
    }

    [Fact]
    public void The_projectile_types_resolve_from_the_scripts_spelling()
    {
        Assert.Equal(TFProjectile.Bullet, WeaponFixtures.Parse("tf_weapon_scattergun").Primary.Projectile);
        Assert.Equal(TFProjectile.Rocket, WeaponFixtures.Parse("tf_weapon_rocketlauncher").Primary.Projectile);
        Assert.Equal(TFProjectile.Pipebomb, WeaponFixtures.Parse("tf_weapon_grenadelauncher").Primary.Projectile);
        Assert.Equal(TFProjectile.PipebombRemote, WeaponFixtures.Parse("tf_weapon_pipebomblauncher").Primary.Projectile);
        Assert.Equal(TFProjectile.Syringe, WeaponFixtures.Parse("tf_weapon_syringegun_medic").Primary.Projectile);
        // The flamethrower names none: its flames are not a ProjectileType.
        Assert.Equal(TFProjectile.None, WeaponFixtures.Parse("tf_weapon_flamethrower").Primary.Projectile);
    }

    [Fact]
    public void The_idle_empty_time_is_read_under_stocks_misspelt_key()
    {
        // The scattergun writes "TimeIdleEmpty" 0.25; stock reads "TimeIdleEmpy".
        WeaponFixtures.Says(WeaponFixtures.Text("tf_weapon_scattergun"), "TimeIdleEmpty", "0.25");
        Assert.Equal(0f, WeaponFixtures.Parse("tf_weapon_scattergun").Primary.TimeIdleEmpty);

        var typo = TFWeaponScriptParser.Parse(KeyValuesText.Parse("W { \"TimeIdleEmpy\" \"0.5\" }").Children[0], "w");
        Assert.Equal(0.5f, typo.Primary.TimeIdleEmpty);
    }

    [Fact]
    public void The_secondary_mode_inherits_each_primary_value_it_does_not_set()
    {
        var script = TFWeaponScriptParser.Parse(KeyValuesText.Parse(
            "W { \"Damage\" \"20\" \"TimeFireDelay\" \"0.3\" \"Secondary_Damage\" \"7\" \"UseRapidFireCrits\" \"1\" }").Children[0], "w");

        Assert.Equal(20, script.Primary.Damage);
        Assert.Equal(7, script.Secondary.Damage);
        Assert.Equal(0.3f, script.Secondary.TimeFireDelay);
        Assert.Equal(8192f, script.Secondary.Range);
        // The rapid-fire flag is the secondary's own key, default 0.
        Assert.True(script.Primary.UseRapidFireCrits);
        Assert.False(script.Secondary.UseRapidFireCrits);
    }

    [Fact]
    public void The_smack_delay_defaults_to_a_fifth_of_a_second_and_the_knife_sets_its_own()
    {
        WeaponFixtures.Lacks(WeaponFixtures.Text("tf_weapon_bat"), "SmackDelay");
        Assert.Equal(0.2f, WeaponFixtures.Parse("tf_weapon_bat").Primary.SmackDelay);
        WeaponFixtures.Says(WeaponFixtures.Text("tf_weapon_knife"), "SmackDelay", "0.1");
        Assert.Equal(0.1f, WeaponFixtures.Parse("tf_weapon_knife").Primary.SmackDelay);
    }

    [Fact]
    public void The_database_reads_a_sealed_script_from_the_game_path()
    {
        var files = WeaponFixtures.Shipped();
        var log = new RecordingLog();
        var scripts = new TFWeaponScripts(log, files);

        // The stimulus is sealed: the plain key is not in the bytes read.
        Assert.DoesNotContain("TimeFireDelay", Encoding.UTF8.GetString(files.Bytes["scripts/tf_weapon_scattergun.ctx"]));

        var script = scripts.Get("tf_weapon_scattergun");

        Assert.NotNull(script);
        Assert.Equal(0.625f, script!.Primary.TimeFireDelay);
        Assert.Equal(new[] { "scripts/tf_weapon_scattergun.txt", "scripts/tf_weapon_scattergun.ctx" }, files.Asked);
        Assert.Empty(log.Warnings());
    }

    [Fact]
    public void The_database_reads_each_name_once_and_answers_the_same_script()
    {
        var files = WeaponFixtures.Shipped();
        var scripts = new TFWeaponScripts(new RecordingLog(), files);

        var first = scripts.Get("TF_WEAPON_MINIGUN");
        var second = scripts.Get("tf_weapon_minigun");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(2, files.Asked.Count);
    }

    [Fact]
    public void A_missing_script_is_null_and_warned_once()
    {
        var files = new TFClassTableTests.Files();
        var log = new RecordingLog();
        var scripts = new TFWeaponScripts(log, files);

        Assert.Null(scripts.Get("tf_weapon_nothing"));
        Assert.Null(scripts.Get("tf_weapon_nothing"));

        Assert.Single(log.Warnings());
        Assert.Contains("tf_weapon_nothing", log.Warnings()[0]);
    }

    [Fact]
    public void The_drop_weight_lands_with_a_zero_for_a_script_that_names_none()
    {
        // The drop weight of CTFPlayer::DropAmmoPack's heaviest-weapon scan
        // (tf_player.cpp:13040-13058). A retail key, so the fixtures carry it.
        WeaponFixtures.Says(WeaponFixtures.Text("tf_weapon_rocketlauncher"), "weight", "3");
        Assert.Equal(3, WeaponFixtures.Parse("tf_weapon_rocketlauncher").Weight);

        var unweighted = TFWeaponScriptParser.Parse(KeyValuesText.Parse("W { }").Children[0], "w");
        Assert.Equal(0, unweighted.Weight);
    }
}
