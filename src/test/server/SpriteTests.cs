using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// env_sprite, env_sprite_oriented and env_glow — one class under three names.
///
/// <para>
/// Everything here is the arithmetic and the rules, tested where no engine is
/// needed, because that is where a sprite's mistakes actually are. A sprite that
/// wraps its frame wrongly loops through the wrong pictures; one that reads the
/// start-on rule wrongly is invisible in a map that has always shown it; one
/// that gets the Worldcraft yaw fixup wrong faces the wrong way in every map
/// authored before 2005. None of the three reports an error.
/// </para>
///
/// <para>
/// The spawn tests go through the registry rather than calling
/// <see cref="EnvSprite.OnSpawn"/> directly, so that the map keys are exercised
/// as a map sends them — the same shape <see cref="EffectEntityTests"/> uses.
/// </para>
/// </summary>
public class SpriteTests
{
    private static readonly EntityHandle Ent = new(0x0044_0044u);

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

    /// <summary>
    /// A sprite spawned with the keys a map wrote.
    ///
    /// <para>
    /// Everything the sprite WRITES — the always-transmit flag, the blending,
    /// the fixed-up angles — is recorded rather than stubbed, because a void
    /// call is always recorded and a test can then assert what was asked for.
    /// Only the two READS need answers.
    /// </para>
    /// </summary>
    private static (ScriptedEntityRegistry, EnvSprite, RecordingEngine) SpawnWith(
        string className, params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);

        // Two reads the sprite makes that no test here is about: the model's
        // index and its frame count. STUBBED rather than ignored, because both
        // decide something — the index is what the client draws with, and the
        // count is what says a one-frame sprite does not animate.
        engine.Returns("Precache.PrecacheModel", 64);
        engine.Returns("Precache.ModelFrameCount", 1);
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvSprite).Assembly);
        registry.Services = services;

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);

        registry.Spawn(Ent, className);

        return (registry, (EnvSprite)registry.Get(Ent)!, engine);
    }

    /// <summary>
    /// The keys dm_lockdown's twenty-three corridor lights actually carry,
    /// landing where the client reads them.
    /// </summary>
    [Fact]
    public void MapKeysReachTheWire()
    {
        var (_, sprite, _) = SpawnWith(EntityNames.EnvSprite,
            ("model", "sprites/glow06.spr"),
            ("scale", "2.5"),
            ("framerate", "10.0"),
            ("rendermode", "5"),
            ("rendercolor", "189 233 247"),
            ("renderamt", "128"),
            ("GlowProxySize", "2.0"),
            ("renderfx", "0"));

        Assert.True(sprite.Sprite.ModelIndex == 64, "the sprite networks the index its material precached to");
        Assert.True(Near(sprite.Sprite.Scale, 2.5f), "the map's scale is what the client draws with");
        Assert.True(sprite.Sprite.RenderMode == 5, "and its render mode");
        Assert.True(sprite.Sprite.Red == 189 && sprite.Sprite.Green == 233 && sprite.Sprite.Blue == 247,
                    "rendercolor arrives as three separate channels");
        Assert.True(sprite.Sprite.Brightness == 128, "renderamt becomes the brightness stock's Spawn copies it into");
        Assert.True(Near(sprite.Sprite.GlowProxySize, 2.0f), "and the occlusion proxy the glow modes fade against");
    }

    /// <summary>
    /// The two values stock sets in its CONSTRUCTOR rather than in Spawn, which
    /// is the trap this whole family is prone to: a managed field starts at
    /// zero, so an entity that reports every key correctly still draws nothing.
    /// A glow proxy of zero is a sprite that is fully on or fully off; an HDR
    /// scale of zero is a sprite that is black under HDR.
    /// </summary>
    [Fact]
    public void ConstructorDefaultsSurviveAMapThatSetsNeither()
    {
        var (_, sprite, _) = SpawnWith(EntityNames.EnvSprite, ("model", "sprites/glow06.spr"));

        Assert.True(Near(sprite.Sprite.GlowProxySize, 2.0f), "a sprite with no GlowProxySize key gets stock's 2, not 0");
        Assert.True(Near(sprite.Sprite.HdrColorScale, 1.0f), "and stock's HDR colour scale of 1, not 0");
    }

    /// <summary>
    /// All three names are one class and one behaviour, which is the reason
    /// they moved together. Each writes its OWN table, because a table is keyed
    /// by the classname the map used.
    /// </summary>
    [Fact]
    public void ThreeNamesOneClass()
    {
        var (_, plain, _) = SpawnWith(EntityNames.EnvSprite, ("scale", "3"));
        var (_, oriented, _) = SpawnWith(EntityNames.EnvSpriteOriented, ("scale", "3"));
        var (_, glow, _) = SpawnWith(EntityNames.EnvGlow, ("scale", "3"));

        Assert.True(Near(plain.Sprite.Scale, 3f), "env_sprite writes env_sprite's table");
        Assert.True(Near(oriented.Oriented.Scale, 3f), "env_sprite_oriented writes its own");
        Assert.True(Near(glow.Glow.Scale, 3f), "and env_glow writes its own");

        // And not each other's. A shared implementation writing one table for
        // three classnames would send the wrong bytes to two of them.
        Assert.True(Near(oriented.Sprite.Scale, 0f), "the oriented sprite does not write env_sprite's table");
        Assert.True(Near(glow.Sprite.Scale, 0f), "and neither does env_glow");
    }

    /// <summary>
    /// Stock's scale clamp, and stock's complaint with it. A map with a scale of
    /// 200 has always been corrected rather than refused.
    /// </summary>
    [Fact]
    public void AScaleOutsideStocksRangeIsClamped()
    {
        var (_, big, _) = SpawnWith(EntityNames.EnvSprite, ("scale", "200"));
        var (_, negative, _) = SpawnWith(EntityNames.EnvSprite, ("scale", "-4"));

        Assert.True(Near(big.Sprite.Scale, EnvSprite.MaxScale), "a scale above 64 is clamped to 64");
        Assert.True(Near(negative.Sprite.Scale, 0f), "and a negative one to zero");
    }

    /// <summary>
    /// Stock's start-on rule, and the half of it that surprises: an UNNAMED
    /// sprite is on whatever its flags say, because nothing could ever switch it
    /// on. Only a named one waits.
    /// </summary>
    [Fact]
    public void OnlyANamedSpriteWaits()
    {
        Assert.True(EnvSprite.StartsOn(EnvSprite.SpawnFlag.None, named: false),
                    "an unnamed sprite with no flags is drawn from the start");

        Assert.True(!EnvSprite.StartsOn(EnvSprite.SpawnFlag.None, named: true),
                    "a named one with no start-on flag waits to be shown");

        Assert.True(EnvSprite.StartsOn(EnvSprite.SpawnFlag.StartOn, named: true),
                    "and a named one with the flag is drawn anyway");
    }

    /// <summary>
    /// The Worldcraft fixup, which is a compatibility rule rather than anything
    /// to do with sprites: the editor only ever wrote yaw, and a sprite's
    /// orientation reads roll.
    /// </summary>
    [Fact]
    public void ABareYawIsMovedToRoll()
    {
        Vector moved = EnvSprite.FixUpAngles(new Vector(0f, 45f, 0f), oriented: false);

        Assert.True(Near(moved.Y, 0f) && Near(moved.Z, 45f), "a yaw with no roll becomes a roll");

        // Not when the mapper set both, which is what says the rule is a fixup
        // for old maps rather than a swap.
        Vector both = EnvSprite.FixUpAngles(new Vector(0f, 45f, 90f), oriented: false);

        Assert.True(Near(both.Y, 45f) && Near(both.Z, 90f), "angles with a roll already set are left alone");
    }

    /// <summary>
    /// env_sprite_oriented's half-turn, and that it is taken from the angles the
    /// MAP wrote rather than from the fixed-up ones — which is what stock does,
    /// by saving a copy before calling down.
    /// </summary>
    [Fact]
    public void AnOrientedSpriteFacesTheOtherWay()
    {
        Vector turned = EnvSprite.FixUpAngles(new Vector(0f, 45f, 0f), oriented: true);

        Assert.True(Near(turned.Y, 225f), "an oriented sprite adds 180 to the yaw the map wrote");
        Assert.True(Near(turned.Z, 45f), "and still gets the Worldcraft roll fixup");

        Vector wrapped = EnvSprite.FixUpAngles(new Vector(0f, 270f, 90f), oriented: true);

        Assert.True(Near(wrapped.Y, 90f), "and the sum is wrapped back into [0, 360)");
    }

    /// <summary>
    /// Whether a sprite animates at all, which is the rule that makes every
    /// sprite in dm_lockdown stand still despite carrying "framerate" "10".
    /// </summary>
    [Fact]
    public void AStillSpriteDoesNotAnimateWhateverItsFramerateSays()
    {
        Assert.True(!EnvSprite.Animates(framerate: 10f, maxFrame: 0f, EnvSprite.SpawnFlag.None),
                    "a one-frame sprite does not animate however fast the map asked");

        Assert.True(EnvSprite.Animates(framerate: 10f, maxFrame: 8f, EnvSprite.SpawnFlag.None),
                    "a multi-frame one does");

        Assert.True(!EnvSprite.Animates(framerate: 0f, maxFrame: 8f, EnvSprite.SpawnFlag.None),
                    "and a multi-frame one with no framerate does not");

        // The one-shot flag overrides both, because a one-shot sprite has to
        // reach its last frame to switch itself off.
        Assert.True(EnvSprite.Animates(framerate: 0f, maxFrame: 0f, EnvSprite.SpawnFlag.PlayOnce),
                    "a one-shot sprite animates whatever else is true");
    }

    /// <summary>
    /// Stock's <c>Animate</c>: wrap against the LAST frame index, and stop
    /// instead if the one-shot flag is set.
    /// </summary>
    [Fact]
    public void TheFrameWrapsUnlessItIsAOneShot()
    {
        var (frame, finished) = EnvSprite.Advance(7f, 2f, maxFrame: 8f, EnvSprite.SpawnFlag.None);

        Assert.True(Near(frame, 1f), "a frame past the end wraps round against the last index");
        Assert.True(!finished, "and a looping sprite is never finished");

        (frame, finished) = EnvSprite.Advance(7f, 2f, maxFrame: 8f, EnvSprite.SpawnFlag.PlayOnce);

        Assert.True(finished, "a one-shot sprite is finished when it passes its last frame");

        (frame, finished) = EnvSprite.Advance(3f, 2f, maxFrame: 8f, EnvSprite.SpawnFlag.PlayOnce);

        Assert.True(Near(frame, 5f) && !finished, "and is not finished before it gets there");
    }

    /// <summary>
    /// Which render modes ignore the depth buffer. Stock's answer is the two
    /// glow modes and nothing else, and getting it wrong is invisible in a test
    /// and obvious in a corridor: a glow that says false is drawn depth-tested
    /// and disappears behind the geometry it is meant to bloom over.
    /// </summary>
    [Fact]
    public void OnlyTheGlowModesIgnoreDepth()
    {
        Assert.True(SpriteRules.IgnoresDepth((int)RenderMode.Glow), "kRenderGlow ignores the depth buffer");
        Assert.True(SpriteRules.IgnoresDepth((int)RenderMode.WorldGlow), "and so does kRenderWorldGlow");

        Assert.True(!SpriteRules.IgnoresDepth((int)RenderMode.TransAdd), "additive does not");
        Assert.True(!SpriteRules.IgnoresDepth((int)RenderMode.Normal), "and neither does normal");
    }

    /// <summary>
    /// The three colour inputs, each replacing one channel and leaving the other
    /// two alone — which is what makes them worth having separately, and what a
    /// handler that wrote the wrong field would get wrong invisibly.
    /// </summary>
    [Fact]
    public void EachColourInputTouchesOneChannel()
    {
        var (registry, sprite, _) = SpawnWith(EntityNames.EnvSprite,
            ("rendercolor", "10 20 30"));

        registry.Input(Ent, "ColorGreenValue", "200", 200f, default);

        Assert.True(sprite.Sprite.Red == 10, "the red channel is untouched");
        Assert.True(sprite.Sprite.Green == 200, "the green one takes the input's value");
        Assert.True(sprite.Sprite.Blue == 30, "and the blue channel is untouched");

        // Clamped, because stock clamps: a map firing 300 gets white rather
        // than a wrapped byte.
        registry.Input(Ent, "ColorBlueValue", "300", 300f, default);

        Assert.True(sprite.Sprite.Blue == 255, "a value above 255 is clamped rather than wrapped");
    }

    /// <summary>
    /// Show, hide and toggle. Three inputs and not one, because the toggle reads
    /// the current state and the other two do not — a class that implemented
    /// only the toggle would pass a test that pressed the button twice.
    /// </summary>
    [Fact]
    public void ShowHideAndToggle()
    {
        var (registry, sprite, _) = SpawnWith(EntityNames.EnvSprite);

        Assert.True(sprite.Sprite.Visible, "an unnamed sprite starts visible");

        registry.Input(Ent, "HideSprite", "", 0f, default);
        Assert.True(!sprite.Sprite.Visible, "HideSprite stops it being drawn");

        registry.Input(Ent, "HideSprite", "", 0f, default);
        Assert.True(!sprite.Sprite.Visible, "and a second one leaves it hidden");

        registry.Input(Ent, "ShowSprite", "", 0f, default);
        Assert.True(sprite.Sprite.Visible, "ShowSprite brings it back");

        registry.Input(Ent, "ToggleSprite", "", 0f, default);
        Assert.True(!sprite.Sprite.Visible, "ToggleSprite reads the state and reverses it");

        registry.Input(Ent, "ToggleSprite", "", 0f, default);
        Assert.True(sprite.Sprite.Visible, "and reverses it back");

        // Stock's obsolete Use handler is the same toggle.
        registry.Input(Ent, "Use", "", 0f, default);
        Assert.True(!sprite.Sprite.Visible, "and a Use does what the toggle does");
    }

    /// <summary>
    /// The "SetScale" input, which is a bare field write despite the
    /// two-argument SetScale in stock's header. No ramp reaches a map.
    /// </summary>
    [Fact]
    public void SetScaleIsAPlainWrite()
    {
        var (registry, sprite, _) = SpawnWith(EntityNames.EnvSprite, ("scale", "1"));

        registry.Input(Ent, "SetScale", "4", 4f, default);
        Assert.True(Near(sprite.Sprite.Scale, 4f), "SetScale replaces the scale outright");

        registry.Input(Ent, "SetScale", "999", 999f, default);
        Assert.True(Near(sprite.Sprite.Scale, EnvSprite.MaxScale), "and is clamped to stock's ceiling");
    }

    /// <summary>
    /// That the sprite tells the shell to send it to everyone.
    ///
    /// <para>
    /// Stock's <c>UpdateTransmitState</c> answers FL_EDICT_ALWAYS for a sprite
    /// with no move parent, and its own comment says why: sprites are strewn
    /// through a level and rarely change, so sending each once beats bursting
    /// them in and out at PVS boundaries. Getting this wrong makes twenty-three
    /// corridor lights pop as the player walks, which nothing reports.
    /// </para>
    /// </summary>
    [Fact]
    public void ASpriteAsksToBeSentToEveryone()
    {
        var services = Services(out var engine);

        engine.Returns("Precache.PrecacheModel", 64);
        engine.Returns("Precache.ModelFrameCount", 1);
        engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvSprite).Assembly);
        registry.Services = services;

        registry.Spawn(Ent, EntityNames.EnvSprite);

        Assert.True(engine.CountOf("EntityRender.SetTransmitAlways") == 1,
                    "a sprite asks the shell to send it to every player");
    }

    /// <summary>
    /// CSprite::SetBrightness / SetScale with a time: the target and how long
    /// the client takes to reach it, both on the wire (m_nBrightness and
    /// m_flBrightnessTime, m_flSpriteScale and m_flScaleTime).
    /// </summary>
    [Fact]
    public void ATimedChangeNetworksItsTargetAndItsTime()
    {
        var (_, sprite, _) = SpawnWith(EntityNames.EnvSprite, ("model", "sprites/glow01.spr"), ("renderamt", "255"));

        sprite.SetBrightness(32, 0.5f);
        sprite.SetScale(0.025f, 0.25f);

        Assert.Equal((32, 0.5f), (sprite.Sprite.Brightness, sprite.Sprite.BrightnessTime));
        Assert.Equal((0.025f, 0.25f), (sprite.Sprite.Scale, sprite.Sprite.ScaleTime));
    }

    /// <summary>
    /// CSprite::FadeAndDie: brightness to zero over the time, and the sprite
    /// removed once the time is up and not before (AnimateUntilDead) -- the
    /// crossbow bolt's glow in a wall, for one.
    /// </summary>
    [Fact]
    public void FadeAndDieFadesThenRemoves()
    {
        float now = 10f;
        var (registry, sprite, engine) = SpawnWith(EntityNames.EnvSprite, ("model", "sprites/glow01.spr"), ("renderamt", "255"));
        engine.ReturnsEachCall("Clock.Now", () => now);

        sprite.FadeAndDie(3f);
        Assert.Equal((0, 3f), (sprite.Sprite.Brightness, sprite.Sprite.BrightnessTime));

        now = 12.9f;
        registry.Think(Ent);
        Assert.Equal(0, registry.Directory.PendingRemovalCount);

        now = 13.1f;
        registry.Think(Ent);
        Assert.Equal(1, registry.Directory.PendingRemovalCount);
    }

    /// <summary>CSprite::FadeOutFromSpawn: a moment later, FadeAndDie( 0.25 ).</summary>
    [Fact]
    public void FadeOutFromSpawnFadesOverAQuarterSecond()
    {
        float now = 10f;
        var (registry, sprite, engine) = SpawnWith(EntityNames.EnvSprite, ("model", "sprites/glow01.spr"), ("renderamt", "128"));
        engine.ReturnsEachCall("Clock.Now", () => now);

        sprite.FadeOutFromSpawn();
        Assert.Equal(128, sprite.Sprite.Brightness);

        now = 10.01f;
        registry.Think(Ent);
        Assert.Equal((0, 0.25f), (sprite.Sprite.Brightness, sprite.Sprite.BrightnessTime));

        now = 10.3f;
        registry.Think(Ent);
        Assert.Equal(1, registry.Directory.PendingRemovalCount);
    }
}
