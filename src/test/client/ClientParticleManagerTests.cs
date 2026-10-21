using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ClientParticleManager's loop against CParticleMgr::UpdateAllEffects and the
/// executor it drove (shell_managed_particles.cpp before C7 particles phase
/// 2.3), step by step; the binding's own decisions are
/// ParticleManagerOracleTests'.
/// </summary>
public class ClientParticleManagerTests
{
    private sealed class Sims : IClientParticleSims
    {
        public readonly List<string> Calls = [];
        public int[] Spawn = [];
        public Func<int, bool> Alive = _ => true;
        public bool Bounds;
        public Vector BoundsMin, BoundsMax;
        public int[] StepSpawns = [];

        public int Emit(uint owner, int channel, ref ParticleEmit emit, Span<int> idsOut)
        {
            Calls.Add($"emit {owner} {channel} dt={emit.Dt} drawn={emit.WasDrawnPrevFrame}");
            if (Bounds)
            {
                emit.HasBounds = 1;
                emit.Mins = BoundsMin;
                emit.Maxs = BoundsMax;
            }
            Spawn.CopyTo(idsOut);
            return Spawn.Length;
        }

        public int Simulate(uint owner, int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
        {
            Calls.Add($"step {owner} {channel} [{string.Join(' ', ids.ToArray())}]");
            for (int i = 0; i < ids.Length; i++)
                aliveOut[i] = Alive(ids[i]) ? (byte)1 : (byte)0;
            return 1;
        }

        public int TakeSimulateSpawns(uint owner, int channel, Span<int> idsOut)
        {
            StepSpawns.CopyTo(idsOut);
            int n = StepSpawns.Length;
            StepSpawns = [];
            return n;
        }

        public int Draw(uint owner, int channel, Span<ParticleQuadRaw> quadsOut)
        {
            // Newest first, as the sims answer, and one id the list does not hold.
            quadsOut[0] = new ParticleQuadRaw { Id = 99, Kind = 3 };
            quadsOut[1] = new ParticleQuadRaw { Id = 2, Kind = 3, Size = 2 };
            return 2;
        }
    }

    private sealed class Random : IUniformRandom
    {
        public readonly List<(int, int)> Asked = [];
        public int Answer = 1;

        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max)
        {
            Asked.Add((min, max));
            return Answer;
        }
    }

    private sealed class NoView : IParticleViewSource
    {
        public ParticleView Read() => default;
    }

    private static (ClientParticleManager, Sims, Random) Make()
    {
        var sims = new Sims();
        var random = new Random();
        return (new ClientParticleManager(() => sims, random, new ParticleViews(new NoView())), sims, random);
    }

    private static ParticleFrameIn At(uint owner, Vector origin) => new() { Owner = owner, Origin = origin };

    private static int Frame(ClientParticleManager m, Span<ParticleCommit> commits, float dt, params ParticleFrameIn[] inputs)
        => m.Frame(new ParticleFrameHeader { Dt = dt }, inputs, 2f, commits);

    // UpdateAllEffects clamps the frame's step to a tenth of a second.
    [Fact]
    public void TheStepIsClampedToATenth()
    {
        (ClientParticleManager m, Sims sims, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        Frame(m, new ParticleCommit[1], 0.5f, At(1, default));
        Assert.Equal("emit 1 0 dt=0.1 drawn=0", sims.Calls[0]);
    }

    // The first frame emits and skips the step; the next steps the list.
    [Fact]
    public void TheFirstFrameEmitsAndSkipsTheStep()
    {
        (ClientParticleManager m, Sims sims, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        sims.Spawn = [1, 2];
        Frame(m, new ParticleCommit[1], 0.05f, At(1, default));
        Assert.DoesNotContain(sims.Calls, c => c.StartsWith("step", StringComparison.Ordinal));
        sims.Spawn = [];
        Frame(m, new ParticleCommit[1], 0.05f, At(1, default));
        Assert.Contains("step 1 0 [2 1]", sims.Calls);
    }

    // AddParticle links each spawn at the head, at where the executor stands after its follow.
    [Fact]
    public void SpawnsAreLinkedAtTheHeadWhereTheFollowLeftTheExecutor()
    {
        (ClientParticleManager m, Sims sims, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        sims.Spawn = [5, 6];
        ParticleFrameIn input = At(1, new Vector(1, 1, 1));
        input.Followed = 1;
        input.FollowOrigin = new Vector(7, 8, 9);
        input.Matrix[0] = input.Matrix[5] = input.Matrix[10] = 1f;
        Frame(m, new ParticleCommit[1], 0.05f, input);
        Assert.Equal([6, 5], m.Binding(1, 0)!.Materials[0].Select(p => p.Id));
        Assert.Equal(new Vector(7, 8, 9).X, m.Binding(1, 0)!.Materials[0][0].Position.X);
    }

    // The step's dead leave in list order; its own spawns are linked after.
    [Fact]
    public void TheStepUnlinksTheDeadAndLinksItsOwnSpawns()
    {
        (ClientParticleManager m, Sims sims, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        sims.Spawn = [1, 2, 3];
        Frame(m, new ParticleCommit[1], 0.05f, At(1, default));
        sims.Spawn = [];
        sims.Alive = id => id != 2;
        sims.StepSpawns = [9];
        Frame(m, new ParticleCommit[1], 0.05f, At(1, default));
        Assert.Equal([9, 3, 1], m.Binding(1, 0)!.Materials[0].Select(p => p.Id));
    }

    // An executor native no longer lists (its owner went) takes its binding with it.
    [Fact]
    public void ABindingWhoseExecutorIsNotListedIsDropped()
    {
        (ClientParticleManager m, _, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        m.Add(2, 0, 0f, alwaysSimulate: true);
        Frame(m, new ParticleCommit[2], 0.05f, At(2, default));
        Assert.Null(m.Binding(1, 0));
        Assert.NotNull(m.Binding(2, 0));
    }

    // The commit is the binding after its update: its box, flags and the leaf system's change.
    [Fact]
    public void TheCommitCarriesTheBoxAndTheChange()
    {
        (ClientParticleManager m, Sims sims, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        sims.Bounds = true;
        sims.BoundsMin = new Vector(-100, -100, -100);
        sims.BoundsMax = new Vector(100, 100, 100);
        var commits = new ParticleCommit[1];
        Assert.Equal(1, Frame(m, commits, 0.05f, At(1, default)));
        Assert.Equal((1u, 100f, 1), (commits[0].Owner, commits[0].Max.X, commits[0].Changed));
        Assert.Equal((int)m.Binding(1, 0)!.Flags, commits[0].Flags);
    }

    // DrawModel draws nothing into a depth pass, and does not count it as drawn.
    [Fact]
    public void ADepthPassDrawsNothing()
    {
        (ClientParticleManager m, _, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        var input = new ParticleDrawIn { Owner = 1, ClientModeDraws = 1, DrawParticles = 1, Flags = 0x40000000 };
        m.DrawBegin(input, out ParticleDrawOut output, new ParticleQuadRaw[4]);
        Assert.Equal(0, output.Result);
        Assert.False(m.Binding(1, 0)!.Has(ParticleBindingFlags.Drawn));
    }

    // With no particles it is drawn and draws nothing (DrawModel's return 1).
    [Fact]
    public void AnEmptyBindingIsDrawnAndDrawsNothing()
    {
        (ClientParticleManager m, _, Random random) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        var input = new ParticleDrawIn { Owner = 1, ClientModeDraws = 1, DrawParticles = 1 };
        m.DrawBegin(input, out ParticleDrawOut output, new ParticleQuadRaw[4]);
        Assert.Equal(1, output.Result);
        Assert.True(m.Binding(1, 0)!.Has(ParticleBindingFlags.Drawn));
        Assert.Empty(random.Asked);
    }

    // The quads come back in the list's order; a particle the sim drew nothing for is Kind -1.
    [Fact]
    public void TheQuadsAreListedInTheBindingsOrder()
    {
        (ClientParticleManager m, _, _) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        m.Binding(1, 0)!.AddParticle(0, 1, default);
        m.Binding(1, 0)!.AddParticle(0, 2, default);
        var quads = new ParticleQuadRaw[4];
        int n = m.DrawBegin(new ParticleDrawIn { Owner = 1, ClientModeDraws = 1, DrawParticles = 1 }, out ParticleDrawOut output, quads);
        Assert.Equal((2, 2), (output.Result, n));
        Assert.Equal((2, 3, 2f), (quads[0].Id, quads[0].Kind, quads[0].Size));
        Assert.Equal((1, -1), (quads[1].Id, quads[1].Kind));
    }

    // Too small a buffer is answered with the count before any decision: no sort draw, not drawn.
    [Fact]
    public void TooSmallABufferIsAnsweredBeforeAnyDecision()
    {
        (ClientParticleManager m, _, Random random) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        m.Binding(1, 0)!.AddParticle(0, 1, default);
        m.Binding(1, 0)!.AddParticle(0, 2, default);
        int n = m.DrawBegin(new ParticleDrawIn { Owner = 1, ClientModeDraws = 1, DrawParticles = 1 }, out _, new ParticleQuadRaw[1]);
        Assert.Equal(2, n);
        Assert.Empty(random.Asked);
        Assert.False(m.Binding(1, 0)!.Has(ParticleBindingFlags.Drawn));
    }

    // The draw's sort choice is the one DrawEnd sorts by.
    [Fact]
    public void TheDrawsBucketChoiceSortsItsKeys()
    {
        (ClientParticleManager m, _, Random random) = Make();
        m.Add(1, 0, 0f, alwaysSimulate: true);
        foreach (int id in new[] { 1, 2, 3 })
            m.Binding(1, 0)!.AddParticle(0, id, default);
        random.Answer = 0;   // RandomInt( 0, 8 ) == 0: the bucket sort
        m.DrawBegin(new ParticleDrawIn { Owner = 1, ClientModeDraws = 1, DrawParticles = 1 }, out _, new ParticleQuadRaw[4]);
        m.DrawEnd(1, 0, [-10f, -50f, -30f]);
        Assert.Equal([2, 1, 3], m.Binding(1, 0)!.Materials[0].Select(p => p.Id));
    }

    // cs_cl_reload: the bindings come back as they were, lists in order.
    [Fact]
    public void TheBindingsCrossAReload()
    {
        (ClientParticleManager m, Sims sims, _) = Make();
        m.Add(1, 3, 4f, alwaysSimulate: false);
        sims.Spawn = [1, 2];
        ParticleFrameIn input = At(1, new Vector(1, 2, 3));
        input.Channel = 3;
        Frame(m, new ParticleCommit[1], 0.05f, input);
        m.IncrementFrameCode();
        var writer = new SourceSharp.Client.ReloadWriter();
        m.Save(writer);

        (ClientParticleManager back, _, _) = Make();
        back.Restore(new SourceSharp.Client.ReloadReader(writer.ToArray()));
        ParticleBinding a = m.Binding(1, 3)!, b = back.Binding(1, 3)!;
        Assert.Equal(a.Materials[0].Select(p => p.Id), b.Materials[0].Select(p => p.Id));
        Assert.Equal((a.Flags, a.Min.X, a.LastMax.Z, a.CullRadius, a.ActiveParticles), (b.Flags, b.Min.X, b.LastMax.Z, b.CullRadius, b.ActiveParticles));
        Assert.Equal(m.FrameCode, back.FrameCode);
    }
}
