using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The hitgroup of the hit being dealt right now: the trace's
/// <see cref="SurfaceTraceResult.HitGroup"/>, carried beside the damage call
/// rather than through the native op (tf2 stage 0.8) — and, with it, whether
/// the shot is a critical hit (<see cref="Critical"/>, tf2 stage 1).
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
///
/// <para>
/// <b>THE CRIT FLAG RIDES THE SAME SCOPE</b>, for the same reason: the damage
/// op has no crit field (stock TF puts <c>DMG_CRITICAL</c> in the damage type
/// and its crit type on <c>CTFTakeDamageInfo</c>, both TF's), and the weapon
/// that decided the crit and the rules that read it are both managed, on one
/// stack. A shot's arms open one scope for both values; outside every scope a
/// hit is not critical.
/// </para>
/// </summary>
public sealed class DamageHitGroups
{
    private readonly Stack<(HitGroup HitGroup, bool Critical)> _open = new();

    /// <summary>
    /// Opens a scope in which every hit landed on <paramref name="hitGroup"/>,
    /// and is critical when <paramref name="critical"/> says so.
    /// </summary>
    public Scope Push(HitGroup hitGroup, bool critical = false)
    {
        _open.Push((hitGroup, critical));
        return new Scope(this);
    }

    /// <summary>The innermost open hitgroup, or <see cref="HitGroup.Generic"/> outside every scope.</summary>
    public HitGroup Current => _open.TryPeek(out var top) ? top.HitGroup : HitGroup.Generic;

    /// <summary>Whether the innermost open scope's hit is critical; false outside every scope.</summary>
    public bool Critical => _open.TryPeek(out var top) && top.Critical;

    public readonly struct Scope(DamageHitGroups owner) : IDisposable
    {
        public void Dispose() => owner._open.Pop();
    }
}
