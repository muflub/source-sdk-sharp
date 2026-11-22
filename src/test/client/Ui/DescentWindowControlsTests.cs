using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using TF2.Descent.Client.Ui;
using TF2.Descent.Client.Ui.Screens;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The Descent windows' controls the coordinator's play found missing: the inventory's
/// move and drop from the screen, and the skill window's assign to a bar slot.
/// </summary>
public class DescentWindowControlsTests
{
    private static Inventory InventoryScreen(out FakeInvActions actions, out FakeInvDrop drop)
    {
        actions = new FakeInvActions();
        drop = new FakeInvDrop();
        return (Inventory)InventorySamples.Build(new UiSampleServices(), new FakeInvEquip(), new FakeInvBackpack(), new FakeInvHover(),
                                                 new FakeInvPurse(), new FakeInvBelt(), actions, drop: drop);
    }

    [Fact]
    public void A_bag_click_equips_by_default()
    {
        var screen = InventoryScreen(out var actions, out var drop);

        screen.OnCell(2);

        Assert.Equal(2, actions.LastUse);
        Assert.Equal(-1, drop.LastDrop);
    }

    [Fact]
    public void In_move_mode_a_click_picks_an_item_up_and_the_next_puts_it_down()
    {
        var screen = InventoryScreen(out var actions, out _);
        screen.SetMode(Inventory.CellMode.Move);

        screen.OnCell(1);
        Assert.Equal(1, screen.Picked);
        screen.OnCell(9);

        Assert.Equal((1, 9), (actions.LastFrom, actions.LastTo));
        Assert.Equal(-1, screen.Picked);
        Assert.Equal(-1, actions.LastUse);
    }

    [Fact]
    public void In_move_mode_a_bag_item_in_hand_goes_onto_the_belt_slot_clicked()
    {
        var screen = InventoryScreen(out var actions, out _);
        screen.SetMode(Inventory.CellMode.Move);

        screen.OnCell(1);
        screen.OnBeltSlot(3);

        Assert.Equal((1, 3), (actions.LastFrom, actions.LastToBelt));
        Assert.Equal(-1, screen.Picked);
    }

    [Fact]
    public void In_move_mode_a_belt_slot_picked_up_goes_into_the_bag_cell_clicked()
    {
        var screen = InventoryScreen(out var actions, out _);
        screen.SetMode(Inventory.CellMode.Move);

        screen.OnBeltSlot(0);
        Assert.Equal(0, screen.PickedBelt);
        screen.OnCell(11);

        Assert.Equal((0, 11), (actions.LastOffBelt, actions.LastTo));
        Assert.Equal(-1, screen.PickedBelt);
    }

    [Fact]
    public void Out_of_move_mode_a_belt_slot_click_does_nothing()
    {
        var screen = InventoryScreen(out var actions, out _);

        screen.OnBeltSlot(0);

        Assert.Equal(-1, screen.PickedBelt);
        Assert.Equal(-1, actions.LastOffBelt);
    }

    [Fact]
    public void In_move_mode_an_empty_cell_picks_nothing_up()
    {
        var screen = InventoryScreen(out var actions, out _);
        screen.SetMode(Inventory.CellMode.Move);

        screen.OnCell(11);

        Assert.Equal(-1, screen.Picked);
        Assert.Equal(-1, actions.LastFrom);
    }

    [Fact]
    public void A_closed_inventory_opens_again_equipping()
    {
        var screen = InventoryScreen(out var actions, out var drop);
        screen.SetMode(Inventory.CellMode.Drop);

        screen.Closed();
        screen.OnCell(0);

        Assert.Equal(Inventory.CellMode.Equip, screen.Mode);
        Assert.Equal((0, -1), (actions.LastUse, drop.LastDrop));
    }

    [Fact]
    public void A_closed_skill_tree_drops_its_pick()
    {
        var screen = TreeScreen(out var slots, out var tree);
        screen.Pick(FirstWhere(tree, s => s.Rank > 0 && s.Kind == DescentSkillKind.Action));

        screen.Closed();
        screen.AssignTo(1);

        Assert.Equal((-1, -1), slots.Last);
    }

    [Fact]
    public void In_drop_mode_a_click_drops_the_item()
    {
        var screen = InventoryScreen(out var actions, out var drop);
        screen.SetMode(Inventory.CellMode.Drop);

        screen.OnCell(0);

        Assert.Equal(0, drop.LastDrop);
        Assert.Equal(-1, actions.LastUse);
    }

    private static SkillTree TreeScreen(out FakeSkillSlotActions slots, out FakeSkillTree tree)
    {
        slots = new FakeSkillSlotActions();
        tree = SkillTreeSamples.SoldierTree(3, 0);
        return (SkillTree)SkillTreeSamples.Build(new UiSampleServices(), tree, slotActions: slots);
    }

    private static int FirstWhere(FakeSkillTree tree, Func<DescentSkill, bool> test)
    {
        var tab = tree.Tab(0);
        for (int i = 0; i < tab.SkillCount; i++)
            if (test(tab.Skill(i)))
                return tab.Skill(i).Id;
        throw new InvalidOperationException("no such card in the sample tree");
    }

    [Fact]
    public void A_picked_learned_action_goes_to_the_clicked_bar_slot()
    {
        var screen = TreeScreen(out var slots, out var tree);
        int action = FirstWhere(tree, s => s.Rank > 0 && s.Kind == DescentSkillKind.Action);

        screen.Pick(action);
        screen.AssignTo(4);

        Assert.Equal((4, action), slots.Last);
        Assert.Equal(-1, screen.Picked);
    }

    [Fact]
    public void An_upgrade_cannot_be_picked()
    {
        var screen = TreeScreen(out var slots, out var tree);
        int upgrade = FirstWhere(tree, s => s.Kind == DescentSkillKind.Upgrade);

        screen.Pick(upgrade);
        screen.AssignTo(0);

        Assert.Equal(-1, screen.Picked);
        Assert.Equal((-1, -1), slots.Last);
    }

    [Fact]
    public void A_bar_slot_clicked_with_nothing_picked_assigns_nothing()
    {
        var screen = TreeScreen(out var slots, out _);

        screen.AssignTo(2);

        Assert.Equal((-1, -1), slots.Last);
    }

    [Fact]
    public void A_second_click_on_the_picked_card_puts_it_back()
    {
        var screen = TreeScreen(out _, out var tree);
        int action = FirstWhere(tree, s => s.Rank > 0 && s.Kind == DescentSkillKind.Action);

        screen.Pick(action);
        screen.Pick(action);

        Assert.Equal(-1, screen.Picked);
    }

    private sealed class FakeTownClose : IDescentTownClose
    {
        public readonly List<string> Closed = [];

        void IDescentTownClose.Closed(string screen) => Closed.Add(screen);
    }

    [Fact]
    public void Closing_the_stash_window_tells_the_town_its_screen_closed()
    {
        var close = new FakeTownClose();
        var s = new UiSampleServices();
        s.Add<IDescentTownClose>(close);
        var screen = StashSamples.Build(s, new FakeStashBag(), new FakeStashLocker(), new FakeStashActions());

        screen.Closed();

        Assert.Equal(["stash"], close.Closed);
    }

    [Fact]
    public void Closing_the_crate_stall_tells_the_town_its_screen_closed()
    {
        var close = new FakeTownClose();
        var s = new UiSampleServices();
        s.Add<IDescentTownClose>(close);
        var screen = CrateSamples.Build(s, CrateSamples.Published());

        screen.Closed();

        Assert.Equal(["crate"], close.Closed);
    }
}

/// <summary>
/// A refusal under an open window was invisible (the HUD's notice line sits under it): the
/// character sheet, the skill tree and the inventory each draw the window's status line.
/// </summary>
public class DescentWindowStatusTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static string StatusText(UiScreen screen)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 10.0);
        var els = UiTestKit.Elements(ctx);
        int status = Array.FindIndex(els, e => UiTestKit.NameOf(ctx, e) == "status");
        Assert.True(status >= 0, "the window has no status line");
        return string.Concat(Enumerable.Range(0, els.Length).Where(i => els[i].Parent == status).Select(i => UiTestKit.RunText(ctx, i)));
    }

    private const string Reason = "Requirement not met: Direct Hit";

    [Fact]
    public void The_skill_tree_shows_the_last_refusal()
        => Assert.Equal(Reason, StatusText(SkillTreeSamples.Build(new UiSampleServices(), SkillTreeSamples.SoldierTree(3, 0),
                                                                  status: new FakeWindowStatus { Text = Reason })));

    [Fact]
    public void The_inventory_shows_the_last_refusal()
        => Assert.Equal(Reason, StatusText(InventorySamples.Build(new UiSampleServices(), new FakeInvEquip(), new FakeInvBackpack(),
                                                                  new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(),
                                                                  new FakeInvActions(), new FakeWindowStatus { Text = Reason })));

    [Fact]
    public void The_character_sheet_shows_the_last_refusal()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(), s => !s.Gallery && s.Group == "charsheet" && s.Slug == "refused");

        Assert.Equal("No points to spend on Aim", StatusText(sample.Create()));
    }

    /// <summary>The doll's cells read "Minig..." and "Rock..." live: they are wide enough for the whole name now.</summary>
    [Fact]
    public void The_dolls_cells_show_their_items_whole_names()
    {
        var sample = Assert.Single(PreviewRun.AllSamples(), s => !s.Gallery && s.Group == "inventory" && s.Slug == "full-kit");
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, sample.Create(), UiSamples.Screen(1920, 1080), 10.0);
        var texts = Enumerable.Range(0, UiTestKit.Elements(ctx).Length).Select(i => UiTestKit.RunText(ctx, i)).ToArray();

        Assert.Contains("Minigun", texts);   // the doll's primary: the bag holds no minigun
        Assert.DoesNotContain(texts, t => t.StartsWith("Minig", StringComparison.Ordinal) && t != "Minigun");
    }

    [Fact]
    public void A_window_with_nothing_to_say_draws_an_empty_line()
        => Assert.Equal("", StatusText(InventorySamples.Build(new UiSampleServices(), new FakeInvEquip(), new FakeInvBackpack(),
                                                              new FakeInvHover(), new FakeInvPurse(), new FakeInvBelt(),
                                                              new FakeInvActions())));
}
