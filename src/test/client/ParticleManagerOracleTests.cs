using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ParticleBinding"/> against stock's CParticleMgr and
/// CParticleEffectBinding as recorded (Fixtures/particlemgr-stock.tsv.gz,
/// README-particlemgr-stock.md). The recording's executor calls (spawns,
/// SetBBox, SetLocalSpaceTransform, the cull radius, the sims' kills), its
/// RandomInt answers, frame times and sort keys are the inputs. Every decision
/// the manager made is checked against the model, as it happened:
/// - the state each binding starts its update with;
/// - the first box;
/// - each list before a step and around each draw;
/// - the full box update and its counter;
/// - the leaf system's change calls;
/// - the draw's frame time and sort choice;
/// - the order each draw leaves;
/// - the frame code and the drawn flags.
/// </summary>
public class ParticleManagerOracleTests
{
    private const float AabbBuffer = 2f; // cl_particleeffect_aabb_buffer's default; the recording never set it

    // The flags the executor sets on its binding (its space and simulation
    // settings, some through inline setters the probe cannot see): inputs here,
    // taken from stock at each update. Every other flag is the manager's and is
    // checked.
    private const ParticleBindingFlags Configured = ParticleBindingFlags.AlwaysSimulate | ParticleBindingFlags.CameraSpace
        | ParticleBindingFlags.DrawThruLeafSystem | ParticleBindingFlags.DrawBeforeViewModel | ParticleBindingFlags.AutoApplyLocalTransform;

    private static void Configure(ParticleBinding m, int stock)
        => m.Flags = (m.Flags & ~Configured) | ((ParticleBindingFlags)stock & Configured);

    private static bool SameDecided(ParticleBinding m, int stock)
        => (m.Flags & ~Configured) == ((ParticleBindingFlags)stock & ~Configured);

    private sealed class Live
    {
        public readonly ParticleBinding Model = new();
        public readonly Dictionary<int, int> MaterialIndex = [];
        public bool Simulating;
        public bool SimBegun;
        public bool FullBBox;
        public bool Changed;
        public Vector SortOrigin;
        public bool DrawBucket;
        public bool DrawBucketChosen;
        public readonly List<float> Keys = [];

        public int Mat(int logged)
        {
            if (!MaterialIndex.TryGetValue(logged, out int m))
                MaterialIndex[logged] = m = MaterialIndex.Count;
            Model.Material(m);
            return m;
        }
    }

    private sealed record Replay(int Updates, int Effects, int FirstBoxes, int Steps, int FullBoxes, int Changes, int Draws,
                                 int Sorts, int BucketSorts, int Swaps, int Posts, int LocalBoxes, List<string> Wrong);

    private static Vector V(string s)
    {
        string[] p = s.Split(',');
        return new Vector(F(p[0]), F(p[1]), F(p[2]));
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static bool Same(Vector a, Vector b) => a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

    private static string Text(Vector v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:R},{v.Y:R},{v.Z:R}");

    private static List<BoundParticle> ParseList(string[] c)
    {
        var list = new List<BoundParticle>();
        if (c.Length < 6)
            return list;
        foreach (string item in c[5].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = item.IndexOf(':');
            list.Add(new BoundParticle(I(item[..colon]), V(item[(colon + 1)..])));
        }
        return list;
    }

    private static readonly Lazy<Replay> s_replay = new(Run);

    private static Replay Run()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "particlemgr-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded particle manager is missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);

        var live = new Dictionary<string, Live>(StringComparer.Ordinal);
        var wrong = new List<string>();
        var rnd = new Queue<(int Lo, int Hi, int V)>();
        ushort frameCode = 1; // CParticleMgr::CParticleMgr sets m_FrameCode = 1
        int updates = 0, effects = 0, firstBoxes = 0, steps = 0, fullBoxes = 0, changes = 0, draws = 0, sorts = 0, buckets = 0, swaps = 0, posts = 0, localBoxes = 0;
        int lineNo = 0;

        int Random(int lo, int hi)
        {
            if (rnd.Count == 0)
            {
                wrong.Add($"line {lineNo}: the model drew RandomInt({lo},{hi}) and stock drew nothing");
                return -1;
            }
            (int l, int h, int v) = rnd.Dequeue();
            if (l != lo || h != hi)
                wrong.Add($"line {lineNo}: RandomInt({lo},{hi}) where stock drew ({l},{h})");
            return v;
        }

        void Check(bool ok, string what)
        {
            if (!ok && wrong.Count < 40)
                wrong.Add($"line {lineNo}: {what}");
        }

        void Compare(Live b, int mat, List<BoundParticle> stock, string tag)
        {
            List<BoundParticle> model = b.Model.Materials[mat];
            bool same = model.Count == stock.Count;
            for (int i = 0; same && i < model.Count; i++)
                same = model[i].Id == stock[i].Id && Same(model[i].Position, stock[i].Position);
            Check(same, $"{tag}: the model's list [{string.Join(' ', model.Take(12).Select(p => p.Id))}] where stock's is [{string.Join(' ', stock.Take(12).Select(p => p.Id))}]");
        }

        void BeginSim(Live b)
        {
            if (b.SimBegun)
                return;
            b.SimBegun = true;
            b.FullBBox = b.Model.BeginBBoxUpdate(Random);
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            if (line.StartsWith('#'))
                continue;
            string[] c = line.Split('\t');
            if (c.Length < 2 || c[0] != "pm")
                continue;
            Live? b = c.Length > 2 && live.TryGetValue(c[2], out Live? found) ? found : null;
            switch (c[1])
            {
                case "add":
                {
                    var n = new Live();
                    var flags = (ParticleBindingFlags)I(c[3]);
                    Check((flags & ~ParticleBindingFlags.AlwaysSimulate) == (n.Model.Flags & ~ParticleBindingFlags.AlwaysSimulate),
                          $"a new binding's flags {(int)n.Model.Flags} where stock's are {(int)flags}");
                    n.Model.Flags = flags;
                    live[c[2]] = n;
                    break;
                }
                case "remove":
                    live.Remove(c[2]);
                    break;
                case "cull":
                    b?.Model.SetParticleCullRadius(F(c[3]));
                    break;
                case "setbbox":
                    b?.Model.SetBBox(V(c[4]), V(c[5]), c[3] == "1");
                    break;
                case "setlocal":
                    b?.Model.SetLocalSpaceTransform(c[3].Split(',').Select(F).ToArray());
                    break;
                case "spawn":
                    b?.Model.AddParticle(0, I(c[3]), V(c[4]));
                    break;
                case "kill":
                    if (b != null)
                        Check(b.Model.RemoveParticle(I(c[3])), $"stock killed {c[3]}, which the model does not hold");
                    break;
                case "rnd":
                    rnd.Enqueue((I(c[2]), I(c[3]), I(c[4])));
                    break;
                case "update":
                    updates++;
                    break;
                case "effect":
                {
                    if (b == null)
                    {
                        Check(false, $"an update of {c[2]}, which was never added");
                        break;
                    }
                    effects++;
                    ParticleBinding m = b.Model;
                    Check(SameDecided(m, I(c[3])), $"flags {(int)m.Flags} where stock's are {c[3]}");
                    Configure(m, I(c[3]));
                    Check(m.ActiveParticles == I(c[4]), $"{m.ActiveParticles} particles where stock has {c[4]}");
                    Check(m.UpdateBBoxCounter == I(c[5]), $"bbox counter {m.UpdateBBoxCounter} where stock's is {c[5]}");
                    Check(Same(m.Min, V(c[6])) && Same(m.Max, V(c[7])), $"box {Text(m.Min)} {Text(m.Max)} where stock's is {c[6]} {c[7]}");
                    Check(Same(m.LastMin, V(c[8])) && Same(m.LastMax, V(c[9])), $"last box {Text(m.LastMin)} {Text(m.LastMax)} where stock's is {c[8]} {c[9]}");
                    b.SortOrigin = V(c[10]);
                    b.SimBegun = false;
                    b.Changed = false;
                    b.Simulating = !m.Has(ParticleBindingFlags.Remove) && !m.Has(ParticleBindingFlags.FirstFrame);
                    if (m.Has(ParticleBindingFlags.Remove))
                        break;
                    if (m.Has(ParticleBindingFlags.NeedsBBoxUpdate))
                    {
                        if (!m.Has(ParticleBindingFlags.AutoUpdateBBox) || m.RecalculateBoundingBox(b.SortOrigin))
                            m.Set(ParticleBindingFlags.NeedsBBoxUpdate, false);
                    }
                    m.Set(ParticleBindingFlags.Drawn, false);
                    // The update itself (spawns, SetBBox, the space) follows in the recording;
                    // a first frame skips the step and clears its flag.
                    if (m.Has(ParticleBindingFlags.FirstFrame))
                        m.Set(ParticleBindingFlags.FirstFrame, false);
                    break;
                }
                case "firstbbox":
                    if (b == null)
                        break;
                    firstBoxes++;
                    Check(b.Model.Has(ParticleBindingFlags.NeedsBBoxUpdate) == (c[3] == "1"), "the first box's needs-update flag");
                    Check(Same(b.Model.Min, V(c[4])) && Same(b.Model.Max, V(c[5])),
                          $"first box {Text(b.Model.Min)} {Text(b.Model.Max)} where stock's is {c[4]} {c[5]}");
                    break;
                case "simlist":
                    if (b == null)
                        break;
                    Check(b.Simulating, "stock stepped a binding the model would not");
                    BeginSim(b);
                    Compare(b, b.Mat(I(c[3])), ParseList(c), "before its step");
                    break;
                case "simbbox":
                {
                    if (b == null)
                        break;
                    steps++;
                    Check(b.Simulating, "stock stepped a binding the model would not");
                    BeginSim(b);
                    bool set = false;
                    if (b.FullBBox)
                    {
                        fullBoxes++;
                        if (!b.Model.LocalSpaceTransformIdentity)
                            localBoxes++;
                        set = b.Model.EndBBoxUpdate(b.SortOrigin);
                    }
                    Check(b.FullBBox == (c[3] == "1"), $"full box update {b.FullBBox} where stock's was {c[3]}");
                    Check(set == (c[4] == "1"), $"a particle set the box: {set}, stock {c[4]}");
                    Check(b.Model.UpdateBBoxCounter == I(c[5]), $"counter after the step {b.Model.UpdateBBoxCounter}, stock {c[5]}");
                    b.Simulating = false;
                    break;
                }
                case "changed":
                    if (b != null)
                        b.Changed = true;
                    break;
                case "after":
                {
                    if (b == null)
                        break;
                    Check(!b.Simulating, "the model would have stepped this binding and stock did not");
                    Configure(b.Model, I(c[3]));
                    bool changed = b.Model.DetectChanges(AabbBuffer);
                    if (changed)
                        changes++;
                    Check(changed == b.Changed, $"DetectChanges {changed} where stock's {b.Changed}");
                    Check(SameDecided(b.Model, I(c[3])), $"flags after {(int)b.Model.Flags}, stock {c[3]}");
                    Check(b.Model.ActiveParticles == I(c[4]), $"particles after {b.Model.ActiveParticles}, stock {c[4]}");
                    Check(Same(b.Model.Min, V(c[5])) && Same(b.Model.Max, V(c[6])), $"box after {Text(b.Model.Min)} {Text(b.Model.Max)}, stock {c[5]} {c[6]}");
                    Check(Same(b.Model.LastMin, V(c[7])) && Same(b.Model.LastMax, V(c[8])), $"last box after {Text(b.Model.LastMin)} {Text(b.Model.LastMax)}, stock {c[7]} {c[8]}");
                    break;
                }
                case "framecode":
                {
                    ++frameCode;
                    if (frameCode == 0)
                    {
                        foreach (Live l in live.Values)
                            l.Model.FrameCode = 0;
                        frameCode = 1;
                    }
                    Check(frameCode == I(c[2]), $"frame code {frameCode}, stock {c[2]}");
                    break;
                }
                case "draw":
                    if (b == null)
                        break;
                    draws++;
                    b.Model.Set(ParticleBindingFlags.Drawn, true);
                    Check(b.Model.ActiveParticles == I(c[4]), $"drawn with {b.Model.ActiveParticles} particles, stock {c[4]}");
                    Check(b.Model.FrameCode == I(c[6]), $"binding frame code {b.Model.FrameCode} at the draw, stock {c[6]}");
                    Check(frameCode == I(c[7]), $"manager frame code {frameCode} at the draw, stock {c[7]}");
                    b.DrawBucketChosen = false;
                    break;
                case "drawbefore":
                    if (b == null)
                        break;
                    Compare(b, b.Mat(I(c[3])), ParseList(c), "before its draw");
                    b.Keys.Clear();
                    break;
                case "drawmat":
                {
                    if (b == null)
                        break;
                    if (!b.DrawBucketChosen)
                    {
                        b.DrawBucket = ParticleBinding.ChooseBucketSort(Random);
                        b.DrawBucketChosen = true;
                        bool zero = b.Model.FrameCode == frameCode;
                        float frameTime = b.Model.DrawFrameTime(frameCode, F(c[5]));
                        Check(zero == (F(c[5]) == 0f) || !zero, $"frame time {frameTime} where stock's is {c[5]}");
                        Check(!zero || frameTime == 0f, "a second draw this frame kept a frame time");
                    }
                    Check(b.DrawBucket == (c[4] == "1"), $"bucket sort {b.DrawBucket} where stock's was {c[4]}");
                    break;
                }
                case "key":
                    b?.Keys.Add(F(c[4]));
                    break;
                case "drawafter":
                {
                    if (b == null)
                        break;
                    int mat = b.Mat(I(c[3]));
                    var before = b.Model.Materials[mat].Select(p => p.Id).ToList();
                    b.Model.SortAfterDraw(mat, b.Keys.ToArray(), b.DrawBucket);
                    sorts++;
                    if (b.DrawBucket)
                        buckets++;
                    else if (!before.SequenceEqual(b.Model.Materials[mat].Select(p => p.Id)))
                        swaps++;
                    Compare(b, mat, ParseList(c), b.DrawBucket ? "after a bucket sort" : "after the draw's swaps");
                    break;
                }
                case "post":
                    if (b == null)
                        break;
                    posts++;
                    Check(b.Model.Has(ParticleBindingFlags.Drawn) == (c[3] == "1"), $"drawn {b.Model.Has(ParticleBindingFlags.Drawn)}, stock {c[3]}");
                    b.Model.PostRender();
                    break;
            }
        }
        Check(rnd.Count == 0, $"{rnd.Count} of stock's RandomInt answers were never asked for");
        return new Replay(updates, effects, firstBoxes, steps, fullBoxes, changes, draws, sorts, buckets, swaps, posts, localBoxes, wrong);
    }

    [Fact]
    public void EveryDecisionTheStockManagerMadeTheModelMakesTheSame()
    {
        Replay r = s_replay.Value;
        Assert.True(r.Wrong.Count == 0, string.Join('\n', r.Wrong.Take(20)));
    }

    // The replay reached each decision (a check that saw none proves nothing).
    [Fact]
    public void TheRecordingReachesEveryDecision()
    {
        Replay r = s_replay.Value;
        Assert.True(r.Updates > 500, $"{r.Updates} updates");
        Assert.True(r.Effects > 5000, $"{r.Effects} binding updates");
        Assert.True(r.FirstBoxes > 0, $"{r.FirstBoxes} first boxes");
        Assert.True(r.Steps > 5000, $"{r.Steps} steps");
        Assert.True(r.FullBoxes > 100, $"{r.FullBoxes} full box updates");
        // Every local-space binding here (the muzzle flashes) sets its box by hand, so the
        // recording has no full update in a local space: ParticleBindingTests hold TransformAABB.
        Assert.True(r.Changes > 1000, $"{r.Changes} leaf system changes");
        Assert.True(r.Draws > 3000, $"{r.Draws} draws");
        Assert.True(r.BucketSorts > 100, $"{r.BucketSorts} bucket sorts");
        Assert.True(r.Swaps > 100, $"{r.Swaps} draws that swapped");
        Assert.True(r.Posts > 5000, $"{r.Posts} post-render passes");
    }
}
