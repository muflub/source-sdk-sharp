using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// env_smokestack and env_particlelight, which are one port.
///
/// <para>
/// The pair moved together because it could not move apart. Stock's
/// <c>CSmokeStack::Activate</c> finds its lights with
/// <c>gEntList.FindEntityByClassname</c>, and that walks the NATIVE entity list,
/// where every managed entity appears as one of a handful of shell classes
/// rather than under the name a map wrote. Port one and not the other and the
/// link breaks silently: the stack still spawns, still networks, and draws flat
/// grey smoke with no fire under it. So the lookup is the thing this suite is
/// mostly about.
/// </para>
/// </summary>
public class SmokeStackTests
{

    /// <summary>
    /// A registry with both classes registered and the engine reads these
    /// checks do not care about named.
    /// </summary>
    /// <param name="stackName">
    /// What every entity in this registry answers when asked its NAME.
    ///
    /// One answer for all of them, because the double is per-engine rather than
    /// per-entity — which is enough here: what the light search compares is the
    /// light's own PSName key against the stack's name, and a light naming
    /// something else is still told apart.
    /// </param>
    private static ScriptedEntityRegistry Registry(string stackName = "")
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvSmokeStack).Assembly);

        var services = Services(out var engine);

        // Four reads the stack does on the way in that none of these checks is
        // about: the material precache, the think it schedules to stand in for
        // stock's Activate, and the two calls that Activate makes about the 3D
        // skybox. The skybox rule cannot be checked here at all — it is a
        // question about the compiled map.
        //
        // The fifth is a light's own position, which the stack networks so the
        // client can measure each puff's distance from it. Where a light is is
        // the map's business, not this suite's.
        engine.Ignores("Precache.PrecacheModel",
                       "Scripted.SetNextThink",
                       "EntityRender.DetectInSkybox",
                       "EntityRender.SetTransmitAlways",
                       "Entities.GetOrigin");

        engine.Returns("Entities.GetName", stackName);

        // The registry IS the directory, which is the shape ServerModule uses
        // and the reason a stack can find its lights at all. Without it the
        // stack's constructor cannot be satisfied and the spawn quietly binds
        // nothing — which reads as a null instance rather than as a missing
        // dependency, so it is worth saying out loud.
        services.AddInstance<IEntityDirectory>(registry);

        registry.Services = services;

        return registry;
    }

    private static T Spawn<T>(ScriptedEntityRegistry registry, string className, uint index,
                              params (string Key, string Value)[] keys)
        where T : ScriptedEntity
    {
        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return (T)registry.Get(handle)!;
    }

    /// <summary>
    /// The SmokeMaterial rule: a name with or without its extension, and the
    /// legacy fallback for a map that names none.
    /// </summary>
    [Fact]
    public void Material()
    {
        Assert.True(EnvSmokeStack.MaterialFor("particle/mysmoke") == "particle/mysmoke.vmt", "a bare material name gains its extension");

        Assert.True(EnvSmokeStack.MaterialFor("particle/mysmoke.vmt") == "particle/mysmoke.vmt", "and one that has it keeps it");

        // Stock's fallback lives at the END of Activate rather than in KeyValue,
        // because it only applies when nothing resolved a model at all. A stack
        // with no material would otherwise draw with an unset one.
        Assert.True(EnvSmokeStack.MaterialFor("") == "particle/SmokeStack.vmt", "no material at all falls back to stock's legacy sheet");
    }

    /// <summary>
    /// The wind rule, which stock decides by accident and this decides on
    /// purpose.
    /// </summary>
    [Fact]
    public void Wind()
    {
        // Stock's KeyValue writes m_vWind from an explicit "Wind" vector AND
        // recomputes it from WindAngle/WindSpeed, each as the key arrives — so
        // in stock the LAST key parsed wins, which a map cannot control. Made
        // explicit here: an explicit vector beats the pair.
        var explicitWind = EnvSmokeStack.WindFor(new Vector(3f, 4f, 5f), windAngle: 90, windSpeed: 100);
        Assert.True(explicitWind.X == 3f && explicitWind.Y == 4f && explicitWind.Z == 5f, "an explicit wind vector is taken as written");

        // Stock's RecalcWindVector: cos and sin of the angle, scaled by the
        // speed, and zero on Z — wind never blows a puff up or down.
        var fromAngle = EnvSmokeStack.WindFor(new Vector(0f, 0f, 0f), windAngle: 0, windSpeed: 40);
        Assert.True(MathF.Abs(fromAngle.X - 40f) < 0.001f, "an angle of zero blows along +X");
        Assert.True(MathF.Abs(fromAngle.Y) < 0.001f, "and not along +Y");

        var ninety = EnvSmokeStack.WindFor(new Vector(0f, 0f, 0f), windAngle: 90, windSpeed: 40);
        Assert.True(MathF.Abs(ninety.Y - 40f) < 0.001f, "ninety degrees blows along +Y");
        Assert.True(MathF.Abs(ninety.X) < 0.001f, "and not along +X");
        Assert.True(ninety.Z == 0f, "and never up or down");

        var still = EnvSmokeStack.WindFor(new Vector(0f, 0f, 0f), 0, 0);
        Assert.True(still.X == 0f && still.Y == 0f && still.Z == 0f, "no wind at all is still");
    }

    /// <summary>
    /// The two defaults that are stock's and look wrong.
    /// </summary>
    [Fact]
    public void Defaults()
    {
        var bare = Spawn<EnvSmokeStack>(Registry(), EntityNames.EnvSmokestack, 61);

        // BLACK at full alpha, where every other entity's rendercolor defaults
        // to white. It is what makes an unlit stack read as smoke rather than as
        // a white cloud, and a class that defaulted to white would look like a
        // lighting bug.
        Assert.True(bare.State.Red == 0 && bare.State.Green == 0 && bare.State.Blue == 0, "a stack with no rendercolor is black");

        Assert.True(bare.State.Alpha == 255, "and fully opaque");

        // ZERO, and that is stock: CSmokeStack's constructor never touches the
        // four DEFINE_INPUT fields, so a map that sets none of them networks
        // four zeros and the column emits nothing. Written down so it is a
        // recorded fact rather than a suspected bug.
        Assert.True(bare.State.Speed == 0f && bare.State.Rate == 0f && bare.State.SpreadSpeed == 0f && bare.State.JetLength == 0f, "and its four input fields are stock's zeros, so it emits nothing");

        Assert.True(!bare.State.Emit, "a stack with no InitialState is off");

        var on = Spawn<EnvSmokeStack>(Registry(), EntityNames.EnvSmokestack, 62,
            ("InitialState", "1"), ("Speed", "60"), ("Rate", "26"));

        Assert.True(on.State.Emit, "an InitialState of on emits");
        Assert.True(on.State.Speed == 60f && on.State.Rate == 26f, "and the keys behind the four inputs reach the wire");
    }

    /// <summary>
    /// The lookup the pair exists for.
    /// </summary>
    [Fact]
    public void Lights()
    {
        // The stack has to be NAMED, because what a light says is the name of
        // the stack it belongs to. An unnamed stack can never be lit — which is
        // why the name is stubbed rather than written as a keyvalue: a
        // targetname belongs to the ENGINE, and the entity asks for it back.
        var registry = Registry(stackName: "chimney");

        var stack = Spawn<EnvSmokeStack>(registry, EntityNames.EnvSmokestack, 63,
            ("InitialState", "1"));

        var fire = Spawn<EnvParticleLight>(registry, EntityNames.EnvParticlelight, 64,
            ("PSName", "chimney"), ("Intensity", "9000"),
            ("Color", "255 130 40"), ("Directional", "1"));

        var sky = Spawn<EnvParticleLight>(registry, EntityNames.EnvParticlelight, 65,
            ("PSName", "chimney"), ("Intensity", "6000"),
            ("Color", "80 130 255"), ("Directional", "0"));

        // A light naming a DIFFERENT stack, which must be ignored. Without this
        // the loop would look right while matching everything.
        var stranger = Spawn<EnvParticleLight>(registry, EntityNames.EnvParticlelight, 66,
            ("PSName", "someotherstack"), ("Intensity", "1"),
            ("Color", "0 255 0"), ("Directional", "1"));

        Assert.True(fire.ParticleSystemName == "chimney", "a light knows which stack it belongs to");
        Assert.True(fire.Directional && !sky.Directional, "and whether it is the directional one");
        Assert.True(stranger.ParticleSystemName != "chimney", "and the stranger is not this stack's");

        // Nothing has been collected yet: stock does this in Activate, which
        // runs after every entity has spawned, and here that is the think.
        Assert.True(stack.State.DirIntensity == 0f, "before the sweep a stack has no directional light");

        registry.Think(new EntityHandle(63));

        Assert.True(stack.State.DirIntensity == 9000f, "the sweep finds the directional light");
        Assert.True(stack.State.AmbientIntensity == 6000f, "and the ambient one");

        // Into 0-1 space, which is where stock puts them at the end of Activate
        // and where the client reads them. A stack that networked 0-255 would
        // be lit two hundred and fifty-five times too brightly.
        Assert.True(stack.State.DirColor.X == 1f, "a light's colour arrives in 0-1 space");
        Assert.True(stack.State.DirColor.Y == 130f / 255f, "and the middle channel with it");
        Assert.True(stack.State.AmbientColor.Z == 1f, "and the ambient one too");

        // The stranger's green must not be anywhere in the stack's state.
        Assert.True(stack.State.DirIntensity != 1f, "a light naming another stack is ignored");
    }

    /// <summary>
    /// The three switching inputs, and the four that are keys as well.
    /// </summary>
    [Fact]
    public void Switching()
    {
        var registry = Registry();
        var handle = new EntityHandle(67);

        registry.Spawn(handle, EntityNames.EnvSmokestack);
        var stack = (EnvSmokeStack)registry.Get(handle)!;

        Assert.True(!stack.State.Emit, "a stack starts off");

        registry.Input(handle, "TurnOn", "", 0f, EntityHandle.None);
        Assert.True(stack.State.Emit, "TurnOn emits");

        registry.Input(handle, "Toggle", "", 0f, EntityHandle.None);
        Assert.True(!stack.State.Emit, "Toggle stops it");

        registry.Input(handle, "TurnOff", "", 0f, EntityHandle.None);
        Assert.True(!stack.State.Emit, "TurnOff leaves it stopped");

        registry.Input(handle, "Speed", "150", 150f, EntityHandle.None);
        Assert.True(stack.State.Speed == 150f, "the Speed input writes the networked field");

        registry.Input(handle, "JetLength", "300", 300f, EntityHandle.None);
        Assert.True(stack.State.JetLength == 300f, "and JetLength");

        registry.Input(handle, "SpreadSpeed", "44", 44f, EntityHandle.None);
        Assert.True(stack.State.SpreadSpeed == 44f, "and SpreadSpeed");

        // Rate reaches the wire like the other three and the CLIENT never
        // re-reads it: C_SmokeStack arms its spawn timer in Start and has no
        // SetSpawnRate. So this checks what the entity reports, which is all
        // stock's own Rate input ever changed either.
        registry.Input(handle, "Rate", "60", 60f, EntityHandle.None);
        Assert.True(stack.State.Rate == 60f, "and Rate, which the client reports and does not obey");
    }
}
