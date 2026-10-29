//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>CPhysicsPushedEntities</c> (physics_main.cpp): what a <c>MOVETYPE_PUSH</c>
/// mover does to whatever is in its way — finds it, pushes it along, and gives
/// up and names it when the pushed position is not valid.
///
/// <para>
/// <b>STOCK'S RULES, IN STOCK'S ORDER.</b> <c>CPushBlockerEnum</c> decides who
/// can be pushed: solid, not <c>MOVETYPE_PUSH</c>/<c>NONE</c>/<c>VPHYSICS</c>/
/// <c>NOCLIP</c>, in a collision group the pusher collides with, not in the
/// pusher's own hierarchy, and either standing on the pusher or intersecting
/// it where it is going. The root move parent of what was found is what is
/// pushed. <c>SpeculativelyCheckPush</c> sweeps each one by the push and puts it
/// where the sweep ended; a linear push that went the whole way is done, and
/// anything else is accepted only if the place it ended is not inside anything.
/// The first one that is not makes the whole push fail: everything goes back
/// where it was and the pusher's <c>Blocked</c> is told who.
/// </para>
///
/// <para>
/// <b>A PHYSICS OBJECT IS NEVER PUSHED HERE</b> — <c>MOVETYPE_VPHYSICS</c> is on
/// the enumerator's refusal list. A prop in a door's way is the solver's
/// business, through the door's shadow.
/// </para>
///
/// <para>
/// <b>TWO HALVES, AROUND THE PUSHER'S OWN COMMIT, and that is the divergence
/// the ABI forces.</b> Stock moves the pusher first and then hides it from the
/// partition for each blocker's sweep (<c>UnlinkPusherList</c>). The ABI's hull
/// sweep skips only the swept entity, so a sweep made after the commit would
/// start inside the pusher. So <see cref="BeginLinear"/>/<see cref="BeginRotate"/>
/// enumerate against where the pusher is GOING (its collision box, put through
/// the final transform) and sweep while it is still where it WAS — where a
/// blocker being pushed away from it cannot meet it — and <see cref="Finish"/>
/// runs after the caller has committed the pusher, so the validity test sees it
/// where stock's does. The one blocker that a sweep before the commit DOES meet
/// is a rider on a pusher moving down, out from under them: stock's sweep would
/// not see the pusher, so that rider's sweep is finished after the commit, from
/// where it stopped, against the pusher's new place. The validity test still
/// sees moveable physics objects, which <c>CTraceFilterPushFinal</c> filters out.
/// </para>
///
/// <para>
/// <b>Not ported</b>: the pusher's move children (the pusher list is the mover
/// itself; a child is excluded from being pushed by the hierarchy rule, as
/// stock's is), <c>PhysicsImpact</c> between a pushed entity and what its sweep
/// struck (the ABI's sweep names nothing), and the player half of
/// <c>FinishRotPushedEntity</c> — the relative view turn
/// (<c>pl.fixangle = FIXANGLE_RELATIVE</c>) has no ABI.
/// </para>
///
/// <para>
/// One instance, one push at a time: the halves keep the list between them, as
/// stock's single <c>g_pPushedEntities</c> does.
/// </para>
/// </summary>
public sealed class PhysicsPushedEntities
{
    /// <summary>How many candidates one enumeration can hold.</summary>
    public const int MaxCandidates = 64;

    /// <summary>
    /// How deep two boxes must overlap to be intersecting rather than touching.
    /// A hull resting on a face sits on it, and stock's own sweep does not call
    /// that solid.
    /// </summary>
    public const float TouchTolerance = 0.001f;

    private readonly IPushWorld _world;
    private readonly ILog _log;

    public PhysicsPushedEntities(IPushWorld world, ILog log)
    {
        _world = world;
        _log = log;
    }

    /// <summary><c>PhysicsPushedInfo_t</c>, plus whether the begin half moved it.</summary>
    private struct Pushed
    {
        public EntityHandle Entity;
        public Vector StartOrigin;
        public Vector Push;
        public float Fraction;
        public bool PusherIsGround;
        public bool Unblockable;
    }

    private readonly List<Pushed> _moved = new();

    private EntityHandle _pusher = EntityHandle.None;
    private int _pusherIndex;
    private bool _rotational;
    private bool _unblockableByPlayer;
    private SolidType _rootSolid;
    private Transform _startLocalToWorld;
    private Transform _endLocalToWorld;
    private QAngle _amove;

    /// <summary>
    /// Say what every push found and decided — the candidates the walk
    /// returned, why each was or was not pushable, each sweep and each fit
    /// test. <c>cs_sv_movers_pushed</c>; off, it costs nothing.
    /// </summary>
    public bool Trace { get; set; }

    private void Say(string message)
    {
        if (Trace)
            _log.Msg($"[cs_sv_movers_pushed] #{_pusher.Value}: {message}\n");
    }

    /// <summary>How many entities the current push is moving. <c>CountMovedEntities</c>.</summary>
    public int Count => _moved.Count;

    /// <summary>The <paramref name="index"/>th entity the current push is moving.</summary>
    public EntityHandle PushedAt(int index) => _moved[index].Entity;

    /// <summary>Where the <paramref name="index"/>th entity the current push is moving started.</summary>
    public Vector PushedStartAt(int index) => _moved[index].StartOrigin;

    //-------------------------------------------------------------------------
    // The begin half: before the pusher is committed.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>PerformLinearPush</c> up to the commit: enumerate what the pusher's
    /// swept box meets (<c>GenerateBlockingEntityListAddBox</c>) and sweep each
    /// by the push. Answers how many entities it is pushing.
    /// </summary>
    public int BeginLinear(EntityHandle pusher, Hierarchy? parenting, Vector fromOrigin,
                           Vector toOrigin, QAngle angles, bool unblockableByPlayer)
    {
        Start(pusher, unblockableByPlayer, rotational: false,
              Transform.FromOriginAngles(fromOrigin, angles),
              Transform.FromOriginAngles(toOrigin, angles), QAngle.Zero);

        Vector push = toOrigin - fromOrigin;
        GenerateBlockingEntityList(parenting, push);

        // SpeculativelyCheckLinearPush's sweeps, in its order.
        for (int i = _moved.Count - 1; i >= 0; i--)
            SweepBlocker(i, push);

        return _moved.Count;
    }

    /// <summary>
    /// <c>PerformRotatePush</c> up to the commit: enumerate what the pusher meets
    /// at its final orientation (<c>GenerateBlockingEntityList</c>) and sweep
    /// each by <c>ComputeRotationalPushDirection</c>.
    /// </summary>
    public int BeginRotate(EntityHandle pusher, Hierarchy? parenting, Vector origin,
                           QAngle fromAngles, QAngle toAngles, bool unblockableByPlayer)
    {
        Start(pusher, unblockableByPlayer, rotational: true,
              Transform.FromOriginAngles(origin, fromAngles),
              Transform.FromOriginAngles(origin, toAngles),
              new QAngle(toAngles.Pitch - fromAngles.Pitch, toAngles.Yaw - fromAngles.Yaw,
                         toAngles.Roll - fromAngles.Roll));

        GenerateBlockingEntityList(parenting, Vector.Zero);

        // SpeculativelyCheckRotPush. vecAbsPush is ONE variable across the loop
        // and ComputeRotationalPushDirection reads it before it writes it — the
        // SOLID_VPHYSICS corner guess uses the previous blocker's move. Stock's
        // first read is uninitialised; this starts it at zero.
        Vector push = Vector.Zero;

        for (int i = _moved.Count - 1; i >= 0; i--)
        {
            push = ComputeRotationalPushDirection(_moved[i].Entity, push);
            SweepBlocker(i, push);
        }

        return _moved.Count;
    }

    private void Start(EntityHandle pusher, bool unblockableByPlayer, bool rotational,
                       Transform start, Transform end, QAngle amove)
    {
        _moved.Clear();
        _pusher = pusher;
        _pusherIndex = _world.Index(pusher);
        _unblockableByPlayer = unblockableByPlayer;
        _rotational = rotational;
        _rootSolid = _world.SolidType(pusher);
        _startLocalToWorld = start;
        _endLocalToWorld = end;
        _amove = amove;
    }

    /// <summary>
    /// <c>GenerateBlockingEntityList</c> and its <c>AddBox</c> twin, and
    /// <c>CPushBlockerEnum</c>. A zero <paramref name="moved"/> is the rotating
    /// form, which does not stretch the box.
    /// </summary>
    private void GenerateBlockingEntityList(Hierarchy? parenting, Vector moved)
    {
        // Don't bother if the pusher isn't solid.
        if ((_world.State(_pusher, out _) & EntityState.Solid) == 0
            || (_world.SolidFlags(_pusher) & SolidFlags.VolumeContents) != 0)
        {
            Say("not solid; pushes nothing");
            return;
        }

        if (!FinalBox(out Vector obbMins, out Vector obbMaxs, out Vector absMins, out Vector absMaxs))
        {
            Say($"no collision box ({obbMins} .. {obbMaxs})");
            return;
        }

        // Back along the move, so the box covers where it came from as well.
        absMins = new Vector(moved.X >= 0f ? absMins.X - moved.X : absMins.X,
                             moved.Y >= 0f ? absMins.Y - moved.Y : absMins.Y,
                             moved.Z >= 0f ? absMins.Z - moved.Z : absMins.Z);
        absMaxs = new Vector(moved.X >= 0f ? absMaxs.X : absMaxs.X - moved.X,
                             moved.Y >= 0f ? absMaxs.Y : absMaxs.Y - moved.Y,
                             moved.Z >= 0f ? absMaxs.Z : absMaxs.Z - moved.Z);

        Span<EntityHandle> found = stackalloc EntityHandle[MaxCandidates];
        int count = _world.EntitiesInBox(absMins, absMaxs, found);

        Say($"box {obbMins} .. {obbMaxs} going to {_endLocalToWorld.Translation}; " +
            $"swept {absMins} .. {absMaxs}; {count} in it");

        if (count == found.Length)
            _log.Warning($"[SourceSharp] pusher #{_pusher.Value}: more than {found.Length} " +
                         "entities in its way; some were not considered\n");

        EntityHandle pusherRoot = RootMoveParent(parenting, _pusher);
        EngineCollisionGroup pusherGroup = (EngineCollisionGroup)_world.CollisionGroup(_pusher);

        for (int i = 0; i < count; i++)
        {
            EntityHandle pushable = GetPushableEntity(parenting, found[i], pusherRoot, pusherGroup,
                                                      obbMins, obbMaxs);

            if (pushable == EntityHandle.None || Contains(pushable))
                continue;

            _moved.Add(new Pushed
            {
                Entity = pushable,
                StartOrigin = _world.Origin(pushable),
                Fraction = 1f,
            });
        }
    }

    /// <summary>
    /// The pusher's collision box at its final transform, and the world AABB
    /// around it. When the box cannot be read, the current world AABB is taken
    /// as the box in the pusher's current frame.
    /// </summary>
    private bool FinalBox(out Vector obbMins, out Vector obbMaxs, out Vector absMins, out Vector absMaxs)
    {
        if (!_world.CollisionBox(_pusher, out obbMins, out obbMaxs))
        {
            var (mins, maxs) = _world.Bounds(_pusher);
            Vector origin = _world.Origin(_pusher);
            Transform toLocal = _startLocalToWorld.InverseTR();

            AabbOf(toLocal, origin + mins, origin + maxs, out obbMins, out obbMaxs);
        }

        AabbOf(_endLocalToWorld, obbMins, obbMaxs, out absMins, out absMaxs);
        return obbMaxs.X >= obbMins.X && obbMaxs.Y >= obbMins.Y && obbMaxs.Z >= obbMins.Z;
    }

    /// <summary><c>CPushBlockerEnum::GetPushableEntity</c>.</summary>
    private EntityHandle GetPushableEntity(Hierarchy? parenting, EntityHandle check, EntityHandle pusherRoot,
                                           EngineCollisionGroup pusherGroup, Vector obbMins, Vector obbMaxs)
    {
        if (check == EntityHandle.None)
            return EntityHandle.None;

        EntityState state = _world.State(check, out MoveType moveType);

        if ((state & EntityState.Solid) == 0)
            return Refuse(check, $"not solid ({state})");

        if (moveType is MoveType.Push or MoveType.None or MoveType.VPhysics or MoveType.NoClip)
            return Refuse(check, $"movetype {moveType}");

        int checkGroup = _world.CollisionGroup(check);
        if (!CollisionRules.ShouldCollide((EngineCollisionGroup)checkGroup, pusherGroup))
            return Refuse(check, $"group {checkGroup} does not collide with {(int)pusherGroup}");

        // We're not pushing stuff we're hierarchically attached to.
        EntityHandle checkRoot = RootMoveParent(parenting, check);
        if (checkRoot == pusherRoot)
            return Refuse(check, "in the pusher's hierarchy");

        // Standing on the pusher, or it is going to be inside it.
        if (!IsStandingOnPusher(check, state))
        {
            Vector origin = _world.Origin(check);
            var (mins, maxs) = _world.Bounds(check);

            if (!ObbIntersectsAabb(obbMins, obbMaxs, _endLocalToWorld, origin + mins, origin + maxs))
                return Refuse(check, $"neither on it nor inside it (box {origin + mins} .. {origin + maxs})");
        }

        Say($"#{check.Value} {moveType} is pushed (as #{checkRoot.Value})");

        // If a rigidly attached child comes into contact with a pusher, the
        // highest parent of that child is what is pushed.
        return checkRoot;
    }

    private EntityHandle Refuse(EntityHandle check, string why)
    {
        Say($"#{check.Value} not pushed: {why}");
        return EntityHandle.None;
    }

    /// <summary><c>CPushBlockerEnum::IsStandingOnPusher</c>, for the one pusher.</summary>
    private bool IsStandingOnPusher(EntityHandle check, EntityState state)
    {
        int ground = _world.GroundIndex(check);

        return ((state & EntityState.OnGround) != 0 || ground >= 0) && ground == _pusherIndex;
    }

    /// <summary>
    /// <c>SpeculativelyCheckPush</c>'s first half: sweep, and move the blocker
    /// to where the sweep ended.
    /// </summary>
    private void SweepBlocker(int index, Vector push)
    {
        Pushed info = _moved[index];
        EntityHandle blocker = info.Entity;

        Vector start = _world.Origin(blocker);
        Vector dest = start + push;

        PushTrace trace = _world.Sweep(blocker, start, dest);

        Say($"#{blocker.Value} swept {start} -> {dest}: fraction {trace.Fraction:R}, " +
            $"end {trace.EndPos}, startsolid {trace.StartSolid}");

        info.Push = push;
        info.Fraction = trace.Fraction;
        info.PusherIsGround = _world.GroundIndex(blocker) == _pusherIndex;
        info.Unblockable = _unblockableByPlayer
                           && (_world.State(blocker, out _) & EntityState.Player) != 0;

        if (info.Unblockable)
            _world.SetOrigin(blocker, dest);
        else if (trace.Fraction != 0f)
            _world.SetOrigin(blocker, trace.EndPos);

        _moved[index] = info;
    }

    /// <summary>
    /// <c>ComputeRotationalPushDirection</c>: where the point of contact goes
    /// when it turns with the pusher. <paramref name="previous"/> is the push the
    /// last blocker got, which the <c>SOLID_VPHYSICS</c> corner guess reads.
    /// </summary>
    private Vector ComputeRotationalPushDirection(EntityHandle blocker, Vector previous)
    {
        Vector start = _world.Origin(blocker);

        if (_rootSolid == SolidType.VPhysics)
        {
            // HACKHACK (stock's): use the move dir to guess which corner of the
            // box determines contact, and rotate the box so that corner stays
            // in the same local position.
            var (mins, maxs) = _world.Bounds(blocker);
            Vector absMins = start + mins;
            Vector absMaxs = start + maxs;

            start = new Vector(previous.X < 0f ? absMaxs.X : absMins.X,
                               previous.Y < 0f ? absMaxs.Y : absMins.Y,
                               previous.Z < 0f ? absMaxs.Z : absMins.Z);

            // Tell the player's physics code, so it can use vphysics to keep
            // players from getting stuck.
            if ((_world.State(blocker, out _) & EntityState.Player) != 0)
                _world.MarkRotPushed(blocker);
        }

        Vector local = _startLocalToWorld.InverseTR().TransformPoint(start);
        Vector end = _endLocalToWorld.TransformPoint(local);

        return end - start;
    }

    //-------------------------------------------------------------------------
    // The finish half: after the pusher is committed.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The rest of <c>SpeculativelyCheckPush</c>, with the pusher where it is
    /// going: is each pushed entity somewhere valid. On the first one that is
    /// not, <c>RegisterBlockage</c> and <c>RestoreEntities</c>, and that entity is
    /// the answer; the caller puts the pusher back and tells it. Otherwise
    /// <c>FinishPush</c>, and the answer is <see cref="EntityHandle.None"/>.
    /// </summary>
    public EntityHandle Finish()
    {
        int blockerIndex = -1;

        for (int i = _moved.Count - 1; i >= 0; i--)
        {
            if (!CheckPushedPosition(i))
            {
                blockerIndex = i;
                break;
            }
        }

        if (blockerIndex >= 0)
        {
            EntityHandle blocker = _moved[blockerIndex].Entity;
            Say($"#{blocker.Value} is not somewhere valid: blocked, everything put back");
            RestoreEntities();
            return blocker;
        }

        if (_rotational)
        {
            for (int i = _moved.Count - 1; i >= 0; i--)
                FinishRotPushedEntity(_moved[i].Entity);
        }

        return EntityHandle.None;
    }

    private bool CheckPushedPosition(int index)
    {
        Pushed info = _moved[index];
        EntityHandle blocker = info.Entity;
        Vector dest = info.StartOrigin + info.Push;

        if (!info.Unblockable)
        {
            // A rider the pusher moved out from under: stock's sweep never saw
            // the pusher, so it is finished now that the pusher has gone.
            if (info.PusherIsGround && info.Fraction < 1f)
            {
                Vector from = _world.Origin(blocker);
                PushTrace rest = _world.Sweep(blocker, from, dest);

                if (rest.Fraction != 0f)
                    _world.SetOrigin(blocker, rest.EndPos);

                info.Fraction += (1f - info.Fraction) * rest.Fraction;
                _moved[index] = info;
            }

            // Not blocked by something point-sized or non-solid.
            if (IsPointSized(blocker)
                || (_world.State(blocker, out _) & EntityState.Solid) == 0
                || (_world.SolidFlags(blocker) & SolidFlags.VolumeContents) != 0)
            {
                return true;
            }

            if (!_rotational && info.Fraction == 1f)
            {
                if (!IsPushedPositionValid(blocker))
                    _log.Warning($"Interpenetrating entities! (#{blocker.Value} and #{_pusher.Value})\n");

                return true;
            }
        }

        // Still blocked by the pushers?
        if (IsPushedPositionValid(blocker))
            return true;

        // A player blocking an unblockable pusher: nudge them half a unit each
        // way along the pusher's own forward and left to fix accumulated error,
        // and if none of that fits, put them where the push said anyway.
        if (info.Unblockable)
        {
            Vector origin = _world.Origin(blocker);

            for (int check = 0; check < 4; check++)
            {
                Vector move = Column(_endLocalToWorld, check >> 1);
                float factor = (check & 1) != 0 ? -0.5f : 0.5f;

                _world.SetOrigin(blocker, origin + move * factor);

                if (IsPushedPositionValid(blocker))
                    return true;
            }

            _world.SetOrigin(blocker, dest);
            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>IsPushedPositionValid</c>: the entity's hull where it now is, inside
    /// nothing.
    /// </summary>
    private bool IsPushedPositionValid(EntityHandle blocker)
    {
        Vector at = _world.Origin(blocker);
        return !_world.Sweep(blocker, at, at).StartSolid;
    }

    /// <summary><c>RestoreEntities</c>: everything back where it started.</summary>
    private void RestoreEntities()
    {
        for (int i = _moved.Count - 1; i >= 0; i--)
        {
            Pushed info = _moved[i];

            if (!Same(_world.Origin(info.Entity), info.StartOrigin))
                _world.SetOrigin(info.Entity, info.StartOrigin);
        }
    }

    /// <summary>
    /// <c>FinishRotPushedEntity</c>. Anything but a player turns its yaw with
    /// the push: freely rotating things should be VPHYSICS or children. A
    /// player's half — the view turned by the push — is not ported (see the
    /// type's remarks).
    /// </summary>
    private void FinishRotPushedEntity(EntityHandle pushed)
    {
        if ((_world.State(pushed, out _) & EntityState.Player) != 0)
            return;

        QAngle angles = _world.Angles(pushed);
        _world.SetAngles(pushed, angles with { Yaw = angles.Yaw + _amove.Yaw });
    }

    //-------------------------------------------------------------------------
    // Helpers.
    //-------------------------------------------------------------------------

    private bool Contains(EntityHandle entity)
    {
        for (int i = 0; i < _moved.Count; i++)
        {
            if (_moved[i].Entity == entity)
                return true;
        }

        return false;
    }

    /// <summary><c>IsPointSized</c>: a bounding radius of zero.</summary>
    private bool IsPointSized(EntityHandle entity)
    {
        var (mins, maxs) = _world.Bounds(entity);
        return Same(mins, Vector.Zero) && Same(maxs, Vector.Zero);
    }

    /// <summary><c>GetRootMoveParent</c>: up the parent links to the top.</summary>
    private static EntityHandle RootMoveParent(Hierarchy? parenting, EntityHandle entity)
    {
        if (parenting is null)
            return entity;

        EntityHandle root = entity;

        // Bounded, so a cycle the registry should never hold cannot hang a frame.
        for (int depth = 0; depth < 64; depth++)
        {
            EntityHandle parent = parenting.StandaloneParentOf(root);

            if (parent == EntityHandle.None)
                break;

            root = parent;
        }

        return root;
    }

    private static Vector Column(in Transform m, int column) => column switch
    {
        0 => new Vector(m.M00, m.M10, m.M20),
        1 => new Vector(m.M01, m.M11, m.M21),
        _ => new Vector(m.M02, m.M12, m.M22),
    };

    private static bool Same(in Vector a, in Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>The axis-aligned box around a box put through a transform.</summary>
    private static void AabbOf(in Transform m, Vector mins, Vector maxs, out Vector absMins, out Vector absMaxs)
    {
        absMins = new Vector(float.MaxValue, float.MaxValue, float.MaxValue);
        absMaxs = new Vector(float.MinValue, float.MinValue, float.MinValue);

        for (int corner = 0; corner < 8; corner++)
        {
            Vector p = m.TransformPoint(new Vector((corner & 1) != 0 ? maxs.X : mins.X,
                                                   (corner & 2) != 0 ? maxs.Y : mins.Y,
                                                   (corner & 4) != 0 ? maxs.Z : mins.Z));

            absMins = new Vector(MathF.Min(absMins.X, p.X), MathF.Min(absMins.Y, p.Y), MathF.Min(absMins.Z, p.Z));
            absMaxs = new Vector(MathF.Max(absMaxs.X, p.X), MathF.Max(absMaxs.Y, p.Y), MathF.Max(absMaxs.Z, p.Z));
        }
    }

    /// <summary>
    /// <c>IntersectsPushers</c>, for a pusher that is its collision box: does an
    /// oriented box overlap a world box by more than <see cref="TouchTolerance"/>.
    /// The separating-axis test over the fifteen axes two boxes have.
    /// </summary>
    public static bool ObbIntersectsAabb(Vector obbMins, Vector obbMaxs, in Transform obbToWorld,
                                         Vector aabbMins, Vector aabbMaxs)
    {
        var box = new SeparatingAxes(Column(obbToWorld, 0), Column(obbToWorld, 1), Column(obbToWorld, 2),
                                     (obbMaxs - obbMins) * 0.5f, (aabbMaxs - aabbMins) * 0.5f,
                                     (aabbMins + aabbMaxs) * 0.5f
                                     - obbToWorld.TransformPoint((obbMins + obbMaxs) * 0.5f));

        var x = new Vector(1f, 0f, 0f);
        var y = new Vector(0f, 1f, 0f);
        var z = new Vector(0f, 0f, 1f);

        if (box.Separated(x) || box.Separated(y) || box.Separated(z)
            || box.Separated(box.A0) || box.Separated(box.A1) || box.Separated(box.A2))
        {
            return false;
        }

        return !(box.Separated(box.A0.Cross(x)) || box.Separated(box.A0.Cross(y)) || box.Separated(box.A0.Cross(z))
              || box.Separated(box.A1.Cross(x)) || box.Separated(box.A1.Cross(y)) || box.Separated(box.A1.Cross(z))
              || box.Separated(box.A2.Cross(x)) || box.Separated(box.A2.Cross(y)) || box.Separated(box.A2.Cross(z)));
    }

    /// <summary>An oriented box and a world box, projected onto one axis at a time.</summary>
    private readonly record struct SeparatingAxes(Vector A0, Vector A1, Vector A2, Vector ObbHalf,
                                                  Vector BoxHalf, Vector Between)
    {
        public bool Separated(Vector axis)
        {
            // A degenerate cross product (parallel edges) separates nothing.
            if (axis.LengthSquared < 1e-10f)
                return false;

            float rObb = ObbHalf.X * MathF.Abs(A0.Dot(axis))
                       + ObbHalf.Y * MathF.Abs(A1.Dot(axis))
                       + ObbHalf.Z * MathF.Abs(A2.Dot(axis));
            float rBox = BoxHalf.X * MathF.Abs(axis.X)
                       + BoxHalf.Y * MathF.Abs(axis.Y)
                       + BoxHalf.Z * MathF.Abs(axis.Z);

            return MathF.Abs(Between.Dot(axis)) >= rObb + rBox - TouchTolerance * axis.Length;
        }
    }
}
