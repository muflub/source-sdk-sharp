using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two entities from the effects batch whose behaviour is arithmetic rather
/// than a dispatch: the belt that carries things, and the gunfire that decides
/// what a burst looks like.
///
/// Both are tested at the level the engine is not needed for, which is where the
/// mistakes actually are. A conveyor that reads "standing on it" wrongly carries
/// somebody walking past below it; a spread that reshapes the group wrongly
/// gives an even spray where the map asked for aimed fire. Neither shows up as
/// an error anywhere — they show up as a level that feels wrong.
/// </summary>
public class EffectEntityTests
{
    private static readonly EntityHandle Ent = new(0x0031_0031u);

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;
    private static bool Near(Vector v, float x, float y, float z)
        => Near(v.X, x) && Near(v.Y, y) && Near(v.Z, z);

    private static (ScriptedEntityRegistry, T) SpawnWith<T>(string className,
        params (string Key, string Value)[] keys) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(FuncConveyor).Assembly);
        registry.Services = Ignoring("Entities.GetBoundsSize", "EntityPhysics.InitStaticPhysics");

        // Keys before spawn, which is the order Source uses.
        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);

        registry.Spawn(Ent, className);

        return (registry, (T)registry.Get(Ent)!);
    }

    [Fact]
    public void ConveyorKeys()
    {
        var (registry, belt) = SpawnWith<FuncConveyor>(EntityNames.FuncConveyor,
            ("movedir", "0 90 0"), ("speed", "200"));

        Assert.True(Near(belt.Direction, 0f, 1f, 0f), "a conveyor runs the way movedir points");
        Assert.True(Near(belt.Speed, 200f), "and at the speed the map asked for");

        // Reversing is the sign of the speed and nothing else, which is what
        // lets one direction vector serve both ways.
        registry.Input(Ent, "ToggleDirection", "", 0f, default);
        Assert.True(Near(belt.Speed, -200f), "ToggleDirection reverses it");

        registry.Input(Ent, "ToggleDirection", "", 0f, default);
        Assert.True(Near(belt.Speed, 200f), "and reverses it back");

        // The use key is the only way a map made before the input existed can
        // reverse one.
        registry.Input(Ent, "Use", "", 0f, default);
        Assert.True(Near(belt.Speed, -200f), "the use key reverses it too");

        registry.Input(Ent, "SetSpeed", "75", 75f, default);
        Assert.True(Near(belt.Speed, 75f), "SetSpeed replaces the speed outright");

        // A negative SetSpeed is a belt running backwards at that speed, not an
        // error to be clamped away.
        registry.Input(Ent, "SetSpeed", "-40", -40f, default);
        Assert.True(Near(belt.Speed, -40f), "and takes a negative as a direction");

        var (_, unset) = SpawnWith<FuncConveyor>(EntityNames.FuncConveyor, ("speed", "0"));
        Assert.True(Near(unset.Speed, 100f), "a belt with no speed keyed runs at 100");
    }

    /// <summary>
    /// Who a belt is carrying. The box below is 128 wide, 64 deep and 16 tall,
    /// centred at the origin, so its top face is at z = 8.
    /// </summary>
    [Fact]
    public void ConveyorRiders()
    {
        var centre = Vector.Zero;
        var half = new Vector(64f, 32f, 8f);

        // A player-sized hull standing squarely on it.
        static (Vector Mins, Vector Maxs) Hull(float x, float y, float feetZ)
            => (new Vector(x - 16f, y - 16f, feetZ), new Vector(x + 16f, y + 16f, feetZ + 72f));

        var on = Hull(0f, 0f, 8f);
        Assert.True(Riders.Riding(centre, half, on.Mins, on.Maxs), "somebody stood on the belt is carried");

        // The movement code decides you are supported by tracing two units down,
        // so anything it would call supported has to count here.
        var hovering = Hull(0f, 0f, 9.5f);
        Assert.True(Riders.Riding(centre, half, hovering.Mins, hovering.Maxs), "and still is a unit and a half above it");

        var jumping = Hull(0f, 0f, 24f);
        Assert.True(!Riders.Riding(centre, half, jumping.Mins, jumping.Maxs), "somebody in the air above it is not");

        // Standing UNDER a belt must not be a ride, which is the case a test
        // that only checked the footprint would pass by accident.
        var beneath = Hull(0f, 0f, -80f);
        Assert.True(!Riders.Riding(centre, half, beneath.Mins, beneath.Maxs), "somebody walking underneath it is not");

        // Half over the edge is still standing on it — a containment test would
        // drop somebody at the lip of the belt, which is exactly where being
        // thrown off matters.
        var edge = Hull(70f, 0f, 8f);
        Assert.True(Riders.Riding(centre, half, edge.Mins, edge.Maxs), "somebody half off the edge is still carried");

        var beside = Hull(100f, 0f, 8f);
        Assert.True(!Riders.Riding(centre, half, beside.Mins, beside.Maxs), "somebody stood next to it is not");

        // The box is located from its centre, so a belt somewhere else in the
        // map has to carry things somewhere else in the map.
        var elsewhere = new Vector(1000f, -500f, 200f);
        var thereOn = Hull(1000f, -500f, 208f);
        Assert.True(Riders.Riding(elsewhere, half, thereOn.Mins, thereOn.Maxs), "a belt away from the world origin carries what is on IT");
        Assert.True(!Riders.Riding(elsewhere, half, on.Mins, on.Maxs), "and not what is at the world origin");
    }

    /// <summary>
    /// The scatter, which is the part of env_gunfire that is a decision rather
    /// than a draw call.
    /// </summary>
    [Fact]
    public void GunfireSpread()
    {
        Assert.True(Near(EnvGunfire.Cone(5), 0.04362f), "a 5 degree spread is Source's 5 degree cone");
        Assert.True(Near(EnvGunfire.Cone(15), 0.13053f), "and 15 degrees its 15");

        // Stock's default branch, and it is a feature: a spread key of 0 is how
        // a map asks for rounds that go exactly where they are aimed.
        Assert.True(Near(EnvGunfire.Cone(7), 0f), "an unrecognised spread is no spread at all");
        Assert.True(Near(EnvGunfire.Cone(0), 0f), "and so is none");

        // Dead centre in, dead centre out, whatever the bias.
        var middle = EnvGunfire.Scatter(1f, -1f, 1f, (_, _) => 0f);
        Assert.True(Near(middle.X, 0f) && Near(middle.Y, 0f), "a round with no deflection goes to the middle");

        // At bias 0.5 the shot bias lands on zero, flatness with it, and the
        // whole draw comes from the second term. That is the flat disc — the
        // case a mapper picks when they want an even spray.
        var flat = EnvGunfire.Scatter(0.5f, -1f, 1f, Sequence(0f, 0.6f, 0f, -0.4f));
        Assert.True(Near(flat.X, 0.6f) && Near(flat.Y, -0.4f), "a flat group takes its offset from one draw");

        // A negative shot bias reflects each axis outward, which is what pushes
        // suppressing fire to the edge of the group. Reflected, (0.2, 0.1)
        // becomes (0.8, 0.9), which is outside the unit circle and rejected —
        // so the cap is what answers, and it answers the middle rather than
        // spinning on the engine's random stream.
        var outward = EnvGunfire.Scatter(0f, -1f, 1f, (_, _) => 0.2f);
        Assert.True(Near(outward.X, 0f) && Near(outward.Y, 0f), "an unresolvable outward group falls back to the middle");

        // Whatever it answers must be inside the circle, because the cone is
        // scaled by it: a value outside would put a round outside the spread the
        // map asked for.
        var random = new Random(20260816);
        bool inside = true;

        for (int i = 0; i < 200; i++)
        {
            float bias = i / 200f;
            var (x, y) = EnvGunfire.Scatter(bias, -1f, 1f,
                (lo, hi) => lo + (float)random.NextDouble() * (hi - lo));

            if (x * x + y * y > 1.0001f)
                inside = false;
        }

        Assert.True(inside, "every round lands inside the group it was given");

        // The bias is a proportion and the keyvalue is a mapper's number.
        var over = EnvGunfire.Scatter(50f, -1f, 1f, Sequence(0f, 0f, 0.5f, 0.5f));
        var atOne = EnvGunfire.Scatter(1f, -1f, 1f, Sequence(0f, 0f, 0.5f, 0.5f));
        Assert.True(Near(over.X, atOne.X) && Near(over.Y, atOne.Y), "a bias above one is the same as one");
    }

    [Fact]
    public void GunfireBursts()
    {
        Assert.True(EnvGunfire.BurstSize(2, 7, (lo, _) => lo) == 2, "the shortest burst is the minimum");

        // Just short of the top of the widened range, which is the value that
        // has to come out as the maximum rather than one over it.
        Assert.True(EnvGunfire.BurstSize(2, 7, (_, hi) => hi - 0.0001f) == 7, "the longest is the maximum");

        // Both ends have to be as likely as the middle, which is what the
        // widened range buys and what rounding would take away.
        Assert.True(EnvGunfire.BurstSize(3, 4, (_, _) => 0.9f) == 3 &&
              EnvGunfire.BurstSize(3, 4, (_, _) => 1.1f) == 4, "both ends are reachable at all");

        // A map with the two the wrong way round asks for something impossible;
        // a burst of at least one round is the useful reading of it.
        Assert.True(EnvGunfire.BurstSize(7, 2, (_, _) => 0f) == 7, "a reversed range still fires");
        Assert.True(EnvGunfire.BurstSize(0, 0, (_, _) => 0f) == 1, "and an empty one fires once");
    }

    /// <summary>
    /// A random source that hands out a known sequence and then zeroes, so a
    /// draw order can be asserted rather than a distribution.
    /// </summary>
    private static Func<float, float, float> Sequence(params float[] values)
    {
        int next = 0;
        return (_, _) => next < values.Length ? values[next++] : 0f;
    }
}
