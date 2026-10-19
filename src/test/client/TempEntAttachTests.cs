extern alias client;

using System.Globalization;

using SourceSharp.Abi;
using client::SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CTempEnts::AttachTentToPlayer against stock's, recorded:
/// <c>Fixtures/tempents-misc-stock.tsv</c> (README-tempents-stock.md, from the
/// client fixtures) holds the battery's twenty attaches over good and bad
/// clients and models, and the two tempents stock made. The managed attach,
/// answered the same, must refuse the same eighteen and make the same two.
/// </summary>
public class TempEntAttachTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    private sealed class SpawningPool(IEngineEffects inner) : EngineEffectsForwarder(inner)
    {
        public List<(Vector Origin, int Model, int Priority)> Created { get; } = new();
        public List<TempEntSpawn> Spawned { get; } = new();

        public override int TempEntPoolCapacity() => 500;

        public override bool TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
        {
            Created.Add((origin, modelIndex, priority));
            return true;
        }

        public override bool TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
        {
            Spawned.Add(spawn);
            return true;
        }
    }

    private static List<string[]> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "tempents-misc-stock.tsv");
        Assert.True(File.Exists(path), "the recorded attaches are missing: " + path);
        return File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
    }

    private static string At(string[] row, string key, int i = 0)
    {
        for (int k = 1; k < row.Length; k++)
        {
            if (row[k].StartsWith(key + "=", StringComparison.Ordinal))
                return i == 0 ? row[k][(key.Length + 1)..] : row[k + i];
        }
        throw new KeyNotFoundException($"{key} in '{string.Join(' ', row)}'");
    }

    private static float F(string[] r, string k, int i = 0) => float.Parse(At(r, k, i), CultureInfo.InvariantCulture);
    private static int I(string[] r, string k, int i = 0) => int.Parse(At(r, k, i), CultureInfo.InvariantCulture);
    private static Vector V(string[] r, string k) => new(F(r, k), F(r, k, 1), F(r, k, 2));

    [Fact]
    public void EveryRecordedAttachIsMadeOrRefusedAsStockDid()
    {
        List<string[]> rows = Recorded();
        var wrong = new List<string>();
        int n = 0, made = 0;

        // The models stock could draw: those it attached.
        var sprites = rows.Where(r => r[0] == "tat-out").Select(r => rows[rows.IndexOf(r) - 1]).Select(r => I(r, "model")).ToHashSet();

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "tat-in")
                continue;
            n++;
            string[] r = rows[i];
            string[]? o = i + 1 < rows.Count && rows[i + 1][0] == "tat-out" ? rows[i + 1] : null;

            var engine = TestServices.Fake();
            var native = new SpawningPool(engine);
            var clock = new Clock { Now = F(r, "t") };
            int model = I(r, "model");
            engine.Returns("Precache.ModelTypeOf", sprites.Contains(model) ? ModelType.Sprite : ModelType.Bad);
            engine.Returns("Precache.ModelFrameCount", 1);
            // The player's origin: the tempent's place less the lift (exact for these whole numbers).
            engine.Returns("Effects.ClientEntityOrigin", true);
            engine.Returns("Effects.ClientEntityOrigin.Origin", o is null ? new Vector(0, 0, 0) : new Vector(F(o, "o"), F(o, "o", 1), F(o, "o", 2) - F(r, "z")));

            var pool = new ClientTempEntPool(new RecordingLog(), native, clock, new TempEntPoolTests.NoSimulation());
            var attach = new TempEntAttachments(new RecordingLog(), pool, engine, native, clock);
            int slot = attach.AttachTentToPlayer(I(r, "client"), model, F(r, "z"), F(r, "life"), I(r, "max"));

            if (o is null)
            {
                if (slot >= 0 || native.Spawned.Count != 0)
                    wrong.Add($"attach {n} ({string.Join(' ', r[1..])}): made a tempent stock refused");
                continue;
            }

            made++;
            if (slot < 0 || native.Spawned.Count != 1)
            {
                wrong.Add($"attach {n}: refused what stock made");
                continue;
            }

            TempEntSpawn s = native.Spawned[0];
            var c = native.Created[0];
            bool ok = c.Origin.Equals(V(o, "o")) && c.Priority == TempEntPriority.High && s.RenderMode == I(o, "mode")
                      && s.ColorA == I(o, "a") && s.RenderAmt == I(o, "amt") && s.RenderFx == I(o, "fx") && s.ClientIndex == I(o, "client")
                      && s.TentOffset.Equals(V(o, "off")) && s.Die.Equals(F(o, "die")) && s.Flags == I(o, "flags")
                      && s.FrameMax.Equals(F(o, "framemax")) && s.FrameRate.Equals(F(o, "rate"));
            if (!ok)
                wrong.Add($"attach {n}: at {c.Origin} flags {s.Flags} die {s.Die} offset {s.TentOffset}, stock {string.Join(' ', o[1..])}");
        }

        Assert.True(n == 20 && made == 2, $"20 attaches, 2 made; read {n}, {made}");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} attaches disagree: " + string.Join(" | ", wrong.Take(6)));
    }

    [Fact]
    public void AStudioModelRidesWithoutAnimating()
    {
        var engine = TestServices.Fake();
        var native = new SpawningPool(engine);
        var clock = new Clock { Now = 5f };
        engine.Returns("Precache.ModelTypeOf", ModelType.Studio);
        engine.Returns("Effects.ClientEntityOrigin", true);
        engine.Returns("Effects.ClientEntityOrigin.Origin", new Vector(1, 2, 3));
        var pool = new ClientTempEntPool(new RecordingLog(), native, clock, new TempEntPoolTests.NoSimulation());

        new TempEntAttachments(new RecordingLog(), pool, engine, native, clock).AttachTentToPlayer(1, 7, 10, 2, 16);

        TempEntSpawn s = Assert.Single(native.Spawned);
        Assert.True(s.FrameMax == 0 && (s.Flags & (int)TempEntFlags.SprAnimate) == 0 && s.FrameRate == 0,
                    "a model is not animated as a sprite is");
    }
}
