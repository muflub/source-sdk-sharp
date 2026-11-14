using System.Globalization;
using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// What became of the jumps begun, by how each ended; a landing is counted by where it came down against the plan's.
/// <see cref="FizzledFelt"/> are the fizzles on which the pilot did feel a blast: the rocket went off near the bot and
/// not where the plan aimed it (live, on a teammate standing in the shot's line), where the rest felt none at all.
/// </summary>
public readonly record struct JumpTallyCounts(long Ended, long LandedNear, long LandedShort, long LandedElsewhere, long ViewLate, long Astray, long Fizzled, long Died,
                                              long TimedOut, long Predicted, long FizzledFelt = 0);

/// <summary>
/// One jump in the tally: what the bot did (<see cref="JumpFlown"/>), how far from the plan's touch
/// it came down, and, once a worker has flown the same recipe from the same launch in the
/// simulator, where the simulator comes down and after how long in the air.
/// </summary>
public sealed record JumpTallied(int Serial, JumpFlown Flown, float OffPlan, Vector? SimTouch = null, float SimAirSeconds = 0f, bool SimFlew = false)
{
    /// <summary>How far the game's landing is from the simulator's for the same recipe and launch, or null before it is known or for a jump that did not land.</summary>
    public float? Gap => SimTouch is { } sim && Flown.End == JumpEnd.Landed ? JumpTally.Flat(Flown.Touch, sim) : null;
}

/// <summary>
/// What each rocket jump a bot began taught (plan_bots_teams_tf2_part_g G.5 step 7, the counting
/// half): every jump by how it ended, a landing by whether it came down where the plan said
/// (within <see cref="NearUnits"/>), short of it along the way, or somewhere else; and the last
/// <see cref="Kept"/> jumps in full, each with the simulator's flight of the same recipe from the
/// same launch beside the game's (the differential). Written on the game's thread and by the
/// planner's worker, read by the dev API: under a lock.
/// </summary>
public sealed class JumpTally
{
    /// <summary>A landing this near the plan's touch landed where predicted.</summary>
    public const float NearUnits = 64f;

    /// <summary>The jumps kept in full.</summary>
    public const int Kept = 64;

    private readonly object _gate = new();
    private readonly List<JumpTallied> _last = [];
    private readonly long[] _ends = new long[Enum.GetValues<JumpEnd>().Length];
    private long _near, _short, _elsewhere, _predicted, _fizzledFelt;
    private int _serial;

    internal static float Flat(Vector a, Vector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>A jump ended: counted and kept. Answers its serial, which its prediction comes back under.</summary>
    public int Ended(JumpFlown flown)
    {
        ArgumentNullException.ThrowIfNull(flown);
        float off = Flat(flown.Touch, flown.Planned.Touch);
        lock (_gate)
        {
            _ends[(int)flown.End]++;
            if (flown.End == JumpEnd.Fizzled && flown.Felt)
                _fizzledFelt++;
            if (flown.End == JumpEnd.Landed)
            {
                // Short: it came down before the plan's touch along the way it was going.
                AiVector way = flown.Planned.Way;
                float before = (flown.Planned.Touch.X - flown.Touch.X) * way.X + (flown.Planned.Touch.Y - flown.Touch.Y) * way.Y;
                if (off <= NearUnits)
                    _near++;
                else if (before > 0f && before >= off * 0.7f)
                    _short++;
                else
                    _elsewhere++;
            }

            if (_last.Count >= Kept)
                _last.RemoveAt(0);
            _last.Add(new JumpTallied(++_serial, flown, off));
            return _serial;
        }
    }

    /// <summary>The simulator's flight of jump <paramref name="serial"/>'s recipe from its launch.</summary>
    public void Predicted(int serial, in JumpOutcome sim)
    {
        lock (_gate)
        {
            int at = _last.FindIndex(j => j.Serial == serial);
            if (at < 0)
                return;
            _last[at] = _last[at] with { SimTouch = sim.Touch, SimAirSeconds = sim.AirSeconds, SimFlew = sim.Flew };
            _predicted++;
        }
    }

    public JumpTallyCounts Counts
    {
        get
        {
            lock (_gate)
            {
                return new JumpTallyCounts(_ends.Sum(), _near, _short, _elsewhere, _ends[(int)JumpEnd.ViewLate], _ends[(int)JumpEnd.Astray], _ends[(int)JumpEnd.Fizzled],
                                           _ends[(int)JumpEnd.Died], _ends[(int)JumpEnd.TimedOut], _predicted, _fizzledFelt);
            }
        }
    }

    /// <summary>The jumps kept, oldest first: a copy.</summary>
    public JumpTallied[] Last()
    {
        lock (_gate)
            return [.. _last];
    }

    /// <summary>The kept jumps as the dev API lists them.</summary>
    public string LastJson()
    {
        var json = new StringBuilder("[");
        JumpTallied[] last = Last();
        for (int i = 0; i < last.Length; i++)
        {
            JumpTallied j = last[i];
            JumpFlown f = j.Flown;
            json.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture,
                $"{{\"serial\":{j.Serial},\"plan\":{f.Planned.Id},\"end\":\"{f.End}\",\"launched\":{(f.Launched ? "true" : "false")},\"felt\":{(f.Felt ? "true" : "false")},"
                + $"\"slid\":{f.Slid:F1},\"approach_seconds\":{f.ApproachCommands * f.TickSeconds:F3},\"stood_seconds\":{f.Stood * f.TickSeconds:F3},"
                + $"\"air_seconds\":{f.AirCommands * f.TickSeconds:F3},\"rise\":{f.Rise:F1},\"plan_apex\":{f.Planned.Apex:F1},"
                + $"\"felt_after_shot\":{f.FeltAfterShot},\"leap\":{P(f.Leap)},"
                + $"\"takeoff\":{P(f.Recipe.Takeoff)},\"landing\":{P(f.Recipe.Landing)},\"touch\":{P(f.Touch)},\"plan_touch\":{P(f.Planned.Touch)},\"off_plan\":{j.OffPlan:F1},"
                + $"\"plan_seconds\":{f.Planned.Seconds:F3},\"plan_air_seconds\":{f.Planned.AirSeconds:F3},\"plan_walk_seconds\":{f.Planned.WalkSeconds:F3},\"plan_health\":{f.Planned.Health:F1},"
                + $"\"pitch\":{f.Recipe.Pitch:F1},\"yaw_offset\":{f.Recipe.YawOffset:F1},\"shot_delay\":{f.Recipe.ShotDelay},\"run_up\":{f.Recipe.RunUp:F0},"
                + $"\"sim_touch\":{(j.SimTouch is { } s ? P(s) : "null")},\"sim_air_seconds\":{j.SimAirSeconds:F3},\"sim_flew\":{(j.SimFlew ? "true" : "false")},"
                + $"\"gap\":{(j.Gap is { } gap ? gap.ToString("F1", CultureInfo.InvariantCulture) : "null")}}}");
        }

        return json.Append(']').ToString();
    }

    private static string P(Vector v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:F1},{v.Y:F1},{v.Z:F1}]");
}
