using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// fx_water.cpp against stock (lane c5-particles): FX_GunshotSplash's drops
/// and gout, FX_GetSplashLighting, CSplashParticle's step and its clipped
/// alpha, as <c>Fixtures/watersplash-stock.tsv.gz</c> recorded them (README
/// beside it). Bit for bit, with the recorded draws and light fed back.
/// </summary>
public class WaterSplashOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "watersplash-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
                rows.Add(line.Split('\t'));
        }
        return rows;
    });

    private sealed class Recorded(IEnumerable<string[]> rows) : IParticleRandom
    {
        private readonly Queue<string[]> _draws = new(rows.Where(r => r[0] == "wsr"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] != "f")
            {
                Wrong.Add($"drew float [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            if (!Same(min, F(r[2])) || !Same(max, F(r[3])))
                Wrong.Add($"drew float [{min}, {max}], stock {string.Join(' ', r)}");
            return F(r[4]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] != "i")
            {
                Wrong.Add($"drew int [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            if (min != I(r[2]) || max != I(r[3]))
                Wrong.Add($"drew int [{min}, {max}], stock [{r[2]}, {r[3]}]");
            return I(r[4]);
        }
    }

    private sealed class Environment : ISplashEnvironment
    {
        public bool ShowSplashes { get; set; } = true;
        public Vector Light;
        public (float, Vector, Vector) Water = (1, default, default);
        public int Sounds;

        public Vector LightForPoint(Vector position) => Light;
        public (float Fraction, Vector End, Vector Normal) WaterTrace(Vector start, Vector end) => Water;
        public void SplashSound(Vector origin) => Sounds++;
    }

    private sealed class Nothing : IParticleViewSource, IParticleWorldTrace, IFxQuadSink, IClientParticleOneShots
    {
        public int Quads;
        public ParticleView Read() => throw new InvalidOperationException();
        public ParticleTrace TraceLine(Vector start, Vector end) => new() { Fraction = 1 };
        public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias, float startAlpha,
                            float endAlpha, float alphaBias, float yaw, float deltaYaw, Vector color, float lifeTime,
                            string material, FxQuadFlags flags) => Quads++;
        public string? NameFromIndex(int index) => null;
        public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points, Vector forward,
                          Vector right, Vector up) => true;
    }

    [Fact]
    public void The_splash_lighting_is_stocks()
    {
        int n = 0;
        var wrong = new List<string>();
        foreach (var r in s_rows.Value.Where(r => r[0] is "wsl" or "wrc"))
        {
            int at = r[0] == "wsl" ? 1 : 5;
            var (color, lum) = WaterMath.SplashLighting(V(r, at));
            if (!Same(color, V(r, at + 4)) || !Same(lum, F(r[at + 7])))
                wrong.Add($"{color} {lum} against {string.Join(' ', r)}");
            n++;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(n >= 30, $"{n} lightings");
    }

    [Fact]
    public void Every_gunshot_splash_makes_stocks_drops_and_gout()
    {
        var rows = s_rows.Value;
        int splashes = 0, drops = 0, gouts = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "wse")
                continue;
            int j = i + 1;
            while (j < rows.Count && rows[j][0] is "wsl" or "wsc" or "wsr" or "wsp" or "wss" or "wre" or "wsd")
                j++;
            var unit = rows.GetRange(i, j - i);
            string[] r = rows[i];
            splashes++;
            var recorded = new Recorded(unit);
            var env = new Environment { ShowSplashes = r[8] == "1", Light = V(unit.First(x => x[0] == "wsl"), 1) };
            var nothing = new Nothing();
            var splashesFx = new WaterSplashes(new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds()),
                                               nothing, new ParticleViews(nothing), env, nothing, nothing, new RandomSeeds(1));
            SplashBurst burst = splashesFx.GunshotSplash(V(r, 1), V(r, 4), F(r[7]), recorded);

            var stockDrops = unit.Where(x => x[0] == "wsp").ToList();
            var portDrops = burst.Drops?.Pending ?? [];
            if (portDrops.Count != stockDrops.Count)
                wrong.Add($"splash {splashes}: {portDrops.Count} drops, stock {stockDrops.Count}");
            for (int k = 0; k < Math.Min(portDrops.Count, stockDrops.Count); k++)
            {
                TrailParticle p = portDrops[k];
                string[] s = stockDrops[k];
                if (!Same(p.Position, V(s, 2)) || !Same(p.Lifetime, F(s[5])) || !Same(p.DieTime, F(s[6])) || !Same(p.Width, F(s[7]))
                    || !Same(p.Length, F(s[8])) || !Same(p.Velocity, V(s, 9)) || p.R != I(s[12]) || p.G != I(s[13]) || p.B != I(s[14]) || p.A != I(s[15]))
                    wrong.Add($"splash {splashes} drop {k}: ({p.Position} {p.DieTime} {p.Width} {p.Length} {p.Velocity} {p.R},{p.G},{p.B},{p.A}) against {string.Join(' ', s)}");
            }
            drops += stockDrops.Count;

            var stockGout = unit.Where(x => x[0] == "wss").ToList();
            var portGout = burst.Gout?.Pending ?? [];
            if (portGout.Count != stockGout.Count)
                wrong.Add($"splash {splashes}: {portGout.Count} gout particles, stock {stockGout.Count}");
            for (int k = 0; k < Math.Min(portGout.Count, stockGout.Count); k++)
            {
                SimpleParticle p = portGout[k];
                string[] s = stockGout[k];
                if (!Same(p.Position, V(s, 2)) || !Same(p.Velocity, V(s, 5)) || !Same(p.Lifetime, F(s[8])) || !Same(p.DieTime, F(s[9]))
                    || p.R != I(s[10]) || p.G != I(s[11]) || p.B != I(s[12]) || p.StartAlpha != I(s[13]) || p.EndAlpha != I(s[14])
                    || p.StartSize != I(s[15]) || p.EndSize != I(s[16]) || !Same(p.Roll, F(s[17])) || !Same(p.RollDelta, F(s[18])))
                    wrong.Add($"splash {splashes} gout {k}: ({p.Position} {p.Velocity} {p.R},{p.G},{p.B} {p.StartAlpha} {p.StartSize}->{p.EndSize} {p.Roll} {p.RollDelta}) against {string.Join(' ', s)}");
            }
            gouts += stockGout.Count;

            if (burst.Gout is { } g && (!g.Rules.UseClipHeight || !Same(g.Rules.ClipHeight, F(r[3]))))
                wrong.Add($"splash {splashes}: the gout is not clipped at the origin's height");
            if (env.Sounds != unit.Count(x => x[0] == "wsd"))
                wrong.Add($"splash {splashes}: {env.Sounds} sounds, stock {unit.Count(x => x[0] == "wsd")}");
            if (!recorded.Drained)
                wrong.Add($"splash {splashes}: stock drew more");
            wrong.AddRange(recorded.Wrong.Select(w => $"splash {splashes}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(splashes >= 20 && drops >= 300 && gouts >= 150, $"{splashes} splashes, {drops} drops, {gouts} gout particles");
    }

    [Fact]
    public void Every_recorded_gout_step_is_reproduced()
    {
        var rows = s_rows.Value;
        int steps = 0, clipped = 0, removed = 0;
        var wrong = new List<string>();
        var none = new Recorded([]);
        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] != "wst")
                continue;
            string[] o = rows[i + 1];
            Assert.Equal("wsto", o[0]);
            steps++;
            var rules = new SplashRules { UseClipHeight = r[4] == "1", ClipHeight = F(r[5]) };
            var p = new SimpleParticle
            {
                Position = V(r, 6), Velocity = V(r, 9), Lifetime = F(r[12]), DieTime = F(r[13]), Roll = F(r[14]),
                RollDelta = F(r[15]), StartSize = (byte)I(r[16]), EndSize = (byte)I(r[17]),
            };
            bool alive = rules.Step(ref p, F(r[3]), none);
            if (o[2] is "clipped" or "removed")
            {
                clipped += o[2] == "clipped" ? 1 : 0;
                removed += o[2] == "removed" ? 1 : 0;
                if (alive)
                    wrong.Add($"step {steps}: alive, stock {o[2]}");
            }
            else if (!alive || !Same(p.Position, V(o, 2)) || !Same(p.Velocity, V(o, 5)) || !Same(p.Lifetime, F(o[8]))
                     || !Same(p.Roll, F(o[9])) || !Same(p.RollDelta, F(o[10])))
            {
                wrong.Add($"step {steps}: ({p.Position} {p.Velocity} {p.Lifetime} {p.Roll} {p.RollDelta}) against {string.Join(' ', o)}");
            }
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 1000 && clipped >= 150, $"{steps} steps ({clipped} clipped, {removed} died)");
    }

    [Fact]
    public void Every_recorded_gout_alpha_is_reproduced()
    {
        int n = 0, partial = 0;
        var wrong = new List<string>();
        foreach (var r in s_rows.Value.Where(r => r[0] == "wsa"))
        {
            var rules = new SplashRules { UseClipHeight = true, ClipHeight = F(r[2]) };
            var p = new SimpleParticle
            {
                Position = new Vector(0, 0, F(r[1])), StartAlpha = (byte)I(r[3]), StartSize = (byte)I(r[4]), EndSize = (byte)I(r[5]),
                Lifetime = F(r[6]), DieTime = F(r[7]),
            };
            float scale = rules.UpdateScale(p), alpha = rules.UpdateAlpha(p);
            if (!Same(scale, F(r[9])) || !Same(alpha, F(r[10])))
                wrong.Add($"{scale} {alpha} against {string.Join(' ', r)}");
            n++;
            float stock = F(r[10]);
            partial += stock > 0 && stock < I(r[3]) / 255.0f ? 1 : 0;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(n >= 1000 && partial >= 50, $"{n} alphas, {partial} part-faded");
    }
}
