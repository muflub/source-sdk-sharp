namespace SourceSharp.Abi;

/// <summary>
/// One route from a side's engine members up into what its composition builds
/// late (plan_engineside_removal Phase 3): the server's sound emitter, its
/// tracer and what the traces cost, its player movement, its physics init and
/// what a teleport hands the player shadows. The side makes it empty, its
/// container registers it before any binding is built, a binding takes it by
/// constructor, and the module fills it once the thing behind it exists.
///
/// <para>
/// <b>Why an object.</b> Each was a settable property on the side, which the
/// module's static routing helpers assigned through the side and the bindings
/// read through it: shared mutable state on a god object, reachable by anything
/// holding the side. A route is its own object in the side's container, as the
/// shell-less routes already were, so who fills it and who reads it are both
/// named in a constructor or a composition step.
/// </para>
///
/// <para>
/// <b>Public, with internal members.</b> A public binding or adapter that takes
/// one by constructor (<c>EngineEffectsWorld</c>) has to be able to name it,
/// and the composition root resolves it to hand in; filling and reading it are
/// the interop's (its routing helpers and the bindings), so nothing outside can
/// change where a side's members go.
/// </para>
/// </summary>
/// <typeparam name="T">What the route answers from: an interface, or a delegate.</typeparam>
public sealed class SideRoute<T> where T : class
{
    private readonly T? _empty;
    private T? _target;

    /// <summary>A route that answers nothing until it is filled.</summary>
    internal SideRoute()
    {
    }

    /// <summary>
    /// A route that answers <paramref name="empty"/> until it is filled, and
    /// again once it is emptied: for one that must always answer (the trace
    /// cost meters).
    /// </summary>
    internal SideRoute(T empty) => _empty = empty ?? throw new ArgumentNullException(nameof(empty));

    /// <summary>What the route answers from now: what filled it, else its empty answer.</summary>
    internal T? Target => _target ?? _empty;

    /// <summary>The route answers from <paramref name="target"/> from now on; null empties it.</summary>
    internal void Fill(T? target) => _target = target;
}
