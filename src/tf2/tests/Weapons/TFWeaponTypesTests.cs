extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

using C = client::SourceSharp.Shared;
using ClientEngineFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientTypes = client::TF2.Shared.TFWeaponTypes;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientClasses = client::TF2.Shared.TFClassTable;
using ClientSchema = client::TF2.Shared.Econ.ItemSchema;
using ClientAmmo = client::TF2.Shared.TFAmmoTypes;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFWeaponTypes"/> and <see cref="TFAmmoTypes"/>: the TF rows
/// the core's catalogues append, from the shipped scripts.
/// </summary>
public sealed class TFWeaponTypesTests
{
    private sealed class ClientFiles(TFClassTableTests.Files files) : ClientEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    private static Dictionary<string, WeaponType> Rows() => WeaponFixtures.Types().Rows().ToDictionary(r => r.Name);

    [Fact]
    public void The_milestone_weapons_are_the_class_scripts_weapons_the_builder_and_the_sapper()
    {
        var names = WeaponFixtures.Types().Names();

        // Every fixture is one: the 30 class weapons, the bow, builder, sapper.
        Assert.Equal(WeaponFixtures.Names().ToArray(), names);
        Assert.Contains("tf_weapon_minigun", names);
        Assert.Contains("tf_weapon_builder", names);
        Assert.Contains("tf_weapon_sapper", names);
    }

    [Fact]
    public void Every_milestone_weapon_has_a_row_named_after_its_script()
    {
        var rows = WeaponFixtures.Types().Rows().ToArray();

        Assert.Equal(33, rows.Length);
        Assert.Equal(WeaponFixtures.Names().ToArray(), rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void The_scattergun_row_carries_its_scripts_rate_pellets_clip_ammo_and_cone()
    {
        var row = Rows()["tf_weapon_scattergun"];

        Assert.Equal(0.625f, row.Interval);
        Assert.Equal(10, row.Pellets);
        Assert.Equal(6, row.ClipSize);
        Assert.Equal("TF_AMMO_PRIMARY", row.AmmoName);
        // asin(0.0675) = 3.870 degrees.
        Assert.Equal(3.8704f, row.Spread, 3);
        Assert.Equal("Weapon_Scatter_Gun.Single", row.FireSound);
        Assert.Equal("Weapon_Scatter_Gun.Empty", row.EmptySound);
        Assert.Equal(TFWeaponTypes.DeployTime, row.DeployTime);
        Assert.Equal("tf2.shotgun", row.Behaviour);
        Assert.False(row.IsMelee);
    }

    [Fact]
    public void A_melee_row_swings_the_scripts_damage_at_the_stock_reach()
    {
        var bat = Rows()["tf_weapon_bat"];

        Assert.True(bat.IsMelee);
        Assert.Equal(35f, bat.MeleeDamage);
        Assert.Equal(48f, bat.MeleeRange);
        Assert.Equal(0.5f, bat.Interval);
        Assert.Equal("tf2.melee", bat.Behaviour);
    }

    [Fact]
    public void A_poolfed_weapon_has_no_clip()
    {
        var rows = Rows();

        Assert.Equal(-1, rows["tf_weapon_minigun"].ClipSize);
        Assert.Equal(-1, rows["tf_weapon_sniperrifle"].ClipSize);
        Assert.Equal(-1, rows["tf_weapon_flamethrower"].ClipSize);
        Assert.Equal("TF_AMMO_PRIMARY", rows["tf_weapon_minigun"].AmmoName);
    }

    [Theory]
    [InlineData("tf_weapon_scattergun", TFWeaponKind.Shotgun)]
    [InlineData("tf_weapon_shotgun_soldier", TFWeaponKind.Shotgun)]
    [InlineData("tf_weapon_pistol", TFWeaponKind.Gun)]
    [InlineData("tf_weapon_pistol_scout", TFWeaponKind.Gun)]
    [InlineData("tf_weapon_smg", TFWeaponKind.Gun)]
    [InlineData("tf_weapon_revolver", TFWeaponKind.Revolver)]
    [InlineData("tf_weapon_minigun", TFWeaponKind.Minigun)]
    [InlineData("tf_weapon_sniperrifle", TFWeaponKind.SniperRifle)]
    [InlineData("tf_weapon_compound_bow", TFWeaponKind.Bow)]
    [InlineData("tf_weapon_rocketlauncher", TFWeaponKind.Projectile)]
    [InlineData("tf_weapon_grenadelauncher", TFWeaponKind.Projectile)]
    [InlineData("tf_weapon_pipebomblauncher", TFWeaponKind.Projectile)]
    [InlineData("tf_weapon_syringegun_medic", TFWeaponKind.Projectile)]
    [InlineData("tf_weapon_flamethrower", TFWeaponKind.Flamethrower)]
    [InlineData("tf_weapon_medigun", TFWeaponKind.Medigun)]
    [InlineData("tf_weapon_knife", TFWeaponKind.Melee)]
    [InlineData("tf_weapon_wrench", TFWeaponKind.Melee)]
    [InlineData("tf_weapon_builder", TFWeaponKind.Builder)]
    [InlineData("tf_weapon_sapper", TFWeaponKind.Builder)]
    [InlineData("tf_weapon_invis", TFWeaponKind.Inert)]
    [InlineData("tf_weapon_pda_spy", TFWeaponKind.Pda)]
    [InlineData("tf_weapon_pda_engineer_build", TFWeaponKind.Pda)]
    [InlineData("tf_weapon_pda_engineer_destroy", TFWeaponKind.Pda)]
    public void A_weapon_runs_the_behaviour_of_its_kind(string name, TFWeaponKind kind)
    {
        Assert.Equal(kind, TFWeaponKinds.Of(WeaponFixtures.Parse(name)));
        Assert.Equal(TFWeaponKinds.Key(kind), Rows()[name].Behaviour);
    }

    [Fact]
    public void The_spread_cone_is_the_angle_whose_sine_the_script_gives()
    {
        Assert.Equal(0f, TFWeaponTypes.SpreadDegrees(0f));
        Assert.Equal(30f, TFWeaponTypes.SpreadDegrees(0.5f), 4);
        Assert.Equal(90f, TFWeaponTypes.SpreadDegrees(2f), 4);
    }

    [Fact]
    public void A_row_wears_its_base_items_models_and_a_per_class_spelling_its_stems()
    {
        var types = WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"));
        var rows = types.Rows().ToDictionary(r => r.Name);

        Assert.Equal("models/weapons/c_models/c_bat.mdl", rows["tf_weapon_bat"].ViewModel);
        // tf_weapon_shotgun_soldier has no base item of its own: the stem's.
        Assert.Equal("tf_weapon_shotgun", types.BaseItemFor("tf_weapon_shotgun_soldier")?.ItemClass);
        Assert.Null(types.BaseItemFor("tf_weapon_nothing"));
    }

    [Fact]
    public void The_sappers_row_wears_the_sapper_item()
    {
        var types = WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt"));
        var item = types.BaseItemFor("tf_weapon_sapper");

        Assert.Equal("TF_WEAPON_BUILDER_SPY", item?.Name);
        Assert.Equal("models/weapons/c_models/c_sapper/c_sapper.mdl", item?.ModelPlayer);
        Assert.True(item?.AttachToHands);

        // The prefab names no world model: the row wears the player's, the
        // sapper's world model stock reads off the object anyway.
        var rows = types.Rows().ToDictionary(r => r.Name);
        Assert.Equal("models/weapons/c_models/c_sapper/c_sapper.mdl", rows["tf_weapon_sapper"].WorldModel);
    }

    [Fact]
    public void The_tf_rows_land_after_the_core_rows_and_both_sides_number_them_alike()
    {
        var files = WeaponFixtures.ShippedWithClasses();
        var log = new RecordingLog();
        var server = new TFWeaponTypes(new TFWeaponScripts(log, files), new TFClassTable(log, files), new ItemSchema(files, log));
        var clientFiles = new ClientFiles(files);
        var clientTypes = new ClientTypes(new ClientScripts(log, clientFiles), new ClientClasses(log, clientFiles), new ClientSchema(clientFiles, log));

        var serverCatalog = new WeaponCatalog(() => [server], log);
        var clientCatalog = new C.WeaponCatalog(() => [clientTypes], log);

        Assert.Equal(WeaponTypes.All.Length + 33, serverCatalog.Count);
        Assert.Equal(serverCatalog.Count, clientCatalog.Count);
        for (int i = 0; i < serverCatalog.Count; i++)
        {
            Assert.Equal(serverCatalog.ByIndex(i)!.Value.Name, clientCatalog.ByIndex(i)!.Value.Name);
            Assert.Equal(serverCatalog.ByIndex(i)!.Value.Behaviour, clientCatalog.ByIndex(i)!.Value.Behaviour);
        }

        Assert.True(serverCatalog.IndexOf("tf_weapon_scattergun") >= WeaponTypes.All.Length, "the TF rows append after the core's");
    }

    [Fact]
    public void The_ammo_rows_are_the_six_tf_ammo_types_counting_on_from_the_cores_wire_index()
    {
        var log = new RecordingLog();
        var server = new AmmoCatalog(() => [new TFAmmoTypes()], log);
        var clientCatalog = new C.AmmoCatalog(() => [new ClientAmmo()], log);

        Assert.Equal(AmmoTypes.All.Length + 6, server.Count);
        Assert.Equal(AmmoTypes.All.Length + 1, server.WireIndex("TF_AMMO_PRIMARY"));
        Assert.Equal(AmmoTypes.All.Length + 6, server.WireIndex("TF_AMMO_GRENADES3"));
        Assert.Equal(server.WireIndex("TF_AMMO_METAL"), clientCatalog.WireIndex("TF_AMMO_METAL"));

        var primary = server.For("TF_AMMO_PRIMARY")!.Value;
        Assert.Equal(5000, primary.MaxCarry);
        Assert.Equal(0f, primary.Damage);
        Assert.Equal(2400f, primary.DamageForce);
        Assert.Equal((DamageType)(0x2 | 0x200000 | 0x20000), primary.DamageType);
    }

    [Fact]
    public void A_row_that_is_not_tf_ammo_is_no_tf_ammo()
    {
        Assert.Equal(TFAmmo.Metal, TFAmmoTypes.Of("TF_AMMO_METAL"));
        Assert.Null(TFAmmoTypes.Of("Pistol"));
        Assert.Null(TFAmmoTypes.Of("tf_ammo_metal"));
    }
}
