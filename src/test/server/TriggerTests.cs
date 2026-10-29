using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The trigger volumes, and the arithmetic they rest on.
///
/// The maths is worth testing on its own: a push that shoves the wrong way is
/// the kind of bug that looks like a physics problem and is actually a sign
/// error in an angle conversion.
/// </summary>
public class TriggerTests
{
    private static readonly EntityHandle Ent = new(0x0011_0011u);
    private static readonly EntityHandle Player = new(0x0012_0012u);
    private static readonly EntityHandle Other = new(0x0013_0013u);

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;
    private static bool Near(Vector v, float x, float y, float z)
        => Near(v.X, x) && Near(v.Y, y) && Near(v.Z, z);

    [Fact]
    public void VectorMath()
    {
        var a = new Vector(1f, 2f, 3f);
        var b = new Vector(10f, 20f, 30f);

        Assert.True(Near(a + b, 11f, 22f, 33f), "vectors add");
        Assert.True(Near(b - a, 9f, 18f, 27f), "vectors subtract");
        Assert.True(Near(a * 2f, 2f, 4f, 6f), "vectors scale");
        Assert.True(Near(2f * a, 2f, 4f, 6f), "scaling commutes");
        Assert.True(Near(a.Dot(b), 140f), "dot product");
        Assert.True(Near(new Vector(3f, 4f, 0f).Length, 5f), "length");
        Assert.True(Near(new Vector(0f, 0f, 7f).Normalized, 0f, 0f, 1f), "normalizing gives unit length");

        // A zero vector has no direction, and dividing by its length would make
        // every component NaN — which then spreads silently through whatever
        // used it.
        Assert.True(Near(Vector.Zero.Normalized, 0f, 0f, 0f), "normalizing zero stays zero");
    }

    [Fact]
    public void AngleConversion()
    {
        Assert.True(Near(Angles.Forward(Vector.Zero), 1f, 0f, 0f), "no rotation faces +X");
        Assert.True(Near(Angles.Forward(new Vector(0f, 90f, 0f)), 0f, 1f, 0f), "yaw 90 faces +Y");
        Assert.True(Near(Angles.Forward(new Vector(0f, 180f, 0f)), -1f, 0f, 0f), "yaw 180 faces -X");

        // Pitch is inverted in Source: a POSITIVE pitch looks down. Getting this
        // backwards is what turns a jump pad into a floor slam.
        Assert.True(Near(Angles.Forward(new Vector(-90f, 0f, 0f)), 0f, 0f, 1f), "pitch -90 faces up");
        Assert.True(Near(Angles.Forward(new Vector(90f, 0f, 0f)), 0f, 0f, -1f), "pitch 90 faces down");

        var basis = Angles.Vectors(Vector.Zero);
        Assert.True(Near(basis.Right, 0f, -1f, 0f), "right is -Y at rest");
        Assert.True(Near(basis.Up, 0f, 0f, 1f), "up is +Z at rest");

        // Map keys are text, and a malformed one must not take the whole entity
        // down with it.
        Assert.True(Near(TriggerPush.ParseVector("-90 0 0"), -90f, 0f, 0f), "map vectors parse");
        Assert.True(Near(TriggerPush.ParseVector("5"), 5f, 0f, 0f), "short map vectors fill with zero");
        Assert.True(Near(TriggerPush.ParseVector("up a bit"), 0f, 0f, 0f), "nonsense map vectors read as zero");
    }

    /// <summary>
    /// Without the engine the clock reads zero forever, so the refire delay would
    /// gate every touch after the first.
    /// </summary>
    private static (ScriptedEntityRegistry, T) Spawn<T>(string className, Func<float>? clock = null) where T : ScriptedEntity
        => SpawnWith<T>(className, clock, ("wait", "0"));

    private static (ScriptedEntityRegistry, T) SpawnWith<T>(string className,
        params (string Key, string Value)[] keys) where T : ScriptedEntity
        => SpawnWith<T>(className, null, keys);

    private static (ScriptedEntityRegistry, T) SpawnWith<T>(string className, Func<float>? clock,
        params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerMultiple).Assembly);
        // MotionControllers.Create: trigger_vphysics_motion makes its medium at
        // spawn, through the container's area since plan_abi_hotreload D8. The
        // recorded engine answers id 0, a refused medium -- what the static
        // engine answered here before, with no engine behind it.
        var services = Services(out var engine);
        if (clock is null)
            engine.Ignores("Clock.Now");
        else
            engine.ReturnsEachCall("Clock.Now", () => clock());
        engine.Ignores("Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType",
                       "MotionControllers.Create", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles",
                       "Entities.GetSolidFlags", "Entities.GetSolidType", "Entities.IsPlayer");

        // Whoever touches is a live entity: CBaseTrigger::EndTouch cleans the
        // gone out of its list before it decides whether the volume emptied.
        engine.Returns("Entities.IsValid", true);
        // trigger_vphysics_motion reaches a player's managed flags through it.
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;

        // Keys BEFORE spawn, which is the order Source uses: an entity is
        // handed every keyvalue in the map and only then spawned.
        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);

        registry.Spawn(Ent, className);

        return (registry, (T)registry.Get(Ent)!);
    }

    [Fact]
    public void VolumeMembership()
    {
        // Stepped past trigger_multiple's wait (0 is 0.2 s) before each entry
        // that has to fire: its wait gates a re-entry as much as a stay.
        float now = 0f;
        var (registry, trigger) = Spawn<TriggerMultiple>(EntityNames.TriggerMultiple, () => now);

        registry.Touch(Ent, Player);
        Assert.True(trigger.OnTrigger.FireCount == 1, "entering fires the trigger");
        Assert.True(trigger.OnStartTouch.FireCount == 1, "and reports the entry");

        // The engine reports entering once, but a re-entry must count again —
        // which is why membership is tracked rather than a first-time flag.
        registry.Touch(Ent, Player);
        Assert.True(trigger.OnTrigger.FireCount == 1, "already being inside fires nothing more");

        registry.EndTouch(Ent, Player);
        Assert.True(trigger.OnLeft.FireCount == 1, "leaving is reported");
        Assert.True(trigger.OnEndTouchAll.FireCount == 1, "the last one out empties it");

        now = 1f;
        registry.Touch(Ent, Player);
        Assert.True(trigger.OnTrigger.FireCount == 2, "re-entering fires again");

        // OnEndTouchAll is not OnEndTouch: with two people inside, one leaving
        // is not the volume emptying.
        registry.Touch(Ent, Other);
        registry.EndTouch(Ent, Player);
        Assert.True(trigger.OnEndTouchAll.FireCount == 1, "one of two leaving does not empty it");
        Assert.True(trigger.OnLeft.FireCount == 2, "but is still reported as leaving");

        registry.EndTouch(Ent, Other);
        Assert.True(trigger.OnEndTouchAll.FireCount == 2, "the second one out empties it");

        // Leaving something never entered must not fire anything.
        registry.EndTouch(Ent, Other);
        Assert.True(trigger.OnLeft.FireCount == 3, "leaving twice reports once");
    }

    [Fact]
    public void OnceAndDisabled()
    {
        var (registry, once) = Spawn<TriggerOnce>(EntityNames.TriggerOnce);

        registry.Touch(Ent, Player);
        registry.EndTouch(Ent, Player);
        registry.Touch(Ent, Player);
        Assert.True(once.OnTrigger.FireCount == 1, "trigger_once fires once and stays fired");

        var (registry2, trigger) = Spawn<TriggerMultiple>(EntityNames.TriggerMultiple);

        registry2.Input(Ent, "Disable", "", 0f, EntityHandle.None);
        registry2.Touch(Ent, Player);
        Assert.True(trigger.OnTrigger.FireCount == 0, "a disabled trigger fires nothing");

        registry2.Input(Ent, "Enable", "", 0f, EntityHandle.None);
        registry2.Touch(Ent, Player);
        Assert.True(trigger.OnTrigger.FireCount == 1, "enabling lets it fire");

        registry2.Input(Ent, "Toggle", "", 0f, EntityHandle.None);
        registry2.EndTouch(Ent, Player);
        registry2.Touch(Ent, Player);
        Assert.True(trigger.OnTrigger.FireCount == 1, "toggling turns it back off");

        // StartDisabled is read at spawn.
        var registry3 = new ScriptedEntityRegistry(TestEngines.Server());
        registry3.RegisterAll(typeof(TriggerMultiple).Assembly);
        registry3.Services = Ignoring("Clock.Now", "Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid",
                                      "Entities.GetSolidFlags", "Entities.GetSolidType", "Entities.IsPlayer");
        registry3.KeyValue(Ent, EntityNames.TriggerMultiple, "StartDisabled", "1");
        registry3.KeyValue(Ent, EntityNames.TriggerMultiple, "wait", "0");
        registry3.Spawn(Ent, EntityNames.TriggerMultiple);

        var offAtStart = (TriggerMultiple)registry3.Get(Ent)!;

        registry3.Touch(Ent, Player);
        Assert.True(offAtStart.OnTrigger.FireCount == 0, "StartDisabled holds from spawn");
    }

    // A trigger whose solidity reads back what it was last set to: a brush's
    // SetBrushSolidity or an explicit SetSolidity, whichever came last.
    private static (ScriptedEntityRegistry, RecordingEngine) Solid(string className, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerMultiple).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.ReturnsEachCall("Entities.GetSolidFlags", () => FlagsOf(engine))
              .ReturnsEachCall("Entities.GetSolidType", () => engine.Last("Entities.SetSolidity")?.ArgAs<SolidType>(1) ?? SolidType.Bsp);
        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);
        registry.Spawn(Ent, className);
        return (registry, engine);
    }

    private static SolidFlags FlagsOf(RecordingEngine engine)
    {
        for (int i = engine.Calls.Count - 1; i >= 0; i--)
        {
            var call = engine.Calls[i];
            if (call.Member == "Entities.SetSolidity")
                return call.ArgAs<SolidFlags>(2);
            if (call.Member == "EntityPhysics.SetBrushSolidity")
                return call.ArgAs<BrushSolidity>(1) switch
                {
                    BrushSolidity.Trigger => SolidFlags.NotSolid | SolidFlags.Trigger,
                    BrushSolidity.None => SolidFlags.NotSolid,
                    _ => SolidFlags.None,
                };
        }

        return SolidFlags.None;
    }

    /// <summary>
    /// CBaseTrigger::Disable and Enable (triggers.cpp): FSOLID_TRIGGER goes
    /// with being on, so a disabled volume leaves the engine's trigger walk
    /// (whoever is inside is told they left, and told they arrived when it
    /// comes back on). Found beside stock (dm_lockdown solidity census,
    /// 2026-10-27): fan_wind and fan_gib_trigger, both disabled, were 0xc
    /// here and 0x4 in stock.
    /// </summary>
    [Fact]
    public void A_disabled_trigger_is_no_trigger_until_it_is_enabled()
    {
        var (registry, engine) = Solid(EntityNames.TriggerMultiple);
        Assert.Equal(SolidFlags.NotSolid | SolidFlags.Trigger, FlagsOf(engine));

        registry.Input(Ent, "Disable", "", 0f, EntityHandle.None);
        Assert.Equal(SolidFlags.NotSolid, FlagsOf(engine));
        Assert.Equal(SolidType.Bsp, engine.Last("Entities.SetSolidity")!.Value.ArgAs<SolidType>(1));
        Assert.Single(engine.AllOf("Entities.SetSolidity"));

        registry.Input(Ent, "Enable", "", 0f, EntityHandle.None);
        Assert.Equal(SolidFlags.NotSolid | SolidFlags.Trigger, FlagsOf(engine));
    }

    /// <summary>InitTrigger: one keyed StartDisabled spawns without FSOLID_TRIGGER.</summary>
    [Fact]
    public void A_trigger_that_starts_disabled_spawns_as_no_trigger()
    {
        var (_, engine) = Solid(EntityNames.TriggerPush, ("StartDisabled", "1"));

        Assert.Equal(SolidFlags.NotSolid, FlagsOf(engine));
    }

    /// <summary>CTriggerBrush's Disable is FL_DONTTOUCH: it stays a trigger.</summary>
    [Fact]
    public void A_disabled_trigger_brush_stays_a_trigger()
    {
        var (registry, engine) = Solid(EntityNames.TriggerBrush);

        registry.Input(Ent, "Disable", "", 0f, EntityHandle.None);

        Assert.Equal(SolidFlags.Trigger, FlagsOf(engine));
    }

    /// <summary>
    /// The bare "trigger" is a base class with a map name and no behaviour, and
    /// the managed one has to be just as inert: what it guards against is the
    /// brush shell's default, a solid visible wall where a mapper drew a volume.
    /// </summary>
    [Fact]
    public void InertTrigger()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Trigger).Assembly);
        registry.Services = Ignoring("Clock.Now", "Host.RandomFloat", "Networking.EntityGetState", "Networking.EntityGetState.moveType", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "Entities.IsValid");
        registry.Spawn(Ent, EntityNames.Trigger);

        Assert.True(registry.Get(Ent) is Trigger, "the bare trigger spawns");
        Assert.True(registry.Touch(Ent, Player), "and swallows a touch without doing anything");
    }

    [Fact]
    public void BrushIgnoreList()
    {
        var (registry, brush) = Spawn<TriggerBrush>(EntityNames.TriggerBrush);

        registry.Touch(Ent, Player);
        Assert.True(brush.OnStartTouch.FireCount == 1, "trigger_brush reports being entered");

        registry.Input(Ent, "Use", "", 0f, Player);
        Assert.True(brush.OnUse.FireCount == 1, "and reports being used");

        // Touching and being used are separately ignorable, which is the whole
        // reason this entity carries a bitfield instead of naming a filter.
        var (silent, ignoresTouch) = SpawnWith<TriggerBrush>(EntityNames.TriggerBrush,
                                                            ("InputFilter", "8"));

        silent.Touch(Ent, Player);
        Assert.True(ignoresTouch.OnStartTouch.FireCount == 0, "an ignored touch reports nothing");

        silent.Input(Ent, "Use", "", 0f, Player);
        Assert.True(ignoresTouch.OnUse.FireCount == 1, "and does not take being used with it");

        var (deaf, ignoresUse) = SpawnWith<TriggerBrush>(EntityNames.TriggerBrush,
                                                        ("InputFilter", "16"));

        deaf.Input(Ent, "Use", "", 0f, Player);
        Assert.True(ignoresUse.OnUse.FireCount == 0, "an ignored use reports nothing");

        deaf.Touch(Ent, Player);
        Assert.True(ignoresUse.OnStartTouch.FireCount == 1, "and leaves touching alone");
    }

    [Fact]
    public void ImpactOnCommand()
    {
        var (registry, impact) = SpawnWith<TriggerImpact>(EntityNames.TriggerImpact,
                                                         ("Magnitude", "200"));

        registry.Touch(Ent, Player);
        Assert.True(impact.ImpactForce.FireCount == 0, "walking into an impact sets nothing off");

        registry.Input(Ent, "Impact", "", 0f, EntityHandle.None);
        Assert.True(impact.ImpactForce.FireCount == 1, "the input does");

        // Three numbers and no brackets, and no signed zero in the flat
        // components: the engine hands an output's value on as a string and
        // converts it at the far end, so what is written here is what a mapper
        // wiring this into something else has to read.
        Assert.True(impact.ImpactForce.LastValue == "200 0 0", "and reports the force it applied");

        registry.Input(Ent, "SetMagnitude", "-50", -50f, EntityHandle.None);
        registry.Input(Ent, "Impact", "", 0f, EntityHandle.None);
        Assert.True(impact.ImpactForce.LastValue == "-50 0 0", "a negative magnitude reverses it");
    }

    /// <summary>
    /// CTriggerImpact: always starts disabled (no FSOLID_TRIGGER); Impact turns
    /// it on for a tenth of a second, and what starts touching it then is
    /// thrown, filter or none. Found beside stock (ss_sandbox solidity census,
    /// 2026-10-27): ours was always on, 0xc where stock's is 0x4.
    /// </summary>
    [Fact]
    public void An_impact_is_on_only_for_its_moment_and_throws_what_touches_it_then()
    {
        var (registry, engine) = Solid(EntityNames.TriggerImpact, ("Magnitude", "200"));
        engine.Returns("EntityPhysics.PhysicsObject", (nint)0x1234);
        var impact = (TriggerImpact)registry.Get(Ent)!;
        Assert.Equal(SolidFlags.NotSolid, FlagsOf(engine));

        impact.OnTouch(Other);
        Assert.False(engine.Called("EntityPhysics.ApplyForceCenter"));

        registry.Input(Ent, "Impact", "", 0f, EntityHandle.None);
        Assert.Equal(SolidFlags.NotSolid | SolidFlags.Trigger, FlagsOf(engine));
        impact.OnTouch(Other);
        Assert.Equal(new Vector(200f, 0f, 0f), engine.Last("EntityPhysics.ApplyForceCenter")!.Value.ArgAs<Vector>(1));

        impact.OnThink("ImpactOff");
        Assert.Equal(SolidFlags.NotSolid, FlagsOf(engine));
    }

    /// <summary>
    /// The medium a trigger_vphysics_motion carries.
    ///
    /// There is no physics here to push, so what is checked is the thing C#
    /// actually owns: the nine numbers, where they come from, and that an input
    /// changes the one it names and leaves the other eight alone. A retune that
    /// quietly reset a neighbour is the bug this shape of API exists to prevent,
    /// and it stays invisible in a running game until a mapper wonders why
    /// setting the gravity turned their current off.
    /// </summary>
    [Fact]
    public void MediumRetuning()
    {
        var (registry, field) = SpawnWith<TriggerVPhysicsMotion>(EntityNames.TriggerVphysicsMotion,
            ("SetGravityScale", "0.25"), ("SetVelocityLimit", "300"),
            ("SetLinearForce", "500"), ("SetLinearForceAngles", "-90 0 0"));

        Assert.True(Near(field.Medium.Gravity, 0.25f), "map keys reach the medium");
        Assert.True(Near(field.Medium.Limit, 300f), "and so do the ones named after inputs");
        Assert.True(Near(field.Medium.Angles, -90f, 0f, 0f), "and the vector one");

        // The neutrals matter as much as the numbers: a scale defaulting to zero
        // rather than to one would stop everything in the volume dead.
        Assert.True(Near(field.Medium.Scale, 1f), "an unset scale does not scale");
        Assert.True(Near(field.Medium.AngScale, 1f), "an unset angular scale does not either");
        Assert.True(Near(field.Medium.AngLimit, 0f), "an unset limit does not limit");

        registry.Input(Ent, "SetGravityScale", "2", 2f, EntityHandle.None);

        Assert.True(Near(field.Medium.Gravity, 2f), "an input retunes what it names");
        Assert.True(Near(field.Medium.Limit, 300f)
                                                     && Near(field.Medium.Force, 500f), "and leaves the rest of the medium alone");

        // Stock's SetVelocityLimitTime carries both numbers in one string and
        // records the destination as the limit, so a later retune aims at where
        // the ramp is going rather than dragging it back to where it started.
        registry.Input(Ent, "SetVelocityLimitTime", "800 3", 0f, EntityHandle.None);
        Assert.True(Near(field.Medium.Limit, 800f), "a ramp records its destination");

        registry.Input(Ent, "SetAdditionalAirDensity", "5", 5f, EntityHandle.None);
        Assert.True(Near(field.Medium.Limit, 800f), "and a later retune keeps it");
        Assert.True(Near(field.Medium.Air, 5f), "while still applying itself");

        // The one line of this class that has nothing to do with the nine
        // numbers, and the one whose absence is invisible from here: the
        // registry hands a class only player touches unless it says otherwise,
        // and a medium exists to act on the things the solver moves. Without
        // this the solver reported a crate entering the volume and the crate
        // never reached the class — measured, and it read as a well that does
        // not slow anything.
        Assert.True(field.TouchedByAnything, "a medium hears about crates and not only players");
    }

    /// <summary>
    /// trigger_playermovement is two spawnflags and nothing else, so there is no
    /// state of its own to check — only that the volume behaves like one.
    ///
    /// The flags themselves are read from the entity through the engine, and
    /// there is no engine here to answer, which is also why no toucher is a
    /// player: what these prove is that a volume with nothing to do still tracks
    /// who is in it and takes every touch without objecting.
    /// </summary>
    [Fact]
    public void PlayerMovementFlags()
    {
        var (registry, plain) = Spawn<TriggerPlayerMovement>(EntityNames.TriggerPlayermovement);

        registry.Touch(Ent, Player);
        Assert.True(plain.OnStartTouch.FireCount == 1, "trigger_playermovement reports being entered");

        registry.EndTouch(Ent, Player);
        Assert.True(plain.OnLeft.FireCount == 1, "and being left");

        // A non-player is dropped before either flag is looked at, which is what
        // stops a crate rolling through from being handed a duck button.
        registry.Touch(Ent, Other);
        Assert.True(plain.OnStartTouch.FireCount == 2, "and a non-player passes through it harmlessly");
    }
}
