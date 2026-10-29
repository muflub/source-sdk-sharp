//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// The player's view this frame, as <c>view-&gt;GetPlayerViewSetup()</c>
/// answers it once <c>CViewRender::Render</c> has scaled the two fields of
/// view: where the eye is, which way it looks, and the world's and the
/// viewmodel's fields of view.
/// </summary>
public readonly record struct PlayerView(Vector Origin, Vector Angles, float Fov, float FovViewmodel)
{
    /// <summary>The setup's view, as the render plan's decision carries it.</summary>
    public static PlayerView Of(in ViewSetup eye) => new(eye.Origin, eye.Angles, eye.Fov, eye.FovViewmodel);
}

/// <summary>The player's view the frame being drawn decided (<see cref="PlayerView"/>).</summary>
public interface IPlayerView
{
    /// <summary>The frame's view; false before the first frame has decided one.</summary>
    bool TryGet(out PlayerView view);
}

/// <summary>
/// <see cref="IPlayerView"/>, written by the render plan once a frame
/// (<see cref="RenderPlanHost"/>, CViewRender::Render's decision) and read by
/// the draws that convert a viewmodel's attachment between its field of view
/// and the world's (<see cref="ViewModelFov"/>).
/// </summary>
public sealed class PlayerViewRecord : IPlayerView
{
    private PlayerView _view;
    private bool _known;

    /// <summary>The frame's decided view.</summary>
    public void Record(in PlayerView view)
    {
        _view = view;
        _known = true;
    }

    public bool TryGet(out PlayerView view)
    {
        view = _view;
        return _known;
    }
}

/// <summary>
/// <c>FormatViewModelAttachment</c> (c_baseviewmodel.cpp): a point between
/// the viewmodel's field of view and the world's.
///
/// <para>
/// The viewmodel is drawn with a projection of its own, so a point on it
/// lands on screen where the world's projection would put a different
/// point. Stock's <c>C_BaseViewModel</c> hands every attachment out through
/// the forward conversion (to where the world's view would draw it), so an
/// effect drawn in the world pass sits on the gun; a weapon drawing in the
/// viewmodel pass converts back with the inverse. The depth along the view is
/// kept; the two screen axes are scaled by the ratio of the two projections'
/// <c>tan(fov/2)</c>.
/// </para>
///
/// <para>
/// This build's viewmodel attachments are the raw ones (the skeleton posed
/// at the eye), which is what a viewmodel-pass draw wants; a world-pass draw
/// at one converts it forward here.
/// </para>
/// </summary>
public static class ViewModelFov
{
    /// <summary>
    /// <paramref name="point"/> converted: from the viewmodel's field of view
    /// to the world's, or back when <paramref name="inverse"/>.
    /// </summary>
    public static Vector Format(Vector point, bool inverse, in PlayerView view)
    {
        float worldx = MathF.Tan(view.Fov * MathF.PI / 360.0f);
        float viewx = MathF.Tan(view.FovViewmodel * MathF.PI / 360.0f);

        // Aspect ratio cancels out, so only one factor; a viewmodel field of
        // view of 0 (viewmodel_fov 0 with the weapon shown) is no factor.
        float factorX = viewx != 0f ? worldx / viewx : 0.0f;
        float factorY = factorX;

        Angles.Basis axes = Angles.Vectors(view.Angles);
        Vector tmp = point - view.Origin;
        float x = axes.Right.Dot(tmp), y = axes.Up.Dot(tmp), z = axes.Forward.Dot(tmp);

        if (inverse)
        {
            if (factorX != 0 && factorY != 0)
            {
                x /= factorX;
                y /= factorY;
            }
            else
            {
                x = 0.0f;
                y = 0.0f;
            }
        }
        else
        {
            x *= factorX;
            y *= factorY;
        }

        return view.Origin + axes.Right * x + axes.Up * y + axes.Forward * z;
    }
}
