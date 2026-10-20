namespace SourceSharp.Abi;

/// <summary>
/// The <c>SHADOW_FLAGS_*</c> bits <c>CClientShadowMgr::CreateShadow</c> is given.
/// Mirrors <c>ishadowmgr.h</c> and <c>itoolentity.h</c>, pinned by a
/// <c>COMPILE_TIME_ASSERT</c> at the shell's one call site.
/// </summary>
[Flags]
public enum ClientShadowFlag
{
    /// <summary>Nothing: what an entity that casts no shadow is given.</summary>
    None = 0,

    /// <summary>It is a shadow rather than a flashlight. Every shadow has this.</summary>
    Shadow = 1 << 1,

    /// <summary>It wants a render-to-texture slot.</summary>
    UseRenderToTexture = 1 << 2,

    /// <summary>That slot has to be re-rendered as the skeleton moves.</summary>
    AnimatingSource = 1 << 3,

    /// <summary>
    /// NOT A SHADOW FLAG: the render policy's answer to
    /// <c>ShouldReceiveProjectedTextures( SHADOW_FLAGS_SHADOW )</c>, carried in
    /// the block's ShadowFlags word above every SHADOW_FLAGS_* bit (C7 cluster
    /// 12 S2, Q2 "match stock"). The shell answers the question from it and
    /// hands CreateShadow the word without it.
    /// </summary>
    ReceivesShadows = 1 << 29,

    /// <summary>
    /// NOT A SHADOW FLAG either: the answer to
    /// <c>ShouldReceiveProjectedTextures( SHADOW_FLAGS_FLASHLIGHT )</c>.
    /// </summary>
    ReceivesFlashlight = 1 << 30,
}
