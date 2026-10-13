using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed weather (<see cref="PrecipitationSystem"/>) against stock's
/// CClient_Precipitation and CSnowFallManager, run offline from the pristine
/// c_effects.cpp by src/test/oracle/native/precipitation_oracle_test.cpp
/// (<c>Fixtures/precipitation-stock.tsv</c>, README beside it).
///
/// <para>
/// Every query the weather makes is answered from the recording and must ask
/// what stock asked: each draw by stream, kind and range, each trace by its
/// endpoints and mask. Every output is compared field by field, floats by their
/// bits: the rain and snow particles made each frame, the simple particles
/// added, the deaths, the splashes, the tracers and the snapshots.
/// </para>
/// </summary>
public class PrecipitationOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static bool Same(string expected, float actual)
        => BitConverter.SingleToInt32Bits(F(expected)) == BitConverter.SingleToInt32Bits(actual);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static string Show(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    // The recording's answers, consumed in order.
    private sealed class Replay(Queue<string[]> queries) : IPrecipitationWorld, IPrecipitationRandom
    {
        public Vector Wind;
        public int Frame;

        private string[] Next(string what)
        {
            Assert.True(queries.Count > 0, $"frame {Frame}: {what}, which stock did not ask");
            return queries.Dequeue();
        }

        private float Draw(char stream, char kind, float min, float max)
        {
            string[] r = Next($"a draw {stream}{kind}({Show(min)}, {Show(max)})");
            Assert.True(r[0] == "R" && r[1][0] == stream && r[2][0] == kind && Same(r[3], min) && Same(r[4], max),
                        $"frame {Frame}: drew {stream} {kind} ({Show(min)}, {Show(max)}), stock {string.Join(" ", r)}");
            return F(r[5]);
        }

        public float EngineFloat(float min, float max) => Draw('e', 'f', min, max);

        public int EngineInt(int min, int max) => (int)DrawInt('e', min, max);

        public float GlobalFloat(float min, float max) => Draw('g', 'f', min, max);

        public int GlobalInt(int min, int max) => (int)DrawInt('g', min, max);

        private int DrawInt(char stream, int min, int max)
        {
            string[] r = Next($"a draw {stream}i({min}, {max})");
            Assert.True(r[0] == "R" && r[1][0] == stream && r[2] == "i" && I(r[3]) == min && I(r[4]) == max,
                        $"frame {Frame}: drew {stream} i ({min}, {max}), stock {string.Join(" ", r)}");
            return I(r[5]);
        }

        public int CRand()
        {
            string[] r = Next("a rand()");
            Assert.True(r[0] == "R" && r[1] == "c", $"frame {Frame}: drew rand(), stock {string.Join(" ", r)}");
            return I(r[3]);
        }

        // The harness's world: solid below z = 0 and in the pillar.
        public int PointContents(in Vector p)
            => p.Z < 0 || (p.X >= 300 && p.X <= 400 && p.Y >= -100 && p.Y <= 100) ? 1 : 0;

        public PrecipitationTrace TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer)
        {
            string[] r = Next($"a trace (mask {mask})");
            Assert.True(r[0] == "L" && uint.Parse(r[1], CultureInfo.InvariantCulture) == mask
                        && Same(r[2], start.X) && Same(r[3], start.Y) && Same(r[4], start.Z)
                        && Same(r[5], end.X) && Same(r[6], end.Y) && Same(r[7], end.Z),
                        $"frame {Frame}: traced {mask} {Show(start.X)} {Show(start.Y)} {Show(start.Z)} -> {Show(end.X)} {Show(end.Y)} {Show(end.Z)}, stock {string.Join(" ", r)}");
            float t = F(r[8]);
            return new PrecipitationTrace
            {
                Fraction = t,
                EndPos = t < 1 ? start + (end - start) * t : end,
                SurfaceFlags = (ushort)I(r[9]),
            };
        }

        public PrecipitationTrace TraceVolume(in Vector start, in Vector end, in Vector hullMin, in Vector hullMax, PrecipitationVolume volume)
        {
            string[] r = Next("a volume trace");
            Assert.True(r[0] == "M" && Same(r[2], start.X) && Same(r[3], start.Y) && Same(r[4], start.Z)
                        && Same(r[5], end.X) && Same(r[6], end.Y) && Same(r[7], end.Z),
                        $"frame {Frame}: traced the volume {Show(start.X)} {Show(start.Y)} {Show(start.Z)} -> {Show(end.X)} {Show(end.Y)} {Show(end.Z)}, stock {string.Join(" ", r)}");
            bool hit = r[8] == "1";
            float t = F(r[9]);
            return new PrecipitationTrace
            {
                HitVolume = hit,
                Fraction = hit ? t : 1.0f,
                StartSolid = r[10] == "1",
                EndPos = hit ? start + (end - start) * t : end,
            };
        }

        public bool InPvs(PrecipitationVolume volume) => true;

        public Vector WindspeedAt(float time) => Wind;

        public int ScreenWidth => 1280;

        public Vector WorldMins => new(-4096, -4096, -4096);

        public Vector WorldMaxs => new(4096, 4096, 4096);
    }

    private static List<string[]> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "precipitation-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock weather is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(rows.Count(r => r[0] == "F") > 500 && rows.Count(r => r[0] == "R") > 10000
                    && rows.Count(r => r[0] == "C") > 300 && rows.Count(r => r[0] == "N") > 500,
                    "the oracle parsed");
        return rows;
    }

    // The harness's view: AngleVectors( 0, yaw, 0 ), world to view as Source's.
    private static void PlaceView(PrecipitationView view, PrecipitationPlayer player, float yaw)
    {
        MoveVec.AngleVectors(new Vector(0, yaw, 0), out Vector fwd, out Vector right, out Vector up);
        float[] m = view.WorldToView;
        Vector o = player.Eye;
        float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        m[0] = right.X; m[1] = right.Y; m[2] = right.Z; m[3] = -Dot(right, o);
        m[4] = up.X; m[5] = up.Y; m[6] = up.Z; m[7] = -Dot(up, o);
        m[8] = -fwd.X; m[9] = -fwd.Y; m[10] = -fwd.Z; m[11] = Dot(fwd, o);
        m[12] = 0; m[13] = 0; m[14] = 0; m[15] = 1;
        view.Forward = fwd;
        view.Origin = o;
        player.Forward = fwd;
    }

    private static void Expect(int frame, string what, IReadOnlyList<string[]> expected, IReadOnlyList<float[]> actual, Func<float[], string> show)
    {
        Assert.True(expected.Count == actual.Count,
                    $"frame {frame}: {actual.Count} {what}, stock {expected.Count}" +
                    (expected.Count > 0 ? $"; stock's first: {string.Join(" ", expected[0])}" : "") +
                    (actual.Count > 0 ? $"; managed's first: {show(actual[0])}" : ""));
        for (int i = 0; i < expected.Count; i++)
        {
            string[] e = expected[i];
            float[] a = actual[i];
            bool same = e.Length - 1 == a.Length && Enumerable.Range(0, a.Length).All(k => Same(e[k + 1], a[k]));
            Assert.True(same, $"frame {frame} {what} {i}: managed {show(a)}, stock {string.Join(" ", e)}");
        }
    }

    private static string Floats(float[] a) => string.Join(" ", a.Select(Show));

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void TheManagedWeatherReplaysStockBitExact()
    {
        var rows = Stock();
        var queries = new Queue<string[]>();
        var replay = new Replay(queries);
        var system = new PrecipitationSystem(replay, replay)
        {
            Settings = new PrecipitationSettings
            {
                WindDir = 225, WindSpeed = 60, RainRadius = 900, SnowParticles = 40,
            },
        };
        var view = new PrecipitationView();
        var byIndex = new Dictionary<int, PrecipitationVolume>();
        int compared = 0;

        int i = 0;
        float curTime = 0, dt = 0, yaw = 0;
        while (i < rows.Count)
        {
            string[] r = rows[i];
            switch (r[0])
            {
                case "P":
                case "B":
                    i++;
                    continue;
                case "D":
                    foreach (int k in byIndex.Keys.OrderByDescending(k => k).ToList())
                        system.Remove(byIndex[k]);
                    byIndex.Clear();
                    i++;
                    continue;
                case "E":
                {
                    var type = (PrecipitationType)I(r[2]);
                    byIndex[I(r[1])] = system.Create(type, I(r[3]), V(r, 4), V(r, 7));
                    i++;
                    if (type == PrecipitationType.SnowFall)
                    {
                        Assert.True(rows[i][0] == "B", "stock set the snowfall bounds");
                        var (min, max) = system.SnowFall!.Bounds;
                        Expect(-1, "snowfall bounds", [rows[i]], [[min.X, min.Y, min.Z, max.X, max.Y, max.Z]], Floats);
                        i++;
                    }

                    continue;
                }
                case "F":
                    break;
                default:
                    Assert.Fail($"row {i}: an unexpected {string.Join(" ", r)}");
                    break;
            }

            // A frame: F, then everything up to the next F, E, P or D.
            int frame = I(r[1]);
            curTime = F(r[2]);
            dt = F(r[3]);
            PrecipitationPlayer player = system.Player;
            player.Exists = true;
            player.Origin = V(r, 4);
            player.Eye = player.Origin + new Vector(0, 0, 64);
            player.Center = player.Origin + new Vector(0, 0, 36);
            player.Velocity = V(r, 7);
            yaw = F(r[10]);
            player.Fov = F(r[11]);
            player.DefaultFov = I(r[12]);
            replay.Wind = V(r, 13);
            replay.Frame = frame;
            PlaceView(view, player, yaw);
            i++;

            var expected = new Dictionary<string, List<string[]>>();
            queries.Clear();
            while (i < rows.Count && rows[i][0] is not ("F" or "E" or "P" or "D"))
            {
                string[] q = rows[i++];
                if (q[0] is "R" or "L" or "M")
                    queries.Enqueue(q);
                else
                    (expected.TryGetValue(q[0], out var l) ? l : expected[q[0]] = new()).Add(q);
            }

            List<string[]> Rows(string tag) => expected.TryGetValue(tag, out var l) ? l : new();

            if (frame < 0)
                continue;

            // The thinks.
            var simpleBefore = AllSimple(system).Select(p => p.Id).ToHashSet();
            system.Think(curTime, dt);

            // The rain and snow made this frame, by volume.
            var made = new List<float[]>();
            foreach (var (index, v) in byIndex.OrderBy(kv => kv.Key))
            {
                if (v.Type is not (PrecipitationType.Rain or PrecipitationType.Snow))
                    continue;
                foreach (var p in v.Particles.Where(p => p.SpawnTime == curTime))
                    made.Add([index, p.Pos.X, p.Pos.Y, p.Pos.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z, p.Mass, p.Ramp]);
            }

            Expect(frame, "particles made", Rows("C"), made, Floats);

            // The simple particles added, emitter by emitter, oldest first.
            var added = new List<string[]>();
            var addedActual = new List<float[]>();
            foreach (var (name, particles, _) in Emitters(system))
            {
                foreach (var p in particles.Reverse().Where(p => !simpleBefore.Contains(p.Id)))
                {
                    addedActual.Add([p.Id, p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z,
                                     p.Roll, p.DieTime, p.Lifetime, p.R, p.G, p.B, p.StartAlpha, p.EndAlpha, p.StartSize,
                                     p.EndSize, p.Flags, p.RollDelta, MaterialCode(name, p.Material)]);
                }
            }

            foreach (var n in Rows("N"))
            {
                // Drop the emitter number; carry the material name as a code at the end.
                added.Add([n[0], n[2], n[4], n[5], n[6], n[7], n[8], n[9], n[10], n[11], n[12], n[13], n[14], n[15],
                           n[16], n[17], n[18], n[19], n[20], n[21],
                           MaterialCode(n[3]).ToString(CultureInfo.InvariantCulture)]);
            }

            Expect(frame, "simple particles added", added, addedActual, Floats);

            // The particle steps.
            var died = new List<int>();
            var ashBefore = system.Volumes.Where(v => v.AshEmitter is not null)
                                  .SelectMany(v => v.AshEmitter!.Particles.Select(p => p.Id)).ToList();
            system.SimulateParticles(curTime, dt, died);
            var ashAfter = system.Volumes.Where(v => v.AshEmitter is not null)
                                 .SelectMany(v => v.AshEmitter!.Particles.Select(p => p.Id)).ToHashSet();
            died.AddRange(ashBefore.Where(id => !ashAfter.Contains(id)));
            Expect(frame, "simple particles died", Rows("X"), died.Select(d => new float[] { d }).ToList(), Floats);

            // The render.
            var tracers = new List<PrecipitationTracer>();
            var splashes = new List<PrecipitationSplash>();
            system.Render(view, curTime, tracers, splashes);
            Expect(frame, "splashes", Rows("W"),
                   splashes.Select(s => new[] { s.Origin.X, s.Origin.Y, s.Origin.Z, s.Scale, s.Lifetime, s.Alpha }).ToList(), Floats);
            if (Rows("T").Count > 0)
            {
                Expect(frame, "tracers", Rows("T"),
                       tracers.Select(t => new[] { t.Start.X, t.Start.Y, t.Start.Z, t.Delta.X, t.Delta.Y, t.Delta.Z, t.Width, t.Alpha }).ToList(),
                       Floats);
            }

            // The snapshots.
            if (Rows("Q").Count > 0)
            {
                var q = new List<float[]>();
                var s = new List<float[]>();
                foreach (var (index, v) in byIndex.OrderBy(kv => kv.Key))
                {
                    if (v.Type is not (PrecipitationType.Rain or PrecipitationType.Snow))
                        continue;
                    q.Add([index, v.Particles.Count, v.Remainder, v.Lifetime, system.Wind.Vector.X, system.Wind.Vector.Y]);
                    foreach (var p in v.Particles)
                        s.Add([index, p.Pos.X, p.Pos.Y, p.Pos.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z, p.Ramp]);
                }

                Expect(frame, "volume snapshots", Rows("Q"), q, Floats);
                Expect(frame, "volume particles", Rows("S"), s, Floats);
            }

            if (Rows("G").Count > 0 || Rows("H").Count > 0)
            {
                var g = new List<string[]>();
                var gActual = new List<float[]>();
                var h = new List<float[]>();
                foreach (var (name, particles, sortOrigin) in Emitters(system))
                {
                    if (particles.Count == 0)
                        continue;
                    gActual.Add([particles.Count, sortOrigin.X, sortOrigin.Y, sortOrigin.Z]);
                    foreach (var p in particles)
                    {
                        float alpha = name == "ashtray"
                            ? new AshDebrisRules().UpdateAlpha(p)
                            : new SimpleEmitterRules().UpdateAlpha(p);
                        h.Add([p.Id, p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z,
                               p.Roll, p.Lifetime, alpha]);
                    }
                }

                foreach (var row in Rows("G"))
                    g.Add([row[0], row[3], row[4], row[5], row[6]]);
                Expect(frame, "emitter snapshots", g, gActual, Floats);
                Expect(frame, "emitter particles", Rows("H"), h, Floats);
            }

            if (queries.Count > 0)
                Assert.Fail($"frame {frame}: {queries.Count} quer(ies) stock made and the managed weather did not; first {string.Join(" ", queries.Peek())}");
            compared++;
        }

        Assert.True(compared > 500, $"the replay compared {compared} frames");
    }

    private static int MaterialCode(string name) => name switch
    {
        "particle/snow" => 10,
        "effects/fleck_ash1" => 0,
        "effects/fleck_ash2" => 1,
        "effects/fleck_ash3" => 2,
        "effects/ember_swirling001" => 3,
        _ => -1,
    };

    private static int MaterialCode(string emitter, int material)
        => emitter == "snowfall" ? 10 : material;

    private static IEnumerable<SimpleParticle> AllSimple(PrecipitationSystem system)
        => Emitters(system).SelectMany(e => e.Particles);

    // The emitters in stock's creation order: the snowfall manager's, then each ash volume's.
    private static IEnumerable<(string Name, IReadOnlyList<SimpleParticle> Particles, Vector SortOrigin)> Emitters(PrecipitationSystem system)
    {
        if (system.SnowFall is { } snow)
            yield return ("snowfall", snow.Particles, snow.SortOrigin);
        foreach (var v in system.Volumes)
        {
            if (v.AshEmitter is { } ash)
                yield return ("ashtray", ash.Particles, v.AshSortOrigin);
        }
    }
}
