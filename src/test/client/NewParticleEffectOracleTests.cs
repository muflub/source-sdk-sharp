using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="NewParticleEffect"/> against stock's CParticleMgr::UpdateNewEffects
/// and CNewParticleEffect as recorded (Fixtures/particlemgr-new-stock.tsv.gz,
/// README-particlemgr-new-stock.md). The particle library's answers (sleep
/// times, particle counts, has-moved, bounds, IsFinished) and the draw's view
/// reads are inputs. Every decision stock made is checked against the model:
/// - the step's dt under r_particle_timescale;
/// - each effect's flags before its update, before and after its step;
/// - the sim list, in order, and which entries are for bounds only;
/// - the seconds each step simulates;
/// - the leaf system's change calls and the last box they keep;
/// - each draw's return and the sleep time it leaves;
/// - the removals Release and the step ask for.
/// </summary>
public class NewParticleEffectOracleTests
{
    private sealed class Live
    {
        public readonly NewParticleEffect Model = new();
        public float NextSleepTime;
        public int Active;
        public bool HasMoved;
        public Vector Min, Max;
        public bool Listed, BoundsOnly;
    }

    private sealed record Replay(int Updates, int Scaled, int Effects, int Listed, int BoundsOnly, int Skipped, int Steps, int Finished,
                                 int Detects, int Changes, int Draws, int Releases, int ReleaseRemovals, int Removals, List<string> Wrong);

    private static Vector V(string s)
    {
        string[] p = s.Split(',');
        return new Vector(F(p[0]), F(p[1]), F(p[2]));
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static bool Same(Vector a, Vector b) => a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

    private static string Text(Vector v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:R},{v.Y:R},{v.Z:R}");

    // The flags the effect rows log, in their order: first frame, needs bbox, auto bbox, remove, simulate.
    private static NewParticleFlags Logged(string[] c)
    {
        NewParticleFlags f = NewParticleFlags.None;
        if (c[4] == "1") f |= NewParticleFlags.FirstFrame;
        if (c[5] == "1") f |= NewParticleFlags.NeedsBBoxUpdate;
        if (c[6] == "1") f |= NewParticleFlags.AutoUpdateBBox;
        if (c[7] == "1") f |= NewParticleFlags.Remove;
        if (c[8] == "1") f |= NewParticleFlags.Simulate;
        return f;
    }

    private const NewParticleFlags LoggedFlags = NewParticleFlags.FirstFrame | NewParticleFlags.NeedsBBoxUpdate
        | NewParticleFlags.AutoUpdateBBox | NewParticleFlags.Remove | NewParticleFlags.Simulate;

    private static readonly Lazy<Replay> s_replay = new(Run);

    private static Replay Run()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "particlemgr-new-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded new particle effects are missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);

        var live = new Dictionary<string, Live>(StringComparer.Ordinal);
        var wrong = new List<string>();
        var frame = new List<string>();       // this update's effects, in update order
        var expectList = new Queue<(string Ptr, bool BoundsOnly)>();
        float dt = 0, now = 0;
        string? pendingChange = null;
        int updates = 0, scaled = 0, effects = 0, listed = 0, boundsOnly = 0, skipped = 0, steps = 0, finished = 0;
        int detects = 0, changes = 0, draws = 0, releases = 0, releaseRemovals = 0, removals = 0;
        NewParticleDrawIn drawIn = default;
        string? drawPtr = null;
        int lineNo = 0;

        void Bad(string what) => wrong.Add($"line {lineNo}: {what}");

        Live Get(string ptr)
        {
            if (!live.TryGetValue(ptr, out Live? l))
            {
                Bad($"{ptr} was never added");
                live[ptr] = l = new Live();
            }
            return l;
        }

        void CheckFlags(string tag, Live l, string[] c)
        {
            NewParticleFlags model = l.Model.Flags & LoggedFlags, stock = Logged(c);
            if (model != stock)
                Bad($"{tag} {c[2]}: model flags {model}, stock {stock}");
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            string[] c = line.Split('\t');
            if (c.Length < 2 || c[0] != "np")
                continue;
            string tag = c[1];

            if (pendingChange != null)
            {
                if (tag != "changed" || c[2] != pendingChange)
                    Bad($"the model told the leaf system {pendingChange} changed and stock did not");
                pendingChange = null;
                if (tag == "changed")
                {
                    changes++;
                    continue;
                }
            }

            switch (tag)
            {
                case "add":
                    live[c[2]] = new Live();
                    break;
                case "update":
                {
                    updates++;
                    float dtIn = F(c[3]), scale = F(c[4]);
                    dt = dtIn * scale;
                    if (!dt.Equals(F(c[5])))
                        Bad($"dt {dtIn} x {scale}: model {dt:R}, stock {c[5]}");
                    if (scale != 1.0f)
                        scaled++;
                    now = F(c[6]);
                    frame.Clear();
                    break;
                }
                case "effect":
                {
                    effects++;
                    Live l = Get(c[2]);
                    CheckFlags(tag, l, c);
                    l.NextSleepTime = F(c[9]);
                    l.Active = I(c[10]);
                    l.HasMoved = c[11] == "1";
                    frame.Add(c[2]);
                    break;
                }
                case "build":
                {
                    if (!F(c[2]).Equals(now))
                        Bad($"the list compares against {c[2]}, the update set {now:R}");
                    expectList.Clear();
                    foreach (string ptr in frame)
                    {
                        Live l = live[ptr];
                        (l.Listed, l.BoundsOnly) = l.Model.SimList(now, l.NextSleepTime, l.Active, l.HasMoved);
                        if (l.Listed)
                            expectList.Enqueue((ptr, l.BoundsOnly));
                        else
                            skipped++;
                    }
                    break;
                }
                case "list":
                {
                    listed++;
                    if (!expectList.TryDequeue(out (string Ptr, bool BoundsOnly) e))
                    {
                        Bad($"stock listed {c[2]} past the end of the model's list");
                        break;
                    }
                    bool only = c[3] == "1";
                    if (e.Ptr != c[2] || e.BoundsOnly != only)
                        Bad($"list: model {e.Ptr} bounds-only {e.BoundsOnly}, stock {c[2]} {only}");
                    if (only)
                        boundsOnly++;
                    break;
                }
                case "proc":
                {
                    if (expectList.Count != 0)
                        Bad($"the model listed {expectList.Count} more effect(s) than stock: {string.Join(' ', expectList.Select(x => x.Ptr))}");
                    expectList.Clear();
                    Live l = Get(c[2]);
                    CheckFlags(tag, l, c);
                    if (l.Model.FirstBoxRecalculates)
                        Bad($"{c[2]}: the model recalculates a first box the recording never asked for");
                    l.Model.FirstBox(recalculated: false);
                    break;
                }
                case "sim":
                {
                    steps++;
                    Live l = Get(c[2]);
                    bool only = c[3] == "1";
                    if (only != l.BoundsOnly)
                        Bad($"sim {c[2]}: bounds-only model {l.BoundsOnly}, stock {only}");
                    float step = l.Model.Step(dt);
                    if (!step.Equals(F(c[4])))
                        Bad($"sim {c[2]}: model steps {step:R}, stock {c[4]}");
                    bool fin = c[5] == "1";
                    if (fin)
                        finished++;
                    l.Model.Finished(fin);
                    break;
                }
                case "simmed":
                {
                    Live l = Get(c[2]);
                    CheckFlags(tag, l, c);
                    l.NextSleepTime = F(c[9]);
                    l.Active = I(c[10]);
                    break;
                }
                case "detect":
                {
                    detects++;
                    Live l = Get(c[2]);
                    l.Min = V(c[5]);
                    l.Max = V(c[6]);
                    if (!Same(l.Model.LastMin, V(c[7])) || !Same(l.Model.LastMax, V(c[8])))
                        Bad($"detect {c[2]}: model last box {Text(l.Model.LastMin)} {Text(l.Model.LastMax)}, stock {c[7]} {c[8]}");
                    if (l.Model.DetectChanges(l.Min, l.Max, F(c[4]), c[3] == "1"))
                        pendingChange = c[2];
                    break;
                }
                case "changed":
                    Bad($"stock told the leaf system {c[2]} changed and the model did not");
                    break;
                case "drawin":
                {
                    Live l = Get(c[2]);
                    drawPtr = c[2];
                    drawIn = new NewParticleDrawIn
                    {
                        Flags = I(c[3]),
                        DrawParticles = true,   // drawin is logged past the switches
                        ClientModeDraws = true,
                        RenderSystems = true,
                        Camera = V(c[4]),
                        Min = l.Min,
                        Max = l.Max,
                        MaxDrawDistance = F(c[6]),
                        CameraObject = (ulong)(I(c[7]) + 1), // the view entity: some entity
                        SkipRenderControlPoint = I(c[8]),
                        HighestControlPoint = I(c[9]),
                        LastSimulationTime = F(c[10]),
                        NoDrawTimeToGoToSleep = F(c[11]),
                        NextSleepTime = l.NextSleepTime,
                    };
                    float dist = NewParticleEffect.SqrDistanceToBox(l.Min, l.Max, drawIn.Camera);
                    if (!dist.Equals(F(c[5])))
                        Bad($"drawin {c[2]}: model distance {dist:R}, stock {c[5]}");
                    break;
                }
                case "draw":
                {
                    draws++;
                    if (drawPtr != c[2])
                    {
                        Bad($"draw {c[2]} past its inputs");
                        break;
                    }
                    Live l = live[c[2]];
                    NewParticleDraw d = NewParticleEffect.Draw(drawIn);
                    string reason = d.Reason switch
                    {
                        NewParticleDrawReason.DrawParticles => "drawparticles",
                        NewParticleDrawReason.ClientMode => "clientmode",
                        NewParticleDrawReason.ShadowDepth => "shadowdepth",
                        NewParticleDrawReason.Distance => "distance",
                        NewParticleDrawReason.SkipSelf => "skip-self",
                        NewParticleDrawReason.SkipRoot => "skip-root",
                        NewParticleDrawReason.SkipInEye => "skip-in-eye",
                        _ => "drawn",
                    };
                    int result = d.Action == NewParticleDrawAction.None ? 0 : 1;
                    if (reason != c[5] || result != I(c[4]))
                        Bad($"draw {c[2]}: model {reason} {result}, stock {c[5]} {c[4]}");
                    // A drawn system's render (the library's) keeps it awake: the
                    // sleep time it leaves is the library's answer, not a decision.
                    if (d.Reason != NewParticleDrawReason.Drawn && !d.NextSleepTime.Equals(F(c[6])))
                        Bad($"draw {c[2]}: model sleeps at {d.NextSleepTime:R}, stock {c[6]}");
                    l.NextSleepTime = F(c[6]);
                    drawPtr = null;
                    break;
                }
                case "release":
                {
                    releases++;
                    Live l = Get(c[2]);
                    bool before = l.Model.Has(NewParticleFlags.Remove);
                    l.Model.Release(I(c[3]), c[4] == "1", c[5] == "1");
                    if (!before && l.Model.Has(NewParticleFlags.Remove))
                        releaseRemovals++;
                    break;
                }
                case "remove":
                    removals++;
                    live.Remove(c[2]);
                    break;
            }
        }
        if (pendingChange != null)
            wrong.Add($"the model told the leaf system {pendingChange} changed at the end of the recording");

        return new Replay(updates, scaled, effects, listed, boundsOnly, skipped, steps, finished, detects, changes, draws,
                          releases, releaseRemovals, removals, wrong);
    }

    [Fact]
    public void EveryDecisionTheStockManagerMadeTheModelMakesTheSame()
    {
        Replay r = s_replay.Value;
        Assert.True(r.Wrong.Count == 0, $"{r.Wrong.Count} wrong:\n" + string.Join('\n', r.Wrong.Take(20)));
    }

    [Fact]
    public void TheRecordingReachesEveryDecision()
    {
        Replay r = s_replay.Value;
        Assert.True(r.Updates > 1000, $"{r.Updates} updates");
        Assert.True(r.Scaled > 0, $"{r.Scaled} updates under r_particle_timescale");
        Assert.True(r.Effects > 20000, $"{r.Effects} effect updates");
        Assert.True(r.Listed > 20000, $"{r.Listed} listed");
        Assert.True(r.Skipped > 0, $"{r.Skipped} skipped");
        Assert.True(r.Steps > 20000, $"{r.Steps} steps");
        Assert.True(r.Finished > 0, $"{r.Finished} finished by their step");
        Assert.True(r.Detects > 20000 && r.Changes > 1000, $"{r.Detects} detects, {r.Changes} changes");
        Assert.True(r.Draws > 20000, $"{r.Draws} draws");
        Assert.True(r.Releases > 0 && r.ReleaseRemovals > 0, $"{r.Releases} releases, {r.ReleaseRemovals} removing");
        Assert.True(r.Removals > 50, $"{r.Removals} removals");
    }
}
