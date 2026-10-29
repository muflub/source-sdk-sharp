using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE SEAM BETWEEN MANAGED PARENTING AND THE ENGINE, held to the header.
///
/// <para>
/// <see cref="EngineHierarchyWorld"/> is the half step 2h deliberately did not
/// write, and its own handover says why: <i>"a stub answering false would be a
/// live wrong answer the day it was wired — a muzzle flash silently composed
/// against an origin."</i> So what is checkable about it without a running game is
/// not what it computes — it computes nothing — but that its VOCABULARY agrees
/// with the native side's, and that it refuses rather than guesses when there is
/// no engine under it.
/// </para>
///
/// <para>
/// <b>Both halves have been the bug before.</b> The ABI's own parity test exists
/// because two adjacent slots once traded places and a string pointer arrived
/// where a handle was expected. A BIT VALUE is the same failure one level down and
/// quieter: swap <c>Standalone</c> and <c>Base</c> and every parent is on the
/// wrong hierarchy, which reads as a frame rule that has stopped working.
/// </para>
/// </summary>
public class EngineHierarchyWorldTests
{
    //=========================================================================//
    // Finding the native side.
    //=========================================================================//

    private static string? Header() => RepoLayout.FindSdkFile(Path.Combine("host", "sourcesharp.h"));

    /// <summary>
    /// The value of one <c>#define</c>, as an integer, or null.
    /// </summary>
    private static int? Define(string header, string name)
    {
        var m = Regex.Match(header, @"#define\s+" + Regex.Escape(name) + @"\s+(0x[0-9a-fA-F]+|\d+)");

        if (!m.Success)
            return null;

        string text = m.Groups[1].Value;

        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(text, 16)
            : int.Parse(text);
    }

    //=========================================================================//
    // The probe's bits.
    //=========================================================================//

    /// <summary>
    /// EVERY BIT, AGAINST THE HEADER THAT DEFINES IT.
    ///
    /// <para>
    /// One call answers three questions and the caller takes them apart by bit, so
    /// a value that disagreed with native would not fail to compile, would not
    /// throw, and would not be logged — it would make
    /// <see cref="ParentFrameRule.KindFor"/> take the wrong arm, which shows as a
    /// child hanging off an attachment on a parent that has no skeleton, or off an
    /// origin when a muzzle was named.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(nameof(ParentProbe.Standalone), "SOURCESHARP_PARENT_PROBE_STANDALONE")]
    [InlineData(nameof(ParentProbe.Base), "SOURCESHARP_PARENT_PROBE_BASE")]
    [InlineData(nameof(ParentProbe.CanPose), "SOURCESHARP_PARENT_PROBE_CAN_POSE")]
    public void EachProbeBitIsTheValueTheHeaderDefines(string managed, string native)
    {
        string? path = Header();

        // Skipped rather than failed: the suite must still run from a directory
        // with no source tree next to it.
        if (path is null)
            return;

        int? want = Define(File.ReadAllText(path), native);

        Assert.True(want.HasValue, $"{native} is not defined in sourcesharp.h");
        Assert.Equal(want!.Value, (int)Enum.Parse<ParentProbe>(managed));
    }

    /// <summary>
    /// The three are distinct powers of two, which is what makes them a bit field
    /// rather than three names for the same answer. A duplicate would make
    /// "standalone" and "base" indistinguishable, and every SDK parent would read
    /// as one of ours.
    /// </summary>
    [Fact]
    public void TheProbeBitsAreThreeDistinctPowersOfTwo()
    {
        int[] bits =
        {
            (int)ParentProbe.Standalone,
            (int)ParentProbe.Base,
            (int)ParentProbe.CanPose,
        };

        Assert.Equal(3, bits.Distinct().Count());
        Assert.All(bits, b => Assert.True(b != 0 && (b & (b - 1)) == 0, $"{b} is not one bit"));
    }

    [Fact]
    public void NoProbeBitsAtAllIsTheAnswerForAHandleThatResolvesToNothing()
        => Assert.Equal(0, (int)ParentProbe.None);

    //=========================================================================//
    // The eleven inbound slots and their counters.
    //=========================================================================//

    /// <summary>
    /// EVERY PARENTING SLOT THE HEADER DECLARES HAS A CROSSING COUNTER, AND
    /// NOTHING ELSE DOES.
    ///
    /// <para>
    /// The crossing counters are the instrument the whole entity port is measured
    /// with, and a slot without one is a crossing that does not appear in
    /// <c>cs_sv_crossings</c> — so the budget stays green over a regression it
    /// cannot see. That is the failure mode
    /// <c>tools/live-gate-budget.tsv</c>'s header spends a page on.
    /// </para>
    ///
    /// <para>
    /// BOTH DIRECTIONS. A counter with no slot is the reverse mistake and is just
    /// as bad in a different way: it makes the report claim coverage of a thunk
    /// that no longer exists.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryParentingSlotInTheHeaderHasACrossingCounterAndViceVersa()
    {
        string? path = Header();

        if (path is null)
            return;

        string header = File.ReadAllText(path);

        // The slot names, from the function-pointer declarations. Hierarchy* is
        // the whole family and the prefix is deliberate: it is what makes the two
        // lists comparable without a table that would be a third thing to keep
        // honest.
        // IManagedHierarchy's slots since plan_engineapi_vtables E8 (the
        // struct's slots went at E-last), named after the slots they replaced:
        // the members of the table managed writes (plan_forwarders_everywhere.md
        // phase 4).
        header = File.ReadAllText(RepoLayout.Repo("src", "shared", "native", "imanaged_areas.h"));
        var slots = Regex.Matches(header, @"\( \*(Hierarchy\w+) \)\( IManagedHierarchy \*self")
                         .Select(m => m.Groups[1].Value)
                         .ToHashSet(StringComparer.Ordinal);

        // Nine since the serialised ABI pass (2026-10-20) removed
        // HierarchyEntityRemoved and HierarchyResolveParentNames with their ops.
        var counters = Enum.GetNames<CrossingOp>()
                           .Where(n => n.StartsWith("ServerHierarchy", StringComparison.Ordinal))
                           .Select(n => n["Server".Length..])
                           .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(9, slots.Count);
        Assert.Equal(slots.OrderBy(s => s), counters.OrderBy(s => s));
    }

    /// <summary>
    /// The four the other way — what managed parenting asks of the world. Named
    /// individually rather than counted, because the count is the thing a
    /// fifth slot would quietly change (EntityLookupAttachmentAny left on
    /// 2026-10-18: the managed skeletons answer it).
    ///
    /// <para>
    /// Two of them were added AFTER the first live run and neither could have
    /// been predicted from reading: <c>EntityGetEngineBlock</c>, because the
    /// payload's length is the entity's tier and stops short of the block, and
    /// <c>EntityDestroyPhysics</c>, because a parented child that keeps its
    /// solver object fights its own composition every tick.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("EntityParentProbe")]
    [InlineData("EntityGetAttachmentFrame")]
    [InlineData("EntityGetEngineBlock")]
    [InlineData("EntityDestroyPhysics")]
    public void EachOutboundParentingSlotIsDeclaredInBothFiles(string slot)
    {
        // A server shim-area method since plan_engineapi_vtables C6c: declared
        // in ishim_areas.h and forwarded by a generated table.
        string areas = AbiParityTests.CodeOnly(File.ReadAllText(
            RepoLayout.Repo("src", "shared", "native", "ishim_areas.h")));

        Assert.Matches($@"\bvirtual\s[^;(]*\b{slot}\s*\(", areas);
        Assert.Contains(typeof(ShimRoot).Assembly.GetTypes(),
                        t => t.Name.StartsWith("Shim", StringComparison.Ordinal)
                             && t.Name.EndsWith("Fwd", StringComparison.Ordinal)
                             && t.GetField(slot) is not null);
    }

    //=========================================================================//
    // What it does when the engine knows nothing of the entity.
    //=========================================================================//

    /// <summary>
    /// A world over a recorded engine whose probe answers <paramref name="bits"/>
    /// for every handle. The engine is the side's own entity area, handed in by
    /// whoever composes the side (plan_abi_hotreload D8), never a process-wide
    /// one.
    /// </summary>
    private static EngineHierarchyWorld World(out RecordingEngine engine,
                                              ParentProbe bits = ParentProbe.None)
    {
        engine = TestServices.Mock().Returns("Entities.ParentProbe.bits", bits);
        return new EngineHierarchyWorld(engine, engine, engine, new RecordingLog());
    }

    /// <summary>
    /// WHEN THE ENGINE KNOWS NOTHING OF THE HANDLE THE WORLD ANSWERS "I DO NOT
    /// KNOW", never a guess.
    ///
    /// <para>
    /// A dead handle, or a dedicated server between reloads, probes as no bits.
    /// The property that matters is the one 2h stopped short for — an
    /// implementation that answered <c>true</c> to <c>IsStandalone</c> or handed
    /// back an identity frame would let <see cref="Hierarchy"/> make links against
    /// entities that are not there and compose children onto the origin.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEntityTheProbeDoesNotKnowIsOnNeitherHierarchy()
    {
        var world = World(out var engine);
        var handle = new EntityHandle(0x2A0001);

        Assert.False(world.IsStandalone(handle));
        Assert.False(world.IsBase(handle));
        Assert.False(world.CanPose(handle));

        // The answer came from the engine it was handed, so the refusals above
        // are the world reading "no bits", not the world never asking.
        Assert.Equal(3, engine.CountOf("Entities.ParentProbe"));
    }

    [Fact]
    public void AnEntityTheProbeDoesNotKnowHasNoFrame()
    {
        var world = World(out var engine);
        var handle = new EntityHandle(0x2A0001);

        engine.Returns("Entities.GetAttachmentFrame", false)
              .Returns("Entities.GetAttachmentFrame.origin", new Vector(7, 8, 9))
              .Returns("Entities.GetAttachmentFrame.angles", new QAngle(1, 2, 3));

        Assert.False(world.TryGetEntityFrame(handle, out Transform entity));
        Assert.False(world.TryGetAttachmentFrame(handle, 1, out Transform attachment));

        // And the out values are the identity rather than uninitialised, so a
        // caller that ignores the answer composes onto itself instead of onto
        // whatever the stack held -- or whatever the engine wrote into the outs
        // before saying no.
        Assert.Equal(Transform.Identity, entity);
        Assert.Equal(Transform.Identity, attachment);
        Assert.True(engine.Called("Entities.GetAttachmentFrame"), "the attachment was never asked for");
    }

    [Fact]
    public void AnEntityTheProbeDoesNotKnowHasNoAbsolutePositionRatherThanZero()
    {
        var world = World(out var engine);

        Assert.False(world.TryGetAbsolute(new EntityHandle(0x2A0001),
                                          out Vector origin, out QAngle angles));

        Assert.True(engine.Called("Entities.ParentProbe"), "the probe was never asked");
        Assert.False(engine.Called("Entities.GetOrigin"), "the origin was read for an entity the probe refused");
    }

    /// <summary>
    /// An empty name is refused before it reaches the engine at all. A
    /// <c>parentname</c> a mapper cleared is "no parent", and a find-by-name for
    /// "" is a walk that can only match an entity that has no targetname.
    /// </summary>
    [Fact]
    public void AnEmptyStandaloneNameFindsNothingAndAsksNothing()
    {
        var world = World(out var engine);

        engine.Returns("Entities.FindByName", new EntityHandle(0x2A0001));

        Assert.Equal(EntityHandle.None, world.FindStandaloneByName(string.Empty));
        Assert.False(engine.Called("Entities.FindByName"), "an empty name reached the engine");

        // The positive arm: a real name is put to the same engine.
        Assert.Equal(new EntityHandle(0x2A0001), world.FindStandaloneByName("door"));
        Assert.Equal(1, engine.CountOf("Entities.FindByName"));
    }

    //=========================================================================//
    // Two sides, two worlds.
    //=========================================================================//

    /// <summary>
    /// TWO COMPOSITIONS DO NOT SHARE AN ENGINE.
    ///
    /// <para>
    /// The world used to put every question to the static <c>Engine</c>, which
    /// follows whichever side is active — so two game instances in one process
    /// would have composed each other's children. Each world is built over its
    /// side's own areas, and every question and every write lands on that side
    /// and nowhere else.
    /// </para>
    /// </summary>
    /// <summary>
    /// VPhysicsInitStatic's move-parent arm (baseentity_shared.cpp): a child
    /// linked while it holds a static object is given a shadow in its place,
    /// made after the static one is gone. Found beside stock (dm_lockdown
    /// solidity census, 2026-10-27): plug_model2 held a shadow in stock and
    /// nothing here.
    /// </summary>
    [Fact]
    public void A_static_child_is_given_a_shadow_in_its_objects_place()
    {
        var world = World(out var engine);
        engine.Returns("EntityPhysics.PhysicsKind", 1).Returns("EntityPhysics.InitShadowPhysics", true);
        var child = new EntityHandle(0x2A0002);

        world.DetachPhysics(child);

        var order = engine.Calls.Select(c => c.Member).ToList();
        int destroyed = order.IndexOf("Entities.DestroyPhysics");
        int shadow = order.IndexOf("EntityPhysics.InitShadowPhysics");
        Assert.True(destroyed >= 0 && shadow > destroyed, string.Join(", ", order));
        var made = engine.Last("EntityPhysics.InitShadowPhysics")!.Value;
        Assert.Equal((child, false, false), (made.ArgAs<EntityHandle>(0), made.ArgAs<bool>(1), made.ArgAs<bool>(2)));
    }

    /// <summary>A simulated child's object is destroyed: the solver would fight its composition.</summary>
    [Fact]
    public void A_simulated_child_loses_its_object()
    {
        var world = World(out var engine);
        engine.Returns("EntityPhysics.PhysicsKind", 3);

        world.DetachPhysics(new EntityHandle(0x2A0003));

        Assert.Equal(1, engine.CountOf("Entities.DestroyPhysics"));
        Assert.False(engine.Called("EntityPhysics.InitShadowPhysics"));
    }

    [Fact]
    public void EachWorldAsksOnlyTheEngineItWasComposedWith()
    {
        var a = World(out var engineA, ParentProbe.Base);
        var b = World(out var engineB, ParentProbe.Standalone);
        var handle = new EntityHandle(0x2A0001);

        engineA.Returns("Entities.FindBaseByName", handle).Returns("EntityPhysics.PhysicsKind", 3);

        Assert.True(a.IsBase(handle), "world A did not read engine A's probe");
        Assert.False(a.IsStandalone(handle));
        Assert.True(b.IsStandalone(handle), "world B did not read engine B's probe");

        Assert.Equal(handle, a.FindBaseByName("door"));
        a.Remove(handle);
        a.DetachPhysics(handle);
        _ = a.BlockOf(handle);

        Assert.Equal(1, engineA.CountOf("Entities.Remove"));
        Assert.Equal(1, engineA.CountOf("Entities.DestroyPhysics"));
        Assert.Equal(1, engineA.CountOf("Entities.FindBaseByName"));
        Assert.Equal(1, engineA.CountOf("Entities.GetEngineBlock"));

        Assert.False(engineB.Called("Entities.Remove"), "world A's removal reached engine B");
        Assert.False(engineB.Called("Entities.DestroyPhysics"), "world A's detach reached engine B");
        Assert.False(engineB.Called("EntityPhysics.PhysicsKind"), "world A's detach asked engine B");
        Assert.False(engineB.Called("Entities.FindBaseByName"), "world A's search reached engine B");
        Assert.False(engineB.Called("Entities.GetEngineBlock"), "world A's block read reached engine B");
        Assert.Equal(1, engineB.CountOf("Entities.ParentProbe"));
    }

    //=========================================================================//
    // The block is not the blob.
    //=========================================================================//

    /// <summary>
    /// The source of one parenting file, from THIS repo. Missing is a failure:
    /// these facts once read a farm path that no longer existed, and skipped
    /// every time. <paramref name="dir"/> is the repo directory the file
    /// lives in: the three leave Abi for their own assemblies
    /// (plan_abi_hotreload Phase 1).
    /// </summary>
    private static string Source(string dir, string file)
    {
        string path = RepoLayout.Repo([.. dir.Split('/'), file]);

        Assert.True(File.Exists(path), $"{path} is missing");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// NOTHING IN PARENTING REACHES THE ENGINE BLOCK THROUGH
    /// <see cref="IEngineNetworking.GetEntityBlob"/>.
    ///
    /// <para>
    /// <b>This one is a text check because it has to be, and the alternative was
    /// tried.</b> The two calls answer the SAME MEMORY at DIFFERENT LENGTHS:
    /// <c>GetEntityBlob</c> is the networked payload, whose length is the
    /// entity's tier, and a class that declared no state of its own gets eight
    /// words of a 352-byte block. Every read or write past byte 32 is then
    /// refused, and refused <i>silently</i>, because a short span is a legal
    /// span.
    /// </para>
    ///
    /// <para>
    /// <b>No unit test can see it.</b> A recorded engine answers an empty span
    /// for both, so both refuse; reverting <c>ApplyTo</c> to the blob leaves
    /// every behavioural test green. It was found by running the game — every
    /// child in <c>dm_lockdown</c> standing still while <c>cs_sv_hierarchy</c>
    /// reported three healthy links — and this is what stops it coming back,
    /// because the next person to write <c>GetEntityBlob</c> here will be doing
    /// the obvious thing.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("src/server/interop", "Hierarchy.cs")]
    [InlineData("src/server/interop", "EngineHierarchyWorld.cs")]
    [InlineData("src/server/interop", "HierarchyDriver.cs")]
    public void ParentingReachesTheBlockThroughGetEngineBlockAndNeverTheBlob(string dir, string file)
    {
        // The open paren is what makes this a CALL rather than a mention: the
        // three files talk about GetEntityBlob at length, because saying why it
        // is the wrong call is how the distinction stays understood.
        Assert.DoesNotContain("GetEntityBlob(", Source(dir, file), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the block getter IS reached, so the fact above is not satisfied by a
    /// file that stopped touching the block at all. The driver reaches it
    /// through its world, which is the side's.
    /// </summary>
    [Theory]
    [InlineData("src/server/interop", "Hierarchy.cs", "entities.GetEngineBlock(")]
    [InlineData("src/server/interop", "EngineHierarchyWorld.cs", "_entities.GetEngineBlock(")]
    [InlineData("src/server/interop", "HierarchyDriver.cs", "_world.BlockOf(")]
    public void EachOfThoseThreeFilesDoesReachTheBlock(string dir, string file, string call)
        => Assert.Contains(call, Source(dir, file), StringComparison.Ordinal);

    //=========================================================================//
    // The compose depth.
    //=========================================================================//

    [Fact]
    public void NothingIsBeingComposedBeforeAnythingHappens()
    {
        var world = World(out _);

        Assert.Equal(0, world.ComposeDepth);
        Assert.False(world.IsComposing);
    }

    /// <summary>
    /// THE DEPTH IS BALANCED EVEN WHEN THE WRITE IS REFUSED.
    ///
    /// <para>
    /// <c>WriteComposed</c> returns early when the entity has no blob — the
    /// ordinary case for an entity being torn down — and an increment that had
    /// already happened would leave <see cref="EngineHierarchyWorld.IsComposing"/>
    /// stuck true for the rest of the map. Every <c>ParentMoved</c> after it would
    /// be suppressed as a re-entry, so nothing on the hierarchy would ever follow
    /// its parent again, and nothing would print.
    /// </para>
    /// </summary>
    [Fact]
    public void ARefusedWriteLeavesTheComposeDepthWhereItWas()
    {
        var world = World(out var engine);

        engine.Returns("Entities.GetOrigin", new Vector(4, 5, 6));

        world.WriteComposed(new EntityHandle(0x2A0001), new Vector(1, 2, 3),
                            QAngle.Zero, Transform.Identity);

        Assert.Equal(0, world.ComposeDepth);
        Assert.False(world.IsComposing);

        // The write was attempted against the side's block and refused there:
        // nothing was announced, and no position change was dispatched.
        Assert.True(engine.Called("Entities.GetEngineBlock"), "the block was never asked for");
        Assert.False(engine.Called("Networking.MarkEntityBlockChanged"), "a refused write was announced");
        Assert.False(engine.Called("Entities.PositionChanged"), "a refused write was dispatched");
    }

    //=========================================================================//
    // The composed write's shape, which no recorded engine can exercise.
    //=========================================================================//

    /// <summary>
    /// THE COMPOSED WRITE IS A BLOCK WRITE **AND** A POSITION-CHANGED DISPATCH,
    /// AND THE PREVIOUS ORIGIN IS READ FIRST.
    ///
    /// <para>
    /// <b>A text check, because a recorded engine has no block to write into
    /// and the claim is worth pinning.</b> Each half is silent when it goes:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>without the dispatch, a composed prop's blob moves and its SPATIAL
    /// PARTITION entry does not — so traces and collisions keep meeting it where
    /// it used to be, and it fires no trigger it is carried into;</item>
    /// <item>without the block write, the dispatch announces a position nobody
    /// wrote;</item>
    /// <item>with the previous origin read AFTER the write, it is the new origin,
    /// so <c>TouchTriggers</c> sees no edge at all and a decoration can be carried
    /// straight through a trigger without touching it.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void TheComposedWriteWritesTheBlockThenSaysThePositionChanged()
    {
        string source = Source("src/server/interop", "EngineHierarchyWorld.cs");
        int body = source.IndexOf("public void WriteComposed(", StringComparison.Ordinal);

        Assert.True(body > 0, "EngineHierarchyWorld.WriteComposed is not in the file");

        string method = source[body..];

        int previous = method.IndexOf("Vector previous = _entities.GetOrigin(child);", StringComparison.Ordinal);
        int write = method.IndexOf("HierarchyBlockWrite.ApplyTo(_entities, _networking, child,", StringComparison.Ordinal);
        int dispatch = method.IndexOf("_entities.PositionChanged(child, previous);", StringComparison.Ordinal);

        Assert.True(previous >= 0, "the previous origin is no longer read");
        Assert.True(write >= 0, "the block write is no longer HierarchyBlockWrite.ApplyTo");
        Assert.True(dispatch >= 0, "the native position funnel is no longer told");

        Assert.True(previous < write, "the previous origin is read AFTER the write");
        Assert.True(write < dispatch, "the position is announced BEFORE it is written");
    }
}
