using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client effect precache system against stock's, recorded:
/// <c>Fixtures/effectprecache-stock.tsv</c> (README beside it) holds six level
/// starts and ends and every call stock made on its registry for each. The
/// managed system, told of the same events over a registry of the same size,
/// must make the same calls in the same order.
/// </summary>
public class ClientEffectPrecacheTests
{
    private sealed class RecordingRegistry(int count) : IFxPrecache
    {
        public readonly List<string> Calls = [];
        public int Count() => count;
        public void Cache(int index, bool precache) => Calls.Add($"pc-cache\t{index}\t{(precache ? 1 : 0)}");
        public void CacheUsedMaterials() => Calls.Add("pc-used");
        public void CacheMaterialHandles() => Calls.Add("pc-handles");
    }

    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "effectprecache-stock.tsv");
        Assert.True(File.Exists(path), "the recorded precache system is missing: " + path);
        return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<string[]> rows = Recorded();

        Assert.True(rows.Count(r => r[0] == "pc-reg") == 17, "17 registrations");
        Assert.True(rows.Count(r => r[0] == "pc-ev" && r[1] == "init") == 6, "six level starts");
        Assert.True(rows.Count(r => r[0] == "pc-cache") == 204, "204 cache calls");
        Assert.True(rows.Count(r => r[0] == "pc-cache" && r[2] == "0") == 102, "half of them releases (the positive arm of the end)");
    }

    [Fact]
    public void EveryLevelStartAndEndMakesStocksCalls()
    {
        List<string[]> rows = Recorded();
        var wrong = new List<string>();
        int events = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "pc-ev")
                continue;

            string[] ev = [rows[i][1], rows[i][2]];
            int n = int.Parse(ev[1]["n=".Length..]);
            var registry = new RecordingRegistry(n);
            var precache = new ClientEffectPrecache(registry);

            switch (ev[0])
            {
                case "init": precache.LevelInit(); break;
                case "shutdownpost": precache.LevelShutdown(); break;
                default: break;   // shutdownpre and shutdown: stock does nothing here (the registry purge is native's)
            }
            events++;

            var want = new List<string>();
            for (int k = i + 1; k < rows.Count && rows[k][0] != "pc-ev" && rows[k][0] != "pc-reg"; k++)
                want.Add(string.Join('\t', rows[k]));

            if (!want.SequenceEqual(registry.Calls))
                wrong.Add($"event {events} ({ev[0]}): managed [{string.Join(", ", registry.Calls.Take(4))}..{registry.Calls.Count}], " +
                          $"stock [{string.Join(", ", want.Take(4))}..{want.Count}]");
        }

        Assert.True(events == 19, $"19 events replayed (6 starts, 6 pre-ends, 6 post-ends, the shutdown), read {events}");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {events} events disagree: " + string.Join(" | ", wrong.Take(4)));
    }
}
