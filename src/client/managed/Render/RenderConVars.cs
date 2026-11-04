using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>A convar read the way the plan reads it: as its float, its truncated int, or non-zero.</summary>
public static class ConVarAs
{
    public static float Cv(FloatConVar c) => c.Value;
    public static float Cv(IntConVar c) => c.Value;
    public static float Cv(BoolConVar c) => c.Value ? 1f : 0f;
    public static int CvInt(FloatConVar c) => (int)c.Value;
    public static int CvInt(IntConVar c) => c.Value;
    public static int CvInt(BoolConVar c) => c.Value ? 1 : 0;
    public static bool CvBool(FloatConVar c) => c.Value != 0f;
    public static bool CvBool(IntConVar c) => c.Value != 0;
    public static bool CvBool(BoolConVar c) => c.Value;
}

/// <summary>The convars RenderView's decisions read, each its own class.</summary>
public sealed class RenderConVars(RSkyboxConVar rSkybox, R3dskyConVar r3dsky, MatMotionBlurEnabledConVar matMotionBlurEnabled,
                                  BuildingCubemapsConVar buildingCubemaps, RFlashlightDepthTextureConVar rFlashlightDepthTexture,
                                  MatViewportscaleConVar matViewportscale, MatViewportupscaleConVar matViewportupscale,
                                  ClLeveloverviewConVar clLeveloverview, FogOverrideConVar fogOverride, FogStartConVar fogStart,
                                  FogEndConVar fogEnd, FogEnableConVar fogEnable, FogMaxdensityConVar fogMaxdensity,
                                  FogRadialConVar fogRadial, FogStartskyboxConVar fogStartskybox, FogEndskyboxConVar fogEndskybox,
                                  FogMaxdensityskyboxConVar fogMaxdensityskybox, FogEnableskyboxConVar fogEnableskybox,
                                  FogRadialskyboxConVar fogRadialskybox, MatDrawwaterConVar matDrawwater,
                                  RWaterforceexpensiveConVar rWaterforceexpensive,
                                  RWaterforcereflectentitiesConVar rWaterforcereflectentities,
                                  RWaterDrawReflectionConVar rWaterDrawReflection, RWaterDrawRefractionConVar rWaterDrawRefraction,
                                  RForceWaterLeafConVar rForceWaterLeaf, MatClipzConVar matClipz,
                                  REyewaterepsilonConVar rEyewaterepsilon, RDrawviewmodelConVar rDrawviewmodel,
                                  RDrawentitiesConVar rDrawentities, RDrawopaquerenderablesConVar rDrawopaquerenderables,
                                  RDrawtranslucentrenderablesConVar rDrawtranslucentrenderables,
                                  RDrawtranslucentworldConVar rDrawtranslucentworld, REntityclipsConVar rEntityclips,
                                  VCollideWireframeConVar vCollideWireframe, RDrawopaqueworldConVar rDrawopaqueworld,
                                  RDrawDetailPropsConVar rDrawDetailProps, ClMaxrenderableDistConVar clMaxrenderableDist,
                                  RWorldlistcacheConVar rWorldlistcache, MatYuvConVar matYuv, MatHsvConVar matHsv,
                                  MatShowlightmappageConVar matShowlightmappage, ClDrawShadowTextureConVar clDrawShadowTexture,
                                  ClShadowtextureoverlaysizeConVar clShadowtextureoverlaysize,
                                  MatShowwatertexturesConVar matShowwatertextures, MatWateroverlaysizeConVar matWateroverlaysize,
                                  MatShowcamerarendertargetConVar matShowcamerarendertarget,
                                  MatShowframebuffertextureConVar matShowframebuffertexture,
                                  MatFramebuffercopyoverlaysizeConVar matFramebuffercopyoverlaysize,
                                  MatDrawTextureScaleConVar matDrawTextureScale, RFlashlightdrawdepthConVar rFlashlightdrawdepth,
                                  ClOverdrawTestConVar clOverdrawTest, FogColorConVar fogColor, FogColorskyboxConVar fogColorskybox,
                                  RFarzConVar rFarz, RMapextentsConVar rMapextents, DefaultFovConVar defaultFov,
                                  SvRestrictAspectRatioFovConVar svRestrictAspectRatioFov, GlClearConVar glClear,
                                  GlClearRandomcolorConVar glClearRandomcolor,
                           ViewmodelFovConVar viewmodelFov)
{
    public readonly RSkyboxConVar RSkybox = rSkybox;
    public readonly R3dskyConVar R3dsky = r3dsky;
    public readonly MatMotionBlurEnabledConVar MatMotionBlurEnabled = matMotionBlurEnabled;
    public readonly BuildingCubemapsConVar BuildingCubemaps = buildingCubemaps;
    public readonly RFlashlightDepthTextureConVar RFlashlightDepthTexture = rFlashlightDepthTexture;
    public readonly MatViewportscaleConVar MatViewportscale = matViewportscale;
    public readonly MatViewportupscaleConVar MatViewportupscale = matViewportupscale;
    public readonly ClLeveloverviewConVar ClLeveloverview = clLeveloverview;
    public readonly FogOverrideConVar FogOverride = fogOverride;
    public readonly FogStartConVar FogStart = fogStart;
    public readonly FogEndConVar FogEnd = fogEnd;
    public readonly FogEnableConVar FogEnable = fogEnable;
    public readonly FogMaxdensityConVar FogMaxdensity = fogMaxdensity;
    public readonly FogRadialConVar FogRadial = fogRadial;
    public readonly FogStartskyboxConVar FogStartskybox = fogStartskybox;
    public readonly FogEndskyboxConVar FogEndskybox = fogEndskybox;
    public readonly FogMaxdensityskyboxConVar FogMaxdensityskybox = fogMaxdensityskybox;
    public readonly FogEnableskyboxConVar FogEnableskybox = fogEnableskybox;
    public readonly FogRadialskyboxConVar FogRadialskybox = fogRadialskybox;
    public readonly MatDrawwaterConVar MatDrawwater = matDrawwater;
    public readonly RWaterforceexpensiveConVar RWaterforceexpensive = rWaterforceexpensive;
    public readonly RWaterforcereflectentitiesConVar RWaterforcereflectentities = rWaterforcereflectentities;
    public readonly RWaterDrawReflectionConVar RWaterDrawReflection = rWaterDrawReflection;
    public readonly RWaterDrawRefractionConVar RWaterDrawRefraction = rWaterDrawRefraction;
    public readonly RForceWaterLeafConVar RForceWaterLeaf = rForceWaterLeaf;
    public readonly MatClipzConVar MatClipz = matClipz;
    public readonly REyewaterepsilonConVar REyewaterepsilon = rEyewaterepsilon;
    public readonly RDrawviewmodelConVar RDrawviewmodel = rDrawviewmodel;
    public readonly RDrawentitiesConVar RDrawentities = rDrawentities;
    public readonly RDrawopaquerenderablesConVar RDrawopaquerenderables = rDrawopaquerenderables;
    public readonly RDrawtranslucentrenderablesConVar RDrawtranslucentrenderables = rDrawtranslucentrenderables;
    public readonly RDrawtranslucentworldConVar RDrawtranslucentworld = rDrawtranslucentworld;
    public readonly REntityclipsConVar REntityclips = rEntityclips;
    public readonly VCollideWireframeConVar VCollideWireframe = vCollideWireframe;
    public readonly RDrawopaqueworldConVar RDrawopaqueworld = rDrawopaqueworld;
    public readonly RDrawDetailPropsConVar RDrawDetailProps = rDrawDetailProps;
    public readonly ClMaxrenderableDistConVar ClMaxrenderableDist = clMaxrenderableDist;
    public readonly RWorldlistcacheConVar RWorldlistcache = rWorldlistcache;
    public readonly MatYuvConVar MatYuv = matYuv;
    public readonly MatHsvConVar MatHsv = matHsv;
    public readonly MatShowlightmappageConVar MatShowlightmappage = matShowlightmappage;
    public readonly ClDrawShadowTextureConVar ClDrawShadowTexture = clDrawShadowTexture;
    public readonly ClShadowtextureoverlaysizeConVar ClShadowtextureoverlaysize = clShadowtextureoverlaysize;
    public readonly MatShowwatertexturesConVar MatShowwatertextures = matShowwatertextures;
    public readonly MatWateroverlaysizeConVar MatWateroverlaysize = matWateroverlaysize;
    public readonly MatShowcamerarendertargetConVar MatShowcamerarendertarget = matShowcamerarendertarget;
    public readonly MatShowframebuffertextureConVar MatShowframebuffertexture = matShowframebuffertexture;
    public readonly MatFramebuffercopyoverlaysizeConVar MatFramebuffercopyoverlaysize = matFramebuffercopyoverlaysize;
    public readonly MatDrawTextureScaleConVar MatDrawTextureScale = matDrawTextureScale;
    public readonly RFlashlightdrawdepthConVar RFlashlightdrawdepth = rFlashlightdrawdepth;
    public readonly ClOverdrawTestConVar ClOverdrawTest = clOverdrawTest;
    public readonly FogColorConVar FogColor = fogColor;
    public readonly FogColorskyboxConVar FogColorskybox = fogColorskybox;
    public readonly RFarzConVar RFarz = rFarz;
    public readonly RMapextentsConVar RMapextents = rMapextents;
    public readonly DefaultFovConVar DefaultFov = defaultFov;
    public readonly SvRestrictAspectRatioFovConVar SvRestrictAspectRatioFov = svRestrictAspectRatioFov;
    public readonly GlClearConVar GlClear = glClear;
    public readonly GlClearRandomcolorConVar GlClearRandomcolor = glClearRandomcolor;
    public readonly ViewmodelFovConVar ViewmodelFov = viewmodelFov;

    /// <summary>fog_color's three numbers, as sscanf( "%f%f%f" ) reads them; -1 for each it does not.</summary>
    public Vector FogColorValue => ParseColor(FogColor.Value);

    /// <summary>fog_colorskybox's three numbers, the same.</summary>
    public Vector FogColorskyboxValue => ParseColor(FogColorskybox.Value);

    internal static Vector ParseColor(string? text)
    {
        Span<float> rgb = [-1f, -1f, -1f];
        if (text is not null)
        {
            string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < 3 && i < words.Length; i++)
            {
                if (!float.TryParse(words[i], NumberStyles.Float, CultureInfo.InvariantCulture, out rgb[i]))
                {
                    rgb[i] = -1f;
                    break;
                }
            }
        }
        return new Vector(rgb[0], rgb[1], rgb[2]);
    }
}

/// <summary>The convars the post-processing planner reads, each its own class.</summary>
public sealed class PostProcessConVars(MatBloomscaleConVar matBloomscale, MatHdrLevelConVar matHdrLevel,
                                       MatBloomamountRateConVar matBloomamountRate,
                                       MatDebugPostprocessingEffectsConVar matDebugPostprocessingEffects,
                                       MatDebugProcessHalfscreenConVar matDebugProcessHalfscreen,
                                       MatPostprocessingCombineConVar matPostprocessingCombine,
                                       MatDynamicTonemappingConVar matDynamicTonemapping, MatShowAbHdrConVar matShowAbHdr,
                                       MatTonemappingOcclusionUseStencilConVar matTonemappingOcclusionUseStencil,
                                       MatDebugAutoexposureConVar matDebugAutoexposure, MatAutoexposureMaxConVar matAutoexposureMax,
                                       MatAutoexposureMinConVar matAutoexposureMin, MatShowHistogramConVar matShowHistogram,
                                       MatHdrTonemapscaleConVar matHdrTonemapscale, MatHdrUncapexposureConVar matHdrUncapexposure,
                                       MatForceBloomConVar matForceBloom, MatDisableBloomConVar matDisableBloom,
                                       MatDebugBloomConVar matDebugBloom,
                                       MatAccelerateAdjustExposureDownConVar matAccelerateAdjustExposureDown,
                                       MatHdrManualTonemapRateConVar matHdrManualTonemapRate,
                                       MatNonHdrBloomScalefactorConVar matNonHdrBloomScalefactor,
                                       MatBloomScalefactorScalarConVar matBloomScalefactorScalar,
                                       MatExposureCenterRegionXConVar matExposureCenterRegionX,
                                       MatExposureCenterRegionYConVar matExposureCenterRegionY,
                                       MatExposureCenterRegionXFlashlightConVar matExposureCenterRegionXFlashlight,
                                       MatExposureCenterRegionYFlashlightConVar matExposureCenterRegionYFlashlight,
                                       MatTonemapAlgorithmConVar matTonemapAlgorithm, MatTonemapPercentTargetConVar matTonemapPercentTarget,
                                       MatTonemapPercentBrightPixelsConVar matTonemapPercentBrightPixels,
                                       MatTonemapMinAvglumConVar matTonemapMinAvglum, MatFullbrightConVar matFullbright,
                                       MatForceTonemapScaleConVar matForceTonemapScale, MatSoftwareAaStrengthConVar matSoftwareAaStrength,
                                       MatSoftwareAaQualityConVar matSoftwareAaQuality,
                                       MatSoftwareAaEdgeThresholdConVar matSoftwareAaEdgeThreshold,
                                       MatSoftwareAaBlurOnePixelLinesConVar matSoftwareAaBlurOnePixelLines,
                                       MatSoftwareAaTapOffsetConVar matSoftwareAaTapOffset, MatSoftwareAaDebugConVar matSoftwareAaDebug,
                                       MatSoftwareAaStrengthVguiConVar matSoftwareAaStrengthVgui,
                                       MatColorcorrectionConVar matColorcorrection, MatPostprocessXConVar matPostprocessX,
                                       MatPostprocessYConVar matPostprocessY, MatDumpRtsConVar matDumpRts, MatQueueModeConVar matQueueMode,
                                       BuildingCubemapsConVar buildingCubemaps, LocalPlayerVisionFlagsConVar localPlayerVisionFlags,
                                       MatMotionBlurEnabledConVar matMotionBlurEnabled,
                                       MatMotionBlurForwardEnabledConVar matMotionBlurForwardEnabled,
                                       MatMotionBlurFallingMinConVar matMotionBlurFallingMin,
                                       MatMotionBlurFallingMaxConVar matMotionBlurFallingMax,
                                       MatMotionBlurFallingIntensityConVar matMotionBlurFallingIntensity,
                                       MatMotionBlurRotationIntensityConVar matMotionBlurRotationIntensity,
                                       MatMotionBlurStrengthConVar matMotionBlurStrength)
{
    public readonly MatBloomscaleConVar MatBloomscale = matBloomscale;
    public readonly MatHdrLevelConVar MatHdrLevel = matHdrLevel;
    public readonly MatBloomamountRateConVar MatBloomamountRate = matBloomamountRate;
    public readonly MatDebugPostprocessingEffectsConVar MatDebugPostprocessingEffects = matDebugPostprocessingEffects;
    public readonly MatDebugProcessHalfscreenConVar MatDebugProcessHalfscreen = matDebugProcessHalfscreen;
    public readonly MatPostprocessingCombineConVar MatPostprocessingCombine = matPostprocessingCombine;
    public readonly MatDynamicTonemappingConVar MatDynamicTonemapping = matDynamicTonemapping;
    public readonly MatShowAbHdrConVar MatShowAbHdr = matShowAbHdr;
    public readonly MatTonemappingOcclusionUseStencilConVar MatTonemappingOcclusionUseStencil = matTonemappingOcclusionUseStencil;
    public readonly MatDebugAutoexposureConVar MatDebugAutoexposure = matDebugAutoexposure;
    public readonly MatAutoexposureMaxConVar MatAutoexposureMax = matAutoexposureMax;
    public readonly MatAutoexposureMinConVar MatAutoexposureMin = matAutoexposureMin;
    public readonly MatShowHistogramConVar MatShowHistogram = matShowHistogram;
    public readonly MatHdrTonemapscaleConVar MatHdrTonemapscale = matHdrTonemapscale;
    public readonly MatHdrUncapexposureConVar MatHdrUncapexposure = matHdrUncapexposure;
    public readonly MatForceBloomConVar MatForceBloom = matForceBloom;
    public readonly MatDisableBloomConVar MatDisableBloom = matDisableBloom;
    public readonly MatDebugBloomConVar MatDebugBloom = matDebugBloom;
    public readonly MatAccelerateAdjustExposureDownConVar MatAccelerateAdjustExposureDown = matAccelerateAdjustExposureDown;
    public readonly MatHdrManualTonemapRateConVar MatHdrManualTonemapRate = matHdrManualTonemapRate;
    public readonly MatNonHdrBloomScalefactorConVar MatNonHdrBloomScalefactor = matNonHdrBloomScalefactor;
    public readonly MatBloomScalefactorScalarConVar MatBloomScalefactorScalar = matBloomScalefactorScalar;
    public readonly MatExposureCenterRegionXConVar MatExposureCenterRegionX = matExposureCenterRegionX;
    public readonly MatExposureCenterRegionYConVar MatExposureCenterRegionY = matExposureCenterRegionY;
    public readonly MatExposureCenterRegionXFlashlightConVar MatExposureCenterRegionXFlashlight = matExposureCenterRegionXFlashlight;
    public readonly MatExposureCenterRegionYFlashlightConVar MatExposureCenterRegionYFlashlight = matExposureCenterRegionYFlashlight;
    public readonly MatTonemapAlgorithmConVar MatTonemapAlgorithm = matTonemapAlgorithm;
    public readonly MatTonemapPercentTargetConVar MatTonemapPercentTarget = matTonemapPercentTarget;
    public readonly MatTonemapPercentBrightPixelsConVar MatTonemapPercentBrightPixels = matTonemapPercentBrightPixels;
    public readonly MatTonemapMinAvglumConVar MatTonemapMinAvglum = matTonemapMinAvglum;
    public readonly MatFullbrightConVar MatFullbright = matFullbright;
    public readonly MatForceTonemapScaleConVar MatForceTonemapScale = matForceTonemapScale;
    public readonly MatSoftwareAaStrengthConVar MatSoftwareAaStrength = matSoftwareAaStrength;
    public readonly MatSoftwareAaQualityConVar MatSoftwareAaQuality = matSoftwareAaQuality;
    public readonly MatSoftwareAaEdgeThresholdConVar MatSoftwareAaEdgeThreshold = matSoftwareAaEdgeThreshold;
    public readonly MatSoftwareAaBlurOnePixelLinesConVar MatSoftwareAaBlurOnePixelLines = matSoftwareAaBlurOnePixelLines;
    public readonly MatSoftwareAaTapOffsetConVar MatSoftwareAaTapOffset = matSoftwareAaTapOffset;
    public readonly MatSoftwareAaDebugConVar MatSoftwareAaDebug = matSoftwareAaDebug;
    public readonly MatSoftwareAaStrengthVguiConVar MatSoftwareAaStrengthVgui = matSoftwareAaStrengthVgui;
    public readonly MatColorcorrectionConVar MatColorcorrection = matColorcorrection;
    public readonly MatPostprocessXConVar MatPostprocessX = matPostprocessX;
    public readonly MatPostprocessYConVar MatPostprocessY = matPostprocessY;
    public readonly MatDumpRtsConVar MatDumpRts = matDumpRts;
    public readonly MatQueueModeConVar MatQueueMode = matQueueMode;
    public readonly BuildingCubemapsConVar BuildingCubemaps = buildingCubemaps;
    public readonly LocalPlayerVisionFlagsConVar LocalPlayerVisionFlags = localPlayerVisionFlags;
    public readonly MatMotionBlurEnabledConVar MatMotionBlurEnabled = matMotionBlurEnabled;
    public readonly MatMotionBlurForwardEnabledConVar MatMotionBlurForwardEnabled = matMotionBlurForwardEnabled;
    public readonly MatMotionBlurFallingMinConVar MatMotionBlurFallingMin = matMotionBlurFallingMin;
    public readonly MatMotionBlurFallingMaxConVar MatMotionBlurFallingMax = matMotionBlurFallingMax;
    public readonly MatMotionBlurFallingIntensityConVar MatMotionBlurFallingIntensity = matMotionBlurFallingIntensity;
    public readonly MatMotionBlurRotationIntensityConVar MatMotionBlurRotationIntensity = matMotionBlurRotationIntensity;
    public readonly MatMotionBlurStrengthConVar MatMotionBlurStrength = matMotionBlurStrength;
}
