using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// C7 particles phase 3: NewParticleManager, the frame the native shells ask
/// for once a frame (IManagedParticlesClient::NewEffectsFrame) and the draw
/// they ask per drawn effect (NewEffectDraw), over a fake library. The
/// decisions themselves are NewParticleEffect's (NewParticleEffectTests and
/// the stock replay); these hold the frame's order and what it commits.
/// </summary>
public class NewParticleManagerTests
{
    // The library as the manager drives it: what it was asked, and what it
    // answers for each batch entry.
    private sealed class FakeLibrary : IParticleCollections
    {
        public readonly List<string> Calls = [];
        public readonly List<ParticleSimEntry> Stepped = [];
        public float LastTime = -1f;
        public Func<ParticleSimEntry, ParticleSimResult> Answer = e => new ParticleSimResult
        {
            ActiveParticles = 1, Min = new Vector(0, 0, 0), Max = new Vector(1, 1, 1),
        };

        public void SetLastSimulationTime(float time)
        {
            Calls.Add("time");
            LastTime = time;
        }

        public void SimulateBatch(ReadOnlySpan<ParticleSimEntry> entries, Span<ParticleSimResult> results)
        {
            Calls.Add($"batch {entries.Length}");
            for (int i = 0; i < entries.Length; i++)
            {
                Stepped.Add(entries[i]);
                results[i] = Answer(entries[i]);
            }
        }

        public void BloatBounds(ulong collection) => Calls.Add("bloat");

        public void Bounds(ulong collection, out Vector min, out Vector max)
        {
            Calls.Add("bounds");
            min = max = default;
        }

        public void SetBounds(ulong collection, Vector min, Vector max) => Calls.Add("setbounds");

        public readonly List<bool> Profiles = [];

        public void CommitProfileInformation(bool commit) => Profiles.Add(commit);
    }

    private static NewParticleIn Effect(uint id, NewParticleInFlags flags = NewParticleInFlags.RenderHandle | NewParticleInFlags.Allocated)
        => new()
        {
            Id = id,
            Flags = flags,
            Collection = 0x1000 + id,
            Definition = 0x9000,
            NextSleepTime = 100f,
        };

    private static NewParticleFrameHeader Header(float dt = 0.05f, float timescale = 1f, float curTime = 10f)
        => new() { Dt = dt, Timescale = timescale, CurTime = curTime, AabbBuffer = 2f };

    private static List<NewParticleCommit> Frame(NewParticleManager m, NewParticleFrameHeader header, params NewParticleIn[] effects)
    {
        var commits = new NewParticleCommit[effects.Length * 4 + 4];
        int n = m.Frame(header, effects, commits);
        return [.. commits.AsSpan(0, n).ToArray()];
    }

    private static NewParticleActions ActionsOf(List<NewParticleCommit> commits, uint id)
        => commits.Where(c => c.Id == id).Aggregate(NewParticleActions.None, (a, c) => a | c.Actions);

    // UpdateNewEffects sets the library's clock before anything steps.
    [Fact]
    public void TheLibrarysClockIsSetBeforeTheBatch()
    {
        var lib = new FakeLibrary();
        Frame(new NewParticleManager(lib), Header(curTime: 12.5f), Effect(1));
        Assert.Equal(["time", "batch 1"], lib.Calls);
        Assert.Equal(12.5f, lib.LastTime);
    }

    // EndSimulateParticles: the library keeps a frame's profile only when
    // the frame took longer than r_particle_sim_spike_threshold_ms.
    [Fact]
    public void TheProfileIsKeptOnlyForAFramePastTheSpikeThreshold()
    {
        var lib = new FakeLibrary();
        var m = new NewParticleManager(lib);
        NewParticleFrameHeader header = Header();
        header.SpikeThresholdMs = 1_000_000;
        Frame(m, header, Effect(1));
        header.SpikeThresholdMs = -1;
        Frame(m, header, Effect(1));
        Assert.Equal([false, true], lib.Profiles);
    }

    // ProcessPSystem: the first frame simulates zero seconds, the next the
    // frame's dt scaled by r_particle_timescale; every listed effect in one call.
    [Fact]
    public void AnEffectStepsZeroOnItsFirstFrameThenTheScaledFrameTime()
    {
        var lib = new FakeLibrary();
        var m = new NewParticleManager(lib);
        Frame(m, Header(dt: 0.04f, timescale: 0.5f), Effect(1), Effect(2));
        Frame(m, Header(dt: 0.04f, timescale: 0.5f), Effect(1), Effect(2));
        Assert.Equal(["time", "batch 2", "time", "batch 2"], lib.Calls);
        Assert.Equal([0f, 0f, 0.02f, 0.02f], lib.Stepped.Select(e => e.Dt));
        Assert.Equal([0x1001UL, 0x1002UL, 0x1001UL, 0x1002UL], lib.Stepped.Select(e => e.Collection));
    }

    // BuildParticleSimList: past its sleep time with particles, an effect is
    // not listed unless it moved, and then for its bounds only.
    [Fact]
    public void ASleepingEffectStepsOnlyItsBoundsAndOnlyWhenItMoved()
    {
        var lib = new FakeLibrary();
        var m = new NewParticleManager(lib);
        NewParticleIn still = Effect(1), moved = Effect(2);
        still.NextSleepTime = moved.NextSleepTime = 5f;
        still.Active = moved.Active = 3;
        moved.HasMoved = 1;
        Frame(m, Header(curTime: 10f), still, moved);
        Assert.Single(lib.Stepped);
        Assert.Equal(0x1002UL, lib.Stepped[0].Collection);
        Assert.Equal(1, lib.Stepped[0].BoundsOnly);
    }

    // DetectChanges: the first box always changes (Construct's inside-out
    // last box); the same box again, inside the buffer, does not.
    [Fact]
    public void TheBoxIsReportedChangedOnceThenNotWhileItStaysInsideTheBuffer()
    {
        var m = new NewParticleManager(new FakeLibrary());
        Assert.Equal(NewParticleActions.Changed, ActionsOf(Frame(m, Header(), Effect(1)), 1));
        Assert.Equal(NewParticleActions.None, ActionsOf(Frame(m, Header(), Effect(1)), 1));
    }

    [Fact]
    public void AnEffectOutOfTheLeafSystemIsNeverReportedChanged()
    {
        var m = new NewParticleManager(new FakeLibrary());
        Assert.Equal(NewParticleActions.None, ActionsOf(Frame(m, Header(), Effect(1, NewParticleInFlags.Allocated)), 1));
    }

    // ProcessPSystem's end: a collection the batch finds finished is removed
    // after the frame, and the manager forgets it.
    [Fact]
    public void AFinishedCollectionIsRemovedAfterItsStep()
    {
        var lib = new FakeLibrary { Answer = _ => new ParticleSimResult { Finished = 1 } };
        var m = new NewParticleManager(lib);
        List<NewParticleCommit> commits = Frame(m, Header(), Effect(1));
        Assert.True(ActionsOf(commits, 1).HasFlag(NewParticleActions.Remove));
        Assert.Equal(NewParticleActions.Remove, commits[^1].Actions);
        Assert.Equal(0, m.Count);
    }

    // Release: let go of when already finished, an allocated effect is
    // removed without being stepped again (the remove flag skips the list).
    [Fact]
    public void AReleasedFinishedEffectIsRemovedWithoutAStep()
    {
        var lib = new FakeLibrary();
        var m = new NewParticleManager(lib);
        Frame(m, Header(), Effect(1));
        lib.Stepped.Clear();
        NewParticleIn released = Effect(1, NewParticleInFlags.Allocated | NewParticleInFlags.Released | NewParticleInFlags.RenderHandle);
        released.Finished = 1;
        List<NewParticleCommit> commits = Frame(m, Header(), released);
        Assert.Empty(lib.Stepped);
        Assert.Equal(NewParticleActions.Remove, ActionsOf(commits, 1));
    }

    [Fact]
    public void AReleasedEffectStillBurningIsKept()
    {
        var m = new NewParticleManager(new FakeLibrary());
        Frame(m, Header(), Effect(1));
        NewParticleIn released = Effect(1, NewParticleInFlags.Allocated | NewParticleInFlags.Released | NewParticleInFlags.RenderHandle);
        Assert.False(ActionsOf(Frame(m, Header(), released), 1).HasFlag(NewParticleActions.Remove));
        Assert.Equal(1, m.Count);
    }

    // AddEffect: a view-model effect's render group, asked for once.
    [Fact]
    public void AViewModelEffectAsksForTheViewModelGroupOnceAtItsFirstFrame()
    {
        var m = new NewParticleManager(new FakeLibrary());
        NewParticleIn vm = Effect(1, NewParticleInFlags.Allocated | NewParticleInFlags.RenderHandle | NewParticleInFlags.ViewModel);
        Assert.True(ActionsOf(Frame(m, Header(), vm), 1).HasFlag(NewParticleActions.ViewGroup));
        Assert.False(ActionsOf(Frame(m, Header(), vm), 1).HasFlag(NewParticleActions.ViewGroup));
        Assert.False(ActionsOf(Frame(new NewParticleManager(new FakeLibrary()), Header(), Effect(2)), 2).HasFlag(NewParticleActions.ViewGroup));
    }

    // An effect native stopped listing went with its shell.
    [Fact]
    public void AnEffectNativeNoLongerListsIsForgotten()
    {
        var m = new NewParticleManager(new FakeLibrary());
        Frame(m, Header(), Effect(1), Effect(2));
        Frame(m, Header(), Effect(2));
        Assert.Equal(1, m.Count);
        Assert.Null(m.Effect(1));
        Assert.NotNull(m.Effect(2));
    }

    // The new effect's first box is not recalculated: Construct leaves
    // m_bNeedsBBoxUpdate false, so the library is never asked to bloat.
    [Fact]
    public void ANewEffectIsNeverAskedForAFirstBox()
    {
        var lib = new FakeLibrary();
        var m = new NewParticleManager(lib);
        Frame(m, Header(), Effect(1));
        Frame(m, Header(), Effect(1));
        Assert.DoesNotContain("bloat", lib.Calls);
    }

    // EarlyRetireParticleSystems: off (cl_particle_retire_cost 0) asks no
    // replacement; on, two first-frame systems of one definition covering the
    // screen past the budget retire the larger.
    [Fact]
    public void RetirementIsOffAtZeroCostAndReplacesAFirstFrameSystemPastTheBudget()
    {
        NewParticleIn a = Effect(1), b = Effect(2);
        a.CullRadius = b.CullRadius = 64f;
        a.CullFillCost = b.CullFillCost = 1f;
        a.CullPoint = b.CullPoint = new Vector(0, 0, 0);
        NewParticleFrameHeader header = Header();
        header.ViewWidth = 640;
        header.ViewHeight = 480;
        header.ViewFov = 90f;
        header.ViewOrigin = new Vector(0, 0, 0);
        header.WorldToScreen = new Matrix4x4Raw { M00 = 1, M11 = 1, M22 = 1, M33 = 1 };

        Assert.DoesNotContain(Frame(new NewParticleManager(new FakeLibrary()), header, a, b),
                              c => c.Actions.HasFlag(NewParticleActions.Replace));

        // each covers the 640x480 view (the cull point is at the eye): 307,200
        // pixels apiece against a 400,000 budget (the cost is in thousands of
        // pixels), so retiring one fits
        header.RetireCost = 400f;
        List<NewParticleCommit> commits = Frame(new NewParticleManager(new FakeLibrary()), header, a, b);
        Assert.Single(commits, c => c.Actions.HasFlag(NewParticleActions.Replace));
    }

    // The commits never run past the buffer native gave.
    [Fact]
    public void TheCommitsAreCutToTheBuffer()
    {
        var m = new NewParticleManager(new FakeLibrary());
        var commits = new NewParticleCommit[1];
        int n = m.Frame(Header(), [Effect(1), Effect(2), Effect(3)], commits);
        Assert.Equal(1, n);
        Assert.Equal(1u, commits[0].Id);
    }

    // DrawModel: past the draw distance the system draws nothing and is kept
    // awake for its no-draw time; a debug box is asked for in culled grey.
    [Fact]
    public void ADistantSystemDrawsNothingStaysAwakeAndShowsAGreyBox()
    {
        var m = new NewParticleManager(new FakeLibrary());
        NewParticleDrawOutRaw output = m.Draw(new NewParticleDrawInRaw
        {
            Id = 1,
            Flags = NewParticleEffect.StudioTransparency,
            DrawParticles = 1, ClientModeDraws = 1, RenderSystems = 1, ShowBBox = 1,
            Camera = new Vector(10000, 0, 0),
            MaxDrawDistance = 100f,
            NoDrawTimeToGoToSleep = 8f,
            LastSimulationTime = 20f,
            NextSleepTime = 5f,
            SkipRenderControlPoint = -1,
        });
        Assert.Equal((int)NewParticleDrawAction.None, output.Action);
        Assert.Equal(1, output.SetSleep);
        Assert.Equal(28f, output.NextSleepTime);
        Assert.Equal((1, 64, 64, 64), (output.BBox, output.BBoxR, output.BBoxG, output.BBoxB));
    }

    [Fact]
    public void ANearTranslucentSystemRendersNowAndAnOpaqueOneGoesToTheRenderCache()
    {
        var m = new NewParticleManager(new FakeLibrary());
        var draw = new NewParticleDrawInRaw
        {
            Id = 1, DrawParticles = 1, ClientModeDraws = 1, RenderSystems = 1,
            MaxDrawDistance = 100f, SkipRenderControlPoint = -1,
        };
        Assert.Equal((int)NewParticleDrawAction.RenderCache, m.Draw(draw).Action);
        draw.Flags = NewParticleEffect.StudioTransparency;
        NewParticleDrawOutRaw output = m.Draw(draw);
        Assert.Equal((int)NewParticleDrawAction.Render, output.Action);
        Assert.Equal((0, 0), (output.SetSleep, output.BBox));
    }

    // cs_cl_reload: each effect's flags and last box come back, so a system
    // already past its first frame is not stepped as new again.
    [Fact]
    public void TheEffectsCrossAReload()
    {
        var lib = new FakeLibrary();
        var m = new NewParticleManager(lib);
        Frame(m, Header(), Effect(7));
        var writer = new SourceSharp.Client.ReloadWriter();
        m.Save(writer);

        var back = new NewParticleManager(lib);
        back.Restore(new SourceSharp.Client.ReloadReader(writer.ToArray()));
        Assert.Equal(m.Effect(7)!.Flags, back.Effect(7)!.Flags);
        Assert.Equal((m.Effect(7)!.LastMin, m.Effect(7)!.LastMax), (back.Effect(7)!.LastMin, back.Effect(7)!.LastMax));

        lib.Stepped.Clear();
        Frame(back, Header(dt: 0.05f), Effect(7));
        Assert.Equal(0.05f, lib.Stepped[0].Dt);
    }
}
