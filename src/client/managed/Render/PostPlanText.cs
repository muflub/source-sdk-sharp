using System.Globalization;

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// What a post program says the recorder will see (shell_render_trace_client.cpp's
/// lines), so a program can be checked against a frozen stock frame
/// (PostProcessPlannerTests). The material-var sets, the occlusion queries,
/// the stencil and colour-correction switches, the scissor and the dumps make
/// no recorded line: their effect shows in the frame's <c>q material.var</c>
/// lines or not at all.
/// </summary>
public static class PostPlanText
{
    private static string Texture(PostTexture t) => t switch
    {
        PostTexture.FullFrameFB => "_rt_fullframefb",
        PostTexture.SmallFB0 => "_rt_smallfb0",
        PostTexture.SmallFB1 => "_rt_smallfb1",
        PostTexture.ResolvedFullFrameDepth => "_rt_resolvedfullframedepth",
        _ => "-",
    };

    /// <summary>
    /// The lines, one expected item each. <paramref name="materialName"/>
    /// names a material id; <paramref name="smallWidth"/> by
    /// <paramref name="smallHeight"/> is the quarter-size targets' size (what
    /// SetRenderTargetAndViewport's viewport reads); an op for which
    /// <paramref name="pattern"/> answers true has its arguments as <c>*</c>.
    /// </summary>
    public static List<RenderPlanText.Expect> Expected(IEnumerable<RenderOp> ops, Func<int, string> materialName,
                                                      int smallWidth, int smallHeight, Func<RenderOp, bool>? pattern = null)
    {
        var e = new List<RenderPlanText.Expect>();
        void Line(string head, string args, in RenderOp op) =>
            e.Add(RenderPlanText.Expect.Exact(pattern != null && pattern(op) ? head + "\t*" : args.Length == 0 ? head : head + "\t" + args));

        bool identity = false;
        foreach (RenderOp op in ops)
        {
            switch ((RenderOpKind)op.Kind)
            {
                case RenderOpKind.PostCopyScreenEffect:
                {
                    string rect = string.Create(CultureInfo.InvariantCulture, $"{op.GetI(0)},{op.GetI(1)},{op.GetI(2)},{op.GetI(3)}");
                    // A destination the source's size (this renderer's targets are the screen's).
                    Line("ctx.CopyRenderTargetToTextureEx", $"texture=_rt_fullframefb id=0 src={rect} dst={(op.GetI(4) != 0 ? "-" : rect)}", op);
                    Line("ctx.SetFrameBufferCopyTexture", "texture=_rt_fullframefb index=0", op);
                    break;
                }
                case RenderOpKind.PostSetRenderTarget:
                    Line("ctx.SetRenderTarget", "rt=" + Texture((PostTexture)op.GetI(0)), op);
                    break;
                case RenderOpKind.PostSetRenderTargetAndViewport:
                    Line("ctx.SetRenderTarget", "rt=" + Texture((PostTexture)op.GetI(0)), op);
                    Line("ctx.Viewport", string.Create(CultureInfo.InvariantCulture, $"0,0,{smallWidth},{smallHeight}"), op);
                    break;
                case RenderOpKind.PostPushRenderTargetAndViewport:
                    Line("ctx.PushRenderTargetAndViewport", "", op);
                    break;
                case RenderOpKind.PopRenderTargetAndViewport:
                    Line("ctx.PopRenderTargetAndViewport", "", op);
                    break;
                case RenderOpKind.PostDrawRect:
                    Line("ctx.DrawScreenSpaceRectangle", string.Create(CultureInfo.InvariantCulture,
                        $"material={materialName(op.GetI(0))} dest={op.GetI(1)},{op.GetI(2)},{op.GetI(3)},{op.GetI(4)} " +
                        $"src={RenderPlanText.G9(op.GetF(0))},{RenderPlanText.G9(op.GetF(1))},{RenderPlanText.G9(op.GetF(2))},{RenderPlanText.G9(op.GetF(3))} " +
                        $"srcsize={op.GetI(5)},{op.GetI(6)} dice={(int)op.GetF(4)},{(int)op.GetF(5)}"), op);
                    break;
                case RenderOpKind.PostGoalToneScale:
                    Line("ctx.SetGoalToneMappingScale", RenderPlanText.G9(op.GetF(0)), op);
                    break;
                case RenderOpKind.PostResetToneScale:
                    Line("ctx.ResetToneMappingScale", RenderPlanText.G9(op.GetF(0)), op);
                    break;
                case RenderOpKind.PostClearColor3ub:
                    Line("ctx.ClearColor3ub", string.Create(CultureInfo.InvariantCulture, $"{op.GetI(0)},{op.GetI(1)},{op.GetI(2)}"), op);
                    break;
                case RenderOpKind.PostViewport:
                    Line("ctx.Viewport", string.Create(CultureInfo.InvariantCulture, $"{op.GetI(0)},{op.GetI(1)},{op.GetI(2)},{op.GetI(3)}"), op);
                    break;
                case RenderOpKind.PostClearColor4ub:
                    Line("ctx.ClearColor4ub", string.Create(CultureInfo.InvariantCulture, $"{op.GetI(0)},{op.GetI(1)},{op.GetI(2)},{op.GetI(3)}"), op);
                    break;
                case RenderOpKind.PostPushRenderTarget:
                    Line("ctx.PushRenderTargetAndViewport", "rt=" + Texture((PostTexture)op.GetI(0)), op);
                    break;
                case RenderOpKind.PostMeshBegin:
                    identity = (op.GetI(3) & 1) != 0;
                    if (identity)
                    {
                        Line("ctx.MatrixMode", "0", op);
                        Line("ctx.PushMatrix", "", op);
                        Line("ctx.MatrixMode", "1", op);
                        Line("ctx.PushMatrix", "", op);
                    }
                    break;
                case RenderOpKind.PostMeshEnd:
                    if (identity)
                    {
                        Line("ctx.MatrixMode", "0", op);
                        Line("ctx.PopMatrix", "", op);
                        Line("ctx.MatrixMode", "1", op);
                        Line("ctx.PopMatrix", "", op);
                    }
                    identity = false;
                    break;
                case RenderOpKind.PostClearBuffers:
                    Line("ctx.ClearBuffers", $"color={op.GetI(0)} depth={op.GetI(1)} stencil=0", op);
                    break;
            }
        }
        return e;
    }
}
