using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// fx.cpp against stock (lane c5-particles): the world, gunship, strider and
/// attached muzzle flashes, the smoke puffs, the hose-gas spurt
/// (<c>CSmokeEmitter</c>), the gunship impact and command pointer overlays and
/// the ricochet sound, as <c>Fixtures/fx-stock.tsv.gz</c> recorded them
/// (README beside it). Bit for bit, with the recorded draws fed back.
/// </summary>
public class FxOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static QAngle A(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "fx-stock.tsv.gz");
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

    private static readonly HashSet<string> s_heads = ["qme", "qge", "qte", "qma", "qse", "qbs", "qie", "qrs"];

    /// <summary>Each routine's call with the rows after it, up to the next call.</summary>
    private static List<List<string[]>> Units(string head)
    {
        var units = new List<List<string[]>>();
        List<string[]>? cur = null;
        foreach (var r in s_rows.Value)
        {
            if (s_heads.Contains(r[0]))
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

    /// <summary>The recorded draws, each checked against the range stock drew from.</summary>
    private sealed class Draws(IEnumerable<string[]> unit) : IParticleRandom
    {
        private readonly Queue<string[]> _draws = new(unit.Where(r => r[0] == "qr"));
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
    }

    /// <summary>The local-space emitter the attached flash fills, and the lines, sounds and overlays.</summary>
    private sealed class Native : IFxEmitters, IFxFlashReads, IFxDiscreetLineSink, IFxRoutineEnvironment, IParticleViewSource, IParticlePixelVisibility
    {
        public readonly List<(FxEmitterKind Kind, string Name, uint Handle, int Attachment, int Flags)> Created = [];
        public readonly List<(Vector Mins, Vector Maxs)> Boxes = [];
        public readonly List<(string Material, Vector Position, FxSimpleParticle Fields)> Added = [];
        public readonly List<(Vector Start, Vector Dir, float Velocity, float Length, float Clip, float Scale, float Life, string Material)> Lines = [];
        public readonly List<(string Script, Vector Origin)> Sounds = [];
        public readonly List<(Vector Position, Vector Normal, float R, float G, float B)> Overlays = [];
        public int Released;

        public int Create(FxEmitterKind kind, string name, uint handle, int attachment, int flags)
        {
            Created.Add((kind, name, handle, attachment, flags));
            return Created.Count;
        }

        public void SetBBox(int emitter, in Vector mins, in Vector maxs) => Boxes.Add((mins, maxs));

        public int Add(int emitter, string material, in Vector position)
        {
            Added.Add((material, position, default));
            return Added.Count - 1;
        }

        public void SetSimple(int emitter, int particle, in FxSimpleParticle fields)
            => Added[particle] = (Added[particle].Material, Added[particle].Position, fields);

        public void Release(int emitter) => Released++;

        public int MaterialsCached() => 3;

        public void AddDiscreetLine(Vector start, Vector direction, float velocity, float length, float clipLength, float scale,
                                    float life, string material)
            => Lines.Add((start, direction, velocity, length, clipLength, scale, life, material));

        public void Sound(string script, Vector origin) => Sounds.Add((script, origin));

        public void ImpactOverlay(Vector position, Vector normal, float r, float g, float b) => Overlays.Add((position, normal, r, g, b));

        public ParticleView Read() => throw new InvalidOperationException();

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();

        // Not reached by fx.cpp's routines.
        public void DrawBeforeViewModel(int emitter) => throw new InvalidOperationException();
        public void SetSortOrigin(int emitter, in Vector origin) => throw new InvalidOperationException();
        public void SetTrailFlag(int emitter, int flag) => throw new InvalidOperationException();
        public void SetCollisionGravity(int emitter, float gravity) => throw new InvalidOperationException();
        public void SetTrail(int emitter, int particle, in FxTrailParticle fields) => throw new InvalidOperationException();
        public bool AttachmentTransform(uint handle, int attachment, out Vector origin, out QAngle angles) => throw new InvalidOperationException();
        public bool AttachmentMatrixOrigin(uint handle, int attachment, out Vector origin) => throw new InvalidOperationException();
        public bool RenderableAttachment(uint handle, int attachment, out Vector origin, out QAngle angles) => throw new InvalidOperationException();
        public int EntityIndex(uint handle) => throw new InvalidOperationException();
        public void Elight(int key, in Vector origin, byte r, byte g, byte b, sbyte exponent, float radius, float decay, float die) => throw new InvalidOperationException();
        public Vector VectorRandom(float lo, float hi) => throw new InvalidOperationException();
    }

    private static (FxEffects Fx, Native Native, FxTestRig Rig) Make()
    {
        var native = new Native();
        var rig = new FxTestRig();
        var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
        var fx = new FxEffects(effects, new ParticleViews(native), native, native, native, rig, native, native, rig, new RandomSeeds(1));
        return (fx, native, rig);
    }

    private static string Check(SimpleParticle p, string[] s)
    {
        bool ok = Same(p.Position, V(s, 1)) && Same(p.Velocity, V(s, 4)) && Same(p.Lifetime, F(s[7])) && Same(p.DieTime, F(s[8]))
                  && p.R == I(s[9]) && p.G == I(s[10]) && p.B == I(s[11]) && p.StartAlpha == I(s[12]) && p.EndAlpha == I(s[13])
                  && p.StartSize == I(s[14]) && p.EndSize == I(s[15]) && Same(p.Roll, F(s[16])) && Same(p.RollDelta, F(s[17]));
        return ok ? "" : $"({G(p.Position)} {G(p.Velocity)} {p.Lifetime:G9} {p.DieTime:G9} {p.R},{p.G},{p.B} {p.StartAlpha}->{p.EndAlpha} "
                         + $"{p.StartSize}->{p.EndSize} {p.Roll:G9} {p.RollDelta:G9}) against {string.Join(' ', s)}";
    }

    /// <summary>Stock's particles by material, in the order made, against the sheet of that material.</summary>
    private static int CompareSheets(string what, List<string[]> stock, IReadOnlyList<SimpleSheet> sheets, List<string> wrong)
    {
        int n = 0;
        foreach (var group in stock.GroupBy(r => r[^1]))
        {
            IReadOnlyList<SimpleParticle> port = sheets.FirstOrDefault(s => s.Material == group.Key)?.Pending ?? [];
            var rows = group.ToList();
            if (port.Count != rows.Count)
                wrong.Add($"{what} {group.Key}: {port.Count} particles, stock {rows.Count}");
            for (int k = 0; k < Math.Min(port.Count, rows.Count); k++)
            {
                string w = Check(port[k], rows[k]);
                if (w.Length > 0)
                    wrong.Add($"{what} {group.Key} {k}: {w}");
            }
            n += rows.Count;
        }
        if (sheets.Sum(s => s.Pending.Count) != stock.Count)
            wrong.Add($"{what}: {sheets.Sum(s => s.Pending.Count)} particles in all, stock {stock.Count}");
        return n;
    }

    private static void Drained(string what, Draws draws, List<string> wrong)
    {
        wrong.AddRange(draws.Wrong.Select(w => $"{what}: {w}"));
        if (!draws.Drained)
            wrong.Add($"{what}: stock drew more");
    }

    [Fact]
    public void Every_world_and_gunship_muzzle_flash_is_stocks()
    {
        var wrong = new List<string>();
        int flashes = 0, particles = 0;
        foreach (var unit in Units("qme"))
        {
            string[] e = unit[0];
            string what = $"muzzle {++flashes}";
            Assert.Equal("0", e[8]);
            var (fx, _, _) = Make();
            var draws = new Draws(unit);
            List<SimpleSheet> sheets = fx.MuzzleEffect(V(e, 1), A(e, 4), F(e[7]), null, draws);
            particles += CompareSheets(what, unit.Where(r => r[0] == "qmp").ToList(), sheets, wrong);
            Drained(what, draws, wrong);
        }
        foreach (var unit in Units("qge"))
        {
            string[] e = unit[0];
            string what = $"gunship muzzle {++flashes}";
            var (fx, _, _) = Make();
            var draws = new Draws(unit);
            SimpleSheet sheet = fx.GunshipMuzzleEffect(V(e, 1), A(e, 4), F(e[7]), draws);
            particles += CompareSheets(what, unit.Where(r => r[0] == "qgp").ToList(), [sheet], wrong);
            Drained(what, draws, wrong);
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(flashes == 12 && particles == 54, $"{flashes} flashes, {particles} particles");
    }

    [Fact]
    public void Every_strider_muzzle_flash_is_five_lines_along_stocks_direction()
    {
        var wrong = new List<string>();
        int flashes = 0;
        foreach (var unit in Units("qte"))
        {
            string[] e = unit[0];
            var (fx, native, _) = Make();
            fx.StriderMuzzleEffect(V(e, 1), A(e, 4), 1);
            flashes++;
            Assert.Equal("|", e[7]);
            if (native.Lines.Count != 5)
                wrong.Add($"strider {flashes}: {native.Lines.Count} lines");
            float speed = 100.0f;
            foreach (var l in native.Lines)
            {
                if (!Same(l.Start, V(e, 1)) || !Same(l.Dir, V(e, 8)) || !Same(l.Velocity, speed) || l.Length != 32
                    || !Same(l.Clip, speed * 0.3f) || l.Scale != 5.0f || !Same(l.Life, 0.3f) || l.Material != "effects/bluespark")
                    wrong.Add($"strider {flashes}: {G(l.Start)} {G(l.Dir)} {l.Velocity} {l.Clip} against {string.Join(' ', e)} at {speed}");
                speed *= 1.5f;
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.Equal(6, flashes);
    }

    [Fact]
    public void Every_attached_muzzle_flash_fills_stocks_local_emitter()
    {
        var wrong = new List<string>();
        int flashes = 0, particles = 0;
        foreach (var unit in Units("qma"))
        {
            string[] e = unit[0];
            string what = $"attached {++flashes}";
            Assert.Equal("0", e[3]);
            float scale = F(e[1]);
            var (fx, native, _) = Make();
            var draws = new Draws(unit);
            fx.MuzzleEffectAttached(scale, 77, I(e[2]), null, e[4] == "1", draws);
            if (native.Created.Count != 1 || native.Created[0] != (FxEmitterKind.LocalSpace, "MuzzleFlash", 77u, I(e[2]), 0) || native.Released != 1)
                wrong.Add($"{what}: emitters {string.Join(',', native.Created)} released {native.Released}");
            Vector half = new Vector(16, 16, 16) * scale;
            if (native.Boxes.Count != 1 || !Same(native.Boxes[0].Mins, new Vector(0, 0, 0) - half) || !Same(native.Boxes[0].Maxs, half))
                wrong.Add($"{what}: boxes {string.Join(',', native.Boxes)}");
            var stock = unit.Where(r => r[0] == "qap").ToList();
            if (stock.Count != native.Added.Count)
                wrong.Add($"{what}: {native.Added.Count} particles, stock {stock.Count}");
            for (int k = 0; k < Math.Min(stock.Count, native.Added.Count); k++)
            {
                var (material, position, f) = native.Added[k];
                var p = new SimpleParticle
                {
                    Position = position, Velocity = f.Velocity, Lifetime = f.Lifetime, DieTime = f.DieTime, R = f.R, G = f.G, B = f.B,
                    StartAlpha = f.StartAlpha, EndAlpha = f.EndAlpha, StartSize = f.StartSize, EndSize = f.EndSize, Roll = f.Roll,
                    RollDelta = f.RollDelta,
                };
                string w = Check(p, stock[k]);
                if (w.Length > 0 || material != stock[k][^1])
                    wrong.Add($"{what} {k} {material}: {w}");
                particles++;
            }
            Drained(what, draws, wrong);
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(flashes == 12 && particles == 96, $"{flashes} flashes, {particles} particles");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void Every_smoke_puff_is_stocks()
    {
        var wrong = new List<string>();
        int calls = 0, puffs = 0;
        foreach (var unit in Units("qse"))
        {
            string[] e = unit[0];
            string what = $"smoke {++calls}";
            var (fx, _, _) = Make();
            var draws = new Draws(unit);
            Color32? color = e[9] == "1" ? new Color32(I(e[10]), I(e[11]), I(e[12])) : null;
            SimpleSheet sheet = fx.Smoke(V(e, 1), A(e, 4), F(e[7]), I(e[8]), color, I(e[13]), draws);
            puffs += CompareSheets(what, unit.Where(r => r[0] == "qsp").ToList(), [sheet], wrong);
            Drained(what, draws, wrong);
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(calls == 3 && puffs == 12, $"{calls} calls, {puffs} puffs");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void The_hose_gas_spurts_when_stocks_did_with_stocks_particles()
    {
        var wrong = new List<string>();
        int spurts = 0, particles = 0, held = 0;
        Span<int> ids = stackalloc int[16];
        var units = Units("qbs");
        foreach (var emitter in units.GroupBy(u => u[0][1]))
        {
            var (fx, _, rig) = Make();
            SmokeSpurt? spurt = null;
            float lastTime = 0;
            foreach (var unit in emitter)
            {
                string[] e = unit[0];
                string what = $"spurt {++spurts}";
                float now = F(e[2]);
                var draws = new Draws(unit);
                var stock = unit.Where(r => r[0] == "qbp").ToList();
                if (spurt is null)
                {
                    // FX_BuildSmoke, from the Smoke effect's arguments (c5p-fx.steps): its first spurt at once.
                    Assert.True(Same(F(e[4]), 0), "the first spurt is the emitter's first");
                    Vector forward = new(F(e[5]), 0, F(e[6]));
                    Assert.True(forward.X == 1 && forward.Z == 0, "the recording's spurt points along +x");
                    rig.Now = now;
                    spurt = fx.BuildSmoke(new Vector(-3843, 3100, 40), new QAngle(0, 0, 0), 0, 0, 100.0f, [50, 50, 50, 255], draws);
                    if (!Same(spurt.DeathTime, F(e[3])))
                        wrong.Add($"{what}: dies at {spurt.DeathTime:G9}, stock {e[3]}");
                    particles += Compare(what, spurt.Sheet.Pending, 0, stock, wrong);
                    var emit = default(ParticleEmit);
                    spurt.Sheet.Emit(ref emit, ids);
                }
                else
                {
                    if (!Same(spurt.LastParticleSpawnTime, F(e[4])))
                        wrong.Add($"{what}: next spawn {spurt.LastParticleSpawnTime:G9}, stock {e[4]}");
                    // Not yet due: the frame before stock's spurt adds nothing.
                    float early = spurt.LastParticleSpawnTime - 0.001f;
                    if (early > lastTime)
                    {
                        rig.Now = early;
                        spurt.Random = draws;
                        if (Step(spurt.Sheet, ids) != 0)
                            wrong.Add($"{what}: spurted at {early:G9}, before its time {spurt.LastParticleSpawnTime:G9}");
                        held++;
                    }
                    rig.Now = now;
                    spurt.Random = draws;
                    int before = spurt.Sheet.Emitter.Particles.Max(p => p.Id);
                    int n = Step(spurt.Sheet, ids);
                    // The emitter keeps its newest first, as stock's linked list did: the spurt's are the new ids.
                    var added = spurt.Sheet.Emitter.Particles.Where(p => p.Id > before).OrderBy(p => p.Id).ToList();
                    if (n != added.Count || !added.Select(p => p.Id).SequenceEqual(ids[..n].ToArray()))
                        wrong.Add($"{what}: the step handed over {n} id(s), added {added.Count}");
                    particles += Compare(what, added, 0, stock, wrong);
                }
                lastTime = now;
                Drained(what, draws, wrong);
                if (wrong.Count > 20)
                    break;
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(spurts >= 200 && particles >= 300 && held >= 100, $"{spurts} spurts, {particles} particles, {held} held back");
    }

    /// <summary>
    /// One step as the executor makes it, at dt 0 so a spawn's fields stay as
    /// made: the spawner runs at its head (stock's AddParticle inside
    /// SimulateParticles) and the ids it kept come back from the same call.
    /// </summary>
    private static int Step(SimpleSheet sheet, Span<int> ids)
    {
        Assert.Equal(1, sheet.Simulate(0f, true, ReadOnlySpan<int>.Empty, Span<byte>.Empty));
        return sheet.TakeSimulateSpawns(ids);
    }

    private static int Compare(string what, IReadOnlyList<SimpleParticle> port, int at, List<string[]> stock, List<string> wrong)
    {
        if (port.Count - at != stock.Count)
            wrong.Add($"{what}: {port.Count - at} particles, stock {stock.Count}");
        for (int k = 0; k < Math.Min(port.Count - at, stock.Count); k++)
        {
            string w = Check(port[at + k], stock[k]);
            if (w.Length > 0)
                wrong.Add($"{what} {k}: {w}");
        }
        return stock.Count;
    }

    /// <summary>
    /// <c>CreateSpurtParticles</c> took whatever <c>GetAttachment</c> wrote and
    /// ignored its answer, and a renderable's <c>GetAttachment</c> writes the
    /// entity's own origin for an attachment its model lacks: the spurt goes
    /// there, not to its last place. With no renderable it stays put.
    /// </summary>
    [Theory]
    [InlineData(1, 5f)]
    [InlineData(0, 5f)]
    [InlineData(-1, 1f)]
    public void A_spurt_on_an_attachment_goes_where_GetAttachment_said(int answer, float x)
    {
        var (fx, _, rig) = Make();
        rig.AttachmentAnswer = answer;
        rig.AttachmentOrigin = new Vector(5, 6, 7);
        SmokeSpurt spurt = fx.BuildSmoke(new Vector(1, 2, 3), new QAngle(0, 0, 0), 3, 2, 100.0f, [50, 50, 50, 255], new ParticleStream(1));
        Vector want = x == 5f ? new Vector(5, 6, 7) : new Vector(1, 2, 3);
        Assert.NotEmpty(spurt.Sheet.Pending);
        Assert.All(spurt.Sheet.Pending, p => Assert.Equal(want, p.Position));
        Assert.Equal(want, spurt.SortOrigin);
    }

    [Fact]
    public void The_command_pointer_and_gunship_impact_overlays_are_stocks()
    {
        var rows = Units("qie").Select(u => u[0]).ToList();
        var wrong = new List<string>();
        var (fx, native, _) = Make();
        int pointers = 0, gunship = 0;
        foreach (string[] r in rows)
        {
            native.Overlays.Clear();
            var data = new EffectData { Origin = V(r, 1) };
            if (Same(F(r[1]), -3800))
            {
                new GunshipImpactEffect(fx).Dispatch(data);
                gunship++;
            }
            else
            {
                data.Color = (int)((F(r[2]) - 3050) / 10);
                new CommandPointerEffect(fx).Dispatch(data);
                pointers++;
            }
            if (native.Overlays.Count != 1 || !Same(native.Overlays[0].Position, V(r, 1)) || !Same(native.Overlays[0].Normal, V(r, 4))
                || !Same(native.Overlays[0].R, F(r[7])) || !Same(native.Overlays[0].G, F(r[8])) || !Same(native.Overlays[0].B, F(r[9])))
                wrong.Add($"{string.Join(',', native.Overlays)} against {string.Join(' ', r)}");
        }

        // Colour index 4 is not a commander colour: stock drew nothing for the fifth pointer of each round.
        Assert.DoesNotContain(rows, r => Same(F(r[2]), 3090));
        native.Overlays.Clear();
        new CommandPointerEffect(fx).Dispatch(new EffectData { Origin = new Vector(-3843, 3090, 10), Color = 4 });
        Assert.Empty(native.Overlays);

        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(pointers == 12 && gunship == 3, $"{pointers} pointers, {gunship} gunship impacts");
    }

    [Fact]
    public void The_ricochet_sound_is_stocks_script_at_the_point()
    {
        var rows = Units("qrs").Select(u => u[0]).ToList();
        Assert.Equal(3, rows.Count);
        var (fx, native, _) = Make();
        foreach (string[] r in rows)
            fx.RicochetSound(V(r, 1));
        Assert.Equal(rows.Select(r => ("FX_RicochetSound.Ricochet", V(r, 1))), native.Sounds);
    }
}
