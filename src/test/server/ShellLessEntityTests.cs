using System;
using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A second shell-less class, so a find-by-class has something to reject.</summary>
[ScriptedEntity("test_shellless_sink")]
public sealed class TestShellLessSink : ScriptedEntity
{
    public readonly List<string> Inputs = new();

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        Inputs.Add(name);
        return true;
    }
}

/// <summary>
/// A class that DOES make a shell, so every fact about the shell-less path has
/// a control that is not the same thing with the flag off.
/// </summary>
[ScriptedEntity("test_shelled_prop", ScriptedEntityKind.Prop)]
public sealed class TestShelledProp : ScriptedEntity
{
}

/// <summary>
/// <b>SHELL-LESS LOGICAL ENTITIES</b> — <c>plan_entity.md</c> step 2e and §3.3:
/// "Entities the engine cannot see have no shell at all… They take handles from
/// the 2048-8191 range, with serials the directory owns, so an
/// <c>!activator</c> or a targetname lookup works uniformly."
///
/// <para>
/// The step's gate is three clauses and each has facts of its own here: the same
/// classnames are still found; an <c>!activator</c> from a shell-less entity
/// resolves; and no handle from the invisible range reaches native code — which
/// in a hermetic test is checked as "the removal batch native is handed never
/// contains one", because the live half of that clause is a native counter.
/// </para>
/// </summary>
public class ShellLessEntityTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);

    private const string Shellless = "test_shellless_relay";
    private const string Sink = "test_shellless_sink";
    private const string Shelled = "test_shelled_prop";

    /// <summary>
    /// This fact's own server engine side (plan_abi_hotreload D7). The
    /// shell-less route is an instance per side, so a fact reads the engine
    /// accessors on the side whose route its registry answers, rather than on
    /// the static <c>Engine</c>, and nothing is shared with any other fact.
    /// </summary>
    private readonly EngineSide _side = EngineSide.Create(Side.Server);

    /// <summary>A registry with this assembly's classes in it, over <see cref="_side"/>'s routes.</summary>
    private ScriptedEntityRegistry Fresh()
    {
        var registry = new ScriptedEntityRegistry(_side.ShellLess, _side.EntityIo, _side.Removal);

        // The engine areas an entity is bound to, over the same side, so an
        // entity's own reads (SpawnFlags, Entities.GetOrigin) reach this route.
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, _side);
        registry.Services = services;

        registry.RegisterAll(typeof(ShellLessEntityTests).Assembly);
        return registry;
    }

    private static TestShellLessRelay Make(ScriptedEntityRegistry registry, out EntityHandle handle,
                                           string? name = null)
    {
        handle = registry.Create(Shellless, Packed(name is null
            ? Array.Empty<(string, string)>()
            : new[] { ("targetname", name) }));

        return (TestShellLessRelay)registry.Get(handle)!;
    }

    /// <summary><c>key\0value\0…</c>, the map spawner's payload.</summary>
    private static byte[] Packed(params (string Key, string Value)[] pairs)
    {
        var bytes = new List<byte>();

        foreach (var (key, value) in pairs)
        {
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(key));
            bytes.Add(0);
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(value));
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    //=========================================================================
    // THE ALLOCATOR — §3.3's "handles from the 2048-8191 range, with serials
    // the directory owns".
    //=========================================================================

    [Fact]
    public void AnAllocatedEntryIsInTheEngineInvisibleRange()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var handle = directory.AllocateInvisible();

        int entry = EntityDirectory.EntryIndex(handle);

        Assert.InRange(entry, EntityDirectory.FirstInvisibleEntry, EntityDirectory.EntryCount - 1);
    }

    [Fact]
    public void TheAllocatorCanNeverProduceEntryZero()
    {
        var directory = new EntityDirectory(new RecordingLog());

        // Entry 0 is worldspawn and a REAL, bindable slot; the whole point of
        // the range is that it starts above every edict. A thousand is more
        // than dm_lockdown's map load takes.
        for (int i = 0; i < 1000; i++)
        {
            var handle = directory.AllocateInvisible();

            Assert.NotEqual(0, EntityDirectory.EntryIndex(handle));
            Assert.NotEqual(default, handle);
        }
    }

    [Fact]
    public void TheAllocatorCanNeverProduceTheInvalidHandle()
    {
        var directory = new EntityDirectory(new RecordingLog());

        for (int i = 0; i < 1000; i++)
            Assert.NotEqual(EntityHandle.None, directory.AllocateInvisible());
    }

    [Fact]
    public void TheFirstEntryHandedOutIsTheTopOfTheRange()
    {
        // THE DIRECTION IS THE COLLISION ARGUMENT, not a style choice. The
        // engine's own free list for this range is built ascending from
        // MAX_EDICTS+1 and handed out from its head, so a stock server-only
        // CBaseEntity gets 2049 and then 2050. Downward from the top is as far
        // from that as the range allows.
        var directory = new EntityDirectory(new RecordingLog());

        Assert.Equal(EntityDirectory.EntryCount - 1,
                     EntityDirectory.EntryIndex(directory.AllocateInvisible()));
    }

    [Fact]
    public void TheAllocatorWalksDownward()
    {
        var directory = new EntityDirectory(new RecordingLog());

        int first = EntityDirectory.EntryIndex(directory.AllocateInvisible());
        int second = EntityDirectory.EntryIndex(directory.AllocateInvisible());

        Assert.Equal(first - 1, second);
    }

    [Fact]
    public void TwoAllocationsNeverShareAnEntry()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var seen = new HashSet<int>();

        for (int i = 0; i < 500; i++)
        {
            var handle = directory.AllocateInvisible();
            directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));

            Assert.True(seen.Add(EntityDirectory.EntryIndex(handle)));
        }
    }

    [Fact]
    public void AFullRangeIsRefusedAndCounted()
    {
        var directory = new EntityDirectory(new RecordingLog());
        int span = EntityDirectory.EntryCount - EntityDirectory.FirstInvisibleEntry;

        for (int i = 0; i < span; i++)
        {
            var handle = directory.AllocateInvisible();
            Assert.True(directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless)));
        }

        Assert.Equal(EntityHandle.None, directory.AllocateInvisible());
        Assert.Equal(1, directory.ShellLessExhausted);
    }

    [Fact]
    public void AReleasedEntryComesBackWithADifferentSerial()
    {
        // CBaseEntityList::RemoveEntityAtSlot bumps the serial for exactly this
        // reason: without it, a handle to the entity that has just gone names
        // whatever takes the entry next.
        //
        // The cursor does not go back, so getting the entry again means going
        // right round the range. That is the honest way to observe it: the
        // allocator deliberately does not hand a just-freed entry straight back
        // out.
        var directory = new EntityDirectory(new RecordingLog());

        var first = directory.AllocateInvisible();
        directory.AttachShellLess(first, new ShellLessEntity(first, Shellless));
        directory.DetachShellLess(first);

        int span = EntityDirectory.EntryCount - EntityDirectory.FirstInvisibleEntry;
        var again = EntityHandle.None;

        for (int i = 0; i < span; i++)
        {
            var handle = directory.AllocateInvisible();

            if (EntityDirectory.EntryIndex(handle) == EntityDirectory.EntryIndex(first))
            {
                again = handle;
                break;
            }

            directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));
        }

        Assert.NotEqual(EntityHandle.None, again);
        Assert.Equal(EntityDirectory.EntryIndex(first), EntityDirectory.EntryIndex(again));
        Assert.NotEqual(first, again);
    }

    [Fact]
    public void ADetachedHandleFindsNoShellLessState()
    {
        var directory = new EntityDirectory(new RecordingLog());

        var first = directory.AllocateInvisible();
        directory.AttachShellLess(first, new ShellLessEntity(first, Shellless));
        directory.DetachShellLess(first);

        var second = directory.AllocateInvisible();
        directory.AttachShellLess(second, new ShellLessEntity(second, Sink));

        Assert.Null(directory.ShellLessAt(first));
        Assert.NotNull(directory.ShellLessAt(second));
    }

    [Fact]
    public void AnEdictEntryIsRefusedShellLessState()
    {
        // An edict entry belongs to an entity the engine can see. Putting
        // shell-less state on one would make every accessor answer out of
        // managed memory for something that has a shell.
        var directory = new EntityDirectory(new RecordingLog());
        var handle = new EntityHandle(64u | (1u << 16));

        Assert.False(directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless)));
    }

    [Fact]
    public void UnbindGivesTheEntryBack()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var handle = directory.AllocateInvisible();

        directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));
        directory.Bind(handle, new TestShellLessSink());

        Assert.Equal(1, directory.ShellLessCount);
        Assert.True(directory.Unbind(handle));
        Assert.Equal(0, directory.ShellLessCount);
    }

    [Fact]
    public void ALevelChangeGivesEveryEntryBack()
    {
        // Clear() does not go through Unbind, so without a sweep of its own a
        // level change leaks every shell-less entry the map used.
        var directory = new EntityDirectory(new RecordingLog());

        for (int i = 0; i < 20; i++)
        {
            var handle = directory.AllocateInvisible();
            directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));
            directory.Bind(handle, new TestShellLessSink());
        }

        directory.Clear();

        Assert.Equal(0, directory.ShellLessCount);
    }

    [Fact]
    public void ALevelChangeAlsoGivesBackAnEntryThatWasNeverBound()
    {
        // The entry a class that failed to build left behind. It is not in the
        // dense list, so a walk of the BOUND entries would leak exactly these.
        var directory = new EntityDirectory(new RecordingLog());
        var handle = directory.AllocateInvisible();

        directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));

        directory.Clear();

        Assert.Equal(0, directory.ShellLessCount);
        Assert.Null(directory.ShellLessAt(handle));
    }

    //=========================================================================
    // KeyValueShellOnly, TRANSLITERATED — the eight keys an entity has because
    // it is an entity rather than because its class declared them.
    //=========================================================================

    [Theory]
    [InlineData("origin")]
    [InlineData("angles")]
    [InlineData("targetname")]
    [InlineData("parentname")]
    [InlineData("parentattachment")]
    [InlineData("spawnflags")]
    [InlineData("health")]
    [InlineData("model")]
    public void EveryKeyTheShellClaimedIsClaimedHere(string key)
    {
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        Assert.True(state.ApplyShellKey(key, "1 2 3"));
    }

    [Fact]
    public void AKeyTheShellNeverClaimedIsNotClaimedHere()
    {
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        Assert.False(state.ApplyShellKey("startdisabled", "1"));
    }

    [Fact]
    public void AShellKeyIsMatchedCaseInsensitively()
    {
        // Q_stricmp, which is what the native arm used.
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        Assert.True(state.ApplyShellKey("SpawnFlags", "7"));
        Assert.Equal(7, state.SpawnFlags);
    }

    [Fact]
    public void OriginTakesThreeNumbers()
    {
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        state.ApplyShellKey("origin", "-3667.48 3439.48 12");

        Assert.Equal(-3667.48f, state.Origin.X, 2);
        Assert.Equal(3439.48f, state.Origin.Y, 2);
        Assert.Equal(12f, state.Origin.Z, 2);
    }

    [Fact]
    public void AnOriginThatIsNotThreeNumbersLeavesTheOldOneAlone()
    {
        // sscanf(...) == 3 is the native rule, and the == 3 is the part that
        // matters: a partly parsed vector is worse than the value it replaced.
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        state.ApplyShellKey("origin", "1 2 3");
        state.ApplyShellKey("origin", "4 5");

        Assert.Equal(3f, state.Origin.Z);
    }

    [Fact]
    public void SpawnflagsParseLikeAtoi()
    {
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        state.ApplyShellKey("spawnflags", "2 trailing rubbish");

        Assert.Equal(2, state.SpawnFlags);
    }

    [Fact]
    public void SpawnflagsThatAreNotANumberReadZero()
    {
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        state.ApplyShellKey("spawnflags", "nonsense");

        Assert.Equal(0, state.SpawnFlags);
    }

    [Fact]
    public void ParentnameIsRecordedRatherThanResolved()
    {
        // The entity it names may not exist yet, and neither end has the
        // position the offset is measured from until Spawn has run.
        var state = new ShellLessEntity(EntityHandle.None, Shellless);

        state.ApplyShellKey("parentname", "the_lift");

        Assert.Equal("the_lift", state.ParentName);
    }

    //=========================================================================
    // CREATION — a class of kind Logical makes no shell, and a class of any
    // other kind is not this route's business.
    //=========================================================================

    [Fact]
    public void ALogicalClassIsShellLess()
    {
        Assert.True(Fresh().ClassIsShellLess(Shellless));
    }

    [Fact]
    public void APropClassIsNotShellLess()
    {
        Assert.False(Fresh().ClassIsShellLess(Shelled));
    }

    [Fact]
    public void AClassNobodyRegisteredIsNotShellLess()
    {
        Assert.False(Fresh().ClassIsShellLess("no_such_class"));
    }

    [Fact]
    public void CreatingAShellLessClassAnswersAnInvisibleHandle()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        Assert.True(EntityDirectory.IsInvisibleEntry(EntityDirectory.EntryIndex(handle)));
    }

    [Fact]
    public void CreatingAShelledClassIsRefusedByThisRoute()
    {
        // Not an error: Engine.CreateEntity asks ClassIsShellLess first and
        // crosses for anything else. A route that answered a handle here would
        // give a prop a managed-only existence.
        Assert.Equal(EntityHandle.None, Fresh().Create(Shelled, default));
    }

    [Fact]
    public void CreationBuildsTheManagedInstance()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        Assert.IsType<TestShellLessRelay>(registry.Get(handle));
    }

    [Fact]
    public void TheCreationPayloadsKeysReachTheShellArm()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, Packed(("origin", "10 20 30"),
                                                       ("spawnflags", "5")));

        var state = registry.StateOf(handle);

        Assert.NotNull(state);
        Assert.Equal(5, state!.SpawnFlags);
        Assert.Equal(30f, state.Origin.Z);
    }

    [Fact]
    public void TheShellKeysAreAppliedBeforeTheInstanceExists()
    {
        // The shell's own order: native created the object, ran
        // KeyValueShellOnly over the map's keys, and only then offered them to
        // managed code -- so an OnSpawn has always been able to read spawnflags
        // and origin off the entity it is about.
        var registry = Fresh();
        var handle = registry.Create(Shellless, Packed(("spawnflags", "9"),
                                                       ("origin", "1 2 3")));

        registry.Spawn(handle);

        var relay = (TestShellLessRelay)registry.Get(handle)!;

        Assert.Equal(9, relay.FlagsAtSpawn);
        Assert.Equal(3f, relay.OriginAtSpawn.Z);
    }

    [Fact]
    public void ASpawnRecordsWhereTheEntityWasBeforeAnythingCouldMoveIt()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, Packed(("origin", "1 2 3")));

        registry.Spawn(handle);
        SideBindings.Of<IEngineEntities>(_side).SetOrigin(handle, new Vector(9f, 9f, 9f));

        Assert.Equal(3f, registry.StateOf(handle)!.SpawnOrigin.Z);
    }

    [Fact]
    public void ASecondSpawnDoesNothing()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        registry.Spawn(handle);
        registry.Spawn(handle);

        Assert.Equal(1, ((TestShellLessRelay)registry.Get(handle)!).SpawnCount);
    }

    [Fact]
    public void ARuntimeKeyReachesBothArms()
    {
        // EntitySetKeyValue's route: the shell arm claims spawnflags AND the
        // key still goes on to the class, exactly as CStandaloneEntity::KeyValue
        // offers every key unconditionally.
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        Assert.True(registry.KeyValue(handle, "spawnflags", "3"));
        Assert.Equal(3, registry.StateOf(handle)!.SpawnFlags);
    }

    [Fact]
    public void UnpackingWalksEveryPair()
    {
        var pairs = ScriptedEntityRegistry.UnpackKeys(
            Packed(("a", "1"), ("b", "2"), ("c", "3")));

        Assert.Equal(3, pairs.Count);
        Assert.Equal(("c", "3"), pairs[2]);
    }

    [Fact]
    public void UnpackingDropsATrailingKeyWithNoValue()
    {
        var payload = new List<byte>(Packed(("a", "1")));
        payload.AddRange(System.Text.Encoding.UTF8.GetBytes("orphan"));
        payload.Add(0);

        Assert.Single(ScriptedEntityRegistry.UnpackKeys(payload.ToArray()));
    }

    //=========================================================================
    // THE GATE, CLAUSE 1 — the same classnames are still found.
    //=========================================================================

    [Fact]
    public void AShellLessEntityIsFoundByItsClassname()
    {
        var registry = Fresh();
        registry.Create(Shellless, default);

        var found = new List<EntityHandle>();

        Assert.Equal(1, registry.FindByClassName(Shellless, found));
    }

    [Fact]
    public void AFindByClassnameDoesNotAnswerAboutADifferentClass()
    {
        var registry = Fresh();
        registry.Create(Sink, default);

        var found = new List<EntityHandle>();

        Assert.Equal(0, registry.FindByClassName(Shellless, found));
    }

    [Fact]
    public void TheWildcardAnswersEveryShellLessEntity()
    {
        // `debug-api ents "*"` is the step's own gate and it asks exactly this.
        var registry = Fresh();
        registry.Create(Shellless, default);
        registry.Create(Sink, default);

        var found = new List<EntityHandle>();

        Assert.Equal(2, registry.FindByClassName("*", found));
    }

    [Fact]
    public void TheCreationPayloadsTargetnameReachesTheDirectorysNameIndex()
    {
        // The index every output resolves its target against. Between
        // MapSpawnCreate and the spawner's own per-key walk the entity would
        // otherwise be unfindable, and a template instantiating its sources
        // never makes that walk at all.
        var registry = Fresh();
        Make(registry, out var handle, "indexed");

        var found = new List<EntityHandle>();
        registry.Directory.FindByName("indexed", found);

        Assert.Equal(new[] { handle }, found);
    }

    [Fact]
    public void AShellLessEntityIsFoundByItsTargetname()
    {
        var registry = Fresh();
        Make(registry, out _, "the_relay");

        var found = new List<EntityHandle>();

        Assert.Equal(1, registry.FindByName("the_relay", found));
    }

    [Fact]
    public void AnUnnamedShellLessEntityIsNotFoundByAWildcardName()
    {
        // A nameless entity is not "every name": stock's FindEntityByName skips
        // an entity whose targetname is empty, and a wildcard that swept them in
        // would make !activator-less outputs address the whole map.
        var registry = Fresh();
        registry.Create(Shellless, default);

        var found = new List<EntityHandle>();

        Assert.Equal(0, registry.FindByName("*", found));
    }

    [Fact]
    public void EngineFindByClassnameAnswersFromTheRoute()
    {
        // The seam `debug-api ents "*"` goes through: with no engine the native
        // walk yields nothing and the shell-less half is the whole answer.
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        Assert.Contains(handle, SideBindings.Of<IEngineEntities>(_side).FindAllByClassname(Shellless));
    }

    [Fact]
    public void EngineFindByNameAnswersFromTheRoute()
    {
        var registry = Fresh();
        Make(registry, out var handle, "findme");

        Assert.Equal(handle, _side.FindEntityByName("findme"));
    }

    //=========================================================================
    // THE GATE, CLAUSE 2 — an !activator from a shell-less entity resolves.
    //=========================================================================

    [Fact]
    public void AnActivatorFromAShellLessEntityResolves()
    {
        var registry = Fresh();

        var cause = Make(registry, out var causeHandle, "cause");
        Make(registry, out var sourceHandle, "src");

        // The source fires at !activator, and the activator is the SHELL-LESS
        // entity that caused the chain. This is the step's own gate clause.
        registry.KeyValue(sourceHandle, Shellless, "OnTrigger", "!activator,Land,,0,-1");

        ((TestShellLessRelay)registry.Get(sourceHandle)!).OnTrigger.Fire(causeHandle);
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land" }, cause.Inputs);
    }

    [Fact]
    public void AShellLessEntityIsTheActivatorItsTargetSees()
    {
        var registry = Fresh();

        Make(registry, out var causeHandle, "cause");
        Make(registry, out var sourceHandle, "src");
        var sink = Make(registry, out _, "sink");

        registry.KeyValue(sourceHandle, Shellless, "OnTrigger", "sink,Land,,0,-1");

        ((TestShellLessRelay)registry.Get(sourceHandle)!).OnTrigger.Fire(causeHandle);
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { causeHandle }, sink.Activators);
    }

    [Fact]
    public void AShellLessEntityIsReachedByNameFromAnotherEntitysOutput()
    {
        var registry = Fresh();

        Make(registry, out var sourceHandle, "src");
        var target = Make(registry, out _, "target");

        registry.KeyValue(sourceHandle, Shellless, "OnTrigger", "target,Land,,0,-1");

        ((TestShellLessRelay)registry.Get(sourceHandle)!).OnTrigger.Fire();
        registry.Directory.ServiceIo(registry.Now);

        Assert.Equal(new[] { "Land" }, target.Inputs);
    }

    //=========================================================================
    // THE GATE, CLAUSE 3 — no handle from the invisible range reaches native.
    //
    // The live half is a NATIVE counter (SourceSharp_ResolveEntity, read back
    // through Engine.InvisibleRangeReach and reported as
    // status.logical.nativeReach), because a managed counter could only count
    // the routes managed code already knows it takes. What is checkable here is
    // the one place a shell-less handle would be handed to native code BY THIS
    // SIDE, deliberately: the removal batch.
    //=========================================================================

    [Fact]
    public void AShellLessRemovalIsNotInTheBatchNativeIsHanded()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var batches = new List<EntityHandle[]>();
        directory.ShellDestroyer = batch => batches.Add(batch.ToArray());

        var handle = directory.AllocateInvisible();
        directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));
        directory.Bind(handle, new TestShellLessSink());

        directory.Remove(handle);
        Assert.Equal(1, directory.FreeRemoved());

        foreach (var batch in batches)
            Assert.DoesNotContain(handle, batch);
    }

    [Fact]
    public void AShelledRemovalIsStillInTheBatch()
    {
        // The control, and it is what stops the fact above passing for a build
        // that simply stopped calling ShellDestroyer.
        var directory = new EntityDirectory(new RecordingLog());
        var batches = new List<EntityHandle[]>();
        directory.ShellDestroyer = batch => batches.Add(batch.ToArray());

        var shelled = new EntityHandle(64u | (1u << 16));
        directory.Bind(shelled, new TestShellLessSink());

        directory.Remove(shelled);
        directory.FreeRemoved();

        Assert.Contains(shelled, batches[0]);
    }

    [Fact]
    public void AMixedBatchKeepsOnlyTheShelledHalf()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var batches = new List<EntityHandle[]>();
        directory.ShellDestroyer = batch => batches.Add(batch.ToArray());

        var shelled = new EntityHandle(64u | (1u << 16));
        directory.Bind(shelled, new TestShellLessSink());

        var invisible = directory.AllocateInvisible();
        directory.AttachShellLess(invisible, new ShellLessEntity(invisible, Shellless));
        directory.Bind(invisible, new TestShellLessSink());

        directory.Remove(shelled);
        directory.Remove(invisible);
        directory.FreeRemoved();

        Assert.Equal(new[] { shelled }, batches[0]);
    }

    [Fact]
    public void AShellLessRemovalStillGivesItsEntryBack()
    {
        var directory = new EntityDirectory(new RecordingLog());

        var handle = directory.AllocateInvisible();
        directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));
        directory.Bind(handle, new TestShellLessSink());

        directory.Remove(handle);
        directory.FreeRemoved();

        Assert.Equal(0, directory.ShellLessCount);
        Assert.Equal(1, directory.ShellLessReleased);
    }

    [Fact]
    public void AShellLessTraceSkipBecomesTheInvalidHandleAndNotEntryZero()
    {
        // A shell-less entity is not in the partition and cannot be hit, so
        // "skip it" and "skip nothing" are the same trace -- but the first
        // spends a handle from the invisible range on a native call. 94 per
        // dm_lockdown run, all emitters excluding themselves.
        //
        // NEVER default: the empty skip is INVALID_EHANDLE_INDEX, and entry
        // zero is worldspawn.
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        Assert.Equal(EntityHandle.None.Value, _side.Skippable(handle));
    }

    [Fact]
    public void AShelledTraceSkipIsPassedThrough()
    {
        var shelled = new EntityHandle(64u | (1u << 16));

        Assert.Equal(shelled.Value, _side.Skippable(shelled));
    }

    //=========================================================================
    // THE ROUTE — the same discipline EntityRemovalRoutes and EntityIoRoutes
    // keep, because the same reload leak applies.
    //=========================================================================

    [Fact]
    public void AnUnboundRouteClaimsNothing()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        Assert.True(_side.ShellLess.Claims(handle));    // the control

        _side.ShellLess.Unbind();

        Assert.False(_side.ShellLess.Claims(handle));
    }

    /// <summary>
    /// The CLIENT side's route does not claim the server's shell-less entity.
    /// It was a side check on one static route; since plan_abi_hotreload D7
    /// each engine side has its own route, and only the server's registry
    /// binds one.
    /// </summary>
    [Fact]
    public void TheRouteRefusesACallOnADifferentSide()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);
        var client = EngineSide.Create(Side.Client);

        Assert.False(client.ShellLess.Claims(handle));
        Assert.True(_side.ShellLess.Claims(handle));    // the control
    }

    [Fact]
    public void TheRouteClaimsAShellLessHandleOnItsOwnSide()
    {
        var registry = Fresh();

        Assert.True(_side.ShellLess.Claims(registry.Create(Shellless, default)));
    }

    [Fact]
    public void TheRouteDoesNotClaimAnEdictHandle()
    {
        Fresh();

        Assert.False(_side.ShellLess.Claims(new EntityHandle(64u | (1u << 16))));
    }

    [Fact]
    public void TheRouteDoesNotClaimTheInvalidHandle()
    {
        Fresh();

        Assert.False(_side.ShellLess.Claims(EntityHandle.None));
    }

    //=========================================================================
    // WHAT THE ENGINE ACCESSORS ANSWER. There is no engine in a test, so the
    // native arm answers its own default -- which is why each of these has a
    // value the default is NOT.
    //=========================================================================

    [Fact]
    public void OriginIsAnsweredFromManagedMemory()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, Packed(("origin", "1 2 3")));

        Assert.Equal(3f, SideBindings.Of<IEngineEntities>(_side).GetOrigin(handle).Z);
    }

    [Fact]
    public void OriginIsWrittenToManagedMemory()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        SideBindings.Of<IEngineEntities>(_side).SetOrigin(handle, new Vector(4f, 5f, 6f));

        Assert.Equal(6f, registry.StateOf(handle)!.Origin.Z);
    }

    [Fact]
    public void ClassnameIsAnsweredFromManagedMemory()
    {
        var registry = Fresh();

        Assert.Equal(Shellless, SideBindings.Of<IEngineEntities>(_side).GetClassName(registry.Create(Shellless, default)));
    }

    [Fact]
    public void NameIsAnsweredFromManagedMemory()
    {
        var registry = Fresh();
        Make(registry, out var handle, "named");

        Assert.Equal("named", SideBindings.Of<IEngineEntities>(_side).GetName(handle));
    }

    /// <summary>
    /// <c>AddOutput "targetname new"</c> renames the entity for EVERY reader.
    /// Stock's <c>CBaseEntity::InputAddOutput</c> hands the pair to the
    /// entity's own <c>KeyValue</c>, which sets <c>m_iName</c>; the managed arm
    /// used to offer it to the registry's managed key path only, which moved the
    /// name index (FindByName found the new name) and left the entity's own name
    /// (GetName) on the old one.
    /// </summary>
    [Fact]
    public void AddOutputTargetnameRenamesTheEntityForGetNameAndFindByNameAlike()
    {
        var registry = Fresh();
        var relay = Make(registry, out var handle, "before");

        Assert.Equal("before", SideBindings.Of<IEngineEntities>(_side).GetName(handle));

        Assert.True(registry.AddOutput(handle, relay, "targetname after"),
                    "a targetname is a key every entity takes, so AddOutput is not refused");

        var found = new List<EntityHandle>();
        registry.FindByName("after", found);

        Assert.Equal(new[] { handle }, found);
        Assert.Equal("after", SideBindings.Of<IEngineEntities>(_side).GetName(handle));
    }

    [Fact]
    public void SpawnflagsAreAnsweredFromManagedMemory()
    {
        var registry = Fresh();

        Assert.Equal(6, SideBindings.Of<IEngineEntities>(_side).GetSpawnFlags(
            registry.Create(Shellless, Packed(("spawnflags", "6")))));
    }

    [Fact]
    public void HealthIsAnsweredFromManagedMemory()
    {
        var registry = Fresh();

        Assert.Equal(42, SideBindings.Of<IEngineEntities>(_side).GetHealth(
            registry.Create(Shellless, Packed(("health", "42")))));
    }

    [Fact]
    public void AShellLessEntityIsValid()
    {
        var registry = Fresh();

        Assert.True(_side.EntityIsValid(registry.Create(Shellless, default)));
    }

    [Fact]
    public void AShellLessEntityHasNoEdictIndex()
    {
        // -1, which is the same answer the shell gave: a Logical class was
        // already server-only and its CStandaloneEntity never claimed an edict.
        var registry = Fresh();

        Assert.Equal(-1, SideBindings.Of<IEngineEntities>(_side).GetIndex(registry.Create(Shellless, default)));
    }

    [Fact]
    public void AShellLessEntityIsNeverAPlayer()
    {
        var registry = Fresh();

        Assert.False(SideBindings.Of<IEngineEntities>(_side).IsPlayer(registry.Create(Shellless, default)));
    }

    [Fact]
    public void TheWorldSpaceCentreOfAPointEntityIsItsOrigin()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, Packed(("origin", "7 8 9")));

        Assert.Equal(9f, SideBindings.Of<IEngineEntities>(_side).GetWorldSpaceCenter(handle).Z);
    }

    //=========================================================================
    // THE HOT RELOAD — the one thing a shell-less entity needs that an entity
    // with a shell does not.
    //
    // Every other reload record is an entity's STATE, put back onto an entity
    // the native world kept standing. A shell-less entity has no native half:
    // the unload DELETES it. The live gate read "395 entities, was 476" before
    // this — 81 lost, exactly dm_lockdown's shell-less count.
    //=========================================================================

    [Fact]
    public void AHotReloadCarriesEveryShellLessEntity()
    {
        var before = Fresh();
        before.Create(Shellless, default);
        before.Create(Sink, default);

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        Assert.Equal(2, after.LiveCount);
    }

    [Fact]
    public void AShellLessEntityComesBackAtTheSameHandle()
    {
        // A reload keeps the native world standing, so every handle anything
        // else holds -- a queued output's target, a parent link -- still names
        // the entity it named before. A fresh entry would leave all of them
        // pointing at nothing.
        var before = Fresh();
        var handle = before.Create(Shellless, default);

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        Assert.NotNull(after.StateOf(handle));
    }

    [Fact]
    public void AShellLessEntitysShellKeysComeBack()
    {
        var before = Fresh();
        var handle = before.Create(Shellless, Packed(("spawnflags", "11"),
                                                     ("origin", "1 2 33")));

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        Assert.Equal(11, after.StateOf(handle)!.SpawnFlags);
        Assert.Equal(33f, after.StateOf(handle)!.Origin.Z);
    }

    [Fact]
    public void AShellLessEntityIsRebuiltAsAnInstance()
    {
        var before = Fresh();
        var handle = before.Create(Shellless, default);

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        Assert.IsType<TestShellLessRelay>(after.Get(handle));
    }

    [Fact]
    public void AShellLessEntityIsStillFoundByNameAfterAReload()
    {
        var before = Fresh();
        Make(before, out var handle, "survivor");

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        var found = new List<EntityHandle>();
        after.Directory.FindByName("survivor", found);

        Assert.Equal(new[] { handle }, found);
    }

    [Fact]
    public void AnActivatorFromAShellLessEntityStillResolvesAfterAReload()
    {
        // The step's own gate clause, taken through the thing that would have
        // broken it.
        var before = Fresh();
        Make(before, out var causeHandle, "cause");
        Make(before, out var sourceHandle, "src");

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        after.KeyValue(sourceHandle, Shellless, "OnTrigger", "!activator,Land,,0,-1");
        ((TestShellLessRelay)after.Get(sourceHandle)!).OnTrigger.Fire(causeHandle);
        after.Directory.ServiceIo(after.Now);

        Assert.Equal(new[] { "Land" },
                     ((TestShellLessRelay)after.Get(causeHandle)!).Inputs);
    }

    [Fact]
    public void ARestoredEntryIsNeverHandedOutAgain()
    {
        // The directory a reload builds adopts handles it did not allocate, and
        // its cursor starts at the top of the range -- where the map's first
        // shell-less entity is.
        var before = Fresh();
        var handle = before.Create(Shellless, default);

        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        Assert.NotEqual(EntityDirectory.EntryIndex(handle),
                        EntityDirectory.EntryIndex(after.Directory.AllocateInvisible()));
    }

    [Fact]
    public void ARestoredEntryAdoptsItsHandlesSerial()
    {
        // Without it the entry's serial counter starts at ZERO in the directory
        // a reload builds, and a later release-and-reuse would hand out a
        // handle EQUAL to one the map's entity had already used -- the
        // use-after-free the serial exists to make impossible.
        //
        // The handle is written by hand rather than allocated, because the
        // allocator walks downward and never hands the same entry out twice in
        // one directory: a serial above zero is exactly what a LATER map load
        // or a reused entry produces and what this has to survive.
        var handle = new EntityHandle(8100u | (7u << 16));

        var before = Fresh();
        before.Directory.AttachShellLess(handle, new ShellLessEntity(handle, Shellless));
        before.SaveForReload(_reload);

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        // Through the real removal path, so the BINDING goes too: DetachShellLess
        // gives the entry back to the allocator but leaves the instance bound,
        // and the allocator skips an entry either table still holds.
        after.Directory.Remove(handle);
        after.Directory.FreeRemoved();

        int span = EntityDirectory.EntryCount - EntityDirectory.FirstInvisibleEntry;
        var again = EntityHandle.None;

        for (int i = 0; i < span; i++)
        {
            var next = after.Directory.AllocateInvisible();

            if (EntityDirectory.EntryIndex(next) == EntityDirectory.EntryIndex(handle))
            {
                again = next;
                break;
            }

            after.Directory.AttachShellLess(next, new ShellLessEntity(next, Shellless));
        }

        Assert.Equal(8u, again.Value >> 16);
    }

    [Fact]
    public void AShellLessEntityWhoseStateWasDroppedStillComesBackNamed()
    {
        // WHY RestoreShellLess SETS THE NAME EVEN THOUGH Apply WOULD.
        //
        // A mutation of that line came back GREEN against every other reload
        // fact here, and it is recorded rather than deleted because the two
        // halves of a reload record are not the same record. Collect() SKIPS an
        // entity whose SaveState threw -- by design, so that one class's bug
        // does not cost the world the other 472 -- and that entity has no
        // SavedEntity for Apply to take a name from. 2e's own record is what
        // still brings it back, and without this line it would come back alive,
        // holding its map keys, and unreachable by every output that names it.
        //
        // Built by hand because a class whose SaveState throws is the thing
        // being modelled and this fact should not need one.
        var state = new ShellLessEntity(new EntityHandle(8000u | (3u << 16)), Shellless)
        {
            Name = "orphan",
        };

        ReloadCarrier.TakeShellLess(_reload);
        ReloadCarrier.PutShellLess(_reload, new[] { state });

        var after = Fresh();
        after.RestoreAfterReload(_reload);

        var found = new List<EntityHandle>();
        after.Directory.FindByName("orphan", found);

        Assert.Equal(new[] { state.Handle }, found);
    }

    [Fact]
    public void TakingTheShellLessStoreEmptiesIt()
    {
        var before = Fresh();
        before.Create(Shellless, default);
        before.SaveForReload(_reload);

        Assert.Equal(1, ReloadCarrier.ShellLessCount(_reload));

        ReloadCarrier.TakeShellLess(_reload);

        Assert.Equal(0, ReloadCarrier.ShellLessCount(_reload));
    }

    [Fact]
    public void AnEntityWithAShellIsNotInTheShellLessRecord()
    {
        // The control: the record is for the entities the unload DELETES, and
        // an entity with a shell is not one of them.
        var registry = Fresh();
        registry.KeyValue(new EntityHandle(64u | (1u << 16)), Shelled, "targetname", "prop");

        Assert.Empty(registry.ShellLessSnapshot());
    }

    //=========================================================================
    // THE READOUT that keeps the safety zero from being vacuous.
    //=========================================================================

    [Fact]
    public void TheLiveCountRisesWithEveryShellLessEntity()
    {
        var registry = Fresh();

        registry.Create(Shellless, default);
        registry.Create(Sink, default);

        Assert.Equal(2, registry.LiveCount);
    }

    [Fact]
    public void TheAllocationTotalsSeparateWhatIsLiveFromWhatHasBeenMade()
    {
        var registry = Fresh();

        var first = registry.Create(Shellless, default);
        registry.Create(Sink, default);

        registry.Directory.Remove(first);
        registry.Directory.FreeRemoved();

        Assert.Equal((2L, 1L), registry.AllocationTotals);
        Assert.Equal(1, registry.LiveCount);
    }

    [Fact]
    public void TheClassCountIsAPropertyOfTheBuildAndNotOfTheMap()
    {
        // Two numbers that a reader will otherwise conflate: the classes that
        // make no shell, and the entities of them that exist right now.
        var registry = Fresh();

        Assert.True(registry.ShellLessClassCount >= 2);
        Assert.Equal(0, registry.LiveCount);
    }

    //=========================================================================
    // A SHELL-LESS ENTITY MAKES A NOISE — plan_entity.md step 8e-sound,
    // closing the refusal step 4b measured and declared.
    //
    // WHAT WAS WRONG. SourceSharp_EntityEmitSound resolved the handle through
    // g_pEntityList so that native code could ask the entity where it is, and a
    // shell-less entity is never in that list by construction. Measured on
    // dm_lockdown, where 81 of 473 live entities are shell-less: three shelled
    // func_doors gave three EmitSound: traces and one shell-less info_target
    // gave zero, while logical.nativeReach moved 24 -> 25 for that one emit.
    //
    // WHY THE COUNTER IS THE SUBJECT HERE. There is no engine in a test, so
    // neither arm of the route can be watched from the outside: the crossing is
    // a null function pointer and the position emit is a no-op. What IS
    // observable is which arm ran, and ShellLessRoutes.SoundsPlacedHere is the
    // managed twin of logical.nativeReach for exactly that — the WORK beside
    // the zero, so that "nothing reached native" cannot be satisfied by
    // "nothing happened".
    //=========================================================================

    [Fact]
    public void AShellLessEntitysSoundIsPlacedOnThisSide()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        long before = _side.ShellLess.SoundsPlacedHere;
        SideBindings.Of<IEngineSound>(_side).EntityEmitSound(handle, "Doors.Move18");

        Assert.Equal(before + 1, _side.ShellLess.SoundsPlacedHere);
    }

    /// <summary>
    /// THE CONTROL, and without it the fact above is "a counter goes up".
    /// </summary>
    /// <remarks>
    /// An edict-range handle is a shelled entity, whose position native code
    /// CAN ask for — so it must take the crossing and leave this counter alone.
    /// A route that claimed every handle would pass the fact above perfectly.
    /// </remarks>
    [Fact]
    public void AShelledEntitysSoundIsNotPlacedOnThisSide()
    {
        Fresh();

        long before = _side.ShellLess.SoundsPlacedHere;
        SideBindings.Of<IEngineSound>(_side).EntityEmitSound(new EntityHandle(64u | (1u << 16)), "Doors.Move18");

        Assert.Equal(before, _side.ShellLess.SoundsPlacedHere);
    }

    /// <summary>
    /// An empty soundscript name is refused before either arm, as it always
    /// was — so the guard cannot be moved below the route by accident.
    /// </summary>
    [Fact]
    public void AnEmptySoundNameTakesNeitherArm()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, default);

        long before = _side.ShellLess.SoundsPlacedHere;
        SideBindings.Of<IEngineSound>(_side).EntityEmitSound(handle, string.Empty);

        Assert.Equal(before, _side.ShellLess.SoundsPlacedHere);
    }

    /// <summary>
    /// <b>The position it is placed at is the entity's OWN</b>, which is the
    /// half that makes the sound come from the right room.
    /// </summary>
    /// <remarks>
    /// Read back through <see cref="EngineSide.EntityGetOrigin"/> rather than off
    /// the state object, because that is the same accessor the emit route uses
    /// and this is the property that has to hold: a route that placed every
    /// shell-less sound at the map origin would satisfy every other fact here.
    /// The live gate asserts the same thing from the other end, by emitting from
    /// one entity twice with the player 144 units away and then 2,190.
    /// </remarks>
    [Fact]
    public void TheShellLessOriginIsWhereTheSoundIsPlaced()
    {
        var registry = Fresh();
        var handle = registry.Create(Shellless, Packed(("origin", "-3657 3583 9")));

        Assert.Equal(new Vector(-3657f, 3583f, 9f), SideBindings.Of<IEngineEntities>(_side).GetOrigin(handle));
    }
}
