//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Client.Interop;
using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>What a post program asks of the host besides its ops: convar writes, overlay text, developer messages.</summary>
public sealed class PostProgramEffects
{
    /// <summary>ConVar::SetValue calls, in order: the name and the string the engine stores.</summary>
    public List<(string Name, string Value)> CvarWrites { get; } = [];

    /// <summary>engine-&gt;Con_NPrintf( line, text ).</summary>
    public List<(int Line, string Text)> Prints { get; } = [];

    /// <summary>DevMsg text.</summary>
    public List<string> DevMessages { get; } = [];

    public void Clear()
    {
        CvarWrites.Clear();
        Prints.Clear();
        DevMessages.Clear();
    }
}

/// <summary>The engine_post material proxy's values (CEnginePostMaterialProxy's statics).</summary>
public readonly record struct EnginePostValues(
    float Aa0, float Aa1, float Aa2, float Aa3, float Aa2_0, float Aa2_1, float Aa2_2, float Aa2_3,
    float Uv0, float Uv1, float Uv2, float Uv3, int BloomEnable);

/// <summary>
/// viewpostprocess.cpp's DoEnginePostProcessing and DoImageSpaceMotionBlur as
/// a program of ops the executor carries out (C7): the bloom amount, the
/// exposure range, the auto-exposure histogram and the tonemap goal, the
/// 8-bit bloom chain, the combined or separate engine-post passes and their
/// material's values, the debug views (the bloom boxes, the histogram, the
/// render-target dumps), the vision point a mod draws at, and the motion
/// blur pass with its values. One per client: what stock kept in statics is
/// this object's.
/// </summary>
public sealed class PostProcessPlanner(Func<string> gameDirectory, IHooks<IPostProcessContributor>? contributors = null)
{
    private const int HdrNone = 0, HdrInteger = 1, HdrFloat = 2;
    private const int HistogramBarSize = 200;

    private readonly LuminanceHistogram _histogram = new();
    private readonly ToneMapScale _toneMap = new();
    private readonly BloomAmount _bloom = new();
    private readonly List<LuminanceQuery> _queries = [];
    private readonly PostVisionContext _vision = new();

    private bool _dumpRenderTargets;
    private int _rtIndex;
    private bool _firstFrame = true;
    private int _debugBoxX;
    private float _aa0, _aa1, _aa2, _aa3, _aa2x, _aa2y; // s_vBloomAAValues2[2..3] stay 0: stock never sets them
    private float _uv0, _uv1, _uv2, _uv3;
    private int _postBloomEnable = 1;

    /// <summary>The motion blur values (the MotionBlur proxy's).</summary>
    public MotionBlur MotionBlur { get; } = new();

    /// <summary>The engine_post proxy's values.</summary>
    public EnginePostValues EnginePost =>
        new(_aa0, _aa1, _aa2, _aa3, _aa2x, _aa2y, 0f, 0f, _uv0, _uv1, _uv2, _uv3, _postBloomEnable);

    /// <summary>The histogram (for its display and the facts).</summary>
    public LuminanceHistogram Histogram => _histogram;

    /// <summary>Whether this frame's passes dump their render targets (g_bDumpRenderTargets).</summary>
    public bool DumpingRenderTargets => _dumpRenderTargets;

    /// <summary>ResetToneMapping's managed half; the caller resets the engine's scale.</summary>
    public void ResetToneMapping() => _toneMap.Reset();

    private static int I(in PostProcessInputs i, PostCvar c) => (int)i.Cvar(c);

    private static float F(in PostProcessInputs i, PostCvar c) => i.Cvar(c);

    private static string FloatText(float v) => v.ToString("F6", CultureInfo.InvariantCulture);

    /// <summary>One post crossing's program: DoEnginePostProcessing or DoImageSpaceMotionBlur, by <see cref="PostProcessInputs.Kind"/>.</summary>
    public void Plan(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects)
    {
        if (input.Kind == (int)PostProgramKind.MotionBlur)
            PlanMotionBlur(input, ops, effects);
        else
            PlanEnginePost(input, ops, effects);
    }

    // DumpTGAofRenderTarget's decisions: refused under a queued material system.
    private void Dump(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects, int w, int h, PostDump name)
    {
        if (I(input, PostCvar.mat_queue_mode) != 0)
        {
            effects.DevMessages.Add("Error: mat_queue_mode must be 0 to dump debug rendertargets\n");
            effects.CvarWrites.Add(("mat_dump_rts", "0"));
            return;
        }
        ops.DumpTga(w, h, name, _rtIndex++);
    }

    // ---------------------------------------------------------------- motion blur

    private void PlanMotionBlur(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects)
    {
        if (I(input, PostCvar.mat_motion_blur_enabled) == 0 || input.HardwareDxLevel < 90)
            return;

        MotionBlur.Update(new MotionBlurFrame(
            input.RealTime, input.ViewOrigin, input.ViewAngles, input.Fov, input.AspectRatio,
            I(input, PostCvar.mat_motion_blur_forward_enabled) != 0,
            F(input, PostCvar.mat_motion_blur_falling_min), F(input, PostCvar.mat_motion_blur_falling_max),
            F(input, PostCvar.mat_motion_blur_falling_intensity), F(input, PostCvar.mat_motion_blur_rotation_intensity),
            F(input, PostCvar.mat_motion_blur_strength)));

        int srcWidth = input.FullFrameWidth, srcHeight = input.FullFrameHeight;
        int destWidth = input.ViewportWidth, destHeight = input.ViewportHeight;
        if (input.HdrType != HdrFloat)
            ops.CopyScreenEffect(input.X, input.Y, input.Width, input.Height, true);

        int material = ops.Material("dev/motion_blur");
        ops.DrawRect(material, 0, 0, destWidth, destHeight, 0, 0, srcWidth - 1, srcHeight - 1, srcWidth, srcHeight, worldRenderable: true);
        if (_dumpRenderTargets)
            Dump(input, ops, effects, destWidth, destHeight, PostDump.MotionBlur);
    }

    // ---------------------------------------------------------------- engine post

    private BloomInputs BloomInputsOf(in PostProcessInputs input) => new(
        input.EngineDxLevel, input.HdrType, I(input, PostCvar.mat_hdr_level), input.MapHasHdrLighting != 0,
        I(input, PostCvar.mat_force_bloom), I(input, PostCvar.mat_disable_bloom), I(input, PostCvar.building_cubemaps) != 0,
        I(input, PostCvar.mat_fullbright), input.CanDoSrgbReadFromRts == 0 && input.FakeSrgbWrite != 0,
        F(input, PostCvar.mat_bloomamount_rate), F(input, PostCvar.mat_bloomscale), input.UseCustomBloomScale != 0,
        input.CustomBloomScale, F(input, PostCvar.mat_non_hdr_bloom_scalefactor), F(input, PostCvar.mat_bloom_scalefactor_scalar));

    private void PlanEnginePost(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects)
    {
        int x = input.X, y = input.Y, w = input.Width, h = input.Height;

        if (_dumpRenderTargets)
            _dumpRenderTargets = false;
        if (I(input, PostCvar.mat_dump_rts) != 0)
        {
            _dumpRenderTargets = true;
            _rtIndex = 0;
            effects.CvarWrites.Add(("mat_dump_rts", "0"));
            Dump(input, ops, effects, w, h, PostDump.BackBuffer);
        }

        float bloomScale = _bloom.Next(BloomInputsOf(input));
        int hdrType = input.HdrType;

        (float exposureMin, float exposureMax) = ExposureRange.Of(
            input.UseCustomAutoExposureMin != 0, input.CustomAutoExposureMin, input.UseCustomAutoExposureMax != 0,
            input.CustomAutoExposureMax, F(input, PostCvar.mat_autoexposure_min), F(input, PostCvar.mat_autoexposure_max),
            I(input, PostCvar.mat_hdr_uncapexposure));

        if (I(input, PostCvar.mat_debug_bloom) == 1)
            DrawBloomDebugBoxes(input, ops);

        int readbackMask = 0;
        if (hdrType == HdrNone || hdrType == HdrInteger)
        {
            bool screenEffectUpdated = false;
            if (hdrType != HdrNone)
                readbackMask = PreBloomTonemapping(input, ops, effects, exposureMin, exposureMax, ref screenEffectUpdated);

            float aaStrength = F(input, PostCvar.mat_software_aa_strength);
            if (aaStrength == -1.0f)
            {
                effects.CvarWrites.Add(("mat_software_aa_strength", FloatText(0.0f)));
                aaStrength = 0.0f;
            }
            if (F(input, PostCvar.mat_software_aa_strength_vgui) == -1.0f)
                effects.CvarWrites.Add(("mat_software_aa_strength_vgui", FloatText(1.0f)));

            const bool performSoftwareAA = false; // X360 only in stock
            bool postVGui = input.PostVGui != 0;
            bool performBloom = !postVGui && bloomScale > 0.0f && input.EngineDxLevel >= 90;
            bool performColCorrect = !postVGui && input.HardwareDxLevel >= 90 && hdrType != HdrFloat &&
                                     input.ColorCorrectionActive != 0 && I(input, PostCvar.mat_colorcorrection) != 0;
            bool splitScreenHdr = I(input, PostCvar.mat_show_ab_hdr) != 0;
            ops.EnableColorCorrection(performColCorrect);

            if (performBloom || performSoftwareAA || performColCorrect)
            {
                int srcWidth = input.FullFrameWidth, srcHeight = input.FullFrameHeight;
                if (!screenEffectUpdated)
                {
                    ops.CopyScreenEffect(x, y, w, h, true);
                    screenEffectUpdated = true;
                }
                if (performBloom)
                    Generate8BitBloomTexture(input, ops, effects, bloomScale);

                // Texel coords at the centres of the viewport's outer pixels.
                float srcX0 = 0.0f, srcY0 = -0.5f, srcX1 = srcWidth / 4 - 1, srcY1 = srcHeight / 4 - 1;
                float dstX0 = 0.0f, dstY0 = 0.0f, dstX1 = srcWidth - 1, dstY1 = srcHeight - 1;
                int rectX = x, rectY = y, rectW = w, rectH = h;
                float destTexW = srcWidth, destTexH = srcHeight;

                float uvScaleX = (srcWidth - (srcWidth / (float)w)) / (srcWidth - 1);
                float uvScaleY = (srcHeight - (srcHeight / (float)h)) / (srcHeight - 1);
                CenterScaleQuadUVs(ref srcX0, ref srcY0, ref srcX1, ref srcY1, uvScaleX, uvScaleY);
                CenterScaleQuadUVs(ref dstX0, ref dstY0, ref dstX1, ref dstY1, uvScaleX, uvScaleY);

                int partX = rectX, partY = rectY, partW = rectW, partH = rectH;
                float pX0 = srcX0, pY0 = srcY0, pX1 = srcX1, pY1 = srcY1;
                if (I(input, PostCvar.mat_debug_postprocessing_effects) == 2)
                {
                    // Only the centre quarter of the screen.
                    partX = (int)(partX + 0.25f * rectW);
                    partY = (int)(partY + 0.25f * rectH);
                    partW = (int)(partW - 0.50f * rectW);
                    partH = (int)(partH - 0.50f * rectH);
                    float postScaleX = 1.0f - ((w / 2) / (float)(w - 1));
                    float postScaleY = 1.0f - ((h / 2) / (float)(h - 1));
                    CenterScaleQuadUVs(ref pX0, ref pY0, ref pX1, ref pY1, postScaleX, postScaleY);
                }

                if (!_firstFrame || !performColCorrect)
                {
                    bool fbUpdated = false;
                    int smallW = input.SmallFB1Width, smallH = input.SmallFB1Height;

                    if (I(input, PostCvar.mat_postprocessing_combine) != 0)
                    {
                        int post = SetupEnginePostMaterial(input, ops, srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1,
                                                           destTexW, destTexH, performSoftwareAA, performBloom, performColCorrect, aaStrength);
                        if (splitScreenHdr)
                            ops.Scissor(partW / 2, 0, partW, partH, true);
                        ops.DrawRect(post, 0, 0, partW, partH, pX0, pY0, pX1, pY1, smallW, smallH, worldRenderable: true,
                                     I(input, PostCvar.mat_postprocess_x), I(input, PostCvar.mat_postprocess_y));
                        if (splitScreenHdr)
                            ops.Scissor(-1, -1, -1, -1, false);
                        fbUpdated = true;
                    }
                    else
                    {
                        if (performBloom)
                        {
                            int bloomMaterial = SetupEnginePostMaterial(input, ops, srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1,
                                                                        destTexW, destTexH, false, performBloom, false, aaStrength);
                            if (splitScreenHdr)
                                ops.Scissor(partW / 2, 0, partW, partH, true);
                            ops.DrawRect(bloomMaterial, partX, partY, partW, partH, pX0, pY0, pX1, pY1, smallW, smallH, worldRenderable: true);
                            if (splitScreenHdr)
                                ops.Scissor(-1, -1, -1, -1, false);
                            fbUpdated = true;
                        }

                        if (performColCorrect)
                        {
                            if (fbUpdated)
                                ops.CopyScreenEffect(x, y, w, h, false);
                            int ccMaterial = SetupEnginePostMaterial(input, ops, srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1,
                                                                     destTexW, destTexH, false, false, performColCorrect, aaStrength);
                            if (splitScreenHdr)
                                ops.Scissor(partW / 2, 0, partW, partH, true);
                            ops.DrawRect(ccMaterial, partX, partY, partW, partH, pX0, pY0, pX1, pY1, smallW, smallH, worldRenderable: true);
                            if (splitScreenHdr)
                                ops.Scissor(-1, -1, -1, -1, false);
                            fbUpdated = true;
                        }
                    }

                    // Stock's pyro-vision point: a mod's own pass.
                    if (contributors != null && contributors.All.Count > 0)
                    {
                        _vision.Inputs = input;
                        _vision.FrameBufferUpdated = fbUpdated;
                        _vision.DestX = partX;
                        _vision.DestY = partY;
                        _vision.DestWidth = partW;
                        _vision.DestHeight = partH;
                        _vision.SrcX0 = pX0;
                        _vision.SrcY0 = pY0;
                        _vision.SrcX1 = pX1;
                        _vision.SrcY1 = pY1;
                        _vision.SmallWidth = smallW;
                        _vision.SmallHeight = smallH;
                        contributors.Each(nameof(IPostProcessContributor.AtVisionPoint), c => c.AtVisionPoint(_vision, ops));
                    }

                    if (_dumpRenderTargets)
                        Dump(input, ops, effects, partW, partH, PostDump.EnginePost);
                }
                _firstFrame = false;
            }

            if (hdrType != HdrNone && I(input, PostCvar.mat_show_histogram) != 0 && input.EngineDxLevel >= 90)
                DisplayHistogram(input, ops, effects, exposureMin, exposureMax, bloomScaleCurrent: CurrentBloomScale(input));
        }
        // HDR_TYPE_FLOAT never runs under this engine's GL renderer: its passes are not ported.

        ops.ReadbackNext(readbackMask);
    }

    private static float CurrentBloomScale(in PostProcessInputs input) =>
        input.UseCustomBloomScale != 0 ? input.CustomBloomScale : F(input, PostCvar.mat_bloomscale);

    private static void CenterScaleQuadUVs(ref float x, ref float y, ref float z, ref float w, float scaleX, float scaleY)
    {
        float midX = 0.5f * (z + x), midY = 0.5f * (w + y);
        float rangeX = 0.5f * (z - x), rangeY = 0.5f * (w - y);
        x = midX - scaleX * rangeX;
        y = midY - scaleY * rangeY;
        z = midX + scaleX * rangeX;
        w = midY + scaleY * rangeY;
    }

    // CEnginePostMaterialProxy::SetupEnginePostMaterial: the proxy's values and the material.
    private int SetupEnginePostMaterial(in PostProcessInputs input, PostOpsWriter ops,
                                        float bloomX0, float bloomY0, float bloomX1, float bloomY1,
                                        float fbX0, float fbY0, float fbX1, float fbY1, float fbW, float fbH,
                                        bool performSoftwareAA, bool performBloom, bool performColCorrect, float aaStrength)
    {
        _postBloomEnable = performBloom ? 1 : 0;

        if (performSoftwareAA)
        {
            _aa0 = aaStrength;
            _aa1 = 1.0f - F(input, PostCvar.mat_software_aa_blur_one_pixel_lines);
            _aa2 = I(input, PostCvar.mat_software_aa_quality);
            _aa3 = I(input, PostCvar.mat_software_aa_debug);
            _aa2x = F(input, PostCvar.mat_software_aa_edge_threshold);
            _aa2y = F(input, PostCvar.mat_software_aa_tap_offset);
        }
        else
        {
            _aa0 = 0.0f;
        }

        if (performSoftwareAA || performColCorrect)
        {
            // From quarter-size bloom texel coords to full-size frame buffer coords.
            float res1X = fbW / 4, res1Y = fbH / 4;
            float uvStart1X = (bloomX0 + 0.5f) / res1X, uvStart1Y = (bloomY0 + 0.5f) / res1Y;
            float uvStart2X = (fbX0 + 0.5f) / fbW, uvStart2Y = (fbY0 + 0.5f) / fbH;
            float dUv1X = (bloomX1 - bloomX0) / res1X, dUv1Y = (bloomY1 - bloomY0) / res1Y;
            float dUv2X = (fbX1 - fbX0) / fbW, dUv2Y = (fbY1 - fbY0) / fbH;
            float offsetX = uvStart2X - uvStart1X, offsetY = uvStart2Y - uvStart1Y;
            float scaleX = dUv2X / dUv1X, scaleY = dUv2Y / dUv1Y;
            offsetX = offsetX + uvStart1X * (1 - scaleX);
            offsetY = offsetY + uvStart1Y * (1 - scaleY);
            _uv0 = offsetX;
            _uv1 = offsetY;
            _uv2 = scaleX;
            _uv3 = scaleY;
            return ops.Material("dev/engine_post");
        }
        // The old additive bloom material (DX8's path, and this renderer's without AA or colour correction).
        return ops.Material("dev/bloomadd");
    }

    private void Generate8BitBloomTexture(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects, float bloomScale)
    {
        ops.PushRenderTargetAndViewport();
        int srcWidth = input.FullFrameWidth, srcHeight = input.FullFrameHeight;
        int downsample = ops.Material("dev/downsample_non_hdr");
        int xblur = ops.Material("dev/blurfilterx_nohdr");
        int yblur = ops.Material("dev/blurfiltery_nohdr");

        ops.SetRenderTargetAndViewport(PostTexture.SmallFB0);
        ops.DrawRect(downsample, 0, 0, srcWidth / 4, srcHeight / 4, 0, 0, srcWidth - 2, srcHeight - 2, srcWidth, srcHeight);
        if (_dumpRenderTargets)
            Dump(input, ops, effects, srcWidth / 4, srcHeight / 4, PostDump.QuarterSizeFB);

        ops.SetRenderTargetAndViewport(PostTexture.SmallFB1);
        ops.DrawRect(xblur, 0, 0, srcWidth / 4, srcHeight / 4, 0, 0, srcWidth / 4 - 1, srcHeight / 4 - 1, srcWidth / 4, srcHeight / 4);
        if (_dumpRenderTargets)
            Dump(input, ops, effects, srcWidth / 4, srcHeight / 4, PostDump.BlurX);

        ops.SetRenderTargetAndViewport(PostTexture.SmallFB0);
        ops.SetVarFloat(yblur, PostVar.BloomAmount, bloomScale);
        ops.DrawRect(yblur, 0, 0, srcWidth / 4, srcHeight / 4, 0, 0, srcWidth / 4 - 1, srcHeight / 4 - 1, srcWidth / 4, srcHeight / 4);
        if (_dumpRenderTargets)
            Dump(input, ops, effects, srcWidth / 4, srcHeight / 4, PostDump.BlurYAndBloom);

        ops.PopRenderTargetAndViewport();
    }

    private void DrawBloomDebugBoxes(in PostProcessInputs input, PostOpsWriter ops)
    {
        ops.SetRenderTarget(PostTexture.None);
        int destWidth = input.BackBufferWidth, destHeight = input.BackBufferHeight;
        ops.Viewport(0, 0, destWidth, destHeight);
        ops.ClearColor3ub(0, 0, 0);
        ops.ClearBuffers(true, true);

        const int inset = 64, size = 32;
        _debugBoxX = (_debugBoxX + 1) & 63;

        ops.Viewport(destWidth / 2 + _debugBoxX, destHeight / 2, size, size);
        ops.ClearColor3ub(255, 255, 255);
        ops.ClearBuffers(true, true);
        ops.Viewport(inset, inset, size, size);
        ops.ClearBuffers(true, true);
        ops.Viewport(destWidth - inset - size, inset, size, size);
        ops.ClearBuffers(true, true);
        ops.Viewport(destWidth - inset - size, destHeight - inset - size, size, size);
        ops.ClearBuffers(true, true);
        ops.Viewport(inset, destHeight - inset - size, size, size);
        ops.ClearBuffers(true, true);
        ops.Viewport(0, 0, destWidth, destHeight);
    }

    // DoPreBloomTonemapping: the histogram's queries and the tonemap goal.
    // Returns the buckets to read back at the next crossing.
    private int PreBloomTonemapping(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects,
                                    float exposureMin, float exposureMax, ref bool screenEffectUpdated)
    {
        bool dynamic = I(input, PostCvar.mat_dynamic_tonemapping) != 0;
        bool showHistogram = I(input, PostCvar.mat_show_histogram) != 0;
        if (!dynamic && !showHistogram)
            return 0;

        if (!screenEffectUpdated)
        {
            ops.CopyScreenEffect(input.X, input.Y, input.Width, input.Height, true);
            screenEffectUpdated = true;
        }

        int algorithm = I(input, PostCvar.mat_tonemap_algorithm);
        var view = new HistogramView(input.ViewportX, input.ViewportY, input.ViewportWidth, input.ViewportHeight,
                                     input.FlashlightOn != 0,
                                     F(input, PostCvar.mat_exposure_center_region_x), F(input, PostCvar.mat_exposure_center_region_y),
                                     F(input, PostCvar.mat_exposure_center_region_x_flashlight),
                                     F(input, PostCvar.mat_exposure_center_region_y_flashlight),
                                     input.HdrType == HdrFloat ? input.ToneScaleLinear : 1.0f);
        _queries.Clear();
        int inForce = _histogram.Update(algorithm, gameDirectory(), view, new Readbacks(input), _queries);
        if (inForce != algorithm)
        {
            effects.CvarWrites.Add(("mat_tonemap_algorithm", inForce.ToString(CultureInfo.InvariantCulture)));
            algorithm = inForce;
        }
        bool stencil = I(input, PostCvar.mat_tonemapping_occlusion_use_stencil) != 0;
        foreach (LuminanceQuery q in _queries)
            IssueQuery(ops, q, stencil);

        float target = _histogram.TargetTonemapScalar(algorithm, F(input, PostCvar.mat_tonemap_percent_bright_pixels),
                                                      F(input, PostCvar.mat_tonemap_percent_target),
                                                      F(input, PostCvar.mat_tonemap_min_avglum), input.ToneScaleLinear);
        float clamped = ExposureRange.Clamp(target, exposureMin, exposureMax);
        float tonemapScale = F(input, PostCvar.mat_hdr_tonemapscale);
        if (dynamic)
        {
            ToneMapAction action = _toneMap.Set(clamped, exposureMin, exposureMax,
                                                F(input, PostCvar.mat_force_tonemap_scale), I(input, PostCvar.mat_fullbright));
            switch (action.Kind)
            {
                case ToneMapActionKind.Reset:
                    effects.CvarWrites.Add(("mat_hdr_tonemapscale", FloatText(action.Value)));
                    ops.ResetToneScale(action.Value);
                    tonemapScale = action.Value;
                    break;
                case ToneMapActionKind.Goal:
                    effects.CvarWrites.Add(("mat_hdr_tonemapscale", FloatText(action.Value)));
                    ops.GoalToneScale(action.Value);
                    tonemapScale = action.Value;
                    if (action.Averaged)
                    {
                        ops.GoalToneScale(action.Average);
                        effects.CvarWrites.Add(("mat_hdr_tonemapscale", FloatText(action.Average)));
                        tonemapScale = action.Average;
                    }
                    break;
            }
        }

        if (I(input, PostCvar.mat_debug_autoexposure) != 0 || showHistogram)
        {
            if (algorithm == 0)
            {
                effects.Prints.Add((19, string.Format(CultureInfo.InvariantCulture,
                    "(Original algorithm) Target Scalar = {0,4:F2}  Min/Max( {1,4:F2}, {2,4:F2} )  Final Scalar: {3,4:F2}  Actual: {4,4:F2}",
                    target, exposureMin, exposureMax, tonemapScale, input.ToneScaleLinear)));
            }
            else
            {
                float bright = F(input, PostCvar.mat_tonemap_percent_bright_pixels), percentTarget = F(input, PostCvar.mat_tonemap_percent_target);
                effects.Prints.Add((19, string.Format(CultureInfo.InvariantCulture,
                    "{0:F2}% of pixels above {1}% target @ {2,4:F2}%  Target Scalar = {3,4:F2}  Min/Max( {4,4:F2}, {5,4:F2} )  Final Scalar: {6,4:F2}  Actual: {7,4:F2}",
                    bright, (int)percentTarget, _histogram.FindLocationOfPercentBrightPixels(algorithm, bright, percentTarget) * 100.0f,
                    _histogram.TargetTonemapScalar(algorithm, bright, percentTarget, F(input, PostCvar.mat_tonemap_min_avglum), input.ToneScaleLinear, idealForDebug: true),
                    exposureMin, exposureMax, tonemapScale, input.ToneScaleLinear)));
            }
        }

        // The buckets the next Update will read: in flight, and two frames old by then.
        return _histogram.DueNextFrame(algorithm);
    }

    // CHistogram_entry_t::IssueQuery's draws.
    private static void IssueQuery(PostOpsWriter ops, in LuminanceQuery q, bool stencil)
    {
        int compare = ops.Material("dev/lumcompare");
        ops.SetVarFloat(compare, PostVar.C0X, q.TestMin);
        ops.SetVarFloat(compare, PostVar.C0Y, q.TestMax);
        ops.SetVarFloat(compare, PostVar.C0Z, q.ToneScale);
        if (stencil)
            ops.Stencil(1);
        else
            ops.QueryBegin(q.Bucket);

        ops.DrawRect(compare, q.X0, q.Y0, 1 + q.X1 - q.X0, 1 + q.Y1 - q.Y0, q.X0, q.Y0, q.X1, q.Y1, q.Width, q.Height);

        if (stencil)
        {
            ops.QueryBegin(q.Bucket);
            ops.Stencil(2);
            int noPixelWrite = ops.Material("dev/no_pixel_write");
            ops.DrawRect(noPixelWrite, q.X0, q.Y0, 1 + q.X1 - q.X0, 1 + q.Y1 - q.Y0, q.X0, q.Y0, q.X1, q.Y1, q.Width, q.Height);
            ops.Stencil(0);
        }
        ops.QueryEnd(q.Bucket);
    }

    private sealed class Readbacks(PostProcessInputs input) : IOcclusionReadback
    {
        public int PixelsRendered(int bucket) => bucket is >= 0 and < 32 ? input.ReadbackOf(bucket) : -1;
    }

    // CLuminanceHistogramSystem::DisplayHistogram.
    private void DisplayHistogram(in PostProcessInputs input, PostOpsWriter ops, PostProgramEffects effects,
                                  float exposureMin, float exposureMax, float bloomScaleCurrent)
    {
        ops.PushRenderTargetAndViewport();
        int algorithm = I(input, PostCvar.mat_tonemap_algorithm);
        int ranges = (algorithm == 1 ? LuminanceHistogram.RangesNew : LuminanceHistogram.Ranges) - 1;
        ReadOnlySpan<HistogramBucket> buckets = _histogram.Buckets;

        int maxValidPixels = 0, totalValidPixels = 0, totalGraphPixelsWide = 0;
        for (int l = 0; l < ranges; l++)
        {
            HistogramBucket e = buckets[l];
            if (e.ContainsValidData)
            {
                totalValidPixels += e.PixelsInRange;
                if (e.PixelsInRange > maxValidPixels)
                    maxValidPixels = e.PixelsInRange;
            }
            totalGraphPixelsWide += BarWidth(e) + 2;
        }

        int destWidth = input.ViewportWidth, destHeight = input.ViewportHeight;
        float minAvgLum = F(input, PostCvar.mat_tonemap_min_avglum);
        effects.Prints.Add((17, "(All values in linear space)"));
        effects.Prints.Add((21, string.Format(CultureInfo.InvariantCulture,
            "AvgLum @ {0,4:F2}%  mat_tonemap_min_avglum = {1,4:F2}%  Using {2} pixels of {3} pixels on screen ({4,3}%)",
            MathF.Max(0.0f, _histogram.FindLocationOfPercentBrightPixels(algorithm, 50.0f)) * 100.0f, minAvgLum,
            totalValidPixels, destWidth * destHeight, (int)(totalValidPixels * 100.0f / (float)(destWidth * destHeight)))));
        effects.Prints.Add((23, string.Format(CultureInfo.InvariantCulture,
            "BloomScale = {0,4:F2}  mat_hdr_manual_tonemap_rate = {1,4:F2}  mat_accelerate_adjust_exposure_down = {2,4:F2}",
            bloomScaleCurrent, F(input, PostCvar.mat_hdr_manual_tonemap_rate), F(input, PostCvar.mat_accelerate_adjust_exposure_down))));
        if (algorithm != 1)
        {
            effects.Prints.Add((17, ""));
            effects.Prints.Add((15, ""));
        }

        int xpStart = destWidth - totalGraphPixelsWide - 10;
        int xp = xpStart;
        for (int l = 0; l < ranges; l++)
        {
            HistogramBucket e = buckets[l];
            int np = e.ContainsValidData ? e.PixelsInRange : 0;
            int width = BarWidth(e);
            if (np != 0)
            {
                float scaled = (float)np / (float)maxValidPixels * HistogramBarSize;
                float capped = HistogramBarSize < scaled ? HistogramBarSize : scaled;
                int height = (int)(1 > capped ? 1 : capped);
                ops.ClearColor3ub(255, 0, 0);
                ops.Viewport(xp, 4 + HistogramBarSize - height, width, height);
                ops.ClearBuffers(true, true);
            }
            else
            {
                const int height = 1;
                ops.ClearColor3ub(0, 0, 255);
                ops.Viewport(xp, 4 + HistogramBarSize - height, width, height);
                ops.ClearBuffers(true, true);
            }
            xp += width + 2;
        }

        if (algorithm == 1)
        {
            float percentTarget = F(input, PostCvar.mat_tonemap_percent_target);
            float yellowTarget = xpStart + (float)totalGraphPixelsWide * percentTarget / 100.0f;
            float yellowAverage = xpStart + (float)totalGraphPixelsWide * minAvgLum / 100.0f;
            float targetStart = xpStart + (float)totalGraphPixelsWide *
                _histogram.FindLocationOfPercentBrightPixels(algorithm, F(input, PostCvar.mat_tonemap_percent_bright_pixels), percentTarget);
            float averageStart = xpStart + (float)totalGraphPixelsWide * _histogram.FindLocationOfPercentBrightPixels(algorithm, 50.0f);
            const int height = HistogramBarSize;

            ops.Viewport((int)yellowTarget, 4 + HistogramBarSize - height, 4, height);
            ops.ClearColor3ub(200, 200, 0);
            ops.ClearBuffers(true, true);
            ops.Viewport((int)targetStart, 4 + HistogramBarSize - height, 4, height);
            ops.ClearColor3ub(0, 255, 0);
            ops.ClearBuffers(true, true);
            ops.Viewport((int)yellowAverage, 4 + HistogramBarSize - height, 4, height);
            ops.ClearColor3ub(200, 200, 0);
            ops.ClearBuffers(true, true);
            ops.Viewport((int)averageStart, 4 + HistogramBarSize - height, 4, height);
            ops.ClearColor3ub(0, 200, 200);
            ops.ClearBuffers(true, true);
        }

        // The actual tonemap value on its range.
        const float barWidth = 600.0f;
        float barStart = destWidth - barWidth - 10.0f;
        ops.Viewport((int)barStart, 4 + HistogramBarSize - 4 + 75, (int)barWidth, 4);
        ops.ClearColor3ub(200, 200, 200);
        ops.ClearBuffers(true, true);
        ops.Viewport((int)barStart, 4 + HistogramBarSize - 4 + 75 + 1, (int)barWidth, 2);
        ops.ClearColor3ub(0, 0, 0);
        ops.ClearBuffers(true, true);
        ops.Viewport((int)(barStart + barWidth * ((input.ToneScaleLinear - exposureMin) / (exposureMax - exposureMin))),
                     4 + HistogramBarSize - 4 + 75 - 6, 4, 16);
        ops.ClearColor3ub(255, 0, 0);
        ops.ClearBuffers(true, true);
        effects.Prints.Add((26, string.Format(CultureInfo.InvariantCulture,
            "{0:F2}                                                                                       {1:F2}                                                                                           {2:F2}",
            exposureMin, (exposureMax + exposureMin) / 2.0f, exposureMax)));

        // The last bar does not clear properly: an extra pixel.
        ops.Viewport(0, 0, 1, 1);
        ops.ClearColor3ub(0, 0, 0);
        ops.ClearBuffers(true, true);
        ops.PopRenderTargetAndViewport();
    }

    // MAX( 1, 500 * ( max - min ) ): the macro's float, stored to an int.
    private static int BarWidth(in HistogramBucket e)
    {
        float span = 500 * (e.MaxLum - e.MinLum);
        return (int)(1 > span ? 1 : span);
    }
}
