namespace SourceSharp.Abi;

/// <summary>
/// The engine's DLIGHT_* bits, as far as anything here needs them.
///
/// <para>
/// BACKED BY A BYTE, because it is networked and its storage is what it costs:
/// <c>DynamicLightNetState.Flags</c> is this type, and an enum is laid out and
/// sent as the integer the author backed it with (plan_nettable_generic.md
/// Step 11). An <c>int</c> here would have widened a field that has two bits in
/// it, which is exactly the choice the backing type exists to let an author
/// make.
/// </para>
/// </summary>
[Flags]
public enum DynamicLightFlags : byte
{
    /// <summary>No bit set: lights both the world and models, which is a light.</summary>
    None = 0b0000_0000,

    /// <summary>Does not light world surfaces — only models.</summary>
    NoWorldIllumination = 0b0000_0001,

    /// <summary>Does not light models — only world surfaces.</summary>
    NoModelIllumination = 0b0000_0010,
}
