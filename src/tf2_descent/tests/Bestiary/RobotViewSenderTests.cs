using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Bestiary;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Hero;

using Xunit;

namespace TF2.Descent.Tests.Bestiary;

/// <summary>
/// What a hero may know of a robot, told to him alone (plan_descent_logic UI6, ruling L6,
/// default E7; <see cref="RobotViewSender"/>): the gated parts of a robot view are absent from
/// the bytes below their threshold and present at it; the counts go at a spawn and when they
/// change; a view goes again only when what he may see of the body changes.
/// </summary>
public sealed class RobotViewSenderTests
{
    /// <summary>Numbers no other field of a view could hold by chance, planted to be looked for in the bytes.</summary>
    private const int PlantedLife = 12345, PlantedMax = 23456;

    private const double PlantedFireResist = 37;

    private const string PlantedStyle = "Reckless Deadeye";

    private sealed class Robots : IRobotFactsSource
    {
        public List<LentRobot> Bodies { get; } = [];

        public void Lent(List<LentRobot> into)
        {
            into.Clear();
            into.AddRange(Bodies);
        }

        public int EntityOfBody(int body) => 0;
    }

    private sealed class Kit
    {
        public readonly HeroRig Hero = new();
        public readonly RecordingEngine Wire = new();
        public readonly Robots Robots = new();
        public readonly RobotViewSender Sender;
        public readonly HeroComponent Component;

        public Kit()
        {
            Sender = new RobotViewSender(Wire, Hero.Heroes, Robots, Hero.Rules);
            Component = Hero.Join()!;
        }

        public void Kills(string template, int kills) =>
            Assert.True(Component.Apply(s => s with { Bestiary = s.Bestiary.SetItem(template, kills) }).Ok);

        public IReadOnlyList<(string Key, int Player, byte[] Bytes)> Sent =>
            [.. Wire.Calls.Where(c => c.Member == "UserMessages.Send").Select(c => (c.ArgAs<string>(0), c.ArgAs<int>(2), c.ArgAs<byte[]>(4)))];

        public IReadOnlyList<byte[]> Bytes<T>(int from = 0) where T : IUserMessagePayload<T> =>
            [.. Sent.Skip(from).Where(s => s.Key == T.Key).Select(s => s.Bytes)];

        public IReadOnlyList<T> Of<T>(int from = 0) where T : IUserMessagePayload<T> =>
            [.. Bytes<T>(from).Select(b =>
            {
                var reader = new UserMessageReader(b);
                return T.Read(ref reader);
            })];
    }

    /// <summary>A fireproof soldier robot with planted numbers in body entity 40.</summary>
    private static LentRobot Soldier(int entity = 40, int serial = 1, double life = PlantedLife) =>
        new(entity, serial, "soldier", 1, life, PlantedMax,
            ImmutableSortedDictionary<string, double>.Empty.Add("blast", 25).Add("fire", PlantedFireResist),
            "fire", PlantedStyle, ["fireproof"]);

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Le(int value) => BitConverter.GetBytes(value);

    private static byte[] Le(short value) => BitConverter.GetBytes(value);

    private static byte[] OneView(Kit k, int kills)
    {
        k.Kills("soldier", kills);
        k.Robots.Bodies.Add(Soldier());
        int from = k.Sent.Count;
        k.Sender.Poll();
        return Assert.Single(k.Bytes<RobotViewWire>(from));
    }

    //-------------------------------------------------------------------------
    // The gate, on the bytes
    //-------------------------------------------------------------------------

    [Fact]
    public void The_shipped_thresholds_are_the_plans_one_fifteen_thirty_and_sixty()
    {
        var s = new HeroRig().Rules.Content.Bestiary;

        Assert.Equal((1, 15, 30, 60), (s.Count, s.Life, s.Resistances, s.Style));
    }

    [Fact]
    public void Below_the_life_threshold_the_life_numbers_are_not_in_the_bytes()
    {
        var bytes = OneView(new Kit(), kills: 14);

        Assert.False(Contains(bytes, Le(PlantedLife)));
        Assert.False(Contains(bytes, Le(PlantedMax)));
    }

    [Fact]
    public void At_the_life_threshold_the_life_numbers_are_in_the_bytes()
    {
        var bytes = OneView(new Kit(), kills: 15);

        Assert.True(Contains(bytes, Le(PlantedLife)));
        Assert.True(Contains(bytes, Le(PlantedMax)));
    }

    [Fact]
    public void Below_the_resistance_threshold_resistances_immunity_and_affixes_are_not_in_the_bytes()
    {
        var k = new Kit();
        var bytes = OneView(k, kills: 29);

        Assert.False(Contains(bytes, Le((short)PlantedFireResist)));
        var view = Assert.Single(k.Of<RobotViewWire>()).View();
        Assert.Equal((0, 0, 0), (k.Of<RobotViewWire>()[0].Resist.Length, k.Of<RobotViewWire>()[0].Immune.Length, k.Of<RobotViewWire>()[0].Affixes.Length));
        Assert.Null(view.Resist);
    }

    [Fact]
    public void At_the_resistance_threshold_resistances_immunity_and_affixes_are_in_the_bytes()
    {
        var k = new Kit();
        var bytes = OneView(k, kills: 30);

        Assert.True(Contains(bytes, Le((short)PlantedFireResist)));
        var view = Assert.Single(k.Of<RobotViewWire>()).View();
        var types = k.Hero.Rules.Content.DamageTypes.Rows.Select(t => t.Id).ToList();
        Assert.Equal((int)PlantedFireResist, view.Resist![types.IndexOf("fire")]);
        Assert.Equal(types.IndexOf("fire"), view.Immunity);
        Assert.Equal([k.Hero.Rules.Content.Champions.Rows.Select(c => c.Id).ToList().IndexOf("fireproof")], view.Affixes);
    }

    [Fact]
    public void Below_the_style_threshold_the_style_is_not_in_the_bytes()
    {
        var bytes = OneView(new Kit(), kills: 59);

        Assert.False(Contains(bytes, System.Text.Encoding.UTF8.GetBytes(PlantedStyle)));
    }

    [Fact]
    public void At_the_style_threshold_the_style_is_in_the_bytes()
    {
        var bytes = OneView(new Kit(), kills: 60);

        Assert.True(Contains(bytes, System.Text.Encoding.UTF8.GetBytes(PlantedStyle)));
    }

    [Fact]
    public void A_first_kill_shows_the_count_only()
    {
        var k = new Kit();
        OneView(k, kills: 1);

        var view = Assert.Single(k.Of<RobotViewWire>()).View();
        Assert.Equal(RevealBits.Count, view.Reveal);
        Assert.Equal((null, null, null, null, null), (view.Life, view.MaxLife, view.Resist, view.Immunity, view.Style));
        var count = Assert.Single(k.Of<BestiaryCounts>()).View();
        Assert.Contains((1, 1), count.Rows);            // robots.json row 1, the soldier: one kill
    }

    [Fact]
    public void The_thirtieth_kill_shows_resistances()
    {
        var k = new Kit();
        k.Kills("soldier", 29);
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        Assert.Null(k.Of<RobotViewWire>().Last().View().Resist);     // before: 29 kills hide them

        k.Kills("soldier", 30);
        int from = k.Sent.Count;
        k.Sender.Poll();

        var view = Assert.Single(k.Of<RobotViewWire>(from)).View();
        Assert.Equal(RevealBits.Count | RevealBits.Life | RevealBits.Resistances, view.Reveal);
        Assert.NotNull(view.Resist);
    }

    [Fact]
    public void With_no_kills_a_view_says_nothing_but_the_body_and_its_template()
    {
        var k = new Kit();
        k.Robots.Bodies.Add(Soldier());

        k.Sender.Poll();

        var view = Assert.Single(k.Of<RobotViewWire>()).View();
        Assert.Equal((40, 2, RevealBits.None), (view.Entity, view.Template, view.Reveal));
        Assert.Equal((null, null, null, null), (view.Life, view.Resist, view.Affixes, view.Style));
    }

    //-------------------------------------------------------------------------
    // Who is told, and when
    //-------------------------------------------------------------------------

    [Fact]
    public void Views_and_counts_go_to_the_hero_alone()
    {
        var k = new Kit();
        k.Kills("soldier", 3);
        k.Robots.Bodies.Add(Soldier());

        k.Sender.Poll();

        Assert.NotEmpty(k.Sent);
        Assert.All(k.Sent, s => Assert.Equal(HeroRig.Index, s.Player));
    }

    [Fact]
    public void A_kill_that_crosses_no_threshold_tells_the_count_and_no_view()
    {
        var k = new Kit();
        k.Kills("soldier", 3);
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Kills("soldier", 4);
        k.Sender.Poll();

        Assert.Empty(k.Of<RobotViewWire>(from));
        Assert.Equal([(1, 4)], Assert.Single(k.Of<BestiaryCounts>(from)).View().Rows);
    }

    [Fact]
    public void A_kill_that_crosses_a_threshold_tells_that_templates_bodies_again_and_no_other()
    {
        var k = new Kit();
        k.Kills("soldier", 14);
        k.Robots.Bodies.Add(Soldier(entity: 40));
        k.Robots.Bodies.Add(Soldier(entity: 41) with { Template = "scout", TemplateRow = 0 });
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Kills("soldier", 15);
        k.Sender.Poll();

        var view = Assert.Single(k.Of<RobotViewWire>(from)).View();
        Assert.Equal((40, PlantedLife), (view.Entity, view.Life));
    }

    [Fact]
    public void Nothing_changed_tells_nothing()
    {
        var k = new Kit();
        k.Kills("soldier", 20);
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Sender.Poll();
        k.Sender.Poll();

        Assert.Equal(from, k.Sent.Count);
    }

    [Fact]
    public void A_revealed_life_that_falls_is_told_again()
    {
        var k = new Kit();
        k.Kills("soldier", 15);
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Robots.Bodies[0] = Soldier(life: 100.2);
        k.Sender.Poll();

        Assert.Equal(101, Assert.Single(k.Of<RobotViewWire>(from)).View().Life);
    }

    [Fact]
    public void A_hidden_life_that_falls_tells_nothing()
    {
        var k = new Kit();
        k.Kills("soldier", 14);
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Robots.Bodies[0] = Soldier(life: 100);
        k.Sender.Poll();

        Assert.Equal(from, k.Sent.Count);
    }

    [Fact]
    public void A_body_lent_again_is_told_again()
    {
        var k = new Kit();
        k.Robots.Bodies.Add(Soldier(serial: 1));
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Robots.Bodies[0] = Soldier(serial: 2);
        k.Sender.Poll();

        Assert.Equal(2, Assert.Single(k.Of<RobotViewWire>(from)).Serial);
    }

    [Fact]
    public void A_parked_body_is_told_gone_once()
    {
        var k = new Kit();
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Robots.Bodies.Clear();
        k.Sender.Poll();
        k.Sender.Poll();

        var gone = Assert.Single(k.Of<RobotViewWire>(from)).View();
        Assert.True(gone.Gone);
        Assert.Equal(40, gone.Entity);
    }

    [Fact]
    public void A_spawn_resyncs_every_count_and_tells_every_view_again()
    {
        var k = new Kit();
        k.Kills("soldier", 7);
        k.Kills("scout", 2);
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Sender.OnSpawned(k.Component.Player, 0f);
        k.Sender.Poll();

        var counts = Assert.Single(k.Of<BestiaryCounts>(from)).View();
        Assert.Equal(BestiaryPhase.Resync, counts.Phase);
        Assert.Equal([(0, 2), (1, 7)], counts.Rows.OrderBy(r => r.Template));
        Assert.Single(k.Of<RobotViewWire>(from));
    }

    [Fact]
    public void A_spawn_with_no_kills_still_resyncs_so_the_client_forgets()
    {
        var k = new Kit();

        k.Sender.OnSpawned(k.Component.Player, 0f);

        var counts = Assert.Single(k.Of<BestiaryCounts>()).View();
        Assert.Equal((BestiaryPhase.Resync, 0), (counts.Phase, counts.Rows.Count));
    }

    [Fact]
    public void After_a_level_ends_the_next_levels_bodies_are_told_anew()
    {
        var k = new Kit();
        k.Robots.Bodies.Add(Soldier());
        k.Sender.Poll();
        int from = k.Sent.Count;

        k.Sender.OnLevelShutdown();
        k.Sender.Poll();

        Assert.Single(k.Of<RobotViewWire>(from));
    }

    [Fact]
    public void A_view_at_its_fullest_fits_one_user_message()
    {
        var k = new Kit();
        k.Kills("soldier", 1000);
        var robot = Soldier() with
        {
            Style = new string('x', 200),
            Affixes = [.. k.Hero.Rules.Content.Champions.Rows.Select(c => c.Id)],
            Resist = k.Hero.Rules.Content.DamageTypes.Rows.Aggregate(ImmutableSortedDictionary<string, double>.Empty, (d, t) => d.Add(t.Id, 75)),
        };

        var wire = k.Sender.Wire(k.Component.Sheet!, robot);

        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];
        Assert.True(wire.Write(buffer) > 0);
        Assert.Equal(RobotViewWire.MaxAffixes, wire.Affixes.Length);
    }
}
