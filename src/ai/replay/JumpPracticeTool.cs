using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Store;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.Replay;

/// <summary>Why the practice tool searched a jump.</summary>
public enum PracticeWhy : byte
{
    /// <summary>A jump a player showed that the game's fit judged <see cref="ShownFitVerdict.NoRecipe"/>.</summary>
    ShownNoRecipe,

    /// <summary>A jump a player showed whose found recipe rests on no cell (<see cref="ShownFitVerdict.Rests"/>).</summary>
    ShownRests,

    /// <summary>A shape link the bots gave up (its <c>Solver</c> row <see cref="LinkTrust.Shut"/>).</summary>
    LinkShut,

    /// <summary>A shape link under the tool's robustness threshold.</summary>
    LinkWeak,

    /// <summary>A candidate the shape pass searched and kept no link for.</summary>
    Candidate,
}

/// <summary>One jump the tool searched: why, the ask, what the game has there (its link's robustness and cost, or none), what the richer search found and spent, the game's verdict of the row it would write, and whether it was written.</summary>
public sealed record PracticeItem(PracticeWhy Why, JumpAsk Ask, float? GameRobustness, float? GameCost, JumpFound? Found, int Flights, PracticeVerdict? Verdict, bool Written);

/// <summary>A practice run: the key it read and wrote, the snapshot's practice rows before, the shape pass's links, the work and what came of it.</summary>
public sealed record PracticeReport(ExperienceKey Key, int RowsBefore, int ShapeLinks, int ShapeCandidates, IReadOnlyList<PracticeItem> Items, int Written, double CpuMs);

/// <summary>
/// <c>tools/ai-jump-practice &lt;map&gt;</c> (plan_bots_teams_tf2_part_g G.6, lane G8): the bots' practice, offline, no game.
/// It opens the experience store for the map's key and loads a snapshot; derives the graph, the free space and the
/// simulators as <c>ai-replay jumps</c> does (<see cref="JumpPlacesTool"/>) and runs the shape pass with the game's
/// settings; picks its work (<see cref="PracticeWhy"/>: the shown jumps the game's fit cannot answer, the shape links the
/// bots gave up or that are weak, the candidates the pass kept nothing for); searches each with richer settings
/// (<see cref="Rich"/>); and writes each recipe the game's own reader would take (<see cref="JumpPractice"/>, run over the
/// snapshot with the new rows) as a <c>link</c> row of <see cref="LinkSource.Practice"/>, in one merge. The store is
/// SQLite in WAL with a busy timeout, so a game that has the file open is not disturbed (<c>SqliteExperienceStoreTests</c>).
/// </summary>
public static class JumpPracticeTool
{
    /// <summary>
    /// The practice's search: finer grids (a pitch every 4 degrees, not 10, and every command of delay), twice the trials (a
    /// robustness to one in forty), more hits measured and room for the flights all that takes. What each is against the
    /// game's is in .scratch/g8-design.md section 7 and the hand-back.
    /// </summary>
    public static JumpSearchSettings Rich => new()
    {
        // 12 pitches by 5 delays (60 of MaxGrid's 64): the game's 5 by 3.
        Up = new JumpGrid(0f, 41f, 85f, 4f, 0, 4, 1),
        // 9 by 7 (63): the game's 4 by 3.
        Forward = new JumpGrid(160f, 28f, 60f, 4f, -3, 3, 1),
        // A floor double's first shot every 5 degrees and command: 8 by 5, the game's 4 by 3.
        Double = new JumpGrid(0f, 50f, 85f, 5f, 0, 4, 1),
        // A wall jump's first shot every 5 degrees and command: 5 by 3, the game's 2 by 2.
        WallFirst = new JumpGrid(0f, 65f, 85f, 5f, 0, 2, 1),
        Trials = 40,
        Measured = 10,
        MaxFlights = 2400,
        DoubleMaxFlights = 2400,
        WallMaxFlights = 3200,
    };

    /// <summary>The shape links under this robustness are searched again: the brief's named threshold (the game keeps from 0.6).</summary>
    public const float Below = 0.8f;

    /// <summary>How far from the landing a body may rest and have made it: the shape pass's (<see cref="JumpPlacesSettings.Tolerance"/>).</summary>
    public const float Tolerance = 24f;

    /// <summary>The level the tool practises on: the graph, the free space, a simulator maker, the pilot's limits, the spawns and a skipped place.</summary>
    public sealed record Level(NavGraph Graph, VoxelColumns Columns, Func<VoxelColumns, BlastJumpSim> Sim, JumpPilotLimits Limits, IReadOnlyList<int> Seeds, JumpSkip? Skip);

    /// <summary>
    /// The practice over <paramref name="snapshot"/> on <paramref name="level"/>: the work, each searched with
    /// <paramref name="rich"/>, and the rows the game's reader would take (<paramref name="rows"/>, to write).
    /// </summary>
    public static IReadOnlyList<PracticeItem> Practise(Level level, MapExperience snapshot, JumpSearchSettings rich, float below, out ExperienceDelta rows,
                                                       out int shapeLinks, out int shapeCandidates, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rich);
        var game = new JumpSearchSettings();
        var costs = new NavLinkCosts { RunSpeed = level.Limits.RunSpeed };
        NavGraph graph = level.Graph;
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, level.Limits.RunSpeed);
        JumpEnvelope reach = JumpEnvelope.Measure(new JumpFlier(level.Sim(JumpEnvelope.OpenAir()), level.Limits), game);

        // The shape pass, as the game runs it: its links and what it proposed.
        var pass = new JumpPlaces(graph, level.Columns, new JumpSearch(new JumpFlier(level.Sim(level.Columns), level.Limits), walk, game), walk, reach,
                                  JumpPlacesSettings.Of(TFNavProfile.ModNumbers), level.Seeds, level.Skip);
        JumpPlacesResult shape = pass.Find(cancel);
        JumpLinkSet set = JumpLinkSet.Of(graph, shape.Links, costs);
        ExperienceLinkKeys keys = TFLinkExperience.KeysOf(set);
        shapeLinks = shape.Links.Count;
        shapeCandidates = shape.Candidates.Count;

        // The work, in a fixed order: the shown jumps the game's fit cannot answer, the shape links gone bad or weak, the
        // candidates the pass kept nothing for.
        var work = new List<(PracticeWhy Why, JumpAsk Ask, float? Robustness, float? Cost)>();
        var fit = new ShownJumpFit(graph, new JumpSearch(new JumpFlier(level.Sim(level.Columns), level.Limits), walk, game), () => reach, new ShownJumpFitSettings());
        ShownFitResult fits = fit.Fit(snapshot, keys, cancel);
        foreach (ShownFit f in fits.Fits)
        {
            if (f.Verdict is ShownFitVerdict.NoRecipe or ShownFitVerdict.Rests && fit.AskOf(f.Jump) is { } ask)
                work.Add((f.Verdict == ShownFitVerdict.NoRecipe ? PracticeWhy.ShownNoRecipe : PracticeWhy.ShownRests, ask, null, null));
        }

        for (int i = 0; i < set.Links.Count; i++)
        {
            JumpLink j = set.JumpOf(i);
            bool shut = snapshot.Links.TryGetValue(keys[i], out LinkStat tried) && LinkTrust.Shut(tried);
            if (!shut && j.Robustness >= below)
                continue;
            JumpFamilies families = j.Recipe.Landing.Z - j.Recipe.Takeoff.Z > reach.Rise * JumpPlacesSettings.Of(TFNavProfile.ModNumbers).ReachShare
                ? JumpFamilies.Both | JumpFamilies.Double | JumpFamilies.Wall : JumpFamilies.Both;
            work.Add((shut ? PracticeWhy.LinkShut : PracticeWhy.LinkWeak, new JumpAsk(j.Recipe.Takeoff, j.Recipe.Landing, Tolerance, families), j.Robustness,
                      JumpSearch.Cost(j.Seconds, j.Robustness)));
        }

        int soldier = (int)TFClass.Soldier;
        foreach (JumpCandidate c in shape.Candidates)
        {
            WorldBin from = WorldBin.Of(new AiVector(c.Takeoff.X, c.Takeoff.Y, c.Takeoff.Z)), to = WorldBin.Of(new AiVector(c.Landing.X, c.Landing.Y, c.Landing.Z));
            if (!ShownJumpJudge.Knows(keys, soldier, from, to))
                work.Add((PracticeWhy.Candidate, new JumpAsk(c.Takeoff, c.Landing, Tolerance, c.Families), null, null));
        }

        // Each searched with the richer settings.
        var search = new JumpSearch(new JumpFlier(level.Sim(level.Columns), level.Limits), walk, rich);
        var found = new List<(JumpFound? Found, int Flights)>(work.Count);
        var trial = new ExperienceDelta();
        foreach (LinkKey k in snapshot.Links.Keys)
            trial.Add(k, snapshot.Links[k]);
        foreach ((_, JumpAsk ask, _, _) in work)
        {
            cancel.ThrowIfCancellationRequested();
            JumpSearchResult r = search.Find(ask);
            found.Add((r.Found, r.Flights));
            if (r.Found is { } f && f.Robustness >= rich.Keep)
                trial.Add(TFLinkExperience.PracticeKeyOf(f.Recipe), StatOf(f, rich.Trials));
        }

        // The game's own reader over the snapshot with the new rows: a row is written only when it would take it.
        var verify = new JumpPracticeSettings();
        var reader = new JumpPractice(graph, new JumpSearch(new JumpFlier(level.Sim(level.Columns), level.Limits), walk, verify.Verifying(game)), walk, verify);
        PracticeResult judged = reader.Check(new MapExperience(snapshot.Key, snapshot.Rounds, trial), set, cancel);
        rows = new ExperienceDelta();
        var items = new List<PracticeItem>(work.Count);
        for (int i = 0; i < work.Count; i++)
        {
            (PracticeWhy why, JumpAsk ask, float? robustness, float? cost) = work[i];
            (JumpFound? f, int flights) = found[i];
            PracticeVerdict? verdict = null;
            bool written = false;
            if (f is { } got && got.Robustness >= rich.Keep)
            {
                LinkKey key = TFLinkExperience.PracticeKeyOf(got.Recipe);
                verdict = judged.Checks.FirstOrDefault(c => c.Row == key)?.Verdict;
                written = verdict == PracticeVerdict.Taken && !snapshot.Links.ContainsKey(key);
                if (written)
                    rows.Add(key, StatOf(got, rich.Trials));
            }

            items.Add(new PracticeItem(why, ask, robustness, cost, f, flights, verdict, written));
        }

        return items;
    }

    /// <summary>What a practice row holds: the trials flown, those that made it, and the recipe's seconds and health a success, as sums.</summary>
    public static LinkStat StatOf(in JumpFound found, int trials)
    {
        double made = Math.Round(found.Robustness * trials);
        return new LinkStat(trials, made, found.Seconds * made, found.Health * made);
    }

    /// <summary>
    /// <c>practice &lt;map.nav3d&gt; &lt;map.bsp&gt; &lt;soldier.txt&gt; &lt;db|default&gt; &lt;out.json&gt; &lt;build&gt; &lt;commit&gt; [mode|-] [below] [skip|-] [write|dry] [half-life]</c>:
    /// the practice on the map's key of the store, its report on the output and in the json, its rows merged unless dry.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length < 7)
        {
            error.WriteLine("usage: ai-replay practice <map.nav3d> <map.bsp> <soldier.txt> <db|default> <out.json> <build> <commit> [mode|-] [below] [skip|-] [write|dry] [half-life]");
            return 2;
        }

        if (ThreadCpuClock.Micros() is not { } clock)
        {
            error.WriteLine("ai-replay: no thread CPU clock on this system");
            return 1;
        }

        string map = Path.GetFileNameWithoutExtension(args[0]);
        string? db = args[3] == ExperienceDbPath.Default
            ? ExperienceDbPath.Resolve(ExperienceDbPath.Default, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), toolGame: false)
            : args[3];
        string? mode = args.Length > 7 && args[7] != "-" ? args[7] : null;
        float below = args.Length > 8 ? float.Parse(args[8], CultureInfo.InvariantCulture) : Below;
        JumpSkip? skip = args.Length > 9 && args[9] != "-" ? JumpSkip.Parse(args[9].Replace(',', ' ')) : null;
        bool write = !(args.Length > 10 && args[10] == "dry");
        double halfLife = args.Length > 11 ? double.Parse(args[11], CultureInfo.InvariantCulture) : ExperienceDecay.DefaultHalfLife;

        // A tool never makes a store: a missing file is a refusal, not an empty one.
        if (db is null || !File.Exists(db))
        {
            error.WriteLine($"ai-jump-practice: no experience store at {db ?? "(no user data directory)"}: a game writes it (ai_experience_db); the tool never makes one");
            return 1;
        }

        try
        {
            byte[] nav = File.ReadAllBytes(args[0]), bsp = File.ReadAllBytes(args[1]);
            string checksum = Convert.ToHexStringLower(SHA256.HashData(bsp))[..16];
            using SqliteExperienceStore store = SqliteExperienceStore.Open(db);
            if (store.Refusal is { } refused)
            {
                error.WriteLine($"ai-jump-practice: {refused}");
                return 1;
            }

            ExperienceKey[] keys = [.. store.Keys().Where(k => string.Equals(k.Map, map, StringComparison.OrdinalIgnoreCase) && k.Checksum == checksum && (mode is null || k.Mode == mode))];
            if (keys.Length != 1)
            {
                error.WriteLine($"ai-jump-practice: {db} holds {keys.Length} keys of {map} at checksum {checksum}{(mode is null ? "" : $" in mode {mode}")}"
                                + $"{(keys.Length > 1 ? $" ({string.Join(", ", keys.Select(k => k.Mode))}): name one with --mode" : ": play the map with the store on first")}");
                return 1;
            }

            ExperienceKey key = keys[0];
            MapExperience snapshot = store.Load(key, halfLife);
            double t0 = clock();
            var source = new Nav3dNavSource(Nav3dReader.Open(nav));
            MapEntities entities = MapEntities.Read(bsp);
            NavGraph graph = NavGraphFromNav3d.Derive(source, TFNavProfile.ModNumbers, NavEdgeCosts.Default, entities.Hurt, entities.LadderClimbs).Graph;
            IReadOnlyList<int> seeds = JumpPlacesTool.Seeds(JumpRoutesTool.ReadEntities(bsp), graph);
            VoxelColumns columns = VoxelColumns.FromNav(source);
            TFClassTable classes = OfflineTF2.Classes(args[2]);
            BlastJumpSims sims = OfflineTF2.Sims(classes);
            JumpLauncher launcher = JumpLauncher.Rocket(OfflineTF2.RocketDamage, OfflineTF2.RocketRefire);
            JumpPilotLimits limits = JumpPilotLimits.Of(TFViewVectors.Params, classes[TFClass.Soldier].MaxSpeed, BlastJumpSims.TickSeconds, TFJumps.PlannerTurn);
            var level = new Level(graph, columns, c => sims.Create(c, launcher), limits, seeds, skip);
            int before = snapshot.Links.Keys.Count(k => k.Source == LinkSource.Practice);
            IReadOnlyList<PracticeItem> items = Practise(level, snapshot, Rich, below, out ExperienceDelta rows, out int shapeLinks, out int shapeCandidates);
            int written = rows.Links.Count;
            if (write && written > 0 && !store.Merge(key, rows, halfLife))
            {
                error.WriteLine($"ai-jump-practice: the merge into {db} failed ({store.Refusal ?? $"busy: {store.BusyFailures} busy failure(s)"}); nothing written");
                return 1;
            }

            var report = new PracticeReport(key, before, shapeLinks, shapeCandidates, items, write ? written : 0, (clock() - t0) / 1e3);
            output.Write(Text(report, db, write, args[5], args[6]));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[4]))!);
            File.WriteAllText(args[4], Json(report, db, write, args[5], args[6]));
            output.WriteLine($"  written     {args[4]}");
            return 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or FormatException)
        {
            error.WriteLine($"ai-jump-practice: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }

    private static string V(Vector v) => string.Create(CultureInfo.InvariantCulture, $"[{v.X:F0}, {v.Y:F0}, {v.Z:F0}]");

    private static string F(float? v, string format = "F2") => v is { } x ? x.ToString(format, CultureInfo.InvariantCulture) : "null";

    /// <summary>The report's text: the key, the shape pass, each piece of work and what came of it.</summary>
    public static string Text(PracticeReport r, string db, bool write, string build, string commit)
    {
        ArgumentNullException.ThrowIfNull(r);
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"ai-jump-practice {r.Key.Map} ({build}, {commit}): {db}, key checksum {r.Key.Checksum} mode {r.Key.Mode}\n");
        s.Append(CultureInfo.InvariantCulture, $"  store       {r.RowsBefore} practice row(s) before\n");
        s.Append(CultureInfo.InvariantCulture, $"  shape pass  {r.ShapeCandidates} candidates, {r.ShapeLinks} links (the game's settings)\n");
        s.Append(CultureInfo.InvariantCulture, $"  work        {r.Items.Count}: {string.Join(", ", r.Items.GroupBy(i => i.Why).Select(g => $"{g.Count()} {g.Key}"))}\n");
        foreach (PracticeItem i in r.Items)
        {
            string found = i.Found is { } f
                ? string.Create(CultureInfo.InvariantCulture, $"{f.Family} robustness {f.Robustness:F3} seconds {f.Seconds:F3} cost {JumpSearch.Cost(f.Seconds, f.Robustness):F3} health {f.Health:F1}")
                : "nothing";
            s.Append(CultureInfo.InvariantCulture,
                $"    {i.Why,-13} {V(i.Ask.Takeoff)} -> {V(i.Ask.Landing)} ({i.Ask.Families}): the game's robustness {F(i.GameRobustness)} cost {F(i.GameCost, "F3")}; found {found} in {i.Flights} flights; the game's reader: {(i.Verdict?.ToString() ?? "-")}{(i.Written ? write ? "; WRITTEN" : "; would be written" : "")}\n");
        }

        s.Append(CultureInfo.InvariantCulture, $"  rows        {r.Written} written{(write ? "" : " (dry run: none)")}; {r.CpuMs / 1e3:F1} s of thread CPU\n");
        return s.ToString();
    }

    /// <summary>The report as json, for the proofs.</summary>
    public static string Json(PracticeReport r, string db, bool write, string build, string commit)
    {
        ArgumentNullException.ThrowIfNull(r);
        var json = new StringBuilder("{\n");
        json.Append(CultureInfo.InvariantCulture,
            $" \"map\": \"{r.Key.Map}\",\n \"checksum\": \"{r.Key.Checksum}\",\n \"mode\": \"{r.Key.Mode}\",\n \"db\": \"{db}\",\n \"build\": \"{build}\",\n \"commit\": \"{commit}\",\n");
        json.Append(CultureInfo.InvariantCulture,
            $" \"write\": {(write ? "true" : "false")},\n \"rows_before\": {r.RowsBefore},\n \"shape_links\": {r.ShapeLinks},\n \"shape_candidates\": {r.ShapeCandidates},\n \"written\": {r.Written},\n \"cpu_ms\": {r.CpuMs:F0},\n \"work\": [\n");
        json.AppendJoin(",\n", r.Items.Select(i => string.Create(CultureInfo.InvariantCulture,
            $"  {{\"why\": \"{i.Why}\", \"takeoff\": {V(i.Ask.Takeoff)}, \"landing\": {V(i.Ask.Landing)}, \"families\": \"{i.Ask.Families}\", \"game_robustness\": {F(i.GameRobustness)}, \"game_cost\": {F(i.GameCost, "F3")}, "
            + $"\"found\": {(i.Found is { } f ? $"{{\"family\": \"{f.Family}\", \"robustness\": {f.Robustness:F3}, \"seconds\": {f.Seconds:F3}, \"health\": {f.Health:F1}, \"pitch\": {f.Recipe.Pitch:F1}, \"shot_delay\": {f.Recipe.ShotDelay}, \"run_up\": {f.Recipe.RunUp:F0}, \"start\": {V(f.Recipe.Start().Origin)}}}" : "null")}, "
            + $"\"flights\": {i.Flights}, \"verdict\": {(i.Verdict is { } v ? $"\"{v}\"" : "null")}, \"written\": {(i.Written ? "true" : "false")}}}")));
        json.Append("\n ]\n}\n");
        return json.ToString();
    }
}
