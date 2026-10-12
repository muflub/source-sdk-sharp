using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>Client console commands. See the server-side equivalent.</summary>
[ConCommand("cs_hello_client", Help = "Say hello from C# (client)")]
public sealed class HelloClientCommand(ILog log) : IConCommand
{
    public void Execute(string args)
        => log.Msg($"[SourceSharp/Client] hello from C#! args='{args}'\n");
}

[ConCommand("cs_status_client", Help = "Print SourceSharp client status")]
public sealed class StatusClientCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientFrameStats _stats;

    /// <summary>The module's counters.</summary>
    public StatusClientCommand(ILog log, IClientFrameStats stats)
    {
        _log = log;
        _stats = stats;
    }

    public void Execute(string args)
    {
        _log.Msg($"[SourceSharp/Client] {SharedInfo.Version}\n");
        _log.Msg($"[SourceSharp/Client] frames since level init: {_stats.FrameCount}\n");
        _log.Msg($"[SourceSharp/Client] user commands seen by C#: {_stats.CommandCount}\n");
    }
}

//-----------------------------------------------------------------------------
// THE BLOCKER-2 AND BLOCKER-3 PROBES.
//
// gib_verdict_test.cpp's blockers 2 and 3 were "the standalone hierarchy has no
// per-instance skin or model scale" and "there is no managed way to make a
// ragdoll from a model". All three are answered now, and these three commands
// are how the answers are SEEN.
//
// They are here rather than left to a unit test because of what a unit test
// cannot say. A test can prove that BecomeRagdoll returns true and that
// RagdollCreate was called; it cannot tell a body that hangs together from one
// that explodes on its first frame, or a skeleton scaled once from one scaled
// twice. Both of those are the actual failure modes, both are visible
// instantly, and neither has any other way to be provoked -- nothing in the
// game asks a managed entity for a skin, a size or a jointed body yet, because
// the gib port that would is still refused on its own separate grounds.
//
// CLIENT-SIDE and for this viewer only, exactly as cs_playseq is: the skin and
// the ragdoll are client facts, and the scale is set on this side's copy. That
// is a property of the probe rather than a limitation of the feature -- in
// production the value rides the payload from the server like any other
// managed state.
//-----------------------------------------------------------------------------

/// <summary>
/// Which skin every matching client entity draws with.
/// </summary>
[ConCommand("cs_setskin",
            Help = "cs_setskin <classname-filter> <n>. Draws every matching client " +
                   "entity with skin n, for this viewer only.")]
public sealed class SetSkinCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public SetSkinCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !int.TryParse(parts[1], out int skin))
        {
            _log.Msg("[SourceSharp/Client] cs_setskin <classname-filter> <n>\n");
            return;
        }

        int touched = 0;

        foreach (var entity in _standalones.Live())
        {
            if (!entity.ClassName.Contains(parts[0], StringComparison.OrdinalIgnoreCase))
                continue;

            _render.SetSkin(entity.Handle, skin);
            touched++;
        }

        _log.Msg($"[SourceSharp/Client] cs_setskin: {touched} entities now on skin {skin}\n");
    }
}

/// <summary>
/// How big every matching client entity wears its model.
/// </summary>
[ConCommand("cs_setscale",
            Help = "cs_setscale <classname-filter> <scale>. Resizes every matching " +
                   "client entity, for this viewer only. Zero and below are refused.")]
public sealed class SetModelScaleCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public SetModelScaleCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out float scale))
        {
            _log.Msg("[SourceSharp/Client] cs_setscale <classname-filter> <scale>\n");
            return;
        }

        int touched = 0;

        foreach (var entity in _standalones.Live())
        {
            if (!entity.ClassName.Contains(parts[0], StringComparison.OrdinalIgnoreCase))
                continue;

            _render.SetModelScale(entity.Handle, scale);
            touched++;
        }

        // The NATIVE side refuses zero and below and says so per entity; this
        // reports what was ASKED rather than what was accepted, deliberately.
        // A probe that quietly reported success for a refused value would be a
        // probe that cannot show the refusal working.
        _log.Msg($"[SourceSharp/Client] cs_setscale: asked {touched} entities for scale {scale}\n");
    }
}

/// <summary>
/// Every matching client entity becomes a jointed body.
/// </summary>
[ConCommand("cs_ragdoll",
            Help = "cs_ragdoll <classname-filter> [fx fy fz]. Turns every matching " +
                   "client entity into a ragdoll, for this viewer only.")]
public sealed class BecomeRagdollCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly Ragdolls.IClientRagdolls _ragdolls;

    public BecomeRagdollCommand(ILog log, IClientStandalones standalones, Ragdolls.IClientRagdolls ragdolls)
    {
        _log = log;
        _standalones = standalones;
        _ragdolls = ragdolls;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 1)
        {
            _log.Msg("[SourceSharp/Client] cs_ragdoll <classname-filter> [fx fy fz]\n");
            return;
        }

        Vector force = default;

        if (parts.Length >= 4 &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fx) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float fy) &&
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float fz))
        {
            force = new Vector(fx, fy, fz);
        }

        int asked = 0;
        int became = 0;

        foreach (var entity in _standalones.Live())
        {
            if (!entity.ClassName.Contains(parts[0], StringComparison.OrdinalIgnoreCase))
                continue;

            asked++;

            if (_ragdolls.Become(entity.Handle, force, -1, out _))
                became++;
        }

        // BOTH NUMBERS, and the gap between them is the point rather than a
        // failure. A ragdoll needs a solid per limb in the model's collision
        // data and most models declare exactly one, so "asked 14, 3 became
        // ragdolls" is the correct report for a map full of oil drums with
        // three head gibs in it — and a run where the two numbers are equal on
        // a mixed set is the suspicious one.
        _log.Msg($"[SourceSharp/Client] cs_ragdoll: asked {asked}, {became} became " +
                $"ragdolls (the rest have no jointed body in their .phy)\n");
    }
}

/// <summary>
/// The client half of <c>cs_vtable</c>: acquire <c>IVEngineClient</c> and
/// exercise the four calls that DO NOT go through the vtable.
///
/// <para>
/// The four proxies are the only new C++ this refactor added, and each exists
/// because managed cannot express the call at all — <c>Con_NPrintf</c> and
/// <c>Con_NXPrintf</c> are varargs, <c>GetLightForPoint</c> and its Fast twin
/// return <c>Vector</c> by value. New native code that nothing calls is new
/// native code nobody has seen work, which is why this is here and not only a
/// unit test.
/// </para>
/// </summary>
[ConCommand("cs_vtable_client", Help = "Acquire IVEngineClient through its vtable and "
                                     + "exercise the four C++ proxies")]
public sealed class VtableClientCommand(ModuleEngine engine, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        // Through this composition's own engine (plan_abi_hotreload D8).
        IVtableEngineClient? client = EngineClient.Acquire(engine, out string why);

        if (client is null)
        {
            log.Msg($"[SourceSharp/Vtable] IVEngineClient: REFUSED — {why}\n");
            return;
        }

        // Through the vtable.
        log.Msg($"[SourceSharp/Vtable] IVEngineClient: bound, LevelName() = "
                + $"{client.LevelName()}, GameDirectory() = {client.GameDirectory()}, "
                + $"ProtocolVersion() = {client.ProtocolVersion()}\n");

        // Through the proxies. The printf pair writes to the debug overlay, so
        // there is nothing to read back — what is being shown is that the call
        // returns rather than faulting, which for a varargs slot reached the
        // wrong way is the failure mode.
        client.ConsolePrintAt(1, "cs_vtable_client: Con_NPrintf reached");
        client.ConsolePrintAt(2, 5f, 0f, 1f, 0f, false,
                              "cs_vtable_client: Con_NXPrintf reached");

        log.Msg("[SourceSharp/Vtable] Con_NPrintf and Con_NXPrintf returned\n");

        var origin = new Vector(0f, 0f, 64f);

        log.Msg(client.TryLightForPoint(origin, true, out Vector light)
            ? $"[SourceSharp/Vtable] GetLightForPoint(0,0,64) = "
              + $"{light.X:0.###} {light.Y:0.###} {light.Z:0.###}\n"
            : "[SourceSharp/Vtable] GetLightForPoint: no answer\n");

        log.Msg(client.TryLightForPointFast(origin, true, out Vector fast)
            ? $"[SourceSharp/Vtable] GetLightForPointFast(0,0,64) = "
              + $"{fast.X:0.###} {fast.Y:0.###} {fast.Z:0.###}\n"
            : "[SourceSharp/Vtable] GetLightForPointFast: no answer\n");
    }
}
