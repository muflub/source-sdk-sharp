using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_globals</c> — what the managed side is reading out of the engine's
/// <c>CGlobalVars</c> right now, and whether it is the server's.
///
/// <para>
/// <b>The gate for plan_crossings.md Phase X1, callable at any moment.</b> The
/// binding itself reports its slot-vs-load agreement once, on the first clock
/// read of the process (<c>Engine.ReportAgreement</c>). That says the two paths
/// are the same memory; it does not say the pointer is still good ten minutes
/// and one level change later, and a pointer that survived load and then went
/// stale is the failure a "bound once, believed forever" design can have. So
/// this asks again, whenever it is asked.
/// </para>
///
/// <para>
/// <b>It goes through <see cref="IEngineEnvironment"/>, deliberately.</b> That
/// is the binding the game itself resolves — the one that was registered from a
/// parameterless constructor and answered <c>IsAvailable == false</c> in every
/// live session before this phase. Reading <c>Engine</c> directly would prove
/// the pointer arrived somewhere and say nothing about whether the thing game
/// code holds can see it.
/// </para>
///
/// <para>
/// <b>The side is reported rather than assumed.</b> A server holding the
/// client's globals is a plausible number, not a crash (globalvars_base.h gives
/// the client's <c>curtime</c> three different meanings), so the line prints
/// what <c>m_bClient</c> says next to the side that is executing. The binder
/// already refuses a mismatch; this is how a reader confirms the refusal never
/// had to fire.
/// </para>
/// </summary>
[ConCommand("cs_sv_globals",
    Help = "What the managed side reads out of the engine's CGlobalVars: availability, side, "
         + "curtime, tickcount, frametime")]
public sealed class GlobalsCommand : IConCommand
{
    private readonly IEngineEnvironment _environment;
    private readonly IEngineConsole _console;
    private readonly IEngineHost _host;

    public GlobalsCommand(IEngineEnvironment environment, IEngineConsole console, IEngineHost host)
    {
        _environment = environment;
        _console = console;
        _host = host;
    }

    public void Execute(string args)
    {
        Globals globals = _environment.Globals;

        if (!globals.IsAvailable)
        {
            _console.Msg($"[globals] side={_host.ActiveSide} available=false — "
                         + "the clock is on the ABI slot, not a load\n");
            return;
        }

        bool sideAgrees = globals.IsClient == (_host.ActiveSide == Side.Client);

        _console.Msg($"[globals] side={_host.ActiveSide} available=true pointer=0x{globals.Pointer:x} "
                     + $"m_bClient={globals.IsClient} sideAgrees={sideAgrees}\n");
        _console.Msg($"[globals] curtime={globals.CurTime} tickcount={globals.TickCount} "
                     + $"frametime={globals.FrameTime} realtime={globals.RealTime}\n");
        _console.Msg($"[globals] maxClients={globals.MaxClients} maxEntities={globals.MaxEntities} "
                     + $"interval_per_tick={globals.IntervalPerTick} serverCount={globals.ServerCount} "
                     + $"mapversion={globals.MapVersion}\n");

        if (!sideAgrees)
            _console.Msg("[globals] SIDE MISMATCH — these are not this side's globals\n");
    }
}
