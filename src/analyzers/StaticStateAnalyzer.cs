using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceSharp.Analyzers;

/// <summary>
/// THE STATIC-STATE GATE, at build time: a static that can hold changing state
/// fails the compile (SS0001) unless the central waiver file names it, and a
/// waiver row naming no such static fails it too (SS0002), so the list only
/// shrinks.
///
/// <para>
/// The owner's rule (2026-09-29): managed code keeps no state in statics; a
/// cache, a registry, a counter, a hook or a "current" pointer is an injected
/// service. Constants, pure functions and constant tables may stay static.
/// </para>
///
/// <para>
/// WHAT COUNTS is StaticStateCensusTests' definition, symbol for symbol (the
/// census is the backstop over the built IL): a static field that is not
/// <c>const</c>, and is either assignable (this includes the backing field of
/// a static auto-property with a setter, and of a static field-like event) or
/// <c>readonly</c> of a type whose instances change. A <c>readonly</c> field of
/// a value type, an enum, a pointer, a string, <c>object</c>, <c>Type</c>,
/// <c>Assembly</c>, <c>Regex</c>, <c>CultureInfo</c>, <c>Lock</c>, a delegate
/// or a read-only/frozen/immutable collection is a constant. Types marked
/// <c>[CompilerGenerated]</c> are skipped, as are the constant tables this
/// repo's generators write into every type they generate.
/// </para>
///
/// <para>
/// Applied only to the runtime assemblies: Directory.Build.props wires it into
/// the projects named in <c>SourceSharpStaticStateGated</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticStateAnalyzer : DiagnosticAnalyzer
{
    public const string MutableStaticId = "SS0001";
    public const string StaleWaiverId = "SS0002";
    public const string BadWaiverFileId = "SS0003";

    public static readonly DiagnosticDescriptor MutableStatic = new(
        MutableStaticId,
        "Mutable static state",
        "'{0}' is mutable static state: inject it as a service (the owner's rule: no state in statics), "
        + "or, if it is a constant table or a stateless null object, waive it in " + StaticStateWaiverFile.FileName,
        "SourceSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StaleWaiver = new(
        StaleWaiverId,
        "A static-state waiver names no mutable static",
        "the waiver '{0}' names a static that is gone or no longer mutable: delete the row, so the list only shrinks",
        "SourceSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor BadWaiverFile = new(
        BadWaiverFileId,
        "The static-state waiver file is missing or malformed",
        "{0}",
        "SourceSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MutableStatic, StaleWaiver, BadWaiverFile);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // Generated code too: the census sees the generators' output in the IL.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext start)
    {
        if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.StaticState))
            return;

        string assembly = start.Compilation.AssemblyName ?? "";
        AdditionalText? file = start.Options.AdditionalFiles
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f.Path), StaticStateWaiverFile.FileName, StringComparison.Ordinal));

        // A missing or unreadable list is a failure, never a pass: without it
        // every waived static would read as unwaived, or (worse) a stale row
        // would never be seen.
        SourceText? text = file?.GetText(start.CancellationToken);

        if (file is null || text is null)
        {
            start.RegisterCompilationEndAction(end => end.ReportDiagnostic(Diagnostic.Create(BadWaiverFile, Location.None,
                $"{StaticStateWaiverFile.FileName} is not among this project's AdditionalFiles; the static-state gate cannot run without it")));
            return;
        }

        var parsed = StaticStateWaiverFile.Parse(text.ToString());
        var waived = new Dictionary<string, StaticStateWaiver>(StringComparer.Ordinal);

        foreach (var row in parsed.Rows)
        {
            if (row.Assembly == assembly)
                waived[row.Key] = row;
        }

        var found = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        start.RegisterSymbolAction(ctx =>
        {
            foreach (var (key, location) in StaticStateRule.MutableStaticsOf(assembly, (INamedTypeSymbol)ctx.Symbol))
            {
                found[key] = 0;

                if (!waived.ContainsKey(key))
                    ctx.ReportDiagnostic(Diagnostic.Create(MutableStatic, location, KeyProperty(key), key));
            }
        }, SymbolKind.NamedType);

        start.RegisterCompilationEndAction(end =>
        {
            foreach (var (line, error) in parsed.Errors)
                end.ReportDiagnostic(Diagnostic.Create(BadWaiverFile, LineLocation(file, text, line), $"{StaticStateWaiverFile.FileName}:{line}: {error}"));

            foreach (var row in waived.Values.OrderBy(r => r.Line))
            {
                if (!found.ContainsKey(row.Key))
                    end.ReportDiagnostic(Diagnostic.Create(StaleWaiver, LineLocation(file, text, row.Line), KeyProperty(row.Key), row.Key));
            }
        });
    }

    /// <summary>The diagnostic's property naming the field, as the waiver file and the census name it.</summary>
    public const string KeyName = "Key";

    private static ImmutableDictionary<string, string?> KeyProperty(string key) =>
        ImmutableDictionary<string, string?>.Empty.Add(KeyName, key);

    private static Location LineLocation(AdditionalText file, SourceText text, int line)
    {
        var span = text.Lines[line - 1].Span;
        return Location.Create(file.Path, span, text.Lines.GetLinePositionSpan(span));
    }
}

/// <summary>
/// StaticStateCensusTests' rule over Roslyn symbols. Kept apart from the
/// analyzer's plumbing so the census's parity fact can name the keys it finds.
/// </summary>
public static class StaticStateRule
{
    /// <summary>Every mutable static <paramref name="type"/> itself declares (not its nested types), as <c>Assembly: Namespace.Type.field</c>, with where to report it.</summary>
    public static IEnumerable<(string Key, Location Location)> MutableStaticsOf(string assembly, INamedTypeSymbol type)
    {
        if (IsCompilerGenerated(type))
            yield break;

        string prefix = assembly + ": " + ClrName(type) + ".";
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field when field.IsStatic && !field.IsConst:
                    if (field.IsReadOnly && IsConstantType(field.Type))
                        continue;

                    if (IsGeneratorConstant(field))
                        continue;

                    // A field-like event's backing field carries the event's
                    // name; it is reported once, under the event below.
                    if (field.AssociatedSymbol is IEventSymbol)
                        continue;

                    if (named.Add(field.Name))
                        yield return (prefix + field.Name, LocationOf(field.AssociatedSymbol ?? field));
                    break;

                case IEventSymbol ev when ev.IsStatic && IsFieldLike(ev):
                    // Its backing field is assignable and of a delegate type:
                    // never readonly, so always mutable.
                    if (named.Add(ev.Name))
                        yield return (prefix + ev.Name, LocationOf(ev));
                    break;
            }
        }
    }

    private static Location LocationOf(ISymbol symbol) => Symbols.LocationOf(symbol);

    /// <summary>An event declared like a field (<c>static event Action E;</c>): the compiler gives it a backing field.</summary>
    private static bool IsFieldLike(IEventSymbol ev) =>
        !ev.IsAbstract
        && ev.DeclaringSyntaxReferences.Length > 0
        && ev.DeclaringSyntaxReferences.All(r => r.GetSyntax() is VariableDeclaratorSyntax);

    /// <summary>The CLR's full name with '+' read as '.': namespace, then each enclosing type by its metadata name (arity suffix included).</summary>
    public static string ClrName(INamedTypeSymbol type)
    {
        var parts = new List<string>();

        for (INamedTypeSymbol? at = type; at is not null; at = at.ContainingType)
            parts.Add(at.MetadataName);

        for (INamespaceSymbol? ns = type.ContainingNamespace; ns is not null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
            parts.Add(ns.MetadataName);

        parts.Reverse();
        return string.Join(".", parts);
    }

    private static bool IsCompilerGenerated(INamedTypeSymbol type) => Symbols.IsCompilerGenerated(type);

    /// <summary>
    /// The constant tables this repo's generators write into every type they
    /// generate: a var set's descriptor table and default prototype
    /// (ScriptedVarsGenerator) and a standalone net state's layout
    /// (NetStateGenerator).
    /// </summary>
    private static bool IsGeneratorConstant(IFieldSymbol field)
    {
        if (!field.IsReadOnly)
            return false;

        INamedTypeSymbol declaring = field.ContainingType;

        for (INamedTypeSymbol? at = declaring; at is not null; at = at.BaseType)
        {
            if (FullName(at) == "SourceSharp.Abi.ScriptedVarSet")
                return field.Name is "s_Descriptors" or "s_Defaults";
        }

        // StandaloneNetStateAttribute is Inherited = false: the type's own.
        if (declaring.GetAttributes().Any(a => FullName(a.AttributeClass) == "SourceSharp.Abi.StandaloneNetStateAttribute"))
            return field.Name == "NetLayout";

        return false;
    }

    private static readonly ImmutableHashSet<string> ConstantClasses = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.String",
        "System.Object",
        "System.Type",
        "System.Reflection.Assembly",
        "System.Text.RegularExpressions.Regex",
        "System.Globalization.CultureInfo",
        "System.Threading.Lock");

    /// <summary>A type whose instances do not change, so a <c>readonly</c> field of it is a constant.</summary>
    public static bool IsConstantType(ITypeSymbol type)
    {
        if (type.IsValueType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            return true;

        // An array is never constant, whatever its elements: its slots can be
        // reassigned.
        if (type is not INamedTypeSymbol named)
            return false;

        if (ConstantClasses.Contains(FullName(named)))
            return true;

        if (named.TypeKind == TypeKind.Delegate)
            return true;

        for (INamedTypeSymbol? at = named; at is not null; at = at.BaseType)
        {
            if (FullName(at) == "System.Delegate")
                return true;
        }

        string name = named.Name;

        return name.StartsWith("IReadOnly", StringComparison.Ordinal)
            || name.StartsWith("Frozen", StringComparison.Ordinal)
            || name.StartsWith("Immutable", StringComparison.Ordinal)
            || name.StartsWith("IImmutable", StringComparison.Ordinal);
    }

    private static string FullName(INamedTypeSymbol? type) => Symbols.ClrFullName(type);
}
