using System.Reflection;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Precaches what every <see cref="PrecacheAttribute"/> names. Split from
/// <see cref="PrecacheRegistry"/> (plan_abi_hotreload Phase 1): the precache
/// slots are server-only, so the sweep that calls them is the server's, while
/// the declarations and their discovery stay in SourceSharp.Shared, which both
/// sides load.
/// </summary>
public static class ServerPrecache
{
    /// <summary>
    /// Precaches everything declared by every assembly loaded into this process
    /// that could carry a <see cref="PrecacheAttribute"/>.
    ///
    /// <para>
    /// THIS IS THE ONE CALLERS SHOULD USE. Naming assemblies by hand reintroduces
    /// the failure the attribute removes, one level up: a new assembly, or a class
    /// moved between two, is silently not swept, and the symptom is a missing model
    /// at runtime rather than an error. It was briefly written that way here and it
    /// had already missed <c>EnvSpark</c>'s sound, which lives in Server while the
    /// temp entity models live in Shared.
    /// </para>
    ///
    /// <para>
    /// "Could carry one" means: the assembly defining the attribute, and any
    /// loaded assembly that REFERENCES it (<see cref="PrecacheRegistry.Scannable"/>).
    /// An assembly that does not reference the one defining the attribute cannot
    /// have applied it, so skipping it is exact rather than a heuristic — and it
    /// keeps a level change from reflecting over every type in the framework to
    /// find nothing.
    /// </para>
    /// </summary>
    public static int PrecacheAll(IEnginePrecache precache, ILog log)
        => PrecacheAll(precache, log, PrecacheRegistry.Scannable());

    /// <summary>
    /// Precaches everything declared across <paramref name="assemblies"/> and
    /// answers how many assets it asked for.
    ///
    /// <para>
    /// <c>Inherited = false</c> on the attribute is deliberate: a derived temp
    /// entity does not re-precache its base's model. Each level is asked for its
    /// own declarations only.
    /// </para>
    ///
    /// <para>
    /// The same asset declared by two classes is precached ONCE. That is not an
    /// optimisation — the engine's own precache is idempotent — but a duplicate
    /// in the count would make the number useless as a check, and the count is
    /// the only thing standing between "the sweep ran" and "the sweep found
    /// nothing".
    /// </para>
    /// </summary>
    public static int PrecacheAll(IEnginePrecache precache, ILog log, params Assembly[] assemblies)
    {
        var done = new HashSet<(PrecacheKind, string)>();

        foreach (var (owner, kind, asset) in PrecacheRegistry.Declared(log, assemblies))
        {
            if (string.IsNullOrWhiteSpace(asset))
            {
                log.Warning($"[SourceSharp] [Precache] on {owner.Name} names nothing; " +
                            "ignored\n");
                continue;
            }

            if (!done.Add((kind, asset)))
                continue;

            switch (kind)
            {
                case PrecacheKind.Model:          precache.PrecacheModel(asset); break;
                case PrecacheKind.Sound:          precache.PrecacheSound(asset); break;
                case PrecacheKind.Material:       precache.PrecacheMaterial(asset); break;
                case PrecacheKind.ParticleSystem: precache.PrecacheParticleSystem(asset); break;
                case PrecacheKind.Decal:          precache.PrecacheDecal(asset); break;
            }
        }

        return done.Count;
    }
}
