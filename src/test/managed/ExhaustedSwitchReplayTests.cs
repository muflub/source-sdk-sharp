extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;

using C = client::SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE REPLAY ANSWERS THE EXHAUSTED HANDS' SWITCH. The server's
/// ReloadOrSwitchWeapons tail puts an empty weapon down for the next best
/// (<see cref="ExhaustedSwitchTests"/>); the client's replay runs the same
/// machine over the same command and must walk too — stock predicts the
/// switch, the shared ItemPostFrame chain on the client
/// (baseplayer_shared.cpp:228) writing m_hActiveWeapon
/// (c_basecombatcharacter.cpp:194). One empty-gun wind-down, both sides, the
/// same pick and the same bytes.
/// </summary>
public class ExhaustedSwitchReplayTests
{
    private const int LocalPlayer = 1;
    private const float Now = 30f;

    private static readonly EntityHandle Player = new(0x0001_0000u | LocalPlayer);

    private static string Script(int weight, bool autoTo = true, bool autoFrom = true)
        => "WeaponData\n{\n\"weight\" \"" + weight + "\"\n\"autoswitchto\" \"" + (autoTo ? 1 : 0)
         + "\"\n\"autoswitchfrom\" \"" + (autoFrom ? 1 : 0) + "\"\n}\n";

    private static Dictionary<string, string> Scripts() => new()
    {
        ["scripts/weapon_crowbar.txt"] = Script(0),
        ["scripts/weapon_pistol.txt"] = Script(2),
        ["scripts/weapon_smg1.txt"] = Script(3),
    };

    /// <summary>The base rows with one gate's variant on top: weights in the scripts, as the pick reads them.</summary>
    private static Dictionary<string, string> Gated(int? crowbarWeight = null, int? pistolWeight = null,
                                                    bool? crowbarTo = null, bool? smgFrom = null) => new()
    {
        ["scripts/weapon_crowbar.txt"] = Script(crowbarWeight ?? 0, autoTo: crowbarTo ?? true),
        ["scripts/weapon_pistol.txt"] = Script(pistolWeight ?? 2),
        ["scripts/weapon_smg1.txt"] = Script(3, autoFrom: smgFrom ?? true),
    };

    /// <summary>The server's run: three carried, the SMG1 in the hands emptied, one idle command.</summary>
    private static WeaponNetState ServerWindDown(IReadOnlyDictionary<string, string>? scriptOverrides = null,
                                                SourceSharp.Shared.WeaponBehaviours? behaviours = null)
    {
        var scripts = new SourceSharp.Shared.WeaponScriptDatabase(new RecordingLog(),
                                                                 p => (scriptOverrides ?? Scripts()).GetValueOrDefault(p));

        // Entries 0x500 and 0x501, the pair EnsureEntities creates once and
        // every carried weapon then rides.
        var created = new Queue<EntityHandle>([new(0x0500u), new(0x0501u)]);
        var engine = TestServices.Mock()
            .Returns("Entities.IsValid", true)
            .Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f)
            .ReturnsEachCall("Entities.Create", () => created.Dequeue())
            .Returns("EntityRender.LookupSequenceByActivity", 0);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        // The pair, spawned where Create will name it: the registry holds
        // them, the way a live entity does.
        registry.Spawn(new EntityHandle(0x0500u), EntityNames.SsWeapon);
        registry.Spawn(new EntityHandle(0x0501u), EntityNames.PredictedViewModel);

        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine,
                                  engine, engine, engine, registry, engine, new DamageHitGroups(),
                                  behaviours: behaviours, scripts: scripts);
        foreach (string w in new[] { EntityNames.WeaponCrowbar, EntityNames.WeaponPistol, EntityNames.WeaponSmg1 })
            Assert.True(arsenal.GiveWeapon(Player, w), $"the server gave {w}");

        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, EntityNames.WeaponSmg1));
        var carry = arsenal.CarryFor(Player);
        carry.Ammo["SMG1"] = 0;
        carry.Ammo["SMG1_Grenade"] = 0;
        ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State.Clip = 0;

        engine.Returns("Clock.Now", Now);
        var cmd = new UserCmd();
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        return ((SsWeapon)registry.Get(arsenal.CarriedPair(Player).Weapon)!).State;
    }

    /// <summary>The replay of the same wind-down, over the networked pair.</summary>
    private static (C.WeaponNetState Weapon, C.PlayerNetState Player) ReplayedWindDown(
        client::SourceSharp.Shared.WeaponScriptDatabase scripts, int held, UInt128 carried,
        client::SourceSharp.Shared.WeaponBehaviours? behaviours = null)
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", Now);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);   // the draw resolves

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0500u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = held;
        weapon.State.Clip = 0;
        weapon.State.Reserve = 0;
        weapon.State.SecondaryReserve = 0;
        weapon.State.LastDeployTime = 10f;   // the 10f switch to it, as the server's entity carries

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0501u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var standalones = new Pair();
        standalones.Directory.Bind(weapon.Handle, -1, weapon);
        standalones.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), standalones, engine, engine, engine,
                                        client::SourceSharp.Abi.NetStateCatalog.Of(TestServices.ClientGame,
                                            new RecordingLog()),
                                        behaviours: behaviours, scripts: scripts);

        // The carried three, as the server's mask names them; the held word
        // the spawn's deploy wrote.
        var working = new C.PlayerNetState();
        working.BindLocal();
        working.HeldWeapon = held + 1;
        working.CarriedMask = carried;

        var player = new EntityHandle(0x0001_0000u | LocalPlayer);
        arsenal.WorkingPlayer = working;

        var basis = new UserCmd { CommandNumber = 30 };
        arsenal.OnPlayerCommand(player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 31 };
        arsenal.OnPlayerCommand(player, in cmd, 1, true, Now);

        return (arsenal.WorkingWeapon, working);
    }

    private sealed class Pair : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Directory.OfName(name);
    }

    [Fact]
    public void TheReplayTakesTheServerNextBest()
    {
        var server = ServerWindDown();

        var catalog = C.WeaponCatalog.Stock();
        int crowbar = catalog.IndexOf(C.EntityNames.WeaponCrowbar);
        int pistol = catalog.IndexOf(C.EntityNames.WeaponPistol);
        int smg1 = catalog.IndexOf(C.EntityNames.WeaponSmg1);

        Assert.Equal(pistol, server.WeaponTypeId);

        var scripts = new client::SourceSharp.Shared.WeaponScriptDatabase(new RecordingLog(),
                                                                          p => Scripts().GetValueOrDefault(p));
        var (weapon, player) = ReplayedWindDown(scripts, smg1, (UInt128)1 << crowbar
                                                               | (UInt128)1 << pistol
                                                               | (UInt128)1 << smg1);

        Assert.Equal(server.WeaponTypeId, weapon.WeaponTypeId);
        Assert.Equal(server.NextPrimaryAttack, weapon.NextPrimaryAttack, 4);
        Assert.Equal(server.NextSecondaryAttack, weapon.NextSecondaryAttack, 4);
        Assert.Equal(server.TimeWeaponIdle, weapon.TimeWeaponIdle, 4);
        Assert.Equal(server.WeaponTypeId + 1, player.HeldWeapon);
    }

    // =====================================================================
    // The gates, both sides: the replay's pick under the gated scripts is
    // the server's, gate for gate.
    // =====================================================================

    /// <summary>A row that never comes back into the hands, on either side's contract.</summary>
    private sealed class DryBehaviour : SourceSharp.Shared.IWeaponBehaviour, C.IWeaponBehaviour
    {
        public bool CanDeploy(in SourceSharp.Shared.WeaponAttackContext attack) => false;

        public bool CanDeploy(in C.WeaponAttackContext attack) => false;
    }

    private sealed class DrySource : SourceSharp.Shared.IWeaponBehaviourSource, C.IWeaponBehaviourSource
    {
        public const string Key = "hl2mp_pistol";

        IEnumerable<KeyValuePair<string, SourceSharp.Shared.IWeaponBehaviour>> SourceSharp.Shared.IWeaponBehaviourSource.Behaviours()
        {
            yield return KeyValuePair.Create<string, SourceSharp.Shared.IWeaponBehaviour>(Key, new DryBehaviour());
        }

        IEnumerable<KeyValuePair<string, C.IWeaponBehaviour>> C.IWeaponBehaviourSource.Behaviours()
        {
            yield return KeyValuePair.Create<string, C.IWeaponBehaviour>(Key, new DryBehaviour());
        }
    }

    /// <summary>The carried three's indexes and mask, as both sides' catalog names them.</summary>
    private static (int Crowbar, int Pistol, int Smg1, UInt128 Mask) Hands()
    {
        var catalog = C.WeaponCatalog.Stock();
        int crowbar = catalog.IndexOf(C.EntityNames.WeaponCrowbar);
        int pistol = catalog.IndexOf(C.EntityNames.WeaponPistol);
        int smg1 = catalog.IndexOf(C.EntityNames.WeaponSmg1);
        return (crowbar, pistol, smg1, (UInt128)1 << crowbar | (UInt128)1 << pistol | (UInt128)1 << smg1);
    }

    private static client::SourceSharp.Shared.WeaponScriptDatabase ClientScripts(Dictionary<string, string> scripts)
        => new(new RecordingLog(), p => scripts.GetValueOrDefault(p));

    [Fact]
    public void TheReplayKeepsAWeaponTheScriptBarsTheSwitchFrom()
    {
        var (crowbar, pistol, smg1, mask) = Hands();
        var gated = Gated(smgFrom: false);

        var server = ServerWindDown(gated);
        var (weapon, _) = ReplayedWindDown(ClientScripts(gated), smg1, mask);

        Assert.Equal(smg1, server.WeaponTypeId);
        Assert.Equal(server.WeaponTypeId, weapon.WeaponTypeId);
    }

    [Fact]
    public void TheReplaySkipsACandidateTheScriptBarsTheSwitchTo()
    {
        var (crowbar, pistol, smg1, mask) = Hands();
        var gated = Gated(crowbarWeight: 7, crowbarTo: false);

        var server = ServerWindDown(gated);
        var (weapon, _) = ReplayedWindDown(ClientScripts(gated), smg1, mask);

        Assert.Equal(pistol, server.WeaponTypeId);
        Assert.Equal(server.WeaponTypeId, weapon.WeaponTypeId);
    }

    [Fact]
    public void TheReplayPassesOverASameWeightCandidateThatCannotDeploy()
    {
        var (crowbar, pistol, smg1, mask) = Hands();
        var gated = Gated(pistolWeight: 3);

        var server = ServerWindDown(gated, new SourceSharp.Shared.WeaponBehaviours(
            () => new SourceSharp.Shared.IWeaponBehaviourSource[] { new DrySource() }));
        var (weapon, _) = ReplayedWindDown(ClientScripts(gated), smg1, mask,
            new client::SourceSharp.Shared.WeaponBehaviours(
                () => new client::SourceSharp.Shared.IWeaponBehaviourSource[] { new DrySource() }));

        Assert.Equal(crowbar, server.WeaponTypeId);
        Assert.Equal(server.WeaponTypeId, weapon.WeaponTypeId);
    }
}
