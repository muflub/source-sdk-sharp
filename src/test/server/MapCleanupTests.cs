using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's <c>CleanUpMap</c> (tf2 ruling N2), over a real registry, directory,
/// I/O queue and map spawner: the map loads from a lump, a round plays on it
/// (a relay removes itself, a fire-once output is spent, an output is queued),
/// and the cleanup brings the map back as it loaded.
/// </summary>
public sealed class MapCleanupTests
{
    // ---- the rig ------------------------------------------------------------------

    /// <summary>
    /// The engine's half: edicts handed out lowest-free from
    /// <see cref="FirstMapEdict"/> (below it are the world and the clients), a
    /// slot asked for when it is free and the lowest free one when it is not
    /// (the native body's fallback), a fresh serial every time, and each
    /// entity's map spawnflags answered from the keys it was created with.
    /// </summary>
    internal sealed class Engine : IEngineEntities, IEdictSlots
    {
        public const int FirstMapEdict = 40;

        public IInterpolationBlocks InterpolationBlocks => NoInterpolationBlocks.Instance;
        public IRenderPolicyBlocks RenderPolicyBlocks => NoRenderPolicyBlocks.Instance;
        public IClientReplayEngine ClientReplay => NoClientReplayEngine.Instance;

        private readonly IEngineEntities _inner;
        private readonly Dictionary<int, EntityHandle> _edicts = new();
        private readonly Dictionary<uint, int> _spawnFlags = new();
        private readonly Dictionary<uint, string> _classOf = new();
        private uint _serial = 0x10;

        internal ScriptedEntityRegistry Registry = null!;

        /// <summary>Every slot a create asked for, in order (-1: any).</summary>
        internal readonly List<int> SlotsAsked = new();

        /// <summary>Edicts a fact says are held by something the directory does not know.</summary>
        internal readonly HashSet<int> HeldElsewhere = new();

        internal int Reuses;

        internal Engine(IEngineEntities inner) => _inner = inner;

        public EntityHandle CreateFromMap(string className, byte[] packedKeys) => Make(className, packedKeys, -1);

        public EntityHandle CreateFromMapAt(string className, byte[] packedKeys, int edictIndex)
            => Make(className, packedKeys, edictIndex);

        private EntityHandle Make(string className, byte[] packedKeys, int slot)
        {
            SlotsAsked.Add(slot);

            int entry = slot >= 0 && !InUse(slot) ? slot : LowestFree();
            var handle = new EntityHandle((uint)entry | (_serial++ << 16));
            _edicts[entry] = handle;
            _classOf[handle.Value] = className;

            var text = Encoding.UTF8.GetString(packedKeys).Split('\0');
            for (int i = 0; i + 1 < text.Length; i += 2)
            {
                if (string.Equals(text[i], "spawnflags", StringComparison.OrdinalIgnoreCase))
                    _spawnFlags[handle.Value] = int.Parse(text[i + 1]);
            }

            return handle;
        }

        private int LowestFree()
        {
            int entry = FirstMapEdict;
            while (InUse(entry))
                entry++;
            return entry;
        }

        public bool InUse(int index) => _edicts.ContainsKey(index) || HeldElsewhere.Contains(index);

        public void AllowImmediateReuse() => Reuses++;

        /// <summary>The directory's shell batch: the edicts go back.</summary>
        internal void Destroy(ReadOnlySpan<EntityHandle> batch)
        {
            foreach (var handle in batch)
            {
                if (_edicts.TryGetValue(handle.EntryIndex, out var held) && held == handle)
                    _edicts.Remove(handle.EntryIndex);
            }
        }

        public EntityHandle Create(string className) => CreateFromMap(className, []);
        public void Spawn(EntityHandle entity) => Registry.Spawn(entity, _classOf[entity.Value]);
        public void Remove(EntityHandle entity) => Registry.Removal.Remove(entity);
        public void CleanupDeleteList() => Registry.Directory.FreeRemoved();
        public int GetSpawnFlags(EntityHandle entity) => _spawnFlags.TryGetValue(entity.Value, out int f) ? f : 0;
        public int GetIndex(EntityHandle h) => h.IsInvisibleRange ? -1 : h.EntryIndex;

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

    /// <summary>A preserve hook naming one class (a mod's D10 addition).</summary>
    private sealed class KeepClass(string className) : IMapCleanupPreserve
    {
        public bool Preserves(string name) => string.Equals(name, className, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Hooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;
        public void Each(string what, Action<T> call) { foreach (var each in all) call(each); }
    }

    internal sealed class Rig
    {
        public readonly Engine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly MapSpawner Spawner;
        public readonly MapCleanup Cleanup;
        public readonly RecordingLog Log = new();
        internal readonly RecordingEngine Recording;

        public Rig(string lump, params IMapCleanupPreserve[] preserve)
        {
            var services = TestServices.Services(out var recording);
            Recording = recording;
            recording.Ignores("Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.GetOrigin",
                              "Entities.GetAngles", "Entities.GetName", "Entities.GetClassName");
            recording.Returns("Level.SpawnWorld", true);
            recording.Returns("Precache.PrecacheModel", 7);
            Engine = new Engine(recording);
            services.AddInstance<IEngineEntities>(Engine);

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(ServerModule).Assembly);
            Registry.RegisterAll(typeof(MapCleanupTests).Assembly);
            Registry.Services = services;
            Registry.Directory.ShellDestroyer = Engine.Destroy;
            Engine.Registry = Registry;

            Spawner = TestMapSpawner.Over(Engine, recording, Registry);
            Spawner.SpawnMapText(Encoding.UTF8.GetBytes(lump));

            Cleanup = new MapCleanup(Registry.Directory, Spawner, Engine, Engine, Log,
                                     preserve.Length > 0 ? new Hooks<IMapCleanupPreserve>(preserve) : null);
        }

        public ScriptedEntity? Named(string name)
            => Registry.Directory.Snapshot().FirstOrDefault(e => e.Name == name);

        public void Fire(string name, string input)
        {
            var entity = Named(name) ?? throw new InvalidOperationException($"no '{name}' to fire");
            Registry.Directory.FireInputAt(entity.Handle, input, null, curTime: 0f);
            Registry.Directory.ServiceIo(0f);
            Registry.Directory.FreeRemoved();
        }

        public List<string> SinkInputs() => ((TestIoSink)Named("sink")!).Inputs;

        /// <summary>A weapon made at runtime, as the arsenal makes one, carried by <paramref name="owner"/> (0: nobody).</summary>
        public EntityHandle Weapon(int owner)
        {
            var handle = Engine.Create(EntityNames.SsWeapon);
            Engine.Spawn(handle);
            ((SsWeapon)Registry.Directory.Resolve(handle)!).State.OwnerIndex = owner;
            return handle;
        }
    }

    private const string World = "{\n\"classname\" \"worldspawn\"\n}\n";

    // relay_once removes itself when it fires (logic_relay spawnflag 1);
    // relay_spent's output fires once (the connection's last field).
    private const string Lump = World
        + "{\n\"classname\" \"logic_relay\"\n\"targetname\" \"relay_once\"\n\"spawnflags\" \"1\"\n\"OnTrigger\" \"sink,Ping,,0,-1\"\n}\n"
        + "{\n\"classname\" \"logic_relay\"\n\"targetname\" \"relay_spent\"\n\"spawnflags\" \"2\"\n\"OnTrigger\" \"sink,Pong,,0,1\"\n}\n"
        + "{\n\"classname\" \"test_io_sink\"\n\"targetname\" \"sink\"\n}\n"
        + "{\n\"classname\" \"info_target\"\n\"targetname\" \"kept\"\n}\n";

    // ---- the round's entities come back -----------------------------------------

    [Fact]
    public void ARelayThatRemovedItselfIsBackAfterCleanUpMapAndFiresAgain()
    {
        var rig = new Rig(Lump);

        rig.Fire("relay_once", "Trigger");
        Assert.Equal(["Ping"], rig.SinkInputs());
        Assert.Null(rig.Named("relay_once"));   // RemoveOnFire took it

        rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        Assert.NotNull(rig.Named("relay_once"));
        rig.Fire("relay_once", "Trigger");
        Assert.Equal(["Ping"], rig.SinkInputs());   // the sink is a NEW one, fired once
    }

    [Fact]
    public void ASpentFireOnceOutputFiresAgainAfterCleanUpMap()
    {
        var rig = new Rig(Lump);

        rig.Fire("relay_spent", "Trigger");
        rig.Fire("relay_spent", "Trigger");
        Assert.Equal(["Pong"], rig.SinkInputs());   // spent after one

        rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        rig.Fire("relay_spent", "Trigger");
        Assert.Equal(["Pong"], rig.SinkInputs());
    }

    [Fact]
    public void APreservedClassKeepsItsEntityAndItsHandle()
    {
        var rig = new Rig(Lump);
        var kept = rig.Named("kept")!;
        var sink = rig.Named("sink")!;

        var report = rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        // info_target is in HL2MP's list: the same object, the same handle,
        // and not created a second time; the sink was removed and made again.
        Assert.Same(kept, rig.Named("kept"));
        Assert.Equal(kept.Handle, rig.Named("kept")!.Handle);
        Assert.NotSame(sink, rig.Named("sink"));
        Assert.Equal(1, rig.Registry.Directory.Snapshot().Count(e => e.Name == "kept"));
        Assert.Equal(2, report.Respawn.Skipped);   // the world and kept: both in the list
    }

    [Fact]
    public void ARecreatedEntityTakesItsOldSlotWhenItIsFree()
    {
        var rig = new Rig(Lump);
        var before = rig.Named("sink")!.Handle;
        rig.Engine.SlotsAsked.Clear();

        var report = rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        var after = rig.Named("sink")!.Handle;
        Assert.Equal(before.EntryIndex, after.EntryIndex);
        Assert.NotEqual(before, after);   // a new entity in the old slot
        Assert.Contains(before.EntryIndex, rig.Engine.SlotsAsked);
        Assert.Equal(report.Respawn.Created, report.Respawn.AtOldSlot);
        Assert.Equal(1, rig.Engine.Reuses);
    }

    [Fact]
    public void ARecreatedEntityTakesAnotherSlotWhenItsOldOneIsTaken()
    {
        var rig = new Rig(Lump);
        int old = rig.Named("sink")!.Handle.EntryIndex;
        rig.Engine.HeldElsewhere.Add(old);
        rig.Engine.SlotsAsked.Clear();

        var report = rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        Assert.NotEqual(old, rig.Named("sink")!.Handle.EntryIndex);
        Assert.DoesNotContain(old, rig.Engine.SlotsAsked);   // not asked for: stock creates anywhere
        Assert.Equal(1, report.Respawn.Elsewhere);
    }

    [Fact]
    public void TheEventQueueIsEmptyAfterCleanUpMap()
    {
        // The relay is KEPT (a mod's preserve hook), so its removal cannot be
        // what cancels the output it queued: only the queue's clear stands
        // between that output and the sink the cleanup makes again by name.
        var rig = new Rig(World
            + "{\n\"classname\" \"logic_relay\"\n\"targetname\" \"late\"\n\"OnTrigger\" \"sink,Late,,5,-1\"\n}\n"
            + "{\n\"classname\" \"test_io_sink\"\n\"targetname\" \"sink\"\n}\n",
            new KeepClass("logic_relay"));

        rig.Fire("late", "Trigger");
        Assert.Equal(1, rig.Registry.Directory.Io.Count);   // queued for t=5

        var report = rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());
        rig.Registry.Directory.ServiceIo(10f);

        Assert.Equal(0, rig.Registry.Directory.Io.Count);
        Assert.Equal(1, report.EventsCleared);
        Assert.Empty(rig.SinkInputs());
    }

    [Fact]
    public void APreserveHookKeepsItsClassStandingAndUncreated()
    {
        var rig = new Rig(Lump, new KeepClass("test_io_sink"));
        var sink = rig.Named("sink")!;

        rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        Assert.Same(sink, rig.Named("sink"));
        Assert.Equal(1, rig.Registry.Directory.Snapshot().Count(e => e.Name == "sink"));
    }

    [Fact]
    public void EveryMapEntityIsBackOnceAndTheCountsSayWhatHappened()
    {
        var rig = new Rig(Lump);
        rig.Fire("relay_once", "Trigger");

        var report = rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        // Three of the four map entities were standing (relay_once had gone);
        // kept is preserved, so two were removed and three made again.
        Assert.Equal(2, report.Removed);
        Assert.Equal(1, report.Kept);
        Assert.Equal(3, report.Respawn.Created);
        foreach (var name in new[] { "relay_once", "relay_spent", "sink", "kept" })
            Assert.Equal(1, rig.Registry.Directory.Snapshot().Count(e => e.Name == name));
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("[SourceSharp/MapCleanup] CleanUpMap:")
                                          && l.Text.Contains("removed 2, kept 1"));
    }

    // ---- the core's filter (CHL2MPRules::CleanUpMap) ----------------------------

    [Fact]
    public void TheCoreFilterKeepsAWeaponWithAnOwnerAndRemovesAnOwnerlessOne()
    {
        var rig = new Rig(World);
        var owned = rig.Weapon(owner: 3);
        var dropped = rig.Weapon(owner: 0);

        var report = rig.Cleanup.CleanUpMap(new HL2MPMapCleanupFilter());

        Assert.NotNull(rig.Registry.Directory.Resolve(owned));
        Assert.Null(rig.Registry.Directory.Resolve(dropped));
        Assert.Equal(1, report.Removed);
    }

    [Theory]
    [InlineData("func_brush", false)]
    [InlineData("FUNC_BRUSH", false)]     // Q_stricmp
    [InlineData("info_player_deathmatch", false)]
    [InlineData("player", false)]
    [InlineData("predicted_viewmodel", false)]
    [InlineData("func_door", true)]
    [InlineData("logic_relay", true)]
    public void TheCoreFilterCreatesAgainEveryClassItsListsDoNotName(string className, bool created)
        => Assert.Equal(created, new HL2MPMapCleanupFilter().ShouldCreateEntity(className));
}
