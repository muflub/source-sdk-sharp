extern alias client;

using Xunit;

using MapOverlayCommand = client::TF2.Descent.Client.Ui.MapOverlayCommand;
using MapOverlayState = client::TF2.Descent.Client.Ui.MapOverlayState;

namespace TF2.Descent.Tests.Ui;

/// <summary>The map overlay's toggle: Tab's command shows the overlay, and the same key puts it down.</summary>
public sealed class MapOverlayStateTests
{
    [Fact]
    public void The_overlay_starts_down()
        => Assert.False(new MapOverlayState().Shown);

    [Fact]
    public void The_command_shows_the_overlay_and_a_second_press_puts_it_down()
    {
        var state = new MapOverlayState();
        var command = new MapOverlayCommand(state);

        command.Execute();
        Assert.True(state.Shown);
        command.Execute();
        Assert.False(state.Shown);
    }
}
