namespace SourceSharp.Ai;

/// <summary>
/// Not shooting a teammate (plan_bots_teams_tf2 B.5), as a player's eyes would: a shot is held
/// when a sighted friend nearer than the aim point is within <see cref="AimCone"/> of the line to
/// it or of the view's own line, and when a friend stands inside the blast of what the bot is
/// about to send. Only actors the bot sees: no trace, no server truth. Pure over the bot's view.
/// A teammate's body is judged where the shot passes it (lane Ge): its point nearest the line, from
/// its feet to its top (a crouched one's lower top when the caller gives one), so a rocket going down
/// at the floor past a teammate standing between is held, and a line over a lower teammate's head is not.
/// </summary>
public static class FriendLine
{
    /// <summary>
    /// Whether a teammate in <paramref name="view"/> is in the way of a shot from the bot's eye,
    /// <paramref name="eyeHeight"/> over its feet, at <paramref name="aimAt"/> (null for a shot not
    /// aimed at a point: it goes where the view points, <paramref name="viewReach"/> out). A
    /// teammate's body is taken up to <paramref name="bodyTop"/> over its feet, or up to
    /// <paramref name="crouchTop"/> when its client draws it crouched (NaN: as standing).
    /// <paramref name="blast"/> is how far from the aim point a teammate can be hurt by the shot,
    /// 0 for one that hurts nobody beside what it hits.
    /// </summary>
    public static bool Blocked(PerceivedView view, float eyeHeight, AiVector? aimAt, float viewReach, float blast, float bodyTop, float crouchTop = float.NaN,
                               ActorId target = default)
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
            // The target itself is never a teammate in its own way: an unmasked Spy is drawn as a teammate.
            if (!TeamRule.Friends(self.Team, f.Team) || (target.IsValid && f.Id == target))
                continue;
            float d = (f.Origin - eye).Length;
            float top = (f.Activity & ActorActivity.Crouched) != 0 && !float.IsNaN(crouchTop) ? crouchTop : bodyTop;
            bool inLine = d < reach && (AimCone.Holds(AngleToBody(aimDir, eye, f.Origin, top), d) || AimCone.Holds(AngleToBody(viewDir, eye, f.Origin, top), d));
            bool inBlast = blast > 0f && f.Origin.DistanceTo(at) <= blast + AimCone.BodyHalfWidth;
            if (inLine || inBlast)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The angle from a line along <paramref name="dir"/> (a unit vector) from <paramref name="eye"/> to a body standing at
    /// <paramref name="origin"/>, up to <paramref name="bodyTop"/>, at the body's point nearest the line: where the shot
    /// passes it. A line along the body's own axis, or one the body is behind, is judged at the shooter's height on it.
    /// </summary>
    private static double AngleToBody(AiVector dir, AiVector eye, AiVector origin, float bodyTop)
    {
        AiVector axis = new(0f, 0f, bodyTop);
        AiVector w = eye - origin;
        float b = dir.Dot(axis), c = axis.LengthSquared, dw = dir.Dot(w), aw = axis.Dot(w), denom = c - b * b;
        float u = Math.Clamp(aw / c, 0f, 1f);
        if (denom > 1e-6f * c)
        {
            // The line's and the axis's nearest points: along the line s = (b aw - c dw) / denom, on the axis u = (aw - b dw) / denom.
            float s = (b * aw - c * dw) / denom;
            if (s > 0f)
                u = Math.Clamp((aw - b * dw) / denom, 0f, 1f);
        }

        AiVector to = (origin + axis * u - eye).Normalised();
        return Math.Acos(Math.Clamp(dir.Dot(to), -1f, 1f)) * (180.0 / Math.PI);
    }
}
