using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A building's sapper, as the bot's team sees it (round 2's S7). A sapper is a model on the building every client draws
/// (<see cref="TFBuildingFlags.Sapped"/> on what its view shows), so a bot reads a building sapped when the building is in its
/// view; and a builder's own HUD tray flags his own buildings sapped wherever he stands (<see cref="TFBuildingTrayMeters"/>),
/// so a bot reads its own buildings sapped through walls. Nobody else's: stock shows a teammate's sapped building to no one
/// but its builder (the builder's tray, and his "Spy sapping" voice line, which is a sound).
///
/// <para>The general play's answer (<see cref="ClassPlay.AnswersSapped"/>, by default <see cref="ClassPlay.SappedAnswerWeight"/>): a friendly building the bot reads sapped within
/// <see cref="SappedAnswerReach"/> draws it there, on the class branch, to stand by it, where the Spy who sapped it is. The
/// Engineer's unsap and a Pyro's Homewrecker are their own lanes'; they read <see cref="SeesSapped"/>.</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>
    /// A sapped friendly building within this is answered: stock's "nearby" for a bot's opportunistic errands (the 1000 u of
    /// surrounding area its tactical monitor searches for a teleporter, tf_bot_tactical_monitor.cpp:528).
    /// </summary>
    public const float SappedAnswerReach = 1000f;

    /// <summary>A building seen sapped this long ago is still believed sapped while the bot walks to it out of sight.</summary>
    public const float SappedHoldSeconds = 3f;

    /// <summary>The bot stands this far from the sapped building, apart from its teammates.</summary>
    public const float SappedStandUnits = 150f;

    /// <summary>The answer's verdicts.</summary>
    public const string SappedGoingVerdict = "to a sapped building", SappedStandVerdict = "by a sapped building";

    private AiVector _sappedAt;

    /// <summary>The decides the bot spent answering a sapped friendly building, for the A/B harness.</summary>
    public int SappedAnswerDecides { get; private set; }

    /// <summary>
    /// Whether the bot reads building <paramref name="id"/> (the binding's id, <see cref="ThingFacts.Id"/>) sapped now: it is
    /// in this decide's view with a sapper drawn on it, or it is one of the bot's own (as it remembers them) and its HUD
    /// tray flags that kind sapped.
    /// </summary>
    public bool SeesSapped(int id)
    {
        foreach (ref readonly ThingFacts t in _view.Things)
        {
            if (t.Kind == ThingKind.Building && t.Id == id)
                return (t.Flags & (int)TFBuildingFlags.Sapped) != 0;
        }

        foreach (KnownBuilding b in FriendlyBuildings)
        {
            if (b.Id == id)
                return TraySapped(in b);
        }

        return false;
    }

    /// <summary>Whether <paramref name="b"/> is the bot's own and its HUD tray flags its kind sapped.</summary>
    private bool TraySapped(in KnownBuilding b)
        => b.Owner == _bot.Slot && TFBuildingTrayMeters.MeterOf(b.Type) is { } meter && Kit?.Meter(meter, 0f) > 0f;

    /// <summary>
    /// The nearest friendly building the bot believes sapped within <see cref="SappedAnswerReach"/>: one it reads sapped now
    /// (<see cref="SeesSapped"/>), or saw sapped in the last <see cref="SappedHoldSeconds"/> and has not seen since.
    /// </summary>
    private bool SappedFriendly(out AiVector at)
    {
        at = default;
        float best = SappedAnswerReach;
        foreach (KnownBuilding b in FriendlyBuildings)
        {
            bool inView = b.Last == _now;
            bool sapped = inView ? (b.Flags & (int)TFBuildingFlags.Sapped) != 0
                                 : TraySapped(in b) || ((b.Flags & (int)TFBuildingFlags.Sapped) != 0 && SecondsSince(b.Last) <= SappedHoldSeconds);
            if (!sapped)
                continue;
            float d = _self.Origin.DistanceTo(b.At);
            if (d < best)
                (best, at) = (d, b.At);
        }

        return best < SappedAnswerReach;
    }

    /// <summary>The answer's score: the class's (<see cref="ClassPlay.AnswersSapped"/>) with a sapped friendly building in reach and nothing carried; else 0.</summary>
    private float SappedUtility()
    {
        float utility = _classPlay.AnswersSapped;
        return utility > 0f && !Carrying && SappedFriendly(out _) ? utility : 0f;
    }

    /// <summary>To the sapped building, and by it, stand.</summary>
    private bool GoSapped()
    {
        if (!SappedFriendly(out AiVector at))
            return false;
        _sappedAt = at;
        SappedAnswerDecides++;
        if (_self.Origin.DistanceTo(at) < ArriveUnits + SappedStandUnits)
        {
            StandApart(at, SappedStandUnits);
            _verdict = SappedStandVerdict;
            return true;
        }

        GoTo(at, MoveSpeed.Run);
        _verdict = SappedGoingVerdict;
        return true;
    }

    /// <summary>The sapped building the bot last went to, for the facts and the inspector.</summary>
    internal AiVector SappedAt => _sappedAt;
}
