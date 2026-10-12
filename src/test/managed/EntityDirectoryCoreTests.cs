extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>THE FOLD — ONE STORAGE SHAPE FOR BOTH SIDES' DIRECTORIES.</b>
/// <c>plan_entity.md</c> §3.5, lane <c>sub-entitylist</c>, answering the
/// request lane <c>entlist-client</c> left for <c>entlist-server</c> and that
/// was orphaned when both merged.
///
/// <para>
/// The 39 facts in <c>ClientEntityDirectoryTests</c> and everything under
/// <c>EntityIoTests</c>, <c>EntityRemovalTests</c> and <c>ShellLessEntityTests</c>
/// already say what each DIRECTORY does; they all passed unchanged across the
/// fold, which is what makes it a retype and not a rewrite. What they cannot
/// say is that there is now exactly ONE implementation of the shape, or which
/// differences between the two sides are deliberate. That is what is here.
/// </para>
///
/// <para>
/// <b>A GREEN SUITE ACROSS A REFACTOR IS NOT A PROOF THE REFACTOR KEPT
/// ANYTHING.</b> Each fact below was mutation-verified against the CORE — the
/// guard broken there, the fact watched to go red — because a fact that only
/// ever ran against the old per-side copy would pass a fold that dropped the
/// rule it was written for.
/// </para>
/// </summary>
public class EntityDirectoryCoreTests
{
    private static EntityHandle H(int entry, int serial)
        => new((uint)entry | ((uint)serial << 16));

    private sealed class ServerProbe : ScriptedEntity
    {
    }

    //=========================================================================
    // 1. THERE IS ONE IMPLEMENTATION, AND BOTH SIDES ARE IT.
    //=========================================================================

    [Fact]
    public void TheServersDirectoryIsTheSharedCore()
    {
        Assert.IsAssignableFrom<EntityDirectoryCore<ScriptedEntity>>(new EntityDirectory(new RecordingLog()));
    }

    [Fact]
    public void TheClientsDirectoryIsTheSameSharedCore()
    {
        // The client's copy of the shared core (plan_side_isolation P7).
        Assert.IsAssignableFrom<client::SourceSharp.Abi.EntityDirectoryCore<ClientStandaloneEntity>>(
            new ClientEntityDirectory());
    }

    /// <summary>
    /// The size is declared ONCE. Both spellings still resolve — an inherited
    /// const is reachable through the derived type name — and they are the same
    /// declaration, which is the whole point: two spellings of one size is the
    /// drift a fixed-width boundary cannot see.
    /// </summary>
    [Fact]
    public void BothSidesReadOneDeclarationOfTheHandleSpaceSize()
    {
        Assert.Equal(8192, EntityDirectory.EntryCount);
        Assert.Equal(EntityDirectory.EntryCount, ClientEntityDirectory.EntryCount);
        Assert.Equal(EntityDirectory.EntryCount, EntityDirectoryCore<ScriptedEntity>.EntryCount);
    }

    //=========================================================================
    // 2. THE TWO RULES entlist-client ASKED TO SURVIVE THE FOLD.
    //
    // Rule 2 -- the guarded index clear -- already has its fact, and it is
    // ClientEntityDirectoryTests.DetachingAnEntityDoesNotStealAnIndexAnotherHandleNowHolds.
    // Breaking the guard in the CORE reddens it, which is what makes it the
    // fold's fact as well as the client's. Rule 1 is here because nothing said
    // it directly: the index table holds the HANDLE, so a stale index answers
    // through the same compare everything else uses.
    //=========================================================================

    /// <summary>
    /// <b>AN INDEX THE DETACH COULD NOT REACH STILL ANSWERS NOTHING, BECAUSE
    /// WHAT IT HOLDS IS A HANDLE.</b>
    ///
    /// <para>
    /// <c>Detach</c> erases an index entry by asking the LEAVING ENTITY which
    /// index it carries, which is one store instead of the O(live) walk by
    /// reference the registry this replaced paid. That is only as good as the
    /// entity's own answer: <c>Bind</c> takes the index as a PARAMETER and the
    /// shell caches its own separately, so an entity filed under an index it
    /// does not carry leaves that entry behind — and the client's own
    /// <c>Detach</c> comment says the same thing from the other direction
    /// ("indices are recycled, and the index is asked of the engine once, at
    /// create, and cached").
    /// </para>
    ///
    /// <para>
    /// The stale entry is therefore not an edge case to be designed out; it is
    /// the case the storage choice answers. Because the table holds the HANDLE
    /// and <c>ByIndex</c> re-resolves it, a stale entry costs one failed
    /// compare. A table of ENTITIES would hand the caller a live object for a
    /// dead entity — and for a trace that is a shot that hits something that is
    /// not there.
    /// </para>
    /// </summary>
    [Fact]
    public void AnIndexTheDetachCouldNotReachStillAnswersNothing()
    {
        var directory = new ClientEntityDirectory();

        // Filed under index 7; the entity itself carries none. Detach will
        // clear nothing, so _byIndex[7] keeps this handle after the unbind.
        var first = new DirectoryProbeEntity("ss_crate");

        Assert.True(directory.Bind(H(7, 1), 7, first));
        Assert.Same(first, directory.ByIndex(7));

        // The engine reuses entry 7 with a bumped serial. Bind's eviction arm
        // detaches the first entity -- and cannot clear index 7, because the
        // entity does not know it was filed there. The second entity is filed
        // under its own index, 9.
        var second = new DirectoryProbeEntity("ss_crate").At(9);
        Assert.True(directory.Bind(H(7, 2), 9, second));

        // Storing the ENTITY under the index would answer whatever is at entry
        // 7 now -- which is `second`, an entity that has nothing to do with
        // index 7. Storing the HANDLE makes the stale entry fail the same
        // compare every other lookup uses.
        Assert.Null(directory.ByIndex(7));
        Assert.Same(second, directory.ByIndex(9));
    }

    //=========================================================================
    // 3. THE DELIBERATE DIFFERENCES BETWEEN THE SIDES, ASSERTED SO THAT NEITHER
    //    READS AS AN OVERSIGHT.
    //=========================================================================

    /// <summary>
    /// The server keeps no index table, and <c>ByIndex</c> says so by answering
    /// null rather than by answering about the ENTRY of the same number.
    ///
    /// <para>
    /// The two coincide on the server — a shell binds the edict index the
    /// engine gave it — so a caller that reached for <c>ByIndex</c> there would
    /// be right by accident, and wrong the moment an engine-invisible entity
    /// (whose entry is no index at all) went through it. The core costs 32KB
    /// per side for the table; the server does not pay it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheServerKeepsNoIndexTableAndSaysSoRatherThanAnsweringAboutTheEntry()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var entity = new ServerProbe();

        Assert.True(directory.Bind(H(31, 4), entity));
        Assert.Same(entity, directory.Resolve(H(31, 4)));

        Assert.Null(directory.ByIndex(31));
    }

    /// <summary>
    /// The client requires a handle to be BOUND before it can be named; the
    /// server does not, because its map parse fills the name index from the
    /// <c>targetname</c> keyvalue.
    /// </summary>
    [Fact]
    public void OnlyTheClientRequiresABindingBeforeAName()
    {
        var client = new ClientEntityDirectory();
        client.SetName(H(44, 1), "door_a");
        Assert.Equal(0, client.NamedCount);

        var server = new EntityDirectory(new RecordingLog());
        server.SetName(H(44, 1), "door_a");
        Assert.Equal(1, server.NamedCount);
    }

    //=========================================================================
    // 4. THE CLASSNAME INDEX, WHICH THE SERVER GAINED AND WHOSE WILDCARD ARM
    //    IS A FIX RATHER THAN A CARRY-OVER.
    //=========================================================================

    /// <summary>
    /// An exact query is a bucket hit and answers the same set the dense walk
    /// answered, case included: the bucket's comparer is OrdinalIgnoreCase,
    /// which is exactly the equality <c>EntityNameMatch.Match</c> performs on a
    /// query with no '*'.
    /// </summary>
    [Fact]
    public void TheServersExactClassQueryAnswersWhatTheWalkAnswered()
    {
        var directory = new EntityDirectory(new RecordingLog());

        var a = new ServerProbe { ClassName = "ss_barrel" };
        var b = new ServerProbe { ClassName = "ss_crate" };
        var c = new ServerProbe { ClassName = "SS_BARREL" };

        Assert.True(directory.Bind(H(2, 1), a));
        Assert.True(directory.Bind(H(3, 1), b));
        Assert.True(directory.Bind(H(4, 1), c));

        var found = new List<EntityHandle>();
        Assert.Equal(2, directory.FindByClassName("ss_barrel", found));
        Assert.Contains(H(2, 1), found);
        Assert.Contains(H(4, 1), found);
        Assert.DoesNotContain(H(3, 1), found);
    }

    /// <summary>
    /// <b>THE WILDCARD ARM WALKS THE ENTITIES AND NOT THE BUCKETS, AND THIS IS
    /// THE FACT THAT SAYS WHY.</b>
    ///
    /// <para>
    /// <c>EntityNameMatch.Match</c> says a query beginning '*' matches an entity
    /// with NO classname at all (<c>EntityIo.cs:316</c>), and an entity with no
    /// classname is in no bucket. The client's <c>OfClass</c> walked the buckets
    /// for '*' and therefore dropped it; the server's walked the entities and
    /// did not. The fold takes the SERVER's arm for both rather than the
    /// cheaper one, so this is a defect fixed on the client and not a
    /// regression carried to the server.
    /// </para>
    /// </summary>
    [Fact]
    public void AWildcardClassQueryStillFindsAnEntityWithNoClassnameAtAll()
    {
        var directory = new EntityDirectory(new RecordingLog());

        var named = new ServerProbe { ClassName = "ss_barrel" };
        var nameless = new ServerProbe { ClassName = string.Empty };

        Assert.True(directory.Bind(H(5, 1), named));
        Assert.True(directory.Bind(H(6, 1), nameless));

        var found = new List<EntityHandle>();
        Assert.Equal(2, directory.FindByClassName("*", found));
        Assert.Contains(H(6, 1), found);
    }

    /// <summary>The client's wildcard arm is the same one, so it finds it too.</summary>
    [Fact]
    public void TheClientsWildcardClassQueryFindsTheNamelessEntityAsWell()
    {
        var directory = new ClientEntityDirectory();

        Assert.True(directory.Bind(H(5, 1), -1, new DirectoryProbeEntity("ss_barrel")));
        Assert.True(directory.Bind(H(6, 1), -1, new DirectoryProbeEntity(string.Empty)));

        Assert.Equal(2, directory.OfClass("*").Count());
    }

    /// <summary>
    /// An unbound entity leaves the classname index on the SERVER too, which it
    /// had no index to leave before the fold.
    /// </summary>
    [Fact]
    public void AnUnboundServerEntityLeavesTheClassBucket()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var entity = new ServerProbe { ClassName = "ss_barrel" };

        Assert.True(directory.Bind(H(8, 1), entity));

        var before = new List<EntityHandle>();
        Assert.Equal(1, directory.FindByClassName("ss_barrel", before));

        Assert.True(directory.Unbind(H(8, 1)));

        var after = new List<EntityHandle>();
        Assert.Equal(0, directory.FindByClassName("ss_barrel", after));
        Assert.DoesNotContain("ss_barrel", directory.ClassNames);
    }

    /// <summary>
    /// A level change empties the classname index as well as the bindings. The
    /// server's <c>Clear</c> takes the core's silent form, so this is the fact
    /// that says it clears the indexes and not only the storage.
    /// </summary>
    [Fact]
    public void AServerLevelChangeEmptiesTheClassIndexToo()
    {
        var directory = new EntityDirectory(new RecordingLog());
        Assert.True(directory.Bind(H(9, 1), new ServerProbe { ClassName = "ss_barrel" }));

        directory.Clear();

        var found = new List<EntityHandle>();
        Assert.Equal(0, directory.FindByClassName("ss_barrel", found));
        Assert.Empty(directory.ClassNames);
        Assert.Equal(0, directory.NamedCount);
    }

    //=========================================================================
    // 5. THE SERVER'S OWN BOOKKEEPING SURVIVED BEING MOVED BEHIND THE CORE.
    //
    // Unbind's five steps have to run BEFORE the entry is emptied and must not
    // run at all for a handle the entry does not hold, which is why the server
    // restates the core's refusals rather than letting UnbindCore make them.
    //=========================================================================

    [Fact]
    public void AStaleServerUnbindRunsNoneOfTheSideBookkeeping()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var live = new ServerProbe { ClassName = "ss_barrel" };

        Assert.True(directory.Bind(H(11, 1), live));
        directory.SetName(H(11, 1), "the_live_one");
        Assert.Equal(RemovalMark.Marked, directory.Remove(H(11, 1)));

        // The entry's previous occupant, whose handle nobody unbound.
        Assert.False(directory.Unbind(H(11, 0)));

        Assert.Same(live, directory.Resolve(H(11, 1)));
        Assert.Equal("the_live_one", directory.NameOf(H(11, 1)));
        Assert.Equal(1, directory.Count);

        // THE REMOVAL MARK IS THE ONE THIS CATCHES. Unbind clears
        // _removing[entry] unconditionally, by ENTRY and not by handle, so a
        // stale unbind that got that far would take the live entity's own mark
        // away and it would never be freed -- and nothing would say so.
        Assert.True(directory.IsRemoved(H(11, 1)));
    }

    [Fact]
    public void AnEvictedServerHandleLosesItsNameAndItsClassBucket()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var first = new ServerProbe { ClassName = "ss_barrel" };

        Assert.True(directory.Bind(H(13, 1), first));
        directory.SetName(H(13, 1), "old");

        // The engine reused the entry with a bumped serial and nobody unbound
        // the first: Bind's eviction arm.
        Assert.True(directory.Bind(H(13, 2), new ServerProbe { ClassName = "ss_crate" }));

        Assert.Equal(string.Empty, directory.NameOf(H(13, 1)));
        Assert.Equal(0, directory.NamedCount);

        var found = new List<EntityHandle>();
        Assert.Equal(0, directory.FindByClassName("ss_barrel", found));
        Assert.Equal(1, directory.Count);
    }

    /// <summary>
    /// An evicted handle's QUEUED I/O goes with it, which nothing said before.
    ///
    /// <para>
    /// Found by mutation while folding: deleting <c>ForgetIo</c> from the
    /// eviction arm reddened nothing in the whole directory suite. It was
    /// equally uncovered before the fold — <c>Bind</c>'s eviction arm called it
    /// there too — so this is a gap being closed rather than one being
    /// introduced, and it is a real one: a delayed output posted by an entity
    /// whose entry has been taken would be delivered from a handle that now
    /// names somebody else.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEvictedHandlesQueuedOutputIsCancelledWithIt()
    {
        var directory = new EntityDirectory(new RecordingLog());

        Assert.True(directory.Bind(H(17, 1), new ServerProbe { ClassName = "ss_relay" }));
        directory.FireInputAt(H(99, 1), "Trigger", null, 0f, 2f, H(17, 1), H(17, 1));
        Assert.Equal(1, directory.Io.Count);

        // The engine reused entry 17 and nobody unbound the first.
        Assert.True(directory.Bind(H(17, 2), new ServerProbe { ClassName = "ss_relay" }));

        Assert.Equal(0, directory.Io.Count);
    }
}
