using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// HE3, sustain: life from damage dealt (counted from what the victim took,
/// no more than the life it had, under the rules' cap), mana from damage
/// dealt, and life regenerated over time, never above TF's maximum.
/// </summary>
public sealed class HeroSustainTests
{
    private static readonly EntityHandle Victim = new(0x0003_0200u);

    /// <summary>A hurt hero (100 of 200) with <paramref name="leechPct"/> % life leech; the victim has <paramref name="victimLife"/>.</summary>
    private static HeroStatsRig Leecher(double leechPct, int victimLife = 1000)
    {
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "life_leech_pct", leechPct);
        rig.Hero.Recompute();
        rig.Health[rig.Player] = 100;
        rig.Health[Victim] = victimLife;
        return rig;
    }

    private static int HealthSet(HeroStatsRig rig) =>
        rig.Rig.Engine.Calls.Where(c => c.Member == "Entities.SetHealth" && c.ArgAs<EntityHandle>(0) == rig.Player)
           .Select(c => c.ArgAs<int>(1)).DefaultIfEmpty(-1).Last();

    [Fact]
    public void A_hit_heals_the_hero_by_leech_times_the_damage_the_victim_took()
    {
        var rig = Leecher(10);

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 50, 950));

        Assert.Equal(105, HealthSet(rig));    // 10 % of 50
    }

    [Fact]
    public void A_hit_on_a_player_heals_as_a_hit_on_an_entity()
    {
        var rig = Leecher(10);
        var player = new EntityHandle(0x0001_0005u);
        rig.Health[player] = 1000;

        rig.Leech.OnPlayerHurt(new PlayerHurt(player, rig.Player, 50, 950));

        Assert.Equal(105, HealthSet(rig));
    }

    [Fact]
    public void Leech_stops_at_the_rules_cap()
    {
        var rig = Leecher(40);
        Assert.Equal(0.15, rig.Hero.Stats!.LifeLeech, 9);

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 100, 900));

        Assert.Equal(115, HealthSet(rig));    // 15 %, not 40 %
    }

    [Fact]
    public void Overkill_heals_only_by_the_life_the_victim_had()
    {
        var rig = Leecher(10, victimLife: 20);

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 100, 0));

        Assert.Equal(102, HealthSet(rig));    // 10 % of 20
    }

    [Fact]
    public void A_hit_the_victim_took_nothing_of_heals_nothing()
    {
        var rig = Leecher(10);

        rig.Leech.OnPlayerHurt(new PlayerHurt(new EntityHandle(0x0001_0005u), rig.Player, 0, 1000));
        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 0, 1000));

        Assert.Equal(-1, HealthSet(rig));
    }

    [Fact]
    public void A_heros_own_blast_heals_nothing()
    {
        var rig = Leecher(10);

        rig.Leech.OnPlayerHurt(new PlayerHurt(rig.Player, rig.Player, 50, 50));

        Assert.Equal(-1, HealthSet(rig));
    }

    [Fact]
    public void Leech_never_heals_above_TFs_maximum()
    {
        var rig = Leecher(15);
        rig.Health[rig.Player] = 198;

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 100, 900));

        Assert.Equal(rig.Rig.Health.Max, HealthSet(rig));
    }

    [Fact]
    public void A_fraction_is_kept_until_it_makes_a_point()
    {
        var rig = Leecher(10);

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 5, 995));    // 0.5: nothing yet
        Assert.Equal(-1, HealthSet(rig));

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 5, 990));    // 1.0
        Assert.Equal(101, HealthSet(rig));
    }

    [Fact]
    public void A_hit_returns_mana_by_mana_leech()
    {
        var rig = Leecher(0);
        rig.Dev.Add(HeroRig.Index, "mana_leech_pct", 4);
        rig.Hero.Recompute();
        rig.Hero.SetMana(0);

        rig.Leech.OnEntityHurt(new EntityHurt(Victim, rig.Player, 100, 900));

        Assert.Equal(4.0, rig.Hero.Mana, 6);
    }

    //-------------------------------------------------------------------------
    // Regeneration
    //-------------------------------------------------------------------------

    private static HeroRegen Regen(HeroStatsRig rig)
    {
        var regen = new HeroRegen(rig.Rig.Heroes, rig.Life);
        regen.Attach(rig.Player);
        regen.Tick(10f);
        return regen;
    }

    [Fact]
    public void A_hero_regenerates_the_rules_life_a_second()
    {
        // 2 % of 267 a second: 5.34.
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "life_regen_pct", 2);
        rig.Hero.Recompute();
        rig.Health[rig.Player] = 100;
        var regen = Regen(rig);

        regen.Tick(11f);

        Assert.Equal(5.34, rig.Hero.Stats!.LifeRegenPerSecond, 6);
        Assert.Equal(105, HealthSet(rig));
    }

    [Fact]
    public void Regeneration_never_rises_above_TFs_maximum()
    {
        var rig = new HeroStatsRig();
        rig.Dev.Add(HeroRig.Index, "life_regen_pct", 3);
        rig.Hero.Recompute();
        rig.Health[rig.Player] = rig.Rig.Health.Max - 1;
        var regen = Regen(rig);

        regen.Tick(12f);

        Assert.Equal(rig.Rig.Health.Max, HealthSet(rig));
    }

    [Fact]
    public void A_hero_with_no_regeneration_regains_nothing()
    {
        var rig = new HeroStatsRig();
        rig.Health[rig.Player] = 100;
        var regen = Regen(rig);

        regen.Tick(20f);

        Assert.Equal(-1, HealthSet(rig));
    }
}
