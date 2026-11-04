using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using static SourceSharp.Client.Render.ConVarAs;

namespace SourceSharp.Client.Render;

/// <summary>
/// viewrender.cpp's fog helpers (GetFogEnable, GetFogColor, GetFogStart,
/// GetFogEnd, GetFogMaxDensity, GetFogRadial and their skybox twins), over the
/// frame's <see cref="RenderInputs"/>. Float arithmetic in stock's order, so the
/// values the executor hands the render context are stock's to the bit.
/// </summary>
public static class FogMath
{
    private static bool Override(RenderConVars cv) => CvInt(cv.FogOverride) != 0;

    private static bool LevelOverview(RenderConVars cv) => cv.ClLeveloverview.Value > 0f;

    /// <summary>AngleVectors' forward (mathlib), in floats with sinf/cosf as mathlib's SinCos.</summary>
    public static Vector Forward(Vector angles)
    {
        const float deg2rad = (float)(Math.PI / 180.0);
        float pitch = angles.X * deg2rad;
        float yaw = angles.Y * deg2rad;
        float sp = MathF.Sin(pitch), cp = MathF.Cos(pitch);
        float sy = MathF.Sin(yaw), cy = MathF.Cos(yaw);
        return new Vector(cp * cy, cp * sy, -sp);
    }

    private static float FLerp(float f1, float f2, float t) => f1 + (f2 - f1) * t;

    private static Vector Normalize(Vector v)
    {
        float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (len == 0f)
            return v;
        float inv = 1.0f / len;
        return new Vector(v.X * inv, v.Y * inv, v.Z * inv);
    }

    private static void CheckAndTransitionColor(float percent, Span<float> color, ReadOnlySpan<float> lerpTo)
    {
        if (lerpTo[0] != color[0] || lerpTo[1] != color[1] || lerpTo[2] != color[2])
        {
            color[0] = FLerp(color[0], lerpTo[0], percent);
            color[1] = FLerp(color[1], lerpTo[1], percent);
            color[2] = FLerp(color[2], lerpTo[2], percent);
        }
        else
        {
            color[0] = lerpTo[0];
            color[1] = lerpTo[1];
            color[2] = lerpTo[2];
        }
    }

    // fog_color / fog_colorskybox, as sscanf( "%f%f%f" ) read them; null when any is negative.
    private static Vector? OverrideColor(Vector color)
    {
        if (color.X < 0 || color.Y < 0 || color.Z < 0)
            return null;
        return color;
    }

    private static Vector Scale255(Vector c)
    {
        const float s = 1.0f / 255.0f;
        return new Vector(c.X * s, c.Y * s, c.Z * s);
    }

    // The blend between two colours by the view's angle to the primary direction.
    private static Vector Blend(Vector forward, Vector dirPrimary, Vector primary, Vector secondary)
    {
        Vector dir = Normalize(dirPrimary);
        float factor = (float)(0.5 * forward.Dot(dir) + 0.5);
        return new Vector(primary.X * factor + secondary.X * (1 - factor),
                          primary.Y * factor + secondary.Y * (1 - factor),
                          primary.Z * factor + secondary.Z * (1 - factor));
    }

    public static bool FogEnable(in RenderInputs i, RenderConVars cv)
    {
        if (LevelOverview(cv))
            return false;
        if (i.ClientModeDrawFog == 0)
            return false;
        if (Override(cv) && CvInt(cv.FogEnable) != -1)
            return Cv(cv.FogEnable) != 0f;
        return i.Fog.Present != 0 && i.Fog.Enable != 0;
    }

    public static Vector FogColor(in RenderInputs i, RenderConVars cv, Vector forward)
    {
        if (Override(cv) && OverrideColor(cv.FogColorValue) is { } o)
            return Scale255(o);
        if (i.Fog.Present == 0)
            return default;

        RenderFogParams f = i.Fog;
        Vector p0 = f.Primary(), s0 = f.Secondary();
        Span<float> primary = [p0.X, p0.Y, p0.Z];
        Span<float> secondary = [s0.X, s0.Y, s0.Z];
        if (f.LerpTime >= i.CurTime)
        {
            float percent = 1.0f - ((f.LerpTime - i.CurTime) / f.Duration);
            Vector pl = f.PrimaryLerpTo(), sl = f.SecondaryLerpTo();
            CheckAndTransitionColor(percent, primary, [pl.X, pl.Y, pl.Z]);
            CheckAndTransitionColor(percent, secondary, [sl.X, sl.Y, sl.Z]);
        }

        Vector p = new(primary[0], primary[1], primary[2]);
        Vector s = new(secondary[0], secondary[1], secondary[2]);
        Vector c = f.Blend != 0 ? Blend(forward, f.DirPrimary(), p, s) : p;
        return Scale255(c);
    }

    public static float FogStart(in RenderInputs i, RenderConVars cv)
    {
        if (Override(cv) && CvInt(cv.FogStart) != -1)
            return Cv(cv.FogStart);
        if (i.Fog.Present == 0)
            return 0f;
        RenderFogParams f = i.Fog;
        if (f.LerpTime > i.CurTime && f.Start != f.StartLerpTo)
            return FLerp(f.Start, f.StartLerpTo, 1.0f - ((f.LerpTime - i.CurTime) / f.Duration));
        return f.Start;
    }

    public static float FogEnd(in RenderInputs i, RenderConVars cv)
    {
        if (Override(cv) && CvInt(cv.FogEnd) != -1)
            return Cv(cv.FogEnd);
        if (i.Fog.Present == 0)
            return 0f;
        RenderFogParams f = i.Fog;
        if (f.LerpTime > i.CurTime && f.End != f.EndLerpTo)
            return FLerp(f.End, f.EndLerpTo, 1.0f - ((f.LerpTime - i.CurTime) / f.Duration));
        return f.End;
    }

    public static float FogMaxDensity(in RenderInputs i, RenderConVars cv)
    {
        if (LevelOverview(cv) || i.ClientModeDrawFog == 0)
            return 1.0f;
        if (Override(cv) && CvInt(cv.FogMaxdensity) != -1)
            return Cv(cv.FogMaxdensity);
        return i.Fog.Present == 0 ? 1.0f : i.Fog.MaxDensity;
    }

    public static bool FogRadial(in RenderInputs i, RenderConVars cv)
    {
        if (LevelOverview(cv) || i.ClientModeDrawFog == 0)
            return false;
        if (Override(cv) && CvInt(cv.FogRadial) != -1)
            return Cv(cv.FogRadial) != 0f;
        return i.Fog.Present != 0 && i.Fog.Radial != 0;
    }

    public static bool SkyFogEnable(in RenderInputs i, RenderConVars cv)
    {
        if (i.SkyPresent == 0)
            return false;
        if (Override(cv) && CvInt(cv.FogEnableskybox) != -1)
            return Cv(cv.FogEnableskybox) != 0f;
        return i.SkyFog.Enable != 0;
    }

    public static Vector SkyFogColor(in RenderInputs i, RenderConVars cv, Vector forward)
    {
        if (i.SkyPresent == 0)
            return default;
        if (Override(cv) && OverrideColor(cv.FogColorskyboxValue) is { } o)
            return Scale255(o);
        RenderFogParams f = i.SkyFog;
        Vector p = f.Primary();
        Vector s = f.Secondary();
        Vector c = f.Blend != 0 ? Blend(forward, f.DirPrimary(), p, s) : p;
        return Scale255(c);
    }

    public static float SkyFogStart(in RenderInputs i, RenderConVars cv)
    {
        if (i.SkyPresent == 0)
            return 0f;
        if (Override(cv) && CvInt(cv.FogStartskybox) != -1)
            return Cv(cv.FogStartskybox);
        return i.SkyFog.Start;
    }

    public static float SkyFogEnd(in RenderInputs i, RenderConVars cv)
    {
        if (i.SkyPresent == 0)
            return 0f;
        if (Override(cv) && CvInt(cv.FogEndskybox) != -1)
            return Cv(cv.FogEndskybox);
        return i.SkyFog.End;
    }

    public static float SkyFogMaxDensity(in RenderInputs i, RenderConVars cv)
    {
        if (i.SkyPresent == 0 || LevelOverview(cv) || i.ClientModeDrawFog == 0)
            return 1.0f;
        if (Override(cv) && CvInt(cv.FogMaxdensityskybox) != -1)
            return Cv(cv.FogMaxdensityskybox);
        return i.SkyFog.MaxDensity;
    }

    public static bool SkyFogRadial(in RenderInputs i, RenderConVars cv)
    {
        if (i.SkyPresent == 0 || LevelOverview(cv) || i.ClientModeDrawFog == 0)
            return false;
        if (Override(cv) && CvInt(cv.FogRadialskybox) != -1)
            return Cv(cv.FogRadialskybox) != 0f;
        return i.SkyFog.Radial != 0;
    }
}
