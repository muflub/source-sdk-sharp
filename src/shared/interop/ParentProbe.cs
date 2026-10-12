namespace SourceSharp.Abi;

/// <summary>
/// What <see cref="EngineSide.EntityParentProbe"/> answers — the mirror of the
/// <c>SOURCESHARP_PARENT_PROBE_*</c> bits in <c>sourcesharp.h</c>.
///
/// <para>
/// A BIT FIELD rather than an enum of three states, because two of the three are
/// independent facts about one entity: which hierarchy holds it, and whether it
/// has a skeleton. <see cref="Standalone"/> and <see cref="Base"/> partition the
/// world exactly — an entity is on one hierarchy or the other and never on both
/// — so a probe answering both is a bug on the native side, and a bit field is
/// what lets a test SEE that rather than infer it from a collapsed enum.
/// </para>
/// </summary>
[Flags]
public enum ParentProbe
{
    /// <summary>The handle resolves to nothing.</summary>
    None = 0,

    /// <summary>It is on the parallel hierarchy.</summary>
    Standalone = 0x1,

    /// <summary>It is on the SDK's.</summary>
    Base = 0x2,

    /// <summary>It has a skeleton an attachment frame could be read from.</summary>
    CanPose = 0x4,
}
