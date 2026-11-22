using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    private bool _paidCabinet, _paidPlace;

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
        _paidPlace = false;
        if (JumpHealthRule.Plain(_self.Health, plan.Health, RocketJumpHealthFloor))
        {
            // Paid plainly: the nearest place by its landing the bot may use is still the one it takes next, when the place
            // is worth it once down (lane G9b, JumpBegun): a route weighs such a link with the health given back.
            foreach (JumpHeal h in plan.Heals)
            {
                if (MayUse(h, out _))
                {
                    (_paidAt, _paidCabinet, _paidPlace) = (h.At, h.Cabinet, true);
                    break;
                }
            }

            return JumpPaid.Plain;
        }

        if (!Feel.JumpHealth || plan.Heals.IsEmpty)
        {
            _refusal = new HealthRefusal(plan.Id, _self.Health, plan.Health, float.NaN, Feel.JumpHealth, 0, 0, default, -1, false, _now.Value);
            return JumpPaid.No;
        }

        int max = MaxHealth, usable = 0, firstItem = -1;
        float fall = JumpHealthRule.Fall(plan, max);
        for (int i = 0; i < plan.Heals.Length; i++)
        {
            JumpHeal h = plan.Heals[i];
            bool may = MayUse(h, out int item);
            if (i == 0)
                firstItem = item;
            if (!may)
                continue;
            usable++;
            if (JumpHealthRule.ByHealth(_self.Health, plan.Health, fall, JumpHealthRule.Pays(h.Cabinet, h.Kind, max), RocketJumpHealthFloor))
            {
                (_paidAt, _paidCabinet, _paidPlace) = (h.At, h.Cabinet, true);
                return JumpPaid.ByHealth;
            }
        }

        // The facts only (lane G9b): the words are made when the dev API asks (LastHealthRefusal).
        _refusal = new HealthRefusal(plan.Id, _self.Health, plan.Health, fall, true, plan.Heals.Length, usable, plan.Heals[0], firstItem,
                                     firstItem >= 0 && Skipped(_map.Items[firstItem].Position), _now.Value)
        {
            Present = firstItem >= 0 && firstItem < _memory.Board.Items.Length ? _memory.Board.Items[firstItem].Present : null,
            Back = firstItem >= 0 && firstItem < _memory.Board.Items.Length ? _memory.Board.Items[firstItem].ExpectedBack.Value : 0,
        };
        return JumpPaid.No;
    }

    /// <summary>
    /// The facts of the last jump the plain floor refused and the health rule did not pay for either (lane G9b: kept in fields
    /// on the decide, the words made by <see cref="LastHealthRefusal"/> when the dev API asks): the plan, the bot's health, the
    /// blast and the worst fall (NaN: not reckoned), whether the switch was on, the places by the landing and how many the bot
    /// might use, the first of them, the map's pack that is (-1: none, or a cabinet), whether the bot had given it up, the
    /// decide's tick, and the bot's belief of that pack (present, expected back).
    /// </summary>
    internal readonly record struct HealthRefusal(int Plan, int Health, float Cost, float Fall, bool SwitchOn, int Places, int Usable, JumpHeal First, int Item,
                                                  bool Skipped, int Now)
    {
        public bool? Present { get; init; }

        public int Back { get; init; }
    }

    private HealthRefusal? _refusal;

    /// <summary>The facts of the last refusal by health, or null before one (lane G9b).</summary>
    internal HealthRefusal? LastHealthRefusalFacts => _refusal;

    /// <summary>The words of <paramref name="r"/>, for the dev API.</summary>
    internal static string Words(in HealthRefusal r)
    {
        var at = string.Create(CultureInfo.InvariantCulture, $"plan {r.Plan}: health {r.Health}, cost {r.Cost:F1}");
        if (float.IsNaN(r.Fall))
            return $"{at}, {(r.SwitchOn ? "no place by its landing" : "switch off")}";
        string first = r.Places == 0 ? ""
            : string.Create(CultureInfo.InvariantCulture,
                $"; first place {(r.First.Cabinet ? "cabinet" : r.First.Kind.ToString())} at ({r.First.At.X:F0} {r.First.At.Y:F0} {r.First.At.Z:F0}), map pack {r.Item}, {(r.Item < 0 ? "no belief" : $"{r.Present?.ToString() ?? "unknown"} back {r.Back} now {r.Now}")}, skipped {r.Skipped}");
        return string.Create(CultureInfo.InvariantCulture, $"{at}, fall {r.Fall:F1}, {r.Places} place(s) by its landing, {r.Usable} usable{first}");
    }

    /// <summary>
    /// Whether the bot may heal at <paramref name="heal"/>: a cabinet of its team or every team's; a pack its beliefs do not say
    /// is gone and it has not given up on. <paramref name="item"/> is the map's pack it is (-1: a cabinet, or none).
    /// </summary>
    private bool MayUse(in JumpHeal heal, out int item)
    {
        item = -1;
        return heal.Cabinet ? heal.Team == 0 || heal.Team == _self.Team : PackThere(heal.At, out item);
    }

    /// <summary>
    /// Whether the pack at <paramref name="at"/> (one of the map's, by its place) is not believed gone, nor given up on for now.
    /// Seen taken, it is believed gone until the bot expects it back (<see cref="ItemBelief.ExpectedBack"/>, its respawn after the
    /// sighting): a person who saw a pack taken two minutes ago, on a stage it cannot see from below, knows it is back (live, on
    /// ss_ai_tf_pack, a bot that took the pack in one run refused the jump to it in the next).
    /// </summary>
    private bool PackThere(AiVector at) => PackThere(at, out _);

    /// <summary><see cref="PackThere(AiVector)"/>, with the map's pack it is (-1: none).</summary>
    private bool PackThere(AiVector at, out int index)
    {
        index = -1;
        // The heal places are read from the level's placed entities, the map's packs from its lump: after a changelevel the
        // placed entities answer where each pack came to rest (live, 1.3 u off), so "the same pack" is the one this near.
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            if (JumpHealthRule.IsPack(item.Kind) && item.Position.DistanceTo(at) <= PackSameUnits)
            {
                index = i;
                return !(i < beliefs.Length && beliefs[i].Present == false && _now < beliefs[i].ExpectedBack) && !Skipped(item.Position);
            }
        }

        return false;
    }

    /// <summary>A heal place and one of the map's packs this near each other are the same pack: under a pack's own size, and no two packs are nearer.</summary>
    public const float PackSameUnits = 16f;

    // A jump by health under way (begun by the health rule), and the visit it owes: its place, and when and with what it landed.
    // A plain jump's visit (lane G9b): a jump the plain floor paid for that lands by a place the bot may use.
    private bool _jumpByHealth, _jumpHealOwed, _jumpHealCabinet, _jumpHealPlain, _landedByHealth, _livedSinceDeath;
    private AiVector _jumpHealAt;
    private Tick _jumpLanded;
    private int _jumpLandHealth, _jumpLowHealth;

    /// <summary>Why the last jump the plain floor refused was not paid for by health either, for the dev API (<c>/ai/jumps/health</c>); empty before one. Made from the facts when asked (<see cref="Words"/>).</summary>
    public string LastHealthRefusal => _refusal is { } r ? Words(r) : "";

    /// <summary>Each jump by health's seconds and the walk's it replaced, as its plan says (the simulator's; 0: no way on foot).</summary>
    public IReadOnlyList<(float Seconds, float WalkSeconds)> HealthJumpTimes => _healthJumpTimes;

    private readonly List<(float Seconds, float WalkSeconds)> _healthJumpTimes = [];

    /// <summary>
    /// Jumps the plain floor paid for that came down by a place the bot may use with the place worth taking (lane G9b,
    /// <see cref="PackWorth"/>): the bot takes it next, as after a jump by health. A route weighs such a link with the health
    /// given back (<see cref="Jump.JumpHeals.Weights"/>), so the visit is what makes that weight true.
    /// </summary>
    public int PackJumps { get; private set; }

    /// <summary>Of those, the landings after which the bot's health rose: health on landing, after, seconds between.</summary>
    public IReadOnlyList<(int Landed, int After, float Seconds)> PackJumpHeals => _packJumpHeals;

    private readonly List<(int Landed, int After, float Seconds)> _packJumpHeals = [];

    /// <summary>Those visits that found the place gone, were not healed in <see cref="HealPatienceSeconds"/> or were ended by a death or the next jump, and deaths within <see cref="HealthJumpRiskSeconds"/> of such a landing.</summary>
    public int PackJumpMissed { get; private set; }

    public int PackJumpDeaths { get; private set; }

    /// <summary>The bot's deaths, every one (lane G9b): what the deaths after a landing are set against.</summary>
    public int Deaths { get; private set; }

    /// <summary>
    /// A place is worth a plain jump's visit when the bot came down at least this far under its maximum: half of what the
    /// smallest pack gives it (a Soldier's 20 of 40), so the walk of up to <see cref="Jump.JumpHealthRule.HealWalk"/> buys at
    /// least that. A hop that cost nothing leaves the bot full and owes nothing.
    /// </summary>
    private float PackWorth => Jump.JumpHealthRule.Pays(false, ItemKind.HealthSmall, MaxHealth) * 0.5f;

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

    /// <summary>Jumps by health, and plain jumps by a place, that came down nowhere within a walk of their place (fizzled, called off, short): no visit owed (lane G9b).</summary>
    public int HealthJumpsOff { get; private set; }

    public int PackJumpsOff { get; private set; }

    /// <summary>Visits a jump by health owed that the bot's death or its next jump ended before it healed (lane G9b): with the healed, gone and unhealed, every visit's end.</summary>
    public int HealthJumpLost { get; private set; }

    /// <summary>A death this soon after a jump by health landed counts against the rule.</summary>
    public const float HealthJumpRiskSeconds = 5f;

    /// <summary>
    /// Whether the bot owes its jump's place a visit now: down from a jump by health, or a plain one whose place was worth it
    /// on landing (lane G9b), the place still worth taking (<see cref="PackWorth"/> under its maximum) and one it may use; a
    /// plain jump's visit not with an enemy in sight (the bot is over its floor, and fights). The heal branch
    /// (<see cref="SurviveUtility"/>, <see cref="HealPlace"/>) sends it there first.
    /// </summary>
    private bool JumpHealDue
        => _jumpHealOwed && !_jumping && _jumpLanded.Value != 0 && _self.Health <= MaxHealth - PackWorth && !(_jumpHealPlain && _targetSighted)
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

    // ------------------------------------------------------------------ a jump to a pack, sought (lane G9b)

    /// <summary>
    /// The farthest a link's start may be from the bot for the bot to go for the pack by its landing (straight): about 2 s of
    /// a Soldier's run, so "near by" is near, and the jump and the walk to the pack after it are the rest of the way.
    /// </summary>
    public const float PackLinkReach = 512f;

    /// <summary>A link's start on the bot's own floor: this near its feet's height (a body's step and a half). One below a ledge the bot stands on is not its way.</summary>
    public const float PackLinkRise = 32f;

    /// <summary>The class's run, from the class table (a Soldier's 240 when it has none).</summary>
    private float RunSpeed
    {
        get
        {
            foreach (Knowledge.TFClassFacts c in _classes)
            {
                if (c.Class == _class && c.MaxSpeed > 0f)
                    return c.MaxSpeed;
            }

            return SoldierRun;
        }
    }

    /// <summary>A Soldier's run, stock's 240 u/s, read when the class table answered nothing.</summary>
    public const float SoldierRun = 240f;

    /// <summary>Times the heal branch went for a pack by way of a link's landing (lane G9b): a jump to a pack sought.</summary>
    public int PackLinkSeeks { get; private set; }

    /// <summary>Of those, the times a jump was begun while the bot went for the pack (lane G9b).</summary>
    public int PackLinkSeekJumps { get; private set; }

    private bool _packLinkSought;

    /// <summary>
    /// A pack the bot goes for by way of a link to it (lane G9b, the ruling's "planned jump to a health pack near by for fast
    /// mobility"): one of the level's links that is the quicker way to a place by its landing the bot may use
    /// (<see cref="IJumpAdvisor.HealLinks"/>, <see cref="JumpPlan.JumpWays"/>), whose start is within <see cref="PackLinkReach"/> of the bot on its floor, whose
    /// blast the bot's health pays for now (plainly, or by that place), the place worth taking (<see cref="PackWorth"/> under
    /// the maximum) and not already within a walk of the bot. A bot going for health takes it when that way (to the start, the jump, the walk after) is shorter than the straight line to
    /// the nearest other place (<paramref name="nearest"/>); one about its errands only when the place is nearer its goal than it is (the pack on its way). Not with an
    /// enemy in sight, carrying, with a jump under way or a visit owed, nor when its route stands at a link's start already. The route takes the link when it is the quicker way
    /// (its weight, <see cref="Jump.JumpHeals.Weights"/>), the tree begins it at its start, and the visit follows. Once begun
    /// it is held on that place until a jump is begun, the place is gone or given up, the health no longer pays, or a fight
    /// (<see cref="PackSeekEnd"/>).
    /// </summary>
    private bool PackLinkPlace(out AiVector at, float nearest = float.PositiveInfinity)
    {
        at = default;
        if (_packLinkSought)
        {
            // Under way: held on the place it went for (its route may take another link to it, further off) until a jump is
            // begun, the place is gone or given up (the heal branch's patience), its health no longer pays, or a fight.
            PackSeekEnd? end = !MayRocketJump || !Feel.JumpHealth || Carrying ? PackSeekEnd.Other
                : _jumping || _jumpHealOwed ? PackSeekEnd.Jumped
                : _targetSighted ? PackSeekEnd.Sighted
                : !MayUse(_seekHeal, out _) ? PackSeekEnd.Gone
                : _self.Health > MaxHealth - PackWorth || !Affords(_seekPlan!, _seekHeal) ? PackSeekEnd.Health
                : null;
            if (end is null)
            {
                at = _seekHeal.At;
                return true;
            }

            SeekEnded(end.Value);
        }

        // A bot its route already stands at a link's start of takes that link: no going for a pack begins there.
        if (!MayRocketJump || !Feel.JumpHealth || Carrying || _targetSighted || _jumping || _jumpHealOwed || _self.Health > MaxHealth - PackWorth
            || (Doing.HasGoal && Jumps!.RouteSaid(_bot.Slot, _now, Doing.Goal) is { IsLink: true, AtStart: true }))
            return false;
        bool errand = !WantsHealth;
        float best = PackLinkReach;
        foreach (JumpPlan plan in Jumps!.HealLinks)
        {
            JumpStart start = plan.Recipe.Start();
            var from = new AiVector(start.Origin.X, start.Origin.Y, start.Origin.Z);
            float d = _self.Origin.DistanceTo(from);
            if (d >= best || MathF.Abs(from.Z - _self.Origin.Z) > PackLinkRise)
                continue;
            foreach (JumpHeal h in plan.JumpWays)
            {
                if (!MayUse(h, out _) || _self.Origin.DistanceTo(h.At) <= JumpHealthRule.HealWalk
                    || (errand && !(Doing.HasGoal && h.At.DistanceTo(Doing.Goal) < _self.Origin.DistanceTo(Doing.Goal)))
                    || !Affords(plan, h))
                    continue;
                // Going for health, the link's way must be shorter than the straight line to the nearest other place (a floor on its walk): the start, the jump at the run's speed, the walk from the landing.
                if (!errand && d + (plan.Seconds * RunSpeed) + h.Walk >= nearest)
                    continue;
                (best, at, _seekPlan, _seekHeal, _packLinkSought) = (d, h.At, plan, h, true);
                break;
            }
        }

        if (_packLinkSought)
            PackLinkSeeks++;
        return _packLinkSought;
    }

    /// <summary>Whether the bot's health pays for <paramref name="plan"/>'s blast now, plainly or by <paramref name="heal"/>.</summary>
    private bool Affords(JumpPlan plan, in JumpHeal heal)
    {
        int max = MaxHealth;
        return JumpHealthRule.Plain(_self.Health, plan.Health, RocketJumpHealthFloor)
               || JumpHealthRule.ByHealth(_self.Health, plan.Health, JumpHealthRule.Fall(plan, max), JumpHealthRule.Pays(heal.Cabinet, heal.Kind, max), RocketJumpHealthFloor);
    }

    /// <summary>How a going for a pack by way of a link ended (lane G9b): a jump begun, an enemy in sight, the place gone or given up, the health, the bot's death, anything else.</summary>
    public enum PackSeekEnd : byte
    {
        Jumped = 0,
        Sighted,
        Gone,
        Health,
        Died,
        Other,
    }

    private readonly int[] _seekEnds = new int[Enum.GetValues<PackSeekEnd>().Length];
    private JumpPlan? _seekPlan;
    private JumpHeal _seekHeal;

    /// <summary>How the goings for a pack by way of a link ended, by <see cref="PackSeekEnd"/>.</summary>
    public IReadOnlyList<int> PackSeekEnds => _seekEnds;

    private void SeekEnded(PackSeekEnd why)
    {
        _seekEnds[(int)why]++;
        (_packLinkSought, _seekPlan) = (false, null);
    }

    /// <summary>
    /// Once a decide alive: a jump by health's visit, followed. The health over its landing's: healed (counted with the
    /// numbers); the pack believed gone: counted and owed no more (the bot heals as it would have); not healed in the
    /// heal branch's patience: counted and owed no more.
    /// </summary>
    private void NoteHealthJump()
    {
        _livedSinceDeath = true;
        if (!_jumpHealOwed || _jumping || _jumpLanded.Value == 0)
            return;
        float since = (_now - _jumpLanded) * _tick;
        if (_self.Health > _jumpLandHealth)
        {
            (_jumpHealPlain ? _packJumpHeals : _healthJumpHeals).Add((_jumpLandHealth, _self.Health, since));
            _jumpHealOwed = false;
        }
        else if (!_jumpHealCabinet && !PackThere(_jumpHealAt))
        {
            if (_jumpHealPlain)
                PackJumpMissed++;
            else
                HealthJumpGone++;
            _jumpHealOwed = false;
        }
        else if (since > HealPatienceSeconds)
        {
            if (_jumpHealPlain)
                PackJumpMissed++;
            else
                HealthJumpUnhealed++;
            _jumpHealOwed = false;
        }
    }

    /// <summary>
    /// The bot died (the dead branch, every dead decide): counted once a life; within <see cref="HealthJumpRiskSeconds"/> of a
    /// jump by health's landing, or in it, it counts against the rule, and within as long of a plain jump's landing by a place
    /// against those; nothing is owed.
    /// </summary>
    private void HealthJumpDied()
    {
        if (_packLinkSought)
            SeekEnded(PackSeekEnd.Died);
        if (_livedSinceDeath)
            Deaths++;
        _livedSinceDeath = false;
        bool soon = _jumpLanded.Value != 0 && (_now - _jumpLanded) * _tick <= HealthJumpRiskSeconds;
        VisitLost();
        if (_jumpByHealth || (soon && _landedByHealth))
            HealthJumpDeaths++;
        else if (soon)
            PackJumpDeaths++;
        (_jumpByHealth, _jumpHealOwed, _jumpLanded, _landedByHealth) = (false, false, default, false);
    }

    /// <summary>A visit owed since a landing that ends unhealed by the bot's death or its next jump is counted, so every visit's end is.</summary>
    private void VisitLost()
    {
        if (!_jumpHealOwed || _jumpLanded.Value == 0)
            return;
        if (_jumpHealPlain)
            PackJumpMissed++;
        else
            HealthJumpLost++;
        _jumpHealOwed = false;
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
        (_jumping, _jumpBegan, LastJump, _jumpLowHealth) = (true, _now, plan, _self.Health);
        VisitLost();
        _jumpByHealth = paid == JumpPaid.ByHealth;
        (_jumpHealOwed, _jumpLanded, _landedByHealth) = (false, default, false);
        // A plain jump by a place owes it a visit too, if the place is worth it once down (lane G9b, JumpUnderWay).
        if (_paidPlace && paid != JumpPaid.No && (_jumpByHealth || Feel.JumpHealth))
            (_jumpHealOwed, _jumpHealAt, _jumpHealCabinet, _jumpHealPlain) = (true, _paidAt, _paidCabinet, !_jumpByHealth);
        if (_packLinkSought)
            PackLinkSeekJumps++;
        if (_jumpByHealth)
        {
            HealthJumps++;
            _healthJumpTimes.Add((plan.Seconds, plan.WalkSeconds));
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
        _jumpLowHealth = Math.Min(_jumpLowHealth, _self.Health);
        // A death clears the hold: the next life's first decide finds it gone.
        if (_jumpUntil.Value == 0 || since > RocketJumpMostSeconds || (!approach && (_self.Flags & SelfFlags.Grounded) != 0))
        {
            _jumping = false;
            if (_jumpHealOwed && _jumpUntil.Value != 0)
            {
                // What it came down with is the jump's lowest health (lane G9b): live on cp_granary a link that lands on its pack
                // healed the bot at the touch, before the decide that saw it down, and the visit read the pack as gone.
                (_jumpLanded, _jumpLandHealth, _landedByHealth) = (_now, _jumpLowHealth, _jumpByHealth);
                // Down nowhere near its place (the jump fizzled, was called off or fell short: lane G9b, live on cp_granary a bot
                // under an upper floor's pack went for it for the heal branch's 15 s): nothing is owed, and it is counted.
                if (Jumps!.LandedBy(_self.Origin, _jumpHealAt) == false)
                {
                    if (_jumpHealPlain)
                        PackJumpsOff++;
                    else
                        HealthJumpsOff++;
                    _jumpHealOwed = false;
                }

                // A plain jump's place is worth the walk only when it gives back enough (lane G9b): else nothing is owed.
                else if (_jumpHealPlain && _jumpLowHealth > MaxHealth - PackWorth)
                    _jumpHealOwed = false;
                else if (_jumpHealPlain)
                    PackJumps++;
            }

            _jumpByHealth = false;
            return false;
        }

        // The hold is kept a decide ahead: the hands are asked before this is (Engage runs first), so a hold that ended on this decide would let one shot go.
        if (_jumpUntil < _now + DecideSchedule.Period + 1)
            _jumpUntil = _now + DecideSchedule.Period + 1;
        return true;
    }
}
