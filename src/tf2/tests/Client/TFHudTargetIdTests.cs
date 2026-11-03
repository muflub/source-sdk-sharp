extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Tests.Weapons;

using Xunit;

using Adapter = client::TF2.Client.Ui.TFHudTargetId;
using Carrier = client::TF2.Shared.Player.TFPlayerNetState;
using PlayerData = client::TF2.Shared.Player.ITFPlayerData;
using ClientViews = client::TF2.Shared.Player.TFPlayerNetStateViews;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Names = client::SourceSharp.Client.Vgui.IPlayerNames;
using PlayerResource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using SsPlayer = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using SsWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using Standalones = client::SourceSharp.Client.IClientStandalones;
using StandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using TFClassId = client::TF2.Shared.TFClass;
using TFCondId = client::TF2.Shared.TFCond;
using ViewSlots = client::SourceSharp.Client.Interop.IPixelVisSlots;
using ViewState = client::SourceSharp.Client.Interop.CurrentViewState;
using Catalog = client::SourceSharp.Shared.WeaponCatalog;

// The contract assembly is unaliased: the interface and the line record are
// global, as the kill feed's are.
using LineKind = TF2.Client.Ui.TFTargetIdLineKind;

namespace TF2.Tests.Client;

/// <summary>
/// The target ID's reads against stock's <c>CTargetID</c> (tf_hud_target_id.cpp):
/// the view's centre ray — the wall first, then the players before it, past
/// the local player (UpdateIDTarget, c_tf_player.cpp:7041-7120) — the
/// validity gate (<c>IsValidIDTarget</c> :340-456, with the three-way
/// InSameDisguisedTeam) and the content gate (:806-810), the team named by
/// the disguise when the real teams differ (:776-779) and the data line's
/// stock order (<c>GetTargetIDDataString</c>, :9748-9829). The aimed
/// player's health rides the player mirror (the core's
/// <c>PlayerNetState.Health</c>), the max off the <c>tf_player_manager</c>
/// row.
/// </summary>
public sealed class TFHudTargetIdTests
{
    // Player 1 is the local RED scout; player 2 the aimed one. The ray stops
    // on player 2 unless a fact says a wall stands between them.
    private const int Local = 1;
    private const int Target = 2;

    private sealed class Rig
    {
        public readonly Engine Engine = new();
        public readonly PlayerResources Resources = new();
        public readonly CarrierList Carriers = new();
        public readonly Directory Stands = new();
        public readonly Roster Names = new((Local, "Gordon"), (Target, "Alyx"), (3, "Barney"));
        public readonly ViewSource View = new();
        public readonly Catalog Catalog;
        public readonly client::TF2.Shared.TFClassTable Classes;
        public readonly Adapter Source;

        public Rig()
        {
            Engine.Returns("Hud.LocalPlayerIndex", Local);
            Engine.Returns("WorldQuery.ClientTraceLine", Miss);
            Engine.Returns("WorldQuery.ClientClipRayToEntities", Hit(Target));

            // The weapon table with the TF rows, as the client builds it: the
            // charge line names the medigun by its index in it.
            var log = new RecordingLog();
            var files = new ClientFiles(WeaponFixtures.ShippedWithClasses());
            var scripts = new client::TF2.Shared.TFWeaponScripts(log, files);
            var classes = new client::TF2.Shared.TFClassTable(log, files);
            Catalog = new Catalog(() => [new client::TF2.Shared.TFWeaponTypes(
                scripts, classes,
                new client::TF2.Shared.Econ.ItemSchema(files, log))], log);

            Source = new Adapter(Engine, Engine, View, Stands, Carriers, Resources, Names, Catalog, classes);
            Classes = classes;
        }

        /// <summary>The target's carried weapon — the medigun, or another
        /// hand by its type id.</summary>
        public SsWeapon Gun(int ownerIndex, float meter, string weapon = "tf_weapon_medigun")
            => Stands.Gun(ownerIndex, Catalog.IndexOf(weapon), meter);

        /// <summary>The tf_player_manager row arrives: the two players'
        /// teams and the aimed one's max health.</summary>
        public PlayerResource ArriveResources(int targetTeam = 2, short maxHealth = 125)
        {
            var cross = new PlayerResource(Engine, Resources);
            cross.State.BindLocal();
            cross.State.Team[Local] = 2;
            cross.State.Team[Target] = (byte)targetTeam;
            cross.State.MaxHealth[Target] = maxHealth;
            cross.OnSpawn();
            return cross;
        }

        /// <summary>The aimed player's carrier, decoded with a class.</summary>
        public Carrier TargetCarrier(TFClassId playerClass, int team = 2, int health = 80)
        {
            var carrier = new Carrier();
            carrier.BindLocal();
            carrier.OwnerIndex = Target;
            ClientViews.SetClass(carrier, playerClass);
            Stands.At(Target, health);
            return Carriers.Serve(Target, carrier);
        }

        /// <summary>The local player's carrier, decoded.</summary>
        public Carrier LocalCarrier(TFClassId playerClass)
        {
            var carrier = new Carrier();
            carrier.BindLocal();
            carrier.OwnerIndex = Local;
            ClientViews.SetClass(carrier, playerClass);
            return Carriers.Serve(Local, carrier);
        }

        public void RayHits(int entity) => Engine.Returns("WorldQuery.ClientClipRayToEntities", Hit(entity));

        public void WallBetween() => Engine.Returns("WorldQuery.ClientClipRayToEntities", Miss);

        private static ClientTraceResult Hit(int entity) =>
            new(true, 0.5f, new Vector(0, 100, 64), new Vector(0, -1, 0), 0, entity, 0, 0);

        private static ClientTraceResult Miss =>
            new(false, 1f, new Vector(0, 56756, 64), new Vector(0, -1, 0), -1, -1, 0, 0);
    }

    /// <summary>The file seam over the client's copy of the interface — the
    /// shared interface compiles into each side (the HUD sources' shim).</summary>
    private sealed class ClientFiles(TFClassTableTests.Files files) : client::SourceSharp.Abi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    /// <summary>The client's decoded carriers, the facts hand out by index.</summary>
    private sealed class CarrierList : PlayerData
    {
        private readonly Dictionary<int, Carrier> _states = new();

        public Carrier Serve(int index, Carrier carrier)
        {
            _states[index] = carrier;
            return carrier;
        }

        public Carrier? Predicted(int playerIndex) => null;

        public Carrier? Networked(int playerIndex) => _states.TryGetValue(playerIndex, out var s) ? s : null;
    }

    /// <summary>The standalone directory: a player body at an index, with a
    /// health, when a fact places one — and a carried weapon when it does.</summary>
    private sealed class Directory : Standalones
    {
        private readonly Dictionary<int, SsPlayer> _players = new();
        private readonly Dictionary<int, SsWeapon> _guns = new();

        public SsPlayer At(int index, int health)
        {
            var player = new SsPlayer(new Engine());
            player.State.BindLocal();
            player.State.Health = health;
            _players[index] = player;
            return player;
        }

        /// <summary>A carried weapon, held by its owner's index.</summary>
        public SsWeapon Gun(int ownerIndex, int typeId, float meter)
        {
            var gun = new SsWeapon(new Engine());
            gun.State.BindLocal();
            gun.State.OwnerIndex = ownerIndex;
            gun.State.WeaponTypeId = typeId;
            gun.State.BehaviourMeter = meter;
            _guns[ownerIndex] = gun;
            return gun;
        }

        public IReadOnlyList<StandaloneEntity> Live()
            => _players.Values.Cast<StandaloneEntity>().Concat(_guns.Values).ToList();

        public StandaloneEntity? GetByIndex(int entityIndex)
            => _players.TryGetValue(entityIndex, out var player) ? player
             : _guns.TryGetValue(entityIndex, out var gun) ? gun : null;

        /// <summary>A body placed as-is — an observer mode a fact sets.</summary>
        public SsPlayer Place(int index, SsPlayer player)
        {
            _players[index] = player;
            return player;
        }
    }

    /// <summary>The render context's view, one per rig (the pixel-vis seam's
    /// CurrentView arm — the others no target ID reads).</summary>
    private sealed class ViewSource : ViewSlots
    {
        public ViewState Current = new(1, new Vector(0, 0, 64), new Vector(0, 1, 0),
                                       new Vector(1, 0, 0), new Vector(0, 0, 1));

        public ViewState View() => Current;

        public int QueryCreate() => 0;

        public void QueryDestroy(int query) { }

        public void QueryReset(int query) { }

        public void QueryPixels(int visible, int count, out int visiblePixels, out int countPixels)
            => (visiblePixels, countPixels) = (0, 0);

        public void Diameters(ReadOnlySpan<float> spheres, Span<float> diameters) { }
    }

    /// <summary>The g_PR role: the two players named, the rest nobody.</summary>
    private sealed class Roster(params (int Slot, string Name)[] rows) : Names
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            foreach (var (found, name) in rows)
                if (found == slot)
                {
                    info = new ClientPlayerInfo(name, 0, false);
                    return true;
                }

            info = default;
            return false;
        }
    }

    [Fact]
    public void A_teammate_aimed_shows_name_health_and_team()
    {
        var rig = new Rig();
        rig.ArriveResources();
        rig.TargetCarrier(TFClassId.Soldier);
        rig.LocalCarrier(TFClassId.Scout);

        Assert.True(rig.Source.Present);
        Assert.Equal("Alyx", rig.Source.Name);
        Assert.Equal(2, rig.Source.Team);
        Assert.True(rig.Source.ShowsHealth);
        Assert.Equal(80, rig.Source.Health);
        Assert.Equal(125, rig.Source.MaxHealth);
        Assert.Equal(LineKind.None, rig.Source.Line.Kind);
    }

    [Fact]
    public void A_plain_enemy_shows_nothing_to_a_scout()
    {
        // IsValidIDTarget :455 — the same-team arms fail, the local player is
        // no spy: stock draws nothing, name and health alike.
        var rig = new Rig();
        rig.ArriveResources(targetTeam: 3);
        rig.TargetCarrier(TFClassId.Soldier, team: 3);
        rig.LocalCarrier(TFClassId.Scout);

        Assert.False(rig.Source.Present);
        Assert.Equal("", rig.Source.Name);
        Assert.False(rig.Source.ShowsHealth);
    }

    [Fact]
    public void A_local_spy_sees_an_enemy_named_with_health()
    {
        var rig = new Rig();
        rig.ArriveResources(targetTeam: 3);
        rig.TargetCarrier(TFClassId.Soldier, team: 3);
        rig.LocalCarrier(TFClassId.Spy);

        Assert.True(rig.Source.Present);
        Assert.Equal("Alyx", rig.Source.Name);
        Assert.Equal(3, rig.Source.Team);
        Assert.True(rig.Source.ShowsHealth);
    }

    [Fact]
    public void A_disguised_enemy_spy_is_named_by_the_disguise_team()
    {
        // UpdateID :776-779 — the real teams differ, so the panel names the
        // disguise's; the InSameDisguisedTeam arm passes the validity gate.
        var rig = new Rig();
        rig.ArriveResources(targetTeam: 3);
        var target = rig.TargetCarrier(TFClassId.Spy, team: 3);
        target.DisguiseClass = (int)TFClassId.Soldier;
        target.DisguiseTeam = 2;
        ClientViews.SetConditions(target, CondMask.Of(TFCondId.Disguised));
        rig.LocalCarrier(TFClassId.Soldier);

        Assert.True(rig.Source.Present);
        Assert.Equal("Alyx", rig.Source.Name);
        Assert.Equal(2, rig.Source.Team);
        Assert.Equal(LineKind.None, rig.Source.Line.Kind);   // the enemy sees a plain soldier
    }

    [Fact]
    public void A_disguised_enemys_panel_borrows_the_targets_name_and_fake_health()
    {
        // bDisguisedEnemy (tf_hud_target_id.cpp:785-794, :822-827): the name
        // is the disguise target's, the health m_iDisguiseHealth over the
        // disguise class's max — the spy's own rows never show.
        var rig = new Rig();
        rig.ArriveResources(targetTeam: 3, maxHealth: 125);
        var target = rig.TargetCarrier(TFClassId.Spy, team: 3, health: 30);
        target.DisguiseClass = (int)TFClassId.HeavyWeapons;
        target.DisguiseTeam = 2;
        target.DisguiseTargetIndex = 3;
        target.DisguiseHealth = 250;
        ClientViews.SetConditions(target, CondMask.Of(TFCondId.Disguised));
        rig.LocalCarrier(TFClassId.Soldier);

        Assert.True(rig.Source.Present);
        Assert.Equal("Barney", rig.Source.Name);
        Assert.Equal(250, rig.Source.Health);
        Assert.Equal(rig.Classes[TFClassId.HeavyWeapons].MaxHealth, rig.Source.MaxHealth);
    }

    [Fact]
    public void A_friendly_spy_wearing_the_enemy_is_revealed_to_his_own_team()
    {
        // GetTargetIDDataString :9768-9779 — the real teams are the same, so
        // the reveal line names the class and the alignment word by the
        // disguise's team.
        var rig = new Rig();
        rig.ArriveResources();
        var target = rig.TargetCarrier(TFClassId.Spy, team: 2);
        target.DisguiseClass = (int)TFClassId.Soldier;
        target.DisguiseTeam = 3;
        ClientViews.SetConditions(target, CondMask.Of(TFCondId.Disguised));
        rig.LocalCarrier(TFClassId.Scout);

        Assert.True(rig.Source.Present);
        Assert.Equal(2, rig.Source.Team);
        Assert.Equal((LineKind.Disguise, "Soldier", true), (rig.Source.Line.Kind, rig.Source.Line.ClassName, rig.Source.Line.Enemy));
    }

    [Fact]
    public void A_real_medic_s_charge_line_replaces_the_disguise_line()
    {
        // GetTargetIDDataString — the medic arm runs after the disguise
        // lines and overwrites them (c_tf_player.cpp:9794-9804).
        var rig = new Rig();
        rig.ArriveResources();
        var target = rig.TargetCarrier(TFClassId.Medic, team: 2);
        target.DisguiseClass = (int)TFClassId.Soldier;
        target.DisguiseTeam = 3;
        rig.Gun(Target, 0.42f);   // the carried medigun's own state
        ClientViews.SetConditions(target, CondMask.Of(TFCondId.Disguised));
        rig.LocalCarrier(TFClassId.Scout);

        Assert.True(rig.Source.Present);
        Assert.Equal((LineKind.MedicCharge, "", 42), (rig.Source.Line.Kind, rig.Source.Line.ClassName, rig.Source.Line.Charge));
    }

    [Fact]
    public void A_disguised_enemy_medic_shows_a_fake_zero_charge()
    {
        // The :9801-9805 arm — an enemy "medic" the spy's own charge never
        // feeds: the panel reads zero, never the spy's real level.
        var rig = new Rig();
        rig.ArriveResources(targetTeam: 3);
        var target = rig.TargetCarrier(TFClassId.Spy, team: 3);
        target.DisguiseClass = (int)TFClassId.Medic;
        target.DisguiseTeam = 2;
        rig.Gun(Target, 0.9f);   // the spy's own — must not leak
        ClientViews.SetConditions(target, CondMask.Of(TFCondId.Disguised));
        rig.LocalCarrier(TFClassId.Soldier);

        Assert.True(rig.Source.Present);
        Assert.Equal((LineKind.MedicCharge, 0), (rig.Source.Line.Kind, rig.Source.Line.Charge));
    }

    [Fact]
    public void A_cloaked_target_shows_nothing_even_to_his_own_team()
    {
        // IsValidIDTarget :414-419 — a stealthed target is nobody's.
        var rig = new Rig();
        rig.ArriveResources();
        var target = rig.TargetCarrier(TFClassId.Spy, team: 2);
        ClientViews.SetConditions(target, CondMask.Of(TFCondId.Stealthed));
        rig.LocalCarrier(TFClassId.Scout);

        Assert.False(rig.Source.Present);
    }

    [Fact]
    public void A_taunting_local_shows_nothing()
    {
        // CMainTargetID::ShouldDraw's taunt arm (:525-529).
        var rig = new Rig();
        rig.ArriveResources();
        rig.TargetCarrier(TFClassId.Soldier);
        var local = rig.LocalCarrier(TFClassId.Scout);
        ClientViews.SetConditions(local, CondMask.Of(TFCondId.Taunting));

        Assert.False(rig.Source.Present);
    }

    [Fact]
    public void An_observer_local_shows_nothing()
    {
        // CMainTargetID::ShouldDraw :1201-1210 — the panel belongs to a
        // playing player; the spectator bar has its own.
        var rig = new Rig();
        rig.ArriveResources();
        rig.TargetCarrier(TFClassId.Soldier);
        rig.LocalCarrier(TFClassId.Scout);
        var self = new SsPlayer(rig.Engine);
        self.State.BindLocal();
        self.State.ObserverMode = 1;
        rig.Stands.Place(Local, self);

        Assert.False(rig.Source.Present);
    }

    [Fact]
    public void A_wall_between_the_players_hides_the_panel()
    {
        // The clip cut at the wall's end answers a miss (UpdateIDTarget's
        // second pass, c_tf_player.cpp:7089-7110).
        var rig = new Rig();
        rig.ArriveResources();
        rig.TargetCarrier(TFClassId.Soldier);
        rig.LocalCarrier(TFClassId.Scout);
        rig.WallBetween();

        Assert.False(rig.Source.Present);
    }

    [Fact]
    public void A_target_with_no_carrier_shows_nothing()
    {
        var rig = new Rig();
        rig.ArriveResources();
        rig.LocalCarrier(TFClassId.Scout);

        Assert.False(rig.Source.Present);
    }

    [Fact]
    public void Without_a_resource_row_the_name_shows_but_the_cross_hides()
    {
        // :835-838 — no g_TF_PR max health, no bShowHealth; the name's gate
        // is independent of it.
        var rig = new Rig();
        rig.TargetCarrier(TFClassId.Soldier, team: 3);
        rig.LocalCarrier(TFClassId.Spy);

        Assert.True(rig.Source.Present);
        Assert.Equal("Alyx", rig.Source.Name);
        Assert.False(rig.Source.ShowsHealth);
        Assert.Equal(80, rig.Source.Health);
        Assert.Equal(0, rig.Source.MaxHealth);
    }
}
