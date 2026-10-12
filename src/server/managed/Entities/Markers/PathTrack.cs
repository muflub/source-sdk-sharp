//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>How a follower should be facing as it goes through a node.</summary>
public enum TrackOrientation
{
    /// <summary>Leave the follower's facing alone.</summary>
    Fixed = 0,

    /// <summary>Face along the track. The default, and what a train looks like.</summary>
    FacePath = 1,

    /// <summary>Face the way the MAPPER pointed the node, whatever the track does.</summary>
    FacePathAngles = 2,
}

/// <summary>
/// One place on a track, the place after it, and the place before it.
///
/// The third and richest of the node types, and the differences from
/// <see cref="PathCorner"/> are all consequences of one: this chain is walked in
/// BOTH directions. So it holds a link each way, only one of which a map can
/// express — nothing says what comes before a node — and the walk takes a
/// direction of ±1 rather than a method per way, which is stock's own signature
/// and the thing that makes reversing a train one line.
///
/// Two more it has that a corner does not. A node can be switched OFF, which is
/// how a track change makes one of two branches the live one, and a node can
/// name an ALTERNATE next node, which is how a track forks — with a spawnflag
/// deciding whether the alternate replaces the next link or the previous one, so
/// the same fork serves a train going either way.
///
/// The chain is named rather than pointed at and there is no second pass to
/// resolve it in. Stock links from Activate, after the whole map has spawned;
/// managed entities spawn together in no order worth relying on, so each node
/// resolves the chain from a think armed at spawn — the idiom logic_auto
/// established — and the first node to get there links the rest. See
/// <see cref="LinkChain"/>.
/// </summary>
[ScriptedEntity(EntityNames.PathTrack)]
public class PathTrack : ScriptedEntity
{
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>Fired as a follower passes this node.</summary>
    public readonly Output OnPass = new();

    /// <summary>Fired when a follower jumps to this node instead of travelling to it.</summary>
    public readonly Output OnTeleport = new();

    /// <summary>What a map may set on this entity. See <see cref="PathTrackVars"/>.</summary>
    [ScriptVars] public PathTrackVars Vars = new();

    private PathTrack? _next;
    private PathTrack? _previous;
    private PathTrack? _alt;
    private bool _linked;

    //-------------------------------------------------------------------------
    // Two spawnflags that are STATE rather than settings, mirrored into fields.
    //
    // Stock keeps both in m_spawnflags and sets and clears the bits at runtime,
    // which it can do because the flags word is its own. A managed entity reads
    // the flags from the engine and cannot write them back, so the two that
    // change are held here and seeded from the map's value at spawn. That is the
    // honest version of the same thing — a bit that changes is not a spawn flag.
    //-------------------------------------------------------------------------
    private bool _disabled;
    private bool _altActive;

    /// <summary>
    /// path_track's own spawnflags, stock's SF_PATH_*.
    ///
    /// <para>
    /// Not path_corner's, which is the point. Both are nodes a mover follows and
    /// both have a teleport flag, and the two are numbered differently -- 0x0010
    /// here against path_corner's 0x002. The two types are what keep a path_corner
    /// bit from being read off a path_track's word.
    /// </para>
    ///
    /// <para>
    /// UpHill and DownHill are exclusive in practice but not in the bits, which is
    /// why HillType tests them in order rather than switching.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        Disabled = 0b0000_0000_0000_0001,
        AltReverse = 0b0000_0000_0000_0100,
        DisableTrain = 0b0000_0000_0000_1000,
        Teleport = 0b0000_0000_0001_0000,
        UpHill = 0b0000_0000_0010_0000,
        DownHill = 0b0000_0000_0100_0000,
        Alternate = 0b1000_0000_0000_0000,  // 0x8000
    }

    /// <summary>
    /// Where the node is, read live.
    ///
    /// Not captured at spawn, for <see cref="PathCorner"/>'s reason: a node is
    /// never the thing that moves, so there is nothing to remember it against,
    /// and stock reads GetLocalOrigin every time. It also means a node placed
    /// after it spawned is where it actually is, which is every node made from
    /// the console.
    /// </summary>
    public Vector Position => _fixedPosition ?? Origin;

    private Vector? _fixedPosition;

    /// <summary>
    /// Puts a node somewhere with no engine to have placed it.
    ///
    /// Within the assembly and for the tests only. LookAheadFrom and Nearest are
    /// the two pieces of real arithmetic in this class and both are entirely about
    /// where nodes ARE, so a test that cannot place one can check the links and
    /// nothing else.
    /// </summary>
    internal void PlaceAt(Vector position) => _fixedPosition = position;

    /// <summary>Zero means the node has nothing to say about speed.</summary>
    public float NodeSpeed => Vars.Speed;

    public float Radius => Vars.Radius;

    /// <summary>A follower reaching this node jumps to it rather than travelling.</summary>
    public bool Teleport => Flags.HasFlag(SpawnFlag.Teleport);

    /// <summary>Passing this node takes the train out of the player's hands for good.</summary>
    public bool DisablesTrain => Flags.HasFlag(SpawnFlag.DisableTrain);

    /// <summary>Whether the alternate link replaces the NEXT one or the PREVIOUS one.</summary>
    private bool AltReplacesPrevious => Flags.HasFlag(SpawnFlag.AltReverse);

    public bool IsDisabled => _disabled;

    public bool HasAlternatePath => _alt is not null;

    /// <summary>
    /// Which of the three hills stock recognises this node is on, as its own
    /// enum values: none, up, down. Read by nothing here — it exists for the
    /// payload cart, which is a native class — and kept so a map's flags mean
    /// what they say.
    /// </summary>
    public int HillType => Flags.HasFlag(SpawnFlag.UpHill) ? 1
                         : Flags.HasFlag(SpawnFlag.DownHill) ? 2
                         : 0;

    public TrackOrientation OrientationType => (TrackOrientation)Vars.OrientationType;

    public override void OnSpawn()
    {
        _disabled = Flags.HasFlag(SpawnFlag.Disabled);
        _altActive = Flags.HasFlag(SpawnFlag.Alternate);

        // The deferred link, and the only window there is. See the note on the
        // class: at spawn the node this one names may not exist yet.
        //
        // A tenth of a second rather than the fifth every mover uses, so that a
        // train looking for its first node finds a chain that is already joined
        // up. A train that gets there first is covered as well — it links the
        // chain itself before walking it — but only for the nodes it can reach
        // going forward, and a node's BACKWARD link is written by whoever names
        // it rather than found.
        SetNextThink(0.1f);
    }

    /// <summary>
    /// The deferred link. Whichever node gets here first joins up as much of the
    /// chain as exists; see <see cref="Link"/> for what happens to the rest.
    /// </summary>
    public override void OnThink() => LinkChain();

    //-------------------------------------------------------------------------
    // Walking the chain.
    //
    // A direction of ±1 rather than a method each way, matching
    // KeyframeTrack.NextKey and stock's own GetNextInDir — what a follower
    // carries is the number, and asking for "the next node in direction d" is
    // then the whole of turning round. Zero answers this node, which is stock's
    // answer and means "not moving".
    //-------------------------------------------------------------------------
    public PathTrack? NextKey(int direction) => direction switch
    {
        1 => GetNext(),
        -1 => GetPrevious(),
        _ => this,
    };

    /// <summary>
    /// The node after this one, taking the fork if it is live.
    ///
    /// The alternate stands in for the FORWARD link unless the node says it
    /// stands in for the backward one. That reads as an odd place to put the
    /// choice until you see what it is for: one fork serves a train going either
    /// way round a loop, and which link the branch replaces is which direction
    /// the branch is a detour from.
    /// </summary>
    public PathTrack? GetNext()
        => (_alt is not null && _altActive && !AltReplacesPrevious) ? _alt : _next;

    public PathTrack? GetPrevious()
        => (_alt is not null && _altActive && AltReplacesPrevious) ? _alt : _previous;

    /// <summary>
    /// The node, or nothing when there is none or it is switched off.
    ///
    /// Static and taking the node, because the whole point is to be safe on a
    /// null — every walk in stock is written as a chain of these rather than as
    /// null checks.
    /// </summary>
    public static PathTrack? ValidPath(PathTrack? node, bool testDisabled = true)
    {
        if (node is null)
            return null;

        return (testDisabled && node._disabled) ? null : node;
    }

    //-------------------------------------------------------------------------
    // Being switched on and off, and the fork.
    //-------------------------------------------------------------------------
    [ScriptedEvent("EnablePath")] private void OnEnablePath() => _disabled = false;
    [ScriptedEvent("DisablePath")] private void OnDisablePath() => _disabled = true;
    [ScriptedEvent("TogglePath")] private void OnTogglePath() => _disabled = !_disabled;

    /// <summary>Within the assembly so a track change can switch a branch without going by name.</summary>
    internal void SetDisabled(bool disabled) => _disabled = disabled;

    /// <summary>
    /// Nothing at all when there is no alternate path, which is stock's guard and
    /// worth keeping: without it a map could arm a fork that does not exist, and
    /// then GetNext would answer the ordinary link while the node claimed to be
    /// forked.
    /// </summary>
    [ScriptedEvent("EnableAlternatePath")]
    private void OnEnableAlternatePath()
    {
        if (_alt is not null)
            _altActive = true;
    }

    [ScriptedEvent("DisableAlternatePath")]
    private void OnDisableAlternatePath()
    {
        if (_alt is not null)
            _altActive = false;
    }

    [ScriptedEvent("ToggleAlternatePath")]
    private void OnToggleAlternatePath()
    {
        if (_alt is not null)
            _altActive = !_altActive;
    }

    /// <summary>
    /// Stock's internal input, not in the FGD: a follower announcing that it got
    /// here. Declared because a map that fires it by hand is entitled to.
    /// </summary>
    [ScriptedEvent("InPass")] private void OnInPass() => OnPass.Fire();

    [ScriptedEvent("InTeleport")] private void OnInTeleport() => OnTeleport.Fire();

    /// <summary>
    /// The same announcements from a follower that already holds this node.
    ///
    /// Directly rather than through the input, for PathCorner's reason: the
    /// follower has the node in hand and going back out through a name lookup
    /// would only be a way of getting it wrong.
    ///
    /// The follower names ITSELF as the activator, which is the whole of what
    /// stock does here and the reason func_trackautochange can tell a train from
    /// a button. Stock spells it as
    ///
    ///     pCurrent->AcceptInput( "InPass", this, this, emptyVariant, 0 );
    ///
    /// so the train is the activator of the input, and CPathTrack::InputPass
    /// passes that same activator straight into OnPass. Firing the output
    /// directly reaches the same place with one hop fewer, but only once the
    /// activator can be carried — before that, a node's OnPass arrived caused by
    /// the node, and the changer took its "somebody pressed a button" branch on
    /// every single node the train passed.
    /// </summary>
    internal void Pass(EntityHandle follower) => OnPass.Fire(follower);

    internal void Teleported(EntityHandle follower) => OnTeleport.Fire(follower);

    //-------------------------------------------------------------------------
    // Building the links.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Resolves the chain forward from this node, once.
    ///
    /// Walked from wherever the first caller happens to be rather than resolved
    /// per node, and an already-linked node ends the walk — which is what makes
    /// a circular track terminate, since the walk arrives back where it started,
    /// and means a second train sharing the track costs nothing. The same shape
    /// KeyframeTrack uses, and for the same reasons.
    /// </summary>
    internal void LinkChain()
    {
        // WHICH NODES THIS WALK HAS SEEN, and stock's own mechanism for it:
        // CPathTrack has BeginIteration/Visit/HasBeenVisited for exactly this.
        //
        // Per walk rather than a "done" flag on the node, because the walk must
        // be able to pass THROUGH a node it has already joined up on an earlier
        // walk. See Link for what makes that necessary. It was a process-wide
        // walk counter stamped onto each node; a set local to the walk is the
        // same answer with nothing shared.
        var walked = new HashSet<PathTrack>(ReferenceEqualityComparer.Instance);

        for (PathTrack? node = this; node is not null && walked.Add(node); node = node._next)
            node.Link();
    }

    /// <summary>
    /// Resolves whatever this node still names, and says nothing once there is
    /// nothing left to resolve.
    ///
    /// A name that found nothing does NOT count as resolved, and that is the
    /// difference between a track a map placed and one built a piece at a time.
    /// In a map every node exists before any of them links, so one pass joins the
    /// whole thing up. From the console they arrive seconds apart, so the first
    /// node's target does not exist yet when the first node looks — and a link
    /// failure that latched would leave the chain broken with nothing to retry
    /// it. So a failure is left open, and the next walk asks again.
    ///
    /// <para>
    /// The warning is printed once even so. A dead name stays dead, and reporting
    /// it on every walk would bury the console under a mapper's single typo.
    /// </para>
    /// </summary>
    private void Link()
    {
        if (_linked)
            return;

        // Stock's Activate gate. An unnamed node cannot be reached by name, so
        // nothing can point at it and stock leaves it out of the chain — which
        // does break a chain running through one, and is what a map with an
        // unnamed node in the middle already did. Latched, because a name is not
        // something that arrives later.
        if (Name.Length == 0)
        {
            _linked = true;
            return;
        }

        bool complete = true;

        if (_next is null && Vars.NextName.Length > 0)
        {
            var target = FindByName<PathTrack>(Vars.NextName);

            if (ReferenceEquals(target, this))
            {
                // Stock leaves the node in place rather than removing it, and its
                // comment says why: an already-linked neighbour would be left
                // pointing at nothing.
                WarnOnce($"[SourceSharp] path_track '{Name}' names itself as its next node\n");
            }
            else if (target is not null)
            {
                _next = target;
                target.SetPrevious(this);
            }
            else
            {
                complete = false;
                WarnOnce($"[SourceSharp] path_track '{Name}': dead end link '{Vars.NextName}'\n");
            }
        }

        if (_alt is null && Vars.AltName.Length > 0)
        {
            _alt = FindByName<PathTrack>(Vars.AltName);

            if (_alt is not null)
            {
                _alt.SetPrevious(this);
            }
            else
            {
                complete = false;
                WarnOnce($"[SourceSharp] path_track '{Name}': no alternate path '{Vars.AltName}'\n");
            }
        }

        _linked = complete;
    }

    private bool _warned;

    private void WarnOnce(string message)
    {
        if (_warned)
            return;

        _warned = true;
        Log.Warning(message);
    }

    /// <summary>
    /// The backward link, written by whoever points at this node, because a map
    /// only ever names forward.
    ///
    /// It declines a link from the node it names as its OWN alternate, which is
    /// stock's guard and is what stops a pair of nodes that fork into each other
    /// from overwriting each other's real predecessor.
    ///
    /// <para>
    /// The guard asks whether there IS an alternate first, which stock does not
    /// have to: it compares two names and both are empty for a node with no
    /// alternate — but the node offering the back-link is always named there,
    /// because only a named node links at all. Here the offer can come from a
    /// node with no name, and without this a track built without names would end
    /// up with no backward links whatsoever.
    /// </para>
    /// </summary>
    private void SetPrevious(PathTrack previous)
    {
        if (Vars.AltName.Length == 0
            || !string.Equals(previous.Name, Vars.AltName, StringComparison.OrdinalIgnoreCase))
        {
            _previous = previous;
        }
    }

    /// <summary>
    /// Joins this node to the next one, as resolving the name would have.
    ///
    /// Within the assembly only, so a track can be built with no map to have
    /// named one — which is how the tests reach the walking below.
    /// </summary>
    internal void LinkTo(PathTrack? next)
    {
        _linked = true;
        _next = next;

        next?.SetPrevious(this);
    }

    /// <summary>The fork, joined the same way, and it back-links as well.</summary>
    internal void LinkAlternate(PathTrack? alt)
    {
        _alt = alt;

        alt?.SetPrevious(this);
    }

    //-------------------------------------------------------------------------
    // The arithmetic a follower asks for.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Where a follower ends up after travelling <paramref name="distance"/>
    /// along the track from <paramref name="from"/>, and which node it is past.
    ///
    /// Stock's LookAhead, with its three out-parameters gathered into one value
    /// instead — the position is the answer as much as the node is, and a caller
    /// that has to pass a Vector in to get it back changed is a caller that can
    /// forget to.
    ///
    /// <para>
    /// A negative distance looks BACKWARD. <paramref name="respectDisabled"/> is
    /// stock's <c>move</c> argument under a name that says what it does: when a
    /// disabled node ends the walk, a follower that is only measuring wants the
    /// position projected past the end of the track rather than nothing, and one
    /// that is actually moving wants to be told it has run out.
    /// </para>
    /// </summary>
    public LookAhead LookAheadFrom(Vector from, float distance, bool respectDisabled)
    {
        PathTrack current = this;
        Vector origin = from;
        Vector position = from;

        float remaining = distance;
        int direction = 1;

        if (remaining < 0f)
        {
            remaining = -remaining;
            direction = -1;
        }

        while (remaining > 0f)
        {
            PathTrack? ahead = ValidPath(current.NextKey(direction), respectDisabled);

            // Out of track. A follower that is measuring gets the position it
            // WOULD have reached, extended along the last segment; one that is
            // moving gets nothing, which is how it learns it is at a dead end.
            if (ahead is null)
            {
                if (!respectDisabled)
                    origin = Project(current.NextKey(-direction), current, remaining);

                return new LookAhead(null, origin, null);
            }

            Vector toAhead = ahead.Position - position;
            float length = toAhead.Length;

            // Standing exactly on the next node with nothing beyond it. Answering
            // that node is right unless nothing was travelled at all, in which
            // case the follower is already in a dead end and there is no answer.
            if (length == 0f && ValidPath(ahead.NextKey(direction), respectDisabled) is null)
                return remaining == distance ? new LookAhead(null, origin, null)
                                             : new LookAhead(ahead, origin, null);

            // The next node is further than there is distance left, so the
            // follower stops short of it and is still past THIS one.
            if (length > remaining)
                return new LookAhead(current, position + toAhead * (remaining / length), ahead);

            remaining -= length;
            position = ahead.Position;
            origin = position;
            current = ahead;
        }

        // The distance ran out exactly on a node.
        return new LookAhead(current, origin, current.NextKey(direction));
    }

    /// <summary>
    /// A point <paramref name="distance"/> beyond <paramref name="end"/>, in the
    /// direction the segment was already going. What a follower measuring past
    /// the end of the track gets instead of nothing.
    /// </summary>
    private static Vector Project(PathTrack? start, PathTrack? end, float distance)
    {
        if (start is null || end is null)
            return end?.Position ?? Vector.Zero;

        return end.Position + (end.Position - start.Position).Normalized * distance;
    }

    /// <summary>
    /// The node on this track closest to a position, ignoring height.
    ///
    /// Height is dropped because the question is "which node is the thing above",
    /// and a train sitting on a track is a train's height above the node it is
    /// on. The walk is forward only and stops when it comes back to where it
    /// started, so a circular track terminates; the count is stock's own
    /// backstop against a chain that loops without passing through here.
    /// </summary>
    public PathTrack Nearest(Vector origin)
    {
        PathTrack nearest = this;
        float closest = Flatten(origin - Position).Length;

        int walked = 0;

        for (PathTrack? node = GetNext(); node is not null && !ReferenceEquals(node, this); node = node.GetNext())
        {
            if (++walked > 9999)
            {
                Log.Warning($"[SourceSharp] path_track '{Name}': bad sequence of nodes\n");
                return nearest;
            }

            float distance = Flatten(origin - node.Position).Length;

            if (distance < closest)
            {
                closest = distance;
                nearest = node;
            }
        }

        return nearest;
    }

    private static Vector Flatten(Vector v) => new(v.X, v.Y, 0f);

    /// <summary>
    /// How a follower going this way should be facing here.
    ///
    /// Along the track, from the node behind to the node ahead — except that a
    /// node marked FacePathAngles hands back the facing the MAPPER gave it, which
    /// is how a track through a corner can be made to point somewhere the
    /// geometry does not.
    ///
    /// At the end of the track there is no node ahead, so the segment BEHIND is
    /// used instead: a train arriving at the last node keeps the facing it came
    /// in with rather than snapping to nothing.
    /// </summary>
    public Vector GetOrientation(bool forward)
    {
        if (OrientationType == TrackOrientation.FacePathAngles)
            return Entities.GetAngles(Handle);

        PathTrack previous = this;
        PathTrack? next = NextKey(forward ? 1 : -1);

        if (next is null)
        {
            previous = NextKey(forward ? -1 : 1) ?? this;
            next = this;
        }

        return Angles.FromVector(next.Position - previous.Position);
    }
}

/// <summary>
/// What looking along a track found: the node the follower is past, where it
/// ended up, and the node after that.
///
/// The third is what a train needs to decide how to be facing before it gets
/// there, and it is separate from <see cref="Node"/> rather than derived from it
/// because the walk already knows and re-deriving it would take the fork
/// decision a second time.
/// </summary>
public readonly record struct LookAhead(PathTrack? Node, Vector Origin, PathTrack? NextNext);

/// <summary>
/// The map keys <see cref="PathTrack"/> declares.
/// </summary>
[ScriptVars]
public partial class PathTrackVars
{
    [VarName("target")]
    public string NextName { get; set; } = string.Empty;

    [VarName("altpath")]
    public string AltName { get; set; } = string.Empty;

    /// <summary>
    /// Read back so a map's value survives, and used by nothing here: it is
    /// consulted only by the NPC track-following behaviour, which is a native
    /// class this hierarchy has no equivalent of.
    /// </summary>
    public float Radius { get; set; }

    public int OrientationType { get; set; } = (int)TrackOrientation.FacePath;

    /// <summary>
    /// How fast the follower should run once it is past here, or zero for
    /// nothing to say. A CBaseEntity key rather than one path_track declares,
    /// which is why stock reads it off the node without the node mentioning it.
    /// </summary>
    public float Speed { get; set; }
}
