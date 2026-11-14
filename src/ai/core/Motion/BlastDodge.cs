namespace SourceSharp.Ai;

/// <summary>
/// Getting out of a blast's way (plan_bots_teams_tf2_part_g G.2.3): whether something a bot sees
/// in flight (a rocket, a grenade), or lying where it landed, will be within its blast of the
/// bot's body, when, and which way out is shortest. Geometry only: what the thing is and how far
/// its blast reaches are the game's knowledge, and whether and when a bot acts on it its skills'.
/// </summary>
public static class BlastDodge
{
    /// <summary>How far ahead a flight is read: a blast further off in time than this is not yet the bot's business.</summary>
    public const float HorizonSeconds = 1.5f;

    /// <summary>Slower than this a thing is lying where it landed, and its blast is where it is.</summary>
    public const float AtRestSpeed = 40f;

    private const float Step = 0.05f;

    /// <summary>
    /// Whether the thing at <paramref name="origin"/> flying at <paramref name="velocity"/>, falling
    /// at <paramref name="gravity"/> units/s² (0 for one that flies flat), comes within
    /// <paramref name="reach"/> of <paramref name="body"/> inside <see cref="HorizonSeconds"/>:
    /// <paramref name="seconds"/> until it is nearest and <paramref name="nearest"/> where it then
    /// is. One falling is taken to land and lie at the height of <paramref name="floorZ"/>. One
    /// flying away is no threat however near it passed.
    /// </summary>
    public static bool Threatens(AiVector body, float floorZ, AiVector origin, AiVector velocity, float gravity, float reach, out float seconds, out AiVector nearest)
    {
        seconds = 0f;
        nearest = origin;
        float speed = velocity.Length;
        if (speed < AtRestSpeed)
            return origin.DistanceTo(body) < reach;

        if (gravity <= 0f)
        {
            float along = (body - origin).Dot(velocity) / (speed * speed);
            if (along <= 0f || along > HorizonSeconds)
                return false;
            seconds = along;
            nearest = origin + velocity * along;
            return nearest.DistanceTo(body) < reach;
        }

        float best = float.MaxValue;
        for (float t = Step; t <= HorizonSeconds; t += Step)
        {
            var at = new AiVector(origin.X + velocity.X * t, origin.Y + velocity.Y * t, origin.Z + velocity.Z * t - 0.5f * gravity * t * t);
            bool landed = at.Z <= floorZ;
            if (landed)
                at = new AiVector(at.X, at.Y, floorZ);
            float d = at.DistanceTo(body);
            if (d < best)
                (best, seconds, nearest) = (d, t, at);
            if (landed)
                break;
        }

        return best < reach;
    }

    /// <summary>
    /// The shortest way out of the blast, flat and of length 1: straight away from where the
    /// thing will be nearest, which for one in flight is across its path on the body's own side.
    /// A body right on the path (or on the thing) goes to the side <paramref name="tieSide"/> says
    /// (1 left of the flight, -1 right).
    /// </summary>
    public static AiVector Away(AiVector body, AiVector velocity, AiVector nearest, int tieSide)
    {
        var out2D = new AiVector(body.X - nearest.X, body.Y - nearest.Y, 0f);
        if (out2D.Length > 8f)
            return out2D.Normalised();
        var flight = new AiVector(velocity.X, velocity.Y, 0f);
        if (flight.Length < 1f)
            return new AiVector(tieSide >= 0 ? 1f : -1f, 0f, 0f);
        flight = flight.Normalised();
        return tieSide >= 0 ? new AiVector(-flight.Y, flight.X, 0f) : new AiVector(flight.Y, -flight.X, 0f);
    }
}
