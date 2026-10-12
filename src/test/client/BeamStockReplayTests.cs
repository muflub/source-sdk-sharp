using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Ropes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE MANAGED BEAMS, AGAINST THE C++ THEY REPLACE (plan_client_novalve.md §5.1,
/// cluster 5).
///
/// <para>
/// <c>Fixtures/beam-stock.tsv</c> is the native <c>CViewRenderBeams</c> and
/// beamdraw.cpp on a live dm_lockdown: every beam temp entity this build fires
/// and the shapes none reaches, created, stepped every frame and drawn in every
/// view, recorded by <c>tools/probes/client/c2-beams</c> before any beam code
/// moved. The replay feeds the managed <see cref="BeamSystem"/> the same
/// creations, frames, kills and draws, answers its engine questions (box culls,
/// entity positions, pixel visibility, screen positions) with the recorded
/// answers in recorded order, and requires every beam's state and every
/// segment and vertex it hands the renderer to match bit for bit. See
/// <c>Fixtures/README-beam-stock.md</c>.
/// </para>
/// </summary>
public class BeamStockReplayTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(in Vector a, in Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string S(in Vector v) => FormattableString.Invariant($"({v.X:R} {v.Y:R} {v.Z:R})");

    private static EntityHandle Ent(int entry) => entry < 0 ? EntityHandle.None : new EntityHandle((uint)entry);

    //-------------------------------------------------------------------------
    // The recorded answers, consumed in order.
    //-------------------------------------------------------------------------

    private sealed class Answers : IBeamWorld, IBeamDrawWorld, IBeamRenderables
    {
        public readonly Queue<string[]> Culls = new();
        public readonly Queue<string[]> Positions = new();
        public readonly Queue<string[]> RingBoxes = new();
        public readonly Queue<string[]> Halos = new();
        public readonly Queue<(Vector Point, Vector Screen)> Screens = new();
        public readonly Dictionary<int, (bool Exists, int Frames, string Name)> Models = [];
        public readonly Dictionary<int, bool> EntityModels = [];
        public readonly List<string> Mismatches = [];
        private string[]? _cull;
        private string[]? _ring;
        private int _renderables;

        public bool ModelExists(int modelIndex) => modelIndex >= 0 && Models.TryGetValue(modelIndex, out var m) && m.Exists;

        public int ModelFrameCount(int modelIndex) => Models.TryGetValue(modelIndex, out var m) ? m.Frames : 0;

        public int ModelIndexForName(string name) => throw new InvalidOperationException("no recorded creation names a model");

        public EntityHandle EntityAt(int entry) => throw new InvalidOperationException("the replay creates from BeamInfo");

        public bool EntityHasModel(EntityHandle entity)
            => EntityModels.TryGetValue((int)entity.Value, out bool m) && m;

        public bool EntityPosition(EntityHandle entity, int attachment, bool useHitboxes, ref Vector position)
        {
            if (Positions.Count == 0)
            {
                Mismatches.Add("an entity position was asked with none recorded");
                return false;
            }

            string[] c = Positions.Dequeue();
            // The attachment is not compared: for an endpoint stock never set,
            // its attachment index is whatever the allocator left there.
            if (I(c[1]) != (int)entity.Value || (c[3] != "0") != useHitboxes)
                Mismatches.Add($"entity position asked for {(int)entity.Value}/{attachment}, recorded {c[1]}/{c[2]}");
            position = V(c, 5);
            return c[4] != "0";
        }

        public bool IsBoxVisible(in Vector mins, in Vector maxs)
        {
            if (Culls.Count == 0)
            {
                Mismatches.Add("a box cull was asked with none recorded");
                return false;
            }

            _cull = Culls.Dequeue();
            if (!Same(V(_cull, 1), mins) || !Same(V(_cull, 4), maxs))
                Mismatches.Add($"cull box {S(mins)}-{S(maxs)}, recorded {S(V(_cull, 1))}-{S(V(_cull, 4))}");
            return _cull[8] != "0";
        }

        public bool CullBox(in Vector mins, in Vector maxs) => _cull is not null && _cull[9] != "0";

        bool IBeamDrawWorld.IsBoxVisible(in Vector mins, in Vector maxs)
        {
            if (RingBoxes.Count == 0)
            {
                Mismatches.Add("a ring box was asked with none recorded");
                return false;
            }

            _ring = RingBoxes.Dequeue();
            if (!Same(V(_ring, 2), mins) || !Same(V(_ring, 5), maxs))
                Mismatches.Add($"ring box {S(mins)}-{S(maxs)}, recorded {S(V(_ring, 2))}-{S(V(_ring, 5))}");
            return _ring[8] != "0";
        }

        bool IBeamDrawWorld.CullBox(in Vector mins, in Vector maxs) => _ring is not null && _ring[9] != "0";

        public float HaloVisibility(Beam beam, in Vector origin, float proxySize)
        {
            if (Halos.Count == 0)
            {
                Mismatches.Add("a halo visibility was asked with none recorded");
                return 0;
            }

            return F(Halos.Dequeue()[3]);
        }

        public Vector ScreenPosition(in Vector point)
        {
            if (Screens.Count == 0)
            {
                Mismatches.Add("a screen position was asked with none recorded");
                return default;
            }

            var (p, s) = Screens.Dequeue();
            if (!Same(p, point))
                Mismatches.Add($"screen position of {S(point)}, recorded {S(p)}");
            return s;
        }

        public int Add(Beam beam) => _renderables++;

        public void Changed(Beam beam) { }

        public void Remove(int handle) { }
    }

    /// <summary>What the managed draw handed the renderer, as rows shaped like the probe's.</summary>
    private sealed class Recorder(Answers answers) : IBeamDrawSink
    {
        public readonly List<string[]> Rows = [];

        private static string G(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        public bool BindSprite(int modelIndex, int frame, int renderMode)
        {
            string name = answers.Models.TryGetValue(modelIndex, out var m) ? m.Name : "?";
            Rows.Add(["ST", name, frame.ToString(CultureInfo.InvariantCulture), renderMode.ToString(CultureInfo.InvariantCulture)]);
            return true;
        }

        public void SetHdrColorScale(int modelIndex, int renderMode, float scale) => Rows.Add(["HD", G(scale)]);

        public bool HasGlow(int modelIndex) => true;

        public void BindGlow(int modelIndex) { }

        public void Strip(ReadOnlySpan<BeamSeg> segments)
        {
            Rows.Add(["SG", segments.Length.ToString(CultureInfo.InvariantCulture)]);
            foreach (BeamSeg s in segments)
                Rows.Add(["SN", G(s.Pos.X), G(s.Pos.Y), G(s.Pos.Z), G(s.Color.X), G(s.Color.Y), G(s.Color.Z),
                          G(s.TexCoord), G(s.Width), G(s.Alpha)]);
        }

        public void Mesh(BeamPrimitive primitive, int primitives, BeamColorKind colorKind, ReadOnlySpan<BeamVertex> vertices)
        {
            Rows.Add(["MB", ((int)primitive).ToString(CultureInfo.InvariantCulture), primitives.ToString(CultureInfo.InvariantCulture)]);
            foreach (BeamVertex v in vertices)
                Rows.Add(["MV", ((int)colorKind).ToString(CultureInfo.InvariantCulture), G(v.Pos.X), G(v.Pos.Y), G(v.Pos.Z),
                          G(v.R), G(v.G), G(v.B), G(v.A), G(v.U), G(v.V)]);
        }
    }

    private sealed class Result
    {
        public readonly List<string> StateMismatches = [];
        public readonly List<string> DrawMismatches = [];
        public readonly List<string> CreationMismatches = [];
        public readonly List<string> AnswerMismatches = [];
        public int States, Draws, Segments, Vertices, Creations, Frames;
    }

    private static readonly Lazy<Result> Replayed = new(Replay);

    private static string FixturePath => RepoLayout.Repo("src", "test", "managed", "Fixtures", "beam-stock.tsv");

    private static BeamInfo InfoFrom(string[] c)
    {
        int start = I(c[3]), end = I(c[6]);
        return new BeamInfo
        {
            Type = (BeamType)I(c[2]),
            StartEnt = Ent(start),
            StartAttachment = I(c[5]),
            EndEnt = Ent(end),
            EndAttachment = I(c[8]),
            Start = V(c, 9),
            End = V(c, 12),
            ModelIndex = I(c[15]),
            ModelName = c[16] == "-" ? null : c[16],
            HaloIndex = I(c[17]),
            HaloName = c[18] == "-" ? null : c[18],
            HaloScale = F(c[19]),
            Life = F(c[20]),
            Width = F(c[21]),
            EndWidth = F(c[22]),
            FadeLength = F(c[23]),
            Amplitude = F(c[24]),
            Brightness = F(c[25]),
            Speed = F(c[26]),
            StartFrame = I(c[27]),
            FrameRate = F(c[28]),
            Red = F(c[29]),
            Green = F(c[30]),
            Blue = F(c[31]),
            Renderable = c[32] != "0",
            Segments = I(c[33]),
            Flags = (BeamFlags)I(c[34]),
            Center = V(c, 35),
            StartRadius = F(c[38]),
            EndRadius = F(c[39]),
        };
    }

    /// <param name="stale">
    /// A beam the point, circle and ring-point creators made and no update has
    /// touched yet: stock leaves its <c>t</c> and noise as the allocator found
    /// them, so they are not compared until its first update writes them.
    /// </param>
    private static void CompareState(string[] c, Beam beam, List<string> mismatches, int line, bool stale = false)
    {
        var want = new (string Name, bool Ok)[]
        {
            ("type", I(c[2]) == (int)beam.Type),
            ("flags", I(c[3]) == (int)beam.Flags),
            ("att0", Same(V(c, 5), beam.Attachment[0])),
            ("att1", Same(V(c, 8), beam.Attachment[1])),
            // attachment[2] and the radii are a ring point's; on any other beam
            // stock leaves them as the allocator found them.
            ("att2", I(c[2]) != (int)BeamType.RingPoint || Same(V(c, 11), beam.Attachment[2])),
            ("delta", Same(V(c, 14), beam.Delta)),
            ("t", stale || !beam.TWritten || Same(F(c[17]), beam.T)),
            ("freq", Same(F(c[18]), beam.Freq)),
            ("die", Same(F(c[19]), beam.Die)),
            ("width", Same(F(c[20]), beam.Width)),
            ("endWidth", Same(F(c[21]), beam.EndWidth)),
            ("fadeLength", Same(F(c[22]), beam.FadeLength)),
            ("amplitude", Same(F(c[23]), beam.Amplitude)),
            ("life", Same(F(c[24]), beam.Life)),
            ("rgb", Same(F(c[25]), beam.R) && Same(F(c[26]), beam.G) && Same(F(c[27]), beam.B)),
            ("brightness", Same(F(c[28]), beam.Brightness)),
            ("speed", Same(F(c[29]), beam.Speed)),
            ("frameRate", Same(F(c[30]), beam.FrameRate)),
            ("frame", Same(F(c[31]), beam.Frame)),
            ("segments", I(c[32]) == beam.Segments),
            ("frameCount", I(c[40]) == beam.FrameCount),
            ("radii", I(c[2]) != (int)BeamType.RingPoint || Same(F(c[41]), beam.StartRadius) && Same(F(c[42]), beam.EndRadius)),
            ("noise", stale || uint.Parse(c[43], CultureInfo.InvariantCulture) == NoiseHash(beam.Noise)),
            ("calculatedNoise", (c[44] != "0") == beam.CalculatedNoise),
            ("mins", Same(V(c, 45), beam.Mins)),
            ("maxs", Same(V(c, 48), beam.Maxs)),
            ("trail", I(c[51]) == TrailCount(beam)),
        };

        foreach (var (name, ok) in want.Where(w => !w.Ok))
            mismatches.Add($"line {line} beam {c[1]} ({c[0]}): {name} differs");
    }

    private static int TrailCount(Beam beam)
    {
        int n = 0;
        for (BeamTrail? t = beam.Trail; t is not null; t = t.Next)
            n++;
        return n;
    }

    /// <summary>The probe's FNV-1a over the noise array's 129 floats.</summary>
    private static uint NoiseHash(float[] noise)
    {
        uint h = 2166136261u;
        foreach (float f in noise)
        {
            int bits = BitConverter.SingleToInt32Bits(f);
            for (int i = 0; i < 4; i++)
                h = (h ^ (byte)(bits >> (8 * i))) * 16777619u;
        }
        return h;
    }

    private static Result Replay()
    {
        Assert.True(File.Exists(FixturePath), "the recorded stock beams are missing: " + FixturePath);

        string[] lines = File.ReadAllLines(FixturePath);
        var answers = new Answers();
        var result = new Result();
        var system = new BeamSystem(answers, answers);
        var ids = new Dictionary<int, Beam>();

        // The answers first: each kind's queue in recorded order.
        foreach (string line in lines)
        {
            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "CB": answers.Culls.Enqueue(c); break;
                case "EP": answers.Positions.Enqueue(c); break;
                case "RB": answers.RingBoxes.Enqueue(c); break;
                case "PV": answers.Halos.Enqueue(c); break;
                case "SC":
                    answers.Screens.Enqueue((V(c, 2), V(c, 5)));
                    answers.Screens.Enqueue((V(c, 8), V(c, 11)));
                    break;
                case "MD":
                    answers.Models[I(c[1])] = (c[2] != "0", I(c[3]), c[4]);
                    break;
            }
        }

        float now = 0, frameTime = 0;
        string lastKind = "";

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 || lines[i][0] == '#')
                continue;

            string[] c = lines[i].Split('\t');
            int line = i + 1;

            switch (c[0])
            {
                case "BI":
                {
                    lastKind = c[1];
                    result.Creations++;
                    now = F(c[40]);
                    BeamInfo info = InfoFrom(c);
                    answers.EntityModels[I(c[3])] = c[4] != "0";
                    answers.EntityModels[I(c[6])] = c[7] != "0";

                    int before = system.Active.Count;
                    Beam? made = c[1] switch
                    {
                        "CreateBeamEnts" => system.CreateBeamEnts(info, now),
                        "CreateBeamEntPoint" => system.CreateBeamEntPoint(info, now),
                        "CreateBeamPoints" => system.CreateBeamPoints(info, now),
                        "CreateBeamCirclePoints" => system.CreateBeamCirclePoints(info, now),
                        "CreateBeamFollow" => system.CreateBeamFollow(info, now),
                        "CreateBeamRingPoint" => system.CreateBeamRingPoint(info, now),
                        "CreateBeamRing" => system.CreateBeamRing(info, now),
                        _ => throw new InvalidOperationException(c[1]),
                    };

                    // Pair the allocation (BA) and the answer (BR) that follow.
                    int j = i + 1;
                    int allocated = -1;
                    for (; j < lines.Length; j++)
                    {
                        string[] d = lines[j].Split('\t');
                        if (d[0] == "BA")
                        {
                            allocated = I(d[1]);
                        }
                        else if (d[0] == "BR")
                        {
                            int returned = I(d[2]);
                            if (system.Active.Count > before)
                                ids[allocated] = system.Active[0];
                            if (returned < 0 ? made is not null : made is null || !ReferenceEquals(ids[returned], made))
                                result.CreationMismatches.Add($"line {line} {c[1]}: stock returned {returned}, managed {(made is null ? "none" : "a beam")}");
                            if (allocated < 0 && system.Active.Count != before)
                                result.CreationMismatches.Add($"line {line} {c[1]}: managed allocated where stock did not");
                            break;
                        }
                    }
                    break;
                }

                case "BS":
                case "UB":
                case "DA":
                {
                    int id = I(c[1]);
                    result.States++;
                    if (!ids.TryGetValue(id, out Beam? beam))
                        result.StateMismatches.Add($"line {line}: beam {id} unknown");
                    else
                        CompareState(c, beam, result.StateMismatches, line, c[0] == "BS" && lastKind is
                            "CreateBeamPoints" or "CreateBeamCirclePoints" or "CreateBeamRingPoint");
                    break;
                }

                case "KD":
                    now = F(c[2]);
                    system.KillDeadBeams(Ent(I(c[1])), now);
                    break;

                case "CL":
                    system.Clear();
                    break;

                case "UF":
                    now = F(c[1]);
                    frameTime = F(c[2]);
                    result.Frames++;
                    system.UpdateTempEntBeams(now, frameTime);
                    break;

                case "DM":
                {
                    int id = I(c[1]);
                    bool full = c[3] == "1";

                    // The group: DV, then everything up to and including DE.
                    string[] dv = lines[i + 1].Split('\t');
                    var view = new BeamView(V(dv, 1), V(dv, 4), V(dv, 7), V(dv, 10), V(dv, 13), V(dv, 16), V(dv, 19),
                                            new BeamMatrix(Enumerable.Range(22, 16).Select(k => F(dv[k])).ToArray()),
                                            F(dv[38]), F(dv[39]), I(dv[40]));

                    var recorded = new List<string[]>();
                    for (int k2 = i + 2; k2 < lines.Length; k2++)
                    {
                        string[] d = lines[k2].Split('\t');
                        if (d[0] == "DE")
                            break;
                        if (d[0] is "ST" or "HD" or "SG" or "SN" or "MB" or "MV")
                            recorded.Add(d);
                    }

                    if (!ids.TryGetValue(id, out Beam? beam))
                    {
                        result.DrawMismatches.Add($"line {line}: draw of unknown beam {id}");
                        break;
                    }

                    var recorder = new Recorder(answers);
                    system.DrawBeam(beam, new BeamDraw(recorder, answers, view), answers);

                    if (full)
                    {
                        result.Draws++;
                        CompareDraw(recorded, recorder.Rows, result, line, id);
                    }
                    break;
                }
            }
        }

        result.AnswerMismatches.AddRange(answers.Mismatches);
        if (answers.Culls.Count + answers.Positions.Count + answers.RingBoxes.Count + answers.Halos.Count + answers.Screens.Count != 0)
            result.AnswerMismatches.Add($"answers never asked: {answers.Culls.Count} culls, {answers.Positions.Count} positions, " +
                                        $"{answers.RingBoxes.Count} ring boxes, {answers.Halos.Count} halos, {answers.Screens.Count} screens");
        return result;
    }

    private static void CompareDraw(List<string[]> recorded, List<string[]> managed, Result result, int line, int id)
    {
        if (recorded.Count != managed.Count)
        {
            result.DrawMismatches.Add($"line {line} beam {id}: stock emitted {recorded.Count} rows " +
                                      $"({string.Join(",", recorded.Select(r => r[0]).Distinct())}), managed {managed.Count} " +
                                      $"({string.Join(",", managed.Select(r => r[0]).Distinct())})");
            return;
        }

        for (int i = 0; i < recorded.Count; i++)
        {
            string[] r = recorded[i], m = managed[i];
            bool ok = r[0] == m[0] && r[0] switch
            {
                "ST" => r[2] == m[1] && r[3] == m[2] && r[4] == m[3],
                "HD" => Same(F(r[2]), F(m[1])),
                "SG" => r[2] == m[1],
                "SN" => Enumerable.Range(0, 9).All(k => Same(F(r[2 + k]), F(m[1 + k]))),
                "MB" => r[2] == m[1] && r[3] == m[2],
                "MV" => r[2] == m[1] && Enumerable.Range(0, 9).All(k => Same(F(r[3 + k]), F(m[2 + k]))),
                _ => false,
            };

            if (r[0] == "SN") result.Segments++;
            if (r[0] == "MV") result.Vertices++;

            if (!ok)
            {
                result.DrawMismatches.Add($"line {line} beam {id} row {i}: stock [{string.Join(" ", r)}] managed [{string.Join(" ", m)}]");
                return;
            }
        }
    }

    private static string First(List<string> m) => m.Count == 0 ? "" :
        $"{m.Count} mismatch(es), first: {m[0]}; by kind: " +
        string.Join(", ", m.GroupBy(x => x.Contains(": ") ? x[(x.LastIndexOf(": ", StringComparison.Ordinal) + 2)..] : x)
                           .OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} x{g.Count()} (first {g.First()})"));

    //-------------------------------------------------------------------------
    // The facts.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheFixtureHoldsEveryDrawnShape()
    {
        Result r = Replayed.Value;

        Assert.True(r.Creations >= 16, $"only {r.Creations} creations replayed");
        Assert.True(r.Frames >= 300, $"only {r.Frames} frames replayed");
        Assert.True(r.Draws >= 50, $"only {r.Draws} draws compared");
        Assert.True(r.Segments >= 500, $"only {r.Segments} strip segments compared");
        Assert.True(r.Vertices >= 700, $"only {r.Vertices} mesh vertices compared");
    }

    [Fact]
    public void TheManagedBeamsAreCreatedAndRefusedAsStockWas()
    {
        Result r = Replayed.Value;

        Assert.True(r.Creations > 0, "no creation was replayed");
        Assert.True(r.CreationMismatches.Count == 0, First(r.CreationMismatches));
    }

    [Fact]
    public void TheManagedBeamsReproduceEveryRecordedStateBitForBit()
    {
        Result r = Replayed.Value;

        Assert.True(r.States > 0, "no state row was compared");
        Assert.True(r.StateMismatches.Count == 0, First(r.StateMismatches));
    }

    [Fact]
    public void TheManagedBeamsAskTheEngineWhatStockAskedInTheSameOrder()
    {
        Result r = Replayed.Value;

        Assert.True(r.AnswerMismatches.Count == 0, First(r.AnswerMismatches));
    }

    [Fact]
    public void TheManagedBeamsDrawEveryRecordedSegmentAndVertexBitForBit()
    {
        Result r = Replayed.Value;

        Assert.True(r.Draws > 0, "no draw was compared");
        Assert.True(r.DrawMismatches.Count == 0, First(r.DrawMismatches));
    }
}
