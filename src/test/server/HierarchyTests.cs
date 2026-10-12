using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PARENTING, IN MANAGED CODE — plan_entity.md step 2h.
///
/// <para>
/// Three claims the step's gate names, and each of them is a thing that used to
/// be checkable only by watching a map:
/// </para>
///
/// <list type="number">
/// <item><b>a child follows its parent through a ROTATION</b> — not just a
/// translation, which is the half a composition that dropped the parent's
/// rotation entirely would still pass;</item>
/// <item><b>a cycle is refused BY NAME</b>, out loud, rather than being made and
/// recursed into;</item>
/// <item><b>removal of a parent detaches its children in ONE frame</b>, on both
/// hierarchies.</item>
/// </list>
///
/// <para>
/// The composition half is asserted against a table taken from the mathlib the
/// port replaces (<c>sourcesharp/test/parent_compose_cases.txt</c>, pinned to
/// native by <c>parent_compose_test.cpp</c>), so it is a comparison and not a
/// self-portrait. The registry half is driven through
/// <see cref="IHierarchyWorld"/> by <see cref="FakeWorld"/>, which is what makes
/// every one of these a fact rather than something somebody once watched happen.
/// </para>
/// </summary>
public sealed class HierarchyTests
{
    //=========================================================================//
    // The world the registry is driven against.
    //=========================================================================//

    /// <summary>
    /// A world of entities that stand still until something moves them.
    ///
    /// <para>
    /// It is NOT a stand-in for the composition arithmetic — that is
    /// <see cref="Hierarchy"/>'s own and is compared with native through the
    /// golden table. What this stands in for is the four things parenting asks of
    /// the engine: who is alive, where they are, what an attachment frame is, and
    /// "write this down". Each is a value the test sets, so a fact can state a
    /// situation rather than arrange one.
    /// </para>
    /// </summary>
    private sealed class FakeWorld : IHierarchyWorld
    {
        internal sealed class Ent
        {
            internal string Name = string.Empty;
            internal bool OnBaseHierarchy;
            internal bool Alive = true;
            internal bool Poses;
            internal Vector Origin;
            internal QAngle Angles;
            internal readonly Dictionary<string, int> AttachmentsByName = new();
            internal readonly Dictionary<int, (Vector Origin, QAngle Angles)> Attachments = new();
        }

        private readonly Dictionary<EntityHandle, Ent> _ents = new();
        private uint _next = 1;

        internal readonly List<string> Warnings = new();
        internal readonly List<EntityHandle> Removed = new();
        internal readonly List<EntityHandle> Written = new();

        /// <summary>
        /// A handle with a non-zero serial, so a fact cannot pass by accident on an
        /// implementation that compared entry indices and ignored the serial.
        /// </summary>
        internal EntityHandle Spawn(string name, Vector origin, QAngle angles,
                                    bool onBase = false, bool poses = false)
        {
            uint entry = _next++;
            var handle = new EntityHandle(entry | (0x2A << 16));

            _ents[handle] = new Ent
            {
                Name = name,
                Origin = origin,
                Angles = angles,
                OnBaseHierarchy = onBase,
                Poses = poses,
            };

            return handle;
        }

        internal Ent At(EntityHandle handle) => _ents[handle];

        internal void Kill(EntityHandle handle) => _ents[handle].Alive = false;

        internal void MoveTo(EntityHandle handle, Vector origin, QAngle angles)
        {
            _ents[handle].Origin = origin;
            _ents[handle].Angles = angles;
        }

        private Ent? Live(EntityHandle handle)
            => _ents.TryGetValue(handle, out var e) && e.Alive ? e : null;

        public bool IsStandalone(EntityHandle handle)
        {
            var e = Live(handle);
            return e is not null && !e.OnBaseHierarchy;
        }

        public bool IsBase(EntityHandle handle)
        {
            var e = Live(handle);
            return e is not null && e.OnBaseHierarchy;
        }

        public bool CanPose(EntityHandle handle) => Live(handle)?.Poses ?? false;

        public bool TryGetEntityFrame(EntityHandle handle, out Transform frame)
        {
            var e = Live(handle);

            if (e is null)
            {
                frame = Transform.Identity;
                return false;
            }

            frame = Transform.FromOriginAngles(e.Origin, e.Angles);
            return true;
        }

        public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame)
        {
            frame = Transform.Identity;

            var e = Live(handle);
            if (e is null || !e.Attachments.TryGetValue(attachment, out var placed))
                return false;

            frame = Transform.FromOriginAngles(placed.Origin, placed.Angles);
            return true;
        }

        public int LookupAttachment(EntityHandle handle, string attachmentName)
        {
            var e = Live(handle);
            return e is not null && e.AttachmentsByName.TryGetValue(attachmentName, out int i) ? i : 0;
        }

        public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles)
        {
            var e = Live(handle);

            if (e is null)
            {
                origin = Vector.Zero;
                angles = QAngle.Zero;
                return false;
            }

            origin = e.Origin;
            angles = e.Angles;
            return true;
        }

        public void WriteComposed(EntityHandle child, Vector origin, QAngle angles,
                                  Transform childToWorld)
        {
            Written.Add(child);

            var e = Live(child);
            if (e is null)
                return;

            e.Origin = origin;
            e.Angles = angles;
        }

        public EntityHandle FindStandaloneByName(string name)
            => _ents.FirstOrDefault(kv => kv.Value.Alive && !kv.Value.OnBaseHierarchy
                                          && kv.Value.Name == name).Key is var h && h.Value != 0
                ? h
                : EntityHandle.None;

        public EntityHandle FindBaseByName(string name)
            => _ents.FirstOrDefault(kv => kv.Value.Alive && kv.Value.OnBaseHierarchy
                                          && kv.Value.Name == name).Key is var h && h.Value != 0
                ? h
                : EntityHandle.None;

        public string NameOf(EntityHandle handle)
            => _ents.TryGetValue(handle, out var e) ? e.Name : string.Empty;

        public void Remove(EntityHandle handle)
        {
            Removed.Add(handle);

            if (_ents.TryGetValue(handle, out var e))
                e.Alive = false;
        }

        /// <summary>
        /// Every child whose solver object a link took away, in order. A LIST and
        /// not a flag, because the two facts worth stating about it are "a
        /// refusal takes nothing" and "one link takes it once".
        /// </summary>
        internal readonly List<EntityHandle> PhysicsDetached = new();

        public void DetachPhysics(EntityHandle child) => PhysicsDetached.Add(child);

        public void Warn(string message) => Warnings.Add(message);
    }

    private static (Hierarchy, FakeWorld) NewWorld()
    {
        var world = new FakeWorld();
        return (new Hierarchy(world), world);
    }

    //=========================================================================//
    // 1. A CHILD FOLLOWS ITS PARENT THROUGH A ROTATION.
    //=========================================================================//

    /// <summary>
    /// THE GATE'S FIRST CLAIM, in the smallest form that a rotation-blind
    /// composition fails: a child ten units in front of its parent, and the parent
    /// yaws ninety degrees. A composition that added the offset as a plain vector
    /// would leave the child at (10, 0, 0).
    /// </summary>
    [Fact]
    public void AChildFollowsItsParentThroughARotation()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", new Vector(0f, 0f, 0f), QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        Assert.True(hierarchy.SetParent(child, parent).Granted);

        world.MoveTo(parent, new Vector(0f, 0f, 0f), new QAngle(0f, 90f, 0f));
        Assert.Equal(1, hierarchy.ParentMoved(parent));

        Assert.Equal(0f, world.At(child).Origin.X, 3);
        Assert.Equal(10f, world.At(child).Origin.Y, 3);
        Assert.Equal(0f, world.At(child).Origin.Z, 3);
    }

    /// <summary>
    /// And its FACING turns with it. Half of parenting is where a child is and the
    /// other half is which way it points, and a composition can get the first
    /// right and the second wrong — the offset applied to the origin and the
    /// child's own angles left alone is exactly that shape.
    /// </summary>
    [Fact]
    public void AChildTurnsWithItsParent()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);

        world.MoveTo(parent, Vector.Zero, new QAngle(0f, 90f, 0f));
        hierarchy.ParentMoved(parent);

        Assert.Equal(90f, world.At(child).Angles.Yaw, 3);
    }

    /// <summary>
    /// A rotation about a parent that has ALSO moved. Kept separate from the pure
    /// rotation because a composition that applied the rotation to the world
    /// position rather than to the offset passes the pure case and fails this one.
    /// </summary>
    [Fact]
    public void AChildFollowsARotationAboutAMovedParent()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);

        world.MoveTo(parent, new Vector(100f, -50f, 25f), new QAngle(0f, 90f, 0f));
        hierarchy.ParentMoved(parent);

        Assert.Equal(100f, world.At(child).Origin.X, 3);
        Assert.Equal(-40f, world.At(child).Origin.Y, 3);
        Assert.Equal(25f, world.At(child).Origin.Z, 3);
    }

    /// <summary>
    /// A GRANDCHILD follows too, in the same call. Depth falls out of the
    /// recursion rather than needing a pass of its own, which is the property that
    /// makes eager composition ordering-free — and it is the property a
    /// non-recursive composition would lose silently, since the child would still
    /// be right.
    /// </summary>
    [Fact]
    public void AGrandchildFollowsTheRotationInTheSameCall()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);
        EntityHandle grandchild = world.Spawn("badge", new Vector(20f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);
        hierarchy.SetParent(grandchild, child);

        world.MoveTo(parent, Vector.Zero, new QAngle(0f, 90f, 0f));
        hierarchy.ParentMoved(parent);

        Assert.Equal(0f, world.At(grandchild).Origin.X, 3);
        Assert.Equal(20f, world.At(grandchild).Origin.Y, 3);
    }

    /// <summary>
    /// The offset is measured ONCE and never re-derived from the composition's own
    /// output. A version that recomputed it each time decays to the identity within
    /// a few frames, because each composition's output is the next derivation's
    /// input — so the child would creep towards its parent's origin over a hundred
    /// rotations and be correct after one.
    /// </summary>
    [Fact]
    public void AHundredRotationsDoNotDecayTheOffset()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);

        for (int i = 1; i <= 100; i++)
        {
            world.MoveTo(parent, Vector.Zero, new QAngle(0f, i * 3.6f, 0f));
            hierarchy.ParentMoved(parent);
        }

        // 100 * 3.6 = 360 degrees, which is where it started.
        Assert.Equal(10f, world.At(child).Origin.X, 2);
        Assert.Equal(0f, world.At(child).Origin.Y, 2);
    }

    /// <summary>
    /// A child hung from an ATTACHMENT is composed against the attachment's frame
    /// and not the parent's origin. The two are the same place only on a model
    /// whose attachment sits at the origin, which is no model anybody ships.
    /// </summary>
    [Fact]
    public void AnAttachmentParentedChildHangsFromTheAttachmentFrame()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle gun = world.Spawn("gun", Vector.Zero, QAngle.Zero, poses: true);
        world.At(gun).AttachmentsByName["muzzle"] = 3;
        world.At(gun).Attachments[3] = (new Vector(0f, 0f, 40f), QAngle.Zero);

        EntityHandle flash = world.Spawn("flash", new Vector(0f, 0f, 40f), QAngle.Zero);

        Assert.True(hierarchy.SetParent(flash, gun, 3).Granted);

        // The muzzle swings out along +Y without the gun's own origin moving.
        world.At(gun).Attachments[3] = (new Vector(0f, 25f, 40f), QAngle.Zero);
        hierarchy.ParentMoved(gun);

        Assert.Equal(25f, world.At(flash).Origin.Y, 3);
        Assert.Equal(40f, world.At(flash).Origin.Z, 3);
    }

    //=========================================================================//
    // The golden table: the composition against the mathlib it replaces.
    //=========================================================================//

    private readonly record struct ComposeCase(
        string Name,
        Vector ParentOrigin, QAngle ParentAngles,
        Vector ChildOrigin, QAngle ChildAngles,
        Vector MovedOrigin, QAngle MovedAngles,
        Vector ExpectedOrigin, QAngle ExpectedAngles);

    /// <summary>
    /// The table is not a fixture this file maintains — it is a record of native,
    /// found on disk and pinned to mathlib by <c>parent_compose_test.cpp</c>. Not
    /// finding it must be a FAILURE and not an empty pass: a loop over nothing is
    /// green.
    /// </summary>
    private static List<ComposeCase> LoadComposeCases()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("sourcesharp", "test", "parent_compose_cases.txt"));
        Assert.True(path is not null, "the golden table is found next to the tests");

        var cases = new List<ComposeCase>();

        foreach (string line in File.ReadAllLines(path!))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 25)
                continue;

            float N(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);

            cases.Add(new ComposeCase(
                f[0],
                new Vector(N(1), N(2), N(3)),    new QAngle(N(4), N(5), N(6)),
                new Vector(N(7), N(8), N(9)),    new QAngle(N(10), N(11), N(12)),
                new Vector(N(13), N(14), N(15)), new QAngle(N(16), N(17), N(18)),
                new Vector(N(19), N(20), N(21)), new QAngle(N(22), N(23), N(24))));
        }

        return cases;
    }

    /// <summary>
    /// The table has to be there and to hold every row, or the comparison below is
    /// a loop over nothing.
    /// </summary>
    [Fact]
    public void TheComposeTableIsThereAndHasEveryRow()
    {
        Assert.Equal(12, LoadComposeCases().Count);
    }

    /// <summary>
    /// EVERY ROW, through the port's own two halves: the offset measured once at
    /// link time, and the composition that reads it.
    /// </summary>
    [Fact]
    public void EveryComposeRowIsReproducedByTheManagedComposition()
    {
        foreach (ComposeCase c in LoadComposeCases())
        {
            Transform parentToWorld = Transform.FromOriginAngles(c.ParentOrigin, c.ParentAngles);
            Transform childToParent = Hierarchy.MeasureOffset(parentToWorld, c.ChildOrigin, c.ChildAngles);

            Transform moved = Transform.FromOriginAngles(c.MovedOrigin, c.MovedAngles);
            Transform composed = Hierarchy.Compose(moved, childToParent);

            Vector origin = composed.Translation;
            QAngle angles = composed.ToAngles();

            Assert.Equal(c.ExpectedOrigin.X, origin.X, 2);
            Assert.Equal(c.ExpectedOrigin.Y, origin.Y, 2);
            Assert.Equal(c.ExpectedOrigin.Z, origin.Z, 2);

            Assert.Equal(c.ExpectedAngles.Pitch, angles.Pitch, 2);
            Assert.Equal(c.ExpectedAngles.Yaw, angles.Yaw, 2);
            Assert.Equal(c.ExpectedAngles.Roll, angles.Roll, 2);
        }
    }

    /// <summary>
    /// And every row driven through the REGISTRY rather than through the two
    /// static halves, so the table checks what a map actually exercises: a link
    /// made, a parent moved, a child written.
    /// </summary>
    [Fact]
    public void EveryComposeRowIsReproducedThroughTheRegistry()
    {
        foreach (ComposeCase c in LoadComposeCases())
        {
            var (hierarchy, world) = NewWorld();

            EntityHandle parent = world.Spawn("parent", c.ParentOrigin, c.ParentAngles);
            EntityHandle child = world.Spawn("child", c.ChildOrigin, c.ChildAngles);

            Assert.True(hierarchy.SetParent(child, parent).Granted, c.Name);

            world.MoveTo(parent, c.MovedOrigin, c.MovedAngles);
            hierarchy.ParentMoved(parent);

            Assert.Equal(c.ExpectedOrigin.X, world.At(child).Origin.X, 2);
            Assert.Equal(c.ExpectedOrigin.Y, world.At(child).Origin.Y, 2);
            Assert.Equal(c.ExpectedOrigin.Z, world.At(child).Origin.Z, 2);
        }
    }

    /// <summary>
    /// THE TABLE IS NOT A TABLE OF TRANSLATIONS, and this is the managed half of
    /// the same guard the gtest keeps: recompute every row the way a
    /// rotation-blind composition would, and require that most of them come out
    /// different. If somebody trims the table down to the easy cases, this fails
    /// rather than the comparison above quietly becoming a check that cannot fail.
    /// </summary>
    [Fact]
    public void MostComposeRowsWouldFailARotationBlindComposition()
    {
        int differ = 0;

        foreach (ComposeCase c in LoadComposeCases())
        {
            Vector blind = c.MovedOrigin + (c.ChildOrigin - c.ParentOrigin);

            bool sameOrigin = MathF.Abs(blind.X - c.ExpectedOrigin.X) < 0.01f
                           && MathF.Abs(blind.Y - c.ExpectedOrigin.Y) < 0.01f
                           && MathF.Abs(blind.Z - c.ExpectedOrigin.Z) < 0.01f;

            bool sameAngles = MathF.Abs(c.ChildAngles.Pitch - c.ExpectedAngles.Pitch) < 0.01f
                           && MathF.Abs(c.ChildAngles.Yaw - c.ExpectedAngles.Yaw) < 0.01f
                           && MathF.Abs(c.ChildAngles.Roll - c.ExpectedAngles.Roll) < 0.01f;

            if (!sameOrigin || !sameAngles)
                differ++;
        }

        Assert.True(differ >= 10,
            $"only {differ} of the table's rows distinguish a rotating composition from a " +
            "translating one; the table has lost the rows that make it a check");
    }

    //=========================================================================//
    // 2. A CYCLE IS REFUSED, BY NAME.
    //=========================================================================//

    /// <summary>
    /// THE GATE'S SECOND CLAIM. A three-link chain closed back on itself is
    /// refused, and the refusal NAMES BOTH ENDS — a mapper reading a console needs
    /// to know which two entities, and "refused" on its own is a silent no-op with
    /// a return value (§7).
    /// </summary>
    [Fact]
    public void ACycleIsRefusedAndTheRefusalNamesBothEnds()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle a = world.Spawn("alpha", Vector.Zero, QAngle.Zero);
        EntityHandle b = world.Spawn("bravo", new Vector(10f, 0f, 0f), QAngle.Zero);
        EntityHandle c = world.Spawn("charlie", new Vector(20f, 0f, 0f), QAngle.Zero);

        Assert.True(hierarchy.SetParent(b, a).Granted);
        Assert.True(hierarchy.SetParent(c, b).Granted);

        ParentResult result = hierarchy.SetParent(a, c);

        Assert.Equal(ParentRefusal.Cycle, result.Refusal);
        Assert.Contains("alpha", result.Message);
        Assert.Contains("charlie", result.Message);
        Assert.Contains("cycle", result.Message);
    }

    /// <summary>An entity parented to itself is the one-link cycle.</summary>
    [Fact]
    public void AnEntityCannotBeItsOwnParent()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle a = world.Spawn("alpha", Vector.Zero, QAngle.Zero);

        Assert.Equal(ParentRefusal.Cycle, hierarchy.SetParent(a, a).Refusal);
    }

    /// <summary>
    /// A refused link leaves the registry exactly as it was. A refusal that had
    /// already dropped the child's existing link would answer "no" and unparent it,
    /// which is a worse outcome than the cycle it was avoiding.
    /// </summary>
    [Fact]
    public void ARefusedCycleLeavesTheExistingLinkAlone()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle a = world.Spawn("alpha", Vector.Zero, QAngle.Zero);
        EntityHandle b = world.Spawn("bravo", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(b, a);
        Assert.Equal(ParentRefusal.Cycle, hierarchy.SetParent(a, b).Refusal);

        Assert.True(hierarchy.IsParented(b));
        Assert.False(hierarchy.IsParented(a));
        Assert.Equal(1, hierarchy.LinkCount);
    }

    /// <summary>
    /// AND A CYCLE ARRIVING AS A MAP'S `parentname` IS REFUSED TOO — which is the
    /// path a mapper can actually reach. The refusal is printed rather than
    /// returned, because nothing is left to return it to by then.
    /// </summary>
    [Fact]
    public void ACycleIsRefusedWhenItArrivesAsADeferredParentName()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle a = world.Spawn("alpha", Vector.Zero, QAngle.Zero);
        EntityHandle b = world.Spawn("bravo", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParentName(a, "bravo");
        hierarchy.SetParentName(b, "alpha");

        hierarchy.ResolveParentNames();

        // Exactly one of the two links survives; the second would close the loop.
        Assert.Equal(1, hierarchy.LinkCount);
        Assert.Contains(world.Warnings, w => w.Contains("cycle"));
    }

    /// <summary>
    /// The cycle walk is BOUNDED as well as tested, so a registry that somehow
    /// already loops reports rather than hangs. Built by hand, because no public
    /// path can produce it.
    /// </summary>
    [Fact]
    public void ARegistryThatAlreadyLoopsIsReportedRatherThanWalkedForEver()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle a = world.Spawn("alpha", Vector.Zero, QAngle.Zero);
        EntityHandle b = world.Spawn("bravo", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(b, a);

        // The loop, forced: a's link is rewritten to name b as its parent without
        // going through SetParent, which is what would have refused it.
        hierarchy.SetParent(a, world.Spawn("temp", Vector.Zero, QAngle.Zero));
        hierarchy.LinkFor(a)!.Parent = b;

        EntityHandle c = world.Spawn("charlie", Vector.Zero, QAngle.Zero);

        Assert.True(hierarchy.WouldCycle(c, a));
    }

    //=========================================================================//
    // 3. REMOVAL DETACHES THE CHILDREN, IN ONE FRAME.
    //=========================================================================//

    /// <summary>
    /// THE GATE'S THIRD CLAIM, on this hierarchy: a parent going takes its link
    /// and its children with it, in the call that announces the removal — not on
    /// the next frame, and not when something happens to walk the registry.
    /// </summary>
    [Fact]
    public void RemovingAParentDetachesItsChildrenInTheSameCall()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle first = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);
        EntityHandle second = world.Spawn("badge", new Vector(0f, 10f, 0f), QAngle.Zero);

        hierarchy.SetParent(first, parent);
        hierarchy.SetParent(second, parent);
        Assert.Equal(2, hierarchy.LinkCount);

        world.Kill(parent);
        Assert.Equal(2, hierarchy.EntityRemoved(parent));

        Assert.Equal(0, hierarchy.LinkCount);
        Assert.Contains(first, world.Removed);
        Assert.Contains(second, world.Removed);
    }

    /// <summary>
    /// A child going takes only its own link. The other direction of the same walk,
    /// and the one that would leave an entry naming a dead child behind to be
    /// matched by whatever is given the slot next.
    /// </summary>
    [Fact]
    public void RemovingAChildDropsItsOwnLinkAndNothingElse()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle first = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);
        EntityHandle second = world.Spawn("badge", new Vector(0f, 10f, 0f), QAngle.Zero);

        hierarchy.SetParent(first, parent);
        hierarchy.SetParent(second, parent);

        world.Kill(first);
        Assert.Equal(0, hierarchy.EntityRemoved(first));

        Assert.Equal(1, hierarchy.LinkCount);
        Assert.DoesNotContain(second, world.Removed);
    }

    /// <summary>
    /// A PARENT ON THE OTHER HIERARCHY ANNOUNCES NOTHING, so the sweep is where its
    /// death is noticed — and ONE sweep is enough. The count is the claim: a second
    /// <see cref="Hierarchy.RunFrame"/> finding the child still linked would mean a
    /// frame in which a decoration stood beside a parent that had gone.
    /// </summary>
    [Fact]
    public void ADeadSdkParentDetachesItsChildrenInOneFrame()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("door", Vector.Zero, QAngle.Zero, onBase: true);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        Assert.True(hierarchy.SetParent(child, parent).Granted);
        Assert.Equal(1, hierarchy.LinkCount);

        world.Kill(parent);

        hierarchy.RunFrame();

        Assert.Equal(0, hierarchy.LinkCount);
        Assert.Contains(child, world.Removed);
    }

    /// <summary>
    /// The pending attachment name goes with the entity too. An entity can carry
    /// one and no link at all — that is the whole window the registry exists for —
    /// and leaving it behind would let a later occupant of the slot pick up a
    /// mapper's attachment name it never asked for.
    /// </summary>
    [Fact]
    public void RemovingAnEntityDropsItsPendingAttachmentName()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle child = world.Spawn("flash", Vector.Zero, QAngle.Zero);

        hierarchy.SetParentAttachmentName(child, "muzzle");
        Assert.Equal(1, hierarchy.PendingAttachmentCount);

        hierarchy.EntityRemoved(child);

        Assert.Equal(0, hierarchy.PendingAttachmentCount);
    }

    /// <summary>
    /// A level ending empties both registries. Called before the entities are
    /// deleted, so no removal finds a child to orphan and nothing is queued for a
    /// deferred delete that nothing will drain.
    /// </summary>
    [Fact]
    public void ALevelShutdownEmptiesBothRegistries()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);
        hierarchy.SetParentAttachmentName(world.Spawn("flash", Vector.Zero, QAngle.Zero), "muzzle");

        hierarchy.LevelShutdown();

        Assert.Equal(0, hierarchy.LinkCount);
        Assert.Equal(0, hierarchy.PendingAttachmentCount);
        Assert.Empty(world.Removed);
    }

    //=========================================================================//
    // Deferred parent names.
    //=========================================================================//

    /// <summary>
    /// A `parentname` written during the parse names an entity that may not exist
    /// yet, so it is recorded UNRESOLVED and finished later. Before the resolve
    /// pass the child is not parented to anything.
    /// </summary>
    [Fact]
    public void ADeferredParentNameIsNotALinkUntilItIsResolved()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParentName(child, "plug");

        Assert.Equal(1, hierarchy.LinkCount);
        Assert.False(hierarchy.LinkFor(child)!.Resolved);
        Assert.False(hierarchy.IsParented(child));
    }

    /// <summary>
    /// And the resolve pass finishes it against an entity that has since spawned,
    /// measuring the offset from where the two of them ended up.
    /// </summary>
    [Fact]
    public void TheResolvePassTurnsANameIntoALinkAndMeasuresTheOffset()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);
        hierarchy.SetParentName(child, "plug");

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);

        Assert.Equal(1, hierarchy.ResolveParentNames());
        Assert.True(hierarchy.IsParented(child));

        world.MoveTo(parent, Vector.Zero, new QAngle(0f, 90f, 0f));
        hierarchy.ParentMoved(parent);

        Assert.Equal(10f, world.At(child).Origin.Y, 3);
    }

    /// <summary>
    /// THIS HIERARCHY FIRST. Two entities of different kinds may carry the same
    /// targetname, and a map that parents to a name meaning both wants the one it
    /// can see.
    /// </summary>
    [Fact]
    public void AnAmbiguousParentNameResolvesToThisHierarchy()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle onBase = world.Spawn("shared", Vector.Zero, QAngle.Zero, onBase: true);
        EntityHandle onThisSide = world.Spawn("shared", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParentName(child, "shared");
        hierarchy.ResolveParentNames();

        Assert.Equal(onThisSide, hierarchy.LinkFor(child)!.Parent);
        Assert.False(hierarchy.LinkFor(child)!.ParentIsBase);
        Assert.NotEqual(onBase, hierarchy.LinkFor(child)!.Parent);
    }

    /// <summary>
    /// A name no entity answers to is REPORTED and the entry is dropped, rather
    /// than being kept as a link that will never resolve.
    /// </summary>
    [Fact]
    public void AParentNameNobodyAnswersToIsReportedAndDropped()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);
        hierarchy.SetParentName(child, "nobody");

        Assert.Equal(0, hierarchy.ResolveParentNames());
        Assert.Equal(0, hierarchy.LinkCount);
        Assert.Contains(world.Warnings, w => w.Contains("nobody") && w.Contains("cap"));
    }

    /// <summary>
    /// `parentattachment` MAY ARRIVE FIRST. A map writes its keys in whatever order
    /// Hammer wrote them, so the attachment name is held on its own until there is
    /// a link to put it in — and this is the order that is wrong on an
    /// implementation that stored it in the link.
    /// </summary>
    [Fact]
    public void AnAttachmentNameWrittenBeforeTheParentNameIsStillUsed()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle flash = world.Spawn("flash", new Vector(0f, 0f, 40f), QAngle.Zero);

        hierarchy.SetParentAttachmentName(flash, "muzzle");
        hierarchy.SetParentName(flash, "gun");

        EntityHandle gun = world.Spawn("gun", Vector.Zero, QAngle.Zero, poses: true);
        world.At(gun).AttachmentsByName["muzzle"] = 3;
        world.At(gun).Attachments[3] = (new Vector(0f, 0f, 40f), QAngle.Zero);

        hierarchy.ResolveParentNames();

        Assert.Equal(3, hierarchy.LinkFor(flash)!.ParentAttachment);
    }

    /// <summary>
    /// The attachment name is TAKEN once. An entry left behind would be picked up
    /// by a later re-parent that never asked for it.
    /// </summary>
    [Fact]
    public void AnAttachmentNameIsTakenByTheFirstLinkAndNotTheSecond()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle flash = world.Spawn("flash", Vector.Zero, QAngle.Zero);

        hierarchy.SetParentAttachmentName(flash, "muzzle");
        hierarchy.SetParentName(flash, "gun");

        Assert.Equal(0, hierarchy.PendingAttachmentCount);
        Assert.Equal("muzzle", hierarchy.LinkFor(flash)!.ParentAttachmentName);

        hierarchy.SetParentName(flash, "other");
        Assert.Equal(string.Empty, hierarchy.LinkFor(flash)!.ParentAttachmentName);
    }

    /// <summary>
    /// An attachment the model does not carry falls back to the parent's origin and
    /// SAYS SO. Refusing the link would leave a decoration standing in the map
    /// while its parent drives away, which is the worse of the two answers.
    /// </summary>
    [Fact]
    public void AnAttachmentTheModelDoesNotHaveFallsBackToTheOriginOutLoud()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle gun = world.Spawn("gun", Vector.Zero, QAngle.Zero, poses: true);
        EntityHandle flash = world.Spawn("flash", new Vector(1f, 0f, 0f), QAngle.Zero);

        Assert.True(hierarchy.SetParent(flash, gun, 7).Granted);

        Assert.Equal(0, hierarchy.LinkFor(flash)!.ParentAttachment);
        Assert.Contains(world.Warnings, w => w.Contains("attachment 7"));
    }

    /// <summary>An empty `parentname` is a mapper clearing the field: no parent.</summary>
    [Fact]
    public void AnEmptyParentNameClearsTheLink()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);
        hierarchy.SetParentName(child, string.Empty);

        Assert.Equal(0, hierarchy.LinkCount);
    }

    //=========================================================================//
    // One parent at a time; the two frames.
    //=========================================================================//

    /// <summary>
    /// Re-parenting replaces the link rather than adding a second. Two entries for
    /// one child would compose it twice from two different frames, the last one to
    /// run winning — which is whichever order the collection happens to be in.
    /// </summary>
    [Fact]
    public void ReparentingReplacesTheLinkRatherThanAddingOne()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle first = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle second = world.Spawn("socket", new Vector(50f, 0f, 0f), QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, first);
        hierarchy.SetParent(child, second);

        Assert.Equal(1, hierarchy.LinkCount);
        Assert.Equal(second, hierarchy.LinkFor(child)!.Parent);
    }

    /// <summary>
    /// A child that moves ITSELF keeps the new place and re-measures its offset —
    /// "put this here" said of a parented entity means here in the world, once, and
    /// relative to the parent from then on.
    /// </summary>
    [Fact]
    public void AChildThatMovesItselfReMeasuresItsOffset()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);

        world.MoveTo(child, new Vector(0f, 0f, 30f), QAngle.Zero);
        hierarchy.ChildMoved(child);

        world.MoveTo(parent, Vector.Zero, new QAngle(0f, 90f, 0f));
        hierarchy.ParentMoved(parent);

        // The offset is now straight up, which a yaw does not move.
        Assert.Equal(0f, world.At(child).Origin.X, 3);
        Assert.Equal(0f, world.At(child).Origin.Y, 3);
        Assert.Equal(30f, world.At(child).Origin.Z, 3);
    }

    /// <summary>
    /// The two frames round-trip. A place converted into the parent's frame and
    /// back is the place it started from, which is the property every caller that
    /// integrates a parented entity in local space relies on.
    /// </summary>
    [Fact]
    public void WorldToLocalAndBackIsTheSamePlace()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", new Vector(20f, -5f, 3f), new QAngle(10f, 35f, -8f));
        EntityHandle child = world.Spawn("cap", new Vector(30f, 12f, 9f), new QAngle(0f, 15f, 0f));

        hierarchy.SetParent(child, parent);

        var origin = new Vector(44f, -13f, 27f);
        var angles = new QAngle(5f, -60f, 12f);

        Assert.True(hierarchy.WorldToLocal(child, ref origin, ref angles));
        Assert.True(hierarchy.LocalToWorld(child, ref origin, ref angles));

        Assert.Equal(44f, origin.X, 2);
        Assert.Equal(-13f, origin.Y, 2);
        Assert.Equal(27f, origin.Z, 2);
    }

    /// <summary>
    /// Both directions answer false and touch nothing for an entity with no parent,
    /// so a caller that does not care can ignore the result and still be correct.
    /// </summary>
    [Fact]
    public void TheTwoFramesLeaveAnUnparentedEntityAlone()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle lonely = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        var origin = new Vector(1f, 2f, 3f);
        var angles = new QAngle(4f, 5f, 6f);

        Assert.False(hierarchy.WorldToLocal(lonely, ref origin, ref angles));
        Assert.False(hierarchy.LocalToWorld(lonely, ref origin, ref angles));

        Assert.Equal(1f, origin.X, 5);
        Assert.Equal(4f, angles.Pitch, 5);
    }

    /// <summary>
    /// A new local offset is recorded and applied, and the child's own children are
    /// composed against the frame the call is ESTABLISHING rather than the one it
    /// replaces.
    /// </summary>
    [Fact]
    public void SetLocalRecordsTheOffsetBeforeItAppliesIt()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, new QAngle(0f, 90f, 0f));
        EntityHandle child = world.Spawn("cap", Vector.Zero, QAngle.Zero);

        hierarchy.SetParent(child, parent);
        hierarchy.SetLocal(child, new Vector(10f, 0f, 0f), QAngle.Zero);

        // Ten units along the PARENT's forward, which is +Y in the world.
        Assert.Equal(0f, world.At(child).Origin.X, 3);
        Assert.Equal(10f, world.At(child).Origin.Y, 3);
    }

    /// <summary>
    /// An unparented entity given a local transform is written in world space
    /// rather than asserted: a movement started while parented can outlive the
    /// parent, and the entity should finish it where it stands.
    /// </summary>
    [Fact]
    public void SetLocalOnAnUnparentedEntityIsAWorldWrite()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle lonely = world.Spawn("cap", Vector.Zero, QAngle.Zero);

        hierarchy.SetLocal(lonely, new Vector(7f, 8f, 9f), new QAngle(0f, 45f, 0f));

        Assert.Equal(7f, world.At(lonely).Origin.X, 3);
        Assert.Equal(45f, world.At(lonely).Angles.Yaw, 3);
    }

    /// <summary>
    /// IS THIS ENTITY CARRIED is not the same question as CAN ITS FRAME BE BUILT. A
    /// parent whose model has no such attachment still owns the child, and
    /// reporting it unparented would have a flight or a solver step move it out of
    /// its parent's hands.
    /// </summary>
    [Fact]
    public void AnEntityWhoseAttachmentFrameFailsIsStillParented()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle gun = world.Spawn("gun", Vector.Zero, QAngle.Zero, poses: true);
        world.At(gun).Attachments[3] = (new Vector(0f, 0f, 40f), QAngle.Zero);

        EntityHandle flash = world.Spawn("flash", new Vector(0f, 0f, 40f), QAngle.Zero);
        hierarchy.SetParent(flash, gun, 3);

        // The model changes under it and the attachment is gone.
        world.At(gun).Attachments.Remove(3);

        Assert.True(hierarchy.IsParented(flash));
        Assert.False(hierarchy.TryGetParentFrameOf(flash, out _));
    }

    /// <summary>
    /// A frame that cannot be built leaves the child EXACTLY where it is. A
    /// decoration that stops following is nameable; one that teleports into the
    /// middle of the thing carrying it is not.
    /// </summary>
    [Fact]
    public void AFrameThatCannotBeBuiltLeavesTheChildWhereItIs()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle gun = world.Spawn("gun", Vector.Zero, QAngle.Zero, poses: true);
        world.At(gun).Attachments[3] = (new Vector(0f, 0f, 40f), QAngle.Zero);

        EntityHandle flash = world.Spawn("flash", new Vector(0f, 0f, 40f), QAngle.Zero);
        hierarchy.SetParent(flash, gun, 3);

        world.At(gun).Attachments.Remove(3);
        hierarchy.ParentMoved(gun);

        Assert.Equal(40f, world.At(flash).Origin.Z, 3);
        Assert.DoesNotContain(flash, world.Written);
    }

    //=========================================================================//
    // The sweep.
    //=========================================================================//

    /// <summary>
    /// AN ORIGIN-PARENTED MANAGED CHILD IS NEVER SWEPT. Every link in every shipped
    /// map so far is one of these, and the per-frame pass must cost them nothing —
    /// eager composition is the port's rule and this is the statement that the
    /// sweep did not quietly replace it.
    /// </summary>
    [Fact]
    public void TheSweepDoesNothingForAnOrdinaryLink()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle parent = world.Spawn("plug", Vector.Zero, QAngle.Zero);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, parent);
        world.Written.Clear();

        Assert.Equal(0, hierarchy.RunFrame());
        Assert.Empty(world.Written);
    }

    /// <summary>
    /// An ATTACHMENT link is swept, because its frame is a function of the parent's
    /// animation clock and no position write fires when a clock advances.
    /// </summary>
    [Fact]
    public void TheSweepComposesAnAttachmentLink()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle gun = world.Spawn("gun", Vector.Zero, QAngle.Zero, poses: true);
        world.At(gun).Attachments[3] = (new Vector(0f, 0f, 40f), QAngle.Zero);

        EntityHandle flash = world.Spawn("flash", new Vector(0f, 0f, 40f), QAngle.Zero);
        hierarchy.SetParent(flash, gun, 3);

        // The skeleton gestures; the gun's origin does not move.
        world.At(gun).Attachments[3] = (new Vector(0f, 30f, 40f), QAngle.Zero);

        Assert.Equal(1, hierarchy.RunFrame());
        Assert.Equal(30f, world.At(flash).Origin.Y, 3);
    }

    /// <summary>
    /// A child of an SDK parent is swept even with no attachment: that parent's
    /// position writes go through the other hierarchy's setters and are not
    /// observable from here.
    /// </summary>
    [Fact]
    public void TheSweepComposesAChildOfAnSdkParent()
    {
        var (hierarchy, world) = NewWorld();

        EntityHandle door = world.Spawn("door", Vector.Zero, QAngle.Zero, onBase: true);
        EntityHandle child = world.Spawn("cap", new Vector(10f, 0f, 0f), QAngle.Zero);

        hierarchy.SetParent(child, door);

        world.MoveTo(door, Vector.Zero, new QAngle(0f, 90f, 0f));

        Assert.Equal(1, hierarchy.RunFrame());
        Assert.Equal(10f, world.At(child).Origin.Y, 3);
    }

    //=========================================================================//
    // The frame rule, against the table both sides read.
    //=========================================================================//

    private readonly record struct KindRow(bool Standalone, bool Base, int Attachment,
                                           bool CanPose, ParentFrameKind Expected);

    private readonly record struct SweepRow(bool Resolved, int Attachment, bool ParentIsBase,
                                            bool Expected);

    private static (List<KindRow> Kinds, List<SweepRow> Sweeps) LoadFrameCases()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("sourcesharp", "test", "parent_frame_cases.txt"));
        Assert.True(path is not null, "the frame rule's table is found next to the tests");

        var kinds = new List<KindRow>();
        var sweeps = new List<SweepRow>();

        foreach (string line in File.ReadAllLines(path!))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (f[0] == "kind" && f.Length == 6)
            {
                kinds.Add(new KindRow(f[1] == "1", f[2] == "1", int.Parse(f[3], CultureInfo.InvariantCulture),
                                      f[4] == "1", Enum.Parse<ParentFrameKind>(f[5])));
            }
            else if (f[0] == "sweep" && f.Length == 5)
            {
                sweeps.Add(new SweepRow(f[1] == "1", int.Parse(f[2], CultureInfo.InvariantCulture),
                                        f[3] == "1", f[4] == "1"));
            }
        }

        return (kinds, sweeps);
    }

    /// <summary>
    /// The table has to be found and to hold EVERY combination. Sixteen and eight:
    /// enumerated, not sampled, because every way this rule can be wrong is silent.
    /// </summary>
    [Fact]
    public void TheFrameRuleTableIsThereAndHasEveryCombination()
    {
        var (kinds, sweeps) = LoadFrameCases();

        Assert.Equal(16, kinds.Count);
        Assert.Equal(8, sweeps.Count);
    }

    /// <summary>
    /// EVERY FRAME ROW is what the managed rule answers.
    ///
    /// <para>
    /// This used to be one half of an agreement: <c>shell_parent_frame_test.cpp</c>
    /// recomputed the same file from <c>shell_parent_frame.h</c>, and neither
    /// declaration of the rule was believed on its own. The native declaration is
    /// gone — <c>SourceSharp_ParentFrameKind</c> survives only in two comments, and
    /// no translation unit on either link line reached the header — so this is now
    /// the ONLY implementation, and the fixture is what it is held to.
    /// </para>
    ///
    /// <para>
    /// That makes the golden file load-bearing rather than corroborating. If a
    /// native frame rule ever comes back, it reads this same file and the pair of
    /// them is believed again; until then, changing an expectation in
    /// <c>parent_frame_cases.txt</c> to make a test pass changes the rule.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryFrameRowIsWhatTheManagedRuleAnswers()
    {
        foreach (KindRow row in LoadFrameCases().Kinds)
        {
            Assert.Equal(row.Expected,
                ParentFrameRule.KindFor(row.Standalone, row.Base, row.Attachment, row.CanPose));
        }
    }

    /// <summary>And every sweep row.</summary>
    [Fact]
    public void EverySweepRowIsWhatTheManagedRuleAnswers()
    {
        foreach (SweepRow row in LoadFrameCases().Sweeps)
        {
            Assert.Equal(row.Expected,
                ParentFrameRule.NeedsSweeping(row.Resolved, row.Attachment, row.ParentIsBase));
        }
    }

    //=========================================================================//
    // THE MARK. Not the value.
    //=========================================================================//

    /// <summary>
    /// A blob base that is not zero and not a round number, so an assertion that
    /// accidentally dropped it would not still pass.
    /// </summary>
    private const int SampleBlockBase = 1952;

    /// <summary>
    /// EXACTLY TWO OFFSETS, and the count is the claim. Marking the origin, the
    /// angles and the twelve words of the world transform would spend eighteen of
    /// the edict's nineteen change offsets on one call and tip it into
    /// <c>FL_FULL_EDICT_CHANGED</c> — a bandwidth regression no functional test
    /// catches, because both forms deliver the right value.
    /// </summary>
    [Fact]
    public void AComposeMarksExactlyTwoOffsets()
    {
        Assert.Equal(2, HierarchyBlockWrite.Marks(SampleBlockBase).Count);
    }

    /// <summary>And they are the origin's and the angles', measured from the object base.</summary>
    [Fact]
    public void AComposeMarksTheOriginAndTheAngles()
    {
        IReadOnlyList<int> marks = HierarchyBlockWrite.Marks(SampleBlockBase);

        Assert.Equal(SampleBlockBase + HierarchyBlockWrite.OffsetOf(nameof(EngineBlockHead.Origin)),
                     marks[0]);
        Assert.Equal(SampleBlockBase + HierarchyBlockWrite.OffsetOf(nameof(EngineBlockHead.Angles)),
                     marks[1]);
    }

    /// <summary>
    /// THE WORLD TRANSFORM IS NEVER MARKED. It is derived from the two above it and
    /// nothing on the client decodes it — the client rebuilds its own from the
    /// origin and angles it did decode.
    /// </summary>
    [Fact]
    public void AComposeNeverMarksTheWorldTransform()
    {
        int transform = SampleBlockBase
                      + HierarchyBlockWrite.OffsetOf(nameof(EngineBlockHead.WorldTransform));

        Assert.DoesNotContain(transform, HierarchyBlockWrite.Marks(SampleBlockBase));
        Assert.DoesNotContain(nameof(EngineBlockHead.WorldTransform), HierarchyBlockWrite.MarkedFields);
    }

    /// <summary>
    /// It IS written, though, and that pair is the whole of the rule: the cached
    /// transform is storage that <c>CollisionToWorldTransform</c> hands back a
    /// reference into, so a composition that wrote the origin and angles alone
    /// would leave every trace against the child working from the frame it had
    /// before it moved.
    /// </summary>
    [Fact]
    public void AComposeWritesTheWorldTransformItDoesNotMark()
    {
        Assert.Contains(nameof(EngineBlockHead.WorldTransform), HierarchyBlockWrite.WrittenFields);
        Assert.Equal(3, HierarchyBlockWrite.WrittenFields.Count);
    }

    /// <summary>
    /// And no mark this class produces is the full-edict sentinel. A test that
    /// merely asserted "the offsets are right" would still pass on the day somebody
    /// added a widen-to-everything path beside them.
    /// </summary>
    [Fact]
    public void AComposeNeverMarksTheWholeEdict()
    {
        Assert.DoesNotContain(EngineBlockMarks.FullEdictSentinel,
                              HierarchyBlockWrite.Marks(SampleBlockBase));

        foreach (string field in HierarchyBlockWrite.MarkedFields)
            Assert.False(EngineBlockMarks.MarksWholeEdict(field));
    }

    /// <summary>
    /// The count stays well inside the edict's limit, stated as arithmetic rather
    /// than as a comment: two of nineteen leaves seventeen for everything else that
    /// changes on the same frame.
    /// </summary>
    [Fact]
    public void TheMarkCountStaysInsideTheEdictChangeLimit()
    {
        Assert.Equal(19, HierarchyBlockWrite.EdictChangeOffsetLimit);
        Assert.True(HierarchyBlockWrite.MarkedFields.Count < HierarchyBlockWrite.EdictChangeOffsetLimit);
    }

    /// <summary>
    /// The marks are the SAME ARITHMETIC every shell setter uses. A prop built one
    /// way and a mark computed another leaves every partial update on the local
    /// backdoor matching nothing, with the right value still arriving by the slow
    /// path for ever.
    /// </summary>
    [Fact]
    public void TheMarksAreTheSameArithmeticAsEveryOtherBlockWrite()
    {
        foreach (string field in HierarchyBlockWrite.MarkedFields)
        {
            Assert.Equal(EngineBlockMarks.MarksForWrite(field, SampleBlockBase)[0],
                         SampleBlockBase + HierarchyBlockWrite.OffsetOf(field));
        }
    }

    //=========================================================================//
    // The block write itself.
    //=========================================================================//

    /// <summary>
    /// The three fields land at the offsets the block declares, read back through
    /// the block's own layout rather than through the numbers the writer used.
    /// </summary>
    [Fact]
    public void AComposeWritesTheOriginAnglesAndTransformIntoTheBlock()
    {
        var blob = new byte[EngineBlock.SizeBytes];

        var origin = new Vector(11f, -22f, 33f);
        var angles = new QAngle(4f, 55f, -6f);
        Transform world = Transform.FromOriginAngles(origin, angles);

        Assert.True(HierarchyBlockWrite.Write(blob, origin, angles, world));

        EngineBlock read = MemoryMarshal.Read<EngineBlock>(blob);

        Assert.Equal(11f, read.Head.Origin.X, 5);
        Assert.Equal(-22f, read.Head.Origin.Y, 5);
        Assert.Equal(33f, read.Head.Origin.Z, 5);
        Assert.Equal(55f, read.Head.Angles.Yaw, 5);
        Assert.Equal(world.M03, read.Head.WorldTransform.M03, 5);
        Assert.Equal(world.M11, read.Head.WorldTransform.M11, 5);
    }

    /// <summary>
    /// A blob too short to hold the block is refused and NOTHING is written. A
    /// partial write would leave the origin moved and the transform describing
    /// where the entity used to be, which is the one outcome worse than not moving
    /// it at all.
    /// </summary>
    [Fact]
    public void AShortBlobIsRefusedAndNothingIsWritten()
    {
        var blob = new byte[EngineBlock.SizeBytes - 1];

        Assert.False(HierarchyBlockWrite.Write(blob, new Vector(1f, 2f, 3f), QAngle.Zero,
                                               Transform.Identity));

        Assert.All(blob, b => Assert.Equal(0, b));
    }

    /// <summary>
    /// The block's declared size is what the writer requires, taken from the
    /// declaration rather than repeated — so a field added to the block cannot
    /// leave this writing past the end of a blob sized for the old one.
    /// </summary>
    [Fact]
    public void TheWriterRequiresExactlyTheBlockTheDeclarationDescribes()
    {
        var blob = new byte[EngineBlock.MarshalledBytes];

        Assert.True(HierarchyBlockWrite.Write(blob, Vector.Zero, QAngle.Zero, Transform.Identity));
        Assert.Equal(EngineBlock.SizeBytes, EngineBlock.MarshalledBytes);
    }
}
