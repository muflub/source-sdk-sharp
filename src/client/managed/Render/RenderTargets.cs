//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// rendertexture.h's getters, one slot each in the header's order (the frame
/// buffers by index, the teeny textures by which). The native store numbers
/// its SS_RT_* slots the same way (render_target_store.h);
/// RenderTargetStoreAbiTests holds the two lists equal.
/// </summary>
public enum RenderTargetSlot
{
    PowerOfTwoFrameBuffer = 0,
    FullFrameFrameBuffer0 = 1,
    FullFrameFrameBuffer1 = 2,
    FullFrameFrameBuffer2 = 3,
    FullFrameFrameBuffer3 = 4,
    WaterReflection = 5,
    WaterRefraction = 6,
    Fullscreen = 7,
    Camera = 8,
    FullFrameDepth = 9,
    SmallBufferHdr0 = 10,
    SmallBufferHdr1 = 11,
    SmallBuffer0 = 12,
    SmallBuffer1 = 13,
    Teeny0 = 14,
    Teeny1 = 15,
    Teeny2 = 16,
    Count = 17,
}

/// <summary>
/// One slot's declaration: the texture FindTexture is asked for and how, and
/// where in the release function's order its reference is dropped (0: it is
/// kept across a release, as stock keeps the fullscreen, HDR and teeny ones).
/// </summary>
public readonly record struct RenderTargetRow(RenderTargetSlot Slot, string Name, string Group, bool Complain, int CreationFlags, int ReleaseOrder);

/// <summary>
/// rendertexture.cpp's decisions: which named render target each getter
/// finds (matrendertexture.cpp's names, as the material system creates them),
/// in which texture group, and which of the references ReleaseRenderTargets
/// drops, in its order. The native store (shell_render_targets_client.cpp)
/// carries them out: it finds on a slot's first use, holds the reference,
/// registers the material system's release function with the first find, and
/// drops the declared references there. Held to the pristine file by
/// RenderTargetCatalogOracleTests over Fixtures/rendertexture-stock.tsv.
/// </summary>
public sealed class RenderTargetCatalog
{
    /// <summary>TEXTURE_GROUP_RENDER_TARGET.</summary>
    public const string RenderTargetGroup = "RenderTargets";

    /// <summary>MAX_FB_TEXTURES (materialsystem/imaterialsystem.h).</summary>
    public const int MaxFrameBufferTextures = 4;

    /// <summary>MAX_TEENY_TEXTURES (rendertexture.h).</summary>
    public const int MaxTeenyTextures = 3;

    public RenderTargetCatalog()
    {
        // ReleaseRenderTargets' order: the power-of-two buffer, the camera,
        // the water reflection and refraction, the two quarter-sized buffers,
        // the depth texture, then the frame buffers by index. It does not
        // release the fullscreen texture, the HDR buffers or the teeny ones.
        var rows = new RenderTargetRow[(int)RenderTargetSlot.Count];
        rows[(int)RenderTargetSlot.PowerOfTwoFrameBuffer] = Row(RenderTargetSlot.PowerOfTwoFrameBuffer, "_rt_PowerOfTwoFB", 1);
        for (int i = 0; i < MaxFrameBufferTextures; i++)
        {
            var slot = RenderTargetSlot.FullFrameFrameBuffer0 + i;
            rows[(int)slot] = Row(slot, FrameBufferName(i), 8 + i);
        }
        rows[(int)RenderTargetSlot.WaterReflection] = Row(RenderTargetSlot.WaterReflection, "_rt_WaterReflection", 3);
        rows[(int)RenderTargetSlot.WaterRefraction] = Row(RenderTargetSlot.WaterRefraction, "_rt_WaterRefraction", 4);
        rows[(int)RenderTargetSlot.Fullscreen] = Row(RenderTargetSlot.Fullscreen, "_rt_Fullscreen", 0);
        rows[(int)RenderTargetSlot.Camera] = Row(RenderTargetSlot.Camera, "_rt_Camera", 2);
        rows[(int)RenderTargetSlot.FullFrameDepth] = Row(RenderTargetSlot.FullFrameDepth, "_rt_FullFrameDepth", 7);
        rows[(int)RenderTargetSlot.SmallBufferHdr0] = Row(RenderTargetSlot.SmallBufferHdr0, "_rt_SmallHDR0", 0);
        rows[(int)RenderTargetSlot.SmallBufferHdr1] = Row(RenderTargetSlot.SmallBufferHdr1, "_rt_SmallHDR1", 0);
        rows[(int)RenderTargetSlot.SmallBuffer0] = Row(RenderTargetSlot.SmallBuffer0, "_rt_SmallFB0", 5);
        rows[(int)RenderTargetSlot.SmallBuffer1] = Row(RenderTargetSlot.SmallBuffer1, "_rt_SmallFB1", 6);
        for (int i = 0; i < MaxTeenyTextures; i++)
        {
            var slot = RenderTargetSlot.Teeny0 + i;
            rows[(int)slot] = Row(slot, TeenyName(i), 0);
        }
        Rows = rows;
    }

    /// <summary>Every slot, in slot order.</summary>
    public IReadOnlyList<RenderTargetRow> Rows { get; }

    /// <summary>GetFullFrameFrameBufferTexture's name: index 0 has no number.</summary>
    public static string FrameBufferName(int index)
        => index != 0 ? "_rt_FullFrameFB" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "_rt_FullFrameFB";

    /// <summary>GetTeenyTexture's name.</summary>
    public static string TeenyName(int which) => "_rt_TeenyFB" + which.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Hands every row to the store, in slot order; answers how many it took.</summary>
    public int DeclareTo(IRenderTargetTable table)
    {
        int taken = 0;
        foreach (RenderTargetRow r in Rows)
        {
            if (table.Declare((int)r.Slot, r.Name, r.Group, r.Complain, r.CreationFlags, r.ReleaseOrder))
                taken++;
        }
        return taken;
    }

    // FindTexture's defaults as every getter calls it: complain, no extra creation flags.
    private static RenderTargetRow Row(RenderTargetSlot slot, string name, int releaseOrder)
        => new(slot, name, RenderTargetGroup, Complain: true, CreationFlags: 0, releaseOrder);
}
