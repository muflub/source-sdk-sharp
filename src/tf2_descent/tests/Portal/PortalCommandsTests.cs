using TF2.Descent.Server.Portal;
using Xunit;

namespace TF2.Descent.Tests.Portal;

/// <summary>The portal's dev commands: open as the scroll does, list the records, use as standing on a pad does.</summary>
public sealed class PortalCommandsTests
{
    [Fact]
    public void The_open_command_opens_a_portal_with_no_scroll()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Level);

        new PortalOpenCommand(rig.Portals, rig.Log).Execute(PortalRig.Slot);

        Assert.Equal(PortalState.Open, Assert.Single(rig.Portals.All).State);
        Assert.Contains("[TF2Descent/Portal] tf2_descent_portal: opened for slot 1\n", rig.Log.Lines);
    }

    [Fact]
    public void The_open_command_says_why_it_was_refused()
    {
        var rig = new PortalRig();
        rig.Load(PortalRig.Town);

        new PortalOpenCommand(rig.Portals, rig.Log).Execute(PortalRig.Slot);

        Assert.Contains($"[TF2Descent/Portal] tf2_descent_portal: refused for slot 1: {TownPortals.InTown}\n", rig.Log.Lines);
    }

    [Fact]
    public void The_list_command_prints_every_record()
    {
        var rig = new PortalRig();
        rig.Opened();
        rig.Opened();

        new PortalListCommand(rig.Portals, rig.Log).Execute();

        string printed = rig.Log.Lines[^1];
        Assert.Contains("2 portal(s) on record", printed);
        Assert.Contains("portal 1: Gone", printed);
        Assert.Contains("portal 2: Open", printed);
    }

    [Fact]
    public void The_use_command_goes_up_as_standing_on_the_entrance_does()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();

        new PortalUseCommand(rig.Portals, rig.Log).Execute(PortalRig.Slot);

        Assert.Equal([$"up {PortalRig.Slot} {PortalRig.Hero} portal {portal.Id}"], rig.Route.Calls);
        Assert.Equal(PortalRig.Hero, portal.Arriving);
    }

    [Fact]
    public void The_use_command_with_a_spent_portals_number_is_refused()
    {
        var rig = new PortalRig();
        var portal = rig.Opened();
        rig.Load(PortalRig.Town);
        new PortalUseCommand(rig.Portals, rig.Log).Execute(PortalRig.Slot);
        rig.Load(PortalRig.Town);

        new PortalUseCommand(rig.Portals, rig.Log).Execute(PortalRig.Slot, portal.Id);

        Assert.Equal(PortalState.Spent, portal.State);
        Assert.Contains($"[TF2Descent/Portal] tf2_descent_portal_use: refused for slot 1: {TownPortals.Spent}\n", rig.Log.Lines);
    }
}
