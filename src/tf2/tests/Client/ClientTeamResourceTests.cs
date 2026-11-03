extern alias client;

using SourceSharp.Abi;
using Xunit;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using Entity = client::TF2.Client.Entities.ClientTFTeamResource;
using Limits = client::TF2.Shared.Resources.ObjectiveLimits;
using Team = client::TF2.Shared.TFTeam;
using TeamState = client::TF2.Shared.Resources.TeamResourceNetState;
using Teams = client::TF2.Client.Entities.TFClientTeamResources;

namespace TF2.Tests.Client;

/// <summary>
/// The client half of <c>tf_team_resource</c>: stock's per-team score, rounds,
/// flag captures, role and player count (<c>C_TFTeam</c> over <c>C_Team</c>),
/// as the scoreboard and the HUD read them.
/// </summary>
public sealed class ClientTeamResourceTests
{
    private const int Blue = (int)Team.Blue;
    private const int Red = (int)Team.Red;

    [Fact]
    public void A_packet_decodes_the_per_team_table()
    {
        var rig = new TeamRig();

        Assert.Equal(0, rig.Source.Score(Blue));
        Assert.Equal(0, rig.Source.RoundsWon(Blue));
        Assert.Equal(0, rig.Source.FlagCaptures(Blue));
        Assert.Equal(0, rig.Source.Role(Blue));
        Assert.Equal(0, rig.Source.PlayerCount(Blue));

        rig.Arrive(s =>
        {
            s.Score[Blue] = 12;
            s.RoundsWon[Blue] = 3;
            s.FlagCaptures[Blue] = 2;
            s.Role[Blue] = 2;
            s.PlayerCount[Blue] = 9;
        });

        Assert.Equal(12, rig.Source.Score(Blue));
        Assert.Equal(3, rig.Source.RoundsWon(Blue));
        Assert.Equal(2, rig.Source.FlagCaptures(Blue));
        Assert.Equal(2, rig.Source.Role(Blue));
        Assert.Equal(9, rig.Source.PlayerCount(Blue));
        Assert.Equal(0, rig.Source.Score(Red));

        Assert.Same(rig.Entity, rig.Source.Live);
    }

    [Fact]
    public void A_team_index_outside_the_table_reads_zero()
    {
        var rig = new TeamRig();
        rig.Arrive(s =>
        {
            s.Score[0] = 5;
            s.Score[Limits.Teams - 1] = 6;
        });

        Assert.Equal(5, rig.Source.Score(0));
        Assert.Equal(6, rig.Source.Score(Limits.Teams - 1));
        Assert.Equal(0, rig.Source.Score(Limits.Teams));
        Assert.Equal(0, rig.Source.Score(-1));
        Assert.Equal(0, rig.Source.RoundsWon(Limits.Teams));
        Assert.Equal(0, rig.Source.FlagCaptures(-1));
        Assert.Equal(0, rig.Source.Role(Limits.Teams));
        Assert.Equal(0, rig.Source.PlayerCount(-1));
    }

    [Fact]
    public void The_table_is_live_from_its_first_update_to_its_removal()
    {
        var rig = new TeamRig();
        Assert.Null(rig.Source.Live);

        rig.Entity.OnSpawn();
        Assert.Same(rig.Entity, rig.Source.Live);

        rig.Arrive(s => s.Score[Blue] = 1);
        Assert.Equal(1, rig.Source.Score(Blue));
        Assert.Equal(1, rig.Entity.Decodes);

        rig.Entity.OnRemoved();
        Assert.Null(rig.Source.Live);
        Assert.Equal(0, rig.Source.Score(Blue));
    }

    /// <summary>One team table over a bound source state, delivered as the registry delivers it.</summary>
    private sealed class TeamRig
    {
        public readonly Engine Engine = Engine.Default();

        public readonly Teams Source = new();

        public readonly TeamState State = new();

        public readonly Entity Entity;

        public TeamRig()
        {
            State.BindLocal();
            Entity = new Entity(Engine, Source);
            Entity.State.BindLocal();
        }

        public void Arrive(Action<TeamState>? write = null)
        {
            write?.Invoke(State);

            byte[] blob = State.Snapshot();
            Entity.State.Decode(blob);
            Entity.OnDataChanged(blob, DataUpdateType.DataTableChanged);
        }
    }
}
