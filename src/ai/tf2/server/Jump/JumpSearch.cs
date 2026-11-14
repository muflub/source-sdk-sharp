using SourceSharp.Abi;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>Which families a search tries.</summary>
[Flags]
public enum JumpFamilies : byte
{
    None = 0,
    Up = 1 << 0,
    Forward = 1 << 1,
    Both = Up | Forward,

    /// <summary>Up with a second rocket in the air (lane G7b): searched only when asked by name, and only when no single family answered a jump worth keeping.</summary>
    Double = 1 << 2,

    /// <summary>
    /// Up with a second rocket in the air at a wall beside the flight (lane G7c): searched only when asked by name, and only
    /// when neither a single family nor <see cref="Double"/> answered a jump worth keeping.
    /// </summary>
    Wall = 1 << 3,
}

/// <summary>
/// One jump asked for: from where, to where, how far over the ground from the
/// landing a body may come to rest and still have made it, which families to
/// try, and the seed its perturbed trials are drawn from.
/// </summary>
public readonly record struct JumpAsk(Vector Takeoff, Vector Landing, float Tolerance, JumpFamilies Families = JumpFamilies.Both, uint Seed = 1,
                                      JumpHint? Hint = null);

/// <summary>
/// Where a search looks first (lane G6b): a jump a player showed, read as a single family's numbers (from standing or
/// from a run of <paramref name="RunUp"/>, the shot's view and its delay against the jump). Its neighbourhood is flown
/// before the grids; what it finds is kept only as the grids' finds are, so a hint is never what is flown.
/// </summary>
public readonly record struct JumpHint(JumpFamily Family, float RunUp, float Pitch, float YawOffset, int ShotDelay);

/// <summary>
/// A jump found: the recipe, its family, the share of its perturbed trials
/// that still landed, the seconds from its first command to the touch and the
/// part of them in the air, the health its own blast costs (the simulator's
/// blast damage, before the damage rules), the seconds a walk between the
/// same two places takes (null when there is none, or nothing to ask), and
/// how far from the landing the unperturbed flight came to rest.
/// </summary>
public readonly record struct JumpFound(JumpRecipe Recipe, JumpFamily Family, float Robustness, float Seconds, float AirSeconds, float Health,
                                        float? WalkSeconds, float Miss);

/// <summary>What a search answered: the jump, or none, and the simulated flights it spent.</summary>
public readonly record struct JumpSearchResult(JumpFound? Found, int Flights);

/// <summary>The time a walk between two places takes, for a search to set beside a jump's.</summary>
public interface IJumpWalk
{
    /// <summary>Seconds on foot from <paramref name="from"/> to <paramref name="to"/>, or null when there is no way.</summary>
    float? Seconds(Vector from, Vector to);
}

/// <summary>Nothing is known of walking: a search over bare free space.</summary>
public sealed class NoJumpWalk : IJumpWalk
{
    public float? Seconds(Vector from, Vector to) => null;
}

/// <summary>
/// Finds the recipe for a jump from one place to another in the simulator
/// (plan_bots_teams_tf2_part_g G.5 step 4), or says there is none.
///
/// <list type="number">
/// <item><b>Coarse.</b> Each family asked for flies its grid of pitches and
/// shot delays, the pilot in the loop: up from standing, forward from a
/// run-up (halved once when a body cannot stand where it would start). A
/// flight is a hit when the body comes to rest standing within the ask's
/// tolerance of the landing, at its height. A hit's grid neighbours that hit
/// too (a pitch step or a delay step either way) are counted: a recipe in the
/// middle of what works is likelier to survive a shot a little off.</item>
/// <item><b>Refined, when nothing hit.</b> Round the nearest miss: half the
/// pitch step, a command of delay and
/// <see cref="JumpSearchSettings.RefineYaw"/> of yaw each way.</item>
/// <item><b>Robustness.</b> The likeliest hits (the quickest, weighed by
/// their neighbours) are flown again <see cref="JumpSearchSettings.Trials"/>
/// times each, perturbed (the start place, the shot's view and its delay).
/// The answer is the one with the least seconds over robustness (the seconds
/// a landing costs when a failed jump is jumped again) among those that land
/// in <see cref="JumpSearchSettings.Keep"/> of their trials; with none that
/// robust, the most robust, its share saying so.</item>
/// <item><b>Refined.</b> Half a pitch step either side of the answer: one
/// that hits and is quicker has its robustness measured and takes its place
/// when its seconds over robustness are less; and on from there, while there
/// are flights to spend.</item>
/// </list>
///
/// The same ask gives the same answer: nothing is drawn but the trials, and
/// those from the ask's seed. A search spends at most
/// <see cref="JumpSearchSettings.MaxFlights"/> flights (with the settings as
/// they stand: 27 coarse, 26 round a miss, 80 trials, then 21 a refining
/// step that hits). It holds one simulator, so it serves one thread at a
/// time.
/// </summary>
public sealed class JumpSearch
{
    private readonly JumpFlier _flier;
    private readonly IJumpWalk _walk;
    private readonly JumpSearchSettings _settings;
    private readonly List<Hit> _hits = [];
    private readonly List<DoubleHit> _doubles = [];
    private readonly List<JumpPoint> _track = [];
    private readonly List<JumpCommand> _sent = [];

    public JumpSearch(JumpFlier flier, IJumpWalk walk, JumpSearchSettings settings)
    {
        _flier = flier;
        _walk = walk;
        _settings = settings;
    }

    public JumpSearchSettings Settings => _settings;

    private readonly record struct Hit(JumpRecipe Recipe, JumpFamily Family, JumpOutcome Outcome, float Miss, int Neighbours)
    {
        public float Likely => JumpSearch.Likely(Outcome.Seconds, Neighbours);
    }

    /// <summary>
    /// What the hits are measured in the order of, least first: the seconds
    /// over a guess at the robustness from the neighbours that hit (a fifth
    /// with none, all of it with four).
    /// </summary>
    internal static float Likely(float seconds, int neighbours) => seconds * 5f / (1 + neighbours);

    /// <summary>Whether <paramref name="outcome"/> made <paramref name="ask"/>'s landing: at rest, standing, within the tolerance and at its height.</summary>
    public bool Made(in JumpAsk ask, in JumpOutcome outcome)
        => outcome.Flew && outcome.OnGround && MathF.Abs(outcome.Rest.Z - ask.Landing.Z) <= _settings.HeightTolerance
           && Flat(outcome.Rest, ask.Landing) <= ask.Tolerance;

    /// <summary>Flies <paramref name="recipe"/> from its own start, unperturbed.</summary>
    public JumpOutcome Fly(in JumpRecipe recipe) => _flier.Fly(recipe, recipe.Start(), _settings.MaxCommands);

    /// <summary>The seconds a landing costs when a failed jump is jumped again: the seconds over the robustness.</summary>
    public static float Cost(float seconds, float robustness) => robustness > 0f ? seconds / robustness : float.PositiveInfinity;

    /// <summary>The search: see the class.</summary>
    public JumpSearchResult Find(in JumpAsk ask)
    {
        JumpSearchResult single = FindSingle(ask);
        JumpFound? best = single.Found;
        int flights = single.Flights;
        foreach (JumpFamily family in (ReadOnlySpan<JumpFamily>)[JumpFamily.Double, JumpFamily.Wall])
        {
            // The two-rocket families in order, each only when asked and while nothing simpler is worth keeping.
            if ((ask.Families & (family == JumpFamily.Double ? JumpFamilies.Double : JumpFamilies.Wall)) == 0 || best is { } kept && kept.Robustness >= _settings.Keep)
                continue;
            JumpFound? two = FindTwo(ask, TwoOf(family), ref flights);
            best = best is { } held ? two is { } d ? Better(held, d, _settings.Keep) : held : two;
        }

        return new JumpSearchResult(best, flights);
    }

    /// <summary>
    /// What a two-rocket family's search tries (lanes G7b, G7c): its first shot's grid, the second rocket's fire commands
    /// from the launcher's refire (<paramref name="Steps"/>, every <see cref="JumpSearchSettings.SecondStep"/>), the second
    /// shot's views (pitches by yaw offsets), whether it is fired at a wall, and the flights it may spend.
    /// </summary>
    private readonly record struct TwoRockets(JumpFamily Family, JumpGrid First, int Steps, float PitchFrom, float PitchStep, int Pitches,
                                              float YawFrom, float YawStep, int Yaws, bool Mirrored, bool Wall, int MaxFlights)
    {
        /// <summary>The second shot's views a grid cell of the first's tries.</summary>
        public int Views => Pitches * Yaws * (Mirrored ? 2 : 1);

        /// <summary>The <paramref name="v"/>th view: (pitch, yaw offset).</summary>
        public (float Pitch, float Yaw) View(int v)
        {
            int side = Mirrored ? v % 2 : 0, rest = Mirrored ? v / 2 : v;
            float yaw = YawFrom + rest % Yaws * YawStep;
            return (PitchFrom + rest / Yaws * PitchStep, side == 1 ? -yaw : yaw);
        }
    }

    private TwoRockets TwoOf(JumpFamily family) => family == JumpFamily.Double
        ? new(family, _settings.Double, _settings.SecondSteps, _settings.SecondPitch, 0f, 1, _settings.SecondYawOffset, 0f, 1, false, false, _settings.DoubleMaxFlights)
        : new(family, _settings.WallFirst, _settings.WallSecondSteps, _settings.WallSecondPitchFrom, _settings.WallSecondPitchStep, _settings.WallSecondPitches,
              _settings.WallSecondYawFrom, _settings.WallSecondYawStep, _settings.WallSecondYaws, true, true, _settings.WallMaxFlights);

    /// <summary>The single families' search: the class's, as it was before a second rocket (Up and Forward as the ask names them).</summary>
    private JumpSearchResult FindSingle(in JumpAsk ask)
    {
        _hits.Clear();
        int flights = 0;
        Hit nearest = default;
        float nearestMiss = float.PositiveInfinity;
        Span<bool> made = stackalloc bool[MaxGrid];
        if (ask.Hint is { } hint && (ask.Families & (hint.Family == JumpFamily.Up ? JumpFamilies.Up : JumpFamilies.Forward)) != 0 && hint.Family is JumpFamily.Up or JumpFamily.Forward)
            TryHint(ask, hint, made, ref flights, ref nearest, ref nearestMiss);
        for (int f = 0; f < 2; f++)
        {
            var family = (JumpFamily)f;
            if ((ask.Families & (family == JumpFamily.Up ? JumpFamilies.Up : JumpFamilies.Forward)) == 0)
                continue;
            JumpGrid grid = family == JumpFamily.Up ? _settings.Up : _settings.Forward;
            float runUp = StandableRunUp(ask, grid.RunUp);
            if (runUp < 0f)
                continue;
            int pitches = grid.Pitches, delays = grid.Delays, first = _hits.Count;
            if (pitches * delays > MaxGrid)
                throw new InvalidOperationException($"a grid of {pitches} pitches by {delays} delays is over {MaxGrid} flights");
            made.Clear();
            for (int p = 0; p < pitches; p++)
            {
                for (int d = 0; d < delays; d++)
                {
                    var recipe = new JumpRecipe(ask.Takeoff, ask.Landing, runUp, grid.PitchFrom + p * grid.PitchStep, _settings.YawOffset,
                                                grid.DelayFrom + d * grid.DelayStep, Ducked: true);
                    made[p * delays + d] = Try(ask, recipe, family, p * delays + d, ref flights, ref nearest, ref nearestMiss);
                }
            }

            // A hit's neighbours: the cells a pitch step or a delay step away that hit too. Neighbours holds the cell until here.
            for (int i = first; i < _hits.Count; i++)
                _hits[i] = _hits[i] with { Neighbours = Neighbours(made, pitches, delays, _hits[i].Neighbours) };
        }

        // Refined, when nothing hit: round the nearest miss.
        if (_hits.Count == 0 && !float.IsPositiveInfinity(nearestMiss))
        {
            Hit centre = nearest;
            float half = (centre.Family == JumpFamily.Up ? _settings.Up : _settings.Forward).PitchStep * 0.5f;
            for (int p = -1; p <= 1; p++)
            {
                for (int d = -1; d <= 1; d++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        float pitch = centre.Recipe.Pitch + p * half;
                        if ((p == 0 && d == 0 && y == 0) || pitch > MaxPitch || pitch < -MaxPitch)
                            continue;
                        Try(ask, centre.Recipe with { Pitch = pitch, ShotDelay = centre.Recipe.ShotDelay + d, YawOffset = centre.Recipe.YawOffset + y * _settings.RefineYaw },
                            centre.Family, 0, ref flights, ref nearest, ref nearestMiss);
                    }
                }
            }
        }

        if (_hits.Count == 0)
            return new JumpSearchResult(null, flights);

        // Robustness, the likeliest first.
        _hits.Sort(static (a, b) => a.Likely != b.Likely ? a.Likely.CompareTo(b.Likely) : a.Miss.CompareTo(b.Miss));
        float? walk = _walk.Seconds(ask.Takeoff, ask.Landing);
        JumpFound? best = null;
        for (int i = 0; i < _hits.Count && i < _settings.Measured && flights + _settings.Trials <= _settings.MaxFlights; i++)
            best = Better(best, Measure(ask, _hits[i], walk, ref flights), _settings.Keep);

        // Refined: half a pitch step either side of the answer, and on from one that is better, while there are flights.
        for (bool better = best is { } kept && kept.Robustness >= _settings.Keep; better;)
        {
            better = false;
            JumpFound answer = best!.Value;
            float half = (answer.Family == JumpFamily.Up ? _settings.Up : _settings.Forward).PitchStep * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                float pitch = answer.Recipe.Pitch + side * half;
                if (pitch > MaxPitch || pitch < -MaxPitch || flights + 1 + _settings.Trials > _settings.MaxFlights)
                    continue;
                int before = _hits.Count;
                if (!Try(ask, answer.Recipe with { Pitch = pitch }, answer.Family, 0, ref flights, ref nearest, ref nearestMiss)
                    || _hits[before].Outcome.Seconds >= answer.Seconds)
                {
                    continue;
                }

                JumpFound stepped = Better(best, Measure(ask, _hits[before], walk, ref flights), _settings.Keep);
                better |= stepped != best;
                best = stepped;
            }
        }

        return new JumpSearchResult(best, flights);
    }

    /// <summary>
    /// A hint's neighbourhood (lane G6b), flown before the grids: its pitch and half its family's pitch step either way, by
    /// its delay and a command either way, from its run-up (halved, as a grid's, when a body cannot stand where it starts).
    /// Its hits join the grids' with their neighbours counted in the 3 x 3; its misses may be the nearest.
    /// </summary>
    private void TryHint(in JumpAsk ask, in JumpHint hint, Span<bool> made, ref int flights, ref Hit nearest, ref float nearestMiss)
    {
        float runUp = StandableRunUp(ask, hint.RunUp);
        if (runUp < 0f)
            return;
        float half = (hint.Family == JumpFamily.Up ? _settings.Up : _settings.Forward).PitchStep * 0.5f;
        int first = _hits.Count;
        made.Clear();
        for (int p = 0; p < HintSide; p++)
        {
            for (int d = 0; d < HintSide; d++)
            {
                float pitch = hint.Pitch + (p - 1) * half;
                if (pitch > MaxPitch || pitch < -MaxPitch)
                    continue;
                var recipe = new JumpRecipe(ask.Takeoff, ask.Landing, runUp, pitch, hint.YawOffset, hint.ShotDelay + d - 1, Ducked: true);
                made[p * HintSide + d] = Try(ask, recipe, hint.Family, p * HintSide + d, ref flights, ref nearest, ref nearestMiss);
            }
        }

        for (int i = first; i < _hits.Count; i++)
            _hits[i] = _hits[i] with { Neighbours = Neighbours(made, HintSide, HintSide, _hits[i].Neighbours) };
    }

    /// <summary>A hint's neighbourhood is this many pitches by this many delays.</summary>
    public const int HintSide = 3;

    /// <summary>
    /// A two-rocket flight that made its landing: the recipe (its second shot's place found), the command its second rocket
    /// was fired on in the grid's flight and the second view it was fired along, its outcome, its grid cell (-1 off the grid)
    /// and how many of its grid neighbours made it too.
    /// </summary>
    private readonly record struct DoubleHit(JumpRecipe Recipe, int At, int View, JumpOutcome Outcome, float Miss, int Cell, int Neighbours);

    /// <summary>
    /// A two-rocket family's search: a jump up from standing whose second rocket, fired in the air once the launcher is ready
    /// again, meets a floor the flight passes over (<see cref="JumpFamily.Double"/>, lane G7b) or a wall beside it
    /// (<see cref="JumpFamily.Wall"/>, lane G7c) and lifts the body a second time.
    ///
    /// <list type="number">
    /// <item><b>Coarse.</b> The first shot's grid, each with its second rocket fired on a fixed command wherever the body is
    /// (the launcher's refire, then every <see cref="JumpSearchSettings.SecondStep"/> on) along each of the family's second
    /// views (straight down and behind for a Double; at either side, a few pitches and yaws, for a Wall). Where the body was
    /// as that rocket left (and the height it went off at) is the recipe's <see cref="JumpSecondShot.Over"/>, the pilot then
    /// firing as the body comes over it; that recipe is flown again, and it is a hit when it makes the landing with both
    /// blasts felt. A rocket that met nothing in reach makes no recipe.</item>
    /// <item><b>Refined, when nothing hit.</b> Round the nearest miss: half a pitch step, a command of the first shot and
    /// three of the second, each way.</item>
    /// <item><b>Robustness</b> as the single families', the second shot's view and place perturbed too
    /// (<see cref="Perturbed"/>); the hits with the most neighbours that made it first, then the quickest. Then half a
    /// pitch step either side of the answer, as the single families'.</item>
    /// </list>
    /// At most the family's flights (<see cref="JumpSearchSettings.DoubleMaxFlights"/>, <see cref="JumpSearchSettings.WallMaxFlights"/>).
    /// </summary>
    private JumpFound? FindTwo(in JumpAsk ask, in TwoRockets two, ref int flights)
    {
        JumpGrid grid = two.First;
        float runUp = StandableRunUp(ask, grid.RunUp);
        if (runUp < 0f)
            return null;
        int spent = 0, earliest = _flier.Sim.RefireCommands, pitches = grid.Pitches, delays = grid.Delays, steps = two.Steps, views = two.Views;
        var made = new bool[pitches * delays * steps * views];
        _doubles.Clear();
        (JumpRecipe Recipe, int At, int View)? nearest = null;
        float nearestMiss = float.PositiveInfinity;
        for (int p = 0; p < pitches; p++)
        {
            for (int d = 0; d < delays; d++)
            {
                for (int k = 0; k < steps; k++)
                {
                    for (int v = 0; v < views; v++)
                    {
                        int cell = ((p * delays + d) * steps + k) * views + v;
                        var first = new JumpRecipe(ask.Takeoff, ask.Landing, runUp, grid.PitchFrom + p * grid.PitchStep, _settings.YawOffset, grid.DelayFrom + d * grid.DelayStep, Ducked: true);
                        made[cell] = TryDouble(ask, two, first, earliest + k * _settings.SecondStep, v, earliest, cell, ref spent, ref nearest, ref nearestMiss);
                    }
                }
            }
        }

        for (int i = 0; i < _doubles.Count; i++)
            _doubles[i] = _doubles[i] with { Neighbours = DoubleNeighbours(made, pitches, delays, steps, views, _doubles[i].Cell) };

        // Refined, when nothing hit: round the nearest miss.
        if (_doubles.Count == 0 && nearest is { } centre)
        {
            float half = grid.PitchStep * 0.5f;
            for (int p = -1; p <= 1; p++)
            {
                for (int d = -1; d <= 1; d++)
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        float pitch = centre.Recipe.Pitch + p * half;
                        int at = Math.Max(earliest, centre.At + k * 3);
                        if ((p == 0 && d == 0 && k == 0) || pitch > MaxPitch || pitch < -MaxPitch)
                            continue;
                        TryDouble(ask, two, centre.Recipe with { Pitch = pitch, ShotDelay = centre.Recipe.ShotDelay + d }, at, centre.View, earliest, -1, ref spent, ref nearest, ref nearestMiss);
                    }
                }
            }
        }

        JumpFound? best = null;
        if (_doubles.Count > 0)
        {
            _doubles.Sort(static (a, b) => a.Neighbours != b.Neighbours ? b.Neighbours.CompareTo(a.Neighbours)
                : a.Outcome.Seconds != b.Outcome.Seconds ? a.Outcome.Seconds.CompareTo(b.Outcome.Seconds) : a.Miss.CompareTo(b.Miss));
            float? walk = _walk.Seconds(ask.Takeoff, ask.Landing);
            int measured = Math.Min(_doubles.Count, _settings.Measured);
            DoubleHit[] likeliest = [.. _doubles.Take(measured)];
            var at = new Dictionary<JumpRecipe, (int At, int View)>();
            foreach (DoubleHit hit in likeliest)
            {
                if (spent + _settings.Trials > two.MaxFlights)
                    break;
                JumpFound measuredHit = MeasureDouble(ask, hit, two.Family, walk, ref spent);
                at[measuredHit.Recipe] = (hit.At, hit.View);
                best = Better(best, measuredHit, _settings.Keep);
            }

            // Refined: half a pitch step either side of the answer, and on from one that is better, while there are flights.
            for (bool better = best is { } kept && kept.Robustness >= _settings.Keep; better;)
            {
                better = false;
                JumpFound answer = best!.Value;
                for (int side = -1; side <= 1; side += 2)
                {
                    float pitch = answer.Recipe.Pitch + side * grid.PitchStep * 0.5f;
                    if (pitch > MaxPitch || pitch < -MaxPitch || spent + 2 + _settings.Trials > two.MaxFlights)
                        continue;
                    int before = _doubles.Count;
                    (int fireAt, int view) = at[answer.Recipe];
                    if (!TryDouble(ask, two, answer.Recipe with { Pitch = pitch, Second = null }, fireAt, view, earliest, -1, ref spent, ref nearest, ref nearestMiss)
                        || _doubles[before].Outcome.Seconds >= answer.Seconds)
                    {
                        continue;
                    }

                    JumpFound stepped = Better(best, MeasureDouble(ask, _doubles[before], two.Family, walk, ref spent), _settings.Keep);
                    at.TryAdd(_doubles[before].Recipe, (fireAt, view));
                    better |= stepped != best;
                    best = stepped;
                }
            }
        }

        flights += spent;
        return best;
    }

    private JumpFound MeasureDouble(in JumpAsk ask, in DoubleHit hit, JumpFamily family, float? walk, ref int spent)
        => new(hit.Recipe, family, Robustness(ask, hit.Recipe, ref spent), hit.Outcome.Seconds, hit.Outcome.AirSeconds, hit.Outcome.BlastDamage, walk, hit.Miss);

    /// <summary>
    /// One two-rocket grid flight: <paramref name="first"/>'s jump with its second rocket fired on command <paramref name="at"/>
    /// after the first trigger wherever the body is, along the family's view <paramref name="view"/>; then the recipe whose
    /// second shot is fired where the body was then, flown again. True when that one made the landing with both its blasts
    /// felt (kept in the hits, its grid cell with it); else it may be the nearest miss yet.
    /// </summary>
    private bool TryDouble(in JumpAsk ask, in TwoRockets two, in JumpRecipe first, int at, int view, int earliest, int cell, ref int spent,
                           ref (JumpRecipe Recipe, int At, int View)? nearest, ref float nearestMiss)
    {
        if (spent + 2 > two.MaxFlights)
            return false;

        // Fired on its command: a place far behind the takeoff the body is past from the start, a height under any it flies
        // over, and a time of that one command.
        (float secondPitch, float secondYaw) = two.View(view);
        float yaw = first.TravelYaw * (MathF.PI / 180f);
        var passed = new Vector(ask.Takeoff.X - MathF.Cos(yaw) * JumpEnvelope.Far, ask.Takeoff.Y - MathF.Sin(yaw) * JumpEnvelope.Far, float.MinValue);
        JumpRecipe timed = first with { Second = new JumpSecondShot(passed, at, at, secondPitch, secondYaw) { Wall = two.Wall } };
        _track.Clear();
        _sent.Clear();
        JumpOutcome flown = _flier.Fly(timed, timed.Start(), _settings.MaxCommands, _track, _sent);
        spent++;
        int press = SecondPress(_sent, first);
        JumpImpact? blast = null;
        if (press > 0)
        {
            foreach (JumpImpact i in _flier.Sim.Impacts)
            {
                if (i.Command >= press && i.Damage > 0f)
                {
                    blast = i;
                    break;
                }
            }
        }

        if (blast is not { } struck)
        {
            Nearer(ask, flown, first, at, view, ref nearest, ref nearestMiss);
            return false;
        }

        Vector seen = _track[press - 1].Origin;
        JumpRecipe recipe = first with
        {
            Second = new JumpSecondShot(new Vector(seen.X, seen.Y, struck.At.Z), earliest, at + _settings.SecondLateCommands, secondPitch, secondYaw) { Wall = two.Wall },
        };
        JumpOutcome again = Fly(recipe);
        spent++;
        int blasts = 0;
        foreach (JumpImpact i in _flier.Sim.Impacts)
            blasts += i.Damage > 0f ? 1 : 0;
        if (!Made(ask, again) || blasts < 2)
        {
            Nearer(ask, again, first, at, view, ref nearest, ref nearestMiss);
            return false;
        }

        _doubles.Add(new DoubleHit(recipe, at, view, again, Flat(again.Rest, ask.Landing), cell, 0));
        return true;
    }

    /// <summary>A miss kept when it is the nearest yet: over the ground and off in height (a rest on the floor under a ledge is far).</summary>
    private static void Nearer(in JumpAsk ask, in JumpOutcome outcome, in JumpRecipe first, int at, int view, ref (JumpRecipe Recipe, int At, int View)? nearest, ref float nearestMiss)
    {
        float miss = Flat(outcome.Rest, ask.Landing) + MathF.Abs(outcome.Rest.Z - ask.Landing.Z);
        if (miss < nearestMiss)
        {
            nearestMiss = miss;
            nearest = (first with { Second = null }, at, view);
        }
    }

    /// <summary>The first command of the second trigger in <paramref name="sent"/>: the first pressing it after the first trigger's run, or -1.</summary>
    private static int SecondPress(List<JumpCommand> sent, in JumpRecipe recipe)
    {
        int first = sent.FindIndex(c => (c.Buttons & GameMovement.InAttack) != 0);
        if (first < 0)
            return -1;
        int i = first;
        while (i < sent.Count && (sent[i].Buttons & GameMovement.InAttack) != 0)
            i++;
        for (; i < sent.Count; i++)
        {
            if ((sent[i].Buttons & GameMovement.InAttack) != 0)
                return i;
        }

        return -1;
    }

    /// <summary>How many of a two-rocket grid cell's neighbours (a step of pitch, of delay or of the second's command either way; the same second view) made it too.</summary>
    private static int DoubleNeighbours(bool[] made, int pitches, int delays, int steps, int views, int cell)
    {
        int k = cell / views % steps, d = cell / (views * steps) % delays, p = cell / (views * steps * delays), n = 0;
        int kStride = views, dStride = views * steps, pStride = views * steps * delays;
        n += p > 0 && made[cell - pStride] ? 1 : 0;
        n += p + 1 < pitches && made[cell + pStride] ? 1 : 0;
        n += d > 0 && made[cell - dStride] ? 1 : 0;
        n += d + 1 < delays && made[cell + dStride] ? 1 : 0;
        n += k > 0 && made[cell - kStride] ? 1 : 0;
        n += k + 1 < steps && made[cell + kStride] ? 1 : 0;
        return n;
    }

    /// <summary>The steepest a view looks: the engine holds the pitch inside it.</summary>
    public const float MaxPitch = 89f;

    /// <summary>The most flights one family's grid may be.</summary>
    public const int MaxGrid = 64;

    private JumpFound Measure(in JumpAsk ask, in Hit hit, float? walk, ref int flights)
        => new(hit.Recipe, hit.Family, Robustness(ask, hit.Recipe, ref flights), hit.Outcome.Seconds, hit.Outcome.AirSeconds, hit.Outcome.BlastDamage, walk, hit.Miss);

    /// <summary>
    /// Of two measured jumps the one to answer with: one worth keeping (its
    /// robustness <paramref name="keep"/> or more) over one that is not, then
    /// the less seconds over robustness, and between two not worth keeping the
    /// more robust; the one held on a tie.
    /// </summary>
    internal static JumpFound Better(JumpFound? held, JumpFound found, float keep)
    {
        if (held is not { } best)
            return found;
        bool keepBest = best.Robustness >= keep, keepFound = found.Robustness >= keep;
        if (keepBest != keepFound)
            return keepFound ? found : best;
        if (!keepFound)
            return found.Robustness > best.Robustness ? found : best;
        return Cost(found.Seconds, found.Robustness) < Cost(best.Seconds, best.Robustness) ? found : best;
    }

    /// <summary>
    /// How many of a grid cell's neighbours hit: the cells a pitch step or a
    /// delay step either way, in a grid of <paramref name="pitches"/> rows of
    /// <paramref name="delays"/>.
    /// </summary>
    internal static int Neighbours(ReadOnlySpan<bool> made, int pitches, int delays, int cell)
    {
        int p = cell / delays, d = cell % delays;
        return (p > 0 && made[cell - delays] ? 1 : 0) + (p + 1 < pitches && made[cell + delays] ? 1 : 0)
               + (d > 0 && made[cell - 1] ? 1 : 0) + (d + 1 < delays && made[cell + 1] ? 1 : 0);
    }

    /// <summary>
    /// The share of <see cref="JumpSearchSettings.Trials"/> perturbed flights of
    /// <paramref name="recipe"/> that still make <paramref name="ask"/>'s
    /// landing (<see cref="Draw"/>, <see cref="Perturbed"/>); the pilot steers
    /// each for the same landing. Drawn from the ask's seed.
    /// </summary>
    public float Robustness(in JumpAsk ask, in JumpRecipe recipe, ref int flights)
    {
        uint state = ask.Seed;
        int made = 0;
        for (int t = 0; t < _settings.Trials; t++)
        {
            (JumpRecipe flown, JumpStart start) = Perturbed(recipe, Draw(ref state, _settings, recipe.Second is not null));
            JumpOutcome outcome = _flier.Fly(flown, start, _settings.MaxCommands);
            flights++;
            if (Made(ask, outcome))
                made++;
        }

        return _settings.Trials == 0 ? 0f : (float)made / _settings.Trials;
    }

    /// <summary>
    /// One trial's perturbation: how far over the ground the body starts from
    /// the recipe's start, how far off the shot's view is in pitch and in
    /// yaw, and how many commands early (negative) or late the trigger comes.
    /// </summary>
    internal readonly record struct Trial(float StartX, float StartY, float Pitch, float Yaw, int Shot)
    {
        /// <summary>A second shot's: its view off in pitch and yaw, and its place moved along the way (lane G7b); none for a one-rocket recipe's trial.</summary>
        public float SecondPitch { get; init; }

        public float SecondYaw { get; init; }

        public float SecondAlong { get; init; }
    }

    /// <summary>
    /// The next trial of a seed's run: each part uniform within its bound
    /// (<see cref="JumpSearchSettings.StartJitter"/> each way along x and y,
    /// <see cref="JumpSearchSettings.ViewJitter"/> degrees in pitch and in
    /// yaw, <see cref="JumpSearchSettings.ShotJitter"/> commands).
    /// </summary>
    internal static Trial Draw(ref uint state, JumpSearchSettings settings, bool second = false)
    {
        // A recipe with a second shot is perturbed as the program flies a Double's first shot (JumpSearchSettings.DoubleStartJitter).
        float start = second ? settings.DoubleStartJitter : settings.StartJitter, view = second ? settings.DoubleViewJitter : settings.ViewJitter;
        int shotJitter = second ? settings.DoubleShotJitter : settings.ShotJitter;
        float x = Signed(ref state) * start, y = Signed(ref state) * start;
        float pitch = Signed(ref state) * view, yaw = Signed(ref state) * view;
        int shot = (int)(Next(ref state) % (uint)(2 * shotJitter + 1)) - shotJitter;
        var trial = new Trial(x, y, pitch, yaw, shot);
        if (!second)
            return trial;

        // The second shot's, drawn after the first's: a one-rocket recipe's trials are the draws they always were.
        float secondPitch = Signed(ref state) * settings.ViewJitter, secondYaw = Signed(ref state) * settings.ViewJitter;
        return trial with { SecondPitch = secondPitch, SecondYaw = secondYaw, SecondAlong = Signed(ref state) * settings.StartJitter };
    }

    /// <summary>
    /// A recipe as a trial flies it: started <paramref name="trial"/>'s
    /// offset from its own start, the shot's view and its delay off by the
    /// trial's. The takeoff and the landing are the recipe's: the pilot still
    /// means the same jump.
    /// </summary>
    internal static (JumpRecipe Recipe, JumpStart Start) Perturbed(in JumpRecipe recipe, in Trial trial)
    {
        JumpStart start = recipe.Start();
        JumpRecipe flown = recipe with
        {
            Pitch = Math.Clamp(recipe.Pitch + trial.Pitch, -MaxPitch, MaxPitch), YawOffset = recipe.YawOffset + trial.Yaw, ShotDelay = recipe.ShotDelay + trial.Shot,
        };
        if (recipe.Second is { } s)
        {
            // The second shot's view off as the first's is, and its place along the way off as the start is.
            float yaw = recipe.TravelYaw * (MathF.PI / 180f);
            flown = flown with
            {
                Second = s with
                {
                    Pitch = Math.Clamp(s.Pitch + trial.SecondPitch, -MaxPitch, MaxPitch), YawOffset = s.YawOffset + trial.SecondYaw,
                    Over = s.Over + new Vector(MathF.Cos(yaw) * trial.SecondAlong, MathF.Sin(yaw) * trial.SecondAlong, 0f),
                },
            };
        }

        return (flown, start with { Origin = start.Origin + new Vector(trial.StartX, trial.StartY, 0f) });
    }

    /// <summary>One unperturbed flight of the search: a hit is kept (its grid cell in <see cref="Hit.Neighbours"/> until the grid is done), a miss is the nearest yet or not.</summary>
    private bool Try(in JumpAsk ask, in JumpRecipe recipe, JumpFamily family, int cell, ref int flights, ref Hit nearest, ref float nearestMiss)
    {
        if (flights >= _settings.MaxFlights)
            return false;
        JumpOutcome outcome = Fly(recipe);
        flights++;
        float flat = Flat(outcome.Rest, ask.Landing);
        if (Made(ask, outcome))
        {
            _hits.Add(new Hit(recipe, family, outcome, flat, cell));
            return true;
        }

        // How far it was from making it: over the ground, and the height it was off by (a rest on the floor under a ledge is far).
        float miss = flat + MathF.Abs(outcome.Rest.Z - ask.Landing.Z);
        if (miss < nearestMiss)
        {
            nearestMiss = miss;
            nearest = new Hit(recipe, family, outcome, miss, 0);
        }

        return false;
    }

    /// <summary>
    /// The run-up a family's flights start at: the grid's, or half of it when a
    /// body cannot stand where that starts, or -1 when it cannot there either
    /// (the takeoff itself, for a jump from standing).
    /// </summary>
    private float StandableRunUp(in JumpAsk ask, float runUp)
    {
        for (int halved = 0; halved < 2; halved++)
        {
            var probe = new JumpRecipe(ask.Takeoff, ask.Landing, runUp, 0f, 0f, 0, false);
            if (Stands(probe.Start()))
                return runUp;
            if (runUp <= 0f)
                break;
            runUp *= 0.5f;
        }

        return -1f;
    }

    /// <summary>
    /// Whether a body put at <paramref name="start"/> stands there: its
    /// standing box is in no solid, and three idle commands leave it on the
    /// ground where it was.
    /// </summary>
    private bool Stands(in JumpStart start)
    {
        BlastJumpSim sim = _flier.Sim;
        MovementParams body = TFViewVectors.Params;
        if (sim.World.Trace(start.Origin, start.Origin, body.HullMin, body.HullMax, 0u, 0).StartSolid)
            return false;
        sim.Begin(start);
        var idle = new JumpCommand(start.View, 0f, 0f, 0f, 0);
        JumpPoint point = default;
        for (int i = 0; i < 3; i++)
            point = sim.Step(idle);
        return point.OnGround && Flat(point.Origin, start.Origin) < 1f && MathF.Abs(point.Origin.Z - start.Origin.Z) < 2f;
    }

    private static float Flat(Vector a, Vector b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The trials' own generator (xorshift32), so a seed draws the same trials on every runtime.</summary>
    private static uint Next(ref uint state)
    {
        if (state == 0)
            state = 0x9E3779B9u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>A draw in [-1, 1].</summary>
    private static float Signed(ref uint state) => (Next(ref state) >> 8) * (2f / 16777215f) - 1f;
}
