using System.Globalization;
using System.Text;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy.Model;
using Xunit;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// A tool, not a fact: RU14's full measurement at large seed counts, written to the file
/// named by DESCENT_MODEL_REPORT and run by hand
/// (<c>DESCENT_MODEL_REPORT=/path make descent-test FILTER=FullyQualifiedName~EconomyModelReport</c>).
/// With the variable unset it does nothing; the gates' facts are in EconomyModelGateTests.
/// </summary>
public sealed class EconomyModelReport
{
    private static string F(double v) => v.ToString(v >= 100 ? "F0" : "F2", CultureInfo.InvariantCulture);

    [Fact]
    public void Tool_writes_the_economy_model_report_when_asked()
    {
        var path = Environment.GetEnvironmentVariable("DESCENT_MODEL_REPORT");
        if (string.IsNullOrEmpty(path)) return;
        int seeds = int.TryParse(Environment.GetEnvironmentVariable("DESCENT_MODEL_SEEDS"), out var n) ? n : 8;
        var plant = Environment.GetEnvironmentVariable("DESCENT_MODEL_PLANT") ?? "";
        var model = new EconomyModel(PlantedContent.With(plant.Split(';', StringSplitOptions.RemoveEmptyEntries)));
        var gates = new ModelGates(model);
        var o = new StringBuilder();
        o.AppendLine($"planted: {(plant.Length == 0 ? "nothing" : plant)}");

        o.AppendLine("# gate 1: threat per XP at monster level 1");
        foreach (var (r, rel) in gates.ThreatPerXpRelative())
            o.AppendLine($"{r.Template,-14} {r.Size,-8} scale {r.Scale:F3} threat {F(r.Threat)} xp {F(r.Xp)} ratio {F(r.Ratio)} relative {rel:F3}");
        o.AppendLine($"worst {gates.ThreatPerXpWorst():F3} (band +/-{model.Rules.Model.ThreatPerXpBandPct} %) in band {gates.ThreatPerXpInBand()}");

        string[] builds = ["artillery", "gunman", "trolldier", "sergeant"];
        o.AppendLine();
        o.AppendLine("# calibration and the level-matched curve: a hero at the depth's monster level, median gear of the depth, fresh packs");
        foreach (int depth in Enumerable.Range(1, 15))
            foreach (var b in builds)
            {
                var hero = gates.LevelMatched(b, depth, 7);
                double life = hero.Life;
                var c = gates.Clear([hero], depth, seeds * 4, 99);
                var boss = gates.BossFight([hero], depth);
                o.AppendLine($"depth {depth,2} {b,-10} lvl {hero.Sheet.Level,2} life {F(life)} power {F(model.Power(hero, depth))}"
                             + $" clear {F(c.MeanSeconds)} s taken {F(c.MeanTakenShare * 100)} % deaths {c.Deaths} all {c.AllCleared}"
                             + (boss is null ? "" : $" | boss {F(boss.Seconds)} s deaths {boss.Deaths} cleared {boss.Cleared}"));
            }
        var stockHero = model.NewHero("gunman");
        var calibration = gates.Clear([stockHero], 1, seeds * 20, 5);
        o.AppendLine($"calibration: level 1 stock Soldier at depth 1, {seeds * 20} packs: {F(calibration.MeanSeconds)} s, taken {F(calibration.MeanTakenShare * 100)} % of life, deaths {calibration.Deaths}");
        o.AppendLine();
        o.AppendLine($"# gate 3: level 30, median gear, depth 15, {seeds * 4} packs");
        var times = new Dictionary<string, double>();
        foreach (var b in builds)
        {
            var hero = gates.Geared(b, 30, 15, 7);
            var c = gates.Clear([hero], 15, seeds * 4, 99);
            var stock = gates.Geared(b, 30, 15, 7);
            gates.StockPrimary(stock);
            var s = gates.Clear([stock], 15, seeds * 4, 99);
            times[b] = c.MeanSeconds;
            foreach (var line in model.Describe(hero)) o.AppendLine("    " + line);
            foreach (var line in model.Describe(stock).Where(l => l.StartsWith("weapon primary", StringComparison.Ordinal))) o.AppendLine("    stock " + line);
            o.AppendLine($"{b,-10} power {F(model.Power(hero, 15))} clear {F(c.MeanSeconds)} s deaths {c.Deaths} all {c.AllCleared}"
                         + $" | stock primary {F(s.MeanSeconds)} s (+{F((s.MeanSeconds / c.MeanSeconds - 1) * 100)} %) deaths {s.Deaths}");
        }
        var three = new[] { times["artillery"], times["gunman"], times["trolldier"] };
        o.AppendLine($"first three max/min {three.Max() / three.Min():F3}");
        var sergeant = gates.Clear([gates.Geared("sergeant", 30, 15, 7), gates.Geared("artillery", 30, 15, 8), gates.Geared("artillery", 30, 15, 9), gates.Geared("artillery", 30, 15, 10)], 15, seeds * 4, 99);
        var four = gates.Clear([gates.Geared("artillery", 30, 15, 7), gates.Geared("artillery", 30, 15, 8), gates.Geared("artillery", 30, 15, 9), gates.Geared("artillery", 30, 15, 10)], 15, seeds * 4, 99);
        o.AppendLine($"party S+3A {F(sergeant.MeanSeconds)} s deaths {sergeant.Deaths} | 4A {F(four.MeanSeconds)} s deaths {four.Deaths}");

        o.AppendLine();
        o.AppendLine($"# gates 2, 4, 5: runs 1..15, {seeds} seeds each");
        var runs = new List<RunReport>();
        foreach (var party in builds.Select(b => new[] { b }).Append(builds))
            for (int seed = 1; seed <= seeds; seed++)
            {
                var run = model.Run(party, (ulong)seed);
                runs.Add(run);
                o.AppendLine($"## {string.Join("+", party)} seed {seed}: cleared {run.Cleared}");
                foreach (var d in run.Depths)
                    o.AppendLine($"depth {d.Depth,2} {(d.Cleared ? "ok  " : "FAIL")} t {F(d.Seconds / 3600)} h lvl {string.Join("/", d.Levels)}"
                                 + $" power {string.Join("/", d.Power.Select(F))} deaths {d.Deaths} au {string.Join("/", d.Australium)}"
                                 + $" kills {d.Kills} packs {d.Packs} potions {d.PotionsDrunk} earned {d.AustraliumEarned} value {d.ItemValueFound} unusual {d.UnusualsFound}"
                                 + (d.Boss is not { } b ? "" : $" | boss at lvl {string.Join("/", d.BossLevels ?? [])}: {F(b.Seconds)} s cleared {b.Cleared} deaths {b.Deaths}"
                                     + $" hit {F(b.LargestHitShare * 100)} % special {F(b.LargestSpecialShare * 100)} % taken {F(b.TakenShare * 100)} % roof {b.RoofFell}"));
            }
        var solo = runs.Where(r => r.Builds.Count == 1).ToList();
        var costs = gates.UnusualCosts(solo, 1);
        o.AppendLine($"unusual: gambling at level 1 {F(costs.Gambling)}, at 30 {F(gates.UnusualCosts(solo, 30).Gambling)}; playing {F(costs.Playing)} ({costs.Unusuals} found)");
        File.WriteAllText(path, o.ToString());
    }
}
