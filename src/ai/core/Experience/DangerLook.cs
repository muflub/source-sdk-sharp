namespace SourceSharp.Ai;

/// <summary>
/// A bot checks the place its side keeps being killed from as it comes into the spot where they die (plan_bots_teams_tf2_part_g
/// G.4's "danger places", lane G4b): a player who died three times coming through a door to someone in one window looks at
/// that window as they come through. On entering a bin with a danger place (<see cref="ExperienceDangers"/>) not checked in
/// the last <see cref="RecheckSeconds"/>, the bot draws once from its own stream whether it checks (its Alertness's chance),
/// and a check holds the look on the place for the seconds its Alertness gives. Where no bin has a place nothing is drawn,
/// so a bot with an empty store looks exactly as before.
/// </summary>
public sealed class DangerLook(AiRandom random)
{
    /// <summary>A place checked (or passed by on a draw that missed) is not drawn for again for this long.</summary>
    public const float RecheckSeconds = 8f;

    private readonly AiRandom _random = random;
    private readonly Dictionary<WorldBin, Tick> _drawn = new();
    private WorldBin _bin;
    private bool _inBin;
    private Tick _until;
    private bool _on;
    private AiVector _at;

    /// <summary>Bins with a danger place the bot came into and drew for, and the draws that became a check.</summary>
    public int Entries { get; private set; }

    public int Checks { get; private set; }

    /// <summary>The place being checked, while <see cref="Step"/> answers true.</summary>
    public AiVector At => _at;

    /// <summary>
    /// Something else has the look (a target, a jump, a heard sound), or the bot has no way: a check under way ends, and the
    /// bin it is in counts as entered anew next time (a bot that turned to a target at a door has not checked the window).
    /// </summary>
    public void Stop()
    {
        _on = false;
        _until = default;
        _inBin = false;
    }

    /// <summary>
    /// One decide of a bot at <paramref name="self"/> with nothing to look at. True while it checks a place, at
    /// <see cref="At"/>. <paramref name="chance"/> is the chance a bin's place is checked, <paramref name="seconds"/> how long.
    /// </summary>
    public bool Step(Tick now, float tickSeconds, float chance, float seconds, ExperienceDangers dangers, AiVector self)
    {
        ArgumentNullException.ThrowIfNull(dangers);
        if (_on && now < _until)
            return true;
        _on = false;
        WorldBin bin = WorldBin.Of(self);
        if (_inBin && bin == _bin)
            return false;
        (_bin, _inBin) = (bin, true);
        if (dangers.At(bin) is not { } place)
            return false;
        if (_drawn.TryGetValue(place.Killer, out Tick last) && (now - last) * tickSeconds < RecheckSeconds)
            return false;
        _drawn[place.Killer] = now;
        Entries++;
        if (_random.NextFloat() >= chance)
            return false;
        Checks++;
        (_on, _at, _until) = (true, place.At, now + Math.Max(1, (int)(seconds / tickSeconds)));
        return true;
    }
}
