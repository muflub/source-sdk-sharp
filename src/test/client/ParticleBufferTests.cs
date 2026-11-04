using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The particle manager's frames and draws reuse their buffers, and the sheets
/// walk their lists instead of sorting them: a warm frame allocates nothing,
/// a draw reads only what the sim wrote this time, one draw started inside
/// another keeps both, and each sheet still answers newest first.
/// </summary>
public class ParticleBufferTests
{
    private sealed class Sims : IClientParticleSims
    {
        public int[] Spawn = [];
        public int[] SpawnEach = [];
        public int[] Quads = [];
        public bool WritesAlive = true;
        public Action? InsideDraw;
        public bool WriteThenNest;

        public int Emit(uint owner, int channel, ref ParticleEmit emit, Span<int> idsOut)
        {
            SpawnEach.CopyTo(idsOut);
            Spawn.CopyTo(idsOut[SpawnEach.Length..]);
            int n = SpawnEach.Length + Spawn.Length;
            Spawn = [];
            return n;
        }

        public int Simulate(uint owner, int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
        {
            if (WritesAlive)
                aliveOut[..ids.Length].Fill(1);
            return 1;
        }

        public int TakeSimulateSpawns(uint owner, int channel, Span<int> idsOut) => 0;

        public int Draw(uint owner, int channel, Span<ParticleQuadRaw> quadsOut)
        {
            if (Quads.Length > quadsOut.Length)
                return Quads.Length;
            Action? inside = InsideDraw;
            InsideDraw = null;
            if (!WriteThenNest)
                inside?.Invoke();
            for (int i = 0; i < Quads.Length; i++)
                quadsOut[i] = new ParticleQuadRaw { Id = Quads[i], Kind = 3, Size = owner };
            if (WriteThenNest)
                inside?.Invoke();
            return Quads.Length;
        }
    }

    private sealed class Random : IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;

        public int RandomInt(int min, int max) => 1;
    }

    private sealed class NoView : IParticleViewSource
    {
        public ParticleView Read() => default;
    }

    private static (ClientParticleManager Manager, Sims Sims) Make()
    {
        var sims = new Sims();
        var manager = new ClientParticleManager(() => sims, new Random(), new ParticleViews(new NoView()), RenderConVarsKit.Particles());
        return (manager, sims);
    }

    private static ParticleDrawIn Draw(uint owner) => new() { Owner = owner, ClientModeDraws = 1 };

    // Two frames: the first takes the spawns, the second is past its first frame.
    private static void Warm(ClientParticleManager manager, Sims sims, uint owner, params int[] ids)
    {
        manager.Add(owner, 0, 0f, alwaysSimulate: true);
        sims.Spawn = ids;
        ParticleFrameIn[] inputs = [new() { Owner = owner }];
        manager.Frame(new ParticleFrameHeader { Dt = 0.01f }, inputs, new ParticleCommit[4]);
        manager.Frame(new ParticleFrameHeader { Dt = 0.01f }, inputs, new ParticleCommit[4]);
    }

    [Fact]
    public void AWarmFrameAndDrawAllocateNothing()
    {
        var (manager, sims) = Make();
        Warm(manager, sims, 1, 1, 2, 3);
        sims.Quads = [3, 2, 1];
        ParticleFrameIn[] inputs = [new() { Owner = 1 }];
        var commits = new ParticleCommit[4];
        var quads = new ParticleQuadRaw[8];
        float[] keys = [1f, 3f, 2f];

        void Frame()
        {
            manager.Frame(new ParticleFrameHeader { Dt = 0.01f }, inputs, commits);
            manager.DrawBegin(Draw(1), out _, quads);
            manager.DrawEnd(1, 0, keys);
        }

        for (int i = 0; i < 32; i++)
            Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 64; i++)
            Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void AParticleTheSimDidNotDrawThisTimeIsNotLastTimesQuad()
    {
        var (manager, sims) = Make();
        Warm(manager, sims, 1, 1, 2);
        var quads = new ParticleQuadRaw[4];

        sims.Quads = [2, 1];
        manager.DrawBegin(Draw(1), out _, quads);
        Assert.Equal(3, quads[0].Kind);
        Assert.Equal(3, quads[1].Kind);

        sims.Quads = [];
        Assert.Equal(2, manager.DrawBegin(Draw(1), out _, quads));
        Assert.Equal(-1, quads[0].Kind);
        Assert.Equal(-1, quads[1].Kind);
    }

    [Fact]
    public void ASimThatAsksForMoreRoomIsAskedAgainWithIt()
    {
        var (manager, sims) = Make();
        Warm(manager, sims, 1, 1, 2);
        sims.Quads = [.. Enumerable.Range(1, 200).Reverse()];
        var quads = new ParticleQuadRaw[4];

        Assert.Equal(2, manager.DrawBegin(Draw(1), out _, quads));

        Assert.Equal(3, quads[0].Kind);
        Assert.Equal(3, quads[1].Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADrawStartedInsideAnotherKeepsBothQuads(bool writeThenNest)
    {
        var (manager, sims) = Make();
        // Both owners in one frame's inputs: a frame drops an entry it was not given.
        manager.Add(1, 0, 0f, alwaysSimulate: true);
        manager.Add(2, 0, 0f, alwaysSimulate: true);
        sims.SpawnEach = [1, 2];
        ParticleFrameIn[] both = [new() { Owner = 1 }, new() { Owner = 2 }];
        manager.Frame(new ParticleFrameHeader { Dt = 0.01f }, both, new ParticleCommit[4]);
        sims.SpawnEach = [];
        manager.Frame(new ParticleFrameHeader { Dt = 0.01f }, both, new ParticleCommit[4]);
        sims.Quads = [2, 1];
        sims.WriteThenNest = writeThenNest;
        var outer = new ParticleQuadRaw[4];
        var inner = new ParticleQuadRaw[4];
        // A warm buffer the two would share were it left in the field.
        manager.DrawBegin(Draw(1), out _, outer);

        sims.InsideDraw = () => manager.DrawBegin(Draw(2), out _, inner);
        manager.DrawBegin(Draw(1), out _, outer);

        Assert.Equal([1f, 1f], outer.Take(2).Select(q => q.Size));
        Assert.Equal([2f, 2f], inner.Take(2).Select(q => q.Size));
    }

    [Fact]
    public void AnAnswerTheSimLeavesUnwrittenIsDeadAfterAFrameThatWroteIt()
    {
        var (manager, sims) = Make();
        Warm(manager, sims, 1, 1, 2);
        Assert.Equal(2, manager.Binding(1, 0)!.Materials[0].Count);

        sims.WritesAlive = false;
        manager.Frame(new ParticleFrameHeader { Dt = 0.01f }, [new ParticleFrameIn { Owner = 1 }], new ParticleCommit[4]);

        Assert.Empty(manager.Binding(1, 0)!.Materials[0]);
    }

    [Fact]
    public void TwoBucketSortsThroughOneScratchMatchFreshOnes()
    {
        static ParticleBinding Bound(int count)
        {
            var b = new ParticleBinding();
            for (int id = 1; id <= count; id++)
                b.AddParticle(0, id, default);
            return b;
        }

        static int[] Ids(ParticleBinding b) => [.. b.Materials[0].Select(p => p.Id)];

        var shared = new ParticleSortScratch();
        ParticleBinding big = Bound(6), small = Bound(3);
        ParticleBinding bigFresh = Bound(6), smallFresh = Bound(3);
        float[] bigKeys = [5f, 1f, 3f, 2f], smallKeys = [-10f, -50f, -30f];

        big.SortAfterDraw(0, bigKeys, bucketSort: true, shared);
        small.SortAfterDraw(0, smallKeys, bucketSort: true, shared);
        bigFresh.SortAfterDraw(0, bigKeys, bucketSort: true, new ParticleSortScratch());
        smallFresh.SortAfterDraw(0, smallKeys, bucketSort: true, new ParticleSortScratch());

        Assert.Equal(Ids(bigFresh), Ids(big));
        Assert.Equal(Ids(smallFresh), Ids(small));
        Assert.Equal(6, Ids(big).Length);
        Assert.Equal(3, Ids(small).Length);
    }

    private static SimpleSheet Sheet()
        => new("m", new ParticleStream(1), new ParticleViews(new NoView()), new Hidden(), () => 0f);

    private sealed class Hidden : IParticlePixelVisibility
    {
        public float FractionVisible(Vector origin, ref int queryHandle) => 0f;
    }

    [Fact]
    public void ASimpleSheetDrawsNewestFirstAfterADeathWithoutAllocating()
    {
        SimpleSheet sheet = Sheet();
        sheet.EffectId = 1;
        sheet.Add(new SimpleParticle { DieTime = 100f });
        sheet.Add(new SimpleParticle { DieTime = 0.001f });
        sheet.Add(new SimpleParticle { DieTime = 100f });
        var emit = new ParticleEmit();
        var ids = new int[4];
        sheet.Emit(ref emit, ids);
        var alive = new byte[3];
        sheet.Simulate(0.01f, true, [1, 2, 3], alive);
        Assert.Equal([1, 0, 1], alive);

        var quads = new ParticleQuadRaw[4];
        for (int i = 0; i < 8; i++)
            sheet.Draw(quads);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int n = 0;
        for (int i = 0; i < 16; i++)
            n = sheet.Draw(quads);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(2, n);
        Assert.Equal(3, quads[0].Id);
        Assert.Equal(1, quads[1].Id);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ASimpleSheetsBoundsAreItsParticlesAndAllocateNothing()
    {
        SimpleSheet sheet = Sheet();
        sheet.Add(new SimpleParticle { Position = new Vector(1, 5, 3), DieTime = 100f });
        sheet.Add(new SimpleParticle { Position = new Vector(-2, 7, 0), DieTime = 100f });
        var ids = new int[4];
        var emit = new ParticleEmit();
        sheet.Emit(ref emit, ids);
        for (int i = 0; i < 8; i++)
            sheet.Emit(ref emit, ids);

        emit = new ParticleEmit();
        long before = GC.GetAllocatedBytesForCurrentThread();
        sheet.Emit(ref emit, ids);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1, emit.HasBounds);
        Assert.Equal(new Vector(-2, 5, 0), emit.Mins);
        Assert.Equal(new Vector(1, 7, 3), emit.Maxs);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AFleckSheetDrawsNewestFirstWithoutAllocating()
    {
        FleckSheet sheet = new FleckSystem("f", default, default, "a", "b").Sheets[0];
        sheet.Add(new FleckParticle());
        sheet.Add(new FleckParticle());
        sheet.Add(new FleckParticle());
        var emit = new ParticleEmit();
        sheet.Emit(ref emit, new int[4]);

        var quads = new ParticleQuadRaw[4];
        for (int i = 0; i < 8; i++)
            sheet.Draw(quads);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int n = 0;
        for (int i = 0; i < 16; i++)
            n = sheet.Draw(quads);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(3, n);
        Assert.Equal([3, 2, 1], quads.Take(3).Select(q => q.Id));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ATeParticleSheetDrawsNewestFirstAndAnswersWhoIsAlive()
    {
        var sheet = new TeParticleSheet(() => 0f, new ParticleStream(1));
        sheet.Add(new TeParticle { Lifetime = 100f });
        sheet.Add(new TeParticle { Lifetime = 100f });
        sheet.Add(new TeParticle { Lifetime = 100f });
        var emit = new ParticleEmit();
        sheet.Emit(ref emit, new int[4]);

        var alive = new byte[2];
        sheet.Simulate(0.01f, true, [2, 99], alive);
        Assert.Equal([1, 0], alive);

        var quads = new ParticleQuadRaw[4];
        Assert.Equal(3, sheet.Draw(quads));
        Assert.Equal([3, 2, 1], quads.Take(3).Select(q => q.Id));
    }
}
