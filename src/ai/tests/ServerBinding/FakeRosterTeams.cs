using SourceSharp.Ai.Server.Teams;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// A fact's <see cref="IRosterTeams"/>: no teams unless <see cref="Teamplay"/>; a join
/// is obeyed at once (0 to the smaller team, people counted) and noted.
/// </summary>
public sealed class FakeRosterTeams : IRosterTeams
{
    public readonly Dictionary<ActorId, int> Bots = new();
    public readonly Dictionary<int, int> Humans = new();
    public readonly List<(ActorId Bot, int Asked)> Joins = new();

    public bool Teamplay { get; set; }

    public int Teams { get; set; } = 2;

    public int TeamCount => Teamplay ? Teams : 0;

    /// <summary>Joins are noted and not obeyed (a mode that refuses).</summary>
    public bool Refuses { get; set; }

    public int TeamOf(ActorId bot) => Bots.GetValueOrDefault(bot);

    public int HumansOn(int team) => Humans.GetValueOrDefault(team);

    /// <summary>The bots alive; every other bot is dead (the balance's default case).</summary>
    public readonly HashSet<ActorId> Alive = new();

    public bool IsAlive(ActorId bot) => Alive.Contains(bot);

    public int Count(int team) => HumansOn(team) + Bots.Values.Count(t => t == team);

    public void Join(ActorId bot, int team)
    {
        Joins.Add((bot, team));
        if (Refuses)
            return;
        Bots[bot] = team != 0 ? team : Enumerable.Range(2, Teams).OrderBy(Count).First();
    }
}
