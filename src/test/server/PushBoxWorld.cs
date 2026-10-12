using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Tests;

/// <summary>
/// A world of boxes for the pusher's facts: every entity is a box, a sweep hits
/// every solid box but the swept one (which is what the ABI's hull sweep does —
/// it does NOT hide the pusher), and a box may be turned about Z.
///
/// <para>
/// It answers three askers from one state, so that old and new code see the
/// same world: <see cref="IPushWorld"/> for <see cref="PhysicsPushedEntities"/>,
/// <see cref="IHierarchyWorld"/> for the mover's own commit, and — through
/// <see cref="Install"/> — the <see cref="RecordingEngine"/> reads a mover makes
/// (origin, angles, bounds, the sphere walk, state, IsPlayer, the brush hull
/// trace). An origin a caller writes through the engine is read back from the
/// engine's call log.
/// </para>
///
/// <para>
/// The overlap test is the face-normal separating-axis test, which is exact
/// for boxes that turn only about Z — every box here — and is written
/// independently of <see cref="PhysicsPushedEntities.ObbIntersectsAabb"/>.
/// </para>
/// </summary>
internal sealed class PushBoxWorld : IPushWorld, IHierarchyWorld
{
    public sealed class Box
    {
        public Vector Origin;
        public QAngle Angles;
        public Vector Mins;
        public Vector Maxs;
        public EntityState State = EntityState.Solid;
        public MoveType MoveType = MoveType.None;
        public SourceSharp.Abi.SolidFlags Flags = SourceSharp.Abi.SolidFlags.None;
        public SourceSharp.Abi.SolidType Solid = SourceSharp.Abi.SolidType.Bsp;
        public int Group;
        public int Ground = -1;
        public int Index;
    }

    private readonly Dictionary<EntityHandle, Box> _boxes = new();
    private readonly List<EntityHandle> _order = new();
    private RecordingEngine? _engine;
    private int _synced;

    /// <summary>Every origin write, in order, from whichever asker made it.</summary>
    public readonly List<(EntityHandle Entity, Vector Origin)> OriginWrites = new();

    /// <summary>Every write the hierarchy made, which is the mover's commit.</summary>
    public readonly List<(EntityHandle Child, Vector Origin, QAngle Angles)> Written = new();

    /// <summary>Who <see cref="MarkRotPushed"/> was told about.</summary>
    public readonly List<EntityHandle> RotPushed = new();

    public Box Add(EntityHandle handle, Vector origin, Vector mins, Vector maxs)
    {
        var box = new Box { Origin = origin, Mins = mins, Maxs = maxs, Index = (int)(handle.Value & 0x7FF) };
        _boxes[handle] = box;
        _order.Add(handle);
        return box;
    }

    /// <summary>A standing player: a 32x32x72 hull, walking, on the ground of <paramref name="ground"/>.</summary>
    public Box AddPlayer(EntityHandle handle, Vector origin, int ground = -1)
    {
        Box box = Add(handle, origin, new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f));
        box.State = EntityState.Solid | EntityState.Player | (ground >= 0 ? EntityState.OnGround : 0);
        box.MoveType = MoveType.Walk;
        box.Solid = SourceSharp.Abi.SolidType.BoundingBox;
        box.Group = (int)EngineCollisionGroup.Player;
        box.Ground = ground;
        return box;
    }

    public Box this[EntityHandle handle]
    {
        get
        {
            Sync();
            return _boxes[handle];
        }
    }

    //-------------------------------------------------------------------------
    // Geometry.
    //-------------------------------------------------------------------------

    private static Vector Rotate(QAngle a, Vector v)
    {
        float r = a.Yaw * MathF.PI / 180f;
        float c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
    }

    private static (Vector Mins, Vector Maxs) WorldBox(Box b, Vector origin)
    {
        var lo = new Vector(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < 8; i++)
        {
            Vector p = origin + Rotate(b.Angles, new Vector((i & 1) != 0 ? b.Maxs.X : b.Mins.X,
                                                            (i & 2) != 0 ? b.Maxs.Y : b.Mins.Y,
                                                            (i & 4) != 0 ? b.Maxs.Z : b.Mins.Z));
            lo = new Vector(MathF.Min(lo.X, p.X), MathF.Min(lo.Y, p.Y), MathF.Min(lo.Z, p.Z));
            hi = new Vector(MathF.Max(hi.X, p.X), MathF.Max(hi.Y, p.Y), MathF.Max(hi.Z, p.Z));
        }

        return (lo, hi);
    }

    /// <summary>The four corners of a box's footprint, in the world.</summary>
    private static Vector[] Footprint(Box b, Vector origin) => new[]
    {
        origin + Rotate(b.Angles, new Vector(b.Mins.X, b.Mins.Y, 0f)),
        origin + Rotate(b.Angles, new Vector(b.Maxs.X, b.Mins.Y, 0f)),
        origin + Rotate(b.Angles, new Vector(b.Maxs.X, b.Maxs.Y, 0f)),
        origin + Rotate(b.Angles, new Vector(b.Mins.X, b.Maxs.Y, 0f)),
    };

    private const float Touch = 0.001f;

    /// <summary>Do two boxes overlap by more than a touch.</summary>
    private static bool Overlap(Box a, Vector aOrigin, Box b, Vector bOrigin)
    {
        // Z first: both are upright.
        if (aOrigin.Z + a.Maxs.Z <= bOrigin.Z + b.Mins.Z + Touch
            || bOrigin.Z + b.Maxs.Z <= aOrigin.Z + a.Mins.Z + Touch)
        {
            return false;
        }

        Vector[] pa = Footprint(a, aOrigin);
        Vector[] pb = Footprint(b, bOrigin);

        foreach (Vector[] poly in new[] { pa, pb })
        {
            for (int i = 0; i < 2; i++)
            {
                Vector edge = poly[i + 1] - poly[i];
                var axis = new Vector(-edge.Y, edge.X, 0f);
                float len = axis.Length;
                axis = axis * (1f / len);

                (float aLo, float aHi) = Project(pa, axis);
                (float bLo, float bHi) = Project(pb, axis);

                if (aHi <= bLo + Touch || bHi <= aLo + Touch)
                    return false;
            }
        }

        return true;
    }

    private static (float, float) Project(Vector[] poly, Vector axis)
    {
        float lo = float.MaxValue, hi = float.MinValue;

        foreach (Vector p in poly)
        {
            float d = p.Dot(axis);
            lo = MathF.Min(lo, d);
            hi = MathF.Max(hi, d);
        }

        return (lo, hi);
    }

    private bool OverlapsAnything(EntityHandle self, Vector at)
    {
        Box me = _boxes[self];

        foreach (EntityHandle other in _order)
        {
            if (other == self)
                continue;

            Box b = _boxes[other];

            if ((b.State & EntityState.Solid) == 0)
                continue;

            if (Overlap(me, at, b, b.Origin))
                return true;
        }

        return false;
    }

    //-------------------------------------------------------------------------
    // The engine's call log: origins written through it.
    //-------------------------------------------------------------------------

    private void Sync()
    {
        if (_engine is null)
            return;

        IReadOnlyList<EngineCall> calls = _engine.Calls;

        for (; _synced < calls.Count; _synced++)
        {
            EngineCall call = calls[_synced];

            if (call.Member == "Entities.SetOrigin"
                && call.Args[0] is EntityHandle h && call.Args[1] is Vector v
                && _boxes.TryGetValue(h, out Box? box))
            {
                box.Origin = v;
                OriginWrites.Add((h, v));
            }
        }
    }

    /// <summary>
    /// Answers the engine reads a mover makes from this world. Strict is left
    /// as the caller set it; every member installed here answers per argument.
    /// </summary>
    public void Install(RecordingEngine engine)
    {
        _engine = engine;
        _synced = engine.Calls.Count;

        EntityHandle Arg0() => (EntityHandle)engine.Calls[^1].Args[0]!;

        engine.ReturnsEachCall("Entities.GetOrigin", () => this[Arg0()].Origin)
              .ReturnsEachCall("Entities.GetAngles", () => this[Arg0()].Angles.AsVector())
              .ReturnsEachCall("Entities.GetBounds", () => Bounds(Arg0()))
              .ReturnsEachCall("Entities.IsPlayer", () => (this[Arg0()].State & EntityState.Player) != 0)
              .ReturnsEachCall("Networking.EntityGetState", () => this[Arg0()].State)
              .ReturnsEachCall("Networking.EntityGetState.moveType", () => this[Arg0()].MoveType)
              .ReturnsEachCall("WorldQuery.EntitiesInSphere.into", () =>
              {
                  EngineCall call = engine.Calls[^1];
                  var centre = (Vector)call.Args[0]!;
                  var radius = (float)call.Args[1]!;
                  var found = new List<EntityHandle>();

                  Sync();
                  foreach (EntityHandle h in _order)
                  {
                      var (lo, hi) = WorldBox(_boxes[h], _boxes[h].Origin);
                      float dx = MathF.Max(lo.X - centre.X, MathF.Max(0f, centre.X - hi.X));
                      float dy = MathF.Max(lo.Y - centre.Y, MathF.Max(0f, centre.Y - hi.Y));
                      float dz = MathF.Max(lo.Z - centre.Z, MathF.Max(0f, centre.Z - hi.Z));

                      if (dx * dx + dy * dy + dz * dz <= radius * radius)
                          found.Add(h);
                  }

                  return found.ToArray();
              })
              // The brush trace the old carry made: nothing in the way.
              .Returns("WorldQuery.TraceHull", new HullResult(false, false, EntityHandle.None))
              .Returns("Console.FindConVarValue", null);
    }

    //-------------------------------------------------------------------------
    // IPushWorld.
    //-------------------------------------------------------------------------

    public int EntitiesInBox(Vector mins, Vector maxs, Span<EntityHandle> into)
    {
        Sync();
        int n = 0;

        foreach (EntityHandle h in _order)
        {
            var (lo, hi) = WorldBox(_boxes[h], _boxes[h].Origin);

            if (lo.X <= maxs.X && hi.X >= mins.X && lo.Y <= maxs.Y && hi.Y >= mins.Y
                && lo.Z <= maxs.Z && hi.Z >= mins.Z && n < into.Length)
            {
                into[n++] = h;
            }
        }

        return n;
    }

    public EntityState State(EntityHandle entity, out MoveType moveType)
    {
        Box b = this[entity];
        moveType = b.MoveType;
        return b.State;
    }

    public SolidFlags SolidFlags(EntityHandle entity) => this[entity].Flags;
    public SolidType SolidType(EntityHandle entity) => this[entity].Solid;
    public int CollisionGroup(EntityHandle entity) => this[entity].Group;
    public Vector Origin(EntityHandle entity) => this[entity].Origin;
    public QAngle Angles(EntityHandle entity) => this[entity].Angles;

    public (Vector Mins, Vector Maxs) Bounds(EntityHandle entity)
    {
        Box b = this[entity];
        var (lo, hi) = WorldBox(b, b.Origin);
        return (lo - b.Origin, hi - b.Origin);
    }

    public bool CollisionBox(EntityHandle entity, out Vector mins, out Vector maxs)
    {
        Box b = this[entity];
        mins = b.Mins;
        maxs = b.Maxs;
        return true;
    }

    public int GroundIndex(EntityHandle entity) => this[entity].Ground;
    public int Index(EntityHandle entity) => this[entity].Index;

    public void SetOrigin(EntityHandle entity, Vector origin)
    {
        this[entity].Origin = origin;
        OriginWrites.Add((entity, origin));
    }

    public void SetAngles(EntityHandle entity, QAngle angles) => this[entity].Angles = angles;

    /// <summary>
    /// The swept box against every other solid box. Starting inside one is
    /// fraction zero and no movement (the conservative answer); otherwise the
    /// last place along the way that overlaps nothing.
    /// </summary>
    public PushTrace Sweep(EntityHandle entity, Vector start, Vector end)
    {
        Sync();

        if (OverlapsAnything(entity, start))
            return new PushTrace(0f, start, true, OverlapsAnything(entity, end));

        float clear = 0f;
        const int Steps = 256;

        for (int i = 1; i <= Steps; i++)
        {
            float t = i / (float)Steps;

            if (OverlapsAnything(entity, start + (end - start) * t))
            {
                float lo = clear, hi = t;

                for (int k = 0; k < 30; k++)
                {
                    float mid = (lo + hi) * 0.5f;

                    if (OverlapsAnything(entity, start + (end - start) * mid))
                        hi = mid;
                    else
                        lo = mid;
                }

                return new PushTrace(lo, start + (end - start) * lo, false, false);
            }

            clear = t;
        }

        return new PushTrace(1f, end, false, false);
    }

    public void MarkRotPushed(EntityHandle player) => RotPushed.Add(player);

    //-------------------------------------------------------------------------
    // IHierarchyWorld: only the commit is reached, for unparented movers.
    //-------------------------------------------------------------------------

    public void WriteComposed(EntityHandle child, Vector origin, QAngle angles, Transform childToWorld)
    {
        Written.Add((child, origin, angles));

        Box b = this[child];
        b.Origin = origin;
        b.Angles = angles;
    }

    public bool IsStandalone(EntityHandle handle) => true;
    public bool IsBase(EntityHandle handle) => false;
    public bool CanPose(EntityHandle handle) => false;

    public bool TryGetEntityFrame(EntityHandle handle, out Transform frame)
    {
        Sync();

        if (!_boxes.TryGetValue(handle, out Box? b))
        {
            frame = Transform.Identity;
            return false;
        }

        frame = Transform.FromOriginAngles(b.Origin, b.Angles);
        return true;
    }

    public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame)
    {
        frame = Transform.Identity;
        return false;
    }

    public int LookupAttachment(EntityHandle handle, string attachmentName) => -1;

    public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles)
    {
        Sync();

        if (!_boxes.TryGetValue(handle, out Box? b))
        {
            origin = Vector.Zero;
            angles = QAngle.Zero;
            return false;
        }

        origin = b.Origin;
        angles = b.Angles;
        return true;
    }

    public EntityHandle FindStandaloneByName(string name) => EntityHandle.None;
    public EntityHandle FindBaseByName(string name) => EntityHandle.None;
    public string NameOf(EntityHandle handle) => "test";
    public void Remove(EntityHandle handle) { }
    public void DetachPhysics(EntityHandle child) { }
    public void Warn(string message) { }
}
