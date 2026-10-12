//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary><c>CParticleEffectBinding</c>'s <c>FLAGS_</c> (particlemgr.h), bit for bit.</summary>
[Flags]
public enum ParticleBindingFlags
{
    None = 0,
    Remove = 1 << 0,
    RemovalInProgress = 1 << 1,
    NeedsBBoxUpdate = 1 << 2,
    AutoUpdateBBox = 1 << 3,
    AlwaysSimulate = 1 << 4,
    Drawn = 1 << 5,
    DrawnPrevFrame = 1 << 6,
    CameraSpace = 1 << 7,
    DrawThruLeafSystem = 1 << 8,
    DrawBeforeViewModel = 1 << 9,
    AutoApplyLocalTransform = 1 << 10,
    FirstFrame = 1 << 11,
    NewParticleSystem = 1 << 12,
}

/// <summary>One particle in a binding's material list: managed's id and the position the manager holds for it.</summary>
public record struct BoundParticle(int Id, Vector Position);

/// <summary>
/// particlemgr.cpp's <c>CParticleEffectBinding</c>, the decisions half: the
/// flags, the world box and its change detection, the per-material particle
/// lists in the order the manager keeps them, the sort each draw leaves
/// behind, and the frame code. Drawing, the leaf system and the matrices are
/// native's; this decides what they are told.
/// </summary>
public sealed class ParticleBinding
{
    /// <summary>particlemgr.cpp's BBOX_UPDATE_EVERY_N and BUCKET_SORT_EVERY_N.</summary>
    public const int BBoxUpdateEveryN = 8;
    public const int BucketSortEveryN = 8;

    private const int NumBuckets = 32;

    // A 3x4 matrix, row-major, as matrix3x4_t.
    private readonly float[] _local = new float[12];

    /// <summary>The constructor's state: a ±50 box, cull radius 0, and its seven flags.</summary>
    public ParticleBinding()
    {
        Flags = ParticleBindingFlags.AutoUpdateBBox | ParticleBindingFlags.FirstFrame | ParticleBindingFlags.NeedsBBoxUpdate
              | ParticleBindingFlags.AlwaysSimulate | ParticleBindingFlags.CameraSpace | ParticleBindingFlags.DrawThruLeafSystem
              | ParticleBindingFlags.AutoApplyLocalTransform;
        Min = new Vector(-50, -50, -50);
        Max = new Vector(50, 50, 50);
        LastMin = Min;
        LastMax = Max;
        SetIdentity();
    }

    public ParticleBindingFlags Flags { get; set; }

    public Vector Min { get; set; }

    public Vector Max { get; set; }

    public Vector LastMin { get; set; }

    public Vector LastMax { get; set; }

    public float CullRadius { get; private set; }

    /// <summary><c>m_nActiveParticles</c> (an unsigned short in stock).</summary>
    public ushort ActiveParticles { get; private set; }

    /// <summary><c>m_FrameCode</c>.</summary>
    public ushort FrameCode { get; set; }

    /// <summary><c>m_UpdateBBoxCounter</c>.</summary>
    public ushort UpdateBBoxCounter { get; set; }

    public bool LocalSpaceTransformIdentity { get; private set; } = true;

    /// <summary>The local space transform's twelve floats (matrix3x4_t, row-major).</summary>
    public ReadOnlySpan<float> LocalSpaceTransform => _local;

    /// <summary><c>m_Materials</c>, in the order they were added: each one's particles in list order.</summary>
    public List<List<BoundParticle>> Materials { get; } = [];

    public bool Has(ParticleBindingFlags flag) => (Flags & flag) != 0;

    public void Set(ParticleBindingFlags flag, bool on) => Flags = on ? Flags | flag : Flags & ~flag;

    /// <summary>SetDrawThruLeafSystem: whether the leaf system draws it (the default).</summary>
    public void SetDrawThruLeafSystem(bool draw) => Set(ParticleBindingFlags.DrawThruLeafSystem, draw);

    /// <summary>SetDrawBeforeViewModel: drawn before the view model, and then never through the leaf system.</summary>
    public void SetDrawBeforeViewModel(bool draw)
    {
        if (draw)
            Set(ParticleBindingFlags.DrawThruLeafSystem, false);
        Set(ParticleBindingFlags.DrawBeforeViewModel, draw);
    }

    /// <summary>
    /// SetParticleCullRadius: true when it changed, which is when stock told
    /// the leaf system the renderable changed.
    /// </summary>
    public bool SetParticleCullRadius(float radius)
    {
        if (CullRadius == radius)
            return false;
        CullRadius = radius;
        return true;
    }

    /// <summary>The material list for <paramref name="material"/>, added at the end the first time it is asked (GetEffectMaterial).</summary>
    public int Material(int material)
    {
        while (Materials.Count <= material)
            Materials.Add([]);
        return material;
    }

    /// <summary>AddParticle: linked at the head of its material's list.</summary>
    public void AddParticle(int material, int id, Vector position)
    {
        Materials[Material(material)].Insert(0, new BoundParticle(id, position));
        ++ActiveParticles;
    }

    /// <summary>RemoveParticle: false when no such particle is bound.</summary>
    public bool RemoveParticle(int id)
    {
        foreach (List<BoundParticle> list in Materials)
        {
            int at = list.FindIndex(p => p.Id == id);
            if (at < 0)
                continue;
            list.RemoveAt(at);
            --ActiveParticles;
            return true;
        }
        return false;
    }

    /// <summary>SetBBox.</summary>
    public void SetBBox(Vector min, Vector max, bool disableAutoUpdate)
    {
        Min = min;
        Max = max;
        if (disableAutoUpdate)
            Set(ParticleBindingFlags.AutoUpdateBBox, false);
    }

    /// <summary>SetLocalSpaceTransform: the matrix, and whether it is the identity (VMatrix::IsIdentity after CopyFrom3x4).</summary>
    public void SetLocalSpaceTransform(ReadOnlySpan<float> matrix3x4)
    {
        matrix3x4[..12].CopyTo(_local);
        LocalSpaceTransformIdentity =
            _local[0] == 1f && _local[1] == 0f && _local[2] == 0f && _local[3] == 0f &&
            _local[4] == 0f && _local[5] == 1f && _local[6] == 0f && _local[7] == 0f &&
            _local[8] == 0f && _local[9] == 0f && _local[10] == 1f && _local[11] == 0f;
    }

    private void SetIdentity()
    {
        Array.Clear(_local);
        _local[0] = _local[5] = _local[10] = 1f;
        LocalSpaceTransformIdentity = true;
    }

    /// <summary>
    /// RecalculateBoundingBox: every particle's position, into world space; with
    /// none, the box closes on the sort origin and the answer is false.
    /// </summary>
    public bool RecalculateBoundingBox(Vector sortOrigin)
    {
        if (ActiveParticles == 0)
        {
            Max = Min = sortOrigin;
            return false;
        }

        var bbMin = new Vector(1e28f, 1e28f, 1e28f);
        var bbMax = new Vector(-1e28f, -1e28f, -1e28f);
        foreach (List<BoundParticle> list in Materials)
        {
            foreach (BoundParticle p in list)
                Grow(ref bbMin, ref bbMax, p.Position);
        }
        ToWorld(bbMin, bbMax, out Vector min, out Vector max);
        Min = min;
        Max = max;
        return true;
    }

    /// <summary>
    /// SimulateParticles' box half, around the sim's step: the counter, the
    /// full update's draw (stock's <c>random-&gt;RandomInt( 0, 8 )</c>, asked only
    /// once the counter reaches eight), and true when this step recomputes the
    /// box. The step itself is the caller's; <see cref="EndBBoxUpdate"/> closes it.
    /// </summary>
    public bool BeginBBoxUpdate(Func<int, int, int> randomInt)
    {
        bool full = false;
        ++UpdateBBoxCounter;
        if ((UpdateBBoxCounter >= BBoxUpdateEveryN && randomInt(0, BBoxUpdateEveryN) == 0) || UpdateBBoxCounter >= 2 * BBoxUpdateEveryN)
        {
            full = true;
            UpdateBBoxCounter = 0;
        }
        return full;
    }

    /// <summary>
    /// The full update's box after the step: BBoxCalcStart, GrowBBoxFromParticlePositions
    /// over each material, BBoxCalcEnd. Nothing when the box is set by hand.
    /// Answers whether any particle set it.
    /// </summary>
    public bool EndBBoxUpdate(Vector sortOrigin)
    {
        if (!Has(ParticleBindingFlags.AutoUpdateBBox))
            return false;

        var bbMin = new Vector(float.MaxValue, float.MaxValue, float.MaxValue);
        var bbMax = new Vector(-float.MaxValue, -float.MaxValue, -float.MaxValue);
        bool set = false;
        foreach (List<BoundParticle> list in Materials)
        {
            foreach (BoundParticle p in list)
            {
                Grow(ref bbMin, ref bbMax, p.Position);
                set = true;
            }
        }

        ToWorld(bbMin, bbMax, out Vector min, out Vector max);
        if (set)
        {
            Min = min;
            Max = max;
        }
        else
        {
            Min = Max = sortOrigin;
        }
        return set;
    }

    /// <summary>
    /// DetectChanges: true when the box left the last one by more than the
    /// buffer (cl_particleeffect_aabb_buffer), which is when stock tells the leaf
    /// system; the last box is then the box padded by the buffer.
    /// </summary>
    public bool DetectChanges(float buffer)
    {
        float extra = buffer * 1.3f;
        Vector min = Min, max = Max, lastMin = LastMin, lastMax = LastMax;
        if (min.X < lastMin.X ||
            min.Y < lastMin.Y ||
            min.Z < lastMin.Z ||
            min.X > (lastMin.X + extra) ||
            min.Y > (lastMin.Y + extra) ||
            min.Z > (lastMin.Z + extra) ||
            max.X > lastMax.X ||
            max.Y > lastMax.Y ||
            max.Z > lastMax.Z ||
            max.X < (lastMax.X - extra) ||
            max.Y < (lastMax.Y - extra) ||
            max.Z < (lastMax.Z - extra))
        {
            LastMin = new Vector(min.X - buffer, min.Y - buffer, min.Z - buffer);
            LastMax = new Vector(max.X + buffer, max.Y + buffer, max.Z + buffer);
            return true;
        }
        return false;
    }

    /// <summary>
    /// DrawModel's frame time: the frame's on the first draw since the manager's
    /// frame code moved, zero on any later draw this frame.
    /// </summary>
    public float DrawFrameTime(ushort managerFrameCode, float frameTime)
    {
        if (FrameCode == managerFrameCode)
            return 0f;
        FrameCode = managerFrameCode;
        return frameTime;
    }

    /// <summary>DrawModel's sort choice: a bucket sort about once in nine draws.</summary>
    public static bool ChooseBucketSort(Func<int, int, int> randomInt) => randomInt(0, BucketSortEveryN) == 0;

    /// <summary>
    /// What a material's draw leaves its list in, given each particle's sort key
    /// in list order (the keys the executor handed GetNext): one pass of the
    /// incremental swap (CParticleRenderIterator::GetNext), or the bucket sort
    /// (CParticleEffectBinding::DoBucketSort).
    /// </summary>
    public void SortAfterDraw(int material, ReadOnlySpan<float> keys, bool bucketSort)
    {
        List<BoundParticle> list = Materials[material];
        if (bucketSort)
        {
            BucketSort(list, keys);
            return;
        }

        float prevZ = 0f;
        int n = Math.Min(list.Count, keys.Length);
        for (int i = 0; i < n; i++)
        {
            if (i != 0 && prevZ > keys[i])
                (list[i - 1], list[i]) = (list[i], list[i - 1]);
            else
                prevZ = keys[i];
        }
    }

    private static void BucketSort(List<BoundParticle> list, ReadOnlySpan<float> keys)
    {
        float minZ = 1e24f, maxZ = -1e24f;
        foreach (float k in keys)
        {
            minZ = k < minZ ? k : minZ;
            maxZ = k > maxZ ? k : maxZ;
        }

        var buckets = new List<BoundParticle>[NumBuckets];
        for (int b = 0; b < NumBuckets; b++)
            buckets[b] = [];

        int n = Math.Min(list.Count, keys.Length);
        for (int i = 0; i < n; i++)
        {
            float percent = maxZ == minZ ? 0f : (keys[i] - minZ) / (maxZ - minZ);
            int add = (int)(percent * (NumBuckets - 0.0001f));
            add = NumBuckets - add - 1;
            buckets[add].Insert(0, list[i]);
        }

        // Each bucket's particles go back in at the list's head, bucket by bucket.
        var rest = list.GetRange(n, list.Count - n);
        list.Clear();
        for (int b = 0; b < NumBuckets; b++)
        {
            foreach (BoundParticle p in buckets[b])
                list.Insert(0, p);
        }
        list.AddRange(rest);
    }

    /// <summary>PostRender: whether it was drawn becomes last frame's, and its first frame is over.</summary>
    public void PostRender()
    {
        Set(ParticleBindingFlags.DrawnPrevFrame, Has(ParticleBindingFlags.Drawn));
        Set(ParticleBindingFlags.FirstFrame, false);
    }

    private static void Grow(ref Vector bbMin, ref Vector bbMax, Vector p)
    {
        // VectorMin / VectorMax: MIN(a, b) is (a < b ? a : b).
        bbMin = new Vector(bbMin.X < p.X ? bbMin.X : p.X, bbMin.Y < p.Y ? bbMin.Y : p.Y, bbMin.Z < p.Z ? bbMin.Z : p.Z);
        bbMax = new Vector(bbMax.X > p.X ? bbMax.X : p.X, bbMax.Y > p.Y ? bbMax.Y : p.Y, bbMax.Z > p.Z ? bbMax.Z : p.Z);
    }

    // The box into world space: as it is under the identity, else TransformAABB.
    private void ToWorld(Vector bbMin, Vector bbMax, out Vector min, out Vector max)
    {
        if (LocalSpaceTransformIdentity)
        {
            min = bbMin;
            max = bbMax;
            return;
        }

        float[] t = _local;
        var c = new Vector((bbMin.X + bbMax.X) * 0.5f, (bbMin.Y + bbMax.Y) * 0.5f, (bbMin.Z + bbMax.Z) * 0.5f);
        var e = new Vector(bbMax.X - c.X, bbMax.Y - c.Y, bbMax.Z - c.Z);
        var wc = new Vector(c.X * t[0] + c.Y * t[1] + c.Z * t[2] + t[3],
                            c.X * t[4] + c.Y * t[5] + c.Z * t[6] + t[7],
                            c.X * t[8] + c.Y * t[9] + c.Z * t[10] + t[11]);
        var we = new Vector(MathF.Abs(e.X * t[0]) + MathF.Abs(e.Y * t[1]) + MathF.Abs(e.Z * t[2]),
                            MathF.Abs(e.X * t[4]) + MathF.Abs(e.Y * t[5]) + MathF.Abs(e.Z * t[6]),
                            MathF.Abs(e.X * t[8]) + MathF.Abs(e.Y * t[9]) + MathF.Abs(e.Z * t[10]));
        min = new Vector(wc.X - we.X, wc.Y - we.Y, wc.Z - we.Z);
        max = new Vector(wc.X + we.X, wc.Y + we.Y, wc.Z + we.Z);
    }
}
