extern alias client;

using Xunit;

using Team = client::TF2.Shared.TFTeam;
using Limits = client::TF2.Shared.Resources.ObjectiveLimits;
using State = client::TF2.Shared.Resources.ObjectiveResourceNetState;

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

    /// <summary>Seed for the FINALCAP countdown: Blue caps point 1 with one
    /// capper and the point's warn sound on the wire.</summary>
    private static void SeedFinalCap(State state)
    {
        state.NumControlPoints = 3;
        state.Owner[1] = (byte)Red;
        state.CappingTeam[1] = (byte)Blue;
        state.TeamInZone[1] = (byte)Blue;
        state.NumTeamMembers[Limits.TeamArray(1, Blue)] = 1;
        state.TeamCapTime[Limits.TeamArray(1, Blue)] = 20f;
        state.WarnOnCap[1] = 1;                         // CP_WARN_FINALCAP
        state.WarnSoundIndexes[1] = 1;                  // the table's second entry
        state.PreviousPoints[Limits.PreviousArray(1, Blue, 0)] = 1;   // requires itself: always cappable
    }

    /// <summary>Stock's ClientThink warn arm (c_team_objectiveresource.cpp:398-424):
    /// while another team counts a FINALCAP point down, the local player hears
    /// the point's own warn name — the engine sound table's name at the wire's
    /// index — played through the think that spends the cap time.</summary>
    [Fact]
    public void An_enemy_final_cap_plays_the_points_warn_name_once()
    {
        var rig = Rig(Red);
        rig.Scripts.Names.AddRange(["_zero", "cp_warn_final"]);
        rig.Arrive(SeedFinalCap, now: 0f);

        rig.Source.CapProgress(1, 6f);

        Assert.Equal(["cp_warn_final"], rig.Sounds.Emitted.Select(s => s.Name).ToArray());
    }

    /// <summary>The next think of the same running cap does not replay it: the
    /// per-point warned flag holds until the capping team changes.</summary>
    [Fact]
    public void The_next_think_of_the_same_cap_does_not_replay_the_warn()
    {
        var rig = Rig(Red);
        rig.Scripts.Names.AddRange(["_zero", "cp_warn_final"]);
        rig.Arrive(SeedFinalCap, now: 0f);
        rig.Source.CapProgress(1, 6f);

        rig.Source.CapProgress(1, 7f);

        Assert.Single(rig.Sounds.Emitted);
        Assert.Equal("cp_warn_final", rig.Sounds.Emitted[0].Name);
    }

    /// <summary>The spam guard is the five-second stamp, not the flag alone: the
    /// flag clears when the cap deteriorates or the capping team changes, but a
    /// re-cap inside five seconds of the last warn stays silent until the
    /// window ends.</summary>
    [Fact]
    public void A_cleared_warn_flag_replays_only_after_the_five_second_window()
    {
        var rig = Rig(Red);
        rig.Scripts.Names.AddRange(["_zero", "cp_warn_final"]);
        rig.Arrive(SeedFinalCap, now: 0f);
        rig.Source.CapProgress(1, 6f);                  // the warn, stamped at 6

        rig.Arrive(s => s.NumTeamMembers[Limits.TeamArray(1, Blue)] = 0, now: 7f);
        rig.Source.CapProgress(1, 7f);                  // nobody on the point: the cap deteriorates and clears the flag

        rig.Arrive(s => s.NumTeamMembers[Limits.TeamArray(1, Blue)] = 1, now: 8f);
        rig.Source.CapProgress(1, 8f);                  // within the window: silent still
        Assert.Single(rig.Sounds.Emitted);

        rig.Source.CapProgress(1, 11.1f);               // past 6 + 5: the warn speaks again
        Assert.Equal(2, rig.Sounds.Emitted.Count);
    }

    /// <summary>The local player's own team capping needs no warn: the arm asks
    /// only when the capping team is not the player's own.</summary>
    [Fact]
    public void The_own_team_capping_a_finalcap_point_warns_nothing()
    {
        var rig = Rig(Blue);
        rig.Scripts.Names.AddRange(["_zero", "cp_warn_final"]);
        rig.Arrive(SeedFinalCap, now: 0f);

        rig.Source.CapProgress(1, 6f);

        Assert.Empty(rig.Sounds.Emitted);
    }

    /// <summary>A point at NORMAL with a warn sound of its own still plays only
    /// the generic announcer name: the contested arm never reads the warn sound
    /// (c_tf_objective_resource.cpp:140-155).</summary>
    [Fact]
    public void A_warn_named_point_at_normal_still_plays_only_the_generic_name()
    {
        var rig = Rig(Red);
        rig.Scripts.Names.AddRange(["_zero", "cp_warn_final"]);

        rig.Arrive(s =>
        {
            s.NumControlPoints = 3;
            s.Owner[1] = (byte)Red;
            s.CappingTeam[1] = (byte)Blue;
            s.WarnSoundIndexes[1] = 1;          // the table's second entry
        });

        Assert.Equal(["Announcer.ControlPointContested"], rig.Sounds.Emitted.Select(s => s.Name).ToArray());
    }
}
