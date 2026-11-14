using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>What the learn pass made of one practice row (lane G8).</summary>
public enum PracticeVerdict : byte
{
    /// <summary>Its recipe, flown in the level's simulator, made its landing robustly and rests on a cell: it joins the level's links.</summary>
    Taken,

    /// <summary>Its bytes are no recipe this build reads (<see cref="TFLinkExperience.TryRecipe"/>).</summary>
    Unread,

    /// <summary>Flown in the level's simulator it makes its landing in fewer than <see cref="JumpSearchSettings.Keep"/> of the trials: never taken blind.</summary>
    Spoiled,

    /// <summary>Its flight comes to rest in no cell at its height, or its takeoff stands on none, or both are one cell.</summary>
    Rests,

    /// <summary>One of the level's own links already goes there from there and costs no more (seconds over robustness), and the bots have not given it up.</summary>
    NotBetter,

    /// <summary>Another practice row, taken before it (the most successes first), already gives a link there.</summary>
    Twin,

    /// <summary>Past the most a pass checks (<see cref="JumpPracticeSettings.MaxRows"/>).</summary>
    Capped,
}

/// <summary>One practice row with its verdict, its link when it gives one, and the flights its check took in this pass (0 when an earlier one did).</summary>
public sealed record PracticeCheck(LinkKey Row, LinkStat Stat, PracticeVerdict Verdict, JumpLink? Link, int Flights);

/// <summary>What a practice pass answered: every Soldier practice row of the snapshot with its verdict, the links in the order they join, and the flights it flew.</summary>
public sealed record PracticeResult(IReadOnlyList<PracticeCheck> Checks, IReadOnlyList<JumpLink> Links, int Flights)
{
    /// <summary>No practice rows.</summary>
    public static PracticeResult None { get; } = new([], [], 0);
}

/// <summary>The practice pass's numbers (lane G8).</summary>
public sealed record JumpPracticeSettings
{
    /// <summary>The most practice rows a pass checks, the most successful first: what a store holding many costs a level (each check is the game's own trials, 21 flights).</summary>
    public int MaxRows { get; init; } = 12;

    /// <summary>How far from its landing a practice recipe may come to rest: the shape pass's (<see cref="JumpPlacesSettings.Tolerance"/>).</summary>
    public float Tolerance { get; init; } = 24f;

    /// <summary>
    /// The perturbed trials a practice recipe is verified with: the practice tool's own (<c>JumpPracticeTool.Rich</c>), so
    /// the game's check flies the same draws the tool measured it with (the trials of one seed are one sequence), twice the
    /// shape pass's 20. A practice recipe is a claim the game checks, and twenty trials put a 0.775 recipe at 0.6 on
    /// ctf_2fort (lane G8's dry run). 41 flights a row, once a level.
    /// </summary>
    public int Trials { get; init; } = 40;

    /// <summary>The search settings a practice row is verified with: the game's, with <see cref="Trials"/>.</summary>
    public JumpSearchSettings Verifying(JumpSearchSettings game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game with { Trials = Trials };
    }
}

/// <summary>
/// The game's reader of a practice row (plan_bots_teams_tf2_part_g G.6, lane G8): a recipe the practice tool
/// (<c>tools/ai-jump-practice</c>) found offline with richer settings and wrote to the store as a <c>link</c> row of
/// <see cref="LinkSource.Practice"/>. Each is read back (<see cref="TFLinkExperience.TryRecipe"/>) and flown in the level's
/// own simulator before it is trusted: unperturbed it must make its landing and rest on a cell at its height, and
/// perturbed it must land in <see cref="JumpSearchSettings.Keep"/> of its trials (the game's perturbations and seed, the
/// tool's count of them: <see cref="JumpPracticeSettings.Verifying"/>; the search it is given is made with those). One that passes is a link like the shape pass's, marked <see cref="JumpLink.Practiced"/>, unless one of
/// the level's own links already goes there from there and costs no more (and the bots have not given it up:
/// <see cref="LinkTrust.Shut"/>), or a practice row taken before it already does.
///
/// <para>
/// Run by the learn pass (<see cref="JumpLearning"/>) before the shown jumps' fit, on a worker; never in a decide. Each row
/// is checked once in the reader's life (a level's), keyed by its recipe bytes: a snapshot whose practice rows change
/// (a new recipe written) checks only those, and the level's set is made again from the snapshot. It holds a search, so it
/// serves one thread at a time.
/// </para>
/// </summary>
public sealed class JumpPractice
{
    private readonly NavGraph _graph;
    private readonly JumpSearch _search;
    private readonly IJumpWalk _walk;
    private readonly JumpPracticeSettings _settings;
    private readonly Dictionary<LinkRecipe, Checked> _checked = new();

    public JumpPractice(NavGraph graph, JumpSearch search, IJumpWalk walk, JumpPracticeSettings settings)
    {
        _graph = graph;
        _search = search;
        _walk = walk;
        _settings = settings;
    }

    private readonly record struct Checked(PracticeVerdict Verdict, JumpLink? Link, int Flights);

    /// <summary>The rows checked in its life.</summary>
    public int Remembered => _checked.Count;

    /// <summary>
    /// The pass over <paramref name="snapshot"/>'s practice rows, against <paramref name="level"/>, the level's own links
    /// (null for none). <paramref name="cancel"/> is looked at before each check.
    /// </summary>
    public PracticeResult Check(MapExperience snapshot, JumpLinkSet? level, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int soldier = (int)TFClass.Soldier;
        KeyValuePair<LinkKey, LinkStat>[] rows = [.. snapshot.Links.Where(r => r.Key.Source == LinkSource.Practice && r.Key.Class == soldier && r.Key.Kind == (int)NavLinkKind.Blast)];
        if (rows.Length == 0)
            return PracticeResult.None;

        // The most successful first; among as successful, by place and then bytes, so a pass is the same every time.
        Array.Sort(rows, static (a, b) =>
        {
            int c = b.Value.Successes.CompareTo(a.Value.Successes);
            if (c != 0)
                return c;
            c = a.Key.From.Packed.CompareTo(b.Key.From.Packed);
            if (c != 0)
                return c;
            c = a.Key.To.Packed.CompareTo(b.Key.To.Packed);
            return c != 0 ? c : a.Key.Recipe.Bytes.AsSpan().SequenceCompareTo(b.Key.Recipe.Bytes.AsSpan());
        });
        ExperienceLinkKeys? keys = level is null ? null : TFLinkExperience.KeysOf(level);
        var checks = new List<PracticeCheck>(rows.Length);
        var links = new List<JumpLink>();
        var taken = new List<LinkKey>();
        int flights = 0, asked = 0;
        foreach ((LinkKey row, LinkStat stat) in rows)
        {
            if (asked >= _settings.MaxRows)
            {
                checks.Add(new PracticeCheck(row, stat, PracticeVerdict.Capped, null, 0));
                continue;
            }

            asked++;
            cancel.ThrowIfCancellationRequested();
            int spent = 0;
            if (!_checked.TryGetValue(row.Recipe, out Checked found))
            {
                found = Verify(row.Recipe);
                _checked[row.Recipe] = found;
                spent = found.Flights;
                flights += spent;
            }

            if (found.Link is not { } link)
            {
                checks.Add(new PracticeCheck(row, stat, found.Verdict, null, spent));
                continue;
            }

            LinkKey key = TFLinkExperience.KeyOf(link.Recipe);
            PracticeVerdict verdict = taken.Any(k => Near(k, key)) ? PracticeVerdict.Twin
                : keys is not null && level is not null && !Better(link, level, keys, snapshot, key) ? PracticeVerdict.NotBetter
                : PracticeVerdict.Taken;
            if (verdict == PracticeVerdict.Taken)
            {
                links.Add(link);
                taken.Add(key);
            }

            checks.Add(new PracticeCheck(row, stat, verdict, verdict == PracticeVerdict.Taken ? link : null, spent));
        }

        return new PracticeResult(checks, links, flights);
    }

    /// <summary>
    /// Whether <paramref name="link"/> is worth adding beside the level's own links: none of them goes there from there
    /// (the watch's rule, <see cref="Near"/>), or every one that does costs more (seconds over robustness) or has been given up by
    /// the bots (its row <see cref="LinkTrust.Shut"/>).
    /// </summary>
    private static bool Better(in JumpLink link, JumpLinkSet level, ExperienceLinkKeys keys, MapExperience snapshot, in LinkKey key)
    {
        float cost = JumpSearch.Cost(link.Seconds, link.Robustness);
        for (int i = 0; i < keys.Count; i++)
        {
            if (!Near(keys[i], key))
                continue;
            JumpLink own = level.JumpOf(i);
            bool shut = snapshot.Links.TryGetValue(keys[i], out LinkStat tried) && LinkTrust.Shut(tried);
            if (!shut && JumpSearch.Cost(own.Seconds, own.Robustness) <= cost)
                return false;
        }

        return true;
    }

    /// <summary>Two rows of one jump by the watch's rule (<see cref="ShownJumpJudge"/>): landings within a bin across, takeoffs within a run-up's bins.</summary>
    private static bool Near(in LinkKey a, in LinkKey b)
        => Within(a.To, b.To, ShownJumpJudge.LandingSlackBins) && Within(a.From, b.From, ShownJumpJudge.TakeoffSlackBins);

    private static bool Within(WorldBin a, WorldBin b, int across) => Math.Abs(a.X - b.X) <= across && Math.Abs(a.Y - b.Y) <= across && Math.Abs(a.Z - b.Z) <= 1;

    /// <summary>One row's recipe read back and flown in the level's simulator: the link it gives, or why none.</summary>
    private Checked Verify(LinkRecipe bytes)
    {
        if (!TFLinkExperience.TryRecipe(bytes, out JumpRecipe recipe))
            return new Checked(PracticeVerdict.Unread, null, 0);
        var ask = new JumpAsk(recipe.Takeoff, recipe.Landing, _settings.Tolerance);
        JumpOutcome flown = _search.Fly(recipe);
        int flights = 1;
        if (!_search.Made(ask, flown))
            return new Checked(PracticeVerdict.Spoiled, null, flights);
        float robustness = _search.Robustness(ask, recipe, ref flights);
        if (robustness < _search.Settings.Keep)
            return new Checked(PracticeVerdict.Spoiled, null, flights);

        // From the cell under the takeoff to the one it comes to rest in, at the height it rests at (the shape pass's rule).
        CellId from = _graph.FindCell(new AiVector(recipe.Takeoff.X, recipe.Takeoff.Y, recipe.Takeoff.Z + ShownJumpJudge.FloorReach));
        CellId rest = _graph.FindCell(new AiVector(flown.Rest.X, flown.Rest.Y, flown.Rest.Z));
        if (!from.IsValid || MathF.Abs(_graph.Cell(from).FloorZ - recipe.Takeoff.Z) > ShownJumpJudge.FloorReach || !rest.IsValid
            || MathF.Abs(_graph.Cell(rest).FloorZ - flown.Rest.Z) > _search.Settings.HeightTolerance || from == rest)
        {
            return new Checked(PracticeVerdict.Rests, null, flights);
        }

        var link = new JumpLink(from.Index, rest.Index, recipe, TFLinkExperience.FamilyOf(recipe), robustness, flown.Seconds, flown.AirSeconds, flown.BlastDamage,
                                _walk.Seconds(recipe.Takeoff, recipe.Landing))
        {
            Practiced = true,
        };
        return new Checked(PracticeVerdict.Taken, link, flights);
    }
}
