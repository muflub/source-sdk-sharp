using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client-side effect list against stock's, recorded:
/// <c>Fixtures/effectlist-stock.tsv.gz</c> (README beside it) is a session of
/// stock's <c>CEffectsList</c> and its four effect kinds, recorded through probe
/// copies: every effect created (directly or by the tracer helpers, with their
/// random draws), every frame's draw with the inputs each effect read, the mesh
/// it built, whether it was still active, and every removal. Replayed through
/// <see cref="ClientEffectList"/> and <see cref="ClientSideEffectFactory"/> with
/// the same inputs, everything must come out the same: the same effects with the
/// same parameters, drawn in the same order, the same vertices bit for bit, the
/// same removals, the same near-miss sounds.
/// </summary>
public class ClientEffectListTests
{
    // ---- the fixture -----------------------------------------------------------------

    private sealed class Row
    {
        public string Tag = "";
        public List<string> Positional = [];
        public Dictionary<string, List<string>> Keys = new(StringComparer.Ordinal);
        public int Line;

        public string Key(string k) => Keys[k][0];
        public float F(string k, int i = 0) => ParseF(Keys[k][i]);
        public Vector V(string k) => new(F(k, 0), F(k, 1), F(k, 2));
        public override string ToString() => $"line {Line}: {Tag} {string.Join(' ', Positional)}";
    }

    private static float ParseF(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static List<Row> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "effectlist-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded stock effect list is missing: " + path);

        var rows = new List<Row>();
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        int n = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            n++;
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            var row = new Row { Tag = c[0], Line = n };
            List<string>? current = null;
            for (int i = 1; i < c.Length; i++)
            {
                int eq = c[i].IndexOf('=');
                if (eq > 0)
                {
                    current = [c[i][(eq + 1)..]];
                    row.Keys[c[i][..eq]] = current;
                }
                else if (current is not null)
                {
                    current.Add(c[i]);
                }
                else
                {
                    row.Positional.Add(c[i]);
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    // ---- the rig -----------------------------------------------------------------------

    private sealed class Replay
    {
        public readonly FxTestRig Rig = new();
        public readonly ClientEffectList List;
        public readonly ClientSideEffectFactory Factory;
        public readonly Dictionary<string, ClientSideEffect> ByPointer = new(StringComparer.Ordinal);
        public bool TracerExtra = true;
        public int Frames, EffectDraws, MeshesCompared, Removals, Creations, Calls, Sounds, NearMissTests;

        public Replay()
        {
            RecordingEngine console = TestServices.Fake();
            console.Returns("Console.FindConVarValue", "0");
            List = new ClientEffectList(Rig, console);
            Factory = new ClientSideEffectFactory(Rig, List, Rig, () => TracerExtra);
        }

        public List<string> Wrong => Rig.Wrong;

        private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

        // ---- creations ----

        /// <summary>Checks a managed effect against stock's fxe-new row and files it under stock's pointer.</summary>
        private void Pair(Row row, ClientSideEffect effect)
        {
            Creations++;
            string kind = row.Positional[0];
            ByPointer[row.Positional[1]] = effect;
            string material = row.Key("mat");

            bool ok = effect.Kind == kind && Rig.MaterialName(effect.Material) == material;
            switch (effect)
            {
                case FxLine line:
                    FxLineData d = line.Data;
                    ok &= Same(d.Start, row.V("s")) && Same(d.End, row.V("e")) && Same(d.StartVelocity, row.V("sv"))
                          && Same(d.EndVelocity, row.V("ev")) && Same(d.StartAlpha, row.F("a")) && Same(d.EndAlpha, row.F("a", 1))
                          && Same(d.StartScale, row.F("sc")) && Same(d.EndScale, row.F("sc", 1)) && Same(d.DieTime, row.F("die"));
                    break;
                case FxQuad quad:
                    FxQuadData q = quad.Data;
                    ok &= (uint)q.Flags == uint.Parse(row.Key("flags"), CultureInfo.InvariantCulture)
                          && Same(q.Origin, row.V("o")) && Same(q.Normal, row.V("n")) && Same(q.StartScale, row.F("sc"))
                          && Same(q.EndScale, row.F("sc", 1)) && Same(q.DieTime, row.F("die")) && Same(q.LifeTime, row.F("life"))
                          && Same(q.StartAlpha, row.F("a")) && Same(q.EndAlpha, row.F("a", 1)) && Same(q.Color, row.V("c"))
                          && Same(q.Yaw, row.F("yaw")) && Same(q.DeltaYaw, row.F("yaw", 1)) && Same(q.ScaleBias, row.F("sb"))
                          && Same(q.AlphaBias, row.F("ab"));
                    break;
                case FxStaticLine st:
                    var ss = st.State;
                    ok &= Same(ss.Start, row.V("s")) && Same(ss.End, row.V("e")) && Same(ss.HalfScale, row.F("scale") * 0.5f)
                          && Same(ss.Life, row.F("life")) && (uint)ss.Flags == uint.Parse(row.Key("flags"), CultureInfo.InvariantCulture);
                    break;
                case FxDiscreetLine dl:
                    var ds = dl.State;
                    ok &= Same(ds.Origin, row.V("o")) && Same(ds.Direction, row.V("d")) && Same(ds.Velocity, row.F("vel"))
                          && Same(ds.Length, row.F("len")) && Same(ds.Clip, row.F("clip")) && Same(ds.Scale, row.F("scale"))
                          && Same(ds.Life, row.F("life"));
                    break;
                default:
                    ok = false;
                    break;
            }

            if (!ok)
                Wrong.Add($"{row}: managed made a {effect.Kind} that differs from stock's");
        }

        /// <summary>A battery's or an explosion's own FX_Add* call, made from stock's fxe-new.</summary>
        public void Direct(Row row)
        {
            int before = List.Count;
            switch (row.Positional[0])
            {
                case "Line":
                    Factory.AddLine(new FxLineData
                    {
                        Start = row.V("s"), End = row.V("e"), StartVelocity = row.V("sv"), EndVelocity = row.V("ev"),
                        StartAlpha = row.F("a"), EndAlpha = row.F("a", 1), StartScale = row.F("sc"), EndScale = row.F("sc", 1),
                        DieTime = row.F("die"), Material = Rig.Id(row.Key("mat")),
                    });
                    break;
                case "Quad":
                    Factory.AddQuad(row.V("o"), row.V("n"), row.F("sc"), row.F("sc", 1), row.F("sb"), row.F("a"), row.F("a", 1),
                                    row.F("ab"), row.F("yaw"), row.F("yaw", 1), row.V("c"), row.F("die"), row.Key("mat"),
                                    (FxQuadFlags)uint.Parse(row.Key("flags"), CultureInfo.InvariantCulture));
                    break;
                case "StaticLine":
                    Factory.AddStaticLine(row.V("s"), row.V("e"), row.F("scale"), row.F("life"), row.Key("mat"),
                                          (FxStaticLineFlags)uint.Parse(row.Key("flags"), CultureInfo.InvariantCulture));
                    break;
                case "DiscreetLine":
                    Factory.AddDiscreetLine(row.V("o"), row.V("d"), row.F("vel"), row.F("len"), row.F("clip"), row.F("scale"),
                                            row.F("life"), row.Key("mat"));
                    break;
                default:
                    Wrong.Add($"{row}: an effect kind this fact does not know");
                    return;
            }

            if (List.Count != before + 1)
            {
                Wrong.Add($"{row}: managed added {List.Count - before} effect(s), stock one");
                return;
            }
            Pair(row, List[before]!);
        }

        // ---- the tracer helpers ----

        /// <summary>One helper call and the rows it produced: its draws, its effects, its sounds.</summary>
        public void Call(Row entry, List<Row> produced)
        {
            Calls++;
            Rig.Randoms.Clear();
            foreach (Row r in produced.Where(r => r.Tag == "fxr"))
                Rig.Randoms.Enqueue((ParseF(r.Positional[1]), ParseF(r.Positional[2]), ParseF(r.Positional[3])));

            Row? sound = entry.Tag == "fxt-sound" ? entry : produced.FirstOrDefault(r => r.Tag == "fxt-sound");
            if (sound is not null)
            {
                Rig.Now = sound.F("cur");
                Rig.Listener = sound.V("listen");
                if (!Same(Factory.NextWhizTime, sound.F("next")))
                    Wrong.Add($"{sound}: stock's whiz timer read {sound.F("next")}, managed's {Factory.NextWhizTime}");
            }

            int before = List.Count;
            int soundsBefore = Rig.Sounds.Count;

            switch (entry.Tag)
            {
                case "fxt-tracer":
                    Factory.Tracer(entry.V("s"), entry.V("e"), int.Parse(entry.Key("vel"), CultureInfo.InvariantCulture), entry.Key("whiz") == "1");
                    break;
                case "fxt-playertracer":
                    Factory.PlayerTracer(entry.V("s"), entry.V("e"));
                    break;
                case "fxt-sound":
                    Factory.TracerSound(entry.V("s"), entry.V("e"), int.Parse(entry.Key("type"), CultureInfo.InvariantCulture));
                    break;
            }

            if (Rig.Randoms.Count != 0)
                Wrong.Add($"{entry}: stock drew {Rig.Randoms.Count} random number(s) managed did not");

            var made = produced.Where(r => r.Tag == "fxe-new").ToList();
            if (List.Count - before != made.Count)
                Wrong.Add($"{entry}: stock made {made.Count} effect(s), managed {List.Count - before}");
            else
                for (int k = 0; k < made.Count; k++)
                    Pair(made[k], List[before + k]!);

            // The near-miss test itself: the listener after the default type's
            // drop, the squared distance and the radius, as stock logged them.
            Row? dist = produced.LastOrDefault(r => r.Tag == "fxt-sound-dist");
            if (dist is not null)
            {
                NearMissTests++;
                if (Factory.LastNearMissTest is not { } got
                    || !Same(got.Listener, dist.V("listen")) || !Same(got.DistanceSqr, dist.F("d2")) || !Same(got.WhizDist, dist.F("whiz")))
                    Wrong.Add($"{dist}: managed's near-miss test read {Factory.LastNearMissTest}");
            }

            var emitted = produced.Where(r => r.Tag == "fxt-sound-emit").ToList();
            if (Rig.Sounds.Count - soundsBefore != emitted.Count)
            {
                Wrong.Add($"{entry}: stock emitted {emitted.Count} sound(s), managed {Rig.Sounds.Count - soundsBefore}");
            }
            else if (emitted.Count == 1)
            {
                // Stock logged the WAVE GameSound_GetParameters picked for the
                // script name (its own random choice, native's to make); the
                // script name per tracer type is NearMissScriptNames' fact.
                Sounds++;
                var (name, _, dir) = Rig.Sounds[^1];
                if (!Same(dir, emitted[0].V("dir")))
                    Wrong.Add($"{emitted[0]}: managed emitted {name} along ({dir.X} {dir.Y} {dir.Z})");
            }

            // The timer after a near-miss is the time plus the draw just checked.
            if (sound is not null && emitted.Count == 1 && produced.Count(r => r.Tag == "fxr") > 0)
            {
                Row last = produced.Last(r => r.Tag == "fxr");
                float expect = sound.F("cur") + ParseF(last.Positional[3]);
                if (!Same(Factory.NextWhizTime, expect))
                    Wrong.Add($"{entry}: managed's whiz timer is {Factory.NextWhizTime}, stock's would be {expect}");
            }
        }

        // ---- frames ----

        public void Frame(Row draw, List<Row> rows, Dictionary<string, int> drawNumber, Dictionary<string, int> lastDraw)
        {
            Frames++;
            int n = int.Parse(draw.Key("n"), CultureInfo.InvariantCulture);
            double ft = double.Parse(draw.Key("ft"), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (List.Count != n)
            {
                Wrong.Add($"{draw}: stock's list held {n}, managed's {List.Count}");
                return;
            }

            // The draws in this frame, in stock's order.
            var expected = new List<(Row Draw, List<Row> Mesh, Row? Drawn)>();
            foreach (Row r in rows)
            {
                if (r.Tag == "fxe-draw")
                    expected.Add((r, [], null));
                else if (r.Tag.StartsWith("fxm-", StringComparison.Ordinal))
                    expected[^1].Mesh.Add(r);
                else if (r.Tag == "fxl-drawn")
                    expected[^1] = (expected[^1].Draw, expected[^1].Mesh, r);
            }

            Row? viewed = expected.Select(e => e.Draw).FirstOrDefault(r => r.Keys.ContainsKey("view"));
            if (viewed is not null)
            {
                Row? full = expected.Select(e => e.Draw).FirstOrDefault(r => r.Keys.ContainsKey("fwd"));
                Rig.ViewAnswer = new FxView(viewed.V("view"), full?.V("fwd") ?? default,
                                            full is null ? 0 : int.Parse(full.Key("sw"), CultureInfo.InvariantCulture));
                if (full is not null)
                    TracerExtra = full.Key("extra") == "1";
            }

            int k = 0;
            int drawsBefore = 0;
            List.BeforeDraw = effect =>
            {
                drawsBefore = Rig.Draws.Count;
                if (k >= expected.Count)
                {
                    Wrong.Add($"{draw}: managed drew more effects than stock's {expected.Count}");
                    return;
                }
                string ptr = expected[k].Draw.Positional[0];
                if (!ByPointer.TryGetValue(ptr, out ClientSideEffect? want) || !ReferenceEquals(want, effect))
                    Wrong.Add($"{expected[k].Draw}: managed drew a different effect ({effect.Kind}) at this place in the frame");
            };
            List.AfterDraw = (effect, active) =>
            {
                if (k >= expected.Count)
                    return;
                var (d, mesh, drawn) = expected[k];
                k++;
                EffectDraws++;
                string ptr = d.Positional[0];
                drawNumber[ptr] = drawNumber.TryGetValue(ptr, out int c) ? c + 1 : 1;
                if (drawn is null || (drawn.Key("active") == "1") != active)
                    Wrong.Add($"{d}: stock's IsActive after this draw was {drawn?.Key("active")}, managed's {active}");

                int number = drawNumber[ptr];
                bool kept = number <= 3 || number % 10 == 0 || number == lastDraw[ptr];
                var managed = Rig.Draws.Skip(drawsBefore).ToList();
                if (!kept)
                    return;
                var verts = mesh.Where(m => m.Tag == "fxm-v").ToList();
                if (verts.Count == 0)
                {
                    if (managed.Count != 0)
                        Wrong.Add($"{d}: stock drew nothing on this draw, managed drew a mesh");
                    return;
                }
                if (managed.Count != 1 || managed[0].Vertices.Length != verts.Count)
                {
                    Wrong.Add($"{d}: stock drew {verts.Count} vertices in one mesh, managed {managed.Sum(m => m.Vertices.Length)} in {managed.Count}");
                    return;
                }
                MeshesCompared++;
                var (_, vs, byteColor) = managed[0];
                for (int i = 0; i < verts.Count; i++)
                {
                    List<string> p = verts[i].Positional;
                    FxVertex v = vs[i];
                    float[] want = [ParseF(p[0]), ParseF(p[1]), ParseF(p[2]), ParseF(p[3]), ParseF(p[4]),
                                    ParseF(p[6]), ParseF(p[7]), ParseF(p[8]), ParseF(p[9]), ParseF(p[10]), ParseF(p[11]), ParseF(p[12])];
                    float[] got = [v.Position.X, v.Position.Y, v.Position.Z, v.S, v.T, v.R, v.G, v.B, v.A, v.Normal.X, v.Normal.Y, v.Normal.Z];
                    bool kindOk = (p[5] == "b") == byteColor;
                    bool same = kindOk;
                    for (int j = 0; same && j < want.Length; j++)
                        same = Same(want[j], got[j]);
                    if (!same)
                    {
                        Wrong.Add($"{verts[i]}: managed vertex {i} is {(byteColor ? "b" : "f")} "
                                  + string.Join(' ', got.Select(g => g.ToString("R", CultureInfo.InvariantCulture))));
                        break;
                    }
                }
            };

            int removed = rows.Count(r => r.Tag == "fxl-remove");
            List.DrawEffects(ft);
            List.BeforeDraw = null;
            List.AfterDraw = null;

            if (k != expected.Count)
                Wrong.Add($"{draw}: stock drew {expected.Count} effect(s), managed {k}");
            if (List.Count != n - removed)
                Wrong.Add($"{draw}: stock removed {removed}, managed's list went {n} -> {List.Count}");
            Removals += removed;
            foreach (Row r in rows.Where(r => r.Tag == "fxl-remove"))
            {
                if (ByPointer.TryGetValue(r.Positional[0], out ClientSideEffect? gone))
                    for (int i = 0; i < List.Count; i++)
                        if (ReferenceEquals(List[i], gone))
                            Wrong.Add($"{r}: stock removed this effect, managed kept it");
            }
        }
    }

    // ---- the facts ---------------------------------------------------------------------

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<Row> rows = Recorded();

        Assert.True(rows.Count(r => r.Tag == "fxe-new") == 138, "138 effects created");
        Assert.True(rows.Count(r => r.Tag == "fxl-draw") == 458, "458 frames drawn");
        Assert.True(rows.Count(r => r.Tag == "fxe-draw") == 5222, "5222 effect draws");
        Assert.True(rows.Count(r => r.Tag == "fxm-begin") == 801, "801 meshes kept");
        Assert.True(rows.Count(r => r.Tag == "fxl-remove") == 117, "117 removals");
        Assert.True(rows.Count(r => r.Tag == "fxt-sound-emit") == 3, "three near-miss sounds (the positive arm)");
    }

    [Fact]
    public void TheListReplaysStockEffectByEffectAndFrameByFrame()
    {
        List<Row> rows = Recorded();
        var replay = new Replay();

        // Each effect's draw count and its last draw, for the mesh-thinning rule
        // the fixture was cut with (tools/probes/fx-select --mesh-every 10).
        var lastDraw = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Row r in rows.Where(r => r.Tag == "fxe-draw"))
            lastDraw[r.Positional[0]] = lastDraw.TryGetValue(r.Positional[0], out int c) ? c + 1 : 1;
        var drawNumber = new Dictionary<string, int>(StringComparer.Ordinal);

        var callTags = new HashSet<string>(StringComparer.Ordinal) { "fxr", "fxe-new", "fxl-add", "fxt-sound-dist", "fxt-sound-emit" };

        int i = 0;
        while (i < rows.Count)
        {
            Row row = rows[i];
            switch (row.Tag)
            {
                case "fxt-tracer":
                case "fxt-playertracer":
                case "fxt-sound":
                {
                    var produced = new List<Row>();
                    int j = i + 1;
                    bool soundTaken = row.Tag == "fxt-sound";
                    while (j < rows.Count)
                    {
                        Row r = rows[j];
                        if (callTags.Contains(r.Tag))
                        {
                            produced.Add(r);
                            j++;
                        }
                        else if (r.Tag == "fxt-sound" && row.Tag == "fxt-tracer" && row.Key("whiz") == "1" && !soundTaken)
                        {
                            produced.Add(r);
                            soundTaken = true;
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    replay.Call(row, produced);
                    i = j;
                    break;
                }
                case "fxe-new":
                    replay.Direct(row);
                    i++;
                    if (i < rows.Count && rows[i].Tag == "fxl-add")
                        i++;
                    break;
                case "fxl-draw":
                {
                    var frame = new List<Row>();
                    int j = i + 1;
                    while (j < rows.Count && (rows[j].Tag is "fxe-draw" or "fxl-drawn" or "fxl-remove" || rows[j].Tag.StartsWith("fxm-", StringComparison.Ordinal)))
                        frame.Add(rows[j++]);
                    replay.Frame(row, frame, drawNumber, lastDraw);
                    i = j;
                    break;
                }
                case "fxl-flush":
                {
                    int j = i + 1;
                    int removed = 0;
                    while (j < rows.Count && rows[j].Tag == "fxl-remove")
                    {
                        removed++;
                        j++;
                    }
                    int before = replay.List.Count;
                    if (before != int.Parse(row.Key("n"), CultureInfo.InvariantCulture))
                        replay.Wrong.Add($"{row}: stock flushed {row.Key("n")}, managed held {before}");
                    replay.List.Flush();
                    replay.Removals += removed;
                    i = j;
                    break;
                }
                default:
                    // fxb-*, fxt-cb (the callbacks' entry state; the next fact's), anything else.
                    i++;
                    break;
            }
        }

        Assert.True(replay.Creations == 138, $"every stock effect was made by managed code too: {replay.Creations} of 138");
        Assert.True(replay.Frames == 458 && replay.EffectDraws == 5222, $"every frame and draw replayed: {replay.Frames} frames, {replay.EffectDraws} draws");
        Assert.True(replay.MeshesCompared > 700, $"the kept meshes were compared: {replay.MeshesCompared}");
        Assert.True(replay.Sounds == 3, $"the three near-misses were emitted: {replay.Sounds}");
        Assert.True(replay.NearMissTests == 22, $"every near-miss test stock made past the timer was compared: {replay.NearMissTests}");
        Assert.True(replay.Wrong.Count == 0, $"{replay.Wrong.Count} disagreement(s) with stock: " + string.Join(" | ", replay.Wrong.Take(8)));

        // Every live effect holds one reference on its material; every removed one gave its back.
        var live = new Dictionary<int, int>();
        for (int k = 0; k < replay.List.Count; k++)
        {
            int m = replay.List[k]!.Material;
            live[m] = live.TryGetValue(m, out int c) ? c + 1 : 1;
        }
        foreach (var (id, refs) in replay.Rig.Refs)
            Assert.True(refs == (live.TryGetValue(id, out int l) ? l : 0),
                        $"{replay.Rig.Names[id]} holds {refs} reference(s) for {(live.TryGetValue(id, out int l2) ? l2 : 0)} live effect(s)");
    }

    [Theory]
    [InlineData(ClientSideEffectFactory.TracerTypeDefault, "Bullets.DefaultNearmiss")]
    [InlineData(ClientSideEffectFactory.TracerTypeGunship, "Bullets.GunshipNearmiss")]
    [InlineData(ClientSideEffectFactory.TracerTypeStrider, "Bullets.StriderNearmiss")]
    [InlineData(ClientSideEffectFactory.TracerTypeWaterBullet, "Underwater.BulletImpact")]
    public void NearMissScriptNames(int type, string script)
    {
        // FX_TracerSound's switch (clientsideeffects_test.cpp): the sound SCRIPT
        // each tracer type plays. The recording logs the wave the script
        // resolved to, so the names are pinned here from stock's source.
        var replay = new Replay();
        replay.Rig.Listener = new Vector(0f, 0f, 0f);
        replay.Rig.Now = 10f;
        replay.Rig.Randoms.Enqueue((type == ClientSideEffectFactory.TracerTypeWaterBullet ? 0.3f : 0.1f,
                                    type == ClientSideEffectFactory.TracerTypeWaterBullet ? 0.6f : 0.1f, 0.1f));

        replay.Factory.TracerSound(new Vector(-100f, 1f, -1f), new Vector(100f, 1f, -1f), type);

        Assert.True(replay.Rig.Sounds.Count == 1 && replay.Rig.Sounds[0].Name == script,
                    $"type {type} plays {script}; managed played {(replay.Rig.Sounds.Count == 1 ? replay.Rig.Sounds[0].Name : "nothing")}");
    }

    [Fact]
    public void AGaussTracerHasNoNearMiss()
    {
        var replay = new Replay();
        replay.Factory.TracerSound(new Vector(-100f, 1f, -1f), new Vector(100f, 1f, -1f), ClientSideEffectFactory.TracerTypeGauss);

        Assert.True(replay.Rig.Sounds.Count == 0 && replay.Rig.Randoms.Count == 0, "stock's switch returns for a type it does not list");
    }
}
