extern alias client;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using IGameClock = client::SourceSharp.Abi.IGameClock;
using IItemViews = client::TF2.Descent.Client.Items.IItemViews;
using IServiceLocator = client::SourceSharp.Abi.IServiceLocator;
using IUiLocalisation = client::SourceSharp.Client.Ui.IUiLocalisation;
using ItemContainer = client::TF2.Descent.Shared.ItemContainer;
using ItemViewFlags = client::TF2.Descent.Shared.ItemViewFlags;
using OwnerItemView = client::TF2.Descent.Shared.OwnerItemView;
using SkillBarState = client::TF2.Descent.Client.Skills.SkillBarState;
using SkillBarView = client::TF2.Descent.Shared.SkillBarView;
using Src = client::TF2.Descent.Client.Ui;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The controls the windows lacked (the polish lane): the skill bar's slots the HUD strip
/// and the skill window draw, the assign, the backpack's drop and move from the screen, and
/// an open window's status line; each sends a verb the server already answers.
/// </summary>
public sealed class WindowControlsTests
{
    private static readonly Catalog Content = new();

    private sealed class Ticks(int now) : Src.IClientTicks
    {
        public int Now { get; set; } = now;
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

    private sealed class Words(params (string Token, string Text)[] rows) : IUiLocalisation, IServiceLocator
    {
        public string? Find(string token) => rows.FirstOrDefault(r => r.Token == token).Text;
        public object? Resolve(Type serviceType) => serviceType == typeof(IUiLocalisation) ? this : null;
        public object? ResolveNamed(string name) => null;
        public bool IsRegistered(Type serviceType) => Resolve(serviceType) is not null;
        public IReadOnlyCollection<Type> ServiceTypes => [typeof(IUiLocalisation)];
        public IReadOnlyCollection<string> Names => [];
    }

    private sealed class Views : IItemViews
    {
        public List<OwnerItemView> Held { get; } = [];
        public int Version => Held.Count;
        public bool Synced => true;
        public bool ContentMismatch => false;
        public long Australium => 0;
        public int BackpackCapacity => 12;
        public IReadOnlyList<OwnerItemView> Items => Held;
        public OwnerItemView? In(ItemContainer container, int slot) => Held.FirstOrDefault(i => i.Container == container && i.Slot == slot);
    }

    private static OwnerItemView Bag(long id, int slot) =>
        new(id, ItemContainer.Backpack, slot, Content.Bases.ToList().FindIndex(b => b.Id == "shotgun"), 0, 5,
            ItemViewFlags.Identified | ItemViewFlags.MeetsRequirements, 50, 60, 1, -1, -1, 0, []);

    private static SkillBarState Bar(int[] ready, int chargeSlot = 0)
    {
        var bar = new SkillBarState();
        bar.OnSkillBar(new SkillBarView(["patriot_missile", "", "rocket_leap", "", "", ""], ready, chargeSlot, 0, 0));
        return bar;
    }

    //-------------------------------------------------------------------------
    // The skill slots

    [Fact]
    public void A_slot_names_its_skill_by_the_contents_token_and_an_empty_one_nothing()
    {
        var slots = new Src.SkillSlotsSource(Bar([0, 0, 0, 0, 0, 0]), new Ticks(100), Content);

        Assert.Equal(6, slots.SlotCount);
        Assert.Equal(Content.Skill("patriot_missile")!.NameToken, slots.SlotNameToken(0));
        Assert.Equal("", slots.SlotNameToken(1));
    }

    [Fact]
    public void A_cooldown_seen_start_drains_from_one_to_zero_by_the_ready_tick()
    {
        var ticks = new Ticks(100);
        var bar = new SkillBarState();
        var slots = new Src.SkillSlotsSource(bar, ticks, Content);
        var view = new SkillBarView(["patriot_missile", "", "", "", "", ""], [200, 0, 0, 0, 0, 0], 0, 0, 0);
        bar.OnSkillBar(view);
        slots.OnSkillBar(view);

        Assert.Equal(1f, slots.SlotCooldown(0));
        ticks.Now = 150;
        Assert.Equal(0.5f, slots.SlotCooldown(0), 3);
        ticks.Now = 200;
        Assert.Equal(0f, slots.SlotCooldown(0));
        Assert.Equal(0f, slots.SlotCooldown(1));
    }

    [Fact]
    public void The_charging_slot_is_the_bars()
    {
        var slots = new Src.SkillSlotsSource(Bar([0, 0, 0, 0, 0, 0], chargeSlot: 3), new Ticks(100), Content);

        Assert.True(slots.SlotCharging(2));
        Assert.False(slots.SlotCharging(0));
    }

    [Fact]
    public void A_level_end_forgets_the_cooldowns_of_the_last_levels_clock()
    {
        var ticks = new Ticks(100);
        var slots = new Src.SkillSlotsSource(new SkillBarState(), ticks, Content);
        slots.OnSkillBar(new SkillBarView(["patriot_missile", "", "", "", "", ""], [5000, 0, 0, 0, 0, 0], 0, 0, 0));

        slots.OnLevelShutdown();
        ticks.Now = 10;

        Assert.Equal(0f, slots.SlotCooldown(0));
    }

    [Fact]
    public void Assigning_sends_the_assign_verb_with_the_one_based_slot_and_the_skills_id()
    {
        var lines = new Lines();

        new Src.SkillSlotActions(Content, lines).Assign(2, Content.SkillIndex("rocket_leap"));

        Assert.Equal(["tf2_descent_assign 3 rocket_leap"], lines.Sent);
    }

    [Fact]
    public void Assigning_out_of_the_bar_or_an_unknown_skill_sends_nothing()
    {
        var lines = new Lines();
        var actions = new Src.SkillSlotActions(Content, lines);

        actions.Assign(6, Content.SkillIndex("rocket_leap"));
        actions.Assign(0, -5);

        Assert.Empty(lines.Sent);
    }

    //-------------------------------------------------------------------------
    // The backpack's drop

    [Fact]
    public void Dropping_a_cell_with_an_item_sends_the_drop_verb_and_an_empty_one_nothing()
    {
        var items = new Views();
        items.Held.Add(Bag(7, 3));
        var lines = new Lines();
        var drop = new Src.ItemDropAction(items, lines);

        drop.Drop(3);
        drop.Drop(4);

        Assert.Equal(["tf2_descent_item_drop 3"], lines.Sent);
    }

    //-------------------------------------------------------------------------
    // The window's status line

    [Fact]
    public void A_notice_shows_on_the_window_line_worded_as_the_hud_words_it()
    {
        var status = new Src.WindowStatusSource(new Clock(), new Words(("#Descent_Refused_requirement", "Requirement not met: %s1")));

        status.OnNotice("#Descent_Refused_requirement", "Direct Hit");

        Assert.True(status.Showing);
        Assert.Equal("Requirement not met: Direct Hit", status.Text);
    }

    [Fact]
    public void The_window_line_clears_five_seconds_after_the_notice()
    {
        var clock = new Clock();
        var status = new Src.WindowStatusSource(clock, new Words());
        status.OnNotice("Cannot assign slot 2: not learned", "");

        clock.Now += 4.9f;
        Assert.True(status.Showing);
        clock.Now += 0.2f;
        Assert.False(status.Showing);
    }

    [Fact]
    public void Nothing_shows_before_a_notice_or_after_a_level_ends()
    {
        var status = new Src.WindowStatusSource(new Clock(), new Words());
        Assert.False(status.Showing);

        status.OnNotice("Recovered 6 item(s)", "");
        status.OnLevelShutdown();

        Assert.False(status.Showing);
    }
}
