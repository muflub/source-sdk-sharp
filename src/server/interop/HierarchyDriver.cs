using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// PARENTING'S FRONT DOOR, and the two words in the engine block that keep the
/// game from having to knock.
///
/// <para>
/// <see cref="Hierarchy"/> is the model — the registry, the composition, the
/// cycle refusal, the deferred <c>parentname</c>, the frame rule. Step 2h wrote
/// it and its 52 facts and then stopped, because every entry point into parenting
/// is called from native and making managed the authority meant crossings. This
/// class is those entry points, and the reason they are affordable.
/// </para>
///
/// <para>
/// <b>THE COST THAT HAD TO BE SETTLED FIRST.</b> 2h's ABI request named it:
/// <i>"slot 1 fires on every position write of an entity that HAS children …
/// a crossing cannot be conditional on managed state that native cannot see …
/// This is the one number that must be settled before the slots are wired."</i>
/// It is settled here, and not with a native flag. The engine block already
/// carried <see cref="EngineBlockHead.ParentHandle"/> and now carries
/// <see cref="EngineBlockHead.ChildCount"/> beside it, and this class is what
/// writes them:
/// </para>
///
/// <list type="table">
/// <item><term><c>ChildCount != 0</c></term><description>something hangs off this
/// entity. Read at the tail of every position write, which is the hottest thing
/// on the hierarchy, and false for every entity in a map that parents
/// nothing.</description></item>
/// <item><term><c>ParentHandle</c> valid</term><description>something carries this
/// entity. Read by the public setters, the two frame conversions and the
/// removal — and it is the WHOLE answer to "is this entity parented", which 2h
/// asked for a slot for and which now costs no crossing at
/// all.</description></item>
/// </list>
///
/// <para>
/// That is §3.2 applied to parenting rather than to a getter: <i>they are
/// answered from memory that managed code writes</i>. Native decides whether to
/// cross with one load at a constant offset instead of crossing to ask managed
/// whether it should cross.
/// </para>
///
/// <para>
/// <b>The two words are re-synced from the registry, not maintained by hand.</b>
/// <see cref="Hierarchy"/> raises no events and this class deliberately did not
/// give it any: a model that had to remember to announce every place it drops a
/// link is a model with a new way to be wrong, and the registry is small enough
/// that recomputing the desired words from it is cheaper than the bookkeeping
/// would be. <see cref="Sync"/> runs after every call that can change the
/// registry, walks the links, and writes ONLY the words that actually differ from
/// what it last wrote — so a frame in which nothing links or unlinks does no
/// blob writes at all.
/// </para>
///
/// <para>
/// <b>What is NOT guarded, and why it is safe.</b> An entity removed while it
/// still holds an UNRESOLVED <c>parentname</c> — the window between the map's
/// parse and <see cref="ResolveParentNames"/> — has neither word set, so native
/// does not tell managed it has gone and the entry lingers. It cannot do harm:
/// the registry is keyed by <see cref="EntityHandle"/>, which is index AND
/// serial, so the slot's next occupant is a different key and can never be
/// matched by it; <see cref="ResolveParentNames"/> drops the entry at the end of
/// the same map load after checking the child is still there; and
/// <see cref="LevelShutdown"/> empties whatever is left. Guarding that window
/// would mean a third word for a state that lasts one map load.
/// </para>
/// </summary>
public sealed class HierarchyDriver
{
    private readonly Hierarchy _hierarchy;
    private readonly IComposedWorld _world;

    /// <summary>
    /// THE WORDS THIS CLASS HAS WRITTEN, so it can tell a change from a repeat.
    ///
    /// <para>
    /// Keyed by the entity whose block holds them. An entity leaves this map when
    /// both of its words go back to their empty values, and leaving is what makes
    /// the entry get CLEARED rather than merely stop being refreshed — a parent
    /// whose last child went and whose <c>ChildCount</c> stayed at one would pay a
    /// crossing on every position write for the rest of the map.
    /// </para>
    /// </summary>
    private readonly Dictionary<EntityHandle, GuardWords> _written = new();

    private readonly Dictionary<EntityHandle, GuardWords> _wanted = new();
    private readonly List<EntityHandle> _stale = new();

    public HierarchyDriver(IComposedWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _hierarchy = new Hierarchy(world);
    }

    /// <summary>The model, for diagnostics and for tests. Nothing else reads it.</summary>
    public Hierarchy Model => _hierarchy;

    /// <summary>How many entities currently carry a non-empty guard word.</summary>
    public int GuardedEntityCount => _written.Count;

    /// <summary>
    /// How many times the guard words have been written into a block since the
    /// level began — including the writes that CLEAR them.
    ///
    /// <para>
    /// <b>The number the "writes only differences" claim is made of.</b> A sync
    /// that rewrote every link's words every time it ran would be correct and
    /// would cost a blob fetch per link per position write, which is exactly the
    /// crossing the guard exists to avoid — and nothing about the game's
    /// behaviour would show it. So it is counted, and a fact holds it still
    /// across a sync that changes nothing.
    /// </para>
    /// </summary>
    public int GuardWritesMade { get; private set; }

    /// <summary>
    /// The guard words of one entity, as the driver last wrote them.
    /// </summary>
    public readonly record struct GuardWords(uint ParentHandle, int ChildCount)
    {
        /// <summary>What an entity that parenting knows nothing about carries.</summary>
        public static readonly GuardWords Empty = new(EntityHandle.None.Value, 0);

        public bool IsEmpty => ParentHandle == EntityHandle.None.Value && ChildCount == 0;
    }

    /// <summary>
    /// The words this entity's block should be carrying.
    ///
    /// <para>
    /// USUALLY THE SAME AS WHAT IT IS CARRYING, and the exception is a parent on
    /// the SDK's hierarchy: it has no block of ours, so its count is recorded
    /// here and written nowhere. That is not a hole — an SDK parent's moves are
    /// not observable from this side at all, which is why they are SWEPT rather
    /// than composed, so nothing native ever reads a word for it.
    /// </para>
    /// </summary>
    public GuardWords GuardsOf(EntityHandle entity)
        => _written.TryGetValue(entity, out GuardWords words) ? words : GuardWords.Empty;

    //-------------------------------------------------------------------------
    // The entry points. One per ABI slot, in the order sourcesharp.h declares
    // them.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A parent moved. From the tail of <c>CStandaloneEntity::OnPositionChanged</c>.
    ///
    /// <para>
    /// <b>The re-entrancy guard, and it is the whole reason this is not a
    /// one-liner.</b> Placing a child goes back through the native position funnel
    /// so the partition and the triggers are updated, and that funnel ends by
    /// calling this method again for the child's own children — while
    /// <see cref="Hierarchy.ComposeOne"/> is already recursing into exactly those
    /// links. Composing them twice from two frames is not merely wasteful: the
    /// second pass runs after the first has moved the child, so a grandchild is
    /// composed against a frame that has already been consumed.
    /// </para>
    ///
    /// <para>
    /// So the managed recursion wins and the re-entry returns. Native does not
    /// need to know, which is the point — the funnel stays one piece of code with
    /// one caller-visible meaning.
    /// </para>
    /// </summary>
    public int ParentMoved(EntityHandle parent)
    {
        if (_world.IsComposing)
            return 0;

        int composed = _hierarchy.ParentMoved(parent);
        Sync();
        return composed;
    }

    /// <summary>A child moved itself, so its offset is re-measured.</summary>
    public void ChildMoved(EntityHandle child)
    {
        if (_world.IsComposing)
            return;

        _hierarchy.ChildMoved(child);
        Sync();
    }

    /// <summary>
    /// The entity API's own SetParent. Answers whether the link was made; a
    /// refusal is PRINTED and names both ends (§7), never a silent no.
    /// </summary>
    public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment)
    {
        ParentResult result = _hierarchy.SetParent(child, parent, parentAttachment);

        if (!result.Granted)
            _world.Warn(result.Message);

        Sync();
        return result.Granted;
    }

    /// <summary>Which frame on the parent, one-based; 0 for its own transform.</summary>
    public int GetParentAttachment(EntityHandle child)
        => _hierarchy.LinkFor(child)?.ParentAttachment ?? 0;

    /// <summary>The map's <c>parentname</c>, recorded unresolved.</summary>
    public void SetParentName(EntityHandle child, string? parentName)
    {
        _hierarchy.SetParentName(child, parentName);
        Sync();
    }

    /// <summary>The map's <c>parentattachment</c>, held until there is a link.</summary>
    public void SetParentAttachmentName(EntityHandle child, string? attachmentName)
        => _hierarchy.SetParentAttachmentName(child, attachmentName);

    /// <summary>
    /// An entity is going. Answers how many children it took with it.
    /// </summary>
    public int EntityRemoved(EntityHandle entity)
    {
        int detached = _hierarchy.EntityRemoved(entity);

        // The entity itself is going, so its words go with it rather than being
        // left for the slot's next occupant to be measured against.
        _written.Remove(entity);

        Sync();
        return detached;
    }

    /// <summary>The map's names turned into links. Answers how many it made.</summary>
    public int ResolveParentNames()
    {
        int made = _hierarchy.ResolveParentNames();
        Sync();
        return made;
    }

    /// <summary>World to the parent's frame, in place.</summary>
    public bool ToLocal(EntityHandle child, ref Vector origin, ref QAngle angles)
        => _hierarchy.WorldToLocal(child, ref origin, ref angles);

    /// <summary>The parent's frame to the world, in place.</summary>
    public bool ToWorld(EntityHandle child, ref Vector origin, ref QAngle angles)
        => _hierarchy.LocalToWorld(child, ref origin, ref angles);

    /// <summary>A new offset, recorded and applied.</summary>
    public void SetLocal(EntityHandle child, Vector origin, QAngle angles)
    {
        _hierarchy.SetLocal(child, origin, angles);
        Sync();
    }

    //-------------------------------------------------------------------------
    // The two the frame drives, and neither is an ABI slot.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE ONCE-A-FRAME SWEEP, FOR NO CROSSING.
    ///
    /// <para>
    /// 2h's request listed this as a slot and then said it might not need to be
    /// one: the server frame is already driven from C#, so the sweep can simply
    /// run there. It does. <c>SourceSharp.Server.StandaloneHierarchy.OnThink</c>
    /// calls it between the directory's think pass and
    /// <c>IEngineFrame.StandaloneFinish</c> — which is exactly where the native
    /// sweep ran, at the top of <c>StandaloneEntities_Finish</c>, and exactly
    /// where step 1g's split says the attachment compose belongs.
    /// </para>
    ///
    /// <para>
    /// It is inside a crossing that already happens, so a slot the native side
    /// calls would have been a second one for the same work.
    /// </para>
    /// </summary>
    public int RunFrame()
    {
        int composed = _hierarchy.RunFrame();
        Sync();
        return composed;
    }

    /// <summary>
    /// The level is going, from the pre-entity teardown — before the entities are
    /// deleted, so no removal finds a child to orphan and nothing is queued for a
    /// deferred delete that nothing will drain.
    ///
    /// <para>
    /// The blocks are NOT written back to their empty values here and that is
    /// deliberate: every entity that carries a word is about to be destroyed, so
    /// writing to it would be a blob fetch per entity to change memory nobody will
    /// read again. The map of what was written is dropped instead, which is what
    /// makes the next level start from nothing.
    /// </para>
    /// </summary>
    public void LevelShutdown()
    {
        _hierarchy.LevelShutdown();
        _written.Clear();
        GuardWritesMade = 0;
        _wanted.Clear();
        _stale.Clear();
    }

    //-------------------------------------------------------------------------
    // The guard words.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE REGISTRY, PROJECTED INTO THE BLOCKS — and only where it differs.
    ///
    /// <para>
    /// Two passes and no allocation after the first few links: the desired words
    /// are built from <see cref="Hierarchy.Links"/>, then every entity that has a
    /// word today and does not want one is cleared. Writing only differences is
    /// what keeps a frame that composes without linking free of blob fetches
    /// entirely.
    /// </para>
    ///
    /// <para>
    /// <b>Only RESOLVED links count.</b> An unresolved <c>parentname</c> is a name
    /// and nothing else — no parent handle to write, and no child to count on a
    /// parent that has not been found yet. Writing a parent handle for one would
    /// make <c>IsParented</c> answer yes for an entity nothing is carrying, which
    /// is the answer that has a flight or a solver step move it out of hands that
    /// are not holding it.
    /// </para>
    /// </summary>
    private void Sync()
    {
        _wanted.Clear();

        foreach (ParentLink link in _hierarchy.Links)
        {
            if (!link.Resolved)
                continue;

            // The child end: who carries it.
            _wanted[link.Child] = Wanted(link.Child) with { ParentHandle = link.Parent.Value };

            // The parent end: how many it carries. A parent on the SDK's
            // hierarchy has no block of ours to write, and needs none — its moves
            // are not observable from this side at all, which is why they are
            // swept rather than composed. The write below simply finds no blob.
            GuardWords parent = Wanted(link.Parent);

            _wanted[link.Parent] = parent with { ChildCount = parent.ChildCount + 1 };
        }

        foreach (var (entity, words) in _wanted)
        {
            if (!_written.TryGetValue(entity, out GuardWords had) || had != words)
                WriteGuards(entity, words);
        }

        // Everything that had words and wants none. Collected first, because the
        // write below changes the map this walks.
        _stale.Clear();

        foreach (var (entity, _) in _written)
        {
            if (!_wanted.ContainsKey(entity))
                _stale.Add(entity);
        }

        foreach (EntityHandle entity in _stale)
            WriteGuards(entity, GuardWords.Empty);

        _stale.Clear();
    }

    /// <summary>
    /// WHAT AN ENTITY WANTS SO FAR, AND <see cref="GuardWords.Empty"/> RATHER
    /// THAN <c>default</c> WHEN IT WANTS NOTHING YET.
    ///
    /// <para>
    /// <b>The two are not the same, and the difference is a live wrong answer.</b>
    /// <c>default(GuardWords)</c> has a <c>ParentHandle</c> of ZERO, and zero is a
    /// REAL handle naming a real entity — the same trap
    /// <c>SourceSharp_EntitySetParent</c> spells out on the other side of the
    /// boundary. So a parent that carries children and is carried by nothing would
    /// have zero written into its block, native would read the word as valid, and
    /// <c>IsParented</c> would answer yes about whatever entity holds slot zero.
    /// The invalid handle is <see cref="EntityHandle.None"/>, and it is what
    /// "nothing carries me" is spelled with everywhere else.
    /// </para>
    /// </summary>
    private GuardWords Wanted(EntityHandle entity)
        => _wanted.TryGetValue(entity, out GuardWords words) ? words : GuardWords.Empty;

    /// <summary>
    /// Two words into one entity's block, and the bookkeeping that says so.
    ///
    /// <para>
    /// <b>NOT MARKED, and that is the same argument
    /// <see cref="HierarchyBlockWrite"/> makes about the world transform.</b>
    /// These two words exist so the SERVER can decide whether to cross; nothing on
    /// the client decodes either of them (<c>C_StandaloneEntity</c> sets
    /// <c>ParentHandle</c> to invalid at construction and never reads it again).
    /// Announcing them would spend the edict's change offsets — of which there are
    /// nineteen — on values no receiver looks at.
    /// </para>
    ///
    /// <para>
    /// A blob too short, or an entity that has gone, writes NOTHING and records
    /// nothing. That is not a failure to report: an entity being torn down is the
    /// ordinary way to reach this, and there is no block left to hold a word.
    /// </para>
    /// </summary>
    private void WriteGuards(EntityHandle entity, GuardWords words)
    {
        // ONLY AN ENTITY OF THIS HIERARCHY HAS ONE OF THESE BLOCKS, and asking
        // is not belt-and-braces.
        //
        // A CBaseEntity answers no blob at all, so it would fall out below on
        // its own -- but WORLDSPAWN DOES ANSWER ONE, and it is not an engine
        // block: it is the world's own managed netstate declaration, whose bytes
        // at 124 and 148 mean something else entirely
        // (shell_entityapi.cpp: "THE WORLD CARRIES A BLOB, AND IT IS ON NEITHER
        // OF THE TWO HIERARCHIES ABOVE"). Nothing can reach here holding the
        // world today -- the probe answers no bits for it, so
        // <see cref="Hierarchy.SetParent"/> refuses it as a parent -- and that
        // is exactly the kind of "cannot happen" this tree has been wrong about
        // before. A length check is not enough to catch it, because the world's
        // blob is long.
        // THE BLOCK, NOT THE BLOB. IEngineEntities.GetEngineBlock says why they
        // are two calls; the short version is that the payload's length is the
        // entity's tier and both of these words sit past it.
        Span<byte> blob = _world.IsStandalone(entity)
            ? _world.BlockOf(entity)
            : Span<byte>.Empty;

        if (blob.Length >= EngineBlock.SizeBytes)
        {
            uint parent = words.ParentHandle;
            int children = words.ChildCount;

            MemoryMarshal.Write(
                blob.Slice(HierarchyBlockWrite.OffsetOf(nameof(EngineBlockHead.ParentHandle))),
                in parent);

            MemoryMarshal.Write(
                blob.Slice(HierarchyBlockWrite.OffsetOf(nameof(EngineBlockHead.ChildCount))),
                in children);
        }

        GuardWritesMade++;

        if (words.IsEmpty)
            _written.Remove(entity);
        else
            _written[entity] = words;
    }
}

/// <summary>
/// HOW <c>Entry.cs</c> REACHES PARENTING, and why it is an interface of its own.
///
/// <para>
/// The eleven parenting thunks have to find the driver the server module built,
/// and <c>IServerModule</c> is a shared type several lanes append to. So this
/// follows the pattern <c>ISideModule</c> already set: a small interface declared
/// beside the thing it exposes, and a cast at the thunk —
/// <c>(s_ServerGame as IHierarchyHost)?.Hierarchy</c>. A module that does not
/// implement it answers null, which reads as "nothing is parented to anything"
/// and is the honest state before a side has loaded.
/// </para>
/// </summary>
public interface IHierarchyHost
{
    /// <summary>
    /// The level's parenting, or null before there is a level. Null is a real
    /// state: the thunks are reachable from a map parse that begins before the
    /// module has finished starting.
    /// </summary>
    HierarchyDriver? Hierarchy { get; }
}
