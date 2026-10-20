namespace SourceSharp.Client.Render.Shadows;

/// <summary>
/// The ConVars the shadow manager reads and writes (clientshadowmgr.cpp's
/// ten and viewrender's cl_drawshadowtexture). S2 binds these to the managed
/// ConVars that take the names over; the engine and the material system read
/// r_shadowrendertotexture, r_shadows, r_shadows_gamecontrol,
/// r_flashlightdepthtexture and r_flashlight_version2 by name, so they keep
/// them.
/// </summary>
public interface IShadowSettings
{
    /// <summary>r_shadowrendertotexture.</summary>
    bool RenderToTexture { get; }

    /// <summary>r_flashlightdepthtexture: read, and turned off when the card cannot do it.</summary>
    bool FlashlightDepthTexture { get; }

    void SetFlashlightDepthTexture(int value);

    /// <summary>r_flashlightdepthres: read, and written back with the width the card gave.</summary>
    int FlashlightDepthResolution { get; }

    void SetFlashlightDepthResolution(int value);

    /// <summary>r_shadows.</summary>
    int Shadows { get; }

    /// <summary>r_shadowmaxrendered.</summary>
    int ShadowMaxRendered { get; }

    /// <summary>r_shadows_gamecontrol.</summary>
    int ShadowsGameControl { get; }

    void SetShadowsGameControl(int value);

    /// <summary>r_flashlightmodels.</summary>
    bool FlashlightModels { get; }

    /// <summary>r_flashlight_version2.</summary>
    int FlashlightVersion2 { get; }

    /// <summary>r_flashlightdrawfrustum.</summary>
    bool FlashlightDrawFrustum { get; }

    /// <summary>cl_drawshadowtexture (viewrender's).</summary>
    int DrawShadowTexture { get; }
}

/// <summary>The console the shadow manager prints to: stock's Msg and Warning (text without the newline).</summary>
public interface IShadowConsole
{
    void Msg(string text);

    void Warning(string text);
}

/// <summary>What the leaf system's enumeration calls back with (IClientLeafShadowEnum).</summary>
public interface IShadowLeafEnumerator
{
    void EnumShadow(int userId);
}

/// <summary>
/// The client leaf system's shadow half (IClientLeafSystem's AddShadow,
/// RemoveShadow, ProjectShadow, ProjectFlashlight and
/// EnumerateShadowsInLeaves). The managed LeafRegistry answers it in S2, and
/// calls the manager's AddShadowToReceiver back for each receiver a
/// projection reaches, in the same process.
/// </summary>
public interface IShadowLeafSystem
{
    int AddShadow(int userId, int flags);

    void RemoveShadow(int handle);

    void ProjectShadow(int handle, ReadOnlySpan<int> leaves);

    void ProjectFlashlight(int handle, ReadOnlySpan<int> leaves);

    void EnumerateShadowsInLeaves(ReadOnlySpan<int> leaves, IShadowLeafEnumerator enumerator);
}
