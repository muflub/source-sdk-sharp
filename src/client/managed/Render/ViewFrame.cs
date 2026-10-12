//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using static SourceSharp.Client.Render.RenderConst;

namespace SourceSharp.Client.Render;

/// <summary>
/// view.cpp's frame decisions (C7 step 3 phase 2): CViewRender::SetUpViews'
/// near and far planes and field of view, and CViewRender::Render's eye --
/// the FOVs widened to the screen, the viewport, the aspect, the clear and
/// draw flags, the level overview. Pure functions of their inputs; the engine,
/// client mode and material calls around them are native's.
/// </summary>
public static class ViewFrame
{
    /// <summary>VIEW_NEARZ (view.h).</summary>
    public const float ViewNearZ = 7f;

    /// <summary>
    /// CViewRender::GetZFar: r_farz when it is set (1 or more), else the map's
    /// extents across the diagonal, pulled in by the viewer's fog controller.
    /// </summary>
    public static float ZFar(float farZ, float mapExtents, float fogFarZ)
    {
        if (farZ >= 1f)
            return farZ;
        float z = mapExtents * 1.73205080757f;
        return fogFarZ > 0f ? fogFarZ : z;
    }

    /// <summary>CViewRender::SetUpViews' values, set before the client mode's override.</summary>
    public static ViewSetUpOut SetUpView(in ViewSetUpIn input)
    {
        float far = ZFar(input.FarZ, input.MapExtents, input.FogFarZ);
        return new ViewSetUpOut
        {
            ZFar = far,
            ZFarViewmodel = far,
            ZNear = ViewNearZ,
            ZNearViewmodel = 1f,
            Fov = input.DefaultFov,
            ViewModelFovOffset = input.ViewModelFov - input.DefaultFov,
        };
    }

    /// <summary>
    /// SetUpViews' viewmodel FOV: the client mode's, moved by however far the
    /// override took the view from the default.
    /// </summary>
    /// <remarks>
    /// Native's carry-out of the offset after the override (the one addition
    /// it makes): stock's <c>GetViewModelFOV() - ( default_fov - fov )</c>.
    /// </remarks>
    public static float ViewModelFov(in ViewSetUpOut setUp, float fov) => fov + setUp.ViewModelFovOffset;

    /// <summary>
    /// ScaleFOVByWidthRatio (view.cpp), step for step: M_PI is a double. tan
    /// and atan are taken in float; the five frozen frames' FOVs (90 and 54 at
    /// 16:9) come out bit for bit either way, so the precision of those two
    /// steps is not pinned by a recording.
    /// </summary>
    public static float ScaleFovByWidthRatio(float fovDegrees, float ratio)
    {
        float halfAngleRadians = (float)(fovDegrees * (0.5f * Math.PI / 180.0f));
        float t = MathF.Tan(halfAngleRadians);
        t *= ratio;
        float retDegrees = (float)(180.0f / Math.PI * MathF.Atan(t));
        return retDegrees * 2.0f;
    }

    /// <summary>
    /// CViewRender::Render's decisions for the mono eye, written into
    /// <paramref name="answer"/>. <paramref name="randomByte"/> stands in for
    /// stock's <c>rand()%256</c> under gl_clear_randomcolor.
    /// </summary>
    public static void Render(in RenderFrameIn frame, ref RenderFrameOut answer, Func<int> randomByte)
    {
        ViewSetup eye = frame.Eye;

        float aspectRatio = frame.ScreenAspect * 0.75f; // / (4/3)
        float limitedAspectRatio = aspectRatio;
        if ((frame.RestrictAspectFov > 0 && frame.Windowed != 0 && frame.MaxClients > 1) || frame.RestrictAspectFov == 2)
            limitedAspectRatio = Math.Min(aspectRatio, 1.85f * 0.75f);

        eye.Fov = ScaleFovByWidthRatio(eye.Fov, limitedAspectRatio);
        eye.FovViewmodel = ScaleFovByWidthRatio(eye.FovViewmodel, aspectRatio);

        float scale = frame.ViewportScale;
        eye.UnscaledX = frame.ViewportX;
        eye.UnscaledY = frame.ViewportY;
        eye.UnscaledWidth = frame.ViewportWidth;
        eye.UnscaledHeight = frame.ViewportHeight;
        eye.X = (int)(frame.ViewportX * scale);
        eye.Y = (int)(frame.ViewportY * scale);
        eye.Width = (int)(frame.ViewportWidth * scale);
        eye.Height = (int)(frame.ViewportHeight * scale);
        eye.AspectRatio = frame.ScreenAspect > 0f ? frame.ScreenAspect : (float)eye.Width / eye.Height;
        if (eye.AspectRatio <= 0f)
            eye.AspectRatio = (float)eye.Width / eye.Height;

        int clear = ClearDepth | ClearStencil;
        answer.RandomClear = 0;
        if (frame.GlClearRandomColor != 0)
        {
            answer.RandomClear = 1;
            answer.ClearR = randomByte();
            answer.ClearG = randomByte();
            answer.ClearB = randomByte();
        }
        else if (frame.GlClear != 0 || frame.ClearColorForAntialiasing != 0)
        {
            clear |= ClearColor;
        }

        bool drawViewModel = frame.ClientModeDrawViewModel != 0;
        answer.Overview = 0;
        if (frame.LevelOverview > 0f)
        {
            SetUpOverView(ref eye, frame.LevelOverview);
            clear |= ClearColor;
            drawViewModel = false;
            answer.Overview = 1;
        }

        answer.Eye = eye;
        answer.ClearFlags = clear;
        answer.DrawFlags = DrawHud | (drawViewModel ? DrawViewModel : 0);
    }

    /// <summary>
    /// CViewRender::SetUpOverView: an orthographic view straight down, the
    /// size cl_leveloverview scales from 1024 units. The movement message and
    /// the green clear are the caller's.
    /// </summary>
    public static void SetUpOverView(ref ViewSetup eye, float levelOverview)
    {
        eye.Ortho = 1;
        float aspect = (float)eye.Width / eye.Height;
        int sizeY = (int)(1024.0f * levelOverview);
        int sizeX = (int)(sizeY * aspect);

        Vector origin = eye.Origin;
        origin.X -= sizeX / 2;
        origin.Y += sizeY / 2;
        eye.Origin = origin;

        eye.OrthoLeft = 0;
        eye.OrthoTop = -sizeY;
        eye.OrthoRight = sizeX;
        eye.OrthoBottom = 0;
        eye.Angles = new Vector(90, 90, 0);
    }

    /// <summary>
    /// CViewRender::WriteSaveGameScreenshotOfSize's view: the player's eye drawn
    /// into the top left <paramref name="width"/> by <paramref name="height"/> of
    /// the back buffer, its FOV widened from 4:3 to that rectangle.
    /// </summary>
    public static ViewSetup Screenshot(in ViewSetup eye, int width, int height)
    {
        ViewSetup view = eye;
        view.X = 0;
        view.Y = 0;
        view.Width = width;
        view.Height = height;
        view.Fov = ScaleFovByWidthRatio(view.Fov, ((float)width / height) / (4.0f / 3.0f));
        view.RenderToSubrect = 1;
        return view;
    }

    /// <summary>SetUpOverView's "simple movement detector": the sum it compares frame to frame.</summary>
    public static int OverviewCrc(in ViewSetup eye) => (int)(eye.Origin.X + eye.Origin.Y + eye.Origin.Z);
}
