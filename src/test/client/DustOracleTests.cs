using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dust motes against stock (lane c5-particles): CSourceSharpDustVolume,
/// stock's CDustEffect and C_Func_Dust's mote half, as
/// <c>Fixtures/dust-stock.tsv.gz</c> recorded it (README beside it). Bit for
/// bit, with the recorded rand() draws and point contents fed back.
/// </summary>
public class DustOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string Show(Vector v) => $"({v.X:G9} {v.Y:G9} {v.Z:G9})";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "dust-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
                rows.Add(line.Split('\t'));
        }
        return rows;
    });

    private static List<string[]> Rows => s_rows.Value;

    /// <summary>rand() as stock drew it, in order; a draw the port makes past the record is a failure.</summary>
    private sealed class Recorded(IEnumerable<string[]> rows) : IParticleRandom
    {
        private readonly Queue<int> _draws = new(rows.Where(r => r[0] == "dmr").Select(r => I(r[1])));
        public readonly List<string> Wrong = [];

        public int Remaining => _draws.Count;

        public float RandomFloat(float min, float max)
        {
            Wrong.Add($"drew RandomFloat [{min}, {max}]; stock's motes draw only rand()");
            return min;
        }

        public int RandomInt(int min, int max)
        {
            if (min != 0 || max != ParticleRandomExtensions.ValveRandMax)
                Wrong.Add($"drew RandomInt [{min}, {max}], not rand()");
            if (_draws.TryDequeue(out int v))
                return v;
            Wrong.Add("drew a rand() stock did not");
            return 0;
        }
    }

    /// <summary>The engine's point test as stock's ran: each point checked, its contents returned.</summary>
    private sealed class Brush(Queue<string[]> tests, List<string> wrong) : IDustBrushPointTest
    {
        public string Volume = "";

        public int PointContents(Vector point)
        {
            if (!tests.TryDequeue(out var r))
            {
                wrong.Add($"tested {Show(point)}, stock tested nothing more");
                return 0;
            }
            if (r[1] != Volume || !Same(point, V(r, 2)))
                wrong.Add($"tested {Volume} {Show(point)}, stock {r[1]} {Show(V(r, 2))}");
            return I(r[5]);
        }
    }

    private static DustVolumeSim FromUpdate(string[] u)
    {
        var sim = new DustVolumeSim
        {
            R = (byte)I(u[3]), G = (byte)I(u[4]), B = (byte)I(u[5]), A = (byte)I(u[6]),
            SpawnRate = I(u[7]), SizeMin = F(u[8]), SizeMax = F(u[9]), SpeedMax = I(u[10]),
            LifetimeMin = I(u[11]), LifetimeMax = I(u[12]), DistMax = I(u[13]), FallSpeed = F(u[14]),
            Flags = I(u[15]), Origin = V(u, 16), VolumeMins = V(u, 19), VolumeMaxs = V(u, 22),
        };
        sim.ApplyUpdate(I(u[2]) != 0);
        return sim;
    }

    [Fact]
    public void TheRecordingHoldsEveryKindOfRow()
    {
        var tags = Rows.GroupBy(r => r[0]).ToDictionary(g => g.Key, g => g.Count());
        foreach (string tag in new[] { "dmu", "dmt", "dmr", "dmp", "dmm", "dmn", "dmb", "dmw", "dmi", "dmo", "dmv", "dmd", "dmc" })
            Assert.True(tags.GetValueOrDefault(tag) > 0, $"no {tag} row: the replay of it would pass over nothing");
        Assert.Equal(2, tags["dmu"]);
        Assert.True(Rows.Count(r => r[0] == "dmo" && r[2] == "removed") > 0, "no mote died in the recorded steps");
    }

    /// <summary>
    /// Every tick: the spawner's events, each event's ten-point search over the
    /// brush's box (the points tested, in stock's order), the mote it made
    /// (place, velocity, die time, size) and the box handed to the particle
    /// manager, with every rand() consumed in stock's order and none left over.
    /// </summary>
    [Fact]
    public void EveryTickSpawnsWhatStockSpawned()
    {
        var wrong = new List<string>();
        var rec = new Recorded(Rows);
        var brush = new Brush(new Queue<string[]>(Rows.Where(r => r[0] == "dmp")), wrong);
        var sims = new Dictionary<string, DustVolumeSim>();
        var made = new Queue<string[]>(Rows.Where(r => r[0] == "dmm"));
        int ticks = 0, motes = 0;

        DustVolumeSim? current = null;
        string volume = "";
        int before = 0;
        foreach (var r in Rows)
        {
            switch (r[0])
            {
                case "dmu":
                    sims[r[1]] = FromUpdate(r);
                    break;
                case "dmt":
                    volume = r[1];
                    current = sims[volume];
                    brush.Volume = volume;
                    before = current.Motes.Count;
                    int events = current.Tick(F(r[2]), V(r, 3), brush, rec);
                    ticks++;
                    // the motes this tick made, against stock's
                    for (int k = before; k < current.Motes.Count; k++)
                    {
                        DustMote m = current.Motes[k];
                        motes++;
                        if (!made.TryDequeue(out var s))
                        {
                            wrong.Add($"tick {ticks}: made a mote stock did not");
                            continue;
                        }
                        if (s[1] != volume || !Same(m.Position, V(s, 2)) || !Same(m.Velocity, V(s, 5))
                            || !Same(m.DieTime, F(s[8])) || !Same(m.Size, F(s[9])))
                            wrong.Add($"tick {ticks}: mote {Show(m.Position)} v {Show(m.Velocity)} die {m.DieTime:G9} size {m.Size:G9}, " +
                                      $"stock {string.Join(' ', s)}");
                    }
                    // the events stock counted come on the dmn row, after the spawns
                    _pendingEvents = events;
                    break;
                case "dmn":
                    if (r[1] != volume || I(r[2]) != _pendingEvents)
                        wrong.Add($"tick {ticks}: {_pendingEvents} spawner event(s), stock {r[2]} ({r[1]})");
                    break;
                case "dmb":
                    current!.Bounds(out Vector mins, out Vector maxs);
                    if (r[1] != volume || !Same(mins, V(r, 2)) || !Same(maxs, V(r, 5)))
                        wrong.Add($"tick {ticks}: box {Show(mins)}..{Show(maxs)}, stock {Show(V(r, 2))}..{Show(V(r, 5))}");
                    break;
            }
            if (wrong.Count > 20)
                break;
        }

        wrong.AddRange(rec.Wrong.Take(10));
        Assert.True(ticks == 800 && motes > 500, $"the replay reached {ticks} tick(s) and {motes} mote(s)");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(rec.Remaining == 0, $"{rec.Remaining} rand() draw(s) stock made and the port did not");
        Assert.True(made.Count == 0, $"{made.Count} mote(s) stock made and the port did not");
    }

    private int _pendingEvents;

    /// <summary>
    /// Every recorded step: a mote's age, its death at its time, its
    /// horizontal velocity closing on the wind by the frame's time, its move.
    /// </summary>
    [Fact]
    public void EveryStepMovesTheMoteAsStockMovedIt()
    {
        var wrong = new List<string>();
        float dt = 0, frameTime = 0;
        Vector wind = default;
        string[]? input = null;
        int steps = 0, deaths = 0;
        foreach (var r in Rows)
        {
            switch (r[0])
            {
                case "dmw":
                    dt = F(r[2]); frameTime = F(r[3]); wind = V(r, 4);
                    Assert.Equal(0, I(r[7]) & DustVolumeSim.FlagFrozen);
                    break;
                case "dmi":
                    input = r;
                    break;
                case "dmo":
                    var m = new DustMote { Position = V(input!, 2), Velocity = V(input!, 5), Lifetime = F(input![8]), DieTime = F(input[9]) };
                    bool alive = DustVolumeSim.Step(ref m, dt, frameTime, wind);
                    steps++;
                    if (r[2] == "removed")
                    {
                        deaths++;
                        if (alive)
                            wrong.Add($"step {steps}: lived, stock removed it ({string.Join(' ', input)})");
                    }
                    else if (!alive || !Same(m.Position, V(r, 2)) || !Same(m.Velocity, V(r, 5)) || !Same(m.Lifetime, F(r[8])))
                    {
                        wrong.Add($"step {steps}: {(alive ? "" : "died, ")}{Show(m.Position)} v {Show(m.Velocity)} life {m.Lifetime:G9}, " +
                                  $"stock {string.Join(' ', r)}");
                    }
                    break;
            }
            if (wrong.Count > 20)
                break;
        }

        Assert.True(steps > 5000 && deaths > 0, $"the replay reached {steps} step(s), {deaths} death(s)");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// Every recorded draw: the mote's view-space place, its alpha (the sine
    /// over its life, faded towards distmax, times the volume's alpha), its
    /// size (scaled by depth for motes), its sort key, and the motes past
    /// distmax that stock did not draw.
    /// </summary>
    [Fact]
    public void EveryDrawIsStocksDraw()
    {
        var wrong = new List<string>();
        DustVolumeSim? sim = null;
        ParticleView view = default;
        int drawn = 0, culled = 0;
        foreach (var r in Rows)
        {
            switch (r[0])
            {
                case "dmv":
                    float[] m = Enumerable.Range(2, 12).Select(k => F(r[k])).ToArray();
                    view = new ParticleView(m, 0);
                    sim = new DustVolumeSim
                    {
                        Flags = I(r[14]), DistMax = I(r[15]),
                        R = (byte)I(r[16]), G = (byte)I(r[17]), B = (byte)I(r[18]), A = (byte)I(r[19]),
                    };
                    break;
                case "dmd":
                case "dmc":
                {
                    var mote = new DustMote { Position = V(r, 2), Lifetime = F(r[5]), DieTime = F(r[6]), Size = F(r[7]) };
                    bool draws = sim!.Render(mote, view, out Vector viewPos, out float alpha, out float size);
                    if (r[0] == "dmc")
                    {
                        culled++;
                        if (draws || !Same(viewPos, V(r, 9)) || !Same(DustVolumeSim.SortKey(viewPos), F(r[12])))
                            wrong.Add($"culled {culled}: draws {draws} at {Show(viewPos)}, stock {string.Join(' ', r)}");
                        break;
                    }
                    drawn++;
                    if (!draws || !Same(viewPos, V(r, 9)) || !Same(alpha, F(r[12])) || !Same(size, F(r[13]))
                        || !Same(DustVolumeSim.SortKey(viewPos), F(r[14])))
                        wrong.Add($"draw {drawn}: {draws} at {Show(viewPos)} alpha {alpha:G9} size {size:G9}, stock {string.Join(' ', r)}");
                    break;
                }
            }
            if (wrong.Count > 20)
                break;
        }

        Assert.True(drawn > 5000 && culled > 0, $"the replay reached {drawn} draw(s), {culled} culled");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
