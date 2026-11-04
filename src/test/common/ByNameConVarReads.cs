using System.Reflection;

namespace SourceSharp.Tests;

/// <summary>
/// Who in a game assembly reads a convar by name: the types whose IL calls
/// <c>IEngineConsole.FindConVarValue</c> or <c>NativeConsole.ValueOf</c>. A fixed name is a
/// class taken by constructor, so the names here come from data or a debug route.
/// </summary>
internal static class ByNameConVarReads
{
    public static string[] Callers(params Assembly[] assemblies)
        => IlCallCensus.Callers(m => m.Name is "FindConVarValue" or "ValueOf"
                                     && m.DeclaringType?.Name is "IEngineConsole" or "NativeConsole", assemblies);
}
