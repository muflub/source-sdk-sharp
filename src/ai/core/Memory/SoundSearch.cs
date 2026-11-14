namespace SourceSharp.Ai;

/// <summary>
/// Going to look into a sound (plan_hl2mp_ai §5.1, §5.4): the one most worth it among those the
/// bot heard and has not explained (<see cref="Blackboard.TryBestInvestigation"/>), while it is
/// fresh by Alertness's <c>searchSeconds</c>; reached, or given up on, it is done with. Pure over
/// the bot's board.
/// </summary>
public static class SoundSearch
{
    /// <summary>A sound this near is reached, and looking into it settles every sound within <see cref="SettlesUnits"/> of it.</summary>
    public const float ReachedUnits = 96f, SettlesUnits = 128f;

    /// <summary>Whether a sound is worth going to now: the board's best, heard less than <paramref name="searchSeconds"/> ago.</summary>
    public static bool Worth(Blackboard board, Tick now, float tickSeconds, float searchSeconds, out Investigation sound)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.TryBestInvestigation(now, tickSeconds, out sound) && (now - sound.Tick) * tickSeconds < searchSeconds;
    }

    /// <summary>
    /// One decide of the search from <paramref name="self"/>: where to go, the board's best sound.
    /// Near it, or <paramref name="gettingNowhere"/> towards it, it is settled (a person gives up on
    /// a noise they cannot get to), and so is anything its source explains. False with no sound.
    /// </summary>
    public static bool Next(Blackboard board, Tick now, float tickSeconds, AiVector self, bool gettingNowhere, out AiVector at)
    {
        ArgumentNullException.ThrowIfNull(board);
        at = default;
        if (!board.TryBestInvestigation(now, tickSeconds, out Investigation sound))
            return false;
        if (self.DistanceTo(sound.Position) < ReachedUnits || gettingNowhere)
            board.Resolve(sound.Position, SettlesUnits, sound.Source);
        at = sound.Position;
        return true;
    }
}
