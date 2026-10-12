using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE TWO BLOCK WORDS PARENTING'S CROSSING BUDGET RESTS ON, and the re-entrancy
/// the native position funnel creates.
///
/// <para>
/// <c>HierarchyTests</c> holds the 52 facts about the MODEL — what a composition
/// computes, which links are refused, when an offset is re-measured. None of
/// them says anything about the two words in the engine block that decide whether
/// native code crosses into that model at all, because when they were written
/// nothing crossed. These are those facts.
/// </para>
///
/// <para>
/// <b>Why they are worth having.</b> Every way the guard words can be wrong is
/// silent in both directions. A word left set on a parent that no longer carries
/// anything costs an inbound crossing on every position write of that entity for
/// the rest of the map — the game is CORRECT and slower, which no behavioural
/// test can see. A word left CLEAR on a parent that does carry something is worse
/// and just as quiet: native never tells managed the parent moved, so the child
/// stops following, and the registry goes on saying it should.
/// </para>
/// </summary>
public class HierarchyDriverTests
{
    //=========================================================================//
    // The world.
    //=========================================================================//

    /// <summary>
    /// A world that answers the four questions parenting asks, and RECORDS what
    /// it was asked to write.
    ///
    /// <para>
    /// It also drives the one thing an engine-less test could not otherwise
    /// reach: <see cref="IComposedWorld.IsComposing"/>. In the game that is set
    /// by <c>EngineHierarchyWorld.WriteComposed</c> while the native position
    /// funnel is unwinding, and the funnel ends by calling
    /// <c>HierarchyParentMoved</c> for the child it has just placed. Here it is
    /// set by the test, which is the only way to state "the funnel came back
    /// round" as a fact rather than as a thing that happens.
    /// </para>
    /// </summary>
    private sealed class FakeWorld : IComposedWorld
    {
        private sealed class Ent
        {
            internal string Name = string.Empty;
            internal bool OnBase;
            internal bool Alive = true;
            internal Vector Origin;
            internal QAngle Angles;
        }

        private readonly Dictionary<EntityHandle, Ent> _ents = new();
        private uint _next = 1;

        internal readonly List<string> Warnings = new();
        internal readonly List<EntityHandle> Removed = new();
        internal readonly List<EntityHandle> Written = new();

        /// <summary>Set while a composition is on the stack. The test drives it.</summary>
        public bool IsComposing { get; set; }

        /// <summary>
        /// A handle with a non-zero serial, so nothing here can pass on an
        /// implementation that compared entry indices and ignored the serial.
        /// </summary>
        internal EntityHandle Spawn(string name, Vector origin = default, bool onBase = false)
        {
            var handle = new EntityHandle(_next++ | (0x2Au << 16));

            _ents[handle] = new Ent { Name = name, Origin = origin, OnBase = onBase };
            return handle;
        }

        internal void Kill(EntityHandle handle) => _ents[handle].Alive = false;

        internal void MoveTo(EntityHandle handle, Vector origin) => _ents[handle].Origin = origin;

        internal Vector OriginOf(EntityHandle handle) => _ents[handle].Origin;

        private Ent? Live(EntityHandle h)
            => _ents.TryGetValue(h, out var e) && e.Alive ? e : null;

        public bool IsStandalone(EntityHandle h) => Live(h) is { OnBase: false };
        public bool IsBase(EntityHandle h) => Live(h) is { OnBase: true };
        public bool CanPose(EntityHandle h) => false;

        public bool TryGetEntityFrame(EntityHandle h, out Transform frame)
        {
            var e = Live(h);
            frame = e is null ? Transform.Identity
                              : Transform.FromOriginAngles(e.Origin, e.Angles);
            return e is not null;
        }

        public bool TryGetAttachmentFrame(EntityHandle h, int attachment, out Transform frame)
        {
            frame = Transform.Identity;
            return false;
        }

        public int LookupAttachment(EntityHandle h, string name) => 0;

        public bool TryGetAbsolute(EntityHandle h, out Vector origin, out QAngle angles)
        {
            var e = Live(h);
            origin = e?.Origin ?? Vector.Zero;
            angles = e?.Angles ?? QAngle.Zero;
            return e is not null;
        }

        public void WriteComposed(EntityHandle child, Vector origin, QAngle angles,
                                  Transform childToWorld)
        {
            Written.Add(child);

            if (Live(child) is { } e)
            {
                e.Origin = origin;
                e.Angles = angles;
            }
        }

        public EntityHandle FindStandaloneByName(string name)
            => _ents.FirstOrDefault(kv => kv.Value.Alive && !kv.Value.OnBase
                                          && kv.Value.Name == name).Key;

        public Span<byte> BlockOf(EntityHandle entity) => Span<byte>.Empty;

        public EntityHandle FindBaseByName(string name)
            => _ents.FirstOrDefault(kv => kv.Value.Alive && kv.Value.OnBase
                                          && kv.Value.Name == name).Key;

        public string NameOf(EntityHandle h) => Live(h)?.Name ?? "(gone)";

        public void Remove(EntityHandle h)
        {
            Removed.Add(h);
            Kill(h);
        }

        internal readonly List<EntityHandle> PhysicsDetached = new();

        public void DetachPhysics(EntityHandle child) => PhysicsDetached.Add(child);

        public void Warn(string message) => Warnings.Add(message);
    }

    private static (FakeWorld World, HierarchyDriver Driver) Fresh()
    {
        var world = new FakeWorld();
        return (world, new HierarchyDriver(world));
    }

    //=========================================================================//
    // The guard words.
    //=========================================================================//

    [Fact]
    public void ALinkPutsTheParentsHandleInTheChildsGuardWord()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        Assert.True(driver.SetParent(child, parent, 0));

        Assert.Equal(parent.Value, driver.GuardsOf(child).ParentHandle);
    }

    [Fact]
    public void ALinkRaisesTheParentsChildCount()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");

        driver.SetParent(world.Spawn("cap"), parent, 0);

        Assert.Equal(1, driver.GuardsOf(parent).ChildCount);
    }

    /// <summary>
    /// TWO CHILDREN ARE TWO, not one. A guard word that only ever said "yes" and
    /// "no" would pass every fact above it and would stop being able to answer
    /// the removal of one child out of two.
    /// </summary>
    [Fact]
    public void TwoChildrenOfOneParentMakeItsChildCountTwo()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");

        driver.SetParent(world.Spawn("cap"), parent, 0);
        driver.SetParent(world.Spawn("lid"), parent, 0);

        Assert.Equal(2, driver.GuardsOf(parent).ChildCount);
    }

    [Fact]
    public void AParentThatIsAlsoAChildCarriesBothWords()
    {
        var (world, driver) = Fresh();

        EntityHandle grandparent = world.Spawn("body");
        EntityHandle parent = world.Spawn("arm");
        EntityHandle child = world.Spawn("hand");

        driver.SetParent(parent, grandparent, 0);
        driver.SetParent(child, parent, 0);

        HierarchyDriver.GuardWords words = driver.GuardsOf(parent);

        Assert.Equal(grandparent.Value, words.ParentHandle);
        Assert.Equal(1, words.ChildCount);
    }

    /// <summary>
    /// UNPARENTING CLEARS BOTH ENDS. This is the direction that costs a crossing
    /// per position write for ever if it is missed, and the direction nothing
    /// about the game would show.
    /// </summary>
    [Fact]
    public void UnparentingClearsTheChildsWordAndTheParentsCount()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, parent, 0);
        driver.SetParent(child, EntityHandle.None, 0);

        Assert.Equal(EntityHandle.None.Value, driver.GuardsOf(child).ParentHandle);
        Assert.Equal(0, driver.GuardsOf(parent).ChildCount);
    }

    /// <summary>
    /// A PARENT THAT NOTHING CARRIES HOLDS THE INVALID HANDLE, NOT ZERO.
    ///
    /// <para>
    /// <b>This is a bug the driver had, caught by this file.</b> The sync built
    /// each entity's words by starting from <c>default(GuardWords)</c>, whose
    /// <c>ParentHandle</c> is 0 — and 0 is a REAL handle naming a real entity, as
    /// <c>SourceSharp_EntitySetParent</c> says out loud on the other side of the
    /// boundary. So every parent got zero written into its block, native read the
    /// word as valid, and <c>StandaloneHierarchy_IsParented</c> answered YES about
    /// a parent nothing was carrying — which is the answer that makes
    /// <c>UpdateFlight</c> integrate it in a frame that does not exist and makes
    /// <c>UpdateMovement</c> convert its target into one.
    /// </para>
    ///
    /// <para>
    /// Nothing about the game would have printed anything, and the 52 facts over
    /// the MODEL could not see it: the model was right, and only the projection
    /// into the block was wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void AParentThatNothingCarriesHoldsTheInvalidHandleAndNotZero()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");

        driver.SetParent(world.Spawn("cap"), parent, 0);

        Assert.Equal(EntityHandle.None.Value, driver.GuardsOf(parent).ParentHandle);
        Assert.NotEqual(0u, driver.GuardsOf(parent).ParentHandle);
    }

    [Fact]
    public void AnEntityWithNoLinkAtAllIsNotTracked()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, parent, 0);
        driver.SetParent(child, EntityHandle.None, 0);

        Assert.Equal(0, driver.GuardedEntityCount);
    }

    /// <summary>
    /// AN UNRESOLVED <c>parentname</c> WRITES NOTHING, and it is the one place
    /// the obvious implementation is wrong. There is no parent handle yet — the
    /// entity the name calls out may not have spawned — and writing anything into
    /// the child's word would make <c>IsParented</c> answer yes for an entity
    /// nothing is carrying. That is the answer that has <c>UpdateFlight</c> fly a
    /// prop out of hands that are not holding it.
    /// </summary>
    [Fact]
    public void AnUnresolvedParentNameWritesNoGuardWord()
    {
        var (world, driver) = Fresh();

        EntityHandle child = world.Spawn("cap");

        driver.SetParentName(child, "plug");

        Assert.Equal(EntityHandle.None.Value, driver.GuardsOf(child).ParentHandle);
        Assert.Equal(0, driver.GuardedEntityCount);
    }

    [Fact]
    public void ResolvingTheNamesWritesTheWords()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        driver.SetParentName(child, "plug");

        Assert.Equal(1, driver.ResolveParentNames());

        Assert.Equal(parent.Value, driver.GuardsOf(child).ParentHandle);
        Assert.Equal(1, driver.GuardsOf(parent).ChildCount);
    }

    [Fact]
    public void RemovingAParentClearsItsOwnWordsAndItsChildrens()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, parent, 0);

        world.Kill(parent);

        Assert.Equal(1, driver.EntityRemoved(parent));

        Assert.Equal(0, driver.GuardedEntityCount);
        Assert.Equal(EntityHandle.None.Value, driver.GuardsOf(child).ParentHandle);
    }

    /// <summary>
    /// A PARENT ON THE SDK'S HIERARCHY IS RECORDED AND NOT WRITTEN, and the sync
    /// does not keep trying.
    ///
    /// <para>
    /// It has no engine block of ours to write, and it needs none — its moves go
    /// through the other hierarchy's setters and are not observable from this
    /// side at all, which is why such links are SWEPT rather than composed. What
    /// matters is that the driver stops attempting it: a write that never lands
    /// and is never recorded would be retried on every sync, and the sync runs
    /// once a frame.
    /// </para>
    /// </summary>
    [Fact]
    public void ABaseParentIsRecordedOnceAndNotWrittenAgainEveryFrame()
    {
        var (world, driver) = Fresh();

        EntityHandle door = world.Spawn("door", new Vector(0, 0, 0), onBase: true);

        driver.SetParent(world.Spawn("cap", new Vector(10, 0, 0)), door, 0);

        Assert.Equal(1, driver.GuardsOf(door).ChildCount);

        int after = driver.GuardWritesMade;

        driver.RunFrame();
        driver.RunFrame();

        Assert.Equal(after, driver.GuardWritesMade);
    }

    [Fact]
    public void TheLevelShutdownDropsEveryGuardWord()
    {
        var (world, driver) = Fresh();

        driver.SetParent(world.Spawn("cap"), world.Spawn("plug"), 0);
        driver.LevelShutdown();

        Assert.Equal(0, driver.GuardedEntityCount);
        Assert.Equal(0, driver.Model.LinkCount);
    }

    /// <summary>
    /// A SYNC THAT CHANGES NOTHING WRITES NOTHING.
    ///
    /// <para>
    /// The sync runs after every entry point that can change the registry, and
    /// <see cref="HierarchyDriver.ParentMoved"/> is one of them — so on a map with
    /// links it runs on every position write of every parent. A version that
    /// rewrote each link's words each time would be CORRECT and would spend a blob
    /// fetch per link per move, which is the very crossing the guard exists to
    /// save. Nothing about the game's behaviour would show it.
    /// </para>
    /// </summary>
    [Fact]
    public void ComposingWithoutLinkingWritesNoGuardWords()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug", new Vector(0, 0, 0));
        driver.SetParent(world.Spawn("cap", new Vector(10, 0, 0)), parent, 0);

        int after = driver.GuardWritesMade;

        world.MoveTo(parent, new Vector(50, 0, 0));
        driver.ParentMoved(parent);
        driver.ParentMoved(parent);
        driver.ParentMoved(parent);

        Assert.Equal(after, driver.GuardWritesMade);
    }

    /// <summary>
    /// And the counter is not stuck at zero, which is what would make the fact
    /// above a check that cannot fail.
    /// </summary>
    [Fact]
    public void MakingALinkDoesWriteGuardWords()
    {
        var (world, driver) = Fresh();

        Assert.Equal(0, driver.GuardWritesMade);

        driver.SetParent(world.Spawn("cap"), world.Spawn("plug"), 0);

        // Two: the child's handle and the parent's count.
        Assert.Equal(2, driver.GuardWritesMade);
    }

    //=========================================================================//
    // The re-entrancy the native position funnel creates.
    //=========================================================================//

    /// <summary>
    /// THE FUNNEL COMING BACK ROUND IS IGNORED.
    ///
    /// <para>
    /// Placing a child ends in <c>ENTITY_OP_POSITION_CHANGED</c>, because the
    /// spatial partition entry and the triggers are native's; that handler ends by
    /// calling <c>StandaloneHierarchy_ParentMoved</c> for the child it just
    /// placed. <see cref="Hierarchy.ComposeOne"/> is already recursing into
    /// exactly those links at that moment.
    /// </para>
    ///
    /// <para>
    /// Composing them twice is not merely wasteful: the second pass runs after the
    /// first has moved the child, so a grandchild is composed against a frame that
    /// has already been consumed.
    /// </para>
    /// </summary>
    [Fact]
    public void AParentMovedArrivingInsideACompositionIsIgnored()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        driver.SetParent(world.Spawn("cap"), parent, 0);

        world.Written.Clear();
        world.IsComposing = true;

        Assert.Equal(0, driver.ParentMoved(parent));
        Assert.Empty(world.Written);
    }

    [Fact]
    public void TheSameParentMovedComposesWhenNoCompositionIsOnTheStack()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, parent, 0);

        world.Written.Clear();
        world.IsComposing = false;

        Assert.Equal(1, driver.ParentMoved(parent));
        Assert.Equal(new[] { child }, world.Written);
    }

    [Fact]
    public void AChildMovedArrivingInsideACompositionIsIgnored()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug", new Vector(0, 0, 0));
        EntityHandle child = world.Spawn("cap", new Vector(10, 0, 0));

        driver.SetParent(child, parent, 0);

        Transform before = driver.Model.LinkFor(child)!.ChildToParent;

        // The composition has just written the child's new absolute. A ChildMoved
        // honoured here would re-measure the offset from the position the
        // composition produced -- writing back the answer it had just computed,
        // which is the one thing the split between the funnel and the public
        // setters exists to prevent.
        world.MoveTo(child, new Vector(999, 0, 0));
        world.IsComposing = true;

        driver.ChildMoved(child);

        Assert.Equal(before, driver.Model.LinkFor(child)!.ChildToParent);
    }

    //=========================================================================//
    // The rest of the entry points, which are relays and are checked as such.
    //=========================================================================//

    [Fact]
    public void ARefusedLinkIsWarnedAboutRatherThanSilentlyDropped()
    {
        var (world, driver) = Fresh();

        EntityHandle a = world.Spawn("a");
        EntityHandle b = world.Spawn("b");

        driver.SetParent(b, a, 0);

        Assert.False(driver.SetParent(a, b, 0));
        Assert.Contains(world.Warnings, w => w.Contains("cycle"));
    }

    [Fact]
    public void ARefusedLinkLeavesTheGuardWordsAsTheyWere()
    {
        var (world, driver) = Fresh();

        EntityHandle a = world.Spawn("a");
        EntityHandle b = world.Spawn("b");

        driver.SetParent(b, a, 0);
        driver.SetParent(a, b, 0);

        Assert.Equal(a.Value, driver.GuardsOf(b).ParentHandle);
        Assert.Equal(EntityHandle.None.Value, driver.GuardsOf(a).ParentHandle);
    }

    //=========================================================================//
    // The one thing linking does that is not arithmetic.
    //=========================================================================//

    /// <summary>
    /// A LINK TAKES THE CHILD'S SOLVER OBJECT AWAY.
    ///
    /// <para>
    /// A stated divergence from stock, which shadows a simulated child instead —
    /// and the reason it is here rather than left out is that leaving the object
    /// alone is strictly WORSE than destroying it. A static object cannot be
    /// moved by writing the entity's origin, and both live children in the
    /// shipped map are exactly that: the drawn model would follow its parent
    /// while an invisible collision hull stayed at the map position for the rest
    /// of the round, stopping bullets and players in mid-air.
    /// </para>
    ///
    /// <para>
    /// <b>It went missing once already.</b> The native hierarchy did it inside
    /// <c>StandaloneHierarchy_SetParentEx</c>; the managed model that replaced
    /// that file did not, and the first live run after the port showed a physics
    /// prop parented to a rotating fan fighting its own solver — the composition
    /// writing one position and the solver writing another, each undoing half of
    /// the other's every tick.
    /// </para>
    /// </summary>
    [Fact]
    public void MakingALinkTakesTheChildsSolverObjectAway()
    {
        var (world, driver) = Fresh();

        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, world.Spawn("plug"), 0);

        Assert.Equal(new[] { child }, world.PhysicsDetached);
    }

    /// <summary>
    /// AND A REFUSAL TAKES NOTHING. A refused parenting has to leave the child
    /// exactly as it was — an entity that lost its physics to a link that was
    /// never made is an entity that has stopped colliding for no reason anybody
    /// can name, and nothing gives it back.
    /// </summary>
    [Fact]
    public void ARefusedLinkLeavesTheChildsSolverObjectAlone()
    {
        var (world, driver) = Fresh();

        EntityHandle a = world.Spawn("a");
        EntityHandle b = world.Spawn("b");

        driver.SetParent(b, a, 0);
        world.PhysicsDetached.Clear();

        Assert.False(driver.SetParent(a, b, 0));
        Assert.Empty(world.PhysicsDetached);
    }

    /// <summary>
    /// Unparenting does not give it back, and that asymmetry is deliberate: only
    /// whoever built the object knows whether it should be static, asleep or
    /// debris.
    /// </summary>
    [Fact]
    public void UnparentingDoesNotGiveTheSolverObjectBack()
    {
        var (world, driver) = Fresh();

        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, world.Spawn("plug"), 0);
        world.PhysicsDetached.Clear();

        driver.SetParent(child, EntityHandle.None, 0);

        Assert.Empty(world.PhysicsDetached);
    }

    [Fact]
    public void TheParentAttachmentIsWhatTheLinkHolds()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug");
        EntityHandle child = world.Spawn("cap");

        driver.SetParent(child, parent, 3);

        Assert.Equal(3, driver.GetParentAttachment(child));
    }

    [Fact]
    public void TheParentAttachmentOfAnUnparentedEntityIsZero()
    {
        var (world, driver) = Fresh();

        Assert.Equal(0, driver.GetParentAttachment(world.Spawn("loose")));
    }

    [Fact]
    public void TheSweepComposesTheLinksTheFunnelCannotReach()
    {
        var (world, driver) = Fresh();

        // A parent on the SDK's hierarchy: its moves go through the other
        // hierarchy's setters and are not observable from this side at all, which
        // is exactly what ParentFrameRule.NeedsSweeping says.
        EntityHandle parent = world.Spawn("door", new Vector(0, 0, 0), onBase: true);
        EntityHandle child = world.Spawn("cap", new Vector(10, 0, 0));

        driver.SetParent(child, parent, 0);

        world.MoveTo(parent, new Vector(0, 0, 64));
        world.Written.Clear();

        Assert.Equal(1, driver.RunFrame());
        Assert.Equal(new Vector(10, 0, 64), world.OriginOf(child));
    }

    [Fact]
    public void SetLocalReplacesTheOffsetRatherThanRederivingIt()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug", new Vector(100, 0, 0));
        EntityHandle child = world.Spawn("cap", new Vector(110, 0, 0));

        driver.SetParent(child, parent, 0);
        driver.SetLocal(child, new Vector(0, 20, 0), QAngle.Zero);

        Assert.Equal(new Vector(100, 20, 0), world.OriginOf(child));
    }

    [Fact]
    public void TheTwoFrameConversionsAnswerFalseForAnUnparentedEntity()
    {
        var (world, driver) = Fresh();

        EntityHandle loose = world.Spawn("loose", new Vector(5, 5, 5));

        var origin = new Vector(1, 2, 3);
        var angles = QAngle.Zero;

        Assert.False(driver.ToLocal(loose, ref origin, ref angles));
        Assert.False(driver.ToWorld(loose, ref origin, ref angles));

        // Untouched, which is what lets a caller ignore the answer and be right.
        Assert.Equal(new Vector(1, 2, 3), origin);
    }

    [Fact]
    public void ToLocalAndToWorldAreEachOthersInverse()
    {
        var (world, driver) = Fresh();

        EntityHandle parent = world.Spawn("plug", new Vector(100, 50, 0));
        EntityHandle child = world.Spawn("cap", new Vector(110, 50, 0));

        driver.SetParent(child, parent, 0);

        var origin = new Vector(130, 70, 8);
        var angles = QAngle.Zero;

        Assert.True(driver.ToLocal(child, ref origin, ref angles));
        Assert.True(driver.ToWorld(child, ref origin, ref angles));

        Assert.Equal(130f, origin.X, 3);
        Assert.Equal(70f, origin.Y, 3);
        Assert.Equal(8f, origin.Z, 3);
    }
}
