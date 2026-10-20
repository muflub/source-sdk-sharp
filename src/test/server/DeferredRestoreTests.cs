using System.Linq;

using SourceSharp.Abi;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <b>A SAVEGAME RESTORE HAS TWO HALVES, AND THE SECOND ONE IS WHY THE PLAYER
/// COMES BACK.</b>
///
/// <para>
/// <c>Restore</c> runs inside <c>engine-&gt;LoadGameState</c>, which runs inside
/// <c>LevelInit</c> — before <c>ServerActivate</c> and before any client is
/// active. Two whole groups of entity cannot exist at that instant:
/// </para>
///
/// <list type="number">
/// <item>the entities the managed level init creates in <c>ServerActivate</c>
/// (10 <c>ss_pickup</c> and 9 <c>ss_beam</c> on <c>dm_lockdown</c>, measured);
/// </item>
/// <item><b>the player and its kit</b> — <c>ss_player</c>,
/// <c>ss_viewmodel</c>, <c>ss_weapon</c> — because the edict they bind to
/// belongs to a client, and no client is active yet.</item>
/// </list>
///
/// <para>
/// <b>THE ENGINE IS WHAT MAKES THE SECOND GROUP UNAVOIDABLE.</b> On a
/// load-game the engine does not call <c>ClientPutInServer</c>; it calls
/// <c>ClientActive</c> with <c>bLoadGame</c> true. Stock can afford that because its own <c>Restore</c> builds
/// the player out of the save's entity table
/// (<c>CBasePlayer::CreatePlayer</c>, <c>saverestore.cpp:2599</c>); this port
/// writes no entity table, so the player is created at client-active — long
/// after the first matching pass is over.
/// </para>
///
/// <para>
/// So the first pass HOLDS what it could not place and a second pass at
/// client-active matches it again. These are the facts that hold that
/// arrangement to its two dangerous edges: a record that should have been
/// placed immediately must not be deferred, and a deferred record must not be
/// replayed twice or over a world that has moved on.
/// </para>
/// </summary>
public class DeferredRestoreTests
{
    private static ScriptedEntityRegistry Registry()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(DeferredRestoreTests).Assembly);
        registry.Services = Services();
        return registry;
    }

    /// <summary>
    /// The same registry, with the engine double handed back so a fact can stub
    /// what the native side answers and read what it was told to do.
    ///
    /// <para>
    /// This is the seam the native section is tested through, and it is the
    /// real one: <c>EnsureInstance</c> binds <c>Resolver.Require&lt;IEngineEntities&gt;()</c>
    /// onto every instance it builds, so an entity in one of these facts is
    /// talking to this object through the same field it talks to the engine
    /// through in a game.
    /// </para>
    /// </summary>
    private static ScriptedEntityRegistry Registry(out RecordingEngine engine)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(DeferredRestoreTests).Assembly);
        registry.Services = Services(out engine);
        return registry;
    }

    /// <summary>One record, carrying a value and an index, as a save wrote it.</summary>
    private static List<SavedEntity> Record(string className, int index, float speed,
                                            string name = "")
    {
        var registry = Registry();
        var handle = new EntityHandle(0x0004_0011u);

        Assert.True(registry.KeyValue(handle, className, "speed",
                                      speed.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var records = new List<SavedEntity>();

        foreach (SavedEntity e in registry.Collect())
        {
            var copy = new SavedEntity(e.Handle, e.ClassName, name, index);

            foreach (var (key, v) in e.Vars)
                copy.SaveVar(key, v.Value, v.LastSetAt);
            foreach (var (field, value) in e.Net)
                copy.SaveNet(field, value);
            foreach (var (n, o) in e.Outputs)
                copy.SaveOutput(n, o.FireCount, o.LastValue);
            foreach (var (key, value) in e.Local)
                copy.Set(key, value);

            records.Add(copy);
        }

        return records;
    }

    private static List<ScriptedEntityRegistry.LiveEntity> World(EntityHandle handle, int index,
                                                                string name = "")
        => new() { new ScriptedEntityRegistry.LiveEntity(handle, index, name) };

    private static List<ScriptedEntityRegistry.LiveEntity> Nowhere()
        => new();

    //-------------------------------------------------------------------------
    // THE POSITIVE CASE COMES FIRST, and it is not decoration. Every fact below
    // it is about a record NOT being placed, and a registry that placed nothing
    // ever would satisfy all of them. This is the one that says the input would
    // otherwise be handled.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A record whose entity is already standing is applied on the spot and is
    /// NOT put on the wait list.
    /// </summary>
    [Fact]
    public void ARecordThatMatchesIsAppliedImmediatelyAndNotHeld()
    {
        var loaded = Registry();
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.RestoreFromSave(Record("reload_fixture", 7, 512.5f),
                                               World(live, 7)));

        Assert.Equal(0, loaded.DeferredRestoreCount);
        Assert.Equal(512.5f, ((ReloadFixtureEntity)loaded.Get(live)!).Vars.Speed);
    }

    /// <summary>
    /// And a record whose entity does not exist YET is held rather than
    /// dropped. This is the player's record at <c>Restore</c> time, exactly.
    /// </summary>
    [Fact]
    public void ARecordThatMatchesNothingIsHeldRatherThanDropped()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 7, 512.5f), Nowhere()));

        Assert.Equal(1, loaded.DeferredRestoreCount);
    }

    /// <summary>
    /// <b>THE ONE THIS WHOLE CHANGE IS FOR.</b> The world at <c>Restore</c>
    /// time has no player in it; the player is created at client-active; the
    /// held record then finds it and its state comes back.
    /// </summary>
    [Fact]
    public void TheHeldRecordIsPlacedOnceItsEntityFinallyExists()
    {
        var loaded = Registry();

        // Restore: nothing to match. The player does not exist yet and cannot.
        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 1, 512.5f), Nowhere()));

        // Client-active: the native shell has created the player on edict 1.
        var player = new EntityHandle(0x0005_0001u);
        Assert.True(loaded.KeyValue(player, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.ApplyDeferredRestore(World(player, 1)));
        Assert.Equal(512.5f, ((ReloadFixtureEntity)loaded.Get(player)!).Vars.Speed);
    }

    /// <summary>
    /// The pass runs ONCE. A second client activating must not replay one
    /// save's records over a world that has moved on since.
    /// </summary>
    [Fact]
    public void TheDeferredPassEmptiesTheWaitListAndDoesNotRunTwice()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 1, 512.5f), Nowhere()));

        var player = new EntityHandle(0x0005_0001u);
        Assert.True(loaded.KeyValue(player, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.ApplyDeferredRestore(World(player, 1)));
        Assert.Equal(0, loaded.DeferredRestoreCount);

        // The second pass has nothing to do — and, crucially, does not undo the
        // player's own play since the load by laying the file over it again.
        ((ReloadFixtureEntity)loaded.Get(player)!).Vars.Speed = 3f;

        Assert.Equal(0, loaded.ApplyDeferredRestore(World(player, 1)));
        Assert.Equal(3f, ((ReloadFixtureEntity)loaded.Get(player)!).Vars.Speed);
    }

    /// <summary>
    /// A held record that STILL matches nothing is dropped for good, and the
    /// wait list is emptied whether or not it placed.
    /// </summary>
    [Fact]
    public void AHeldRecordThatStillMatchesNothingIsDroppedForGood()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 1, 512.5f), Nowhere()));
        Assert.Equal(0, loaded.ApplyDeferredRestore(Nowhere()));
        Assert.Equal(0, loaded.DeferredRestoreCount);
    }

    /// <summary>
    /// Nothing held is nothing done — the ordinary case, since the pass is
    /// driven on every savegame client-active whether or not the first pass
    /// left anything behind.
    /// </summary>
    [Fact]
    public void NothingHeldMeansNothingPlaced()
    {
        var loaded = Registry();
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "reload_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(Record("reload_fixture", 7, 512.5f),
                                               World(live, 7)));

        Assert.Equal(0, loaded.ApplyDeferredRestore(World(live, 7)));
    }

    /// <summary>
    /// <b>THE CLASSNAME GUARD IS NOT SKIPPED BY THE SECOND PASS.</b> Deferring
    /// a record delays the match; it must not weaken it. An index that now
    /// holds a different class is the silent-corruption case the whole format
    /// is arranged against, and it is MORE likely here than on the first pass —
    /// the player's kit is created after the map, so its indices are the ones
    /// most likely to have been handed to something else.
    /// </summary>
    [Fact]
    public void TheClassnameGuardStillRefusesOnTheDeferredPass()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 1, 512.5f), Nowhere()));

        // The index came back, but a different class is standing on it.
        var other = new EntityHandle(0x0005_0001u);
        Assert.True(loaded.KeyValue(other, "reload_forgetful", "speed", "1"));

        Assert.Equal(0, loaded.ApplyDeferredRestore(World(other, 1)));
        Assert.Equal(1f, ((ReloadForgetfulEntity)loaded.Get(other)!).Vars.Speed);
    }

    /// <summary>
    /// A new restore replaces the wait list. Records held from a previous load
    /// belong to a world that is gone, and letting them survive into the next
    /// one is the same mis-application by a slower route.
    /// </summary>
    [Fact]
    public void ANewRestoreThrowsAwayTheRecordsTheOldOneWasHolding()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 1, 512.5f), Nowhere()));
        Assert.Equal(1, loaded.DeferredRestoreCount);

        // A second load, whose records all place.
        var live = new EntityHandle(0x0005_0011u);
        Assert.True(loaded.KeyValue(live, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.RestoreFromSave(Record("reload_fixture", 7, 64f),
                                               World(live, 7)));

        Assert.Equal(0, loaded.DeferredRestoreCount);
    }

    /// <summary>
    /// And <see cref="ScriptedEntityRegistry.Clear"/> takes the wait list with
    /// the world it was matched against.
    /// </summary>
    [Fact]
    public void ClearingTheRegistryClearsTheWaitList()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", 1, 512.5f), Nowhere()));
        Assert.Equal(1, loaded.DeferredRestoreCount);

        loaded.Clear();

        Assert.Equal(0, loaded.DeferredRestoreCount);
    }

    /// <summary>
    /// The targetname fallback works on the deferred pass too — which matters
    /// because the entities that have NO index at all are the same
    /// server-only ones the first pass could already reach, and a second walk
    /// that quietly lost the fallback would drop them without a number
    /// changing.
    /// </summary>
    [Fact]
    public void TheTargetnameFallbackSurvivesTheDeferral()
    {
        var loaded = Registry();

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", -1, 512.5f, "antspawn"),
                                               Nowhere()));

        var late = new EntityHandle(0x0005_0044u);
        Assert.True(loaded.KeyValue(late, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.ApplyDeferredRestore(World(late, -1, "antspawn")));
        Assert.Equal(512.5f, ((ReloadFixtureEntity)loaded.Get(late)!).Vars.Speed);
    }

    /// <summary>
    /// <b>AND THE SECOND PASS DOES NOT LOWER THE BAR.</b> A record with NEITHER
    /// key is held by the first pass — it matched nothing, which is all the
    /// first pass can tell — and then dropped by the second, with the entity
    /// that produced it standing right there both times.
    ///
    /// <para>
    /// This is <c>docs/managed-saverestore.md</c> §7e.3's 57 records, and them
    /// going nowhere is the intended outcome rather than the gap it was filed
    /// as. Measured out of the bytes of four independent <c>.sav</c> files, the
    /// 57 are identical in every store but one output tally: what they hold is
    /// what the map respawns. <see cref="SaveRoundTripTests"/> carries the
    /// first-pass half and the composition; this is the one that says the
    /// deferral did not quietly become a third, looser key.
    /// </para>
    /// </summary>
    [Fact]
    public void ARecordWithNeitherKeyIsPlacedOnNothingByTheDeferredPassEither()
    {
        // THE POSITIVE CASE FIRST, on the world the refusal below is refused
        // against. TheTargetnameFallbackSurvivesTheDeferral makes the same
        // claim standing alone; it is made again here because without it this
        // fact is satisfied by a deferred pass that places nothing at all.
        var loaded = Registry();
        var live = new EntityHandle(0x0005_0044u);

        Assert.True(loaded.KeyValue(live, "reload_fixture", "speed", "1"));

        Assert.Equal(0, loaded.RestoreFromSave(Record("reload_fixture", -1, 512.5f, "antspawn"),
                                               Nowhere()));
        Assert.Equal(1, loaded.ApplyDeferredRestore(World(live, -1, "antspawn")));
        Assert.Equal(512.5f, ((ReloadFixtureEntity)loaded.Get(live)!).Vars.Speed);

        // The same record with no name on it. Held, because the first pass
        // cannot tell "not here yet" from "has no key" -- and then dropped,
        // because the second pass has no key to match it by either.
        var keyless = Registry();
        var standing = new EntityHandle(0x0005_0044u);

        Assert.True(keyless.KeyValue(standing, "reload_fixture", "speed", "1"));

        Assert.Equal(0, keyless.RestoreFromSave(Record("reload_fixture", -1, 512.5f),
                                                Nowhere()));
        Assert.Equal(1, keyless.DeferredRestoreCount);

        Assert.Equal(0, keyless.ApplyDeferredRestore(World(standing, -1)));
        Assert.Equal(0, keyless.DeferredRestoreCount);
        Assert.Equal(1f, ((ReloadFixtureEntity)keyless.Get(standing)!).Vars.Speed);
    }

    //-------------------------------------------------------------------------
    // The NATIVE section, which is what version 4 added.
    //
    // A record used to carry the MANAGED half of an entity and nothing else --
    // not merely no origin: CShellGameDll::Save makes one call, which writes
    // the managed block, WriteSaveHeaders writes nothing and the file has no
    // ETABLE. So the player came back with its [NetState] and not where it was
    // standing, and tools/save-live-gate's damaged func_breakable_surf came
    // back at full health.
    //
    // The facts below are driven through the RecordingEngine the registry binds
    // onto every instance, which is the same field an entity uses in a game.
    //-------------------------------------------------------------------------

    /// <summary>The saved box, whose corners are DIFFERENT so a swap is visible.</summary>
    internal static readonly Vector SavedMins = new(-13f, -14f, 0f);

    /// <summary>Its other corner. See <see cref="SavedMins"/>.</summary>
    internal static readonly Vector SavedMaxs = new(13f, 14f, 71f);

    /// <summary>
    /// The saved think schedule, as the DELAY the getter answers and the setter
    /// takes. Not a round number, and not one of the sentinels: a 0 would be
    /// indistinguishable from an overdue think and a -1 from no think at all.
    /// </summary>
    internal const float SavedThinkDelay = 2.5f;

    /// <summary>
    /// The saved gravity scale. NOT 1, which is the value that means "no
    /// change" and would be restored correctly by a build that restored
    /// nothing.
    /// </summary>
    internal const float SavedGravity = 0.25f;

    /// <summary>
    /// The saved spawnflags. Several bits, so a build that wrote a bool or a
    /// bit count rather than the value would not pass.
    /// </summary>
    internal const int SavedSpawnFlags = 0x2005;

    /// <summary>What a native side would answer, so a record can be collected from one.</summary>
    private static RecordingEngine WithNativeReads(RecordingEngine engine,
                                                   Vector origin, int health)
        => engine.Returns("Entities.GetOrigin", origin)
                 .Returns("Entities.GetAngles", new Vector(0f, 90f, 0f))
                 .Returns("Entities.GetVelocity", new Vector(0f, 0f, -32f))
                 .Returns("Entities.GetAngularVelocity", Vector.Zero)
                 .Returns("Entities.GetHealth", health)
                 .Returns("Entities.GetArmor", 0)
                 // The seven version-4 added as KEYS, with no format bump.
                 .Returns("Entities.GetSpawnFlags", SavedSpawnFlags)
                 .Returns("Entities.GetBounds", (Mins: SavedMins, Maxs: SavedMaxs))
                 .Returns("Entities.GetSolidType", SolidType.VPhysics)
                 .Returns("Entities.GetSolidFlags", SolidFlags.Trigger | SolidFlags.NotSolid)
                 .Returns("Entities.GetGravity", SavedGravity);

    /// <summary>One record off a registry that HAS a native side under it.</summary>
    private static List<SavedEntity> NativeRecord(int index, Vector origin, int health,
                                                  float speed = 512.5f)
    {
        var registry = Registry(out RecordingEngine engine);
        WithNativeReads(engine, origin, health);

        var handle = new EntityHandle(0x0004_0011u);

        Assert.True(registry.KeyValue(handle, "native_fixture", "speed",
                                      speed.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // THE SCHEDULE IS MANAGED NOW (plan_entity.md §3.4, step 1g), so the
        // only way a collected record carries a think delay is that the entity
        // actually booked one. It used to come from the engine double, because
        // SaveState read it back over an ABI slot that no longer exists.
        ((NativeFixtureEntity)registry.Get(handle)!).BookThink(SavedThinkDelay);

        var records = new List<SavedEntity>();

        foreach (SavedEntity e in registry.Collect())
        {
            var copy = new SavedEntity(e.Handle, e.ClassName, "", index);

            foreach (var (key, v) in e.Vars)
                copy.SaveVar(key, v.Value, v.LastSetAt);
            foreach (var (field, value) in e.Net)
                copy.SaveNet(field, value);
            foreach (var (n, o) in e.Outputs)
                copy.SaveOutput(n, o.FireCount, o.LastValue);
            foreach (var (key, value) in e.Local)
                copy.Set(key, value);
            foreach (var (field, value) in e.Native)
                copy.SaveNative(field, value);

            records.Add(copy);
        }

        return records;
    }

    /// <summary>
    /// A float, to the precision a coordinate round-trips at through the
    /// record's decimal text. Written out rather than reached for from xUnit so
    /// the tolerance is one number a reader can see.
    /// </summary>
    private static void Near(float expected, float actual)
        => Assert.True(MathF.Abs(expected - actual) < 0.01f,
                       $"expected about {expected}, got {actual}");

    /// <summary>Every write the entity made, in the order it made them.</summary>
    private static List<string> Wrote(RecordingEngine engine)
        => engine.Calls.Select(c => c.Member)
                       .Where(m => m.StartsWith("Entities.Set", StringComparison.Ordinal)
                                   || m == "Entities.Teleport")
                       .ToList();

    /// <summary>
    /// <b>THE POSITIVE CASE, AND IT COMES FIRST.</b> Collect reads the native
    /// side and the record carries it — so every fact below about a native
    /// section NOT being applied is about a record that would otherwise have
    /// one.
    /// </summary>
    [Fact]
    public void CollectReadsTheNativeSideIntoTheRecord()
    {
        SavedEntity record = Assert.Single(NativeRecord(1, new Vector(-3194.96f, 3768f, 1.92f), 4));

        Assert.Equal(13, record.Native.Count);
        Assert.Equal(4, record.NativeInt(NativeField.Health));
        Near(-3194.96f, record.NativeVector(NativeField.Origin).X);
    }

    /// <summary>
    /// <b>THE ONE THIS ITEM EXISTS FOR.</b> The record is held at
    /// <c>Restore</c> time, the player is spawned at client-active, and the
    /// deferred pass puts it back WHERE IT WAS — not where the spawn point is.
    /// </summary>
    [Fact]
    public void TheHeldRecordsNativeStateIsAppliedOnTheDeferredPass()
    {
        var saved = NativeRecord(1, new Vector(-3194.96f, 3768f, 1.92f), 4);

        var loaded = Registry(out RecordingEngine engine);
        Assert.Equal(0, loaded.RestoreFromSave(saved, Nowhere()));

        // Client-active: the shell has built the player, at a spawn point.
        var player = new EntityHandle(0x0005_0001u);
        Assert.True(loaded.KeyValue(player, "native_fixture", "speed", "1"));

        Assert.Equal(1, loaded.ApplyDeferredRestore(World(player, 1)));

        // A TELEPORT, after the fixture's own LoadState has moved it with a
        // SetOrigin -- which is the point: the overlay has to be what the
        // engine is left holding.
        EngineCall placed = engine.Calls.Last(c => c.Member == "Entities.Teleport");

        Assert.Equal(player.Value, placed.ArgAs<EntityHandle>(0).Value);
        Near(-3194.96f, placed.ArgAs<Vector>(1).X);
        Assert.Equal(4, Assert.Single(engine.Calls, c => c.Member == "Entities.SetHealth")
                                .ArgAs<int>(1));
    }

    /// <summary>
    /// <b>THE SAVED VALUE WINS OVER WHATEVER THE ENTITY'S OWN LoadState DID.</b>
    /// A savegame load in this port is a <c>map</c> plus this overlay, so the
    /// spawn has already placed the entity and the class may have moved it
    /// again; an origin written into a record and then overwritten is the exact
    /// bug this item is about, reappearing one layer down.
    /// </summary>
    [Fact]
    public void TheSavedNativeStateIsAppliedAfterTheEntitysOwnLoadState()
    {
        var saved = NativeRecord(7, new Vector(-3194.96f, 3768f, 1.92f), 4);

        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(saved, World(live, 7)));

        // NativeFixtureEntity.LoadState moves itself to (99, 99, 99) -- which
        // stands in for the spawn and for any class that positions itself. The
        // overlay has to come after it, and the LAST write is the one the
        // engine ends up holding.
        List<string> wrote = Wrote(engine);

        Assert.Equal("Entities.SetOrigin", wrote[0]);   // the class's own move

        EngineCall last = engine.Calls.Last(c => c.Member == "Entities.Teleport");

        Near(-3194.96f, last.ArgAs<Vector>(1).X);
        Assert.NotEqual(99f, last.ArgAs<Vector>(1).X);
    }

    /// <summary>
    /// And the order WITHIN the overlay is the written-down one: the transform,
    /// then the motion expressed in its frame, then health — which can be
    /// changed by everything above it, since putting an entity back can drop it
    /// into the trigger_hurt it was standing in.
    /// </summary>
    [Fact]
    public void TheNativeOverlayAppliesTheTransformBeforeTheMotionAndHealthLast()
    {
        var saved = NativeRecord(7, new Vector(1f, 2f, 3f), 4);

        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(saved, World(live, 7)));

        // The fixture's own LoadState move is first and is not the overlay.
        List<string> overlay = Wrote(engine).Skip(1).ToList();

        Assert.Equal(new[]
        {
            "Entities.SetSpawnFlags",
            "Entities.SetBounds",
            "Entities.SetSolidity",
            "Entities.Teleport",
            "Entities.SetVelocity",
            "Entities.SetAngularVelocity",
            "Entities.SetGravity",
            "Entities.SetHealth",
            "Entities.SetArmor",
        }, overlay);
    }

    /// <summary>
    /// <b>THE SAVED POSE IS PUT BACK WITH A TELEPORT</b>, which moves the
    /// physics object too, and not with SetOrigin and SetAngles, which move
    /// only the entity. A load in this port is a fresh <c>map</c> plus this
    /// overlay, so a prop's physics object is standing at its SPAWN pose: set
    /// the entity alone and the next physics update drags it back there, and
    /// the saved pose is lost. Stock never had the problem, because its restore
    /// puts the physics object back from the save (<c>CPhysicsProp</c>'s
    /// vphysics block). Measured live: the map's crates dropped from their
    /// spawn heights again after a load, and one put back under a shelf was
    /// ejected into it hard enough to break.
    /// </summary>
    [Fact]
    public void TheSavedPoseIsPutBackWithATeleportThatCarriesTheAngles()
    {
        var saved = NativeRecord(7, new Vector(-3777f, 5828f, 20.6f), 4);

        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(saved, World(live, 7)));

        EngineCall teleport = Assert.Single(engine.Calls, c => c.Member == "Entities.Teleport");

        Assert.Equal(live.Value, teleport.ArgAs<EntityHandle>(0).Value);
        Near(-3777f, teleport.ArgAs<Vector>(1).X);
        Near(20.6f, teleport.ArgAs<Vector>(1).Z);
        Near(90f, teleport.ArgAs<Vector?>(2)!.Value.Y);

        // NOT ZEROED: the record's own velocity is written right after it.
        Assert.False(teleport.ArgAs<bool>(3));

        // And the pose is not ALSO written the entity-only way: the one
        // SetOrigin is the fixture's own LoadState move.
        Assert.Equal(99f, Assert.Single(engine.Calls, c => c.Member == "Entities.SetOrigin")
                                .ArgAs<Vector>(1).X);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetAngles");
    }

    /// <summary>
    /// Angles saved WITHOUT an origin still go back, on their own: there is no
    /// position to teleport to, and the spawn's must stand.
    /// </summary>
    [Fact]
    public void SavedAnglesWithoutAnOriginAreSetOnTheirOwn()
    {
        var saved = NativeRecord(7, new Vector(1f, 2f, 3f), 4);

        var trimmed = new SavedEntity(saved[0].Handle, saved[0].ClassName, "", 7);
        trimmed.SaveNative(NativeField.Angles, saved[0].NativeVector(NativeField.Angles));

        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave([trimmed], World(live, 7)));

        Near(90f, Assert.Single(engine.Calls, c => c.Member == "Entities.SetAngles")
                        .ArgAs<Vector>(1).Y);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Teleport");
    }

    /// <summary>
    /// <b>A RELOAD DOES NOT PUT THE NATIVE HALF BACK</b>, which is the same
    /// split <see cref="SavedEntity.Index"/> makes. A reload keeps the native
    /// world standing, so there is nothing to restore — and the values were
    /// read before the unload, so writing them back could only be a no-op or
    /// STALE, teleporting a rolling barrel back to where it was.
    /// </summary>
    [Fact]
    public void AReloadDoesNotPutTheNativeHalfBack()
    {
        var saved = NativeRecord(7, new Vector(-3194.96f, 3768f, 1.92f), 4);

        var loaded = Registry(out RecordingEngine engine);

        // The reload path matches on the HANDLE, which is right for it: the
        // entity never went away.
        Assert.Equal(1, loaded.RestoreFrom(saved));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetHealth");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetAngles");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.Teleport");

        // ...and the only SetOrigin is the fixture's own LoadState move, which
        // is the vacuity guard: without it this fact would pass against a
        // registry that never restored anything at all.
        EngineCall moved = Assert.Single(engine.Calls, c => c.Member == "Entities.SetOrigin");

        Assert.Equal(99f, moved.ArgAs<Vector>(1).X);
    }

    /// <summary>
    /// A field the record does not carry is LEFT ALONE rather than defaulted. A
    /// save from a build that did not know about a field must leave the spawn's
    /// value standing; replacing it with a zero would put every entity at the
    /// world origin on the day somebody adds one.
    /// </summary>
    [Fact]
    public void ANativeFieldTheRecordDoesNotCarryIsNotWritten()
    {
        var saved = NativeRecord(7, new Vector(1f, 2f, 3f), 4);

        // One field, as an older save would have carried it.
        var trimmed = new SavedEntity(saved[0].Handle, saved[0].ClassName, "", 7);
        trimmed.SaveNative(NativeField.Health, 4);

        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave([trimmed], World(live, 7)));

        Assert.Equal(4, Assert.Single(engine.Calls, c => c.Member == "Entities.SetHealth")
                                .ArgAs<int>(1));
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetAngles");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetVelocity");
    }

    /// <summary>
    /// An entity with no native side under it records NOTHING rather than
    /// zeroes — because a zero origin is a real coordinate, the middle of the
    /// map, and a record carrying it would move every entity there on restore.
    /// </summary>
    [Fact]
    public void AnEntityWithNoNativeSideRecordsNoNativeSectionAtAll()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0012u),
                                      "reload_fixture", "speed", "1"));

        Assert.Empty(Assert.Single(registry.Collect()).Native);
    }

    //-------------------------------------------------------------------------
    // THE SEVEN KEYS THAT COST NO FORMAT VERSION.
    //
    // Bounds, solidity, spawnflags, think time and gravity were each named on
    // NativeField as readable and not writable, or writable and not readable,
    // through the managed ABI. Six appended ABI slots later they are neither,
    // and they arrive as KEYS in the existing store -- which is the property
    // SavedEntity.Native is a dictionary FOR, exercised rather than claimed.
    //
    // Every fact below is driven through the same RecordingEngine the registry
    // binds onto the instance, so the reads, the writes and the ORDER are
    // measured rather than described.
    //-------------------------------------------------------------------------

    /// <summary>Every write the entity made, with its arguments, in order.</summary>
    private static EngineCall Wrote(RecordingEngine engine, string member)
        => engine.Calls.Last(c => c.Member == member);

    /// <summary>
    /// <b>THE POSITIVE CASE FOR THE SEVEN, AND IT COMES FIRST.</b> Collect
    /// reads each of them off the native side and the record carries it — so
    /// every fact below about one NOT being applied is about a record that
    /// would otherwise have carried it.
    /// </summary>
    [Fact]
    public void CollectReadsTheSevenNewNativeFieldsIntoTheRecord()
    {
        SavedEntity record = Assert.Single(NativeRecord(1, new Vector(-3194.96f, 3768f, 1.92f), 4));

        Assert.Equal(SavedSpawnFlags, record.NativeInt(NativeField.SpawnFlags));
        Assert.Equal(SavedMins, record.NativeVector(NativeField.Mins));
        Assert.Equal(SavedMaxs, record.NativeVector(NativeField.Maxs));
        Assert.Equal((int)SolidType.VPhysics, record.NativeInt(NativeField.SolidType));
        Assert.Equal((int)(SolidFlags.Trigger | SolidFlags.NotSolid),
                     record.NativeInt(NativeField.SolidFlags));
        Near(SavedGravity, record.NativeFloat(NativeField.Gravity));
        Near(SavedThinkDelay, record.NativeFloat(NativeField.NextThink));
    }

    /// <summary>
    /// <b>AND THE FORMAT VERSION DID NOT MOVE.</b> The point of the store being
    /// a dictionary was that a new native field is a new KEY, not a new layout
    /// — so seven of them arriving at once must not have cost a version. This
    /// asserts the stamp, beside the facts that assert the seven are really
    /// there, because "no bump" is only a claim worth making about a change
    /// that actually added fields.
    /// </summary>
    [Fact]
    public void TheSevenNewKeysCostNoFormatVersion()
    {
        Assert.Equal(4, SavedEntityCodec.Version);
        Assert.Equal(13, Assert.Single(NativeRecord(1, Vector.Zero, 4)).Native.Count);
    }

    /// <summary>
    /// The spawnflags are put back, and BEFORE anything physical happens to the
    /// entity — a class's own logic reads them, and it should read the saved
    /// ones rather than the map lump's.
    /// </summary>
    [Fact]
    public void TheSpawnFlagsAreRestoredAndAreTheFirstThingWritten()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(NativeRecord(7, new Vector(1f, 2f, 3f), 4),
                                               World(live, 7)));

        Assert.Equal(SavedSpawnFlags, Wrote(engine, "Entities.SetSpawnFlags").ArgAs<int>(1));

        // The overlay's first write, past the fixture's own LoadState move.
        Assert.Equal("Entities.SetSpawnFlags", Wrote(engine).Skip(1).First());
    }

    /// <summary>
    /// The box comes back as the box that was saved — BOTH corners, and not
    /// swapped. A size would not have been enough: most models put their origin
    /// on the base, so <see cref="SavedMins"/> and <see cref="SavedMaxs"/> are
    /// deliberately not each other's negation.
    /// </summary>
    [Fact]
    public void TheCollisionBoxIsRestoredWithBothCornersTheRightWayRound()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(NativeRecord(7, new Vector(1f, 2f, 3f), 4),
                                               World(live, 7)));

        EngineCall set = Wrote(engine, "Entities.SetBounds");

        Assert.Equal(SavedMins, set.ArgAs<Vector>(1));
        Assert.Equal(SavedMaxs, set.ArgAs<Vector>(2));
    }

    /// <summary>
    /// <b>A HALF-CARRIED BOX IS NOT A SMALLER RESTORE, IT IS A SHAPE NOBODY
    /// SAVED</b> — so a record with one corner and not the other leaves the
    /// spawn's box alone rather than inventing the missing half.
    ///
    /// <para>
    /// The fact above is what makes this one mean anything: it says the same
    /// input WITH both corners does write the box.
    /// </para>
    /// </summary>
    [Fact]
    public void ABoxMissingOneCornerIsNotAppliedAtAll()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        SavedEntity full = NativeRecord(7, new Vector(1f, 2f, 3f), 4)[0];
        var half = new SavedEntity(full.Handle, full.ClassName, "", 7);

        foreach (var (field, value) in full.Native)
        {
            if (field != NativeField.Maxs)
                half.SaveNative(field, value);
        }

        // The needle is really gone, and only it.
        Assert.True(half.HasNative(NativeField.Mins));
        Assert.False(half.HasNative(NativeField.Maxs));

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave([half], World(live, 7)));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetBounds");

        // ...and the rest of the section still ran, so this is a fact about the
        // pair and not about a restore that gave up.
        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetSolidity");
    }

    /// <summary>
    /// Solidity is put back as ONE call carrying both halves — the type and the
    /// flags — because the shell decides which spatial-partition lists an
    /// entity belongs in by reading the pair together.
    /// </summary>
    [Fact]
    public void SolidityIsRestoredAsOneCallCarryingBothHalves()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(NativeRecord(7, new Vector(1f, 2f, 3f), 4),
                                               World(live, 7)));

        EngineCall set = Assert.Single(engine.Calls, c => c.Member == "Entities.SetSolidity");

        Assert.Equal(SolidType.VPhysics, set.ArgAs<SolidType>(1));
        Assert.Equal(SolidFlags.Trigger | SolidFlags.NotSolid, set.ArgAs<SolidFlags>(2));
    }

    /// <summary>
    /// And a record carrying one half of solidity applies neither, for the same
    /// reason a half-carried box applies neither. The fact above is the
    /// positive case this one rests on.
    /// </summary>
    [Fact]
    public void SolidityMissingOneHalfIsNotAppliedAtAll()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        SavedEntity full = NativeRecord(7, new Vector(1f, 2f, 3f), 4)[0];
        var half = new SavedEntity(full.Handle, full.ClassName, "", 7);

        foreach (var (field, value) in full.Native)
        {
            if (field != NativeField.SolidFlags)
                half.SaveNative(field, value);
        }

        Assert.True(half.HasNative(NativeField.SolidType));
        Assert.False(half.HasNative(NativeField.SolidFlags));

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave([half], World(live, 7)));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Entities.SetSolidity");
        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetBounds");
    }

    /// <summary>
    /// <b>THE SHAPE IS RESTORED BEFORE THE TRANSFORM, AND BOUNDS BEFORE
    /// SOLIDITY WITHIN IT.</b> That inner order is the one thing here resting
    /// on a line of native code rather than on an argument:
    /// <c>SetSolidity</c> runs <c>UpdatePartition</c>, which creates or
    /// destroys the partition handle and inserts using whatever bounds are
    /// current, while <c>SetCollisionBounds</c> only moves an element that
    /// already exists.
    /// </summary>
    [Fact]
    public void TheShapeIsRestoredBeforeTheTransformAndBoundsBeforeSolidity()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(NativeRecord(7, new Vector(1f, 2f, 3f), 4),
                                               World(live, 7)));

        // Past the fixture's own LoadState move, which is not the overlay.
        List<string> overlay = Wrote(engine).Skip(1).ToList();

        Assert.True(overlay.IndexOf("Entities.SetBounds")
                  < overlay.IndexOf("Entities.SetSolidity"),
                    "bounds are written before solidity");

        Assert.True(overlay.IndexOf("Entities.SetSolidity")
                  < overlay.IndexOf("Entities.Teleport"),
                    "the shape is written before the transform");
    }

    /// <summary>
    /// The gravity scale comes back. <c>trigger_gravity</c> sets a toucher's
    /// scale on the way in and restores it on the way out, so a player saved
    /// standing in one falls at the wrong rate for the rest of the session
    /// without this — the trigger will never fire its restoring half, because
    /// the player never leaves a volume it was never in.
    /// </summary>
    [Fact]
    public void TheGravityScaleIsRestored()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(NativeRecord(7, new Vector(1f, 2f, 3f), 4),
                                               World(live, 7)));

        Near(SavedGravity, Wrote(engine, "Entities.SetGravity").ArgAs<float>(1));
    }

    /// <summary>
    /// <b>THE THINK SCHEDULE IS THE LAST THING THE OVERLAY APPLIES</b>, after
    /// health rather than with it, because everything above it can RESCHEDULE
    /// it: a solidity change re-inserts into the partition and a placement
    /// re-runs the touch sweep, a touch fires an input, and an input can
    /// schedule a think. The saved schedule has to be the one left standing.
    ///
    /// <para>
    /// IT USED TO BE READ OFF THE ENGINE DOUBLE as the last
    /// <c>Entities.Set*</c> call. Step 1g moved the schedule into managed code,
    /// so the ordering is asserted where it now happens: the fixture's own
    /// <c>LoadState</c> books a DECOY think — the last thing it does, after
    /// chaining to base — and the overlay has to overwrite it. If the schedule
    /// were applied before the entity's own load, the decoy would be what is
    /// standing at the end.
    /// </para>
    /// </summary>
    [Fact]
    public void TheThinkScheduleIsTheLastThingTheOverlayApplies()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));
        Assert.Equal(1, loaded.RestoreFromSave(NativeRecord(7, new Vector(1f, 2f, 3f), 4),
                                               World(live, 7)));

        var back = Assert.IsType<NativeFixtureEntity>(loaded.Get(live));

        Assert.True(back.DecoyWasBooked, "the fixture's LoadState ran and booked its decoy");
        Near(SavedThinkDelay, back.NextThinkDelay());
    }

    /// <summary>
    /// <b>AND IT IS CARRIED AS A DELAY, NOT AS AN ABSOLUTE TIME.</b> A load
    /// restarts the clock, so an absolute think time read on one map and
    /// written on the next names a moment that has already gone — every
    /// restored entity thinking on the first frame — or one minutes away. The
    /// number that came out of the getter is the number that goes into the
    /// setter, unchanged, and both are seconds from now.
    /// </summary>
    [Fact]
    public void TheThinkTimeIsCarriedAsADelayAndReachesTheScheduleUnchanged()
    {
        var loaded = Registry(out RecordingEngine engine);
        var live = new EntityHandle(0x0005_0011u);

        List<SavedEntity> saved = NativeRecord(7, new Vector(1f, 2f, 3f), 4);

        Near(SavedThinkDelay, saved[0].NativeFloat(NativeField.NextThink));

        // A DIFFERENT NUMBER FROM THE ONE THE LIVE ENTITY BOOKS FOR ITSELF.
        // NativeFixtureEntity books SavedThinkDelay on every key, so asserting
        // SavedThinkDelay after the restore would pass with the restore doing
        // nothing at all. The record is given a value the live entity cannot
        // have arrived at on its own, and THAT is what has to be standing at
        // the end.
        const float restored = 4.25f;
        saved[0].SaveNative(NativeField.NextThink, restored);

        Assert.True(loaded.KeyValue(live, "native_fixture", "speed", "1"));

        // The LIVE entity books its own, so what is standing at the end is a
        // number the record put there and not one that was already there.
        var before = Assert.IsType<NativeFixtureEntity>(loaded.Get(live));
        before.BookThink(SavedThinkDelay);
        Near(SavedThinkDelay, before.NextThinkDelay());

        Assert.Equal(1, loaded.RestoreFromSave(saved, World(live, 7)));

        Near(restored, Assert.IsType<NativeFixtureEntity>(loaded.Get(live)).NextThinkDelay());
    }
}

/// <summary>
/// An entity that HAS a native side, and that moves itself in its own
/// <c>LoadState</c>.
///
/// <para>
/// The self-move is what makes the ordering testable. A savegame load in this
/// port is a <c>map</c> plus the overlay, so the spawn has already placed the
/// entity by the time a record is applied and a class may place it again; this
/// stands in for both. If the overlay ran first, the entity would come back at
/// (99, 99, 99) — plausibly, and not where it was.
/// </para>
///
/// <para>
/// It reaches the engine through <see cref="ScriptedEntity.Entities"/> rather
/// than through the <c>Origin</c> property, and that is not incidental: the
/// property goes to the static <c>Engine</c>, which in a test is not the double
/// the registry bound and answers nothing to anybody.
/// </para>
/// </summary>
[ScriptedEntity("native_fixture")]
public sealed class NativeFixtureEntity : ScriptedEntity
{
    [ScriptVars] public ReloadVars Vars = new();

    /// <summary>Yes — which is what <c>Engine.IsInitialized</c> would say in a game.</summary>
    protected internal override bool HasNativeSide => true;


    /// <summary>
    /// Books a think under this entity's own handle, so a record collected off
    /// it has one to carry. Public because the schedule is managed now and a
    /// test has no other way in: <c>SetNextThink</c> is protected, as it should
    /// be.
    /// </summary>
    public void BookThink(float delay) => SetNextThink(delay);

    /// <summary>
    /// The decoy think this fixture's own <c>LoadState</c> books, so that
    /// "the overlay is applied afterwards" is observable. A value no record
    /// carries.
    /// </summary>
    public const float DecoyThinkDelay = 0.125f;

    /// <summary>Whether <see cref="LoadState"/> ran and booked its decoy.</summary>
    public bool DecoyWasBooked { get; private set; }

    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);

        Entities.SetOrigin(Handle, new Vector(99f, 99f, 99f));

        // LAST, so the native overlay has something of its own to overwrite.
        // See TheThinkScheduleIsTheLastThingTheOverlayApplies.
        SetNextThink(DecoyThinkDelay);
        DecoyWasBooked = true;
    }
}
