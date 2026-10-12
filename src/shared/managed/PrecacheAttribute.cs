using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>What kind of asset a <see cref="PrecacheAttribute"/> names.</summary>
public enum PrecacheKind
{
    Model,
    Sound,
    Material,
    ParticleSystem,
    Decal,
}

/// <summary>
/// One asset a class needs to exist before it can be used, declared on the class
/// that needs it.
///
/// <code>
/// [Precache(PrecacheKind.Model, "sprites/gunsmoke.vmt")]
/// [TempEntity("CTESprite", "DT_TESprite")]
/// public class TeSprite : TeBaseTempEntity { … }
/// </code>
///
/// <para>
/// WHY THIS EXISTS, and it is a failure this project has already paid for
/// twice. A precache that is not done shows up as a missing model or a silent
/// sound at RUNTIME, never as a build error — so the cost of forgetting one is
/// paid by whoever next plays the game, and the symptom points at the effect
/// rather than at the list.
/// </para>
///
/// <para>
/// Both times it was an eviction that did it. Stock's
/// <c>CBaseTempEntity::PrecacheTempEnts</c> walked a singleton list and called
/// each class's own <c>Precache</c>; evicting a <c>te_*.cpp</c> took its
/// singleton off that list and nothing anywhere said so. And
/// <c>CEnvSpark::Precache</c> called <c>PrecacheScriptSound("DoSpark")</c> —
/// that file went, managed <c>EnvSpark</c> precached nothing, and every audible
/// spark logged <c>not precached</c> and played silence until it was noticed
/// months later (behaviour ledger row S7).
/// </para>
///
/// <para>
/// The common shape of both: the asset was named in one place and the class
/// that needed it in another, so removing the class left the name behind or
/// removing the name left the class. Declaring it ON the class means the two
/// cannot be separated — delete the class and its precache goes with it, keep
/// the class and its precache is found by reflection whether or not anybody
/// remembered a list.
/// </para>
///
/// <para>
/// NOT TEMP ENTITY SPECIFIC. It is discovered on any type in a scanned
/// assembly, which is why it lives here rather than beside the temp entity
/// schema.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true,
                Inherited = false)]
public sealed class PrecacheAttribute : Attribute
{
    public PrecacheAttribute(PrecacheKind kind, string asset)
    {
        Kind = kind;
        Asset = asset;
    }

    public PrecacheKind Kind { get; }
    public string Asset { get; }
}

/// <summary>
/// Finds every <see cref="PrecacheAttribute"/> in an assembly. Precaching what
/// it names is the server's (<c>SourceSharp.Server.ServerPrecache</c>): the
/// precache slots are server-only, so the sweep that calls them lives there
/// while the declarations and their discovery stay shared.
/// </summary>
public static class PrecacheRegistry
{
    /// <summary>
    /// Every loaded assembly that references the one defining
    /// <see cref="PrecacheAttribute"/>, plus that assembly itself.
    /// </summary>
    public static Assembly[] Scannable()
    {
        Assembly mine = typeof(PrecacheAttribute).Assembly;
        string name = mine.GetName().Name!;

        var found = new List<Assembly> { mine };

        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(a, mine) || a.IsDynamic)
                continue;

            try
            {
                if (a.GetReferencedAssemblies().Any(r => r.Name == name))
                    found.Add(a);
            }
            catch
            {
                // An assembly that will not answer what it references cannot be
                // asked what it declares either. Skipped rather than fatal: one
                // awkward assembly must not cost every precache in the build.
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Every declared asset, without precaching any of it — what a test asserts
    /// against, so that a class losing its attribute is a red test rather than a
    /// silent runtime absence.
    /// </summary>
    public static IEnumerable<(Type Owner, PrecacheKind Kind, string Asset)> Declared(
        ILog log, params Assembly[] assemblies)
    {
        foreach (Assembly assembly in assemblies)
            foreach (Type type in Types(assembly, log))
                foreach (PrecacheAttribute a in
                         type.GetCustomAttributes<PrecacheAttribute>(inherit: false))
                    yield return (type, a.Kind, a.Asset);
    }

    /// <summary>
    /// A partly-loadable assembly answers what it can rather than throwing. One
    /// unloadable type must not cost every precache in the build.
    /// </summary>
    private static IEnumerable<Type> Types(Assembly assembly, ILog log)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            log.Warning($"[SourceSharp] [Precache] {assembly.GetName().Name} did not fully " +
                        "load; scanning the types that did\n");

            return e.Types.Where(t => t is not null)!;
        }
    }
}
