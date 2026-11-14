using System.Collections.Immutable;
using Xunit;

namespace SourceSharp.Ai.Tests.Knowledge;

/// <summary>
/// The core's room for a class-based game (plan_bots_teams_tf2 C.2, C.3, C.6),
/// each with a default a game without it answers: an ability table, a map's
/// objectives, the scoreboard's team and class.
/// </summary>
public class GameKnowledgeShapeTests
{
    private static readonly WeaponKnowledge NoWeapons = new([]);

    [Fact]
    public void TheEmptyAbilityTableHasNoRow()
    {
        AbilityTable none = default;
        Assert.Equal(0, none.Count);
        Assert.False(none.TryGet(0, out _));
    }

    [Fact]
    public void AnAbilityTableAnswersItsRowsByIndexOnly()
    {
        var table = new AbilityTable([new AbilityRow("jump", AbilityRun.Sequence)]);
        Assert.True(table.TryGet(0, out AbilityRow row));
        Assert.Equal("jump", row.Name);
        Assert.False(table.TryGet(1, out _));
        Assert.False(table.TryGet(-1, out _));
    }

    [Fact]
    public void AMapWithoutThemHasNoAbilitiesAndNoObjectives()
    {
        var map = new MapKnowledge("dm", [], [], NoWeapons);
        Assert.Equal(0, map.Abilities.Count);
        Assert.Empty(map.Objectives.All);
    }

    [Fact]
    public void AMapCarriesTheAbilitiesAndObjectivesItIsGiven()
    {
        var flag = new Objective(1, ObjectiveKind.Carry, 2, 1 << 3, ObjectiveState.Home, new AiVector(10f, 20f, 0f));
        var map = new MapKnowledge("ctf", [], [], NoWeapons, abilities: new AbilityTable([new AbilityRow("cloak", AbilityRun.Command, "cloak")]),
                                   objectives: new ObjectiveKnowledge([flag], version: 4));
        Assert.Equal("cloak", map.Abilities.Rows[0].Command);
        Assert.Equal(flag, Assert.Single(map.Objectives.All));
        Assert.Equal(4, map.Objectives.Version);
    }

    [Fact]
    public void AScoreboardRowsTeamAndClassReachTheBoard()
    {
        var world = new MemoryWorld(new MapKnowledge("dm", [], [], NoWeapons), null, 0.015f, id => id.ToString());
        var memory = new BotMemory(new ActorId(1, 1), 33, MemoryParams.For(Trees.Duel.Preset("normal")), world, new HabitStore());
        var them = new ActorId(4, 1);
        memory.Apply(AiEvent.ScoreboardRow(new ActorId(1, 1), new Tick(10), them, 3, 1, rank: 2, team: 3, playerClass: 7));
        Assert.True(memory.Board.Of(them, make: false) is { BoardTeam: 3, BoardClass: 7, Frags: 3, Rank: 2 });
    }

    [Fact]
    public void AScoreboardRowWithoutTeamOrClassIsTheOldRow()
        // The recording's layout: a row with neither carries the zero vector it always did.
        => Assert.Equal(default, AiEvent.ScoreboardRow(new ActorId(1, 1), new Tick(10), new ActorId(4, 1), 3, 1, 2).Position);
}
