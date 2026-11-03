extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;
using TF2.Tests.Weapons;

using Xunit;

using CAbi = client::SourceSharp.Abi;
using CClient = client::SourceSharp.Client;
using CTF = client::TF2.Shared;
using CTFEcon = client::TF2.Shared.Econ;
using CShared = client::SourceSharp.Shared;
using ClientOffset = client::TF2.Client.Weapons.TFViewmodelOffset;
using ClientMinCvar = client::TF2.Client.TFUseMinViewmodelsConVar;
using ClientOverrideCvar = client::TF2.Client.TFViewmodelsOffsetOverrideConVar;
using CTFPlayer = client::TF2.Client.Player;
using CTFData = client::TF2.Shared.Player;

namespace TF2.Tests.Player;

/// <summary>
/// The weapons wired to the carrier (ITFWeaponOwners) on both sides: the
/// class (the ammo maxima), the one condition set (the minigun's and sniper
/// rifle's aiming and zoom), GetCritMult, the round-win arm and the items.
/// </summary>
public sealed class TFWeaponOwnersTests
{
    private static (PlayerRig Rig, TFServerWeaponOwners Owners, TFRoundWinners Winners, TFPlayerItems Items) Server(TFClass cls)
    {
        var rig = new PlayerRig().As(cls);
        var winners = new TFRoundWinners();
        var items = new TFPlayerItems();
        return (rig, new TFServerWeaponOwners(rig.Carriers, winners, items, rig.Engine, new GroundDirectory(), new TFPlayerStats()), winners, items);
    }

    [Fact]
    public void OffGround_is_the_players_flags_word()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        var directory = new GroundDirectory();
        var owners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), new TFPlayerItems(), rig.Engine, directory, new TFPlayerStats());

        directory.Player.State.Flags = 0;
        Assert.True(owners.OffGround(PlayerRig.Index));

        directory.Player.State.Flags = GameMovement.FlOnGround;
        Assert.False(owners.OffGround(PlayerRig.Index));

        // Nobody behind the index is not off the ground either.
        Assert.False(owners.OffGround(PlayerRig.Index + 1));
    }

    [Fact]
    public void The_class_is_the_carriers()
    {
        var (_, owners, _, _) = Server(TFClass.HeavyWeapons);

        Assert.Equal(TFClass.HeavyWeapons, owners.ClassOf(PlayerRig.Index));
        Assert.Equal(TFClass.Undefined, owners.ClassOf(PlayerRig.Index + 1));
    }

    [Fact]
    public void The_conditions_are_the_carriers_one_set()
    {
        var (rig, owners, _, _) = Server(TFClass.HeavyWeapons);

        var conditions = owners.ConditionsOf(PlayerRig.Index);
        Assert.Same(rig.Carriers.ConditionsOf(PlayerRig.Index), conditions);

        conditions!.Add(TFCond.Aiming);

        Assert.True(rig.State.InCond(TFCond.Aiming));
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(255, 4f)]
    [InlineData(85, 2f)]
    public void Crit_mult_remaps_the_carriers_byte(int critMult, float expected)
    {
        var (rig, owners, _, _) = Server(TFClass.Scout);
        rig.State.CritMult = critMult;

        Assert.Equal(expected, owners.CritMult(PlayerRig.Index), 3);
    }

    [Fact]
    public void Only_the_winning_team_wins_crits()
    {
        var (rig, owners, winners, _) = Server(TFClass.Scout);
        Assert.False(owners.WinsCrits(PlayerRig.Index));

        winners.WinningTeam = TFTeam.Red;
        Assert.True(owners.WinsCrits(PlayerRig.Index));

        rig.Engine.Returns("Players.GetTeam", (int)TFTeam.Blue);
        Assert.False(owners.WinsCrits(PlayerRig.Index));
    }

    [Fact]
    public void The_item_is_the_one_the_loadout_handed_out()
    {
        var (rig, owners, _, items) = Server(TFClass.Scout);
        var loadout = new TF2.Server.Econ.TFLoadoutProvider(new TF2.Server.Econ.LocalInventory(
                new TF2.Server.Econ.InventoryStore(new ModInfo(_ => null), rig.Schema, rig.Log), rig.Schema,
                new NoClients(), rig.Log),
            rig.Schema, rig.Carriers, items);
        Assert.Null(owners.ItemOf(PlayerRig.Index, "tf_weapon_scattergun"));

        _ = loadout.Weapons(PlayerRig.Player);

        var item = owners.ItemOf(PlayerRig.Index, "tf_weapon_scattergun");
        var container = Assert.IsType<AttributeContainer>(item);
        Assert.Equal("TF_WEAPON_SCATTERGUN", container.Item!.Name);
        Assert.Equal(AttributeProviderType.Weapon, container.ProviderType);
    }

    /// <summary>The kit hand-out (TFLoadoutProvider.Weapons) writes each weapon's
    /// item definition index onto the carrier, keyed by the catalogue row — what
    /// stock networks on each weapon's own item view (m_iItemDefinitionIndex,
    /// DT_ScriptCreatedItem, econ_item_view.cpp:182) — as defindex + 1, so the
    /// array's untouched zero is no item and the schema's own item 0 (the bat)
    /// keeps a value of its own. A new kit starts from no items.</summary>
    [Fact]
    public void The_loadout_writes_each_weapons_item_defindex_on_the_carrier()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        int scattergun = rig.Catalog.IndexOf("tf_weapon_scattergun");
        Assert.True(scattergun >= 0);
        Assert.Equal(0, rig.State!.WeaponItemDefIndex[scattergun]);   // no kit handed out yet

        _ = rig.Loadout.Weapons(PlayerRig.Player);

        // The schema's scattergun: item 13 of the trimmed fixture, on the wire as 14.
        Assert.Equal(14, rig.State.WeaponItemDefIndex[scattergun]);

        // The soldier's kit clears the scout's rows and writes its own: the
        // shovel (item 6) and the class shotgun (item 10).
        rig.As(TFClass.Soldier);
        _ = rig.Loadout.Weapons(PlayerRig.Player);
        Assert.Equal(0, rig.State.WeaponItemDefIndex[scattergun]);
        Assert.Equal(7, rig.State.WeaponItemDefIndex[rig.Catalog.IndexOf("tf_weapon_shovel")]);
        Assert.Equal(11, rig.State.WeaponItemDefIndex[rig.Catalog.IndexOf("tf_weapon_shotgun_soldier")]);
    }

    [Fact]
    public void The_carry_rule_reads_the_class_through_the_owners()
    {
        var (rig, owners, _, _) = Server(TFClass.Scout);
        var rule = new TFAmmoCarryRule(rig.Classes, owners);
        var primary = new AmmoType(TFAmmoNames.Name(TFAmmo.Primary), default, TFAmmoTypes.AmmoMax);

        Assert.Equal(32, rule.MaxCarry(PlayerRig.Index, in primary, TFAmmoTypes.AmmoMax));
    }

    /// <summary>
    /// The player's own attribute manager (stock's <c>m_AttributeManager</c>):
    /// the server's <see cref="ITFWeaponOwners.ManagerOf"/> answers what
    /// <see cref="TFPlayerItems"/> holds, and a granted <c>mult_maxammo_metal</c>
    /// scales the class's engineer maximum through the carry rule.
    /// </summary>
    [Fact]
    public void The_players_attribute_manager_scales_the_carry_rule()
    {
        var (rig, owners, _, items) = Server(TFClass.Engineer);
        var rule = new TFAmmoCarryRule(rig.Classes, owners);
        var metal = new AmmoType(TFAmmoNames.Name(TFAmmo.Metal), default, TFAmmoTypes.AmmoMax);

        Assert.Null(owners.ManagerOf(PlayerRig.Index));
        Assert.Equal(200, rule.MaxCarry(PlayerRig.Index, in metal, TFAmmoTypes.AmmoMax));

        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5000, "attr 5000", "mult_maxammo_metal",
                                    AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("x")), 1.5f));
        items.SetManager(PlayerRig.Index, manager);

        Assert.Same(manager, owners.ManagerOf(PlayerRig.Index));
        Assert.Equal(300, rule.MaxCarry(PlayerRig.Index, in metal, TFAmmoTypes.AmmoMax));

        // The player leaves: the manager and the scale go with them.
        items.Forget(PlayerRig.Index);
        Assert.Null(owners.ManagerOf(PlayerRig.Index));
        Assert.Equal(200, rule.MaxCarry(PlayerRig.Index, in metal, TFAmmoTypes.AmmoMax));
    }

    [Fact]
    public void A_win_boosts_the_living_winners_for_the_bonus_time()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        var winners = new TFRoundWinners();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var roster = new Roster { Red = [PlayerRig.Player] };
        var crits = new TFRoundWinCrits(winners, roster, conditions);

        crits.OnRoundWon(TFTeam.Red, WinReason.AllPointsCaptured, 16f);

        Assert.Equal(TFTeam.Red, winners.WinningTeam);
        Assert.True(rig.State.InCond(TFCond.CritboostedBonusTime));
        Assert.Equal(16f, conditions.Of(PlayerRig.Index)!.Duration(TFCond.CritboostedBonusTime));
    }

    [Fact]
    public void A_dead_winner_is_not_boosted()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine);
        var crits = new TFRoundWinCrits(new TFRoundWinners(), new Roster { Red = [PlayerRig.Player], Dead = true }, conditions);

        crits.OnRoundWon(TFTeam.Red, WinReason.AllPointsCaptured, 16f);

        Assert.False(rig.State.InCond(TFCond.CritboostedBonusTime));
    }

    [Fact]
    public void A_stalemate_win_boosts_nobody_and_leaving_team_win_ends_the_arm()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        var winners = new TFRoundWinners();
        var crits = new TFRoundWinCrits(winners, new Roster { Red = [PlayerRig.Player] },
                                        new TFPlayerConditions(rig.Carriers, rig.Engine));

        crits.OnRoundWon(TFTeam.Unassigned, WinReason.Stalemate, 16f);
        Assert.False(rig.State.InCond(TFCond.CritboostedBonusTime));

        crits.OnRoundWon(TFTeam.Red, WinReason.AllPointsCaptured, 16f);
        crits.OnStateChanged(RoundState.TeamWin, RoundState.Preround);
        Assert.Equal(TFTeam.Unassigned, winners.WinningTeam);
    }

    [Fact]
    public void The_client_binds_the_local_players_conditions_to_the_working_copy()
    {
        var working = new CTFData.TFPlayerNetState();
        working.BindLocal();
        var owners = new CTFPlayer.TFClientWeaponOwners(new ClientData(working, local: 2), new Standalones());

        var conditions = owners.ConditionsOf(2);
        Assert.NotNull(conditions);
        conditions!.Add(CTF.TFCond.Aiming);

        Assert.True(CTFData.TFPlayerNetStateViews.InCond(working, CTF.TFCond.Aiming));
        Assert.Null(owners.ConditionsOf(5));
    }

    [Fact]
    public void A_reset_working_copy_is_what_the_client_set_reads_next()
    {
        var working = new CTFData.TFPlayerNetState();
        working.BindLocal();
        var owners = new CTFPlayer.TFClientWeaponOwners(new ClientData(working, local: 2), new Standalones());
        owners.ConditionsOf(2)!.Add(CTF.TFCond.Aiming);

        CTFData.TFPlayerNetStateViews.SetConditions(working, default);

        Assert.False(owners.ConditionsOf(2)!.Has(CTF.TFCond.Aiming));
    }

    [Fact]
    public void The_clients_class_is_the_networked_one_for_anyone_else()
    {
        var other = new CTFData.TFPlayerNetState();
        other.BindLocal();
        CTFData.TFPlayerNetStateViews.SetClass(other, CTF.TFClass.Medic);
        var owners = new CTFPlayer.TFClientWeaponOwners(new ClientData(null, local: 2, networked: other), new Standalones());

        Assert.Equal(CTF.TFClass.Medic, owners.ClassOf(7));
        Assert.False(owners.WinsCrits(7));
        Assert.Null(owners.ItemOf(7, "tf_weapon_bonesaw"));
    }

    /// <summary>The client's item (<see cref="CTFPlayer.TFClientWeaponOwners.ItemOf"/>):
    /// the defindex the carrier carries for the weapon's catalogue row, looked
    /// up in the client's own parsed schema — stock's
    /// <c>CEconItemView::GetItemDefinition</c> (<c>GetStaticData</c> looks
    /// <c>m_iItemDefinitionIndex</c> up in the schema, econ_item_view.cpp:435). The
    /// definition's statics ride: the scattergun's prefab gives it the stattrak
    /// scale (0.608, a percentage), so the walk answers it.</summary>
    [Fact]
    public void The_client_answers_the_schema_item_behind_a_predicted_weapon()
    {
        var econ = new ClientEcon();
        var networked = new CTFData.TFPlayerNetState();
        networked.BindLocal();
        networked.WeaponItemDefIndex[econ.Catalog.IndexOf("tf_weapon_scattergun")] = 13 + 1;   // the fixture's scattergun, on the wire as defindex + 1
        var owners = econ.Owners(networked);

        var container = Assert.IsType<CTFEcon.AttributeContainer>(owners.ItemOf(2, "tf_weapon_scattergun"));
        Assert.Equal(13, container.Item!.Defindex);
        Assert.Equal("TF_WEAPON_SCATTERGUN", container.Item!.Name);
        Assert.Equal(CTFEcon.AttributeProviderType.Weapon, container.ProviderType);
        Assert.Equal(0.608f, new CTFEcon.AttributeHooks().Float(1f, "weapon_stattrak_module_scale", container), 3);
    }

    /// <summary>No item is answered when nothing names one: the untouched row,
    /// a weapon that is no catalogue row, and a defindex the client's parsed
    /// schema does not hold (stock's item view without a definition —
    /// <c>GetItem</c> answers null).</summary>
    [Fact]
    public void The_client_answers_no_item_when_nothing_names_one()
    {
        var econ = new ClientEcon();
        var networked = new CTFData.TFPlayerNetState();
        networked.BindLocal();
        var owners = econ.Owners(networked);

        Assert.Null(owners.ItemOf(2, "tf_weapon_scattergun"));                       // the row is untouched

        networked.WeaponItemDefIndex[econ.Catalog.IndexOf("tf_weapon_bonesaw")] = 8 + 1;
        Assert.Null(owners.ItemOf(2, "tf_weapon_not_a_weapon"));                     // no catalogue row

        networked.WeaponItemDefIndex[econ.Catalog.IndexOf("tf_weapon_scattergun")] = 999_999 + 1;
        Assert.Null(owners.ItemOf(2, "tf_weapon_scattergun"));                      // not in the schema
    }

    /// <summary>The deferred row's proof: the server's kit item and the client's
    /// schema-rebuilt item — the defindex the same loadout write put on the
    /// carrier's row, read by the client's catalogue (the same row, both sides
    /// build it from the same scripts) and rebuilt from the client's own schema
    /// — walk alike under each side's own AttributeHooks over a stimulus that
    /// is not the identity: the scattergun prefab's 0.608 stattrak scale.</summary>
    [Fact]
    public void The_two_sides_walk_the_same_weapons_item_alike()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        _ = rig.Loadout.Weapons(PlayerRig.Player);
        var serverOwners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), rig.Items,
                                                    rig.Engine, new GroundDirectory(), new TFPlayerStats());
        var serverItem = serverOwners.ItemOf(PlayerRig.Index, "tf_weapon_scattergun");
        Assert.NotNull(serverItem);

        int serverRow = rig.Catalog.IndexOf("tf_weapon_scattergun");
        var econ = new ClientEcon();
        int clientRow = econ.Catalog.IndexOf("tf_weapon_scattergun");
        Assert.Equal(serverRow, clientRow);   // the wire index: both sides number the rows alike

        var networked = new CTFData.TFPlayerNetState();
        networked.BindLocal();
        networked.WeaponItemDefIndex[clientRow] = rig.State!.WeaponItemDefIndex[serverRow];
        var clientItem = econ.Owners(networked).ItemOf(2, "tf_weapon_scattergun");
        Assert.NotNull(clientItem);

        Assert.Equal(0.608f, new AttributeHooks().Float(1f, "weapon_stattrak_module_scale", serverItem), 3);
        Assert.Equal(0.608f, new CTFEcon.AttributeHooks().Float(1f, "weapon_stattrak_module_scale", clientItem), 3);
    }

    /// <summary>The min-viewmodel chain end to end on stock weapons
    /// (tf_weaponbase.cpp GetViewmodelOffset): the sniper's kit item carries
    /// the prefab's <c>min_viewmodel_offset</c> "10 0 -10" on both sides, the
    /// wire carries only the defindex, and the client's
    /// <see cref="ClientOffset"/> under <c>tf_use_min_viewmodels 1</c> — fed
    /// the real client owners (arsenal, carrier state, schema, catalogue and
    /// the local index) — plucks exactly that triple.</summary>
    [Fact]
    public void The_snipers_min_viewmodel_offset_walks_the_wire_into_the_clients_offset()
    {
        var rig = new PlayerRig().As(TFClass.Sniper);
        _ = rig.Loadout.Weapons(PlayerRig.Player);
        var serverOwners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), rig.Items,
                                                    rig.Engine, new GroundDirectory(), new TFPlayerStats());
        var serverItem = serverOwners.ItemOf(PlayerRig.Index, "tf_weapon_sniperrifle");
        Assert.NotNull(serverItem);
        Assert.Equal("10 0 -10", new AttributeHooks().String(null, "min_viewmodel_offset", serverItem));

        int serverRow = rig.Catalog.IndexOf("tf_weapon_sniperrifle");
        var econ = new ClientEcon();
        int clientRow = econ.Catalog.IndexOf("tf_weapon_sniperrifle");
        Assert.Equal(serverRow, clientRow);

        var networked = new CTFData.TFPlayerNetState();
        networked.BindLocal();
        Assert.NotEqual(0, rig.State!.WeaponItemDefIndex[serverRow]);   // the loadout wrote the rifle
        networked.WeaponItemDefIndex[clientRow] = rig.State.WeaponItemDefIndex[serverRow];
        var clientItem = econ.Owners(networked).ItemOf(2, "tf_weapon_sniperrifle");
        Assert.NotNull(clientItem);
        Assert.Equal("10 0 -10", new CTFEcon.AttributeHooks().String(null, "min_viewmodel_offset", clientItem));

        var engine = new CAbi.RecordingEngine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        var minCvar = new ClientMinCvar();
        minCvar.Bind(-1, "tf_use_min_viewmodels", "1");
        var overrideCvar = new ClientOverrideCvar();
        overrideCvar.Bind(-1, "tf_viewmodels_offset_override", "");
        var owners = new CTFPlayer.TFClientWeaponOwners(new ClientData(null, 2, networked), new Standalones(),
                                                        null, econ.Schema, econ.Catalog, new StubArsenal(), engine);
        var offset = new ClientOffset(minCvar, overrideCvar, engine, owners);

        var taken = new float[3];
        Assert.True(offset.Offset(taken));
        Assert.Equal(new[] { 10f, 0f, -10f }, taken);

        minCvar.Bind(-1, "tf_use_min_viewmodels", "0");
        Assert.False(offset.Offset(taken));
    }

    /// <summary>The client's own econ parts over the shipped scripts and the
    /// trimmed fixture: the client's copies of <c>TF2.Shared.Econ</c> and
    /// <c>SourceSharp.Shared</c> are distinct compiled types from the server's
    /// this project also holds, and the alias names the client's (precedent
    /// <c>TF2ClientPredictionTests.ClientWorld</c>).</summary>
    private sealed class ClientEcon
    {
        public readonly CTFEcon.ItemSchema Schema;
        public readonly CShared.WeaponCatalog Catalog;

        public ClientEcon()
        {
            var files = new ClientFiles();
            Schema = new CTFEcon.ItemSchema(files, new CAbi.RecordingLog());
            Catalog = new CShared.WeaponCatalog(
                () => [new CTF.TFWeaponTypes(new CTF.TFWeaponScripts(new CAbi.RecordingLog(), files),
                                             new CTF.TFClassTable(new CAbi.RecordingLog(), files),
                                             new CTFEcon.ItemSchema(files, new CAbi.RecordingLog()))],
                new CAbi.RecordingLog());
        }

        /// <summary>The owner over these parts, reading <paramref name="networked"/> as everyone else's state.</summary>
        public CTFPlayer.TFClientWeaponOwners Owners(CTFData.TFPlayerNetState? networked) =>
            new(new ClientData(null, local: 2, networked: networked), new Standalones(), null, Schema, Catalog);
    }

    /// <summary>The shipped scripts and the trimmed schema, served as the client's
    /// <see cref="CAbi.IEngineFiles"/> — the pack the client really has at boot
    /// (a test-served GAME path; a missing file is the schema refusing, never a
    /// silent pass).</summary>
    private sealed class ClientFiles : CAbi.IEngineFiles
    {
        private readonly TFClassTableTests.Files _files = WeaponFixtures.ShippedWithClasses();

        public ClientFiles() =>
            _files.Bytes[ItemSchema.FilePath] = System.Text.Encoding.UTF8.GetBytes(EconTestFiles.Fixture("items_game.trimmed.txt"));

        public int GameFileSize(string path) => _files.GameFileSize(path);
        public string? ReadGameFile(string path) => _files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => _files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => _files.ReadFileOnPath(path, pathId);
    }

    /// <summary>CalcIsAttackCritical's round-win arm (tf_weaponbase.cpp:1563):
    /// during TeamWin the winners' side crits outright — not only through the
    /// 16 s CritboostedBonusTime grant — so the client's prediction reads the
    /// resource's words the server writes every think.</summary>
    [Fact]
    public void The_client_crits_the_winning_teams_player_during_team_win()
    {
        var resources = new CTFPlayer.TFClientPlayerResources();
        var resource = new CTFPlayer.ClientTFPlayerResource(new CAbi.RecordingEngine(), resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.RoundState = (byte)RoundState.TeamWin;
        resource.State.WinningTeam = (byte)TFTeam.Red;
        resource.State.Team[2] = (byte)TFTeam.Red;
        var owners = new CTFPlayer.TFClientWeaponOwners(new ClientData(null, local: 2), new Standalones(), resources);

        Assert.True(owners.WinsCrits(2));

        // The losers do not crit.
        resource.State.Team[2] = (byte)TFTeam.Blue;
        Assert.False(owners.WinsCrits(2));

        // Nor does anyone once the round is running again.
        resource.State.Team[2] = (byte)TFTeam.Red;
        resource.State.RoundState = (byte)RoundState.RoundRunning;
        Assert.False(owners.WinsCrits(2));

        // And nobody crits with no resource arrived at all.
        resources.Live = null;
        Assert.False(owners.WinsCrits(2));
    }

    [Fact]
    public void The_eyes_under_water_is_the_movement_state_s_water_level()
    {
        var rig = new PlayerRig().As(TFClass.Scout);
        var player = new SsPlayer(rig.Engine, rig.Engine, TestServices.Impacts(rig.Engine), TestServices.Fluid())
            { Handle = PlayerRig.Player };
        player.State.BindLocal();
        var owners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), new TFPlayerItems(),
                                              rig.Engine, new OnePlayer(player), new TFPlayerStats());

        player.State.WaterLevel = GameMovement.WlEyes;
        Assert.True(owners.EyesUnderwater(PlayerRig.Index));

        player.State.WaterLevel = GameMovement.WlWaist;
        Assert.False(owners.EyesUnderwater(PlayerRig.Index));

        // A player index with no entity is not underwater.
        Assert.False(owners.EyesUnderwater(PlayerRig.Index + 1));
    }

    [Fact]
    public void The_client_refuses_the_flamethrower_only_at_the_eyes_water_level()
    {
        var owners = new CTFPlayer.TFClientWeaponOwners(new ClientData(null, local: 2), new Standalones(3));

        Assert.True(owners.EyesUnderwater(2));
        Assert.False(owners.EyesUnderwater(7), "only the local player's prediction is read");
    }

    [Fact]
    public void The_client_reads_the_local_players_ground_flag()
    {
        var airborne = new CTFPlayer.TFClientWeaponOwners(new ClientData(null, local: 2), new Standalones(flags: 0));
        var grounded = new CTFPlayer.TFClientWeaponOwners(new ClientData(null, local: 2), new Standalones(flags: 1));

        Assert.True(airborne.OffGround(2));
        Assert.False(grounded.OffGround(2));
        Assert.False(airborne.OffGround(7), "nobody behind the index is not off the ground");
    }

    [Fact]
    public void The_server_scan_registers_the_carriers_owners()
    {
        var c = new ServiceContainer();
        var engine = new RecordingEngine();
        c.AddInstance<IEnginePlayers>(engine);
        c.AddInstance<IEntityDirectory>(new TF2.Tests.Combat.NoEntities());
        SideServices.Register(c, Side.Server, typeof(TFServerWeaponOwners).Assembly.GetTypes());

        Assert.IsType<TFServerWeaponOwners>(c.Resolve(typeof(ITFWeaponOwners)));
    }

    [Fact]
    public void The_client_scan_registers_the_carriers_owners()
    {
        var c = new CAbi.ServiceContainer();
        c.AddInstance<ILog>(new RecordingLog());
        c.AddInstance<CAbi.IEngineFiles>(new ClientFiles());   // the owner now reaches the schema through the scan
        c.AddInstance(CAbi.NetStateCatalog.Of(typeof(CTFPlayer.TFClientWeaponOwners).Assembly, new RecordingLog()));
        c.AddInstance<CClient.IClientStandalones>(new Standalones());
        // The prediction's clock and cloak convars, as the client's composition holds them.
        c.AddInstance<CAbi.IEngineEnvironment>(PredictionDeps.Engine());
        var cloakVars = PredictionDeps.SpyConVars();
        c.AddInstance(cloakVars.Get<CTF.TFSpyCloakConsumeRateConVar>()!);
        c.AddInstance(cloakVars.Get<CTF.TFSpyCloakRegenRateConVar>()!);
        c.AddInstance(cloakVars.Get<CTF.TFSpyInvisTimeConVar>()!);
        c.AddInstance(cloakVars.Get<CTF.TFSpyInvisUnstealthTimeConVar>()!);
        CAbi.SideServices.Register(c, Side.Client, typeof(CTFPlayer.TFClientWeaponOwners).Assembly.GetTypes());

        Assert.IsType<CTFPlayer.TFClientWeaponOwners>(c.Resolve(typeof(CTF.ITFWeaponOwners)));
    }

    private sealed class ClientData(CTFData.TFPlayerNetState? working, int local, CTFData.TFPlayerNetState? networked = null)
        : CTFData.ITFPlayerData
    {
        public CTFData.TFPlayerNetState? Predicted(int playerIndex) => playerIndex == local ? working : null;
        public CTFData.TFPlayerNetState? Networked(int playerIndex) => networked;
    }

    /// <summary>An arsenal with the sniper rifle in the hands: enough for the
    /// owners' active-item walk.</summary>
    private sealed class StubArsenal : CClient.ILocalArsenal
    {
        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = "tf_weapon_sniperrifle";
            clip = 10;
            reserve = 40;
            return true;
        }
    }

    /// <summary>A directory with one hand-built player at <see cref="PlayerRig.Player"/>, on the ground by default.</summary>
    private sealed class GroundDirectory : IEntityDirectory
    {
        public GroundDirectory()
        {
            var engine = new RecordingEngine();
            Player = new SsPlayer(engine, engine, TestServices.Impacts(engine), TestServices.Fluid())
                { Handle = PlayerRig.Player };
            Player.State.BindLocal();
            Player.State.Flags = GameMovement.FlOnGround;
        }

        public SsPlayer Player { get; }

        public ScriptedEntity? Get(EntityHandle h) => h == PlayerRig.Player ? Player : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>The standalones' view, with one player at index 2 at a given water level and flags word (on the ground by default).</summary>
    private sealed class Standalones(byte waterLevel = 0, uint flags = 1) : CClient.IClientStandalones
    {
        public IReadOnlyList<CClient.Standalone.ClientStandaloneEntity> Live() => [];

        public CClient.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
        {
            if (entityIndex != 2)
                return null;

            var player = new CClient.Standalone.ClientSsPlayer(new CAbi.RecordingEngine());
            player.State.BindLocal();
            player.State.WaterLevel = waterLevel;
            player.State.Flags = flags;
            return player;
        }
    }

    private sealed class OnePlayer(SsPlayer? player) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h) => h == PlayerRig.Player ? player : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == EntityNames.Player;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    internal sealed class NoClients : IConnectedClients
    {
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => entIndex;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    internal sealed class Roster : ITeamRoundRoster
    {
        public List<EntityHandle> Red { get; init; } = [];
        public bool Dead { get; init; }
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => team == TFTeam.Red ? Red : [];
        public TFTeam TeamOf(EntityHandle player) => Red.Contains(player) ? TFTeam.Red : TFTeam.Unassigned;
        public bool IsAlive(EntityHandle player) => !Dead;
        public float DeathTime(EntityHandle player) => -1f;
        public bool IsReadyToPlay(EntityHandle player) => true;
        public List<EntityHandle> Respawned { get; } = [];
        public void ForceRespawn(EntityHandle player) => Respawned.Add(player);
        public bool ChangeTeam(EntityHandle player, TFTeam team) => true;
        public string Name(EntityHandle player) => "player";
        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    [Fact]
    public void The_server_owners_answer_the_rounds_ranged_pair_the_crit_rate_measures_from()
    {
        // stock's FindPlayerStats(pPlayer)->statsCurrentRound read for the
        // observed rate (tf_weaponbase.cpp:4639-4645): what the damage layer
        // books, the owners hand to the crit roll.
        var rig = new PlayerRig().As(TFClass.Scout);
        var stats = new TFPlayerStats();
        var owners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), new TFPlayerItems(),
                                              rig.Engine, new GroundDirectory(), stats);

        Assert.Equal((0, 0), owners.RangedCritDamageOf(PlayerRig.Index));   // nothing booked yet

        stats.Increment(PlayerRig.Player, TFStat.DamageRanged, 100);
        stats.Increment(PlayerRig.Player, TFStat.DamageRangedCritRandom, 30);
        stats.Increment(PlayerRig.Player, TFStat.DamageRangedCritBoosted, 40);   // not the random one

        Assert.Equal((100, 30), owners.RangedCritDamageOf(PlayerRig.Index));
    }
}
