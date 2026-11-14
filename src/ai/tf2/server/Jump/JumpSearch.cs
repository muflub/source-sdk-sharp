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
}

/// <summary>
/// One jump asked for: from where, to where, how far over the ground from the
/// landing a body may come to rest and still have made it, which families to
/// try, and the seed its perturbed trials are drawn from.
/// </summary>
public readonly record struct JumpAsk(Vector Takeoff, Vector Landing, float Tolerance, JumpFamilies Families = JumpFamilies.Both, uint Seed = 1);

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
        _hits.Clear();
        int flights = 0;
        Hit nearest = default;
        float nearestMiss = float.PositiveInfinity;
        Span<bool> made = stackalloc bool[MaxGrid];
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
            (JumpRecipe flown, JumpStart start) = Perturbed(recipe, Draw(ref state, _settings));
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
    internal readonly record struct Trial(float StartX, float StartY, float Pitch, float Yaw, int Shot);

    /// <summary>
    /// The next trial of a seed's run: each part uniform within its bound
    /// (<see cref="JumpSearchSettings.StartJitter"/> each way along x and y,
    /// <see cref="JumpSearchSettings.ViewJitter"/> degrees in pitch and in
    /// yaw, <see cref="JumpSearchSettings.ShotJitter"/> commands).
    /// </summary>
    internal static Trial Draw(ref uint state, JumpSearchSettings settings)
    {
        float x = Signed(ref state) * settings.StartJitter, y = Signed(ref state) * settings.StartJitter;
        float pitch = Signed(ref state) * settings.ViewJitter, yaw = Signed(ref state) * settings.ViewJitter;
        int shot = (int)(Next(ref state) % (uint)(2 * settings.ShotJitter + 1)) - settings.ShotJitter;
        return new Trial(x, y, pitch, yaw, shot);
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
