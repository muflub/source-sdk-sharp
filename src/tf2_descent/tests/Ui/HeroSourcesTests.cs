extern alias client;

using TF2.Descent.Client.Ui;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using HeroState = client::TF2.Descent.Client.HeroState;
using IGameClock = client::SourceSharp.Abi.IGameClock;
using IHeroSheetNumbers = client::TF2.Descent.Client.Items.IHeroSheetNumbers;
using IHeroSkillRanks = client::TF2.Descent.Client.Items.IHeroSkillRanks;
using IHeroStates = client::TF2.Descent.Client.IHeroStates;
using IItemViews = client::TF2.Descent.Client.Items.IItemViews;
using IPlayerNames = client::SourceSharp.Client.Vgui.IPlayerNames;
using ItemContainer = client::TF2.Descent.Shared.ItemContainer;
using ItemViewFlags = client::TF2.Descent.Shared.ItemViewFlags;
using OwnerItemView = client::TF2.Descent.Shared.OwnerItemView;
using SheetNumbersView = client::TF2.Descent.Shared.SheetNumbersView;
using SkillWhy = client::TF2.Descent.Shared.SkillWhy;
using Src = client::TF2.Descent.Client.Ui;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The character sheet's, the inventory's and the skill tree's live sources (plan_descent_logic
/// UI3) over fakes of what the owner's client holds: the hero state, the item views, the sheet
/// numbers, the skill ranks; the content is the client's own embedded tables.
/// </summary>
public sealed class HeroSourcesTests
{
    private const int Local = 1;
    private static readonly Catalog Content = new();

    private static int Attr(string id) => Content.Attributes.ToList().FindIndex(a => a.Id == id);
    private static int Base(string id) => Content.Bases.ToList().FindIndex(b => b.Id == id);
    private static int Affix(string id) => Content.Affixes.ToList().FindIndex(a => a.Id == id);
    private static int Skill(string id) => Content.SkillIndex(id);
    private static int EquipSlot(string id) => Content.EquipSlots.ToList().FindIndex(e => e.Id == id);

    //-------------------------------------------------------------------------
    // Fakes
    //-------------------------------------------------------------------------

    private sealed class Heroes(params HeroState[] heroes) : IHeroStates
    {
        public HeroState? Of(int playerIndex) => heroes.FirstOrDefault(h => h.PlayerIndex == playerIndex);
        public HeroState? Local() => Of(HeroSourcesTests.Local);
        public IReadOnlyList<HeroState> All() => heroes;
    }

    private static HeroState Hero(int level = 3, int attr = 10, int skill = 2) =>
        new(Local, 3, level, 340, 300, 400, 175, 60, 80, attr, skill, false, 0, false);

    private sealed class Names : IPlayerNames
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            info = new ClientPlayerInfo("Bugha", slot, false);
            return slot == Local;
        }
    }

    private static ClientEngine Engine(int health = 150)
        => new ClientEngine { Strict = false }.Returns("Hud.LocalPlayerIndex", Local).Returns("Hud.LocalPlayerHealth", health);

    private sealed class Numbers(SheetNumbersView? sheet) : IHeroSheetNumbers
    {
        public SheetNumbersView? Sheet { get; set; } = sheet;
    }

    /// <summary>A Soldier's sheet: brawn 20, smarts 5, aim 20, grit 15 (caps 90 50 90 80), grit worth 3 life a point, smarts 1.5 mana.</summary>
    private static SheetNumbersView Sheet() => new(
        Content.Fingerprint, [20, 5, 20, 15], [200, 50, 245, 150], [90, 50, 90, 80],
        [0, 0, 0, 30], [0, 15, 0, 0], [3, 3, 3, 3], 75, 4, 25, 81, 135);

    private sealed class FakeItems : IItemViews
    {
        public List<OwnerItemView> Held { get; } = [];
        public int Version => Held.Count;
        public bool Synced => true;
        public bool ContentMismatch { get; set; }
        public long Australium { get; set; } = 42;
        public int BackpackCapacity { get; set; } = 12;
        public IReadOnlyList<OwnerItemView> Items => Held;
        public OwnerItemView? In(ItemContainer container, int slot) => Held.FirstOrDefault(i => i.Container == container && i.Slot == slot);
    }

    private static OwnerItemView Item(long id, ItemContainer container, int slot, string baseId, int rarity = 0,
                                      ItemViewFlags flags = ItemViewFlags.Identified | ItemViewFlags.MeetsRequirements,
                                      int unmet = 0, int unusual = -1, params (int, float)[] affixes)
        => new(id, container, slot, Base(baseId), rarity, 5, flags, 50, 60, 1, unusual, -1, unmet, affixes);

    private sealed class Ranks : IHeroSkillRanks
    {
        public Dictionary<int, (int Rank, SkillWhy Why, int Level)> Of { get; } = new();
        public bool Known { get; set; } = true;
        public int RankOf(int skill) => Of.GetValueOrDefault(skill).Rank;
        public SkillWhy WhyOf(int skill) => Of.TryGetValue(skill, out var r) ? r.Why : SkillWhy.NoPoints;
        public int NextLevelOf(int skill) => Of.GetValueOrDefault(skill).Level;
        public HashSet<int> PrerequisitesMissing { get; } = [];
        public bool PrerequisitesMetOf(int skill) => !PrerequisitesMissing.Contains(skill);
    }

    private sealed class Lines : Src.IDescentCommandLine
    {
        public List<string> Sent { get; } = [];
        public void Send(string line) => Sent.Add(line);
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed record Sheets(Src.SheetSource Sheet, Src.SheetActions Actions, Src.SheetHoverSource Hover, Lines Lines,
                                 Clock Clock, FakeItems Items);

    private static Sheets SheetWindow(HeroState? hero = null, SheetNumbersView? numbers = null)
    {
        var heroes = hero is null ? new Heroes() : new Heroes(hero);
        var n = new Numbers(numbers ?? Sheet());
        var lines = new Lines();
        var clock = new Clock();
        var items = new FakeItems();
        var plan = new Src.SheetPlan(heroes, n, Content, lines);
        var sheet = new Src.SheetSource(heroes, Engine(), new Names(), n, items, Content, plan);
        var pointer = new Src.SheetPointer(clock);
        return new Sheets(sheet, new Src.SheetActions(plan, pointer), new Src.SheetHoverSource(pointer, n, Content, sheet), lines, clock, items);
    }

    //-------------------------------------------------------------------------
    // The character sheet
    //-------------------------------------------------------------------------

    [Fact]
    public void The_sheet_reads_the_heros_name_class_level_experience_life_mana_and_points()
    {
        var sheet = SheetWindow(Hero(level: 3, attr: 5)).Sheet;

        Assert.Equal(("Bugha", "#Descent_Class_Soldier", 3), (sheet.Name, sheet.ClassToken, sheet.Level));
        Assert.Equal((340L, 60L), (sheet.Xp, sheet.XpToNext));
        Assert.Equal((150, 175, 60, 80, 5), (sheet.Life, sheet.LifeMax, sheet.Mana, sheet.ManaMax, sheet.AttributePoints));
    }

    [Fact]
    public void The_four_attributes_read_the_servers_base_now_and_cap()
    {
        var sheet = SheetWindow(Hero()).Sheet;

        Assert.Equal(4, sheet.AttributeCount);
        Assert.Equal(new DescentAttribute("#Descent_Attr_Aim", 20, 25, 90, 0), sheet.Attribute(Attr("aim")));   // now 24.5 rounds to 25
        Assert.Equal(new DescentAttribute("#Descent_Attr_Brawn", 20, 20, 90, 0), sheet.Attribute(Attr("brawn")));
    }

    [Fact]
    public void Resistances_armour_crit_damage_and_australium_are_the_servers()
    {
        var w = SheetWindow(Hero());
        var sheet = w.Sheet;

        Assert.Equal((4, 75), (sheet.ResistCount, sheet.ResistCap));
        Assert.Equal(("#Descent_Type_Fire", 3), (sheet.ResistLabel(0), sheet.ResistPercent(0)));
        Assert.Equal((4, 3, 81, 135), (sheet.Armour, sheet.CritPercent, sheet.DamageMin, sheet.DamageMax));
        Assert.Equal(42L, sheet.Australium);
    }

    [Fact]
    public void Before_the_numbers_arrive_the_sheet_shows_no_attribute()
    {
        var w = SheetWindow(Hero());
        var sheet = new Src.SheetSource(new Heroes(Hero()), Engine(), new Names(), new Numbers(null), w.Items, Content,
                                        new Src.SheetPlan(new Heroes(Hero()), new Numbers(null), Content, new Lines()));

        Assert.Equal((0, 0, 0, 0), (sheet.AttributeCount, sheet.ResistCount, sheet.Armour, sheet.DamageMax));
    }

    [Fact]
    public void With_no_local_hero_the_sheet_is_empty()
    {
        var sheet = SheetWindow(hero: null).Sheet;

        Assert.Equal(("", "", 0, 0L, 0, 0), (sheet.Name, sheet.ClassToken, sheet.Level, sheet.Xp, sheet.Life, sheet.AttributeCount));
    }

    [Fact]
    public void Staged_grit_raises_the_pending_life_by_the_servers_life_a_point()
    {
        var w = SheetWindow(Hero());

        w.Actions.Stage(Attr("grit"), 2);

        Assert.Equal(2, w.Sheet.Attribute(Attr("grit")).Pending);
        Assert.Equal(175 + 6, w.Sheet.PendingLifeMax);
        Assert.Equal(80, w.Sheet.PendingManaMax);
    }

    [Fact]
    public void Staging_never_passes_the_points_held()
    {
        var w = SheetWindow(Hero(attr: 3));

        w.Actions.Stage(Attr("aim"), 5);

        Assert.Equal(3, w.Sheet.Attribute(Attr("aim")).Pending);
    }

    [Fact]
    public void Staging_never_passes_the_cap()
    {
        var w = SheetWindow(Hero(attr: 100));

        w.Actions.Stage(Attr("smarts"), 5);   // smarts is at its cap of 50 already? no: base 5, cap 50

        Assert.Equal(5, w.Sheet.Attribute(Attr("smarts")).Pending);
        w.Actions.Stage(Attr("smarts"), 100);
        Assert.Equal(45, w.Sheet.Attribute(Attr("smarts")).Pending);
    }

    [Fact]
    public void Apply_sends_each_staged_attribute_as_the_spend_verb_and_clears_the_plan()
    {
        var w = SheetWindow(Hero());
        w.Actions.Stage(Attr("aim"), 2);
        w.Actions.Stage(Attr("grit"), 1);

        w.Actions.Apply();

        Assert.Equal(["tf2_descent_spend aim 2", "tf2_descent_spend grit 1"], w.Lines.Sent);
        Assert.Equal(0, w.Sheet.Attribute(Attr("aim")).Pending);
    }

    [Fact]
    public void Reset_hands_every_staged_point_back_and_sends_nothing()
    {
        var w = SheetWindow(Hero());
        w.Actions.Stage(Attr("aim"), 2);

        w.Actions.Reset();
        w.Actions.Apply();

        Assert.Equal(0, w.Sheet.Attribute(Attr("aim")).Pending);
        Assert.Empty(w.Lines.Sent);
    }

    [Fact]
    public void An_attributes_hint_says_what_each_point_gives_from_the_content_and_the_server()
    {
        var w = SheetWindow(Hero());

        w.Actions.Hover(Attr("grit"), -1);

        Assert.True(w.Hover.Active);
        Assert.Equal(Attr("grit"), w.Hover.HoverAttribute);
        Assert.Equal("#Descent_Attr_Grit", w.Hover.TitleToken);
        var lines = Enumerable.Range(0, w.Hover.LineCount).Select(i => (w.Hover.LineToken(i), w.Hover.LineGood(i))).ToList();
        Assert.Contains(("#Descent_Stat_resist_all_pct", "+0.2"), lines);   // attributes.json
        Assert.Contains(("#Descent_Stat_max_life", "+3"), lines);           // the server's life a point
    }

    [Fact]
    public void A_hint_lapses_when_the_pointer_stops_reporting()
    {
        var w = SheetWindow(Hero());
        w.Actions.Hover(Attr("aim"), -1);

        w.Clock.Now += 1f;

        Assert.False(w.Hover.Active);
        Assert.Equal(-1, w.Hover.HoverAttribute);
    }

    [Fact]
    public void A_derived_plates_hint_names_it_and_shows_its_value()
    {
        var w = SheetWindow(Hero());

        w.Actions.Hover(-1, 4);

        Assert.Equal(4, w.Hover.HoverDerived);
        Assert.Equal("#Descent_Sheet_Damage", w.Hover.TitleToken);
        Assert.Equal("81 - 135", w.Hover.ValueData(0));
        Assert.Equal("#Descent_Sheet_Damage_Detail", w.Hover.LineToken(0));
    }

    //-------------------------------------------------------------------------
    // The inventory
    //-------------------------------------------------------------------------

    private static Src.ItemViewNames Names2 => new(Content);

    [Fact]
    public void The_doll_has_the_nine_equipment_slots_each_earned_holding_what_is_equipped()
    {
        var items = new FakeItems();
        items.Held.Add(Item(7, ItemContainer.Equipped, EquipSlot("melee"), "shovel"));
        var doll = new Src.EquipmentSource(new Heroes(Hero()), items, Names2, Content);

        Assert.Equal(9, doll.SlotCount);
        Assert.Equal(DescentEquipSlot.Melee, doll.Slot(EquipSlot("melee")));
        Assert.Equal(DescentEquipSlot.Amulet, doll.Slot(EquipSlot("amulet")));
        Assert.All(Enumerable.Range(0, 9), i => Assert.True(doll.SlotEarned(i)));
        Assert.Equal("Shovel", doll.SlotItem(EquipSlot("melee"))!.Value.BaseName);
        Assert.Null(doll.SlotItem(EquipSlot("primary")));
    }

    [Fact]
    public void The_backpack_is_its_items_by_cell_with_the_servers_capacity()
    {
        var items = new FakeItems { BackpackCapacity = 12 };
        items.Held.Add(Item(1, ItemContainer.Backpack, 3, "shotgun"));
        items.Held.Add(Item(2, ItemContainer.Belt, 0, "health_small"));
        var bag = new Src.BackpackSource(items, Names2);

        Assert.Equal((1, 12, false), (bag.CellCount, bag.Capacity, bag.Overflow));
        Assert.Equal("Shotgun", bag.Cell(3)!.Value.BaseName);
        Assert.Null(bag.Cell(0));
    }

    [Fact]
    public void An_item_past_the_capacity_reads_withdraw_only()
    {
        var items = new FakeItems { BackpackCapacity = 12 };
        items.Held.Add(Item(1, ItemContainer.Backpack, 14, "shotgun"));

        Assert.True(new Src.BackpackSource(items, Names2).Overflow);
    }

    [Fact]
    public void An_unidentified_item_reads_unidentified_with_no_affix_lines()
    {
        var items = new FakeItems();
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "rocket_launcher", rarity: 3, flags: ItemViewFlags.None));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);
        pointer.Report(0);

        Assert.True(hover.Hovering);
        Assert.False(hover.Item.Identified);
        Assert.True(hover.Item.HasUnusual);   // the rarity is public: Unusual wears its star
        Assert.Equal(0, hover.AffixLineCount);
    }

    [Fact]
    public void An_identified_items_affix_lines_read_a_tf_multiplier_as_its_percent()
    {
        var items = new FakeItems();
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "rocket_launcher", affixes: [(Affix("heavy"), 1.08f), (Affix("of_aim"), 6f)]));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);
        pointer.Report(0);

        Assert.Equal(2, hover.AffixLineCount);
        Assert.Equal(new DescentAffixLine("Heavy", 8), hover.AffixLine(0));
        Assert.Equal(new DescentAffixLine("of Aim", 6), hover.AffixLine(1));
    }

    [Fact]
    public void A_broken_item_reads_broken_and_an_unusable_one_inactive()
    {
        var names = Names2;

        var broken = names.View(Item(1, ItemContainer.Equipped, 0, "rocket_launcher", flags: ItemViewFlags.Identified | ItemViewFlags.Broken | ItemViewFlags.Inactive));
        var unusable = names.View(Item(2, ItemContainer.Backpack, 0, "rocket_launcher", flags: ItemViewFlags.Identified | ItemViewFlags.Unusable));

        Assert.True(broken.Broken);
        Assert.True(unusable.Inactive);
        Assert.False(unusable.Broken);
    }

    [Fact]
    public void An_item_that_fails_a_requirement_shows_the_unmet_one()
    {
        var items = new FakeItems();
        // The Direct Hit: level 6, aim 30. The server says the aim is unmet (bit 1), the level met.
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "direct_hit", flags: ItemViewFlags.Identified, unmet: 0b10));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);
        pointer.Report(0);

        Assert.Equal("#Descent_Req_aim_30", hover.RequirementToken);
        Assert.False(hover.RequirementMet);
    }

    [Fact]
    public void An_unmet_level_is_the_requirement_shown_first()
    {
        var items = new FakeItems();
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "direct_hit", flags: ItemViewFlags.Identified, unmet: 0b11));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);
        pointer.Report(0);

        Assert.Equal("#Descent_ReqLevel_6", hover.RequirementToken);
        Assert.False(hover.RequirementMet);
    }

    [Fact]
    public void A_met_requirement_is_still_shown_as_met()
    {
        var items = new FakeItems();
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "direct_hit"));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);
        pointer.Report(0);

        Assert.Equal(("#Descent_Req_aim_30", true), (hover.RequirementToken, hover.RequirementMet));
    }

    [Fact]
    public void A_backpack_weapon_compares_with_the_one_in_its_slot()
    {
        var items = new FakeItems();
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "direct_hit"));
        items.Held.Add(Item(2, ItemContainer.Equipped, EquipSlot("primary"), "rocket_launcher"));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);
        pointer.Report(0);

        Assert.True(hover.CompareHas);
        Assert.Equal("Rocket Launcher", hover.CompareItem.BaseName);
    }

    [Fact]
    public void A_doll_slot_hovers_as_minus_one_minus_the_slot()
    {
        var items = new FakeItems();
        items.Held.Add(Item(2, ItemContainer.Equipped, EquipSlot("melee"), "shovel"));
        var pointer = new Src.ItemPointer(new Clock());
        var hover = new Src.ItemHoverSource(pointer, items, Names2, Content);

        pointer.Report(-1 - EquipSlot("melee"));

        Assert.Equal("Shovel", hover.Item.BaseName);
        Assert.False(hover.CompareHas);
    }

    [Fact]
    public void Using_a_backpack_item_sends_the_equip_verb_and_a_doll_slot_the_unequip_verb()
    {
        var items = new FakeItems();
        items.Held.Add(Item(1, ItemContainer.Backpack, 4, "shotgun"));
        items.Held.Add(Item(2, ItemContainer.Equipped, EquipSlot("melee"), "shovel"));
        var lines = new Lines();
        var actions = new Src.ItemActions(items, Content, lines, new Src.ItemPointer(new Clock()));

        actions.Use(4);
        actions.Use(-1 - EquipSlot("melee"));
        actions.Move(4, 9);

        Assert.Equal(["tf2_descent_item_equip 4", "tf2_descent_item_unequip melee", "tf2_descent_item_move 4 9"], lines.Sent);
    }

    [Fact]
    public void Using_an_empty_cell_sends_nothing()
    {
        var lines = new Lines();
        var actions = new Src.ItemActions(new FakeItems(), Content, lines, new Src.ItemPointer(new Clock()));

        actions.Use(0);
        actions.Use(-1);
        actions.Move(0, 1);

        Assert.Empty(lines.Sent);
    }

    [Fact]
    public void The_purse_is_the_item_views_australium()
    {
        Assert.Equal(42L, new Src.PurseSource(new FakeItems()).Australium);
    }

    [Fact]
    public void Items_from_other_content_are_not_shown()
    {
        var items = new FakeItems { ContentMismatch = true };
        items.Held.Add(Item(1, ItemContainer.Backpack, 0, "shotgun"));

        Assert.Equal(0, new Src.BackpackSource(items, Names2).CellCount);
        Assert.Null(new Src.BackpackSource(items, Names2).Cell(0));
    }

    //-------------------------------------------------------------------------
    // The skill tree
    //-------------------------------------------------------------------------

    private static (Src.SkillsSource Skills, Ranks Ranks, Src.SkillTabSelectionSource Selection) Tree(HeroState? hero = null)
    {
        var ranks = new Ranks();
        var selection = new Src.SkillTabSelectionSource();
        return (new Src.SkillsSource(new Heroes(hero ?? Hero()), ranks, Content, selection), ranks, selection);
    }

    [Fact]
    public void A_soldiers_window_has_his_three_trees_then_the_mercenary_tab()
    {
        var (skills, _, _) = Tree();

        Assert.Equal(4, skills.TabCount);
        Assert.Equal(["#Descent_SkillTab_ordnance", "#Descent_SkillTab_airborne", "#Descent_SkillTab_command", "#Descent_SkillTab_mercenary"],
                     Enumerable.Range(0, 4).Select(i => skills.Tab(i).TitleToken));
        Assert.Equal(("#Descent_Class_Soldier", 3, 2), (skills.ClassToken, skills.HeroLevel, skills.SkillPoints));
    }

    [Fact]
    public void A_card_names_its_skill_and_reads_the_servers_rank_and_word()
    {
        var (skills, ranks, _) = Tree();
        ranks.Of[Skill("patriot_missile")] = (2, SkillWhy.Buyable, 1);

        var card = skills.Tab(0).Skill(0);

        Assert.Equal((Skill("patriot_missile"), "#Descent_SkillName_patriot_missile", DescentSkillKind.Action), (card.Id, card.NameToken, card.Kind));
        Assert.Equal((2, 5, true, true, true), (card.Rank, card.MaxRank, card.Buyable, card.Unlocked, card.PrereqMet));
        Assert.Equal(("4 mana", ""), (card.ManaCost, card.Cooldown));
    }

    [Fact]
    public void A_skill_above_the_heros_level_is_locked_at_the_level_it_asks()
    {
        var (skills, ranks, _) = Tree();
        ranks.Of[Skill("salvo")] = (0, SkillWhy.BelowLevel, 6);
        var tab = skills.Tab(0);
        int salvo = Enumerable.Range(0, tab.SkillCount).Single(i => tab.Skill(i).Id == Skill("salvo"));

        var card = tab.Skill(salvo);

        Assert.Equal((false, false, 6, 2), (card.Unlocked, card.Buyable, card.TierUnlockLevel, card.Tier));
        Assert.Equal(("", ""), (card.ManaCost, card.Cooldown));   // the level line takes the costs' row
    }

    [Fact]
    public void A_skill_whose_prerequisite_is_missing_reads_prerequisite_unmet_and_names_it()
    {
        var (skills, ranks, _) = Tree();
        ranks.Of[Skill("salvo")] = (0, SkillWhy.BelowLevel, 6);   // the rules name the level first
        ranks.PrerequisitesMissing.Add(Skill("salvo"));
        var tab = skills.Tab(0);
        int salvo = Enumerable.Range(0, tab.SkillCount).Single(i => tab.Skill(i).Id == Skill("salvo"));

        Assert.False(tab.Skill(salvo).PrereqMet);
        Assert.Equal(1, tab.PrereqCount(salvo));
        Assert.Equal(Skill("patriot_missile"), tab.PrereqId(salvo, 0));
    }

    [Fact]
    public void Buying_a_card_sends_the_learn_verb_with_the_skills_id()
    {
        var lines = new Lines();
        var actions = new Src.SkillActions(Content, lines, new Src.SkillPointer(new Clock()));

        actions.Buy(Skill("salvo"));
        actions.Buy(-5);

        Assert.Equal(["tf2_descent_learn salvo"], lines.Sent);
    }

    [Fact]
    public void A_tab_clicked_is_the_tab_shown()
    {
        var (skills, _, selection) = Tree();

        selection.SelectTab(2);

        Assert.Equal(2, skills.SelectedTab);
        Assert.Equal(2, ((IDescentSkillTabSelection)skills).SelectedTab);
    }

    [Fact]
    public void Before_the_ranks_arrive_the_window_has_one_empty_tab()
    {
        var (skills, ranks, _) = Tree();
        ranks.Known = false;

        Assert.Equal((1, 0), (skills.TabCount, skills.Tab(0).SkillCount));
    }

    [Fact]
    public void A_cards_hint_reads_its_rank_and_why_the_next_rank_cannot_be_bought()
    {
        var ranks = new Ranks();
        ranks.Of[Skill("salvo")] = (0, SkillWhy.BelowLevel, 6);
        var pointer = new Src.SkillPointer(new Clock());
        var hover = new Src.SkillHoverSource(pointer, ranks, Content);

        pointer.Report(Skill("salvo"));

        Assert.Equal(("#Descent_SkillName_salvo", "0 / 5"), (hover.TitleToken, hover.SubtitleData));
        var lines = Enumerable.Range(0, hover.LineCount).Select(i => (hover.LineToken(i), hover.LineGood(i))).ToList();
        Assert.Contains(("#Descent_SkillWhy_BelowLevel", "6"), lines);
        Assert.Contains(("#Descent_Hint_Cooldown", "6 s"), lines);
    }

    [Fact]
    public void The_hint_part_reads_whichever_subject_the_pointer_is_on()
    {
        var w = SheetWindow(Hero());
        var pointer = new Src.SkillPointer(w.Clock);
        var skillHover = new Src.SkillHoverSource(pointer, new Ranks(), Content);
        var town = new client::TF2.Descent.Client.Town.TownCache(Content);
        var vendorHover = new Src.VendorHoverSource(new Src.WarePointer(w.Clock), town, w.Items, new Src.ItemViewNames(Content), Content);
        var tip = new Src.HintTipSource(w.Hover, skillHover, vendorHover);
        Assert.False(tip.Active);

        pointer.Report(Skill("salvo"));
        Assert.Equal("#Descent_SkillName_salvo", tip.TitleToken);

        w.Clock.Now += 1f;
        w.Actions.Hover(Attr("aim"), -1);
        Assert.Equal("#Descent_Attr_Aim", tip.TitleToken);
    }
}
