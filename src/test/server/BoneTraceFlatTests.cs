using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared.Bones;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// The flat ray and trace of IManagedBones::TestHitboxes (plan
/// physics_bones_native_zero step 1.1): the two declarations of its indices
/// held equal, and the codec's round trip.
/// </summary>
public class BoneTraceFlatTests
{
    private static Dictionary<string, int> NativeEnum()
    {
        string path = RepoLayout.Repo("src", "shared", "native", "standalone_bone_trace.h");
        string text = File.ReadAllText(path);
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(text, @"\b(SS_BONE_(?:RAY|TRACE)_\w+)\s*=\s*(\d+)\s*,"))
            found.Add(m.Groups[1].Value, int.Parse(m.Groups[2].Value));

        return found;
    }

    // SS_BONE_TRACE_PLANE_NORMAL_X -> TracePlaneNormalX
    private static string ManagedName(string native)
    {
        string[] words = native["SS_BONE_".Length..].Split('_');
        string name = string.Empty;

        foreach (string w in words)
            name += w.Length == 1 ? w : w[0] + w[1..].ToLowerInvariant();

        return name;
    }

    [Fact]
    public void EveryNativeIndexHasTheSameManagedIndex()
    {
        Dictionary<string, int> native = NativeEnum();
        var managed = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (FieldInfo f in typeof(BoneTraceFlat).GetFields(BindingFlags.Public | BindingFlags.Static))
            managed.Add(f.Name, (int)f.GetRawConstantValue()!);

        // The positive arm: the header was read and named every field.
        Assert.Equal(24, native.Count);

        foreach (var (name, value) in native)
        {
            string twin = ManagedName(name);
            Assert.True(managed.TryGetValue(twin, out int mine), twin + " is missing from BoneTraceFlat");
            Assert.Equal(value, mine);
        }

        Assert.Equal(native.Count, managed.Count);
    }

    [Fact]
    public void ATraceRoundTripsThroughTheCodec()
    {
        var tr = new BoneTrace
        {
            StartPos = new NVector(1f, 2f, 3f),
            EndPos = new NVector(4f, 5f, 6f),
            PlaneNormal = new NVector(0f, 0f, 1f),
            PlaneDist = 7f,
            Fraction = 0.25f,
            PlaneType = 2,
            Contents = 0x40000000,
            AllSolid = false,
            StartSolid = true,
            HitGroup = 1,
            HitBox = 3,
            PhysicsBone = 4,
            SurfaceFlags = BoneSetupTrace.SurfHitbox,
            SurfaceProps = 42,
            SurfaceName = BoneSetupTrace.StudioSurfaceName,
        };

        float[] floats = new float[BoneTraceFlat.TraceFloats];
        int[] ints = new int[BoneTraceFlat.TraceInts];
        BoneTraceCodec.WriteTrace(tr, floats, ints);
        BoneTrace back = BoneTraceCodec.ReadTrace(floats, ints);

        Assert.Equal((tr.StartPos, tr.EndPos, tr.PlaneNormal, tr.PlaneDist, tr.Fraction),
                     (back.StartPos, back.EndPos, back.PlaneNormal, back.PlaneDist, back.Fraction));
        Assert.Equal((tr.PlaneType, tr.Contents, tr.StartSolid, tr.HitGroup, tr.HitBox, tr.PhysicsBone, tr.SurfaceFlags, tr.SurfaceProps),
                     (back.PlaneType, back.Contents, back.StartSolid, back.HitGroup, back.HitBox, back.PhysicsBone, back.SurfaceFlags, back.SurfaceProps));
        Assert.Equal(1, ints[BoneTraceFlat.TraceSurfaceIsStudio]);
    }

    [Fact]
    public void ARayReadsItsFourVectorsAndTwoFlags()
    {
        float[] flat = new float[BoneTraceFlat.RayFloats];
        flat[BoneTraceFlat.RayStartX] = 1f;
        flat[BoneTraceFlat.RayDeltaX + 1] = 2f;
        flat[BoneTraceFlat.RayStartOffsetX + 2] = 3f;
        flat[BoneTraceFlat.RayExtentsX] = 4f;
        flat[BoneTraceFlat.RayIsRay] = 1f;

        BoneRay ray = BoneTraceCodec.ReadRay(flat);

        Assert.Equal((1f, 2f, 3f, 4f, true, false),
                     (ray.Start.X, ray.Delta.Y, ray.StartOffset.Z, ray.Extents.X, ray.IsRay, ray.IsSwept));
    }

    [Fact]
    public void AnUntestedTraceIsNotWrittenBack()
    {
        // No block: not a standalone entity, nothing tested, nothing written.
        var skeletons = new ServerSkeletons(new NoBlocks(), new StudioModels(new NoMemory()), new NoClock(), new RecordingLog(),
                                            new DefaultForwarders.NoSurfaceProps());
        float[] floats = { 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f };
        int[] ints = new int[BoneTraceFlat.TraceInts];

        Assert.False(skeletons.TestHitboxes(new EntityHandle(1u), Span<byte>.Empty, new float[BoneTraceFlat.RayFloats],
                                            1, floats, ints));
        Assert.All(floats, f => Assert.Equal(9f, f));
    }

    private sealed class NoBlocks : IStandaloneBlocks
    {
        public Span<byte> Block(EntityHandle entity) => Span<byte>.Empty;
    }

    private sealed class NoMemory : IStudioMemory
    {
        public byte[]? ModelImage(int modelIndex) => null;
        public byte[]? ModelAnimBlock(int modelIndex, int block) => null;
        public byte[]? IncludeImage(int modelIndex, string includeName) => null;
        public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block) => null;
    }

    private sealed class NoClock : IGameClock
    {
        public float Now => 0f;
    }
}
