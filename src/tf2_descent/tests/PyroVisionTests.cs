extern alias client;

using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

using IPostProcessContributor = client::SourceSharp.Client.Render.IPostProcessContributor;
using PostCvar = SourceSharp.Client.Interop.PostCvar;
using PostOpsWriter = client::SourceSharp.Client.Render.PostOpsWriter;
using PostPlanText = client::SourceSharp.Client.Render.PostPlanText;
using PostProcessInputs = SourceSharp.Client.Interop.PostProcessInputs;
using PostProcessPlanner = client::SourceSharp.Client.Render.PostProcessPlanner;
using PostProgramEffects = client::SourceSharp.Client.Render.PostProgramEffects;
using PostProgramKind = SourceSharp.Client.Interop.PostProgramKind;
using PostTexture = SourceSharp.Client.Interop.PostTexture;
using PyroVision = TF2.Client.PyroVision;
using RenderOp = SourceSharp.Client.Interop.RenderOp;
using RenderOpKind = SourceSharp.Client.Interop.RenderOpKind;
using RenderPlanBuilder = client::SourceSharp.Client.Render.RenderPlanBuilder;
using RenderPlanText = client::SourceSharp.Client.Render.RenderPlanText;
using RenderTrace = client::SourceSharp.Client.Render.RenderTrace;

namespace TF2.Descent.Tests;

/// <summary>
/// TF2.Client's pyro vision (viewpostprocess.cpp's DrawPyroVignette,
/// CreatePyroSide, DrawPyroPost; user ruling 2026-10-12: "port but move it to
/// the tf2 client library"), at the core's vision point. Against the frozen
/// stock frame post-sandbox-pyro (src/test/client/Fixtures/post-stock), and
/// synthetically over a scripted random stream for the randomised sides.
/// These move to TF2.Tests when the TF2 lane's project lands (docs/todo.md).
/// </summary>
public sealed class PyroVisionTests
{
    private static string Fixture(string name)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string path = Path.Combine(d.FullName, "src", "test", "client", "Fixtures", "post-stock", name + ".txt.gz");
            if (File.Exists(path))
                return path;
        }
        Assert.Fail("the frozen post-stock frame is missing: " + name);
        return "";
    }

    private static (RenderTrace Trace, string Text) Load(string name)
    {
        using var gz = new GZipStream(File.OpenRead(Fixture(name)), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        string text = reader.ReadToEnd();
        return (RenderTrace.Parse(text), text);
    }

    private static float Num(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static PostProcessInputs InputsOf(RenderTrace t, string text)
    {
        string frame = text.Split('\n').First(l => l.StartsWith("frame\t", StringComparison.Ordinal));
        int[] rect = frame[(frame.IndexOf("rect=", StringComparison.Ordinal) + 5)..].Trim().Split(',').Select(Int).ToArray();
        int[] viewport = t.Environment["viewport"].Split(',').Select(Int).ToArray();
        var i = new PostProcessInputs
        {
            Kind = (int)PostProgramKind.EnginePost,
            X = rect[0], Y = rect[1], Width = rect[2], Height = rect[3],
            HdrType = Int(t.Environment["hdrtype"]), EngineDxLevel = Int(t.Environment["dxlevel"]),
            HardwareDxLevel = Int(t.Environment["hwdxlevel"]), MapHasHdrLighting = Int(t.Environment["maphashdrlighting"]),
            CanDoSrgbReadFromRts = Int(t.Environment["candosrgbreadfromrts"]), FakeSrgbWrite = Int(t.Environment["fakesrgbwrite"]),
            SupportsPixelShaders20 = Int(t.Environment["supportsps20"]),
            FullFrameWidth = rect[2], FullFrameHeight = rect[3], SmallFB1Width = rect[2] / 4, SmallFB1Height = rect[3] / 4,
            ViewportX = viewport[0], ViewportY = viewport[1], ViewportWidth = viewport[2], ViewportHeight = viewport[3],
            BackBufferWidth = rect[2], BackBufferHeight = rect[3],
            FrameTime = Num(t.Environment["frametime"]), RealTime = Num(t.Environment["realtime"]),
            ToneScaleLinear = 1f,
        };
        i.SetTextureSize(PostTexture.FullFrameFB, rect[2], rect[3]);
        i.SetTextureSize(PostTexture.SmallFB0, rect[2] / 4, rect[3] / 4);
        i.SetTextureSize(PostTexture.SmallFB1, rect[2] / 4, rect[3] / 4);
        i.SetTextureSize(PostTexture.ResolvedFullFrameDepth, rect[2], rect[3]);
        for (int b = 0; b < 32; b++)
            i.SetReadback(b, -1);
        for (var c = (PostCvar)0; c < PostCvar.Count; c++)
            i.SetCvar(c, Num(t.ConVars[c.ToString()]));
        return i;
    }

    /// <summary>A scripted uniform stream: each draw takes the next fraction of its range.</summary>
    private sealed class ScriptedRandom(params float[] fractions) : IUniformRandom
    {
        private int _next;

        public int Draws => _next;

        public float RandomFloat(float min, float max) => min + (max - min) * fractions[_next++ % fractions.Length];

        public int RandomInt(int min, int max) => min;

        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    /// <summary>The convars by name, as the engine answers them.</summary>
    private sealed class Convars(IReadOnlyDictionary<string, string> values) : IEngineConsole
    {
        public Dictionary<string, string> Overrides { get; } = new(StringComparer.Ordinal);

        public string? FindConVarValue(string name) =>
            Overrides.TryGetValue(name, out string? o) ? o : values.TryGetValue(name, out string? v) ? v : null;

        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) { }
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private sealed class Hooks(params IPostProcessContributor[] hooks) : IHooks<IPostProcessContributor>
    {
        public IReadOnlyList<IPostProcessContributor> All => hooks;

        public void Each(string what, Action<IPostProcessContributor> call)
        {
            foreach (IPostProcessContributor h in hooks)
                call(h);
        }
    }

    private sealed class Materials
    {
        private readonly List<string> _names = [""];

        public int Of(string name)
        {
            int at = _names.IndexOf(name);
            if (at > 0)
                return at;
            _names.Add(name);
            return _names.Count - 1;
        }

        // This game ships none of pyro's materials: FindMaterial answers the error material (the frozen frame draws ___error).
        public string Name(int id) =>
            id > 0 && id < _names.Count ? (_names[id].StartsWith("dev/pyro_", StringComparison.Ordinal) ? "___error" : _names[id]) : "<none>";

        public string Asked(int id) => id > 0 && id < _names.Count ? _names[id] : "<none>";
    }

    private static (RenderOp[] Ops, Materials Names) Run(PostProcessPlanner planner, in PostProcessInputs input, int frames = 1)
    {
        var names = new Materials();
        var builder = new RenderPlanBuilder();
        var writer = new PostOpsWriter(builder, names.Of);
        for (int f = 0; f < frames; f++)
        {
            builder.Clear();
            planner.Plan(input, writer, new PostProgramEffects());
        }
        return (builder.ToArray(), names);
    }

    // The frozen frame's DoEnginePostProcessing calls (its last SoundExtraUpdate to GrabPreColorCorrectedFrame).
    private static List<string> PostSegment(RenderTrace t)
    {
        List<string> calls = t.Calls.ToList();
        int grab = calls.FindIndex(c => c.StartsWith("engine.GrabPreColorCorrectedFrame\t", StringComparison.Ordinal));
        int sound = calls.Take(grab).ToList().FindLastIndex(c => c == "engine.Sound_ExtraUpdate");
        return calls.Skip(sound + 1).Take(grab - sound - 1).ToList();
    }

    [Fact]
    public void TheCoreProgramWithPyroVisionMakesTheFrozenPyroFramesCalls()
    {
        (RenderTrace t, string text) = Load("post-sandbox-pyro");
        PostProcessInputs input = InputsOf(t, text);
        Assert.Equal(1f, input.Cvar(PostCvar.localplayer_visionflags));
        var pyro = new PyroVision(new ScriptedRandom(0.5f), new Convars(t.ConVars));
        var planner = new PostProcessPlanner(() => "mods/test", new Hooks(pyro));
        (RenderOp[] ops, Materials names) = Run(planner, input, frames: 13);

        var expected = PostPlanText.Expected(ops, names.Name, input.SmallFB1Width, input.SmallFB1Height,
                                             op => op.Kind == (int)RenderOpKind.PostGoalToneScale);
        List<string> calls = PostSegment(t);
        bool ok = RenderPlanText.Matches(expected, calls, out int furthest);
        Assert.True(ok, $"parts from stock after item {furthest} of {expected.Count}: expected " +
                        $"'{(furthest < expected.Count ? expected[furthest].ToString() : "<end>")}'\n  " + string.Join("\n  ", calls));
        // The vignette's sides and the post bars are meshes the recorder does not line; they are here.
        Assert.Equal(2, ops.Count(o => o.Kind == (int)RenderOpKind.PostMeshBegin));
        Assert.Equal(16, ops.SkipWhile(o => o.Kind != (int)RenderOpKind.PostMeshBegin).Skip(1)
                            .SkipWhile(o => o.Kind != (int)RenderOpKind.PostMeshBegin).Count(o => o.Kind == (int)RenderOpKind.PostMeshVertex));
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.PostMeshBegin && names.Asked(o.GetI(0)) == "dev/pyro_post" && (o.GetI(3) & 4) != 0);
    }

    [Fact]
    public void WithVisionOffOrWithoutPixelShaders20NothingDraws()
    {
        (RenderTrace t, string text) = Load("post-sandbox-pyro");
        PostProcessInputs input = InputsOf(t, text);
        var random = new ScriptedRandom(0.5f);
        var pyro = new PyroVision(random, new Convars(t.ConVars));
        var planner = new PostProcessPlanner(() => "mods/test", new Hooks(pyro));
        input.SetCvar(PostCvar.localplayer_visionflags, 2f);
        Assert.DoesNotContain(Run(planner, input).Ops, o => o.Kind == (int)RenderOpKind.PostMeshBegin);
        input.SetCvar(PostCvar.localplayer_visionflags, 1f);
        input.SupportsPixelShaders20 = 0;
        Assert.DoesNotContain(Run(planner, input).Ops, o => o.Kind == (int)RenderOpKind.PostMeshBegin);
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void VignetteOneDrawsTheBorderAndNoSides()
    {
        (RenderTrace t, string text) = Load("post-sandbox-pyro");
        var console = new Convars(t.ConVars);
        console.Overrides["pyro_vignette"] = "1";
        var random = new ScriptedRandom(0.5f);
        var planner = new PostProcessPlanner(() => "mods/test", new Hooks(new PyroVision(random, console)));
        (RenderOp[] ops, Materials names) = Run(planner, InputsOf(t, text));
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.PostDrawRect && names.Asked(o.GetI(0)) == "dev/pyro_vignette_border");
        RenderOp mesh = Assert.Single(ops, o => o.Kind == (int)RenderOpKind.PostMeshBegin);
        Assert.Equal("dev/pyro_post", names.Asked(mesh.GetI(0)));
        Assert.Equal(0, random.Draws);
    }

    // CreatePyroSide, by hand from stock's switch: side 0 is the top-left edge,
    // horizontal, its far corner at (-1 + 2 * width, 1 - length * 0.25).
    [Fact]
    public void TheFirstSideIsTheTopLeftEdgeSizedByItsFourDraws()
    {
        (RenderTrace t, string text) = Load("post-sandbox-pyro");
        // limit, rate, width, length: fractions of 0.1-0.35, 0.05-0.2, 0.65-0.95, 0.3-0.55.
        var random = new ScriptedRandom(0.2f, 0.4f, 0.5f, 0.8f, 0f);
        var pyro = new PyroVision(random, new Convars(t.ConVars));
        var planner = new PostProcessPlanner(() => "mods/test", new Hooks(pyro));
        PostProcessInputs input = InputsOf(t, text);
        input.FrameTime = 0.1f;
        Run(planner, input);

        PyroVision.PyroSide first = pyro.Sides[0];
        Assert.True(first.Alive && first.Horizontal && first.Increasing);
        Assert.Equal(0.1f + 0.25f * 0.2f, first.IntensityLimit, 6);
        Assert.Equal(0.05f + 0.15f * 0.4f, first.Rate, 6);
        Assert.Equal((-1f, 1f), (first.X0, first.Y0));
        Assert.Equal(-1f + (0.65f + 0.3f * 0.5f) * 2f, first.X1, 6);
        Assert.Equal(1f - (0.3f + 0.25f * 0.8f) * 0.25f, first.Y1, 6);
        Assert.Equal(first.Rate * 0.1f, first.Intensity, 6); // one frame's fade-in
        // Fewer than 25 alive, so the frame made side 1 (top-right) as well.
        Assert.True(pyro.Sides[1].Alive);
        Assert.Equal((1f, 1f), (pyro.Sides[1].X0, pyro.Sides[1].Y0));
        Assert.True(pyro.Sides[1].Horizontal); // edges 0-1 and 4-5 are horizontal, 2-3 and 6-7 vertical ((n >> 1) & 1)
    }

    [Fact]
    public void ASideFadesUpToItsLimitThenDownAndDies()
    {
        (RenderTrace t, string text) = Load("post-sandbox-pyro");
        var pyro = new PyroVision(new ScriptedRandom(0f), new Convars(t.ConVars)); // limit 0.1, rate 0.05
        var planner = new PostProcessPlanner(() => "mods/test", new Hooks(pyro));
        PostProcessInputs input = InputsOf(t, text);
        input.FrameTime = 0.5f;

        // Stock's arithmetic, step by step, in float.
        float intensity = 0f, step = 0.05f * 0.5f;
        bool increasing = true;
        int frames = 0;
        do
        {
            Run(planner, input);
            frames++;
            if (increasing)
            {
                intensity += step;
                if (intensity >= 0.1f)
                    increasing = false;
            }
            else
            {
                intensity -= step;
            }
            if (intensity > 0f || increasing)
            {
                Assert.True(pyro.Sides[0].Alive, $"frame {frames}: side 0 died early");
                Assert.Equal(intensity, pyro.Sides[0].Intensity);
                Assert.Equal(increasing, pyro.Sides[0].Increasing);
            }
        }
        while ((intensity > 0f || increasing) && frames < 50);
        Assert.InRange(frames, 8, 12);
        Assert.True(!pyro.Sides[0].Alive || pyro.Sides[0].Intensity < step, "side 0 outlived its fade");
    }

    [Fact]
    public void PyroVisionIsAClientSideServiceAtTheVisionPoint()
    {
        Assert.True(typeof(IPostProcessContributor).IsAssignableFrom(typeof(PyroVision)));
        var attribute = typeof(PyroVision).GetCustomAttributes(false).Single(a => a.GetType().Name == "SideServiceAttribute");
        Assert.Equal("Client", attribute.GetType().GetProperty("Side")!.GetValue(attribute)!.ToString());
    }

    // viewpostprocess.cpp's pyro_* declarations, as stock wrote them (all FCVAR_ARCHIVE, no help).
    [Fact]
    public void ThePyroConvarsAreStocksDeclarations()
    {
        var stock = new Dictionary<string, string>
        {
            ["pyro_vignette"] = "2", ["pyro_vignette_distortion"] = "1", ["pyro_min_intensity"] = "0.1",
            ["pyro_max_intensity"] = "0.35", ["pyro_min_rate"] = "0.05", ["pyro_max_rate"] = "0.2",
            ["pyro_min_side_length"] = "0.3", ["pyro_max_side_length"] = "0.55", ["pyro_min_side_width"] = "0.65",
            ["pyro_max_side_width"] = "0.95",
            // viewrender.cpp's (C7 step 3: declared beside the rest when that TU left).
            ["pyro_dof"] = "1",
        };
        var declared = typeof(PyroVision).Assembly.GetTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttributes(typeof(ConVarAttribute), false).OfType<ConVarAttribute>().SingleOrDefault()))
            .Where(x => x.Attr != null && x.Attr.Name.StartsWith("pyro_", StringComparison.Ordinal))
            .ToDictionary(x => x.Attr!.Name, x => x.Attr!);
        Assert.Equal(stock.Keys.Order(StringComparer.Ordinal), declared.Keys.Order(StringComparer.Ordinal));
        foreach ((string name, string def) in stock)
        {
            Assert.Equal(def, declared[name].DefaultValue);
            Assert.Equal(ConVarFlags.Archive, declared[name].Flags);
            Assert.Equal("", declared[name].Help);
        }
    }
}
