using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE STATE THE WORLD HOLDS ABOUT ITS ENTITIES, across a hot reload.
///
/// <para>
/// None of this is a field on an entity, so no declaration on an entity could
/// carry it: a parent link belongs to two entities, a touch pair belongs to
/// two, a think booking is a position in a heap. All of it lives in a service
/// the unload destroys, and none of it was carried before this lane.
/// </para>
///
/// <para>
/// Measured on <c>dm_lockdown</c> across one <c>cs_sv_reload</c>, with
/// <c>make check</c> and the live gate both green: <c>think.pending</c> read 8
/// before and 0 after; the census read 473 before and 483 after, ten of one
/// class. These are the hermetic halves of both.
/// </para>
/// </summary>
public class ReloadSubsystemTests
{
    // ONE ENTITY'S FAILURE IS NOT THE RELOAD'S -- RestoreFrom's per-entity
    // guard -- IS NOT ASSERTED HERE, AND THAT IS DELIBERATE RATHER THAN AN
    // OMISSION.
    //
    // Two hermetic fixtures were written for it and BOTH were checks that
    // cannot fail, which the mutation run caught rather than the reviewer:
    //
    //   a throwing LoadState      Apply already catches one, so it never
    //                             reaches RestoreFrom.
    //   a throwing constructor    EnsureInstance already answers null for one,
    //                             so it never reaches RestoreFrom either.
    //
    // The site that is genuinely unguarded is NetStateBinding.AttachBound inside
    // EnsureInstance, and reaching it needs a real blob behind a real handle --
    // which is the engine, not a fixture. So the proof is the LIVE gate's own
    // before and after, which is a measurement rather than a contrivance:
    //
    //   before  BlobTooShortException: PlayerNetState needs 652 byte(s) and
    //           entity 2 ('ss_player') offers 0
    //           server: managed reload complete -- 478 saved, -1 restored
    //   after   478 saved, 478 restored
    //
    // and the gate asserts restored == saved, which is the check that was red.

    //-- the touch table ------------------------------------------------------

    private static EntityHandle H(uint index, uint serial = 1)
        => new((serial << 16) | index);

    [Fact]
    public void ARestoredTouchPairIsInTheTable()
    {
        var directory = new EntityDirectory(new RecordingLog());

        Assert.True(directory.RestoreTouch(H(4), H(9)));
        Assert.True(directory.IsTouching(H(4), H(9)));
    }

    /// <summary>
    /// <b>NO EDGE.</b> The pair never separated, so an arrival is exactly the
    /// double-fire this restore exists to stop — a <c>trigger_hurt</c>
    /// re-applying its entry damage, a counter double-counting.
    /// </summary>
    [Fact]
    public void RestoringATouchPairRaisesNoEdge()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var edges = new List<(EntityHandle Entity, EntityDirectory.TouchEdge Edge)>();
        directory.TouchRouting = (entity, other, edge) => edges.Add((entity, edge));

        directory.RestoreTouch(H(4), H(9));

        Assert.Empty(edges);
    }

    /// <summary>
    /// A pair the next report still names takes the STAY arm, not the START
    /// arm. Without the restored link it would take START and re-announce an
    /// arrival that never happened.
    /// </summary>
    [Fact]
    public void ARestoredPairTakesTheStayArmOnTheNextReport()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var edges = new List<EntityDirectory.TouchEdge>();
        directory.TouchRouting = (_, _, edge) => edges.Add(edge);

        directory.RestoreTouch(H(4), H(9));
        directory.TouchFrame(new[] { new OverlapPair(H(4), H(9)) });

        Assert.DoesNotContain(EntityDirectory.TouchEdge.Start, edges);
    }

    /// <summary>
    /// The mirror, and the half a bare "re-derive it next frame" cannot do: a
    /// pair that SEPARATED while the assembly was swapping still gets the
    /// <c>OnEndTouch</c> it is owed. Without the restore it is simply absent
    /// from the new table and the "every start touch gets an end touch"
    /// invariant is broken for the rest of the map.
    /// </summary>
    [Fact]
    public void APairThatDepartedDuringTheReloadStillGetsItsEndTouch()
    {
        var directory = new EntityDirectory(new RecordingLog());
        var edges = new List<EntityDirectory.TouchEdge>();
        directory.TouchRouting = (_, _, edge) => edges.Add(edge);

        directory.RestoreTouch(H(4), H(9));
        directory.TouchFrame(Array.Empty<OverlapPair>());

        Assert.Contains(EntityDirectory.TouchEdge.End, edges);
    }

    [Fact]
    public void RestoringTheInvalidHandleAsATouchPairIsRefused()
    {
        var directory = new EntityDirectory(new RecordingLog());

        Assert.False(directory.RestoreTouch(EntityHandle.None, H(9)));
        Assert.Equal(0, directory.TouchLinkCount);
    }

    [Fact]
    public void RestoringATouchPairWithItselfIsRefused()
    {
        var directory = new EntityDirectory(new RecordingLog());

        Assert.False(directory.RestoreTouch(H(4), H(4)));
    }

    [Fact]
    public void RestoringAPairTwiceAddsOneLink()
    {
        var directory = new EntityDirectory(new RecordingLog());

        Assert.True(directory.RestoreTouch(H(4), H(9)));
        Assert.False(directory.RestoreTouch(H(9), H(4)));
        Assert.Equal(1, directory.TouchLinkCount);
    }

    [Fact]
    public void EveryTouchingPairIsReported()
    {
        var directory = new EntityDirectory(new RecordingLog());
        directory.RestoreTouch(H(4), H(9));
        directory.RestoreTouch(H(5), H(9));

        Assert.Equal(2, directory.TouchingPairs().Count());
    }

    //-- the think schedule ---------------------------------------------------

    [Fact]
    public void EveryBookingIsReported()
    {
        var scheduler = new ThinkScheduler();
        scheduler.Schedule(H(4), null, 10f);
        scheduler.Schedule(H(4), "spin", 11f);
        scheduler.Schedule(H(5), null, 12f);

        Assert.Equal(3, scheduler.Bookings().Count());
    }

    [Fact]
    public void ABookingReportsItsContextAndTime()
    {
        var scheduler = new ThinkScheduler();
        scheduler.Schedule(H(4), "spin", 11f);

        ScheduledThink booked = scheduler.Bookings().Single();

        Assert.Equal("spin", booked.Context);
        Assert.Equal(11f, booked.Time);
    }

    /// <summary>
    /// The default context is <c>null</c>, and it has to come back as
    /// <c>null</c>: an empty string is a NAMED context called "", which is a
    /// different booking that nothing cancels.
    /// </summary>
    [Fact]
    public void TheDefaultContextIsReportedAsNull()
    {
        var scheduler = new ThinkScheduler();
        scheduler.Schedule(H(4), null, 10f);

        Assert.Null(scheduler.Bookings().Single().Context);
    }

    /// <summary>
    /// LIVE ONLY, NOT THE HEAP. The heap carries superseded entries by design;
    /// carrying those across would re-book the entity at every time it had ever
    /// been booked at, and the earliest would win.
    /// </summary>
    [Fact]
    public void ASupersededBookingIsNotReported()
    {
        var scheduler = new ThinkScheduler();
        scheduler.Schedule(H(4), null, 10f);
        scheduler.Schedule(H(4), null, 20f);

        Assert.True(scheduler.HeapDepth > 1, "the heap keeps the superseded entry");
        Assert.Equal(20f, scheduler.Bookings().Single().Time);
    }

    [Fact]
    public void ACancelledBookingIsNotReported()
    {
        var scheduler = new ThinkScheduler();
        scheduler.Schedule(H(4), null, 10f);
        scheduler.Cancel(H(4), null);

        Assert.Empty(scheduler.Bookings());
    }

    /// <summary>
    /// The round trip the reload actually does: read every booking as SECONDS
    /// REMAINING and book it again against a clock that has moved on. An
    /// absolute time would put every booking in the past and fire the lot on
    /// the first frame.
    /// </summary>
    [Fact]
    public void ABookingSurvivesAClockThatMovedOn()
    {
        var before = new ThinkScheduler();
        before.Schedule(H(4), "spin", 10f);

        const float wasNow = 8f;
        const float isNow = 100f;

        var after = new ThinkScheduler();

        foreach (ScheduledThink booked in before.Bookings())
            after.Schedule(booked.Entity, booked.Context, isNow + (booked.Time - wasNow));

        Assert.Equal(102f, after.NextThinkTime(H(4), "spin"));
    }

    //-- parenting ------------------------------------------------------------

    /// <summary>
    /// A world where every entity exists, is standalone and sits at the origin.
    /// What these facts are about is the LINK TABLE — <c>RestoreLink</c> reads
    /// no transform and writes no position — so the world only has to answer
    /// rather than to be interesting.
    /// </summary>
    private sealed class FlatWorld : IHierarchyWorld
    {
        public bool IsStandalone(EntityHandle handle) => handle != EntityHandle.None;
        public bool IsBase(EntityHandle handle) => false;
        public bool CanPose(EntityHandle handle) => false;

        public bool TryGetEntityFrame(EntityHandle handle, out Transform frame)
        {
            frame = Transform.Identity;
            return true;
        }

        public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame)
        {
            frame = Transform.Identity;
            return false;
        }

        public int LookupAttachment(EntityHandle handle, string attachmentName) => 0;

        public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles)
        {
            origin = default;
            angles = default;
            return true;
        }

        public void WriteComposed(EntityHandle child, Vector origin, QAngle angles,
                                  Transform childToWorld) { }

        public EntityHandle FindStandaloneByName(string name) => EntityHandle.None;
        public EntityHandle FindBaseByName(string name) => EntityHandle.None;
        public string NameOf(EntityHandle handle) => handle.Value.ToString();
        public void Remove(EntityHandle handle) { }
        public void DetachPhysics(EntityHandle child) { }
        public void Warn(string message) { }
    }

    [Fact]
    public void ARestoredLinkIsInTheTable()
    {
        var hierarchy = new Hierarchy(new FlatWorld());

        Assert.True(hierarchy.RestoreLink(H(4), H(9), 0, false, true, Transform.Identity, "", ""));
        Assert.True(hierarchy.IsParented(H(4)));
    }

    /// <summary>
    /// THE OFFSET IS REPLAYED, NOT RECOMPUTED. A reload moves nothing, so the
    /// link that has to come back is the one measured when the parenting was
    /// first asked for — re-measuring it would be a different answer for a
    /// child hung off a model attachment whose pose the engine has not built
    /// yet.
    /// </summary>
    [Fact]
    public void ARestoredLinkKeepsTheOffsetItWasSavedWith()
    {
        var hierarchy = new Hierarchy(new FlatWorld());
        Transform offset = Transform.FromOriginAngles(new Vector(3f, 4f, 5f), default);

        hierarchy.RestoreLink(H(4), H(9), 0, false, true, offset, "", "");

        Assert.Equal(offset.M03, hierarchy.LinkFor(H(4))!.ChildToParent.M03);
        Assert.Equal(offset.M23, hierarchy.LinkFor(H(4))!.ChildToParent.M23);
    }

    /// <summary>
    /// The records come from outside this object — another build's <c>.sav</c>,
    /// a restore replayed twice — so a cycle in them is an infinite recursion in
    /// <c>ParentMoved</c> rather than a wrong position.
    /// </summary>
    [Fact]
    public void ARestoredLinkThatWouldCloseACycleIsRefused()
    {
        var hierarchy = new Hierarchy(new FlatWorld());

        Assert.True(hierarchy.RestoreLink(H(4), H(9), 0, false, true, Transform.Identity, "", ""));
        Assert.False(hierarchy.RestoreLink(H(9), H(4), 0, false, true, Transform.Identity, "", ""));
        Assert.Equal(1, hierarchy.LinkCount);
    }

    [Fact]
    public void ARestoredLinkOfAnEntityToItselfIsRefused()
    {
        var hierarchy = new Hierarchy(new FlatWorld());

        Assert.False(hierarchy.RestoreLink(H(4), H(4), 0, false, true, Transform.Identity, "", ""));
    }

    [Fact]
    public void ARestoredLinkKeepsTheParentNameForALaterResolve()
    {
        var hierarchy = new Hierarchy(new FlatWorld());
        hierarchy.RestoreLink(H(4), EntityHandle.None, 0, false, false, Transform.Identity,
                              "lift_01", "");

        Assert.Equal("lift_01", hierarchy.LinkFor(H(4))!.ParentName);
        Assert.False(hierarchy.LinkFor(H(4))!.Resolved);
    }

    /// <summary>
    /// A map writes <c>parentattachment</c> in whatever order Hammer wrote it,
    /// so one can be waiting for a <c>parentname</c> that has not arrived. A
    /// reload that dropped it leaves the child on its parent's ORIGIN instead of
    /// on the attachment the mapper named, with nothing in any log.
    /// </summary>
    [Fact]
    public void APendingAttachmentNameIsReportedSoItCanBeCarried()
    {
        var hierarchy = new Hierarchy(new FlatWorld());
        hierarchy.SetParentAttachmentName(H(4), "muzzle");

        Assert.Equal((H(4), "muzzle"), hierarchy.PendingAttachments().Single());
    }
}
