using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// A carrier whose entity dies while its player stays in the game (the mode
/// switch's <c>sv_mode null</c> cleanup reached it at the round gate's mode
/// step, before TF2's preserve rows named it): the registry must never answer
/// through the dead binding. The regression: every movement read of that
/// player threw <see cref="StaleEntityException"/> and lost the command run,
/// because <see cref="ITFPlayerData.Predicted"/> kept handing back the
/// attached component's old <see cref="TFPlayerNetState"/>.
///
/// <para>
/// The rig runs the real <see cref="ScriptedEntityRegistry"/> so removal goes
/// the way the round's cleanup removes: mark, free, and the freed edict taken
/// by the next entity at a fresh serial. The rig's setup arm asserts that
/// removal took effect — the directory no longer answers for the carrier and
/// its slot answers for the squatter — because without it the facts could
/// pass on a carrier that never died. With no engine behind the test side the
/// binding is the state's local storage (the documented no-game arm of
/// <c>NetStateServerBinding.Bind</c>), so the pre-fix red line here is the
/// registry still ANSWERING the dead carrier — the class read hands back the
/// stale class instead of no carrier; on the live server the same stale
/// answer is what threw <see cref="StaleEntityException"/> on every movement
/// read (the round gate's mode step, and the fix's live proof).
/// </para>
/// </summary>
public sealed class TFPlayerCarrierStalenessTests
{
    private const int PlayerIndex = 3;
    private static readonly EntityHandle Player = new(0x0001_0000u | PlayerIndex);

    /// <summary>
    /// The engine's half, as <c>MapCleanupTests</c> runs it: fresh handles
    /// with fresh serials, edicts lowest-free, and <c>Spawn</c> through the
    /// real registry so the entity's net state binds to its slot.
    /// </summary>
    private sealed class Engine : IEngineEntities, IEdictSlots
    {
        private readonly IEngineEntities _inner;
        private readonly Dictionary<int, EntityHandle> _edicts = new();
        private readonly Dictionary<uint, string> _classOf = new();
        private uint _serial = 0x10;
        private int _nextEntry = 40;

        internal ScriptedEntityRegistry Registry = null!;

        internal Engine(RecordingEngine inner) => _inner = inner;

        public EntityHandle Create(string className)
        {
            int entry = LowestFree();
            var handle = new EntityHandle((uint)entry | (_serial++ << 16));
            _edicts[entry] = handle;
            _classOf[handle.Value] = className;
            return handle;
        }

        private int LowestFree()
        {
            while (_edicts.ContainsKey(_nextEntry))
                _nextEntry++;
            return _nextEntry;
        }

        public void Spawn(EntityHandle entity) => Registry.Spawn(entity, _classOf[entity.Value]);
        public void Remove(EntityHandle entity) => Registry.Removal.Remove(entity);
        public void CleanupDeleteList() => Registry.Directory.FreeRemoved();
        public int GetIndex(EntityHandle h) => h.EntryIndex;
        public bool InUse(int index) => _edicts.ContainsKey(index);
        public void AllowImmediateReuse() { }

        internal void Destroy(ReadOnlySpan<EntityHandle> batch)
        {
            foreach (var handle in batch)
                if (_edicts.TryGetValue(handle.EntryIndex, out var held) && held == handle)
                    _edicts.Remove(handle.EntryIndex);
        }

        public IInterpolationBlocks InterpolationBlocks => _inner.InterpolationBlocks;
        public IRenderPolicyBlocks RenderPolicyBlocks => _inner.RenderPolicyBlocks;
        public IClientReplayEngine ClientReplay => _inner.ClientReplay;
        public EntityHandle CreateFromMap(string className, byte[] packedKeys) => Create(className);
        public EntityHandle CreateFromMapAt(string className, byte[] packedKeys, int edictIndex) => Create(className);
        public int GetSpawnFlags(EntityHandle entity) => _inner.GetSpawnFlags(entity);
        public int Count() => _edicts.Count;
        public void PurgeMapRefs() => _inner.PurgeMapRefs();
        public void SetClassName(EntityHandle h, string className) => _inner.SetClassName(h, className);
        public void UseTriggerBounds(EntityHandle entity, bool enable, float bloat, bool uniform = false) => _inner.UseTriggerBounds(entity, enable, bloat, uniform);
        public int InvalidateMdlCacheAll() => _inner.InvalidateMdlCacheAll();
        public int UpdateVisibilityAll() => _inner.UpdateVisibilityAll();
        public void AddBaseVelocity(EntityHandle entity, Vector velocity) => _inner.AddBaseVelocity(entity, velocity);
        public void PositionChanged(EntityHandle entity, Vector previousOrigin) => _inner.PositionChanged(entity, previousOrigin);
        public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => _inner.ApplyVelocityImpulse(h, impulse, clearGround);
        public IEnumerable<EntityHandle> FindAllByClassname(string className) => _inner.FindAllByClassname(className);
        public List<EntityHandle> FindAllByName(string name, int limit = 512) => _inner.FindAllByName(name, limit);
        public EntityHandle FindByName(string name) => _inner.FindByName(name);
        public bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default) => _inner.FireInput(target, input, value, activator);
        public Vector GetAngles(EntityHandle h) => _inner.GetAngles(h);
        public Vector GetAngularVelocity(EntityHandle entity) => _inner.GetAngularVelocity(entity);
        public int GetArmor(EntityHandle entity) => _inner.GetArmor(entity);
        public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => _inner.GetBounds(entity);
        public Vector GetBoundsSize(EntityHandle entity) => _inner.GetBoundsSize(entity);
        public string GetClassName(EntityHandle h) => _inner.GetClassName(h);
        public Vector GetEyeAngles(EntityHandle h) => _inner.GetEyeAngles(h);
        public Vector GetEyePosition(EntityHandle h) => _inner.GetEyePosition(h);
        public int GetHealth(EntityHandle h) => _inner.GetHealth(h);
        public string GetName(EntityHandle entity) => _inner.GetName(entity);
        public Vector GetOrigin(EntityHandle h) => _inner.GetOrigin(h);
        public Vector GetSpawnAngles(EntityHandle entity) => _inner.GetSpawnAngles(entity);
        public Vector GetSpawnOrigin(EntityHandle entity) => _inner.GetSpawnOrigin(entity);
        public Vector GetVelocity(EntityHandle h) => _inner.GetVelocity(h);
        public Vector GetWorldSpaceCenter(EntityHandle h) => _inner.GetWorldSpaceCenter(h);
        public bool IsPlayer(EntityHandle h) => _inner.IsPlayer(h);
        public bool IsValid(EntityHandle h) => _inner.IsValid(h);
        public void SetAngles(EntityHandle entity, Vector angles) => _inner.SetAngles(entity, angles);
        public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => _inner.SetAngularVelocity(entity, degreesPerSecond);
        public void SetArmor(EntityHandle entity, int armor) => _inner.SetArmor(entity, armor);
        public void SetHealth(EntityHandle h, int health) => _inner.SetHealth(h, health);
        public bool SetKeyValue(EntityHandle entity, string key, string value) => _inner.SetKeyValue(entity, key, value);
        public void SetOrigin(EntityHandle h, Vector origin) => _inner.SetOrigin(h, origin);
        public void SetOwner(EntityHandle h, EntityHandle owner) => _inner.SetOwner(h, owner);
        public void SetVelocity(EntityHandle h, Vector velocity) => _inner.SetVelocity(h, velocity);
        public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => _inner.Teleport(h, origin, angles, zeroVelocity);
        public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => _inner.SetParent(child, parent, parentAttachment);
        public EntityHandle GetParent(EntityHandle child) => _inner.GetParent(child);
        public int GetParentAttachment(EntityHandle child) => _inner.GetParentAttachment(child);
        public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => _inner.SetLocalTransform(child, origin, angles);
        public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles) => _inner.GetLocalTransform(child, out origin, out angles);
        public ParentProbe ParentProbe(EntityHandle entity) => _inner.ParentProbe(entity);
        public bool GetAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles) => _inner.GetAttachmentFrame(entity, attachment, out origin, out angles);
        public int LookupAttachmentAny(EntityHandle entity, string attachmentName) => _inner.LookupAttachmentAny(entity, attachmentName);
        public EntityHandle FindBaseByName(string name) => _inner.FindBaseByName(name);
        public Span<byte> GetEngineBlock(EntityHandle entity) => _inner.GetEngineBlock(entity);
        public void DestroyPhysics(EntityHandle entity) => _inner.DestroyPhysics(entity);
        public float GetGravity(EntityHandle entity) => _inner.GetGravity(entity);
        public void SetGravity(EntityHandle entity, float gravity) => _inner.SetGravity(entity, gravity);
        public SolidFlags GetSolidFlags(EntityHandle entity) => _inner.GetSolidFlags(entity);
        public SolidType GetSolidType(EntityHandle entity) => _inner.GetSolidType(entity);
        public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => _inner.SetSolidity(entity, solidType, solidFlags);
        public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => _inner.SetBounds(entity, mins, maxs);
        public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => _inner.SetSpawnFlags(entity, spawnFlags);
    }

    private sealed class Hooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var each in hooks) call(each); }
    }

    private sealed class World
    {
        public readonly RecordingLog Log = new();
        public readonly Engine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly TFPlayerCarriers Carriers = new();
        public readonly PlayerComposites Composites;
        private readonly RecordingEngine _recording;
        private MapSpawner? _spawner;

        public World()
        {
            var services = TestServices.Services(out var recording);
            _recording = recording;
            recording.Ignores("Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.GetOrigin",
                              "Entities.GetAngles", "Entities.GetName", "Entities.GetClassName");
            recording.Returns("Players.GetHandle", Player);
            recording.Returns("Entities.IsValid", true);
            recording.Returns("Clock.Now", 0f);
            services.AddInstance<IEngineEntityRender>(recording);

            Engine = new Engine(recording);
            services.AddInstance<IEngineEntities>(Engine);

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(ServerModule).Assembly);
            Registry.RegisterAll(typeof(TFPlayerDataEntity).Assembly);
            Registry.Services = services;
            Registry.Directory.ShellDestroyer = Engine.Destroy;
            Engine.Registry = Registry;

            var source = new TFPlayerComponents(Engine, Registry, Carriers, Log,
                                                new TFPlayerItems(), new TFPlayerConditions(Carriers, recording),
                                                new TF2.Tests.Entities.Items.FakeMapPlayers(), new TF2.Tests.Rules.FakeClock(), recording,
                                                EconTestFiles.TrimmedSchema(), new PoollessGiver(), recording, recording);
            Composites = new PlayerComposites(Log, new Hooks<IPlayerComponentSource>(source), recording, recording);
        }

        public void Join() => ((IClientLifecycle)Composites).OnClientActive(PlayerIndex);
        public void Leave() => ((IClientLifecycle)Composites).OnClientDisconnected(PlayerIndex);

        /// <summary>
        /// The cleanup's removal, in <see cref="MapCleanupCleanupShape"/>'s
        /// order: mark the entity, then free it, then hand the edict to the
        /// next entity made — the slot the mode step found reused. Returns
        /// the dead carrier and the squatter that took its slot.
        /// </summary>
        public (EntityHandle Carrier, EntityHandle Squatter) RemoveCarrierAndReuseItsSlot()
        {
            var carrier = Carriers.CarrierOf(PlayerIndex);
            Assert.NotEqual(EntityHandle.None, carrier);
            Assert.Equal(RemovalMark.Marked, Registry.Directory.Remove(carrier));
            Engine.CleanupDeleteList();

            // The edict comes back around at once (AllowImmediateReuse), and a
            // made entity takes it: a fresh serial in the dead binding's slot.
            var squatter = Engine.Create("logic_relay");
            Engine.Spawn(squatter);
            Assert.Equal(carrier.EntryIndex, squatter.EntryIndex);
            return (carrier, squatter);
        }

        /// <summary>
        /// The round's cleanup over this rig, answering the preserve chain
        /// with what the server's registration would hand it (and with no
        /// filter's own list — the caller names the filter at
        /// <c>CleanUpMap</c>, the way <c>GameModes</c> hands the mode's, or
        /// NullMode's absence of one, to <see cref="IMapCleanup"/>).
        /// </summary>
        public MapCleanup Cleanup(params IMapCleanupPreserve[] preserve)
            => new(Registry.Directory,
                   _spawner ??= TestMapSpawner.Over(Engine, _recording, Registry),
                   Engine, Engine, Log,
                   preserve.Length > 0 ? new Hooks<IMapCleanupPreserve>(preserve) : null);
    }

    // MapCleanup's shape spelled back: the fact removes the way the round's
    // cleanup does (mark, free, immediate reuse).
    private static class MapCleanupCleanupShape { }

    private static TFClass ClassOfLive(ITFPlayerData data, int playerIndex)
        => data.Predicted(playerIndex) is { } state ? state.Class() : TFClass.Undefined;

    [Fact]
    public void A_carrier_whose_entity_died_under_the_attach_never_answers_through_the_dead_binding()
    {
        var world = new World();
        world.Join();
        Assert.True(world.Carriers.SetClass(PlayerIndex, TFClass.Scout));

        var (carrier, squatter) = world.RemoveCarrierAndReuseItsSlot();

        // The rig's own proof the carrier died — the removal took effect and
        // is not a no-op the fact could ride through. (Passes before and
        // after the fix.)
        Assert.Null(world.Registry.Directory.Resolve(carrier));              // the instance is gone
        Assert.Equal(squatter, world.Registry.Directory.HandleOf(carrier.EntryIndex));   // its slot answers for the squatter
        Assert.NotEqual(carrier, squatter);                                   // a fresh serial, not a re-bound same

        // The red line: before the fix the registry still answered the dead
        // carrier — the class read handed back Scout from the state it had
        // cached at the attach. On the live server the same dead answer is
        // what threw StaleEntityException on every movement read and lost
        // the command run (the round gate's mode step).
        Assert.Equal(TFClass.Undefined, ClassOfLive((ITFPlayerData)world.Carriers, PlayerIndex));
        Assert.Null(((ITFPlayerData)world.Carriers).Predicted(PlayerIndex));
        Assert.Null(((ITFPlayerData)world.Carriers).Networked(PlayerIndex));
        Assert.Equal(0, world.Carriers.Count);   // dropped, not merely emptied
    }

    [Fact]
    public void The_death_of_an_attached_carrier_is_named_once()
    {
        var world = new World();
        world.Join();
        world.RemoveCarrierAndReuseItsSlot();

        Assert.Equal(TFClass.Undefined, ClassOfLive((ITFPlayerData)world.Carriers, PlayerIndex));
        Assert.Equal(TFClass.Undefined, ClassOfLive((ITFPlayerData)world.Carriers, PlayerIndex));   // asked again

        // A dead carrier is not a silent no-carrier: the drop says so, once,
        // naming the class — not once per command read at 60 Hz.
        Assert.Equal(1, world.Log.Lines.Count(l => l.Warning && l.Text.Contains(TFEntityNames.PlayerData)));
    }

    [Fact]
    public void A_reattached_player_gets_a_new_carrier_the_registry_answers()
    {
        var world = new World();
        world.Join();
        world.RemoveCarrierAndReuseItsSlot();
        Assert.Null(((ITFPlayerData)world.Carriers).Predicted(PlayerIndex));   // the drop took effect

        world.Leave();
        world.Join();

        Assert.True(world.Carriers.SetClass(PlayerIndex, TFClass.Soldier));
        var state = ((ITFPlayerData)world.Carriers).Predicted(PlayerIndex);
        Assert.NotNull(state);                       // a new carrier, live, answering
        Assert.Equal(TFClass.Soldier, state!.Class());
        Assert.NotEqual(EntityHandle.None, world.Carriers.CarrierOf(PlayerIndex));
    }

    [Fact]
    public void A_foreign_filter_round_cleanup_leaves_the_named_carrier_standing()
    {
        var world = new World();
        world.Join();
        Assert.True(world.Carriers.SetClass(PlayerIndex, TFClass.Medic));
        var carrier = world.Carriers.CarrierOf(PlayerIndex);

        // The sv_mode null shape: the core's HL2MP list, which names none of
        // TF2's classes, answering while TF2's hook stands beside it.
        var report = world.Cleanup(new TFMapCleanupPreserve()).CleanUpMap(new HL2MPMapCleanupFilter());

        Assert.Equal(0, report.Removed);
        Assert.Equal(carrier, world.Carriers.CarrierOf(PlayerIndex));   // the same entity, not a replacement
        Assert.NotNull(world.Registry.Directory.Resolve(carrier));
        Assert.Equal(TFClass.Medic, ClassOfLive((ITFPlayerData)world.Carriers, PlayerIndex));
    }

    [Fact]
    public void Without_the_hook_the_same_cleanup_is_the_one_that_killed_the_carrier()
    {
        var world = new World();
        world.Join();
        Assert.True(world.Carriers.SetClass(PlayerIndex, TFClass.Medic));
        var carrier = world.Carriers.CarrierOf(PlayerIndex);

        // The differential the hook fact needs: with nothing in the chain,
        // the HL2MP list removes the carrier — the mode step's removal,
        // reproduced. The registry then answers no-carrier (the guard), not
        // the stale class and not a throw.
        var report = world.Cleanup().CleanUpMap(new HL2MPMapCleanupFilter());

        Assert.Equal(1, report.Removed);
        Assert.Null(world.Registry.Directory.Resolve(carrier));
        Assert.Equal(TFClass.Undefined, ClassOfLive((ITFPlayerData)world.Carriers, PlayerIndex));
    }
}
