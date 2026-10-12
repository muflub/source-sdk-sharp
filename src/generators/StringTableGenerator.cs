using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceSharp.Generators;

/// <summary>
/// Writes the class that answers for every <c>[StringTable]</c> declaration.
///
/// <para>
/// WHAT THE AUTHOR WRITES is one line — an interface extending
/// <c>IEngineStringTable</c> with the table's name and size on it. WHAT THIS
/// WRITES is the concrete type the container can instantiate and register under
/// that interface, forwarding to the one handle that does the work.
/// </para>
///
/// <para>
/// <b>Why a generator and not something dynamic.</b> An interface cannot be
/// instantiated, so a declaration needs a type made for it. The runtime
/// alternative is <c>System.Reflection.DispatchProxy</c>, which emits a type
/// into a dynamic assembly — a second thing holding this module's COLLECTIBLE
/// load context alive across a <c>cs_sv_reload</c>, which is the one thing this
/// project's hot reload cannot afford. The generator also refuses a malformed
/// declaration where the author is looking at it, at COMPILE time; reflection
/// could only refuse it at startup, on a server, in a log.
/// </para>
///
/// <para>
/// <b>It writes a forwarder rather than the policy.</b> Finding the table by
/// name on every use, refusing an absent one once and loudly, and refusing a
/// client-side add all live in <c>StringTableHandle</c> and are written once.
/// A generator that emitted the policy would be emitting a copy of it per
/// declaration, and the copies would be the thing that drifts.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StringTableGenerator : IIncrementalGenerator
{
    private const string TableAttribute = "SourceSharp.Abi.StringTableAttribute";
    private const string TableInterface = "SourceSharp.Abi.IEngineStringTable";

    private static readonly DiagnosticDescriptor NeedsTheInterface = new(
        "SSTAB001", "A string table declaration must extend IEngineStringTable",
        "[StringTable(\"{0}\")] on {1}, which does not extend IEngineStringTable: the declaration " +
        "would have no members and nothing a consumer could call",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NeedsRoom = new(
        "SSTAB002", "A string table must have room in it",
        "[StringTable(\"{0}\")] is sized {1}: the engine sizes a table when it creates it and " +
        "there is no resize, so a table with no room can never be written to",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoMembersOfItsOwn = new(
        "SSTAB003", "A string table declaration declares no members of its own",
        "[StringTable(\"{0}\")] on {1} declares '{2}': a declaration is a NAME for a table and the " +
        "class that answers for it is generated, so there is nothing to write a body for",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MustBeTopLevel = new(
        "SSTAB004", "A string table must be declared at namespace level",
        "[StringTable(\"{0}\")] on {1}, which is nested inside {2}: declare it beside the code that " +
        "injects it instead, so the type a consumer names is the type the container registers",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>One declaration, and everything needed to write its answer.</summary>
    private sealed record Declaration(
        string? Namespace,
        string TypeName,
        string TableName,
        ImmutableArray<Diagnostic> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            TableAttribute,
            predicate: static (node, _) => node is InterfaceDeclarationSyntax,
            transform: static (ctx, _) => Describe(ctx));

        context.RegisterSourceOutput(declarations, static (spc, declaration) =>
        {
            if (declaration is null)
                return;

            foreach (var diagnostic in declaration.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            // Still emitted when a rule was broken. The diagnostic is an error
            // and stops the build on its own; emitting nothing as well would
            // bury it under a hundred "no implementation" errors from every
            // consumer of the declaration.
            spc.AddSource($"{declaration.TypeName}.StringTable.g.cs",
                          SourceText.From(Emit(declaration), Encoding.UTF8));
        });
    }

    //-------------------------------------------------------------------------

    private static Declaration? Describe(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var attribute = ctx.Attributes.FirstOrDefault();
        if (attribute is null || attribute.ConstructorArguments.Length < 2)
            return null;

        string name = attribute.ConstructorArguments[0].Value as string ?? type.Name;
        int maxEntries = attribute.ConstructorArguments[1].Value is int given ? given : 0;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var where = type.Locations.FirstOrDefault();

        if (!type.AllInterfaces.Any(i => i.ToDisplayString() == TableInterface))
            diagnostics.Add(Diagnostic.Create(NeedsTheInterface, where, name, type.Name));

        if (maxEntries <= 0)
            diagnostics.Add(Diagnostic.Create(NeedsRoom, where, name, maxEntries));

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            diagnostics.Add(Diagnostic.Create(NoMembersOfItsOwn, member.Locations.FirstOrDefault() ?? where,
                                              name, type.Name, member.Name));
        }

        if (type.ContainingType is not null)
            diagnostics.Add(Diagnostic.Create(MustBeTopLevel, where, name, type.Name,
                                              type.ContainingType.Name));

        return new Declaration(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            name,
            diagnostics.ToImmutable());
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// The forwarder. Six members, none of which decides anything: the handle
    /// it is given is where finding the table by name, refusing an absent one
    /// and refusing a client-side add all live.
    /// </summary>
    private static string Emit(Declaration declaration)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/> by SourceSharp.Generators.StringTableGenerator.");
        sb.AppendLine("// Edit the [StringTable] declaration, not this.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (declaration.Namespace is not null)
        {
            sb.Append("namespace ").Append(declaration.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        string cls = declaration.TypeName + "__StringTable";

        sb.Append("/// <summary>What answers for <see cref=\"").Append(declaration.TypeName)
          .AppendLine("\"/>, registered by StringTableRegistry.</summary>");
        sb.Append("internal sealed class ").Append(cls).Append(" : ").Append(declaration.TypeName).AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::SourceSharp.Abi.IEngineStringTable _table;");
        sb.AppendLine();
        sb.Append("    public ").Append(cls)
          .AppendLine("(global::SourceSharp.Abi.IEngineStringTable table) => _table = table;");
        sb.AppendLine();
        sb.AppendLine("    public string Name => _table.Name;");
        sb.AppendLine("    public int Id => _table.Id;");
        sb.AppendLine("    public int Count => _table.Count;");
        sb.AppendLine("    public int AddString(string value) => _table.AddString(value);");
        sb.AppendLine("    public int FindStringIndex(string value) => _table.FindStringIndex(value);");
        sb.AppendLine("    public string? GetString(int stringIndex) => _table.GetString(stringIndex);");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
