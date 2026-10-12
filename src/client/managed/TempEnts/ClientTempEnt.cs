using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// A client temp entity that is handed its own DECLARATION, already read.
///
/// <para>
/// WHAT THIS REMOVES. Every player's first line used to be the same line:
/// </para>
/// <code>
/// public void Play(in TempEntReader te)
/// {
///     TeSparks d = TempEntDeclarationReader.Read&lt;TeSparks&gt;(in te);
///     ...
/// </code>
/// <para>
/// Forty-odd classes each restating which declaration they play, in a line the
/// compiler could not check against the <c>[ClientTempEnt("CTESparks")]</c>
/// above it. Deriving from <c>ClientTempEnt&lt;TeSparks&gt;</c> says it once, in
/// the place a reader looks for it, and makes it the SIGNATURE:
/// </para>
/// <code>
/// public override void Play(TeSparks te)
/// </code>
///
/// <para>
/// THE READER STILL EXISTS AND IS STILL BORROWED. It addresses the native
/// shell's storage and is valid only for the call, which is why the read happens
/// here and the subclass never sees it — a state object outlives the blob, a
/// <see cref="TempEntReader"/> must not, and making that impossible to get wrong
/// is most of the point.
/// </para>
///
/// <para>
/// Everything the effect needs is in the blob, INCLUDING the base chain's
/// fields: <c>Origin</c> belongs to <c>DT_TEParticleSystem</c> and arrives with
/// the rest, because a base nested at datatable offset zero puts its props at
/// their own offsets in the same object and the declaration reader walks the
/// chain root-first.
/// </para>
/// </summary>
/// <typeparam name="T">
/// The shared declaration this plays — the same class the server sends through,
/// so the two sides cannot disagree about a field without disagreeing about a
/// type.
/// </typeparam>
public abstract class ClientTempEnt<T> : IClientTempEnt
    where T : TempEntState, new()
{
    /// <summary>
    /// The untyped entry the registry calls, which reads the blob and is the
    /// only place that does. EXPLICIT, so it is not part of a subclass's surface
    /// and cannot be called with a reader by mistake.
    /// </summary>
    void IClientTempEnt.Play(in TempEntReader te)
        => Play(TempEntDeclarationReader.Read<T>(in te));

    /// <summary>
    /// The locally-forwarded entry. The cast is checked and cannot be wrong for
    /// a state the dispatcher built: it looks the player up by the class name
    /// the declaration itself carries, so the two agree by construction.
    /// </summary>
    void IClientTempEnt.Play(TempEntState state) => Play((T)state);

    /// <summary>One arrived, already decoded into the declaration it was sent as.</summary>
    public abstract void Play(T te);
}
