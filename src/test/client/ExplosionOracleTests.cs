using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// fx_explosion.cpp against stock (lane c5-particles): the three explosions'
/// force probes, sounds, core, sparks, flecks, water shafts and splash, and
/// CExplosionParticle's steps and draws, as
/// <c>Fixtures/explosion-stock.tsv.gz</c> recorded them (README beside it).
/// Bit for bit, with the recorded draws, traces and light fed back.
/// </summary>
public class ExplosionOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "explosion-stock.tsv.gz");
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

    /// <summary>The recorded world: draws, the explosion's traces and lights, the collision's traces, each checked.</summary>
    private sealed class Recorded : IParticleRandom, IParticleWorldTrace, IExplosionEnvironment
    {
        private readonly Queue<string[]> _draws;
        private readonly Queue<string[]> _probes;
        private readonly Queue<string[]> _collision;
        private readonly Queue<Vector> _lights;
        private readonly Queue<MaskTrace> _water = new();
        public readonly List<string> Sounds = [];
        public readonly List<string> Wrong = [];

        public Recorded(List<string[]> unit)
        {
            _draws = new(unit.Where(r => r[0] is "xr" or "pcr"));
            _probes = new(unit.Where(r => r[0] == "xpt"));
            _collision = new(unit.Where(r => r[0] == "pct"));
            _lights = new(unit.Where(r => r[0] == "xl").Select(r => V(r, 1)));
            if (unit.FirstOrDefault(r => r[0] == "xwt") is { } w)
            {
                Vector surface = V(w, 4);
                if (w[1] == "1")
                {
                    _water.Enqueue(new MaskTrace(F(w[2]), surface, default, true, F(w[3])));
                }
                else
                {
                    _water.Enqueue(new MaskTrace(1, default, default, false, 0));
                    _water.Enqueue(new MaskTrace(F(w[2]), surface, default, false, F(w[3])));
                }
            }
        }

        public bool Drained => _draws.Count == 0 && _probes.Count == 0 && _collision.Count == 0 && _lights.Count == 0 && _water.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r))
            {
                Wrong.Add($"a float draw [{min}, {max}] stock did not take");
                return min;
            }
            bool tagged = r[0] == "xr";
            int at = tagged ? 2 : 1;
            if ((tagged && r[1] != "f") || !Same(min, F(r[at])) || !Same(max, F(r[at + 1])))
                Wrong.Add($"drew float [{min}, {max}], stock {string.Join(' ', r)}");
            return F(r[at + 2]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[0] != "xr" || r[1] != "i")
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
            if (!_collision.TryDequeue(out var r))
            {
                Wrong.Add("a collision trace stock did not make");
                return new ParticleTrace { Fraction = 1 };
            }
            if (!Same(start, V(r, 1)) || !Same(end, V(r, 4)))
                Wrong.Add($"traced {start} -> {end}, stock {V(r, 1)} -> {V(r, 4)}");
            return new ParticleTrace { Fraction = F(r[8]), EndPos = V(r, 9), Normal = V(r, 12), Dist = F(r[15]), AllSolid = r[16] == "1", StartSolid = r[17] == "1" };
        }

        public MaskTrace Trace(Vector start, Vector end, uint mask)
        {
            if (mask == IParticleMaskTrace.MaskWater)
            {
                if (_water.TryDequeue(out var w))
                    return w;
                Wrong.Add("a water trace stock did not make");
                return new MaskTrace(1, end, default, false, 0);
            }
            if (!_probes.TryDequeue(out var r))
            {
                Wrong.Add("a probe stock did not make");
                return new MaskTrace(1, end, default, false, 0);
            }
            if (mask != IParticleMaskTrace.ContentsSolid || !Same(start, V(r, 1)) || !Same(end, V(r, 4)))
                Wrong.Add($"probed {start} -> {end} ({mask}), stock {string.Join(' ', r)}");
            return new MaskTrace(F(r[7]), default, default, false, 0);
        }

        public Vector LightForPoint(Vector position)
        {
            if (_lights.TryDequeue(out var l))
                return l;
            Wrong.Add("a light stock did not read");
            return default;
        }

        public void Sound(string script, Vector origin) => Sounds.Add(script);
    }

    private sealed class Sinks : IFxQuadSink, IFxLineSink, IParticleViewSource, IParticlePixelVisibility
    {
        public readonly List<(float Yaw, float DeltaYaw, float Alpha)> Quads = [];
        public readonly List<FxLineData> Lines = [];

        public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias, float startAlpha,
                            float endAlpha, float alphaBias, float yaw, float deltaYaw, Vector color, float lifeTime,
                            string material, FxQuadFlags flags) => Quads.Add((yaw, deltaYaw, startAlpha));

        public void AddLine(FxLineData data, string material) => Lines.Add(data);

        public ParticleView Read() => throw new InvalidOperationException();

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
    }

    private static List<List<string[]>> Units()
    {
        var rows = s_rows.Value;
        var units = new List<List<string[]>>();
        List<string[]>? cur = null;
        foreach (var r in rows)
        {
            if (r[0] == "xwt" || (r[0] == "xe" && (cur is null || cur.Any(x => x[0] == "xe"))))
            {
                cur = [r];
                units.Add(cur);
                continue;
            }
            if (cur is null)
                continue;
            if (r[0] is "xgs" or "xgso" or "xrd")
            {
                cur = null;
                continue;
            }
            cur.Add(r);
        }
        return units;
    }

    private static ExplosionKind KindOf(List<string[]> unit)
    {
        if (unit.Any(x => x[0] == "xwt"))
            return ExplosionKind.Water;
        // The mega bomb is the only one whose core makes fireballs and nothing else.
        string[] e = unit.First(x => x[0] == "xe");
        return F(e[4]) == 1.0f && F(e[5]) == 1.0f && e[6] == "0" && !unit.Any(x => x[0] == "xsp" && x[^1] == "particle/particle_noisesphere")
               && unit.Any(x => x[0] == "xsp") ? ExplosionKind.MegaBomb : ExplosionKind.Base;
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void Every_explosion_makes_stocks_force_core_debris_and_water()
    {
        int explosions = 0, simple = 0, trails = 0, flecks = 0, water = 0;
        var wrong = new List<string>();
        foreach (var unit in Units())
        {
            explosions++;
            string what = $"explosion {explosions}";
            string[] e = unit.First(x => x[0] == "xe");
            var kind = KindOf(unit);
            water += kind == ExplosionKind.Water ? 1 : 0;
            var rec = new Recorded(unit);
            var sinks = new Sinks();
            var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
            var fleckSystems = new FleckSystems(effects, rec, new RandomSeeds(1));
            var fx = new ExplosionEffects(effects, rec, new ParticleViews(sinks), sinks, rec, fleckSystems, sinks, sinks, new RandomSeeds(1));
            ExplosionBurst burst = fx.Create(kind, V(e, 1), F(e[4]), F(e[5]), I(e[6]), rec);

            string[] xf = unit.First(x => x[0] == "xf");
            if (!Same(burst.Direction, V(xf, 1)) || !Same(burst.Force, F(xf[4])))
                wrong.Add($"{what}: force {G(burst.Direction)} {burst.Force:G9} against {string.Join(' ', xf)}");
            var stockSounds = unit.Where(x => x[0] == "xs").Select(x => x[1]).ToList();
            if (!rec.Sounds.SequenceEqual(stockSounds))
                wrong.Add($"{what}: sounds {string.Join(',', rec.Sounds)}, stock {string.Join(',', stockSounds)}");

            // The simple particles, by stock's emitter and material, in the order made.
            foreach (var group in unit.Where(x => x[0] == "xsp").GroupBy(x => (x[1], x[^1])))
            {
                var sheets = group.Key.Item1 switch
                {
                    "bubbles" => burst.Bubbles,
                    _ => burst.Core,
                };
                IReadOnlyList<SimpleParticle> port = group.Key.Item1 == "splish"
                    ? burst.Splish?.Pending ?? []
                    : sheets.FirstOrDefault(s => s.Material == group.Key.Item2)?.Pending ?? [];
                var stock = group.ToList();
                if (port.Count != stock.Count)
                    wrong.Add($"{what} {group.Key}: {port.Count} particles, stock {stock.Count}");
                for (int k = 0; k < Math.Min(port.Count, stock.Count); k++)
                {
                    SimpleParticle p = port[k];
                    string[] s = stock[k];
                    if (!Same(p.Position, V(s, 2)) || !Same(p.Velocity, V(s, 5)) || !Same(p.Lifetime, F(s[8])) || !Same(p.DieTime, F(s[9]))
                        || p.R != I(s[10]) || p.G != I(s[11]) || p.B != I(s[12]) || p.StartAlpha != I(s[13]) || p.EndAlpha != I(s[14])
                        || p.StartSize != I(s[15]) || p.EndSize != I(s[16]) || !Same(p.Roll, F(s[17])) || !Same(p.RollDelta, F(s[18])))
                        wrong.Add($"{what} {group.Key} {k}: ({G(p.Position)} {G(p.Velocity)} {p.DieTime} {p.R},{p.G},{p.B} {p.StartAlpha} {p.StartSize}->{p.EndSize} {p.Roll} {p.RollDelta}) against {string.Join(' ', s)}");
                }
                simple += stock.Count;
            }

            var stockTrails = unit.Where(x => x[0] == "xtp").ToList();
            var portTrails = (burst.Sparks?.Pending ?? []).Concat(burst.Drops?.Pending ?? []).ToList();
            if (portTrails.Count != stockTrails.Count)
                wrong.Add($"{what}: {portTrails.Count} trail particles, stock {stockTrails.Count}");
            for (int k = 0; k < Math.Min(portTrails.Count, stockTrails.Count); k++)
            {
                TrailParticle p = portTrails[k];
                string[] s = stockTrails[k];
                if (!Same(p.Position, V(s, 1)) || !Same(p.DieTime, F(s[5])) || !Same(p.Width, F(s[6])) || !Same(p.Length, F(s[7]))
                    || !Same(p.Velocity, V(s, 8)) || p.R != I(s[11]) || p.G != I(s[12]) || p.B != I(s[13]) || p.A != I(s[14]))
                    wrong.Add($"{what} trail {k}: ({G(p.Position)} {p.DieTime} {p.Width} {p.Length} {G(p.Velocity)} {p.R},{p.G},{p.B},{p.A}) against {string.Join(' ', s)}");
            }
            trails += stockTrails.Count;

            var stockFlecks = unit.Where(x => x[0] == "xfp").ToList();
            if (burst.FlecksMade.Count != stockFlecks.Count)
                wrong.Add($"{what}: {burst.FlecksMade.Count} flecks, stock {stockFlecks.Count}");
            for (int k = 0; k < Math.Min(stockFlecks.Count, burst.FlecksMade.Count); k++)
            {
                FleckParticle p = burst.FlecksMade[k];
                string[] s = stockFlecks[k];
                if (p.Sheet != I(s[1]) || !Same(p.Position, V(s, 2)) || !Same(p.Velocity, V(s, 5)) || !Same(p.DieTime, F(s[9]))
                    || p.Size != I(s[10]) || !Same(p.Roll, F(s[11])) || !Same(p.RollDelta, F(s[12])) || p.R != I(s[13]))
                    wrong.Add($"{what} fleck {k}: ({p.Sheet} {G(p.Position)} {G(p.Velocity)} {p.Size} {p.Roll} {p.RollDelta} {p.R}) against {string.Join(' ', s)}");
            }
            flecks += stockFlecks.Count;
            if (unit.FirstOrDefault(x => x[0] == "pcp") is { } pcp && burst.Flecks is { } fs && fs.Collision.ActivePlanes != I(pcp[2]))
                wrong.Add($"{what}: the flecks' collision has {fs.Collision.ActivePlanes} planes, stock {pcp[2]}");

            var stockLines = unit.Where(x => x[0] == "xln").ToList();
            if (sinks.Lines.Count != stockLines.Count)
                wrong.Add($"{what}: {sinks.Lines.Count} lines, stock {stockLines.Count}");
            for (int k = 0; k < Math.Min(stockLines.Count, sinks.Lines.Count); k++)
            {
                FxLineData l = sinks.Lines[k];
                string[] s = stockLines[k];
                if (!Same(l.Start.Z, F(s[1])) || !Same(l.End.Z, F(s[2])) || !Same(l.EndVelocity.Z, F(s[3])) || !Same(l.DieTime, F(s[4]))
                    || !Same(l.StartAlpha, F(s[5])) || !Same(l.StartScale, F(s[6])) || !Same(l.EndScale, F(s[7])))
                    wrong.Add($"{what} line {k}: ({l.Start.Z} {l.End.Z} {l.EndVelocity.Z} {l.DieTime} {l.StartAlpha} {l.StartScale} {l.EndScale}) against {string.Join(' ', s)}");
            }

            if (!rec.Drained)
                wrong.Add($"{what}: stock drew, traced or lit more");
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(explosions >= 20 && simple >= 1000 && trails >= 200 && flecks >= 300 && water >= 3,
            $"{explosions} explosions ({water} water), {simple} simple particles, {trails} trail particles, {flecks} flecks");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void Every_smoke_step_is_reproduced()
    {
        var rows = s_rows.Value;
        var rules = new ExplosionRules();
        var none = new Recorded([]);
        int steps = 0, floored = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] != "xgs")
                continue;
            string[] o = rows[i + 1];
            var p = new SimpleParticle
            {
                Position = V(r, 5), Velocity = V(r, 8), Lifetime = F(r[11]), DieTime = F(r[12]), Roll = F(r[13]), RollDelta = F(r[14]),
            };
            float dt = F(r[3]);
            Vector before = p.Velocity;
            rules.UpdateVelocity(ref p, dt, none);
            Vector decayed = before * ExplosionRules.Decay(dt);
            floored += Same(p.Velocity, decayed) ? 0 : 1;
            p.Position = p.Position + p.Velocity * dt;
            p.Lifetime += dt;
            rules.UpdateRoll(ref p, dt);
            steps++;
            if (!Same(p.Position, V(o, 2)) || !Same(p.Velocity, V(o, 5)) || !Same(p.Lifetime, F(o[8])) || !Same(p.Roll, F(o[9]))
                || !Same(p.RollDelta, F(o[10])))
                wrong.Add($"step {steps}: ({G(p.Position)} {G(p.Velocity)} {p.Lifetime:G9} {p.Roll:G9} {p.RollDelta:G9}) against {string.Join(' ', o)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 3000 && floored >= 300, $"{steps} steps ({floored} held at the speed floor)");
    }

    [Fact]
    public void Every_smoke_draw_fades_as_stocks()
    {
        int draws = 0;
        var wrong = new List<string>();
        var rules = new ExplosionRules();
        foreach (var r in s_rows.Value.Where(x => x[0] == "xrd" && x[1] == "exp_smoke"))
        {
            var p = new SimpleParticle
            {
                Lifetime = F(r[2]), DieTime = F(r[3]), R = (byte)I(r[4]), G = (byte)I(r[5]), B = (byte)I(r[6]),
                StartAlpha = (byte)I(r[7]), EndAlpha = (byte)I(r[8]), StartSize = (byte)I(r[9]), EndSize = (byte)I(r[10]),
            };
            draws++;
            Vector c = rules.UpdateColor(p);
            float a = rules.UpdateAlpha(p), s = rules.UpdateScale(p);
            if (!Same(c, V(r, 14)) || !Same(a, F(r[17])) || !Same(s, F(r[18])))
                wrong.Add($"draw {draws}: {G(c)} {a:G9} {s:G9} against {string.Join(' ', r)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(draws >= 1500, $"{draws} draws");
    }
}
