using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>The managed map spawner</b> — plan_entity.md §3.5 and step 2d.
///
/// <para>
/// The facts here are about the two things the step is measured by: that the
/// keys reach managed code WITHOUT a crossing, and that the ORDER a map is built
/// in is stock's. The block-splitting arithmetic underneath is pinned separately
/// and against the real parser, in <see cref="MapEntityBlockTests"/>'s
/// <c>spawnorder</c> rows — a fixture that stands in for native has to be checked
/// against native, and this file cannot do that.
/// </para>
/// </summary>
public sealed class MapSpawnerTests
{
    /// <summary>
    /// An entity area that hands out handles and remembers what it was asked for.
    ///
    /// <para>
    /// <b>Handles start at 0x0500 with a non-zero serial</b>, deliberately. Entry
    /// ZERO is a real slot — it is worldspawn — so a double that started at zero
    /// would let an implementation confusing "no entity" with "the first one"
    /// pass. Two lanes have measured the wrong path that way.
    /// </para>
    /// </summary>
    private sealed class Creator : IEngineEntities
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public IInterpolationBlocks InterpolationBlocks => NoInterpolationBlocks.Instance;
        public IRenderPolicyBlocks RenderPolicyBlocks => NoRenderPolicyBlocks.Instance;
        public IClientReplayEngine ClientReplay => NoClientReplayEngine.Instance;

        private readonly IEngineEntities _engine;
        private uint _next = 0x0500;

        internal readonly List<(string ClassName, byte[] Keys)> Created = new();

        internal readonly List<EntityHandle> Spawned = new();
        internal readonly List<EntityHandle> RemovedHandles = new();

        /// <summary>
        /// Every step of the load, in the order it happened — <c>parse</c>,
        /// <c>create:X</c>, <c>spawn:X</c>.
        ///
        /// <para>
        /// ONE list for all three, because "the parse came before the first
        /// create" is a comparison and a comparison needs both events on one
        /// timeline. Three separate counters can only say that all three
        /// happened.
        /// </para>
        /// </summary>
        internal readonly List<string> Log = new();

        /// <summary>Classnames this double refuses to create, so a fact can ask
        /// what the spawner does with a failure.</summary>
        internal readonly HashSet<string> Refuse = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The registry a spawn runs against, when a fact wants a class's own
        /// <c>OnSpawn</c> to run — which is how a <c>point_template</c> reaches
        /// the window at all. Null means "record the spawn and do nothing", which
        /// is what a fact about ORDER wants.
        /// </summary>
        internal ScriptedEntityRegistry? RunSpawns;

        private readonly Dictionary<uint, string> _classOf = new();

        internal Creator(IEngineEntities engine) => _engine = engine;

        internal EntityHandle HandleOf(int nth) => new((uint)(0x0500 + nth) | (0x2Au << 16));

        public EntityHandle CreateFromMap(string className, byte[] packedKeys)
        {
            if (Refuse.Contains(className))
                return EntityHandle.None;

            var handle = new EntityHandle(_next++ | (0x2Au << 16));

            Created.Add((className, packedKeys));
            Log.Add($"create:{className}");
            _classOf[handle.Value] = className;

            return handle;
        }

        public EntityHandle Create(string className) => CreateFromMap(className, Array.Empty<byte>());

        public void Spawn(EntityHandle entity)
        {
            Spawned.Add(entity);
            Log.Add($"spawn:{Class(entity)}");

            RunSpawns?.Spawn(entity, Class(entity));
        }

        internal string Class(EntityHandle handle)
            => _classOf.TryGetValue(handle.Value, out string? name) ? name : "?";

        public void Remove(EntityHandle entity) => RemovedHandles.Add(entity);

        // Everything else is the recording double's, so stubs and assertions
        // keep working through this wrapper and nothing here has an opinion it
        // was not asked for. The same forwarding block WeaponSwitchTests'
        // SpawningEntities carries, and for the same reason.
        public int Count() => _engine.Count();
        public void PurgeMapRefs() => _engine.PurgeMapRefs();
        public void CleanupDeleteList() => _engine.CleanupDeleteList();
        public int InvalidateMdlCacheAll() => _engine.InvalidateMdlCacheAll();
        public int UpdateVisibilityAll() => _engine.UpdateVisibilityAll();
        public void AddBaseVelocity(EntityHandle entity, Vector velocity) => _engine.AddBaseVelocity(entity, velocity);
        public void PositionChanged(EntityHandle entity, Vector previousOrigin) => _engine.PositionChanged(entity, previousOrigin);
        public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => _engine.ApplyVelocityImpulse(h, impulse, clearGround);
        public IEnumerable<EntityHandle> FindAllByClassname(string className) => _engine.FindAllByClassname(className);
        public List<EntityHandle> FindAllByName(string name, int limit = 512) => _engine.FindAllByName(name, limit);
        public EntityHandle FindByName(string name) => _engine.FindByName(name);
        public bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default) => _engine.FireInput(target, input, value, activator);
        public Vector GetAngles(EntityHandle h) => _engine.GetAngles(h);
        public Vector GetAngularVelocity(EntityHandle entity) => _engine.GetAngularVelocity(entity);
        public int GetArmor(EntityHandle entity) => _engine.GetArmor(entity);
        public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => _engine.GetBounds(entity);
        public Vector GetBoundsSize(EntityHandle entity) => _engine.GetBoundsSize(entity);
        public string GetClassName(EntityHandle h) => _engine.GetClassName(h);
        public Vector GetEyeAngles(EntityHandle h) => _engine.GetEyeAngles(h);
        public Vector GetEyePosition(EntityHandle h) => _engine.GetEyePosition(h);
        public int GetHealth(EntityHandle h) => _engine.GetHealth(h);
        public int GetIndex(EntityHandle h) => _engine.GetIndex(h);
        public string GetName(EntityHandle entity) => _engine.GetName(entity);
        public Vector GetOrigin(EntityHandle h) => _engine.GetOrigin(h);
        public Vector GetSpawnAngles(EntityHandle entity) => _engine.GetSpawnAngles(entity);
        public int GetSpawnFlags(EntityHandle entity) => _engine.GetSpawnFlags(entity);
        public Vector GetSpawnOrigin(EntityHandle entity) => _engine.GetSpawnOrigin(entity);
        public Vector GetVelocity(EntityHandle h) => _engine.GetVelocity(h);
        public Vector GetWorldSpaceCenter(EntityHandle h) => _engine.GetWorldSpaceCenter(h);
        public bool IsPlayer(EntityHandle h) => _engine.IsPlayer(h);
        public bool IsValid(EntityHandle h) => _engine.IsValid(h);
        public void SetAngles(EntityHandle entity, Vector angles) => _engine.SetAngles(entity, angles);
        public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => _engine.SetAngularVelocity(entity, degreesPerSecond);
        public void SetArmor(EntityHandle entity, int armor) => _engine.SetArmor(entity, armor);
        public void SetHealth(EntityHandle h, int health) => _engine.SetHealth(h, health);
        public bool SetKeyValue(EntityHandle entity, string key, string value) => _engine.SetKeyValue(entity, key, value);
        public void SetOrigin(EntityHandle h, Vector origin) => _engine.SetOrigin(h, origin);
        public void SetOwner(EntityHandle h, EntityHandle owner) => _engine.SetOwner(h, owner);
        public void SetVelocity(EntityHandle h, Vector velocity) => _engine.SetVelocity(h, velocity);
        public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => _engine.Teleport(h, origin, angles, zeroVelocity);
        public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => _engine.SetParent(child, parent, parentAttachment);
        public EntityHandle GetParent(EntityHandle child) => _engine.GetParent(child);
        public int GetParentAttachment(EntityHandle child) => _engine.GetParentAttachment(child);
        public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => _engine.SetLocalTransform(child, origin, angles);
        public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles) => _engine.GetLocalTransform(child, out origin, out angles);
        public ParentProbe ParentProbe(EntityHandle entity) => _engine.ParentProbe(entity);
        public bool GetAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles) => _engine.GetAttachmentFrame(entity, attachment, out origin, out angles);
        public int LookupAttachmentAny(EntityHandle entity, string attachmentName) => _engine.LookupAttachmentAny(entity, attachmentName);
        public EntityHandle FindBaseByName(string name) => _engine.FindBaseByName(name);
        public Span<byte> GetEngineBlock(EntityHandle entity) => _engine.GetEngineBlock(entity);
        public void DestroyPhysics(EntityHandle entity) => _engine.DestroyPhysics(entity);
        public float GetGravity(EntityHandle entity) => _engine.GetGravity(entity);
        public void SetGravity(EntityHandle entity, float gravity) => _engine.SetGravity(entity, gravity);
        public SolidFlags GetSolidFlags(EntityHandle entity) => _engine.GetSolidFlags(entity);
        public SolidType GetSolidType(EntityHandle entity) => _engine.GetSolidType(entity);
        public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => _engine.SetSolidity(entity, solidType, solidFlags);
        public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => _engine.SetBounds(entity, mins, maxs);
        public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => _engine.SetSpawnFlags(entity, spawnFlags);
    }

    /// <summary>
    /// The level area, forwarding to the recording double and writing
    /// <c>parse</c> onto the SAME timeline the creates and spawns are on.
    ///
    /// <para>
    /// It exists only so a fact can say "the parse came before the first create",
    /// which is a comparison and needs one list. Everything else forwards
    /// untouched.
    /// </para>
    /// </summary>
    private sealed class LevelSpy : IEngineLevel
    {
        private readonly IEngineLevel _inner;
        private readonly List<string> _log;

        internal LevelSpy(IEngineLevel inner, List<string> log)
        {
            _inner = inner;
            _log = log;
        }

        public bool SpawnWorld(IReadOnlyList<KeyValuePair<string, string>> keys)
        {
            _log.Add("world");
            return _inner.SpawnWorld(keys);
        }

        public void ResetServerTime() => _inner.ResetServerTime();
        public void ParseParticleManifest() => _inner.ParseParticleManifest();
        public void ParseParticleManifestForMap(string m) => _inner.ParseParticleManifestForMap(m);
        public void PrecachePointTemplates(IReadOnlyList<TemplateSpawn> t) => _inner.PrecachePointTemplates(t);
        public void SetLowViolence(string m) => _inner.SetLowViolence(m);
        public void LoadMessageOfTheDay() => _inner.LoadMessageOfTheDay();
        public int ResetDeleteList() => _inner.ResetDeleteList();
        public void ActivateAllEntities() => _inner.ActivateAllEntities();
        public void InitPostEntitySystems() => _inner.InitPostEntitySystems();
        public void SetPrecacheAllowed(bool a) => _inner.SetPrecacheAllowed(a);
        public void ShutdownPreClearSteamSystems() => _inner.ShutdownPreClearSteamSystems();
        public void ClearSteamAPIContext() => _inner.ClearSteamAPIContext();
        public void ShutdownPreEntitySystems() => _inner.ShutdownPreEntitySystems();
        public void ShutdownStandaloneEntities() => _inner.ShutdownStandaloneEntities();
        public void ShutdownSoundEnt() => _inner.ShutdownSoundEnt();
        public void ClearEntityList() => _inner.ClearEntityList();
        public void ShutdownPostEntitySystems() => _inner.ShutdownPostEntitySystems();
        public void UncacheAllParticleSystems() => _inner.UncacheAllParticleSystems();
        public void RecreateParticleDictionary() => _inner.RecreateParticleDictionary();
        public void PropDataLoadFile() => _inner.PropDataLoadFile();
        public void PropDataClearFile() => _inner.PropDataClearFile();
        public bool LoadSaveGameState(string m, bool c) => _inner.LoadSaveGameState(m, c);
    }

    /// <summary>The lump used by most facts: three entities, one of them a class
    /// nothing on the managed side owns.</summary>
    private const string ThreeEntities = """
        {
        "classname" "worldspawn"
        }
        {
        "classname" "test_spawn_relay"
        "targetname" "alpha"
        "origin" "1 2 3"
        }
        {
        "classname" "test_spawn_sink"
        "targetname" "beta"
        }
        """;

    /// <summary>
    /// A registry that owns the two <c>test_spawn_*</c> classes and nothing else,
    /// so <c>worldspawn</c> is residue exactly as it is in a real map.
    /// </summary>
    private static ScriptedEntityRegistry Registry()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SpawnRelay).Assembly);
        return registry;
    }

    private static (MapSpawner Spawner, Creator Entities, RecordingEngine Engine, TemplateStore Templates)
        Build(ScriptedEntityRegistry? registry = null)
    {
        var engine = new RecordingEngine();
        // Native answers whether the world was built; TRUE, as a real level's would.
        engine.Returns("Level.SpawnWorld", true);
        var entities = new Creator(engine);
        var templates = new TemplateStore(new RecordingLog());
        var level = new LevelSpy(engine, entities.Log);
        var spawner = TestMapSpawner.Over(entities, level, registry ?? Registry(), templates);

        return (spawner, entities, engine, templates);
    }

    /// <summary>
    /// The world's pairs native was last handed, as <c>key=value</c> joined by
    /// <c>;</c> in the order they crossed. Empty when SpawnWorld was not called.
    /// </summary>
    private static string WorldKeys(RecordingEngine engine)
        => engine.Last("Level.SpawnWorld") is { } call
            ? string.Join(';', call.ArgAs<KeyValuePair<string, string>[]>(0)
                                   .Select(kv => kv.Key + "=" + kv.Value))
            : string.Empty;

    private static string KeysOf(byte[] packed) =>
        string.Join('~', Encoding.UTF8.GetString(packed).Split('\0',
                                                 StringSplitOptions.RemoveEmptyEntries));

    // ------------------------------------------------------------------------
    // THE LEVER: the keys reach managed code, and they do not cross.
    // ------------------------------------------------------------------------

    /// <summary>
    /// <b>The fact the whole step exists for.</b> Every key of a managed entity
    /// is applied, and the count is the one that used to be
    /// <c>crossings.entityOps.KeyValue</c>.
    /// </summary>
    [Fact]
    public void EveryKeyOfAManagedEntityIsApplied()
    {
        var (spawner, _, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        // 3 on the relay (classname, targetname, origin) and 2 on the sink.
        Assert.True(spawner.LastManagedKeys == 5,
            $"every key of every managed entity is applied; got {spawner.LastManagedKeys}");
    }

    /// <summary>
    /// And they land on the INSTANCE, which is what "applied" has to mean — a
    /// counter that rose while the value went nowhere is the failure this fact
    /// exists to exclude.
    /// </summary>
    [Fact]
    public void AMapKeyReachesTheManagedInstance()
    {
        var registry = Registry();
        var (spawner, entities, _, _) = Build(registry);

        spawner.SpawnMapText(Encoding.UTF8.GetBytes("""
            {
            "classname" "test_spawn_relay"
            "mapvalue" "7"
            }
            """));

        Assert.True(registry.Directory.Resolve(entities.HandleOf(0)) is SpawnRelay { MapValue: 7 },
            "the map's value is on the C# object");
    }

    /// <summary>
    /// <b>And no crossing was paid for it.</b> One create per entity is what the
    /// step costs; a key costs nothing.
    /// </summary>
    [Fact]
    public void TheKeysCostNoCrossingAtAll()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(spawner.LastCreateCrossings == 2 && entities.Created.Count == 2,
            "two managed entities, two creation crossings -- and the five keys added none");
    }

    /// <summary>
    /// The SHELL's copy of the keys still goes down, in one payload, unfiltered.
    /// A managed side that kept them to itself would leave every entity at the
    /// origin with no name and no model.
    /// </summary>
    [Fact]
    public void EveryKeyAlsoGoesDownToTheShellInOnePayload()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(KeysOf(entities.Created[0].Keys)
                    == "classname~test_spawn_relay~targetname~alpha~origin~1 2 3",
            $"the payload is every key, in map order; got {KeysOf(entities.Created[0].Keys)}");
    }

    /// <summary>
    /// <b>Nothing is filtered on the way down</b>, which is the difference from
    /// the route step 2a refuted: a key the managed class does not declare is
    /// exactly what the near-miss report is looking at, so dropping it before
    /// either side sees it is the one thing this must not do.
    /// </summary>
    [Fact]
    public void AKeyNoManagedClassDeclaresIsStillSentAndStillApplied()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes("""
            {
            "classname" "test_spawn_relay"
            "mapvalu" "7"
            }
            """));

        Assert.True(KeysOf(entities.Created[0].Keys).Contains("mapvalu")
                 && spawner.LastManagedKeys == 2,
            "a mapper's typo reaches both sides, which is what lets it be reported");
    }

    // ------------------------------------------------------------------------
    // THE SPLIT: what stays native.
    // ------------------------------------------------------------------------

    [Fact]
    public void ABlockWhoseClassTheRegistryOwnsIsNotHandedToNative()
    {
        var (spawner, _, engine, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        string world = WorldKeys(engine);

        Assert.True(world.Length > 0 && !world.Contains("test_spawn_relay") && !world.Contains("test_spawn_sink"),
            $"a managed classname is the spawner's, not native's; got: {world}");
    }

    [Fact]
    public void TheWorldspawnBlockIsHandedToNativeAsItsPairs()
    {
        var (spawner, _, engine, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.Equal("classname=worldspawn", WorldKeys(engine));
    }

    /// <summary>
    /// plan_server_novalve.md P5: native no longer walks text, so the pairs are
    /// what CEntityMapData's walk would have handed CWorld::KeyValue -- every
    /// one, classname included, in block order.
    /// </summary>
    [Fact]
    public void TheWorldsPairsCrossInBlockOrderWithTheClassname()
    {
        var (spawner, _, engine, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes("""
            {
            "skyname" "sky_day01_01"
            "classname" "worldspawn"
            "maxpropscreenwidth" "-1"
            }
            """));

        Assert.Equal("skyname=sky_day01_01;classname=worldspawn;maxpropscreenwidth=-1",
                     WorldKeys(engine));
    }

    [Fact]
    public void AResidueBlockThatIsNotTheWorldIsNotHandedToNative()
    {
        var (spawner, _, engine, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes("""
            {
            "classname" "worldspawn"
            }
            {
            "classname" "func_useableladder"
            "targetname" "ladder1"
            }
            """));

        string world = WorldKeys(engine);

        Assert.True(world == "classname=worldspawn",
            $"only the world crosses; the other residue is declared, not built; got: {world}");
        Assert.Equal(1, engine.Calls.Count(c => c.Member == "Level.SpawnWorld"));
    }

    [Fact]
    public void AMapWithNoWorldspawnMakesNoSpawnWorldCall()
    {
        var (spawner, _, engine, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes("""
            {
            "classname" "test_spawn_sink"
            "targetname" "beta"
            }
            """));

        // Positive arm first: the map WAS processed, so the absence is about the
        // world and not about a spawner that never ran.
        Assert.Equal(1, spawner.LastManagedBlocks);
        Assert.Null(engine.Last("Level.SpawnWorld"));
    }

    /// <summary>
    /// The residue count is what says when <c>mapentities.cpp</c> can leave the
    /// vpc: zero, and there is nothing left for the native parse to do.
    /// </summary>
    [Fact]
    public void TheResidueIsCountedAndNamed()
    {
        var (spawner, _, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(spawner.LastNativeBlocks == 1
                 && spawner.LastManagedBlocks == 2
                 && spawner.LastResidueClasses.Count == 1
                 && spawner.LastResidueClasses[0] == "worldspawn",
            "one residue block, named, so a reader can see what is left");
    }

    // ------------------------------------------------------------------------
    // THE ORDER.
    // ------------------------------------------------------------------------

    /// <summary>
    /// <b>The world before anything else.</b> The native parse spawns worldspawn
    /// inside its own loop, and everything downstream — the physics environment,
    /// the game rules, a trace — wants a world; a managed entity created first
    /// would spawn into a level that has none.
    /// </summary>
    [Fact]
    public void TheNativeParseRunsBeforeAnyManagedEntityIsCreated()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(entities.Log.IndexOf("world") == 0,
            $"the residue is parsed first; the load ran {string.Join(" -> ", entities.Log)}");
    }

    /// <summary>
    /// <b>Everything is created and keyed before anything spawns.</b> Stock parses
    /// the whole map before it spawns any of it, and the reason is one a mapper
    /// relies on: an entity that refers to another by name wants the other to
    /// exist. A create-then-spawn-each loop would break every such reference in
    /// map order.
    /// </summary>
    [Fact]
    public void EveryEntityIsCreatedBeforeAnyEntityIsSpawned()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(entities.Log.SequenceEqual(new[]
                    {
                        "world",
                        "create:test_spawn_relay",
                        "create:test_spawn_sink",
                        "spawn:test_spawn_relay",
                        "spawn:test_spawn_sink",
                    }),
            $"the whole order, on one timeline: {string.Join(" -> ", entities.Log)}");
    }

    /// <summary>
    /// <b>And the timeline above would catch a reordering rather than merely
    /// record one.</b> A sequence assertion is only a check if some other
    /// sequence fails it, so this is the interleaved order a create-then-spawn
    /// loop would produce, asserted to be different.
    /// </summary>
    [Fact]
    public void AnInterleavedCreateAndSpawnWouldNotMatchTheTimeline()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        var interleaved = new[]
        {
            "world",
            "create:test_spawn_relay",
            "spawn:test_spawn_relay",
            "create:test_spawn_sink",
            "spawn:test_spawn_sink",
        };

        Assert.True(!entities.Log.SequenceEqual(interleaved),
            "a loop that spawned each entity as it created it is a DIFFERENT timeline, "
          + "which is what makes the fact above a check");
    }

    [Fact]
    public void TheEntitiesSpawnInMapOrder()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(entities.Spawned.SequenceEqual(new[]
                    { entities.HandleOf(0), entities.HandleOf(1) }),
            "the relay spawns before the sink, which is the order the map wrote them");
    }

    /// <summary>
    /// A class the shell could not build is skipped, and everything after it
    /// still loads. A map that lost its whole tail to one bad classname would be
    /// the worst possible failure: it looks like half a map.
    /// </summary>
    // ------------------------------------------------------------------------
    // THE SPAWN-PRIORITY RULE -- plan_entity.md Phase 4 exit, lane p4-lifecycle.
    //
    // Stock breaks CompareSpawnOrder's depth ties with a thirteen-classname
    // CStringRegistry built in SortSpawnListByHierarchy, over pSpawnList: the
    // CBaseEntity spawn list. ELEVEN of those thirteen classnames are managed
    // entities now and a managed entity is never in that list -- it is HERE, and
    // it was spawned in the order the mapper happened to place it. The rule
    // still ran and had nothing left to apply itself to.
    //
    // Measured from the shipped BSPs' entity lumps, as pairs where an entity
    // stock spawns LATER appears earlier in the file: dm_lockdown 189,
    // ss_sandbox 183.
    // ------------------------------------------------------------------------

    /// <summary>A prop and a constraint, with the prop written FIRST.</summary>
    private const string APropBeforeItsConstraint = """
        {
        "classname" "worldspawn"
        }
        {
        "classname" "prop_physics"
        "targetname" "crate"
        }
        {
        "classname" "phys_hinge"
        "targetname" "hinge"
        }
        """;

    [Fact]
    public void AConstraintSpawnsBeforeAPropTheMapWroteFirst()
    {
        var (spawner, entities, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(APropBeforeItsConstraint));

        // Created in MAP order -- stock parses every block before it spawns any,
        // and a mapper relies on that. Only the SPAWN pass is reordered.
        Assert.Equal(new[] { "create:prop_physics", "create:phys_hinge" },
                     entities.Log.Where(l => l.StartsWith("create:", StringComparison.Ordinal)));

        // And SPAWNED in stock's priority order: phys_hinge is 8, prop_physics 7,
        // and higher spawns first.
        Assert.Equal(new[] { "spawn:phys_hinge", "spawn:prop_physics" },
                     entities.Log.Where(l => l.StartsWith("spawn:", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnEntityThatCouldNotBeCreatedDoesNotStopTheLoad()
    {
        var (spawner, entities, _, _) = Build();
        entities.Refuse.Add("test_spawn_relay");

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(spawner.Entities.Count == 1
                 && spawner.Entities[0].ClassName == "test_spawn_sink",
            "the block after the refusal is still built");
    }

    // ------------------------------------------------------------------------
    // ACTIVATE.
    // ------------------------------------------------------------------------

    [Fact]
    public void ActivateReachesEveryEntityThatSpawned()
    {
        var registry = Registry();
        var (spawner, entities, _, _) = Build(registry);

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        Assert.True(spawner.ActivateAll() == 2, "both managed entities are activated");
    }

    [Fact]
    public void ActivateRunsTheClassesOwnActivate()
    {
        var registry = Registry();
        var (spawner, entities, _, _) = Build(registry);

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));
        spawner.ActivateAll();

        Assert.True(registry.Directory.Resolve(entities.HandleOf(0)) is SpawnRelay { Activated: true },
            "the class's own OnServerActivate ran");
    }

    /// <summary>
    /// Activate is a SECOND moment and not a second name for spawn: it runs after
    /// every entity in the map exists, which is the whole reason stock has both.
    /// </summary>
    [Fact]
    public void ActivateRunsAfterSpawnAndNotInsteadOfIt()
    {
        var registry = Registry();
        var (spawner, entities, _, _) = Build(registry);

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));

        var relay = (SpawnRelay)registry.Directory.Resolve(entities.HandleOf(0))!;
        bool activatedDuringSpawn = relay.Activated;

        spawner.ActivateAll();

        Assert.True(!activatedDuringSpawn && relay.Activated,
            "nothing was activated by the spawn pass, and everything was by the activate pass");
    }

    // ------------------------------------------------------------------------
    // THE TEMPLATE WINDOW.
    //
    // A point_template claims its sources between the map's entities being
    // created and being spawned. The window used to be native
    // (MapEntity_TemplateCapture, over the parse's own local arrays); it is the
    // spawner's now, and a real PointTemplate is what reaches it -- through
    // TemplateStore.Capture, which is the one line of PointTemplate.OnSpawn that
    // changed.
    // ------------------------------------------------------------------------

    /// <summary>A lump with a real point_template naming a real source.</summary>
    private const string TemplateAndSource = """
        {
        "classname" "worldspawn"
        }
        {
        "classname" "test_spawn_relay"
        "targetname" "alpha"
        }
        {
        "classname" "point_template"
        "targetname" "maker"
        "Template01" "alpha"
        }
        """;

    /// <summary>
    /// Builds a spawner whose spawns run the real classes, so a point_template's
    /// own <c>OnSpawn</c> reaches the window the way it does in the game.
    /// </summary>
    private static (MapSpawner Spawner, Creator Entities, TemplateStore Templates)
        WithLiveSpawns()
    {
        var store = new TemplateStore(new RecordingLog());
        var services = TestServices.Services(out var engine, store);

        engine.Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetAngles", Vector.Zero)
              .Returns("Entities.GetSpawnFlags", 0)
              .Returns("Level.SpawnWorld", true);

        var registry = new ScriptedEntityRegistry(TestEngines.Server()) { Services = services };
        registry.RegisterAll(typeof(PointTemplate).Assembly);
        registry.RegisterAll(typeof(SpawnRelay).Assembly);

        var entities = new Creator(engine) { RunSpawns = registry };
        var level = new LevelSpy(engine, entities.Log);

        return (TestMapSpawner.Over(entities, level, registry, store), entities, store);
    }

    [Fact]
    public void TheWindowIsShutOutsideAMapLoad()
    {
        var (spawner, _, _, _) = Build();

        Assert.True(spawner.Capture("alpha", removeSources: false, limit: 64).Count == 0,
            "a point_template made by ent_create finds nothing, which is stock's answer");
    }

    [Fact]
    public void APointTemplateClaimsItsSourceOutOfTheWindow()
    {
        var (spawner, _, templates) = WithLiveSpawns();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(TemplateAndSource));

        Assert.True(templates.Count == 1,
            $"the source's map text is stored as a template; got {templates.Count}");
    }

    /// <summary>
    /// And the stored text is the SOURCE's, not the template's. A window that
    /// matched on the wrong entity would still store one thing and still count
    /// one.
    /// </summary>
    [Fact]
    public void TheStoredTemplateIsTheSourcesOwnMapText()
    {
        var (spawner, _, templates) = WithLiveSpawns();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(TemplateAndSource));

        string stored = Encoding.UTF8.GetString(templates.MapData(0));

        Assert.True(stored.Contains("test_spawn_relay") && !stored.Contains("point_template"),
            $"the source is what was templated; got: {stored}");
    }

    /// <summary>
    /// <b>A claimed source never spawns.</b> Stock removes it from the spawn list,
    /// and a template whose sources also stood in the map would double every
    /// group — one copy the mapper sees and one the template makes.
    /// </summary>
    [Fact]
    public void ASourceTheWindowClaimsNeverSpawns()
    {
        var (spawner, entities, _) = WithLiveSpawns();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(TemplateAndSource));

        Assert.True(!entities.Log.Contains("spawn:test_spawn_relay"),
            $"the claimed source is not spawned; the load ran {string.Join(" -> ", entities.Log)}");
    }

    /// <summary>
    /// And it is REMOVED as well as unspawned, which is the other half stock does:
    /// left alive it would still answer a find-by-name after the template ate it.
    /// </summary>
    [Fact]
    public void AClaimedSourceIsAlsoRemoved()
    {
        var (spawner, entities, _) = WithLiveSpawns();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(TemplateAndSource));

        Assert.True(entities.RemovedHandles.Count == 1
                 && entities.Class(entities.RemovedHandles[0]) == "test_spawn_relay",
            "the source the template consumed is gone from the world");
    }

    /// <summary>
    /// <b>The template spawns before the entities it claims could have.</b> That
    /// is the whole meaning of the window, and it is the one ordering nobody
    /// would choose by accident: a template that ran last would find its sources
    /// already spawned.
    /// </summary>
    [Fact]
    public void TheTemplateSpawnsBeforeTheRestOfTheMap()
    {
        var (spawner, entities, _) = WithLiveSpawns();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(TemplateAndSource));

        Assert.True(entities.Log.SequenceEqual(new[]
                    {
                        "world",
                        "create:test_spawn_relay",
                        "create:point_template",
                        "spawn:point_template",
                    }),
            $"the template spawns inside its window; the load ran "
          + string.Join(" -> ", entities.Log));
    }

    /// <summary>
    /// The window shuts again. A template made later — by <c>ent_create</c>, or
    /// by a spawner at runtime — must find nothing, which is stock's answer and
    /// the reason the word "window" is the right one.
    /// </summary>
    [Fact]
    public void TheWindowIsShutAgainOnceTheMapHasSpawned()
    {
        var (spawner, _, templates) = WithLiveSpawns();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(TemplateAndSource));

        Assert.True(templates.MapWindow is null
                 && spawner.Capture("alpha", removeSources: false, limit: 64).Count == 0,
            "the window is not left open behind the load");
    }

    [Fact]
    public void TheSpawnerIsEmptyBetweenMaps()
    {
        var (spawner, _, _, _) = Build();

        spawner.SpawnMapText(Encoding.UTF8.GetBytes(ThreeEntities));
        spawner.Clear();

        Assert.True(spawner.Entities.Count == 0 && spawner.LastManagedKeys == 0,
            "every entity here belonged to the map that went");
    }

    // ------------------------------------------------------------------------
    // The packing, which is what the one crossing carries.
    // ------------------------------------------------------------------------

    [Fact]
    public void ThePackedPayloadSeparatesEveryKeyAndValueWithANul()
    {
        byte[] packed = MapSpawner.Pack(new[] { ("a", "1"), ("b", "2") });

        Assert.True(packed.SequenceEqual("a\01\0b\02\0"u8.ToArray()),
            "key, NUL, value, NUL -- and a NUL cannot appear in map text");
    }

    [Fact]
    public void ThePackedPayloadKeepsAValueThatContainsSpacesWhole()
    {
        byte[] packed = MapSpawner.Pack(new[] { ("origin", "1 2 3") });

        Assert.True(KeysOf(packed) == "origin~1 2 3",
            "a value is one field however many spaces are in it");
    }

    /// <summary>
    /// An entity-I/O value carries the ESC delimiter, which is a byte a
    /// separator must not collide with. It does not: the separator is NUL, and
    /// map text cannot hold one.
    /// </summary>
    [Fact]
    public void ThePackedPayloadKeepsAnEntityIoValueWhole()
    {
        byte[] packed = MapSpawner.Pack(new[] { ("OnTrigger", "beta\x1bKill\x1b\x1b0\x1b-1") });

        Assert.True(KeysOf(packed) == "OnTrigger~beta\x1bKill\x1b\x1b0\x1b-1",
            "the ESC delimiter is data, and NUL is the separator");
    }
}

/// <summary>
/// A class for <see cref="MapSpawnerTests"/> to spawn. Declared here because a
/// fact about "every key reaches the instance" needs an instance with a key on
/// it, and inventing one per fact would be inventing the thing under test.
/// </summary>
[ScriptedEntity("test_spawn_relay")]
public sealed class SpawnRelay : ScriptedEntity
{
    /// <summary>What a map may set on this entity.</summary>
    [ScriptVars] public SpawnRelayVars Vars = new();

    public int MapValue => Vars.MapValue;

    public bool Activated { get; private set; }

    public override void OnServerActivate() => Activated = true;
}

/// <summary>The map keys <see cref="SpawnRelay"/> declares. One, because one is
/// what a fact about "the key reached the instance" needs.</summary>
[ScriptVars]
public partial class SpawnRelayVars
{
    public int MapValue { get; set; }
}

/// <summary>The other end, so a fixture can have two managed entities and a fact
/// can be about the order between them.</summary>
[ScriptedEntity("test_spawn_sink")]
public sealed class SpawnSink : ScriptedEntity
{
}
