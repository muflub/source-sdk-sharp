namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The bot's team role from the allocator (plan_bots_teams_tf2_part_g G.2.2, <see cref="RoleDeal"/>),
/// where it was its slot's remainder for the life of the slot. The bot deals for itself, from what
/// a player on its team knows: who is on the team (its scoreboard), where each flag is and who
/// carries one (its HUD's objectives), and what the team's people are doing, as far as it has
/// seen them lately (one by the team's flag defends, one by the carrier escorts). The split is
/// the policy it was started with (<see cref="StartRoles"/>), never restated here.
///
/// <para>It deals again on a flag event (a flag taken, dropped, returned or captured), when the
/// team's size changes, and every <see cref="RedealSeconds"/> for what the people have turned
/// to; a role is held for <see cref="RoleHold.HoldSeconds"/> before a deal may change it (but an
/// attacker dealt the escort goes with the new carrier at once). Until
/// its scoreboard shows it a teammate there is nothing to share out, and it plays what the policy
/// makes of a team of one (<see cref="StartRole"/>), whatever its slot.</para>
///
/// <para>The tree's utilities read two roles, so an escort reads as an attacker there
/// (<see cref="RoleRead"/>); what makes it an escort is the weight on the Escort branch
/// (<see cref="RoleWeight"/>): the dealt escort's stands as the tree scored it, and once roles
/// are dealt nobody else's is worth leaving its own job for (<see cref="NotTheEscort"/>). Under
/// the CTF policy (<see cref="CtfRoleSplit"/>) every bot but the defenders and the carrier is
/// dealt the escort, so the defenders are the ones kept home. A carrier who calls for help is
/// worth the escort's score to anyone who hears it (<see cref="CarrierCalled"/>).</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>With no flag event the bot deals again this often, for what the team's people have turned to.</summary>
    public const float RedealSeconds = 15f;

    /// <summary>A person seen within this long is taken to be doing what they were seen doing; longer ago, to attack.</summary>
    public const float PersonSeenSeconds = 20f;

    /// <summary>A person this near the team's flag at home defends it; one this near the flag a teammate carries escorts.</summary>
    public const float PersonDefendsWithin = 900f, PersonEscortsWithin = 600f;

    /// <summary>What is left of the Escort score of a bot not dealt the escort, once roles are dealt; the dealt escort's is as the tree scored it.</summary>
    public const float NotTheEscort = 0.15f;

    private readonly RoleHold _roleHold = new();
    private RoleSplit _split = RoleSplits.ThirdDefend;
    private RoleMember[] _team = [];
    private bool _rolesRead;
    private Tick _rolesAt, _nextDeal, _swapSeen;
    private int _dealKey;
    private AiVector _ownFlagHome;
    private bool _ownFlagHomeKnown;

    /// <summary>The allocator's start, the constructor's: the split policy the bot deals by.</summary>
    internal void StartRoles(RoleSplit split) => _split = split;

    /// <summary>The role the allocator has the bot in this decide: the deal it holds, else its start.</summary>
    internal TeamRole Dealt
    {
        get
        {
            DealRoles();
            return _roleHold.Dealt ? _roleHold.Role : StartRole;
        }
    }

    /// <summary>Whether the bot has been dealt a role (it knows of a teammate), for the facts and the inspector.</summary>
    internal bool RolesDealt => _roleHold.Dealt;

    /// <summary>How often a deal changed the bot's role, and how many deals it worked out, for the facts and the counters.</summary>
    internal int RoleFlips => _roleHold.Flips;

    internal int RoleDeals { get; private set; }

    /// <summary>
    /// The role as the tree's utilities read it: a defender, or an attacker (an escort is an attacker told to go with the
    /// carrier). The class has its say over the dealt role (P7, <see cref="ClassPlay.Role"/>): by default it plays the one dealt.
    /// </summary>
    internal TFTeamRole RoleRead => ClassRole(Dealt == TeamRole.Defend ? TFTeamRole.Defend : TFTeamRole.Attack);

    private TFTeamRole ClassRole(TFTeamRole dealt) => _classPlay is null ? dealt : _classPlay.Role(dealt);

    /// <summary>The role of a bot that knows of no teammate: what the policy makes of a team of one (the CTF policy's has no defender), not its slot's remainder.</summary>
    private TeamRole StartRole => _split(1).Defend > 0 ? TeamRole.Defend : TeamRole.Attack;

    private static bool IsPerson(ActorId id) => (id.Serial & AiThreat.HumanSerialBit) != 0;

    /// <summary>
    /// The role's weight on <paramref name="branch"/>'s score, <see cref="Weigh"/>'s: once roles
    /// are dealt, going with the carrier is the dealt escort's job (its score stands) and not
    /// worth another's while.
    /// </summary>
    private float RoleWeight(string branch)
    {
        if (branch != Escort)
            return 1f;
        TeamRole role = Dealt;
        if (role == TeamRole.Escort || CarrierCalled)
            return 1f;
        return _roleHold.Dealt ? NotTheEscort : 1f;
    }

    /// <summary>
    /// Whether the carrier has called for help (the voice menu, in: "Help!" or "Medic!", heard as
    /// <see cref="BotMemory.LastHelpCall"/>) within the bot's Teamwork's <c>followSeconds</c>: the
    /// call came from the carrier, or from beside the flag it carries. Then going with the carrier
    /// is worth the escort's score (the tree's own) to this bot too, whatever it was dealt.
    /// </summary>
    private bool CarrierCalled
    {
        get
        {
            BotMemory.HeardCallout call = _memory.LastHelpCall;
            return call.Kind == CalloutKind.Help && (_now - call.Tick) * _tick < _p.FollowSeconds
                   && _enemyFlag is { State: ObjectiveState.Carried } flag && flag.CarrierSlot != _bot.Slot
                   && (call.Speaker.Slot == flag.CarrierSlot || call.Place.DistanceTo(flag.Position) < PersonEscortsWithin);
        }
    }

    /// <summary>Works the bot's deal out when something it is made from has changed; once a decide at most.</summary>
    private void DealRoles()
    {
        if (_rolesRead && _rolesAt == _now)
            return;
        (_rolesRead, _rolesAt) = (true, _now);
        if (!TeamRule.IsGameTeam(_self.Team))
            return;
        if (_memory.OwnSwap != _swapSeen)
        {
            // Another team: another flag, another roster.
            _swapSeen = _memory.OwnSwap;
            _roleHold.Reset();
            _ownFlagHomeKnown = false;
        }

        if (_ownFlag is { State: ObjectiveState.Home } home)
            (_ownFlagHome, _ownFlagHomeKnown) = (home.Position, true);

        Blackboard board = _memory.Board;
        if (_team.Length == 0)
            _team = new RoleMember[board.Slots + 1];
        int n = 0;
        _team[n++] = new RoleMember(_bot.Slot, false);
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && a.Id != _bot && _memory.IsFriend(a))
                _team[n++] = new RoleMember(a.Id.Slot, IsPerson(a.Id));
        }

        if (n < 2)
            return;
        int carrier = _enemyFlag is { State: ObjectiveState.Carried } taken ? taken.CarrierSlot : 0;
        int key = n | ((int)(_ownFlag?.State ?? 0) << 8) | ((int)(_enemyFlag?.State ?? 0) << 12) | (carrier << 16);
        if (_roleHold.Dealt && key == _dealKey && _now < _nextDeal)
            return;

        for (int i = 1; i < n; i++)
        {
            if (_team[i].Human)
                _team[i] = _team[i] with { Doing = PersonDoing(board.Actor(_team[i].Slot), carrier) };
        }

        TeamRole dealt = RoleDeal.Of(_bot.Slot, _team.AsSpan(0, n), _split(n), carrier);
        RoleDeals++;
        _roleHold.Take(dealt, _now, _tick);
        // A deal the hold did not let through is worked out again at the next decide.
        if (_roleHold.Role == dealt)
            (_dealKey, _nextDeal) = (key, _now + (int)(RedealSeconds / _tick));
    }

    /// <summary>What a person on the team is doing, by where the bot sees them or saw them last: by the team's flag, with the carrier, or (anywhere else, or not seen lately) attacking.</summary>
    private TeamRole PersonDoing(KnownActor person, int carrier)
    {
        bool sighted = _view.TryActor(person.Id, out ActorFacts drawn);
        if (!sighted && !(person.EverSeen && (_now - person.LastSeenTick) * _tick < PersonSeenSeconds))
            return TeamRole.Attack;
        AiVector at = sighted ? drawn.Origin : person.LastSeenPos;
        if (carrier != 0 && person.Id.Slot != carrier && _enemyFlag is { } flag && at.DistanceTo(flag.Position) < PersonEscortsWithin)
            return TeamRole.Escort;
        return _ownFlagHomeKnown && at.DistanceTo(_ownFlagHome) < PersonDefendsWithin ? TeamRole.Defend : TeamRole.Attack;
    }
}
