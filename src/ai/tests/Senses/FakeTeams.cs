using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>A fact's <see cref="IRenderedTeams"/>: no teams unless told, then each player's by handle.</summary>
public sealed class FakeTeams : IRenderedTeams
{
    private readonly Dictionary<EntityHandle, int> _teams = new();
    private readonly Dictionary<int, int> _scores = new();

    public bool Teamplay { get; set; }

    public bool FriendlyFire { get; set; }

    public int Teams { get; set; } = 2;

    public int TeamCount => Teamplay ? Teams : 0;

    public FakeTeams Put(EntityHandle player, int team)
    {
        Teamplay = true;
        _teams[player] = team;
        return this;
    }

    public FakeTeams Scored(int team, int score)
    {
        _scores[team] = score;
        return this;
    }

    public int TeamOf(EntityHandle player) => Teamplay ? _teams.GetValueOrDefault(player) : 0;

    public int Score(int team) => Teamplay ? _scores.GetValueOrDefault(team) : 0;
}
