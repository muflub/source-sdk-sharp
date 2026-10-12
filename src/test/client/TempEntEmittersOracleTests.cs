using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The temp entities' muzzle-flash emitters against stock (lane c5-particles):
/// the shotgun NPC flash's embers, a <c>CEmberEffect</c>, as
/// <c>Fixtures/tempent-emitters-stock.tsv.gz</c> recorded their steps and
/// draws (README beside it). Bit for bit, with the recorded wander fed back.
/// </summary>
public class TempEntEmittersOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tempent-emitters-stock.tsv.gz");
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

    /// <summary>
    /// <c>Vector::Random</c>'s three <c>rand()</c> draws, given back as the
    /// recorded components: each is found as the <c>rand()</c> value whose
    /// <c>FRand</c> is that component bit for bit.
    /// </summary>
    private sealed class Wander(Vector offset) : IParticleRandom
    {
        private readonly Queue<float> _components = new([offset.X, offset.Y, offset.Z]);
        public readonly List<string> Wrong = [];

        public bool Drained => _components.Count == 0;

        public float RandomFloat(float min, float max)
        {
            Wrong.Add($"a float draw [{min}, {max}] stock did not take");
            return min;
        }

        public int RandomInt(int min, int max)
        {
            if (min != 0 || max != ParticleRandomExtensions.ValveRandMax || !_components.TryDequeue(out float want))
            {
                Wrong.Add($"an int draw [{min}, {max}] stock did not take");
                return min;
            }
            const float lo = -0.125f, hi = 0.125f;
            int guess = (int)MathF.Round((want - lo) / (hi - lo) * ParticleRandomExtensions.ValveRandMax);
            for (int r = Math.Max(0, guess - 3); r <= Math.Min(ParticleRandomExtensions.ValveRandMax, guess + 3); r++)
            {
                if (Same(lo + ((float)r / ParticleRandomExtensions.ValveRandMax) * (hi - lo), want))
                    return r;
            }
            Wrong.Add($"no rand() gives {want:G9}");
            return guess;
        }
    }

    [Fact]
    public void Every_ember_step_is_reproduced()
    {
        var rows = s_rows.Value;
        var rules = new EmberRules();
        int steps = 0;
        var wrong = new List<string>();
        for (int i = 0; i + 2 < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] != "egs")
                continue;
            string[] w = rows[i + 1];
            string[] o = rows[i + 2];
            Assert.True(w[0] == "egr" && o[0] == "egso" && o[1] == r[2], $"row {i}: a step without its wander and outcome");

            var emitter = new SimpleEmitter(rules);
            emitter.Add(new SimpleParticle
            {
                Id = 1, Position = V(r, 5), Velocity = V(r, 8), Lifetime = F(r[11]), DieTime = F(r[12]), Roll = F(r[13]), RollDelta = F(r[14]),
            });
            var wander = new Wander(V(w, 1));
            emitter.Simulate(F(r[3]), wander);
            steps++;

            bool died = o[11] == "1";
            if (died != (emitter.Particles.Count == 0))
                wrong.Add($"step {steps}: died {emitter.Particles.Count == 0}, stock {o[11]}");
            if (emitter.Particles.Count == 1)
            {
                SimpleParticle p = emitter.Particles[0];
                if (!Same(p.Position, V(o, 2)) || !Same(p.Velocity, V(o, 5)) || !Same(p.Lifetime, F(o[8])) || !Same(p.Roll, F(o[9])))
                    wrong.Add($"step {steps}: ({G(p.Position)} {G(p.Velocity)} {p.Lifetime:G9} {p.Roll:G9}) against {string.Join(' ', o)}");
            }
            wrong.AddRange(wander.Wrong.Select(x => $"step {steps}: {x}"));
            if (!wander.Drained)
                wrong.Add($"step {steps}: stock drew more");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 500, $"{steps} steps");
    }

    /// <summary>
    /// The draws, near clip included: <c>CEmberEffect</c> never calls
    /// <c>SetNearClip</c>, so it fades over <c>CSimpleEmitter</c>'s own 16 to
    /// 64 units (its constructor), as every emitter that does not set one does.
    /// </summary>
    [Fact]
    public void Every_ember_draw_darkens_and_fades_as_stocks()
    {
        int draws = 0;
        var wrong = new List<string>();
        var emitter = new SimpleEmitter(new EmberRules());
        foreach (var r in s_rows.Value.Where(x => x[0] == "xrd" && x[1] == "muzzle_embers"))
        {
            var p = new SimpleParticle
            {
                Lifetime = F(r[2]), DieTime = F(r[3]), R = (byte)I(r[4]), G = (byte)I(r[5]), B = (byte)I(r[6]),
                StartAlpha = (byte)I(r[7]), EndAlpha = (byte)I(r[8]), StartSize = (byte)I(r[9]), EndSize = (byte)I(r[10]),
            };
            ParticleQuad q = emitter.Draw(p);
            draws++;
            if (!Same(q.Color, V(r, 14)) || !Same(q.Alpha, F(r[17])) || !Same(q.Size, F(r[18]))
                || !Same(q.FadeNear, F(r[11])) || !Same(q.FadeFar, F(r[12])))
                wrong.Add($"draw {draws}: {G(q.Color)} {q.Alpha:G9} {q.Size:G9} near {q.FadeNear} {q.FadeFar} against {string.Join(' ', r)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(draws >= 500, $"{draws} draws");
    }
}
