using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

// THE ROPE CONVARS, which c_rope.cpp declared as file statics and which moved
// with the simulation. Names, defaults, help and bounds as stock spelled them
// (docs/novalve-snapshot/client-c0/client-convars-contract.txt is the check).
// mat_fullbright is NOT here: c_rope.cpp's copy was a child of the engine's
// ("get it from the engine"), and the rope reads the engine's by name.

[ConVar("rope_shake", "0")]
public sealed class RopeShakeConVar : IntConVar { }

[ConVar("rope_subdiv", "2", Help = "Rope subdivision amount", Min = 0, Max = RopeBuilder.MaxSubdivs)]
public sealed class RopeSubdivConVar : IntConVar { }

[ConVar("rope_collide", "1", Help = "Collide rope with the world")]
public sealed class RopeCollideConVar : IntConVar { }

[ConVar("rope_smooth", "1", Help = "Do an antialiasing effect on ropes")]
public sealed class RopeSmoothConVar : IntConVar { }

[ConVar("rope_smooth_enlarge", "1.4", Help = "How much to enlarge ropes in screen space for antialiasing effect")]
public sealed class RopeSmoothEnlargeConVar : FloatConVar { }

[ConVar("rope_smooth_minwidth", "0.3", Help = "When using smoothing, this is the min screenspace width it lets a rope shrink to")]
public sealed class RopeSmoothMinWidthConVar : FloatConVar { }

[ConVar("rope_smooth_minalpha", "0.2", Help = "Alpha for rope antialiasing effect")]
public sealed class RopeSmoothMinAlphaConVar : FloatConVar { }

[ConVar("rope_smooth_maxalphawidth", "1.75")]
public sealed class RopeSmoothMaxAlphaWidthConVar : FloatConVar { }

[ConVar("rope_smooth_maxalpha", "0.5", Help = "Alpha for rope antialiasing effect")]
public sealed class RopeSmoothMaxAlphaConVar : FloatConVar { }

[ConVar("r_drawropes", "1", Flags = ConVarFlags.Cheat)]
public sealed class DrawRopesConVar : IntConVar { }

[ConVar("r_queued_ropes", "1")]
public sealed class QueuedRopesConVar : IntConVar { }

[ConVar("r_ropetranslucent", "1")]
public sealed class RopeTranslucentConVar : IntConVar { }

[ConVar("r_rope_holiday_light_scale", "0.055", Flags = ConVarFlags.DevelopmentOnly)]
public sealed class RopeHolidayLightScaleConVar : FloatConVar { }

[ConVar("r_ropes_holiday_lights_allowed", "1", Flags = ConVarFlags.DevelopmentOnly)]
public sealed class RopesHolidayLightsAllowedConVar : IntConVar { }

[ConVar("rope_wind_dist", "1000", Help = "Don't use CPU applying small wind gusts to ropes when they're past this distance.")]
public sealed class RopeWindDistConVar : FloatConVar { }

[ConVar("rope_averagelight", "1", Help = "Makes ropes use average of cubemap lighting instead of max intensity.")]
public sealed class RopeAverageLightConVar : IntConVar { }

[ConVar("rope_rendersolid", "1")]
public sealed class RopeRenderSolidConVar : IntConVar { }

[ConVar("rope_solid_minwidth", "0.3")]
public sealed class RopeSolidMinWidthConVar : FloatConVar { }

[ConVar("rope_solid_maxwidth", "1")]
public sealed class RopeSolidMaxWidthConVar : FloatConVar { }

[ConVar("rope_solid_minalpha", "0.0")]
public sealed class RopeSolidMinAlphaConVar : FloatConVar { }

[ConVar("rope_solid_maxalpha", "1")]
public sealed class RopeSolidMaxAlphaConVar : FloatConVar { }

/// <summary>The rope ConVars as one dependency, for the rope system's reads.</summary>
public sealed class RopeConVars(
    RopeShakeConVar shake, RopeSubdivConVar subdiv, RopeCollideConVar collide, RopeSmoothConVar smooth,
    RopeSmoothEnlargeConVar smoothEnlarge, RopeSmoothMinWidthConVar smoothMinWidth,
    RopeSmoothMinAlphaConVar smoothMinAlpha, RopeSmoothMaxAlphaWidthConVar smoothMaxAlphaWidth,
    RopeSmoothMaxAlphaConVar smoothMaxAlpha, DrawRopesConVar drawRopes, RopeTranslucentConVar translucent,
    RopeWindDistConVar windDist, RopeAverageLightConVar averageLight, RopeRenderSolidConVar renderSolid,
    RopeSolidMinWidthConVar solidMinWidth, RopeSolidMaxWidthConVar solidMaxWidth,
    RopeSolidMinAlphaConVar solidMinAlpha, RopeSolidMaxAlphaConVar solidMaxAlpha)
{
    public RopeSimSettings Sim => new(drawRopes.Value != 0, shake.Value != 0, collide.Value, windDist.Value,
                                      averageLight.Value != 0);

    public bool DrawRopes => drawRopes.Value != 0;
    public bool Translucent => translucent.Value != 0;
    public bool Smooth => smooth.Value != 0;
    public bool RenderSolid => renderSolid.Value != 0;
    public int Subdiv => subdiv.Value;
    public float SmoothEnlarge => smoothEnlarge.Value;
    public float SmoothMinWidth => smoothMinWidth.Value;
    public float SmoothMinAlpha => smoothMinAlpha.Value;
    public float SmoothMaxAlphaWidth => smoothMaxAlphaWidth.Value;
    public float SmoothMaxAlpha => smoothMaxAlpha.Value;
    public float SolidMinWidth => solidMinWidth.Value;
    public float SolidMaxWidth => solidMaxWidth.Value;
    public float SolidMinAlpha => solidMinAlpha.Value;
    public float SolidMaxAlpha => solidMaxAlpha.Value;
}
