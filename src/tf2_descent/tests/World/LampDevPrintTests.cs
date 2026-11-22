using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.World;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Lamp;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.World;

/// <summary>
/// The lamp stats in the dev prints: <c>tf2_descent_stats</c> and <c>tf2_descent_lamps</c> say each
/// hero's lamp radius and brightness, the bonus behind each, what it comes from and its bounds.
/// </summary>
public sealed class LampDevPrintTests
{
    private sealed class World : ILampWorld
    {
        public bool IsAlive(EntityHandle player) => true;
        public double Pitch(EntityHandle player) => 0;
        public int Depth => 12;
    }

    private sealed class ModeOn : IDescentModeState
    {
        public bool Active => true;
    }

    private sealed class LampGear(LampSettings lamp) : IHeroContributionSource
    {
        public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) =>
        [
            new(lamp.Range.Stat, 12, new ContributionSource(SourceKind.Item, "hat")),
            new(lamp.Range.Stat, 8, new ContributionSource(SourceKind.Skill, "lamplighter")),
            new(lamp.Brightness.Stat, 15, new ContributionSource(SourceKind.Skill, "lamplighter")),
        ];
    }

    private static readonly TF2.Descent.Rules.TF2DescentRules Rules = NewRules();

    private static LampSettings Lamp => Rules.Content.Lamp;

    [Fact]
    public void The_lamp_phrase_names_each_stat_its_bonus_its_sources_and_its_bounds()
    {
        var stats = Rules.Heroes.Stats(Soldier(Rules), [.. new LampGear(Lamp).ContributionsOf(1, Soldier(Rules))]);

        string phrase = HeroLamps.Describe(stats, Lamp);

        Assert.Contains($"lamp radius {Lamp.Light.Radius * 1.2:0} (+20 %: item hat +12, skill lamplighter +8; -50..+90)", phrase);
        Assert.Contains("brightness x1.15 (+15 %: skill lamplighter +15; -50..+100)", phrase);
    }

    [Fact]
    public void A_bare_heroes_lamp_phrase_says_nothing_raises_it()
    {
        string phrase = HeroLamps.Describe(Rules.Heroes.Stats(Soldier(Rules)), Lamp);

        Assert.Contains($"lamp radius {Lamp.Light.Radius:0} (+0 %: nothing;", phrase);
        Assert.Contains("brightness x1 (+0 %: nothing;", phrase);
    }

    [Fact]
    public void The_stats_command_prints_the_lamp_phrase()
    {
        var rig = new HeroRig();
        rig.Contributions.Add(new LampGear(rig.Rules.Content.Lamp));
        var hero = rig.Join()!;

        string line = HeroStatsCommand.Describe(HeroRig.Index, hero, hero.Stats!, null, 200, 240f, 200, 0, rig.Rules.Content.Lamp);

        Assert.Contains($"| {HeroLamps.Describe(hero.Stats!, rig.Rules.Content.Lamp)} |", line);
        Assert.Contains("skill lamplighter +15", line);
    }

    [Fact]
    public void The_lamps_command_prints_each_heroes_wire_and_lamp_phrase()
    {
        var rig = new HeroRig();
        rig.Contributions.Add(new LampGear(rig.Rules.Content.Lamp));
        var lamps = new HeroLamps(new ModeOn(), rig.Heroes, rig.Rules, new World(), rig.Log);
        rig.Join();
        lamps.OnThink(0.015f);

        string report = lamps.Report();

        Assert.Contains($"radius {(int)Math.Round(Lamp.Light.Radius * 1.2)} brightness 115 %", report);
        Assert.Contains("item hat +12", report);
    }
}
