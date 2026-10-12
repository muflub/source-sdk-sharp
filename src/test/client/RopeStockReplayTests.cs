using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Client.Ropes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE MANAGED ROPE, AGAINST THE C++ IT REPLACES (plan_client_novalve.md §5.1,
/// cluster 6).
///
/// <para>
/// <c>Fixtures/rope-stock.tsv</c> is two of dm_lockdown's shower cables as the
/// native <c>C_RopeKeyframe</c> ran them, from creation through 240 thinks,
/// recorded by <c>tools/probes/client/c2-beams/c_rope.diff</c> before any of the
/// rope moved. The replay creates a managed rope from each <c>RC</c> row and
/// feeds it the same configuration, endpoint writes, frames and random draws in
/// the same order. It then requires every node, every piece of state and every
/// built segment to come out bit for bit as the engine's did. See
/// <c>Fixtures/README-rope-stock.md</c>.
/// </para>
/// </summary>
public class RopeStockReplayTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(in Vector a, in Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string S(in Vector v) => FormattableString.Invariant($"({v.X:R}, {v.Y:R}, {v.Z:R})");

    //-------------------------------------------------------------------------
    // The replay's sources: the draws and the light the engine answered.
    //-------------------------------------------------------------------------

    private sealed class RecordedRandom : IRopeRandom
    {
        public readonly Queue<(int Line, string Kind, float Lo, float Hi, Vector Value)> Draws = new();
        public readonly List<string> Mismatches = [];
        public int Consumed;

        private (int, string, float, float, Vector) Next(string kind, float lo, float hi)
        {
            if (Draws.Count == 0)
            {
                Mismatches.Add($"{kind}({lo}, {hi}) drawn with no recorded draw left");
                return (0, kind, lo, hi, default);
            }

            var d = Draws.Dequeue();
            Consumed++;
            if (d.Kind != kind || !Same(d.Lo, lo) || !Same(d.Hi, hi))
                Mismatches.Add($"line {d.Line}: recorded {d.Kind}({d.Lo}, {d.Hi}), drawn {kind}({lo}, {hi})");
            return d;
        }

        public float RandomFloat(float lo, float hi) => Next("RF", lo, hi).Item5.X;

        public Vector RandomVector(float lo, float hi) => Next("RV", lo, hi).Item5;
    }

    private sealed class RecordedWorld : IRopeWorld
    {
        public Vector[] Light = [];
        public int Calls;

        public RopeTrace TraceHull(in Vector start, in Vector end, in Vector mins, in Vector maxs)
            => throw new InvalidOperationException("the recorded ropes do not collide (ROPE_COLLIDE is off)");

        public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
        {
            boxColors.Clear();
            return Light.Length == 0 ? default : Light[Calls++ % Light.Length];
        }
    }

    //-------------------------------------------------------------------------
    // The replay, run once.
    //-------------------------------------------------------------------------

    private sealed class Rope
    {
        public readonly RopeSimulation Sim = new();
        public readonly RecordedRandom Random = new();
        public readonly RecordedWorld World = new();
        public RopeFrame Frame;
        public RopeSimSettings Settings = RopeSimSettings.Defaults;
        public readonly List<Vector[]> PredictedSnapshots = [];
        public int NodeRow;
    }

    private sealed class Result
    {
        public readonly List<string> NodeMismatches = [];
        public readonly List<string> StateMismatches = [];
        public readonly List<string> BuildMismatches = [];
        public readonly List<string> ChainMismatches = [];
        public readonly List<string> DrawMismatches = [];
        public int Nodes, States, Segments, Builds, Draws, WindThinks, Thinks;
    }

    private static readonly Lazy<Result> Replayed = new(Replay);

    private static string FixturePath => RepoLayout.Repo("src", "test", "managed", "Fixtures", "rope-stock.tsv");

    private static Result Replay()
    {
        Assert.True(File.Exists(FixturePath), "the recorded stock rope is missing: " + FixturePath);

        string[] lines = File.ReadAllLines(FixturePath);
        var ropes = new Dictionary<int, Rope>();
        var result = new Result();

        // The draws first: each rope's queue in recorded order.
        for (int i = 0; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            if (c[0] is "RF" or "RV")
            {
                int id = int.Parse(c[1], CultureInfo.InvariantCulture);
                if (!ropes.TryGetValue(id, out Rope? r))
                    ropes[id] = r = new Rope();
                Vector value = c[0] == "RF" ? new Vector(F(c[4]), 0, 0) : V(c, 4);
                r.Random.Draws.Enqueue((i + 1, c[0], F(c[2]), F(c[3]), value));
            }
        }

        // The light the engine answered: the first build's light values (they
        // are computed once, at init, and never change).
        for (int i = 0; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            if (c[0] != "BR")
                continue;
            int id = int.Parse(c[1], CultureInfo.InvariantCulture);
            Rope r = ropes.TryGetValue(id, out Rope? got) ? got : ropes[id] = new Rope();
            if (r.World.Light.Length != 0)
                continue;
            int n = int.Parse(c[9], CultureInfo.InvariantCulture);
            r.World.Light = new Vector[n];
            for (int k = 0; k < n; k++)
                r.World.Light[k] = V(lines[i + 1 + k].Split('\t'), 6);
        }

        BuildGroup? build = null;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 || lines[i][0] == '#')
                continue;

            string[] c = lines[i].Split('\t');
            int id = int.Parse(c[1], CultureInfo.InvariantCulture);
            if (!ropes.TryGetValue(id, out Rope? rope))
                ropes[id] = rope = new Rope();
            RopeSimulation sim = rope.Sim;
            int line = i + 1;

            switch (c[0])
            {
                case "RC":
                    sim.Create(F(c[4]), int.Parse(c[3], CultureInfo.InvariantCulture),
                               (RopeFlags)int.Parse(c[2], CultureInfo.InvariantCulture),
                               int.Parse(c[6], CultureInfo.InvariantCulture));
                    sim.ConstrainBetweenEndpoints = c[8] != "0";
                    sim.NewDataThisFrame = c[9] != "0";
                    sim.LockedPoints = (RopeLock)int.Parse(c[11], CultureInfo.InvariantCulture);
                    sim.Subdiv = int.Parse(c[12], CultureInfo.InvariantCulture);
                    sim.TextureScale = F(c[13]);
                    sim.CurrentGustTimer = F(c[14]);
                    sim.CurrentGustLifetime = F(c[15]);
                    sim.WindDir = V(c, 16);
                    sim.PreviousImpulse = V(c, 19);
                    break;

                case "RS":
                    sim.RopeLength = int.Parse(c[2], CultureInfo.InvariantCulture);
                    sim.Slack = int.Parse(c[3], CultureInfo.InvariantCulture);
                    sim.Width = F(c[4]);
                    sim.TextureScale = F(c[5]);
                    sim.Subdiv = int.Parse(c[6], CultureInfo.InvariantCulture);
                    sim.ScrollSpeed = F(c[7]);
                    sim.RecomputeSprings();
                    break;

                case "SP":
                    if (c[2] == "0")
                        sim.SetStartPos(V(c, 3));
                    else
                        sim.SetEndPos(V(c, 3));
                    break;

                case "RT":
                    rope.Frame = new RopeFrame(F(c[2]), F(c[3]), V(c, 4), V(c, 7));
                    rope.Settings = new RopeSimSettings(c[10] != "0", c[11] != "0",
                                                        int.Parse(c[13], CultureInfo.InvariantCulture), F(c[12]), true);
                    sim.Think(rope.Frame, rope.Settings, rope.Random, rope.World);
                    rope.NodeRow = 0;
                    rope.PredictedSnapshots.Add(sim.Physics.Nodes.ToArray().Select(n => n.Predicted).ToArray());
                    result.Thinks++;
                    break;

                case "RN":
                {
                    int k = int.Parse(c[2], CultureInfo.InvariantCulture);
                    RopeNode node = k < sim.Physics.NodeCount ? sim.Physics.Node(k) : default;
                    result.Nodes++;
                    if (!Same(node.Pos, V(c, 3)) || !Same(node.PrevPos, V(c, 6)) || !Same(node.Predicted, V(c, 9)))
                        result.NodeMismatches.Add($"line {line} rope {id} node {k}: stock pos {S(V(c, 3))} prev {S(V(c, 6))} " +
                                                  $"pred {S(V(c, 9))}; managed pos {S(node.Pos)} prev {S(node.PrevPos)} pred {S(node.Predicted)}");
                    break;
                }

                case "RX":
                {
                    result.States++;
                    if (c[3] != "0")
                        result.WindThinks++;
                    var want = new (string Name, bool Ok)[]
                    {
                        ("initted", (c[2] != "0") == sim.PhysicsInitted),
                        ("applyWind", (c[3] != "0") == sim.ApplyWind),
                        ("impulse", Same(V(c, 4), sim.Impulse)),
                        ("prevImpulse", Same(V(c, 7), sim.PreviousImpulse)),
                        ("gustTimer", Same(F(c[10]), sim.CurrentGustTimer)),
                        ("gustLifetime", Same(F(c[11]), sim.CurrentGustLifetime)),
                        ("nextGust", Same(F(c[12]), sim.TimeToNextGust)),
                        ("windDir", Same(V(c, 13), sim.WindDir)),
                        ("forceMove", int.Parse(c[16], CultureInfo.InvariantCulture) == sim.ForcePointMoveCounter),
                        ("prevLocked", int.Parse(c[17], CultureInfo.InvariantCulture) == (int)sim.PrevLockedPoints),
                        ("locked", int.Parse(c[18], CultureInfo.InvariantCulture) == (int)sim.LockedPoints),
                        ("touching", int.Parse(c[19], CultureInfo.InvariantCulture) == sim.LinksTouchingSomething),
                        ("mins", Same(V(c, 20), sim.RenderMins)),
                        ("maxs", Same(V(c, 23), sim.RenderMaxs)),
                        ("springLength", Same(F(c[26]), sim.Physics.SpringLength)),
                    };
                    foreach (var (name, ok) in want.Where(w => !w.Ok))
                        result.StateMismatches.Add($"line {line} rope {id}: {name} differs");
                    break;
                }

                case "DM":
                    sim.PrepareDraw(rope.Frame, rope.Settings, rope.Random, rope.World);
                    break;

                case "BR":
                    build = new BuildGroup(line, id, c);
                    break;

                case "BP":
                    build!.Predicted.Add(V(c, 3));
                    build.Light.Add(V(c, 6));
                    break;

                case "BS":
                    build!.Segments.Add(c);
                    break;

                case "BE":
                    CheckBuild(build!, c, ropes[build!.Rope], result);
                    build = null;
                    break;
            }
        }

        foreach (var (id, rope) in ropes)
        {
            result.Draws += rope.Random.Consumed;
            result.DrawMismatches.AddRange(rope.Random.Mismatches.Select(m => $"rope {id}: {m}"));
            if (rope.Random.Draws.Count != 0)
                result.DrawMismatches.Add($"rope {id}: {rope.Random.Draws.Count} recorded draw(s) never drawn, " +
                                          $"first at line {rope.Random.Draws.Peek().Line}");
        }

        return result;
    }

    private sealed class BuildGroup(int line, int rope, string[] header)
    {
        public readonly int Line = line;
        public readonly int Rope = rope;
        public readonly string[] Header = header;
        public readonly List<Vector> Predicted = [];
        public readonly List<Vector> Light = [];
        public readonly List<string[]> Segments = [];
    }

    private static void CheckBuild(BuildGroup b, string[] end, Rope rope, Result result)
    {
        string[] h = b.Header;
        result.Builds++;

        var view = new RopeBuildView(
            ViewForward: V(h, 3), ViewOrigin: V(h, 6),
            ScreenWidth: int.Parse(h[23], CultureInfo.InvariantCulture),
            FakeAA: h[22] != "0", FullBright: h[24] != "0",
            SubdivConVar: int.Parse(h[20], CultureInfo.InvariantCulture),
            SmoothEnlarge: F(h[26]), SmoothMinAlpha: F(h[27]), SmoothMaxAlpha: F(h[28]),
            SmoothMinWidth: F(h[29]), SmoothMaxAlphaWidth: F(h[30]));

        var input = new RopeBuildInput
        {
            Predicted = b.Predicted.ToArray(),
            LightValues = b.Light.ToArray(),
            ColorMod = V(h, 10),
            RopeLength = F(h[13]),
            Slack = F(h[14]),
            CurScroll = F(h[15]),
            Width = F(h[16]),
            TextureScale = F(h[17]),
            TextureHeight = int.Parse(h[18], CultureInfo.InvariantCulture),
            Subdiv = int.Parse(h[19], CultureInfo.InvariantCulture),
            Flags = (RopeFlags)int.Parse(h[21], CultureInfo.InvariantCulture),
        };

        var segs = new BeamSeg[RopeBuilder.MaxSegments];
        var back = new float[RopeBuilder.MaxSegments];
        int n = RopeBuilder.Build(input, view, segs, back, out float maxBack);

        int want = int.Parse(end[2], CultureInfo.InvariantCulture);
        if (n != want)
            result.BuildMismatches.Add($"line {b.Line} rope {b.Rope}: {n} segments built, stock built {want}");
        if (!Same(maxBack, F(end[3])))
            result.BuildMismatches.Add($"line {b.Line} rope {b.Rope}: max back width {maxBack:R}, stock {F(end[3]):R}");

        foreach (string[] s in b.Segments)
        {
            int k = int.Parse(s[2], CultureInfo.InvariantCulture);
            result.Segments++;
            BeamSeg got = k < n ? segs[k] : default;
            if (!Same(got.Pos, V(s, 3)) || !Same(got.Color, V(s, 6)) || !Same(got.TexCoord, F(s[9])) ||
                !Same(got.Alpha, F(s[10])) || !Same(got.Width, F(s[11])) || !Same(back[k], F(s[12])))
                result.BuildMismatches.Add($"line {b.Line} rope {b.Rope} segment {k}: stock pos {S(V(s, 3))} " +
                                           $"tex {F(s[9]):R} alpha {F(s[10]):R} width {F(s[11]):R} back {F(s[12]):R}; managed " +
                                           $"pos {S(got.Pos)} tex {got.TexCoord:R} alpha {got.Alpha:R} width {got.Width:R} back {back[k]:R}");
        }

        // The chain: every build drew positions the managed rope itself produced.
        Vector[] drawn = [.. b.Predicted];
        if (!rope.PredictedSnapshots.Any(p => p.Length == drawn.Length && p.Zip(drawn).All(z => Same(z.First, z.Second))))
            result.ChainMismatches.Add($"line {b.Line} rope {b.Rope}: the build read positions no managed think produced");

        if (!b.Light.Zip(rope.Sim.LightValues.ToArray()).All(z => Same(z.First, z.Second)))
            result.ChainMismatches.Add($"line {b.Line} rope {b.Rope}: the build read light the managed rope did not compute");
    }

    private static string First(List<string> m) => m.Count == 0 ? "" : $"{m.Count} mismatch(es), first: {m[0]}";

    //-------------------------------------------------------------------------
    // The facts.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheFixtureHoldsWindGustsAndBuilds()
    {
        Result r = Replayed.Value;

        Assert.True(r.Thinks >= 400, $"only {r.Thinks} thinks replayed");
        Assert.True(r.WindThinks >= 200, $"only {r.WindThinks} thinks with the wind on");
        Assert.True(r.Draws >= 8, $"only {r.Draws} random draws consumed");
        Assert.True(r.Segments >= 500, $"only {r.Segments} built segments compared");
    }

    [Fact]
    public void TheManagedRopeReproducesEveryRecordedNodeBitForBit()
    {
        Result r = Replayed.Value;

        Assert.True(r.Nodes > 0, "no node row was compared");
        Assert.True(r.NodeMismatches.Count == 0, First(r.NodeMismatches));
    }

    [Fact]
    public void TheManagedRopeReproducesEveryRecordedStateBitForBit()
    {
        Result r = Replayed.Value;

        Assert.True(r.States > 0, "no state row was compared");
        Assert.True(r.StateMismatches.Count == 0, First(r.StateMismatches));
    }

    [Fact]
    public void TheManagedRopeDrawsEveryRecordedRandomNumberInOrder()
    {
        Result r = Replayed.Value;

        Assert.True(r.Draws > 0, "no draw was consumed");
        Assert.True(r.DrawMismatches.Count == 0, First(r.DrawMismatches));
    }

    [Fact]
    public void TheManagedBuildReproducesEveryRecordedSegmentBitForBit()
    {
        Result r = Replayed.Value;

        Assert.True(r.Builds > 0, "no build was compared");
        Assert.True(r.BuildMismatches.Count == 0, First(r.BuildMismatches));
    }

    [Fact]
    public void EveryRecordedBuildReadWhatTheManagedRopeProduced()
    {
        Result r = Replayed.Value;

        Assert.True(r.Builds > 0, "no build was compared");
        Assert.True(r.ChainMismatches.Count == 0, First(r.ChainMismatches));
    }
}
