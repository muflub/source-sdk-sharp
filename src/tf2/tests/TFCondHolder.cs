using TF2.Shared.Conditions;

namespace TF2.Tests;

/// <summary>
/// A plain <see cref="ITFCondWords"/> for facts that keep one player's set in
/// the conditions object itself: <see cref="Read"/> answers the bits the last
/// <see cref="Write"/> stored, <see cref="Write"/> stores just what it was
/// handed — the same round trip the carrier's words make, with no entity to
/// bind a net state to. Facts that ride a bound state hand the carrier's own
/// <c>TFCarrierCondWords</c> instead.
/// </summary>
public sealed class TFCondHolder : ITFCondWords
{
    private TFCondMask bits;

    public TFCondMask Read() => bits;

    public void Write(in TFCondMask bits) => this.bits = bits;
}
