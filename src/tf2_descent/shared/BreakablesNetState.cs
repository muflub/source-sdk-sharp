using SourceSharp.Abi;
using SourceSharp.Shared.Bones;

using NVector = System.Numerics.Vector3;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client
// (plan_side_isolation §2.4): the one entity that owns a level's crates and
// barrels, its networked list, and the box test both sides answer traces with.

namespace TF2.Descent.Shared;

/// <summary>
/// <c>tf2_descent_breakables</c>'s networked state (the user, 2026-11-15: "a single breakable prop
/// manager as an entity that owns all the props on the level and their health + collision"): every
/// crate and barrel of the level, at most <see cref="Max"/>, by place, facing and catalog entry, sent
/// once with the entity; and one bit each for broken, which is all a break changes on the wire.
/// </summary>
[StandaloneNetState(ClassName)]
public partial class BreakablesNetState
{
    public const string ClassName = "tf2_descent_breakables";

    /// <summary>The most crates and barrels one level's entity holds.</summary>
    public const int Max = 128;

    /// <summary>How many of the slots below hold a prop.</summary>
    public partial byte Count { get; set; }

    /// <summary>Each prop's foot (the marker's point, on the floor), in whole units: the list fits the largest blob tier (2048 bytes with the block).</summary>
    [NetArray(Max)] public partial NetArray<short> X { get; }

    [NetArray(Max)] public partial NetArray<short> Y { get; }

    [NetArray(Max)] public partial NetArray<short> Z { get; }

    /// <summary>Each prop's facing in 256ths of a turn, counter-clockwise from +x.</summary>
    [NetArray(Max)] public partial NetArray<byte> Yaw { get; }

    /// <summary>Each prop's <see cref="FurnitureCatalog"/> number (a breakable's: its model and its box).</summary>
    [NetArray(Max)] public partial NetArray<byte> Entry { get; }

    /// <summary>One bit a prop, set when it is broken (or breaking): it is drawn and stops nothing no more.</summary>
    [NetArray(Max / 32)] public partial NetArray<uint> Broken { get; }
}

/// <summary>One crate or barrel as both sides read it: its slot, catalog entry, foot and facing (degrees).</summary>
public readonly record struct BreakableSpot(int Slot, byte Entry, NVector Foot, float Yaw);

/// <summary>
/// The crates' and barrels' boxes and the trace both sides answer with: the box of each model about
/// its foot (from the models' studiohdr hulls), turned to the prop's facing, and a ray or a swept box
/// against the live ones, nearest hit first. Pure: the server's entity and the client's (prediction)
/// run the same test over the same networked list.
/// </summary>
public static class BreakableGeometry
{
    /// <summary><c>CONTENTS_SOLID</c>: what a prop is to a trace.</summary>
    public const int ContentsSolid = 0x1;

    /// <summary><c>CONTENTS_HITBOX</c>: a shot's mask carries it.</summary>
    public const int ContentsHitbox = unchecked((int)0x40000000);

    /// <summary><c>CONTENTS_MONSTER</c>, <c>CONTENTS_MOVEABLE</c>, <c>CONTENTS_WINDOW</c>, <c>CONTENTS_GRATE</c>: masks a solid prop answers too.</summary>
    public const int ContentsSolidLike = 0x1 | 0x2 | 0x8 | 0x4000 | 0x2000000;

    /// <summary>A prop's box about its foot, unturned: the miningcrate's and the wooden barrel's hulls.</summary>
    public static (NVector Mins, NVector Maxs) LocalBox(byte entry) =>
        FurnitureCatalog.Of(entry)?.Model switch
        {
            FurnitureCatalog.WoodenBarrelModel => (new NVector(-23f, -23f, 0f), new NVector(23f, 23f, 60f)),
            _ => (new NVector(-21f, -36f, 0f), new NVector(22f, 37f, 39f)),
        };

    /// <summary>How high above its foot a prop's model origin stands (the barrel's origin is at its middle).</summary>
    public static float Lift(byte entry) => FurnitureCatalog.Of(entry)?.Model == FurnitureCatalog.WoodenBarrelModel ? 30f : 0f;

    /// <summary>The prop's box in the world: its local box turned by its facing (the box of the turned box), at its foot.</summary>
    public static (NVector Mins, NVector Maxs) WorldBox(in BreakableSpot prop)
    {
        var (mins, maxs) = LocalBox(prop.Entry);
        float a = prop.Yaw * MathF.PI / 180f, c = MathF.Abs(MathF.Cos(a)), s = MathF.Abs(MathF.Sin(a));
        var centre = (mins + maxs) * 0.5f;
        var half = (maxs - mins) * 0.5f;
        float ca = MathF.Cos(a), sa = MathF.Sin(a);
        var turned = new NVector(centre.X * ca - centre.Y * sa, centre.X * sa + centre.Y * ca, centre.Z);
        var extent = new NVector(half.X * c + half.Y * s, half.X * s + half.Y * c, half.Z);
        var at = prop.Foot + turned;
        return (at - extent, at + extent);
    }

    /// <summary>A facing in degrees as the wire's 256ths of a turn, and back.</summary>
    public static byte YawByte(float degrees) => (byte)((int)MathF.Round(((degrees % 360f) + 360f) % 360f / 360f * 256f) & 0xFF);

    public static float YawDegrees(byte b) => b * 360f / 256f;

    /// <summary>A coordinate as the wire's whole unit.</summary>
    public static short Coord(float v) => (short)Math.Clamp(MathF.Round(v), short.MinValue, short.MaxValue);

    /// <summary>A prop as the wire carries it (its foot in whole units, its facing in 256ths): the server tests what the client tests.</summary>
    public static BreakableSpot OnTheWire(int slot, byte entry, NVector foot, float yaw) =>
        new(slot, entry, new NVector(Coord(foot.X), Coord(foot.Y), Coord(foot.Z)), YawDegrees(YawByte(yaw)));

    /// <summary>
    /// <paramref name="ray"/> against each prop <paramref name="live"/> lets through: the nearest
    /// box it enters writes <paramref name="tr"/> (fraction, end, plane, solid contents, the prop's
    /// slot as its hitbox), a start inside one is solid at fraction 0. Answers the slot hit, or -1.
    /// A mask with nothing solid in it passes through every prop.
    /// </summary>
    public static int Trace(in BoneRay ray, int contentsMask, BoneTrace tr, IReadOnlyList<BreakableSpot> props, Func<int, bool> live)
    {
        if ((contentsMask & (ContentsSolidLike | ContentsHitbox)) == 0)
            return -1;
        float best = tr.Fraction;
        int hit = -1;
        NVector normal = default;
        bool startSolid = false, allSolid = false;
        foreach (var prop in props)
        {
            if (!live(prop.Slot))
                continue;
            var (mins, maxs) = WorldBox(prop);
            if (!Sweep(ray.Start, ray.Delta, ray.Extents, mins, maxs, out float t, out var n, out bool inside, out bool stays))
                continue;
            if (inside)
            {
                best = 0f;
                hit = prop.Slot;
                normal = default;
                startSolid = true;
                allSolid = stays;
                break;
            }

            if (t < best)
            {
                best = t;
                hit = prop.Slot;
                normal = n;
            }
        }

        if (hit < 0)
            return -1;
        var origin = ray.Start + ray.StartOffset;
        tr.Fraction = best;
        tr.EndPos = origin + ray.Delta * best;
        tr.PlaneNormal = normal;
        tr.PlaneDist = NVector.Dot(normal, tr.EndPos);
        tr.PlaneType = 3;
        tr.Contents = ContentsSolid;
        tr.StartSolid = startSolid;
        tr.AllSolid = allSolid;
        tr.HitBox = hit;
        tr.HitGroup = 0;
        return hit;
    }

    /// <summary>
    /// A box of half size <paramref name="extents"/> centred at <paramref name="start"/>, moving by
    /// <paramref name="delta"/>, against the box [<paramref name="mins"/>, <paramref name="maxs"/>]
    /// (the slab test on the box grown by the extents): the fraction it enters at and the face's
    /// normal; <paramref name="inside"/> when it starts inside, <paramref name="stays"/> when it ends
    /// inside too.
    /// </summary>
    public static bool Sweep(NVector start, NVector delta, NVector extents, NVector mins, NVector maxs,
                             out float enter, out NVector normal, out bool inside, out bool stays)
    {
        var lo = mins - extents;
        var hi = maxs + extents;
        enter = 0f;
        normal = default;
        inside = Within(start, lo, hi);
        stays = inside && Within(start + delta, lo, hi);
        if (inside)
            return true;

        float tIn = float.NegativeInfinity, tOut = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float s = Axis(start, axis), d = Axis(delta, axis), l = Axis(lo, axis), h = Axis(hi, axis);
            if (MathF.Abs(d) < 1e-7f)
            {
                if (s < l || s > h)
                    return false;
                continue;
            }

            float t1 = (l - s) / d, t2 = (h - s) / d;
            float sign = -1f;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                sign = 1f;
            }

            if (t1 > tIn)
            {
                tIn = t1;
                normal = axis switch { 0 => new NVector(sign, 0, 0), 1 => new NVector(0, sign, 0), _ => new NVector(0, 0, sign) };
            }

            tOut = MathF.Min(tOut, t2);
            if (tIn > tOut)
                return false;
        }

        if (tIn < 0f || tIn > 1f)
            return false;
        enter = tIn;
        return true;
    }

    private static bool Within(NVector p, NVector lo, NVector hi) =>
        p.X > lo.X && p.X < hi.X && p.Y > lo.Y && p.Y < hi.Y && p.Z > lo.Z && p.Z < hi.Z;

    private static float Axis(NVector v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
