extern alias client;

using System.Collections.Generic;

using Xunit;

using AnimWeapons = client::TF2.Client.Player.TFClientAnimWeapons;
using Carriers = client::TF2.Client.Player.TFClientCarriers;
using Catalog = client::SourceSharp.Shared.WeaponCatalog;
using ClassTable = client::TF2.Shared.TFClassTable;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientSsWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using Cond = client::TF2.Shared.TFCond;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using EntityBase = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using IStandalones = client::SourceSharp.Client.IClientStandalones;
using PlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using Resource = client::TF2.Client.Player.ClientTFPlayerResource;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using Schema = client::TF2.Shared.Econ.ItemSchema;
using Scripts = client::TF2.Shared.TFWeaponScripts;
using TFClass = client::TF2.Shared.TFClass;
using TFTeam = client::TF2.Shared.TFTeam;
using TFWeaponRole = client::TF2.Shared.Anim.TFWeaponRole;
using Views = client::TF2.Shared.Player.TFPlayerNetStateViews;
using WeaponTypes = client::TF2.Shared.TFWeaponTypes;

namespace TF2.Tests.Client;

/// <summary>
/// The body's stance on a client: the translation reads the weapon in the hand
/// of whoever it animates, every player and not only the viewer
/// (tf_weaponbase.cpp:4185-4222), and a disguised spy's for an enemy is the
/// disguise's.
/// </summary>
public sealed class TFClientAnimWeaponsTests
{
    private const int Local = 1, Other = 2;

    private sealed class Rig
    {
        public readonly AnimWeapons Weapons;
        public readonly PlayerData OtherCarrier;
        public readonly PlayerResources Resources = new();
        public readonly List<EntityBase> Live = [];

        public Rig(TFClass otherClass = TFClass.Soldier, string? carried = "tf_weapon_rocketlauncher")
        {
            var log = new ClientLog();
            var files = new TFWeaponSelectUiTests.FilesShim(TFWeaponSelectUiTests.FixtureBytes());
            var classes = new ClassTable(log, files);
            var scripts = new Scripts(log, files);
            var schema = new Schema(files, log);
            var types = new WeaponTypes(scripts, classes, schema);
            var catalog = new Catalog(() => [new WeaponTypes(scripts, classes, schema)], log);

            var hud = new Engine();
            hud.Returns("Hud.LocalPlayerIndex", Local);

            var standalones = new Standalones(this);
            var carriers = new Carriers();
            OtherCarrier = new PlayerData(new Engine(), carriers, standalones, Resources, new Engine(), classes,
                                          new client::TF2.Client.Player.TFClientConditionDriver(new NoHooks()), log);
            OtherCarrier.State.BindLocal();
            OtherCarrier.State.OwnerIndex = Other;
            OtherCarrier.OnSpawn();
            Views.SetClass(OtherCarrier.State, otherClass);

            if (carried is not null)
            {
                var gun = new ClientSsWeapon(new Engine());
                gun.State.BindLocal();
                gun.State.OwnerIndex = Other;
                gun.State.WeaponTypeId = catalog.IndexOf(carried);
                Live.Add(gun);
            }

            Weapons = new AnimWeapons(hud, new NoArsenal(), types, scripts, standalones, catalog, schema,
                                      classes, carriers, Resources);
        }

        /// <summary>The scoreboard's teams, the enemy read's two rows.</summary>
        public void Teams(TFTeam local, TFTeam other)
        {
            var resource = new Resource(new Engine(), Resources);
            resource.State.BindLocal();
            resource.State.Team[Local] = (byte)local;
            resource.State.Team[Other] = (byte)other;
            Resources.Live = resource;
        }

        public void Disguise(TFClass cls)
        {
            Views.SetConditions(OtherCarrier.State, CondMask.Of([Cond.Disguised]));
            OtherCarrier.State.DisguiseClass = (int)cls;
        }
    }

    private sealed class Standalones(Rig rig) : IStandalones
    {
        public IReadOnlyList<EntityBase> Live() => rig.Live;

        public EntityBase? GetByIndex(int entityIndex) => null;

        public IEnumerable<EntityBase> OfClass(string? className) => rig.Live;
    }

    private sealed class NoHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, System.Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }

    private sealed class NoArsenal : client::SourceSharp.Client.ILocalArsenal
    {
        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = "";
            clip = reserve = -1;
            return false;
        }
    }

    [Fact]
    public void Another_players_stance_is_the_gun_in_their_hands()
    {
        var weapon = new Rig().Weapons.ActiveWeapon(Other);

        Assert.NotNull(weapon);
        Assert.Equal(TFWeaponRole.Primary, weapon.Value.Role);
    }

    [Fact]
    public void A_player_with_no_gun_entity_has_no_stance_weapon()
        => Assert.Null(new Rig(carried: null).Weapons.ActiveWeapon(Other));

    [Fact]
    public void A_disguised_spy_stands_as_the_disguises_primary_to_an_enemy()
    {
        var rig = new Rig(TFClass.Spy, "tf_weapon_revolver");
        rig.Teams(TFTeam.Blue, TFTeam.Red);
        rig.Disguise(TFClass.Soldier);

        var weapon = rig.Weapons.DisguiseWeapon(Other);

        Assert.NotNull(weapon);
        Assert.Equal(TFWeaponRole.Primary, weapon.Value.Role);
    }

    [Fact]
    public void A_teammate_sees_no_disguise_stance()
    {
        var rig = new Rig(TFClass.Spy, "tf_weapon_revolver");
        rig.Teams(TFTeam.Red, TFTeam.Red);
        rig.Disguise(TFClass.Soldier);

        Assert.Null(rig.Weapons.DisguiseWeapon(Other));
    }

    [Fact]
    public void The_viewer_never_swaps_their_own_stance()
    {
        var rig = new Rig(TFClass.Spy, "tf_weapon_revolver");
        rig.Teams(TFTeam.Blue, TFTeam.Red);

        Assert.Null(rig.Weapons.DisguiseWeapon(Local));
    }
}
