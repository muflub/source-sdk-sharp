using SourceSharp.Abi;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What the learn pass made of one jump a player showed (lane G6b).</summary>
public enum ShownFitVerdict : byte
{
    /// <summary>A recipe was found for it and it joins the level's links.</summary>
    Fitted,

    /// <summary>Seen too seldom now, but kept: the bots' own tries of its link landed (its <c>Solver</c> row).</summary>
    Kept,

    /// <summary>Seen too seldom (<see cref="ShownJumpFitSettings.MinSightings"/>), and no bot has landed its link.</summary>
    Few,

    /// <summary>Past the most a pass searches (<see cref="ShownJumpFitSettings.MaxAsks"/>).</summary>
    Capped,

    /// <summary>Its takeoff or its landing is in no cell of the graph.</summary>
    NoCell,

    /// <summary>Its takeoff and its landing are one cell.</summary>
    Same,

    /// <summary>One of the level's own links already goes there from there (<see cref="ShownJumpJudge.Knows"/>).</summary>
    Known,

    /// <summary>No family of the search has a body standing where it would start.</summary>
    NoStand,

    /// <summary>The search found no recipe that lands in <see cref="JumpSearchSettings.Keep"/> of its trials.</summary>
    NoRecipe,

    /// <summary>The recipe found, flown again, comes to rest in no cell at its height.</summary>
    Rests,

    /// <summary>It fits a jump another shown jump, seen more, already gave a link for.</summary>
    Twin,

    /// <summary>A practice row's link, verified in the simulator this level (<see cref="JumpPractice"/>, lane G8), already goes there from there: not searched.</summary>
    Practiced,
}

/// <summary>One shown jump with its verdict, its link when it gives one, and the flights its search took in this pass (0 when an earlier one did).</summary>
public sealed record ShownFit(ExperienceShownJump Jump, ShownFitVerdict Verdict, JumpLink? Link, int Flights);

/// <summary>What a learn pass answered: every Soldier blast jump of the snapshot with its verdict, the links in the order they join, and the flights it flew.</summary>
public sealed record ShownFitResult(IReadOnlyList<ShownFit> Fits, IReadOnlyList<JumpLink> Links, int Flights, int Searched);

/// <summary>The learn pass's numbers (lane G6b).</summary>
public sealed record ShownJumpFitSettings
{
    /// <summary>
    /// The sightings (the count row's decayed tries) a shown jump needs to be proposed: one sighting within a half-life
    /// (a row fades 0.5 each <c>ai_experience_half_life</c> rounds). Low on purpose: the fit is the filter (a recipe at the
    /// shape pass's robustness) and the bots' tries are the trust (<see cref="LinkTrust"/>).
    /// </summary>
    public double MinSightings { get; init; } = 0.5d;

    /// <summary>The most shown jumps a pass searches, the most seen first: what a store holding hundreds costs a level.</summary>
    public int MaxAsks { get; init; } = 12;

    /// <summary>How far from the landing a fitted jump may come to rest: the shape pass's (<see cref="JumpPlacesSettings.Tolerance"/>).</summary>
    public float Tolerance { get; init; } = 24f;

    /// <summary>A takeoff faster than this over the ground ran at the jump (the Forward family's hint); slower, stood (Up).</summary>
    public float RanSpeed { get; init; } = 120f;

    /// <summary>The share of one rocket's rise past which a second rocket is asked of the search too (the shape pass's <see cref="JumpPlacesSettings.ReachShare"/>).</summary>
    public float ReachShare { get; init; } = 0.9f;
}

/// <summary>
/// Fits a recipe to each jump a player showed (plan_bots_teams_tf2_part_g G.6, lane G6b): the experience's shown jumps of
/// a Soldier's blast kind become asks of the jump search, from the exemplar's takeoff to its landing (put on their cells'
/// floors), with the exemplar's view at the shot, its delay and whether it ran as the search's hint
/// (<see cref="JumpHint"/>): where to look first, never what to fly. A recipe found at the shape pass's robustness, whose
/// flight comes to rest on a cell, is a link like the shape pass's, marked <see cref="JumpLink.Shown"/>.
///
/// <para>
/// What it leaves out, each with its verdict: a jump the level's own links already make (<see cref="ShownJumpJudge.Knows"/>,
/// the watch's rule: one link, not two); one seen fewer than <see cref="ShownJumpFitSettings.MinSightings"/> times, unless
/// the bots' own tries of its link landed (its <c>Solver</c> row's successes as many: a person repeating a jump the level
/// now has is judged known by the watch and stops counting, so a working link outlives its human row); one past
/// <see cref="ShownJumpFitSettings.MaxAsks"/>; one with no cell under its ends; one the search finds no robust recipe for
/// or whose rest is on no cell; one that fits where a more seen one already did.
/// </para>
///
/// <para>
/// Each jump (its count row's key and its exemplar) is searched once in the fitter's life (a level's): a new snapshot
/// costs only the jumps it has not seen. It holds a search, so it serves one thread at a time.
/// </para>
/// </summary>
public sealed class ShownJumpFit
{
    private readonly NavGraph _graph;
    private readonly JumpSearch _search;
    private readonly Func<JumpEnvelope> _reach;
    private readonly ShownJumpFitSettings _settings;
    private readonly Dictionary<(LinkKey Count, ShownJumpRecipe? Exemplar), Searched> _searched = new();
    private JumpEnvelope? _envelope;

    /// <param name="reach">The jump's reach, asked once, at the first search (it flies the envelope).</param>
    public ShownJumpFit(NavGraph graph, JumpSearch search, Func<JumpEnvelope> reach, ShownJumpFitSettings settings)
    {
        _graph = graph;
        _search = search;
        _reach = reach;
        _settings = settings;
    }

    private readonly record struct Searched(ShownFitVerdict Verdict, JumpLink? Link, int Flights);

    /// <summary>The jumps searched in its life.</summary>
    public int Remembered => _searched.Count;

    /// <summary>
    /// The pass over <paramref name="snapshot"/>'s shown jumps, against <paramref name="level"/>, the keys of the level's own
    /// links (null for none), and <paramref name="practiced"/>, the rows of the practice links taken this pass (lane G8;
    /// null for none). <paramref name="cancel"/> is looked at before each search.
    /// </summary>
    public ShownFitResult Fit(MapExperience snapshot, ExperienceLinkKeys? level, CancellationToken cancel = default, IReadOnlyList<LinkKey>? practiced = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int soldier = (int)TFClass.Soldier;

        // The most seen first; among as seen, the view's order (by place), so a pass is the same every time.
        ExperienceShownJump[] jumps = [.. snapshot.For(ExperienceContext.All).ShownJumps()
                                                 .Where(j => j.Key.Class == soldier && j.Kind == ShownJumpKind.Blast)
                                                 .OrderByDescending(j => j.Stat.Tries)];
        var fits = new List<ShownFit>(jumps.Length);
        var links = new List<JumpLink>();
        var keys = new List<LinkKey>();
        int flights = 0, asked = 0;
        foreach (ExperienceShownJump j in jumps)
        {
            bool seen = j.Stat.Tries >= _settings.MinSightings;
            if (level is not null && ShownJumpJudge.Knows(level, soldier, j.Key.From, j.Key.To))
            {
                fits.Add(new ShownFit(j, ShownFitVerdict.Known, null, 0));
                continue;
            }

            // A practice recipe for the place was flown first (lane G8): verified, it answers the jump, and nothing is searched.
            if (practiced is not null && practiced.Any(k => k.Class == soldier && Near(k, j.Key)))
            {
                fits.Add(new ShownFit(j, ShownFitVerdict.Practiced, null, 0));
                continue;
            }

            if (!seen && !BotsLanded(snapshot, j.Key))
            {
                fits.Add(new ShownFit(j, ShownFitVerdict.Few, null, 0));
                continue;
            }

            if (asked >= _settings.MaxAsks)
            {
                fits.Add(new ShownFit(j, ShownFitVerdict.Capped, null, 0));
                continue;
            }

            asked++;
            cancel.ThrowIfCancellationRequested();
            int spent = 0;
            if (!_searched.TryGetValue((j.Key, j.Exemplar), out Searched found))
            {
                found = Search(j);
                _searched[(j.Key, j.Exemplar)] = found;
                spent = found.Flights;
                flights += spent;
            }

            if (found.Link is not { } link)
            {
                fits.Add(new ShownFit(j, found.Verdict, null, spent));
                continue;
            }

            LinkKey key = TFLinkExperience.KeyOf(link.Recipe);
            ShownFitVerdict verdict = !seen && !(snapshot.Links.TryGetValue(key, out LinkStat tried) && tried.Successes >= _settings.MinSightings) ? ShownFitVerdict.Few
                : keys.Any(k => Near(k, key)) ? ShownFitVerdict.Twin
                : seen ? ShownFitVerdict.Fitted : ShownFitVerdict.Kept;
            if (verdict is ShownFitVerdict.Fitted or ShownFitVerdict.Kept)
            {
                links.Add(link);
                keys.Add(key);
            }

            fits.Add(new ShownFit(j, verdict, verdict is ShownFitVerdict.Fitted or ShownFitVerdict.Kept ? link : null, spent));
        }

        return new ShownFitResult(fits, links, flights, asked);
    }

    /// <summary>Whether a bot has landed a link like this jump: a Soldier's <c>Solver</c> row near its ends with the floor's successes.</summary>
    private bool BotsLanded(MapExperience snapshot, LinkKey jump)
    {
        foreach ((LinkKey k, LinkStat s) in snapshot.Links)
        {
            if (k.Source == LinkSource.Solver && k.Class == jump.Class && s.Successes >= _settings.MinSightings && Near(k, jump))
                return true;
        }

        return false;
    }

    /// <summary>Two rows of one jump by the watch's rule: landings within a bin across, takeoffs within a run-up's bins (<see cref="ShownJumpJudge"/>).</summary>
    private static bool Near(in LinkKey a, in LinkKey b)
        => Within(a.To, b.To, ShownJumpJudge.LandingSlackBins) && Within(a.From, b.From, ShownJumpJudge.TakeoffSlackBins);

    private static bool Within(WorldBin a, WorldBin b, int across) => Math.Abs(a.X - b.X) <= across && Math.Abs(a.Y - b.Y) <= across && Math.Abs(a.Z - b.Z) <= 1;

    /// <summary>
    /// The ask the search is given for <paramref name="j"/> (the practice tool asks it again with richer settings, lane G8),
    /// or null when its ends have no cell or one cell: the exemplar's feet, or with none the bins' middles, each on the floor
    /// of the cell under it; families by the rise; the exemplar's hint.
    /// </summary>
    public JumpAsk? AskOf(in ExperienceShownJump j) => Ask(j, out _, out _, out _);

    private JumpAsk? Ask(in ExperienceShownJump j, out CellId from, out CellId to, out ShownFitVerdict refused)
    {
        ShownJumpRecipe? e = j.Exemplar;
        (AiVector up, AiVector down, float reach) = e is not null ? (e.Takeoff, e.Landing, ShownJumpJudge.FloorReach) : (j.Key.From.Centre, j.Key.To.Centre, WorldBin.Height * 0.5f);
        from = CellUnder(up, reach);
        to = CellUnder(down, reach);
        refused = !from.IsValid || !to.IsValid ? ShownFitVerdict.NoCell : ShownFitVerdict.Same;
        if (!from.IsValid || !to.IsValid || from == to)
            return null;
        var takeoff = new Vector(up.X, up.Y, _graph.Cell(from).FloorZ + VoxelMoveWorld.DistEpsilon);
        var landing = new Vector(down.X, down.Y, _graph.Cell(to).FloorZ + VoxelMoveWorld.DistEpsilon);

        // Past one rocket's rise a second rocket is asked too: at a floor under the way, else at a wall beside it (lane G8; the
        // search tries them in that order, as the shape pass's do).
        _envelope ??= _reach();
        JumpFamilies families = landing.Z - takeoff.Z > _settings.ReachShare * _envelope.Value.Rise ? JumpFamilies.Both | JumpFamilies.Double | JumpFamilies.Wall : JumpFamilies.Both;
        return new JumpAsk(takeoff, landing, _settings.Tolerance, families, Hint: e is null ? null : HintOf(e, takeoff, landing));
    }

    /// <summary>The search for one jump: its ask (places, families, hint), and what came of it.</summary>
    private Searched Search(in ExperienceShownJump j)
    {
        if (Ask(j, out CellId from, out _, out ShownFitVerdict refused) is not { } ask)
            return new Searched(refused, null, 0);
        JumpSearchResult result = _search.Find(ask);
        if (result.Found is not { } found)
            return new Searched(result.Flights == 0 ? ShownFitVerdict.NoStand : ShownFitVerdict.NoRecipe, null, result.Flights);
        if (found.Robustness < _search.Settings.Keep)
            return new Searched(ShownFitVerdict.NoRecipe, null, result.Flights);

        // Where it comes to rest is the cell the link goes to, at the height it rests at (the shape pass's rule).
        JumpOutcome flown = _search.Fly(found.Recipe);
        CellId rest = _graph.FindCell(new AiVector(flown.Rest.X, flown.Rest.Y, flown.Rest.Z));
        if (!rest.IsValid || MathF.Abs(_graph.Cell(rest).FloorZ - flown.Rest.Z) > _search.Settings.HeightTolerance)
            return new Searched(ShownFitVerdict.Rests, null, result.Flights + 1);
        var link = new JumpLink(from.Index, rest.Index, found.Recipe, found.Family, found.Robustness, found.Seconds, found.AirSeconds, found.Health, found.WalkSeconds)
        {
            Shown = true,
        };
        return new Searched(ShownFitVerdict.Fitted, link, result.Flights + 1);
    }

    /// <summary>The cell under the feet at <paramref name="at"/>: the nearest floor below a point <paramref name="reach"/> over them, within that of them.</summary>
    private CellId CellUnder(AiVector at, float reach)
    {
        CellId cell = _graph.FindCell(at + new AiVector(0f, 0f, reach));
        return cell.IsValid && MathF.Abs(at.Z - _graph.Cell(cell).FloorZ) <= reach ? cell : CellId.None;
    }

    /// <summary>
    /// Where the search looks first for <paramref name="e"/>: ran at it (faster than <see cref="ShownJumpFitSettings.RanSpeed"/>
    /// over the ground at the takeoff) the Forward family from its grid's run-up, else Up from its own; the shot's pitch, its
    /// yaw against the way from <paramref name="takeoff"/> to <paramref name="landing"/>, and the commands from the jump to it.
    /// No shot seen, no hint.
    /// </summary>
    public JumpHint? HintOf(ShownJumpRecipe e, Vector takeoff, Vector landing)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ShotCommand is not { } shot)
            return null;
        float speed = MathF.Sqrt(e.GroundVelocity.X * e.GroundVelocity.X + e.GroundVelocity.Y * e.GroundVelocity.Y);
        bool ran = speed > _settings.RanSpeed;
        float way = MathF.Atan2(landing.Y - takeoff.Y, landing.X - takeoff.X) * (180f / MathF.PI);
        JumpSearchSettings s = _search.Settings;
        return new JumpHint(ran ? JumpFamily.Forward : JumpFamily.Up, ran ? s.Forward.RunUp : s.Up.RunUp, Math.Clamp(shot.Pitch, -JumpSearch.MaxPitch, JumpSearch.MaxPitch),
                            JumpPilot.WrapDegrees(shot.Yaw - way), e.ShotDelay ?? 0);
    }
}
