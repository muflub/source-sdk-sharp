using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's effect dispatch-by-name table against stock's, recorded:
/// <c>Fixtures/effect-dispatch-stock.tsv</c> (README beside it) holds the
/// <c>CClientEffectRegistration</c> list in stock's walk order and 397 names
/// with the registration stock's <c>DispatchEffectToCallback</c> picked for
/// each. The managed table, filled from the same list, must pick the same one
/// every time.
/// </summary>
public class ClientEffectDispatchTableTests
{
    private sealed record Stock(List<string> Registrations, List<(int Hit, string Name)> Lookups, int LiveRuns);

    private static Stock Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "effect-dispatch-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock dispatch table is missing: " + path);

        var regs = new List<string>();
        var lookups = new List<(int, string)>();
        int live = 0;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "R":
                    Assert.True(int.Parse(c[1]) == regs.Count, $"registration rows out of walk order at '{line}'");
                    regs.Add(c[2]);
                    break;
                case "L":
                    lookups.Add((int.Parse(c[1]), c[2]));
                    break;
                case "X":
                    live++;
                    break;
                default:
                    Assert.Fail($"not a fixture row: '{line}'");
                    break;
            }
        }

        return new Stock(regs, lookups, live);
    }

    private static EffectDispatchTable TableOf(IEnumerable<string> names)
    {
        var table = new EffectDispatchTable();
        foreach (string name in names)
            table.Add(name);
        return table;
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        Stock stock = Recorded();

        Assert.True(stock.Registrations.Count == 26, $"26 registrations recorded, read {stock.Registrations.Count}");
        Assert.True(stock.Lookups.Count == 397, $"397 lookups recorded, read {stock.Lookups.Count}");
        Assert.True(stock.Lookups.Count(l => l.Hit >= 0) == 74, "74 of the lookups resolved in stock");
        Assert.True(stock.LiveRuns > 0, "the probe saw a live dispatch run (the positive arm)");
    }

    [Fact]
    public void EveryRecordedLookupPicksTheRegistrationStockPicked()
    {
        Stock stock = Recorded();
        EffectDispatchTable table = TableOf(stock.Registrations);

        var wrong = stock.Lookups
            .Where(l => table.Lookup(l.Name) != l.Hit)
            .Select(l => $"{l.Name}: stock {l.Hit}, managed {table.Lookup(l.Name)}")
            .ToList();

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {stock.Lookups.Count} lookups disagree: " + string.Join("; ", wrong.Take(10)));
    }

    [Fact]
    public void ADuplicateNameResolvesToTheFirstInWalkOrder()
    {
        EffectDispatchTable table = TableOf(["Smoke", "Tracer", "smoke"]);

        Assert.True(table.Lookup("SMOKE") == 0, "the walk stops at the first match, as stock's does");
    }

    [Fact]
    public void OnlyAsciiLettersFoldCase()
    {
        // Q_stricmp folds A-Z by | 0x20 and nothing else; OrdinalIgnoreCase
        // would also fold these two.
        EffectDispatchTable table = TableOf(["éclat", "Tracer"]);

        Assert.True(table.Lookup("ÉCLAT") == -1, "a non-ASCII letter does not fold");
        Assert.True(table.Lookup("éCLAT") == 0, "the ASCII letters around it still do");
    }
}
