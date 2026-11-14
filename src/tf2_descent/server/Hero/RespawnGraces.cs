using SourceSharp.Abi;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// A hero's respawn grace: the tick he stood again, the tick it lapses, and whether he ended it by
/// acting; or, <see cref="Dead"/>, that he died and has not been told to stand again yet.
/// </summary>
public readonly record struct RespawnGrace(long From, long Until, bool Acted, bool Dead = false)
{
    /// <summary>Whether he takes no damage at <paramref name="tick"/> (the rules' <c>DownedRules.Untouchable</c>).</summary>
    public bool Untouchable(long tick) => !Dead && !Acted && tick < Until;
}

/// <summary>
/// Each hero's respawn grace as the downed state carries it out (WO6), on a board the horde reads
/// without being handed the downed state: <see cref="HeroDowned"/>'s listener chain holds the
/// horde's session (<see cref="DownedHordeTap"/>), so the session given <see cref="HeroDowned"/>
/// would be a constructor cycle. Passive: only <see cref="HeroDowned"/> writes it (a respawn
/// begins a grace, an act ends it, a death marks him dead, a new level drops it); the horde's session reads it
/// each tick (<c>Horde.Session.HordeSession</c>: a hero in his grace is not there for the robots).
/// </summary>
[SideService(Side.Server)]
public sealed class RespawnGraces
{
    private readonly Dictionary<int, RespawnGrace> _graces = [];

    /// <summary>The hero in <paramref name="playerIndex"/> stood again at <paramref name="tick"/>, untouchable to <paramref name="untilTick"/>.</summary>
    public void Began(int playerIndex, long tick, long untilTick) => _graces[playerIndex] = new RespawnGrace(tick, untilTick, Acted: false);

    /// <summary>He acted in his grace (a shot, a swing, a cast): it ends now; the board keeps that he did.</summary>
    public void Acted(int playerIndex)
    {
        if (_graces.TryGetValue(playerIndex, out var g))
            _graces[playerIndex] = g with { Acted = true };
    }

    /// <summary>
    /// He died: marked dead until his respawn is told. The engine stands a hero again a frame
    /// before the downed state tells it (the mode's respawn runs first), and in that frame the
    /// horde must not see him as anyone's; the mark says so.
    /// </summary>
    public void Died(int playerIndex) => _graces[playerIndex] = new RespawnGrace(0, 0, Acted: false, Dead: true);

    /// <summary>A new level: nobody is in a grace.</summary>
    public void Clear() => _graces.Clear();

    /// <summary>His grace since his last respawn on this level, or his death since, if any (lapsed or acted ones too, until the level's end).</summary>
    public bool TryGet(int playerIndex, out RespawnGrace grace) => _graces.TryGetValue(playerIndex, out grace);

    /// <summary>Whether the hero in <paramref name="playerIndex"/> takes no damage at <paramref name="tick"/>.</summary>
    public bool Untouchable(int playerIndex, long tick) => _graces.TryGetValue(playerIndex, out var g) && g.Untouchable(tick);
}
