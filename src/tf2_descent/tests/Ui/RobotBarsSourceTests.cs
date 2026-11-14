extern alias client;

using SourceSharp.Abi;

using TF2.Descent.Client.Ui;
using TF2.Descent.Shared;

using Xunit;

using Bodies = client::TF2.Descent.Client.Ui.IClientRobotBodies;
using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using ClientFlags = client::TF2.Descent.Shared.RobotFlags;
using ClientReveal = client::TF2.Descent.Shared.RevealBits;
using ClientView = client::TF2.Descent.Shared.RobotView;
using IGameClock = client::SourceSharp.Abi.IGameClock;
using IRobotKnowledge = client::TF2.Descent.Client.Bestiary.IRobotKnowledge;
using IRobotBarView = client::TF2.Descent.Client.Ui.IRobotBarView;
using OnClient = client::TF2.Descent.Client.Ui.RobotOnClient;
using Source = client::TF2.Descent.Client.Ui.RobotBarsSource;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The robot bars' source (plan_descent_logic UI6): which robots get a bar (under the crosshair,
/// seen hurt, IMMUNE fresh), and that each bar shows what the hero's knowledge holds and nothing
/// more: the count only after a first kill, resistances only once a view carries them.
/// </summary>
public sealed class RobotBarsSourceTests
{
    private static readonly Catalog Content = new();

    /// <summary>A body standing straight ahead: feet and head project to the screen's middle column.</summary>
    private static OnClient Ahead(int entity = 40, int template = 2, byte life = 255, ClientFlags flags = ClientFlags.Lent | ClientFlags.Champion,
                                  byte statuses = 0, float distance = 500) =>
        new(entity, new Vector(distance, 0, 0), 1f, template, life, flags, statuses);

    private sealed class Robots : Bodies
    {
        public List<OnClient> Live { get; } = [];

        void Bodies.Live(List<OnClient> into)
        {
            into.Clear();
            into.AddRange(Live);
        }
    }

    /// <summary>The player at the origin looking along +x: x maps to depth; y and z to the screen, centred.</summary>
    private sealed class View : IRobotBarView
    {
        public bool Walls { get; set; }
        public float Aim { get; set; }                  // a sideways shift of the view: 0 looks straight at y = 0

        public Vector LocalOrigin => default;

        public bool ToScreen(Vector world, out float x, out float y)
        {
            float depth = Math.Max(world.X, 1f);
            x = 0.5f + (world.Y - Aim) / depth;
            y = 0.5f - (world.Z - 40f) / depth;
            return world.X > 0;
        }

        public bool Clear(Vector from, Vector to) => !Walls;
    }

    private sealed class Knowledge : IRobotKnowledge
    {
        public Dictionary<int, int> Kills { get; } = [];
        public Dictionary<int, ClientView> ViewsHeld { get; } = [];
        public Dictionary<int, (float, int)> Immune { get; } = [];
        public bool ContentMismatch => false;
        public int KillsOf(int template) => Kills.GetValueOrDefault(template);
        public ClientView? ViewOf(int entity) => ViewsHeld.GetValueOrDefault(entity);
        public (float At, int DamageType)? ImmuneOf(int entity) => Immune.TryGetValue(entity, out var i) ? i : null;
        public IReadOnlyDictionary<int, ClientView> Views => ViewsHeld;
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Kit
    {
        public readonly Robots Robots = new();
        public readonly View View = new();
        public readonly Knowledge Known = new();
        public readonly Clock Clock = new();
        public readonly Source Source;

        public Kit() => Source = new Source(Robots, Known, Content, View, Clock);

        public void Frame(float seconds = 0.016f) => Clock.Now += seconds;
    }

    private static ClientView View30(int entity = 40) =>
        new(entity, 1, 2, Content.RobotFingerprint, ClientReveal.Count | ClientReveal.Life | ClientReveal.Resistances,
            150, 200, [0, 25, 0, -20], 0, [Content.Champions.ToList().FindIndex(c => c.Id == "fireproof")], null);

    [Fact]
    public void A_robot_under_the_crosshair_has_a_bar_with_its_templates_name()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead());

        Assert.Equal(1, k.Source.BarCount);
        Assert.Equal(Content.Robots[1].Name, k.Source.Bar(0).Name);    // the net state's template 2 is robots.json row 1
    }

    [Fact]
    public void A_robot_beside_the_crosshair_has_none()
    {
        var k = new Kit();
        k.View.Aim = 200;                               // looking well to the side of it
        k.Robots.Live.Add(Ahead());

        Assert.Equal(0, k.Source.BarCount);
    }

    [Fact]
    public void A_robot_behind_a_wall_has_none()
    {
        var k = new Kit();
        k.View.Walls = true;
        k.Robots.Live.Add(Ahead());

        Assert.Equal(0, k.Source.BarCount);
    }

    [Fact]
    public void A_robot_out_of_range_has_none()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead(distance: Source.CrosshairRange + 100));

        Assert.Equal(0, k.Source.BarCount);
    }

    [Fact]
    public void A_robot_seen_hurt_keeps_its_bar_for_five_seconds_off_the_crosshair()
    {
        var k = new Kit();
        k.View.Aim = 200;
        k.Robots.Live.Add(Ahead(life: 255) with { Origin = new Vector(500, 150, 0) });
        Assert.Equal(0, k.Source.BarCount);              // seen whole, off the crosshair: no bar

        k.Robots.Live[0] = k.Robots.Live[0] with { Life = 200 };
        k.Frame();
        Assert.Equal(1, k.Source.BarCount);

        k.Frame(Source.RecentSeconds + 0.1f);
        Assert.Equal(0, k.Source.BarCount);
    }

    [Fact]
    public void A_fresh_immune_shows_the_bar_with_immune_and_the_type()
    {
        var k = new Kit();
        k.View.Aim = 200;
        k.Robots.Live.Add(Ahead());
        k.Known.Immune[40] = (k.Clock.Now, 0);

        var bar = k.Source.Bar(Assert.IsType<int>(k.Source.BarCount) - 1);

        Assert.True(bar.Immune);
        Assert.Equal("#Descent_Type_" + Content.DamageTypes[0].Name, bar.ImmuneToken);
        k.Frame(Source.ImmuneSeconds + 0.1f);
        Assert.Equal(0, k.Source.BarCount);
    }

    [Fact]
    public void Immune_shows_over_the_robot_that_was_immune_and_no_other()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead(entity: 40));
        k.Robots.Live.Add(Ahead(entity: 41, distance: 700));
        k.Known.Immune[41] = (k.Clock.Now, 0);

        Assert.Equal(2, k.Source.BarCount);
        Assert.Equal((false, true), (k.Source.Bar(0).Immune, k.Source.Bar(1).Immune));   // nearest first: 40, then 41
    }

    [Fact]
    public void With_no_kills_the_bar_shows_no_count_no_numbers_no_resistances()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead());

        var bar = k.Source.Bar(Assert.IsType<int>(k.Source.BarCount) - 1);

        Assert.Equal((-1, 0, ""), (bar.Kills, bar.LifeMax, bar.Style));
        Assert.Equal(0, k.Source.ResistCount(0));
    }

    [Fact]
    public void A_first_kill_shows_the_count_only()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead());
        k.Known.Kills[1] = 1;
        k.Known.ViewsHeld[40] = new ClientView(40, 1, 2, Content.RobotFingerprint, ClientReveal.Count, null, null, null, null, null, null);

        var bar = k.Source.Bar(Assert.IsType<int>(k.Source.BarCount) - 1);

        Assert.Equal((1, 0, ""), (bar.Kills, bar.LifeMax, bar.Style));
        Assert.Equal((0, 0), (k.Source.ResistCount(0), k.Source.AffixCount(0)));
    }

    [Fact]
    public void The_thirtieth_kill_shows_resistances_the_immunity_and_the_affixes()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead());
        k.Known.Kills[1] = 30;
        k.Known.ViewsHeld[40] = View30();

        var bar = k.Source.Bar(Assert.IsType<int>(k.Source.BarCount) - 1);

        Assert.Equal((30, 150, 200), (bar.Kills, bar.Life, bar.LifeMax));
        Assert.Equal(4, k.Source.ResistCount(0));
        Assert.Equal((25, -20), (k.Source.ResistPercent(0, 1), k.Source.ResistPercent(0, 3)));
        Assert.True(k.Source.ResistImmune(0, 0));
        Assert.Equal("Fireproof", k.Source.AffixName(0, 0));
    }

    [Fact]
    public void A_view_of_another_template_is_not_shown_on_this_body()
    {
        var k = new Kit();
        k.Robots.Live.Add(Ahead(template: 3));          // the body was lent again to another template
        k.Known.ViewsHeld[40] = View30();

        Assert.Equal(1, k.Source.BarCount);
        Assert.Equal((0, 0), (k.Source.Bar(0).LifeMax, k.Source.ResistCount(0)));
    }

    [Fact]
    public void Statuses_are_the_bits_of_the_bodys_state_in_table_order()
    {
        var k = new Kit();
        int stun = Content.Statuses.ToList().IndexOf("stun"), burn = Content.Statuses.ToList().IndexOf("burn");
        k.Robots.Live.Add(Ahead(statuses: (byte)((1 << stun) | (1 << burn))));

        Assert.Equal(1, k.Source.BarCount);
        Assert.Equal(2, k.Source.StatusCount(0));
        Assert.Equal(["#Descent_Status_burn", "#Descent_Status_stun"],
                     new[] { k.Source.StatusToken(0, 0), k.Source.StatusToken(0, 1) }.Order());
    }

    [Fact]
    public void At_most_six_bars_nearest_first()
    {
        var k = new Kit();
        for (int i = 0; i < 9; i++)
            k.Robots.Live.Add(Ahead(entity: 40 + i, distance: 1000 - i * 50) with { Life = 100 });
        k.Source.Bar(0);
        Assert.Equal(Source.MaxBars, k.Source.BarCount);    // the stimulus: nine under the crosshair

        // A nearer head stands higher on screen (a smaller y): nearest first is y rising, and the
        // six are the six nearest (600 to 850 units), so the farthest kept is the 850's.
        var picked = Enumerable.Range(0, Source.MaxBars).Select(i => k.Source.Bar(i).Y).ToList();
        Assert.Equal(picked.Order(), picked);
        Assert.Equal(0.5f - (Source.HullHeight + Source.OverHead - 40f) / 850f, picked[^1], 4);
    }
}
