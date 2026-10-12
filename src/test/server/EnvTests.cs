using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// env_shake's radius rule, which a map does not write and cannot see, and
/// which is therefore wrong silently.
/// </summary>
public class EnvTests
{

    [Fact]
    public void FadeFlagsFromSpawnFlags()
    {
        // One of IN or OUT is always set, and never both: a fade with neither is
        // not a fade, and the engine's own comment on FFADE_IN says it exists so
        // that zero is never passed in.
        Assert.True(EnvFade.FlagsFor(EnvFade.SpawnFlag.None) == FadeFlags.Out, "no flags means fading out");
        Assert.True(EnvFade.FlagsFor(EnvFade.SpawnFlag.FadeIn) == FadeFlags.In, "the in flag means fading in");

        Assert.True(EnvFade.FlagsFor(EnvFade.SpawnFlag.Modulate) == (FadeFlags.Out | FadeFlags.Modulate), "modulate is carried alongside the direction");

        Assert.True(EnvFade.FlagsFor(EnvFade.SpawnFlag.StayOut) == (FadeFlags.Out | FadeFlags.StayOut), "stay-out is carried alongside the direction");

        // The only-one flag picks a RECIPIENT, not a behaviour, so it must not
        // leak into the flags the client is handed.
        Assert.True(EnvFade.FlagsFor(EnvFade.SpawnFlag.OnlyOne) == FadeFlags.Out, "the only-one flag is not a fade flag");

        Assert.True(EnvFade.FlagsFor(EnvFade.SpawnFlag.FadeIn | EnvFade.SpawnFlag.Modulate | EnvFade.SpawnFlag.StayOut) == (FadeFlags.In | FadeFlags.Modulate | FadeFlags.StayOut), "several at once combine");
    }

    [Fact]
    public void PunchRadius()
    {
        Assert.True(EnvViewPunch.RadiusFor(EnvViewPunch.SpawnFlag.None, 256f) == 256f, "a punch with a radius keeps it");

        Assert.True(EnvViewPunch.RadiusFor(EnvViewPunch.SpawnFlag.Everyone, 256f) == 0f, "the punch-everyone flag beats a radius that was left behind");
    }

    private static T Spawn<T>(string className, uint index, params (string Key, string Value)[] keys)
        where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvShake).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return (T)registry.Get(handle)!;
    }

    [Fact]
    public void ShakeRadius()
    {
        var bounded = Spawn<EnvShake>(EntityNames.EnvShake, 3, ("radius", "512"));
        Assert.True(bounded.EffectiveRadius == 512f, "a shake with a radius keeps it");

        // Zero radius means EVERYONE, not nobody — the engine reads it as "no
        // falloff, send to all". So the spawnflag and an unset radius arrive at
        // the same place, and a map that sets the flag but left a radius behind
        // must not have the radius win.
        //
        // Through the static form rather than the property, because SpawnFlags
        // asks the ENGINE and a test has none to ask: it would see zero, take
        // the other branch, and pass for entirely the wrong reason.
        Assert.True(EnvShake.RadiusFor(spawnFlags: EnvShake.SpawnFlag.Everyone, radius: 512f) == 0f, "the shake-everyone flag beats a radius that was left behind");

        Assert.True(EnvShake.RadiusFor(spawnFlags: EnvShake.SpawnFlag.None, radius: 512f) == 512f, "without the flag the radius stands");

        var unset = Spawn<EnvShake>(EntityNames.EnvShake, 5);
        Assert.True(unset.EffectiveRadius == 0f, "a shake with no radius shakes everyone");
    }

    /// <summary>
    /// env_steam and env_steamjet, which are one class under two names.
    ///
    /// <para>
    /// The bit that separates them is decided by ASKING THE CLASSNAME, which is
    /// stock's own test and the only reason two names exist. Nothing in play
    /// would show it inverted — a jet pointing the wrong way still looks like a
    /// jet — and every legacy map that has one was built around the wrong
    /// direction being right.
    /// </para>
    ///
    /// <para>
    /// The four DEFINE_INPUTs are checked as inputs as well as keys, because in
    /// Source they are one declaration and here they are two: a property on the
    /// vars class and a handler on the entity. Two declarations of one thing is
    /// exactly what drifts.
    /// </para>
    /// </summary>
    [Fact]
    public void SteamJet()
    {
        var steam = Spawn<EnvSteam>(EntityNames.EnvSteam, 11,
            ("spreadspeed", "15"), ("speed", "120"), ("startsize", "10"),
            ("endsize", "25"), ("rate", "26"), ("jetlength", "80"),
            ("initialstate", "1"), ("type", "1"));

        Assert.True(!steam.State.FaceLeft, "env_steam points forward");
        Assert.True(steam.State.SpreadSpeed == 15f, "a jet reads its spread speed");
        Assert.True(steam.State.Speed == 120f, "and its speed");
        Assert.True(steam.State.StartSize == 10f, "and its start size");
        Assert.True(steam.State.EndSize == 25f, "and its end size");
        Assert.True(steam.State.Rate == 26f, "and its rate");
        Assert.True(steam.State.JetLength == 80f, "and its length");
        Assert.True(steam.State.Type == 1, "and the heat-haze type");
        Assert.True(steam.State.Emit, "an initial state of on emits");

        // Stock's constructor sets this before any key is read, so a map that
        // says nothing gets 8 rather than 0. A puff that never turns is the
        // silent failure this pins.
        Assert.True(steam.State.RollSpeed == 8f, "roll speed defaults to stock's 8");

        var legacy = Spawn<EnvSteam>(EntityNames.EnvSteamjet, 12);
        Assert.True(legacy.State.FaceLeft, "env_steamjet points left, which is the whole of the difference");

        Assert.True(!legacy.State.Emit, "a jet with no initial state is off");

        // The two tables are separate objects keyed by classname, so writing one
        // must not be visible through the other.
        Assert.True(!ReferenceEquals(steam.State, legacy.State), "the two classnames have their own tables");

        var driven = Spawn<EnvSteam>(EntityNames.EnvSteam, 13);
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvShake).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(14);
        registry.Spawn(handle, EntityNames.EnvSteam);
        var driven2 = (EnvSteam)registry.Get(handle)!;

        registry.Input(handle, "Speed", "250", 250f, EntityHandle.None);
        Assert.True(driven2.State.Speed == 250f, "the Speed input writes the networked field");

        registry.Input(handle, "JetLength", "300", 300f, EntityHandle.None);
        Assert.True(driven2.State.JetLength == 300f, "and JetLength");

        registry.Input(handle, "SpreadSpeed", "44", 44f, EntityHandle.None);
        Assert.True(driven2.State.SpreadSpeed == 44f, "and SpreadSpeed");

        registry.Input(handle, "Rate", "60", 60f, EntityHandle.None);
        Assert.True(driven2.State.Rate == 60f, "and Rate");

        registry.Input(handle, "TurnOn", "", 0f, EntityHandle.None);
        Assert.True(driven2.State.Emit, "TurnOn emits");

        registry.Input(handle, "Toggle", "", 0f, EntityHandle.None);
        Assert.True(!driven2.State.Emit, "Toggle stops it");

        registry.Input(handle, "Toggle", "", 0f, EntityHandle.None);
        Assert.True(driven2.State.Emit, "and starts it again");

        registry.Input(handle, "TurnOff", "", 0f, EntityHandle.None);
        Assert.True(!driven2.State.Emit, "TurnOff stops it");

        Assert.True(driven.State.Speed == 0f, "a jet spawned with no keys is still a jet");
    }

    /// <summary>
    /// env_embers, whose two decisions are both easy to get backwards and
    /// invisible when they are.
    ///
    /// <para>
    /// The first is that an UNNAMED volume starts emitting whatever its flags
    /// say, because nothing could ever switch it on. Read the condition the
    /// obvious way round and every unnamed env_embers in every map goes dark.
    /// </para>
    ///
    /// <para>
    /// The second is that without the Toggle flag a Use is spent — stock clears
    /// its own use function after the first press. A volume that could be
    /// switched back off looks like a fix and is a different entity.
    /// </para>
    /// </summary>
    [Fact]
    public void Embers()
    {
        var named = Spawn<EnvEmbers>(EntityNames.EnvEmbers, 21,
            ("targetname", "embers1"), ("density", "40"), ("lifetime", "4"), ("speed", "32"));

        Assert.True(named.State.Density == 40, "embers read their density");
        Assert.True(named.State.Lifetime == 4, "and their lifetime");
        Assert.True(named.State.Speed == 32, "and their speed");

        // Through the static form rather than the property, because the entity
        // asks the ENGINE for its name and a test has none to ask: it would see
        // an empty name, take the other branch, and pass for the wrong reason.
        Assert.True(EnvEmbers.StartsOn(EnvEmbers.SpawnFlag.None, named: false), "an unnamed volume emits from the start, because nothing can switch it on");

        Assert.True(!EnvEmbers.StartsOn(EnvEmbers.SpawnFlag.None, named: true), "a named volume with no start-on flag waits");

        Assert.True(EnvEmbers.StartsOn(EnvEmbers.SpawnFlag.StartOn, named: true), "the start-on flag beats being named");

        Assert.True(!EnvEmbers.StartsOn(EnvEmbers.SpawnFlag.Toggle, named: true), "the toggle flag is not a start-on flag");

        // The Use rule, through its static form for the reason above: an entity
        // with no engine reads its spawnflags as zero, so every case would
        // collapse into the unflagged one and pass for the wrong reason.
        var first = EnvEmbers.AfterUse(EnvEmbers.SpawnFlag.None, emitting: true, spent: false);
        Assert.True(!first.Emit, "one Use switches a volume off");
        Assert.True(first.Spent, "and spends the Use, because stock clears its own use function");

        var second = EnvEmbers.AfterUse(EnvEmbers.SpawnFlag.None, emitting: first.Emit, spent: first.Spent);
        Assert.True(!second.Emit && second.Spent, "so the second Use does nothing at all");

        var toggling = EnvEmbers.AfterUse(EnvEmbers.SpawnFlag.Toggle, emitting: true, spent: false);
        Assert.True(!toggling.Emit, "a toggling volume switches off");
        Assert.True(!toggling.Spent, "and is never spent");

        var again = EnvEmbers.AfterUse(EnvEmbers.SpawnFlag.Toggle, emitting: toggling.Emit, spent: toggling.Spent);
        Assert.True(again.Emit, "so it answers every Use");
    }
}