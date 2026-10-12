//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

// viewpostprocess.cpp's convars, declared here since that TU left client.so
// (C7): names, defaults, flags and help as stock declared them
// (ViewPostProcessConVarOracleTests holds each to the pristine file). The post
// program reads them by name (kPostCvars). Not here: the ten the engine itself
// defines (stock's declarations were children of the engine's; the native
// registration refuses a managed alias of one), which the post program reads
// by name all the same -- mat_fullbright, mat_hdr_level, mat_dynamic_tonemapping,
// mat_show_ab_hdr, mat_tonemapping_occlusion_use_stencil, mat_hdr_tonemapscale,
// mat_accelerate_adjust_exposure_down, mat_hdr_manual_tonemap_rate,
// mat_tonemap_algorithm and mat_force_tonemap_scale; and the pyro_* family,
// which is TF2.Client's (PyroConVars.cs).

[ConVar("mat_bloomscale", "1")]
public sealed class MatBloomscaleConVar : FloatConVar { }

[ConVar("mat_bloomamount_rate", "0.05f", Flags = ConVarFlags.Cheat)]
public sealed class MatBloomamountRateConVar : FloatConVar { }

[ConVar("mat_debug_postprocessing_effects", "0", Help = "0 = off, 1 = show post-processing passes in quadrants of the screen, 2 = only apply post-processing to the centre of the screen")]
public sealed class MatDebugPostprocessingEffectsConVar : FloatConVar { }

[ConVar("mat_debug_process_halfscreen", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatDebugProcessHalfscreenConVar : FloatConVar { }

[ConVar("mat_postprocessing_combine", "1", Help = "Combine bloom, software anti-aliasing and color correction into one post-processing pass")]
public sealed class MatPostprocessingCombineConVar : FloatConVar { }

[ConVar("mat_debug_autoexposure", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatDebugAutoexposureConVar : FloatConVar { }

[ConVar("mat_autoexposure_max", "2")]
public sealed class MatAutoexposureMaxConVar : FloatConVar { }

[ConVar("mat_autoexposure_min", "0.5")]
public sealed class MatAutoexposureMinConVar : FloatConVar { }

[ConVar("mat_show_histogram", "0")]
public sealed class MatShowHistogramConVar : FloatConVar { }

[ConVar("mat_hdr_uncapexposure", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatHdrUncapexposureConVar : FloatConVar { }

[ConVar("mat_force_bloom", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatForceBloomConVar : FloatConVar { }

[ConVar("mat_disable_bloom", "0")]
public sealed class MatDisableBloomConVar : FloatConVar { }

[ConVar("mat_debug_bloom", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatDebugBloomConVar : FloatConVar { }

[ConVar("mat_non_hdr_bloom_scalefactor", ".3")]
public sealed class MatNonHdrBloomScalefactorConVar : FloatConVar { }

[ConVar("mat_bloom_scalefactor_scalar", "1.0")]
public sealed class MatBloomScalefactorScalarConVar : FloatConVar { }

[ConVar("mat_exposure_center_region_x", "0.9", Flags = ConVarFlags.Cheat)]
public sealed class MatExposureCenterRegionXConVar : FloatConVar { }

[ConVar("mat_exposure_center_region_y", "0.85", Flags = ConVarFlags.Cheat)]
public sealed class MatExposureCenterRegionYConVar : FloatConVar { }

[ConVar("mat_exposure_center_region_x_flashlight", "0.9", Flags = ConVarFlags.Cheat)]
public sealed class MatExposureCenterRegionXFlashlightConVar : FloatConVar { }

[ConVar("mat_exposure_center_region_y_flashlight", "0.85", Flags = ConVarFlags.Cheat)]
public sealed class MatExposureCenterRegionYFlashlightConVar : FloatConVar { }

[ConVar("mat_tonemap_percent_target", "60.0", Flags = ConVarFlags.Cheat)]
public sealed class MatTonemapPercentTargetConVar : FloatConVar { }

[ConVar("mat_tonemap_percent_bright_pixels", "2.0", Flags = ConVarFlags.Cheat)]
public sealed class MatTonemapPercentBrightPixelsConVar : FloatConVar { }

[ConVar("mat_tonemap_min_avglum", "3.0", Flags = ConVarFlags.Cheat)]
public sealed class MatTonemapMinAvglumConVar : FloatConVar { }

[ConVar("mat_software_aa_strength", "-1.0", Flags = ConVarFlags.Archive, Help = "Software AA - perform a software anti-aliasing post-process (an alternative/supplement to MSAA). This value sets the strength of the effect: (0.0 - off), (1.0 - full)")]
public sealed class MatSoftwareAaStrengthConVar : FloatConVar { }

[ConVar("mat_software_aa_quality", "0", Flags = ConVarFlags.Archive, Help = "Software AA quality mode: (0 - 5-tap filter), (1 - 9-tap filter)")]
public sealed class MatSoftwareAaQualityConVar : FloatConVar { }

[ConVar("mat_software_aa_edge_threshold", "1.0", Flags = ConVarFlags.Archive, Help = "Software AA - adjusts the sensitivity of the software AA shader's edge detection (default 1.0 - a lower value will soften more edges, a higher value will soften fewer)")]
public sealed class MatSoftwareAaEdgeThresholdConVar : FloatConVar { }

[ConVar("mat_software_aa_blur_one_pixel_lines", "0.5", Flags = ConVarFlags.Archive, Help = "How much software AA should blur one-pixel thick lines: (0.0 - none), (1.0 - lots)")]
public sealed class MatSoftwareAaBlurOnePixelLinesConVar : FloatConVar { }

[ConVar("mat_software_aa_tap_offset", "1.0", Flags = ConVarFlags.Archive, Help = "Software AA - adjusts the displacement of the taps used by the software AA shader (default 1.0 - a lower value will make the image sharper, higher will make it blurrier)")]
public sealed class MatSoftwareAaTapOffsetConVar : FloatConVar { }

[ConVar("mat_software_aa_debug", "0", Help = "Software AA debug mode: (0 - off), (1 - show number of 'unlike' samples: 0->black, 1->red, 2->green, 3->blue), (2 - show anti-alias blend strength), (3 - show averaged 'unlike' colour)")]
public sealed class MatSoftwareAaDebugConVar : FloatConVar { }

[ConVar("mat_software_aa_strength_vgui", "-1.0", Flags = ConVarFlags.Archive, Help = "Same as mat_software_aa_strength, but forced to this value when called by the post vgui AA pass.")]
public sealed class MatSoftwareAaStrengthVguiConVar : FloatConVar { }

[ConVar("mat_dump_rts", "0")]
public sealed class MatDumpRtsConVar : FloatConVar { }

[ConVar("r_queued_post_processing", "0")]
public sealed class RQueuedPostProcessingConVar : FloatConVar { }

[ConVar("mat_postprocess_x", "4")]
public sealed class MatPostprocessXConVar : FloatConVar { }

[ConVar("mat_postprocess_y", "1")]
public sealed class MatPostprocessYConVar : FloatConVar { }

[ConVar("mat_motion_blur_forward_enabled", "0")]
public sealed class MatMotionBlurForwardEnabledConVar : FloatConVar { }

[ConVar("mat_motion_blur_falling_min", "10.0")]
public sealed class MatMotionBlurFallingMinConVar : FloatConVar { }

[ConVar("mat_motion_blur_falling_max", "20.0")]
public sealed class MatMotionBlurFallingMaxConVar : FloatConVar { }

[ConVar("mat_motion_blur_falling_intensity", "1.0")]
public sealed class MatMotionBlurFallingIntensityConVar : FloatConVar { }

[ConVar("mat_motion_blur_rotation_intensity", "1.0")]
public sealed class MatMotionBlurRotationIntensityConVar : FloatConVar { }

[ConVar("mat_motion_blur_strength", "1.0")]
public sealed class MatMotionBlurStrengthConVar : FloatConVar { }
