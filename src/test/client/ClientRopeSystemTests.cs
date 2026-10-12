using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ropes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed rope system around the simulation RopeStockReplayTests pins:
/// configuring a span from its link, stepping it once a frame, building and
/// handing its strip to the executor, the ConVars that stop it, carrying it
/// across a reload, and the "ShakeRopes" effect.
/// </summary>
public class ClientRopeSystemTests
{
    private const int OwnerIndex = 78;
    private static readonly EntityHandle Owner = new(0x0001004e);
    private static readonly Vector Start = new(-4037f, 2929f, 254.559f);
    private static readonly Vector End = new(-3601f, 2816f, 254.559f);

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Backend : IConVarBackend
    {
        public readonly Dictionary<string, int> Ids = new(StringComparer.OrdinalIgnoreCase);

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            int id = Ids.Count;
            Ids[name] = id;
            return id;
        }

        public void Set(int id, string value) { }
        public string Get(int id) => "";
        public string? ValueOf(string name) => null;
    }

    private sealed class Executor : IClientRopeExecutor
    {
        public readonly List<(int Material, float[] Segments, float[] Solid, int Flags)> Draws = [];
        public ClientViewState ViewState = new(new Vector(0, 1, 0), new Vector(-3800, 2500, 65),
                                               new Vector(-3800, 2500, 65), 1280, 95, false,
                                               new Vector(-3800, 2500, 65));
        public bool HasBack = true;

        public int Material(string name, out int textureHeight, out bool hasBack)
        {
            textureHeight = 1;
            hasBack = HasBack;
            return name.Length == 0 ? -1 : 3;
        }

        public void Draw(int material, ReadOnlySpan<float> segments, ReadOnlySpan<float> solid, int flags)
            => Draws.Add((material, segments.ToArray(), solid.ToArray(), flags));

        public ClientViewState View(bool insideView) => ViewState;

        public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors)
        {
            boxColors.Clear();
            return new Vector(0.5f, 0.5f, 0.5f);
        }

        public ClientHullTrace TraceHullWorld(in Vector start, in Vector end, in Vector mins, in Vector maxs, int mask)
            => new(1f, false, false, default, 0f);
    }

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = new();
        public readonly Executor Exec = new();
        public readonly Clock Clock = new();
        public readonly Backend Backend = new();
        public readonly ConVarRegistry ConVars;
        public readonly ClientRopeSystem Ropes;
        private float _random = 0.25f;

        public Rig()
        {
            Engine.Strict = false;
            Engine.Returns("Effects.ClientEntityOrigin", true)
                  .Returns("Effects.ClientEntityOrigin.Origin", Start)
                  .Returns("Effects.ClientWindSpeed", Vector.Zero)
                  .Returns("Console.FindConVarValue", "0")
                  .ReturnsEachCall("Host.RandomFloat", () => _random = (_random + 0.37f) % 1f);

            ConVars = new ConVarRegistry(Backend);
            ConVars.RegisterTypes(typeof(RopeConVars).Assembly.GetTypes()
                .Where(t => t.Namespace == typeof(RopeConVars).Namespace && t.IsSubclassOf(typeof(ConVarBase))));

            var vars = new RopeConVars(
                Get<RopeShakeConVar>(), Get<RopeSubdivConVar>(), Get<RopeCollideConVar>(), Get<RopeSmoothConVar>(),
                Get<RopeSmoothEnlargeConVar>(), Get<RopeSmoothMinWidthConVar>(), Get<RopeSmoothMinAlphaConVar>(),
                Get<RopeSmoothMaxAlphaWidthConVar>(), Get<RopeSmoothMaxAlphaConVar>(), Get<DrawRopesConVar>(),
                Get<RopeTranslucentConVar>(), Get<RopeWindDistConVar>(), Get<RopeAverageLightConVar>(),
                Get<RopeRenderSolidConVar>(), Get<RopeSolidMinWidthConVar>(), Get<RopeSolidMaxWidthConVar>(),
                Get<RopeSolidMinAlphaConVar>(), Get<RopeSolidMaxAlphaConVar>());

            Ropes = new ClientRopeSystem(Exec, Engine, Engine, Engine, Engine, Clock, vars, Engine);
        }

        private T Get<T>() where T : ConVarBase => ConVars.Get<T>()!;

        public void Set(string name, string value) => ConVars.NotifyChanged(Backend.Ids[name], value, "");

        public void Configure() => Ropes.Configure(Owner, OwnerIndex, End, 200f, 0.5f, 1f, 5, 2, 0f, "cable/cable");

        public void Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Clock.Now += 1f / 60f;
                Ropes.Frame(1f / 60f);
            }
        }
    }

    [Fact]
    public void TheConVarsRegisterAtStocksDefaults()
    {
        var rig = new Rig();

        Assert.Equal(1, rig.ConVars.Get<DrawRopesConVar>()!.Value);
        Assert.Equal(2, rig.ConVars.Get<RopeSubdivConVar>()!.Value);
        Assert.Equal(1000f, rig.ConVars.Get<RopeWindDistConVar>()!.Value);
        Assert.Equal(1.4f, rig.ConVars.Get<RopeSmoothEnlargeConVar>()!.Value);
    }

    [Fact]
    public void AConfiguredSpanIsSteppedAndItsBoundsReachTheOwner()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(3);

        RopeSpanReport span = Assert.Single(rig.Ropes.Report());
        Assert.True(span.Initted, "the span never initialised");
        Assert.Equal(3, span.Thinks);
        Assert.True(span.Sag > 50f, $"a 450-unit span with 200 slack hangs; sag {span.Sag}");
        Assert.True(span.StartError < 0.01f && span.EndError < 0.01f, "the ends are not locked where they were put");
        Assert.True(rig.Engine.CountOf("EntityRender.SetCustomDraw") >= 1, "the owner was never given the rope's bounds");
    }

    [Fact]
    public void ADrawHandsTheBuiltStripAndItsSolidPassToTheExecutor()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(2);

        // Close enough that the back widths stay above rope_solid_minwidth:
        // 0.5 - z * 1.4 / 1280 > 0.3 for z under 183.
        rig.Exec.ViewState = rig.Exec.ViewState with { Origin = new Vector(-3800, 2750, 250) };
        Assert.True(rig.Ropes.Draw(Owner));

        var draw = Assert.Single(rig.Exec.Draws);
        Assert.Equal(3, draw.Material);
        // Five nodes and two subdivisions between each: 5 + 4 * 2 = 13 segments,
        // two vertices each, seventeen floats a vertex.
        Assert.Equal(13 * 2 * 17, draw.Segments.Length);
        // FakeAA: a back material, rope_smooth on, DX 95 and no hardware AA.
        Assert.Equal(0x1 | 0x2, draw.Flags);
        Assert.True(draw.Solid.Length == 13 * 2 * 17, "the FakeAA solid pass was not handed over");
    }

    [Fact]
    public void AFarRopeSkipsTheSolidPassAsStockDoes()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(2);

        // From 400 units every back width is under rope_solid_minwidth and
        // rope_solid_minalpha is 0: RenderSolidRopes' `continue`.
        Assert.True(rig.Ropes.Draw(Owner));

        var draw = Assert.Single(rig.Exec.Draws);
        Assert.Equal(0x1 | 0x2, draw.Flags);
        Assert.Empty(draw.Solid);
    }

    [Fact]
    public void WithoutABackMaterialTheStripIsDrawnSolidOnly()
    {
        var rig = new Rig();
        rig.Exec.HasBack = false;
        rig.Configure();
        rig.Frames(2);

        Assert.True(rig.Ropes.Draw(Owner));

        var draw = Assert.Single(rig.Exec.Draws);
        Assert.Equal(0x2, draw.Flags);
        Assert.Empty(draw.Solid);
        // The no-smoothing arm: every alpha 0.3 (a vertex's seventh float).
        for (int i = 0; i < 26; i++)
            Assert.Equal(0.3f, draw.Segments[i * 17 + 6]);
    }

    [Fact]
    public void TheStripFacesTheRenderContextsCamera()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(2);

        // A camera well away from the view origin: the strip must face it.
        Vector camera = new(-3000, 1800, 900);
        rig.Exec.ViewState = rig.Exec.ViewState with { Camera = camera };
        Assert.True(rig.Ropes.Draw(Owner));

        float[] v = Assert.Single(rig.Exec.Draws).Segments;
        Assert.Equal(26 * 17, v.Length);
        int checkedPairs = 0;
        for (int k = 0; k < 13; k++)
        {
            int a = k * 2 * 17, b = a + 17;
            // The pair straddles the segment along tangent S, which is
            // perpendicular to the sight line from the camera to the segment.
            float mx = (v[a] + v[b]) * 0.5f - camera.X, my = (v[a + 1] + v[b + 1]) * 0.5f - camera.Y,
                  mz = (v[a + 2] + v[b + 2]) * 0.5f - camera.Z;
            float len = MathF.Sqrt(mx * mx + my * my + mz * mz);
            float dot = (mx * v[a + 11] + my * v[a + 12] + mz * v[a + 13]) / len;
            Assert.True(MathF.Abs(dot) < 1e-3f, $"segment {k}: tangent S is {dot} along the sight line");
            Assert.True(MathF.Abs(v[a + 11]) + MathF.Abs(v[a + 12]) + MathF.Abs(v[a + 13]) > 0.5f, $"segment {k}: no normal");
            checkedPairs++;
        }
        Assert.Equal(13, checkedPairs);
    }

    [Fact]
    public void DrawRopesOffQueuesNothing()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(2);

        rig.Set("r_drawropes", "0");
        rig.Ropes.Draw(Owner);
        Assert.Empty(rig.Exec.Draws);

        rig.Set("r_drawropes", "1");
        Assert.True(rig.Ropes.Draw(Owner));
        Assert.Single(rig.Exec.Draws);
    }

    [Fact]
    public void ASpanWaitsForItsOwnerBeforeItHangs()
    {
        var rig = new Rig();
        rig.Engine.Returns("Effects.ClientEntityOrigin", false);
        rig.Configure();
        rig.Frames(3);

        Assert.False(Assert.Single(rig.Ropes.Report()).Initted, "a span hung before its owner was known");
        Assert.False(rig.Ropes.Draw(Owner));

        rig.Engine.Returns("Effects.ClientEntityOrigin", true);
        rig.Frames(1);

        RopeSpanReport span = Assert.Single(rig.Ropes.Report());
        Assert.True(span.Initted);
        Assert.Equal(450.41f, span.Chord, 0.01f);
    }

    [Fact]
    public void ARemovedLinkTakesItsSpan()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(1);

        rig.Ropes.Remove(Owner);

        Assert.Empty(rig.Ropes.Report());
        Assert.False(rig.Ropes.Draw(Owner));
    }

    [Fact]
    public void AReloadCarriesEverySpanBitForBit()
    {
        var old = new Rig();
        old.Configure();
        old.Frames(30);

        var writer = new ReloadWriter();
        old.Ropes.Save(writer);

        var restored = new Rig();
        restored.Clock.Now = old.Clock.Now;
        restored.Ropes.Restore(new ReloadReader(writer.ToArray()));

        // The positive control: a fresh span configured the same way is NOT
        // where the old one is, so equality below is the carry and not a
        // coincidence of two identical runs.
        var fresh = new Rig();
        fresh.Clock.Now = old.Clock.Now;
        fresh.Configure();

        old.Frames(1);
        restored.Frames(1);
        fresh.Frames(1);

        RopeSpanReport a = Assert.Single(old.Ropes.Report());
        RopeSpanReport b = Assert.Single(restored.Ropes.Report());
        RopeSpanReport c = Assert.Single(fresh.Ropes.Report());

        Assert.Equal(a.Sag, b.Sag);
        Assert.Equal(a.Thinks, b.Thinks);
        Assert.Equal(old.Ropes.Steps, restored.Ropes.Steps);
        Assert.True(a.Sag != c.Sag, $"a fresh span sags exactly like a carried one ({c.Sag}); the comparison proves nothing");

        Assert.True(old.Ropes.Draw(Owner) && restored.Ropes.Draw(Owner));
        Assert.Equal(old.Exec.Draws[0].Segments, restored.Exec.Draws[0].Segments);
    }

    [Fact]
    public void ShakeRopesIsAManagedEffectAndPushesEveryNearbyRope()
    {
        var rig = new Rig();
        rig.Configure();
        rig.Frames(60);
        float settled = Assert.Single(rig.Ropes.Report()).Sag;

        var handlers = new ClientEffectHandlers([new ShakeRopesEffect(rig.Ropes)]);
        var data = new EffectData { Origin = (Start + End) * 0.5f, Radius = 1000f, Magnitude = 500f };

        Assert.True(handlers.TryDispatch("shakeropes", data), "the effect name is matched as stock matches it");
        Assert.False(handlers.TryDispatch("Impact", data), "a name managed code does not answer goes native");

        rig.Frames(2);
        float shaken = Assert.Single(rig.Ropes.Report()).Sag;

        Assert.True(shaken < settled - 0.01f, $"the upward impulse did not lift the rope ({settled} -> {shaken})");
        Assert.Equal(1, handlers.Counts["ShakeRopes"]);
    }
}
