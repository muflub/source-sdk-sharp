using System;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceSharp.Analyzers;

/// <summary>
/// Which of this repo's rules apply to the project being compiled. Every gated
/// project loads the one analyzer assembly; Directory.Build.props says which
/// rule sets it is under in the <c>SourceSharpAnalyzerRoles</c> property, which
/// it makes visible to the compiler (CompilerVisibleProperty), comma-separated:
/// the value reaches the compiler through an .editorconfig, where a ';' starts
/// a comment and would empty it.
/// </summary>
public static class AnalyzerRoles
{
    public const string Property = "build_property.SourceSharpAnalyzerRoles";

    /// <summary>The runtime assemblies: no mutable static state (SS0001-SS0003).</summary>
    public const string StaticState = "static-state";

    /// <summary>A mod's assemblies: the core only by injection (SS0010-SS0013).</summary>
    public const string Mod = "mod";

    /// <summary>The test projects: their own rules (SS0020-).</summary>
    public const string Test = "test";

    /// <summary>The never-reloading contract assemblies: interfaces, enums and pure data only (SS0030).</summary>
    public const string Interface = "interface";

    public static bool Has(AnalyzerOptions options, string role)
    {
        if (!options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Property, out string? roles) || roles is null)
            return false;

        return roles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Any(r => r.Trim() == role);
    }
}
