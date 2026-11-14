using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Ai.Doors;

/// <summary>
/// The door shove, pure (plan_descent_logic L9, 3.9, HO11): robots held up by a hero standing in
/// a doorway push him out of it, so a door cannot be held against a horde. Which door a hero
/// stands in, whether a robot's path runs through it, how long the robot waits first, and the
/// push: along the door's normal, away from the robot, sized by the robot's scale and the
/// hero's knockback resistance, with a lift so the ground does not eat it. No damage. Every
/// number is horde.json's <c>shove</c> block (and the director's step height).
/// </summary>
public sealed class DoorShoveRules
{
    private readonly ShoveSettings _s;
    private readonly float _step;
    private readonly double _ticksPerSecond;

    public DoorShoveRules(HordeSettings settings, double ticksPerSecond)
    {
        _s = settings.Shove;
        _step = (float)settings.Director.StepHeight;
        _ticksPerSecond = ticksPerSecond;
    }

    public ShoveSettings Settings => _s;

    /// <summary>The horizontal unit along a door's opening (its normal turned a quarter left).</summary>
    private static AiVector Across(HordeDoor door) => new(-door.Normal.Y, door.Normal.X, 0);

    /// <summary>How far <paramref name="p"/> stands past the door's plane, along its normal (negative on the first room's side).</summary>
    public static float Along(HordeDoor door, AiVector p) =>
        (p.X - door.Position.X) * door.Normal.X + (p.Y - door.Position.Y) * door.Normal.Y;

    /// <summary>How far <paramref name="p"/> stands from the opening's middle, across it.</summary>
    public static float Aside(HordeDoor door, AiVector p)
    {
        var a = Across(door);
        return (p.X - door.Position.X) * a.X + (p.Y - door.Position.Y) * a.Y;
    }

    /// <summary>
    /// Whether a hero whose feet are at <paramref name="feet"/> stands inside the door's volume:
    /// within half its width of the middle, within the shove's half-depth of its plane, and from
    /// a step below its sill to its height above. A hero beside the door, or on open floor, is not.
    /// </summary>
    public bool InDoorway(HordeDoor door, AiVector feet)
    {
        float up = feet.Z - door.Position.Z;
        return MathF.Abs(Along(door, feet)) <= _s.HalfDepth
               && MathF.Abs(Aside(door, feet)) <= door.Width / 2
               && up >= -_step && up <= MathF.Min(door.Height, 512f);
    }

    /// <summary>
    /// Whether a robot at <paramref name="robot"/> whose path runs on through <paramref name="path"/>
    /// (its corners left, nearest first) goes into or through the door: it stands within the
    /// shove's reach of the door's plane, and a corner of the path lies inside the doorway (a
    /// robot making for the hero who holds it) or a leg crosses the plane inside the opening.
    /// </summary>
    public bool PathCrosses(HordeDoor door, AiVector robot, ReadOnlySpan<AiVector> path)
    {
        if (MathF.Abs(Along(door, robot)) > _s.Reach) return false;
        var from = robot;
        foreach (var to in path)
        {
            if (InDoorway(door, to)) return true;
            float a = Along(door, from), b = Along(door, to);
            if ((a <= 0 && b > 0) || (a >= 0 && b < 0))
            {
                float t = a / (a - b);
                float aside = Aside(door, from) + (Aside(door, to) - Aside(door, from)) * t;
                if (MathF.Abs(aside) <= door.Width / 2) return true;
            }
            from = to;
        }
        return false;
    }

    /// <summary>
    /// The door of <paramref name="level"/> a hero at <paramref name="heroFeet"/> stands in and a
    /// robot at <paramref name="robot"/> means to go through by <paramref name="path"/>; -1 for none.
    /// </summary>
    public int DoorFor(HordeLevel level, AiVector robot, ReadOnlySpan<AiVector> path, AiVector heroFeet)
    {
        for (int d = 0; d < level.Doors.Length; d++)
        {
            var door = level.Doors[d];
            if (InDoorway(door, heroFeet) && PathCrosses(door, robot, path)) return d;
        }
        return -1;
    }

    /// <summary>
    /// Whether a robot at <paramref name="robot"/>, <paramref name="robotHalf"/> wide each way
    /// with melee <paramref name="reach"/>, can lay hands on a hero at <paramref name="hero"/>
    /// (whose hull is the standing one, <paramref name="heroHalf"/> each way): their hulls are
    /// within the robot's reach of each other, across the floor. 3.9's shove is a melee intent,
    /// and a robot that has come to fight the hero holding a doorway stands here, not walking
    /// through it, so it is never held up by him.
    /// </summary>
    public static bool InContact(AiVector robot, float robotHalf, double reach, AiVector hero, float heroHalf)
    {
        float dx = hero.X - robot.X, dy = hero.Y - robot.Y;
        double within = robotHalf + heroHalf + reach;
        return dx * dx + dy * dy <= within * within && MathF.Abs(hero.Z - robot.Z) <= 64f;
    }

    /// <summary>The door of <paramref name="level"/> whose doorway a hero at <paramref name="heroFeet"/> holds against a robot at <paramref name="robot"/> within the shove's reach of its plane; -1 for none.</summary>
    public int DoorHeld(HordeLevel level, AiVector robot, AiVector heroFeet)
    {
        for (int d = 0; d < level.Doors.Length; d++)
        {
            var door = level.Doors[d];
            if (InDoorway(door, heroFeet) && MathF.Abs(Along(door, robot)) <= _s.Reach) return d;
        }
        return -1;
    }

    /// <summary>How long a robot of <paramref name="discipline"/> is held up before it shoves: at once at or below the low discipline, else the dwell.</summary>
    public long DwellTicks(int discipline) =>
        discipline <= _s.LowDiscipline ? 0 : (long)Math.Round(_s.DwellSeconds * _ticksPerSecond);

    /// <summary>Server ticks between two shoves at one door.</summary>
    public long CooldownTicks => Math.Max(1, (long)Math.Round(_s.CooldownSeconds * _ticksPerSecond));

    /// <summary>
    /// The shove a robot at <paramref name="robot"/> of <paramref name="scale"/> gives a hero at
    /// <paramref name="hero"/> with <paramref name="knockbackResist"/> (a fraction; negative
    /// takes more): along the door's normal away from the robot's side,
    /// speed x scale x (1 - resistance, held to 0..2), and the lift up. None when the two stand
    /// level along the normal, which has no side to push from.
    /// </summary>
    public AiVector Impulse(HordeDoor door, AiVector robot, AiVector hero, double scale, double knockbackResist)
    {
        float apart = Along(door, hero) - Along(door, robot);
        if (apart == 0) return AiVector.Zero;
        double speed = _s.Speed * scale * Math.Clamp(1 - knockbackResist, 0, 2);
        var away = door.Normal * MathF.Sign(apart);
        return new AiVector(away.X * (float)speed, away.Y * (float)speed, (float)_s.Lift);
    }
}
