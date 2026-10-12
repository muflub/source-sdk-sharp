using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's decal tables against stock <c>decals.cpp</c>, recorded live
/// (<c>Fixtures/decals-stock.tsv</c>, README beside it): the script stock read,
/// the index the engine gave each entry, every weighted pick with the random
/// draws it took, every index's name, and five names translated over all 256
/// game materials. Bit-exact: the draws are replayed, not re-rolled, and each
/// replayed draw must be asked for with the recorded bounds.
/// </summary>
public class ClientDecalsOracleTests
{
    private sealed record Pick(string Group, int Answer, List<(float Min, float Max, float Value)> Draws);

    private sealed record Oracle(string Script, Dictionary<string, int> Index, List<Pick> Picks,
                                 List<(int Index, string Name)> Names,
                                 List<(string Name, int Material, string Answer)> Translations);

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Oracle Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "decals-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock decal table is missing: " + path);

        var script = new StringBuilder();
        var translation = new StringBuilder();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var picks = new List<Pick>();
        var names = new List<(int, string)>();
        var translations = new List<(string, int, string)>();
        string? open = null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "S":
                    if (open != c[1])
                    {
                        if (open is not null)
                            script.Append("}\n");
                        script.Append('"').Append(c[1]).Append("\"\n{\n");
                        open = c[1];
                    }
                    script.Append("\t\"").Append(c[2]).Append("\"\t\"").Append(c[3]).Append("\"\n");
                    break;
                case "M":
                    translation.Append("\t\"").Append(c[1]).Append("\"\t\"").Append(c[2] == "-" ? "" : c[2]).Append("\"\n");
                    break;
                case "P":
                    index[c[1]] = int.Parse(c[2], CultureInfo.InvariantCulture);
                    break;
                case "I":
                    var draws = new List<(float, float, float)>();
                    if (c[3] != "-")
                    {
                        foreach (string d in c[3].Split(';'))
                        {
                            string[] v = d.Split(',');
                            draws.Add((F(v[0]), F(v[1]), F(v[2])));
                        }
                    }
                    picks.Add(new Pick(c[1], int.Parse(c[2], CultureInfo.InvariantCulture), draws));
                    break;
                case "N":
                    names.Add((int.Parse(c[1], CultureInfo.InvariantCulture), c[2]));
                    break;
                case "T":
                    translations.Add((c[1], int.Parse(c[2], CultureInfo.InvariantCulture), c[3]));
                    break;
                default:
                    Assert.Fail($"an unknown row kind '{c[0]}' in the decal oracle");
                    break;
            }
        }

        if (open is not null)
            script.Append("}\n");
        script.Append("\"TranslationData\"\n{\n").Append(translation).Append("}\n");

        // The positive arm: a fixture that parsed to nothing would pass every
        // comparison below vacuously.
        Assert.True(index.Count >= 100 && picks.Count >= 200 && names.Count >= 200 && translations.Count == 5 * 256,
            $"the oracle parsed ({index.Count} indices, {picks.Count} picks, {names.Count} names, {translations.Count} translations)");
        return new Oracle(script.ToString(), index, picks, names, translations);
    }

    /// <summary>A random stream that replays the recorded draws and refuses any other.</summary>
    private sealed class Replay
    {
        public Queue<(float Min, float Max, float Value)> Pending { get; } = new();
        public int Drawn { get; private set; }

        public float Draw(float min, float max)
        {
            Assert.True(Pending.Count > 0, $"a draw stock did not take: RandomFloat({min}, {max})");
            var (eMin, eMax, value) = Pending.Dequeue();
            Assert.True(BitConverter.SingleToInt32Bits(min) == BitConverter.SingleToInt32Bits(eMin)
                        && BitConverter.SingleToInt32Bits(max) == BitConverter.SingleToInt32Bits(eMax),
                $"the draw's bounds: stock asked RandomFloat({eMin:R}, {eMax:R}), the port ({min:R}, {max:R})");
            Drawn++;
            return value;
        }
    }

    private static (ClientDecals Decals, Replay Random) Loaded(Oracle oracle)
    {
        var random = new Replay();
        var decals = new ClientDecals(new RecordingLog(),
                                      path => path == "scripts/decals_subrect.txt" ? oracle.Script : null,
                                      name => oracle.Index.TryGetValue(name, out int i) ? i : -1,
                                      random.Draw);
        decals.Init();
        decals.LevelInitPreEntity();
        return (decals, random);
    }

    [Fact]
    public void EveryWeightedPickAnswersStocksIndexFromStocksDraws()
    {
        var oracle = Stock();
        var (decals, random) = Loaded(oracle);
        int checkedPicks = 0;

        foreach (var pick in oracle.Picks)
        {
            foreach (var draw in pick.Draws)
                random.Pending.Enqueue(draw);

            int answer = decals.IndexForName(pick.Group);

            Assert.True(random.Pending.Count == 0,
                $"'{pick.Group}': stock took {pick.Draws.Count} draw(s), the port left {random.Pending.Count} untaken");
            Assert.True(answer == pick.Answer, $"'{pick.Group}': stock answered {pick.Answer}, the port {answer}");
            checkedPicks++;
        }

        Assert.True(checkedPicks == oracle.Picks.Count && random.Drawn > 500,
            $"the picks were replayed ({checkedPicks} picks, {random.Drawn} draws)");
    }

    [Fact]
    public void EveryIndexNamesStocksEntry()
    {
        var oracle = Stock();
        var (decals, _) = Loaded(oracle);

        foreach (var (index, name) in oracle.Names)
        {
            string port = decals.NameForIndex(index) is { Length: > 0 } n ? n : "-";
            Assert.True(port == name, $"index {index}: stock named '{name}', the port '{port}'");
        }
    }

    [Fact]
    public void EveryGameMaterialTranslatesAsStockDid()
    {
        var oracle = Stock();
        var (decals, _) = Loaded(oracle);
        int translated = 0;

        foreach (var (name, material, answer) in oracle.Translations)
        {
            string? port = decals.TranslateForGameMaterial(name, material);
            string shown = port is null ? "=" : port.Length == 0 ? "-" : port;
            Assert.True(shown == answer, $"'{name}' on material {material}: stock '{answer}', the port '{shown}'");
            if (port is { Length: > 0 })
                translated++;
        }

        Assert.True(translated > 20, $"some materials translated to another group ({translated})");
    }

    [Fact]
    public void AReloadCarriesTheTablesAFreshInstanceDoesNotHave()
    {
        var oracle = Stock();
        var (before, _) = Loaded(oracle);
        var writer = new ReloadWriter();
        ((IReloadParticipant)before).Save(writer);
        byte[] carried = writer.ToArray();

        // The fresh instance the rebuilt container makes: native does not call
        // Init again, so without the restore it answers nothing.
        var replay = new Replay();
        var after = new ClientDecals(new RecordingLog(), _ => null, _ => -1, replay.Draw);
        int empty = after.IndexForName("Impact.Metal");
        Assert.True(empty == -1 && after.Counts.Entries == 0, "a fresh instance holds no table");

        var reader = new ReloadReader(carried);
        ((IReloadParticipant)after).Restore(reader);
        Assert.True(reader.AtEnd, "the restore read every carried byte");
        Assert.True(after.Counts == before.Counts && after.Counts.Entries > 100,
            $"the restore carried the tables ({after.Counts} vs {before.Counts})");

        // Stock's first Impact.Metal pick, replayed through the restored copy.
        var pick = oracle.Picks.First(p => p.Group == "Impact.Metal");
        foreach (var draw in pick.Draws)
            replay.Pending.Enqueue(draw);
        Assert.True(after.IndexForName("Impact.Metal") == pick.Answer, "the restored copy picks as stock did");
        Assert.True(after.NameForIndex(pick.Answer) == before.NameForIndex(pick.Answer), "and names it the same");

        var again = new ReloadWriter();
        ((IReloadParticipant)after).Save(again);
        Assert.True(again.ToArray().AsSpan().SequenceEqual(carried), "a second save carries the same bytes");
    }
}
