using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The light family, which is nearly all decision and almost no engine.
///
/// <para>
/// Everything a runtime light does reduces to "which string does the engine's
/// style slot hold now", and every one of the ways it can be wrong is invisible
/// in play: a light that keys its style below 32 writes over one of the world's
/// preset animations and changes every surface in the map that uses it; a light
/// that forgets the start-off flag comes up lit; a fade that steps the wrong way
/// runs away from its target and never arrives. None of those produce a log
/// line, so they are checked here.
/// </para>
///
/// <para>
/// The engine is not up, so <c>Engine.EngineLightStyle</c> is a no-op — which is
/// exactly why the class keeps <see cref="Light.IsOn"/> and
/// <see cref="Light.Style"/> readable. What is being tested is the decision, and
/// the decision is legible without the engine.
/// </para>
/// </summary>
public class LightTests
{
    private static readonly EntityHandle Ent = new(0x0057_0001u);

    /// <summary>
    /// What a light_dynamic reads that a test about its COLOUR is not about:
    /// where it is pointing. Named rather than defaulted, so a reader can see
    /// what was set aside.
    /// </summary>
    private static readonly string[] Aimless = { "Entities.GetAngles" };

    private static (ScriptedEntityRegistry, T) SpawnWith<T>(string className,
        params (string Key, string Value)[] keys) where T : ScriptedEntity
        => SpawnIgnoring<T>(className, Array.Empty<string>(), keys);

    /// <summary>
    /// The same, naming the engine reads the test does not exercise.
    ///
    /// <c>light_dynamic</c> needs one: it re-derives its aim at spawn from
    /// whatever it points at, so it reads the entity's angles. A test about the
    /// colour has no opinion about where the light looks, and saying so is
    /// better than letting the double answer quietly — which is the whole
    /// reason the double throws.
    /// </summary>
    private static (ScriptedEntityRegistry, T) SpawnIgnoring<T>(string className,
        string[] ignores, params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Light).Assembly);
        registry.Services = Ignoring([.. ignores, "Entities.GetOrigin"]);

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);

        registry.Spawn(Ent, className);
        return (registry, (T)registry.Get(Ent)!);
    }

    /// <summary>
    /// The thirteen animations that came over from world.cpp. Two of them are
    /// spot-checked by content rather than by count, because a table that is the
    /// right LENGTH and shifted by one gives every map's candles a strobe.
    /// </summary>
    [Fact]
    public void PresetTable()
    {
        Assert.True(LightStyles.PresetCount == 13, "there are thirteen preset lightstyles");
        Assert.True(LightStyles.Default(0) == "m", "style 0 is steady normal");
        Assert.True(LightStyles.Default(4) == "mamamamamama", "style 4 is the fast strobe");
        Assert.True(LightStyles.Default(10) == "mmamammmmammamamaaamammma", "style 10 is the fluorescent flicker");

        // A number the map made up answers steady rather than throwing: it comes
        // from a keyvalue, and a mapper's typo should light the room.
        Assert.True(LightStyles.Default(99) == LightStyles.Normal, "a preset number past the end falls back to normal");
        Assert.True(LightStyles.Default(-1) == LightStyles.Normal, "and so does a negative one");
    }

    [Fact]
    public void SwitchingAndPatterns()
    {
        // A named light with a switchable style: the case the entity exists for.
        var (registry, light) = SpawnWith<Light>(EntityNames.Light,
            ("targetname", "lamp"), ("style", "33"));

        Assert.True(light.Style == 33, "a named light keeps its switchable style");
        Assert.True(light.IsOn, "and comes up lit");

        registry.Input(Ent, "TurnOff", "", 0f, default);
        Assert.True(!light.IsOn, "TurnOff puts it out");

        registry.Input(Ent, "Toggle", "", 0f, default);
        Assert.True(light.IsOn, "Toggle brings it back");

        registry.Input(Ent, "Toggle", "", 0f, default);
        Assert.True(!light.IsOn, "and Toggle puts it out again");

        // Setting a pattern lights it whether or not it was lit. Stock does the
        // same and the alternative reads as an input that did nothing.
        registry.Input(Ent, "SetPattern", "mmnmmommo", 0f, default);
        Assert.True(light.IsOn, "SetPattern relights a dark light");

        // The spawnflag, which is the other way a light starts dark. Through the
        // static form, because the property asks the engine and there is not one
        // here.
        Assert.True(Light.StartsOff(1), "the start-off flag holds a light dark");
        Assert.True(!Light.StartsOff(0), "and no flags leaves it lit");

        // light_environment is a Light and inherits all of it, which is the
        // whole claim the class makes.
        var (_, sun) = SpawnWith<LightEnvironment>(EntityNames.LightEnvironment,
            ("targetname", "sun"), ("style", "35"), ("_light", "255 255 255 200"));
        Assert.True(sun.IsOn && sun.Style == 35, "light_environment is a switchable light too");

        // The other half of the naming rule, and the one that costs edicts if it
        // is wrong: a light nobody named has nothing to switch and goes away.
        var (_, unnamed) = SpawnWith<Light>(EntityNames.LightSpot, ("style", "0"));
        Assert.True(unnamed.IsInert, "an unnamed light deletes itself");
        Assert.True(!light.IsInert, "and a named one does not");
    }

    /// <summary>
    /// FadeToPattern walks one letter per tenth of a second and stops on
    /// arrival. The bug worth catching is a step in the wrong direction — the
    /// light would walk away from its target and never stop thinking.
    /// </summary>
    [Fact]
    public void FadeWalksOneStepAtATime()
    {
        var (registry, light) = SpawnWith<Light>(EntityNames.Light,
            ("targetname", "dimmer"), ("style", "36"), ("pattern", "m"));

        // 'm' down to 'j' is three steps.
        registry.Input(Ent, "FadeToPattern", "jjj", 0f, default);
        Assert.True(light.IsOn, "fading lights the light");

        registry.Think(Ent);
        registry.Think(Ent);
        Assert.True(light.FadeInProgress, "two steps in, the fade is still running");
        Assert.True(light.CurrentFadeLetter == 'k', "and it has moved two letters");

        registry.Think(Ent);
        Assert.True(!light.FadeInProgress, "the third step arrives");

        // Fading UP has to step the other way, which is the half a single test
        // direction would miss.
        registry.Input(Ent, "FadeToPattern", "p", 0f, default);
        registry.Think(Ent);
        Assert.True(light.CurrentFadeLetter == 'k', "fading upward steps upward");

        // Turning the light off abandons the fade rather than racing it.
        registry.Input(Ent, "FadeToPattern", "z", 0f, default);
        registry.Input(Ent, "TurnOff", "", 0f, default);
        Assert.True(!light.FadeInProgress, "switching off cancels a fade in progress");
    }

    //-------------------------------------------------------------------------
    // A SPOTLIGHT'S POOL, whose four networked colour channels became one
    // Color32.
    //
    // The two setters are the seam the conversion could have broken, and it is
    // a seam a compiler cannot see: how bright the patch is and what colour it
    // is are set by different callers at different times, so a Color32 written
    // whole by one of them would silently undo the other. With four separate
    // byte fields that was impossible by construction; with one field it is a
    // rule, and a rule needs a test.
    //-------------------------------------------------------------------------
    [Fact]
    public void ASpotlightsPoolKeepsItsColourAndItsAlphaApart()
    {
        var (_, pool) = SpawnWith<SpotlightEnd>(EntityNames.SpotlightEnd);

        pool.State.BindLocal();

        // Spawn's default. A pool nothing has faded is fully opaque, or it is
        // not a light at all.
        pool.OnSpawn();
        Assert.True(pool.State.Tint.A == 255, "a fresh pool starts opaque");

        pool.SetColour(10, 20, 30);

        Assert.True(pool.State.Tint == new Color32(10, 20, 30, 255), "setting the colour leaves the alpha where OnSpawn put it");

        pool.Place(new Vector(1f, 2f, 3f), lightScale: 40f, alpha: 128);

        Assert.True(pool.State.Tint == new Color32(10, 20, 30, 128), "and placing it fades the alpha without losing the colour");

        Assert.True(pool.State.LightScale == 40f, "while the size is the size it was placed at");

        // The other order, because a rule tested one way round is a rule half
        // tested: a spotlight sets both, and which arrives first is its business.
        pool.SetColour(200, 100, 50);

        Assert.True(pool.State.Tint == new Color32(200, 100, 50, 128), "recolouring after a fade keeps the faded alpha");
    }

    //-------------------------------------------------------------------------
    // A DYNAMIC LIGHT'S COLOUR, which is now one field where it was three.
    //
    // THE SEAM THIS IS FOR. Merging three channels into one Color32 makes a
    // partial write possible where it was not: three separate byte fields could
    // only be set one at a time, and one field can be set whole by a caller who
    // meant to change one channel. light_dynamic turns out to be the good case —
    // it has exactly ONE writer and that writer always had the whole colour in
    // its hand, as a packed uint out of the `_light` key — but "there is only
    // one writer" is a fact about today's code, so it is asserted rather than
    // remembered.
    //
    // The fourth component is the other half. `_light` carries one, vrad reads
    // it as a brightness, and this entity's brightness is the exponent — so it
    // has always been dropped, and a Color32 with somewhere to put it is exactly
    // the shape that would start carrying it by accident.
    //-------------------------------------------------------------------------
    [Fact]
    public void ADynamicLightTakesItsColourWholeAndDropsVradsBrightness()
    {
        var (_, light) = SpawnIgnoring<LightDynamic>(
            EntityNames.LightDynamic, Aimless, ("_light", "10 20 30 200"));

        Assert.True(light.State.Colour is { R: 10, G: 20, B: 30 }, "the three channels of _light arrive in order");

        Assert.True(light.State.Colour.A == 255, "and the fourth number is vrad's brightness, dropped rather than sent as an alpha");

        // A SECOND KEY REPLACES THE WHOLE COLOUR, which is the behaviour three
        // separate fields had and the one a merge could quietly change: a writer
        // that built the new colour from the old one's channels would leave a
        // channel behind whenever the new value had fewer components.
        var (_, shorter) = SpawnIgnoring<LightDynamic>(
            EntityNames.LightDynamic, Aimless, ("_light", "255 255 255"), ("_light", "1 2 3"));

        Assert.True(shorter.State.Colour is { R: 1, G: 2, B: 3, A: 255 }, "a second _light replaces every channel rather than merging into the first");

        // A key with too few components zeroes the rest, which is ParseColour's
        // rule and not this entity's — pinned here because the entity now hands
        // the result over in one piece and a reader cannot see the fill.
        var (_, partial) = SpawnIgnoring<LightDynamic>(
            EntityNames.LightDynamic, Aimless, ("_light", "255 255 255"), ("_light", "7"));

        Assert.True(partial.State.Colour is { R: 7, G: 0, B: 0, A: 255 }, "and a one-component _light is red on black, not a partial overwrite of what was there");
    }

    //-------------------------------------------------------------------------
    // A light with no _light key at all. Black, and it must still be a colour
    // rather than whatever the blob happened to hold.
    //
    // Worth its own case because the colour's word is no longer shared with
    // Style: an uninitialised colour used to be three bytes of a word something
    // else wrote, and is now a word of its own that only the colour touches.
    //-------------------------------------------------------------------------
    [Fact]
    public void ADynamicLightWithNoColourKeyIsBlackAndOpaque()
    {
        var (_, light) = SpawnIgnoring<LightDynamic>(EntityNames.LightDynamic, Aimless);

        Assert.True(light.State.Colour is { R: 0, G: 0, B: 0 }, "a light with no _light key is black");

        // Zero, not 255 — nothing has written the field, so the alpha is the
        // blob's rather than the writer's. Stated because the pane shows it, and
        // "rgba(0,0,0,0)" should read as "never set" rather than as a bug.
        Assert.True(light.State.Colour.A == 0, "and its alpha is the blob's zero, because no writer has touched the field");
    }
}
