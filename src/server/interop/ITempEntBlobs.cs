namespace SourceSharp.Abi;

/// <summary>
/// The server's half of a native temp-entity forward, on its own so that
/// TempEntsBinding (plan_engineapi_vtables E13) depends on this one member,
/// not the whole module.
/// </summary>
public interface ITempEntBlobs
{
    /// <summary>
    /// Pack a native forward into the blob its class expects, and say which
    /// class. Answers the byte count, or -1 for an id nothing is written for, a
    /// class with no declaration, or a blob that does not fit.
    ///
    /// <para>
    /// THE SEND STAYS NATIVE, which is why this hands bytes back rather than
    /// playing anything: a server forward's <c>IRecipientFilter</c> belongs to
    /// its caller and cannot be reconstructed from a managed <c>TeFilter</c>,
    /// which is a kind and an origin.
    /// </para>
    /// </summary>
    int BuildForwardBlob(int id, ReadOnlySpan<float> floats, ReadOnlySpan<int> ints,
                         Span<byte> into, out int classHandle);
}
