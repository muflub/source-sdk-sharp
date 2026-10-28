using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_map_load P1: the spawn's material-reload window, driven as the hooked
/// engine calls drive it (AddSearchPath, Begin/EndMapAccess, ReloadMaterials).
/// Ported from map_material_reload_test.cpp with the state machine.
/// </summary>
public class MapMaterialReloadWindowTests
{
    private sealed class Spawn
    {
        public readonly List<string> Asked = [];   // "next|previous"
        public string? Answer;
        public readonly MapMaterialReloadWindow Window;

        public Spawn(string? answer)
        {
            Answer = answer;
            Window = new MapMaterialReloadWindow((next, previous) => { Asked.Add(next + "|" + previous); return Answer; });
        }

        public void Mount(string path)
        {
            Window.OnAddSearchPath(path, "GAME", addToHead: true);
            Window.OnBeginMapAccess();
        }
    }

    [Fact]
    public void OutsideASpawnTheFullReloadIsPassedThrough()
    {
        var s = new Spawn("tile/floor");
        Assert.Null(s.Window.Plan(null));
        Assert.Empty(s.Asked);
    }

    [Fact]
    public void ASubstringReloadIsAlwaysPassedThrough()
    {
        var s = new Spawn("tile/floor");
        s.Mount("/game/maps/a.bsp");
        Assert.Null(s.Window.Plan("tile/floor"));
        Assert.Empty(s.Asked);
        Assert.True(s.Window.IsArmed);
    }

    [Fact]
    public void InsideASpawnTheAnsweredNamesArePlanned()
    {
        var s = new Spawn("tile/floor\nmetal/grate");
        s.Mount("/game/maps/a.bsp");
        Assert.Equal("tile/floor\nmetal/grate", s.Window.Plan(null));
        Assert.Equal(["/game/maps/a.bsp|"], s.Asked);
        Assert.False(s.Window.IsArmed);
    }

    [Fact]
    public void AnEmptyAnswerPlansNothing()
    {
        var s = new Spawn("");
        s.Mount("/game/maps/a.bsp");
        Assert.Equal("", s.Window.Plan(null));
    }

    [Fact]
    public void NoAnswerIsTheFullReload()
    {
        var s = new Spawn(null);
        s.Mount("/game/maps/a.bsp");
        Assert.Null(s.Window.Plan(null));
        Assert.Single(s.Asked);
    }

    [Fact]
    public void OnlyAMapPakAtTheHeadOfGameArmsIt()
    {
        var s = new Spawn("");
        s.Window.OnAddSearchPath("/game/maps/a.bsp", "MOD", addToHead: true);
        s.Window.OnBeginMapAccess();
        Assert.False(s.Window.IsArmed);
        s.Window.OnAddSearchPath("/game/maps/a.bsp", "GAME", addToHead: false);
        s.Window.OnBeginMapAccess();
        Assert.False(s.Window.IsArmed);
        s.Window.OnAddSearchPath("/game/custom/pack.vpk", "GAME", addToHead: true);
        s.Window.OnBeginMapAccess();
        Assert.False(s.Window.IsArmed);
        s.Window.OnAddSearchPath("/game/maps/a.bsp", "GAME", addToHead: true);
        s.Window.OnBeginMapAccess();
        Assert.True(s.Window.IsArmed);   // the positive arm: the same calls do arm it
    }

    [Fact]
    public void APathTooLongForTheEnginesBufferDoesNotArmIt()
    {
        var s = new Spawn("");
        s.Window.OnAddSearchPath("/" + new string('x', MapMaterialReloadWindow.PathMax) + ".bsp", "GAME", addToHead: true);
        s.Window.OnBeginMapAccess();
        Assert.False(s.Window.IsArmed);
    }

    [Fact]
    public void EndMapAccessClosesTheWindow()
    {
        var s = new Spawn("");
        s.Mount("/game/maps/a.bsp");
        s.Window.OnEndMapAccess();
        Assert.Null(s.Window.Plan(null));   // a later mat_reloadallmaterials stays full
        Assert.Empty(s.Asked);
    }

    [Fact]
    public void TheNextSpawnIsToldThePreviousMap()
    {
        var s = new Spawn("");
        s.Mount("/game/maps/a.bsp");
        s.Window.Plan(null);
        s.Window.OnEndMapAccess();
        s.Mount("/game/maps/b.bsp");
        s.Window.Plan(null);
        Assert.Equal(["/game/maps/a.bsp|", "/game/maps/b.bsp|/game/maps/a.bsp"], s.Asked);
        Assert.Equal("/game/maps/b.bsp", s.Window.Previous);
    }

    // ---- the binding the hooks call ----------------------------------------

    private sealed class Host(MapMaterialReloadWindow window) : IClientRuntimeHost
    {
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) => true;
        public void LevelInitPreEntity() { }
        public void PostInit(int adapterMaxDxLevel) { }
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) => null;
        public void MapMaterialSearchPath(string? path, string? pathId, bool addToHead) => window.OnAddSearchPath(path, pathId, addToHead);
        public void MapMaterialMapAccess(bool open) { if (open) window.OnBeginMapAccess(); else window.OnEndMapAccess(); }
        public string? MapMaterialReloadPlan(string? subString) => window.Plan(subString);
        public int WorldDataChanged(ReadOnlySpan<byte> blob, int updateType, Span<float> plan) => 0;
    }

    private static ClientRuntimeBinding Binding(Crossings crossings, MapMaterialReloadWindow window)
    {
        var log = new RecordingLog();
        var models = new ModelDescriptors(_ => -1, _ => string.Empty, log);
        return new ClientRuntimeBinding(crossings, log, new Host(window), new StandaloneMirror(), models,
                                        new BreakModels(_ => -1, _ => -1, _ => null, models, log));
    }

    private static unsafe void Mount(ClientRuntimeBinding binding, string path)
    {
        byte[] p = Encoding.UTF8.GetBytes(path + "\0");
        byte[] id = "GAME\0"u8.ToArray();
        fixed (byte* pp = p) fixed (byte* pid = id)
            binding.MapMaterialSearchPath(pp, pid, 1);
        binding.MapMaterialMapAccess(1);
    }

    private static unsafe int Plan(ClientRuntimeBinding binding, string? sub, int size, out string written)
    {
        byte[] s = sub is null ? [] : Encoding.UTF8.GetBytes(sub + "\0");
        var outBuf = new byte[size];
        int r;
        fixed (byte* ps = s) fixed (byte* po = outBuf)
            r = binding.MapMaterialReloadPlan(sub is null ? null : ps, po, size);
        written = r > 0 ? Encoding.UTF8.GetString(outBuf, 0, r) : string.Empty;
        return r;
    }

    [Fact]
    public void TheBindingWritesThePlannedNames()
    {
        var binding = Binding(new Crossings(), new MapMaterialReloadWindow((_, _) => "tile/floor\nmetal/grate"));
        Mount(binding, "/game/maps/a.bsp");

        Assert.Equal(22, Plan(binding, null, 256, out string written));
        Assert.Equal("tile/floor\nmetal/grate", written);
    }

    [Fact]
    public void ThePassThroughIsMinusOne()
    {
        var binding = Binding(new Crossings(), new MapMaterialReloadWindow((_, _) => "tile/floor"));
        Mount(binding, "/game/maps/a.bsp");

        Assert.Equal(-1, Plan(binding, "tile/floor", 256, out _));
    }

    [Fact]
    public void APlanThatDoesNotFitIsTheFullReload()
    {
        var binding = Binding(new Crossings(), new MapMaterialReloadWindow((_, _) => new string('x', 300)));
        Mount(binding, "/game/maps/a.bsp");

        Assert.Equal(-1, Plan(binding, null, 256, out _));
    }

    [Fact]
    public void TheThreeHooksCountTheirCrossings()
    {
        var crossings = new Crossings();
        var binding = Binding(crossings, new MapMaterialReloadWindow((_, _) => ""));

        Mount(binding, "/game/maps/a.bsp");
        Plan(binding, null, 16, out _);
        binding.MapMaterialMapAccess(0);

        Assert.Equal(1, crossings.Read(CrossingOp.ClientMapMaterialSearchPath));
        Assert.Equal(2, crossings.Read(CrossingOp.ClientMapMaterialMapAccess));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientMapMaterialReloadPlan));
    }
}
