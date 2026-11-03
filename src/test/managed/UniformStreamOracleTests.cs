extern alias client;

using System.Globalization;

using SourceSharp.Shared;

using Xunit;

using C = client::SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// TF2 STAGE 1 CORE: A SEEDED ROLL DRAWS WHAT STOCK DRAWS, ON BOTH SIDES.
///
/// <para>
/// Stock's predicted weapon code reseeds vstdlib's stream from the command's
/// seed and draws (<c>RandomSeed(iSeed); RandomInt(0, WEAPON_RANDOM_RANGE - 1)</c>,
/// TF's <c>CalcIsAttackCriticalHelper</c>), so the server and the client's
/// replay of a command roll the same crit. The shared
/// <see cref="UniformStream"/> compiles into both sides; each side's copy must
/// draw what the game's own libvstdlib.so drew from the same seeds
/// (<c>Fixtures/vstdlib-draws-stock.tsv</c>, recorded as its README says).
/// </para>
/// </summary>
public class UniformStreamOracleTests
{
    private sealed record Row(int Seed, string Kind, string[] Draws);

    private static IReadOnlyList<Row> Fixture()
    {
        string path = RepoLayout.Repo("src", "test", "managed", "Fixtures", "vstdlib-draws-stock.tsv");
        var rows = File.ReadAllLines(path)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Select(f => new Row(int.Parse(f[0], CultureInfo.InvariantCulture), f[1], f[4].Split(' ')))
            .ToList();

        // A short or empty fixture is a failure, not a pass: 11 seeds x 3 runs.
        Assert.Equal(33, rows.Count);
        return rows;
    }

    private static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

    private static uint Hex(string s) => uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>One fixture run through a stream of either side, as the recorded calls made it.</summary>
    private static string[] Draw(Func<int, (Func<int, int, int> Int, Func<float, float, float> Float)> seeded, Row row)
    {
        var (randomInt, randomFloat) = seeded(row.Seed);

        return row.Kind switch
        {
            "int"   => [.. Enumerable.Range(0, row.Draws.Length).Select(_ => randomInt(0, 9999).ToString(CultureInfo.InvariantCulture))],
            "float" => [.. Enumerable.Range(0, row.Draws.Length).Select(_ => Bits(randomFloat(0f, 1f)).ToString("x8", CultureInfo.InvariantCulture))],
            "mixed" =>
            [
                randomInt(-3, 3).ToString(CultureInfo.InvariantCulture),
                Bits(randomFloat(-10f, 10f)).ToString("x8", CultureInfo.InvariantCulture),
                randomInt(0, 1).ToString(CultureInfo.InvariantCulture),
                Bits(randomFloat(0.5f, 0.75f)).ToString("x8", CultureInfo.InvariantCulture),
            ],
            _ => throw new InvalidDataException($"fixture kind '{row.Kind}'"),
        };
    }

    private static (Func<int, int, int>, Func<float, float, float>) Server(int seed)
    {
        var stream = new UniformStream();
        stream.SetSeed(seed);
        return (stream.RandomInt, stream.RandomFloat);
    }

    private static (Func<int, int, int>, Func<float, float, float>) Client(int seed)
    {
        var stream = new C.UniformStream();
        stream.SetSeed(seed);
        return (stream.RandomInt, stream.RandomFloat);
    }

    [Fact]
    public void TheServersStreamDrawsWhatTheGamesVstdlibDrewFromEachSeed()
    {
        foreach (var row in Fixture())
            Assert.True(row.Draws.SequenceEqual(Draw(Server, row)),
                $"seed {row.Seed} {row.Kind}: stock [{string.Join(' ', row.Draws)}], server [{string.Join(' ', Draw(Server, row))}]");
    }

    [Fact]
    public void TheClientsStreamDrawsWhatTheGamesVstdlibDrewFromEachSeed()
    {
        foreach (var row in Fixture())
            Assert.True(row.Draws.SequenceEqual(Draw(Client, row)),
                $"seed {row.Seed} {row.Kind}: stock [{string.Join(' ', row.Draws)}], client [{string.Join(' ', Draw(Client, row))}]");
    }

    [Fact]
    public void ReseedingRestartsTheSequenceAndAnotherSeedIsAnotherSequence()
    {
        var stream = new UniformStream(7);
        int[] first = [.. Enumerable.Range(0, 4).Select(_ => stream.RandomInt(0, 9999))];

        stream.SetSeed(7);
        int[] again = [.. Enumerable.Range(0, 4).Select(_ => stream.RandomInt(0, 9999))];

        stream.SetSeed(8);
        int[] other = [.. Enumerable.Range(0, 4).Select(_ => stream.RandomInt(0, 9999))];

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void TheStreamIsTheEnginesRandomInterface()
    {
        // A behaviour that takes an IUniformRandom takes a seeded stream as
        // well as the engine's: the two answer the same two calls.
        Abi.IUniformRandom random = new UniformStream(12345);
        var row = Fixture().First(r => r.Seed == 12345 && r.Kind == "int");

        Assert.Equal(int.Parse(row.Draws[0], CultureInfo.InvariantCulture), random.RandomInt(0, 9999));
    }
}
