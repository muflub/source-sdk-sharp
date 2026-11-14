namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>
/// The HL2MP trees' team layer (plan_bots_teams_tf2 B.4, B.5): what a bot on a team
/// says to its teammates. Nothing here runs without a team: a deathmatch bot's
/// decides, and so its recordings, are as they were.
/// </summary>
public sealed partial class Hl2mpDecider
{
    /// <summary>At most one callout per bot this often (B.4: 3 s).</summary>
    public const float CalloutCooldownSeconds = 3f;

    /// <summary>A pickup this near is the place a callout names; else the room-sized square it is in.</summary>
    public const float CalloutPickupReach = 512f, CalloutSquare = 256f;

    /// <summary>Enemies this near the called one are counted with it.</summary>
    public const float CalloutGroup = 512f;

    private readonly AiRandom _calloutRng;
    private SayIntent _say;
    private Tick _lastCallout;
    private ulong _sightedEnemies;

    /// <summary>Callouts this bot has said, for the facts and the inspector.</summary>
    public int CalloutsSaid { get; private set; }

    /// <summary>Whether a teammate is in the game as far as the bot knows: someone to tell.</summary>
    private bool HasTeammate()
    {
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && _memory.IsFriend(a))
                return true;
        }

        return false;
    }

    /// <summary>
    /// An enemy newly in sight is called out to the team (B.4): a place (the pickup
    /// nearest it, else its square), how many are there, after the bot's own delay,
    /// at most once per <see cref="CalloutCooldownSeconds"/>, and not every time.
    /// </summary>
    private void Callout()
    {
        ulong now = 0;
        AiVector at = default;
        int subject = -1;
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (!TeamRule.IsGameTeam(_self.Team) || TeamRule.Friends(_self.Team, f.Team) || (uint)f.Id.Slot >= 64u)
                continue;
            ulong bit = 1UL << f.Id.Slot;
            now |= bit;
            if ((_sightedEnemies & bit) != 0 || subject >= 0)
                continue;
            // Each new sighting draws whether it is worth a word.
            // B.4's one in three unsaid, by Teamwork (B.7): calloutChance 0.45 to 0.85.
            if (_calloutRng.NextFloat() >= _p.CalloutChance)
                continue;

            (subject, at) = (f.Id.Slot, f.Origin);
        }

        _sightedEnemies = now;
        if (subject < 0 || _say.Says || !_self.Alive || (_lastCallout.Value > 0 && (_now - _lastCallout) * _tick < CalloutCooldownSeconds) || !HasTeammate())
            return;
        int count = 0;
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (!TeamRule.Friends(_self.Team, f.Team) && f.Origin.DistanceTo(at) <= CalloutGroup)
                count++;
        }

        Say(CalloutKind.Enemy, CalloutPlace(at), count, subject + 1);
    }

    /// <summary>Says <paramref name="kind"/> this decide, after the bot's own delay.</summary>
    private void Say(CalloutKind kind, AiVector place, int count, int subject)
    {
        int delay = Math.Max(AiSession.CalloutMinDelayTicks, (int)MathF.Ceiling(_p.CalloutDelayMs / 1000f / _tick));
        _say = new SayIntent(kind, place, count, subject, delay, _self.Team, Text: true);
        _lastCallout = _now;
        CalloutsSaid++;
    }

    /// <summary>A place a person would name (B.4): the pickup nearest, else the middle of the square it stands in. Never the exact spot.</summary>
    private AiVector CalloutPlace(AiVector at)
    {
        float best = CalloutPickupReach;
        AiVector? pickup = null;
        foreach (ItemSpawn i in _map.Items)
        {
            float d = i.Position.DistanceTo(at);
            if (d < best)
                (best, pickup) = (d, i.Position);
        }

        if (pickup is { } p)
            return p;
        static float Mid(float v) => (MathF.Floor(v / CalloutSquare) + 0.5f) * CalloutSquare;
        return new AiVector(Mid(at.X), Mid(at.Y), at.Z);
    }

    // ---------------------------------------------------------------- roles (B.5)

    /// <summary>A teammate this near counts as with the bot (Stick's reach, B.5).</summary>
    public const float StickReach = 1024f;

    /// <summary>A trade is taken within this of the teammate's death.</summary>
    public const float TradeSeconds = 2f;

    /// <summary>A callout is a fight to cover for this long after it arrives.</summary>
    public const float CoverCallSeconds = 4f;

    /// <summary>Cover stands this far to the side of the line from the bot to the called place.</summary>
    public const float CoverSide = 384f;

    /// <summary>No progress: losing on the team scores with no team kill for this long (B.5: 90 s).</summary>
    public const float NoProgressTeamSeconds = 90f;

    /// <summary>Rally: the least Teamwork that calls one (B.5: 4), the most that ignores one (2).</summary>
    public const float RallyCallTeamwork = 4f, RallyIgnoreTeamwork = 2f;

    /// <summary>A rally pushes when this many stand within <see cref="RallyGatherUnits"/>, or when its time runs out.</summary>
    public const int RallyGathered = 3;

    public const float RallyGatherUnits = 384f;

    /// <summary>B.5's rallyTimeoutS and rallyCooldownS, and the jitter a team's seed draws on the timeout.</summary>
    public const float RallyTimeoutSeconds = 25f, RallyCooldownSeconds = 120f, RallyJitterSeconds = 10f;

    /// <summary>A push call is gone for after it is heard for this long.</summary>
    public const float PushSeconds = 12f;

    /// <summary>
    /// The team role's utilities against the tree's (B.5). Provisional numbers, to be set by
    /// the tdm jank run's measurement (B.5's last line); none of them can outrank a fight in sight.
    /// </summary>
    public const float StickBase = 0.12f, StickTeamwork = 0.3f, CoverBase = 0.35f, RallyUtility = 0.62f, PushUtility = 0.66f;

    private string _teamRole = "";
    private Tick _pushCalledAt;

    /// <summary>Decides on which a team role overrode the tree, by role, for the facts and the inspector.</summary>
    public int TeamRoleDecides { get; private set; }

    /// <summary>The team role this decide ("" none): Stick, Cover, Trade, Regroup, Push.</summary>
    public string TeamRole => _teamRole;

    private float Teamwork => _p.Teamwork;

    /// <summary>A former teammate just turned enemy (B.9) is a target only after the bot's recognition time: a person needs the moment too.</summary>
    private bool NotYetRecognised(KnownActor a)
        => a.FoeSince.Value > 0 && (_now - a.FoeSince) * _tick < Math.Max(_p.RecognizeSeconds, MinRecogniseSeconds);

    /// <summary>The least a swap takes to recognise, whatever the curve says.</summary>
    public const float MinRecogniseSeconds = 0.25f;

    private Tick _ownSwapSeen;

    /// <summary>The bot itself was moved (B.9): its role and goal end, as a new life's would.</summary>
    private void NoteOwnSwap()
    {
        if (_memory.OwnSwap == _ownSwapSeen)
            return;
        _ownSwapSeen = _memory.OwnSwap;
        _tree.Reset();
        _goalHeld = false;
        _heldFor = _lastTarget = null;
        _ambushUntil = default;
        _approachOf = ActorId.None;
    }

    /// <summary>
    /// The team layer over the tree (B.5), after the tree has decided: a deathmatch bot's
    /// decide is never touched. Trade comes first (a teammate just died in sight to an enemy
    /// in sight); otherwise nothing interrupts a fight or a retreat to heal, and a role takes
    /// the move only when its utility beats the branch the tree chose.
    /// </summary>
    private void TeamRoles()
    {
        _teamRole = "";
        if (!TeamRule.IsGameTeam(_self.Team) || !_self.Alive)
            return;
        string chosen = _root.Chosen?.Name ?? "";
        if (chosen == "Survive")
            return;
        if (Trade())
            return;
        CallRally();
        if (chosen == "Fight")
            return;

        float beat = _root.Chosen is null ? 0f : Utility(_root.Chosen);
        AiVector goal = default;
        string role = "";
        float best = beat;
        MoveSpeed speed = MoveSpeed.Run;
        void Offer(string name, float utility, AiVector at, MoveSpeed how)
        {
            if (utility > best)
                (best, role, goal, speed) = (utility, name, at, how);
        }

        if (OrderPlace(out AiVector ordered, out string order))
            Offer(order, OrderUtility, ordered, order == "hold" ? MoveSpeed.Walk : MoveSpeed.Run);
        if (PushPlace(out AiVector push))
            Offer("push", PushUtility, push, MoveSpeed.Run);
        if (RallyPlace(out AiVector rally))
            Offer("regroup", RallyUtility, rally, MoveSpeed.Run);
        if (CoverPlace(out AiVector cover))
            Offer("cover", CoverBase + 0.3f * _p.TeamCoverWeight, cover, MoveSpeed.Run);
        if (StickPlace(out AiVector stick))
            Offer("stick", StickBase + StickTeamwork * Teamwork / StandardSkills.Cap, stick, MoveSpeed.Walk);
        if (role.Length == 0)
            return;
        _teamRole = role;
        _leaf = role;
        _verdict = "team: " + role;
        GoTo(goal, speed, MoveKind.Follow);
        TeamRoleDecides++;
    }

    /// <summary>Trade (B.5): the enemy that just killed a teammate in sight is pushed now, while it is likely reloading or hurt.</summary>
    private bool Trade()
    {
        (ActorId killer, Tick when) = _memory.TradeChance;
        if (!killer.IsValid || (_now - when) * _tick > TradeSeconds || !_view.TryActor(killer, out ActorFacts f)
            || TeamRule.Friends(_self.Team, f.Team) || TradeDraw() >= _p.TradeWeight)
            return false;
        if (_memory.Board.Of(killer, make: false) is not { } k)
            return false;
        (_target, _targetSighted, _targetPos, _targetVel, _targetDist) = (k, true, f.Origin, f.Velocity, _self.Origin.DistanceTo(f.Origin));
        _teamRole = "trade";
        _leaf = "trade";
        _verdict = "team: trade";
        GoTo(f.Origin, MoveSpeed.Run, MoveKind.Follow);
        LookAtTargetAndFire();
        FriendGate();
        TeamRoleDecides++;
        return true;
    }

    private Tick _tradeRolled;
    private float _tradeRoll;

    /// <summary>One draw per trade chance, so a bot of middling Teamwork trades some and not others, the same in a replay.</summary>
    private float TradeDraw()
    {
        Tick when = _memory.TradeChance.When;
        if (when != _tradeRolled)
        {
            _tradeRolled = when;
            _tradeRoll = _calloutRng.NextFloat();
        }

        return _tradeRoll;
    }

    /// <summary>Stick (B.5): beyond its leash from the nearest teammate known within reach, the bot walks back toward them.</summary>
    private bool StickPlace(out AiVector at)
    {
        at = default;
        float nearest = StickReach;
        bool found = false;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || !_memory.IsFriend(a) || !a.EverSeen || a.Confidence < 0.2f)
                continue;
            float d = _self.Origin.DistanceTo(a.LastSeenPos);
            if (d < nearest)
                (nearest, at, found) = (d, a.LastSeenPos, true);
        }

        if (!found || nearest <= _p.LeashUnits)
            return false;
        // Toward them, stopping at half the leash: walk, do not shadow.
        AiVector to = at - _self.Origin;
        at = _self.Origin + to * ((nearest - _p.LeashUnits * 0.5f) / nearest);
        return true;
    }

    /// <summary>Cover (B.5): a teammate's fight called out a moment ago is seen from another angle: a point to the side of the line to it.</summary>
    private bool CoverPlace(out AiVector at)
    {
        at = default;
        BotMemory.HeardCallout call = _memory.LastEnemyCall;
        if (call.Kind != CalloutKind.Enemy || (_now - call.Tick) * _tick > CoverCallSeconds || _p.TeamCoverWeight <= 0f)
            return false;
        AiVector line = new AiVector(call.Place.X - _self.Origin.X, call.Place.Y - _self.Origin.Y, 0f);
        float length = line.Length;
        if (length < 1f)
            return false;
        AiVector side = new AiVector(-line.Y, line.X, 0f) * (1f / length);
        // The side away from the teammate who called it, by its slot: the two approach from two angles.
        float sign = (call.Speaker.Slot & 1) == (_bot.Slot & 1) ? -1f : 1f;
        at = call.Place + side * (CoverSide * sign);
        return true;
    }

    /// <summary>Regroup (B.5): a rally heard, not too late for this bot's Teamwork and not ignored by it.</summary>
    private bool RallyPlace(out AiVector at)
    {
        BotMemory.HeardCallout rally = _memory.LastRally;
        at = rally.Place;
        if (rally.Kind != CalloutKind.Rally || Teamwork <= RallyIgnoreTeamwork || _memory.LastPush.Tick > rally.Tick)
            return false;
        float since = (_now - rally.Tick) * _tick;
        // A low-Teamwork bot comes late (B.5): its followSeconds' shortfall from the top.
        float late = (StandardSkills.Cap - Teamwork) * 0.6f;
        return since >= late && since <= RallyTimeoutSeconds + RallyJitterSeconds;
    }

    /// <summary>Push (B.5): a push heard a moment ago goes to where the team last called the enemy, else the rally's place onward.</summary>
    private bool PushPlace(out AiVector at)
    {
        BotMemory.HeardCallout push = _memory.LastPush;
        at = _memory.LastEnemyCall.Kind == CalloutKind.Enemy ? _memory.LastEnemyCall.Place : push.Place;
        return push.Kind == CalloutKind.Push && (_now - push.Tick) * _tick <= PushSeconds && Teamwork > RallyIgnoreTeamwork;
    }

    /// <summary>
    /// The no-progress call (B.5): losing on the team scores with no team kill for 90 s, a bot of
    /// Teamwork 4 or more that is the lowest slot of its known team calls a rally at its team's
    /// believed centre; at the rally, three gathered or its time run out, it calls the push.
    /// </summary>
    private void CallRally()
    {
        if (_say.Says)
            return;
        BotMemory.HeardCallout rally = _memory.LastRally;
        if (rally.Kind == CalloutKind.Rally && rally.Speaker == _bot && _memory.LastPush.Tick < rally.Tick)
        {
            float timeout = RallyTimeoutSeconds + Jitter(rally.Tick);
            if (Gathered(rally.Place) >= RallyGathered || (_now - rally.Tick) * _tick >= timeout)
            {
                Say(CalloutKind.Push, rally.Place, 0, 0);
                _memory.Said(CalloutKind.Push, rally.Place, _now);
                _pushCalledAt = _now;
            }

            return;
        }

        if (Teamwork < RallyCallTeamwork || !NoTeamProgress() || !LowestOfTeam()
            || (_pushCalledAt.Value > 0 && (_now - _pushCalledAt) * _tick < RallyCooldownSeconds)
            || (_memory.LastPush.Kind == CalloutKind.Push && (_now - _memory.LastPush.Tick) * _tick < RallyCooldownSeconds))
            return;
        AiVector place = CalloutPlace(TeamCentre());
        Say(CalloutKind.Rally, place, 0, 0);
        _memory.Said(CalloutKind.Rally, place, _now);
    }

    /// <summary>±<see cref="RallyJitterSeconds"/> drawn from the rally's own tick and the team (B.5: so it does not look scripted).</summary>
    private float Jitter(Tick calledAt)
    {
        ulong h = (ulong)(uint)calledAt.Value * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)_self.Team * 0xC2B2AE3D27D4EB4FUL;
        h ^= h >> 29;
        return ((h % 2001UL) / 1000f - 1f) * RallyJitterSeconds;
    }

    /// <summary>Not leading on the team scores the HUD shows (some team, of two to four, is ahead), with no team kill in the last 90 s.</summary>
    private bool NoTeamProgress()
    {
        if (!_memory.Board.Behind(_self.Team))
            return false;
        Tick last = _memory.LastTeamKill;
        return (_now - last) * _tick >= NoProgressTeamSeconds;
    }

    /// <summary>Whether no teammate the bot knows has a lower slot: the one caller a team has, the same at any thread count.</summary>
    private bool LowestOfTeam()
    {
        Blackboard board = _memory.Board;
        for (int s = 0; s < _bot.Slot && s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && _memory.IsFriend(a))
                return false;
        }

        return true;
    }

    /// <summary>Where the bot believes its team is: itself and the teammates it has seen lately.</summary>
    private AiVector TeamCentre()
    {
        AiVector sum = _self.Origin;
        int n = 1;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && !a.Dead && _memory.IsFriend(a) && a.EverSeen && a.Confidence >= 0.2f)
            {
                sum += a.LastSeenPos;
                n++;
            }
        }

        return sum * (1f / n);
    }

    /// <summary>How many of the team stand within <see cref="RallyGatherUnits"/> of <paramref name="place"/>: the bot and the teammates it sees there.</summary>
    private int Gathered(AiVector place)
    {
        int n = _self.Origin.DistanceTo(place) <= RallyGatherUnits ? 1 : 0;
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (TeamRule.Friends(_self.Team, f.Team) && f.Origin.DistanceTo(place) <= RallyGatherUnits)
                n++;
        }

        return n;
    }

    // ---------------------------------------------------------------- orders (B.5, Q5)

    /// <summary>An order outranks every role but a fight in sight or a retreat to heal.</summary>
    public const float OrderUtility = 0.75f;

    /// <summary>Following, the bot keeps between these of the one it follows (B.5: 200-400 u).</summary>
    public const float FollowNear = 200f, FollowFar = 400f;

    /// <summary>Under this Teamwork a bot ignores orders, as it ignores a rally.</summary>
    public const float OrderIgnoreTeamwork = RallyIgnoreTeamwork;

    private Tick _holdOrderSeen;
    private AiVector _holdAt;

    /// <summary>
    /// A teammate's order (B.5): "follow me" keeps 200-400 u of them for followSeconds, "help"
    /// goes to them to fight beside them, "hold" stays where it was told, "go" releases. Where
    /// they are is where the bot last saw them: it follows what it sees.
    /// </summary>
    private bool OrderPlace(out AiVector at, out string role)
    {
        at = default;
        role = "";
        (OrderKind kind, ActorId from, Tick when) = _memory.LastOrder;
        if (kind is OrderKind.None or OrderKind.Go || Teamwork <= OrderIgnoreTeamwork || (_now - when) * _tick > _p.FollowSeconds)
            return false;
        if (kind == OrderKind.Hold)
        {
            if (when != _holdOrderSeen)
                (_holdOrderSeen, _holdAt) = (when, _self.Origin);
            at = _holdAt;
            role = "hold";
            return true;
        }

        if (_memory.Board.Of(from, make: false) is not { EverSeen: true } leader || !_memory.IsFriend(leader))
            return false;
        AiVector them = _view.TryActor(from, out ActorFacts f) ? f.Origin : leader.LastSeenPos;
        float d = _self.Origin.DistanceTo(them);
        role = kind == OrderKind.Help ? "help" : "follow";
        if (kind == OrderKind.Follow && d <= FollowFar)
        {
            // Close enough: stand by them rather than on them.
            at = d < FollowNear ? _self.Origin : _self.Origin + (them - _self.Origin) * ((d - FollowNear) / Math.Max(d, 1f));
            return true;
        }

        at = kind == OrderKind.Help || d <= FollowFar ? them : _self.Origin + (them - _self.Origin) * ((d - FollowNear) / d);
        return true;
    }
}
