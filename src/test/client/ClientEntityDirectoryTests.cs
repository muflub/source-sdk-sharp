using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>THE CLIENT'S ENTITY LIST LOGIC, IN MANAGED CODE</b> —
/// <c>plan_entity.md</c> §3.3/§3.5, lane <c>entlist-client</c>.
///
/// <para>
/// §3.3 draws the line these facts police: the native slot table "cannot
/// iterate, has no listeners, and knows no names", and "everything a list does
/// — iterate, find by name, find by class, notify on add and remove — lives in
/// the managed <c>EntityDirectory</c>". So every capability asserted here is
/// one that must NOT have a native implementation, and the value of the facts
/// is that they hold the behaviour still while the native side loses it.
/// </para>
///
/// <para>
/// SERIAL SEMANTICS COME FIRST, because that is the one thing a table of this
/// shape gets wrong silently. <c>default(EntityHandle)</c> is a VALID handle —
/// entry 0, serial 0 — so a directory that treated it as the empty slot would
/// answer worldspawn's entry to every unbound question, and two lanes of this
/// port have already measured the wrong path on precisely that.
/// </para>
/// </summary>
public class ClientEntityDirectoryTests
{
    private static EntityHandle H(int entry, int serial)
        => new((uint)entry | ((uint)serial << 16));

    private static ClientEntityDirectory Directory() => new();

    //-------------------------------------------------------------------------
    // Serial semantics.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABoundHandleResolvesToItsEntity()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel");

        Assert.True(directory.Bind(H(12, 3), -1, entity));
        Assert.Same(entity, directory.Resolve(H(12, 3)));
    }

    [Fact]
    public void RebindingAnEntryLeavesTheOldHandleResolvingToNothing()
    {
        var directory = Directory();
        var first = new DirectoryProbeEntity("ss_barrel");
        var second = new DirectoryProbeEntity("ss_barrel");

        directory.Bind(H(12, 3), -1, first);
        directory.Unbind(H(12, 3));
        directory.Bind(H(12, 4), -1, second);

        Assert.Null(directory.Resolve(H(12, 3)));
        Assert.Same(second, directory.Resolve(H(12, 4)));
    }

    [Fact]
    public void AnEntryTakenByANewSerialWithoutAnUnbindStillEvictsTheOldHandle()
    {
        var directory = Directory();
        var first = new DirectoryProbeEntity("ss_barrel");
        var second = new DirectoryProbeEntity("ss_barrel");

        directory.Bind(H(12, 3), -1, first);
        directory.Bind(H(12, 4), -1, second);

        Assert.Null(directory.Resolve(H(12, 3)));
        Assert.Same(second, directory.Resolve(H(12, 4)));
        Assert.Equal(1, directory.Count);
    }

    [Fact]
    public void TheEntryZeroHandleBindsLikeAnyOther()
    {
        var directory = Directory();
        var world = new DirectoryProbeEntity("worldspawn");

        Assert.True(directory.Bind(new EntityHandle(0u), -1, world));
        Assert.Same(world, directory.Resolve(new EntityHandle(0u)));
    }

    [Fact]
    public void TheEmptySlotIsNoneAndNotDefault()
    {
        var directory = Directory();

        // Nothing is bound, so entry zero is empty -- and the empty answer must
        // be None. A directory that spelled the empty slot `default` would
        // report entry zero as occupied by whatever `default` resolves to.
        Assert.Equal(EntityHandle.None, directory.HandleOf(0));
        Assert.Null(directory.Resolve(default));
    }

    [Fact]
    public void NoneIsRefusedRatherThanBound()
    {
        var directory = Directory();

        Assert.False(directory.Bind(EntityHandle.None, -1, new DirectoryProbeEntity("ss_barrel")));
        Assert.Equal(0, directory.Count);
    }

    [Fact]
    public void AnEntryBeyondTheTableIsRefusedRatherThanWrapped()
    {
        var directory = Directory();

        // ENTRY MASK IS WIDER THAN THE TABLE by stock's own arithmetic
        // (0xFFFF against 8192 entries), so a handle can name an entry that
        // does not exist. Binding it anywhere would be a silent mis-address.
        Assert.False(directory.Bind(H(ClientEntityDirectory.EntryCount, 1), -1,
                                    new DirectoryProbeEntity("ss_barrel")));
        Assert.Equal(0, directory.Count);
    }

    [Fact]
    public void UnbindingOnAStaleHandleDoesNotEvictTheLiveOccupant()
    {
        var directory = Directory();
        var first = new DirectoryProbeEntity("ss_barrel");
        var second = new DirectoryProbeEntity("ss_barrel");

        directory.Bind(H(12, 3), -1, first);
        directory.Bind(H(12, 4), -1, second);

        Assert.False(directory.Unbind(H(12, 3)));
        Assert.Same(second, directory.Resolve(H(12, 4)));
    }

    //-------------------------------------------------------------------------
    // By index -- what a trace and a user message answer with.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEntityIsFoundByTheEngineIndexItWasBoundUnder()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel").At(41);

        directory.Bind(H(41, 2), 41, entity);

        Assert.Same(entity, directory.ByIndex(41));
    }

    [Fact]
    public void AnIndexWhoseEntityHasGoneAnswersNothing()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel").At(41);

        directory.Bind(H(41, 2), 41, entity);
        directory.Unbind(H(41, 2));

        Assert.Null(directory.ByIndex(41));
    }

    [Fact]
    public void AnIndexRecycledToANewEntityAnswersTheNewOne()
    {
        var directory = Directory();
        var gone = new DirectoryProbeEntity("ss_barrel").At(41);
        var arrived = new DirectoryProbeEntity("hl2mp_ragdoll").At(41);

        directory.Bind(H(41, 2), 41, gone);
        directory.Bind(H(41, 3), 41, arrived);

        Assert.Same(arrived, directory.ByIndex(41));
    }

    [Fact]
    public void DetachingAnEntityDoesNotStealAnIndexAnotherHandleNowHolds()
    {
        var directory = Directory();
        var stale = new DirectoryProbeEntity("ss_barrel").At(41);
        var live = new DirectoryProbeEntity("hl2mp_ragdoll").At(41);

        // TWO ENTITIES CARRYING ONE ENGINE INDEX AT DIFFERENT ENTRIES. The
        // index an entity carries is asked of the engine ONCE, in
        // ClientStandaloneRegistry.Create, and cached for the entity's life; an
        // answer that was already stale when it arrived puts the client in
        // exactly this state, with no way to notice.
        directory.Bind(H(12, 1), 41, stale);
        directory.Bind(H(41, 3), 41, live);

        Assert.Same(live, directory.ByIndex(41));

        // The erase must therefore check WHOSE index it is clearing. Clearing
        // it because the LEAVING entity happens to carry the same number makes
        // a bound, live entity unreachable by index -- which for a trace means
        // the shot passes through it.
        directory.Unbind(H(12, 1));

        Assert.Same(live, directory.ByIndex(41));
    }

    [Fact]
    public void AnIndexOutsideTheTableAnswersNothingRatherThanThrowing()
    {
        var directory = Directory();

        Assert.Null(directory.ByIndex(-1));
        Assert.Null(directory.ByIndex(ClientEntityDirectory.EntryCount));
    }

    //-------------------------------------------------------------------------
    // Iterate.
    //-------------------------------------------------------------------------

    [Fact]
    public void IterationYieldsEveryBoundEntityAndNothingElse()
    {
        var directory = Directory();
        var a = new DirectoryProbeEntity("ss_barrel");
        var b = new DirectoryProbeEntity("hl2mp_ragdoll");

        directory.Bind(H(4, 1), -1, a);
        directory.Bind(H(9, 1), -1, b);

        Assert.Equal(new HashSet<ClientStandaloneEntity> { a, b },
                     directory.Entities.ToHashSet());
    }

    [Fact]
    public void IterationCostsTheLiveCountAndNotTheTableSize()
    {
        var directory = Directory();

        directory.Bind(H(8000, 1), -1, new DirectoryProbeEntity("ss_barrel"));

        // ONE entity at a HIGH entry. A directory that walked its 8,192 entries
        // would answer the same thing; the fact that fails on a walk is the
        // count, because a walk yields nothing for the 8,191 empty entries and
        // this asserts what was ENUMERATED, not what came back.
        Assert.Single(directory.Entities);
        Assert.Equal(1, directory.Count);
    }

    [Fact]
    public void UnbindingTheMiddleEntityLeavesTheOthersEnumerable()
    {
        var directory = Directory();
        var a = new DirectoryProbeEntity("a");
        var b = new DirectoryProbeEntity("b");
        var c = new DirectoryProbeEntity("c");

        directory.Bind(H(1, 1), -1, a);
        directory.Bind(H(2, 1), -1, b);
        directory.Bind(H(3, 1), -1, c);
        directory.Unbind(H(2, 1));

        // THE SWAP-WITH-LAST IS WHAT THIS CHECKS. A dense list that removed by
        // index without repairing the moved entry's back-pointer loses the
        // entity that was last, not the one that was removed.
        Assert.Equal(new HashSet<ClientStandaloneEntity> { a, c },
                     directory.Entities.ToHashSet());
    }

    [Fact]
    public void BindingsCarryTheHandleEachEntityIsBoundUnder()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel");

        directory.Bind(H(7, 5), -1, entity);

        Assert.Equal((H(7, 5), (ClientStandaloneEntity)entity), Assert.Single(directory.Bindings));
    }

    [Fact]
    public void ASnapshotSurvivesTheCallerRemovingWhatItWasHanded()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("a"));
        directory.Bind(H(2, 1), -1, new DirectoryProbeEntity("b"));

        int seen = 0;

        foreach (var entity in directory.Snapshot())
        {
            directory.Clear();
            seen++;
        }

        Assert.Equal(2, seen);
    }

    //-------------------------------------------------------------------------
    // Find by class.
    //-------------------------------------------------------------------------

    [Fact]
    public void OfClassAnswersEveryEntityOfThatClassname()
    {
        var directory = Directory();
        var one = new DirectoryProbeEntity("ss_barrel");
        var two = new DirectoryProbeEntity("ss_barrel");

        directory.Bind(H(1, 1), -1, one);
        directory.Bind(H(2, 1), -1, two);
        directory.Bind(H(3, 1), -1, new DirectoryProbeEntity("hl2mp_ragdoll"));

        Assert.Equal(new HashSet<ClientStandaloneEntity> { one, two },
                     directory.OfClass("ss_barrel").ToHashSet());
    }

    [Fact]
    public void OfClassIsCaseInsensitiveLikeTheSdksOwnLookup()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_Barrel");

        directory.Bind(H(1, 1), -1, entity);

        Assert.Same(entity, Assert.Single(directory.OfClass("SS_BARREL")));
    }

    [Fact]
    public void OfClassTakesAPrefixWildcard()
    {
        var directory = Directory();
        var barrel = new DirectoryProbeEntity("ss_barrel");
        var pickup = new DirectoryProbeEntity("ss_pickup");

        directory.Bind(H(1, 1), -1, barrel);
        directory.Bind(H(2, 1), -1, pickup);
        directory.Bind(H(3, 1), -1, new DirectoryProbeEntity("env_fog_controller"));

        Assert.Equal(new HashSet<ClientStandaloneEntity> { barrel, pickup },
                     directory.OfClass("ss_*").ToHashSet());
    }

    [Fact]
    public void AnEmptyClassQueryAnswersNothingRatherThanEverything()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));

        Assert.Empty(directory.OfClass(null));
        Assert.Empty(directory.OfClass(string.Empty));
    }

    [Fact]
    public void AnUnboundEntityLeavesItsClassBucket()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.Unbind(H(1, 1));

        Assert.Empty(directory.OfClass("ss_barrel"));
        Assert.DoesNotContain("ss_barrel", directory.ClassNames);
    }

    [Fact]
    public void OfClassToleratesTheCallerRemovingWhatItIsHanded()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.Bind(H(2, 1), -1, new DirectoryProbeEntity("ss_barrel"));

        int seen = 0;

        foreach (var entity in directory.OfClass("ss_barrel"))
        {
            directory.Unbind(directory.Bindings.First(b => b.Entity == entity).Handle);
            seen++;
        }

        Assert.Equal(2, seen);
    }

    //-------------------------------------------------------------------------
    // Find by name.
    //-------------------------------------------------------------------------

    [Fact]
    public void ANamedEntityIsFoundByItsName()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel");

        directory.Bind(H(1, 1), -1, entity);
        directory.SetName(H(1, 1), "red_barrel");

        Assert.Same(entity, Assert.Single(directory.OfName("red_barrel")));
        Assert.Equal("red_barrel", directory.NameOf(H(1, 1)));
    }

    [Fact]
    public void RenamingAnEntityForgetsTheNameItHad()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.SetName(H(1, 1), "first");
        directory.SetName(H(1, 1), "second");

        Assert.Empty(directory.OfName("first"));
        Assert.Single(directory.OfName("second"));
    }

    [Fact]
    public void AnUnboundEntityIsNoLongerFoundByName()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.SetName(H(1, 1), "red_barrel");
        directory.Unbind(H(1, 1));

        Assert.Empty(directory.OfName("red_barrel"));
        Assert.Equal(0, directory.NamedCount);
    }

    [Fact]
    public void NamingAHandleThatIsNotBoundDoesNothing()
    {
        var directory = Directory();

        directory.SetName(H(1, 1), "nobody");

        Assert.Empty(directory.OfName("nobody"));
        Assert.Equal(0, directory.NamedCount);
    }

    [Fact]
    public void AnEmptyNameQueryAnswersNothingLikeFindEntityByName()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.SetName(H(1, 1), "red_barrel");

        Assert.Empty(directory.OfName(null));
        Assert.Empty(directory.OfName(string.Empty));
    }

    [Fact]
    public void OfNameTakesAPrefixWildcard()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.Bind(H(2, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.SetName(H(1, 1), "barrel_a");
        directory.SetName(H(2, 1), "crate_b");

        Assert.Single(directory.OfName("barrel*"));
    }

    //-------------------------------------------------------------------------
    // Notify on add and remove -- the managed answer to IEntityListener.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABindNotifiesWithTheHandleAndTheEntity()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel");
        var seen = new List<(EntityHandle, ClientStandaloneEntity)>();

        directory.EntityBound += (h, e) => seen.Add((h, e));
        directory.Bind(H(6, 2), -1, entity);

        Assert.Equal((H(6, 2), (ClientStandaloneEntity)entity), Assert.Single(seen));
    }

    [Fact]
    public void AnUnbindNotifiesWithTheEntityThatLeft()
    {
        var directory = Directory();
        var entity = new DirectoryProbeEntity("ss_barrel");
        var seen = new List<ClientStandaloneEntity>();

        directory.Bind(H(6, 2), -1, entity);
        directory.EntityUnbound += (h, e) => seen.Add(e);
        directory.Unbind(H(6, 2));

        Assert.Same(entity, Assert.Single(seen));
    }

    [Fact]
    public void TheUnbindNotificationSeesTheWorldTheRemovalLeaves()
    {
        var directory = Directory();

        directory.Bind(H(6, 2), -1, new DirectoryProbeEntity("ss_barrel"));

        ClientStandaloneEntity? stillThere = null;
        directory.EntityUnbound += (h, e) => stillThere = directory.Resolve(h);

        directory.Unbind(H(6, 2));

        // NOTIFIED AFTER THE TABLES ARE CLEARED. A listener that asked the
        // directory a question mid-removal and got the leaving entity back
        // would act on a world that no longer exists a line later.
        Assert.Null(stillThere);
    }

    [Fact]
    public void ARefusedBindNotifiesNobody()
    {
        var directory = Directory();
        int notifications = 0;

        directory.EntityBound += (h, e) => notifications++;
        directory.Bind(EntityHandle.None, -1, new DirectoryProbeEntity("ss_barrel"));

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void AnUnbindOfNothingNotifiesNobody()
    {
        var directory = Directory();
        int notifications = 0;

        directory.EntityUnbound += (h, e) => notifications++;
        directory.Unbind(H(6, 2));

        Assert.Equal(0, notifications);
    }

    //-------------------------------------------------------------------------
    // Level change.
    //-------------------------------------------------------------------------

    [Fact]
    public void ClearReleasesEveryBindingAndSaysHowMany()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("a"));
        directory.Bind(H(2, 1), -1, new DirectoryProbeEntity("b"));

        Assert.Equal(2, directory.Clear());
        Assert.Equal(0, directory.Count);
        Assert.Empty(directory.Entities);
    }

    [Fact]
    public void ClearNotifiesForEveryEntityItReleases()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("a"));
        directory.Bind(H(2, 1), -1, new DirectoryProbeEntity("b"));

        int notified = 0;
        directory.EntityUnbound += (h, e) => notified++;

        directory.Clear();

        // A LEVEL CHANGE THAT SAID NOTHING would leak exactly the registrations
        // a listener took on bind -- the dlight registry and the pool slots are
        // both of that shape.
        Assert.Equal(2, notified);
    }

    [Fact]
    public void ClearEmptiesTheClassAndNameIndexesToo()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("ss_barrel"));
        directory.SetName(H(1, 1), "red_barrel");

        directory.Clear();

        Assert.Empty(directory.OfClass("ss_barrel"));
        Assert.Empty(directory.OfName("red_barrel"));
        Assert.Empty(directory.ClassNames);
        Assert.Equal(0, directory.NamedCount);
    }

    [Fact]
    public void BindAndUnbindTotalsCountEveryOneThatTook()
    {
        var directory = Directory();

        directory.Bind(H(1, 1), -1, new DirectoryProbeEntity("a"));
        directory.Bind(EntityHandle.None, -1, new DirectoryProbeEntity("b"));
        directory.Unbind(H(1, 1));
        directory.Unbind(H(1, 1));

        Assert.Equal(1, directory.Binds);
        Assert.Equal(1, directory.Unbinds);
    }
}
