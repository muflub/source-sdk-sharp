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
/// PHASE 0.4: THE CLIENT PREDICTS A MOD WEAPON WITH THE BEHAVIOUR THE SERVER RAN.
///
/// <para>
/// A mod's shared source compiles into both of its side assemblies, the way
/// the core's does, and each copy registers its weapon rows and behaviours in
/// its side's container. Here one mod text is compiled against each side (two
/// assemblies of the same name), both copies go into ONE container, each side's
/// tables are built from it the way the modules build them, and the server's
/// <see cref="Arsenal"/> and the client's <c>ClientArsenal</c> each run the same
/// trigger pull. The behaviour doubles the fire interval, so the stock answer
/// (one interval) and the mod's (two) are told apart by the timer both write.
/// </para>
/// </summary>
public class WeaponBehaviourParityTests
{
    private const string Gun = "wep_parity_gun";
    private const float Now = 10f;
    private const float RowInterval = 0.2f;
    private const int LocalPlayer = 1;
    private static readonly EntityHandle Player = new(0x0001_0000u | LocalPlayer);

    private const string ModSource = """
        using System.Collections.Generic;
        using SourceSharp.Shared;

        namespace WepParity;

        public sealed class DoublesTheInterval : IWeaponBehaviour
        {
            public float ModifyFireInterval(in WeaponType type, float interval) => interval * 2f;
        }

        public sealed class Mod : IWeaponTypeSource, IWeaponBehaviourSource
        {
            public IEnumerable<WeaponType> Rows() => new[]
            {
                new WeaponType("wep_parity_gun", Interval: 0.2f)
                {
                    ClipSize = 18, AmmoName = "Pistol", DeployTime = 0f, Behaviour = "wep.parity",
                },
            };

            public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
                => new[] { KeyValuePair.Create<string, IWeaponBehaviour>("wep.parity", new DoublesTheInterval()) };
        }
        """;

    private sealed class Standalones : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);
        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Directory.OfClass(className);
        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Directory.OfName(name);
    }

    /// <summary>
    /// One container holding the server's and the client's copy of the mod and
    /// both sides' tables, registered as ServerModule and ClientModule do.
    /// </summary>
    private static ServiceContainer Composition()
    {
        var log = new RecordingLog();
        var container = new ServiceContainer();

        var serverMod = EmittedWeaponSources.Instantiate<IWeaponTypeSource>(
            "WepParity", "WepParity.Mod", ModSource, typeof(WeaponType).Assembly);
        var clientMod = EmittedWeaponSources.Instantiate<C.IWeaponTypeSource>(
            "WepParity", "WepParity.Mod", ModSource, typeof(C.WeaponType).Assembly);

        container.AddInstance(serverMod.GetType(), serverMod);
        container.AddInstance(clientMod.GetType(), clientMod);

        container.AddInstance(new WeaponCatalog(() => container.ResolveAll<IWeaponTypeSource>(), log));
        container.AddInstance(new WeaponBehaviours(() => container.ResolveAll<IWeaponBehaviourSource>(), log));
        container.AddInstance(new C.WeaponCatalog(() => container.ResolveAll<C.IWeaponTypeSource>(), log));
        container.AddInstance(new C.WeaponBehaviours(() => container.ResolveAll<C.IWeaponBehaviourSource>(), log));
        return container;
    }

    /// <summary>
    /// The server's run: the arsenal gives the gun and runs one trigger pull.
    /// Not first-time, so the arms (traces, sounds) stay out of it: the timer
    /// is state, and effects never write state.
    /// </summary>
    private static WeaponNetState ServerShot(ServiceContainer container)
    {
        // Entries 0x500 and 0x501, clear of the player's entry 1.
        var weapon = new EntityHandle(0x0500u);
        var viewModel = new EntityHandle(0x0501u);
        var created = new Queue<EntityHandle>([weapon, viewModel]);

        var engine = TestServices.Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", LocalPlayer)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", Now).Ignores("EntityRender.LookupSequenceByActivity")
            .ReturnsEachCall("Entities.Create", () => created.Dequeue());

        // The carried pair, spawned where the arsenal's Create will name them.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");
        registry.Spawn(weapon, EntityNames.SsWeapon);
        registry.Spawn(viewModel, EntityNames.SsViewModel);

        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(),
                                  weapons: container.Require<WeaponCatalog>(),
                                  behaviours: container.Require<WeaponBehaviours>());

        Assert.True(arsenal.GiveWeapon(Player, Gun), "the server gives the mod's gun");
        Assert.Equal(weapon, arsenal.CarriedPair(Player).Weapon);

        var cmd = new UserCmd { CommandNumber = 1, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: false);

        return Assert.IsType<SsWeapon>(registry.Get(weapon)).State;
    }

    /// <summary>The client's prediction of the same pull, over the networked index.</summary>
    private static C.WeaponNetState ClientShot(ServiceContainer container, int networkedIndex)
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", Now);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = networkedIndex;
        weapon.State.Clip = 18;

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0042u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;

        var standalones = new Standalones();
        standalones.Directory.Bind(weapon.Handle, -1, weapon);
        standalones.Directory.Bind(viewModel.Handle, -1, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), standalones, engine, engine, engine,
                                        client::SourceSharp.Abi.NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        weapons: container.Require<C.WeaponCatalog>(),
                                        behaviours: container.Require<C.WeaponBehaviours>());

        var basis = new UserCmd { CommandNumber = 0 };
        arsenal.OnPlayerCommand(Player, in basis, 2, false);

        var cmd = new UserCmd { CommandNumber = 1, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, 1, false, Now);

        var state = arsenal.WorkingWeapon;
        Assert.True(state.WeaponTypeId == networkedIndex, "the client predicted the networked weapon");
        return state;
    }

    [Fact]
    public void BothArsenalsRunTheModsBehaviourForTheModsWeapon()
    {
        var container = Composition();

        var server = ServerShot(container);
        var client = ClientShot(container, server.WeaponTypeId);

        // One table on both sides: the mod row is the first after the core's on each.
        Assert.Equal(WeaponTypes.All.Length, server.WeaponTypeId);
        Assert.Equal(server.WeaponTypeId, container.Require<C.WeaponCatalog>().IndexOf(Gun));

        // Both spent the round and both charged TWO intervals: the behaviour,
        // not stock's one (10.2).
        Assert.Equal((17, Now + 2 * RowInterval), (server.Clip, server.NextPrimaryAttack));
        Assert.Equal((server.Clip, server.NextPrimaryAttack), (client.Clip, client.NextPrimaryAttack));
    }
}
