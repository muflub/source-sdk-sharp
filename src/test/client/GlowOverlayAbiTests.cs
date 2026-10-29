using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.GlowOverlays;
using SourceSharp.Client.Render.PixelVisibility;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The glow overlays' boundary (C7): the command words and the view a draw
/// crosses with (shell_glow_client.h against <see cref="GlowCommand"/>,
/// <see cref="GlowClip"/> and <see cref="GlowHost"/>), the words the host writes,
/// the engine's trace, the ConVar watch, the three creators (the fireball, the
/// impact, the RPG shot down) as their native bodies made them, and the row.
/// </summary>
public class GlowOverlayAbiTests
{
    private static string Native(string file)
    {
        string path = RepoLayout.Repo("src", "client", "native", file);
        Assert.True(File.Exists(path), "missing: " + path);
        return AbiParityTests.CodeOnly(File.ReadAllText(path));
    }

    private static int Value(string code, string name)
    {
        Match m = Regex.Match(code, $@"\b{name}\s*=\s*([0-9 +*]+),");
        Assert.True(m.Success, name + " is not declared");
        int sum = 0;
        foreach (string term in m.Groups[1].Value.Split('+'))
            sum += term.Split('*').Select(f => int.Parse(f.Trim(), CultureInfo.InvariantCulture)).Aggregate(1, (a, b) => a * b);
        return sum;
    }

    [Fact]
    public void TheCommandWordsAreTheHeadersWords()
    {
        string h = Native("shell_glow_client.h");
        Assert.Equal((int)GlowCommand.Clip, Value(h, "SS_GLOW_CMD_CLIP"));
        Assert.Equal((int)GlowCommand.HdrScale, Value(h, "SS_GLOW_CMD_HDRSCALE"));
        Assert.Equal((int)GlowCommand.Quad, Value(h, "SS_GLOW_CMD_QUAD"));
        Assert.Equal((int)GlowCommand.WireQuad, Value(h, "SS_GLOW_CMD_WIREQUAD"));
        Assert.Equal(GlowHost.QuadWords, Value(h, "SS_GLOW_QUAD_WORDS"));
        Assert.Equal(GlowHost.WireQuadWords, Value(h, "SS_GLOW_WIREQUAD_WORDS"));
        Assert.Equal(GlowHost.ViewFloats, Value(h, "SS_GLOW_VIEW_FLOATS"));
        // The clip modes are the executor's: 0 on and kept, 1 off, 2 the kept answer.
        Assert.Equal([0, 1, 2], Enum.GetValues<GlowClip>().Select(c => (int)c));
        string cpp = Native("shell_glow_client.cpp");
        Assert.Contains("if (nMode == 0)", cpp);
        Assert.Contains("nMode == 1 ? false : bSaved", cpp);
    }

    private sealed class Answers : IPixelVisibility, IGlowEngine, IPixelVisFrame
    {
        public readonly List<string> Asked = [];
        public float FractionVisible(in PixelVisParams p, ref int queryHandle) => 1.0f;
        public float FractionVisibleNoHandle(in Vector position) => 1.0f;
        public bool IsAvailable => true;
        public float GlowSightDistance(in Vector glowOrigin, bool shouldTrace) => 1.0f;
        public int FindMaterial(string name, string group)
        {
            Asked.Add(name);
            return name == GlowOverlaySystem.DefaultMaterial ? 7 : 8;
        }
        public void TraceSolid(in Vector start, in Vector end, out float fraction, out int surfaceFlags)
        {
            fraction = 1;
            surfaceFlags = 0;
        }
        public int FrameCount => 1;
        public float FrameTime => 1 / 60f;
    }

    private static GlowSettings Settings(Dictionary<string, string?> values)
        => new(() => values["building_cubemaps"] != "0", () => VariantParse.Atof(values["cl_sun_decay_rate"]));

    private static float[] ViewFloats(int drawSprites, bool wireframe)
    {
        var v = new float[GlowHost.ViewFloats];
        v[3] = 1;                       // forward +x
        v[6] = 8000;                    // zFar
        // Five planes everything ahead is inside: normal +x, dist -10.
        for (int i = 0; i < 5; i++)
        {
            v[7 + i * 4] = 1;
            v[10 + i * 4] = -10;
        }
        v[27] = wireframe ? 1 : 0;
        v[28] = drawSprites;
        return v;
    }

    [Fact]
    public void ADrawIsWrittenAsTheExecutorReadsIt()
    {
        var answers = new Answers();
        GlowSettings settings = Settings(new() { ["building_cubemaps"] = "0", ["cl_sun_decay_rate"] = "0.05" });
        var system = new GlowOverlaySystem(answers, answers, settings, answers);
        var glow = new GlowOverlay { Pos = new Vector(300, 0, 0), NSprites = 1, HdrColorScale = 2.0f };
        glow.Sprites[0].Color = new Vector(1, 1, 1);
        system.Activate(glow);
        var host = new GlowHost(system, settings);

        var words = new int[256];
        int n = host.DrawOverlays(ViewFloats(1, wireframe: true), cacheFullSceneState: false, words);

        // Clip on and kept, clip back to it for the overlay, its $hdrcolorscale,
        // its quad, its wireframe quad, and the kept answer restored.
        int expected = 2 + 2 + 3 + GlowHost.QuadWords + GlowHost.WireQuadWords + 2;
        Assert.Equal(expected, n);
        Assert.Equal([(int)GlowCommand.Clip, (int)GlowClip.EnableAndSave, (int)GlowCommand.Clip, (int)GlowClip.Saved], words[..4]);
        Assert.Equal([(int)GlowCommand.HdrScale, 7, BitConverter.SingleToInt32Bits(2.0f)], words[4..7]);
        Assert.Equal((int)GlowCommand.Quad, words[7]);
        Assert.Equal(7, words[8]);
        // The first corner's position: a hundred units out along +x, its colour full.
        Assert.Equal(100f, BitConverter.Int32BitsToSingle(words[9]));
        Assert.Equal(1f, BitConverter.Int32BitsToSingle(words[12]));
        int wire = 7 + GlowHost.QuadWords;
        Assert.Equal((int)GlowCommand.WireQuad, words[wire]);
        Assert.Equal(8, words[wire + 1]);
        Assert.Equal([(int)GlowCommand.Clip, (int)GlowClip.Saved], words[( n - 2 )..n]);
        Assert.Equal([GlowOverlaySystem.DefaultMaterial, GlowOverlaySystem.WireframeMaterial], answers.Asked);
    }

    [Fact]
    public void DrawsThatDoNotFitAreAskedForAgainAndRDrawSpritesComesWithTheView()
    {
        var answers = new Answers();
        GlowSettings settings = Settings(new() { ["building_cubemaps"] = "0", ["cl_sun_decay_rate"] = "0.05" });
        var system = new GlowOverlaySystem(answers, answers, settings, answers);
        var glow = new GlowOverlay { Pos = new Vector(300, 0, 0), NSprites = 1 };
        glow.Sprites[0].Color = new Vector(1, 1, 1);
        system.Activate(glow);
        var host = new GlowHost(system, settings);

        var small = new int[4];
        int n = host.DrawOverlays(ViewFloats(1, wireframe: false), false, small);
        Assert.True(n > small.Length);
        var large = new int[n];
        Assert.Equal(n, host.Commands(large));
        Assert.Equal((int)GlowCommand.Quad, large[7]);

        // r_drawsprites 0, handed with the view: the overlay draws nothing.
        Assert.Equal(6, host.DrawOverlays(ViewFloats(0, wireframe: false), false, large));
        Assert.Equal(0, settings.DrawSprites);
    }

    [Fact]
    public void TheSettingsReadTheirConVarsAtEachQuestion()
    {
        var values = new Dictionary<string, string?> { ["building_cubemaps"] = "0", ["cl_sun_decay_rate"] = "0.05" };
        GlowSettings s = Settings(values);
        Assert.False(s.BuildingCubemaps);
        Assert.Equal(0.05f, s.SunDecayRate);
        values["building_cubemaps"] = "1";
        values["cl_sun_decay_rate"] = "0.2";
        Assert.True(s.BuildingCubemaps);
        Assert.Equal(0.2f, s.SunDecayRate);
    }

    [Fact]
    public void TheEngineTracesTheSkyLineSolidWithTheSimpleFilter()
    {
        var engine = TestServices.Fake();
        engine.Strict = false;
        engine.Returns("WorldQuery.ClientTraceLine", new ClientTraceResult(true, 0.5f, Vector.Zero, Vector.Zero, 0, -1, 0, GlowOverlaySystem.SurfSky));
        var glowEngine = new ClientGlowEngine(new NoMaterials(), engine);
        glowEngine.TraceSolid(Vector.Zero, new Vector(100, 0, 0), out float fraction, out int flags);
        Assert.Equal(0.5f, fraction);
        Assert.Equal(GlowOverlaySystem.SurfSky, flags);
        var call = engine.Last("WorldQuery.ClientTraceLine")!.Value;
        Assert.Equal(TraceMask.ContentsSolid, call.ArgAs<TraceMask>(2));
        Assert.Equal(TraceFilter.Simple, call.ArgAs<TraceFilter>(3));
        Assert.Equal(-1, call.ArgAs<int>(4));
    }

    private sealed class NoMaterials : IGlowMaterials
    {
        public int Find(string name, string group) => 0;
    }

    [Fact]
    public void TheCreatorsMakeTheirFilesFlashes()
    {
        var answers = new Answers();
        GlowSettings settings = Settings(new() { ["building_cubemaps"] = "0", ["cl_sun_decay_rate"] = "0.05" });
        var system = new GlowOverlaySystem(answers, answers, settings, answers);
        var creators = new GlowOverlayCreators(system);

        creators.Fireball(new Vector(1, 2, 3));
        creators.ImpactOverlay(new Vector(4, 5, 6), new Vector(0.5f, 0.25f, 1));
        creators.ShotDown(new Vector(7, 8, 9));
        Assert.Equal(3, system.Count);

        // A tenth of a second later all three have faded out and leave at the draw.
        var host = new GlowHost(system, settings);
        var words = new int[1024];
        for (int i = 0; i < 7; i++)
            host.DrawOverlays(ViewFloats(1, false), false, words);
        Assert.Equal(0, system.Count);
    }

    private sealed class RampProbe : IGlowDraw
    {
        public void Clip(GlowClip clip) { }
        public void HdrColorScale(int material, float scale) { }
        public void Quad(int material, ReadOnlySpan<GlowVertex> vertices) { }
        public void WireframeQuad(int material, ReadOnlySpan<Vector> positions) { }
    }

    [Fact]
    public void TheFireballIsSixteenUnitsASecondAndTheImpactOne()
    {
        var answers = new Answers();
        GlowSettings settings = Settings(new() { ["building_cubemaps"] = "0", ["cl_sun_decay_rate"] = "0.05" });
        var system = new GlowOverlaySystem(answers, answers, settings, answers);
        var creators = new GlowOverlayCreators(system);
        creators.Fireball(Vector.Zero);
        creators.ImpactOverlay(Vector.Zero, new Vector(1, 1, 1));
        var fireball = Overlay(system, 0);
        var impact = Overlay(system, 1);
        Assert.Equal(0.05f, fireball.Sprites[0].HorzSize);
        Assert.Equal(0.025f, fireball.Sprites[0].VertSize);
        Assert.Equal(new Vector(1.0f, 0.9f, 0.7f), fireball.BaseColors[0]);
        Assert.Equal(0.01f, impact.Sprites[0].HorzSize);
        Assert.Equal(0.01f, impact.Sprites[0].VertSize);

        Assert.True(fireball.Update(0.05f));
        Assert.True(impact.Update(0.05f));
        Assert.Equal(0.05f + 16.0f * 0.05f, fireball.Sprites[0].HorzSize);
        Assert.Equal(0.01f + 1.0f * 0.05f, impact.Sprites[0].HorzSize);
        system.DrawOverlays(new GlowView(Vector.Zero, new Vector(1, 0, 0), 8000, []), false, new RampProbe());
    }

    private static RampOverlay Overlay(GlowOverlaySystem system, int index) => (RampOverlay)system.Overlays.ElementAt(index);

    private sealed class Sounds : IFxGameSounds
    {
        public readonly List<(string Wave, Vector Origin)> Emitted = [];

        public bool Parameters(string script, out GameSoundParams parameters)
        {
            parameters = new GameSoundParams(script + ".wav", 1, 100, 100, 100, 75, 0);
            return script == "Missile.ShotDown";
        }

        public void EmitAtPosition(in GameSoundParams parameters, float volume, int pitch, in Vector origin)
            => Emitted.Add((parameters.Wave, origin));

        public void EmitScript(string script, in Vector origin) => Emitted.Add((script, origin));
    }

    [Fact]
    public void TheRpgShotDownEffectSoundsAndFlashes()
    {
        var answers = new Answers();
        GlowSettings settings = Settings(new() { ["building_cubemaps"] = "0", ["cl_sun_decay_rate"] = "0.05" });
        var system = new GlowOverlaySystem(answers, answers, settings, answers);
        var sounds = new Sounds();
        var effect = new RpgShotDownEffect(sounds, new GlowOverlayCreators(system));
        Assert.Equal("RPGShotDown", effect.EffectName);

        effect.Dispatch(new EffectData { Origin = new Vector(10, 20, 30) });

        Assert.Equal([("Missile.ShotDown", new Vector(10, 20, 30))], sounds.Emitted);
        RampOverlay flash = Overlay(system, 0);
        Assert.Equal(new Vector(10, 20, 30), flash.Pos);
        Assert.Equal(0.01f, flash.Sprites[0].HorzSize);
        Assert.Equal(0.005f, flash.Sprites[0].VertSize);
        Assert.True(flash.Update(0.05f));
        Assert.Equal(0.01f + 16.0f * 0.05f, flash.Sprites[0].HorzSize);
    }
}
