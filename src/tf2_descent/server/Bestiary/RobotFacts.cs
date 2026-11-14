using System.Collections.Immutable;

using SourceSharp.Abi;

using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Bestiary;

/// <summary>
/// One lent robot body as the bestiary reads it: the body's entity index and lend serial, the
/// robot's template (id and robots.json row), its life now and at most, its rolled resistances
/// and immunity (RU7's <see cref="RobotStats"/>), its temperament style and its champion
/// affixes. Every reference is the roster's own object: building one allocates nothing.
/// </summary>
public readonly record struct LentRobot(
    int Entity, int Serial, string Template, int TemplateRow, double Life, double MaxLife,
    ImmutableSortedDictionary<string, double> Resist, string? Immunity, string Style, ImmutableArray<string> Affixes);

/// <summary>The horde's lent bodies, for the robot views (UI6), and a body's entity (IMMUNE over the robot).</summary>
public interface IRobotFactsSource
{
    /// <summary>Fills <paramref name="into"/> (cleared first) with every lent body whose robot lives on the roster.</summary>
    void Lent(List<LentRobot> into);

    /// <summary>The entity index of pool body <paramref name="body"/>; 0 when there is no such body.</summary>
    int EntityOfBody(int body);
}

/// <summary><see cref="IRobotFactsSource"/> over this level's horde (<see cref="HordeService.Current"/>); nothing while there is none.</summary>
[SideService(Side.Server, As = [typeof(IRobotFactsSource)])]
public sealed class HordeRobotFacts(HordeService horde, IEngineEntities entities) : IRobotFactsSource
{
    public void Lent(List<LentRobot> into)
    {
        into.Clear();
        if (horde.Current is not { Begun: true, Roster: { } roster, Bodies: { } bodies })
            return;
        for (int b = 1; b <= bodies.LastBody; b++)
        {
            var body = bodies.Body(b);
            if (body.Parked || !roster.IsLive(body.Robot))
                continue;
            int i = body.Robot.Index;
            var stats = roster.Stats(i);
            int row = body.State.Template - 1;
            into.Add(new LentRobot(entities.GetIndex(body.Handle), body.Lease.Serial, roster.Template(i), row,
                                   roster.Life(i), stats.Life, stats.Resist, stats.Immunity, roster.Temperament(i).Style, roster.Affixes(i)));
        }
    }

    public int EntityOfBody(int body) =>
        horde.Current is { Bodies: { } bodies } && body >= 1 && body <= bodies.LastBody ? entities.GetIndex(bodies.Body(body).Handle) : 0;
}

/// <summary>The robot rows the bestiary's messages name by index, as the rules hold them (<see cref="RobotContentKeys"/>).</summary>
public static class RobotKeys
{
    public static uint Fingerprint(DescentContent content) => RobotContentKeys.Fingerprint(
        content.Robots.Rows.Select(r => r.Id), content.Champions.Rows.Select(r => r.Id),
        content.DamageTypes.Rows.Select(r => r.Id), content.Status.Rows.Select(r => r.Id));
}
