using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Render;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientRender"/> over the render plan host (plan_client_novalve
/// C7, §4.4's <c>render</c> and <c>render/compare</c>). The compare is a write:
/// it answers only with sv_cheats on, and says so otherwise.
/// </summary>
public sealed class ClientDebugRender(RenderPlanHost plan, IEngineConsole console, Render.ObserverCamera? observer = null) : IDebugClientRender
{
    public DebugClientRender Read(bool resetTimes = false)
    {
        RenderCompareReading c = plan.CompareReading();
        static DebugClientRenderTimes Times((long Frames, double TotalMs, double MaxMs) t)
            => new(t.Frames, t.Frames > 0 ? t.TotalMs / t.Frames : 0, t.MaxMs);
        var report = new DebugClientRender(plan.Planned, plan.DeclinedToStock,
                                           plan.ViewPrograms, plan.ViewProgramsDeclined,
                                           plan.LastPlanKinds(),
                                           new DebugClientRenderCompare(c.Sequence, c.State, c.Mutated, c.Equal, c.Index, c.Stock, c.Plan,
                                                                        c.StockCalls, c.PlanCalls, c.StockFrame, c.PlanFrame, c.PlanNote, c.SceneDifference, c.MainView,
                                                                        c.StockCopyStart, c.PlanCopyStart),
                                           Times(plan.PlanTimes),
                                           observer is null ? null : new DebugClientObserverView(
                                               observer.Frames, observer.Last.HasValue,
                                               observer.Last is { } v ? [v.Origin.X, v.Origin.Y, v.Origin.Z] : [],
                                               observer.Last is { } w ? [w.Angles.Pitch, w.Angles.Yaw, w.Angles.Roll] : [],
                                               (observer.LastInputs?.ToString() ?? "") + " | " + observer.TargetNote),
                                           plan.MonitorPasses, plan.ViewDecisions);
        if (resetTimes)
            plan.ResetTimes();
        return report;
    }

    public DebugClientRenderRequest CompareFrozen(string path, bool mutate)
    {
        if (console.FindConVarValue("sv_cheats") is not "1")
            return new DebugClientRenderRequest(false, "a write: needs sv_cheats 1", plan.CompareReading().Sequence);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DebugClientRenderRequest(false, $"the frozen frame {path} is unreadable: {e.Message}", plan.CompareReading().Sequence);
        }
        RenderTrace frozen = RenderTrace.Parse(text);
        if (frozen.Calls.Count == 0)
            return new DebugClientRenderRequest(false, $"the frozen frame {path} holds no calls", plan.CompareReading().Sequence);
        return plan.RequestFrozenCompare(frozen, mutate, out int sequence)
            ? new DebugClientRenderRequest(true, mutate ? "armed against the frozen frame, with the plan frame's DrawLights dropped" : "armed against the frozen frame", sequence)
            : new DebugClientRenderRequest(false, "a compare is already running", sequence);
    }
}
