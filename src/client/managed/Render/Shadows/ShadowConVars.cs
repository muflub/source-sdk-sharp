//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Flashlight;

namespace SourceSharp.Client.Render.Shadows;

// clientshadowmgr.cpp's ConVars, declared here since the file left client.so
// (C7 cluster 12 S2): the five the C0 snapshot holds as client.so's
// (docs/novalve-snapshot/client-c0/client-convars-contract.txt, ClientDll),
// with stock's names, defaults and flags (ClientShadowSystemTests holds them to
// it). Not here: r_shadowrendertotexture, r_flashlightdepthtexture, r_shadows,
// r_shadows_gamecontrol and r_flashlight_version2, which the engine declares
// (stock's client copies were children of the engine's, "hook into engine's
// cvars", and a managed declaration of an engine name is refused: ConVarOwnership),
// read and set by name through the convar watch; and cl_drawshadowtexture,
// viewrender's (shell_viewrender_client.cpp), read by name.

/// <summary>r_flashlightdrawfrustum: every flashlight's frustum drawn.</summary>
[ConVar("r_flashlightdrawfrustum", "0")]
public sealed class FlashlightDrawFrustumConVar : BoolConVar { }

/// <summary>r_flashlightmodels: the flashlight lights models too.</summary>
[ConVar("r_flashlightmodels", "1")]
public sealed class FlashlightModelsConVar : BoolConVar { }

/// <summary>r_flashlightdepthres: the depth textures' size (stock's PC default; written back with what the card gave).</summary>
[ConVar("r_flashlightdepthres", "1024")]
public sealed class FlashlightDepthResConVar : IntConVar { }

/// <summary>Declared by stock and read by nothing (its threaded arm is not ported: dead in stock, 4048).</summary>
[ConVar("r_threaded_client_shadow_manager", "0")]
public sealed class ThreadedClientShadowManagerConVar : BoolConVar { }

/// <summary>r_shadowmaxrendered: the render-to-texture shadows drawn a frame before the rest fall back to their LOD.</summary>
[ConVar("r_shadowmaxrendered", "32")]
public sealed class ShadowMaxRenderedConVar : IntConVar { }

/// <summary>
/// The shadow manager's ConVars (<see cref="IShadowSettings"/>): its own
/// declarations, and the engine's five and viewrender's one through the
/// convar watch (the module routes the watch's changes to <see cref="OnChanged"/>).
/// </summary>
public sealed class ShadowConVars : IShadowSettings
{
    /// <summary>The ConVars the manager reads but does not declare: the engine's five and viewrender's one.</summary>
    public static string[] EngineNames() =>
        ["r_shadowrendertotexture", "r_flashlightdepthtexture", "r_shadows", "r_shadows_gamecontrol", "r_flashlight_version2", "cl_drawshadowtexture"];

    private readonly FlashlightDrawFrustumConVar _drawFrustum;
    private readonly FlashlightModelsConVar _models;
    private readonly FlashlightDepthResConVar _depthRes;
    private readonly ShadowMaxRenderedConVar _maxRendered;
    private readonly WatchedConVarValues _engine;
    private readonly IEngineConsole _console;

    public ShadowConVars(ConVarRegistry convars, WatchedConVarValues engine, IEngineConsole console)
    {
        _drawFrustum = convars.Get<FlashlightDrawFrustumConVar>() ?? throw new InvalidOperationException("r_flashlightdrawfrustum is not registered");
        _models = convars.Get<FlashlightModelsConVar>() ?? throw new InvalidOperationException("r_flashlightmodels is not registered");
        _depthRes = convars.Get<FlashlightDepthResConVar>() ?? throw new InvalidOperationException("r_flashlightdepthres is not registered");
        _maxRendered = convars.Get<ShadowMaxRenderedConVar>() ?? throw new InvalidOperationException("r_shadowmaxrendered is not registered");
        _engine = engine;
        _console = console;
    }

    /// <summary>Watches the six it reads by name.</summary>
    public void Start() => _engine.Watch(EngineNames());

    public bool OnChanged(int id, string newValue) => _engine.OnChanged(id, newValue);

    public bool RenderToTexture => _engine.Bool("r_shadowrendertotexture");

    public bool FlashlightDepthTexture => _engine.Bool("r_flashlightdepthtexture");

    public void SetFlashlightDepthTexture(int value) =>
        _console.SetConVarValueByName("r_flashlightdepthtexture", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public int FlashlightDepthResolution => _depthRes.Value;

    public void SetFlashlightDepthResolution(int value) => _depthRes.SetValue(value);

    public int Shadows => _engine.Int("r_shadows");

    public int ShadowMaxRendered => _maxRendered.Value;

    public int ShadowsGameControl => _engine.Int("r_shadows_gamecontrol");

    public void SetShadowsGameControl(int value) => _console.SetConVarValueByName("r_shadows_gamecontrol", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public bool FlashlightModels => _models.Value;

    public int FlashlightVersion2 => _engine.Int("r_flashlight_version2");

    public bool FlashlightDrawFrustum => _drawFrustum.Value;

    public int DrawShadowTexture => _engine.Int("cl_drawshadowtexture");
}

/// <summary>Stock's Msg and Warning, each a line.</summary>
public sealed class ShadowConsole(IEngineConsole console) : IShadowConsole
{
    public void Msg(string text) => console.Msg(text + "\n");

    public void Warning(string text) => console.Warning(text + "\n");
}

/// <summary>A stock CCommand's argv: the command's name, then its arguments.</summary>
internal static class ShadowCommandArgs
{
    public static IReadOnlyList<string> Of(string name, string args)
    {
        var argv = new List<string> { name };
        argv.AddRange(Soundscapes.SoundscapeArgs.Split(args));
        return argv;
    }
}

/// <summary><c>r_shadowdir [x y z]</c>.</summary>
[ConCommand("r_shadowdir", Help = "Set shadow direction", Flags = ConVarFlags.Cheat)]
public sealed class ShadowDirCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args) => shadows.Command(m => m.CommandShadowDir(ShadowCommandArgs.Of("r_shadowdir", args)));
}

/// <summary><c>r_shadowangles [pitch yaw roll]</c>.</summary>
[ConCommand("r_shadowangles", Help = "Set shadow angles", Flags = ConVarFlags.Cheat)]
public sealed class ShadowAnglesCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args) => shadows.Command(m => m.CommandShadowAngles(ShadowCommandArgs.Of("r_shadowangles", args)));
}

/// <summary><c>r_shadowcolor [r g b]</c>.</summary>
[ConCommand("r_shadowcolor", Help = "Set shadow color", Flags = ConVarFlags.Cheat)]
public sealed class ShadowColorCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args) => shadows.Command(m => m.CommandShadowColor(ShadowCommandArgs.Of("r_shadowcolor", args)));
}

/// <summary><c>r_shadowdist [d]</c>.</summary>
[ConCommand("r_shadowdist", Help = "Set shadow distance", Flags = ConVarFlags.Cheat)]
public sealed class ShadowDistCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args) => shadows.Command(m => m.CommandShadowDist(ShadowCommandArgs.Of("r_shadowdist", args)));
}

/// <summary><c>r_shadowblobbycutoff [area]</c>.</summary>
[ConCommand("r_shadowblobbycutoff", Help = "some shadow stuff", Flags = ConVarFlags.Cheat)]
public sealed class ShadowBlobbyCutoffCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args) => shadows.Command(m => m.CommandShadowBlobbyCutoff(ShadowCommandArgs.Of("r_shadowblobbycutoff", args)));
}

#if DEBUG
/// <summary><c>r_shadowbreak</c>, stock's _DEBUG build's.</summary>
[ConCommand("r_shadowbreak")]
public sealed class ShadowBreakCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args) => shadows.Command(m => m.CommandShadowBreak());
}
#endif

/// <summary>The fork's <c>cs_cl_shadow_report [reset]</c>.</summary>
[ConCommand("cs_cl_shadow_report",
    Help = "cs_cl_shadow_report [reset] — every client shadow: who it is for, where it was last projected from, and whether its renderable still resolves")]
public sealed class ShadowReportCommand(ClientShadowSystem shadows) : IConCommand
{
    public void Execute(string args)
    {
        bool reset = ShadowCommandArgs.Of("cs_cl_shadow_report", args) is { Count: > 1 } argv
                     && string.Equals(argv[1], "reset", StringComparison.OrdinalIgnoreCase);
        shadows.Command(m => m.PrintShadowReport(reset));
    }
}

/// <summary><c>cs_cl_shadow_stats [reset]</c>: the shadow manager's calls, managed time, queue and native runs per frame (S3).</summary>
[ConCommand("cs_cl_shadow_stats", Help = "cs_cl_shadow_stats [reset]: the client shadow manager's calls, managed time, queued entries and native runs per frame since the last reset.")]
public sealed class ShadowStatsCommand(ILog log, ShadowSystemStats stats) : IConCommand
{
    public void Execute(string args)
    {
        log.Msg(stats.Report());
        if (args.Trim() == "reset")
        {
            stats.Reset();
            log.Msg("[SourceSharp/Client] shadow manager: counters reset\n");
        }
    }
}
