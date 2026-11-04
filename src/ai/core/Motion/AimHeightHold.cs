namespace SourceSharp.Ai;

/// <summary>How high over its feet a body shows one point, in each stance (§3.4.1). The binding's data: a game's models say.</summary>
public readonly record struct StanceHeight(float Standing, float Crouched)
{
    public float At(bool crouched) => crouched ? Crouched : Standing;
}

/// <summary>What an aim's height is doing (§3.4.1).</summary>
public enum AimHeightPhase : byte
{
    /// <summary>On the height, following the body's small rises and falls.</summary>
    Settled = 0,

    /// <summary>The body has left the height; the eye has not yet acted on it.</summary>
    Reacting,

    /// <summary>The crosshair going to the new height.</summary>
    Moving,
}

/// <summary>
/// The height a bot's crosshair is held at (§3.4.1, ruling 2026-11-03: "Bots default aim should be
/// around shoulders thus crouching requires re aiming"). A person sets a crosshair at a height and
/// keeps it there, not on the model's centre: stairs and slopes it follows, and when the body leaves
/// that height, a crouch, a stand or a jump, the aim stays where it was for a reaction, then
/// moves there in a ballistic move as long as Fitts' law says, past it or short of it by a
/// signed share, and settles. Only the height: where across the body it aims is the pursuit's, and
/// what the view may do in a tick is <see cref="HumanMotion"/>'s. Pure and per bot.
/// </summary>
public sealed class AimHeightHold(AiRandom random)
{
    /// <summary>A body this far from the aim has left it: a stance's change is 18 u, and the steepest stairs at a run lag by about 13 u.</summary>
    public const float StimulusUnits = 14f;

    /// <summary>How fast the aim follows a body that has not left it, seconds to fall to 1/e.</summary>
    public const float FollowSeconds = 0.1f;

    /// <summary>A body that comes back this near while the eye is still reacting asks for nothing: a jump's top, a dip too short to act on.</summary>
    public const float BackUnits = 7f;

    private readonly AiRandom _random = random;
    private float _timer, _from, _seconds, _landing, _overshootPct;

    /// <summary>The height held now, world z.</summary>
    public float Height { get; private set; }

    public AimHeightPhase Phase { get; private set; }

    /// <summary>A new engagement: the aim is at <paramref name="height"/>, and nothing is under way.</summary>
    public void Start(float height)
    {
        Height = height;
        Phase = AimHeightPhase.Settled;
    }

    /// <summary>
    /// One step of <paramref name="dt"/> seconds with the body's point at <paramref name="wanted"/>;
    /// <paramref name="distance"/> away (a height's angle is what a move's length is).
    /// Answers the height held.
    /// </summary>
    public float Step(float wanted, float reactionSeconds, float overshootPct, float distance, float dt)
    {
        _overshootPct = overshootPct;
        float off = wanted - Height;
        switch (Phase)
        {
            case AimHeightPhase.Settled:
                if (MathF.Abs(off) > StimulusUnits)
                {
                    Phase = AimHeightPhase.Reacting;
                    _timer = reactionSeconds - dt;
                    if (_timer > 0f)
                        break;
                    StartMove(off, distance);
                    break;
                }

                Height += off * (1f - MathF.Exp(-dt / FollowSeconds));
                break;

            case AimHeightPhase.Reacting:
                _timer -= dt;
                if (MathF.Abs(off) <= BackUnits)
                    Phase = AimHeightPhase.Settled;
                else if (_timer <= 0f)
                    StartMove(off, distance);
                break;

            default:
                _timer += dt;
                float tau = MathF.Min(1f, _timer / _seconds);
                float s = tau * tau * tau * (10f + tau * (-15f + 6f * tau));
                // Aimed at where the body is now, past or short of it by the share drawn.
                Height = _from + (wanted + _landing - _from) * s;
                if (tau >= 1f)
                    Phase = AimHeightPhase.Settled;
                break;
        }

        return Height;
    }

    private void StartMove(float off, float distance)
    {
        // Signed: past the height or short of it, at most the skill's share.
        float pct = _overshootPct / 100f;
        _landing = off * pct * (2f * _random.NextFloat() - 1f);
        float degrees = MathF.Atan2(MathF.Abs(off), MathF.Max(distance, 1f)) * (180f / MathF.PI);
        _seconds = HumanMotion.FittsA + HumanMotion.FittsB * MathF.Log2(1f + degrees / HumanMotion.TargetWidthDeg);
        _from = Height;
        _timer = 0f;
        Phase = AimHeightPhase.Moving;
    }
}
