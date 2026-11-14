using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The hero in the engine (plan_descent_logic HE2), unhosted: a hero's sheet,
/// its stats and mana, its one networked entity, and what other server code
/// may ask of it.
/// </summary>
public sealed class HeroComponentTests
{
    private static HeroComponent Joined(HeroRig rig) =>
        rig.Join() ?? throw new InvalidOperationException("no hero joined");

    //-------------------------------------------------------------------------
    // The sheet, unhosted
    //-------------------------------------------------------------------------

    [Fact]
    public void An_unhosted_hero_is_a_new_level_1_soldier()
    {
        var rig = new HeroRig();

        var hero = Joined(rig);

        Assert.Equal("soldier", hero.Sheet!.ClassId);
        Assert.Equal(1, hero.Sheet.Level);
        Assert.True(rig.Rules.Heroes.Audit(hero.Sheet).Ok);
        Assert.False(hero.Sheet.Hardcore);
    }

    [Fact]
    public void An_unhosted_sheet_is_the_same_after_a_level_change()
    {
        var rig = new HeroRig();
        var first = Joined(rig);
        Assert.True(first.Apply(s => rig.AtLevel(s, 4)).Ok);
        var before = first.Sheet;

        rig.LevelShutdown();
        var again = Joined(rig);

        Assert.NotSame(first, again);
        Assert.Same(before, again.Sheet);
        Assert.Equal(4, again.Sheet!.Level);
    }

    [Fact]
    public void An_unhosted_sheet_is_the_same_after_a_respawn()
    {
        // A respawn keeps the client in the game: the composite keeps the hero,
        // and the hero reads its sheet from the store every tick.
        var rig = new HeroRig();
        var hero = Joined(rig);
        Assert.True(hero.Apply(s => rig.AtLevel(s, 3)).Ok);
        var before = hero.Sheet;

        hero.Tick(1f);
        rig.Engine.Returns("Entities.GetHealth", 200);    // the respawn's full health
        hero.Tick(2f);

        Assert.Same(before, rig.Heroes.Of(HeroRig.Index)!.Sheet);
    }

    [Fact]
    public void An_unhosted_sheet_outlives_a_reconnect_keyed_by_steamid()
    {
        var rig = new HeroRig();
        Assert.True(Joined(rig).Apply(s => rig.AtLevel(s, 5)).Ok);

        rig.Leave();
        rig.Players.Steam[7] = HeroRig.SteamId;    // the same player, back in another slot
        var back = rig.Join(7)!;

        Assert.Equal(5, back.Sheet!.Level);
    }

    [Fact]
    public void Two_unhosted_players_are_two_heroes()
    {
        var rig = new HeroRig();
        rig.Players.Steam[4] = HeroRig.SteamId + 1;
        Assert.True(Joined(rig).Apply(s => rig.AtLevel(s, 2)).Ok);

        var other = rig.Join(4)!;

        Assert.Equal(1, other.Sheet!.Level);
    }

    //-------------------------------------------------------------------------
    // The entity: one a hero
    //-------------------------------------------------------------------------

    [Fact]
    public void The_hero_entity_is_made_at_the_attach_and_removed_at_the_detach()
    {
        var rig = new HeroRig();

        var hero = Joined(rig);
        Assert.Equal(1, rig.Created);
        Assert.Equal(0, rig.Removed);
        Assert.Equal(HeroRig.EntityOf(1), hero.Entity);

        rig.Leave();
        Assert.Equal(1, rig.Created);
        Assert.Equal(1, rig.Removed);
        Assert.Null(rig.Heroes.Of(HeroRig.Index));
    }

    [Fact]
    public void Each_hero_has_exactly_one_entity_and_a_level_change_makes_it_again()
    {
        var rig = new HeroRig();
        rig.Players.Steam[4] = HeroRig.SteamId + 1;

        rig.Join();
        rig.Join(4);
        Assert.Equal(2, rig.Created);

        rig.LevelShutdown();
        Assert.Equal(2, rig.Removed);

        rig.Join();
        Assert.Equal(3, rig.Created);
    }

    [Fact]
    public void A_bot_is_no_hero_and_has_no_entity()
    {
        var rig = new HeroRig();
        rig.Bots.Add(HeroRig.PlayerOf(HeroRig.Index));

        Assert.Null(rig.Join());
        Assert.Equal(0, rig.Created);
    }

    [Fact]
    public void Without_the_Descent_mode_nobody_is_a_hero()
    {
        var rig = new HeroRig { ModeActive = false };

        Assert.Null(rig.Join());
        Assert.Equal(0, rig.Created);
    }

    //-------------------------------------------------------------------------
    // Mana
    //-------------------------------------------------------------------------

    [Fact]
    public void A_hero_joins_with_full_mana()
    {
        var hero = Joined(new HeroRig());

        Assert.True(hero.Stats!.MaxMana > 0);
        Assert.Equal(hero.Stats.MaxMana, hero.Mana);
    }

    [Fact]
    public void The_wire_shows_full_mana_from_the_attach_before_any_tick()
    {
        var hero = Joined(new HeroRig());

        Assert.True(hero.State!.ManaMax > 0);
        Assert.Equal(hero.State.ManaMax, hero.State.Mana);
    }

    [Fact]
    public void Mana_regenerates_by_the_rules_rate()
    {
        var hero = Joined(new HeroRig());
        hero.SetMana(0);
        double rate = hero.Stats!.ManaRegenPerSecond;
        Assert.True(rate > 0);

        hero.Tick(10f);
        hero.Tick(12f);

        Assert.Equal(rate * 2, hero.Mana, 6);
    }

    [Fact]
    public void Mana_regeneration_stops_at_the_maximum()
    {
        var hero = Joined(new HeroRig());
        hero.SetMana(1);

        hero.Tick(0f);
        hero.Tick(100_000f);

        Assert.Equal(hero.Stats!.MaxMana, hero.Mana);
    }

    [Fact]
    public void Spending_more_mana_than_the_hero_has_is_refused_and_spends_nothing()
    {
        var hero = Joined(new HeroRig());
        hero.SetMana(5);

        Assert.False(hero.TrySpendMana(5.5));
        Assert.Equal(5, hero.Mana);
    }

    [Fact]
    public void Spending_mana_the_hero_has_takes_it()
    {
        var hero = Joined(new HeroRig());
        hero.SetMana(5);

        Assert.True(hero.TrySpendMana(5));
        Assert.Equal(0, hero.Mana);
    }

    [Fact]
    public void A_refill_fills_life_to_TF_max_health_and_mana_to_its_maximum()
    {
        var rig = new HeroRig();
        var hero = Joined(rig);
        hero.SetMana(0);
        rig.Engine.Returns("Entities.GetHealth", 1);
        rig.Health.Max = 237;

        hero.RefillLifeAndMana();

        var set = Assert.Single(rig.Engine.Calls, c => c.Member == "Entities.SetHealth");
        Assert.Equal(HeroRig.PlayerOf(HeroRig.Index), set.ArgAs<EntityHandle>(0));
        Assert.Equal(237, set.ArgAs<int>(1));
        Assert.Equal(hero.Stats!.MaxMana, hero.Mana);
    }

    [Fact]
    public void A_refill_keeps_an_overheal()
    {
        var rig = new HeroRig();
        var hero = Joined(rig);
        rig.Engine.Returns("Entities.GetHealth", 300);
        rig.Health.Max = 200;

        hero.RefillLifeAndMana();

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Entities.SetHealth");
    }

    //-------------------------------------------------------------------------
    // Changing the sheet
    //-------------------------------------------------------------------------

    [Fact]
    public void A_change_that_breaks_the_audit_is_refused_and_logged()
    {
        var rig = new HeroRig();
        var hero = Joined(rig);
        var before = hero.Sheet;

        var result = hero.Apply(s => s with { AttributePoints = s.AttributePoints + 1 });

        Assert.False(result.Ok);
        Assert.Equal("attribute_points", result.Rule);
        Assert.Same(before, hero.Sheet);
        Assert.Same(before, rig.Sheets.Of(HeroRig.Index));
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("refused: attribute_points", StringComparison.Ordinal));
    }

    [Fact]
    public void A_change_the_audit_accepts_is_the_heros_sheet_and_its_stats_follow()
    {
        var rig = new HeroRig();
        var hero = Joined(rig);
        double lifeBefore = hero.Stats!.MaxLife;

        var result = hero.Apply(s => rig.AtLevel(s, 6));

        Assert.True(result.Ok);
        Assert.Equal(6, hero.Sheet!.Level);
        Assert.Equal(rig.Rules.Heroes.Stats(hero.Sheet).MaxLife, hero.Stats!.MaxLife);
        Assert.True(hero.Stats.MaxLife > lifeBefore);
    }

    [Fact]
    public void A_contribution_counts_in_the_stats()
    {
        var rig = new HeroRig();
        var hero = Joined(rig);
        double before = hero.Stats!.MaxMana;

        rig.Contributions.Add(new Grant(new Contribution("max_mana", 10, new ContributionSource(SourceKind.Item, "test"))));
        hero.Recompute();

        Assert.Equal(before + 10, hero.Stats!.MaxMana, 6);
    }

    private sealed class Grant(params Contribution[] contributions) : IHeroContributionSource
    {
        public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) => contributions;
    }

    //-------------------------------------------------------------------------
    // The wire
    //-------------------------------------------------------------------------

    [Fact]
    public void The_net_state_shows_the_sheets_level_xp_and_points_and_the_mana()
    {
        var rig = new HeroRig();
        var hero = Joined(rig);
        Assert.True(hero.Apply(s => rig.AtLevel(s, 3)).Ok);
        Assert.True(hero.Apply(s => rig.Rules.Heroes.Spend(s, "grit", 2).Sheet).Ok);
        hero.SetMana(7.9);

        var state = hero.State!;
        var sheet = hero.Sheet!;
        Assert.Equal(HeroRig.Index, state.OwnerIndex);
        Assert.Equal((byte)TFClass.Soldier, state.ClassId);
        Assert.Equal(3, state.Level);
        Assert.Equal(sheet.Xp, state.Xp);
        Assert.Equal(rig.Rules.Heroes.XpAtLevel(3), state.LevelXp);
        Assert.Equal(rig.Rules.Heroes.XpAtLevel(4), state.NextLevelXp);
        Assert.Equal(sheet.AttributePoints, state.AttributePoints);
        Assert.Equal(8, state.AttributePoints);
        Assert.Equal(2, state.SkillPoints);
        Assert.Equal(7, state.Mana);
        Assert.Equal((int)Math.Floor(hero.Stats!.MaxMana), state.ManaMax);
        Assert.Equal((int)Math.Floor(hero.Stats.MaxLife), state.LifeMax);
        Assert.False(state.Downed);
        Assert.Equal(0, state.DownedEndTick);
        Assert.False(state.Hardcore);
    }

    [Fact]
    public void The_hero_state_is_32_bytes_after_the_engine_block()
    {
        // 488 bytes of engine block, then four ints and four packed words
        // (measured live: tools/debug-api payload, layoutBytes 520).
        Assert.Equal(520, HeroNetState.NetLayout.Size);
    }

    [Fact]
    public void Mana_on_the_wire_is_the_whole_number_rounded_down()
    {
        Assert.Equal(0, HeroNetState.Whole(0.99));
        Assert.Equal(7, HeroNetState.Whole(7.999));
        Assert.Equal(8, HeroNetState.Whole(8));
        Assert.Equal(0, HeroNetState.Whole(-3));
        Assert.Equal(0, HeroNetState.Whole(double.NaN));
        Assert.Equal(ushort.MaxValue, HeroNetState.Whole(1e9));
    }

    [Fact]
    public void Regenerating_mana_leaves_the_entity_unchanged_until_the_shown_number_changes()
    {
        var hero = Joined(new HeroRig());
        hero.SetMana(3);
        double rate = hero.Stats!.ManaRegenPerSecond;    // under one mana a second at level 1
        Assert.InRange(rate, 0.01, 0.99);

        hero.Tick(0f);
        var before = hero.State!.Snapshot();
        hero.Tick(0.5f);                                  // 3 + rate/2: still 3 on the wire
        var during = hero.State.Snapshot();
        hero.Tick(1f / (float)rate + 0.1f);               // past 4

        Assert.True(hero.Mana > 3 && hero.Mana < 4.5);
        Assert.Equal(before, during);
        Assert.Equal(4, hero.State.Mana);
        Assert.NotEqual(before, hero.State.Snapshot());
    }

    [Fact]
    public void Full_mana_shows_as_the_maximum_on_the_wire()
    {
        var hero = Joined(new HeroRig());

        hero.Tick(0f);
        hero.Tick(1_000f);

        Assert.Equal(hero.State!.ManaMax, hero.State.Mana);
    }
}
