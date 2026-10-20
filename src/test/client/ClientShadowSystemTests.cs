using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Render.Shadows;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The wiring of the managed shadow manager (S2 of
/// docs/plans/c7_shadow_manager.md): the entities' half (their shadow handles
/// and dirty bits, and the shells' queue applied), the native binding's
/// slots, the record kinds' agreement with fwd_shadows.h, and the two
/// proxies' bind through the managed proxy host (Q1). Each runs over the
/// oracle replay's stand-ins where a manager is needed.
/// </summary>
public unsafe class ClientShadowSystemTests
{
    private static string X(float f) => ShadowManagerOracleTests.X(f);

    private static string V(float x, float y, float z) => X(x) + "," + X(y) + "," + X(z);

    private const string Zero3 = "00000000,00000000,00000000";

    private static string T(int id, int modelType, float x, float y, float z, float h, bool gone = false)
    {
        string transform = string.Join(',', new[] { 1f, 0f, 0f, x, 0f, 1f, 0f, y, 0f, 0f, 1f, z }.Select(X));
        string mins = V(-h, -h, -h), maxs = V(h, h, h);
        return string.Join('\t', "T", id, 1, modelType, V(x, y, z), Zero3, mins, maxs, mins, maxs, V(x - h, y - h, z - h), V(x + h, y + h, z + h), transform,
                           0, X(0), 0, Zero3, -1, -1, -1, 0, 65535, 0, 65535, 0, 0, 1, 0, gone ? 1 : 0);
    }

    private static readonly string[] World =
    [
        "ENV\tleaves\t40\t" + X(100f),
        "ENV\tviewbox\t" + V(-100, -1000, -500) + "\t" + V(3000, 1000, 500),
        "ENV\tambient\t" + V(0.1f, 0.2f, 0.5f),
        "ENV\tppu\t" + X(4f),
        "ENV\tdxlevel\t90",
        "ENV\tsupportsdepth\t1",
        "ENV\tmaxrendertarget\t2048",
        "ENV\tfog\t1",
        "ENV\theightclip\t1",
        "ENV\ttools\t0",
        "F\t1\t" + X(0.015f),
        "C\tInit",
        "C\tLevelInitPreEntity",
    ];

    // The replay's entity handle for a thing (its id, serial 1).
    private static uint Entity(int id) => (uint)id | (1u << 16);

    // A manager over the replay's stand-ins, whose renderables' shadow handle
    // and dirty bit are the entities' half's, as in the game.
    private static (ShadowManagerOracleTests.Replay Replay, EntityShadows Entities) World0(params string[] things)
    {
        var replay = new ShadowManagerOracleTests.Replay();
        var entities = new EntityShadows(replay);
        replay.Manager = new ShadowManager(replay, replay, replay, replay, entities, replay, replay, replay, replay);
        foreach (string row in World.Concat(things))
            replay.Run(row);
        return (replay, entities);
    }

    private static int Count(ShadowManagerOracleTests.Replay replay, string prefix) =>
        replay.Log.Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

    private static int[] Entry(int kind, uint entity, int value) => [kind, unchecked((int)entity), value];

    private const int Simple = ShadowTypes.Simple;
    private static readonly int SimpleFlags = ShadowFlags.Shadow;

    //-------------------------------------------------------------------------
    // The entities' half and the queue.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARefreshCreatesTheEntitysShadowOnceAndASecondKeepsIt()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10));
        int before = Count(replay, "E\tsm.CreateShadowEx");

        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)), replay.Manager);
        Assert.Equal(before + 1, Count(replay, "E\tsm.CreateShadowEx"));
        Assert.True(entities.HasShadow(Entity(5)));
        int shadow = entities.ShadowHandle(replay.RenderableOfThing(5));
        Assert.True(replay.Manager.IsShadow(shadow));
        Assert.Equal(Entity(5), replay.Manager.EntityOf(shadow));

        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)), replay.Manager);
        Assert.Equal(before + 1, Count(replay, "E\tsm.CreateShadowEx"));
    }

    [Fact]
    public void ARefreshToNoShadowAndADestroyEachDestroyItOnce()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10), T(6, 3, 200, 0, 40, 10));
        entities.Apply([.. Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)),
                        .. Entry(EntityShadows.PendingRefresh, Entity(6), Simple | (SimpleFlags << 8))], replay.Manager);
        int destroys = Count(replay, "E\tsm.DestroyShadow");

        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), ShadowTypes.None), replay.Manager);
        Assert.Equal(destroys + 1, Count(replay, "E\tsm.DestroyShadow"));
        Assert.False(entities.HasShadow(Entity(5)));

        entities.Apply([.. Entry(EntityShadows.PendingDestroy, Entity(6), 0), .. Entry(EntityShadows.PendingDestroy, Entity(6), 0)], replay.Manager);
        Assert.Equal(destroys + 2, Count(replay, "E\tsm.DestroyShadow"));
        Assert.False(entities.HasShadow(Entity(6)));
        Assert.Equal(0, entities.Count);
    }

    [Fact]
    public void ADirtyMarkPutsTheRenderableOnTheDirtyListOnceAndThePreRenderTakesItOff()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10));
        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)), replay.Manager);
        nint renderable = replay.RenderableOfThing(5);
        // CreateShadow put it on the list; the frame takes it off.
        Assert.True(entities.IsShadowDirty(renderable));
        replay.Run("C\tPreRender");
        Assert.False(entities.IsShadowDirty(renderable));

        entities.Apply(Entry(EntityShadows.PendingDirty, Entity(5), 0), replay.Manager);
        Assert.True(entities.IsShadowDirty(renderable));
        int enables = Count(replay, "E\tsm.EnableShadow");
        replay.Run("C\tPreRender");
        Assert.True(Count(replay, "E\tsm.EnableShadow") > enables, "the dirty walk did not update the marked shadow");
        Assert.False(entities.IsShadowDirty(renderable));
    }

    [Fact]
    public void ADirtyMarkWithTheTextureBitMarksTheTextureDirty()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10));
        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)), replay.Manager);
        int shadow = entities.ShadowHandle(replay.RenderableOfThing(5));
        replay.Run("C\tPreRender");
        // Clear the bit the creation set, as a drawn texture does.
        replay.Manager.Shadow(shadow).Flags &= ~ShadowFlags.TextureDirty;

        entities.Apply(Entry(EntityShadows.PendingDirty, Entity(5), 0), replay.Manager);
        Assert.Equal(0, replay.Manager.FlagsOf(shadow) & ShadowFlags.TextureDirty);
        entities.Apply(Entry(EntityShadows.PendingDirty, Entity(5), 1), replay.Manager);
        Assert.NotEqual(0, replay.Manager.FlagsOf(shadow) & ShadowFlags.TextureDirty);
    }

    [Fact]
    public void AnEntityWithNoShadowIsNotDirtiedAndAnUnknownDestroyDoesNothing()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10));
        int calls = replay.Log.Count;
        entities.Apply([.. Entry(EntityShadows.PendingDirty, Entity(5), 1), .. Entry(EntityShadows.PendingDestroy, Entity(5), 0)], replay.Manager);
        Assert.Equal(calls, replay.Log.Count);
        Assert.False(entities.IsShadowDirty(replay.RenderableOfThing(5)));
    }

    [Fact]
    public void AShadowTheManagerRetiredIsNotDestroyedAgainByTheEntitysDestroy()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10));
        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)), replay.Manager);
        // The renderable goes without a destroy: the walk retires the shadow.
        replay.Run(T(5, 3, 100, 0, 40, 10, gone: true));
        replay.Run("C\tPreRender");
        int destroys = Count(replay, "E\tsm.DestroyShadow");
        Assert.True(destroys > 0, "the walk did not retire the shadow whose renderable went");

        entities.Apply(Entry(EntityShadows.PendingDestroy, Entity(5), 0), replay.Manager);
        Assert.Equal(destroys, Count(replay, "E\tsm.DestroyShadow"));
        Assert.False(entities.HasShadow(Entity(5)));
    }

    [Fact]
    public void AHotReloadCarriesTheEntitiesHalf()
    {
        var (replay, entities) = World0(T(5, 3, 100, 0, 40, 10));
        entities.Apply(Entry(EntityShadows.PendingRefresh, Entity(5), Simple | (SimpleFlags << 8)), replay.Manager);
        nint renderable = replay.RenderableOfThing(5);
        int shadow = entities.ShadowHandle(renderable);

        var writer = new ReloadWriter();
        entities.Save(writer);
        var restored = new EntityShadows(replay);
        restored.Restore(new ReloadReader(writer.ToArray()));

        Assert.True(restored.HasShadow(Entity(5)));
        Assert.Equal(shadow, restored.ShadowHandle(renderable));
        Assert.Equal(entities.IsShadowDirty(renderable), restored.IsShadowDirty(renderable));
        Assert.Equal(ShadowFlags.InvalidHandle, restored.ShadowHandle(replay.RenderableOfThing(5) + 1));
    }

    //-------------------------------------------------------------------------
    // The native binding.
    //-------------------------------------------------------------------------

    private sealed class Host : IClientShadowsHost
    {
        public readonly List<string> Calls = [];
        public bool InitAnswer = true;

        public bool Init()
        {
            Calls.Add("Init");
            return InitAnswer;
        }

        public void Shutdown() => Calls.Add("Shutdown");

        public void LevelInitPreEntity() => Calls.Add("LevelInitPreEntity");

        public void LevelShutdownPostEntity() => Calls.Add("LevelShutdownPostEntity");

        public void PreRender(ReadOnlySpan<int> pending) => Calls.Add("PreRender " + string.Join(',', pending.ToArray()));

        public void AdvanceFrame(ReadOnlySpan<int> pending) => Calls.Add("AdvanceFrame " + string.Join(',', pending.ToArray()));

        public void ComputeShadowTextures(ReadOnlySpan<int> leaves, ReadOnlySpan<int> pending) =>
            Calls.Add("ComputeShadowTextures " + string.Join(',', leaves.ToArray()) + " / " + string.Join(',', pending.ToArray()));

        public void ComputeShadowDepthTextures(in ShadowViewSetup view, ReadOnlySpan<int> pending) =>
            Calls.Add(FormattableString.Invariant($"ComputeShadowDepthTextures {view.Origin.X} {view.Angles.Yaw} {view.ZNear} {view.ZFar} {view.Fov} {view.AspectRatio} / {pending.Length}"));

        public void UnlockAllShadowDepthTextures(ReadOnlySpan<int> pending) => Calls.Add("UnlockAllShadowDepthTextures " + pending.Length);

        public void RestoreRenderState() => Calls.Add("RestoreRenderState");

        public void RenderShadowTexture(int width, int height) => Calls.Add($"RenderShadowTexture {width} {height}");

        public void Pending(ReadOnlySpan<int> entries) => Calls.Add("Pending " + string.Join(',', entries.ToArray()));
    }

    private sealed class Thrower : IClientShadowsHost
    {
        public bool Init() => throw new InvalidOperationException("init");
        public void Shutdown() => throw new InvalidOperationException();
        public void LevelInitPreEntity() => throw new InvalidOperationException();
        public void LevelShutdownPostEntity() => throw new InvalidOperationException();
        public void PreRender(ReadOnlySpan<int> pending) => throw new InvalidOperationException();
        public void AdvanceFrame(ReadOnlySpan<int> pending) => throw new InvalidOperationException();
        public void ComputeShadowTextures(ReadOnlySpan<int> leaves, ReadOnlySpan<int> pending) => throw new InvalidOperationException();
        public void ComputeShadowDepthTextures(in ShadowViewSetup view, ReadOnlySpan<int> pending) => throw new InvalidOperationException();
        public void UnlockAllShadowDepthTextures(ReadOnlySpan<int> pending) => throw new InvalidOperationException();
        public void RestoreRenderState() => throw new InvalidOperationException();
        public void RenderShadowTexture(int width, int height) => throw new InvalidOperationException();
        public void Pending(ReadOnlySpan<int> entries) => throw new InvalidOperationException();
    }

    [Fact]
    public void TheBindingHandsEachSlotToTheHostAndCountsItsCrossing()
    {
        var crossings = new Crossings();
        var host = new Host();
        var binding = new ShadowsClientBinding(crossings, new RecordingLog(), host);

        int[] pending = [1, 7, 1, 3, 9, 0];
        ushort[] leaves = [4, 9, 2];
        float[] view = [1, 2, 3, 0, 90, 0, 4, 300, 60, 1.5f];
        Assert.Equal(1, binding.Init());
        fixed (int* p = pending)
            binding.PreRender(p, 2);
        fixed (ushort* l = leaves)
        fixed (int* p2 = pending)
            binding.ComputeShadowTextures(l, 3, p2, 1);
        fixed (float* v = view)
            binding.ComputeShadowDepthTextures(v, 10, null, 0);
        fixed (int* p = pending)
            binding.Pending(p, 1);
        binding.RenderShadowTexture(256, 128);

        Assert.Equal(["Init", "PreRender 1,7,1,3,9,0", "ComputeShadowTextures 4,9,2 / 1,7,1", "ComputeShadowDepthTextures 1 90 4 300 60 1.5 / 0",
                      "Pending 1,7,1", "RenderShadowTexture 256 128"], host.Calls);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientShadowsPreRender));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientShadowsComputeShadowTextures));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientShadowsPending));
    }

    [Fact]
    public void AShortViewIsRefusedAndAnEmptyQueueIsNone()
    {
        var host = new Host();
        var binding = new ShadowsClientBinding(new Crossings(), new RecordingLog(), host);
        float[] view = new float[9];
        fixed (float* v = view)
            binding.ComputeShadowDepthTextures(v, 9, null, 0);
        binding.PreRender(null, 0);
        Assert.Equal(["PreRender "], host.Calls);
    }

    [Fact]
    public void AThrowingHostRefusesNothingAndEverySlotIsCaught()
    {
        var log = new RecordingLog();
        var binding = new ShadowsClientBinding(new Crossings(), log, new Thrower());
        Assert.Equal(1, binding.Init());
        binding.Shutdown();
        binding.PreRender(null, 0);
        binding.AdvanceFrame(null, 0);
        binding.RestoreRenderState();
        Assert.Contains(log.Lines, l => l.Warning);
    }

    //-------------------------------------------------------------------------
    // The record kinds, held to fwd_shadows.h's.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheRecordKindsAreTheNativeHeadersNameForNameAndNumberForNumber()
    {
        string path = RepoLayout.Repo("src", "fwd", "native", "fwd_shadows.h");
        Assert.True(File.Exists(path), path);
        string text = File.ReadAllText(path);
        var native = Regex.Matches(text, @"SS_SHADOW_(\w+)\s*=\s*(\d+)\s*,")
                          .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        var managed = Enum.GetValues<ShadowNativeOp>().ToDictionary(k => k.ToString(), k => (int)k);
        Assert.True(native.Count == 82, $"read {native.Count} kinds from fwd_shadows.h, not the 49 calls and 33 questions");
        Assert.Equal(native.OrderBy(k => k.Value).Select(k => (k.Key, k.Value)), managed.OrderBy(k => k.Value).Select(k => (k.Key, k.Value)));
        Assert.All(managed.Where(k => k.Value >= 64), k => Assert.True(k.Value < 128));
    }

    //-------------------------------------------------------------------------
    // The ConVars and commands, held to the C0 snapshot of client.so's own.
    //-------------------------------------------------------------------------

    private static Dictionary<string, string[]> Contract(string file)
    {
        string path = RepoLayout.Repo("docs", "novalve-snapshot", "client-c0", file);
        Assert.True(File.Exists(path), path);
        return File.ReadAllLines(path).Select(l => l.Split('\t')).Where(f => f.Length > 1).GroupBy(f => f[0]).ToDictionary(g => g.Key, g => g.First());
    }

    private static readonly string[] EngineOwned =
        ["r_shadowrendertotexture", "r_flashlightdepthtexture", "r_shadows", "r_shadows_gamecontrol", "r_flashlight_version2"];

    [Fact]
    public void TheShadowManagersConVarsAreClientDllsOwnAndTheEnginesFiveAreReadByName()
    {
        // The managed declarations in the shadows namespace: each a ClientDll
        // convar of the C0 snapshot, with its default. The engine's five are
        // absent from the snapshot (they are not client.so's), and no managed
        // class declares them: a declaration of an engine name refuses the load.
        Dictionary<string, string[]> convars = Contract("client-convars-contract.txt");
        var declared = typeof(ShadowConVars).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ShadowConVars).Namespace)
            .Select(t => (Type: t, Attr: t.GetCustomAttributes(typeof(ConVarAttribute), false).Cast<ConVarAttribute>().FirstOrDefault()))
            .Where(x => x.Attr is not null)
            .ToDictionary(x => x.Attr!.Name, x => x.Attr!);
        Assert.Equal(["r_flashlightdepthres", "r_flashlightdrawfrustum", "r_flashlightmodels", "r_shadowmaxrendered", "r_threaded_client_shadow_manager"],
                     declared.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, attr) in declared)
        {
            Assert.True(convars.TryGetValue(name, out string[]? row), name + " is not in the C0 snapshot");
            Assert.Contains("ClientDll", row![5], StringComparison.Ordinal);
            Assert.Equal(row[1], attr.DefaultValue);
        }
        foreach (string name in EngineOwned)
        {
            Assert.False(convars.ContainsKey(name), name + " is client.so's in the snapshot");
            Assert.Contains(name, ShadowConVars.EngineNames());
        }
    }

    [Fact]
    public void TheShadowManagersCommandsAreTheSnapshotsWithTheirCheatFlags()
    {
        Dictionary<string, string[]> commands = Contract("client-commands-contract.txt");
        var declared = typeof(ShadowConVars).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ShadowConVars).Namespace)
            .Select(t => t.GetCustomAttributes(typeof(ConCommandAttribute), false).Cast<ConCommandAttribute>().FirstOrDefault())
            .Where(a => a is not null)
            .ToDictionary(a => a!.Name, a => a!);
        string[] stock = ["r_shadowdir", "r_shadowangles", "r_shadowcolor", "r_shadowdist", "r_shadowblobbycutoff", "cs_cl_shadow_report"];
        foreach (string name in stock)
        {
            Assert.True(declared.TryGetValue(name, out ConCommandAttribute? attr), name + " is not declared");
            Assert.True(commands.TryGetValue(name, out string[]? row), name + " is not in the C0 snapshot");
            Assert.Equal(row![2].Contains("Cheat", StringComparison.Ordinal), attr!.Flags.HasFlag(ConVarFlags.Cheat));
        }
#if DEBUG
        Assert.True(declared.ContainsKey("r_shadowbreak"), "stock's _DEBUG build's r_shadowbreak");
#endif
    }

    //-------------------------------------------------------------------------
    // The proxies through the managed proxy host (Q1).
    //-------------------------------------------------------------------------

    private sealed class Source : IShadowProxySource
    {
        public int AtlasPage { get; set; } = 3;

        public readonly Dictionary<int, ShadowEngineInfo> Infos = [];

        public int Asked;

        public bool TryGetShadowInfo(int clientShadowHandle, out ShadowEngineInfo info)
        {
            Asked++;
            return Infos.TryGetValue(clientShadowHandle, out info);
        }
    }

    private static MaterialProxyHost ShadowHost(MaterialProxyHostTests.FakeExecutor exec, Source source)
        => new(exec, new MaterialProxyHostTests.QueueUniform(), () => false, new StandardErrorLog(), null, null, source);

    [Fact]
    public void TheShadowProxyBindsTheAtlasThroughTheHost()
    {
        var exec = new MaterialProxyHostTests.FakeExecutor();
        exec.TextureSlots["_rt_Shadows"] = 11;
        var host = ShadowHost(exec, new Source());
        Assert.True(host.Init(4, "Shadow", "decals/rendershadow", []));

        List<ProxyOp> ops = MaterialProxyHostTests.Bind(host, 4, 0, 1, default, arg: 6);
        ProxyOp op = Assert.Single(ops);
        Assert.Equal((int)ProxyOpKind.Texture, op.Kind);
        Assert.Equal(11, op.Arg);
    }

    [Fact]
    public void TheShadowModelProxyWritesWhatTheEnginesInfoSaysThroughTheHost()
    {
        var exec = new MaterialProxyHostTests.FakeExecutor();
        foreach (string var in new[] { "$basetextureoffset", "$basetexturescale", "$basetexturetransform", "$falloffoffset", "$falloffdistance", "$falloffamount" })
            exec.Vars.Add(var);
        exec.TextureSlots["_rt_Shadows"] = 11;
        var source = new Source();
        var info = new ShadowEngineInfo { FalloffOffset = 3, MaxDist = 70, FalloffAmount = 240, TexOriginX = 0.25f, TexOriginY = 0.5f, TexSizeX = 0.125f, TexSizeY = 0.0625f };
        for (int i = 0; i < 16; i++)
            info.WorldToShadow[i] = i + 1;
        source.Infos[6] = info;
        var host = ShadowHost(exec, source);
        Assert.True(host.Init(4, "ShadowModel", "decals/rendermodelshadow", []));

        // The bind argument is the client shadow handle (stock's (intp)pProxyData & 0xffff).
        List<ProxyOp> ops = MaterialProxyHostTests.Bind(host, 4, 0, 7, default, arg: 0x10006);
        Assert.Equal(1, source.Asked);
        Assert.Equal([(int)ProxyOpKind.Texture, (int)ProxyOpKind.Matrix, (int)ProxyOpKind.VecN, (int)ProxyOpKind.VecN,
                      (int)ProxyOpKind.Float, (int)ProxyOpKind.Float, (int)ProxyOpKind.Float], ops.Select(o => o.Kind));
        Assert.Equal(11, ops[0].Arg);
        Assert.Equal(1f, ops[1].Value(0));
        Assert.Equal(16f, ops[1].Value(15));
        Assert.Equal(0.25f, ops[2].Value(0));
        Assert.Equal(0.0625f, ops[3].Value(1));
        Assert.Equal(3f, ops[4].Value(0));
        Assert.Equal(70f, ops[5].Value(0));
        Assert.Equal(240f, ops[6].Value(0));

        // A handle that names no shadow sets only the texture.
        List<ProxyOp> none = MaterialProxyHostTests.Bind(host, 4, 0, 7, default, arg: 9);
        Assert.Equal([(int)ProxyOpKind.Texture], none.Select(o => o.Kind));
    }
}
