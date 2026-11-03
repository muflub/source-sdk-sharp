using TF2.Shared;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The visible-slot table the loadout screen and the reply builder share:
/// stock's <c>g_VisibleLoadoutSlotsPerClass</c>
/// (game/client/tf/vgui/class_loadout_panel.cpp:105-195), a panel position
/// per slot where 0 is hidden, and the default row for every class but the
/// spy (whose sapper takes the building position and hides the primary) and
/// the engineer (whose toolbox takes the PDA one).
/// </summary>
public sealed class TFLoadoutSlotsTests
{
    [Fact]
    public void TheDefaultRowHidesEverythingButTheSevenWeaponsAndWearables()
    {
        // class_loadout_panel.cpp:105-129: primary 1, secondary 2, melee 3,
        // head 5, misc 6, misc2 7, action 8; utility/building/pda/pda2 0.
        Assert.Equal(1, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Primary));
        Assert.Equal(2, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Secondary));
        Assert.Equal(3, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Melee));
        Assert.Equal(5, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Head));
        Assert.Equal(6, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Misc));
        Assert.Equal(7, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Misc2));
        Assert.Equal(8, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Action));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Utility));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Building));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Pda));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Pda2));
    }

    [Fact]
    public void EveryClassButTheSpyAndEngineerWearsTheDefaultRow()
    {
        Assert.Equal(TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Primary),
                     TFLoadoutSlots.Position(TFClass.HeavyWeapons, TFLoadoutSlot.Primary));
        Assert.Equal(TFLoadoutSlots.Position(TFClass.Scout, TFLoadoutSlot.Misc2),
                     TFLoadoutSlots.Position(TFClass.Medic, TFLoadoutSlot.Misc2));
    }

    [Fact]
    public void TheSpyHidesItsPrimaryAndShowsItsSapperAndWatch()
    {
        // :131-153: primary 0, secondary 1, melee 2, building 4 (sapper),
        // pda 0 (disguise kit), pda2 3 (watch); the wearables as default.
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Primary));
        Assert.Equal(1, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Secondary));
        Assert.Equal(2, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Melee));
        Assert.Equal(4, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Building));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Pda));
        Assert.Equal(3, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Pda2));
        Assert.Equal(5, TFLoadoutSlots.Position(TFClass.Spy, TFLoadoutSlot.Head));
    }

    [Fact]
    public void TheEngineerShowsItsPdaWhereTheSpyShowsItsSapper()
    {
        // :156-178: pda 4, pda2 0, building 0.
        Assert.Equal(4, TFLoadoutSlots.Position(TFClass.Engineer, TFLoadoutSlot.Pda));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Engineer, TFLoadoutSlot.Pda2));
        Assert.Equal(0, TFLoadoutSlots.Position(TFClass.Engineer, TFLoadoutSlot.Building));
        Assert.Equal(1, TFLoadoutSlots.Position(TFClass.Engineer, TFLoadoutSlot.Primary));
    }

    [Fact]
    public void TheVisibleSlotsComeBackInPanelOrder()
    {
        // The default row's seven, in position order; the spy's and the
        // engineer's eight with their extra in the building/pda position.
        Assert.Equal(new[]
        {
            TFLoadoutSlot.Primary, TFLoadoutSlot.Secondary, TFLoadoutSlot.Melee,
            TFLoadoutSlot.Head, TFLoadoutSlot.Misc, TFLoadoutSlot.Misc2, TFLoadoutSlot.Action,
        }, TFLoadoutSlots.Visible(TFClass.Scout));

        Assert.Equal(new[]
        {
            TFLoadoutSlot.Secondary, TFLoadoutSlot.Melee, TFLoadoutSlot.Pda2, TFLoadoutSlot.Building,
            TFLoadoutSlot.Head, TFLoadoutSlot.Misc, TFLoadoutSlot.Misc2, TFLoadoutSlot.Action,
        }, TFLoadoutSlots.Visible(TFClass.Spy));

        Assert.Equal(new[]
        {
            TFLoadoutSlot.Primary, TFLoadoutSlot.Secondary, TFLoadoutSlot.Melee, TFLoadoutSlot.Pda,
            TFLoadoutSlot.Head, TFLoadoutSlot.Misc, TFLoadoutSlot.Misc2, TFLoadoutSlot.Action,
        }, TFLoadoutSlots.Visible(TFClass.Engineer));

        Assert.Empty(TFLoadoutSlots.Visible(TFClass.Civilian));
    }
}
