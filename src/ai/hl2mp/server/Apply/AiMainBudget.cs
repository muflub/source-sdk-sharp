using System.Diagnostics;
using System.Globalization;
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
public sealed class AiMainBudget(IEngineEnvironment environment, IEngineConsole console, IEngineHost host,
                                 AiBudgetConVar on, AiBudgetFloorConVar floor, AiBudgetWaitConVar wait)
{
    /// <summary>The engine's idle spin before a frame on Linux, a listen server's (CEngine::Frame's <c>fBusyWaitMS</c>).</summary>
    public const double ListenSpinUs = 1500d;

    /// <summary>How many ticks <c>fps_max</c>'s value is kept for: a lookup by name, so not every tick.</summary>
    public const int TargetReadTicks = 64;

    private int _sinceRead = int.MaxValue / 2;
    private double _targetUs;
    private double _spinUs;

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

        if (++_sinceRead >= TargetReadTicks)
        {
            _sinceRead = 0;
            bool dedicated = host.IsDedicatedServer;
            _targetUs = dedicated ? g.IntervalPerTick * 1e6 : TargetOf(console.FindConVarValue("fps_max"));
            _spinUs = dedicated ? 0d : ListenSpinUs;
        }

        bool capped = on.Value && !sync;
        Measure.Begin(g.FrameCount, new BudgetFrame(_targetUs, _spinUs, floor.Value, wait.Value, capped));
        Current = Measure;
    }

    /// <summary>A frame's length at <paramref name="fpsMax"/>, the convar's text; 0 for none.</summary>
    public static double TargetOf(string? fpsMax)
        => float.TryParse(fpsMax, NumberStyles.Float, CultureInfo.InvariantCulture, out float fps) && fps > 0f ? 1e6 / fps : 0d;
}
