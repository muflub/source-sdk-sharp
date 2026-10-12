using System.Runtime.InteropServices;

namespace SourceSharp.Client.Interop;

/// <summary>Which of viewpostprocess.cpp's entry points a post crossing stands in for.</summary>
public enum PostProgramKind
{
    /// <summary>DoEnginePostProcessing.</summary>
    EnginePost = 0,
    /// <summary>DoImageSpaceMotionBlur.</summary>
    MotionBlur = 1,
}

/// <summary>
/// The convars viewpostprocess.cpp's decisions read, gathered by name into
/// <see cref="PostProcessInputs.Cvars"/> (the native table lists the same
/// names in this order).
/// </summary>
public enum PostCvar
{
    mat_bloomscale = 0,
    mat_hdr_level,
    mat_bloomamount_rate,
    mat_debug_postprocessing_effects,
    mat_debug_process_halfscreen,
    mat_postprocessing_combine,
    mat_dynamic_tonemapping,
    mat_show_ab_hdr,
    mat_tonemapping_occlusion_use_stencil,
    mat_debug_autoexposure,
    mat_autoexposure_max,
    mat_autoexposure_min,
    mat_show_histogram,
    mat_hdr_tonemapscale,
    mat_hdr_uncapexposure,
    mat_force_bloom,
    mat_disable_bloom,
    mat_debug_bloom,
    mat_accelerate_adjust_exposure_down,
    mat_hdr_manual_tonemap_rate,
    mat_non_hdr_bloom_scalefactor,
    mat_bloom_scalefactor_scalar,
    mat_exposure_center_region_x,
    mat_exposure_center_region_y,
    mat_exposure_center_region_x_flashlight,
    mat_exposure_center_region_y_flashlight,
    mat_tonemap_algorithm,
    mat_tonemap_percent_target,
    mat_tonemap_percent_bright_pixels,
    mat_tonemap_min_avglum,
    mat_fullbright,
    mat_force_tonemap_scale,
    mat_software_aa_strength,
    mat_software_aa_quality,
    mat_software_aa_edge_threshold,
    mat_software_aa_blur_one_pixel_lines,
    mat_software_aa_tap_offset,
    mat_software_aa_debug,
    mat_software_aa_strength_vgui,
    mat_colorcorrection,
    mat_postprocess_x,
    mat_postprocess_y,
    mat_dump_rts,
    mat_queue_mode,
    building_cubemaps,
    localplayer_visionflags,
    mat_motion_blur_enabled,
    mat_motion_blur_forward_enabled,
    mat_motion_blur_falling_min,
    mat_motion_blur_falling_max,
    mat_motion_blur_falling_intensity,
    mat_motion_blur_rotation_intensity,
    mat_motion_blur_strength,
    Count,
}

/// <summary>
/// What viewpostprocess.cpp's decisions read at the point the plan's post op
/// runs (<c>SourceSharpPostInputs_t</c>): the view's rectangle, the
/// hardware's answers, the render targets' sizes, the viewport then, the
/// map's tonemap overrides, the clock, the engine's tonemap scale, the view
/// motion blur reads, the histogram's read-backs (-1 where none was asked or
/// none had finished) and the convars by <see cref="PostCvar"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PostProcessInputs
{
    public int Kind;
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public int FlashlightOn;
    public int PostVGui;
    public int HdrType;
    public int EngineDxLevel;
    public int HardwareDxLevel;
    public int MapHasHdrLighting;
    public int CanDoSrgbReadFromRts;
    public int FakeSrgbWrite;
    public int SupportsPixelShaders20;
    public int ColorCorrectionActive;
    public int FullFrameWidth;
    public int FullFrameHeight;
    public int SmallFB1Width;
    public int SmallFB1Height;
    public int ViewportX;
    public int ViewportY;
    public int ViewportWidth;
    public int ViewportHeight;
    public int BackBufferWidth;
    public int BackBufferHeight;
    public int UseCustomAutoExposureMin;
    public int UseCustomAutoExposureMax;
    public int UseCustomBloomScale;
    public float CustomAutoExposureMin;
    public float CustomAutoExposureMax;
    public float CustomBloomScale;
    public float RealTime;
    public float CurTime;
    public float FrameTime;
    public float ToneScaleLinear;
    public fixed float ViewOriginXYZ[3];
    public fixed float ViewAnglesXYZ[3];
    public float Fov;
    public float AspectRatio;
    public fixed int Readback[32];
    /// <summary>Each PostTexture's actual width and height (two words each, by its number).</summary>
    public fixed int TextureSize[16];
    public fixed float Cvars[64];

    public readonly float Cvar(PostCvar c) { fixed (float* p = Cvars) return p[(int)c]; }

    public void SetCvar(PostCvar c, float value) { fixed (float* p = Cvars) p[(int)c] = value; }

    public readonly int TextureWidth(PostTexture t) { fixed (int* p = TextureSize) return p[2 * (int)t]; }

    public readonly int TextureHeight(PostTexture t) { fixed (int* p = TextureSize) return p[2 * (int)t + 1]; }

    public void SetTextureSize(PostTexture t, int width, int height)
    {
        fixed (int* p = TextureSize) { p[2 * (int)t] = width; p[2 * (int)t + 1] = height; }
    }

    public readonly int ReadbackOf(int bucket) { fixed (int* p = Readback) return p[bucket]; }

    public void SetReadback(int bucket, int pixels) { fixed (int* p = Readback) p[bucket] = pixels; }

    public readonly SourceSharp.Abi.Vector ViewOrigin { get { fixed (float* p = ViewOriginXYZ) return new(p[0], p[1], p[2]); } }

    public readonly SourceSharp.Abi.Vector ViewAngles { get { fixed (float* p = ViewAnglesXYZ) return new(p[0], p[1], p[2]); } }

    public void SetView(SourceSharp.Abi.Vector origin, SourceSharp.Abi.Vector angles)
    {
        fixed (float* o = ViewOriginXYZ) { o[0] = origin.X; o[1] = origin.Y; o[2] = origin.Z; }
        fixed (float* a = ViewAnglesXYZ) { a[0] = angles.X; a[1] = angles.Y; a[2] = angles.Z; }
    }
}
