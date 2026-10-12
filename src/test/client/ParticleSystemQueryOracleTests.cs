using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed <see cref="ParticleSystemQuery"/> against stock
/// particlesystemquery.cpp's <c>CParticleSystemQuery</c>, as particles.a
/// called it: <c>Fixtures/particlequery-stock.tsv.gz</c> (README beside it),
/// recorded through the C0 particle sweep with the lane's probe. Bit-exact:
/// every recorded answer is reproduced from the recorded inputs and arm state.
/// </summary>
public class ParticleSystemQueryOracleTests
{
    private sealed record Row(string Kind, string[] In, string[] Out);

    private static readonly Lazy<List<Row>> Rows = new(Load);

    private static List<Row> Load()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "particlequery-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<Row>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] cells = line.Split('\t');
            int bar = Array.IndexOf(cells, "|");
            rows.Add(bar < 0
                ? new Row(cells[0], cells[1..], [])
                : new Row(cells[0], cells[1..bar], cells[(bar + 1)..]));
        }
        return rows;
    }

    private static IEnumerable<Row> Of(string kind) => Rows.Value.Where(r => r.Kind == kind);

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] cells, int at) => new(F(cells[at]), F(cells[at + 1]), F(cells[at + 2]));

    /// <summary>An engine that answers one recorded call's arm state, and says what it was asked.</summary>
    private sealed class RecordedEngine(bool inGame, Vector light) : IParticleQueryEngine
    {
        public readonly List<(Vector At, bool Clamp)> Asked = [];

        public bool IsInGame() => inGame;

        public Vector LightForPoint(Vector position, bool clamp)
        {
            Asked.Add((position, clamp));
            return light;
        }
    }

    [Fact]
    public void The_recording_reaches_every_live_method_and_no_refused_one()
    {
        // The positive arm: a replay over an empty fixture passes vacuously.
        Assert.True(Of("cg").Count() >= 20, "collision-group rows");
        Assert.True(Of("light").Count() >= 50, "lighting rows");
        Assert.True(Of("trace").Count() >= 1000, "trace rows");
        Assert.True(Of("rp").Count() >= 100, "hit-box point rows");
        Assert.True(Of("hbi").Count() >= 100, "hit-box info rows");

        // C0 probe 1's finding, re-measured: no definition calls the six.
        Assert.Empty(Of("never"));
    }

    [Fact]
    public void Collision_group_names_answer_as_stock()
    {
        var query = new ParticleSystemQuery(new RecordedEngine(true, default));
        foreach (var row in Of("cg"))
            Assert.True(I(row.In[1]) == query.CollisionGroupFromName(row.In[0]), $"'{row.In[0]}' is {row.In[1]}");
    }

    [Fact]
    public void Lighting_answers_as_stock_bit_for_bit()
    {
        int distinct = Of("light").Select(r => string.Join(",", r.Out)).Distinct().Count();
        Assert.True(distinct >= 5, $"only {distinct} distinct tints: the conversion would barely be exercised");

        foreach (var row in Of("light"))
        {
            Vector origin = V(row.In, 0);
            bool inGame = row.In[3] == "1";
            var engine = new RecordedEngine(inGame, V(row.In, 4));
            var (r, g, b, a) = new ParticleSystemQuery(engine).LightingAtPoint(origin);

            Assert.Equal(row.Out, new[] { r.ToString(CultureInfo.InvariantCulture), g.ToString(CultureInfo.InvariantCulture),
                                          b.ToString(CultureInfo.InvariantCulture), a.ToString(CultureInfo.InvariantCulture) });
            if (inGame)
                Assert.Equal([(origin, true)], engine.Asked);
        }
    }

    [Fact]
    public void Every_in_game_trace_is_traced_as_the_library_asked()
    {
        foreach (var row in Of("trace"))
        {
            bool inGame = row.In[9] == "1";
            Assert.Equal(inGame, new ParticleSystemQuery(new RecordedEngine(inGame, default)).ShouldTrace());
        }

        // Both answers are traces the engine made (the native thunk carries
        // the decision out with the library's own mask, ignore and group): a
        // trace that hit something and one that did not are both recorded.
        Assert.Contains(Of("trace"), r => F(r.Out[12]) < 1f);
        Assert.Contains(Of("trace"), r => F(r.Out[12]) == 1f);
    }

    [Fact]
    public void Hit_box_points_answer_the_control_point_as_stock()
    {
        var query = new ParticleSystemQuery(new RecordedEngine(true, default));
        foreach (var row in Of("rp"))
        {
            int n = I(row.In[1]);
            bool hasObject = row.In[7] == "1";
            Vector cp = V(row.In, 8);
            bool hasIndex = row.In[11] == "1", hasRelative = row.In[12] == "1";

            var points = new Vector[n];
            var indices = hasIndex ? new int[n] : [];
            var relative = hasRelative ? new Vector[n] : [];
            // Poisoned, so an answer that leaves a slot alone cannot pass.
            for (int i = 0; i < n; i++)
            {
                points[i] = new Vector(-9, -9, -9);
                if (hasIndex) indices[i] = -7;
                if (hasRelative) relative[i] = new Vector(9, 9, 9);
            }

            query.RandomPointsOnControllingObjectHitBox(cp, hasObject, points, indices, relative);

            int stride = 3 + (hasIndex ? 1 : 0) + (hasRelative ? 3 : 0);
            Assert.Equal(n * stride, row.Out.Length);
            for (int i = 0; i < n; i++)
            {
                int at = i * stride;
                Assert.Equal(V(row.Out, at), points[i]);
                if (hasIndex)
                    Assert.Equal(I(row.Out[at + 3]), indices[i]);
                if (hasRelative)
                    Assert.Equal(V(row.Out, at + 3 + (hasIndex ? 1 : 0)), relative[i]);
            }
        }
    }

    [Fact]
    public void Hit_box_info_answers_as_stock()
    {
        var query = new ParticleSystemQuery(new RecordedEngine(true, default));
        foreach (var row in Of("hbi"))
            Assert.Equal(I(row.Out[0]), query.ControllingObjectHitBoxInfo(row.In[2] == "1"));
    }

    // The out-of-game arm, which the sweep cannot reach (it runs in a level):
    // particlesystemquery.cpp's else branches, read, not recorded.

    [Fact]
    public void Out_of_game_the_light_is_white_and_opaque_and_the_engine_is_not_asked()
    {
        var engine = new RecordedEngine(false, new Vector(0.5f, 0.5f, 0.5f));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), new ParticleSystemQuery(engine).LightingAtPoint(new Vector(1, 2, 3)));
        Assert.Empty(engine.Asked);
    }

    [Fact]
    public void Out_of_game_nothing_is_traced()
        => Assert.False(new ParticleSystemQuery(new RecordedEngine(false, default)).ShouldTrace());

    [Fact]
    public void Every_named_group_and_the_case_folded_spelling_answer_as_the_table()
    {
        var query = new ParticleSystemQuery(new RecordedEngine(true, default));
        Assert.Equal((int)EngineCollisionGroup.None, query.CollisionGroupFromName("none"));
        Assert.Equal((int)EngineCollisionGroup.Debris, query.CollisionGroupFromName("debris"));
        Assert.Equal((int)EngineCollisionGroup.Interactive, query.CollisionGroupFromName("Interactive"));
        Assert.Equal((int)EngineCollisionGroup.Npc, query.CollisionGroupFromName("NPC"));
        Assert.Equal((int)EngineCollisionGroup.NpcActor, query.CollisionGroupFromName("ACTOR"));
        Assert.Equal((int)EngineCollisionGroup.PassableDoor, query.CollisionGroupFromName("PASSABLE"));
        Assert.Equal((int)EngineCollisionGroup.None, query.CollisionGroupFromName("ROCKETS"));
    }
}
