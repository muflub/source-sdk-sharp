using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Flashlight;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The local player's flashlight driver (<see cref="LocalPlayerFlashlight"/>,
/// stock's C_BasePlayer::UpdateFlashlight), the viewer it reads, and the
/// ConVars it is configured by (its own, with r_newflashlight's callback, and
/// the four it watches by name). The effect itself is held to stock by
/// <see cref="FlashlightOracleTests"/>.
/// </summary>
public class FlashlightTests
{
    private sealed class World : IFlashlightWorld
    {
        public readonly List<string> Calls = [];
        public readonly List<(Vector Start, Vector End, TraceMask Mask)> Traces = [];
        public int NextHandle = 7;

        public int? DXSupportLevel() => 95;
        public bool SupportsBorderColor() => true;
        public int Texture(string name) { Calls.Add("texture " + name); return 0; }
        public int Create(in FlashlightState state) { Calls.Add("create"); return NextHandle++; }
        public void Update(int handle, in FlashlightState state) => Calls.Add("update " + handle);
        public void Project(int handle) => Calls.Add("project " + handle);
        public void Destroy(int handle) => Calls.Add("destroy " + handle);
        public FlashlightTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask)
        {
            Traces.Add((start, end, mask));
            return new FlashlightTrace(false, 1f, end);
        }
        public FlashlightTrace TraceLine(Vector start, Vector end, TraceMask mask)
        {
            Traces.Add((start, end, mask));
            return new FlashlightTrace(false, 1f, end);
        }
        public void BoxOverlay(Vector origin, Vector mins, Vector maxs, QAngle orientation, int r, int g, int b, int a, float duration) { }
        public void LineOverlay(Vector origin, Vector dest, int r, int g, int b, bool noDepthTest, float duration) { }
        public void DynamicLightUpdate(in DynamicLight light) => Calls.Add("dlight " + light.Key);
        public void DynamicLightRelease(int key) => Calls.Add("dlight-off " + key);

        public int Count(string call) => Calls.Count(c => c == call);
    }

    /// <summary>Stock's defaults (flashlighteffect.cpp, clientshadowmgr.cpp, the material system).</summary>
    private sealed class StockSettings : IFlashlightSettings
    {
        public bool NewFlashlight { get; set; } = true;
        public void DisableNewFlashlight() => NewFlashlight = false;
        public bool SwingFlashlight => true;
        public bool LockPosition => false;
        public float Fov => 45f;
        public float OffsetY => -20f;
        public float Near => 4f;
        public float Far => 750f;
        public float ConstantAtten => 0f;
        public float LinearAtten => 100f;
        public float QuadraticAtten => 0f;
        public bool VisualizeTrace => false;
        public float Ambient => 0f;
        public float ShadowAtten => 0.35f;
        public float LadderDist => 40f;
        public float SlopeScaleDepthBias => 16f;
        public float DepthBias => 0.0005f;
        public bool DepthTexture => true;
        public int DepthResolution => 1024;
    }

    private sealed class Viewers : IFlashlightViewerSource
    {
        public FlashlightViewer? Next;
        public FlashlightViewer? Viewer() => Next;
    }

    private static readonly Vector Eye = new(10f, 20f, 64f);

    private static (LocalPlayerFlashlight Light, World World, Viewers Viewers) Build()
    {
        var world = new World();
        var viewers = new Viewers();
        return (new LocalPlayerFlashlight(viewers, world, new StockSettings()), world, viewers);
    }

    private static FlashlightViewer Lit(bool dim = true, bool ladder = false, QAngle? angles = null)
        => new(3, dim, Eye, angles ?? new QAngle(0f, 90f, 0f), ladder);

    [Fact]
    public void NoLocalPlayerLightsNothing()
    {
        var (light, world, _) = Build();
        light.Pass();
        Assert.Null(light.Effect);
        Assert.Empty(world.Calls);
    }

    [Fact]
    public void ALocalPlayerWithoutTheDimLightBitLightsNothing()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit(dim: false);
        light.Pass();
        Assert.Null(light.Effect);
        Assert.Empty(world.Calls);
    }

    [Fact]
    public void TheDimLightBitMakesAnEffectTurnsItOnAndLightsIt()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit();

        light.Pass();

        Assert.NotNull(light.Effect);
        Assert.True(light.Effect!.IsOn);
        Assert.Equal(3, light.Effect.EntIndex);
        Assert.Equal(1, light.Made);
        Assert.Equal(["texture " + FlashlightEffect.BorderTexture, "create", "project 7"], world.Calls);
    }

    [Fact]
    public void LaterFramesUpdateTheSameEffect()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit();

        light.Pass();
        light.Pass();
        light.Pass();

        Assert.Equal(1, light.Made);
        Assert.Equal(1, world.Count("create"));
        Assert.Equal(2, world.Count("update 7"));
        Assert.Equal(3, world.Count("project 7"));
        Assert.Equal(3, light.LitFrames);
    }

    [Fact]
    public void TheBitGoingDeletesTheEffectAndItsShadow()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit();
        light.Pass();

        viewers.Next = Lit(dim: false);
        light.Pass();

        Assert.Null(light.Effect);
        Assert.Equal(1, world.Count("destroy 7"));

        light.Pass();
        Assert.Equal(1, world.Count("destroy 7"));
    }

    [Fact]
    public void TheLocalPlayerGoingDeletesTheEffectToo()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit();
        light.Pass();

        viewers.Next = null;
        light.Pass();

        Assert.Null(light.Effect);
        Assert.Equal(1, world.Count("destroy 7"));
    }

    [Fact]
    public void LevelShutdownForgetsTheShadowWithoutDestroyingIt()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit();
        light.Pass();

        light.LevelShutdown();

        Assert.Null(light.Effect);
        Assert.Equal(0, world.Count("destroy 7"));

        // The next level's frame makes a new effect and a new shadow.
        light.Pass();
        Assert.Equal(2, light.Made);
        Assert.Equal(2, world.Count("create"));
    }

    [Fact]
    public void TheLightTracesFromTheEyeAlongTheEyeAngles()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit(angles: new QAngle(0f, 90f, 0f));

        light.Pass();

        // The origin trace (the eye down by r_flashlightoffsety), then the
        // direction trace to r_flashlightfar along yaw 90's forward.
        Assert.Equal(2, world.Traces.Count);
        Assert.Equal(Eye, world.Traces[0].Start);
        Assert.Equal(TraceMask.Solid, world.Traces[0].Mask);
        MoveVec.AngleVectors(new Vector(0f, 90f, 0f), out Vector forward, out _, out _);
        Assert.Equal(Eye + forward * 750f, world.Traces[1].End);
        Assert.Equal(TraceMask.OpaqueAndNpcsWindow, world.Traces[1].Mask);
    }

    [Fact]
    public void OnALadderTheOriginTraceIsSkipped()
    {
        var (light, world, viewers) = Build();
        viewers.Next = Lit(ladder: true);

        light.Pass();

        Assert.Single(world.Traces);
        Assert.Equal(TraceMask.OpaqueAndNpcsWindow, world.Traces[0].Mask);
        Assert.Equal(Eye, world.Traces[0].Start);
    }

    // ---- the module's wiring ------------------------------------------------

    private static string Module() => File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"));

    /// <summary>
    /// THE CALL IS THE FLASHLIGHT. Without the pass the game runs perfectly
    /// and no flashlight is ever lit, so the stage's block is read for it:
    /// FRAME_RENDER_START, where stock's C_BasePlayer::Simulate ran it.
    /// </summary>
    [Fact]
    public void ThePassIsWiredIntoTheRenderStartStage()
    {
        string module = Module();
        int stage = module.IndexOf("case ClientFrameStage.RenderStart:", StringComparison.Ordinal);
        Assert.True(stage >= 0, "ClientModule has no RenderStart stage");
        int end = module.IndexOf("break;", stage, StringComparison.Ordinal);
        int pass = module.IndexOf("_flashlight?.Pass();", stage, StringComparison.Ordinal);
        Assert.True(pass > stage && pass < end, "the RenderStart stage does not run the flashlight's pass");
    }

    [Fact]
    public void TheModuleRoutesTheWatchesChangesAndTheLevelsEnd()
    {
        string module = Module();
        Assert.Contains("if (_flashlightSettings is { } flashlight && flashlight.OnChanged(id, newValue))", module);
        int shutdown = module.IndexOf("public void LevelShutdown()", StringComparison.Ordinal);
        Assert.True(shutdown >= 0 && module.IndexOf("_flashlight?.LevelShutdown();", shutdown, StringComparison.Ordinal) > shutdown,
                    "LevelShutdown does not forget the flashlight");
    }

    // ---- the viewer --------------------------------------------------------

    private const int Local = 2;

    private sealed class Directory : IClientStandalones
    {
        public readonly ClientEntityDirectory Entities = new();
        public IReadOnlyList<ClientStandaloneEntity> Live() => [];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Entities.ByIndex(entityIndex);
        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Entities.OfClass(className);
        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Entities.OfName(name);
    }

    private sealed class Blocks(byte[] block) : IStandaloneBlocks
    {
        public Span<byte> Block(EntityHandle entity) => block;
    }

    private static (StandaloneFlashlightViewers Source, ClientSsPlayer Me, byte[] Block, ViewReadout View) Viewer()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        var me = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0000u | Local) };
        me.State.BindLocal();
        me.State.Origin = new Vector(1f, 2f, 3f);
        me.State.ViewOffset = new Vector(0f, 0f, 64f);
        var directory = new Directory();
        directory.Entities.Bind(me.Handle, Local, me);
        var block = new byte[EngineBlock.SizeBytes];
        var view = new ViewReadout();
        return (new StandaloneFlashlightViewers(directory, engine, new Blocks(block), view), me, block, view);
    }

    private static void SetEffects(byte[] block, int effects)
        => BitConverter.TryWriteBytes(block.AsSpan(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Effects))), effects);

    [Fact]
    public void TheViewerIsTheLocalPlayerWithItsDimLightBit()
    {
        var (source, _, block, _) = Viewer();

        Assert.False(source.Viewer()!.Value.DimLight);

        SetEffects(block, (int)(EntityEffect.DimLight | EntityEffect.NoShadow));
        FlashlightViewer viewer = source.Viewer()!.Value;
        Assert.True(viewer.DimLight);
        Assert.Equal(Local, viewer.Index);

        SetEffects(block, (int)EntityEffect.NoShadow);
        Assert.False(source.Viewer()!.Value.DimLight);
    }

    [Fact]
    public void TheEyeIsThePublishedOneOrTheOriginPlusTheViewOffset()
    {
        var (source, _, _, view) = Viewer();

        Assert.Equal(new Vector(1f, 2f, 67f), source.Viewer()!.Value.Eye);

        view.PublishEye(new Vector(5f, 6f, 70f));
        view.PublishAngles(12f, 34f);
        FlashlightViewer viewer = source.Viewer()!.Value;
        Assert.Equal(new Vector(5f, 6f, 70f), viewer.Eye);
        Assert.Equal(new QAngle(12f, 34f, 0f), viewer.EyeAngles);
    }

    [Fact]
    public void TheLadderIsTheNetworkedMovetype()
    {
        var (source, me, _, _) = Viewer();
        Assert.False(source.Viewer()!.Value.OnLadder);
        me.State.MoveType = 9;   // MOVETYPE_LADDER
        Assert.True(source.Viewer()!.Value.OnLadder);
        me.State.MoveType = 8;   // MOVETYPE_NOCLIP
        Assert.False(source.Viewer()!.Value.OnLadder);
    }

    [Fact]
    public void NoLocalPlayerIsNoViewer()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", Local);
        var source = new StandaloneFlashlightViewers(new Directory(), engine, new Blocks(new byte[EngineBlock.SizeBytes]),
                                                     new ViewReadout());
        Assert.Null(source.Viewer());
    }

    // ---- the watched ConVars ----------------------------------------------

    /// <summary>shell_convars_client.h's SOURCESHARP_CONVAR_WATCH_ID_BASE.</summary>
    private const int WatchIdBase = 0x10000;

    private sealed class WatchSlots : IClientConVarSlots
    {
        public readonly List<string> Watched = [];
        public int Watch(string name)
        {
            if (name == "no_such_convar")
                return -1;
            Watched.Add(name);
            return WatchIdBase + Watched.Count - 1;
        }
        public bool SetValue(string name, float value) => false;
    }

    [Fact]
    public void AWatchedValueIsReadOnceThenFollowsItsChanges()
    {
        var slots = new WatchSlots();
        var values = new WatchedConVarValues(slots, name => name == "r_flashlightfov" ? "45.0" : null);
        values.Watch(["r_flashlightfov", "no_such_convar"]);

        Assert.Equal(1, values.Watched);
        Assert.Equal(45f, values.Float("r_flashlightfov"));

        Assert.True(values.OnChanged(WatchIdBase, "60.5x"));
        Assert.Equal(60.5f, values.Float("r_flashlightfov"));   // atof's prefix
        Assert.Equal(60, values.Int("r_flashlightfov"));        // GetInt: the float truncated
        Assert.False(values.OnChanged(WatchIdBase + 1, "1"), "an id it never watched is not its");
    }

    [Fact]
    public void GetBoolIsGetIntNotZero()
    {
        var slots = new WatchSlots();
        var values = new WatchedConVarValues(slots, _ => "0.5");
        values.Watch(["r_newflashlight"]);
        Assert.False(values.Bool("r_newflashlight"));
        values.OnChanged(WatchIdBase, "1.0");
        Assert.True(values.Bool("r_newflashlight"));
    }

    // ---- the flashlight's own ConVars ---------------------------------------

    /// <summary>A console that keeps what it is set to, as the engine's ConVar does.</summary>
    private sealed class Backend : IConVarBackend
    {
        public readonly Dictionary<string, int> Ids = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, string> Values = [];
        public readonly List<(string Name, string Value)> Sets = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            int id = Ids.Count;
            Ids[name] = id;
            Values[id] = defaultValue;
            return id;
        }

        public void Set(int id, string value)
        {
            Values[id] = value;
            Sets.Add((Ids.First(p => p.Value == id).Key, value));
        }

        public string Get(int id) => Values[id];
        public string? ValueOf(string name) => null;
    }

    private static Type[] FlashlightConVarTypes()
        => typeof(FlashlightConVars).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(FlashlightConVars).Namespace && typeof(ConVarBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

    private sealed class ConVarRig
    {
        public readonly Backend Backend = new();
        public readonly ConVarRegistry Registry;
        public readonly FlashlightConVars Settings;

        public ConVarRig(int? dx = 95, Func<string, string?>? engineValue = null)
        {
            Registry = new ConVarRegistry(Backend);
            Registry.RegisterTypes(FlashlightConVarTypes());
            Settings = new FlashlightConVars(Registry, new WatchedConVarValues(new WatchSlots(), engineValue ?? (_ => null)), () => dx);
            Settings.Start();
        }

        public void Change(string name, string value)
        {
            int id = Backend.Ids[name];
            string old = Backend.Values[id];
            Backend.Values[id] = value;
            Assert.True(Registry.NotifyChanged(id, value, old));
        }
    }

    [Fact]
    public void TheFlashlightDeclaresSixteenConVarsAndWatchesTheFourItDoesNot()
    {
        var rig = new ConVarRig();
        Assert.Equal(16, rig.Backend.Ids.Count);
        Assert.Equal(4, FlashlightConVars.EngineNames().Distinct().Count());
        Assert.All(FlashlightConVars.EngineNames(), n => Assert.False(rig.Backend.Ids.ContainsKey(n), n + " is registered by the flashlight"));
    }

    [Fact]
    public void EachSettingReadsItsOwnConVar()
    {
        // A distinct value per name: a setting that read a neighbour's name
        // would answer that neighbour's number.
        string[] engineNames = FlashlightConVars.EngineNames();
        var engine = engineNames.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => (p.i + 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var rig = new ConVarRig(engineValue: n => engine[n]);
        var own = rig.Backend.Ids.Keys.OrderBy(n => n, StringComparer.Ordinal).Select((n, i) => (n, i))
            .ToDictionary(p => p.n, p => (p.i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (name, value) in own)
            rig.Change(name, value);
        var settings = rig.Settings;

        float Value(string name) => float.Parse(own.TryGetValue(name, out var v) ? v : engine[name], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Value("r_flashlightfov"), settings.Fov);
        Assert.Equal(Value("r_flashlightoffsety"), settings.OffsetY);
        Assert.Equal(Value("r_flashlightnear"), settings.Near);
        Assert.Equal(Value("r_flashlightfar"), settings.Far);
        Assert.Equal(Value("r_flashlightconstant"), settings.ConstantAtten);
        Assert.Equal(Value("r_flashlightlinear"), settings.LinearAtten);
        Assert.Equal(Value("r_flashlightquadratic"), settings.QuadraticAtten);
        Assert.Equal(Value("r_flashlightambient"), settings.Ambient);
        Assert.Equal(Value("r_flashlightshadowatten"), settings.ShadowAtten);
        Assert.Equal(Value("r_flashlightladderdist"), settings.LadderDist);
        Assert.Equal(Value("mat_slopescaledepthbias_shadowmap"), settings.SlopeScaleDepthBias);
        Assert.Equal(Value("mat_depthbias_shadowmap"), settings.DepthBias);
        Assert.Equal((int)Value("r_flashlightdepthres"), settings.DepthResolution);
        Assert.True(settings.DepthTexture, "r_flashlightdepthtexture reads a non-zero value");
    }

    [Fact]
    public void EachBoolSettingReadsItsOwnConVar()
    {
        // Turn each off alone: only its own setting may follow.
        (string Name, Func<FlashlightConVars, bool> Read)[] bools =
        [
            ("r_newflashlight", s => s.NewFlashlight), ("r_swingflashlight", s => s.SwingFlashlight),
            ("r_flashlightlockposition", s => s.LockPosition), ("r_flashlightvisualizetrace", s => s.VisualizeTrace),
        ];
        foreach (var (name, _) in bools)
        {
            var rig = new ConVarRig();
            foreach (var (other, _) in bools)
                rig.Change(other, "1");
            rig.Change(name, "0");
            foreach (var (other, read) in bools)
                Assert.True(read(rig.Settings) == (other != name), name + " off moved " + other);
        }
    }

    [Fact]
    public void BelowDx70TheCallbackPutsTheOldFlashlightBack()
    {
        // r_newflashlightCallback_f: any change on a card below DX7 sets it to 0.
        var rig = new ConVarRig(dx: 60);
        rig.Change("r_newflashlight", "0");
        rig.Change("r_newflashlight", "1");
        Assert.False(rig.Settings.NewFlashlight);
        Assert.Contains(("r_newflashlight", "0"), rig.Backend.Sets);
    }

    [Fact]
    public void AtDx70TheCallbackLeavesTheNewFlashlight()
    {
        var rig = new ConVarRig(dx: 70);
        rig.Change("r_newflashlight", "0");
        rig.Change("r_newflashlight", "1");
        Assert.True(rig.Settings.NewFlashlight);
        Assert.Empty(rig.Backend.Sets);
    }

    [Fact]
    public void DisablingTheNewFlashlightSetsItsConVarThroughTheEngine()
    {
        var rig = new ConVarRig();
        Assert.True(rig.Settings.NewFlashlight);
        rig.Settings.DisableNewFlashlight();
        Assert.Equal([("r_newflashlight", "0")], rig.Backend.Sets);
        Assert.False(rig.Settings.NewFlashlight);
    }
}
