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
/// How the dark and the lamp change how far a robot sees a hero (lamp.json's <c>sight</c>;
/// plan_tf2_rpg B.3: "darkness [is a] multiplier on range"; mine plan M9 and section 6).
/// <see cref="Darkness"/> is each depth's darkness from 0 (lit: the lamp changes nothing) to 1
/// (black), depth 1 first; a deeper level takes the last. The levels carry no light the server
/// can read, so the depth stands for the room (mine plan 6: lit to level 10, "side rooms may be
/// fully dark from level 11 down", level 15 the darkest). In the dark a hero is seen from
/// <see cref="LampOn"/> times a robot's range with his lamp lit and <see cref="LampOff"/> times
/// it with it out, each eased in by the darkness (<see cref="LampRules.SightScale"/>).
/// </summary>
public sealed record LampSight(IReadOnlyList<double> Darkness, double LampOn, double LampOff);

/// <summary>
/// lamp.json: the light, the depth from which a hero's lamp is on when he arrives
/// (<see cref="OnFromDepth"/>: the town is depth 0), the step the hero's look is sent in
/// (<see cref="PitchStep"/> degrees: every client points another hero's lamp by it), and what the
/// dark and the lamp do to a robot's sight of him (<see cref="Sight"/>).
/// </summary>
public sealed record LampSettings(LampLight Light, int OnFromDepth, double PitchStep, LampSight Sight)
{
    /// <summary>
    /// The lamp's range as a hero stat (the user, 2026-11-13: "brightness increase and range
    /// increase from skills and items"): its stat's total is a percent added to
    /// <see cref="LampLight.Radius"/>, held between the floor (<see cref="ClampedStat.Min"/>) and
    /// the ceiling (<see cref="ClampedStat.Max"/>). A hero with none of it has lamp.json's radius.
    /// </summary>
    public ClampedStat Range { get; init; } = new("", 0, 0, 0);

    /// <summary>
    /// The lamp's brightness as a hero stat: its stat's total is a percent added to the light's
    /// intensity (its colour times two to its exponent), held between the floor and the ceiling.
    /// </summary>
    public ClampedStat Brightness { get; init; } = new("", 0, 0, 0);
}

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
        var sight = f.Obj("sight");
        var darkness = new List<double>();
        foreach (var e in sight.Array("darkness"))
        {
            if (e.ValueKind == System.Text.Json.JsonValueKind.Number)
                darkness.Add(e.GetDouble());
            else
                f.Errors.Add(f.Table, f.Row, ContentRule.Malformed, "'sight.darkness' must hold only numbers");
        }

        var lampSight = new LampSight(darkness, sight.Num("lampOn"), sight.Num("lampOff"));
        sight.End();
        return new LampSettings(light, f.Int("onFromDepth"), f.Num("pitchStep"), lampSight)
        {
            Range = Bonus(f.Obj("range")),
            Brightness = Bonus(f.Obj("brightness")),
        };
    }

    /// <summary>A lamp stat: the stat whose total is the bonus in percent, its floor and its ceiling.</summary>
    private static ClampedStat Bonus(JsonFields f)
    {
        var c = new ClampedStat(f.Str("stat"), 0, f.Num("min"), f.Num("max"));
        f.End();
        return c;
    }

    /// <summary>The most a lamp's brightness may be raised, in percent: 100 + this fits the wire's nine bits.</summary>
    public const double BrightnessCeilingMost = 300;

    /// <summary>The most a lamp's brightness or range may be lowered, in percent: never to nothing.</summary>
    public const double FloorLeast = -90;

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
        if (s.Sight.Darkness.Count == 0)
            errors.Add(t, null, ContentRule.OutOfRange, "'sight.darkness' names no depth");
        for (int d = 0; d < s.Sight.Darkness.Count; d++)
            ContentChecks.Range(errors, t, null, $"sight.darkness[{d}]", s.Sight.Darkness[d], 0, 1);
        ContentChecks.Range(errors, t, null, "sight.lampOn", s.Sight.LampOn, 0.05, 4);
        ContentChecks.Range(errors, t, null, "sight.lampOff", s.Sight.LampOff, 0.05, 4);

        // The lamp's stats: a bare hero has lamp.json's lamp (0 lies between the floor and the
        // ceiling), and no total can make a lamp that lights the whole level or none: the radius
        // they allow stays within the light's own range, the brightness above a tenth and within
        // what the wire carries.
        // A radius out of its own range has its own error: the ceiling then is not judged by it.
        double radiusCeiling = l.Radius is >= 16 and <= 4096 ? Math.Floor((4096 / l.Radius - 1) * 100) : double.MaxValue;
        foreach (var (field, c, ceiling) in new[] { ("range", s.Range, radiusCeiling), ("brightness", s.Brightness, BrightnessCeilingMost) })
        {
            ContentChecks.Stat(content, errors, t, null, c.Stat);
            ContentChecks.Range(errors, t, null, field + ".min", c.Min, FloorLeast, 0);
            ContentChecks.Range(errors, t, null, field + ".max", c.Max, 0, ceiling);
        }
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

    /// <summary>The darkness of <paramref name="depth"/> (0 lit to 1 black): the town (0 and below) is lit; past the table, its last.</summary>
    public static double Darkness(LampSettings s, int depth)
    {
        var table = s.Sight.Darkness;
        return depth <= 0 || table.Count == 0 ? 0 : table[Math.Min(depth, table.Count) - 1];
    }

    /// <summary>
    /// The multiplier on a robot's sight range for a hero in <paramref name="darkness"/> (held to
    /// [0, 1]) with his lamp <paramref name="lit"/>: <c>1 + darkness · (lamp − 1)</c>, the lamp's
    /// <see cref="LampSight.LampOn"/> or <see cref="LampSight.LampOff"/>. Lit (0) is exactly 1,
    /// whatever the lamp.
    /// </summary>
    public static double SightScale(LampSettings s, double darkness, bool lit)
    {
        double d = double.IsNaN(darkness) ? 0 : Math.Clamp(darkness, 0, 1);
        return 1 + d * ((lit ? s.Sight.LampOn : s.Sight.LampOff) - 1);
    }

    /// <summary>The most steps the wire carries: what its field's width must hold.</summary>
    public static int MaxSteps(LampSettings s) => (int)Math.Round(-2 * PitchFloor / s.PitchStep, MidpointRounding.AwayFromZero);
}
