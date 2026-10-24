using System.Reflection;

namespace SourceSharp.Tests;

/// <summary>
/// The engine interfaces' rule, for <c>IHooks&lt;T&gt;</c>: a consumer takes
/// the hooks it dispatches to by constructor, as the side's container
/// registered them, and never checks them for null. A side whose mods
/// register no hook hands an empty set, never a null.
///
/// <para>
/// Two readings of the built assemblies. <see cref="Nullable"/> is
/// <see cref="NullableEngineInterfaceCensus"/>'s walk over members, for a
/// nullable <c>IHooks&lt;T&gt;</c> at any depth of a member's type.
/// <see cref="Resolvers"/> reads the IL for a <c>Resolve&lt;IHooks&lt;T&gt;&gt;</c>
/// call, which answers null when nothing registered the hooks and so is the
/// look-up a null branch follows (<c>is { } hooks</c>); the members' walk
/// cannot see it, because its result is a local. A composition root that
/// holds a set it registered asks with <c>Require</c>, which never answers
/// null.
/// </para>
/// </summary>
internal static class HooksCensus
{
    /// <summary>Whether <paramref name="type"/> is <c>IHooks&lt;T&gt;</c>, on either side.</summary>
    public static bool IsHooks(Type type)
        => type is { IsInterface: true, IsGenericType: true }
           && type.GetGenericTypeDefinition() is { Name: "IHooks`1", Namespace: "SourceSharp.Abi" };

    /// <summary>
    /// The members of <paramref name="types"/> that hold, take or answer a
    /// nullable <c>IHooks&lt;T&gt;</c>, and how many members mention one at all.
    /// </summary>
    public static (List<string> Nullable, int Mentions) Nullable(IEnumerable<Type> types)
        => NullableEngineInterfaceCensus.Scan(types, IsHooks);

    /// <summary>
    /// The outermost types of <paramref name="assemblies"/> whose IL resolves
    /// an <c>IHooks&lt;T&gt;</c> through a look-up that answers null:
    /// <c>Resolve&lt;IHooks&lt;T&gt;&gt;</c> on a locator.
    /// </summary>
    public static string[] Resolvers(params Assembly[] assemblies)
        => IlCallCensus.Callers(IsNullableHooksLookup, assemblies);

    private static bool IsNullableHooksLookup(MethodBase callee)
        => callee is MethodInfo { Name: "Resolve", IsGenericMethod: true } method
           && method.GetGenericArguments() is [var wanted]
           && IsHooks(wanted);
}
