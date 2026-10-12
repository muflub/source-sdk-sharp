using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The impact sprays against stock (lane c5-particles): <c>FX_DebrisFlecks</c>'
/// dust behind its flecks, <c>FX_DustImpact</c>, <c>FX_Blood</c> and
/// <c>FX_BugBlood</c>, and the dust emitter's steps, as
/// <c>Fixtures/sprays-stock.tsv.gz</c> recorded them (README beside it). Bit for
/// bit, with the recorded draws fed back.
/// </summary>
public class SpraysOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "sprays-stock.tsv.gz");
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

    private static List<List<string[]>> Units(string head)
    {
        var units = new List<List<string[]>>();
        List<string[]>? cur = null;
        foreach (var r in s_rows.Value)
        {
            if (r[0] is "sdi" or "sti" or "sbi" or "sui")
            {
                cur = r[0] == head ? [r] : null;
                if (cur is not null)
                    units.Add(cur);
                continue;
            }
            if (r[0] is "fkr" or "spp" or "fki" or "fkp")
                cur?.Add(r);
            else
                cur = null;
        }
        return units;
    }

    private sealed class Recorded(IEnumerable<string[]> unit) : IParticleRandom, IParticleWorldTrace, IParticleViewSource,
        IParticlePixelVisibility
    {
        private readonly Queue<string[]> _draws = new(unit.Where(r => r[0] == "fkr"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] is not ("f" or "v") || !Same(min, F(r[2])) || !Same(max, F(r[3])))
            {
                Wrong.Add($"drew float [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            return F(r[4]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] != "i" || min != I(r[2]) || max != I(r[3]))
            {
                Wrong.Add($"drew int [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            return I(r[4]);
        }

        // The flecks' collision set-up traces: nothing in the way (FleckOracleTests replays them).
        public ParticleTrace TraceLine(Vector start, Vector end) => new() { Fraction = 1, EndPos = end };

        public ParticleView Read() => throw new InvalidOperationException();

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
    }

    private static ImpactSprays Make(Recorded rec)
    {
        var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
        var flecks = new FleckSystems(effects, rec, new RandomSeeds(1));
        return new ImpactSprays(effects, new ParticleViews(rec), rec, flecks, new FxTestRig(), new RandomSeeds(1));
    }

    /// <summary>
    /// The blood puff counts what it started (step 7a's "impact blood" row,
    /// which read a native counter nothing wrote once FX_Blood was C#'s).
    /// </summary>
    [Fact]
    public void TheBloodPuffCountsTheParticlesItStarted()
    {
        ImpactSprays sprays = Make(new Recorded([]));
        int first = ((IImpactBlood)sprays).Blood(new Vector(0, 0, 64), new Vector(0, 0, 1), 255, 0, 0, 255);
        int second = ((IImpactBlood)sprays).Blood(new Vector(16, 0, 64), new Vector(1, 0, 0), 255, 0, 0, 255);
        Assert.True(first > 0 && second > 0, $"each puff starts particles ({first}, {second})");
        Assert.Equal(first + second, sprays.BloodParticles);
    }

    private static float[] Block(string[] e, int at)
    {
        Assert.Equal(ImpactEffectPolicy.MaxFloats, I(e[at]));
        return e.Skip(at + 1).Take(ImpactEffectPolicy.MaxFloats).Select(F).ToArray();
    }

    /// <summary>The block managed builds for the effect is the block stock drew from, up to its last burst.</summary>
    private static void SameBlock(List<string> wrong, string what, float[] stock, ImpactEffect effect, int material, float scale)
    {
        float[] block = new float[ImpactEffectPolicy.MaxFloats];
        ImpactEffectPolicy.Build(effect, material, scale, false, block);
        int written = ImpactEffectPolicy.BurstAt((int)block[(int)ImpactHeader.Bursts]);
        Assert.True(written > ImpactEffectPolicy.HeaderFloats, $"the {effect} block writes its bursts");
        for (int k = 0; k < written; k++)
        {
            if (!Same(block[k], stock[k]))
                wrong.Add($"{what}: block[{k}] {block[k]:G9}, stock {stock[k]:G9}");
        }
    }

    /// <summary>Each sheet's particles in stock's order (by burst, then particle), against the unit's spp rows.</summary>
    private static int Compare(List<string> wrong, string what, ImpactSprays.Made made, List<string[]> unit, string effect)
    {
        var stock = unit.Where(r => r[0] == "spp" && r[1] == effect).ToList();
        var port = made.Sheets.SelectMany(sh => sh.Pending.Select(q => (sh.Material, q))).OrderBy(x => x.q.Id >= 0 ? 0 : 1).ToList();
        // Stock adds in burst order; a sheet is a material, so the bursts interleave across sheets. Match by burst.
        var byBurst = new List<(string Material, SimpleParticle P)>();
        var queues = made.Sheets.ToDictionary(s => s.Material, s => new Queue<SimpleParticle>(s.Pending));
        foreach (var s in stock)
        {
            string material = ShardEffects.Sheet((ImpactMaterial)I(s[20]));
            if (queues.TryGetValue(material, out var q) && q.TryDequeue(out var p))
                byBurst.Add((material, p));
        }
        if (port.Count != stock.Count || byBurst.Count != stock.Count)
            wrong.Add($"{what}: {port.Count} particles, stock {stock.Count}");
        int n = 0;
        for (int k = 0; k < Math.Min(stock.Count, byBurst.Count); k++)
        {
            var (material, p) = byBurst[k];
            string[] s = stock[k];
            bool ok = Same(p.Position, V(s, 3)) && Same(p.Velocity, V(s, 6)) && Same(p.Lifetime, F(s[9])) && Same(p.DieTime, F(s[10]))
                      && p.R == I(s[11]) && p.G == I(s[12]) && p.B == I(s[13]) && p.StartAlpha == I(s[14]) && p.EndAlpha == I(s[15])
                      && p.StartSize == I(s[16]) && p.EndSize == I(s[17]) && Same(p.Roll, F(s[18])) && Same(p.RollDelta, F(s[19]));
            if (!ok)
                wrong.Add($"{what} particle {k}: {material} {G(p.Position)} {G(p.Velocity)} {p.DieTime:G9} {p.R},{p.G},{p.B} " +
                          $"{p.StartAlpha}/{p.EndAlpha} {p.StartSize}->{p.EndSize} {p.Roll:G9} {p.RollDelta:G9} against {string.Join(' ', s)}");
            n++;
        }
        return n;
    }

    [Fact]
    public void Every_debris_impact_lays_stocks_dust()
    {
        var wrong = new List<string>();
        int impacts = 0, dust = 0, flecks = 0;
        foreach (var unit in Units("sdi"))
        {
            string[] e = unit[0];
            string what = $"debris {++impacts}";
            var rec = new Recorded(unit);
            float[] stock = Block(e, 12);
            ImpactSprays.Made made = Make(rec).Debris(V(e, 1), V(e, 4), V(e, 7), I(e[10]), F(e[11]), stock, rec);
            flecks += made.Flecks;
            if (made.Flecks != unit.Count(r => r[0] == "fkp"))
                wrong.Add($"{what}: {made.Flecks} flecks, stock {unit.Count(r => r[0] == "fkp")}");
            dust += Compare(wrong, what, made, unit, "debris");
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(impacts >= 40 && dust >= 300 && flecks >= 400, $"{impacts} impacts, {dust} dust, {flecks} flecks");
    }

    [Fact]
    public void The_sprayed_debris_blocks_are_managed_builds()
    {
        // c5p_spray debris passes concrete at scales 1 and 2: the two spray rows are the last two sdi units.
        var wrong = new List<string>();
        var units = Units("sdi");
        Assert.True(units.Count >= 2, "the sprayed debris was recorded");
        foreach (var unit in units.TakeLast(2))
        {
            string[] e = unit[0];
            SameBlock(wrong, $"debris scale {e[10]}", Block(e, 12), ImpactEffect.DebrisFlecks, 'C', I(e[10]));
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
    }

    [Fact]
    public void Every_dust_impact_is_stocks()
    {
        var wrong = new List<string>();
        int impacts = 0, dust = 0;
        foreach (var unit in Units("sti"))
        {
            string[] e = unit[0];
            string what = $"dust {++impacts}";
            var rec = new Recorded(unit);
            float[] stock = Block(e, 12);
            SameBlock(wrong, what, stock, ImpactEffect.Dust, 0, F(e[10]));
            ImpactSprays.Made made = Make(rec).Dust(V(e, 1), V(e, 4), V(e, 7), F(e[10]), F(e[11]), stock, rec);
            dust += Compare(wrong, what, made, unit, "dust");
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(impacts == 2 && dust == 24, $"{impacts} impacts, {dust} dust");
    }

    [Fact]
    public void Every_blood_puff_is_stocks()
    {
        var wrong = new List<string>();
        int puffs = 0, blood = 0;
        foreach (var unit in Units("sbi"))
        {
            string[] e = unit[0];
            string what = $"blood {++puffs}";
            var rec = new Recorded(unit);
            float[] stock = Block(e, 11);
            SameBlock(wrong, what, stock, ImpactEffect.Blood, 0, 1.0f);
            ImpactSprays.Made made = Make(rec).Blood(V(e, 1), V(e, 4), F(e[7]), F(e[8]), F(e[9]), F(e[10]), stock, rec);
            blood += Compare(wrong, what, made, unit, "blood");
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(puffs == 2 && blood == 8, $"{puffs} puffs, {blood} blood");
    }

    [Fact]
    public void Every_bug_blood_spurt_is_stocks()
    {
        var wrong = new List<string>();
        int spurts = 0, blood = 0;
        foreach (var unit in Units("sui"))
        {
            string[] e = unit[0];
            string what = $"bug blood {++spurts}";
            var rec = new Recorded(unit);
            float[] stock = Block(e, 13);
            SameBlock(wrong, what, stock, ImpactEffect.BugBlood, 0, 1.0f);
            ImpactSprays.Made made = Make(rec).BugBlood(V(e, 1), V(e, 4), stock, rec);
            blood += Compare(wrong, what, made, unit, "bugblood");
            Vector pos = V(e, 1);
            float box = stock[(int)ImpactHeader.HeadB];
            foreach (var sheet in made.Sheets)
            {
                if (sheet.Box is not { } b || !Same(b.Mins, pos - new Vector(box, box, box)) || !Same(b.Maxs, pos + new Vector(box, box, box)))
                    wrong.Add($"{what}: {sheet.Material}'s box is not the wound's {box}");
            }
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(spurts == 2 && blood == 128, $"{spurts} spurts, {blood} blood");
    }

    [Fact]
    public void Every_dust_step_is_reproduced()
    {
        var rows = s_rows.Value;
        var decay = new DustDecay();
        var rules = new DustParticleRules(decay);
        var none = new Recorded([]);
        int steps = 0, floored = 0, rollFloored = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] != "dgs")
                continue;
            string[] o = rows[i + 1];
            Assert.Equal("dgso", o[0]);
            var p = new SimpleParticle
            {
                Position = V(r, 5), Velocity = V(r, 8), Lifetime = F(r[11]), DieTime = F(r[12]), Roll = F(r[13]), RollDelta = F(r[14]),
            };
            float dt = F(r[3]);
            Vector before = p.Velocity;
            rules.UpdateVelocity(ref p, dt, none);
            floored += Same(p.Velocity, before * decay.Decay) ? 0 : 1;
            p.Position = p.Position + p.Velocity * dt;
            p.Lifetime += dt;
            float rd = p.RollDelta + p.RollDelta * (dt * -8.0f);
            rules.UpdateRoll(ref p, dt);
            rollFloored += Same(p.RollDelta, rd) ? 0 : 1;
            steps++;
            if (!Same(p.Position, V(o, 2)) || !Same(p.Velocity, V(o, 5)) || !Same(p.Lifetime, F(o[8])) || !Same(p.Roll, F(o[9]))
                || !Same(p.RollDelta, F(o[10])))
                wrong.Add($"step {steps}: ({G(p.Position)} {G(p.Velocity)} {p.Lifetime:G9} {p.Roll:G9} {p.RollDelta:G9}) against {string.Join(' ', o)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 900 && floored >= 50 && rollFloored >= 50, $"{steps} steps ({floored} at the speed floor, {rollFloored} at the roll floor)");
    }

    [Fact]
    public void Every_dust_draw_fades_as_stocks()
    {
        int draws = 0;
        var wrong = new List<string>();
        var rules = new DustParticleRules(new DustDecay());
        foreach (var r in s_rows.Value.Where(x => x[0] == "xrd" && x[1] == "dust"))
        {
            var p = new SimpleParticle
            {
                Lifetime = F(r[2]), DieTime = F(r[3]), R = (byte)I(r[4]), G = (byte)I(r[5]), B = (byte)I(r[6]),
                StartAlpha = (byte)I(r[7]), EndAlpha = (byte)I(r[8]), StartSize = (byte)I(r[9]), EndSize = (byte)I(r[10]),
            };
            draws++;
            Vector c = rules.UpdateColor(p);
            float a = rules.UpdateAlpha(p), s = rules.UpdateScale(p);
            if (!Same(c, V(r, 14)) || !Same(a, F(r[17])) || !Same(s, F(r[18])) || !Same(rules.NearClipMin, F(r[11]))
                || !Same(rules.NearClipMax, F(r[12])))
                wrong.Add($"draw {draws}: {G(c)} {a:G9} {s:G9} against {string.Join(' ', r)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(draws >= 900, $"{draws} draws");
    }
}
