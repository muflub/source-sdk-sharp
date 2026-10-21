using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ParticleCollision"/> against stock particle_collision.cpp's
/// <c>CParticleCollision</c>: <c>Fixtures/particlecollision-stock.tsv.gz</c>
/// (README beside it). Every Setup is replayed with the world traces it made
/// (their inputs checked, their answers fed back) and must find the same
/// planes; every recorded MoveParticle is replayed on its object's planes
/// with its traces and random draws and must land bit for bit.
/// </summary>
public class ParticleCollisionOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool IsZero(Vector v) => v.X == 0 && v.Y == 0 && v.Z == 0;

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static List<List<string[]>> Units()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "particlecollision-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var units = new List<List<string[]>>();
        List<string[]>? cur = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] r = line.Split('\t');
            if (r[0] is "pcs" or "pcm")
                cur = [r];
            else
                cur!.Add(r);
            if (r[0] is "pcp" or "pcmo")
            {
                units.Add(cur!);
                cur = null;
            }
        }
        return units;
    }

    /// <summary>The world as recorded: each trace's inputs checked, its answer handed back.</summary>
    private sealed class RecordedWorld(IEnumerable<string[]> rows) : IParticleWorldTrace, IParticleRandom
    {
        private readonly Queue<string[]> _traces = new(rows.Where(r => r[0] == "pct"));
        private readonly Queue<string[]> _draws = new(rows.Where(r => r[0] == "pcr"));
        public readonly List<string> Wrong = [];

        public bool Drained => _traces.Count == 0 && _draws.Count == 0;

        public ParticleTrace TraceLine(Vector start, Vector end)
        {
            if (!_traces.TryDequeue(out var r))
            {
                Wrong.Add("a trace stock did not make");
                return new ParticleTrace { Fraction = 1 };
            }
            if (!Same(start, V(r, 1)) || !Same(end, V(r, 4)))
                Wrong.Add($"traced {start} -> {end}, stock {V(r, 1)} -> {V(r, 4)}");
            return new ParticleTrace
            {
                Fraction = F(r[8]), EndPos = V(r, 9), Normal = V(r, 12), Dist = F(r[15]),
                AllSolid = r[16] == "1", StartSolid = r[17] == "1",
            };
        }

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r))
            {
                Wrong.Add("a random draw stock did not take");
                return min;
            }
            if (!Same(min, F(r[1])) || !Same(max, F(r[2])))
                Wrong.Add($"drew in [{min}, {max}], stock [{r[1]}, {r[2]}]");
            return F(r[3]);
        }

        public int RandomInt(int min, int max)
        {
            Wrong.Add("RandomInt");
            return min;
        }

    }

    [Fact]
    public void Every_recorded_setup_and_move_is_reproduced_bit_for_bit()
    {
        var units = Units();
        var objects = new Dictionary<string, ParticleCollision>();
        int setups = 0, moves = 0, bounces = 0, stops = 0, unknown = 0, unsetup = 0;
        var wrong = new List<string>();

        foreach (var u in units)
        {
            string[] head = u[0], tail = u[^1];
            var world = new RecordedWorld(u);
            if (head[0] == "pcs")
            {
                var c = new ParticleCollision();
                Vector? dir = head[5] == "1" ? V(head, 6) : null;
                c.Setup(V(head, 2), dir, F(head[9]), F(head[10]), F(head[11]), F(head[12]), F(head[13]), world);
                objects[head[1]] = c;
                setups++;

                int planes = int.Parse(tail[2], CultureInfo.InvariantCulture);
                if (c.ActivePlanes != planes)
                    wrong.Add($"setup {setups}: {c.ActivePlanes} planes, stock {planes}");
                for (int i = 0; i < Math.Min(planes, c.ActivePlanes); i++)
                {
                    var (n, d) = c.Plane(i);
                    if (!Same(n, V(tail, 3 + i * 4)) || !Same(d, F(tail[6 + i * 4])))
                        wrong.Add($"setup {setups} plane {i}: ({n}, {d}), stock ({V(tail, 3 + i * 4)}, {tail[6 + i * 4]})");
                }
            }
            else
            {
                if (!objects.TryGetValue(head[1], out var c))
                {
                    // Never Setup (an emitter that only sets gravity): the
                    // constructor's no planes, which the move replays as well.
                    if (head[13] != "0")
                    {
                        unknown++;
                        continue;
                    }
                    c = new ParticleCollision();
                    objects[head[1]] = c;
                }
                moves++;

                // An address reused by an object that was never Setup (an
                // emitter that only sets gravity): its planes are the
                // constructor's none.
                if (head[13] == "0" && c.ActivePlanes != 0)
                {
                    c = new ParticleCollision();
                    objects[head[1]] = c;
                    unsetup++;
                }

                c.Gravity = F(head[11]);
                c.CollisionDampen = F(head[12]);
                if (c.ActivePlanes != int.Parse(head[13], CultureInfo.InvariantCulture))
                    wrong.Add($"move {moves}: {c.ActivePlanes} planes, stock had {head[13]}");

                Vector origin = V(head, 2), velocity = V(head, 5);
                bool hasRoll = head[8] == "1";
                float roll = F(head[9]);
                var trace = new ParticleTrace { Fraction = -1 };
                bool bounced = c.MoveParticle(ref origin, ref velocity, ref roll, hasRoll, F(head[10]), world, world, ref trace);

                if (!Same(origin, V(tail, 2)) || !Same(velocity, V(tail, 5)) || (hasRoll && !Same(roll, F(tail[8]))))
                    wrong.Add($"move {moves}: ({origin}, {velocity}, {roll}), stock ({V(tail, 2)}, {V(tail, 5)}, {tail[8]})");
                if (bounced != (tail[9] == "1"))
                    wrong.Add($"move {moves}: bounced {bounced}, stock {tail[9]}");
                if (c.ActivePlanes > 0 && trace.Fraction != -1 && (!Same(trace.Fraction, F(tail[10])) || trace.AllSolid != (tail[11] == "1")))
                    wrong.Add($"move {moves}: trace ({trace.Fraction}, {trace.AllSolid}), stock ({tail[10]}, {tail[11]})");
                bounces += bounced ? 1 : 0;
                stops += IsZero(velocity) && !IsZero(V(head, 5)) ? 1 : 0;
            }

            if (!world.Drained)
                wrong.Add($"unit {setups + moves}: stock made traces or draws the port did not");
            wrong.AddRange(world.Wrong);
            if (wrong.Count > 20)
                break;
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(setups >= 50 && moves >= 4000 && bounces >= 500 && stops >= 20,
            $"the recording reaches every arm: {setups} setups, {moves} moves, {bounces} bounces, {stops} stops");
        Assert.True(unknown < 10 && unsetup < 10, $"{unknown} moves on an unrecorded object, {unsetup} on a reused address");
    }
}
