namespace SourceSharp.Client.Interop;

/// <summary>
/// The refusal an adapter gives when it is called with no shim area behind it.
///
/// <para>
/// An adapter takes the areas it calls by constructor (plan_shim_areas_split
/// phase 5). A client composition built over an engine that never attached
/// (the test doubles' unattached engine) hands it nulls, and a call then
/// throws this, as <c>EngineSide.ClientShims</c> threw before the attach when
/// adapters reached the areas through it.
/// </para>
/// </summary>
internal static class ShimAreaGuard
{
    public static InvalidOperationException NotAttached(string area)
        => new($"an engine slot of {area} was called on the client before its shim areas were attached");
}
