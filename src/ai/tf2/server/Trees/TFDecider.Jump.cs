using System.Diagnostics.CodeAnalysis;
using SourceSharp.Ai.TF2.Jump;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Soldier's rocket jump as the tree decides it (plan_bots_teams_tf2_part_g G.5 step 6). The
/// tree says only whether a jump is allowed now; where one pays, and its recipe, is read from the
/// level's book of jumps (<see cref="IJumpAdvisor"/>), which a worker fills from the simulator: a
/// decide never flies or searches.
///
/// <para>
/// Allowed: a Soldier with its launcher in hand, loaded with a rocket to spare and not being
/// reloaded, on the ground and moving along a way to a goal, nobody in sight, not carrying, not in
/// the middle of a glance, and with the health to pay for it. Then the book is asked about the
/// stretch ahead (the bot's place, its heading, its goal), and the jump it answers is begun when
/// the health left after its blast stays over the floor: one ability one-shot that carries the
/// plan's number to the program that flies it (<see cref="RocketJumpProgram"/>).
/// </para>
///
/// <para>
/// A link the bot's route takes (lane G5e) is begun under the same rule but two of its parts: it is not
/// spaced from the jump before it (<see cref="RocketJumpEverySeconds"/> was made for a bot bouncing down a
/// corridor, and it made a chain of links wait out its follower's hold at the second, and the first link
/// after a stretch jump too), and a glance does not hold it back (the program stands the bot at the start
/// and turns the view itself). What keeps a chain from jumping the bot to death is the health: each link
/// is begun only with the health after its own blast over the floor, checked as the bot stands at it,
/// and a search takes a link only while it has that health to spend (<c>TFNavProfile.LinksFor</c>).
/// </para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The level's jumps, or null: a tree given none makes no rocket jump.</summary>
    public IJumpAdvisor? Jumps { get; init; }

    /// <summary>A jump is begun with at least this many rockets loaded: one for the jump, one to spare.</summary>
    public const int RocketJumpRockets = 2;

    /// <summary>The rockets a link's jump is begun with loaded: its recipe's (two with a second rocket in the air, lane G7b) and the one to spare.</summary>
    public static int RocketsFor(JumpPlan plan) => (plan ?? throw new ArgumentNullException(nameof(plan))).Recipe.Rockets + RocketJumpRockets - 1;

    /// <summary>The longest a jump begun is taken to be under way, whatever the bot's feet say.</summary>
    public const float RocketJumpMostSeconds = 9f;

    private bool _jumping;
    private Tick _jumpBegan;

    /// <summary>The plan of the last jump begun, for the facts.</summary>
    public JumpPlan? LastJump { get; private set; }

    /// <summary>
    /// Whether a rocket jump may be asked about now: everything but the route's say. The health is
    /// the floor's here; what the jump itself costs is known with its plan.
    /// </summary>
    private bool JumpAllowed => MayRocketJump && JumpRefusalNow == JumpLinkRefusal.None;

    /// <summary>Whether this bot rocket jumps at all: a Soldier with the ability and a book of jumps.</summary>
    private bool MayRocketJump => _class == TFClass.Soldier && _rocketJumpRow >= 0 && Jumps is not null;

    /// <summary>The first part of <see cref="JumpAllowed"/>'s rule that fails now, or none.</summary>
    private JumpLinkRefusal JumpRefusalNow
        => !HoldingLauncher ? JumpLinkRefusal.Launcher
           : _self.Clip < RocketJumpRockets ? JumpLinkRefusal.Clip
           : (_self.Flags & SelfFlags.Reloading) != 0 ? JumpLinkRefusal.Reloading
           : (_self.Flags & SelfFlags.Grounded) == 0 ? JumpLinkRefusal.Airborne
           : _targetSighted ? JumpLinkRefusal.Sighted
           : Carrying ? JumpLinkRefusal.Carrying
           : _self.Health < JumpHealthLeast ? JumpLinkRefusal.Health
           : !_hasGoal || _move.Kind != MoveKind.Follow ? JumpLinkRefusal.NoGoal
           : _glance.Holding(_now) ? JumpLinkRefusal.Glance
           : _now < _nextJumpAt ? JumpLinkRefusal.Spacing
           : JumpLinkRefusal.None;

    /// <summary>
    /// Whether a link the route takes may be begun, refused for <paramref name="why"/> (<see cref="JumpRefusalNow"/>): every
    /// part of the rule but the spacing and a glance (lane G5e), and a reload under way with the rockets to begin it loaded
    /// (lane G9): the launcher loads singly, and its trigger with a rocket loaded ends a reload and fires (the weapon's
    /// <c>ReloadSinglyFrame</c>, stock's), while its rockets load slower than the follower waits at a takeoff. Live on
    /// ss_ai_tf_pack, with no cabinet since the spawn, a bot came to the third jump loading its third rocket, was refused there
    /// 3 or 4 times a run and gave the course up. The clip is the rule's still (<see cref="RouteLinkAdvised"/>).
    /// </summary>
    private static bool LinkMayBegin(JumpLinkRefusal why)
        => why is JumpLinkRefusal.None or JumpLinkRefusal.Spacing or JumpLinkRefusal.Glance or JumpLinkRefusal.Reloading;

    /// <summary>
    /// The route's own jump: a blast link its follower says the bot stands at the start of, on its way to this decide's
    /// goal (lane G5c-2b: the follower only says it; the tree begins it), with its rockets and one to spare loaded
    /// (<see cref="RocketsFor"/>: a second rocket in the air needs three, lane G7b) and whose blasts leave the bot over its
    /// health floor. <paramref name="refused"/> says which of the two failed when one did.
    /// </summary>
    private bool RouteLinkAdvised([NotNullWhen(true)] out JumpPlan? plan, out JumpLinkRefusal refused, out JumpPaid paid)
    {
        plan = null;
        refused = JumpLinkRefusal.Health;
        paid = JumpPaid.No;
        if (Jumps!.RouteLink(_bot.Slot, _now, _goal) is not { } link)
            return false;
        if (_self.Clip < RocketsFor(link))
        {
            refused = JumpLinkRefusal.Clip;
            return false;
        }

        paid = PaidFor(link);
        plan = paid != JumpPaid.No ? link : null;
        if (plan is null)
            (_linkHealthRefused, _linkHealthRefusedAt) = (link.Health, _now);
        return plan is not null;
    }

    /// <summary>
    /// The jump the book advises for the stretch ahead, or false: false when the bot is not moving along a way or nothing
    /// is known for the stretch (it is asked about, for the next time), or when the jump's blast would take the bot under
    /// its health floor.
    /// </summary>
    private bool JumpAdvised([NotNullWhen(true)] out JumpPlan? plan, out JumpPaid paid)
    {
        plan = null;
        paid = JumpPaid.No;
        var way = new AiVector(_self.Velocity.X, _self.Velocity.Y, 0f);
        if (way.Length < RocketJumpSpeed)
            return false;
        JumpPlan? advised = Jumps!.Advise(new JumpStretchAsk(_self.Origin, way.Normalised(), _goal, _now));
        if (advised is null || (paid = PaidFor(advised)) == JumpPaid.No)
            return false;
        plan = advised;
        return true;
    }

    // ------------------------------------------------------------------ a jump to health (lane G9)

    /// <summary>How a jump's health is paid for: not, by the plain floor, or by a heal place by its landing.</summary>
    private enum JumpPaid : byte
    {
        No = 0,
        Plain,
        ByHealth,
    }

    /// <summary>The least health a jump may be asked about with: the hard floor with the health rule on (a jump by health may go under the floor), else the floor.</summary>
    private float JumpHealthLeast => Feel.JumpHealth ? JumpHealthRule.HardFloor : RocketJumpHealthFloor;

    private AiVector _paidAt;
    private bool _paidCabinet;

    // The blast cost of the last link the route stood at that the rule refused for the health, and when (the Course branch's wait).
    private float _linkHealthRefused;
    private Tick _linkHealthRefusedAt;

    /// <summary>
    /// How <paramref name="plan"/>'s blast is paid for now (<see cref="JumpHealthRule"/>): the plain floor; else, with the
    /// switch on (<see cref="TFFeelSwitches.JumpHealth"/>), the nearest heal place by its landing the bot may use whose pay
    /// covers it (its place kept for <see cref="JumpBegun"/>); else not.
    /// </summary>
    private JumpPaid PaidFor(JumpPlan plan)
    {
        if (JumpHealthRule.Plain(_self.Health, plan.Health, RocketJumpHealthFloor))
            return JumpPaid.Plain;
        if (!Feel.JumpHealth || plan.Heals.IsEmpty)
        {
            LastHealthRefusal = $"plan {plan.Id}: health {_self.Health}, cost {plan.Health:F1}, {(Feel.JumpHealth ? "no place by its landing" : "switch off")}";
            return JumpPaid.No;
        }

        int max = MaxHealth, usable = 0;
        float fall = JumpHealthRule.Fall(plan, max);
        foreach (JumpHeal h in plan.Heals)
        {
            if (!MayUse(h))
                continue;
            usable++;
            if (JumpHealthRule.ByHealth(_self.Health, plan.Health, fall, JumpHealthRule.Pays(h.Cabinet, h.Kind, max), RocketJumpHealthFloor))
            {
                (_paidAt, _paidCabinet) = (h.At, h.Cabinet);
                return JumpPaid.ByHealth;
            }
        }

        LastHealthRefusal = $"plan {plan.Id}: health {_self.Health}, cost {plan.Health:F1}, fall {fall:F1}, {plan.Heals.Length} place(s) by its landing, {usable} usable"
                            + (plan.Heals.IsEmpty ? "" : HealDiag(plan.Heals[0]));
        return JumpPaid.No;
    }

    /// <summary>What the bot makes of <paramref name="heal"/>, for <see cref="LastHealthRefusal"/>: its place, the map's nearest pack and the bot's belief of it.</summary>
    private string HealDiag(in JumpHeal heal)
    {
        int best = -1;
        float d = float.PositiveInfinity;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            float di = _map.Items[i].Position.DistanceTo(heal.At);
            if (di < d)
                (best, d) = (i, di);
        }

        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        string belief = best >= 0 && best < beliefs.Length ? $"{beliefs[best].Present?.ToString() ?? "unknown"} back {beliefs[best].ExpectedBack.Value} now {_now.Value}" : "no belief";
        return $"; first place {(heal.Cabinet ? "cabinet" : heal.Kind.ToString())} at ({heal.At.X:F0} {heal.At.Y:F0} {heal.At.Z:F0}), map pack {best} {d:F1} u off, {belief}, skipped {(best >= 0 && Skipped(_map.Items[best].Position))}";
    }

    /// <summary>Whether the bot may heal at <paramref name="heal"/>: a cabinet of its team or every team's; a pack its beliefs do not say is gone and it has not given up on.</summary>
    private bool MayUse(in JumpHeal heal)
        => heal.Cabinet ? heal.Team == 0 || heal.Team == _self.Team : PackThere(heal.At);

    /// <summary>
    /// Whether the pack at <paramref name="at"/> (one of the map's, by its place) is not believed gone, nor given up on for now.
    /// Seen taken, it is believed gone until the bot expects it back (<see cref="ItemBelief.ExpectedBack"/>, its respawn after the
    /// sighting): a person who saw a pack taken two minutes ago, on a stage it cannot see from below, knows it is back (live, on
    /// ss_ai_tf_pack, a bot that took the pack in one run refused the jump to it in the next).
    /// </summary>
    private bool PackThere(AiVector at)
    {
        // The heal places are read from the level's placed entities, the map's packs from its lump: after a changelevel the
        // placed entities answer where each pack came to rest (live, 1.3 u off), so "the same pack" is the one this near.
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            if (JumpHealthRule.IsPack(item.Kind) && item.Position.DistanceTo(at) <= PackSameUnits)
                return !(i < beliefs.Length && beliefs[i].Present == false && _now < beliefs[i].ExpectedBack) && !Skipped(item.Position);
        }

        return false;
    }

    /// <summary>A heal place and one of the map's packs this near each other are the same pack: under a pack's own size, and no two packs are nearer.</summary>
    public const float PackSameUnits = 16f;

    // A jump by health under way (begun by the health rule), and the visit it owes: its place, and when and with what it landed.
    private bool _jumpByHealth, _jumpHealOwed, _jumpHealCabinet;
    private AiVector _jumpHealAt;
    private Tick _jumpLanded;
    private int _jumpLandHealth;

    /// <summary>Why the last jump the plain floor refused was not paid for by health either, for the dev API (<c>/ai/jumps/health</c>); empty before one.</summary>
    public string LastHealthRefusal { get; private set; } = "";

    /// <summary>Jumps begun that the plain floor refused and the health rule paid for.</summary>
    public int HealthJumps { get; private set; }

    /// <summary>Of those, the landings after which the bot's health rose (a pack or a cabinet): each one's health on landing, health after and seconds between.</summary>
    public IReadOnlyList<(int Landed, int After, float Seconds)> HealthJumpHeals => _healthJumpHeals;

    private readonly List<(int Landed, int After, float Seconds)> _healthJumpHeals = [];

    /// <summary>Deaths within <see cref="HealthJumpRiskSeconds"/> of such a landing (or in the jump): the risk the rule takes.</summary>
    public int HealthJumpDeaths { get; private set; }

    /// <summary>Visits owed that found the pack gone (the bot's beliefs said so before it healed), and those not healed in <see cref="HealPatienceSeconds"/>.</summary>
    public int HealthJumpGone { get; private set; }

    public int HealthJumpUnhealed { get; private set; }

    /// <summary>A death this soon after a jump by health landed counts against the rule.</summary>
    public const float HealthJumpRiskSeconds = 5f;

    /// <summary>
    /// Whether the bot owes its jump's place a visit now: down from a jump by health, under the floor, the place still one
    /// it may use. The heal branch (<see cref="SurviveUtility"/>, <see cref="HealPlace"/>) sends it there first.
    /// </summary>
    private bool JumpHealDue
        => _jumpHealOwed && !_jumping && _jumpLanded.Value != 0 && _self.Health < RocketJumpHealthFloor
           && (_jumpHealCabinet ? !Skipped(_jumpHealAt) : PackThere(_jumpHealAt));

    /// <summary>
    /// The place a jump by health owes, when <see cref="JumpHealDue"/>, and while that jump is under way: hurt by its own blast
    /// the bot may go for health in the air (the blast is a hit in the last 3 s), and the place its landing is by is the one
    /// it goes for. Live on ss_ai_tf_pack, with the pack by the landing given up on for a while (the heal branch's patience,
    /// spent on it from a stage below), it went for the farther pack in the air and steered itself off the jump.
    /// </summary>
    private bool JumpHealPlace(out AiVector at)
    {
        at = _jumpHealAt;
        return JumpHealDue || (_jumpHealOwed && _jumping && _jumpLanded.Value == 0);
    }

    /// <summary>
    /// Once a decide alive: a jump by health's visit, followed. The health over its landing's: healed (counted with the
    /// numbers); the pack believed gone: counted and owed no more (the bot heals as it would have); not healed in the
    /// heal branch's patience: counted and owed no more.
    /// </summary>
    private void NoteHealthJump()
    {
        if (!_jumpHealOwed || _jumping || _jumpLanded.Value == 0)
            return;
        float since = (_now - _jumpLanded) * _tick;
        if (_self.Health > _jumpLandHealth)
        {
            _healthJumpHeals.Add((_jumpLandHealth, _self.Health, since));
            _jumpHealOwed = false;
        }
        else if (!_jumpHealCabinet && !PackThere(_jumpHealAt))
        {
            HealthJumpGone++;
            _jumpHealOwed = false;
        }
        else if (since > HealPatienceSeconds)
        {
            HealthJumpUnhealed++;
            _jumpHealOwed = false;
        }
    }

    /// <summary>The bot died (the dead branch): within <see cref="HealthJumpRiskSeconds"/> of a jump by health's landing, or in it, it counts; nothing is owed.</summary>
    private void HealthJumpDied()
    {
        if (_jumpByHealth || (_jumpLanded.Value != 0 && (_now - _jumpLanded) * _tick <= HealthJumpRiskSeconds))
            HealthJumpDeaths++;
        (_jumpByHealth, _jumpHealOwed, _jumpLanded) = (false, false, default);
    }

    /// <summary>
    /// A place the bot was sent to by hand (<c>ai_tf_errand</c>, for proving a jump), with nobody in sight and nothing
    /// carried: its goal this decide, whatever the tree chose. Runs after the tree and before the jump's rule.
    /// </summary>
    private void Errand()
    {
        if (Jumps is null || !Jumps.Errand(_bot.Slot, out AiVector goal) || _targetSighted || Carrying || !_self.Alive)
            return;
        GoTo(goal, MoveSpeed.Run);
        _leaf = "errand";
    }

    /// <summary>
    /// A jump begins: the hands are off until <see cref="JumpUnderWay"/> says it is over. One the plain rule refused and the
    /// health rule paid for (<see cref="JumpPaid.ByHealth"/>) leaves the bot owing a visit to its place (<see cref="JumpHealDue"/>).
    /// </summary>
    private void JumpBegun(JumpPlan plan, JumpPaid paid)
    {
        (_jumping, _jumpBegan, LastJump) = (true, _now, plan);
        _jumpByHealth = paid == JumpPaid.ByHealth;
        if (_jumpByHealth)
        {
            HealthJumps++;
            (_jumpHealOwed, _jumpHealAt, _jumpHealCabinet, _jumpLanded) = (true, _paidAt, _paidCabinet, default);
        }
    }

    /// <summary>
    /// Whether the jump last begun still has the bot: through its approach
    /// (<see cref="RocketJumpBusySeconds"/> from the one-shot) and then for as long as the bot is
    /// off the ground, the hands kept off a decide ahead. A bot that died in it, or one
    /// <see cref="RocketJumpMostSeconds"/> on, has it no longer.
    /// </summary>
    private bool JumpUnderWay()
    {
        if (!_jumping)
            return false;
        float since = (_now - _jumpBegan) * _tick;
        bool approach = since <= RocketJumpBusySeconds;
        // A death clears the hold: the next life's first decide finds it gone.
        if (_jumpUntil.Value == 0 || since > RocketJumpMostSeconds || (!approach && (_self.Flags & SelfFlags.Grounded) != 0))
        {
            _jumping = false;
            if (_jumpByHealth && _jumpUntil.Value != 0)
                (_jumpLanded, _jumpLandHealth) = (_now, _self.Health);
            _jumpByHealth = false;
            return false;
        }

        // The hold is kept a decide ahead: the hands are asked before this is (Engage runs first), so a hold that ended on this decide would let one shot go.
        if (_jumpUntil < _now + DecideSchedule.Period + 1)
            _jumpUntil = _now + DecideSchedule.Period + 1;
        return true;
    }
}
