extern alias client;

using TF2.Tests;
using TF2.Tests.Weapons;

using Xunit;

using Adapter = client::TF2.Client.Ui.TFHudScope;
using Arsenal = client::SourceSharp.Client.ILocalArsenal;
using ClientFiles = client::SourceSharp.Abi.IEngineFiles;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientScripts = client::TF2.Shared.TFWeaponScripts;
using ClientSsWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using Clock = client::SourceSharp.Abi.IGameClock;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using Cond = client::TF2.Shared.TFCond;
using Carrier = client::TF2.Shared.Player.TFPlayerNetState;
using DrawStates = client::SourceSharp.Client.WeaponEffects.IWeaponDrawStates;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Standalones = client::SourceSharp.Client.IClientStandalones;

namespace TF2.Tests.Client;

/// <summary>
/// The scope's reads (tf_hud_scope.cpp): the zoom is the local carrier's
/// <c>TF_COND_ZOOMED</c> word behind the HIDEHUD_PLAYERDEAD alive arm
/// (<c>CHudScope::ShouldDraw</c>, :373-390) and the charge meter's source is
/// the carried rifle (<c>CHudScopeCharge::Paint</c>'s assert_cast, :162-226),
/// whose charge derives from the carried state's charge-begin clock over
/// <c>TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX</c> 150 — the client's stand-in for
/// the material proxy's <c>m_flChargedDamage</c>.
/// </summary>
public sealed class TFHudScopeTests
{
    private sealed class RigArsenal : Arsenal
    {
        public string? Weapon;

        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = Weapon ?? "";
            clip = -1;
            reserve = 0;
            return Weapon is not null;
        }
    }

    /// <summary>The shipped pack, served as the client's files (the prediction rig's Served).</summary>
    private sealed class Served(TFClassTableTests.Files files) : ClientFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    private sealed class Carriers : client::TF2.Shared.Player.ITFPlayerData
    {
        public readonly Dictionary<int, Carrier> States = new();

        public Carrier? Predicted(int playerIndex) => States.TryGetValue(playerIndex, out var s) ? s : null;
        public Carrier? Networked(int playerIndex) => States.TryGetValue(playerIndex, out var s) ? s : null;
    }

    private sealed class GunStands : Standalones
    {
        private readonly Dictionary<int, ClientSsWeapon> _guns = [];

        public ClientSsWeapon Gun(int ownerIndex, float meter)
        {
            var gun = new ClientSsWeapon(new Engine());
            gun.State.BindLocal();
            gun.State.OwnerIndex = ownerIndex;
            gun.State.BehaviourMeter = meter;
            _guns[ownerIndex] = gun;
            return gun;
        }

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live()
            => _guns.Values.Cast<client::SourceSharp.Client.Standalone.ClientStandaloneEntity>().ToList();

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
        public IEnumerable<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> OfClass(string? className) => [];
        public IEnumerable<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> OfName(string? name) => [];
    }

    private sealed class DrawStatesDouble : DrawStates
    {
        public float? Working;

        public client::SourceSharp.Shared.WeaponNetState? PredictedStateOf(ClientSsWeapon weapon)
        {
            if (Working is not { } meter)
                return null;
            var copy = new client::SourceSharp.Shared.WeaponNetState();
            copy.BindLocal();
            copy.BehaviourMeter = meter;
            return copy;
        }
    }

    private sealed class HudClock : Clock
    {
        public float Now { get; set; }
    }

    private static Adapter Scope(Engine engine, RigArsenal? arsenal = null, Carriers? carriers = null,
                                 GunStands? stands = null, DrawStatesDouble? states = null, HudClock? clock = null) =>
        new(engine, carriers ?? new Carriers(), arsenal ?? new RigArsenal(),
            new ClientScripts(new ClientLog(), new Served(WeaponFixtures.ShippedWithClasses())),
            stands ?? new GunStands(), states ?? new DrawStatesDouble(), clock ?? new HudClock());

    [Fact]
    public void The_scope_draws_on_the_local_zoom_condition()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 125);
        var carriers = new Carriers();
        var scope = Scope(engine, carriers: carriers);

        // No carrier decoded yet: stock's ShouldDraw refuses (tf_hud_scope.cpp:381).
        Assert.False(scope.Zoomed);

        // The cross arrived unzoomed: still nothing.
        var carrier = new Carrier();
        carrier.BindLocal();
        carriers.States[2] = carrier;
        Assert.False(scope.Zoomed);

        // Zoomed in (the rifle's ZoomIn arms the condition, tf_weapon_sniperrifle.cpp:523-545):
        // m_nPlayerCond's bit, tf_cond's index 1.
        carrier.PlayerCond = 1 << (int)Cond.Zoomed;
        Assert.True(scope.Zoomed);

        // Dead while the word stands: the HIDEHUD_PLAYERDEAD bit hides it (tf_hud_scope.cpp:265).
        engine.Returns("Hud.LocalPlayerHealth", 0);
        Assert.False(scope.Zoomed);
    }

    [Fact]
    public void The_charge_meter_is_the_hands_rifle_and_reads_the_carried_state()
    {
        var engine = new Engine();
        engine.Returns("Hud.LocalPlayerIndex", 2);
        engine.Returns("Hud.LocalPlayerHealth", 125);
        var stands = new GunStands();
        var states = new DrawStatesDouble();
        var clock = new HudClock { Now = 41f };
        var scope = Scope(engine, arsenal: new RigArsenal { Weapon = "tf_weapon_sniperrifle" },
                          stands: stands, states: states, clock: clock);

        // Nothing carried: stock's assert_cast arm refuses (tf_hud_scope.cpp:178-180).
        Assert.False(scope.MeterPresent);
        Assert.Equal(0f, scope.Charge, 5);

        // The rifle carried, the charge begun at the clock's 40: 50/s of the
        // 150 max for the second since (TFSniperRifleBehaviour.Charge).
        stands.Gun(2, 40f);
        Assert.True(scope.MeterPresent);
        Assert.Equal(50f / 150f, scope.Charge, 4);

        // The charge not begun is no charge (the zero-clock arm).
        stands.Gun(2, 0f);
        Assert.Equal(0f, scope.Charge, 5);

        // The replay's working copy, while it is fresh, is what the meter reads.
        states.Working = 40.5f;
        Assert.Equal(25f / 150f, scope.Charge, 4);
    }
}
