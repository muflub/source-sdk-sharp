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
using BeltState = client::TF2.Descent.Client.Belt.BeltState;
using GearWearState = client::TF2.Descent.Client.Belt.GearWearState;
using BeltView = client::TF2.Descent.Shared.BeltView;
using GearWearView = client::TF2.Descent.Shared.GearWearView;
using GearWearLevel = client::TF2.Descent.Shared.GearWearLevel;

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
                                       new Standalones([Rows((Local, 99))]), new ClientEngine { Strict = false }, ClientParties.Solo());

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
                                       new Standalones([Rows((Local, 99), (other, 88))], (other, player)), entities,
                                       ClientParties.Of(Local, (Local, "Bugha"), (other, "Merasmus")));

        Assert.Equal(2, party.MemberCount);
        var (first, second) = (party.Member(0), party.Member(1));
        Assert.Equal(("Bugha", true, 120), (first.Name, first.Local, first.Life));
        Assert.Equal(("Merasmus", false, 7), (second.Name, second.Local, second.Level));
        Assert.Equal((88, 210), (second.Life, second.LifeMax));   // the player row's health over his sheet's maximum
        Assert.Equal(500, second.DistanceUnits);                  // 3-4-5
    }

    [Fact]
    public void A_hero_on_the_server_who_is_not_in_the_party_has_no_frame()
    {
        const int stranger = 4, mate = 5;
        var party = new Src.PartySource(new Heroes(Hero(), Hero(index: stranger), Hero(index: mate, level: 9)), Engine(health: 120),
                                       new Names((Local, "Bugha"), (stranger, "Stranger"), (mate, "Mate")),
                                       new Standalones([Rows((Local, 99), (stranger, 50), (mate, 60))]), new ClientEngine { Strict = false },
                                       ClientParties.Of(Local, (Local, "Bugha"), (mate, "Mate")));

        Assert.Equal(2, party.MemberCount);
        Assert.Equal(["Bugha", "Mate"], new[] { party.Member(0).Name, party.Member(1).Name });
        Assert.Equal((9, 60), (party.Member(1).Level, party.Member(1).Life));
    }

    [Fact]
    public void Solo_among_other_heroes_the_frames_are_the_local_hero_alone()
    {
        var party = new Src.PartySource(new Heroes(Hero(), Hero(index: 4)), Engine(health: 120), new Names((Local, "Bugha"), (4, "Other")),
                                       new Standalones([Rows((Local, 99), (4, 50))]), new ClientEngine { Strict = false }, ClientParties.Solo());

        Assert.Equal(1, party.MemberCount);
        Assert.True(party.Member(0).Local);
    }

    [Fact]
    public void A_member_not_on_this_server_is_off_level_with_the_name_and_level_the_server_sent()
    {
        var party = new Src.PartySource(new Heroes(Hero()), Engine(health: 120), new Names((Local, "Bugha")),
                                       new Standalones([Rows((Local, 99))]), new ClientEngine { Strict = false },
                                       ClientParties.Of(Local, (Local, "Bugha"), (0, "InTown")));

        Assert.Equal(2, party.MemberCount);
        var away = party.Member(1);
        Assert.Equal(("InTown", "#Descent_Class_Soldier", 5, true), (away.Name, away.ClassToken, away.Level, away.OffLevel));
        Assert.Equal((0, false, false), (away.Life, away.Local, away.Downed));
        Assert.False(party.Member(0).OffLevel);
    }

    private sealed class Minimap : client::SourceSharp.Client.Vgui.IMinimapWorld
    {
        public Vector LocalOrigin => default;
        public float LocalYaw => 0f;
        public void OtherPlayers(List<Vector> into) => into.Clear();
        public bool OnScreen(Vector origin) => false;
    }

    [Fact]
    public void The_automaps_party_marks_are_the_party_members_only()
    {
        const int stranger = 4, mate = 5;
        var strangerBody = new client::TF2.Descent.Client.ClientHeroEntity(new ClientEngine());
        var mateBody = new client::TF2.Descent.Client.ClientHeroEntity(new ClientEngine());
        // Each origin asked is a new place: a stranger's would be a second mark.
        int asked = 0;
        var entities = new ClientEngine { Strict = false };
        entities.ReturnsEachCall("Entities.GetOrigin", () => new Vector(50 + 100 * asked++, 60, 0));
        var world = new Src.EngineAutomapWorld(Engine(), new Minimap(), new Heroes(Hero(), Hero(index: stranger), Hero(index: mate)),
                                               new Standalones([], (stranger, strangerBody), (mate, mateBody)), entities,
                                               ClientParties.Of(Local, (Local, "Bugha"), (mate, "Mate")));
        var marks = new List<Vector>();

        world.PartyPositions(marks);

        Assert.Equal([new Vector(50, 60, 0)], marks);
    }

    //-------------------------------------------------------------------------
    // Cast bar
    //-------------------------------------------------------------------------

    private static readonly client::TF2.Descent.Client.Content.DescentCatalog Catalog = new();

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
        var cast = new Src.CastBarSource(Bar(chargeSlot: 2, chargeStart: 1000), new Lengths(66), new Ticks(now), Catalog);

        Assert.True(cast.Casting);
        Assert.Equal(fraction, cast.Fraction, 0.001f);
        Assert.False(cast.Interrupted);
    }

    [Fact]
    public void With_nothing_charging_there_is_no_cast()
    {
        var cast = new Src.CastBarSource(Bar(chargeSlot: 0, chargeStart: 0), new Lengths(66), new Ticks(1033), Catalog);

        Assert.False(cast.Casting);
        Assert.Equal(("", 0f), (cast.SpellName, cast.Fraction));
    }

    [Fact]
    public void Before_any_skill_bar_there_is_no_cast()
    {
        var cast = new Src.CastBarSource(new SkillBarState(), new Lengths(66), new Ticks(1033), Catalog);

        Assert.False(cast.Casting);
    }

    [Fact]
    public void An_unknown_charge_length_shows_the_skill_with_an_empty_meter()
    {
        var bar = Bar(chargeSlot: 2, chargeStart: 1000);   // a bar that carries no length
        var cast = new Src.CastBarSource(bar, new Src.SkillBarChargeLengths(bar), new Ticks(1033), Catalog);

        Assert.True(cast.Casting);
        Assert.Equal(0f, cast.Fraction);
    }

    [Fact]
    public void The_cast_bar_names_the_charging_skill_by_its_content_name()
    {
        var cast = new Src.CastBarSource(Bar(chargeSlot: 2, chargeStart: 1000), new Lengths(66), new Ticks(1033), Catalog);

        Assert.Equal("Patriot Missile", cast.SpellName);
    }

    [Fact]
    public void A_skill_the_client_does_not_have_is_named_by_its_id()
    {
        var bar = new SkillBarState();
        bar.OnSkillBar(new SkillBarView(["no_such_skill", "", "", "", "", ""], [0, 0, 0, 0, 0, 0], 1, 1000, 0, 66));
        var cast = new Src.CastBarSource(bar, new Src.SkillBarChargeLengths(bar), new Ticks(1033), Catalog);

        Assert.Equal("no_such_skill", cast.SpellName);
    }

    [Fact]
    public void The_charge_length_is_the_one_the_skill_bar_carries_for_the_charging_skill()
    {
        var bar = new SkillBarState();
        bar.OnSkillBar(new SkillBarView(["", "salvo", "", "", "", ""], [0, 0, 0, 0, 0, 0], 2, 1000, 0, 120));
        var lengths = new Src.SkillBarChargeLengths(bar);

        Assert.Equal(120, lengths.TicksOf("salvo"));
        Assert.Equal(0, lengths.TicksOf("patriot_missile"));
    }

    [Fact]
    public void A_bar_charging_for_sixty_of_its_hundred_and_twenty_ticks_reads_half()
    {
        var bar = new SkillBarState();
        bar.OnSkillBar(new SkillBarView(["", "salvo", "", "", "", ""], [0, 0, 0, 0, 0, 0], 2, 1000, 0, 120));
        var cast = new Src.CastBarSource(bar, new Src.SkillBarChargeLengths(bar), new Ticks(1060), Catalog);

        Assert.Equal(0.5f, cast.Fraction, 0.001f);
        Assert.Equal("Salvo", cast.SpellName);
    }

    //-------------------------------------------------------------------------
    // What the wire does not carry yet
    //-------------------------------------------------------------------------

    [Fact]
    public void Before_a_belt_arrives_it_is_four_empty_slots_ready()
    {
        var belt = new Src.BeltSource(new BeltState(), new Ticks(500));

        Assert.Equal(4, belt.SlotCount);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(("", 0), (belt.SlotName(i), belt.SlotCountOf(i))));
        Assert.True(belt.SlotReady);
    }

    private static BeltState Belt(int readyTick)
    {
        var state = new BeltState();
        state.OnBelt(new BeltView(["health_small", "", "mana_small", ""], ["Sandvich Bite", "", "Bonk! Shot", ""], [3, 0, 1, 0],
                                  readyTick, readyTick == 0 ? 0 : 200));
        return state;
    }

    [Fact]
    public void The_belt_shows_each_slots_potion_and_count_as_the_server_sent_them()
    {
        var belt = new Src.BeltSource(Belt(readyTick: 0), new Ticks(500));

        Assert.Equal(("Sandvich Bite", 3), (belt.SlotName(0), belt.SlotCountOf(0)));
        Assert.Equal(("", 0), (belt.SlotName(1), belt.SlotCountOf(1)));
        Assert.Equal(("Bonk! Shot", 1), (belt.SlotName(2), belt.SlotCountOf(2)));
        Assert.Equal(("", 0), (belt.SlotName(7), belt.SlotCountOf(7)));
    }

    [Theory]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(900, true)]
    public void The_belt_is_ready_once_the_clients_tick_reaches_the_cooldowns_end(int now, bool ready) =>
        Assert.Equal(ready, new Src.BeltSource(Belt(readyTick: 600), new Ticks(now)).SlotReady);

    [Fact]
    public void A_belt_cleared_at_level_end_is_empty_and_ready_again()
    {
        var state = Belt(readyTick: 600);
        state.OnLevelShutdown();
        var belt = new Src.BeltSource(state, new Ticks(10));

        Assert.Equal(("", 0), (belt.SlotName(0), belt.SlotCountOf(0)));
        Assert.True(belt.SlotReady);
    }

    [Fact]
    public void The_gear_warning_names_the_worst_worn_piece_and_how_much_is_left()
    {
        var wear = new GearWearState();
        wear.OnGearWear(new GearWearView("primary", "Rocket Launcher", GearWearLevel.Low, 12, 60));
        wear.OnGearWear(new GearWearView("melee", "Shovel", GearWearLevel.Ok, 80, 80));
        var warning = new Src.GearWarningSource(wear);

        Assert.True(warning.Present);
        Assert.Equal("Rocket Launcher", warning.ItemName);
        Assert.Equal(0.2f, warning.Fraction, 3);
    }

    [Fact]
    public void A_broken_piece_is_warned_of_at_nothing_left()
    {
        var wear = new GearWearState();
        wear.OnGearWear(new GearWearView("primary", "Rocket Launcher", GearWearLevel.Broken, 0, 60));
        var warning = new Src.GearWarningSource(wear);

        Assert.True(warning.Present);
        Assert.Equal(0f, warning.Fraction);
    }

    [Fact]
    public void Gear_above_the_warning_line_warns_of_nothing()
    {
        var wear = new GearWearState();
        wear.OnGearWear(new GearWearView("primary", "Rocket Launcher", GearWearLevel.Ok, 59, 60));
        var warning = new Src.GearWarningSource(wear);

        Assert.False(warning.Present);
        Assert.Equal(("", 0f), (warning.ItemName, warning.Fraction));
    }

    [Fact]
    public void No_boss_and_no_gear_warning_are_shown()
    {
        Assert.False(new Src.BossSource().Present);
        Assert.False(new Src.GearWarningSource(new GearWearState()).Present);
    }

    [Fact]
    public void The_spellbook_and_the_inspect_modal_have_nothing_to_show()
    {
        // The town's windows are live since UI4 (TownSourcesTests).
        Assert.Equal(0, new Src.SpellsSource().SpellCount);
        Assert.False(new Src.InspectSource().Showing);
    }
}
