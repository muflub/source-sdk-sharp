using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The shards against stock (lane c5-particles): <c>FX_GlassImpact</c>'s
/// spinning glass and the dust behind it, and <c>C_TEShatterSurface</c>'s grid
/// of glass or tile, as <c>Fixtures/shards-stock.tsv.gz</c> recorded them
/// (README beside it). Bit for bit, with the recorded draws and light fed back.
/// </summary>
public class ShardsOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "shards-stock.tsv.gz");
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
            if (r[0] is "gli" or "shi")
            {
                cur = r[0] == head ? [r] : null;
                if (cur is not null)
                    units.Add(cur);
                continue;
            }
            cur?.Add(r);
        }
        return units;
    }

    private sealed class Recorded(IEnumerable<string[]> unit, Vector light) : IParticleRandom, IClientComputeLighting, IParticleWorldTrace,
        IParticleViewSource, IParticlePixelVisibility
    {
        private readonly Queue<string[]> _draws = new(unit.Where(r => r[0] is "fkr" or "shr"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] != "f" || !Same(min, F(r[2])) || !Same(max, F(r[3])))
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

        public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors) => light;

        // The collision's set-up traces: nothing in the way (they are the step's, not the spawn's).
        public ParticleTrace TraceLine(Vector start, Vector end) => new() { Fraction = 1, EndPos = end };

        public ParticleView Read() => throw new InvalidOperationException();

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
    }

    private sealed class NoSounds : IFxRoutineEnvironment
    {
        public void Sound(string script, Vector origin) { }
        public void ImpactOverlay(Vector position, Vector normal, float r, float g, float b) { }
    }

    private static ShardEffects Make(Recorded rec)
    {
        var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
        return new ShardEffects(effects, new ParticleViews(rec), rec, rec, rec, new FxTestRig(), new RandomSeeds(1), new NoSounds());
    }

    [Fact]
    public void Every_glass_impact_is_stocks_shards_dust_and_collision()
    {
        var wrong = new List<string>();
        int impacts = 0, shards = 0, dust = 0;
        foreach (var unit in Units("gli"))
        {
            string[] e = unit[0];
            string what = $"glass {++impacts}";
            var rec = new Recorded(unit, V(e, 7));

            // The block managed builds is the one stock drew from.
            float[] block = new float[ImpactEffectPolicy.MaxFloats];
            ImpactEffectPolicy.Build(ImpactEffect.Glass, 0, 1.0f, false, block);
            Assert.Equal(ImpactEffectPolicy.MaxFloats, I(e[10]));
            // The bursts the effect writes; past them the native buffer held whatever its stack did.
            int written = ImpactEffectPolicy.BurstAt((int)block[(int)ImpactHeader.Bursts]);
            Assert.True(written > ImpactEffectPolicy.HeaderFloats, "the glass block writes its bursts");
            for (int k = 0; k < written; k++)
            {
                if (!Same(block[k], F(e[11 + k])))
                    wrong.Add($"{what}: block[{k}] {block[k]:G9}, stock {e[11 + k]}");
            }

            ShardEffects.Made made = Make(rec).GlassImpact(V(e, 1), V(e, 4), rec)!;
            var stockShards = unit.Where(r => r[0] == "glp").ToList();
            if (made.Shards.Made.Count != stockShards.Count)
                wrong.Add($"{what}: {made.Shards.Made.Count} shards, stock {stockShards.Count}");
            for (int k = 0; k < Math.Min(stockShards.Count, made.Shards.Made.Count); k++)
            {
                Particle3D p = made.Shards.Made[k];
                string[] s = stockShards[k];
                string material = made.Shards.Sheets.First(x => x.Particles.Concat(x.Pending).Any(q => q.Id == p.Id)).Material;
                bool ok = material == (s[1] == "0" ? "effects/fleck_glass1" : "effects/fleck_glass2")
                          && Same(p.Position, V(e, 1)) && Same(p.LifeRemaining, F(s[2])) && Same(p.Velocity, V(s, 3)) && p.Size == I(s[6])
                          && Same(p.Angles, V(s, 7)) && Same(p.AngSpeed, F(s[10]))
                          && p.FrontR == I(s[11]) && p.FrontG == I(s[12]) && p.FrontB == I(s[13])
                          && p.BackR == I(s[14]) && p.BackG == I(s[15]) && p.BackB == I(s[16]);
                if (!ok)
                    wrong.Add($"{what} shard {k}: {material} {p.LifeRemaining:G9} {G(p.Velocity)} {p.Size} {G(p.Angles)} {p.AngSpeed:G9} " +
                              $"{p.FrontR},{p.FrontG},{p.FrontB}/{p.BackR},{p.BackG},{p.BackB} against {string.Join(' ', s)}");
                shards++;
            }

            string[] c = unit.First(r => r[0] == "glc");
            if (!Same(made.Spread, F(c[1])) || !Same(made.MinSpeed, F(c[2])) || !Same(made.MaxSpeed, F(c[3]))
                || !Same(made.Gravity, F(c[4])) || !Same(made.Dampen, F(c[5])) || !Same(made.Direction, V(e, 4)))
                wrong.Add($"{what}: collision {made.Spread} {made.MinSpeed} {made.MaxSpeed} {made.Gravity} {made.Dampen} against {string.Join(' ', c)}");

            var stockDust = unit.Where(r => r[0] == "gld").ToList();
            // Stock's order is by burst then particle; each burst here has its own sheet, in burst order.
            var portDust = made.Dust.SelectMany(sh => sh.Pending.Select(q => (sh.Material, q))).ToList();
            if (portDust.Count != stockDust.Count)
                wrong.Add($"{what}: {portDust.Count} dust, stock {stockDust.Count}");
            for (int k = 0; k < Math.Min(stockDust.Count, portDust.Count); k++)
            {
                var (material, p) = portDust[k];
                string[] s = stockDust[k];
                bool ok = material == ShardEffects.Sheet((ImpactMaterial)I(s[19])) && Same(p.Position, V(s, 2)) && Same(p.Velocity, V(s, 5))
                          && Same(p.Lifetime, F(s[8])) && Same(p.DieTime, F(s[9])) && p.R == I(s[10]) && p.G == I(s[11]) && p.B == I(s[12])
                          && p.StartAlpha == I(s[13]) && p.EndAlpha == I(s[14]) && p.StartSize == I(s[15]) && p.EndSize == I(s[16])
                          && Same(p.Roll, F(s[17])) && Same(p.RollDelta, F(s[18]));
                if (!ok)
                    wrong.Add($"{what} dust {k}: {material} {G(p.Position)} {G(p.Velocity)} {p.DieTime:G9} {p.R},{p.G},{p.B} {p.StartAlpha} " +
                              $"{p.StartSize}->{p.EndSize} {p.Roll:G9} {p.RollDelta:G9} against {string.Join(' ', s)}");
                dust++;
            }

            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(impacts == 5 && shards == 13 && dust == 25, $"{impacts} impacts, {shards} shards, {dust} dust");
    }

    [Fact]
    public void Every_shattered_pane_is_stocks_grid()
    {
        var wrong = new List<string>();
        int panes = 0, shards = 0;
        foreach (var unit in Units("shi"))
        {
            string[] e = unit[0];
            string what = $"pane {++panes}";
            var rec = new Recorded(unit, V(e, 23));
            float size = F(e[13]);
            int wide = I(e[14]), high = I(e[15]);
            var front = new Color32(I(e[17]), I(e[18]), I(e[19]));
            var back = new Color32(I(e[20]), I(e[21]), I(e[22]));
            ShardEffects.Made made = Make(rec).ShatterSurface(V(e, 1), V(e, 4), V(e, 7), V(e, 10), wide * size, high * size, size,
                                                              I(e[16]), front, back, rec)!;
            var stock = unit.Where(r => r[0] == "shp").ToList();
            if (made.Shards.Made.Count != stock.Count)
                wrong.Add($"{what}: {made.Shards.Made.Count} shards, stock {stock.Count}");
            string[] glass = I(e[16]) == 0 ? ["effects/fleck_glass1", "effects/fleck_glass2"] : ["effects/fleck_tile1", "effects/fleck_tile2"];
            for (int k = 0; k < Math.Min(stock.Count, made.Shards.Made.Count); k++)
            {
                Particle3D p = made.Shards.Made[k];
                string[] s = stock[k];
                string material = made.Shards.Sheets.First(x => x.Particles.Concat(x.Pending).Any(q => q.Id == p.Id)).Material;
                bool ok = material == glass[I(s[1])] && Same(p.Position, V(s, 2)) && Same(p.LifeRemaining, F(s[5])) && Same(p.Velocity, V(s, 6))
                          && p.Size == I(s[9]) && Same(p.AngSpeed, F(s[10])) && Same(p.Angles, V(e, 4))
                          && p.FrontR == I(s[11]) && p.FrontG == I(s[12]) && p.FrontB == I(s[13])
                          && p.BackR == I(s[14]) && p.BackG == I(s[15]) && p.BackB == I(s[16]);
                if (!ok)
                    wrong.Add($"{what} shard {k}: {material} {G(p.Position)} {p.LifeRemaining:G9} {G(p.Velocity)} {p.Size} {p.AngSpeed:G9} " +
                              $"{p.FrontR},{p.FrontG},{p.FrontB}/{p.BackR},{p.BackG},{p.BackB} against {string.Join(' ', s)}");
                shards++;
                if (wrong.Count > 20)
                    break;
            }
            string[] c = unit.First(r => r[0] == "shc");
            if (!Same(made.Direction, V(c, 1)) || !Same(made.MinSpeed, F(c[4])) || !Same(made.MaxSpeed, F(c[5])))
                wrong.Add($"{what}: collision {G(made.Direction)} {made.MinSpeed:G9} {made.MaxSpeed:G9} against {string.Join(' ', c)}");
            wrong.AddRange(rec.Wrong.Take(5).Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(panes == 4 && shards == 384, $"{panes} panes, {shards} shards");
    }

    [Theory]
    [InlineData(0f, 32f, 48f)]
    [InlineData(4f, 2f, 48f)]
    [InlineData(4f, 32f, 0f)]
    [InlineData(0.25f, 32f, 48f)]
    public void A_pane_the_decode_refuses_draws_nothing(float shardSize, float width, float height)
    {
        var rec = new Recorded([], default);
        Assert.Null(Make(rec).ShatterSurface(default, default, new Vector(0, 0, 1), default, width, height, shardSize, 0,
                                             new Color32(1, 2, 3), new Color32(4, 5, 6), rec));
        Assert.Empty(rec.Wrong);
    }
}
