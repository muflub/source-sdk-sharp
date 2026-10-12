using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// fx_blood.cpp against stock (lane c5-particles): FX_BloodSpray,
/// FX_BloodBulletImpact, BloodImpactCallback's choice and HunterDamageCallback's
/// flecks, and CBloodSprayEmitter's steps through its shared decay, as
/// <c>Fixtures/bloodspray-stock.tsv.gz</c> recorded them (README beside it).
/// Bit for bit, with the recorded draws, light and traces fed back.
/// </summary>
public class BloodOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "bloodspray-stock.tsv.gz");
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

    private sealed class Recorded(IEnumerable<string[]> rows) : IParticleRandom, IParticleWorldTrace
    {
        private readonly Queue<string[]> _draws = new(rows.Where(r => r[0] is "br" or "pcr"));
        private readonly Queue<string[]> _traces = new(rows.Where(r => r[0] == "pct"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0 && _traces.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r))
            {
                Wrong.Add($"a float draw [{min}, {max}] stock did not take");
                return min;
            }
            bool tagged = r[0] == "br";
            int at = tagged ? 2 : 1;
            if ((tagged && r[1] != "f") || !Same(min, F(r[at])) || !Same(max, F(r[at + 1])))
                Wrong.Add($"drew float [{min}, {max}], stock {string.Join(' ', r)}");
            return F(r[at + 2]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[0] != "br" || r[1] != "i")
            {
                Wrong.Add($"drew int [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            if (min != I(r[2]) || max != I(r[3]))
                Wrong.Add($"drew int [{min}, {max}], stock [{r[2]}, {r[3]}]");
            return I(r[4]);
        }

        public ParticleTrace TraceLine(Vector start, Vector end)
        {
            if (!_traces.TryDequeue(out var r))
            {
                Wrong.Add("a trace stock did not make");
                return new ParticleTrace { Fraction = 1 };
            }
            if (!Same(start, V(r, 1)) || !Same(end, V(r, 4)))
                Wrong.Add($"traced {start} -> {end}, stock {V(r, 1)} -> {V(r, 4)}");
            return new ParticleTrace { Fraction = F(r[8]), EndPos = V(r, 9), Normal = V(r, 12), Dist = F(r[15]), AllSolid = r[16] == "1", StartSolid = r[17] == "1" };
        }
    }

    private sealed class Environment : IBloodEnvironment
    {
        public bool LowViolence { get; set; }
        public Vector Light;
        public int MaxClients { get; set; } = 1;
        public Vector LightForPoint(Vector position) => Light;
    }

    private sealed class Nothing : IParticleViewSource, IParticlePixelVisibility, IClientParticleOneShots
    {
        public ParticleView Read() => throw new InvalidOperationException();
        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
        public string? NameFromIndex(int index) => null;
        public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points, Vector forward,
                          Vector right, Vector up) => true;
    }

    private static BloodEffects Blood(IParticleWorldTrace world, Environment env)
    {
        var none = new Nothing();
        return new BloodEffects(new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds()), world,
                                new ParticleViews(none), none, env, none, new RandomSeeds(1));
    }

    private static IEnumerable<List<string[]>> Units(string head)
    {
        var rows = s_rows.Value;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != head)
                continue;
            int j = i + 1;
            while (j < rows.Count && rows[j][0] is "bl" or "br" or "btp" or "bsp" or "b3p" or "pcs" or "pct" or "pcp" or "pcr" or "bgc")
                j++;
            yield return rows.GetRange(i, j - i);
        }
    }

    private static void CompareTrails(IReadOnlyList<TrailParticle> port, List<string[]> stock, string what, List<string> wrong)
    {
        if (port.Count != stock.Count)
            wrong.Add($"{what}: {port.Count} drops, stock {stock.Count}");
        for (int k = 0; k < Math.Min(port.Count, stock.Count); k++)
        {
            TrailParticle p = port[k];
            string[] s = stock[k];
            if (!Same(p.Position, V(s, 1)) || !Same(p.Lifetime, F(s[4])) || !Same(p.DieTime, F(s[5])) || !Same(p.Width, F(s[6]))
                || !Same(p.Length, F(s[7])) || !Same(p.Velocity, V(s, 8)) || p.R != I(s[11]) || p.G != I(s[12]) || p.B != I(s[13]) || p.A != I(s[14]))
                wrong.Add($"{what} drop {k}: ({G(p.Position)} {p.DieTime} {p.Width} {p.Length} {G(p.Velocity)} {p.R},{p.G},{p.B},{p.A}) against {string.Join(' ', s)}");
        }
    }

    private static void CompareSimple(IReadOnlyList<SimpleParticle> port, List<string[]> stock, string what, List<string> wrong)
    {
        if (port.Count != stock.Count)
            wrong.Add($"{what}: {port.Count} particles, stock {stock.Count}");
        for (int k = 0; k < Math.Min(port.Count, stock.Count); k++)
        {
            SimpleParticle p = port[k];
            string[] s = stock[k];
            if (!Same(p.Position, V(s, 1)) || !Same(p.Velocity, V(s, 4)) || !Same(p.Lifetime, F(s[7])) || !Same(p.DieTime, F(s[8]))
                || p.R != I(s[9]) || p.G != I(s[10]) || p.B != I(s[11]) || p.StartAlpha != I(s[12]) || p.EndAlpha != I(s[13])
                || p.StartSize != I(s[14]) || p.EndSize != I(s[15]) || !Same(p.Roll, F(s[16])) || !Same(p.RollDelta, F(s[17])))
                wrong.Add($"{what} {k}: ({G(p.Position)} {G(p.Velocity)} {p.DieTime} {p.R},{p.G},{p.B} {p.StartAlpha} {p.StartSize}->{p.EndSize} {p.Roll}) against {string.Join(' ', s)}");
        }
    }

    [Fact]
    public void Every_blood_spray_makes_stocks_drops_gore_and_cloud()
    {
        int sprays = 0, drops = 0, simple = 0;
        var wrong = new List<string>();
        foreach (var unit in Units("bse"))
        {
            string[] r = unit[0];
            sprays++;
            var rec = new Recorded(unit);
            var env = new Environment { LowViolence = r[12] == "1", Light = unit.FirstOrDefault(x => x[0] == "bl") is { } bl ? V(bl, 1) : default };
            BloodBurst burst = Blood(rec, env).BloodSpray(V(r, 1), V(r, 4), F(r[7]), (byte)I(r[8]), (byte)I(r[9]), (byte)I(r[10]), I(r[11]), rec);
            string what = $"spray {sprays}";
            var stockDrops = unit.Where(x => x[0] == "btp").ToList();
            CompareTrails(burst.Drops?.Pending ?? [], stockDrops, what, wrong);
            drops += stockDrops.Count;
            foreach (var group in unit.Where(x => x[0] == "bsp").GroupBy(x => x[^1]))
            {
                var sheet = burst.Sheets.FirstOrDefault(s => s.Material == group.Key);
                CompareSimple(sheet?.Pending ?? [], group.ToList(), $"{what} {group.Key}", wrong);
                simple += group.Count();
            }
            if (burst.Sheets.Count != unit.Where(x => x[0] == "bsp").Select(x => x[^1]).Distinct().Count())
                wrong.Add($"{what}: {burst.Sheets.Count} sheets");
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(sprays >= 40 && drops >= 600 && simple >= 200, $"{sprays} sprays, {drops} drops, {simple} gore and cloud");
    }

    [Fact]
    public void Every_bullet_impact_makes_stocks_core_gore_and_colliding_drops()
    {
        int impacts = 0;
        var wrong = new List<string>();
        foreach (var unit in Units("bbe"))
        {
            string[] r = unit[0];
            impacts++;
            var rec = new Recorded(unit);
            var env = new Environment { LowViolence = r[12] == "1", MaxClients = I(r[11]), Light = V(unit.First(x => x[0] == "bl"), 1) };
            BloodBurst burst = Blood(rec, env).BloodBulletImpact(V(r, 1), V(r, 4), F(r[7]), (byte)I(r[8]), (byte)I(r[9]), (byte)I(r[10]), rec);
            string what = $"impact {impacts}";
            CompareTrails(burst.Drops?.Pending ?? [], unit.Where(x => x[0] == "btp").ToList(), what, wrong);
            var simple = unit.Where(x => x[0] == "bsp").ToList();
            var port = burst.Sheets.SelectMany(s => s.Pending).ToList();
            CompareSimple(port, simple, what, wrong);
            var pcp = unit.First(x => x[0] == "pcp");
            if (burst.Drops is not { } d || d.Collision.ActivePlanes != I(pcp[2]) || (d.Flags & TrailFlags.Collide) == 0)
                wrong.Add($"{what}: the drops' collision differs from {string.Join(' ', pcp)}");
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew or traced more");
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(impacts >= 4, $"{impacts} impacts");
    }

    [Fact]
    public void Every_hunter_damage_throws_stocks_flecks()
    {
        int hits = 0, flecks = 0;
        var wrong = new List<string>();
        foreach (var unit in Units("bhd"))
        {
            string[] r = unit[0];
            hits++;
            var rec = new Recorded(unit);
            Simple3DSystem system = Blood(rec, new Environment()).HunterDamage(V(r, 1), V(r, 4), rec);
            var stock = unit.Where(x => x[0] == "b3p").ToList();
            if (system.Made.Count != stock.Count)
                wrong.Add($"hunter {hits}: {system.Made.Count} flecks, stock {stock.Count}");
            for (int k = 0; k < Math.Min(stock.Count, system.Made.Count); k++)
            {
                Particle3D p = system.Made[k];
                string[] s = stock[k];
                if (!Same(p.Position, V(s, 1)) || !Same(p.Velocity, V(s, 4)) || !Same(p.Angles, V(s, 7)) || !Same(p.AngSpeed, F(s[10]))
                    || !Same(p.LifeRemaining, F(s[11])) || p.Size != I(s[12]) || p.FrontR != I(s[13]) || p.BackR != I(s[16]))
                    wrong.Add($"hunter {hits} fleck {k}: ({G(p.Position)} {G(p.Velocity)} {G(p.Angles)} {p.AngSpeed} {p.LifeRemaining} {p.Size} {p.FrontR} {p.BackR}) against {string.Join(' ', s)}");
            }
            flecks += stock.Count;
            var pcp = unit.First(x => x[0] == "pcp");
            if (system.Collision.ActivePlanes != I(pcp[2]))
                wrong.Add($"hunter {hits}: {system.Collision.ActivePlanes} planes, stock {pcp[2]}");
            if (!rec.Drained)
                wrong.Add($"hunter {hits}: stock drew or traced more");
            wrong.AddRange(rec.Wrong.Select(w => $"hunter {hits}: {w}"));
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(hits >= 2 && flecks >= 60, $"{hits} hits, {flecks} flecks");
    }

    [Fact]
    public void Every_blood_impact_names_stocks_system_along_the_reversed_normal()
    {
        int n = 0;
        var wrong = new List<string>();
        foreach (var r in s_rows.Value.Where(x => x[0] == "bic"))
        {
            n++;
            if (BloodEffects.ImpactSystem(I(r[1])) != r[2])
                wrong.Add($"colour {r[1]}: {BloodEffects.ImpactSystem(I(r[1]))}, stock {r[2]}");
            Vector n3 = V(r, 6);
            Vector angles = ParticleCollision.VectorAngles(new Vector(-n3.X, -n3.Y, -n3.Z));
            if (!Same(angles, V(r, 9)))
                wrong.Add($"angles {G(angles)} against {string.Join(' ', r)}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(n >= 12, $"{n} impacts");
        Assert.Null(BloodEffects.ImpactSystem(BloodEffects.Mech));
    }

    [Fact]
    public void Every_gore_step_is_reproduced_through_the_shared_decay()
    {
        var rows = s_rows.Value;
        var gravity = new Dictionary<string, float>();
        var decay = new BloodDecay();
        var none = new Recorded([]);
        int steps = 0, frozen = 0, unknown = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] == "bgc")
            {
                gravity[r[1]] = F(r[2]);
                continue;
            }
            if (r[0] != "bgs")
                continue;
            string[] o = rows[i + 1];
            Assert.Equal("bgso", o[0]);
            var rules = new BloodSprayRules(decay);
            if (gravity.TryGetValue(r[1], out float g))
                rules.Gravity = g;
            else
                unknown++;
            var p = new SimpleParticle
            {
                Flags = unchecked((byte)I(r[4])), Position = V(r, 5), Velocity = V(r, 8), Lifetime = F(r[11]), DieTime = F(r[12]),
                Roll = F(r[13]), RollDelta = F(r[14]),
            };
            frozen += (p.Flags & BloodSprayRules.FlagNoVelocityDecay) != 0 ? 1 : 0;
            float dt = F(r[3]);
            rules.UpdateVelocity(ref p, dt, none);
            p.Position = p.Position + p.Velocity * dt;
            p.Lifetime += dt;
            rules.UpdateRoll(ref p, dt);
            steps++;
            if (!Same(p.Position, V(o, 2)) || !Same(p.Velocity, V(o, 5)) || !Same(p.Lifetime, F(o[8])) || !Same(p.Roll, F(o[9]))
                || !Same(p.RollDelta, F(o[10])) || (p.Lifetime >= p.DieTime) != (o[11] == "1"))
                wrong.Add($"step {steps}: ({G(p.Position)} {G(p.Velocity)} {p.Lifetime:G9} {p.Roll:G9}) against {string.Join(' ', o)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 8000 && frozen >= 1000 && steps - frozen >= 5000 && unknown == 0,
            $"{steps} steps ({frozen} with stock's uninitialised no-decay bit set, {unknown} of an unknown emitter)");
    }
}
