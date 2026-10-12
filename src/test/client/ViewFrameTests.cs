using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ViewFrame"/> held to the frozen stock frames (render-stock): from
/// each frame's convars and rectangle, SetUpViews' and Render's decisions are
/// the view stock handed RenderView -- its FOVs, planes, viewport, aspect, clear
/// and draw flags -- and the branches no recording reaches are held to
/// view.cpp's rules.
/// </summary>
public class ViewFrameTests
{
    private static IEnumerable<(string Name, RenderTrace Trace, string Text)> Frames()
    {
        string dir = RepoLayout.Repo("src", "test", "client", "Fixtures", "render-stock");
        foreach (string path in Directory.GetFiles(dir, "*.txt").Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);
            yield return (Path.GetFileName(path), RenderTrace.Parse(text), text);
        }
    }

    private static float Cv(RenderTrace t, string name) => float.Parse(t.ConVars[name.ToUpperInvariant()], CultureInfo.InvariantCulture);

    // The frame's rectangle, its convars and the engine's aspect (the one the
    // recorded view carries: stock's engine answer when it is positive), with
    // the eye as SetUpViews and the client mode's override left it.
    private static (RenderFrameIn Frame, ViewSetUpOut SetUp) Ask(RenderTrace t, string text, in RenderInputs recorded)
    {
        Match rect = Regex.Match(text, @"^frame\t.* rect=(-?\d+),(-?\d+),(\d+),(\d+)", RegexOptions.Multiline);
        Assert.True(rect.Success, "no frame line");
        int R(int g) => int.Parse(rect.Groups[g].Value, CultureInfo.InvariantCulture);

        ViewSetUpOut setUp = ViewFrame.SetUpView(new ViewSetUpIn
        {
            FarZ = Cv(t, "r_farz"), MapExtents = Cv(t, "r_mapextents"), FogFarZ = 0f,
            DefaultFov = Cv(t, "default_fov"), ViewModelFov = Cv(t, "viewmodel_fov"),
        });
        var eye = new ViewSetup
        {
            ZFar = setUp.ZFar, ZFarViewmodel = setUp.ZFarViewmodel, ZNear = setUp.ZNear, ZNearViewmodel = setUp.ZNearViewmodel,
            Fov = setUp.Fov, Origin = recorded.View.Origin, Angles = recorded.View.Angles,
            DoBloomAndToneMapping = recorded.View.DoBloomAndToneMapping,
        };
        eye.FovViewmodel = ViewFrame.ViewModelFov(setUp, eye.Fov);

        var frame = new RenderFrameIn
        {
            X = R(1), Y = R(2), Width = R(3), Height = R(4),
            Eye = eye,
            ScreenAspect = recorded.View.AspectRatio,
            Windowed = 1, MaxClients = recorded.MaxClients,
            RestrictAspectFov = (int)Cv(t, "sv_restrict_aspect_ratio_fov"),
            ViewportScale = Cv(t, "mat_viewportscale"),
            GlClear = (int)Cv(t, "gl_clear"), GlClearRandomColor = (int)Cv(t, "gl_clear_randomcolor"),
            ClientModeDrawViewModel = 1,
            LevelOverview = Cv(t, "cl_leveloverview"),
            ViewportX = R(1), ViewportY = R(2), ViewportWidth = R(3), ViewportHeight = R(4),
        };
        return (frame, setUp);
    }

    [Fact]
    public void EveryFrozenFrameIsTheViewStockHandedRenderView()
    {
        int n = 0;
        foreach ((string name, RenderTrace t, string text) in Frames())
        {
            RenderInputs recorded = t.Inputs() ?? throw new Xunit.Sdk.XunitException($"{name}: no inputs");
            (RenderFrameIn frame, _) = Ask(t, text, recorded);
            var answer = default(RenderFrameOut);
            ViewFrame.Render(frame, ref answer, () => throw new InvalidOperationException("no random clear"));

            ViewSetup want = recorded.View, got = answer.Eye;
            Assert.True(BitConverter.SingleToInt32Bits(want.Fov) == BitConverter.SingleToInt32Bits(got.Fov), $"{name}: fov {want.Fov:R} vs {got.Fov:R}");
            Assert.True(BitConverter.SingleToInt32Bits(want.FovViewmodel) == BitConverter.SingleToInt32Bits(got.FovViewmodel),
                        $"{name}: fovvm {want.FovViewmodel:R} vs {got.FovViewmodel:R}");
            Assert.Equal((want.ZNear, want.ZFar, want.ZNearViewmodel, want.ZFarViewmodel), (got.ZNear, got.ZFar, got.ZNearViewmodel, got.ZFarViewmodel));
            Assert.Equal((want.X, want.Y, want.Width, want.Height), (got.X, got.Y, got.Width, got.Height));
            Assert.Equal((want.UnscaledX, want.UnscaledY, want.UnscaledWidth, want.UnscaledHeight),
                         (got.UnscaledX, got.UnscaledY, got.UnscaledWidth, got.UnscaledHeight));
            Assert.Equal(want.AspectRatio, got.AspectRatio);
            Assert.Equal(want.Ortho, got.Ortho);
            Assert.Equal(recorded.ClearFlags, answer.ClearFlags);
            Assert.Equal(recorded.WhatToDraw, answer.DrawFlags);
            Assert.Equal(0, answer.RandomClear);
            Assert.Equal(0, answer.Overview);
            n++;
        }
        Assert.Equal(5, n);
    }

    // CViewRender::GetZFar.
    [Theory]
    [InlineData(-1f, 16384f, 0f, 28377.9199f)]
    [InlineData(0.5f, 16384f, 0f, 28377.9199f)]
    [InlineData(-1f, 16384f, 4000f, 4000f)]
    [InlineData(1f, 16384f, 4000f, 1f)]
    [InlineData(9000f, 16384f, 0f, 9000f)]
    public void TheFarPlaneIsRFarzElseTheMapsDiagonalPulledInByFog(float farZ, float extents, float fogFarZ, float expected)
        => Assert.Equal(expected, ViewFrame.ZFar(farZ, extents, fogFarZ), 2);

    // SetUpViews: the viewmodel FOV moves with the view's zoom. Managed decides
    // the offset; native's one addition after the override carries it out
    // (ViewSetUpOut's remarks say why that is not a second crossing), and it
    // is stock's GetViewModelFOV() - ( default_fov - fov ).
    [Fact]
    public void TheViewModelFovFollowsTheOverridesZoom()
    {
        ViewSetUpOut setUp = ViewFrame.SetUpView(new ViewSetUpIn { DefaultFov = 90, ViewModelFov = 54, FarZ = -1, MapExtents = 16384 });
        Assert.Equal(90f, setUp.Fov);
        Assert.Equal(54f, ViewFrame.ViewModelFov(setUp, 90f));
        Assert.Equal(24f, ViewFrame.ViewModelFov(setUp, 60f));
        Assert.Equal(-36f, setUp.ViewModelFovOffset);
        foreach (float fov in new[] { 90f, 75f, 40f, 20f })
            Assert.Equal(54f - (90f - fov), fov + setUp.ViewModelFovOffset);
        Assert.Equal((7f, 1f), (setUp.ZNear, setUp.ZNearViewmodel));
    }

    private static RenderFrameIn Plain() => new()
    {
        Eye = new ViewSetup { Fov = 90, FovViewmodel = 54, Origin = new Vector(100, 200, 50) },
        ScreenAspect = 2.3333333f, Windowed = 1, MaxClients = 8, RestrictAspectFov = 1, ViewportScale = 1f,
        ClientModeDrawViewModel = 1, ViewportWidth = 2560, ViewportHeight = 1080,
    };

    private static RenderFrameOut Decide(RenderFrameIn frame, Func<int>? random = null)
    {
        var answer = default(RenderFrameOut);
        ViewFrame.Render(frame, ref answer, random ?? (() => 0));
        return answer;
    }

    // sv_restrict_aspect_ratio_fov caps the world FOV at 1.85:1 in a windowed
    // multiplayer game, and always at 2; the viewmodel's is never capped.
    [Fact]
    public void ARestrictedAspectCapsTheWorldFovAndNotTheViewModels()
    {
        float capped = ViewFrame.ScaleFovByWidthRatio(90, 1.85f * 0.75f);
        float wide = ViewFrame.ScaleFovByWidthRatio(90, 2.3333333f * 0.75f);
        Assert.Equal(capped, Decide(Plain()).Eye.Fov);
        RenderFrameIn single = Plain();
        single.MaxClients = 1;
        Assert.Equal(wide, Decide(single).Eye.Fov);
        RenderFrameIn always = single;
        always.RestrictAspectFov = 2;
        Assert.Equal(capped, Decide(always).Eye.Fov);
        Assert.Equal(ViewFrame.ScaleFovByWidthRatio(54, 2.3333333f * 0.75f), Decide(Plain()).Eye.FovViewmodel);
    }

    // mat_viewportscale shrinks the drawn viewport; the unscaled one is the rectangle.
    [Fact]
    public void TheViewportScaleShrinksTheViewAndKeepsTheUnscaledRectangle()
    {
        RenderFrameIn frame = Plain();
        frame.ViewportScale = 0.5f;
        frame.ViewportX = 3;
        ViewSetup eye = Decide(frame).Eye;
        Assert.Equal((1, 0, 1280, 540), (eye.X, eye.Y, eye.Width, eye.Height));
        Assert.Equal((3, 0, 2560, 1080), (eye.UnscaledX, eye.UnscaledY, eye.UnscaledWidth, eye.UnscaledHeight));
    }

    [Fact]
    public void WithNoEngineAspectTheViewsOwnIsUsed()
    {
        RenderFrameIn frame = Plain();
        frame.ScreenAspect = 0f;
        Assert.Equal(2560f / 1080f, Decide(frame).Eye.AspectRatio);
    }

    // gl_clear_randomcolor clears to a random colour first and adds no colour
    // clear; gl_clear and the ATI antialiasing case add it.
    [Fact]
    public void TheClearFlagsFollowGlClearAndTheAntialiasingCase()
    {
        Assert.Equal(ClearDepthStencil, Decide(Plain()).ClearFlags);
        RenderFrameIn gl = Plain();
        gl.GlClear = 1;
        Assert.Equal(ClearDepthStencil | RenderConst.ClearColor, Decide(gl).ClearFlags);
        RenderFrameIn aa = Plain();
        aa.ClearColorForAntialiasing = 1;
        Assert.Equal(ClearDepthStencil | RenderConst.ClearColor, Decide(aa).ClearFlags);

        RenderFrameIn random = gl;
        random.GlClearRandomColor = 1;
        int next = 10;
        RenderFrameOut answer = Decide(random, () => next++);
        Assert.Equal(ClearDepthStencil, answer.ClearFlags);
        Assert.Equal((1, 10, 11, 12), (answer.RandomClear, answer.ClearR, answer.ClearG, answer.ClearB));
    }

    private const int ClearDepthStencil = RenderConst.ClearDepth | RenderConst.ClearStencil;

    [Fact]
    public void TheViewModelDrawsWhenTheClientModeSays()
    {
        Assert.Equal(RenderConst.DrawHud | RenderConst.DrawViewModel, Decide(Plain()).DrawFlags);
        RenderFrameIn no = Plain();
        no.ClientModeDrawViewModel = 0;
        Assert.Equal(RenderConst.DrawHud, Decide(no).DrawFlags);
    }

    // CViewRender::SetUpOverView: straight down, orthographic, sized from 1024
    // units, the colour cleared and no viewmodel.
    [Fact]
    public void TheLevelOverviewLooksStraightDownOrthographically()
    {
        RenderFrameIn frame = Plain();
        frame.ViewportWidth = 1280;
        frame.ViewportHeight = 720;
        frame.LevelOverview = 2f;
        RenderFrameOut answer = Decide(frame);
        ViewSetup eye = answer.Eye;
        Assert.Equal(1, answer.Overview);
        Assert.Equal(1, eye.Ortho);
        Assert.Equal(new Vector(90, 90, 0), eye.Angles);
        // 2048 tall, 2048 * 16/9 = 3640 wide; the origin moves to the corner.
        Assert.Equal((0f, -2048f, 3640f, 0f), (eye.OrthoLeft, eye.OrthoTop, eye.OrthoRight, eye.OrthoBottom));
        Assert.Equal(new Vector(100 - 1820, 200 + 1024, 50), eye.Origin);
        Assert.Equal(ClearDepthStencil | RenderConst.ClearColor, answer.ClearFlags);
        Assert.Equal(RenderConst.DrawHud, answer.DrawFlags);
        Assert.Equal(100 - 1820 + 200 + 1024 + 50, ViewFrame.OverviewCrc(eye));
    }

    // CViewRender::WriteSaveGameScreenshotOfSize's view: the top-left rectangle,
    // the FOV widened from 4:3 to it, drawn as a subrect of the back buffer.
    [Fact]
    public void TheScreenshotViewIsTheEyeAtTheShotsSize()
    {
        var eye = new ViewSetup { X = 10, Y = 20, Width = 1280, Height = 720, Fov = 90, FovViewmodel = 54, Origin = new Vector(1, 2, 3) };
        ViewSetup shot = ViewFrame.Screenshot(eye, 180, 100);
        Assert.Equal((0, 0, 180, 100, 1), (shot.X, shot.Y, shot.Width, shot.Height, shot.RenderToSubrect));
        Assert.Equal(ViewFrame.ScaleFovByWidthRatio(90, (180f / 100f) / (4f / 3f)), shot.Fov);
        Assert.Equal((54f, new Vector(1, 2, 3)), (shot.FovViewmodel, shot.Origin));
    }
}
