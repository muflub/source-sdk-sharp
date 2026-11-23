using System.Buffers.Binary;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// The lumps of a map's BSP tree that say which brushes a brush model is made of: planes (1),
/// nodes (5), leaves (10, with the lump's version: 1 is 32 bytes a leaf, 0 is 56), models (14),
/// leaf brushes (17), brushes (18) and brush sides (19), each as the file holds it, uncompressed.
/// </summary>
public readonly record struct BspTreeLumps(
    ReadOnlyMemory<byte> Planes,
    ReadOnlyMemory<byte> Nodes,
    ReadOnlyMemory<byte> Leaves,
    int LeafVersion,
    ReadOnlyMemory<byte> Models,
    ReadOnlyMemory<byte> LeafBrushes,
    ReadOnlyMemory<byte> Brushes,
    ReadOnlyMemory<byte> BrushSides);

/// <summary>
/// A brush model's volume as boxes, one a brush, for a trigger made of several brushes
/// (plan_bots_teams_tf2_part_g, Q-R3 of 2026-11-13). A model's bounds are the box around all of
/// its brushes: jump_academy2_rc9a's 264 <c>trigger_teleport</c>s hold 910.7 G cubic units by
/// their bounds and 145.2 G by their brushes, and one course's fail floor, 2.2 % of its bounds,
/// covered the course's capture area.
/// <list type="bullet">
/// <item><b>The brushes</b> are those of the leaves under the model's head node, each once.</item>
/// <item><b>A brush's box</b> is where its axis-aligned sides meet. A brush the compiler leaves
/// with only six sides is that box exactly; a brush with a sloped side is the box around it
/// (the compiler gives every brush its axis-aligned bevels, so none is unbounded).</item>
/// <item><b>Where it is:</b> a model's brushes are stored about the entity's <c>origin</c>, so
/// each box is moved by it.</item>
/// </list>
/// </summary>
public static class BrushModelBoxes
{
    private const int PlaneBytes = 20;
    private const int NodeBytes = 32;
    private const int ModelBytes = 48;
    private const int BrushBytes = 12;
    private const int SideBytes = 8;

    /// <summary>The boxes of model <paramref name="model"/>'s brushes, about <paramref name="origin"/>; none for a model the lumps do not hold.</summary>
    public static IReadOnlyList<HurtVolume> Of(in BspTreeLumps lumps, int model, AiVector origin)
    {
        ReadOnlySpan<byte> models = lumps.Models.Span, nodes = lumps.Nodes.Span, leaves = lumps.Leaves.Span;
        ReadOnlySpan<byte> leafBrushes = lumps.LeafBrushes.Span;
        if (model < 0 || (model + 1) * ModelBytes > models.Length)
            return [];
        int leafBytes = lumps.LeafVersion == 0 ? 56 : 32;
        const int brushAt = 24; // first leaf brush and count: the same place in both versions (version 0 adds its light cube after)
        var brushes = new SortedSet<int>();
        var stack = new Stack<int>();
        stack.Push(BinaryPrimitives.ReadInt32LittleEndian(models[(model * ModelBytes + 36)..]));
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (n < 0)
            {
                int leaf = -n - 1;
                if ((leaf + 1) * leafBytes > leaves.Length)
                    continue;
                ReadOnlySpan<byte> l = leaves.Slice(leaf * leafBytes, leafBytes);
                int first = BinaryPrimitives.ReadUInt16LittleEndian(l[brushAt..]), count = BinaryPrimitives.ReadUInt16LittleEndian(l[(brushAt + 2)..]);
                for (int i = first; i < first + count && (i + 1) * 2 <= leafBrushes.Length; i++)
                    brushes.Add(BinaryPrimitives.ReadUInt16LittleEndian(leafBrushes[(i * 2)..]));
                continue;
            }

            if ((n + 1) * NodeBytes > nodes.Length)
                continue;
            ReadOnlySpan<byte> node = nodes.Slice(n * NodeBytes, NodeBytes);
            stack.Push(BinaryPrimitives.ReadInt32LittleEndian(node[4..]));
            stack.Push(BinaryPrimitives.ReadInt32LittleEndian(node[8..]));
        }

        var boxes = new List<HurtVolume>(brushes.Count);
        foreach (int b in brushes)
        {
            if (Box(lumps, b) is { } box)
                boxes.Add(new HurtVolume(box.Min + origin, box.Max + origin));
        }

        return boxes;
    }

    /// <summary>Where brush <paramref name="brush"/>'s axis-aligned sides meet; null for one without a side on each face of a box.</summary>
    public static HurtVolume? Box(in BspTreeLumps lumps, int brush)
    {
        ReadOnlySpan<byte> all = lumps.Brushes.Span, sides = lumps.BrushSides.Span, planes = lumps.Planes.Span;
        if (brush < 0 || (brush + 1) * BrushBytes > all.Length)
            return null;
        int first = BinaryPrimitives.ReadInt32LittleEndian(all[(brush * BrushBytes)..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(all[(brush * BrushBytes + 4)..]);
        Span<float> lo = [float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity];
        Span<float> hi = [float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity];
        for (int s = first; s < first + count && (s + 1) * SideBytes <= sides.Length; s++)
        {
            int plane = BinaryPrimitives.ReadUInt16LittleEndian(sides[(s * SideBytes)..]);
            if ((plane + 1) * PlaneBytes > planes.Length)
                continue;
            ReadOnlySpan<byte> p = planes.Slice(plane * PlaneBytes, PlaneBytes);
            float dist = BinaryPrimitives.ReadSingleLittleEndian(p[12..]);
            for (int axis = 0; axis < 3; axis++)
            {
                float n = BinaryPrimitives.ReadSingleLittleEndian(p[(axis * 4)..]);
                if (n == 1f)
                    hi[axis] = MathF.Min(hi[axis], dist);
                else if (n == -1f)
                    lo[axis] = MathF.Max(lo[axis], -dist);
            }
        }

        for (int axis = 0; axis < 3; axis++)
        {
            if (!float.IsFinite(lo[axis]) || !float.IsFinite(hi[axis]) || hi[axis] <= lo[axis])
                return null;
        }

        return new HurtVolume(new AiVector(lo[0], lo[1], lo[2]), new AiVector(hi[0], hi[1], hi[2]));
    }
}
