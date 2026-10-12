using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================//
// PARENTING, IN MANAGED CODE — plan_entity.md step 2h.
//
// One entity carried by another: the model on the plug, the cap on the fan, the
// muzzle flash on a gun's attachment. The whole of it is three decisions and one
// piece of arithmetic, and none of the four is something the engine needs to be
// asked about:
//
//   WHICH FRAME a child hangs from            ParentFrameRule
//   WHETHER a link would close a loop         Hierarchy.WouldCycle
//   WHEN a deferred name becomes a link       Hierarchy.ResolveParentNames
//   WHERE the child ends up                   Hierarchy.Compose, in Transform
//
// That is policy end to end, so it is here. What stays native is exactly what
// the engine holds and managed code cannot: posing a model's skeleton to read an
// attachment frame out of it, and the spatial partition and trigger sweep that
// follow a position write.
//
//-----------------------------------------------------------------------------
// A REGISTRY KEYED BY HANDLE, not m_pParent/m_pFirstChild/m_pNextSibling threaded
// through the entities. The three reasons are the native file's and they survive
// the move unchanged:
//
//   A dangling parent is not expressible. Both ends resolve through the world
//   seam on every walk, and an entry whose child or parent has gone is dropped
//   where it is found. A registry degrades to "nobody", which is the value every
//   caller already tests for; a stale pointer inside an object degrades to a
//   dereference.
//
//   An entity's layout does not change, so the six hundred entities in a map
//   that parents nothing pay one empty-dictionary test per position write.
//
//   The invariant lives in one file, which is this one.
//
//-----------------------------------------------------------------------------
// NO PRODUCTION CALLER YET, AND THAT IS STATED RATHER THAN HIDDEN.
//
// Every entry point into parenting is reached from NATIVE — the position funnel,
// the KeyValue parse, the destructor, the map's resolve pass, the movement
// integrators — so making this the live path means turning each of those into a
// native→managed crossing, which is a slot in the managed export table and
// therefore an ABI bump. The slots are requested in
// .claude/entity-port/abi-requests/2h.md and the integrator lands them.
//
// Until then shell_standalone_hierarchy.cpp is what runs, and this side is not
// left to be believed. Two golden tables pin it to the arithmetic it replaces,
// and both are records of NATIVE rather than of the port:
//
//   sourcesharp/test/parent_frame_cases.txt    the frame rule and the sweep rule,
//       every combination, recomputed by HierarchyTests. It was recomputed by
//       shell_parent_frame_test.cpp as well until lane 3c-entityshell removed
//       shell_parent_frame.h: no translation unit on either link line reached
//       that header, and SourceSharp_ParentFrameKind is now two comments and no
//       code. So this table stopped being a second opinion and became the only
//       statement of the rule that is not the code itself.
//
//   sourcesharp/test/parent_compose_cases.txt  the composition, taken from the
//       AngleMatrix / MatrixInvert / ConcatTransforms / MatrixAngles calls the
//       shell makes, recomputed by parent_compose_test.cpp and by
//       MeasureOffset / Compose below.
//
// The one thing this side cannot answer for itself is an ATTACHMENT frame: that
// is a posed skeleton, it is the engine's, and IHierarchyWorld is where it is
// asked for.
//
//-----------------------------------------------------------------------------
// THE NAME IS THE AUTHORITY AND THE HANDLES ARE A CACHE OF IT. A map writes a
// `parentname` during the parse, when the entity it names may not exist yet and
// when neither end has the position an offset would be measured from. So the
// entry is recorded UNRESOLVED and finished later, and the name is what survives
// in between. <see cref="Hierarchy.ResolveParentNames"/> is that later.
//=============================================================================//

/// <summary>
/// Which matrix a composition should use as the parent's frame.
///
/// <para>
/// <c>SourceSharpParentFrame_t</c> was the native spelling, and the two
/// declarations were held equal by a fixture both sides read —
/// <c>sourcesharp/test/parent_frame_cases.txt</c>. There is no native
/// declaration left to hold equal: lane 3c-entityshell removed
/// <c>shell_parent_frame.h</c> after measuring 0 translation units reaching it
/// on either link line. The fixture stays, and is now what this side is held
/// to on its own.
///
/// <para>
/// The native shell still poses the skeleton an attachment frame is read out
/// of — that is <c>IHierarchyWorld</c>, below — but the RULE that picks between
/// the frames is only here.
/// </para>
/// </para>
///
/// <para>
/// <see cref="None"/> is a real answer and not an error code: a link whose parent
/// has gone, or whose parent is of neither kind, composes NOTHING. The caller
/// leaves the child exactly where it is, which is the honest outcome — a
/// decoration that stops following is nameable, and one that teleports into the
/// middle of the thing carrying it is not.
/// </para>
/// </summary>
public enum ParentFrameKind
{
    /// <summary>Compose nothing; leave the child where it is.</summary>
    None = 0,

    /// <summary>The parent's own transform, on the parallel hierarchy.</summary>
    StandaloneOrigin = 1,

    /// <summary>A frame on the parent's model, posed from the server skeleton.</summary>
    StandaloneAttachment = 2,

    /// <summary>The SDK's own <c>EntityToWorldTransform</c>.</summary>
    BaseOrigin = 3,

    /// <summary>The SDK's own <c>CBaseAnimating::GetAttachment</c>.</summary>
    BaseAttachment = 4,
}

/// <summary>
/// WHICH FRAME A PARENTED CHILD HANGS FROM, and whether that frame can move
/// without this side being told.
///
/// <para>
/// The whole of the interesting behaviour in parenting is the FIRST of the two
/// matrices it composes: an attachment on a gesturing model is somewhere quite
/// different from the entity's origin, and the two hierarchies answer with
/// different calls. Every way of getting it wrong is SILENT:
/// </para>
///
/// <list type="bullet">
/// <item>an attachment index used against a parent with no skeleton reads a
/// matrix nobody wrote — not a crash, a decoration a few dozen units from where
/// it belongs, which reads as a mapper's mistake;</item>
/// <item>an attachment index DROPPED, and the child composed against the origin
/// instead, is a muzzle flash at an NPC's feet — also not a crash;</item>
/// <item>and <see cref="NeedsSweeping"/> decides whether an attachment-parented
/// child is recomposed each frame at all. Too permissive and every ordinary link
/// pays a per-frame sweep it does not need; too strict and a child parented to a
/// gesturing model freezes in mid-air the moment its parent stops moving.</item>
/// </list>
///
/// <para>
/// None of those would fail a build or print a line, which is why the rule is a
/// named thing with its own facts rather than four <c>if</c>s inside the
/// composition.
/// </para>
/// </summary>
public static class ParentFrameRule
{
    /// <summary>
    /// The rule.
    ///
    /// <para>
    /// <b>THIS HIERARCHY WINS</b> when both arms are somehow set. It cannot
    /// legitimately happen — the two resolutions partition the world exactly, so
    /// at most one arm is ever answered — but a rule with a defined answer for an
    /// impossible input is a rule that cannot be surprised by one.
    /// </para>
    ///
    /// <para>
    /// <b>AN ATTACHMENT ON A PARENT THAT CANNOT POSE FALLS BACK TO THE ORIGIN</b>
    /// rather than answering <see cref="ParentFrameKind.None"/>, and that is the
    /// one judgement call in here. A mapper who names an attachment on a brush
    /// entity has made a mistake, but the child is still meant to be carried;
    /// refusing the link would leave it standing in the map while its parent
    /// drives away, which is a worse answer than carrying it from the origin and
    /// saying so on the console.
    /// </para>
    /// </summary>
    /// <param name="hasStandalone">The parent resolved on the parallel hierarchy.</param>
    /// <param name="hasBase">The parent resolved on the SDK's.</param>
    /// <param name="parentAttachment">One-based; 0 for the parent's own transform.</param>
    /// <param name="parentCanPose">The parent has a skeleton to read an attachment from.</param>
    public static ParentFrameKind KindFor(bool hasStandalone, bool hasBase,
                                          int parentAttachment, bool parentCanPose)
    {
        if (hasStandalone)
        {
            return (parentAttachment > 0 && parentCanPose)
                ? ParentFrameKind.StandaloneAttachment
                : ParentFrameKind.StandaloneOrigin;
        }

        if (hasBase)
        {
            return (parentAttachment > 0 && parentCanPose)
                ? ParentFrameKind.BaseAttachment
                : ParentFrameKind.BaseOrigin;
        }

        return ParentFrameKind.None;
    }

    /// <summary>
    /// Whether this link's frame can change WITHOUT this hierarchy being told.
    ///
    /// <para>
    /// The port composes EAGERLY — at the instant a parent's position is written
    /// — rather than with stock's deferred dirty-transform sweep, and that
    /// decision is not reversed here. Eager composition works because every write
    /// to a position on this hierarchy goes through one funnel, which calls
    /// <see cref="Hierarchy.ParentMoved"/> on the way past.
    /// </para>
    ///
    /// <para>
    /// <b>TWO KINDS OF LINK ARE OUTSIDE THAT FUNNEL</b>, and both were found by
    /// trying to verify them in a map rather than by reading. An ATTACHMENT frame
    /// is a function of the parent's animation CLOCK, and no position setter fires
    /// when a clock advances. An SDK PARENT is on the other hierarchy, whose
    /// position writes go through its own setters and must not be made to know
    /// about this registry — keeping that spine untouched is the whole reason an
    /// SDK parent costs no wire change and no client change.
    /// </para>
    ///
    /// <para>
    /// Both are the same shape of problem — a frame this side cannot observe
    /// changing — so they get the same answer, and it is deliberately the
    /// NARROWEST one that covers them. A managed child of a managed parent, hung
    /// from the origin, is still composed at the instant its parent moves and is
    /// swept never.
    /// </para>
    ///
    /// <para>
    /// <paramref name="resolved"/> is IN the rule rather than tested by the
    /// caller because an unresolved link has no parent yet: its attachment index
    /// has not been looked up against a model, so a positive value there means "a
    /// name was written", not "a frame exists".
    /// </para>
    /// </summary>
    public static bool NeedsSweeping(bool resolved, int parentAttachment, bool parentIsBase)
        => resolved && (parentAttachment > 0 || parentIsBase);
}

/// <summary>Why a parenting request was not granted.</summary>
public enum ParentRefusal
{
    /// <summary>It was granted.</summary>
    None = 0,

    /// <summary>The child handle names nothing this side can hold as a child.</summary>
    NoChild,

    /// <summary>
    /// One of the two ends has no handle yet. Parenting is keyed by handle, so an
    /// entity linked before it has one could never be found again and the entry
    /// would sit in the registry naming nothing.
    /// </summary>
    NotSpawned,

    /// <summary>
    /// The link would close a loop. Refused rather than made: the composition
    /// recurses into the grandchildren, so a loop is unbounded recursion, and a
    /// stack overflow names nothing that has anything to do with parenting.
    /// </summary>
    Cycle,

    /// <summary>A deferred <c>parentname</c> that no entity answers to.</summary>
    UnknownName,
}

/// <summary>The answer to a parenting request, and why when it is no.</summary>
/// <param name="Refusal">
/// <see cref="ParentRefusal.None"/> when the link was made or cleared.
/// </param>
/// <param name="Message">
/// What would be printed. Empty on success. NAMES BOTH ENDS on a refusal, which
/// is the whole difference between a refusal and a silent no-op (plan_entity.md
/// §7): a mapper reading a console needs to know which two entities.
/// </param>
public readonly record struct ParentResult(ParentRefusal Refusal, string Message)
{
    /// <summary>Granted.</summary>
    public static readonly ParentResult Ok = new(ParentRefusal.None, string.Empty);

    /// <summary>Whether the link was made (or cleared) as asked.</summary>
    public bool Granted => Refusal == ParentRefusal.None;
}

/// <summary>
/// ONE PARENT-CHILD RELATIONSHIP.
///
/// <para>
/// A class rather than a struct: <see cref="ChildToParent"/> is rewritten in
/// place when a parented child moves itself, and a value type in a list would
/// make that a read-modify-write that is one forgotten store away from being
/// silently lost.
/// </para>
/// </summary>
public sealed class ParentLink
{
    public ParentLink(EntityHandle child) => Child = child;

    /// <summary>The carried entity. Always on this hierarchy.</summary>
    public EntityHandle Child { get; }

    /// <summary>The carrier, once the name has been resolved.</summary>
    public EntityHandle Parent { get; internal set; } = EntityHandle.None;

    /// <summary>
    /// What the map wrote, and the authority until it is resolved. Kept
    /// afterwards too, because it is what a diagnostic names the parent by.
    /// </summary>
    public string ParentName { get; internal set; } = string.Empty;

    /// <summary>
    /// The attachment as the MAP wrote it, which is a NAME, kept for exactly as
    /// long as the parent's name is: an attachment index means something only
    /// against the model it was looked up in, and the parent's model is not known
    /// until the resolve pass finds the parent. Empty for a link made in code,
    /// where the caller already has an index.
    /// </summary>
    public string ParentAttachmentName { get; internal set; } = string.Empty;

    /// <summary>
    /// WHICH FRAME ON THE PARENT, one-based, 0 for the parent's own transform.
    ///
    /// <para>
    /// One-based because <c>LookupAttachment</c> is, on both sides, and because 0
    /// is the engine's own spelling of "no attachment" — so a mapper's
    /// <c>parentattachment</c> naming something the model does not have lands on
    /// the parent's origin rather than on attachment 1, which is a different place
    /// on every model.
    /// </para>
    /// </summary>
    public int ParentAttachment { get; internal set; }

    /// <summary>
    /// Whether the PARENT is on the SDK's hierarchy rather than this one. A
    /// discriminator rather than a second handle field, because a handle is a
    /// handle: what differs is which resolution answers it.
    /// </summary>
    public bool ParentIsBase { get; internal set; }

    /// <summary>
    /// Whether <see cref="ParentName"/> has been looked up yet. False is not an
    /// error state: it is the whole window between the map's parse and the
    /// resolve pass.
    /// </summary>
    public bool Resolved { get; internal set; }

    /// <summary>
    /// THE OFFSET. Computed ONCE, at link time, and then left alone.
    ///
    /// <para>
    /// This is what parenting actually IS: the child keeps a fixed offset in its
    /// parent's space, and every composition afterwards reads this matrix rather
    /// than deriving it. A version that recomputed it from the current absolutes
    /// each frame would decay to the identity within a few frames, because each
    /// composition's output is the next derivation's input.
    /// </para>
    /// </summary>
    public Transform ChildToParent { get; internal set; } = Transform.Identity;

    /// <summary>Whether this link is one the per-frame sweep has to look at.</summary>
    public bool NeedsSweeping
        => ParentFrameRule.NeedsSweeping(Resolved, ParentAttachment, ParentIsBase);
}

/// <summary>
/// WHAT PARENTING NEEDS FROM THE WORLD, and it is deliberately small.
///
/// <para>
/// Everything the composition decides is above this line; everything it must ASK
/// is below it. Two things are genuinely native — posing a model's skeleton to
/// read an attachment frame out of it, and the partition/trigger work that
/// follows a position write — and the rest is here so that the whole of
/// <see cref="Hierarchy"/> can be driven by a test with no game running, which is
/// what makes "a child follows its parent through a rotation" a fact rather than
/// a thing somebody watched once.
/// </para>
/// </summary>
public interface IHierarchyWorld
{
    /// <summary>Whether this handle names a live entity on the parallel hierarchy.</summary>
    bool IsStandalone(EntityHandle handle);

    /// <summary>Whether this handle names a live entity on the SDK's hierarchy.</summary>
    bool IsBase(EntityHandle handle);

    /// <summary>
    /// Whether the parent has a skeleton an attachment frame could be read from.
    /// </summary>
    bool CanPose(EntityHandle handle);

    /// <summary>
    /// The entity's own frame — its cached world transform, which on this
    /// hierarchy is the engine block's <c>WorldTransform</c> and on the SDK's is
    /// <c>EntityToWorldTransform</c>.
    /// </summary>
    bool TryGetEntityFrame(EntityHandle handle, out Transform frame);

    /// <summary>
    /// A frame on the parent's MODEL, one-based. False means the model does not
    /// carry that attachment, and the caller is told rather than composed against
    /// a fallback silently.
    /// </summary>
    bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame);

    /// <summary>
    /// An attachment NAME to an index on this parent's model. One-based, 0 for a
    /// name the model does not carry — the engine's own spelling, and the same
    /// answer both sides' <c>LookupAttachment</c> gives.
    /// </summary>
    int LookupAttachment(EntityHandle handle, string attachmentName);

    /// <summary>Where the entity currently is, in the world.</summary>
    bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles);

    /// <summary>
    /// WHERE THE CHILD ENDED UP, written.
    ///
    /// <para>
    /// The block write and its marks are <see cref="HierarchyBlockWrite"/>'s;
    /// this is only "apply it". The implementation must NOT re-derive the offset
    /// from the new absolute — that is the difference between this and a public
    /// position setter, and re-deriving would write back the answer the
    /// composition had just produced.
    /// </para>
    /// </summary>
    void WriteComposed(EntityHandle child, Vector origin, QAngle angles, Transform childToWorld);

    /// <summary>
    /// The entity a <c>parentname</c> names on THIS hierarchy, or
    /// <see cref="EntityHandle.None"/>.
    /// </summary>
    EntityHandle FindStandaloneByName(string name);

    /// <summary>The same question of the SDK's hierarchy.</summary>
    EntityHandle FindBaseByName(string name);

    /// <summary>
    /// What to call this entity in a message — its targetname, or its classname
    /// when it has none. Never null.
    /// </summary>
    string NameOf(EntityHandle handle);

    /// <summary>
    /// The deferred delete. A child is removed WITH its parent rather than left
    /// standing where it was, which is what the SDK's own removal does and for a
    /// reason worth repeating: a child is almost always part of the thing rather
    /// than something near it.
    /// </summary>
    void Remove(EntityHandle handle);

    /// <summary>
    /// THE CHILD'S SOLVER OBJECT, DESTROYED — and it is the one thing linking
    /// does that is not arithmetic.
    ///
    /// <para>
    /// <b>A DIVERGENCE FROM STOCK, and the stated kind.</b> Stock hands a
    /// simulated child to a SHADOW controller: the solver keeps the object and is
    /// told to follow the parent's transform each tick, so the child still
    /// collides and still pushes things. There is no shadow support on this
    /// hierarchy, and leaving the object alone is strictly WORSE than destroying
    /// it — a static object cannot be moved by writing the entity's origin, and
    /// both live children in the shipped map are exactly that. Without this the
    /// drawn model follows its parent while an invisible collision hull stays at
    /// the map position for the rest of the round, stopping bullets and players
    /// in mid-air.
    /// </para>
    ///
    /// <para>
    /// So a parented child stops being simulated and becomes a thing that
    /// follows. It is on this interface rather than done by the caller because
    /// WHEN it happens is parenting's decision — at the moment a link is made,
    /// and never on a refusal — while WHAT it does is the engine's.
    /// </para>
    ///
    /// <para>
    /// <b>NOT UNDONE BY <see cref="Hierarchy.ClearParent"/>.</b> The object was
    /// built from the model's compiled collision data by whoever asked for it,
    /// and only they know whether it should be static, asleep or debris.
    /// Re-creating one here would be parenting guessing at a decision that
    /// belongs to the entity.
    /// </para>
    /// </summary>
    void DetachPhysics(EntityHandle child);

    /// <summary>A line on the console. Refusals are printed, never silent (§7).</summary>
    void Warn(string message);
}

/// <summary>
/// THE BLOCK WRITE A COMPOSITION MAKES, and — the point of the type — EXACTLY
/// WHAT IT MARKS.
///
/// <para>
/// <b>THREE FIELDS ARE WRITTEN AND TWO ARE MARKED.</b> The composed origin, the
/// composed angles and the composed world transform all go into the engine block,
/// because <c>CollisionToWorldTransform</c> hands back a reference into the block
/// rather than computing anything — the transform is STORAGE, and a composition
/// that wrote the origin and angles alone would leave every trace against the
/// child working from the frame it had before it moved.
/// </para>
///
/// <para>
/// <b>The transform is not MARKED, and that is not an oversight.</b> It is a value
/// derived from the two above it and nothing on the client decodes it: the client
/// shell rebuilds its own from the origin and angles it did decode. Marking it
/// would put twelve words of matrix on the local backdoor for a number the
/// receiver is about to recompute — and <c>MAX_CHANGE_OFFSETS</c> is
/// <see cref="EdictChangeOffsetLimit"/>, so a move that marked the origin, the
/// angles and the twelve words of the transform would spend eighteen of the
/// nineteen on one call and tip the edict into <c>FL_FULL_EDICT_CHANGED</c>
/// anyway. That is a bandwidth regression no functional test catches, because
/// both forms deliver the right VALUE.
/// </para>
///
/// <para>
/// So the mark set is a named, counted thing with facts over it rather than two
/// calls at the bottom of a composition.
/// </para>
/// </summary>
public static class HierarchyBlockWrite
{
    /// <summary>
    /// <c>MAX_CHANGE_OFFSETS</c> — how many individual offsets an edict can
    /// record before the engine gives up and marks the whole thing changed.
    /// </summary>
    public const int EdictChangeOffsetLimit = 19;

    /// <summary>
    /// The head fields a composition MARKS, in the order it marks them. Two, and
    /// a fact asserts the count: widening this to include the transform is the
    /// silent regression this type exists to make loud.
    /// </summary>
    public static IReadOnlyList<string> MarkedFields { get; } = new[]
    {
        nameof(EngineBlockHead.Origin),
        nameof(EngineBlockHead.Angles),
    };

    /// <summary>
    /// The head fields a composition WRITES. Three: the two above plus the cached
    /// world transform, which is storage rather than something computed per call.
    /// </summary>
    public static IReadOnlyList<string> WrittenFields { get; } = new[]
    {
        nameof(EngineBlockHead.Origin),
        nameof(EngineBlockHead.Angles),
        nameof(EngineBlockHead.WorldTransform),
    };

    /// <summary>
    /// The change offsets one composition records, measured from the object base
    /// — which is what <c>edict_t::StateChanged</c> takes and what a SendProp's
    /// own offset is measured from.
    ///
    /// <para>
    /// Built by asking <see cref="EngineBlockMarks"/> per field rather than by
    /// arithmetic of its own, so the composition and every shell setter compute a
    /// mark the same way. A prop built one way and a mark computed another leaves
    /// every partial update on the local backdoor matching nothing, with the right
    /// value still arriving by the slow path for ever.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> Marks(int blockBase)
    {
        if (blockBase < 0)
            throw new ArgumentOutOfRangeException(nameof(blockBase));

        var marks = new int[MarkedFields.Count];

        for (int i = 0; i < MarkedFields.Count; i++)
            marks[i] = EngineBlockMarks.MarksForWrite(MarkedFields[i], blockBase)[0];

        return marks;
    }

    /// <summary>
    /// The three fields, into an entity's blob. Answers false and writes NOTHING
    /// for a blob too short to hold the block — a partial write would leave the
    /// origin moved and the transform describing where it used to be, which is
    /// the one outcome worse than not moving it.
    /// </summary>
    public static bool Write(Span<byte> blob, Vector origin, QAngle angles, Transform childToWorld)
    {
        if (blob.Length < EngineBlock.SizeBytes)
            return false;

        MemoryMarshal.Write(blob.Slice(OffsetOf(nameof(EngineBlockHead.Origin))), in origin);
        MemoryMarshal.Write(blob.Slice(OffsetOf(nameof(EngineBlockHead.Angles))), in angles);
        MemoryMarshal.Write(blob.Slice(OffsetOf(nameof(EngineBlockHead.WorldTransform))), in childToWorld);

        return true;
    }

    /// <summary>Where one head field sits in the blob. The block is at offset 0.</summary>
    public static int OffsetOf(string headFieldName)
        => EngineBlockMarks.OffsetInBlockOf(headFieldName);

    /// <summary>
    /// THE COMPOSITION, APPLIED TO A LIVE ENTITY: the three fields written into
    /// its blob and the two offsets announced.
    ///
    /// <para>
    /// <see cref="IEngineNetworking.MarkEntityBlockChanged"/> takes an offset measured INSIDE
    /// the block and adds the block base itself — the one number managed code
    /// cannot know, because it is the alignment of the largest shell and moves with
    /// any edit to one. So the announcement is <c>Marks( 0 )</c>, which is the same
    /// arithmetic as every other mark with the base left to native.
    /// </para>
    ///
    /// <para>
    /// Answers false and announces NOTHING when the entity has no blob or the blob
    /// is too short: an entity that could not be written must not be reported as
    /// changed, or the client decodes the value it already had and the sender has
    /// spent a delta on it.
    /// </para>
    /// </summary>
    public static bool ApplyTo(IEngineEntities entities, IEngineNetworking networking,
                               EntityHandle child, Vector origin, QAngle angles,
                               Transform childToWorld)
    {
        // THE BLOCK AND NOT THE BLOB, and the difference is not a synonym.
        //
        // <see cref="IEngineNetworking.GetEntityBlob"/> answers the NETWORKED payload,
        // whose length is the entity's tier — eight words for a class that
        // declared no state of its own, which is most of a map. The block is the
        // fixed 352-byte prefix of the same allocation. Written through the
        // payload's length this refuses every entity below the tier that holds a
        // block, and refuses it SILENTLY, because a short span is a legal span:
        // the composition computes the right answer and then declines to store
        // it, and every child in the map stands still with nothing printed.
        if (!Write(entities.GetEngineBlock(child), origin, angles, childToWorld))
            return false;

        IReadOnlyList<int> marks = Marks(0);

        for (int i = 0; i < marks.Count; i++)
            networking.MarkEntityBlockChanged(child, marks[i]);

        return true;
    }
}

/// <summary>
/// THE PARENT LINKS OF ONE LEVEL, and everything that follows from them.
///
/// <para>
/// <b>NOT A STATIC.</b> One per side module (<c>plan.md</c> §1a). It holds a
/// level's worth of links and is emptied by <see cref="LevelShutdown"/>.
/// </para>
///
/// <para>
/// <b>Composition is EAGER.</b> Stock marks a subtree dirty and resolves it
/// inside the next read, which is why its dirty-transform flag exists and why the
/// SDK has to be careful about the order things are read in. Here a child is
/// composed at the moment its parent moves, and that single decision removes every
/// ordering question: by the time anything can observe a parent's new position its
/// children already have theirs, and depth falls out of the recursion rather than
/// needing a pass of its own. <see cref="RunFrame"/> is the narrow exception and
/// <see cref="ParentFrameRule.NeedsSweeping"/> says exactly how narrow.
/// </para>
/// </summary>
public sealed class Hierarchy
{
    private readonly IHierarchyWorld _world;

    /// <summary>
    /// Every link in the level, keyed by CHILD — which is also the statement that
    /// a child has at most one parent. Re-parenting is a reasonable thing to ask
    /// for, and two entries for one child would compose it twice from two
    /// different frames, the last one to run winning.
    /// </summary>
    private readonly Dictionary<EntityHandle, ParentLink> _links = new();

    /// <summary>
    /// A map's <c>parentattachment</c>, held between the parse and the link.
    ///
    /// <para>
    /// It cannot live in the link, because a map writes its keys in whatever order
    /// Hammer wrote them and <c>parentattachment</c> may arrive BEFORE
    /// <c>parentname</c> — before there is a link to put it in. It cannot be
    /// resolved at parse time either: an attachment is a NAME, looked up on the
    /// PARENT's model, and neither the parent nor its model is known yet.
    /// </para>
    /// </summary>
    private readonly Dictionary<EntityHandle, string> _pendingAttachments = new();

    /// <summary>Scratch, so the per-frame sweep allocates nothing when it finds nothing.</summary>
    private readonly List<ParentLink> _scratch = new();
    private readonly List<EntityHandle> _orphans = new();

    public Hierarchy(IHierarchyWorld world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>How many links the level holds. Zero in every map that parents nothing.</summary>
    public int LinkCount => _links.Count;

    /// <summary>How many parse-time attachment names are still waiting for a link.</summary>
    public int PendingAttachmentCount => _pendingAttachments.Count;

    /// <summary>Every link, in no defined order. For diagnostics and for tests.</summary>
    public IEnumerable<ParentLink> Links => _links.Values;

    /// <summary>The link naming this entity as its child, or null.</summary>
    public ParentLink? LinkFor(EntityHandle child)
        => _links.TryGetValue(child, out var link) ? link : null;

    //-------------------------------------------------------------------------
    // Asking.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether something CARRIES this entity — which is a different question from
    /// whether its frame can be built right now. A parent whose model has no such
    /// attachment still owns the child, and reporting it unparented would have a
    /// flight or a solver step move it out of its parent's hands.
    /// </summary>
    public bool IsParented(EntityHandle child)
    {
        var link = LinkFor(child);

        return link is not null && link.Resolved && ParentIsAlive(link);
    }

    /// <summary>
    /// The parent, as an entity of OURS only — the shape the cycle walk needs.
    ///
    /// <para>
    /// A chain can only be walked upward through entities this registry can hold
    /// as children, and it holds none from the other hierarchy. A link to an SDK
    /// parent therefore ENDS the chain, which is not a limitation but the fact:
    /// nothing on that side is a child of anything here.
    /// </para>
    /// </summary>
    public EntityHandle StandaloneParentOf(EntityHandle child)
    {
        var link = LinkFor(child);

        if (link is null || !link.Resolved || link.ParentIsBase)
            return EntityHandle.None;

        return _world.IsStandalone(link.Parent) ? link.Parent : EntityHandle.None;
    }

    /// <summary>
    /// WHICH FRAME, resolved: the rule applied to what the world says about this
    /// parent right now.
    /// </summary>
    public ParentFrameKind FrameKindOf(EntityHandle parent, int parentAttachment)
    {
        bool standalone = _world.IsStandalone(parent);
        bool baseSide = !standalone && _world.IsBase(parent);

        return ParentFrameRule.KindFor(standalone, baseSide, parentAttachment,
                                       _world.CanPose(parent));
    }

    /// <summary>
    /// THE FRAME A CHILD HANGS FROM. One method, and every composition goes
    /// through it — which is what keeps "parented to an origin" and "parented to
    /// an attachment" from drifting into two different answers.
    /// </summary>
    public bool TryGetParentFrame(EntityHandle parent, int parentAttachment, out Transform frame)
    {
        frame = Transform.Identity;

        switch (FrameKindOf(parent, parentAttachment))
        {
            case ParentFrameKind.StandaloneOrigin:
            case ParentFrameKind.BaseOrigin:
                return _world.TryGetEntityFrame(parent, out frame);

            case ParentFrameKind.StandaloneAttachment:
            case ParentFrameKind.BaseAttachment:
                return _world.TryGetAttachmentFrame(parent, parentAttachment, out frame);

            default:
                return false;
        }
    }

    /// <summary>
    /// The frame this child hangs from, whichever hierarchy holds its parent and
    /// whether or not an attachment was named. Answers false for an entity with no
    /// parent, for a parent that has gone, and for an attachment frame that could
    /// not be built — three reasons a caller does not need to tell apart, because
    /// all three mean "compose nothing".
    /// </summary>
    public bool TryGetParentFrameOf(EntityHandle child, out Transform frame)
    {
        frame = Transform.Identity;

        var link = LinkFor(child);
        if (link is null || !link.Resolved || !ParentIsAlive(link))
            return false;

        return TryGetParentFrame(link.Parent, link.ParentAttachment, out frame);
    }

    //-------------------------------------------------------------------------
    // The arithmetic.
    //-------------------------------------------------------------------------

    /// <summary>
    /// ONE CHILD PLACED IN ITS PARENT'S FRAME.
    ///
    /// <code>childToWorld = parentToWorld * childToParent</code>
    ///
    /// <para>
    /// The whole of parenting, and the reason it is a named method rather than one
    /// line inside the two callers: the two things that can move a child — its
    /// parent being written, and its parent's attachment clock advancing — must
    /// put it in the same place BY CONSTRUCTION rather than by two copies of the
    /// same three lines agreeing.
    /// </para>
    /// </summary>
    public static Transform Compose(Transform parentToWorld, Transform childToParent)
        => parentToWorld * childToParent;

    /// <summary>
    /// THE OFFSET, from where the two of them currently are.
    ///
    /// <code>childToParent = worldToParent * childToWorld</code>
    ///
    /// <para>
    /// <see cref="Compose"/> backwards. The child's frame is rebuilt from its
    /// origin and angles rather than read out of its cached transform, so this
    /// does not depend on when the child's matrix was last touched.
    /// </para>
    /// </summary>
    public static Transform MeasureOffset(Transform parentToWorld,
                                          Vector childOrigin, QAngle childAngles)
        => parentToWorld.InverseTR() * Transform.FromOriginAngles(childOrigin, childAngles);

    //-------------------------------------------------------------------------
    // Linking.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE LINK. Either hierarchy may hold the parent; the child is always ours.
    ///
    /// <para>
    /// A parent of <see cref="EntityHandle.None"/> is how unparenting is said, the
    /// way the SDK's own <c>SetParent</c> says it.
    /// </para>
    /// </summary>
    public ParentResult SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0)
    {
        if (!_world.IsStandalone(child))
            return new ParentResult(ParentRefusal.NoChild,
                "[SourceSharp] cannot parent an entity this hierarchy does not hold");

        // A NULL parent is how unparenting is said.
        if (parent == EntityHandle.None || (!_world.IsStandalone(parent) && !_world.IsBase(parent)))
        {
            if (parent == EntityHandle.None)
            {
                ClearParent(child);
                return ParentResult.Ok;
            }

            return new ParentResult(ParentRefusal.NotSpawned,
                $"[SourceSharp] cannot parent '{_world.NameOf(child)}' before both it and " +
                "its parent have spawned");
        }

        // Only the standalone arm can close a loop, because only entities on this
        // side are ever children here — an SDK parent ENDS the chain.
        if (_world.IsStandalone(parent) && WouldCycle(child, parent))
        {
            return new ParentResult(ParentRefusal.Cycle,
                $"[SourceSharp] refusing to parent '{_world.NameOf(child)}' to " +
                $"'{_world.NameOf(parent)}': it would close a cycle");
        }

        if (parentAttachment < 0)
            parentAttachment = 0;

        // One parent at a time.
        ClearParent(child);

        // AND THE CHILD STOPS BEING SIMULATED. After the refusals and before the
        // link, which is the only order that is right: a refused parenting must
        // leave the child exactly as it was, and a link made while the solver
        // still owned the object would compose a child whose position the solver
        // overwrites on the next tick. See IHierarchyWorld.DetachPhysics for why
        // this is a divergence rather than a detail, and for why unparenting does
        // not give the object back.
        _world.DetachPhysics(child);

        var link = new ParentLink(child)
        {
            Parent = parent,
            Resolved = true,
            ParentAttachment = parentAttachment,
            ParentIsBase = !_world.IsStandalone(parent),
            ParentName = _world.NameOf(parent),
        };

        // The offset, measured from the frame the child will actually hang from —
        // the attachment's if one was named, and the entity's otherwise. Measuring
        // it against the entity and then composing against an attachment is the
        // arithmetic version of parenting to the wrong thing, and it looks like a
        // model that jumps once at link time and then follows correctly.
        if (!TryMeasure(link))
        {
            // A named attachment the model does not have. The link is still made
            // and the child sits ON the parent's frame rather than nowhere,
            // because the alternative is a silent refusal that leaves a decoration
            // behind when its parent moves.
            _world.Warn($"[SourceSharp] '{_world.NameOf(child)}' is parented to attachment " +
                        $"{parentAttachment} of '{link.ParentName}', which that model does not " +
                        "have; it will follow the parent's own origin");

            link.ParentAttachment = 0;

            if (!TryMeasure(link))
                link.ChildToParent = Transform.Identity;
        }

        _links[child] = link;
        return ParentResult.Ok;
    }

    /// <summary>
    /// The offset measured and stored, or false when the frame could not be built.
    /// </summary>
    private bool TryMeasure(ParentLink link)
    {
        if (!TryGetParentFrame(link.Parent, link.ParentAttachment, out Transform parentToWorld))
            return false;

        if (!_world.TryGetAbsolute(link.Child, out Vector origin, out QAngle angles))
            return false;

        link.ChildToParent = MeasureOffset(parentToWorld, origin, angles);
        return true;
    }

    /// <summary>
    /// WOULD THIS LINK CLOSE A LOOP?
    ///
    /// <para>
    /// Stock only asserts on an entity parented to itself, which is the easy half.
    /// A genuine cycle here is worse than a wrong transform: the composition
    /// recurses into the grandchildren, so a loop is unbounded recursion and a
    /// stack overflow names nothing that has anything to do with parenting.
    /// </para>
    ///
    /// <para>
    /// The walk is BOUNDED as well as tested, because it is only as trustworthy as
    /// the registry it walks: a loop somehow already present would hang here
    /// instead of being reported.
    /// </para>
    /// </summary>
    public bool WouldCycle(EntityHandle child, EntityHandle parent)
    {
        EntityHandle ancestor = parent;

        for (int depth = 0; ancestor != EntityHandle.None; depth++)
        {
            if (ancestor == child)
                return true;

            // A chain cannot be longer than the number of links there are, so
            // running past that means the registry already loops.
            if (depth > _links.Count)
                return true;

            ancestor = StandaloneParentOf(ancestor);
        }

        return false;
    }

    /// <summary>
    /// Unparented. The link is dropped and the child is left exactly where it is,
    /// in the world's frame, which is where the last composition put it.
    /// </summary>
    public bool ClearParent(EntityHandle child) => _links.Remove(child);

    /// <summary>
    /// Puts a saved link back, EXACTLY as it was, with no side effects.
    ///
    /// <para>
    /// <b>WHY NOT JUST CALL <see cref="SetParent"/>.</b> A reload does not move
    /// anything: the native world stands, both entities are where they were, and
    /// the link that has to come back is the one that was measured when the
    /// parenting was first asked for. <c>SetParent</c> would re-measure it from
    /// the two entities' CURRENT world transforms — which is the same answer up
    /// to float drift for a simple frame, and a different one entirely for a
    /// child hung off a model ATTACHMENT, whose frame depends on a pose the
    /// engine may not have built yet at restore time. It would also re-run
    /// <c>DetachPhysics</c> and re-run the cycle check against a half-restored
    /// table.
    /// </para>
    ///
    /// <para>
    /// So the offset is carried and replayed rather than recomputed. What is NOT
    /// carried is the composition: the child is already in the right place,
    /// because it never went anywhere.
    /// </para>
    ///
    /// <para>
    /// The cycle check IS still made, because the records come from outside this
    /// object — a <c>.sav</c> from another build, a restore replayed twice — and
    /// a cycle in the table is an infinite recursion in
    /// <see cref="ParentMoved"/> rather than a wrong position.
    /// </para>
    ///
    /// <para>Answers whether the link was added.</para>
    /// </summary>
    public bool RestoreLink(EntityHandle child, EntityHandle parent, int parentAttachment,
                            bool parentIsBase, bool resolved, Transform childToParent,
                            string parentName, string parentAttachmentName)
    {
        if (child == EntityHandle.None || child == parent)
            return false;

        if (parent != EntityHandle.None && WouldCycle(child, parent))
        {
            _world.Warn($"[SourceSharp] reload: refusing to restore the parent link " +
                           $"{child} -> {parent}: it would close a cycle\n");
            return false;
        }

        _links[child] = new ParentLink(child)
        {
            Parent = parent,
            ParentAttachment = parentAttachment < 0 ? 0 : parentAttachment,
            ParentIsBase = parentIsBase,
            Resolved = resolved,
            ChildToParent = childToParent,
            ParentName = parentName ?? string.Empty,
            ParentAttachmentName = parentAttachmentName ?? string.Empty,
        };

        return true;
    }

    /// <summary>
    /// The parse-time attachment names that have not become links yet, so they
    /// can be carried across a reload too.
    ///
    /// <para>
    /// A map writes <c>parentattachment</c> in whatever order Hammer wrote it
    /// and it may arrive before <c>parentname</c>. A reload that dropped these
    /// would leave a child that had not yet been linked hanging off its parent's
    /// ORIGIN instead of off the attachment the mapper named — which is a model
    /// in the wrong place and nothing in any log.
    /// </para>
    /// </summary>
    public IEnumerable<(EntityHandle Child, string Attachment)> PendingAttachments()
    {
        foreach (var (child, name) in _pendingAttachments)
            yield return (child, name);
    }

    //-------------------------------------------------------------------------
    // The map's parentname.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The map's <c>parentattachment</c>, recorded.
    ///
    /// <para>
    /// Taken ONCE when the link is made: the name belongs to the link from then
    /// on, and an entry left behind would be picked up by a later re-parent that
    /// never asked for it. An empty value is a mapper clearing the field, and
    /// clearing it is all it should do.
    /// </para>
    /// </summary>
    public void SetParentAttachmentName(EntityHandle child, string? attachmentName)
    {
        if (child == EntityHandle.None)
            return;

        _pendingAttachments.Remove(child);

        if (!string.IsNullOrEmpty(attachmentName))
            _pendingAttachments[child] = attachmentName;
    }

    /// <summary>
    /// WHAT THE MAP WROTE, remembered until it can be acted on.
    ///
    /// <para>
    /// The name goes in the registry rather than into a field on the entity: a
    /// 64-byte field would be carried by every one of the six hundred entities in a
    /// map so that two of them could use it. An empty <c>parentname</c> is a mapper
    /// clearing the field, which means no parent.
    /// </para>
    /// </summary>
    public void SetParentName(EntityHandle child, string? parentName)
    {
        if (child == EntityHandle.None)
            return;

        ClearParent(child);

        if (string.IsNullOrEmpty(parentName))
            return;

        var link = new ParentLink(child)
        {
            ParentName = parentName,
            Resolved = false,

            // Identity until it is resolved, so an entry read before the resolve
            // pass composes the child onto itself rather than onto whatever the
            // matrix happened to hold.
            ChildToParent = Transform.Identity,
        };

        if (_pendingAttachments.Remove(child, out string? attachment))
            link.ParentAttachmentName = attachment;

        _links[child] = link;
    }

    /// <summary>
    /// TURNS THE NAMES INTO LINKS, once every entity has spawned.
    ///
    /// <para>
    /// A third pass, and it has to be a third one. The names exist from parse time,
    /// but the OFFSET a child keeps is measured from where the two of them ended
    /// up — and an entity does not have that until its own <c>Spawn</c> has run.
    /// </para>
    ///
    /// <para>
    /// <b>THIS HIERARCHY FIRST</b>, and the order is the answer to an ambiguity
    /// rather than a preference: two entities of different kinds may carry the same
    /// targetname, and a map that parents to a name meaning both wants the one it
    /// can see.
    /// </para>
    ///
    /// <para>Answers how many links it made.</para>
    /// </summary>
    public int ResolveParentNames()
    {
        _scratch.Clear();

        foreach (var link in _links.Values)
        {
            if (!link.Resolved)
                _scratch.Add(link);
        }

        int made = 0;

        foreach (var link in _scratch)
        {
            // Dropped as it is dealt with — resolved or not, it has had its turn.
            _links.Remove(link.Child);

            // Named by an entity that has since gone — a template source consumed
            // between the parse and here, most likely.
            if (!_world.IsStandalone(link.Child))
                continue;

            EntityHandle parent = _world.FindStandaloneByName(link.ParentName);

            if (parent == EntityHandle.None)
                parent = _world.FindBaseByName(link.ParentName);

            if (parent == EntityHandle.None)
            {
                _world.Warn($"[SourceSharp] '{_world.NameOf(link.Child)}' is parented to " +
                            $"'{link.ParentName}', which no entity is called");
                continue;
            }

            // The attachment, resolved NOW because now is when the parent's model
            // is known. Asked of the parent through the same LookupAttachment both
            // sides answer with, so a name means the same index here as it does
            // where the muzzle flash is drawn.
            int attachment = 0;

            if (link.ParentAttachmentName.Length != 0)
            {
                attachment = _world.LookupAttachment(parent, link.ParentAttachmentName);

                if (attachment <= 0)
                {
                    _world.Warn($"[SourceSharp] '{_world.NameOf(link.Child)}' is parented to " +
                                $"attachment '{link.ParentAttachmentName}' of '{link.ParentName}', " +
                                "which that model does not have; it will follow the parent's origin");
                }
            }

            ParentResult result = SetParent(link.Child, parent, attachment);

            if (result.Granted)
                made++;
            else
                _world.Warn(result.Message);
        }

        _scratch.Clear();
        return made;
    }

    //-------------------------------------------------------------------------
    // Composing.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A PARENT HAS MOVED, SO ITS CHILDREN MOVE WITH IT.
    ///
    /// <para>
    /// Called at the single place an absolute position is written on this
    /// hierarchy, which is what makes composition eager rather than deferred and
    /// therefore what makes ordering a non-question. Recursion carries it to the
    /// grandchildren.
    /// </para>
    ///
    /// <para>
    /// The children are COPIED OUT before a single one is moved, and that is not
    /// tidiness: moving a child recurses back into this method for the
    /// grandchildren, and a link whose child has gone is dropped where it is found
    /// — so the collection this walks can be both shortened and appended to
    /// underneath the walk.
    /// </para>
    ///
    /// <para>Answers how many children it composed.</para>
    /// </summary>
    public int ParentMoved(EntityHandle parent)
    {
        if (_links.Count == 0 || parent == EntityHandle.None)
            return 0;

        var children = new List<ParentLink>();
        List<EntityHandle>? dead = null;

        foreach (var link in _links.Values)
        {
            if (!link.Resolved || link.Parent != parent)
                continue;

            if (!_world.IsStandalone(link.Child))
            {
                (dead ??= new List<EntityHandle>()).Add(link.Child);
                continue;
            }

            children.Add(link);
        }

        if (dead is not null)
        {
            foreach (EntityHandle gone in dead)
                _links.Remove(gone);
        }

        int composed = 0;

        foreach (var link in children)
        {
            if (ComposeOne(link))
                composed++;
        }

        return composed;
    }

    /// <summary>
    /// One link composed and written. Answers false when the child has gone, the
    /// parent has gone, or the frame could not be built — in which case the child
    /// is left exactly where it is rather than collapsed onto the parent's origin.
    /// </summary>
    public bool ComposeOne(ParentLink link)
    {
        if (!_world.IsStandalone(link.Child) || !link.Resolved || !ParentIsAlive(link))
            return false;

        if (!TryGetParentFrame(link.Parent, link.ParentAttachment, out Transform parentToWorld))
            return false;

        Transform childToWorld = Compose(parentToWorld, link.ChildToParent);

        _world.WriteComposed(link.Child, childToWorld.Translation, childToWorld.ToAngles(),
                             childToWorld);

        // And then the grandchildren, from the position this one now has.
        ParentMoved(link.Child);

        return true;
    }

    /// <summary>
    /// A CHILD HAS MOVED ITSELF, so its offset from its parent is now something
    /// else.
    ///
    /// <para>
    /// The other half of the split, called from the public position setters. The
    /// SDK draws exactly this line: it writes the absolute and then recomputes the
    /// local, because "put this here" said of a parented entity means here in the
    /// world, once, and relative to the parent from then on.
    /// </para>
    /// </summary>
    public void ChildMoved(EntityHandle child)
    {
        var link = LinkFor(child);

        if (link is null || !link.Resolved)
            return;

        // The parent has gone. Dropped here rather than remembered as broken,
        // which is the invariant this registry exists to keep: an entry that
        // cannot name both of its ends does not stay.
        if (!ParentIsAlive(link))
        {
            _links.Remove(child);
            return;
        }

        // A frame that cannot be built leaves the offset ALONE rather than
        // recomputing it against a matrix nobody wrote. The child keeps the offset
        // it had, which is the value the next successful composition wants.
        TryMeasure(link);
    }

    //-------------------------------------------------------------------------
    // The two frames.
    //
    // An entity with no parent lives in the world's frame and one with a parent
    // lives in its parent's, and code that MOVES an entity has to know which:
    // integrating a parented entity in world space means its own step and its
    // parent's composition writing the same origin from two different frames, each
    // undoing half of the other's every frame. The symptom is jitter, which reads
    // as a solver problem.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A world place and facing, expressed in the child's parent's frame. False,
    /// and both arguments untouched, for an entity with no parent — so a caller
    /// that does not care can ignore the result and still be correct.
    /// </summary>
    public bool WorldToLocal(EntityHandle child, ref Vector origin, ref QAngle angles)
    {
        if (!TryGetParentFrameOf(child, out Transform parentToWorld))
            return false;

        Transform local = parentToWorld.InverseTR() * Transform.FromOriginAngles(origin, angles);

        origin = local.Translation;
        angles = local.ToAngles();
        return true;
    }

    /// <summary>The other direction, and the reason the offset is stored at all.</summary>
    public bool LocalToWorld(EntityHandle child, ref Vector origin, ref QAngle angles)
    {
        if (!TryGetParentFrameOf(child, out Transform parentToWorld))
            return false;

        Transform world = Compose(parentToWorld, Transform.FromOriginAngles(origin, angles));

        origin = world.Translation;
        angles = world.ToAngles();
        return true;
    }

    /// <summary>
    /// A NEW OFFSET, recorded and applied.
    ///
    /// <para>
    /// Recorded FIRST, because applying it composes this entity's own children —
    /// and they should be composed against the frame this call is establishing
    /// rather than the one it is replacing.
    /// </para>
    ///
    /// <para>
    /// An entity that turns out not to be parented is written in world space
    /// instead of asserted: a movement started while parented can outlive the
    /// parent, and the entity should finish it where it stands instead of stopping.
    /// </para>
    /// </summary>
    public void SetLocal(EntityHandle child, Vector origin, QAngle angles)
    {
        var link = LinkFor(child);

        if (link is null || !link.Resolved)
        {
            Transform world = Transform.FromOriginAngles(origin, angles);
            _world.WriteComposed(child, origin, angles, world);
            ParentMoved(child);
            return;
        }

        link.ChildToParent = Transform.FromOriginAngles(origin, angles);

        ComposeOne(link);
    }

    //-------------------------------------------------------------------------
    // The frame, and teardown.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE LINKS THIS HIERARCHY CANNOT SEE MOVE, once a frame.
    ///
    /// <para>
    /// Everything else is composed at the instant a position is written, and that
    /// stays true — <see cref="ParentFrameRule.NeedsSweeping"/> names the two kinds
    /// of link that funnel cannot reach.
    /// </para>
    ///
    /// <para>
    /// <b>THE DEAD LINKS GO FIRST</b>, and this pass is the only place some of them
    /// can be noticed at all. A parent on this hierarchy announces its own removal
    /// through <see cref="EntityRemoved"/>; an SDK parent announces nothing, which
    /// is the same reason its MOVES have to be swept. Without this the child of a
    /// deleted SDK parent would stand where it was for ever, holding a link naming
    /// a handle whose slot the engine has already given to something else.
    /// </para>
    ///
    /// <para>
    /// Runs AFTER the frame's thinks and before the deferred deletes — a think can
    /// advance a parent's sequence, so an attachment composed before them would be
    /// one frame stale.
    /// </para>
    ///
    /// <para>Answers how many links it composed.</para>
    /// </summary>
    public int RunFrame()
    {
        if (_links.Count == 0)
            return 0;

        _orphans.Clear();

        foreach (var link in _links.Values)
        {
            if (link.Resolved && !ParentIsAlive(link))
                _orphans.Add(link.Child);
        }

        foreach (EntityHandle orphan in _orphans)
        {
            _links.Remove(orphan);

            // Removed WITH the parent rather than left standing, which is the
            // answer EntityRemoved already gives for a parent on this hierarchy.
            // The two paths agree on purpose: a child is almost always part of the
            // thing rather than something near it, and which hierarchy held the
            // parent is not a reason to treat the decoration differently.
            if (_world.IsStandalone(orphan))
                _world.Remove(orphan);
        }

        _orphans.Clear();

        _scratch.Clear();

        foreach (var link in _links.Values)
        {
            if (link.NeedsSweeping)
                _scratch.Add(link);
        }

        int composed = 0;

        foreach (var link in _scratch)
        {
            if (ComposeOne(link))
                composed++;
        }

        _scratch.Clear();
        return composed;
    }

    /// <summary>
    /// AN ENTITY IS GOING, in both directions.
    ///
    /// <para>
    /// The second direction is the part that is easy to forget: an entry naming a
    /// dead PARENT would hand the next walk a handle whose slot has been recycled.
    /// Resolving it would answer nobody and the entry would be dropped then — but
    /// only once something happened to walk it, and in the meantime the child is a
    /// thing that used to follow something and now does not, with nothing anywhere
    /// saying why.
    /// </para>
    ///
    /// <para>
    /// <b>BOTH registries</b>, not just the links: an entity can carry a pending
    /// attachment name and no link at all — that is exactly the window this
    /// registry exists for — and an early return on the links alone would leave its
    /// handle behind to be matched by whatever is given the slot next.
    /// </para>
    ///
    /// <para>Answers how many children were detached and removed with it.</para>
    /// </summary>
    public int EntityRemoved(EntityHandle entity)
    {
        if (entity == EntityHandle.None)
            return 0;

        _pendingAttachments.Remove(entity);
        _links.Remove(entity);

        if (_links.Count == 0)
            return 0;

        _orphans.Clear();

        foreach (var link in _links.Values)
        {
            if (link.Resolved && link.Parent == entity)
                _orphans.Add(link.Child);
        }

        // Collected while the registry is walked and acted on afterwards: the
        // removal below is deferred, but it is still one more thing that must not
        // happen halfway through a walk of a collection it can change.
        int detached = _orphans.Count;

        foreach (EntityHandle orphan in _orphans)
        {
            _links.Remove(orphan);
            _world.Remove(orphan);
        }

        _orphans.Clear();
        return detached;
    }

    /// <summary>
    /// The level is going.
    ///
    /// <para>
    /// Called BEFORE the entities are deleted rather than after, and the order is
    /// the whole point: with the registry already empty, no removal finds a child
    /// to orphan and no entity is queued for a deferred delete that nothing will
    /// ever drain.
    /// </para>
    /// </summary>
    public void LevelShutdown()
    {
        _links.Clear();
        _pendingAttachments.Clear();
        _scratch.Clear();
        _orphans.Clear();
    }

    /// <summary>
    /// Whether the parent end still resolves, ON THE HIERARCHY THE OFFSET WAS
    /// MEASURED AGAINST. A slot answering the other hierarchy now is a different
    /// entity wearing the same number, which is precisely the case a raw pointer
    /// would have got wrong — so it is treated as the parent having gone.
    /// </summary>
    private bool ParentIsAlive(ParentLink link)
        => link.ParentIsBase ? _world.IsBase(link.Parent) : _world.IsStandalone(link.Parent);
}
