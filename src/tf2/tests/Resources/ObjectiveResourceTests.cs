using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server.Entities;
using TF2.Shared;
using TF2.Shared.Resources;
using TF2.Tests.Client;

using Xunit;

namespace TF2.Tests.Resources;

/// <summary>
/// The objective table (stage 2.4): stock's CBaseTeamObjectiveResource
/// defaults and setters, and the net state the entity attaches.
/// </summary>
public sealed class ObjectiveResourceTests
{
    private const int Red = (int)TFTeam.Red;
    private const int Blue = (int)TFTeam.Blue;

    private static ObjectiveResourceNetState LocalState()
    {
        var state = new ObjectiveResourceNetState();
        state.BindLocal();
        return state;
    }

    [Fact]
    public void A_new_table_holds_stocks_spawn_defaults()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);

        // team_objectiveresource.cpp:136-182.
        Assert.Equal((byte)TFTeam.Unassigned, state.Owner[3]);
        Assert.Equal(-1, state.Group[3]);
        Assert.Equal(-1f, state.TimerTimes[3]);
        Assert.True(state.Visible[3]);
        Assert.True(state.InMiniRound[3]);
        Assert.True(state.CapRateScalesWithPlayers[3]);
        Assert.Equal(-1, state.PreviousPoints[ObjectiveLimits.PreviousArray(3, Blue, 2)]);
        Assert.Equal(-1, state.BaseControlPoints[Red]);
        Assert.Equal(-1f, state.CustomPositionX);
    }

    [Fact]
    public void Attaching_publishes_what_was_written_before_the_entity_existed()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        table.SetNumControlPoints(3);
        table.SetOwningTeam(2, Red);
        table.SetCPPosition(1, new Vector(10f, 20f, 30f));

        var state = LocalState();
        Assert.Equal(0, state.Owner[2]);

        table.Attach(state);

        Assert.Equal(3, state.NumControlPoints);
        Assert.Equal((byte)Red, state.Owner[2]);
        Assert.Equal(20f, state.Positions[1 * 3 + 1]);
    }

    [Fact]
    public void A_write_after_attaching_reaches_the_net_state()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);
        table.SetNumControlPoints(2);

        byte before = state.Owner[1];
        table.SetOwningTeam(1, Blue);

        Assert.NotEqual(before, state.Owner[1]);
        Assert.Equal((byte)Blue, state.Owner[1]);
    }

    [Fact]
    public void Setting_the_owner_clears_the_capping_team()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        table.SetNumControlPoints(1);
        table.SetCappingTeam(0, Blue);
        Assert.Equal(Blue, table.GetCappingTeam(0));

        table.SetOwningTeam(0, Red);

        Assert.Equal((int)TFTeam.Unassigned, table.GetCappingTeam(0));
    }

    [Fact]
    public void An_owner_past_the_point_count_reads_unassigned()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        table.SetNumControlPoints(1);
        table.SetOwningTeam(4, Red);

        Assert.Equal((int)TFTeam.Unassigned, table.GetOwningTeam(4));

        table.SetNumControlPoints(5);
        Assert.Equal(Red, table.GetOwningTeam(4));
    }

    [Fact]
    public void The_hud_parity_bumps_on_a_visible_change_and_wraps_at_its_bits()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        table.SetNumControlPoints(1);

        table.SetCapBlocked(0, true);
        Assert.Equal(1, table.UpdateCapHudParity);

        // No change, no bump (SetCapBlocked compares first).
        table.SetCapBlocked(0, true);
        Assert.Equal(1, table.UpdateCapHudParity);

        for (int i = 0; i < ObjectiveLimits.CapHudParityMask; i++)
            table.SetNumPlayers(0, Red, i);

        Assert.Equal(0, table.UpdateCapHudParity);
    }

    [Fact]
    public void The_lazy_percentage_follows_only_a_point_being_captured()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        table.SetNumControlPoints(2);
        table.SetCPCapPercentage(0, 0.25f);
        table.SetCPCapPercentage(1, 0.75f);
        table.SetCappingTeam(1, Blue);

        table.ObjectiveThink();

        Assert.Equal(0f, table.GetLazyCapPercentage(0));
        Assert.Equal(0.75f, table.GetLazyCapPercentage(1));
    }

    [Fact]
    public void Hill_data_appends_per_team_and_reset_clears_it()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);

        table.SetHillData(Red, 0.1f, 0.2f, downhill: true);
        table.SetHillData(Red, 0.5f, 0.6f, downhill: false);

        int red = Red * ObjectiveLimits.TrainMaxHills * ObjectiveLimits.TrainFloatsPerHill;
        Assert.Equal(2, state.NumNodeHillData[Red]);
        Assert.Equal(0.5f, state.NodeHillData[red + 2]);
        Assert.True(state.HillIsDownhill[Red * ObjectiveLimits.TrainMaxHills]);

        table.ResetHillData(Red);

        Assert.Equal(0, state.NumNodeHillData[Red]);
        Assert.Equal(0f, state.NodeHillData[red + 2]);
        Assert.False(state.HillIsDownhill[Red * ObjectiveLimits.TrainMaxHills]);
    }

    [Fact]
    public void The_cap_layout_keeps_stocks_buffer_length()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);

        table.SetCapLayoutInHud(new string('x', 40));

        Assert.Equal(ObjectiveLimits.MaxCapLayoutLength - 1, state.CapLayoutInHud.Length);
    }

    [Fact]
    public void A_point_or_team_outside_the_table_throws()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());

        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetOwningTeam(ObjectiveLimits.MaxControlPoints, Red));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetCPCapTime(0, ObjectiveLimits.Teams, 1f));
    }

    [Fact]
    public void The_mode_parts_reach_the_net_state()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);

        IModeObjectives mode = table;
        mode.SetTrainProgress(Blue, 0.4f);
        mode.SetTrainCappers(Blue, -1);
        mode.SetTrainSpeedLevel(Blue, 2);
        mode.SetTrainRecedeTime(Blue, 30.5f);
        mode.SetFlag(Red, status: 1, carrierIndex: 7, resetTime: 0f);
        mode.SetFlagPresent(Red, true);
        mode.SetFlagOrigin(Red, new Vector(100f, 200f, 300f));
        mode.SetZoneOrigin(Blue, new Vector(-10f, -20f, 0f));
        mode.SetMultipleTrains(true);

        Assert.Equal(0.4f, state.TrainProgress[Blue]);
        Assert.Equal(-1, state.TrainCappers[Blue]);
        Assert.Equal(2, state.TrainSpeedLevel[Blue]);
        Assert.Equal(30.5f, state.TrainRecedeTime[Blue], 3);
        Assert.Equal(1, state.FlagStatus[Red]);
        Assert.Equal(7, state.FlagCarrier[Red]);
        Assert.True(state.HasFlag[Red]);
        Assert.Equal(100f, state.FlagOrigin[Red * 3], 3);
        Assert.Equal(200f, state.FlagOrigin[Red * 3 + 1], 3);
        Assert.Equal(300f, state.FlagOrigin[Red * 3 + 2], 3);
        Assert.Equal(-10f, state.ZoneOrigin[Blue * 3], 3);
        Assert.Equal(-20f, state.ZoneOrigin[Blue * 3 + 1], 3);
        Assert.Equal(0f, state.ZoneOrigin[Blue * 3 + 2], 3);
        Assert.True(state.MultipleTrains);
    }

    [Fact]
    public void The_watchers_cart_values_return_to_their_defaults_on_clear()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);
        IModeObjectives mode = table;
        mode.SetTrainCappers(Blue, 3);
        mode.SetTrainSpeedLevel(Blue, 2);
        mode.SetTrainRecedeTime(Blue, 30.5f);
        mode.SetFlagPresent(Blue, true);
        mode.SetMultipleTrains(true);

        table.Clear();

        Assert.Equal(0, state.TrainCappers[Blue]);
        Assert.Equal(0, state.TrainSpeedLevel[Blue]);
        Assert.Equal(0f, state.TrainRecedeTime[Blue], 3);
        Assert.False(state.HasFlag[Blue]);
        Assert.False(state.MultipleTrains);
    }

    [Fact]
    public void Clear_returns_a_written_table_to_the_defaults()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);
        table.SetNumControlPoints(2);
        table.SetOwningTeam(1, Red);

        table.Clear();

        Assert.Equal(0, state.NumControlPoints);
        Assert.Equal(0, state.Owner[1]);
    }

    [Fact]
    public void The_table_fits_a_tier()
    {
        // 2,048 is the largest tier (generic_nettable.h).
        Assert.InRange(ObjectiveResourceNetState.NetLayout.Size, 1, 2048);
        Assert.Equal(ObjectiveClassNames.ObjectiveResource, ObjectiveResourceNetState.NetLayout.ClassName);
    }

    [Fact]
    public void The_entity_attaches_its_state_and_is_sent_to_everyone()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ObjectiveResourceEntity).Assembly);
        var services = TestServices.Services(out RecordingEngine engine);
        var table = new ObjectiveResource(new ServerWarnScripts());
        table.SetNumControlPoints(4);
        services.AddInstance<IObjectiveResource>(table);
        registry.Services = services;

        var handle = new EntityHandle(0x0021_0021u);
        registry.Spawn(handle, ObjectiveClassNames.ObjectiveResource);

        var entity = Assert.IsType<ObjectiveResourceEntity>(registry.Get(handle));
        Assert.Equal(4, entity.State.NumControlPoints);
        Assert.True(engine.Called("EntityRender.SetTransmitAlways"));

        table.SetOwningTeam(3, Blue);
        Assert.Equal((byte)Blue, entity.State.Owner[3]);
    }

    /// <summary>
    /// m_iszWarnSound on the wire (stock holds the name on the entity):
    /// SetWarnSound keeps the name for the server's reads and writes the
    /// engine sound-script index into the attached state, so the client's
    /// warn arm can read the name back through its half of the engine table.
    /// </summary>
    [Fact]
    public void A_warn_sound_rides_the_wire_as_its_engine_sound_index()
    {
        var table = new ObjectiveResource(new ServerWarnScripts("_zero", "cp_warn_final"));
        var state = LocalState();
        table.Attach(state);

        table.SetWarnSound(1, "cp_warn_final");

        Assert.Equal(1, state.WarnSoundIndexes[1]);     // the table's second entry
        Assert.Equal("cp_warn_final", table.GetWarnSound(1));
    }

    /// <summary>The index replays through Publish for a write made before the entity attached.</summary>
    [Fact]
    public void A_warn_sound_written_before_the_entity_attached_publishes_its_index()
    {
        var table = new ObjectiveResource(new ServerWarnScripts("_zero", "cp_warn_final"));
        table.SetWarnSound(2, "cp_warn_final");

        var state = LocalState();
        table.Attach(state);

        Assert.Equal(1, state.WarnSoundIndexes[2]);     // the table's second entry
    }

    /// <summary>A point with no warn sound publishes no index (-1): silence, not a lookup.</summary>
    [Fact]
    public void A_point_with_no_warn_sound_publishes_no_index()
    {
        var table = new ObjectiveResource(new ServerWarnScripts());
        var state = LocalState();
        table.Attach(state);

        Assert.Equal(-1, state.WarnSoundIndexes[3]);
    }
}
