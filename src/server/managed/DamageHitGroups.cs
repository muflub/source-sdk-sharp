using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The hitgroup of the hit being dealt right now: the trace's
/// <see cref="SurfaceTraceResult.HitGroup"/>, carried beside the damage call
/// rather than through the native op (tf2 stage 0.8).
///
/// <para>
/// Stock reads <c>tr.hitgroup</c> at the trace site and hands the trace to
/// <c>TraceAttack</c>; its <c>CTakeDamageInfo</c> has no hitgroup, and neither
/// has the damage op (<c>SourceSharpOpArgs</c>). Both ends of a bullet are
/// managed — <see cref="Arsenal"/> fires it, the victim's
/// <see cref="ScriptedEntity.OnDamagedAt"/> answers it — and the call between
/// them is synchronous (managed -&gt; native TakeDamage -&gt; the victim on the
/// same stack), so a scope opened around the damage call is exactly the hits
/// that call deals, as <see cref="DamageInflictors"/> carries the inflictor.
/// Outside every scope the hitgroup is <see cref="HitGroup.Generic"/>, which
/// is stock's answer for damage with no trace behind it. Scopes nest; one per
/// composition (statics to zero).
/// </para>
/// </summary>
public sealed class DamageHitGroups
{
    private readonly Stack<HitGroup> _open = new();

    /// <summary>Opens a scope in which every hit landed on <paramref name="hitGroup"/>.</summary>
    public Scope Push(HitGroup hitGroup)
    {
        _open.Push(hitGroup);
        return new Scope(this);
    }

    /// <summary>The innermost open hitgroup, or <see cref="HitGroup.Generic"/> outside every scope.</summary>
    public HitGroup Current => _open.TryPeek(out var top) ? top : HitGroup.Generic;

    public readonly struct Scope(DamageHitGroups owner) : IDisposable
    {
        public void Dispose() => owner._open.Pop();
    }
}
