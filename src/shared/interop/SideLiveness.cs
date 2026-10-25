namespace SourceSharp.Abi;

/// <summary>
/// Where a side's <c>EntityIsValid</c> finds the liveness mirror it answers
/// from (plan_abi_hotreload D7): the server composition's, attached while it is
/// composed and taken back at its Shutdown. Null means every ask crosses to
/// the slot.
///
/// <para>
/// Its own object rather than two fields on the side (plan_engineside_removal
/// Phase 2), so the side's container can register it and whatever asks after
/// the mirror is handed this by constructor rather than reaching into the side.
/// </para>
/// </summary>
internal sealed class SideLiveness
{
    /// <summary>The mirror attached now; null when none is.</summary>
    internal LivenessMirror? Mirror { get; private set; }

    /// <summary>
    /// Has <see cref="Mirror"/> answer <paramref name="mirror"/> until the
    /// returned attachment is disposed, which puts back whatever was attached
    /// before, and only if this mirror is still the one attached.
    /// </summary>
    internal IDisposable Attach(LivenessMirror mirror)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        LivenessMirror? previous = Mirror;
        Mirror = mirror;
        return new Attachment(this, mirror, previous);
    }

    private sealed class Attachment(SideLiveness liveness, LivenessMirror mirror, LivenessMirror? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (ReferenceEquals(liveness.Mirror, mirror))
                liveness.Mirror = previous;
        }
    }
}
