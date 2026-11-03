extern alias client;

using Xunit;

using Team = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The contested-point announcer (stock's C_TFObjectiveResource::SetCappingTeam,
/// c_tf_objective_resource.cpp:135-161): the start of a cap on a point that
/// warns, that the capping team does not own and the local player's team is
/// not capping, is announced from the local player's ears — contested on an
/// owned point, neutral on one with no owner.
/// </summary>
public sealed class ContestedCapTests
{
    private const int Blue = (int)Team.Blue;
    private const int Red = (int)Team.Red;

    private static ObjectiveRig Rig(int localTeam)
    {
        var rig = new ObjectiveRig();
        rig.Local.Team = localTeam;
        return rig;
    }

    private static void Cap(ObjectiveRig rig, int owner, int capping)
        => rig.Arrive(s =>
        {
            s.NumControlPoints = 3;
            s.Owner[1] = (byte)owner;
            s.CappingTeam[1] = (byte)capping;
        });

    [Fact]
    public void An_enemy_cap_on_a_point_our_team_owns_is_contested()
    {
        var rig = Rig(Red);

        Cap(rig, owner: Red, capping: Blue);

        Assert.Equal(["Announcer.ControlPointContested"], rig.Sounds.Emitted.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void An_enemy_cap_on_a_neutral_point_is_contested_neutral()
    {
        var rig = Rig(Red);

        Cap(rig, owner: (int)Team.Unassigned, capping: Blue);

        Assert.Equal(["Announcer.ControlPointContested_Neutral"], rig.Sounds.Emitted.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Our_own_cap_is_not_contested()
    {
        var rig = Rig(Blue);

        Cap(rig, owner: Red, capping: Blue);

        Assert.Empty(rig.Sounds.Emitted);
    }

    [Fact]
    public void A_point_that_warns_of_a_final_cap_is_not_contested()
    {
        var rig = Rig(Red);

        rig.Arrive(s =>
        {
            s.NumControlPoints = 3;
            s.Owner[1] = (byte)Red;
            s.CappingTeam[1] = (byte)Blue;
            s.WarnOnCap[1] = 1;                 // CP_WARN_FINALCAP
        });

        Assert.Empty(rig.Sounds.Emitted);
    }

    [Fact]
    public void No_team_starting_a_cap_is_not_contested()
    {
        var rig = Rig(Red);

        Cap(rig, owner: Red, capping: (int)Team.Unassigned);

        Assert.Empty(rig.Sounds.Emitted);
    }

    [Fact]
    public void A_capping_team_that_has_not_changed_is_not_announced_again()
    {
        var rig = Rig(Red);
        Cap(rig, owner: Red, capping: Blue);

        Cap(rig, owner: Red, capping: Blue);

        Assert.Single(rig.Sounds.Emitted);
    }
}
