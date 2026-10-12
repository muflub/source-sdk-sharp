using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A second camera's view, as managed code decides it (lane camproto, ahead of
/// plan_client_novalve C7). Mirrors <c>SourceSharpViewTargetSetup</c>;
/// IShimViewClient's <c>RenderViewToTarget</c> queues one for the frame's
/// render and the native executor copies it into a <c>CViewSetup</c> field for
/// field, adding nothing of its own.
/// </summary>
/// <remarks>
/// Ortho extents are world units about the view's centre, as CViewSetup's
/// <c>m_Ortho*</c> are. Width and height are pixels of the target, drawn from
/// its top left; the executor refuses a size larger than the target.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct ViewTargetSetup
{
    public float OriginX, OriginY, OriginZ;
    public float Pitch, Yaw, Roll;

    /// <summary>Horizontal field of view, degrees, at the target's aspect.</summary>
    public float Fov;

    public float ZNear;
    public float ZFar;
    public float OrthoLeft;
    public float OrthoTop;
    public float OrthoRight;
    public float OrthoBottom;
    public int Width;
    public int Height;
    public ViewTargetFlags Flags;
}

/// <summary><c>SOURCESHARP_VIEWTARGET_*</c>.</summary>
[Flags]
public enum ViewTargetFlags
{
    None = 0,

    /// <summary>The ortho extents, not the field of view.</summary>
    Ortho = 1,

    /// <summary>The local player's body draws in this view (it does not in first person).</summary>
    DrawViewer = 2,
}
