using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using static SourceSharp.Client.Render.RenderConst;

namespace SourceSharp.Client.Render;

/// <summary>
/// What a plan says the recorder will see (shell_render_trace_client.cpp's
/// lines), so a plan can be checked offline against a recorded stock frame
/// (RenderPlanFixtureTests): each op becomes the lines it makes, a line whose
/// arguments only the engine knows becomes a pattern, and an op whose calls
/// depend on what is in view (the entity and translucent draws) becomes a gap.
/// <see cref="Matches"/> then asks whether a recording fits.
/// </summary>
public static class RenderPlanText
{
    /// <summary>One expected item: an exact line, a line pattern (<c>*</c> matches any run), or a gap (any number of lines).</summary>
    public readonly record struct Expect(string? Line, bool Gap, string[]? Except = null)
    {
        public static Expect Exact(string line) => new(line, false);

        public static readonly Expect AnyLines = new(null, true);

        /// <summary>A gap whose lines may not begin with any of <paramref name="heads"/>.</summary>
        public static Expect LinesExcept(string[] heads) => new(null, true, heads);

        /// <summary>Whether this gap may hold <paramref name="call"/>.</summary>
        public bool Holds(string call)
        {
            if (Except == null)
                return true;
            foreach (string head in Except)
                if (call.StartsWith(head, StringComparison.Ordinal))
                    return false;
            return true;
        }

        public override string ToString() => Gap ? (Except == null ? "<any lines>" : $"<any lines but {string.Join(", ", Except)}>") : Line!;
    }

    /// <summary>
    /// What one entity's own draw may make (C7 step 2): anything but the calls
    /// its view's program makes around it -- a colour, a static-prop batch, a
    /// translucent leaf, the depth texture's flag -- so the program's own calls
    /// are held one for one.
    /// </summary>
    public static string[] EntityDrawExcept() =>
    [
        "render.SetColorModulation\t", "props.DrawStaticProps\t", "render.DrawTranslucentSurfaces\t",
        "ctx.SetFullScreenDepthTextureValidityFlag\t", "render.DrawWorldLists\t",
    ];

    /// <summary>C's <c>%.9g</c>: the recorder's float format, so a float prints back to the same float.</summary>
    public static string G9(float value)
    {
        if (float.IsNaN(value))
            return "nan";
        if (float.IsInfinity(value))
            return value > 0 ? "inf" : "-inf";
        bool negative = value < 0 || (value == 0 && float.IsNegative(value));
        double d = Math.Abs((double)value);
        string e = d.ToString("E8", CultureInfo.InvariantCulture); // d.dddddddddE+xxx
        string digits = e[0] + e.Substring(2, 8);
        int exponent = int.Parse(e[11..], CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (negative)
            sb.Append('-');
        if (d == 0)
        {
            sb.Append('0');
            return sb.ToString();
        }
        if (exponent >= -4 && exponent < 9)
        {
            string body;
            if (exponent >= 0)
            {
                string intPart = digits[..(exponent + 1)];
                string frac = digits[(exponent + 1)..].TrimEnd('0');
                body = frac.Length > 0 ? intPart + "." + frac : intPart;
            }
            else
            {
                string frac = (new string('0', -exponent - 1) + digits).TrimEnd('0');
                body = "0." + frac;
            }
            sb.Append(body);
        }
        else
        {
            string frac = digits[1..].TrimEnd('0');
            sb.Append(digits[0]);
            if (frac.Length > 0)
                sb.Append('.').Append(frac);
            sb.Append('e').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>The recorder's text for a CViewSetup (its ViewText).</summary>
    public static string ViewText(in ViewSetup v)
    {
        Vector o = v.Origin, a = v.Angles;
        return string.Create(CultureInfo.InvariantCulture,
            $"xywh={v.X},{v.Y},{v.Width},{v.Height} unscaled={v.UnscaledX},{v.UnscaledY},{v.UnscaledWidth},{v.UnscaledHeight} " +
            $"eye={v.StereoEye} ortho={(v.Ortho != 0 ? 1 : 0)}:{G9(v.OrthoLeft)},{G9(v.OrthoTop)},{G9(v.OrthoRight)},{G9(v.OrthoBottom)} " +
            $"fov={G9(v.Fov)} fovvm={G9(v.FovViewmodel)} origin={G9(o.X)},{G9(o.Y)},{G9(o.Z)} angles={G9(a.X)},{G9(a.Y)},{G9(a.Z)} " +
            $"z={G9(v.ZNear)},{G9(v.ZFar)} zvm={G9(v.ZNearViewmodel)},{G9(v.ZFarViewmodel)} subrect={(v.RenderToSubrect != 0 ? 1 : 0)} " +
            $"aspect={G9(v.AspectRatio)} offcenter={(v.OffCenter != 0 ? 1 : 0)}:{G9(v.OffCenterTop)},{G9(v.OffCenterBottom)},{G9(v.OffCenterLeft)},{G9(v.OffCenterRight)} " +
            $"bloom={(v.DoBloomAndToneMapping != 0 ? 1 : 0)} cachefull={(v.CacheFullSceneState != 0 ? 1 : 0)} projoverride={(v.ViewToProjectionOverride != 0 ? 1 : 0)}");
    }

    private static string TargetName(int target) => (RenderTarget)target switch
    {
        RenderTarget.WaterReflection => "_rt_WaterReflection",
        RenderTarget.WaterRefraction => "_rt_WaterRefraction",
        RenderTarget.FullFrameFB0 => "*",
        RenderTarget.Saved => "*",
        RenderTarget.ViewTarget0 => "_rt_SSView0",
        RenderTarget.ShadowColor or RenderTarget.ShadowDepth => "*",
        _ => "-",
    };

    // viewrender.cpp's BuildEngineDrawWorldListFlags (DRAWWORLDLISTS_ bits, ivrenderview.h).
    private const uint StrictlyAbove = 0x001, StrictlyUnder = 0x002, Intersects = 0x004, WaterSurface = 0x008,
                       Skybox = 0x010, ClipSkybox = 0x020, ShadowDepth = 0x040, Refraction = 0x080, Reflection = 0x100,
                       Ssao = 0x800;

    public static uint EngineDrawWorldListFlags(int draw)
    {
        uint flags = 0;
        if ((draw & DfDrawSkybox) != 0)
            flags |= Skybox;
        if ((draw & DfRenderAbovewater) != 0)
            flags |= StrictlyAbove | Intersects;
        if ((draw & DfRenderUnderwater) != 0)
            flags |= StrictlyUnder | Intersects;
        if ((draw & DfRenderWater) != 0)
            flags |= WaterSurface;
        if ((draw & DfClipSkybox) != 0)
            flags |= ClipSkybox;
        if ((draw & 0x100000) != 0)
            flags |= ShadowDepth;
        if ((draw & DfRenderRefraction) != 0)
            flags |= Refraction;
        if ((draw & DfRenderReflection) != 0)
            flags |= Reflection;
        if ((draw & 0x100) != 0)
        {
            flags |= Ssao | StrictlyUnder | Intersects | StrictlyAbove;
            flags &= ~(WaterSurface | Refraction | Reflection);
        }
        return flags;
    }

    /// <summary>
    /// The lines the plan makes, given the vis flags the engine answers to its
    /// ViewSetupVisEx (radial vis changes a clear) and whether r_drawopaqueworld
    /// is on (stock's DrawWorld reads it).
    /// </summary>
    public static List<Expect> Expected(IReadOnlyList<RenderOp> ops, in RenderInputs input, uint visFlags)
        => Expected(ops, input, visFlags, null);

    /// <summary>
    /// The calls a plan makes. With <paramref name="scenes"/> (each entity
    /// view's scene, in the order the views build their lists), each view's
    /// list-dependent draws are its <see cref="ViewDrawProgram"/>'s calls; without
    /// them, or for a view without entities, they are any lines.
    /// </summary>
    public static List<Expect> Expected(IReadOnlyList<RenderOp> ops, in RenderInputs input, uint visFlags,
                                        IReadOnlyList<ViewSceneData>? scenes)
    {
        var e = new List<Expect>();
        var planOps = ops.ToArray();
        int nextScene = 0;
        var programs = new Stack<Queue<List<RenderOp>>?>();
        var builtRenderables = new Stack<bool>();
        var views = new Stack<(ViewSetup View, int Draw)>();
        var cache = new List<string>();
        void X(string line) => e.Add(Expect.Exact(line));
        void Gap() { if (e.Count == 0 || !e[^1].Gap || e[^1].Except != null) e.Add(Expect.AnyLines); }
        void EntityGap()
        {
            if (e.Count > 0 && e[^1].Gap && e[^1].Except != null)
                return;
            e.Add(Expect.LinesExcept(EntityDrawExcept()));
        }
        bool radial = false;
        float levelOverview = input.Cvar(RenderCvar.cl_leveloverview);

        for (int index = 0; index < planOps.Length; index++)
        {
            RenderOp op = planOps[index];
            int I(int i) => op.GetI(i);
            float F(int i) => op.GetF(i);
            ViewSetup v = op.View;
            switch ((RenderOpKind)op.Kind)
            {
                case RenderOpKind.SceneBegin: X("render.SceneBegin"); break;
                case RenderOpKind.SceneEnd: X("render.SceneEnd"); break;
                case RenderOpKind.TurnOnToneMapping: X("ctx.TurnOnToneMapping"); break;
                case RenderOpKind.ShadowAdvanceFrame: X("shadow.AdvanceFrame"); break;
                case RenderOpKind.Push3DView:
                    X($"render.Push3DView\tclear=0x{I(0):x} rt={TargetName(I(1))} depth={TargetName(I(2))} {ViewText(v)}");
                    break;
                case RenderOpKind.Push2DView:
                    X($"render.Push2DView\tclear=0x{I(0):x} rt={TargetName(I(1))} xywh={v.X},{v.Y},{v.Width},{v.Height}");
                    break;
                case RenderOpKind.PopView: X("render.PopView"); break;
                case RenderOpKind.SetupCurrentView:
                    X($"ctx.SetIntRenderingParameter\t10={(I(0) == ViewMain || I(0) == View3DSky ? 1 : 0)}");
                    break;
                case RenderOpKind.ShadowPreRender: X("shadow.PreRender"); break;
                case RenderOpKind.ShadowComputeDepthTextures: X("shadow.ComputeShadowDepthTextures"); break;
                case RenderOpKind.ShadowUnlockDepthTextures: X("shadow.UnlockAllShadowDepthTextures"); break;
                case RenderOpKind.ViewSetupVisEx:
                    X($"render.ViewSetupVisEx\tnovis={I(0)} n=1 origin={G9(F(0))},{G9(F(1))},{G9(F(2))} => flags=0x{visFlags:x}");
                    radial = (visFlags & 1) != 0;
                    break;
                case RenderOpKind.ViewSetupVis:
                    X($"render.ViewSetupVis\tnovis={I(0)} n=1 origin={G9(F(0))},{G9(F(1))},{G9(F(2))}");
                    break;
                case RenderOpKind.ClearColorToFogIfRadialVis:
                    if (radial)
                        X("ctx.ClearColor4ub\t*");
                    break;
                case RenderOpKind.SetClearColorToFogColor:
                case RenderOpKind.ClearColorFromFogColor:
                    X("ctx.ClearColor4ub\t*");
                    break;
                case RenderOpKind.ClearColor4ub: X($"ctx.ClearColor4ub\t{I(0)},{I(1)},{I(2)},{I(3)}"); break;
                case RenderOpKind.ClearColor3ub: X($"ctx.ClearColor3ub\t{I(0)},{I(1)},{I(2)}"); break;
                case RenderOpKind.ForcedMaterialOverride: X($"model.ForcedMaterialOverride\tmaterial=- type={I(0)}"); break;
                case RenderOpKind.WorldViewBegin: views.Push((v, I(0))); programs.Push(null); builtRenderables.Push(false); break;
                case RenderOpKind.WorldViewEnd:
                    if (views.Count > 0) views.Pop();
                    if (programs.Count > 0) programs.Pop();
                    if (builtRenderables.Count > 0) builtRenderables.Pop();
                    break;
                case RenderOpKind.ViewScene:
                {
                    bool entities = builtRenderables.Count > 0 && builtRenderables.Peek() && input.Cvar(RenderCvar.r_drawentities) != 0f;
                    if (scenes == null || !entities || nextScene >= scenes.Count || programs.Count == 0)
                        break;
                    ViewDrawProgram.Context? context = ViewDrawProgram.ContextOf(planOps, index, input);
                    if (context == null)
                        break;
                    var program = new RenderPlanBuilder();
                    ViewDrawProgram.Build(scenes[nextScene++], context, program);
                    var sections = new Queue<List<RenderOp>>();
                    var section = new List<RenderOp>();
                    foreach (RenderOp p in program.Ops)
                    {
                        if (p.Kind == (int)RenderOpKind.SectionEnd)
                        {
                            sections.Enqueue(section);
                            section = [];
                        }
                        else
                            section.Add(p);
                    }
                    programs.Pop();
                    programs.Push(sections);
                    break;
                }
                case RenderOpKind.BeginUpdateLightmaps: X("render.BeginUpdateLightmaps"); break;
                case RenderOpKind.EndUpdateLightmaps: X("render.EndUpdateLightmaps"); break;
                case RenderOpKind.BuildWorldLists:
                {
                    ViewSetup view = views.Count > 0 ? views.Peek().View : default;
                    string key = CacheKey(view);
                    bool hit = I(2) != 0 && cache.Contains(key);
                    if (!hit)
                    {
                        X("render.CreateWorldList\tlist=*");
                        string height = I(4) != 0 ? G9(F(0)) : "-";
                        X($"render.BuildWorldLists\tlist=* forceleaf={I(1)} visdata=0 shadowdepth={I(3)} reflheight={height} => *");
                        if (I(2) != 0)
                            cache.Add(key);
                    }
                    if (I(0) != 0)
                    {
                        Vector o = view.Origin;
                        X($"props.ComputePropOpacity\torigin={G9(o.X)},{G9(o.Y)},{G9(o.Z)} factor={(levelOverview > 0 ? "-1" : "1")}");
                    }
                    break;
                }
                case RenderOpKind.BuildRenderables:
                    if (builtRenderables.Count > 0)
                    {
                        builtRenderables.Pop();
                        builtRenderables.Push(true);
                    }
                    X("render.BeginUpdateLightmaps");
                    Gap();
                    X("render.EndUpdateLightmaps");
                    break;
                case RenderOpKind.FogLinear:
                    X("ctx.FogMode\t1");
                    X($"ctx.FogColor3fv\t{G9(F(0))},{G9(F(1))},{G9(F(2))}");
                    X($"ctx.FogStart\t{G9(F(3))}");
                    X($"ctx.FogEnd\t{G9(F(4))}");
                    X($"ctx.FogMaxDensity\t{G9(F(5))}");
                    X($"ctx.FogRadial\t{I(0)}");
                    break;
                case RenderOpKind.FogNone: X("ctx.FogMode\t0"); break;
                case RenderOpKind.FogVolumeState: X($"render.SetFogVolumeState\tvolume={I(0)} heightfog={I(1)}"); break;
                case RenderOpKind.WorldViewShadowTextures: X("shadow.ComputeShadowTextures\tleaves=*"); break;
                case RenderOpKind.SoundExtraUpdate: X("engine.Sound_ExtraUpdate"); break;
                case RenderOpKind.SetFogZ: X($"ctx.SetFogZ\t{G9(F(0))}"); break;
                case RenderOpKind.SetHeightClipZ: X($"ctx.SetHeightClipZ\t{G9(F(0))}"); break;
                case RenderOpKind.SetHeightClipMode: X($"ctx.SetHeightClipMode\t{I(0)}"); break;
                case RenderOpKind.ClearForView:
                {
                    int clear = I(0);
                    if (I(1) != 0 && radial)
                        clear |= ClearColor;
                    if ((clear & (ClearDepth | ClearColor | ClearStencil)) != 0)
                    {
                        int c = (clear & ClearColor) != 0 ? 1 : 0, d = (clear & ClearDepth) != 0 ? 1 : 0, s = (clear & ClearStencil) != 0 ? 1 : 0;
                        X((clear & ClearObeyStencil) != 0
                            ? $"ctx.ClearBuffersObeyStencil\tcolor={c} depth={d}"
                            : $"ctx.ClearBuffers\tcolor={c} depth={d} stencil={s}");
                    }
                    break;
                }
                case RenderOpKind.WaterLightmapScale: X("ctx.SetToneMappingScaleLinear\t*"); break;
                case RenderOpKind.RestoreWaterLightmapScale: Gap(); break;
                case RenderOpKind.FrameBufferCopyPush:
                    if (I(0) != 0)
                        X("ctx.SetFrameBufferCopyTexture\ttexture=_rt_poweroftwofb index=0");
                    break;
                case RenderOpKind.FrameBufferCopyPop: X("ctx.SetFrameBufferCopyTexture\ttexture=* index=0"); break;
                case RenderOpKind.DrawWorld:
                    X($"render.DrawWorldLists\tlist=* flags=0x{(uint)I(0):x} waterz={G9(F(0))}");
                    break;
                case RenderOpKind.Flush: X($"ctx.Flush\thardware={I(0)}"); break;
                case RenderOpKind.CopyRefractionTarget:
                    X($"ctx.CopyRenderTargetToTextureEx\ttexture=_rt_WaterRefraction id=0 src={I(0)},{I(1)},{I(2)},{I(3)} dst=-");
                    break;
                case RenderOpKind.PushCustomClipPlane: X($"ctx.PushCustomClipPlane\t{G9(F(0))},{G9(F(1))},{G9(F(2))},{G9(F(3))}"); break;
                case RenderOpKind.PopCustomClipPlane: X("ctx.PopCustomClipPlane"); break;
                case RenderOpKind.DrawPortals: X("engine.DrawPortals"); break;
                case RenderOpKind.DrawLights: X("render.DrawLights"); break;
                case RenderOpKind.ViewDrawFade:
                    X($"render.ViewDrawFade\tcolor={I(0)},{I(1)},{I(2)},{I(3)} material={(I(4) != 0 ? "engine/modulatesinglecolor" : "debug/debugtranslucentsinglecolor")}");
                    break;
                case RenderOpKind.GrabPreColorCorrectedFrame: X($"engine.GrabPreColorCorrectedFrame\t{I(0)},{I(1)},{I(2)},{I(3)}"); break;
                case RenderOpKind.ToneMapScaleLinear: X($"ctx.SetToneMappingScaleLinear\t{G9(F(0))},{G9(F(1))},{G9(F(2))}"); break;
                case RenderOpKind.SetRenderTargetSaved: X("ctx.SetRenderTarget\trt=*"); break;
                case RenderOpKind.ViewModels:
                    X("ctx.MatrixMode\t1");
                    X("ctx.PushMatrix");
                    X("render.SetColorModulation\t1,1,1");
                    X("render.SetBlend\t1");
                    X($"render.Push3DView\tclear=0x0 rt=- depth=- {ViewText(v)}");
                    X("ctx.DepthRange\t0,0.100000001");
                    Gap();
                    X("ctx.DepthRange\t0,1");
                    X("render.PopView");
                    X("ctx.MatrixMode\t1");
                    X("ctx.PopMatrix");
                    break;
                case RenderOpKind.HudPaint:
                    X($"ctx.PushRenderTargetAndViewport\trt=- depth=- xywh={v.UnscaledX},{v.UnscaledY},{v.UnscaledWidth},{v.UnscaledHeight}");
                    Gap();
                    X("render.VGui_Paint\tmode=0x2");
                    Gap();
                    X("ctx.PopRenderTargetAndViewport");
                    X("ctx.Flush\thardware=0");
                    break;
                case RenderOpKind.Draw3DDebugOverlays: X("render.Draw3DDebugOverlays"); break;
                case RenderOpKind.PushDefaultTarget: X("ctx.PushRenderTargetAndViewport\trt=-"); break;
                case RenderOpKind.PopRenderTargetAndViewport: X("ctx.PopRenderTargetAndViewport"); break;
                // The debug overlays' own draws, and the line file (not a recorded call).
                case RenderOpKind.DebugQuad:
                case RenderOpKind.DebugScreenMaterial:
                case RenderOpKind.DebugLightmapPage:
                case RenderOpKind.DebugShadowTexture:
                case RenderOpKind.DebugShowTexture:
                case RenderOpKind.DebugFlashlightDepth:
                    Gap();
                    break;
                // A view's list-dependent draws: its program's section, when
                // the view's scene is known.
                case RenderOpKind.DrawOpaque:
                case RenderOpKind.DrawTranslucent:
                case RenderOpKind.DrawNoZTranslucent:
                case RenderOpKind.DrawTranslucentWorldInLeaves:
                    if (programs.Count > 0 && programs.Peek() is { Count: > 0 } queue)
                        SectionLines(queue.Dequeue(), X, Gap, EntityGap);
                    else
                        Gap();
                    break;
                // The helpers whose own draws depend on what is in view: any lines.
                case RenderOpKind.ClientSystemsPreRender:
                case RenderOpKind.ClientSystemsPostRender:
                case RenderOpKind.GlowDrawOverlays:
                case RenderOpKind.GlowUpdateSkyOverlays:
                case RenderOpKind.DrawPrecipitation:
                case RenderOpKind.ClientEffectsDraw:
                case RenderOpKind.RenderPlayerSprites:
                case RenderOpKind.MotionBlur:
                case RenderOpKind.PostScreenSpaceEffects:
                case RenderOpKind.UnderwaterOverlay:
                case RenderOpKind.SmokeFogOverlay:
                case RenderOpKind.ScreenOverlay:
                case RenderOpKind.EnginePostProcessing:
                case RenderOpKind.Upscale:
                case RenderOpKind.FreezeFrameDraw:
                case RenderOpKind.FreezeFrameCapture:
                case RenderOpKind.PixelVisEndCurrentView:
                case RenderOpKind.PixelVisEndScene:
                    Gap();
                    break;
                default:
                    // FinishCurrentView, AllowCurrentViewAccess, ParticleIncrementFrameCode,
                    // SaveRenderTarget, the area bits, the overlay material and the current
                    // setup make no recorded call.
                    break;
            }
        }
        return e;
    }

    private static string CacheKey(in ViewSetup v)
    {
        Vector o = v.Origin, a = v.Angles;
        return string.Join(",", new[]
        {
            v.OrthoLeft, v.OrthoTop, v.OrthoRight, v.OrthoBottom, v.Fov, o.X, o.Y, o.Z, a.X, a.Y, a.Z,
            v.ZNear, v.ZFar, v.AspectRatio, v.OffCenterTop, v.OffCenterBottom, v.OffCenterLeft, v.OffCenterRight,
        }.Select(f => BitConverter.SingleToInt32Bits(f).ToString(CultureInfo.InvariantCulture))) + $",{v.Ortho},{v.OffCenter}";
    }

    private static bool LineMatches(string pattern, string line)
    {
        int star = pattern.IndexOf('*');
        if (star < 0)
            return string.Equals(pattern, line, StringComparison.Ordinal);
        // A '*' matches any run; the pieces between must appear in order.
        string[] pieces = pattern.Split('*');
        if (!line.StartsWith(pieces[0], StringComparison.Ordinal))
            return false;
        int at = pieces[0].Length;
        for (int p = 1; p < pieces.Length; p++)
        {
            if (p == pieces.Length - 1)
                return line.Length - at >= pieces[p].Length && line.EndsWith(pieces[p], StringComparison.Ordinal);
            int found = line.IndexOf(pieces[p], at, StringComparison.Ordinal);
            if (found < 0)
                return false;
            at = found + pieces[p].Length;
        }
        return true;
    }

    /// <summary>
    /// Whether the recorded calls fit the expected items, gaps matching any
    /// run of lines. <paramref name="furthest"/> is how many expected items the
    /// best attempt matched, for a message that says where it stopped.
    /// </summary>
    // The calls one program section makes: exact where the op is one recorded
    // call, any lines where an entity's or a helper's own draws follow.
    private static void SectionLines(List<RenderOp> section, Action<string> x, Action gap, Action entityGap)
    {
        int statics = 0, detailLeaves = 0;
        foreach (RenderOp op in section)
        {
            switch ((RenderOpKind)op.Kind)
            {
                case RenderOpKind.SetBlend: x($"render.SetBlend\t{G9(op.GetF(0))}"); break;
                case RenderOpKind.SetColorModulation:
                    x($"render.SetColorModulation\t{G9(op.GetF(0))},{G9(op.GetF(1))},{G9(op.GetF(2))}");
                    break;
                case RenderOpKind.StaticProp: statics++; break;
                case RenderOpKind.DrawStaticPropsBatch:
                    x($"props.DrawStaticProps\tcount={statics} shadowdepth={(op.GetI(0) != 0 ? 1 : 0)} wireframe={op.GetI(1)}");
                    statics = 0;
                    break;
                case RenderOpKind.UpdateFullScreenDepthTexture:
                    x("ctx.CopyRenderTargetToTextureEx\t*");
                    x("ctx.SetFullScreenDepthTextureValidityFlag\t1");
                    break;
                case RenderOpKind.FullScreenDepthTextureValidity: x($"ctx.SetFullScreenDepthTextureValidityFlag\t{op.GetI(0)}"); break;
                case RenderOpKind.TranslucentSurfaces:
                    x($"render.DrawTranslucentSurfaces\tlist=* leaf={op.GetI(0)} flags=0x{(uint)op.GetI(1):x} shadowdepth={op.GetI(2)}");
                    break;
                case RenderOpKind.DetailLeaf: detailLeaves++; break;
                case RenderOpKind.DetailRenderTranslucent:
                    if (detailLeaves > 0)
                        gap();
                    detailLeaves = 0;
                    break;
                case RenderOpKind.DrawEntity: entityGap(); break;
                case RenderOpKind.RopeDrawCache:
                case RenderOpKind.ParticleSystemDrawCache:
                case RenderOpKind.ParticleSingletons:
                case RenderOpKind.DetailRenderInLeaf:
                case RenderOpKind.FrameBufferForEntity:
                case RenderOpKind.UpdateRefractTexture:
                case RenderOpKind.UpdateScreenEffectTexture:
                    gap();
                    break;
            }
        }
    }

    public static bool Matches(IReadOnlyList<Expect> expected, IReadOnlyList<string> calls, out int furthest)
    {
        int k = expected.Count, n = calls.Count;
        // can[i, j]: expected[i..] matches calls[j..]
        var can = new bool[k + 1, n + 1];
        can[k, n] = true;
        for (int i = k - 1; i >= 0; i--)
        {
            for (int j = n; j >= 0; j--)
            {
                if (expected[i].Gap)
                    can[i, j] = can[i + 1, j] || (j < n && expected[i].Holds(calls[j]) && can[i, j + 1]);
                else
                    can[i, j] = j < n && LineMatches(expected[i].Line!, calls[j]) && can[i + 1, j + 1];
            }
        }
        furthest = 0;
        if (can[0, 0])
        {
            furthest = k;
            return true;
        }
        // The longest prefix of expected that matches some prefix of calls.
        var reach = new bool[k + 1, n + 1];
        reach[0, 0] = true;
        for (int i = 0; i < k; i++)
        {
            bool any = false;
            for (int j = 0; j <= n; j++)
            {
                if (!reach[i, j])
                    continue;
                if (expected[i].Gap)
                {
                    for (int m = j; m <= n; m++)
                    {
                        reach[i + 1, m] = true;
                        if (m < n && !expected[i].Holds(calls[m]))
                            break;
                    }
                    any = true;
                }
                else if (j < n && LineMatches(expected[i].Line!, calls[j]))
                {
                    reach[i + 1, j + 1] = true;
                    any = true;
                }
            }
            if (!any)
                break;
            furthest = i + 1;
        }
        return false;
    }
}
