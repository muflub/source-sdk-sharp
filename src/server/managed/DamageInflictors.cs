using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The inflictor of the damage being dealt right now: CTakeDamageInfo's
/// inflictor, carried beside the call rather than through the native op.
///
/// <para>
/// The damage op names only the attacker (SourceSharpOpArgs.hOther), and the
/// managed damage entry points build CTakeDamageInfo( attacker, attacker ). A
/// victim that needs to know where the hit came FROM -- the player's Damage
/// message, stock's m_DmgOrigin = info.GetInflictor()->GetAbsOrigin() -- reads
/// it here. The damage call is synchronous (managed -> native TakeDamage ->
/// the victim's OnDamaged on the same stack), so a scope opened around it is
/// exactly the hits it deals. Scopes nest; one per composition (statics to zero).
/// </para>
/// </summary>
public sealed class DamageInflictors
{
    private readonly Stack<(EntityHandle Inflictor, Vector Origin)> _open = new();

    /// <summary>Opens a scope in which every hit was inflicted by <paramref name="inflictor"/>, from <paramref name="origin"/>.</summary>
    public Scope Push(EntityHandle inflictor, Vector origin)
    {
        _open.Push((inflictor, origin));
        return new Scope(this);
    }

    /// <summary>The innermost open inflictor, or false outside every scope.</summary>
    public bool TryCurrent(out EntityHandle inflictor, out Vector origin)
    {
        if (_open.TryPeek(out var top))
        {
            (inflictor, origin) = top;
            return true;
        }

        inflictor = EntityHandle.None;
        origin = default;
        return false;
    }

    public readonly struct Scope(DamageInflictors owner) : IDisposable
    {
        public void Dispose() => owner._open.Pop();
    }
}
