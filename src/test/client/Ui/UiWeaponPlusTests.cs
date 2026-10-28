using SourceSharp.Client.Ui.Screens;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The plus selection's grid against stock's Paint (xModifiers, yModifiers):
/// bucket 1 runs up, 2 right, 3 down, 4 left, 5 down and left, 6 down and right.
/// </summary>
public class UiWeaponPlusTests
{
    [Theory]
    [InlineData(0, -2, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(0, 1, 2)]
    [InlineData(-1, 0, 3)]
    [InlineData(-2, 2, 4)]
    [InlineData(3, 3, 5)]
    public void A_cell_on_an_arm_is_that_bucket_s(int x, int y, int slot)
        => Assert.Equal(slot, WeaponPlusPanel.ArmAt(x, y));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(1, -1)]
    [InlineData(-2, -2)]
    public void The_centre_and_cells_off_every_arm_are_no_bucket_s(int x, int y)
        => Assert.Equal(-1, WeaponPlusPanel.ArmAt(x, y));
}
