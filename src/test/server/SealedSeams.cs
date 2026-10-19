using System.Reflection;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// Ruling D10 over the REAL types of an assembly, by reflection: which classes
/// dispatch to a set of mod-facing contracts, and whether a sealed one reaches
/// them through a registration seam (an <see cref="IHooks{T}"/> chain, the
/// container, or a named proxy) rather than holding a hard-wired instance a
/// mod could never replace. A hand-written list of types cannot see the next
/// class somebody adds; this walk does.
/// </summary>
internal static class SealedSeams
{
    private const BindingFlags Fields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every non-compiler-generated class in <paramref name="assembly"/> with an
    /// instance field (primary-constructor captures included) whose type
    /// mentions one of <paramref name="contracts"/> — directly, as a generic
    /// argument (<c>List&lt;T&gt;</c>, <c>IHooks&lt;T&gt;</c>, a dictionary's
    /// value), or as an array element.
    /// </summary>
    public static IReadOnlyList<Type> Dispatchers(Assembly assembly, IReadOnlyCollection<Type> contracts)
        => assembly.GetTypes()
                   .Where(t => t.IsClass && !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
                   .Where(t => t.GetFields(Fields).Any(f => Mentions(f.FieldType, contracts)))
                   .ToArray();

    /// <summary>
    /// The sealed dispatchers with no seam: none of their fields is an
    /// <see cref="IHooks{T}"/>, the container (<see cref="IServiceLocator"/>),
    /// or one of <paramref name="proxies"/> — and they are not a nested part of
    /// a class that has one (PlayerComposites' slot record and its skipped-
    /// component wrapper hold components the seamed composite handed them).
    /// </summary>
    public static IReadOnlyList<Type> SealedWithoutSeam(IEnumerable<Type> dispatchers, IReadOnlyCollection<Type> proxies)
        => dispatchers.Where(t => t.IsSealed)
                      .Where(t => !HasSeam(t, proxies) && !NestedInSeamed(t, proxies))
                      .ToArray();

    private static bool HasSeam(Type t, IReadOnlyCollection<Type> proxies)
        => t.GetFields(Fields).Any(f => IsSeam(f.FieldType, proxies));

    private static bool NestedInSeamed(Type t, IReadOnlyCollection<Type> proxies)
    {
        for (Type? outer = t.DeclaringType; outer is not null; outer = outer.DeclaringType)
        {
            if (HasSeam(outer, proxies))
                return true;
        }

        return false;
    }

    private static bool IsSeam(Type type, IReadOnlyCollection<Type> proxies)
    {
        Type bare = Nullable.GetUnderlyingType(type) ?? type;
        return bare == typeof(IServiceLocator)
            || proxies.Contains(bare)
            || (bare.IsGenericType && bare.GetGenericTypeDefinition() == typeof(IHooks<>));
    }

    private static bool Mentions(Type type, IReadOnlyCollection<Type> contracts)
    {
        if (contracts.Contains(type))
            return true;

        if (type.HasElementType && type.GetElementType() is { } element)
            return Mentions(element, contracts);

        return type.IsGenericType && type.GetGenericArguments().Any(a => Mentions(a, contracts));
    }
}
