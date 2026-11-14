using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Ctf;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

public sealed class FlagCarriersTests
{
    private static CtfRig Map()
    {
        var rig = new CtfRig();
        // The carrier's pick-up fires through the events; the stub answers none.
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        rig.Engine.Returns("Entities.IsValid", true);
        return rig;
    }

    [Fact]
    public void The_carriers_seam_answers_the_flag_the_rules_walk()
    {
        var rig = Map();
        ItemTeamFlag red = rig.Flag(TFTeam.Red);
        EntityHandle carrier = rig.Players.Add(3, TFTeam.Blue);
        EntityHandle other = rig.Players.Add(4, TFTeam.Red);
        rig.Registry.Touch(red.Handle, carrier);

        var carriers = new FlagCarriers(rig.Registry);

        Assert.Equal(red, carriers.CarriedBy(carrier));
        Assert.Null(carriers.CarriedBy(other));
        Assert.Null(carriers.CarriedBy(EntityHandle.None));
    }

    [Fact]
    public void The_carriers_seam_loses_the_answer_when_the_flag_leaves_the_carrier()
    {
        var rig = Map();
        ItemTeamFlag red = rig.Flag(TFTeam.Red);
        EntityHandle carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, carrier);
        var carriers = new FlagCarriers(rig.Registry);

        red.RemoveFromCarrier(carrier);
        Assert.Null(carriers.CarriedBy(carrier));   // dropped: no longer stolen

        red.ResetFlag();
        Assert.Null(carriers.CarriedBy(carrier));
    }
}
