using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>How beams draw; off at 0, wireframe at 2. The engine holds it.</summary>
[EngineConVar("r_DrawBeams", Fallback = "1")]
public sealed class RDrawBeamsConVar : IntConVar { }

/// <summary>The local player's vision filter bits, which native declares.</summary>
[EngineConVar("localplayer_visionflags")]
public sealed class LocalPlayerVisionFlagsConVar : IntConVar { }

/// <summary>The shadow map's slope-scaled depth bias, the material system's.</summary>
[EngineConVar("mat_slopescaledepthbias_shadowmap")]
public sealed class MatSlopeScaleDepthBiasShadowMapConVar : FloatConVar { }

/// <summary>The shadow map's depth bias, the material system's.</summary>
[EngineConVar("mat_depthbias_shadowmap")]
public sealed class MatDepthBiasShadowMapConVar : FloatConVar { }

/// <summary>Whether flashlights use a depth texture.</summary>
[EngineConVar("r_flashlightdepthtexture")]
public sealed class RFlashlightDepthTextureConVar : BoolConVar { }

/// <summary>Whether shadows render to a texture.</summary>
[EngineConVar("r_shadowrendertotexture")]
public sealed class RShadowRenderToTextureConVar : BoolConVar { }

/// <summary>The engine's shadow switch.</summary>
[EngineConVar("r_shadows")]
public sealed class RShadowsConVar : IntConVar { }

/// <summary>Whether a map's shadow_control may turn shadows on.</summary>
[EngineConVar("r_shadows_gamecontrol")]
public sealed class RShadowsGameControlConVar : IntConVar { }

/// <summary>Which flashlight the shaders draw.</summary>
[EngineConVar("r_flashlight_version2")]
public sealed class RFlashlightVersion2ConVar : IntConVar { }

/// <summary>The debug draw of the shadow texture; native declares it.</summary>
[EngineConVar("cl_drawshadowtexture")]
public sealed class ClDrawShadowTextureConVar : IntConVar { }

/// <summary>Every area portal held open.</summary>
[EngineConVar("r_portalsopenall")]
public sealed class RPortalsOpenAllConVar : BoolConVar { }

/// <summary>Set while buildcubemaps runs; the engine's.</summary>
[EngineConVar("building_cubemaps")]
public sealed class BuildingCubemapsConVar : BoolConVar { }

/// <summary>The engine's decal switch.</summary>
[EngineConVar("r_decals")]
public sealed class RDecalsConVar : BoolConVar { }

/// <summary>Whether models take decals.</summary>
[EngineConVar("r_drawmodeldecals")]
public sealed class RDrawModelDecalsConVar : BoolConVar { }

/// <summary><c>mat_hdr_level</c>, which the render plan reads.</summary>
[EngineConVar("mat_hdr_level")]
public sealed class MatHdrLevelConVar : FloatConVar { }

/// <summary><c>mat_dynamic_tonemapping</c>, which the render plan reads.</summary>
[EngineConVar("mat_dynamic_tonemapping")]
public sealed class MatDynamicTonemappingConVar : FloatConVar { }

/// <summary><c>mat_show_ab_hdr</c>, which the render plan reads.</summary>
[EngineConVar("mat_show_ab_hdr")]
public sealed class MatShowAbHdrConVar : FloatConVar { }

/// <summary><c>mat_tonemapping_occlusion_use_stencil</c>, which the render plan reads.</summary>
[EngineConVar("mat_tonemapping_occlusion_use_stencil")]
public sealed class MatTonemappingOcclusionUseStencilConVar : FloatConVar { }

/// <summary><c>mat_hdr_tonemapscale</c>, which the render plan reads.</summary>
[EngineConVar("mat_hdr_tonemapscale")]
public sealed class MatHdrTonemapscaleConVar : FloatConVar { }

/// <summary><c>mat_accelerate_adjust_exposure_down</c>, which the render plan reads.</summary>
[EngineConVar("mat_accelerate_adjust_exposure_down")]
public sealed class MatAccelerateAdjustExposureDownConVar : FloatConVar { }

/// <summary><c>mat_hdr_manual_tonemap_rate</c>, which the render plan reads.</summary>
[EngineConVar("mat_hdr_manual_tonemap_rate")]
public sealed class MatHdrManualTonemapRateConVar : FloatConVar { }

/// <summary><c>mat_tonemap_algorithm</c>, which the render plan reads.</summary>
[EngineConVar("mat_tonemap_algorithm")]
public sealed class MatTonemapAlgorithmConVar : FloatConVar { }

/// <summary><c>mat_force_tonemap_scale</c>, which the render plan reads.</summary>
[EngineConVar("mat_force_tonemap_scale")]
public sealed class MatForceTonemapScaleConVar : FloatConVar { }

/// <summary><c>mat_colorcorrection</c>, which the render plan reads.</summary>
[EngineConVar("mat_colorcorrection")]
public sealed class MatColorcorrectionConVar : FloatConVar { }

/// <summary><c>mat_queue_mode</c>, which the render plan reads.</summary>
[EngineConVar("mat_queue_mode")]
public sealed class MatQueueModeConVar : FloatConVar { }

/// <summary><c>mat_motion_blur_enabled</c>, which the render plan reads.</summary>
[EngineConVar("mat_motion_blur_enabled")]
public sealed class MatMotionBlurEnabledConVar : FloatConVar { }

/// <summary><c>r_waterforceexpensive</c>, which the render plan reads.</summary>
[EngineConVar("r_waterforceexpensive")]
public sealed class RWaterforceexpensiveConVar : FloatConVar { }

/// <summary><c>r_waterforcereflectentities</c>, which the render plan reads.</summary>
[EngineConVar("r_waterforcereflectentities")]
public sealed class RWaterforcereflectentitiesConVar : FloatConVar { }

/// <summary><c>r_drawentities</c>, which the render plan reads.</summary>
[EngineConVar("r_drawentities")]
public sealed class RDrawentitiesConVar : FloatConVar { }

/// <summary><c>r_drawtranslucentworld</c>, which the render plan reads.</summary>
[EngineConVar("r_drawtranslucentworld")]
public sealed class RDrawtranslucentworldConVar : FloatConVar { }

/// <summary><c>r_DrawDetailProps</c>, which the render plan reads.</summary>
[EngineConVar("r_DrawDetailProps")]
public sealed class RDrawDetailPropsConVar : FloatConVar { }

/// <summary><c>r_flashlightdrawdepth</c>, which the render plan reads.</summary>
[EngineConVar("r_flashlightdrawdepth")]
public sealed class RFlashlightdrawdepthConVar : FloatConVar { }

/// <summary><c>sv_restrict_aspect_ratio_fov</c>, the server's replicated cap on a wide view's FOV.</summary>
[EngineConVar("sv_restrict_aspect_ratio_fov")]
public sealed class SvRestrictAspectRatioFovConVar : IntConVar { }

/// <summary>How sprites draw; native declares it.</summary>
[EngineConVar("r_drawsprites", Fallback = "1")]
public sealed class RDrawSpritesConVar : IntConVar { }

/// <summary>view.h's ShouldDrawInWireFrameMode: mat_wireframe, under sv_cheats.</summary>
public sealed class WireframeMode(SourceSharp.Shared.SvCheatsConVar cheats, MatWireframeConVar wireframe)
{
    public bool On => cheats.Value && wireframe.Value != 0;
}

/// <summary>The material system's wireframe mode.</summary>
[EngineConVar("mat_wireframe")]
public sealed class MatWireframeConVar : IntConVar { }

/// <summary>The material system's fullbright mode.</summary>
[EngineConVar("mat_fullbright")]
public sealed class MatFullbrightConVar : IntConVar { }

/// <summary>Whether particle systems thin themselves out.</summary>
[EngineConVar("mat_reduceparticles")]
public sealed class MatReduceParticlesConVar : BoolConVar { }

/// <summary>The stock glow outline's switch; on while nothing holds it.</summary>
[EngineConVar("glow_outline_effect_enable", Fallback = "1")]
public sealed class GlowOutlineEffectEnableConVar : BoolConVar { }

/// <summary>Whether the client interpolates; on while nothing holds it.</summary>
[EngineConVar("cl_interpolate", Fallback = "1")]
public sealed class ClInterpolateConVar : BoolConVar { }

/// <summary>Whether a single-player game simulates on alternate ticks.</summary>
[EngineConVar("sv_alternateticks")]
public sealed class SvAlternateTicksConVar : BoolConVar { }

/// <summary>The client's prediction switch, before the server's bound.</summary>
[EngineConVar("cl_predict")]
public sealed class ClPredictConVar : FloatConVar { }

/// <summary>The server's override of cl_predict; -1 for none.</summary>
[EngineConVar("sv_client_predict")]
public sealed class SvClientPredictConVar : FloatConVar { }

/// <summary>The client's interp, before the server's bound.</summary>
[EngineConVar("cl_interp")]
public sealed class ClInterpConVar : FloatConVar { }

/// <summary>The client's interp ratio, before the server's bound.</summary>
[EngineConVar("cl_interp_ratio")]
public sealed class ClInterpRatioConVar : FloatConVar { }

/// <summary>The client's update rate, before the server's bound.</summary>
[EngineConVar("cl_updaterate")]
public sealed class ClUpdateRateConVar : FloatConVar { }

/// <summary>The server's gravity, replicated. Stock's GetCurrentGravity on this side.</summary>
[EngineConVar("sv_gravity", Fallback = "600")]
public sealed class ClientSvGravityConVar : FloatConVar { }

/// <summary>Whether water splashes show; on while nothing holds it.</summary>
[EngineConVar("cl_show_splashes", Fallback = "1")]
public sealed class ClShowSplashesConVar : BoolConVar { }

/// <summary>Whether the engine found an Xbox controller.</summary>
[EngineConVar("joy_xcontroller_found")]
public sealed class JoyXControllerFoundConVar : BoolConVar { }
