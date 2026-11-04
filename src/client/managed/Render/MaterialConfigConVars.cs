using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>
/// Marks the convars the material system's config reads as material-thread ones, so a change to any of them is
/// queued and the engine leaves queued rendering before it looks at the config.
///
/// <para>
/// Unmarked, a change that reloads materials and nothing else is handled during queued rendering, under the
/// material system's own lock. Leaving the lock puts the main thread back on a queued context that has been
/// ended, and the lock's mutex stays held: the frame dies in SetRenderTarget, and moving the thread to another
/// context only turns that into a deadlock when queued rendering resumes. Stock hl2mp dies the same way, on
/// <c>mat_reducefillrate 1</c> in a map. r_waterforceexpensive is already marked; its change alone reloads
/// nothing, so the reload waits for the next change of a value here.
/// </para>
/// </summary>
internal sealed class MaterialConfigConVars(IClientConVarFlags flags, ILog log)
{
    /// <summary>The config's convars. None is written every frame: a marked write costs a frame of queued rendering.</summary>
    internal static IReadOnlyList<string> Names =>
    [
        "mat_vsync", "mat_trilinear", "mat_specular", "mat_bumpmap", "mat_phong", "mat_parallaxmap", "mat_reducefillrate",
        "mat_forceaniso", "mat_dxlevel", "mat_picmip", "mat_forcehardwaresync",
        "mat_slopescaledepthbias_decal", "mat_depthbias_decal", "mat_slopescaledepthbias_normal", "mat_depthbias_normal",
        "mat_slopescaledepthbias_shadowmap", "mat_depthbias_shadowmap",
        "mat_monitorgamma", "mat_monitorgamma_tv_range_min", "mat_monitorgamma_tv_range_max", "mat_monitorgamma_tv_exp",
        "mat_monitorgamma_tv_enabled", "mat_antialias", "mat_aaquality", "mat_diffuse", "mat_normalmaps",
        "mat_showlowresimage", "mat_measurefillrate", "mat_fillrate", "mat_filterlightmaps", "mat_filtertextures",
        "mat_mipmaptextures", "mat_showmiplevels", "mat_reversedepth", "mat_bufferprimitives", "mat_drawflat",
        "mat_softwarelighting", "mat_proxy", "mat_norendering", "mat_compressedtextures", "mat_fastspecular",
        "mat_fullbright", "mat_fastnobump", "mat_motion_blur_enabled", "mat_supportflashlight",
        "r_flashlightdepthtexture", "mat_vrmode_adapter",
    ];

    public void Mark()
    {
        foreach (string name in Names)
        {
            if (!flags.AddFlags(name, ConVarFlags.MaterialSystemThread))
                log.Msg($"[SourceSharp/Render] the material config has no convar {name}\n");
        }
    }
}
