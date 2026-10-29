using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IHierarchyWorld"/> ANSWERED BY THE RUNNING GAME — the half step 2h
/// deliberately did not write, and the reason its model was not the live path.
///
/// <para>
/// 2h's own handover says why it stopped here: <i>"a stub answering false would
/// be a live wrong answer the day it was wired — a muzzle flash silently composed
/// against an origin."</i> Nothing in this class answers a question it cannot
/// actually answer. The one capability that had no route out of managed code —
/// an attachment frame, which is a posed skeleton — is
/// <see cref="IEngineEntities.GetAttachmentFrame"/>, added as a slot rather than
/// re-derived here.
/// </para>
///
/// <para>
/// <b>IT HOLDS NO POLICY AND THAT IS THE TEST OF IT.</b> Every method below is a
/// question put to the engine and the answer handed back unchanged. What to DO
/// with the answers — which frame a child hangs from, whether a link would close
/// a cycle, when an offset is re-measured, what is composed and in what order —
/// is <see cref="Hierarchy"/>'s, and none of it is repeated here. A method in
/// this file that made a decision would be native policy that had merely moved
/// house.
/// </para>
///
/// <para>
/// <b>WHAT IT COSTS, STATED (the lane brief requires it).</b> These are
/// managed→native calls, which the crossing counters do NOT count — they count
/// inbound thunk entries. So the numbers here are not in
/// <c>tools/live-gate-budget.tsv</c> and are written down instead:
/// <see cref="TryGetParentFrameKindInputs"/> is three probes,
/// <see cref="TryGetEntityFrame"/> is one blob fetch or two reads,
/// <see cref="WriteComposed"/> is one blob fetch, two marks and one dispatch.
/// One composed child per frame is therefore of the order of ten outbound calls.
/// Nothing here caches, deliberately: a memo of "is this handle still alive" is
/// exactly the stale answer the handle discipline exists to prevent, and the
/// later performance pass has this paragraph to work from.
/// </para>
/// </summary>
/// <summary>
/// AN <see cref="IHierarchyWorld"/> THAT KNOWS WHEN IT IS BEING COMPOSED
/// THROUGH.
///
/// <para>
/// One property, and it is here rather than on <see cref="IHierarchyWorld"/>
/// because it is not a question about the world. It is a fact about the CALL
/// STACK — whether a composition is unwound far enough that an entry point
/// arriving now is the native position funnel coming back round, rather than
/// the game asking for something new. <see cref="Hierarchy"/> has no business
/// knowing that; <c>HierarchyDriver</c>, which owns the entry points, has
/// nothing else to learn it from.
/// </para>
///
/// <para>
/// It is also what lets the driver be tested without a game: a fake world that
/// implements this drives every entry point, and the one that matters — the
/// re-entrant <c>ParentMoved</c> — can be produced deliberately.
/// </para>
/// </summary>
public interface IComposedWorld : IHierarchyWorld
{
    /// <summary>Whether a composition is on the stack right now.</summary>
    bool IsComposing { get; }

    /// <summary>
    /// The entity's engine block — THE BLOCK, NOT THE BLOB (see
    /// <see cref="IEngineEntities.GetEngineBlock"/>) — or an empty span when it
    /// has none. Here so the driver's guard words reach the block through the
    /// side's world rather than a process-wide engine.
    /// </summary>
    Span<byte> BlockOf(EntityHandle entity);
}

public sealed class EngineHierarchyWorld : IComposedWorld
{
    private readonly ILog _log;

    /// <summary>
    /// The side's entity and networking areas, handed over by whoever composes
    /// the side's parenting (<c>StandaloneHierarchy</c>). Every question below is
    /// put to THESE, never to a process-wide engine, so two game instances in one
    /// process each compose against their own entities.
    /// </summary>
    private readonly IEngineEntities _entities;
    private readonly IEngineNetworking _networking;
    private readonly IEngineEntityPhysics _physics;

    /// <param name="log">Where the hierarchy's warnings go (statics to zero).</param>
    public EngineHierarchyWorld(IEngineEntities entities, IEngineNetworking networking, IEngineEntityPhysics physics, ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _entities = entities;
        _networking = networking;
        _physics = physics;
    }

    /// <summary>
    /// How deep the composition currently is — and the ONE piece of state here.
    ///
    /// <para>
    /// <b>Why it exists.</b> <see cref="WriteComposed"/> ends by telling native
    /// that the child's position changed, because the partition entry and the
    /// triggers are native's and a blob write alone would leave a moved prop
    /// still occupying its old volume. The native funnel for that ends by asking
    /// managed code to move the child's OWN children — so a composition of a
    /// grandparent re-enters the hierarchy through the front door, while
    /// <see cref="Hierarchy.ComposeOne"/> is already recursing into exactly the
    /// same links.
    /// </para>
    ///
    /// <para>
    /// So the re-entry is suppressed and the managed recursion is the authority.
    /// It is counted rather than a bool because the composition nests: a
    /// grandchild's write re-enters while the grandparent's is still unwound.
    /// </para>
    ///
    /// <para>
    /// The suppression is read by the driver, not applied here, because the
    /// thing being suppressed is an ENTRY POINT and this class has none — see
    /// <c>HierarchyDriver.ParentMoved</c>.
    /// </para>
    /// </summary>
    public int ComposeDepth { get; private set; }

    /// <inheritdoc/>
    public bool IsComposing => ComposeDepth > 0;

    /// <inheritdoc/>
    public Span<byte> BlockOf(EntityHandle entity) => _entities.GetEngineBlock(entity);

    public bool IsStandalone(EntityHandle handle)
        => (_entities.ParentProbe(handle) & ParentProbe.Standalone) != 0;

    public bool IsBase(EntityHandle handle)
        => (_entities.ParentProbe(handle) & ParentProbe.Base) != 0;

    public bool CanPose(EntityHandle handle)
        => (_entities.ParentProbe(handle) & ParentProbe.CanPose) != 0;

    /// <summary>
    /// The three probe answers together, for a caller that wants the frame rule's
    /// inputs and would otherwise ask three times. Named so the cost paragraph
    /// above has something to point at; <see cref="Hierarchy"/> does not use it,
    /// because the interface it is written against has three questions.
    /// </summary>
    public ParentProbe TryGetParentFrameKindInputs(EntityHandle handle)
        => _entities.ParentProbe(handle);

    /// <summary>
    /// The entity's own frame.
    ///
    /// <para>
    /// <b>Two routes, and they compute the same matrix.</b> On this hierarchy the
    /// cached transform IS storage — the block's <c>WorldTransform</c>, which
    /// <c>CollisionToWorldTransform</c> hands back a reference into — so it is
    /// read straight out of the blob. On the SDK's it is
    /// <c>m_rgflCoordinateFrame</c>, which has no slot and does not need one:
    /// <c>CBaseEntity::CalcAbsolutePosition</c> builds it with
    /// <c>AngleMatrix( m_angAbsRotation, m_vecAbsOrigin )</c>, which is exactly
    /// <see cref="Transform.FromOriginAngles"/> of the two values that DO have
    /// slots. Adding a twelve-float getter to fetch a value that is a function of
    /// two we already fetch would be a second answer to one question.
    /// </para>
    /// </summary>
    public bool TryGetEntityFrame(EntityHandle handle, out Transform frame)
    {
        frame = Transform.Identity;

        ParentProbe probe = _entities.ParentProbe(handle);

        if ((probe & ParentProbe.Standalone) != 0)
        {
            // THE BLOCK, NOT THE BLOB — see IEngineEntities.GetEngineBlock. The payload's
            // length is the entity's tier and stops well short of the transform
            // for every class that declared no state of its own.
            Span<byte> block = _entities.GetEngineBlock(handle);

            if (block.Length < EngineBlock.SizeBytes)
                return false;

            frame = MemoryMarshal.Read<Transform>(
                block.Slice(HierarchyBlockWrite.OffsetOf(nameof(EngineBlockHead.WorldTransform))));

            return true;
        }

        if ((probe & ParentProbe.Base) == 0)
            return false;

        frame = Transform.FromOriginAngles(_entities.GetOrigin(handle),
                                           QAngle.FromVector(_entities.GetAngles(handle)));
        return true;
    }

    public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame)
    {
        frame = Transform.Identity;

        if (!_entities.GetAttachmentFrame(handle, attachment,
                                        out Vector origin, out QAngle angles))
        {
            return false;
        }

        frame = Transform.FromOriginAngles(origin, angles);
        return true;
    }

    public int LookupAttachment(EntityHandle handle, string attachmentName)
        => _entities.LookupAttachmentAny(handle, attachmentName);

    public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles)
    {
        origin = default;
        angles = default;

        if (_entities.ParentProbe(handle) == ParentProbe.None)
            return false;

        origin = _entities.GetOrigin(handle);
        angles = QAngle.FromVector(_entities.GetAngles(handle));
        return true;
    }

    /// <summary>
    /// WHERE THE CHILD ENDED UP, WRITTEN — and then said, which is the half a
    /// blob write on its own would miss.
    ///
    /// <para>
    /// <b>The write is <see cref="HierarchyBlockWrite.ApplyTo"/></b>, unchanged
    /// from step 2h: three head fields written and two of them marked. That type
    /// is where "what a composition marks" is decided and tested, and this is the
    /// production caller <c>MarkEntityBlockChanged</c> was documented as
    /// not yet having.
    /// </para>
    ///
    /// <para>
    /// <b>Then <see cref="IEngineEntities.PositionChanged"/>, and it is not
    /// optional.</b> A composed prop that only had its blob written would keep its
    /// old entry in the spatial partition — so traces and collisions would still
    /// meet it where it used to be — and would fire no trigger it had just been
    /// carried into. Both of those are the engine's own bookkeeping, done by
    /// <c>CStandaloneEntity::OnPositionChanged</c>, which is exactly what the
    /// <c>ENTITY_OP_POSITION_CHANGED</c> op exists to let managed code ask for
    /// after writing the block itself.
    /// </para>
    ///
    /// <para>
    /// <b>The duplicate marks cost nothing.</b> That handler marks Origin and
    /// Angles too, so each is announced twice — and <c>CBaseEdict::StateChanged</c>
    /// walks its change-offset list and returns early on an offset already in it
    /// (<c>public/edict.h</c>). So the second announcement is
    /// two compares, and none of the edict's nineteen change offsets is spent on
    /// it.
    /// </para>
    ///
    /// <para>
    /// <b>The previous origin is read before the write</b>, because it is the one
    /// value the trigger half needs and the one value that cannot be recovered
    /// once the block has been written. A composition that passed the NEW origin
    /// as the previous one would fire no trigger edges at all, which is a
    /// decoration that can be carried through a trigger without ever touching it.
    /// </para>
    /// </summary>
    public void WriteComposed(EntityHandle child, Vector origin, QAngle angles,
                              Transform childToWorld)
    {
        Vector previous = _entities.GetOrigin(child);

        if (!HierarchyBlockWrite.ApplyTo(_entities, _networking, child, origin, angles, childToWorld))
            return;

        ComposeDepth++;

        try
        {
            _entities.PositionChanged(child, previous);
        }
        finally
        {
            ComposeDepth--;
        }
    }

    /// <summary>
    /// The name on THIS hierarchy. <see cref="IEngineEntities.FindByName"/> (<see cref="EngineSide.FindEntityByName"/>) walks the
    /// shim's own vector and answers nothing from the SDK's side, which is what
    /// makes it exactly this question and not the other one.
    /// </summary>
    public EntityHandle FindStandaloneByName(string name)
        => name.Length == 0 ? EntityHandle.None : _entities.FindByName(name);

    public EntityHandle FindBaseByName(string name)
        => _entities.FindBaseByName(name);

    /// <summary>
    /// What to call this entity in a message. Never null: an entity with no
    /// targetname is named by its classname, which is what every warning in the
    /// native file it replaces did.
    /// </summary>
    public string NameOf(EntityHandle handle)
    {
        string name = _entities.GetName(handle);

        return name.Length != 0 ? name : _entities.GetClassName(handle);
    }

    public void Remove(EntityHandle handle) => _entities.Remove(handle);

    /// <summary>
    /// A static object becomes a shadow, as stock's VPhysicsInitStatic makes
    /// one for an entity with a move parent: it follows the entity, which
    /// follows its parent, and still collides. Any other object is destroyed.
    /// </summary>
    public void DetachPhysics(EntityHandle child)
    {
        bool wasStatic = _physics.PhysicsKind(child) == StaticPhysicsKind;
        _entities.DestroyPhysics(child);

        if (wasStatic)
            _physics.InitShadowPhysics(child);
    }

    /// <summary><see cref="IEngineEntityPhysics.PhysicsKind"/> for a static object.</summary>
    private const int StaticPhysicsKind = 1;

    /// <summary>
    /// A refusal on the console, never a silent no-op (§7). <c>Warning</c> and not
    /// <c>Msg</c>, because every message that reaches here is one: a cycle
    /// refused, a parentname nothing is called, an attachment a model does not
    /// carry.
    /// </summary>
    public void Warn(string message) => _log.Warning(message);
}
