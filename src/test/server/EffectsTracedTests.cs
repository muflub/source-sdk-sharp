using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: the two traced effects, managed. PaintDecal and
/// BubbleEffect as SourceSharp_PaintDecal and SourceSharp_BubbleEffect answered
/// them (stock's CDecal / CProjectedDecal and UTIL_Bubbles behind them), over a
/// recording effects world and a recording temp entity sink. One behaviour per
/// fact.
/// </summary>
public class EffectsTracedTests
{
    // bspflags.h
    private const uint MaskSolid = 0x200400Bu;
    private const uint MaskSolidBrushOnly = 0x400Bu;
    private const uint MaskWater = 0x4030u;

    private static readonly EntityHandle Door = new(40u | (3u << 12));
    private static readonly EntityHandle World = new(0u | (1u << 12));

    private sealed class Sent(string member, TeFilter filter, object?[] args)
    {
        public string Member { get; } = member;
        public TeFilter Filter { get; } = filter;
        public object?[] Args { get; } = args;
    }

    private sealed class Temps : TempEntsDouble
    {
        public readonly List<Sent> Calls = [];

        public override void BSPDecal(TeFilter filter, float delay, Vector position, int entity, int index)
            => Calls.Add(new("BSPDecal", filter, [position, entity, index, delay]));

        public override void ProjectDecal(TeFilter filter, float delay, Vector position, QAngle angles,
                                          float distance, int index)
            => Calls.Add(new("ProjectDecal", filter, [position, angles, distance, index, delay]));

        public override void Bubbles(TeFilter filter, float delay, Vector mins, Vector maxs, float height,
                                     int modelIndex, int count, float speed)
            => Calls.Add(new("Bubbles", filter, [mins, maxs, height, modelIndex, count, speed, delay]));

        public override void BubbleTrail(TeFilter filter, float delay, Vector from, Vector to, float waterZ,
                                         int modelIndex, int count, float speed)
            => Calls.Add(new("BubbleTrail", filter, [from, to, waterZ, modelIndex, count, speed, delay]));
    }

    private sealed class World_ : IEffectsWorld
    {
        public readonly List<(Vector Start, Vector End, uint Mask)> Traces = [];
        public readonly List<(Vector Position, int Decal, int Entity, int Model, bool Low)> Statics = [];
        public bool Traceable = true;
        public EntityHandle Hit = EntityHandle.None;
        public float Fraction = 1f;
        public Vector EndPos;
        public int Sprite = 17;
        public readonly Dictionary<EntityHandle, int> Indices = [];
        public readonly Dictionary<EntityHandle, byte[]> Blocks = [];

        /// <summary>Where a trace ends, when a fact answers per line (a water surface below its start).</summary>
        public Func<Vector, Vector, Vector>? EndFor;

        public ServerTrace? Trace(Vector start, Vector end, uint mask)
        {
            Traces.Add((start, end, mask));
            Vector endPos = EndFor?.Invoke(start, end) ?? EndPos;
            return Traceable ? new ServerTrace(new EngineGameTrace { Fraction = Fraction, EndPos = endPos }, Hit) : null;
        }

        public void StaticDecal(Vector originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowPriority)
            => Statics.Add((originInEntitySpace, decalIndex, entityIndex, modelIndex, lowPriority));

        public int PrecacheModel(string name) => name == "sprites/bubble.vmt" ? Sprite : -9;

        public int EntityIndex(EntityHandle entity) => Indices.TryGetValue(entity, out int i) ? i : -1;

        public Span<byte> Block(EntityHandle entity) => Blocks.TryGetValue(entity, out byte[]? b) ? b : Span<byte>.Empty;

        public (string ClassName, string Name) Names(EntityHandle entity) => ("func_door", "door1");
    }

    private static byte[] BlockAt(Vector origin, Vector mins, Vector maxs, int modelIndex, Transform frame)
    {
        var block = new byte[Marshal.SizeOf<EngineBlock>()];
        MemoryMarshal.Write(block.AsSpan(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Origin))), in origin);
        MemoryMarshal.Write(block.AsSpan(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Mins))), in mins);
        MemoryMarshal.Write(block.AsSpan(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Maxs))), in maxs);
        MemoryMarshal.Write(block.AsSpan(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.ModelIndex))), in modelIndex);
        MemoryMarshal.Write(block.AsSpan(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.WorldTransform))), in frame);
        return block;
    }

    private static (EffectsOverTempEnts Effects, Temps Temps, World_ World, List<string> Warnings) Rig()
    {
        var engine = TestServices.Fake();
        var temps = new Temps();
        var world = new World_();
        var warnings = new List<string>();
        return (new EffectsOverTempEnts(engine, temps, engine, engine, world, warnings.Add), temps, world, warnings);
    }

    // ---- PaintDecal ------------------------------------------------------------

    [Fact]
    public void ANegativeTexturePaintsNothing()
    {
        var (effects, temps, world, _) = Rig();
        Assert.False(effects.PaintDecal(-1, Vector.Zero, null, 0f, permanent: true, lowPriority: false));
        Assert.Empty(temps.Calls);
        Assert.Empty(world.Traces);
    }

    [Fact]
    public void TheProjectedFormSendsAProjectedDecalToEveryone()
    {
        var (effects, temps, world, _) = Rig();

        Assert.True(effects.PaintDecal(4, new Vector(1, 2, 3), new Vector(10, 90, 0), 64f, permanent: false, lowPriority: false));

        Sent sent = Assert.Single(temps.Calls);
        Assert.Equal("ProjectDecal", sent.Member);
        Assert.Equal(TeFilterKind.Broadcast, sent.Filter.Kind);
        Assert.Equal(new Vector(1, 2, 3), sent.Args[0]);
        Assert.Equal(new QAngle(10, 90, 0), sent.Args[1]);
        Assert.Equal(64f, sent.Args[2]);
        Assert.Equal(4, sent.Args[3]);
        Assert.Equal(0f, sent.Args[4]);
        Assert.Empty(world.Traces);
    }

    [Fact]
    public void APermanentProjectedDecalGoesToEveryoneConnectedToo()
    {
        var (effects, temps, _, _) = Rig();

        Assert.True(effects.PaintDecal(4, Vector.Zero, new Vector(0, 0, 0), 32f, permanent: true, lowPriority: false));

        Assert.Equal(TeFilterKind.Broadcast, Assert.Single(temps.Calls).Filter.Kind);
    }

    [Fact]
    public void TheTracedFormLooksFiveUnitsEitherSideOfThePosition()
    {
        var (effects, _, world, _) = Rig();

        effects.PaintDecal(4, new Vector(10, 20, 30), null, 0f, permanent: false, lowPriority: false);

        var trace = Assert.Single(world.Traces);
        Assert.Equal(new Vector(5, 15, 25), trace.Start);
        Assert.Equal(new Vector(15, 25, 35), trace.End);
    }

    [Fact]
    public void ASprayedDecalTracesBrushesOnlyAndSendsABspDecalOnWhatItHit()
    {
        var (effects, temps, world, _) = Rig();
        world.Hit = Door;
        world.Indices[Door] = 7;

        Assert.True(effects.PaintDecal(4, new Vector(1, 2, 3), null, 0f, permanent: false, lowPriority: false));

        Assert.Equal(MaskSolidBrushOnly, Assert.Single(world.Traces).Mask);
        Sent sent = Assert.Single(temps.Calls);
        Assert.Equal("BSPDecal", sent.Member);
        Assert.Equal(TeFilterKind.Broadcast, sent.Filter.Kind);
        Assert.Equal(new Vector(1, 2, 3), sent.Args[0]);
        Assert.Equal(7, sent.Args[1]);
        Assert.Equal(4, sent.Args[2]);
        Assert.Empty(world.Statics);
    }

    [Fact]
    public void ADecalOnNothingIsOnTheWorld()
    {
        var (effects, temps, _, _) = Rig();

        effects.PaintDecal(4, Vector.Zero, null, 0f, permanent: false, lowPriority: false);

        Assert.Equal(0, Assert.Single(temps.Calls).Args[1]);
    }

    [Fact]
    public void AnEntityWithNoEdictIsTheWorldsIndex()
    {
        var (effects, temps, world, _) = Rig();
        world.Hit = Door;   // no index: -1, a server-only entity

        effects.PaintDecal(4, Vector.Zero, null, 0f, permanent: false, lowPriority: false);

        Assert.Equal(0, Assert.Single(temps.Calls).Args[1]);
    }

    [Fact]
    public void APermanentDecalTracesSolidAndIsStoredOnTheWorldInWorldSpace()
    {
        var (effects, temps, world, _) = Rig();
        world.Hit = World;
        world.Indices[World] = 0;

        Assert.True(effects.PaintDecal(4, new Vector(1, 2, 3), null, 0f, permanent: true, lowPriority: true));

        Assert.Equal(MaskSolid, Assert.Single(world.Traces).Mask);
        Assert.Equal((new Vector(1, 2, 3), 4, 0, 0, true), Assert.Single(world.Statics));
        Assert.Empty(temps.Calls);
    }

    [Fact]
    public void APermanentDecalOnAnEntityIsStoredInItsFrameAgainstItsModel()
    {
        var (effects, _, world, _) = Rig();
        world.Hit = Door;
        world.Indices[Door] = 7;
        Transform frame = Transform.FromOriginAngles(new Vector(100, 0, 0), new QAngle(0, 90, 0));
        world.Blocks[Door] = BlockAt(Vector.Zero, Vector.Zero, Vector.Zero, 33, frame);

        Assert.True(effects.PaintDecal(4, new Vector(100, 10, 0), null, 0f, permanent: true, lowPriority: false));

        var stored = Assert.Single(world.Statics);
        Assert.Equal(7, stored.Entity);
        Assert.Equal(33, stored.Model);
        Assert.True(Math.Abs(stored.Position.X - 10f) < 1e-4f && Math.Abs(stored.Position.Y) < 1e-4f,
            $"ten units along the door's +Y in the world is ten along its +X: {stored.Position}");
    }

    [Fact]
    public void ADecalOnAnEntityWithNoModelIsRefusedAndSaysSo()
    {
        var (effects, _, world, warnings) = Rig();
        world.Hit = Door;
        world.Indices[Door] = 7;
        world.Blocks[Door] = BlockAt(Vector.Zero, Vector.Zero, Vector.Zero, 0, Transform.Identity);

        Assert.False(effects.PaintDecal(4, Vector.Zero, null, 0f, permanent: true, lowPriority: false));

        Assert.Empty(world.Statics);
        Assert.Contains("class:func_door, name:door1", Assert.Single(warnings));
    }

    [Fact]
    public void WithNoTraceNothingIsPainted()
    {
        var (effects, temps, world, _) = Rig();
        world.Traceable = false;

        Assert.False(effects.PaintDecal(4, Vector.Zero, null, 0f, permanent: false, lowPriority: false));
        Assert.Empty(temps.Calls);
    }

    // ---- BubbleTrailEffect ------------------------------------------------------
    //
    // UTIL_BubbleTrail, which this mod did not send at all (stock A/B,
    // 2026-10-25: a crossbow bolt under water left no wake).

    /// <summary>A pool whose surface is at z 100: a MASK_WATER line from above stops there, or at its end when that is above it.</summary>
    private static (EffectsOverTempEnts Effects, Temps Temps) Pool()
    {
        var (effects, temps, world, _) = Rig();
        world.EndFor = (start, end) => end.Z < 100f && start.Z > 100f ? new Vector(end.X, end.Y, 100f) : end;
        return (effects, temps);
    }

    [Fact]
    public void ATrailUnderWaterRunsItsLengthUpToTheSurface()
    {
        var (effects, temps) = Pool();

        effects.BubbleTrailEffect(new Vector(0f, 0f, 10f), new Vector(30f, 0f, 20f), 5);

        var trail = Assert.Single(temps.Calls, c => c.Member == "BubbleTrail");
        Assert.Equal(TeFilters.Broadcast, trail.Filter);
        Assert.Equal(new object?[] { new Vector(0f, 0f, 10f), new Vector(30f, 0f, 20f), 100f, 17, 5, 8f, 0f }, trail.Args);
    }

    [Fact]
    public void ATrailInTheAirMakesNoBubbles()
    {
        var (effects, temps) = Pool();

        effects.BubbleTrailEffect(new Vector(0f, 0f, 150f), new Vector(30f, 0f, 160f), 5);

        Assert.DoesNotContain(temps.Calls, c => c.Member == "BubbleTrail");
    }

    [Fact]
    public void ATrailIntoTheWaterStartsAtTheSurfaceWithItsCountCut()
    {
        var (effects, temps) = Pool();

        effects.BubbleTrailEffect(new Vector(0f, 0f, 120f), new Vector(40f, 0f, 80f), 5);

        var trail = Assert.Single(temps.Calls, c => c.Member == "BubbleTrail");
        Assert.Equal(new object?[] { new Vector(20f, 0f, 100f), new Vector(40f, 0f, 80f), 100f, 17, 2, 8f, 0f }, trail.Args);
    }

    // ---- BubbleEffect ----------------------------------------------------------

    [Fact]
    public void AnEntityWithNoBlockMakesNoBubbles()
    {
        var (effects, temps, _, _) = Rig();
        effects.BubbleEffect(Door, 8, 2f);
        Assert.Empty(temps.Calls);
    }

    [Fact]
    public void NoBubbleSpriteMakesNoBubbles()
    {
        var (effects, temps, world, _) = Rig();
        world.Blocks[Door] = BlockAt(Vector.Zero, new Vector(-8, -8, -8), new Vector(8, 8, 8), 0, Transform.Identity);
        world.Sprite = 0;

        effects.BubbleEffect(Door, 8, 2f);

        Assert.Empty(temps.Calls);
    }

    [Fact]
    public void TheBubblesRiseOutOfTheVolumesWorldBoxToEveryone()
    {
        var (effects, temps, world, _) = Rig();
        world.Blocks[Door] = BlockAt(new Vector(100, 0, 50), new Vector(-10, -20, -5), new Vector(30, 40, 15), 0, Transform.Identity);

        effects.BubbleEffect(Door, 8, 2f);

        Sent sent = Assert.Single(temps.Calls);
        Assert.Equal("Bubbles", sent.Member);
        Assert.Equal(TeFilterKind.Broadcast, sent.Filter.Kind);
        Assert.Equal(new Vector(90, -20, 45), sent.Args[0]);
        Assert.Equal(new Vector(130, 40, 65), sent.Args[1]);
        Assert.Equal(17, sent.Args[3]);
        Assert.Equal(8, sent.Args[4]);
        Assert.Equal(2f, sent.Args[5]);
    }

    [Fact]
    public void TheHeightIsTheVolumesOwnWhenNoWaterEndsAbove()
    {
        var (effects, temps, world, _) = Rig();
        world.Blocks[Door] = BlockAt(Vector.Zero, new Vector(-10, -10, 0), new Vector(10, 10, 20), 0, Transform.Identity);

        effects.BubbleEffect(Door, 8, 2f);

        Assert.Equal(20f, Assert.Single(temps.Calls).Args[2]);
        var trace = Assert.Single(world.Traces);
        Assert.Equal(new Vector(0, 0, 20), trace.Start);
        Assert.Equal(new Vector(0, 0, 1044), trace.End);
        Assert.Equal(MaskWater, trace.Mask);
    }

    [Fact]
    public void TheHeightRunsToWhereTheWaterEnds()
    {
        var (effects, temps, world, _) = Rig();
        world.Blocks[Door] = BlockAt(Vector.Zero, new Vector(-10, -10, 0), new Vector(10, 10, 20), 0, Transform.Identity);
        world.Fraction = 0.5f;
        world.EndPos = new Vector(0, 0, 300);

        effects.BubbleEffect(Door, 8, 2f);

        Assert.Equal(300f, Assert.Single(temps.Calls).Args[2]);
    }

    [Fact]
    public void TheServersTempEntsPlayTheBubblesBlobItsDeclarationLaysOut()
    {
        var engine = TestServices.Fake();
        engine.Returns("Effects.TempEntRegister", 5);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        temps.Bubbles(TeFilters.Broadcast, 0f, new Vector(1, 2, 3), new Vector(4, 5, 6), 7f, 8, 9, 10f);

        var expected = new SourceSharp.Shared.TeBubbles
        {
            Mins = new Vector(1, 2, 3), Maxs = new Vector(4, 5, 6), Height = 7f, ModelIndex = 8, Count = 9, Speed = 10f,
        };
        byte[] blob = engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<byte[]>(4);

        Assert.Equal(expected.ToBlob(registry.Find("CTEBubbles")!), blob);
    }

    [Fact]
    public void TheInverseTransformIsTheTransposedRotationOfTheOffset()
    {
        Transform frame = Transform.FromOriginAngles(new Vector(5, 6, 7), new QAngle(0, 90, 0));
        Vector local = new(3, 0, 1);
        Vector back = EffectsOverTempEnts.InverseTransformPoint(frame.TransformPoint(local), frame);
        Assert.True((back - local).Length < 1e-4f, $"{back}");
    }
}
