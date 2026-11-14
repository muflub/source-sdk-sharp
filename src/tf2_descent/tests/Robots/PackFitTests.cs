using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// The pack roll with a fit (plan_tf2_rpg 5d; plan_descent_logic HO9): a robot stands at the
/// first marker from its turn that holds it at its scale; one no marker holds is drawn again,
/// then left out; with everything fitting the pack is the roll's of before; the half-immune cap
/// is over the robots kept.
/// </summary>
public sealed class PackFitTests
{
    private static readonly TF2DescentRules Rules = new();
    private static PackRules Packs => Rules.Packs;

    private sealed class Fit(Func<RobotTemplate, double, int, bool> fits) : IPackFit
    {
        public int Asked { get; private set; }

        public bool Fits(RobotTemplate template, double scale, int marker)
        {
            Asked++;
            return fits(template, scale, marker);
        }
    }

    private static string Describe(RolledRobot r) =>
        $"{r.Index}/{r.Template}/{r.Scale:R}/{string.Join("+", r.Affixes)}/{string.Join(",", r.Temperament.Levels)}/{r.Stats.Life:R}/{r.Marker}";

    [Fact]
    public void A_fit_that_holds_everything_rolls_the_pack_of_no_fit()
    {
        var all = new Fit((_, _, _) => true);
        for (ulong seed = 0; seed < 400; seed++)
        {
            int depth = 1 + (int)(seed % 15), party = 1 + (int)(seed / 15 % 4);
            var plain = Packs.RollPack(seed, 3, depth, party, 3);
            var fitted = Packs.RollPack(seed, 3, depth, party, 3, all);
            Assert.Equal(plain.Robots.Select(Describe), fitted.Robots.Select(Describe));
            Assert.Equal((0, 0), (fitted.Refits, fitted.Unplaced));
        }
        Assert.True(all.Asked > 1000);
    }

    [Fact]
    public void A_robot_whose_turn_does_not_hold_it_stands_at_the_next_marker_that_does()
    {
        // Marker 0 holds nothing: every robot stands at marker 1 or 2, in turn from its own.
        var fit = new Fit((_, _, m) => m != 0);
        var pack = Packs.RollPack(5, 1, 15, 4, 3, fit);

        Assert.True(pack.Robots.Length > 3);
        Assert.All(pack.Robots, r => Assert.Equal(r.Index % 3 == 0 ? 1 : r.Index % 3, r.Marker));
        Assert.Equal(0, pack.Unplaced);
    }

    [Fact]
    public void A_robot_no_marker_holds_at_its_scale_is_drawn_again()
    {
        // Nothing past scale 1.2 fits: depth 15's giants are drawn again as smaller robots.
        var fit = new Fit((_, s, _) => s <= 1.2);
        int refits = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            var plain = Packs.RollPack(seed, 0, 15, 4, 3);
            var pack = Packs.RollPack(seed, 0, 15, 4, 3, fit);
            Assert.All(pack.Robots, r => Assert.True(r.Scale <= 1.2, $"seed {seed}: {r.Template} at {r.Scale}"));
            Assert.Equal(plain.Robots.Length, pack.RolledSize);
            refits += pack.Refits;
        }
        Assert.True(refits > 50);   // the stimulus: giants were rolled and drawn again
    }

    [Fact]
    public void A_robot_that_fits_nothing_is_left_out_and_counted()
    {
        var pack = Packs.RollPack(9, 2, 6, 3, 3, new Fit((_, _, _) => false));

        Assert.Empty(pack.Robots);
        Assert.True(pack.Unplaced > 0);
        Assert.Equal(Packs.RollPack(9, 2, 6, 3, 3).Robots.Length, pack.Unplaced);
    }

    [Fact]
    public void A_robot_left_out_moves_no_other_robots_champion_roll()
    {
        // Robot 0 never fits (nor anything drawn again for it); every other robot keeps its
        // template, scale, and whether it is a champion and with how many affixes.
        int champions = 0;
        for (ulong seed = 0; seed < 3000; seed++)
        {
            var plain = Packs.RollPack(seed, 0, 12, 1, 1);
            var first = plain.Robots[0];
            var pack = Packs.RollPack(seed, 0, 12, 1, 1, new Fit((t, s, _) => !(t.Id == first.Template && s == first.Scale)));
            foreach (var r in pack.Robots.Where(r => r.Index > 0))
            {
                var before = plain.Robots[r.Index];
                Assert.Equal((before.Template, before.Scale, before.Champion, before.Affixes.Length), (r.Template, r.Scale, r.Champion, r.Affixes.Length));
                if (r.Champion) champions++;
            }
        }
        Assert.True(champions > 20);
    }

    [Fact]
    public void The_half_immune_cap_is_over_the_robots_kept()
    {
        // Planted: every robot a champion whose only affixes are Fireproof and Extra Fast;
        // a robot fits one draw in eight, so robots are left out and the cap must follow the smaller pack.
        var content = PackTests.Planted(
            (ContentTables.Levels, n => { foreach (var d in n.AsArray()) d!["championPct"] = 100; }),
            (ContentTables.Champions, n =>
            {
                var keep = n.AsArray().Where(a => (string?)a!["id"] is "fireproof" or "extra_fast").Select(a => a!.DeepClone()).ToArray();
                n.AsArray().Clear();
                foreach (var a in keep) n.AsArray().Add(a);
            }));
        var robots = new RobotRules(content);
        var packs = new PackRules(content, robots, new TemperamentRules(content, robots));
        var fit = new Fit((_, s, _) => s * 1000 % 8 < 1);

        int immune = 0, short_ = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            var pack = packs.RollPack(seed, (int)(seed % 7), 12, 4, 3, fit);
            if (pack.Unplaced > 0) short_++;
            int fire = pack.Robots.Count(r => r.Stats.Immunity == "fire");
            immune += fire;
            Assert.True(fire * 2 <= pack.Robots.Length, $"seed {seed}: {fire} of {pack.Robots.Length} fire-immune");
        }
        Assert.True(immune > 500, $"immune {immune}, short {short_}");
        Assert.True(short_ > 500, $"immune {immune}, short {short_}");
    }
}
