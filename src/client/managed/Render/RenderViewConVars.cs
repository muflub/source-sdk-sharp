//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

// view.cpp's, viewrender.cpp's and viewdebug.cpp's convars, declared here since
// those TUs left client.so (C7 step 3): names, defaults, flags, help and bounds
// as stock declared them (RenderViewConVarOracleTests holds each to the
// pristine files). The plan and the executor read them by name. Not here: the
// four native code still links against by object -- mat_wireframe,
// r_DrawDetailProps and cl_drawshadowtexture (fork TUs' `extern ConVar`) and
// cl_software_cursor (its change callback sets the vgui surface), which
// shell_viewrender_client.cpp defines -- and pyro_dof, TF2.Client's
// (PyroConVars.cs). Nor the five the engine itself defines (stock's
// declarations were children of the engine's; the registration refuses a
// managed alias of one), which the plan reads by name all the same:
// r_drawtranslucentworld, r_flashlightdrawdepth, r_visocclusion,
// r_waterforceexpensive and r_waterforcereflectentities.

[ConVar("zoom_sensitivity_ratio", "1.0", Flags = ConVarFlags.Archive, Help = "Additional mouse sensitivity scale factor applied when FOV is zoomed in.")]
public sealed class ZoomSensitivityRatioConVar : FloatConVar { }

[ConVar("v_centermove", "0.15")]
public sealed class VCentermoveConVar : FloatConVar { }

[ConVar("v_centerspeed", "500")]
public sealed class VCenterspeedConVar : FloatConVar { }

[ConVar("viewmodel_fov", "54", Flags = ConVarFlags.Cheat, Help = "Sets the field-of-view for the viewmodel.", Min = 0.1f, Max = 179.9f)]
public sealed class ViewmodelFovConVar : FloatConVar { }

[ConVar("mat_viewportscale", "1.0", Flags = ConVarFlags.Archive, Help = "Scale down the main viewport (to reduce GPU impact on CPU profiling)", Min = 1.0f / 640.0f, Max = 1.0f)]
public sealed class MatViewportscaleConVar : FloatConVar { }

[ConVar("mat_viewportupscale", "1", Flags = ConVarFlags.Archive, Help = "Scale the viewport back up")]
public sealed class MatViewportupscaleConVar : FloatConVar { }

[ConVar("cl_leveloverview", "0", Flags = ConVarFlags.Cheat)]
public sealed class ClLeveloverviewConVar : FloatConVar { }

[ConVar("r_mapextents", "16384", Flags = ConVarFlags.Cheat, Help = "Set the max dimension for the map.  This determines the far clipping plane")]
public sealed class RMapextentsConVar : FloatConVar { }

[ConVar("gl_clear", "0")]
public sealed class GlClearConVar : FloatConVar { }

[ConVar("gl_clear_randomcolor", "0", Flags = ConVarFlags.Cheat, Help = "Clear the back buffer to random colors every frame. Helps spot open seams in geometry.")]
public sealed class GlClearRandomcolorConVar : FloatConVar { }

[ConVar("r_farz", "-1", Flags = ConVarFlags.Cheat, Help = "Override the far clipping plane. -1 means to use the value in env_fog_controller.")]
public sealed class RFarzConVar : FloatConVar { }

[ConVar("cl_demoviewoverride", "0", Help = "Override view during demo playback")]
public sealed class ClDemoviewoverrideConVar : FloatConVar { }

[ConVar("cl_maxrenderable_dist", "3000", Flags = ConVarFlags.Cheat, Help = "Max distance from the camera at which things will be rendered")]
public sealed class ClMaxrenderableDistConVar : FloatConVar { }

[ConVar("r_entityclips", "1")]
public sealed class REntityclipsConVar : FloatConVar { }

[ConVar("r_drawopaqueworld", "1", Flags = ConVarFlags.Cheat)]
public sealed class RDrawopaqueworldConVar : FloatConVar { }

[ConVar("r_3dsky", "1", Help = "Enable the rendering of 3d sky boxes")]
public sealed class R3dskyConVar : FloatConVar { }

[ConVar("r_skybox", "1", Flags = ConVarFlags.Cheat, Help = "Enable the rendering of sky boxes")]
public sealed class RSkyboxConVar : FloatConVar { }

[ConVar("r_drawviewmodel", "1", Flags = ConVarFlags.Cheat)]
public sealed class RDrawviewmodelConVar : FloatConVar { }

[ConVar("r_drawtranslucentrenderables", "1", Flags = ConVarFlags.Cheat)]
public sealed class RDrawtranslucentrenderablesConVar : FloatConVar { }

[ConVar("r_drawopaquerenderables", "1", Flags = ConVarFlags.Cheat)]
public sealed class RDrawopaquerenderablesConVar : FloatConVar { }

[ConVar("r_threaded_renderables", "0")]
public sealed class RThreadedRenderablesConVar : FloatConVar { }

[ConVar("r_worldlistcache", "1")]
public sealed class RWorldlistcacheConVar : FloatConVar { }

[ConVar("fog_override", "0", Flags = ConVarFlags.Cheat)]
public sealed class FogOverrideConVar : FloatConVar { }

[ConVar("fog_start", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogStartConVar : FloatConVar { }

[ConVar("fog_end", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogEndConVar : FloatConVar { }

[ConVar("fog_color", "-1 -1 -1", Flags = ConVarFlags.Cheat)]
public sealed class FogColorConVar : StringConVar { }

[ConVar("fog_enable", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogEnableConVar : FloatConVar { }

[ConVar("fog_startskybox", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogStartskyboxConVar : FloatConVar { }

[ConVar("fog_endskybox", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogEndskyboxConVar : FloatConVar { }

[ConVar("fog_maxdensityskybox", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogMaxdensityskyboxConVar : FloatConVar { }

[ConVar("fog_colorskybox", "-1 -1 -1", Flags = ConVarFlags.Cheat)]
public sealed class FogColorskyboxConVar : StringConVar { }

[ConVar("fog_enableskybox", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogEnableskyboxConVar : FloatConVar { }

[ConVar("fog_maxdensity", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogMaxdensityConVar : FloatConVar { }

[ConVar("fog_radial", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogRadialConVar : FloatConVar { }

[ConVar("fog_radialskybox", "-1", Flags = ConVarFlags.Cheat)]
public sealed class FogRadialskyboxConVar : FloatConVar { }

[ConVar("r_debugcheapwater", "0", Flags = ConVarFlags.Cheat)]
public sealed class RDebugcheapwaterConVar : FloatConVar { }

[ConVar("r_WaterDrawRefraction", "1", Help = "Enable water refraction")]
public sealed class RWaterDrawRefractionConVar : FloatConVar { }

[ConVar("r_WaterDrawReflection", "1", Help = "Enable water reflection")]
public sealed class RWaterDrawReflectionConVar : FloatConVar { }

[ConVar("r_ForceWaterLeaf", "1", Help = "Enable for optimization to water - considers view in leaf under water for purposes of culling")]
public sealed class RForceWaterLeafConVar : FloatConVar { }

[ConVar("mat_drawwater", "1", Flags = ConVarFlags.Cheat)]
public sealed class MatDrawwaterConVar : FloatConVar { }

[ConVar("mat_clipz", "1")]
public sealed class MatClipzConVar : FloatConVar { }

[ConVar("r_screenfademinsize", "0")]
public sealed class RScreenfademinsizeConVar : FloatConVar { }

[ConVar("r_screenfademaxsize", "0")]
public sealed class RScreenfademaxsizeConVar : FloatConVar { }

[ConVar("cl_drawmonitors", "1")]
public sealed class ClDrawmonitorsConVar : FloatConVar { }

[ConVar("r_eyewaterepsilon", "10.0f", Flags = ConVarFlags.Cheat)]
public sealed class REyewaterepsilonConVar : FloatConVar { }

[ConVar("r_drawopaquestaticpropslast", "0", Help = "Whether opaque static props are rendered after non-npcs")]
public sealed class RDrawopaquestaticpropslastConVar : FloatConVar { }

[ConVar("r_drawopaque_old", "0", Help = "Whether old unbucketed technique is used")]
public sealed class RDrawopaqueOldConVar : FloatConVar { }

[ConVar("r_drawopaquesbucket", "0", Flags = ConVarFlags.Cheat, Help = "Draw only specific bucket: positive - props, negative - ents")]
public sealed class RDrawopaquesbucketConVar : FloatConVar { }

[ConVar("r_drawopaquesbucket_stats", "0", Flags = ConVarFlags.Cheat, Help = "Draw distribution of props/ents in the buckets")]
public sealed class RDrawopaquesbucketStatsConVar : FloatConVar { }

[ConVar("cl_drawmaterial", "", Flags = ConVarFlags.Cheat, Help = "Draw a particular material over the frame")]
public sealed class ClDrawmaterialConVar : StringConVar { }

[ConVar("mat_showwatertextures", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatShowwatertexturesConVar : FloatConVar { }

[ConVar("mat_wateroverlaysize", "256")]
public sealed class MatWateroverlaysizeConVar : FloatConVar { }

[ConVar("mat_showframebuffertexture", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatShowframebuffertextureConVar : FloatConVar { }

[ConVar("mat_framebuffercopyoverlaysize", "256")]
public sealed class MatFramebuffercopyoverlaysizeConVar : FloatConVar { }

[ConVar("mat_showcamerarendertarget", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatShowcamerarendertargetConVar : FloatConVar { }

[ConVar("mat_camerarendertargetoverlaysize", "256", Flags = ConVarFlags.Cheat)]
public sealed class MatCamerarendertargetoverlaysizeConVar : FloatConVar { }

[ConVar("mat_hsv", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatHsvConVar : FloatConVar { }

[ConVar("mat_yuv", "0", Flags = ConVarFlags.Cheat)]
public sealed class MatYuvConVar : FloatConVar { }

[ConVar("cl_overdraw_test", "0", Flags = ConVarFlags.Cheat | ConVarFlags.NeverAsString)]
public sealed class ClOverdrawTestConVar : FloatConVar { }

[ConVar("mat_drawTexture", "", Help = "Enable debug view texture")]
public sealed class MatDrawTextureConVar : StringConVar { }

[ConVar("mat_drawTextureScale", "1.0", Help = "Debug view texture scale")]
public sealed class MatDrawTextureScaleConVar : FloatConVar { }

[ConVar("mat_drawColorRamp", "0", Help = "Draw color test pattern (0=Off, 1=[0..255], 2=[0..127]")]
public sealed class MatDrawColorRampConVar : FloatConVar { }

[ConVar("mat_showlightmappage", "-1")]
public sealed class MatShowlightmappageConVar : FloatConVar { }

[ConVar("cl_shadowtextureoverlaysize", "256", Flags = ConVarFlags.Cheat)]
public sealed class ClShadowtextureoverlaysizeConVar : FloatConVar { }

