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
/// What viewpostprocess.cpp's decisions read at the point the plan's post op
/// runs (<c>SourceSharpPostInputs_t</c>): the view's rectangle, the
/// hardware's answers, the render targets' sizes, the viewport then, the
/// map's tonemap overrides, the clock, the engine's tonemap scale, the view
/// motion blur reads and the histogram's read-backs (-1 where none was asked or
/// none had finished). The convars are PostProcessConVars'.
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
