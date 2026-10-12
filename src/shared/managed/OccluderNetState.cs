using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What an occluder tells the client about itself: which hole in the compiled
/// occlusion data it is, and whether that hole is occluding.
///
/// <para>
/// Both halves have to travel. The index is vbsp's — it is written into the
/// entity as "occludernumber" at compile time and means nothing without the map
/// it was compiled from — and the active flag is the only thing about an
/// occluder a map can change at runtime.
/// </para>
///
/// <para>
/// Nothing about WHERE the occluder is appears here, and that is not an
/// omission: an occluder's geometry is baked into the level, and the entity is
/// a switch for it rather than a shape in the world. Stock networks the brush
/// model too and then refuses to draw it — <c>C_FuncOccluder::ShouldDraw</c>
/// returns false and <c>DrawModel</c> asserts.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncOccluder)]
public partial class OccluderNetState
{
    /// <summary>vbsp's "occludernumber". Ten bits on the wire in stock; the map cannot have more.</summary>
    [NetField(Bits = 10)] public partial uint Index { get; set; }

    /// <summary>Whether this occluder is occluding.</summary>
    public partial bool Active { get; set; }
}
