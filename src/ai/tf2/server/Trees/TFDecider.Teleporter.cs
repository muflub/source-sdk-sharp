//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Buildings;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A friendly teleporter as a way to the bot's goal (round 2's S8, after stock's CTFBotTacticalMonitor::FindNearbyTeleporter
/// and CTFBotUseTeleporter, tf_bot_tactical_monitor.cpp:333-347, :513-570, tf_bot_use_teleporter.cpp). The route choice
/// takes a link from a friendly entrance to its exit: a goal the bot walks to (<see cref="GoTo"/>) is walked to by way of
/// the entrance when the walk to the entrance, the trip and the walk from the exit together beat the walk there by
/// <see cref="TeleporterSaveSeconds"/>. Every walk is the bot's own way over the graph, searched by the shared
/// <see cref="Walks"/> service (pooled pathfinders, answers kept for all bots) and capped at
/// <see cref="TeleporterSearchBudget"/> cells: the walk there from where the bot stands (a graph with drops is not the
/// same both ways, so no walk is taken as another's difference), a lower bound when the cap stops it, which only makes
/// the trip harder to take; the two legs of the way by the teleporter must be found whole, or there is no trip. From the
/// exit the way back by the entrance can never beat the walk, so a bot never rides back.
///
/// <list type="bullet">
/// <item>The entrance is one the bot sees (or saw in the last <see cref="TeleporterGlimpseSeconds"/>) within <see cref="TeleporterReach"/> (stock's 1000 u of surrounding area),
/// spun up and ready to send (<see cref="TFBuildingFlags.Ready"/>): stock takes a teleporter only "if it is ready NOW", and
/// neither waits for a recharging one (its level 3 wait is commented out for the pile-ups it made).</item>
/// <item>The exit is the entrance's builder's, where the bot last saw it, however long ago: the pair is one builder's
/// (stock's FindMatch), a player knows where an exit is from having seen it, and a ready entrance proves its exit stands
/// (the port's Ready wants a matched, built, unsapped exit). Only the place can be stale, and a wrong belief costs one
/// ride. Live on cp_granary a Soldier's life outlasted a 120 s memory, and no Soldier weighed an entrance it passed.</item>
/// <item>Only the bot's own team's (stock's CollectBuiltObjects of the bot's team; an enemy's sends only its own team).</item>
/// <item>Stock's ShouldOpportunisticallyTeleport: never a Medic; an Engineer only once its own entrance stands, as his own
/// HUD tray shows it (<see cref="TFBuildingTrayMeters"/>). Never with the flag (TF2 refuses a carrier the trip).</item>
/// <item>On the way: an entrance seen recharging or not ready is given up (stock's USE_IF_READY "Teleporter is not
/// available"), as is one not reached and ridden in <see cref="TeleporterPatienceSeconds"/>; a given-up entrance is not
/// tried for <see cref="TeleporterSkipSeconds"/>.</item>
/// <item>The choice is weighed again every <see cref="TeleporterSearchSeconds"/> (stock's 1-2 s timer) or when the goal
/// moves, by the walking graph's own route costs.</item>
/// </list>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>A friendly entrance within this of the bot is weighed (stock's surrounding area, tf_bot_tactical_monitor.cpp:528).</summary>
    public const float TeleporterReach = 1000f;

    /// <summary>The choice is weighed again this often (stock's <c>RandomFloat(1, 2)</c>, its mean), or when the goal moves this far.</summary>
    public const float TeleporterSearchSeconds = 1.5f, TeleporterGoalMoved = 256f;

    /// <summary>
    /// An entrance seen ready this long ago is weighed as ready now: the search's beat and a decide, so a sighting between two
    /// weighs is not missed (live on cp_granary, Soldiers walking past an entrance saw it ready on 2 decides and weighed it on none).
    /// </summary>
    public const float TeleporterGlimpseSeconds = 2f;

    /// <summary>The trip must save at least this much of the walk (ours: a trip that saves less is a coin flip over the stop on the pad).</summary>
    public const float TeleporterSaveSeconds = 1f;

    /// <summary>An entrance not reached and ridden in this long is given up for <see cref="TeleporterSkipSeconds"/>.</summary>
    public const float TeleporterPatienceSeconds = 12f, TeleporterSkipSeconds = 30f;

    /// <summary>
    /// The seconds the trip itself takes: the stop on the pad (a booked rider is sent once it stands still), the send's beat
    /// and the two fades (TFTeleporter's <c>SendDelay</c> and <c>FadeTime</c>, tf_obj_teleporter.cpp:126-127).
    /// </summary>
    public const float TeleporterTripSeconds = TeleporterStopSeconds + TFTeleporter.SendDelay + 2f * TFTeleporter.FadeTime;

    /// <summary>The seconds a running bot takes to come to a stand on the pad (ours: a TF2 body stops from its run in about this).</summary>
    public const float TeleporterStopSeconds = 0.3f;

    /// <summary>A step longer than this in one decide while going for an entrance is the trip: where the bot stands is where the exit is.</summary>
    public const float TeleporterJumpUnits = 300f;

    /// <summary>The cells one walk's search may expand (each of a weigh's three); its answer past them is a lower bound.</summary>
    public const int TeleporterSearchBudget = 60_000;

    /// <summary>The walking graph's route costs, one service for every bot (<see cref="Nav.TFWalkCosts"/>); with none, no bot takes a teleporter.</summary>
    public Nav.TFWalkCosts? Walks { get; init; }
    private Tick _teleWeighedAt = new(-1_000_000), _teleSince, _teleSkipUntil;
    private AiVector _teleGoalWeighed, _teleEntrance, _teleExit, _teleLastAt;
    private int _teleEntranceId, _teleSkipId;
    private bool _teleChosen, _teleGoing;

    /// <summary>Trips taken (the bot came out at the exit it went for), and entrances given up, for the A/B harness and the facts.</summary>
    public int TeleporterTrips { get; private set; }

    public int TeleporterGiveUps { get; private set; }

    /// <summary>
    /// Entrances weighed (in sight, ready, in reach, an exit known) and the last one's saving in units (the walk less the way by
    /// it and the margin; over 0 it was taken), for the harness and the inspector: whether a bot that rode none saw none.
    /// </summary>
    public int TeleporterWeighs { get; private set; }

    public float TeleporterLastSaving { get; private set; }

    /// <summary>The weighs' searches (kept answers not counted), the cells they expanded, their microseconds, and the dearest weigh's microseconds.</summary>
    public long TeleporterSearches { get; private set; }

    public long TeleporterExpanded { get; private set; }

    public long TeleporterSearchMicros { get; private set; }

    public long TeleporterWeighMostMicros { get; private set; }

    /// <summary>Entrances glimpsed ready and passed over: out of reach, no exit known, or a leg of the way not found within the cap.</summary>
    public int TeleporterSkipsFar { get; private set; }

    public int TeleporterSkipsNoExit { get; private set; }

    public int TeleporterSkipsNoWalk { get; private set; }

    /// <summary>Whether the bot's way this decide goes by a teleporter, and to which entrance.</summary>
    internal (bool Going, AiVector Entrance) TeleporterWay => (_teleGoing, _teleEntrance);

    /// <summary>
    /// Whether the class takes a friendly teleporter on its own account: stock's ShouldOpportunisticallyTeleport
    /// (tf_bot_tactical_monitor.cpp:491-509), never a Medic, an Engineer once its own entrance stands.
    /// </summary>
    private bool TakesTeleporters => _class switch
    {
        TFClass.Medic => false,
        TFClass.Engineer => OwnEntranceStands,
        _ => true,
    };

    /// <summary>Whether the bot's own entrance stands, as its HUD tray shows it: the tray has a row for it, sapped or not.</summary>
    private bool OwnEntranceStands => Kit?.Meter(TFBuildingTrayMeters.EntranceSapped, -1f) >= 0f;

    /// <summary>
    /// Where the bot walks for <paramref name="goal"/>: the entrance of a friendly teleporter whose trip saves time, or the goal.
    /// Also notes a trip made (the bot came out by the exit) and gives up an entrance seen unready.
    /// </summary>
    private AiVector ByTeleporter(AiVector goal)
    {
        if (Carrying || Walks is null || !TakesTeleporters || _buildingCount == 0)
            return Unchosen(goal);
        if (_teleChosen && !EntranceStillUsable())
            return Unchosen(goal);
        if (_teleChosen && _teleGoing && SecondsSince(_teleSince) > TeleporterPatienceSeconds)
        {
            GiveUpEntrance();
            return Unchosen(goal);
        }

        // Weighed at most once a decide, and again on the search's beat or a moved goal. A second goal in a decide that weighed
        // another (a stand spot beside the branch's goal) is walked to plainly, the way by the teleporter left as it was.
        bool moved = goal.DistanceTo(_teleGoalWeighed) > TeleporterGoalMoved;
        if (moved && _teleWeighedAt == _now)
            return goal;
        if (moved || SecondsSince(_teleWeighedAt) >= TeleporterSearchSeconds)
            WeighTeleporters(goal);
        if (!_teleChosen)
            return Unchosen(goal);
        if (!_teleGoing)
            (_teleGoing, _teleSince) = (true, _now);
        return _teleEntrance;
    }

    private AiVector Unchosen(AiVector goal)
    {
        (_teleChosen, _teleGoing) = (false, false);
        return goal;
    }

    /// <summary>Whether the entrance gone for may still be ridden: not seen gone unready (stock's USE_IF_READY), and not skipped.</summary>
    private bool EntranceStillUsable()
    {
        foreach (KnownBuilding b in FriendlyBuildings)
        {
            if (b.Id != _teleEntranceId)
                continue;
            if (b.Last == _now && (b.Flags & (int)TFBuildingFlags.Ready) == 0)
            {
                GiveUpEntrance();
                return false;
            }

            return true;
        }

        return false;
    }

    private void GiveUpEntrance()
    {
        (_teleSkipId, _teleSkipUntil) = (_teleEntranceId, _now + (int)(TeleporterSkipSeconds / _tick));
        (_teleChosen, _teleGoing) = (false, false);
        TeleporterGiveUps++;
    }

    /// <summary>
    /// A step past <see cref="TeleporterJumpUnits"/> while going for an entrance is a trip made (a death ends the going first):
    /// the exit is where the bot came out, so its builder's exit is remembered there, as the bot now sees it. Once a decide,
    /// before the tree.
    /// </summary>
    private void NoteTrip()
    {
        AiVector at = _self.Origin;
        float step = at.DistanceTo(_teleLastAt);
        _teleLastAt = at;
        if (!_teleGoing || step < TeleporterJumpUnits)
            return;
        TeleporterTrips++;
        for (int i = 0; i < _buildingCount; i++)
        {
            if (_buildings[i].Type == TFThingType.TeleporterExit && _buildings[i].At.DistanceTo(_teleExit) <= 1f)
                _buildings[i] = _buildings[i] with { At = at, Last = _now };
        }

        (_teleChosen, _teleGoing) = (false, false);
    }

    /// <summary>The bot died: a way by a teleporter is over.</summary>
    private void TeleporterDied() => (_teleChosen, _teleGoing) = (false, false);

    /// <summary>
    /// Weighs every friendly entrance the bot saw ready within reach against the walk to <paramref name="goal"/>: the walk
    /// from where the bot stands (a lower bound when the cap stops it), and the way by the entrance, whose two legs must be
    /// found whole.
    /// </summary>
    private void WeighTeleporters(AiVector goal)
    {
        (bool wasChosen, int wasId) = (_teleChosen, _teleEntranceId);
        (_teleWeighedAt, _teleGoalWeighed, _teleChosen) = (_now, goal, false);
        float best = float.PositiveInfinity;
        float speed = RunSpeed;
        long weighMicros = 0;
        TFWalk? direct = null;
        foreach (KnownBuilding entrance in FriendlyBuildings)
        {
            // Seen ready within the search's beat (a bot passing an entrance has it in view a moment, between two weighs), or the
            // one already gone for as last seen (a glance away on the way does not lose it).
            bool seen = SecondsSince(entrance.Last) <= TeleporterGlimpseSeconds || (wasChosen && entrance.Id == wasId);
            if (entrance.Type != TFThingType.TeleporterEntrance || !seen || (entrance.Flags & (int)TFBuildingFlags.Ready) == 0
                || (entrance.Id == _teleSkipId && _now < _teleSkipUntil))
                continue;
            if (_self.Origin.DistanceTo(entrance.At) > TeleporterReach)
            {
                TeleporterSkipsFar++;
                continue;
            }

            if (!ExitOf(in entrance, out AiVector exit))
            {
                TeleporterSkipsNoExit++;
                continue;
            }

            direct ??= Walk(_self.Origin, goal, ref weighMicros);
            TFWalk toEntrance = Walk(_self.Origin, entrance.At, ref weighMicros), fromExit = Walk(exit, goal, ref weighMicros);
            if (!toEntrance.Whole || !fromExit.Whole || !float.IsFinite(toEntrance.Cost + fromExit.Cost) || !float.IsFinite(direct.Value.Cost))
            {
                TeleporterSkipsNoWalk++;
                continue;
            }

            float walk = direct.Value.Cost;
            float via = toEntrance.Cost + TeleporterTripSeconds * speed + fromExit.Cost;
            (TeleporterWeighs, TeleporterLastSaving) = (TeleporterWeighs + 1, walk - via - TeleporterSaveSeconds * speed);
            if (via + TeleporterSaveSeconds * speed < walk && via < best)
                (best, _teleChosen, _teleEntrance, _teleEntranceId, _teleExit) = (via, true, entrance.At, entrance.Id, exit);
        }

        TeleporterWeighMostMicros = Math.Max(TeleporterWeighMostMicros, weighMicros);
    }

    /// <summary>The exit of <paramref name="entrance"/>'s builder the bot saw last, wherever it saw it (the entrance is ready, so an exit stands).</summary>
    private bool ExitOf(in KnownBuilding entrance, out AiVector exit)
    {
        exit = default;
        Tick last = new(int.MinValue);
        bool found = false;
        foreach (KnownBuilding b in FriendlyBuildings)
        {
            if (b.Type == TFThingType.TeleporterExit && entrance.Owner > 0 && b.Owner == entrance.Owner && b.Last > last)
                (exit, last, found) = (b.At, b.Last, true);
        }

        return found;
    }

    /// <summary>
    /// The walk from <paramref name="from"/> to <paramref name="to"/> by the shared service, capped at
    /// <see cref="TeleporterSearchBudget"/>, its work counted (into <paramref name="micros"/> too); the straight line with no graph.
    /// </summary>
    private TFWalk Walk(AiVector from, AiVector to, ref long micros)
    {
        if (_graph is null || Walks is null)
            return new TFWalk(from.DistanceTo(to), true, 0, 0, false);
        TFWalk walk = Walks.Walk(_graph, from, to, TeleporterSearchBudget, _now.Value);
        if (!walk.Kept)
            TeleporterSearches++;
        (TeleporterExpanded, TeleporterSearchMicros) = (TeleporterExpanded + walk.Expanded, TeleporterSearchMicros + walk.Microseconds);
        micros += walk.Microseconds;
        return walk;
    }
}
