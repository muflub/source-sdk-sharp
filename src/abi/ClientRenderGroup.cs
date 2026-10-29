namespace SourceSharp.Abi;

/// <summary>
/// Which list the renderer walks an entity in. Mirrors the engine's
/// <c>RenderGroup_t</c> (<c>public/engine/IClientLeafSystem.h</c>), and the
/// numbers are pinned to it by a <c>COMPILE_TIME_ASSERT</c> at the shell's one
/// call site — which is the only place both declarations are in scope.
///
/// <para>
/// ONLY THE FOUR THIS PORT CAN CHOOSE ARE NAMED. The engine's enum has ten;
/// the six left out are the static-prop buckets and <c>TWOPASS</c>, which
/// nothing on this hierarchy has ever asked for. Naming a value this side
/// cannot produce would be a value the shell would have to answer for.
/// </para>
/// </summary>
public enum ClientRenderGroup
{
    /// <summary>Opaque entity — the smallest size bucket, and the default.</summary>
    OpaqueEntity = 7,

    /// <summary>Blended, and drawn in the translucent pass after the opaque one.</summary>
    TranslucentEntity = 8,

    /// <summary>
    /// Opaque AND translucent: the model in the opaque pass, then again in the
    /// translucent pass with <c>STUDIO_TWOPASS</c>. Stock's stunstick and
    /// physcannon answer this from <c>GetRenderGroup</c> so that their glows
    /// draw in the translucent pass while the weapon itself is solid.
    /// </summary>
    TwoPass = 9,

    /// <summary>Solid weapon view models: a list of its own, never the leaves.</summary>
    ViewModelOpaque = 10,

    /// <summary>
    /// Transparent overlays on a view model — the second half of the viewmodel
    /// pair, and named because the sprite renderer's reflection rule refuses
    /// BOTH of them. A viewmodel sprite drawn into a mirror or a water surface
    /// would be showing the viewer's own arms standing in the world.
    /// </summary>
    ViewModelTranslucent = 11,

    /// <summary>
    /// Brushes. NOT what this port puts brush entities in — see the render-group
    /// rule for the measurement — and named so that the rule's refusal of it is
    /// a value with a name rather than a number nobody wrote down.
    /// </summary>
    OpaqueBrush = 12,
}
