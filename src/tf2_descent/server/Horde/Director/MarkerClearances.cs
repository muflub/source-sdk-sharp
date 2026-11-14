using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Server.Horde.Director;

/// <summary>
/// The room round each pack marker, measured on the server at level ready (plan_descent_logic
/// HO9; plan_tf2_rpg 5d): a level ships no clearance, so the horde measures it from the level's
/// own geometry before the spawn director plans, and hands it over as data on each marker.
/// </summary>
public interface IMarkerClearances
{
    /// <summary><paramref name="level"/> with each pack marker's clearance measured.</summary>
    HordeLevel Measure(HordeLevel level);

    /// <summary>Markers the last <see cref="Measure"/> measured, and how many of them stood in solid (every side 0).</summary>
    (int Markers, int InSolid) Last { get; }
}

/// <summary>
/// Measures with the motion's own world (<see cref="IMotionWorld"/>: solid brushes, props,
/// bodies and players, as a moving robot meets them): up from the marker's floor to the
/// ceiling, a line, at most <see cref="Reach"/>; and west, east, south and north a probe
/// <see cref="ProbeHalf"/> units each way, from a step above the floor (horde.json's
/// <c>director.stepHeight</c>, so a stair is not a wall) to the tallest robot's head or the
/// ceiling, whichever is lower, at most <see cref="Reach"/> out. A side reaches as far as its
/// probe's middle got, plus the probe's half. A marker standing in solid holds nothing.
/// Eight sweeps a marker, once a level.
/// </summary>
[SideService(Side.Server, As = [typeof(IMarkerClearances)])]
public sealed class MarkerClearances(IMotionWorld world, IHordeRules rules) : IMarkerClearances
{
    /// <summary>How far a measurement looks: a side or a ceiling further than this is this far.</summary>
    public const float Reach = 512f;

    /// <summary>The side probe's half-width.</summary>
    public const float ProbeHalf = 1f;

    /// <summary>How far above its floor a marker's measurements start: the motion's own lift off a floor.</summary>
    public const float Lift = RobotMotion.SettleLift;

    public (int Markers, int InSolid) Last { get; private set; }

    public HordeLevel Measure(HordeLevel level)
    {
        int markers = 0, solid = 0;
        var measured = level.WithClearances((_, _, m) =>
        {
            markers++;
            var c = At(m.Position);
            if (c.Narrowest <= 0) solid++;
            return c;
        });
        Last = (markers, solid);
        return measured;
    }

    /// <summary>The clearance of a marker whose floor is at <paramref name="floor"/>.</summary>
    public MarkerClearance At(AiVector floor)
    {
        var settings = rules.Rules.Content;
        float step = (float)settings.Horde.Director.StepHeight;
        float tallest = (float)(settings.RobotRules.Push.HullHeight * settings.RobotRules.Sizes.Max(s => s.ScaleMax));
        var feet = new Vector(floor.X, floor.Y, floor.Z + Lift);

        var up = world.Sweep(feet, feet + new Vector(0, 0, Reach), Vector.Zero, Vector.Zero, EntityHandle.None);
        if (up.StartSolid)
            return new MarkerClearance(0, 0, 0, 0, 0);
        float height = Lift + up.Fraction * Reach;
        float top = MathF.Min(height - Lift - 1f, tallest);
        if (top <= step + 1f)
            return new MarkerClearance(0, 0, 0, 0, height);

        var mins = new Vector(-ProbeHalf, -ProbeHalf, step + 1f);
        var maxs = new Vector(ProbeHalf, ProbeHalf, top);
        float Side(float dx, float dy)
        {
            var t = world.Sweep(feet, feet + new Vector(dx * Reach, dy * Reach, 0), mins, maxs, EntityHandle.None);
            return t.StartSolid ? 0 : t.Fraction * Reach + ProbeHalf;
        }

        return new MarkerClearance(Side(-1, 0), Side(1, 0), Side(0, -1), Side(0, 1), height);
    }
}
