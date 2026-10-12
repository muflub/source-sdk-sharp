using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The six classes in <c>smoke_trail.cpp</c>.
///
/// <para>
/// What is worth checking about them is not that a keyvalue reaches a field —
/// the vars machinery does that for every entity in the tree — but the DEFAULTS.
/// Every one of these six sets most of its networked state in a C++ CONSTRUCTOR
/// and not in <c>Spawn</c>, so a map that says nothing still gets a working
/// trail: ten particles a second, five seconds each, grey fading to black. A
/// managed field starts at zero instead, and a trail with a spawn rate of zero
/// emits nothing at all while reporting every field correctly through
/// <c>debug-api payload</c> — which is the silent failure this suite exists to
/// pin.
/// </para>
/// </summary>
public class TrailTests
{

    private static T Spawn<T>(string className, uint index, params (string Key, string Value)[] keys)
        where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvFireTrail).Assembly);

        // Two engine reads these checks are not about, named rather than
        // defaulted: the precache every trail does on the way in, and the clock
        // env_smoketrail asks for to turn a mapper's emittime DURATION into the
        // absolute time the field holds. The clock rule is checked directly
        // through StopEmitTimeFor, where the time is an argument.
        registry.Services = Ignoring("Precache.PrecacheMaterial", "Host.CurrentTime");

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return (T)registry.Get(handle)!;
    }

    /// <summary>
    /// env_fire_trail, whose whole networked state is two numbers a map cannot
    /// reach.
    ///
    /// <para>
    /// Both are <c>DEFINE_FIELD</c> on stock's datadesc rather than
    /// <c>DEFINE_KEYFIELD</c>, so the only thing that ever wrote them was
    /// <c>CFireTrail::FollowEntity</c> — called from the C++ creators in
    /// <c>hl2/</c>, none of which this build compiles. Zero is therefore what a
    /// map-placed one always has, and zero is the MEANINGFUL value for both:
    /// <c>C_FireTrail::Update</c> reads a lifetime of zero as "never stop", and
    /// an attachment of zero as the entity's own origin. That is what makes
    /// this class portable with no keys at all rather than broken with none.
    /// </para>
    /// </summary>
    [Fact]
    public void FireTrail()
    {
        var trail = Spawn<EnvFireTrail>(EntityNames.EnvFireTrail, 41);

        Assert.True(trail.State.Lifetime == 0f, "a fire trail never stops by default");
        Assert.True(trail.State.Attachment == 0, "and hangs off no attachment");
    }

    /// <summary>
    /// env_smoketrail, where the defaults are the whole of the server
    /// behaviour.
    /// </summary>
    [Fact]
    public void SmokeTrail()
    {
        // Thirteen fields set in stock's CONSTRUCTOR and not in Spawn. A map
        // that writes nothing must still get a working trail, and this is the
        // check that would have caught the silent version: every field a
        // plausible number, spawn rate not zero, and a trail that never stops.
        var bare = Spawn<EnvSmokeTrail>(EntityNames.EnvSmoketrail, 42);

        Assert.True(bare.State.SpawnRate == 10f, "a bare smoke trail emits at stock's ten a second");
        Assert.True(bare.State.ParticleLifetime == 5f, "and its puffs live five seconds");
        Assert.True(bare.State.Opacity == 0.5f, "and are half opaque");
        Assert.True(bare.State.MinSpeed == 2f && bare.State.MaxSpeed == 4f, "and drift between 2 and 4");
        Assert.True(bare.State.MaxDirectedSpeed == 0f, "and are given no directed speed");
        Assert.True(bare.State.StartSize == 35f && bare.State.EndSize == 55f, "and grow from 35 to 55");
        Assert.True(bare.State.SpawnRadius == 2f, "and appear within 2 units");
        Assert.True(bare.State.Emit, "and it emits");

        // 0.5 grey fading to black, which is stock's Init(0.5,0.5,0.5) and
        // Init(0,0,0) reached through the mapper's 0-255 units.
        Assert.True(bare.State.StartColor.X == 0.5f && bare.State.StartColor.Z == 0.5f, "a bare smoke trail starts mid grey in 0-1 space");

        Assert.True(bare.State.EndColor.X == 0f, "and fades to black");

        // The divide is the units bug this pins. A trail that networked 0-255
        // would be white and stay white, which reads as a lighting fault.
        var coloured = Spawn<EnvSmokeTrail>(EntityNames.EnvSmoketrail, 43,
            ("startcolor", "255 128 0"), ("endcolor", "0 0 255"));

        Assert.True(coloured.State.StartColor.X == 1f, "a mapper's 0-255 colour arrives in 0-1 space");
        Assert.True(coloured.State.StartColor.Y == 128f / 255f, "and the middle channel with it");
        Assert.True(coloured.State.EndColor.Z == 1f, "and the end colour too");

        // The emittime rule. Stock's key is a DURATION and the field is an
        // ABSOLUTE time; zero in the field means never stop. So an absent key
        // must NOT become "now", which would stop the trail the instant it
        // started — and an explicit zero must, because that is what stock's
        // curtime + 0 does.
        Assert.True(EnvSmokeTrail.StopEmitTimeFor(emitTime: -1f, now: 40f) == 0f, "no emittime means the trail never stops");

        Assert.True(EnvSmokeTrail.StopEmitTimeFor(emitTime: 12f, now: 40f) == 52f, "an emittime is added to the clock");

        Assert.True(EnvSmokeTrail.StopEmitTimeFor(emitTime: 0f, now: 40f) == 40f, "an explicit zero stops it at once, as stock's curtime + 0 does");
    }

    /// <summary>
    /// env_dusttrail, which is the same entity with one colour instead of two.
    ///
    /// <para>
    /// Checked separately rather than assumed identical, because the two are
    /// separate classes in stock with separate constructors holding the same
    /// numbers — which is exactly the shape that drifts. And because the KEY
    /// differs: dust reads "color" where smoke reads "startcolor", and a class
    /// that copied its twin's key would take the mapper's colour on neither.
    /// </para>
    /// </summary>
    [Fact]
    public void DustTrail()
    {
        var bare = Spawn<EnvDustTrail>(EntityNames.EnvDusttrail, 44);

        Assert.True(bare.State.SpawnRate == 10f, "a bare dust trail emits at stock's ten a second");
        Assert.True(bare.State.ParticleLifetime == 5f, "and its puffs live five seconds");
        Assert.True(bare.State.StartSize == 35f && bare.State.EndSize == 55f, "and grow from 35 to 55");
        Assert.True(bare.State.Emit, "and it emits");

        Assert.True(bare.State.Color.X == 0.5f && bare.State.Color.Z == 0.5f, "a bare dust trail is mid grey in 0-1 space");

        // "color", not "startcolor" — stock's DustTrail::KeyValue answers that
        // name and no other, and a class that took its twin's key would leave
        // every dust trail grey.
        var coloured = Spawn<EnvDustTrail>(EntityNames.EnvDusttrail, 45,
            ("color", "255 0 128"));

        Assert.True(coloured.State.Color.X == 1f && coloured.State.Color.Y == 0f, "a dust trail reads the 'color' key, not 'startcolor'");
    }

    /// <summary>
    /// env_rockettrail, which has no map keys at all.
    ///
    /// <para>
    /// <c>RocketTrail</c>'s datadesc is fifteen <c>DEFINE_FIELD</c>s and not one
    /// <c>DEFINE_KEYFIELD</c>, and the class has no <c>KeyValue</c> override
    /// either. So the constructor IS the entity, and this is the only check
    /// that says the constructor came across — there is no key a map could set
    /// to notice it had not.
    /// </para>
    /// </summary>
    [Fact]
    public void RocketTrail()
    {
        var trail = Spawn<EnvRocketTrail>(EntityNames.EnvRockettrail, 46);

        Assert.True(trail.State.SpawnRate == 10f, "a rocket trail emits at stock's ten a second");
        Assert.True(trail.State.ParticleLifetime == 5f, "and its puffs live five seconds");
        Assert.True(trail.State.Opacity == 0.5f, "and are half opaque");
        Assert.True(trail.State.MinSpeed == 2f && trail.State.MaxSpeed == 4f, "and drift between 2 and 4");
        Assert.True(trail.State.StartSize == 35f && trail.State.EndSize == 55f, "and grow from 35 to 55");
        Assert.True(trail.State.SpawnRadius == 2f, "and appear within 2 units");
        Assert.True(trail.State.Emit, "and it emits");
        Assert.True(trail.State.StopEmitTime == 0f, "and never stops");
        Assert.True(trail.State.StartColor.X == 0.5f, "a rocket trail starts mid grey");
        Assert.True(trail.State.EndColor.X == 0f, "and fades to black");

        // Stock's flare is 1.5, and the client randomises around it by half a
        // unit either way — so a zero here would be a flare that never appears.
        Assert.True(trail.State.FlareScale == 1.5f, "the flare is stock's 1.5");

        Assert.True(!trail.State.Damaged, "and it is not a shot-down rocket");

        // MINUS ONE, alone among the four trails that carry this field.
        // C_RocketTrail's constructor sets -1 where its neighbours set 0, and
        // GetAimEntOrigin only consults an attachment above zero — so both mean
        // "no attachment" and this keeps stock's value rather than tidying it.
        Assert.True(trail.State.Attachment == -1, "a rocket trail's attachment is stock's -1, not zero");
    }

    /// <summary>
    /// env_sporetrail, which is inert as shipped.
    ///
    /// <para>
    /// The check that matters here is the one that looks wrong: a map-placed
    /// spore trail does NOT emit. <c>SporeTrail::SporeTrail</c> sets six fields
    /// and does not touch <c>m_bEmit</c>, entity memory arrives zeroed, and the
    /// class has no key, no input and no <c>Spawn</c> — so nothing a map can do
    /// will raise it. Writing it down as a check is what stops a later reader
    /// "fixing" it into something stock never was.
    /// </para>
    /// </summary>
    [Fact]
    public void SporeTrail()
    {
        var trail = Spawn<EnvSporeTrail>(EntityNames.EnvSporetrail, 47);

        Assert.True(!trail.State.Emit, "a map-placed spore trail does not emit, as in stock");

        Assert.True(trail.State.SpawnRate == 100f, "its declared rate is stock's 100 a second");
        Assert.True(trail.State.ParticleLifetime == 1f, "its spores would live a second");
        Assert.True(trail.State.StartSize == 1f, "and start at one unit");
        Assert.True(trail.State.EndSize == 0f, "and end at nothing");
        Assert.True(trail.State.SpawnRadius == 16f, "within sixteen units of the entity");
        Assert.True(trail.State.EndColor.X == 0f, "and its end colour is black");
    }

    /// <summary>
    /// env_sporeexplosion, the only one of the six a map can drive.
    ///
    /// <para>
    /// And the evidence that <see cref="EnvSporeTrail"/>'s inert emit flag is a
    /// stock oversight rather than a decision: these two classes are five
    /// fields apart in one file, and this one's constructor sets the flag and
    /// its <c>Spawn</c> then clears it. Somebody thought about this one.
    /// </para>
    /// </summary>
    [Fact]
    public void SporeExplosion()
    {
        var burst = Spawn<EnvSporeExplosion>(EntityNames.EnvSporeexplosion, 48,
            ("spawnrate", "250"));

        // Not emitting however the map placed it: stock's constructor raises the
        // flag and Spawn lowers it again, so an explosion always waits.
        Assert.True(!burst.State.Emit, "a spore explosion starts not emitting");
        Assert.True(!burst.State.DontRemove, "and is not yet map-driven");
        Assert.True(burst.State.SpawnRate == 250f, "it keeps the mapper's rate");
        Assert.True(burst.State.ParticleLifetime == 1f && burst.State.StartSize == 1f && burst.State.EndSize == 0f && burst.State.SpawnRadius == 16f, "and stock's four constructor numbers");

        var bare = Spawn<EnvSporeExplosion>(EntityNames.EnvSporeexplosion, 49);
        Assert.True(bare.State.SpawnRate == 100f, "with no rate key it is stock's 100 a second");

        // The two inputs. Both raise DontRemove, INCLUDING Disable — an
        // explosion a map can switch back on must not have been collected by
        // the client in between, and a Disable that lowered the flag would
        // undo exactly that.
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvSporeExplosion).Assembly);
        registry.Services = Ignoring("Precache.PrecacheMaterial", "Host.CurrentTime");

        var handle = new EntityHandle(50);
        registry.Spawn(handle, EntityNames.EnvSporeexplosion);
        var driven = (EnvSporeExplosion)registry.Get(handle)!;

        registry.Input(handle, "Enable", "", 0f, EntityHandle.None);
        Assert.True(driven.State.Emit, "Enable starts the spores");
        Assert.True(driven.State.DontRemove, "and marks it map-driven");
        Assert.True(!driven.Disabled, "and clears the disabled flag");

        registry.Input(handle, "Disable", "", 0f, EntityHandle.None);
        Assert.True(!driven.State.Emit, "Disable stops them");
        Assert.True(driven.Disabled, "and sets the disabled flag");
        Assert.True(driven.State.DontRemove, "and LEAVES it map-driven, so the client does not collect it");
    }
}
