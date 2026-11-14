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

        // Every fixture is one: the 30 class weapons, the sword, the bow,
        // builder, sapper —
        // and the seeded item-carried weapons (the flare gun, the crossbow, the
        // Gunslinger's arm, the Wrangler, the lunchbox and its drink, the soldier's buff item,
        // the katana and the jars, none named by a class script's weaponN; their own facts
        // stand them up). The engineer's rescue shotgun seeds the same way.
        // The energy family joins them as the equipped unlocks their rows: the bison,
        // the mangler, the pomson — and the scout's hype pair and the Shortstop the same way.
        Assert.Equal(
            WeaponFixtures.Names().Append("tf_weapon_flaregun").Append("tf_weapon_crossbow")
                .Append("tf_weapon_robot_arm").Append("tf_weapon_laser_pointer").Append("tf_weapon_lunchbox").Append("tf_weapon_lunchbox_drink")
                .Append("tf_weapon_buff_item").Append(TFWeaponTypes.Katana)
                .Append("tf_weapon_rocketlauncher_airstrike")
                .Append("tf_weapon_handgun_scout_primary")
                .Append("tf_weapon_flaregun_revenge")
                .Append("tf_weapon_sniperrifle_decap").Append("tf_weapon_sniperrifle_classic")
                .Append("tf_weapon_jar").Append("tf_weapon_jar_gas").Append("tf_weapon_jar_milk")
                .Append("tf_weapon_rocketlauncher_fireball")
                .Append("tf_weapon_bat_giftwrap")
                .Append("tf_weapon_sentry_revenge")
                .Append("tf_weapon_raygun").Append("tf_weapon_particle_cannon")
                .Append("tf_weapon_drg_pomson")
                .Append("tf_weapon_soda_popper").Append("tf_weapon_pep_brawler_blaster")
                .Append("tf_weapon_cleaver")
                .Append("tf_weapon_rocketpack")
                .Append("tf_weapon_mechanical_arm")
                .Append("tf_weapon_charged_smg")
                .Append("tf_weapon_shotgun_building_rescue")
                .Order(StringComparer.Ordinal).ToArray(),
            names);
        Assert.Contains("tf_weapon_minigun", names);
        Assert.Contains("tf_weapon_builder", names);
        Assert.Contains("tf_weapon_sapper", names);
    }

    [Fact]
    public void Every_milestone_weapon_has_a_row_named_after_its_script()
    {
        var rows = WeaponFixtures.Types().Rows().ToArray();

        Assert.Equal(34, rows.Length);
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
    public void The_scouts_hype_pair_run_the_scattergun()
    {
        // CTFScatterGun subclasses of their own (tf_weapon_shotgun.h:154, :170):
        // the Soda Popper and the Baby Face's Blaster. The row composes from
        // the script alone (TFWeaponTypes.Row); the game's schema names both
        // items (items_working.tsv:151, :199).
        var popper = TFWeaponTypes.Row(WeaponFixtures.ParseExtra("tf_weapon_soda_popper"), null);
        var brawler = TFWeaponTypes.Row(WeaponFixtures.ParseExtra("tf_weapon_pep_brawler_blaster"), null);

        Assert.Equal(TFWeaponKind.Shotgun, TFWeaponKinds.Of(WeaponFixtures.ParseExtra("tf_weapon_soda_popper")));
        Assert.Equal(TFWeaponKind.Shotgun, TFWeaponKinds.Of(WeaponFixtures.ParseExtra("tf_weapon_pep_brawler_blaster")));
        Assert.Equal("tf2.shotgun", popper.Behaviour);
        Assert.Equal("tf2.shotgun", brawler.Behaviour);
        Assert.Equal(6, popper.ClipSize);
        Assert.Equal(10, popper.Pellets);
    }

    /// <summary>
    /// The Flying Guillotine and its Promo skin (items_working.tsv:222, :226): the
    /// scout's thrown cleaver, held as the equipped item whose class is its own
    /// row. The kind answers through the script's own shape — a secondary that
    /// throws <c>projectile_cleaver</c> (stock's <c>TF_WEAPON_CLEAVER</c>, the
    /// cleaver entity behind the bounce-and-return); the row and the behaviour
    /// key compose over the extras script like the hype pair above.
    /// </summary>
    [Fact]
    public void The_flying_guillotine_runs_the_projectile_of_its_cleaver_script()
    {
        var script = WeaponFixtures.ParseExtra("tf_weapon_cleaver");

        Assert.Equal(TFWeaponKind.Projectile, TFWeaponKinds.Of(script));
        Assert.Equal("tf2.projectile", ExtrasRows()["tf_weapon_cleaver"].Behaviour);
    }

    /// <summary>The composed rows over the shipped scripts plus the extras (weapons-extra/README).</summary>
    private static Dictionary<string, WeaponType> ExtrasRows()
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        var log = new RecordingLog();
        return new TFWeaponTypes(new TFWeaponScripts(log, files), new TFClassTable(log, files), new ItemSchema(files, log))
            .Rows().ToDictionary(r => r.Name);
    }

    [Fact]
    public void The_flare_gun_is_a_row_the_kit_stands_up()
    {
        // No class script's weaponN names it: the pyro carries the flare gun as
        // an equipped item, and the row is what the kit's spawn gate stands the
        // entity on (stock gives the item's own weapon entity,
        // ManageRegularWeapons over GetLoadoutItem, tf_player.cpp:4496).
        Assert.True(ExtrasRows().TryGetValue("tf_weapon_flaregun", out WeaponType row), "no flare gun row");

        Assert.Equal("tf2.projectile", row.Behaviour);
        Assert.Equal("TF_AMMO_SECONDARY", row.AmmoName);
    }

    [Fact]
    public void The_gunslinger_arm_is_a_row_the_kit_stands_up()
    {
        // The Gunslinger's entity class is the item's own row
        // (items_working.tsv:58): the engineer's unlock wrench, held as an
        // equipped item, its script a melee the kit deploys on. The held-item
        // attribute asks ride the same name (TFObjectBuilder's wrench lookup).
        Assert.True(ExtrasRows().TryGetValue(TFWeaponTypes.WrenchArm, out WeaponType row), "no robot arm row");

        Assert.True(row.IsMelee);
        Assert.Equal("tf2.melee", row.Behaviour);
    }

    [Fact]
    public void The_buff_item_is_a_row_the_kit_stands_up()
    {
        // The soldier's buff items carry the tf_weapon_buff_item class
        // (items_working.tsv:51, :100, :128): held as equipped items, the one
        // press they have deploys the item's buff (TFBuffItemBehaviour,
        // tf_weapon_buff_item.cpp:116-135).
        Assert.True(ExtrasRows().TryGetValue("tf_weapon_buff_item", out WeaponType row), "no buff item row");

        // No bullets of its own: the core's plain-row shape, with no swing —
        // the press deploys the buff, it fires nothing.
        Assert.Equal("", row.AmmoName);
        Assert.Equal(0f, row.MeleeRange, 4);
        Assert.Equal("tf2.buff", row.Behaviour);
    }

    [Fact]
    public void The_crossbow_is_a_row_the_kit_stands_up()
    {
        // The medic's unlock (items 305/1079), held as an equipped item: the
        // row is what the kit's spawn gate stands the entity on, the flare
        // gun's rule. Its clip of one reloads each shot (the inherited
        // m_bReloadsSingly, CTFCrossbow : CTFRocketLauncher,
        // tf_weapon_rocketlauncher.h:166).
        Assert.True(ExtrasRows().TryGetValue("tf_weapon_crossbow", out WeaponType row), "no crossbow row");

        Assert.Equal("tf2.projectile", row.Behaviour);
        Assert.Equal("TF_AMMO_PRIMARY", row.AmmoName);
        Assert.Equal(1, row.ClipSize);
        Assert.Equal(0.23f, row.Interval);
    }

    [Fact]
    public void The_katana_is_a_row_the_kit_stands_up()
    {
        // The Half-Zatoichi (items_working.tsv:357), the soldier's and
        // demoman's unlock held as an equipped item: the row is what the
        // kit's spawn gate stands the entity on, the flare gun's rule. The
        // katana overrides no GetWeaponID, so the decap base's sword id stands
        // (tf_weapon_sword.h:45) and the row runs the plain melee over it.
        Assert.True(ExtrasRows().TryGetValue(TFWeaponTypes.Katana, out WeaponType row), "no katana row");

        Assert.Equal("tf2.melee", row.Behaviour);
        Assert.Equal(65f, row.MeleeDamage, 4);
        Assert.Equal(0.8f, row.Interval, 4);
    }

    [Fact]
    public void The_crossbow_reloads_one_bolt_at_a_time()
    {
        Assert.True(TFWeaponKinds.ReloadsSingly(TFWeaponId.Crossbow));
        Assert.False(TFWeaponKinds.ReloadsSingly(TFWeaponId.Flamethrower));
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

    /// <summary>A schema of classes with nothing behind them, plus one real weapon.</summary>
    private const string BranchSchema = """
        "items_game"
        {
            "qualities"
            {
            }
            "attributes"
            {
            }
            "items"
            {
                "101"
                {
                    "name"        "TF_WEARABLE_101"
                    "item_class"  "tf_wearable"
                    "item_name"   "#Item_101"
                    "item_slot"   "secondary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "scout"    "1"
                    }
                }
                "102"
                {
                    "name"        "ITEM_102"
                    "item_class"  "tf_powerup_bottle"
                    "item_name"   "#Item_102"
                    "item_slot"   "secondary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "scout"    "1"
                    }
                }
                "103"
                {
                    "name"        "ITEM_103"
                    "item_class"  "tf_weapon_spellbook"
                    "item_name"   "#Item_103"
                    "item_slot"   "secondary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "scout"    "1"
                    }
                }
                "104"
                {
                    "name"        "ITEM_104"
                    "item_class"  "tf_weapon_syringegun_medic"
                    "item_name"   "#Item_104"
                    "item_slot"   "secondary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "scout"    "1"
                    }
                }
            }
        }
        """;

    /// <summary>
    /// The shipped game's shape: every row costs a bit of the carried-weapon
    /// mask, so the schema branch keeps only what is a real weapon with a
    /// running behaviour — a tf_weapon_ class with a script whose kind is not
    /// the Inert arm — and the cosmetic classes the schema also names are not
    /// rows. A class a script does name is catalogued whatever its kind
    /// (the invis watch keeps the row its behaviour needs).
    /// </summary>
    [Fact]
    public void A_schema_name_is_only_a_row_when_a_script_behind_it_runs()
    {
        var names = WeaponFixtures.Types(BranchSchema).Names();

        // The real weapon the schema names earns its row.
        Assert.Contains("tf_weapon_syringegun_medic", names);
        // The cosmetic classes the schema also names (the shipped items_game
        // spells them bundle, craft_item, tf_wearable, no_entity): no bit for
        // a class no script backs.
        Assert.DoesNotContain("tf_wearable", names);
        Assert.DoesNotContain("tf_powerup_bottle", names);
        // A tf_weapon_ spelling with no script file at all is refused by the
        // script arm, read quiet: a miss is an absent row, not a finding.
        Assert.DoesNotContain("tf_weapon_spellbook", names);
        // The spy's watch: its script answers the Inert kind, but its class
        // script names it, so its row (and the cloak its behaviour runs)
        // stands.
        Assert.Contains("tf_weapon_invis", names);
    }

    /// <summary>A schema whose base items end at the launch weapons: the Huntsman is a plain item, twice over.</summary>
    private const string HuntsmanSchema = """
        "items_game"
        {
            "qualities"
            {
            }
            "attributes"
            {
            }
            "items"
            {
                "56"
                {
                    "name"        "TF_WEAPON_COMPOUND_BOW"
                    "item_class"  "tf_weapon_compound_bow"
                    "item_name"   "#TF_Weapon_CompoundBow"
                    "item_slot"   "primary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "sniper"    "1"
                    }
                    "attach_to_hands" "1"
                    "model_player"    "models/weapons/c_models/c_bow/c_bow.mdl"
                }
                "1092"
                {
                    "name"        "TF_WEAPON_COMPOUND_BOW_LONGBOW"
                    "item_class"  "tf_weapon_compound_bow"
                    "item_name"   "#TF_Weapon_CompoundBow_Lonbow"
                    "item_slot"   "primary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "sniper"    "1"
                    }
                    "attach_to_hands" "1"
                    "model_player"    "models/weapons/c_models/c_bow_lonbow/c_bow_lonbow.mdl"
                }
            }
        }
        """;

    /// <summary>No base items, and two item classes that are both stems of one weapon name, longer one first.</summary>
    private const string StemSchema = """
        "items_game"
        {
            "qualities"
            {
            }
            "attributes"
            {
            }
            "items"
            {
                "70"
                {
                    "name"        "ITEM_70"
                    "item_class"  "tf_weapon_stem_long"
                    "item_name"   "#Item_70"
                    "item_slot"   "primary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "sniper"    "1"
                    }
                }
                "71"
                {
                    "name"        "ITEM_71"
                    "item_class"  "tf_weapon_stem"
                    "item_name"   "#Item_71"
                    "item_slot"   "primary"
                    "item_quality" "normal"
                    "min_ilevel"  "1"
                    "max_ilevel"  "1"
                    "used_by_classes"
                    {
                        "sniper"    "1"
                    }
                }
            }
        }
        """;

    [Fact]
    public void A_class_the_base_items_miss_takes_the_longest_stem_among_all_items()
    {
        // The same rule the base-item walk keeps: the walk's later, shorter
        // stem must not replace the longer one it already found.
        var types = WeaponFixtures.Types(StemSchema);

        Assert.Equal(70, types.BaseItemFor("tf_weapon_stem_long_red")?.Defindex);
    }

    [Fact]
    public void A_class_the_base_items_miss_wears_the_classs_lowest_item()
    {
        // The Huntsman: the base items end at the launch weapons, so
        // tf_weapon_compound_bow wears no base item, and stock's dress reads
        // the equipped item (CTFWeaponBase::GetViewModel,
        // tf_weaponbase.cpp:651-676). The class's lowest defindex stands in.
        var types = WeaponFixtures.Types(HuntsmanSchema);
        var rows = types.Rows().ToDictionary(r => r.Name);

        Assert.Equal(56, types.BaseItemFor("tf_weapon_compound_bow")?.Defindex);
        Assert.Equal("models/weapons/c_models/c_bow/c_bow.mdl", rows["tf_weapon_compound_bow"].ViewModel);
        Assert.Equal("models/weapons/c_models/c_bow/c_bow.mdl", rows["tf_weapon_compound_bow"].WorldModel);
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

        Assert.Equal(WeaponTypes.All.Length + 34, serverCatalog.Count);
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
