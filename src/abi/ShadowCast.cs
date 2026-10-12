namespace SourceSharp.Abi;

/// <summary>
/// What a client entity is casting right now. Mirrors the engine's
/// ShadowType_t, with -1 added for a handle that resolves to nothing —
/// an absent entity is not "casting nothing", it is absent.
/// </summary>
public enum ShadowCast
{
    /// <summary>No such entity on this client.</summary>
    Unknown = -1,

    /// <summary>Casting nothing.</summary>
    None = 0,

    /// <summary>The cheap blob.</summary>
    Simple = 1,

    /// <summary>A rendered shadow, baked once.</summary>
    RenderToTexture = 2,

    /// <summary>A rendered shadow that is re-rendered as the skeleton moves.</summary>
    RenderToTextureDynamic = 3,

}
