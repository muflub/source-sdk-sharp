using System.Globalization;
using System.Text;

namespace SourceSharp.MapGen;

/// <summary>
/// A face's material and its texture scale: map units per texel, so a
/// 1024-texel rock at 0.25 repeats every 256 units. Faces are projected on
/// the world's axes (see <see cref="Brush.ToVmf"/>), so any face of one scale
/// lines up with its neighbours.
/// </summary>
public readonly record struct Surface(string Material, float Scale = Surface.DefaultScale)
{
    /// <summary>Hammer's default, and the town's.</summary>
    public const float DefaultScale = 0.25f;

    public static implicit operator Surface(string material) => new(material);

    public override string ToString() => Material;
}

/// <summary>A compass direction on the map's floor: east is +x, north is +y.</summary>
public enum Compass
{
    East,
    West,
    North,
    South,
}

/// <summary>
/// A displacement on one four-sided face: a grid of (2^power + 1)² vertices,
/// each pushed along <see cref="Normal"/> by its distance.
/// </summary>
/// <remarks>
/// Only powers 2 and 3: map-tools' rooms pipeline carries a displacement's
/// collision only as the mesh vbsp builds for those, and refuses power 4.
/// Row r and column c of <see cref="Distances"/> are the vertex at
/// fraction c/(n−1) along the face's first edge from
/// <see cref="BrushFace.Polygon"/>[0] and r/(n−1) along its last edge
/// (the polygon's winding, which for a floor patch is +x then +y).
/// </remarks>
public sealed class Displacement
{
    public Displacement(int power, Func<float, float, float> height, Point normal)
    {
        if (power is not (2 or 3))
            throw new ArgumentOutOfRangeException(nameof(power), power, "a displacement is power 2 or 3; map-tools refuses power 4");
        Power = power;
        Normal = normal;
        int n = Size;
        Distances = new float[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                Distances[r, c] = height(c / (float)(n - 1), r / (float)(n - 1));
    }

    public int Power { get; }

    /// <summary>The vertices along one edge.</summary>
    public int Size => (1 << Power) + 1;

    /// <summary>The direction every vertex moves in (unit).</summary>
    public Point Normal { get; }

    /// <summary>Each vertex's distance along <see cref="Normal"/>, [row, column].</summary>
    public float[,] Distances { get; }

    /// <summary>The largest distance, the surface's reach off its face.</summary>
    public float MaxDistance => Distances.Cast<float>().Max();
}

/// <summary>
/// One face of a <see cref="Brush"/>: a convex polygon wound counter-clockwise
/// seen from outside the brush, its surface, and optionally a displacement.
/// </summary>
public sealed record BrushFace(IReadOnlyList<Point> Polygon, Surface Surface, Displacement? Displacement = null)
{
    /// <summary>The outward unit normal (Newell's method, so a slightly uneven polygon still gives its plane).</summary>
    public Point Normal
    {
        get
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < Polygon.Count; i++)
            {
                Point a = Polygon[i], b = Polygon[(i + 1) % Polygon.Count];
                x += (a.Y - b.Y) * (a.Z + b.Z);
                y += (a.Z - b.Z) * (a.X + b.X);
                z += (a.X - b.X) * (a.Y + b.Y);
            }

            double length = Math.Sqrt(x * x + y * y + z * z);
            return new((float)(x / length), (float)(y / length), (float)(z / length));
        }
    }

    /// <summary>The plane's distance: n · p for every p on the face.</summary>
    public float Distance => Dot(Normal, Polygon[0]);

    internal static float Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>
/// A convex brush: its faces, each a polygon on the brush's boundary. Built
/// by the shape helpers below (boxes, wedges, stairs, prisms, ring walls),
/// which give every vertex in whole units, and checked convex and closed on
/// construction, so a brush that would turn inside out in vbsp (a face wound
/// the wrong way) is refused here instead.
/// </summary>
public sealed class Brush
{
    /// <summary>How far a point may stand off a plane and still count as on it.</summary>
    public const float Epsilon = 0.01f;

    public Brush(IReadOnlyList<BrushFace> faces, Surface surface)
    {
        if (faces.Count < 4)
            throw new ArgumentException("a brush has at least four faces", nameof(faces));
        Faces = faces;
        Surface = surface;
        var all = faces.SelectMany(f => f.Polygon).ToArray();
        Mins = new(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z));
        Maxs = new(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z));
        Point centre = new(all.Average(p => p.X), all.Average(p => p.Y), all.Average(p => p.Z));
        foreach (BrushFace face in faces)
        {
            Point n = face.Normal;
            float d = face.Distance;
            if (BrushFace.Dot(n, centre) >= d - Epsilon)
                throw new ArgumentException($"a face of the brush at ({Mins})-({Maxs}) faces inward or holds its centre");
            foreach (Point p in all)
                if (BrushFace.Dot(n, p) > d + 0.05f)
                    throw new ArgumentException($"the brush at ({Mins})-({Maxs}) is not convex: ({p}) is outside a face");
        }
    }

    public IReadOnlyList<BrushFace> Faces { get; }

    /// <summary>The brush's body surface: what most of its faces are.</summary>
    public Surface Surface { get; }

    /// <summary>The body's material.</summary>
    public string Material => Surface.Material;

    public Point Mins { get; }

    public Point Maxs { get; }

    /// <summary>Whether this is an axis-aligned box (six faces, every normal on an axis).</summary>
    public bool IsBox => Faces.Count == 6 && Faces.All(f => Math.Abs(Math.Abs(f.Normal.X) + Math.Abs(f.Normal.Y) + Math.Abs(f.Normal.Z) - 1) < 1e-4);

    /// <summary>The same brush moved by <paramref name="by"/>.</summary>
    public Brush Offset(Point by) =>
        new([.. Faces.Select(f => f with { Polygon = [.. f.Polygon.Select(p => p + by)] })], Surface);

    /// <summary>The same brush with every face of <paramref name="from"/> made <paramref name="to"/>.</summary>
    public Brush Retextured(string from, Surface to) =>
        new([.. Faces.Select(f => f.Surface.Material == from ? f with { Surface = to } : f)], Surface.Material == from ? to : Surface);

    /// <summary>Whether <paramref name="p"/> is inside or on the brush.</summary>
    public bool Contains(Point p) => Faces.All(f => BrushFace.Dot(f.Normal, p) <= f.Distance + Epsilon);

    /// <summary>
    /// The vertical extent of the brush over the column at (<paramref name="x"/>,
    /// <paramref name="y"/>), or null when the column misses it or only grazes
    /// a side. Displacements are not counted: a displaced face's brush is its
    /// base.
    /// </summary>
    public (float Low, float High)? Column(float x, float y)
    {
        float low = float.NegativeInfinity, high = float.PositiveInfinity;
        foreach (BrushFace face in Faces)
        {
            Point n = face.Normal;
            float rest = face.Distance - n.X * x - n.Y * y;
            if (Math.Abs(n.Z) < 1e-6f)
            {
                if (rest < Epsilon)
                    return null;
                continue;
            }

            float z = rest / n.Z;
            if (n.Z > 0)
                high = Math.Min(high, z);
            else
                low = Math.Max(low, z);
        }

        return high - low > Epsilon ? (low, high) : null;
    }

    /// <summary>
    /// Whether the brush and the box overlap with volume (touching is not
    /// overlapping). Tests the box's axes and the brush's face planes, which
    /// is exact for the shapes here (boxes, wedges and prisms against boxes)
    /// up to edge-on-edge cases, where it errs towards overlapping.
    /// </summary>
    public bool Overlaps(Point mins, Point maxs)
    {
        if (Maxs.X <= mins.X + Epsilon || Mins.X >= maxs.X - Epsilon
            || Maxs.Y <= mins.Y + Epsilon || Mins.Y >= maxs.Y - Epsilon
            || Maxs.Z <= mins.Z + Epsilon || Mins.Z >= maxs.Z - Epsilon)
            return false;
        foreach (BrushFace face in Faces)
        {
            Point n = face.Normal;
            // The box's corner deepest into the face's half-space.
            Point deepest = new(n.X > 0 ? mins.X : maxs.X, n.Y > 0 ? mins.Y : maxs.Y, n.Z > 0 ? mins.Z : maxs.Z);
            if (BrushFace.Dot(n, deepest) >= face.Distance - Epsilon)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The brush as a map file writes it. Every face is projected on the
    /// world axis nearest its normal (Hammer's world alignment), at its
    /// surface's scale, with the texture's origin at <paramref name="anchor"/>:
    /// a room passes its cell's corner, so its textures start at the cell
    /// whatever cell of the library it is written in, and the rooms of a
    /// level line up wherever the link puts them.
    /// </summary>
    public VmfSolid ToVmf(Point anchor = default)
    {
        var solid = new VmfSolid();
        foreach (BrushFace face in Faces)
        {
            var (u, v) = Axes(face.Normal);
            float s = face.Surface.Scale;
            // vbsp's normal is (p0 - p1) x (p2 - p1), outward: for a polygon
            // wound counter-clockwise from outside that is (v2, v1, v0).
            var side = new VmfSide
            {
                P0 = face.Polygon[2], P1 = face.Polygon[1], P2 = face.Polygon[0],
                Material = face.Surface.Material,
                UAxis = Axis(u, -BrushFace.Dot(anchor, u) / s, s),
                VAxis = Axis(v, -BrushFace.Dot(anchor, v) / s, s),
            };
            if (face.Displacement is { } disp)
                side.Displacement = VmfDisplacement.From(face, disp);
            solid.Sides.Add(side);
        }

        return solid;
    }

    /// <summary>The world-aligned texture axes for a face of normal <paramref name="n"/>: <see cref="VmfMap.Box"/>'s.</summary>
    public static (Point U, Point V) Axes(Point n)
    {
        float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        if (az >= ax && az >= ay)
            return (new(1, 0, 0), new(0, -1, 0));
        return ax >= ay ? (new(0, 1, 0), new(0, 0, -1)) : (new(1, 0, 0), new(0, 0, -1));
    }

    private static string Axis(Point a, float offset, float scale) =>
        $"[{Point.Fmt(a.X)} {Point.Fmt(a.Y)} {Point.Fmt(a.Z)} {Point.Fmt(offset == 0 ? 0 : offset)}] {Point.Fmt(scale)}";

    //-------------------------------------------------------------------------
    // Shapes. Every one takes whole-unit corners; the polygons are wound
    // counter-clockwise from outside.
    //-------------------------------------------------------------------------

    /// <summary>An axis-aligned box, its top face optionally another surface.</summary>
    public static Brush Box(Point mins, Point maxs, Surface surface, Surface? top = null)
    {
        float x0 = mins.X, y0 = mins.Y, z0 = mins.Z, x1 = maxs.X, y1 = maxs.Y, z1 = maxs.Z;
        if (x1 <= x0 || y1 <= y0 || z1 <= z0)
            throw new ArgumentException($"a box ({mins})-({maxs}) has no volume");
        return new(
        [
            new([new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)], top ?? surface),
            new([new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0)], surface),
            new([new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1)], surface),
            new([new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0)], surface),
            new([new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0)], surface),
            new([new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1)], surface),
        ], surface);
    }

    /// <summary>
    /// A wedge (a ramp): the box's footprint, its top a slope from
    /// <paramref name="mins"/>.Z at the low end up to <paramref name="maxs"/>.Z
    /// at the end toward <paramref name="rises"/>. Five faces.
    /// </summary>
    public static Brush Wedge(Point mins, Point maxs, Compass rises, Surface surface, Surface? slope = null)
    {
        // Built rising east, then turned: the footprint is turned about its centre.
        float lx = rises is Compass.East or Compass.West ? maxs.X - mins.X : maxs.Y - mins.Y;
        float ly = rises is Compass.East or Compass.West ? maxs.Y - mins.Y : maxs.X - mins.X;
        float h = maxs.Z - mins.Z;
        if (lx <= 0 || ly <= 0 || h <= 0)
            throw new ArgumentException($"a wedge ({mins})-({maxs}) has no volume");
        Point[] bottom = [new(0, 0, 0), new(0, ly, 0), new(lx, ly, 0), new(lx, 0, 0)];
        Point hiA = new(lx, 0, h), hiB = new(lx, ly, h);
        BrushFace[] faces =
        [
            new([bottom[0], bottom[1], bottom[2], bottom[3]], surface),
            new([bottom[3], bottom[2], hiB, hiA], surface),
            new([bottom[0], hiA, hiB, bottom[1]], slope ?? surface),
            new([bottom[0], bottom[3], hiA], surface),
            new([bottom[1], hiB, bottom[2]], surface),
        ];
        return Turned(faces, rises, lx, ly, mins, surface);
    }

    /// <summary>
    /// A flight of stairs filling the box: <paramref name="steps"/> boxes, each
    /// a step higher, climbing toward <paramref name="rises"/>. Each step's
    /// rise is the box's height over the steps (keep it at 18 or under to
    /// walk it).
    /// </summary>
    public static IReadOnlyList<Brush> Stairs(Point mins, Point maxs, Compass rises, int steps, Surface surface)
    {
        if (steps < 1)
            throw new ArgumentOutOfRangeException(nameof(steps), steps, "a flight has a step");
        float h = maxs.Z - mins.Z, rise = h / steps;
        float run = (rises is Compass.East or Compass.West ? maxs.X - mins.X : maxs.Y - mins.Y) / steps;
        if (rise != MathF.Round(rise) || run != MathF.Round(run))
            throw new ArgumentException($"stairs ({mins})-({maxs}) in {steps} steps are not whole units ({rise} rise, {run} run)");
        var list = new List<Brush>(steps);
        for (int i = 0; i < steps; i++)
        {
            // Step i runs from its start to the far end, so the flight is solid underneath.
            float top = mins.Z + rise * (i + 1);
            float a = run * i;
            list.Add(rises switch
            {
                Compass.East => Box(new(mins.X + a, mins.Y, mins.Z), new(maxs.X, maxs.Y, top), surface),
                Compass.West => Box(new(mins.X, mins.Y, mins.Z), new(maxs.X - a, maxs.Y, top), surface),
                Compass.North => Box(new(mins.X, mins.Y + a, mins.Z), new(maxs.X, maxs.Y, top), surface),
                _ => Box(new(mins.X, mins.Y, mins.Z), new(maxs.X, maxs.Y - a, top), surface),
            });
        }

        return list;
    }

    /// <summary>
    /// An upright n-sided prism, a round pillar for n of 8 or more: its
    /// corners on the circle of <paramref name="radius"/> round
    /// (<paramref name="centreX"/>, <paramref name="centreY"/>), rounded to
    /// whole units, the first at <paramref name="startDegrees"/>.
    /// </summary>
    public static Brush Prism(float centreX, float centreY, float radius, int sides, float z0, float z1, Surface surface,
                              float startDegrees = 0, Surface? top = null)
    {
        if (sides < 3)
            throw new ArgumentOutOfRangeException(nameof(sides), sides, "a prism has three sides at least");
        Point[] ring = [.. Enumerable.Range(0, sides).Select(i => Corner(centreX, centreY, radius, startDegrees + 360f * i / sides, 0))];
        var faces = new List<BrushFace>
        {
            new([.. ring.Select(p => p with { Z = z1 })], top ?? surface),
            new([.. ring.Reverse().Select(p => p with { Z = z0 })], surface),
        };
        for (int i = 0; i < sides; i++)
        {
            Point a = ring[i], b = ring[(i + 1) % sides];
            faces.Add(new([a with { Z = z0 }, b with { Z = z0 }, b with { Z = z1 }, a with { Z = z1 }], surface));
        }

        return new(faces, surface);
    }

    /// <summary>
    /// An upright prism over a convex footprint: <paramref name="footprint"/>
    /// is its corners on the floor plan, counter-clockwise from above, from
    /// <paramref name="z0"/> to <paramref name="z1"/>. A diagonal cut across a
    /// room's corner is a triangle; a rounded corner, a fan of them.
    /// </summary>
    public static Brush Column(IReadOnlyList<(float X, float Y)> footprint, float z0, float z1, Surface surface, Surface? top = null)
    {
        if (footprint.Count < 3)
            throw new ArgumentException("a column's footprint has three corners at least", nameof(footprint));
        if (z1 <= z0)
            throw new ArgumentException($"a column from {z0} to {z1} has no height");
        double area = 0;
        for (int i = 0; i < footprint.Count; i++)
        {
            var (ax, ay) = footprint[i];
            var (bx, by) = footprint[(i + 1) % footprint.Count];
            area += (double)ax * by - (double)bx * ay;
        }

        if (area <= 0)
            throw new ArgumentException("a column's footprint is wound clockwise or has no area; give it counter-clockwise from above", nameof(footprint));
        Point[] ring = [.. footprint.Select(p => new Point(p.X, p.Y, 0))];
        var faces = new List<BrushFace>
        {
            new([.. ring.Select(p => p with { Z = z1 })], top ?? surface),
            new([.. ring.Reverse().Select(p => p with { Z = z0 })], surface),
        };
        for (int i = 0; i < ring.Length; i++)
        {
            Point a = ring[i], b = ring[(i + 1) % ring.Length];
            faces.Add(new([a with { Z = z0 }, b with { Z = z0 }, b with { Z = z1 }, a with { Z = z1 }], surface));
        }

        return new(faces, surface);
    }

    /// <summary>
    /// A spike: an n-sided pyramid whose base is the circle of
    /// <paramref name="radius"/> at <paramref name="zBase"/> and whose tip is
    /// at <paramref name="zTip"/> over the centre (below the base for a
    /// stalactite, above it for a stalagmite).
    /// </summary>
    public static Brush Spike(float centreX, float centreY, float radius, int sides, float zTip, float zBase, Surface surface)
    {
        if (sides < 3 || zTip == zBase)
            throw new ArgumentException("a spike has three sides at least and a height");
        Point[] ring = [.. Enumerable.Range(0, sides).Select(i => Corner(centreX, centreY, radius, 360f * i / sides, zBase))];
        Point tip = new(MathF.Round(centreX), MathF.Round(centreY), zTip);
        bool down = zTip < zBase;
        var faces = new List<BrushFace> { new(down ? ring : [.. ring.Reverse()], surface) };
        for (int i = 0; i < sides; i++)
        {
            Point a = ring[i], b = ring[(i + 1) % sides];
            faces.Add(new(down ? [b, a, tip] : [a, b, tip], surface));
        }

        return new(faces, surface);
    }

    /// <summary>
    /// A round wall: <paramref name="sides"/> segments between the circles of
    /// <paramref name="inner"/> and <paramref name="outer"/> radius, each a
    /// four-sided prism sharing its corners with the next, so the ring is
    /// closed. <paramref name="keep"/> leaves out the segments it refuses (an
    /// opening for a door's arm).
    /// </summary>
    public static IReadOnlyList<Brush> RingWall(float centreX, float centreY, float inner, float outer, int sides, float z0, float z1,
                                                Surface surface, Func<int, bool>? keep = null, float startDegrees = 0)
    {
        if (inner <= 0 || outer <= inner)
            throw new ArgumentException($"a ring wall needs 0 < inner < outer ({inner}, {outer})");
        var list = new List<Brush>();
        for (int i = 0; i < sides; i++)
        {
            if (keep is not null && !keep(i))
                continue;
            float a0 = startDegrees + 360f * i / sides, a1 = startDegrees + 360f * (i + 1) / sides;
            Point i0 = Corner(centreX, centreY, inner, a0, 0), i1 = Corner(centreX, centreY, inner, a1, 0);
            Point o0 = Corner(centreX, centreY, outer, a0, 0), o1 = Corner(centreX, centreY, outer, a1, 0);
            Point[] foot = [i0, o0, o1, i1];
            var faces = new List<BrushFace>
            {
                new([.. foot.Select(p => p with { Z = z1 })], surface),
                new([.. foot.Reverse().Select(p => p with { Z = z0 })], surface),
            };
            for (int k = 0; k < 4; k++)
            {
                Point a = foot[k], b = foot[(k + 1) % 4];
                faces.Add(new([a with { Z = z0 }, b with { Z = z0 }, b with { Z = z1 }, a with { Z = z1 }], surface));
            }

            // Each face is wound outward by the footprint's order; the footprint
            // (inner a0, outer a0, outer a1, inner a1) runs counter-clockwise.
            list.Add(new(faces, surface));
        }

        return list;
    }

    /// <summary>
    /// A box whose top face is a displacement: the heights come from
    /// <paramref name="height"/>(u, v), u and v from 0 to 1 across the box
    /// from its low corner along +x and +y.
    /// </summary>
    public static Brush DisplacedBox(Point mins, Point maxs, int power, Func<float, float, float> height, Surface surface, Surface? top = null)
    {
        Brush box = Box(mins, maxs, surface, top);
        var faces = box.Faces.ToArray();
        faces[0] = faces[0] with { Displacement = new Displacement(power, height, new(0, 0, 1)) };
        return new(faces, surface);
    }

    /// <summary>
    /// A box whose bottom face is a displacement hanging down from it (an
    /// uneven cave roof): heights from <paramref name="height"/>(u, v), down.
    /// </summary>
    public static Brush DisplacedUnder(Point mins, Point maxs, int power, Func<float, float, float> height, Surface surface)
    {
        Brush box = Box(mins, maxs, surface);
        var faces = box.Faces.ToArray();
        faces[1] = faces[1] with { Displacement = new Displacement(power, height, new(0, 0, -1)) };
        return new(faces, surface);
    }

    /// <summary>
    /// A box whose face toward <paramref name="facing"/> is a displacement
    /// (a cave wall's relief): heights along that face's outward normal from
    /// <paramref name="height"/>(u, v), u along the face, v up it.
    /// </summary>
    public static Brush DisplacedWall(Point mins, Point maxs, Compass facing, int power, Func<float, float, float> height, Surface surface)
    {
        Brush box = Box(mins, maxs, surface);
        int index = facing switch { Compass.East => 2, Compass.West => 3, Compass.North => 4, _ => 5 };
        var faces = box.Faces.ToArray();
        BrushFace face = faces[index];
        // Rewind the face so its first edge runs along the wall and its last edge up it.
        Point[] p = [.. face.Polygon];
        Point[] ordered = facing switch
        {
            // east (x1): (y0,z0) (y1,z0) (y1,z1) (y0,z1)
            Compass.East => [p[0], p[1], p[2], p[3]],
            // west (x0): polygon (y0,z0) (y0,z1) (y1,z1) (y1,z0) -> start (y1,z0), along to (y0,z0)
            Compass.West => [p[3], p[0], p[1], p[2]],
            // north (y1): (x0,z0) (x0,z1) (x1,z1) (x1,z0) -> start (x1,z0) along to (x0,z0)
            Compass.North => [p[3], p[0], p[1], p[2]],
            // south (y0): (x0,z0) (x1,z0) (x1,z1) (x0,z1)
            _ => [p[0], p[1], p[2], p[3]],
        };
        faces[index] = new BrushFace(ordered, surface, new Displacement(power, height, face.Normal));
        return new(faces, surface);
    }

    private static Point Corner(float cx, float cy, float r, float degrees, float z)
    {
        double a = degrees * Math.PI / 180.0;
        return new((float)Math.Round(cx + r * Math.Cos(a)), (float)Math.Round(cy + r * Math.Sin(a)), z);
    }

    /// <summary>Turns faces built in a local frame rising east (+x) to <paramref name="toward"/> and moves them to <paramref name="mins"/>.</summary>
    private static Brush Turned(BrushFace[] faces, Compass toward, float lx, float ly, Point mins, Surface surface)
    {
        Point Map(Point p) => toward switch
        {
            Compass.East => new(mins.X + p.X, mins.Y + p.Y, mins.Z + p.Z),
            // Rising west: mirror x and y (a half turn keeps the winding).
            Compass.West => new(mins.X + lx - p.X, mins.Y + ly - p.Y, mins.Z + p.Z),
            // Rising north: a quarter turn counter-clockwise, (x, y) -> (ly - y, x).
            Compass.North => new(mins.X + ly - p.Y, mins.Y + p.X, mins.Z + p.Z),
            _ => new(mins.X + p.Y, mins.Y + lx - p.X, mins.Z + p.Z),
        };
        return new([.. faces.Select(f => f with { Polygon = [.. f.Polygon.Select(Map)] })], surface);
    }
}

/// <summary>A displacement as a map file writes it under its side (map-tools' <c>RoomsFeaturesSample.Patch</c> layout).</summary>
public sealed class VmfDisplacement
{
    public required int Power { get; init; }
    public required Point Start { get; init; }
    public required Point Normal { get; init; }
    public required float[,] Distances { get; init; }

    internal static VmfDisplacement From(BrushFace face, Displacement d) =>
        new() { Power = d.Power, Start = face.Polygon[0], Normal = d.Normal, Distances = d.Distances };

    internal void Write(StringBuilder text, string tab)
    {
        int n = (1 << Power) + 1;
        string normal = $"{Point.Fmt(Normal.X)} {Point.Fmt(Normal.Y)} {Point.Fmt(Normal.Z)}";
        text.Append($"{tab}dispinfo\n{tab}{{\n");
        text.Append($"{tab}\t\"power\" \"{Power.ToString(CultureInfo.InvariantCulture)}\"\n");
        text.Append($"{tab}\t\"startposition\" \"[{Start}]\"\n");
        text.Append($"{tab}\t\"flags\" \"0\"\n{tab}\t\"elevation\" \"0\"\n{tab}\t\"subdiv\" \"0\"\n");
        Rows("normals", r => Repeat(normal, n));
        Rows("distances", r => string.Join(' ', Enumerable.Range(0, n).Select(c => Point.Fmt(Distances[r, c]))));
        Rows("offsets", r => Repeat("0 0 0", n));
        Rows("offset_normals", r => Repeat(normal, n));
        Rows("alphas", r => Repeat("0", n));
        Rows("triangle_tags", r => Repeat("9", 2 * (n - 1)), n - 1);
        text.Append($"{tab}\tallowed_verts\n{tab}\t{{\n{tab}\t\t\"10\" \"{Repeat("-1", 10)}\"\n{tab}\t}}\n");
        text.Append($"{tab}}}\n");

        void Rows(string name, Func<int, string> row, int count = -1)
        {
            text.Append($"{tab}\t{name}\n{tab}\t{{\n");
            for (int r = 0; r < (count < 0 ? n : count); r++)
                text.Append($"{tab}\t\t\"row{r.ToString(CultureInfo.InvariantCulture)}\" \"{row(r)}\"\n");
            text.Append($"{tab}\t}}\n");
        }
    }

    private static string Repeat(string s, int count) => string.Join(' ', Enumerable.Repeat(s, count));
}
