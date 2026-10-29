using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Render;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using Xunit;

using Charger = SourceSharp.Client.WeaponEffects.CrossbowDrawEffect.Charger;

namespace SourceSharp.Tests;

/// <summary>
/// CWeaponCrossbow's charger and load effect (weapon_crossbow.cpp:
/// SetChargerState, DoLoadEffect; c_weapon_crossbow.cpp: CrossbowLoad), held
/// to stock's numbers: the glow at the viewmodel's bolt tip, orange, additive,
/// no dissipation, 0.1 large and dark when made, 32 at 0.025 over half a
/// second while charging, 80 at 0.1 when ready, out on a shot; a shot and a
/// reload's first event spark at the "spark" attachment and flare there.
/// </summary>
public class CrossbowDrawEffectTests
{
    private const int StartLoad = CrossbowDrawEffect.StartLoadEvent, StartCharge = CrossbowDrawEffect.StartChargeEvent,
                      Ready = CrossbowDrawEffect.ReadyEvent;

    private static readonly Vector Tip = new(30, -2, -4), Spark = new(25, -1, -5), VmOrigin = new(1, 1, 1);

    private sealed class SpriteCalls : IClientSpriteDraw
    {
        public readonly List<(uint Owner, Vector Offset, int Model, int Mode, int Fx, int R, int G, int B, int A, float Scale)> Drawn = [];

        public int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a,
                              float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize)
            => throw new InvalidOperationException();

        public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx,
                                    int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                                    float glowProxySize)
        {
            Drawn.Add((owner.Value, offset, modelIndex, renderMode, renderFx, r, g, b, a, scale));
            return 1;
        }

        public int PrepareSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b,
                                 int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize)
            => throw new InvalidOperationException();
    }

    private sealed class SparkCalls : IClientSparks
    {
        public readonly List<Vector> Electric = [];
        public void ElectricSpark(Vector position, int magnitude, int trailLength, Vector? direction) => Electric.Add(position);
        public void MetalSpark(Vector position, Vector direction, Vector surfaceNormal, int scale = 1) => throw new InvalidOperationException();
        public void EnergySplash(Vector position, Vector normal, bool explosive) => throw new InvalidOperationException();
        public void GaussExplosion(Vector position, Vector direction, int type) => throw new InvalidOperationException();
    }

    /// <summary>The model table: every sprite name at index 42, each asked recorded.</summary>
    private sealed class ModelTable : SourceSharp.Client.Interop.IClientBeamExecutor
    {
        public readonly List<string> Asked = [];
        public int ModelIndex(string name)
        {
            Asked.Add(name);
            return 42;
        }

        public void View(bool insideView, Span<float> out42) => throw new InvalidOperationException();
        public bool ModelInfo(int modelIndex, out int frameCount) => throw new InvalidOperationException();
        public EntityHandle EntityAt(int entry) => throw new InvalidOperationException();
        public bool EntityHasModel(EntityHandle entity) => throw new InvalidOperationException();
        public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9) => throw new InvalidOperationException();
        public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled) => throw new InvalidOperationException();
        public Vector ScreenPosition(in Vector point) => throw new InvalidOperationException();
        public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs) => throw new InvalidOperationException();
        public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs) => throw new InvalidOperationException();
        public void RenderableRemove(int renderable) => throw new InvalidOperationException();
        public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe) => throw new InvalidOperationException();
        public void SetHdrColorScale(int modelIndex, int renderMode, float scale) => throw new InvalidOperationException();
        public bool Glow(int modelIndex, bool bind) => throw new InvalidOperationException();
        public void Strip(ReadOnlySpan<float> vertices) => throw new InvalidOperationException();
        public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices) => throw new InvalidOperationException();
    }

    private static readonly Vector Lifted = new(0, 0, 100);

    private sealed class Lift : IViewModelAttachmentFormat
    {
        public Vector ForWorldPass(EntityHandle entity, Vector attachment) => attachment + Lifted;
    }

    /// <summary>The world's translucent pass: each renderable added, where it was last put, and its draw.</summary>
    private sealed class Translucent : IClientTranslucentRenderables
    {
        public readonly Dictionary<int, (Vector Origin, Action Draw)> Live = [];
        public readonly List<int> Removed = [];
        private int _next = 5;

        public int Add(Vector origin, Vector mins, Vector maxs, Action draw)
        {
            Live[_next] = (origin, draw);
            return _next++;
        }

        public void Changed(int handle, Vector origin, Vector mins, Vector maxs)
            => Live[handle] = (origin, Live[handle].Draw);

        public void Remove(int handle)
        {
            Live.Remove(handle);
            Removed.Add(handle);
        }

        public void DrawPass()
        {
            foreach (var (_, draw) in Live.Values)
                draw();
        }
    }

    private sealed record Rig(CrossbowDrawEffect Effect, WeaponEffectRig W, SpriteCalls Sprites, SparkCalls Sparks, ModelTable Models,
                              Translucent Pass)
    {
        /// <summary>A frame at <paramref name="now"/>: the bow thinks on what it holds, then the world's pass draws.</summary>
        public void DrawAt(float now)
        {
            W.Now = now;
            Effect.Think(W.FirstPerson());
            Pass.DrawPass();
        }

        public void Think(int animEvent, int clip, bool inReload = false, float? now = null)
        {
            if (now is { } t)
                W.Now = t;
            W.Weapon.State.AnimEvent = animEvent;
            W.Weapon.State.Clip = clip;
            W.Weapon.State.InReload = inReload;
            Effect.Think(W.FirstPerson());
        }

        public Charger State => Effect.BowOf(W.Weapon.Handle)!.State;
    }

    private static Rig Make()
    {
        var w = new WeaponEffectRig(EntityNames.WeaponCrossbow);
        w.Models.At(w.ViewModel.Handle, CrossbowDrawEffect.BoltTipAttachment, Tip);
        w.Models.At(w.ViewModel.Handle, CrossbowDrawEffect.SparkAttachment, Spark);
        w.Engine.Returns("Entities.GetOrigin", VmOrigin);
        w.Now = 10f;

        var sprites = new SpriteCalls();
        var sparks = new SparkCalls();
        var models = new ModelTable();
        var pass = new Translucent();
        var effect = new CrossbowDrawEffect(sprites, w.Models, w.Engine, models, sparks, new Lift(), pass);
        return new Rig(effect, w, sprites, sparks, models, pass);
    }

    [Theory]
    [InlineData(Ready, 1, false, Charger.Off, Charger.StartCharge)]
    [InlineData(Ready, 0, true, Charger.StartLoad, Charger.StartCharge)]
    [InlineData(Ready, 0, false, Charger.Off, Charger.Off)]
    [InlineData(StartCharge, 0, true, Charger.Discharge, Charger.StartLoad)]
    [InlineData(StartCharge, 1, false, Charger.Off, Charger.Off)]
    [InlineData(StartLoad, 0, true, Charger.Discharge, Charger.Discharge)]
    [InlineData(0, 1, false, Charger.StartCharge, Charger.Ready)]
    [InlineData(0, 0, false, Charger.Ready, Charger.Discharge)]
    [InlineData(0, 0, false, Charger.Off, Charger.Off)]
    public void TheChargerIsTheLastEventTheWeaponHandled(int pending, int clip, bool inReload, Charger was, Charger expected)
    {
        var state = new WeaponNetState();
        state.BindLocal();
        state.AnimEvent = pending;
        state.Clip = clip;
        state.InReload = inReload;

        Assert.Equal(expected, CrossbowDrawEffect.StateOf(state, was));
    }

    [Fact]
    public void ADarkChargerDrawsNothing()
    {
        var r = Make();
        r.Think(StartCharge, 1);

        r.DrawAt(10f);

        Assert.Empty(r.Sprites.Drawn);
    }

    [Fact]
    public void ChargingBrightensTheGlowTo32AtAQuarterTheSizeOverHalfASecond()
    {
        var r = Make();
        r.Think(StartCharge, 1);
        r.Think(Ready, 1, now: 11f);

        r.DrawAt(11.25f);

        // At the bolt's tip where the world's pass sees it.
        var glow = Assert.Single(r.Sprites.Drawn);
        Assert.Equal((r.W.ViewModel.Handle.Value, Tip + Lifted - VmOrigin, 42, 5, 14, 255, 128, 0, 16),
                     (glow.Owner, glow.Offset, glow.Model, glow.Mode, glow.Fx, glow.R, glow.G, glow.B, glow.A));
        // Halfway from 0.1 to 0.025.
        Assert.Equal(0.0625f, glow.Scale, 4);
    }

    [Fact]
    public void ReadyIsTheGlowAt80AndATenthLarge()
    {
        var r = Make();
        r.Think(Ready, 1);
        r.Think(0, 1, now: 12f);

        r.DrawAt(20f);

        var glow = Assert.Single(r.Sprites.Drawn);
        Assert.Equal(80, glow.A);
        Assert.Equal(0.1f, glow.Scale, 4);
        Assert.Equal(CrossbowDrawEffect.GlowSprite, r.Models.Asked[^1]);
    }

    [Fact]
    public void AShotPutsTheChargerOutAndFlares()
    {
        var r = Make();
        r.Think(0, 1);
        Assert.Equal(Charger.Ready, r.State);

        r.Think(0, 0, now: 30f);

        Assert.Equal(Charger.Discharge, r.State);
        Assert.Equal([Spark + new Vector(0, 0, 100)], r.Sparks.Electric);

        r.DrawAt(30.005f);
        var flare = Assert.Single(r.Sprites.Drawn);
        Assert.Equal((Spark + Lifted - VmOrigin, 255, 255, 255, 128), (flare.Offset, flare.R, flare.G, flare.B, flare.A));
        Assert.Equal(0.2f, flare.Scale, 4);
    }

    [Fact]
    public void AReloadsFirstEventSparks()
    {
        var r = Make();
        r.Think(StartLoad, 0, inReload: true);
        Assert.Empty(r.Sparks.Electric);

        r.Think(StartCharge, 0, inReload: true);

        Assert.Equal(Charger.StartLoad, r.State);
        Assert.Single(r.Sparks.Electric);
    }

    [Theory]
    [InlineData(0f, 128)]
    [InlineData(0.01f, 128)]
    [InlineData(0.135f, 64)]
    [InlineData(0.26f, 0)]
    [InlineData(0.5f, 0)]
    public void TheFlareFadesOverAQuarterSecondFromAHundredthOn(float since, int brightness)
        => Assert.Equal(brightness, CrossbowDrawEffect.BlastBrightness(since));

    [Fact]
    public void SomebodyElsesBowDrawsNothing()
    {
        var r = Make();
        r.W.Weapon.State.AnimEvent = 0;
        r.W.Weapon.State.Clip = 1;

        r.Effect.Think(r.W.ThirdPerson());

        Assert.Null(r.Effect.BowOf(r.W.Weapon.Handle));
    }

    /// <summary>Stock's crossbow draws nothing after its viewmodel: the viewmodel drew over its sprites.</summary>
    [Fact]
    public void NothingIsDrawnAfterTheViewmodel()
    {
        var r = Make();
        r.Think(0, 1);
        r.Think(0, 1, now: 20f);

        r.Effect.ViewModelDrawn(r.W.FirstPerson());

        Assert.Empty(r.Sprites.Drawn);
        Assert.Single(r.Pass.Live);
    }

    [Fact]
    public void TheBowsRenderableFollowsTheBoltTipAsTheWorldsPassSeesIt()
    {
        var r = Make();
        r.Think(0, 1);
        Assert.Equal(Tip + Lifted, Assert.Single(r.Pass.Live).Value.Origin);

        r.W.Models.At(r.W.ViewModel.Handle, CrossbowDrawEffect.BoltTipAttachment, Tip + new Vector(5, 0, 0));
        r.Think(0, 1, now: 11f);

        Assert.Equal(Tip + new Vector(5, 0, 0) + Lifted, Assert.Single(r.Pass.Live).Value.Origin);
    }

    [Fact]
    public void ABowSeenInTheThirdPersonDrawsNothingInTheWorldsPass()
    {
        var r = Make();
        r.Think(0, 1);
        r.Think(0, 1, now: 20f);

        r.Effect.Think(r.W.ThirdPerson());
        r.Pass.DrawPass();

        Assert.Empty(r.Sprites.Drawn);
    }

    [Fact]
    public void ABowLetGoRemovesItsRenderable()
    {
        var r = Make();
        r.Think(0, 1);
        int handle = Assert.Single(r.Pass.Live).Key;

        r.Effect.Forget(r.W.Weapon.Handle);

        Assert.Empty(r.Pass.Live);
        Assert.Equal([handle], r.Pass.Removed);
    }
}
