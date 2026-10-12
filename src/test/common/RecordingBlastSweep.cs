using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Tests;

/// <summary>
/// WHAT AN ENTITY ASKED AN EXPLOSION TO DO, without running the traversal.
///
/// <para>
/// The seam <c>Damage.ExplosionDamage</c> used to be. Until the shim wave's
/// <c>policy-rules</c> lane, an explosion was ONE outbound ABI call and every
/// fact about "what did this entity ask for" read it out of
/// <see cref="RecordingEngine"/>'s log. The falloff, the line of sight and the
/// blocking rule are managed now (<see cref="BlastSweep"/>), so that log
/// records the SWEEP's conversation with the engine instead — twenty traces and
/// a sphere query, none of which say what magnitude was asked for or which
/// entity the blast was told to pass through.
/// </para>
///
/// <para>
/// So the request gets a recorder of its own, and every fact that used to read
/// the ABI call reads this instead. It records and does NOTHING: a test about
/// what a rocket asks for must not depend on what the world happens to contain,
/// which is the same reason the engine double records rather than answers.
/// <see cref="BlastRulesTests"/> is where the traversal's own arithmetic is
/// pinned.
/// </para>
/// </summary>
public sealed class RecordingBlastSweep : IBlastSweep
{
    /// <summary>One request, with every argument it carried.</summary>
    public readonly record struct Request(
        Vector Origin,
        float Magnitude,
        float Radius,
        DamageType DamageType,
        EntityHandle Attacker,
        EntityHandle Inflictor,
        EntityHandle Ignored,
        float ExplicitForce);

    private readonly List<Request> _requests = new();

    /// <summary>Every blast asked for, in order.</summary>
    public IReadOnlyList<Request> Requests => _requests;

    public void Apply(Vector origin, float magnitude, float radius,
                      DamageType damageType,
                      EntityHandle attacker,
                      EntityHandle inflictor,
                      EntityHandle ignored,
                      float explicitForce = 0f)
        => _requests.Add(new Request(origin, magnitude, radius, damageType,
                                     attacker, inflictor, ignored, explicitForce));
}
