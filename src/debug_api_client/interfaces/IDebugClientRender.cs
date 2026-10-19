namespace Debug.Api.Client.Interfaces;

/// <summary>
/// The last same-run A/B of the render plan (<c>/client/{slot}/render/compare</c>):
/// one frame drawn by stock and the next by the plan, both recorded, and where
/// their calls part.
/// </summary>
/// <param name="State">"none" (never asked), "stock" (waiting for the stock frame), "plan" (waiting for the plan frame) or "done".</param>
/// <param name="Mutated">The plan frame dropped one op on purpose (the control: a compare that cannot see that is no instrument).</param>
/// <param name="Index">The first call where they part, or -1.</param>
/// <param name="StockCopyStart">The frame-buffer copy texture the frozen frame began with, or null when it cannot say (RenderTrace.FrameStartCopyName).</param>
/// <param name="PlanCopyStart">The one the plan frame began with: session history its restore is named by.</param>
public sealed record DebugClientRenderCompare(int Sequence, string State, bool Mutated, bool Equal, int Index,
                                              string? Stock, string? Plan, int StockCalls, int PlanCalls,
                                              int StockFrame, int PlanFrame, string? PlanNote, string? SceneDifference = null,
                                              string? MainView = null, string? StockCopyStart = null, string? PlanCopyStart = null);

/// <summary>
/// The render plan as <c>/client/{slot}/render</c> reports it (plan_client_novalve
/// §4.4): the path in force, how many RenderViews the plan drew and handed back,
/// how many world views drew from managed's view program and how many fell back
/// to stock's primitives (C7 step 2), the last plan's ops, and the last compare.
/// </summary>
public sealed record DebugClientRender(long Planned, long DeclinedToStock, long ViewPrograms, long ViewProgramsDeclined,
                                       IReadOnlyList<string> LastPlan,
                                       DebugClientRenderCompare Compare, DebugClientRenderTimes Plan,
                                       DebugClientObserverView? Observer = null, long MonitorPasses = 0,
                                       long ViewDecisions = 0);

/// <summary>The observer's camera (C7 item 5): frames drawn from it, and the last view it handed over (null while the player's own eye draws).</summary>
public sealed record DebugClientObserverView(long Frames, bool Active, float[] Origin, float[] Angles, string Inputs = "");

/// <summary>How long CViewRender::Render took on one path, over the frames since the last reset (wall ms).</summary>
public sealed record DebugClientRenderTimes(long Frames, double MsAvg, double MsMax);

/// <summary>A compare asked for: whether it was taken, why not, and its sequence number.</summary>
public sealed record DebugClientRenderRequest(bool Accepted, string Reason, int Sequence);

/// <summary>The client's render plan (C7). Read on the channel's thread, answered from the game thread's last frame.</summary>
public interface IDebugClientRender
{
    /// <summary>The report; <paramref name="resetTimes"/> starts the per-path render times again after reading them.</summary>
    DebugClientRender Read(bool resetTimes = false);

    /// <summary>
    /// A write (sv_cheats): the frozen stock frame at <paramref name="path"/>
    /// (render-stock's recordings) against the next plan frame; <paramref name="mutate"/>
    /// drops one op from the plan frame (C7 step 3: the live compare once the
    /// stock path is gone).
    /// </summary>
    DebugClientRenderRequest CompareFrozen(string path, bool mutate);
}
