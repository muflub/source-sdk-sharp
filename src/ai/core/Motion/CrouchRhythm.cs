namespace SourceSharp.Ai;

/// <summary>
/// When a fighting bot crouches (plan_hl2mp_ai §5.4, ruling 2026-11-02: "bots should crouch as well
/// when shooting to make it hard to hit"), lifted from the HL2MP decider for every game's: while the
/// game's own rule says a crouch fits, for a stretch or a quick dip, <c>crouchShare</c> of the time.
/// The holds and gaps are by the clock, not the decide, so the key does not flicker; what ends a fit
/// ends a crouch, and a new one waits its gap. Per bot; its draws come from the stream it is given:
/// one when a fit begins, three for each crouch, every one whatever the share, so the stream does not
/// depend on the weights.
/// </summary>
public sealed class CrouchRhythm(AiRandom random)
{
    /// <summary>The shortest a crouch is held, and the shortest gap after one: nobody's knee does less, and IN_DUCK flicking on and off looks wrong.</summary>
    public const float MinHoldSeconds = 0.35f, MinGapSeconds = 0.6f;

    /// <summary>A quick dip's upper end, a stretch's, and how often a crouch is a dip: holds a person's length (the user's: a median of about 0.9 s).</summary>
    public const float DipSeconds = 0.6f, StretchSeconds = 2.5f, DipShare = 0.3f;

    private readonly AiRandom _random = random;
    private bool _crouching, _fit;
    private Tick _until, _next;

    /// <summary>Whether the last step crouched.</summary>
    public bool Crouching => _crouching;

    /// <summary>
    /// One decide at <paramref name="now"/>, <paramref name="fit"/> whether the game's rule lets a
    /// crouch be held now, at <paramref name="share"/> of the time: whether to crouch.
    /// </summary>
    public bool Step(Tick now, float tickSeconds, bool fit, float share)
    {
        if (!fit)
        {
            if (_crouching)
                _next = now + (int)(MinGapSeconds / tickSeconds);
            _crouching = false;
            _fit = false;
            return false;
        }

        if (!_fit)
        {
            // A fight does not open with a crouch every time: the first waits part of a gap.
            _fit = true;
            Tick first = now + (int)(_random.NextFloat() * Gap(1f, share) / tickSeconds);
            if (first > _next)
                _next = first;
        }

        if (_crouching && now >= _until)
            _crouching = false;
        else if (!_crouching && now >= _next)
        {
            float kind = _random.NextFloat(), length = _random.NextFloat(), jitter = _random.NextFloat();
            float hold = kind < DipShare
                ? MinHoldSeconds + (DipSeconds - MinHoldSeconds) * length
                : DipSeconds + (StretchSeconds - DipSeconds) * length;
            _crouching = share > 0f;
            _until = now + (int)(hold / tickSeconds);
            _next = _until + (int)(MathF.Max(MinGapSeconds, Gap(hold, share) * (0.5f + jitter)) / tickSeconds);
        }

        return _crouching;
    }

    /// <summary>The knee lets go (a death): the crouch ends, and nothing else of the rhythm changes.</summary>
    public void Release() => _crouching = false;

    /// <summary>The gap that makes a hold this long <paramref name="share"/> of the time.</summary>
    public static float Gap(float hold, float share)
    {
        share = MathF.Max(share, 0.002f);
        return hold * (1f - share) / share;
    }
}
