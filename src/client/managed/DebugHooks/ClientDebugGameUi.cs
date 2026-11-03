using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientGameUi"/> over the GameUI call log
/// (plan_gameui_managed.md §4): each kept call's outcome is the host's
/// ruling (<see cref="GameUiHost.AnswersInertly"/>) when a host took it;
/// the last load's paint count is the load meter's.
/// </summary>
public sealed class ClientDebugGameUi(GameUiCallLog log, GameUiLoadMeter loads) : IDebugClientGameUi
{
    public DebugClientGameUi Read()
    {
        var counts = new List<DebugCount>();
        for (var m = GameUiMethod.Initialize; m <= GameUiMethod.ConsoleSetParent; m++)
        {
            if (log.Count(m) > 0)
                counts.Add(new DebugCount(m.ToString(), log.Count(m)));
        }
        var recent = new List<DebugClientGameUiCall>();
        foreach (var c in log.Recent())
        {
            string outcome = !c.Hosted ? "unhosted" : GameUiHost.AnswersInertly(c.Method) ? "inert" : "handled";
            recent.Add(new DebugClientGameUiCall(c.Sequence, c.Method.ToString(), c.Arguments, outcome, c.Repeats));
        }
        var last = loads.Last is { } l ? new DebugClientGameUiLoad(l.Dialog, l.Updates, l.Repaints, l.Frames) : null;
        return new DebugClientGameUi(log.Calls, log.Unhosted, counts, recent, loads.Loads, last);
    }
}
