extern alias client;
using System.Buffers.Binary;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
// The client's copy of the shared save vocabulary (plan_side_isolation P7: the
// shared source compiles into each side); unqualified, the names are the server's.
using ClientSavedEntity = client::SourceSharp.Abi.SavedEntity;
using ClientSavedEntityCodec = client::SourceSharp.Abi.SavedEntityCodec;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The managed half of a save, end to end: a live entity, through the real
/// registry, through <see cref="SavedEntityCodec"/>'s bytes, back into a
/// registry that has never seen it.
///
/// <para>
/// <c>docs/managed-saverestore.md</c> is the lane. Its argument is that neither
/// side needs a new state model — the reload path already produces the records —
/// and this is the fact that has to hold for that argument to be true. It uses
/// <c>ReloadFixtureEntity</c>, which is the reload suite's own fixture, on
/// purpose: if the save path needed a fixture of its own, the argument would
/// already be wrong.
/// </para>
///
/// <para>
/// THE BYTES ARE IN THE MIDDLE, and that is the whole point of the shape. A
/// test that called <c>Collect</c> and handed the list straight to
/// <c>RestoreFrom</c> would pass with a codec that dropped every field, because
/// the objects on both sides would be the same objects. Everything here crosses
/// a byte array.
/// </para>
/// </summary>
public class SaveRoundTripTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);
    private readonly ReloadStore _clientReload = new(Side.Client);

    private static ScriptedEntityRegistry Registry()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SaveRoundTripTests).Assembly);
        registry.Services = Services();
        return registry;
    }

    /// <summary>The bytes, as a save would write and read them.</summary>
    private static List<SavedEntity> ThroughBytes(IReadOnlyList<SavedEntity> collected)
    {
        var block = new byte[SavedEntityCodec.ByteCount(collected)];

        Assert.Equal(block.Length, SavedEntityCodec.Write(collected, block, new RecordingLog()));
        Assert.True(SavedEntityCodec.TryRead(block, out var back, out string? why), why);

        return back;
    }

    /// <summary><see cref="ThroughBytes"/> for the client's records, through the client's codec.</summary>
    private static List<ClientSavedEntity> ClientThroughBytes(IReadOnlyList<ClientSavedEntity> collected)
    {
        var block = new byte[ClientSavedEntityCodec.ByteCount(collected)];

        Assert.Equal(block.Length, ClientSavedEntityCodec.Write(collected, block, new RecordingLog()));
        Assert.True(ClientSavedEntityCodec.TryRead(block, out var back, out string? why), why);

        return back;
    }

    [Fact]
    public void AnEntitySurvivesTheWholeSavePathAndComesBackWithEveryStore()
    {
        var registry = Registry();
        var handle = new EntityHandle(0x0004_0011u);

        // What a map does, which is the same path the reload suite drives.
        Assert.True(registry.KeyValue(handle, "reload_fixture", "speed", "512.5"));

        var live = (ReloadFixtureEntity)registry.Get(handle)!;
        live.TravelledLegs = 3;
        live.OnFirst.Fire("gone?");

        List<SavedEntity> collected = registry.Collect();
        Assert.Single(collected);

        // A REGISTRY THAT HAS NEVER SEEN IT, rebuilt from bytes alone.
        var loaded = Registry();
        Assert.Null(loaded.Get(handle));

        Assert.Equal(1, loaded.RestoreFrom(ThroughBytes(collected)));

        var back = (ReloadFixtureEntity)loaded.Get(handle)!;

        Assert.False(ReferenceEquals(back, live));
        Assert.Equal(512.5f, back.Vars.Speed);        // the map's key
        Assert.Equal(3, back.TravelledLegs);          // its own local state
        Assert.Equal(1, back.OnFirst.FireCount);      // and what it had already done
    }

    [Fact]
    public void CollectDoesNotTouchTheReloadStore()
    {
        // THE REASON Collect EXISTS SEPARATELY. The reload store is a handover
        // between two halves of ONE reload. A save that wrote into it would
        // leave records there for a reload that is not coming, and the next
        // cs_sv_reload's Take would hand them over -- restoring a world from a
        // save nobody asked to load.
        ReloadCarrier.Take(_reload);   // start from empty, whatever ran before

        var registry = Registry();
        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0012u), "reload_fixture",
                                      "speed", "1.0"));

        Assert.Single(registry.Collect());
        Assert.Equal(0, ReloadCarrier.Count(_reload));

        // ...and SaveForReload, which is the same walk, still does fill it --
        // so the split moved the store write rather than losing it.
        Assert.Equal(1, registry.SaveForReload(_reload));
        Assert.Equal(1, ReloadCarrier.Count(_reload));

        ReloadCarrier.Take(_reload);
    }

    [Fact]
    public void AnEmptyWorldSavesAndRestoresAsAnEmptyWorld()
    {
        // What a save taken before any managed entity spawns produces. It must
        // read back as nothing rather than as a refusal, because a map with no
        // scripted entities in it is an ordinary map.
        var registry = Registry();

        List<SavedEntity> collected = registry.Collect();
        Assert.Empty(collected);

        Assert.Equal(0, Registry().RestoreFrom(ThroughBytes(collected)));
    }

    [Fact]
    public void ManyEntitiesAllComeBackAndKeepTheirOwnValues()
    {
        // One entity round-tripping proves the format; four hundred proves the
        // per-record framing. dm_lockdown carries 473 and the reload path moves
        // 472 of them, so this is the size the real thing has to survive -- and
        // it is where an off-by-one in a length prefix shows up and one entity
        // does not.
        var registry = Registry();

        for (int i = 0; i < 400; i++)
        {
            Assert.True(registry.KeyValue(new EntityHandle(0x0005_0000u + (uint)i),
                                          "reload_fixture", "speed",
                                          i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        List<SavedEntity> collected = registry.Collect();
        Assert.Equal(400, collected.Count);

        var loaded = Registry();
        Assert.Equal(400, loaded.RestoreFrom(ThroughBytes(collected)));

        for (int i = 0; i < 400; i++)
        {
            var back = (ReloadFixtureEntity)loaded.Get(new EntityHandle(0x0005_0000u + (uint)i))!;
            Assert.Equal((float)i, back.Vars.Speed);
        }
    }

    //-------------------------------------------------------------------------
    // The CLIENT's pair, which had no round-trip test until a review found the
    // gap. `docs/managed-saverestore.md` §4 claimed "and the client's pair"
    // had landed while every fact above drove only the server's registry --
    // which is how a side gets a claim and no check.
    //-------------------------------------------------------------------------

    private static ClientStandaloneRegistry ClientRegistry()
    {
        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientDynamicLight).Assembly);
        registry.Services = new client::SourceSharp.Abi.ServiceContainer()
            .AddInstance<IDynamicLights>(new DynamicLights(ClientDoubles.Fake()));
        return registry;
    }

    [Fact]
    public void TheClientsRegistryAlsoSurvivesTheWholeSavePath()
    {
        var registry = ClientRegistry();
        var light = new EntityHandle(0x0701_0101u);

        Assert.True(registry.Create(light, EntityNames.LightDynamic));

        List<ClientSavedEntity> collected = registry.Collect();
        Assert.Single(collected);
        Assert.Equal(EntityNames.LightDynamic, collected[0].ClassName);

        var loaded = ClientRegistry();
        Assert.Null(loaded.Get(light));

        Assert.Equal(1, loaded.RestoreFrom(ClientThroughBytes(collected)));
        Assert.NotNull(loaded.Get(light));

        // AND WHAT CAME BACK IS COMPARED, which the first version of this fact
        // did not do -- it asserted only that an instance existed, so a codec
        // that dropped every field would have passed it. That is precisely the
        // failure this class's own header warns about, reintroduced on the
        // client side by putting the bytes in the middle and then asserting
        // nothing that crossed them. Found by review.
        //
        // Re-collecting is the comparison that needs no knowledge of which
        // fields this class happens to declare: if the round trip lost one, the
        // second walk cannot produce it.
        ClientSavedEntity after = Assert.Single(loaded.Collect());

        Assert.Equal(collected[0].Net.Count, after.Net.Count);

        foreach (var (field, value) in collected[0].Net)
        {
            Assert.True(after.Net.TryGetValue(field, out string? back),
                        $"the networked field '{field}' did not survive the round trip");
            Assert.Equal(value, back);
        }
    }

    [Fact]
    public void TheClientsRecordCarriesSomethingForThatComparisonToBeAbout()
    {
        // The vacuity guard for the fact above. Comparing two empty dictionaries
        // passes for free, and the client's SaveState writes ONLY [NetState]
        // fields -- so if this class declared none, the comparison would be
        // about nothing at all.
        var registry = ClientRegistry();
        Assert.True(registry.Create(new EntityHandle(0x0701_0103u), EntityNames.LightDynamic));

        ClientSavedEntity record = Assert.Single(registry.Collect());

        Assert.True(record.Net.Count > 0,
                    "ClientDynamicLight carries no networked state, so the round-trip "
                    + "comparison above compares nothing");
    }

    [Fact]
    public void TheClientsCollectDoesNotTouchTheReloadStoreEither()
    {
        // Same property as the server's, asserted separately because it is a
        // separate implementation -- the two registries are line-for-line
        // alike, which is exactly the situation where one gets fixed and the
        // other does not.
        ReloadCarrier.Take(_clientReload);

        var registry = ClientRegistry();
        Assert.True(registry.Create(new EntityHandle(0x0701_0102u), EntityNames.LightDynamic));

        Assert.Single(registry.Collect());
        Assert.Equal(0, ReloadCarrier.Count(_clientReload));

        Assert.Equal(1, registry.SaveForReload(_clientReload));
        Assert.Equal(1, ReloadCarrier.Count(_clientReload));

        ReloadCarrier.Take(_clientReload);
    }

    [Fact]
    public void ARefusedBlockRestoresNothingAtAll()
    {
        // The end of the argument in SavedEntityCodec's header, seen from the
        // registry: a corrupt .sav must leave the world empty rather than
        // half-populated. Half a restored world is worse than none, because the
        // player keeps playing in it.
        var registry = Registry();
        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0013u), "reload_fixture",
                                      "speed", "7.0"));

        // TWO entities, and that is the whole point of the fact. With one, the
        // list is empty when ReadOne fails whether or not the refusal path
        // clears it -- so the first version of this passed with the clear-on-
        // refusal deleted, which is the guard it exists to protect. Found by
        // review.
        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0014u), "reload_fixture",
                                      "speed", "9.0"));

        List<SavedEntity> two = registry.Collect();
        Assert.Equal(2, two.Count);

        var block = new byte[SavedEntityCodec.ByteCount(two)];
        SavedEntityCodec.Write(two, block, new RecordingLog());

        // Cut inside the SECOND record, so the first has already parsed --
        // and RESTAMPED, or the checksum refuses it at the header and this
        // fact never reaches the clear-on-refusal it exists to protect.
        byte[] cut = CutAndRestamp(block, 1);

        Assert.False(SavedEntityCodec.TryRead(cut, out var back, out string? why));
        Assert.NotNull(why);
        Assert.DoesNotContain("checksum", why);

        var loaded = Registry();
        Assert.Equal(0, loaded.RestoreFrom(back));
        Assert.Null(loaded.Get(new EntityHandle(0x0004_0013u)));
    }

    /// <summary>
    /// A block cut short, with its checksum RESTAMPED over what is left.
    ///
    /// <para>
    /// The restamp is the whole point. Version 2 put a checksum in the header,
    /// and a plain truncation now fails THAT — at the header, before the reader
    /// ever reaches the per-record path this fact is about. The fact went on
    /// passing, for a reason that had nothing to do with its subject. Measured
    /// by the codec's own mutation harness, which found this fact staying green
    /// with the guard it exists to protect deleted.
    /// </para>
    ///
    /// <para>
    /// So: cut, then make the checksum honest about the cut. What is left is a
    /// block whose integrity check AGREES and whose contents are still short —
    /// which is the only input that exercises a refusal past the header.
    /// </para>
    /// </summary>
    private static byte[] CutAndRestamp(byte[] block, int drop)
    {
        byte[] cut = block.AsSpan(0, block.Length - drop).ToArray();

        BinaryPrimitives.WriteUInt32LittleEndian(
            cut.AsSpan(SavedEntityCodec.ChecksumAt),
            SavedEntityCodec.Checksum(cut.AsSpan(SavedEntityCodec.ChecksumFrom)));

        return cut;
    }

    //-------------------------------------------------------------------------
    // RE-IDENTIFICATION — the half a save needs and a reload does not.
    //
    // THE MEASUREMENT THESE FACTS EXIST FOR. A handle is an entry index packed
    // with a serial, and a save/load rebuilds the world: on dm_lockdown, 0 of
    // 470 handles survived a load and every serial was exactly one higher,
    // while all 470 indices were unchanged and named the same class. So
    // RestoreFrom's handle match — which is right for a reload, where the world
    // stands still — places NOTHING out of a .sav, and RestoreFromSave exists
    // because of that number.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The world was rebuilt: same index, new serial. The record must still
    /// find its entity.
    /// </summary>
    [Fact]
    public void ARecordFindsItsEntityAgainAfterEverySerialHasMoved()
    {
        var registry = Registry();
        var saved = new EntityHandle(0x0004_0011u);

        Assert.True(registry.KeyValue(saved, "reload_fixture", "speed", "512.5"));
        ((ReloadFixtureEntity)registry.Get(saved)!).TravelledLegs = 3;

        // Collect with no engine leaves Index at -1, so the record is given the
        // index it would have carried. This is the ONE place the fixture stands
        // in for the engine, and it stands in for exactly the fact measured
        // live.
        List<SavedEntity> collected = Reindexed(registry.Collect(), 0x0011);

        // THE REBUILT WORLD: the same entity at the same index, one serial on.
        var rebuilt = new EntityHandle(0x0005_0011u);
        var loaded = Registry();
        Assert.True(loaded.KeyValue(rebuilt, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.RestoreFromSave(ThroughBytes(collected), World(rebuilt, 0x0011)));

        var back = (ReloadFixtureEntity)loaded.Get(rebuilt)!;
        Assert.Equal(512.5f, back.Vars.Speed);
        Assert.Equal(3, back.TravelledLegs);
    }

    /// <summary>
    /// And the control: the SAME records through the reload's handle match
    /// place nothing at all, because the handle they carry names nothing.
    /// </summary>
    [Fact]
    public void TheReloadsHandleMatchPlacesNoneOfTheSameRecords()
    {
        var registry = Registry();
        var saved = new EntityHandle(0x0004_0011u);

        Assert.True(registry.KeyValue(saved, "reload_fixture", "speed", "512.5"));

        List<SavedEntity> collected = Reindexed(registry.Collect(), 0x0011);

        var rebuilt = new EntityHandle(0x0005_0011u);
        var loaded = Registry();
        Assert.True(loaded.KeyValue(rebuilt, "reload_fixture", "speed", "1"));

        // RestoreFrom creates an instance for the stale handle rather than
        // finding the live one — which is the failure, spelled out: the state
        // lands on an entity that is not in the world.
        loaded.RestoreFrom(ThroughBytes(collected));

        // AND SINCE STEP 1g THE DAMAGE IS VISIBLE RATHER THAN MERELY WASTED.
        // The stale handle names the SAME ENTRY as the live one, one serial
        // back, and the registry's storage is now an array indexed by that
        // entry (plan_entity.md §3.4) exactly as the engine's slot table is —
        // one entity per entry, not one per handle. So the instance conjured
        // for the dead handle TAKES the entry, and the live entity is no longer
        // findable by its own handle.
        //
        // The control's claim is unchanged and is if anything sharper: the
        // saved state did not reach the live entity. It reached a ghost.
        Assert.Null(loaded.Get(rebuilt));
        Assert.Equal(512.5f, ((ReloadFixtureEntity)loaded.Get(saved)!).Vars.Speed);
    }

    /// <summary>
    /// <b>THE ONE THAT MATTERS.</b> An index that now holds a different class
    /// must be refused, not replayed. Keys the two share by name would be
    /// applied and the rest dropped — a load with no error and a subtly wrong
    /// world.
    /// </summary>
    [Fact]
    public void ARecordWhoseIndexNowHoldsAnotherClassIsDroppedRatherThanReplayed()
    {
        var registry = Registry();
        var saved = new EntityHandle(0x0004_0011u);

        Assert.True(registry.KeyValue(saved, "reload_fixture", "speed", "512.5"));

        List<SavedEntity> collected = Reindexed(registry.Collect(), 0x0011);

        // The same index, a different class standing on it.
        var rebuilt = new EntityHandle(0x0005_0011u);
        var loaded = Registry();
        Assert.True(loaded.KeyValue(rebuilt, "reload_forgetful", "speed", "1"));

        Assert.Equal(0, loaded.RestoreFromSave(ThroughBytes(collected), World(rebuilt, 0x0011)));
        Assert.Equal(1f, ((ReloadForgetfulEntity)loaded.Get(rebuilt)!).Vars.Speed);
    }

    /// <summary>
    /// A record whose index matches nothing is dropped and COUNTED OUT, rather
    /// than making the return value quietly smaller than what was saved.
    /// </summary>
    [Fact]
    public void ARecordThatMatchesNothingIsNotCountedAsRestored()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0011u),
                                      "reload_fixture", "speed", "512.5"));

        List<SavedEntity> collected = Reindexed(registry.Collect(), 0x0011);

        // A world with an entity at a DIFFERENT index: the record's key names
        // nothing, and "nothing" must not become "restored".
        var loaded = Registry();
        var elsewhere = new EntityHandle(0x0005_0099u);
        Assert.True(loaded.KeyValue(elsewhere, "reload_fixture", "speed", "1"));

        Assert.Equal(0, loaded.RestoreFromSave(ThroughBytes(collected),
                                               World(elsewhere, 0x0099)));
    }

    /// <summary>
    /// <b>THE FALLBACK, AND IT IS NOT OPTIONAL.</b> 81 of dm_lockdown's 473
    /// entities have NO index — they are server-only and hold no edict, so
    /// <c>SourceSharp_EntityGetIndex</c> answers -1 for them by design: 38
    /// <c>env_soundscape</c>, 9 <c>logic_relay</c>, 9
    /// <c>info_player_deathmatch</c> and the rest. An index-only restore drops
    /// every one of them, and the three relays that proved this lane live were
    /// among them.
    /// </summary>
    [Fact]
    public void AnEntityWithNoIndexIsFoundByItsTargetnameInstead()
    {
        var registry = Registry();
        var saved = new EntityHandle(0x0004_0011u);

        Assert.True(registry.KeyValue(saved, "reload_fixture", "speed", "512.5"));

        List<SavedEntity> collected = Named(Reindexed(registry.Collect(), -1), "antspawn");

        var rebuilt = new EntityHandle(0x0005_0044u);
        var loaded = Registry();
        Assert.True(loaded.KeyValue(rebuilt, "reload_fixture", "speed", "1"));

        Assert.Equal(1, loaded.RestoreFromSave(ThroughBytes(collected),
                                               World(rebuilt, -1, "antspawn")));

        Assert.Equal(512.5f, ((ReloadFixtureEntity)loaded.Get(rebuilt)!).Vars.Speed);
    }

    /// <summary>
    /// A name a map used twice is not a key. Picking whichever the dictionary
    /// happened to hold would be the same silent mis-application the classname
    /// guard exists to stop — so a repeated name is struck out and the record
    /// is dropped instead.
    /// </summary>
    [Fact]
    public void ARepeatedTargetnameIsNotAKeyAtAll()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0011u),
                                      "reload_fixture", "speed", "512.5"));

        List<SavedEntity> collected = Named(Reindexed(registry.Collect(), -1), "twice");

        var a = new EntityHandle(0x0005_0044u);
        var b = new EntityHandle(0x0005_0045u);
        var loaded = Registry();
        Assert.True(loaded.KeyValue(a, "reload_fixture", "speed", "1"));
        Assert.True(loaded.KeyValue(b, "reload_fixture", "speed", "1"));

        var world = new List<ScriptedEntityRegistry.LiveEntity>
        {
            new(a, -1, "twice"),
            new(b, -1, "twice"),
        };

        Assert.Equal(0, loaded.RestoreFromSave(ThroughBytes(collected), world));
        Assert.Equal(1f, ((ReloadFixtureEntity)loaded.Get(a)!).Vars.Speed);
        Assert.Equal(1f, ((ReloadFixtureEntity)loaded.Get(b)!).Vars.Speed);
    }

    /// <summary>
    /// <b>THE SHAPE §7e.3 IS ABOUT, AND THE REFUSAL IS DELIBERATE.</b> A record
    /// with NEITHER key — no index, because the entity is server-only and holds
    /// no edict, and no targetname, because the map gave it none — names
    /// nothing. It is placed on nothing even when the only entity in the world
    /// is the very one that produced it.
    ///
    /// <para>
    /// <b>AND WHAT THAT COSTS IS NOTHING, WHICH IS A MEASUREMENT.</b> 51 of
    /// <c>dm_lockdown</c>'s 472 records are this shape (38 <c>env_soundscape</c>,
    /// 9 <c>info_player_deathmatch</c>, and one each of
    /// <c>env_fog_controller</c>, <c>phys_hinge</c>,
    /// <c>game_ragdoll_manager</c>, <c>info_player_start</c>) and 6 more are
    /// struck out by <see cref="ARepeatedTargetnameIsNotAKeyAtAll"/>'s rule —
    /// the 57 the ledger counts. Read out of the bytes of four independent
    /// <c>.sav</c> files, all 57 are IDENTICAL in native placement, in map
    /// keys, in <c>[NetState]</c> (they carry none: 0 of the 81 index-less
    /// records do, against 68 of the 391 indexed ones) and in locals. The only
    /// field that ever moves in the whole set is which one
    /// <c>env_soundscape</c> carries <c>OnPlay</c> = 1, and an output is a
    /// TALLY — <c>Output.Restore</c> puts the count back and touches nothing
    /// else, because the one-shot budget lives in the native
    /// <c>CEventAction</c> list a managed restore does not reach.
    /// </para>
    ///
    /// <para>
    /// So this fact is not a gap being recorded. It is the closing argument
    /// made executable: a later key that guessed at these records — a spawn
    /// ordinal is the obvious one — would have to turn this red first, and
    /// that is the point at which somebody has to say what stops it drifting
    /// onto the wrong entity of the same classname, which the classname guard
    /// cannot see.
    /// </para>
    /// </summary>
    [Fact]
    public void ARecordWithNeitherKeyIsPlacedOnNothingAtAll()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0011u),
                                      "reload_fixture", "speed", "512.5"));

        List<SavedEntity> keyless = Reindexed(registry.Collect(), -1);

        // Reindexed walks upward from what it is given, so one record is the
        // only shape in which -1 means -1.
        Assert.Single(keyless);
        Assert.Equal(string.Empty, keyless[0].Name);

        // THE POSITIVE CASE FIRST, and it is not decoration. This fact is about
        // an input being REFUSED, and a registry that placed nothing ever would
        // satisfy the half below on its own. The same record, the same world,
        // the same live entity -- with a name on both sides it places, so what
        // the refusal refuses is an input that would OTHERWISE be handled.
        var named = Registry();
        var withName = new EntityHandle(0x0005_0044u);

        Assert.True(named.KeyValue(withName, "reload_fixture", "speed", "1"));

        Assert.Equal(1, named.RestoreFromSave(ThroughBytes(Named(keyless, "antspawn")),
                                              World(withName, -1, "antspawn")));

        Assert.Equal(512.5f, ((ReloadFixtureEntity)named.Get(withName)!).Vars.Speed);

        // And now with no name on either side, which is the record as the save
        // actually wrote it. The handles differ because they always do -- every
        // serial moves across a load, 0 of 470 measured -- so index and name
        // are the only keys in play, and neither exists.
        var loaded = Registry();
        var live = new EntityHandle(0x0005_0044u);

        Assert.True(loaded.KeyValue(live, "reload_fixture", "speed", "1"));

        Assert.Equal(0, loaded.RestoreFromSave(ThroughBytes(keyless), World(live, -1)));

        // The live entity keeps its own spawn value. "Restored 0" and "restored
        // onto the wrong thing" are both zero-returning if nobody looks.
        Assert.Equal(1f, ((ReloadFixtureEntity)loaded.Get(live)!).Vars.Speed);
    }

    /// <summary>The records with a targetname on them.</summary>
    private static List<SavedEntity> Named(List<SavedEntity> records, string name)
    {
        var named = new List<SavedEntity>(records.Count);

        foreach (SavedEntity e in records)
        {
            var copy = new SavedEntity(e.Handle, e.ClassName, name, e.Index);

            foreach (var (key, v) in e.Vars)
                copy.SaveVar(key, v.Value, v.LastSetAt);
            foreach (var (field, value) in e.Net)
                copy.SaveNet(field, value);
            foreach (var (n, o) in e.Outputs)
                copy.SaveOutput(n, o.FireCount, o.LastValue);
            foreach (var (key, value) in e.Local)
                copy.Set(key, value);

            named.Add(copy);
        }

        return named;
    }

    /// <summary>The index survives the bytes, which is what makes any of the
    /// above possible at all.</summary>
    [Fact]
    public void TheIndexCrossesTheBytes()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0011u),
                                      "reload_fixture", "speed", "1"));

        List<SavedEntity> back = ThroughBytes(Reindexed(registry.Collect(), 4242));

        Assert.Equal(4242, back[0].Index);
    }

    /// <summary>
    /// The records a live <c>Collect</c> would produce, given the indices the
    /// engine would have answered. A save's records carry one; a test registry
    /// has no engine to ask, so this is where that stands in.
    /// </summary>
    /// <summary>
    /// The live world, as the engine would describe it. A test registry has no
    /// native side, so the index and the targetname — both of which only the
    /// engine can answer — are supplied here.
    /// </summary>
    private static List<ScriptedEntityRegistry.LiveEntity> World(EntityHandle handle, int index,
                                                                string name = "")
        => new() { new ScriptedEntityRegistry.LiveEntity(handle, index, name) };

    private static List<SavedEntity> Reindexed(List<SavedEntity> collected, int index)
    {
        var reindexed = new List<SavedEntity>(collected.Count);

        foreach (SavedEntity e in collected)
        {
            var copy = new SavedEntity(e.Handle, e.ClassName, e.Name, index);

            foreach (var (key, v) in e.Vars)
                copy.SaveVar(key, v.Value, v.LastSetAt);

            foreach (var (field, value) in e.Net)
                copy.SaveNet(field, value);

            foreach (var (name, o) in e.Outputs)
                copy.SaveOutput(name, o.FireCount, o.LastValue);

            foreach (var (key, value) in e.Local)
                copy.Set(key, value);

            reindexed.Add(copy);
            index++;
        }

        return reindexed;
    }

    //-------------------------------------------------------------------------
    // A2: Collect must not drop a record in silence.
    //
    // A save that quietly omitted two entities and one that carried all 473
    // print the same nothing at the end of the walk. That is the failure class
    // this whole lane has been bitten by twice, and it is the last open review
    // finding.
    //-------------------------------------------------------------------------

    /// <summary>Everything a sink saw while <paramref name="what"/> ran.</summary>
    // What the subject printed: its console's lines (statics to zero).
    private static List<string> Logged(ConsoleLines console, Action what)
    {
        var lines = new List<string>();
        console.Tap = (text, _) => lines.Add(text);

        try
        {
            what();
        }
        finally
        {
            console.Tap = null;
        }

        return lines;
    }

    /// <summary>The same, over the client's console (each side compiles its own, plan_shim_areas_split phase 3).</summary>
    private static List<string> Logged(client::SourceSharp.Abi.ConsoleLines console, Action what)
    {
        var lines = new List<string>();
        console.Tap = (text, _) => lines.Add(text);

        try
        {
            what();
        }
        finally
        {
            console.Tap = null;
        }

        return lines;
    }

    /// <summary>
    /// <b>THE POSITIVE CASE, AND IT COMES FIRST.</b> A thrower's neighbour is
    /// collected — so the two facts below are about a record that WOULD
    /// otherwise have been carried, rather than about a walk that carries
    /// nothing.
    /// </summary>
    [Fact]
    public void AThrowersNeighbourIsStillCollected()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0021u), "save_thrower", "speed", "1"));
        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0022u), "reload_fixture", "speed", "2"));

        SavedEntity carried = Assert.Single(registry.Collect());

        Assert.Equal("reload_fixture", carried.ClassName);
    }

    /// <summary>
    /// <b>A2.</b> The walk says how many records it did not carry, and names
    /// one, so that a partial save is distinguishable from a whole one at the
    /// point it happens.
    /// </summary>
    [Fact]
    public void CollectReportsTheRecordsItSkipped()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0023u), "save_thrower", "speed", "1"));
        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0024u), "reload_fixture", "speed", "2"));

        List<string> lines = Logged(registry.Lines, () => Assert.Single(registry.Collect()));

        string report = Assert.Single(lines, l => l.Contains("1 of 2"));

        // THE COUNT AND THE NAME, both. A count of 1 and a count of 400 want
        // different responses; a count alone sends its reader to a scrollback
        // that has already gone.
        Assert.Contains("save_thrower", report);
        Assert.Contains("skipped", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And it says nothing when it skipped nothing. A report printed on every
    /// ordinary save is a report nobody reads on the save that mattered.
    /// </summary>
    [Fact]
    public void CollectReportsNothingWhenItSkippedNothing()
    {
        var registry = Registry();

        Assert.True(registry.KeyValue(new EntityHandle(0x0004_0025u), "reload_fixture", "speed", "2"));

        List<string> lines = Logged(registry.Lines, () => Assert.Single(registry.Collect()));

        Assert.DoesNotContain(lines, l => l.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The client's walk is a separate implementation of the same loop, so it
    /// gets its own fact. The two registries are line-for-line alike, which is
    /// exactly the situation where one is fixed and the other is not.
    /// </summary>
    [Fact]
    public void TheClientsCollectReportsTheRecordsItSkipped()
    {
        var registry = ClientRegistryWithFixtures();

        Assert.True(registry.Create(new EntityHandle(0x0701_0201u), "save_thrower_client"));
        Assert.True(registry.Create(new EntityHandle(0x0701_0202u), EntityNames.LightDynamic));

        List<string> lines = Logged(registry.Lines, () => Assert.Single(registry.Collect()));

        string report = Assert.Single(lines, l => l.Contains("1 of 2"));

        Assert.Contains("save_thrower_client", report);
        Assert.Contains("skipped", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The client's vacuity guard: its thrower's neighbour is carried.</summary>
    [Fact]
    public void TheClientsThrowersNeighbourIsStillCollected()
    {
        var registry = ClientRegistryWithFixtures();

        Assert.True(registry.Create(new EntityHandle(0x0701_0203u), "save_thrower_client"));
        Assert.True(registry.Create(new EntityHandle(0x0701_0204u), EntityNames.LightDynamic));

        ClientSavedEntity carried = Assert.Single(registry.Collect());

        Assert.Equal(EntityNames.LightDynamic, carried.ClassName);
    }

    /// <summary>
    /// A client registry that also knows this assembly's fixtures.
    /// <see cref="ClientRegistry"/> registers the mod's assembly alone, which
    /// is right for the facts about real classes and cannot see a thrower.
    /// </summary>
    private static ClientStandaloneRegistry ClientRegistryWithFixtures()
    {
        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientDynamicLight).Assembly);
        registry.RegisterAll(typeof(SaveRoundTripTests).Assembly);
        registry.Services = new client::SourceSharp.Abi.ServiceContainer()
            .AddInstance<IDynamicLights>(new DynamicLights(ClientDoubles.Fake()));
        return registry;
    }
}

/// <summary>The client's half of the same fixture. See <see cref="SaveThrowerEntity"/>.</summary>
[ClientStandaloneEntity("save_thrower_client")]
public sealed class SaveThrowerClientEntity : ClientStandaloneEntity
{
    public SaveThrowerClientEntity(client::SourceSharp.Abi.IEngineEntityRender render) : base(render) { }

    public override string ClassName => "save_thrower_client";

    public override void SaveState(ClientSavedEntity state)
        => throw new InvalidOperationException("the level tore this down first");
}
