using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.PixelVisibility;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The pixel visibility's boundary (C7): the command words and the view a
/// view's end crosses with (shell_pixelvis_client.h against
/// <see cref="PixelVisCommand"/> and <see cref="PixelVisHost"/>), the words
/// <see cref="PixelVisHost"/> writes, and the sprite request's and plan's
/// handle slots (shell_sprite_leaf.cpp against <see cref="ClientSpriteHost"/>).
/// </summary>
public class PixelVisibilityAbiTests
{
    private static string Native(string file)
    {
        string path = RepoLayout.Repo("src", "client", "native", file);
        Assert.True(File.Exists(path), "missing: " + path);
        return AbiParityTests.CodeOnly(File.ReadAllText(path));
    }

    private static int Value(string code, string name)
    {
        Match m = Regex.Match(code, $@"\b{name}\s*=\s*([0-9 +]+),");
        Assert.True(m.Success, name + " is not declared");
        return m.Groups[1].Value.Split('+').Sum(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TheCommandWordsAreTheHeadersWords()
    {
        string h = Native("shell_pixelvis_client.h");
        Assert.Equal((int)PixelVisCommand.Bind, Value(h, "SS_PIXELVIS_CMD_BIND"));
        Assert.Equal((int)PixelVisCommand.DepthRange, Value(h, "SS_PIXELVIS_CMD_DEPTHRANGE"));
        Assert.Equal((int)PixelVisCommand.Proxy, Value(h, "SS_PIXELVIS_CMD_PROXY"));
        Assert.Equal(PixelVisHost.ProxyWords, Value(h, "SS_PIXELVIS_PROXY_WORDS"));
        Assert.Equal(PixelVisHost.ViewFloats, Value(h, "SS_PIXELVIS_VIEW_FLOATS"));
        Assert.Equal(3, Enum.GetValues<PixelVisCommand>().Length);
    }

    [Fact]
    public void TheSpriteHandleSlotsCloseTheRequestAndThePlan()
    {
        string code = Native("shell_sprite_leaf.cpp");
        Assert.Matches(@"SS_SPRITE_IN_QUERY_HANDLE,\s*SS_SPRITE_IN_FLOATS,", code);
        Assert.Matches(@"SS_SPRITE_OUT_QUERY_HANDLE,\s*SS_SPRITE_OUT_FLOATS,", code);
        Match inFloats = Regex.Match(code, @"COMPILE_TIME_ASSERT\( SS_SPRITE_IN_FLOATS == (\d+) \);");
        Match outFloats = Regex.Match(code, @"COMPILE_TIME_ASSERT\( SS_SPRITE_OUT_FLOATS == (\d+) \);");
        Assert.True(inFloats.Success && outFloats.Success, "the two counts are asserted natively");
        Assert.Equal(ClientSpriteHost.InFloats, int.Parse(inFloats.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(ClientSpriteHost.OutFloats, int.Parse(outFloats.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(ClientSpriteHost.InFloats - 1, ClientSpriteHost.InQueryHandle);
        Assert.Equal(ClientSpriteHost.OutFloats - 1, ClientSpriteHost.OutQueryHandle);
        Assert.Equal(ClientSpriteRequest.InFloats, ClientSpriteHost.InFloats);
        Assert.Equal(ClientSpriteRequest.OutFloats, ClientSpriteHost.OutFloats);
    }

    // A system whose one set's proxy is in front of the eye.
    private sealed class World : IPixelVisEngine, ICurrentView, IPixelVisFrame, IPixelVisSettings
    {
        private int _next = 1;
        public int Frame = 1;
        public int DxSupportLevel() => 95;
        public int CreateQuery() => _next++;
        public readonly List<int> Destroyed = [];
        public void DestroyQuery(int query) => Destroyed.Add(query);
        public void ResetQuery(int query) { }
        public void PixelsRendered(int visible, int count, out int visiblePixels, out int countPixels)
        {
            visiblePixels = 10;
            countPixels = 20;
        }
        public void PixelDiameters(ReadOnlySpan<float> spheres, Span<float> diameters) => diameters.Fill(4.0f);
        public int FindMaterial(string name) => name.EndsWith("countdraw", StringComparison.Ordinal) ? 6 : 5;
        public void ReleaseMaterial(int material) { }
        public float TraceGlowSight(in Vector start, in Vector end) => 1.0f;
        public void DevMsg(int level, string text) { }
        public void Msg(string text) { }
        public CurrentViewState Read() => new(0, Vector.Zero, new Vector(1, 0, 0), new Vector(0, -1, 0), new Vector(0, 0, 1));
        public int FrameCount => Frame;
        public float FrameTime => 1 / 60f;
        public bool Partial => true;
        public bool Enabled => true;
        public bool Spew => false;
        public bool BuildingCubemaps => false;
    }

    private static float[] ViewFloats()
    {
        var v = new float[PixelVisHost.ViewFloats];
        v[4] = 1; v[8] = -1; v[12] = 1;          // forward x, right -y, up z
        float[] m = [0, -1, 0, 0, 0, 0, 1.7777778f, 0, 0, 0, 1, 0, 1, 0, 0, 0];
        m.CopyTo(v, 13);
        return v;
    }

    private static (PixelVisibilitySystem System, PixelVisHost Host) Asked()
    {
        var world = new World();
        var system = new PixelVisibilitySystem(world, world, world, world);
        system.LevelInitPreEntity();
        int handle = 0;
        system.FractionVisible(new PixelVisParams(new Vector(200, 10, 5)), ref handle);
        return (system, new PixelVisHost(system));
    }

    [Fact]
    public void AViewsEndIsWrittenAsTheExecutorReadsIt()
    {
        (_, PixelVisHost host) = Asked();
        var words = new int[256];
        int n = host.EndView(ViewFloats(), words);

        // Bind, the counting pass between its depth ranges, then the visible pass.
        int expected = 2 + 3 + PixelVisHost.ProxyWords + 3 + PixelVisHost.ProxyWords;
        Assert.Equal(expected, n);
        Assert.Equal([(int)PixelVisCommand.Bind, 5], words[..2]);
        Assert.Equal((int)PixelVisCommand.DepthRange, words[2]);
        Assert.Equal(0.0f, BitConverter.Int32BitsToSingle(words[3]));
        Assert.Equal(0.01f, BitConverter.Int32BitsToSingle(words[4]));
        Assert.Equal((int)PixelVisCommand.Proxy, words[5]);
        Assert.Equal(3, words[6]);                // the set's counting query (1 was the level start's probe)
        Assert.Equal(5, words[7]);
        int second = 5 + PixelVisHost.ProxyWords;
        Assert.Equal((int)PixelVisCommand.DepthRange, words[second]);
        Assert.Equal(1.0f, BitConverter.Int32BitsToSingle(words[second + 2]));
        Assert.Equal((int)PixelVisCommand.Proxy, words[second + 3]);
        Assert.Equal(2, words[second + 4]);       // its visible query
        // The pyramid's apex is the proxy's origin pulled toward the eye.
        Assert.True(BitConverter.Int32BitsToSingle(words[second + 6]) is > 197 and < 200);
    }

    [Fact]
    public void AReloadMidLevelDestroysTheOldQueriesAndKeepsTheLevel()
    {
        var world = new World();
        var old = new PixelVisibilitySystem(world, world, world, world);
        old.LevelInitPreEntity();
        int handle = 0;
        old.FractionVisible(new PixelVisParams(new Vector(200, 10, 5)), ref handle);
        var writer = new ReloadWriter();
        ((IReloadParticipant)old).Save(writer);
        world.Destroyed.Clear();

        var fresh = new PixelVisibilitySystem(world, world, world, world);
        ((IReloadParticipant)fresh).Restore(new ReloadReader(writer.ToArray()));

        // The old build's two query objects (its set's visible and counting ones) are destroyed.
        Assert.Equal([2, 3], world.Destroyed);
        Assert.True(fresh.LevelActive);
        // The caller's old handle names nothing now: its question makes a new set, and the level's
        // end releases the materials the old build found and destroys the new queries.
        int again = handle;
        fresh.FractionVisible(new PixelVisParams(new Vector(200, 10, 5)), ref again);
        Assert.Equal(1, again);
        Assert.Equal((1, 0, 1, 0), fresh.Counts);
        world.Destroyed.Clear();
        fresh.LevelShutdownPostEntity();
        Assert.Equal([4, 5], world.Destroyed);
        Assert.False(fresh.LevelActive);
    }

    [Fact]
    public void CommandsThatDoNotFitAreAskedForAgain()
    {
        (_, PixelVisHost host) = Asked();
        var small = new int[8];
        int n = host.EndView(ViewFloats(), small);
        Assert.True(n > small.Length, $"{n} words are more than eight");
        Assert.All(small, w => Assert.Equal(0, w));

        var large = new int[n];
        Assert.Equal(n, host.Commands(large));
        Assert.Equal((int)PixelVisCommand.Bind, large[0]);
        Assert.Equal((int)PixelVisCommand.Proxy, large[n - PixelVisHost.ProxyWords]);
    }

    private sealed class HandleGlow : IFxSpriteGlow
    {
        public int Asked = -1;

        public float FractionVisible(in Vector position, float proxySize, float proxyAspect, ref int queryHandle)
        {
            Asked = queryHandle;
            queryHandle = 0x00030004;
            return 1.0f;
        }

        public float SightDistance(in Vector position) => 100.0f;
    }

    [Fact]
    public void ASpritesPlanCarriesItsRenderersHandleBothWays()
    {
        var glow = new HandleGlow();
        var host = new ClientSpriteHost(new SpriteRenderer(new NullLog()), glow, null!);
        var input = new float[ClientSpriteHost.InFloats];
        input[ClientSpriteHost.InDrawSprites] = 1;
        input[ClientSpriteHost.InRenderMode] = (int)RenderMode.Glow;
        input[ClientSpriteHost.InAlpha] = 255;
        input[ClientSpriteHost.InBlend] = 1;
        input[ClientSpriteHost.InScale] = 1;
        input[ClientSpriteHost.InGlowProxySize] = 2;
        input[ClientSpriteHost.InQueryHandle] = BitConverter.Int32BitsToSingle(0x00010002);
        var output = new float[ClientSpriteHost.OutFloats];

        Assert.Equal(ClientSpriteHost.OutFloats, host.Plan(input, output));
        Assert.Equal(0x00010002, glow.Asked);
        Assert.Equal(0x00030004, BitConverter.SingleToInt32Bits(output[ClientSpriteHost.OutQueryHandle]));
    }

    [Fact]
    public void ASpriteThatDrawsNothingHandsItsHandleBackUnchanged()
    {
        var glow = new HandleGlow();
        var host = new ClientSpriteHost(new SpriteRenderer(new NullLog()), glow, null!);
        var input = new float[ClientSpriteHost.InFloats];
        input[ClientSpriteHost.InQueryHandle] = BitConverter.Int32BitsToSingle(0x00010002);
        var output = new float[ClientSpriteHost.OutFloats];

        Assert.Equal(ClientSpriteHost.OutFloats, host.Plan(input, output));
        Assert.Equal(-1, glow.Asked);
        Assert.Equal(0x00010002, BitConverter.SingleToInt32Bits(output[ClientSpriteHost.OutQueryHandle]));
    }

    private sealed class NullLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private sealed class ListLog : ILog
    {
        public readonly List<string> Lines = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W " + message);
    }

    private sealed class Slots : IClientConVarSlots
    {
        public readonly Dictionary<string, int> Ids = new(StringComparer.Ordinal);

        public int Watch(string name)
        {
            int id = 1000 + Ids.Count;
            Ids[name] = id;
            return id;
        }

        public bool SetValue(string name, float value) => false;
    }

    [Fact]
    public void TheDrawConVarsChangeIsStocksCallback()
    {
        var draw = new DrawPixelVisibilityConVar();
        var shown = new List<bool>();
        draw.Changed += shown.Add;

        draw.NotifyChanged("1", "0");
        draw.NotifyChanged("0", "1");

        Assert.Equal([true, false], shown);
    }

    [Fact]
    public void BuildingCubemapsIsTheEnginesWatchedByName()
    {
        var slots = new Slots();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { [BuildingCubemapsWatch.Name] = "0" };
        var watch = new BuildingCubemapsWatch(new SourceSharp.Client.Flashlight.WatchedConVarValues(slots, n => values.GetValueOrDefault(n)));
        watch.Start();
        Assert.Equal(["building_cubemaps"], slots.Ids.Keys);
        Assert.False(watch.Value);

        Assert.True(watch.OnChanged(slots.Ids[BuildingCubemapsWatch.Name], "1"));
        Assert.True(watch.Value);
        Assert.False(watch.OnChanged(99, "0"));
        Assert.True(watch.Value);
    }

    [Fact]
    public void TheSettingsReadTheirConVarsAtEachQuestion()
    {
        bool partial = true, enabled = true, spew = false, cubemaps = false;
        var settings = new PixelVisSettings(() => partial, () => enabled, () => spew, () => cubemaps);
        Assert.True(settings.Partial && settings.Enabled && !settings.Spew && !settings.BuildingCubemaps);
        (partial, enabled, spew, cubemaps) = (false, false, true, true);
        Assert.True(!settings.Partial && !settings.Enabled && settings.Spew && settings.BuildingCubemaps);
    }

    [Fact]
    public void TheEngineTracesTheGlowLineAndGatesDevMsgOnDeveloper()
    {
        var engine = TestServices.Fake();
        engine.Strict = false;
        engine.Returns("WorldQuery.ClientTraceLine", new ClientTraceResult(true, 0.5f, Vector.Zero, Vector.Zero, 0, -1, 0, 0));
        var log = new ListLog();
        string developer = "0";
        var pixelVis = new ClientPixelVisEngine(null!, null!, engine, () => 95, log, n => n == "developer" ? developer : null);

        Assert.Equal(0.5f, pixelVis.TraceGlowSight(Vector.Zero, new Vector(10, 0, 0)));
        var call = engine.Last("WorldQuery.ClientTraceLine")!.Value;
        Assert.Equal(TraceMask.GlowSight, call.ArgAs<TraceMask>(2));
        Assert.Equal(TraceFilter.Glow, call.ArgAs<TraceFilter>(3));

        pixelVis.DevMsg(1, "hidden\n");
        developer = "1";
        pixelVis.DevMsg(1, "shown\n");
        pixelVis.DevMsg(2, "too deep\n");
        Assert.Equal(["shown\n"], log.Lines);
        Assert.Equal(95, pixelVis.DxSupportLevel());
    }
}
