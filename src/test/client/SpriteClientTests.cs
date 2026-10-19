extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client halves of env_sprite, env_sprite_oriented and env_glow.
///
/// <para>
/// A separate class from <see cref="SpriteTests"/> because the two sides fail
/// differently. The server's mistakes are arithmetic and show up in a payload
/// dump; the client's are about WHERE a call is made from and WHICH argument
/// went where, and a screenshot cannot tell you either — a sprite drawn with
/// two arguments transposed is still a sprite.
/// </para>
/// </summary>
public class SpriteClientTests
{
    /// <summary>
    /// What a sprite tells the shell about how it is drawn.
    ///
    /// <para>
    /// THIS IS THE TEST THAT WOULD HAVE CAUGHT THE PORT'S ONE REAL BUG. The
    /// blending was pushed from the SERVER half first, and
    /// StandaloneSetBlending is wired inside the gamesystem's CLIENT_DLL guard
    /// — so on the server it is a null function pointer, Engine.cs skips the
    /// call, and nothing at all is logged. Live, the additive sprites still
    /// drew and the two GLOW modes did not, because the entity stayed in the
    /// opaque list where DrawNoZBufferTranslucentRenderables never looks.
    /// </para>
    ///
    /// <para>
    /// Which is why this asserts the call is made BY THIS CLASS rather than
    /// that some field ends up holding the right value: what was wrong was
    /// where the call was made from.
    /// </para>
    /// </summary>
    [Fact]
    public void TheClientHalfDeclaresHowItIsDrawn()
    {
        var (glow, engine) = Client<ClientSprite>(EntityNames.EnvSprite);

        glow.State.ModelIndex = 64;
        glow.State.Scale = 0.5f;
        glow.State.Brightness = 255;
        glow.State.RenderMode = (int)RenderMode.WorldGlow;
        glow.State.Visible = true;

        glow.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var blending = engine.Calls.LastOrDefault(c => c.Member == "EntityRender.SetBlending");

        Assert.True(blending.Member == "EntityRender.SetBlending", "the client half says how the sprite is blended");
        Assert.True(blending.ArgAs<bool>(1), "every sprite is translucent");
        Assert.True(blending.ArgAs<bool>(2), "and a world glow ignores the depth buffer");
        Assert.True(blending.ArgAs<int>(3) == 255, "and the fx blend is renderamt");

        // The bounds go over too, and they must be a BOX rather than the
        // entity's model bounds: a sprite's model is a picture with no
        // geometry, so the leaf system would cull it by a point.
        var bounds = engine.Calls.LastOrDefault(c => c.Member == "EntityRender.SetCustomDraw");

        Assert.True(bounds.Member == "EntityRender.SetCustomDraw", "and what box it occupies while drawing itself");
        Assert.True(bounds.ArgAs<Vector>(2).X > 0f, "which is not empty");
    }

    /// <summary>An additive sprite is translucent too, and is NOT exempt from depth.</summary>
    [Fact]
    public void AnAdditiveSpriteIsDepthTested()
    {
        var (sprite, engine) = Client<ClientSpriteOriented>(EntityNames.EnvSpriteOriented);

        sprite.State.RenderMode = (int)RenderMode.TransAdd;
        sprite.State.Scale = 2.5f;
        sprite.State.Brightness = 128;

        sprite.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var blending = engine.Calls.LastOrDefault(c => c.Member == "EntityRender.SetBlending");

        Assert.True(blending.ArgAs<bool>(1), "an additive sprite is still translucent");
        Assert.True(!blending.ArgAs<bool>(2), "and is drawn depth-tested");
    }

    /// <summary>Nothing is drawn while the sprite is off, or before its material loaded.</summary>
    [Fact]
    public void AHiddenSpriteDrawsNothing()
    {
        var (sprite, engine) = Client<ClientSprite>(EntityNames.EnvSprite);

        sprite.State.ModelIndex = 64;
        sprite.State.Visible = false;

        Assert.True(sprite.OnDraw() == 0, "a hidden sprite draws nothing");
        Assert.True(engine.CountOf("Effects.DrawSprite") == 0, "and does not ask the engine to");

        sprite.State.Visible = true;
        sprite.State.ModelIndex = -1;

        Assert.True(sprite.OnDraw() == 0, "and neither does one whose material would not load");
        Assert.True(engine.CountOf("Effects.DrawSprite") == 0, "which is checked before the call, not after");
    }

    /// <summary>
    /// What a visible sprite hands the engine, in the order the native call
    /// reads it.
    ///
    /// A draw call with two arguments transposed still draws something, which is
    /// exactly the failure a screenshot cannot catch.
    /// </summary>
    [Fact]
    public void ADrawnSpriteHandsOverEveryValue()
    {
        var (sprite, engine) = Client<ClientGlow>(EntityNames.EnvGlow);

        engine.Returns("Effects.DrawSprite", 1);

        sprite.State.ModelIndex = 64;
        sprite.State.Visible = true;
        sprite.State.Frame = 3f;
        sprite.State.RenderMode = (int)RenderMode.WorldGlow;
        sprite.State.RenderFx = 0;
        sprite.State.Red = 10;
        sprite.State.Green = 20;
        sprite.State.Blue = 30;
        sprite.State.Brightness = 200;
        sprite.State.Scale = 1.5f;
        sprite.State.WorldSpaceScale = false;
        sprite.State.HdrColorScale = 0.7f;
        sprite.State.GlowProxySize = 4f;

        Assert.True(sprite.OnDraw() == 1, "a visible sprite reports what the engine drew");

        var draw = engine.Calls.LastOrDefault(c => c.Member == "Effects.DrawSprite");

        Assert.True(draw.ArgAs<int>(1) == 64, "the model index goes over");
        Assert.True(draw.ArgAs<float>(2) == 3f, "and which frame");
        Assert.True(draw.ArgAs<int>(3) == (int)RenderMode.WorldGlow, "and the render mode");
        Assert.True(draw.ArgAs<int>(5) == 10 && draw.ArgAs<int>(6) == 20 && draw.ArgAs<int>(7) == 30,
                    "and the three colour channels, in order");
        Assert.True(draw.ArgAs<int>(8) == 200, "and the brightness, as the quad's alpha");
        Assert.True(draw.ArgAs<float>(9) == 1.5f, "and the scale");
        Assert.True(!draw.ArgAs<bool>(10), "and whether that scale is world units");
        Assert.True(draw.ArgAs<float>(11) == 0.7f, "and the HDR colour scale");
        Assert.True(draw.ArgAs<float>(12) == 4f, "and the glow proxy size");

        // The PLACE is not among them, and that is the design rather than an
        // omission: native reads the interpolated origin off the entity, and
        // this side would answer a silent zero if it were asked.
        Assert.True(draw.Args.Length == 13, "and not where it is, which native already holds");
    }

    /// <summary>
    /// A sprite's client half, built through the registry the game uses rather
    /// than with <c>new</c>, because the thing under test is partly the
    /// REGISTRATION: a classname with no client class joins with a datatable
    /// error rather than a missing sprite.
    /// </summary>
    private static (T, RecordingEngine) Client<T>(string className) where T : ClientSpriteBase
    {
        var registry = new ClientStandaloneRegistry(ClientDoubles.UnattachedEngine());
        registry.RegisterAll(typeof(ClientSprite).Assembly);

        var services = Services(out var engine);

        // The sprite draw is planned on the client's side (ClientSpriteHost);
        // these facts are about what the entity hands it, so the double
        // records the hand-over where the engine double already reads it.
        services.AddInstance<client::SourceSharp.Client.Effects.IClientSpriteDraw>(new SpriteDrawToEngine(engine));
        registry.Services = services;

        // The renderable registration the base makes on the way in. These tests
        // are about what the entity says on an update, and the double refuses
        // anything nobody named.
        engine.Ignores("EntityRender.RegisterRenderable", "EntityRender.UnregisterRenderable");

        // The index the creation push caches (Phase X4). ClientStandaloneRegistry
        // asks the engine once per entity so that nothing has to ask again per
        // frame; these tests are about what the entity draws, not what it is
        // numbered.
        engine.Ignores("Entities.GetIndex");

        var handle = new EntityHandle(0x0044_0055u);

        Assert.True(registry.Create(handle, className), $"{className} has a client half at all");

        return ((T)registry.Get(handle)!, engine);
    }

    private sealed class SpriteDrawToEngine(IEngineEffects effects) : client::SourceSharp.Client.Effects.IClientSpriteDraw
    {
        public int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx,
                              int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                              float glowProxySize)
            => effects.DrawSprite(owner, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale,
                                  hdrColorScale, glowProxySize);

        public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode,
                                    int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale,
                                    float hdrColorScale, float glowProxySize)
            => effects.DrawSpriteOffset(owner, offset, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale,
                                        worldSpaceScale, hdrColorScale, glowProxySize);

        public int PrepareSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx,
                                 int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                                 float glowProxySize) => -1;
    }
}
