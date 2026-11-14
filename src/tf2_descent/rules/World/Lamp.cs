using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.World;

/// <summary>
/// The miner's lamp's light (lamp.json; plan_descent_mine_levels M9, D7, E5): a spot cone from
/// the hero's eye along his look. <see cref="Radius"/> is how far it reaches, the angles its
/// cone in degrees (full light inside <see cref="InnerAngle"/>, none past <see cref="OuterAngle"/>),
/// <see cref="Red"/>, <see cref="Green"/> and <see cref="Blue"/> its colour (0 to 255) with the
/// engine's shared <see cref="Exponent"/> (a power of two over the colour, what lets a light be
/// brighter than white), and <see cref="Forward"/> how far ahead of the eye it starts.
/// </summary>
public sealed record LampLight(double Radius, double InnerAngle, double OuterAngle, int Red, int Green, int Blue, int Exponent, double Forward);

/// <summary>
/// lamp.json: the light, the depth from which a hero's lamp is on when he arrives
/// (<see cref="OnFromDepth"/>: the town is depth 0), and the step the hero's look is sent in
/// (<see cref="PitchStep"/> degrees: every client points another hero's lamp by it).
/// </summary>
public sealed record LampSettings(LampLight Light, int OnFromDepth, double PitchStep);

/// <summary>
/// The lamp table. Load errors: a radius, cone, colour, exponent, offset, depth or pitch step out
/// of range, and an inner cone wider than the outer.
/// </summary>
public sealed class LampTable : SingleTable<LampSettings>
{
    public const string TableName = "lamp";

    public override string Name => TableName;

    protected override LampSettings Read(JsonFields f)
    {
        var l = f.Obj("light");
        var colour = l.Obj("colour");
        var light = new LampLight(l.Num("radius"), l.Num("innerAngle"), l.Num("outerAngle"),
            colour.Int("r"), colour.Int("g"), colour.Int("b"), l.Int("exponent"), l.Num("forward"));
        colour.End();
        l.End();
        return new LampSettings(light, f.Int("onFromDepth"), f.Num("pitchStep"));
    }

    protected override void Validate(DescentContent content, LampSettings s, ContentErrors errors)
    {
        string t = Name;
        var l = s.Light;
        ContentChecks.Range(errors, t, null, "light.radius", l.Radius, 16, 4096);
        ContentChecks.Range(errors, t, null, "light.innerAngle", l.InnerAngle, 0, 89);
        ContentChecks.Range(errors, t, null, "light.outerAngle", l.OuterAngle, 1, 89);
        if (l.InnerAngle > l.OuterAngle)
            errors.Add(t, null, ContentRule.OutOfRange, $"'light.innerAngle' {l.InnerAngle} is wider than 'light.outerAngle' {l.OuterAngle}");
        ContentChecks.Range(errors, t, null, "light.colour.r", l.Red, 0, 255);
        ContentChecks.Range(errors, t, null, "light.colour.g", l.Green, 0, 255);
        ContentChecks.Range(errors, t, null, "light.colour.b", l.Blue, 0, 255);
        ContentChecks.Range(errors, t, null, "light.exponent", l.Exponent, -4, 6);
        ContentChecks.Range(errors, t, null, "light.forward", l.Forward, 0, 64);
        ContentChecks.Range(errors, t, null, "onFromDepth", s.OnFromDepth, 0, 99);
        // 1.5 degrees is 120 steps: the most the wire's 7 bits hold.
        ContentChecks.Range(errors, t, null, "pitchStep", s.PitchStep, 1.5, 30);
    }
}

/// <summary>
/// The lamp's rules (pure): whether a hero's lamp is on when he arrives at a depth, and the
/// hero's look as the wire carries it. A pitch is held to [-90, 90] degrees and sent as a whole
/// number of steps above -90, so a hero who looks around changes the wire once a step.
/// </summary>
public static class LampRules
{
    /// <summary>The lowest pitch the wire carries: straight up, in the engine's sense (negative is up).</summary>
    public const double PitchFloor = -90;

    /// <summary>Whether a hero's lamp is lit when he arrives at <paramref name="depth"/> (the town is 0).</summary>
    public static bool OnByDefault(LampSettings s, int depth) => depth >= s.OnFromDepth;

    /// <summary>The wire's step count for <paramref name="pitch"/> degrees: held to [-90, 90], to the nearest step. A NaN is level.</summary>
    public static byte PitchSteps(LampSettings s, double pitch)
    {
        if (double.IsNaN(pitch))
            pitch = 0;
        double held = Math.Clamp(pitch, PitchFloor, -PitchFloor);
        return (byte)Math.Round((held - PitchFloor) / s.PitchStep, MidpointRounding.AwayFromZero);
    }

    /// <summary>The pitch in degrees a wire step count stands for.</summary>
    public static double PitchOf(LampSettings s, int steps) => Math.Clamp(PitchFloor + steps * s.PitchStep, PitchFloor, -PitchFloor);

    /// <summary>The most steps the wire carries: what its field's width must hold.</summary>
    public static int MaxSteps(LampSettings s) => (int)Math.Round(-2 * PitchFloor / s.PitchStep, MidpointRounding.AwayFromZero);
}
