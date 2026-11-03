namespace SourceSharp.Ai;

/// <summary>
/// Steering beside the path (plan_hl2mp_ai §9 Phase 5 step 4): a bot keeps
/// its distance from the players it can see, as a player sidesteps one in a
/// corridor. Pure.
/// </summary>
public static class Steering
{
    /// <summary>Closer than this, a seen player pushes the bot's way aside.</summary>
    public const float Radius = 72f;

    /// <summary>
    /// The push away from the players at <paramref name="others"/> within
    /// <see cref="Radius"/> of <paramref name="self"/>, growing as they near
    /// (flat, at most 1 per player); zero with nobody near.
    /// </summary>
    public static AiVector Separation(AiVector self, ReadOnlySpan<AiVector> others, float radius = Radius)
    {
        AiVector push = default;
        foreach (AiVector o in others)
        {
            var away = new AiVector(self.X - o.X, self.Y - o.Y, 0f);
            float d = away.Length;
            if (d >= radius || MathF.Abs(self.Z - o.Z) > 64f)
                continue;
            if (d < 1e-3f)
                away = new AiVector(1f, 0f, 0f);
            push += away.Normalised() * (1f - d / radius);
        }

        return push;
    }

    /// <summary>A way to go, bent by a separation push: the sum, normalised; the way itself when nothing pushes.</summary>
    public static AiVector Bend(AiVector way, AiVector push, float weight = 1.5f)
    {
        if (push.LengthSquared < 1e-6f)
            return way;
        AiVector bent = new AiVector(way.X, way.Y, 0f).Normalised() + push * weight;
        return bent.LengthSquared < 1e-6f ? way : bent.Normalised();
    }
}
