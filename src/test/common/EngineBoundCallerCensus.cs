using System.Reflection;

namespace SourceSharp.Tests;

/// <summary>
/// Who calls the side's bound slots (<c>EngineBound</c>) in a product
/// assembly, read from its IL: only the acquisition layer may. Everything
/// else is handed the engine interface it calls by constructor, as the
/// composition registered it (EngineServices.AddForwarders), so it never
/// looks one up through the side.
/// </summary>
internal static class EngineBoundCallerCensus
{
    /// <summary>
    /// The acquisition layer: the slots themselves and the composition that
    /// acquires and registers each interface. The side's own model caches are
    /// built by that composition over the model info it registered.
    /// </summary>
    public static readonly string[] Allowed =
    [
        "SourceSharp.Abi.EngineBound",
        "SourceSharp.Abi.EngineBoundSet",
        "SourceSharp.Abi.EngineServices",
    ];

    private const string Bound = "SourceSharp.Abi.EngineBound";

    /// <summary>
    /// The types of <paramref name="assemblies"/> whose IL calls a member of
    /// <c>EngineBound</c>, each named by its outermost type (a lambda's closure
    /// counts against the type it was written in): those outside
    /// <see cref="Allowed"/>, and all of them, so a scan that saw none is visible.
    /// </summary>
    public static (string[] Outside, string[] All) Callers(params Assembly[] assemblies)
    {
        string[] all = IlCallCensus.Callers(callee => callee.DeclaringType?.FullName == Bound, assemblies);
        return (all.Except(Allowed).ToArray(), all);
    }
}
