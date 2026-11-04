using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Ai.Hl2mp.Senses;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// The binding's <see cref="MainThreadBudget"/>: its clocks, and each frame's
/// settings from the convars and the engine.
///
/// <para>
/// A LISTEN SERVER protects its frame: the target is <c>fps_max</c>'s period, and
/// <c>fps_max 0</c> has no idle time, so the bots get the floor.
/// A DEDICATED SERVER has no render to protect and only the tick to keep: its
/// target is the tick interval, whatever <c>fps_max</c> says, and its wait does
/// not spin. So a bot waits only when the tick would not fit in its interval, and
/// the same floor and bound hold there.
/// </para>
/// <para><c>ai_sync 1</c> has no cap: every step in order, so the facts hold.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class AiMainBudget(IEngineEnvironment environment, FpsMaxConVar fpsMax, IEngineHost host,
                                 AiBudgetConVar on, AiBudgetFloorConVar floor, AiBudgetWaitConVar wait)
{
    /// <summary>The engine's idle spin before a frame on Linux, a listen server's (CEngine::Frame's <c>fBusyWaitMS</c>).</summary>
    public const double ListenSpinUs = 1500d;

    /// <summary>The measure, capped or not: what the bots use and how long frames take is read either way.</summary>
    public MainThreadBudget Measure { get; } = new(() => Stopwatch.GetTimestamp() * 1e6 / Stopwatch.Frequency, ThreadCpuClock.Micros());

    /// <summary>
    /// This tick's budget: every turn charged to it, and capped unless <c>ai_budget 0</c>
    /// or <c>ai_sync 1</c>, when its every turn runs and bots go in slot order as ever. Null with no engine.
    /// </summary>
    public MainThreadBudget? Current { get; private set; }

    /// <summary>The start of a tick (the bots' first main-thread work of it).</summary>
    public void Begin(bool sync)
    {
        Globals g = environment.Globals;
        if (!g.IsAvailable)
        {
            Current = null;
            return;
        }

        bool dedicated = host.IsDedicatedServer;
        double targetUs = dedicated ? g.IntervalPerTick * 1e6 : TargetOf(fpsMax.Value);
        double spinUs = dedicated ? 0d : ListenSpinUs;

        bool capped = on.Value && !sync;
        Measure.Begin(g.FrameCount, new BudgetFrame(targetUs, spinUs, floor.Value, wait.Value, capped));
        Current = Measure;
    }

    /// <summary>A frame's length at <paramref name="fps"/>; 0 for none.</summary>
    public static double TargetOf(float fps) => fps > 0f ? 1e6 / fps : 0d;
}
