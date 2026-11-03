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
            // The command each point was handed, for the seed facts.
            public WeaponCommand Seen;

            // The owner each clip and interval point was asked about, for the owner fact.
            public int ClipOwner = -1, IntervalOwner = -1;

            public float ModifyFireInterval(in WeaponType type, float interval, int ownerIndex)
            { IntervalOwner = ownerIndex; return interval * 2f; }

            public int ModifyClipSize(in WeaponType type, int clipSize, int ownerIndex)
            { ClipOwner = ownerIndex; return clipSize; }

            public void PrimaryAttack(in WeaponAttackContext attack) => Seen = attack.Command;
        }

        public sealed class Mod : IWeaponTypeSource, IWeaponBehaviourSource
        {
            public readonly DoublesTheInterval Behaviour = new();

            public IEnumerable<WeaponType> Rows() => new[]
            {
                new WeaponType("wep_parity_gun", Interval: 0.2f)
                {
                    ClipSize = 18, AmmoName = "Pistol", DeployTime = 0f, Behaviour = "wep.parity",
                },
            };

            public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
                => new[] { KeyValuePair.Create<string, IWeaponBehaviour>("wep.parity", Behaviour) };
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
    private static WeaponNetState ServerShot(ServiceContainer container, int commandNumber = 1)
    {
        // Entries 0x500 and 0x501, clear of the player's entry 1.
        var weapon = new EntityHandle(0x0500u);
        var viewModel = new EntityHandle(0x0501u);
        var created = new Queue<EntityHandle>([weapon, viewModel]);

        var engine = TestServices.Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", LocalPlayer)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", Now).Ignores("EntityRender.LookupSequenceByActivity")
            .ReturnsEachCall("Entities.Create", () => created.Dequeue());

        // The carried pair, spawned where the arsenal's Create will name them.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.Spawn(weapon, EntityNames.SsWeapon);
        registry.Spawn(viewModel, EntityNames.PredictedViewModel);

        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(),
                                  weapons: container.Require<WeaponCatalog>(),
                                  behaviours: container.Require<WeaponBehaviours>());

        Assert.True(arsenal.GiveWeapon(Player, Gun), "the server gives the mod's gun");
        Assert.Equal(weapon, arsenal.CarriedPair(Player).Weapon);

        var cmd = new UserCmd { CommandNumber = commandNumber, Buttons = (int)InputButtons.Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: false);

        return Assert.IsType<SsWeapon>(registry.Get(weapon)).State;
    }

    /// <summary>The client's prediction of the same pull, over the networked index.</summary>
    private static C.WeaponNetState ClientShot(ServiceContainer container, int networkedIndex, int commandNumber = 1)
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", Now);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        // Entry 0x500: a networked entity holds one entity index on both sides,
        // and the server's weapon is entry 0x500.
        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0500u) };
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

        var cmd = new UserCmd { CommandNumber = commandNumber, Buttons = (int)InputButtons.Attack };
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

    /// <summary>The command the server's (or the client's) copy of the mod's behaviour last saw.</summary>
    private static (int Seed, int Weapon, int Owner) SeenBy(object mod)
    {
        object behaviour = mod.GetType().GetField("Behaviour")!.GetValue(mod)!;
        object seen = behaviour.GetType().GetField("Seen")!.GetValue(behaviour)!;
        var t = seen.GetType();
        return ((int)t.GetProperty("Seed")!.GetValue(seen)!,
                (int)t.GetProperty("WeaponIndex")!.GetValue(seen)!,
                (int)t.GetProperty("OwnerIndex")!.GetValue(seen)!);
    }

    /// <summary>
    /// TF's crit roll seeds with the command's seed and the weapon's and the
    /// player's entity indices (<c>CTFWeaponBase::CalcIsAttackCriticalHelper</c>,
    /// tf_weaponbase.cpp); the server's run and the client's replay of one
    /// command must hand a behaviour the same three, or every crit mispredicts.
    /// </summary>
    [Fact]
    public void BothArsenalsHandTheBehaviourOneSeedAndTheWeaponAndOwnerIndices()
    {
        var container = Composition();

        var server = ServerShot(container, commandNumber: 7);
        ClientShot(container, server.WeaponTypeId, commandNumber: 7);

        var seen = container.ResolveAll<IWeaponTypeSource>().Select(SeenBy).Single();
        var predicted = container.ResolveAll<C.IWeaponTypeSource>().Select(SeenBy).Single();

        // The stock seed of command 7 (ReadUsercmd's MD5_PseudoRandom & 0x7fffffff),
        // the weapon's entry and the player's: non-zero, so the stimulus reached.
        Assert.Equal((UserCmdCodec.RandomSeedFor(7), 0x500, LocalPlayer), seen);
        Assert.NotEqual(0, seen.Seed);
        Assert.Equal(seen, predicted);
    }

    /// <summary>The owner each of the mod's clip and interval points was last asked about.</summary>
    private static (int Clip, int Interval) OwnersAsked(object mod)
    {
        object behaviour = mod.GetType().GetField("Behaviour")!.GetValue(mod)!;
        return ((int)behaviour.GetType().GetField("ClipOwner")!.GetValue(behaviour)!,
                (int)behaviour.GetType().GetField("IntervalOwner")!.GetValue(behaviour)!);
    }

    /// <summary>
    /// A TF item scales the clip and the fire interval through the behaviour's
    /// two points (<c>CTFWeaponBase::GetMaxClip1</c>/<c>ApplyFireDelay</c>): a
    /// per-player question the server answers from the item it gave the player.
    /// If the client's prediction did not ask about the same owner it would
    /// scale from a different answer and mispredict the clip and the clocks.
    /// </summary>
    [Fact]
    public void BothArsenalsAskTheClipAndIntervalPointsAboutTheSameOwner()
    {
        var container = Composition();

        var server = ServerShot(container);
        ClientShot(container, server.WeaponTypeId);

        var asked = container.ResolveAll<IWeaponTypeSource>().Select(OwnersAsked).Single();
        var predicted = container.ResolveAll<C.IWeaponTypeSource>().Select(OwnersAsked).Single();

        // Both sides asked, and about the player holding the weapon — the same
        // index the command carries, non-zero, so the stimulus reached the point.
        Assert.Equal((LocalPlayer, LocalPlayer), asked);
        Assert.Equal(asked, predicted);
    }

    [Fact]
    public void AnotherCommandHandsTheBehaviourAnotherSeedOnBothSides()
    {
        var container = Composition();

        var server = ServerShot(container, commandNumber: 8);
        ClientShot(container, server.WeaponTypeId, commandNumber: 8);

        var seen = container.ResolveAll<IWeaponTypeSource>().Select(SeenBy).Single();
        var predicted = container.ResolveAll<C.IWeaponTypeSource>().Select(SeenBy).Single();

        Assert.NotEqual(UserCmdCodec.RandomSeedFor(7), seen.Seed);
        Assert.Equal(UserCmdCodec.RandomSeedFor(8), seen.Seed);
        Assert.Equal(seen, predicted);
    }
}
