extern alias client;

using SourceSharp.Abi;

using TF2.Descent.Client.Ui;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using ClientPlayerResource = client::SourceSharp.Client.Standalone.ClientPlayerResource;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;

using HeroState = client::TF2.Descent.Client.HeroState;
using IClientStandalones = client::SourceSharp.Client.IClientStandalones;
using IHeroStates = client::TF2.Descent.Client.IHeroStates;
using IPlayerNames = client::SourceSharp.Client.Vgui.IPlayerNames;
using SkillBarState = client::TF2.Descent.Client.Skills.SkillBarState;
using SkillBarView = client::TF2.Descent.Shared.SkillBarView;
using Src = client::TF2.Descent.Client.Ui;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The HUD's live sources (plan_descent_logic UI2) over a fake client: the
/// local hero's state, the local player's health, the player rows, the
/// skill bar. What the wire does not carry answers nothing.
/// </summary>
public sealed class HudSourcesTests
{
    private const int Local = 1;

    private sealed class Heroes(params HeroState[] heroes) : IHeroStates
    {
        public HeroState? Of(int playerIndex) => heroes.FirstOrDefault(h => h.PlayerIndex == playerIndex);

        public HeroState? Local() => Of(HudSourcesTests.Local);

        public IReadOnlyList<HeroState> All() => heroes.OrderBy(h => h.PlayerIndex).ToList();
    }

    /// <summary>The client's entities: <paramref name="all"/> live, and <paramref name="indexed"/> by entity index.</summary>
    private sealed class Standalones(ClientStandaloneEntity[] all, params (int Index, ClientStandaloneEntity Entity)[] indexed) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => all;

        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => indexed.Where(e => e.Index == entityIndex).Select(e => e.Entity).FirstOrDefault();
    }

    private sealed class Names(params (int Slot, string Name)[] names) : IPlayerNames
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            foreach (var (s, n) in names)
                if (s == slot)
                {
                    info = new ClientPlayerInfo(n, slot, false);
                    return true;
                }
            info = default;
            return false;
        }
    }

    /// <summary>A hero: a Soldier at <paramref name="level"/>, 40 XP into a 100 XP level, 175 life, 60 of 80 mana.</summary>
    private static HeroState Hero(int index = Local, int level = 3, int xp = 340, int levelXp = 300, int nextXp = 400,
                                  int lifeMax = 175, int mana = 60, int manaMax = 80, int attr = 5, int skill = 1,
                                  bool hardcore = false)
        => new(index, 3, level, xp, levelXp, nextXp, lifeMax, mana, manaMax, attr, skill, false, 0, hardcore);

    private static ClientEngine Engine(int health = 150)
        => new ClientEngine { Strict = false }.Returns("Hud.LocalPlayerIndex", Local).Returns("Hud.LocalPlayerHealth", health);

    //-------------------------------------------------------------------------
    // Vitals
    //-------------------------------------------------------------------------

    [Fact]
    public void The_orbs_read_the_local_players_health_over_the_heros_life_and_the_heros_mana()
    {
        var vitals = new Src.VitalsSource(new Heroes(Hero()), Engine(health: 150));

        Assert.True(vitals.HasHero);
        Assert.Equal((150, 175, 60, 80), (vitals.Life, vitals.LifeMax, vitals.Mana, vitals.ManaMax));
    }

    [Fact]
    public void With_no_local_hero_the_orbs_have_no_hero_and_read_zero()
    {
        var vitals = new Src.VitalsSource(new Heroes(Hero(index: 2)), Engine(health: 150));

        Assert.False(vitals.HasHero);
        Assert.Equal((0, 0, 0, 0), (vitals.Life, vitals.LifeMax, vitals.Mana, vitals.ManaMax));
    }

    [Fact]
    public void A_hero_at_no_mana_reads_zero_mana_of_his_maximum()
    {
        var vitals = new Src.VitalsSource(new Heroes(Hero(mana: 0)), Engine());

        Assert.Equal((0, 80), (vitals.Mana, vitals.ManaMax));
    }

    [Fact]
    public void A_dead_heros_negative_health_reads_zero_life()
    {
        var vitals = new Src.VitalsSource(new Heroes(Hero()), Engine(health: -12));

        Assert.Equal(0, vitals.Life);
    }

    //-------------------------------------------------------------------------
    // XP
    //-------------------------------------------------------------------------

    [Fact]
    public void The_xp_bar_reads_the_experience_inside_the_level_and_the_levels_span()
    {
        var xp = new Src.XpSource(new Heroes(Hero(level: 3, xp: 340, levelXp: 300, nextXp: 400, attr: 5, skill: 1)));

        Assert.Equal((3, 40L, 100L, 5, 1), (xp.Level, xp.Xp, xp.XpToNext, xp.AttributePoints, xp.SkillPoints));
    }

    [Fact]
    public void At_the_level_cap_the_span_is_zero_so_the_bar_reads_full()
    {
        // The cap: the next level's experience is the level's own (HeroNetState.NextLevelXp).
        var hero = Hero(level: 30, xp: 900_000, levelXp: 850_000, nextXp: 850_000);
        var xp = new Src.XpSource(new Heroes(hero));

        Assert.Equal(0L, xp.XpToNext);
        Assert.Equal(1.0, hero.LevelProgress);   // the bar's fill (XpBar: no span reads full)
    }

    [Fact]
    public void With_no_local_hero_the_xp_bar_is_level_zero()
    {
        var xp = new Src.XpSource(new Heroes());

        Assert.Equal((0, 0L, 0L, 0, 0), (xp.Level, xp.Xp, xp.XpToNext, xp.AttributePoints, xp.SkillPoints));
    }

    //-------------------------------------------------------------------------
    // Party
    //-------------------------------------------------------------------------

    private static ClientPlayerResource Rows(params (int Slot, int Health)[] rows)
    {
        var resource = new ClientPlayerResource(new ClientEngine());
        resource.State.BindLocal();
        foreach (var (slot, health) in rows)
            resource.State.Health[slot] = (short)health;
        return resource;
    }

    [Fact]
    public void A_party_of_one_is_the_local_hero_with_his_own_health()
    {
        var party = new Src.PartySource(new Heroes(Hero(hardcore: true)), Engine(health: 120), new Names((Local, "Bugha")),
                                       new Standalones([Rows((Local, 99))]), new ClientEngine { Strict = false });

        Assert.Equal(1, party.MemberCount);
        var m = party.Member(0);
        Assert.Equal(("Bugha", "#Descent_Class_Soldier", 3), (m.Name, m.ClassToken, m.Level));
        Assert.Equal((120, 175), (m.Life, m.LifeMax));      // the engine's health, not the row's 99
        Assert.Equal((true, true, false, 0, false), (m.Local, m.Hardcore, m.Downed, m.DistanceUnits, m.OffLevel));
    }

    [Fact]
    public void A_party_of_two_reads_the_other_heros_health_from_his_player_row_and_his_distance()
    {
        const int other = 4;
        var player = new client::TF2.Descent.Client.ClientHeroEntity(new ClientEngine());   // the client entity at index 4
        var entities = new ClientEngine { Strict = false }.Returns("Entities.GetOrigin", new Vector(300, 400, 0));
        var hud = Engine(health: 120).Returns("Hud.LocalPlayerOrigin", (Vector?)new Vector(0, 0, 0));
        var party = new Src.PartySource(new Heroes(Hero(), Hero(index: other, level: 7, lifeMax: 210)), hud,
                                       new Names((Local, "Bugha"), (other, "Merasmus")),
                                       new Standalones([Rows((Local, 99), (other, 88))], (other, player)), entities);

        Assert.Equal(2, party.MemberCount);
        var (first, second) = (party.Member(0), party.Member(1));
        Assert.Equal(("Bugha", true, 120), (first.Name, first.Local, first.Life));
        Assert.Equal(("Merasmus", false, 7), (second.Name, second.Local, second.Level));
        Assert.Equal((88, 210), (second.Life, second.LifeMax));   // the player row's health over his sheet's maximum
        Assert.Equal(500, second.DistanceUnits);                  // 3-4-5
    }

    //-------------------------------------------------------------------------
    // Cast bar
    //-------------------------------------------------------------------------

    private sealed class Ticks(int now) : Src.IClientTicks
    {
        public int Now { get; set; } = now;
    }

    private sealed class Lengths(int ticks) : Src.ISkillChargeLengths
    {
        public int TicksOf(string skillId) => skillId == "patriot_missile" ? ticks : 0;
    }

    private static SkillBarState Bar(int chargeSlot, int chargeStart)
    {
        var bar = new SkillBarState();
        bar.OnSkillBar(new SkillBarView(["", "patriot_missile", "", "", "", ""], [0, 0, 0, 0, 0, 0], chargeSlot, chargeStart, 0));
        return bar;
    }

    [Theory]
    [InlineData(1000, 0f)]      // the charge's first tick
    [InlineData(1033, 0.5f)]    // half of 66 ticks
    [InlineData(1066, 1f)]      // the charge's length
    [InlineData(1500, 1f)]      // held past it: still full
    public void A_charging_slot_fills_the_cast_bar_by_ticks_held_over_the_charges_length(int now, float fraction)
    {
        var cast = new Src.CastBarSource(Bar(chargeSlot: 2, chargeStart: 1000), new Lengths(66), new Ticks(now));

        Assert.True(cast.Casting);
        Assert.Equal("patriot_missile", cast.SpellName);
        Assert.Equal(fraction, cast.Fraction, 0.001f);
        Assert.False(cast.Interrupted);
    }

    [Fact]
    public void With_nothing_charging_there_is_no_cast()
    {
        var cast = new Src.CastBarSource(Bar(chargeSlot: 0, chargeStart: 0), new Lengths(66), new Ticks(1033));

        Assert.False(cast.Casting);
        Assert.Equal(("", 0f), (cast.SpellName, cast.Fraction));
    }

    [Fact]
    public void Before_any_skill_bar_there_is_no_cast()
    {
        var cast = new Src.CastBarSource(new SkillBarState(), new Lengths(66), new Ticks(1033));

        Assert.False(cast.Casting);
    }

    [Fact]
    public void An_unknown_charge_length_shows_the_skill_with_an_empty_meter()
    {
        var cast = new Src.CastBarSource(Bar(chargeSlot: 2, chargeStart: 1000), new Src.UnknownChargeLengths(), new Ticks(1033));

        Assert.True(cast.Casting);
        Assert.Equal(0f, cast.Fraction);
    }

    //-------------------------------------------------------------------------
    // What the wire does not carry yet
    //-------------------------------------------------------------------------

    [Fact]
    public void The_belt_is_four_empty_slots_ready()
    {
        var belt = new Src.BeltSource();

        Assert.Equal(4, belt.SlotCount);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(("", 0), (belt.SlotName(i), belt.SlotCountOf(i))));
        Assert.True(belt.SlotReady);
    }

    [Fact]
    public void No_boss_no_gear_warning_and_no_map_are_shown()
    {
        Assert.False(new Src.BossSource().Present);
        Assert.False(new Src.GearWarningSource().Present);
        Assert.Equal(0, new Src.AutomapSource().RoomCount);
        Assert.False(new Src.DownedSource().Downed);
    }

    [Fact]
    public void The_hero_and_town_windows_have_nothing_to_show()
    {
        Assert.Equal((0, 0), (new Src.SheetSource().AttributeCount, new Src.SheetSource().Level));
        Assert.Equal(0, new Src.EquipmentSource().SlotCount);
        Assert.Equal((0, 0), (new Src.BackpackSource().CellCount, new Src.BackpackSource().Capacity));
        Assert.Equal(0, new Src.SkillsSource().TabCount);
        Assert.Equal(0, new Src.SpellsSource().SpellCount);
        Assert.Equal((0, 0), (new Src.VendorSource().BuyCount, new Src.VendorSource().SellCount));
        Assert.Equal(0, new Src.StashSource().CellCount);
        Assert.Equal(0, new Src.DepthsSource().RowCount);
        Assert.Equal(0, new Src.CrateSource().OddsRowCount);
        Assert.Null(new Src.CraftSource().ItemUnderTest);
        Assert.False(new Src.InspectSource().Showing);
        Assert.False(new Src.ItemHoverSource().Hovering);
        Assert.False(new Src.HintTipSource().Active);
        Assert.Equal(0L, new Src.PurseSource().Australium);
    }
}
