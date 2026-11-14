namespace SourceSharp.Ai;

/// <summary>
/// Not shooting a teammate (plan_bots_teams_tf2 B.5), as a player's eyes would: a shot is held
/// when a sighted friend nearer than the aim point is within <see cref="AimCone"/> of the line to
/// it or of the view's own line, and when a friend stands inside the blast of what the bot is
/// about to send. Only actors the bot sees: no trace, no server truth. Pure over the bot's view.
/// </summary>
public static class FriendLine
{
    /// <summary>
    /// Whether a teammate in <paramref name="view"/> is in the way of a shot from the bot's eye,
    /// <paramref name="eyeHeight"/> over its feet, at <paramref name="aimAt"/> (null for a shot not
    /// aimed at a point: it goes where the view points, <paramref name="viewReach"/> out). A
    /// teammate's body is taken up to <paramref name="bodyTop"/> over its feet.
    /// <paramref name="blast"/> is how far from the aim point a teammate can be hurt by the shot,
    /// 0 for one that hurts nobody beside what it hits.
    /// </summary>
    public static bool Blocked(PerceivedView view, float eyeHeight, AiVector? aimAt, float viewReach, float blast, float bodyTop)
    {
        ArgumentNullException.ThrowIfNull(view);
        SelfState self = view.Self;
        AiVector eye = self.Origin + new AiVector(0f, 0f, eyeHeight);
        AiVector viewDir = VisionMath.Forward(self.Eye);
        AiVector at = aimAt ?? eye + viewDir * viewReach;
        float reach = (at - eye).Length;
        AiVector aimDir = reach > 0f ? (at - eye) * (1f / reach) : viewDir;
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            if (!TeamRule.Friends(self.Team, f.Team))
                continue;
            float d = (f.Origin - eye).Length;
            bool inLine = d < reach && (AimCone.Holds(AngleToBody(aimDir, eye, f.Origin, bodyTop), d) || AimCone.Holds(AngleToBody(viewDir, eye, f.Origin, bodyTop), d));
            bool inBlast = blast > 0f && f.Origin.DistanceTo(at) <= blast + AimCone.BodyHalfWidth;
            if (inLine || inBlast)
                return true;
        }

        return false;
    }

    /// <summary>The angle from a line along <paramref name="dir"/> to a standing body at <paramref name="origin"/>, at the shooter's own height on it (<see cref="AimCone.AngleToBody"/>'s rule).</summary>
    private static double AngleToBody(AiVector dir, AiVector eye, AiVector origin, float bodyTop)
    {
        AiVector axis = new(0f, 0f, bodyTop);
        float u = Math.Clamp((eye - origin).Dot(axis) / axis.LengthSquared, 0f, 1f);
        AiVector to = (origin + axis * u - eye).Normalised();
        return Math.Acos(Math.Clamp(dir.Dot(to), -1f, 1f)) * (180.0 / Math.PI);
    }
}
