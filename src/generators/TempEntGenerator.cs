using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceSharp.Generators;

/// <summary>
/// Writes a temp entity declaration's read and write from its <c>[TeProp]</c> members.
///
/// <para>
/// WHAT THE AUTHOR WRITES is a <c>partial</c> class or struct with <c>[TempEntity]</c>, whose
/// public <c>[TeProp]</c> fields are the wire, in declaration order, root of the chain first.
/// WHAT THIS WRITES on it: <c>ReadMembers</c>, <c>WriteMembers</c>, a static <c>ReadFrom</c> on
/// a class, the class name, and <c>[TeGenerated]</c> saying what position each field was given.
/// </para>
///
/// <para>
/// A temp entity arrives per bullet impact, so the generated code reads and writes each member
/// directly: no reflection, no boxing, no name to hash. A position is an index into
/// <c>TempEntType.Slots</c>, which the registry builds from the schema, and the registry refuses
/// at startup a type whose <c>[TeGenerated]</c> disagrees with that list. The schema stays the
/// only thing that says where a field is and how wide it is; this says only which field a
/// member is.
/// </para>
///
/// <para>
/// Everything a declaration cannot express is a diagnostic here, naming the class and the
/// member. A runtime warning would mean a field left at zero, which looks like one that
/// arrived as zero.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TempEntGenerator : IIncrementalGenerator
{
    private const string DeclarationAttribute = "SourceSharp.Abi.TempEntityAttribute";
    private const string PropAttribute        = "SourceSharp.Abi.TePropAttribute";
    private const string StateType            = "SourceSharp.Abi.TempEntState";
    private const string VectorType           = "SourceSharp.Abi.Vector";
    private const string AngleType            = "SourceSharp.Abi.QAngle";
    private const string ColorType            = "SourceSharp.Abi.Color32";
    private const string HandleType           = "SourceSharp.Abi.EntityHandle";
    private const string InlineArray          = "System.Runtime.CompilerServices.InlineArrayAttribute";

    private const string Reader = "global::SourceSharp.Abi.TempEntReader";
    private const string Writer = "global::SourceSharp.Abi.TempEntWriter";

    /// <summary>TePropKind's values, as the attribute's constructor argument carries them.</summary>
    private const int KindDataTable = 5;
    private const int KindArray     = 6;

    /// <summary>TeFlags.Unsigned.</summary>
    private const int FlagUnsigned = 1;

    /// <summary>How deep tables may nest: what TempEntRegistry.AddFieldsOf flattens.</summary>
    private const int MaxNesting = 8;

    private enum Shape { Bool, Byte, Short, Int, Float, Vector, Angle, Color, Handle, Enum, Run, Nested }

    /// <summary>One <c>[TeProp]</c> member of a declaration, at its position.</summary>
    private sealed record Member(string Name, Shape Shape, bool Unsigned, string TypeName, int Slot, int RunLength);

    private sealed record Described(
        string Key,
        string? Namespace,
        ImmutableArray<string> Containers,
        string Keyword,
        string TypeName,
        bool IsStruct,
        bool IsAbstract,
        bool HasDefaultConstructor,
        bool BaseIsDeclaration,
        string ClassName,
        int FirstSlot,
        ImmutableArray<Member> Members,
        ImmutableArray<string> Slots,
        ImmutableArray<Diagnostic> Diagnostics,
        bool Emit);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            DeclarationAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax,
            transform: static (ctx, _) => Describe(ctx));

        context.RegisterSourceOutput(declarations, static (spc, d) =>
        {
            if (d is null)
                return;

            foreach (var diagnostic in d.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            if (d.Emit)
            {
                string hint = d.Key.Replace("global::", "").Replace('<', '_').Replace('>', '_');
                spc.AddSource($"{hint}.TempEnt.g.cs", SourceText.From(Emit(d), Encoding.UTF8));
            }
        });

        // [TeProp] on a type the registry never finds: read and written by nothing.
        var strays = context.SyntaxProvider.ForAttributeWithMetadataName(
            PropAttribute,
            predicate: static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
            transform: static (ctx, _) => DescribeStray(ctx));

        context.RegisterSourceOutput(strays, static (spc, diagnostic) =>
        {
            if (diagnostic is not null)
                spc.ReportDiagnostic(diagnostic);
        });
    }

    private static Diagnostic? DescribeStray(GeneratorAttributeSyntaxContext ctx)
    {
        ISymbol member = ctx.TargetSymbol;

        if (member.ContainingType is not { } owner || IsDeclaration(owner))
            return null;

        return Diagnostic.Create(NoDeclaration, member.Locations.FirstOrDefault(), owner.Name, member.Name);
    }

    //-------------------------------------------------------------------------

    private static Described? Describe(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var location = type.Locations.FirstOrDefault();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        string key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        Described Refused() => new(key, null, ImmutableArray<string>.Empty, "", type.Name, false, false, false, false,
                                   "", 0, ImmutableArray<Member>.Empty, ImmutableArray<string>.Empty,
                                   diagnostics.ToImmutable(), Emit: false);

        if (type.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(Generic, location, type.Name));
            return Refused();
        }

        if (!IsPartial(type))
        {
            diagnostics.Add(Diagnostic.Create(NotPartial, location, type.Name));
            return Refused();
        }

        var containers = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (!IsPartial(outer))
            {
                diagnostics.Add(Diagnostic.Create(NotPartial, location, outer.Name));
                return Refused();
            }

            containers.Insert(0, KeywordOf(outer) + " " + outer.Name);
        }

        bool isStruct = type.IsValueType;
        var baseType = type.BaseType;
        bool baseIsDeclaration = !isStruct && baseType is not null && IsDeclaration(baseType);

        if (!isStruct && !baseIsDeclaration && baseType?.ToDisplayString() != StateType)
        {
            diagnostics.Add(Diagnostic.Create(BadBase, location, type.Name));
            return Refused();
        }

        string? className = ClassNameOf(type);
        if (className is null)
        {
            diagnostics.Add(Diagnostic.Create(BadName, location, type.Name));
            return Refused();
        }

        // Everything before this class's own members: what the chain's generated reads fill.
        var slots = new List<string>();
        if (baseIsDeclaration)
            AppendChain(baseType!, slots, depth: 0);

        int first = slots.Count;

        var members = ImmutableArray.CreateBuilder<Member>();

        foreach (ISymbol symbol in type.GetMembers())
        {
            if (symbol.IsImplicitlyDeclared || PropOf(symbol) is not { } prop)
                continue;

            if (symbol is not IFieldSymbol { IsStatic: false, IsReadOnly: false, IsConst: false,
                                             DeclaredAccessibility: Accessibility.Public } field)
            {
                diagnostics.Add(Diagnostic.Create(NotAField, symbol.Locations.FirstOrDefault(), type.Name, symbol.Name));
                continue;
            }

            if (!Classify(type, field, prop, diagnostics, out Shape shape, out int runLength))
                continue;

            members.Add(new Member(field.Name, shape, IsUnsigned(prop),
                                   field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                   slots.Count - first, runLength));

            AppendMember(field, prop, shape, slots, depth: 0);
        }

        if (diagnostics.Count > 0)
            return Refused();

        if (NestingOf(type, 0) > MaxNesting)
        {
            diagnostics.Add(Diagnostic.Create(TooDeep, location, type.Name, members.First(m => m.Shape == Shape.Nested).Name, MaxNesting));
            return Refused();
        }

        return new Described(
            key,
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            containers.ToImmutable(),
            KeywordOf(type),
            type.Name,
            isStruct,
            type.IsAbstract,
            isStruct || type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility != Accessibility.Private),
            baseIsDeclaration,
            className,
            first,
            members.ToImmutable(),
            slots.ToImmutableArray(),
            diagnostics.ToImmutable(),
            Emit: true);
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static string KeywordOf(INamedTypeSymbol type) =>
        (type.IsRecord, type.IsValueType) switch
        {
            (true, true)   => "record struct",
            (true, false)  => "record",
            (false, true)  => "struct",
            (false, false) => "class",
        };

    private static AttributeData? DeclarationOf(INamedTypeSymbol type) =>
        type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DeclarationAttribute);

    private static bool IsDeclaration(INamedTypeSymbol type) => DeclarationOf(type) is not null;

    private static AttributeData? PropOf(ISymbol member) =>
        member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == PropAttribute);

    /// <summary>TempEntityAttribute.ClassNameOf's rule, or null where that rule throws.</summary>
    private static string? ClassNameOf(INamedTypeSymbol type)
    {
        var attribute = DeclarationOf(type);

        if (attribute is { ConstructorArguments.Length: > 0 } && attribute.ConstructorArguments[0].Value is string said)
            return said;

        return type.Name.StartsWith("Te", StringComparison.Ordinal) && type.Name.Length > 2
            ? "CTE" + type.Name.Substring(2)
            : null;
    }

    private static string? WireNameOf(AttributeData prop)
    {
        foreach (var arg in prop.NamedArguments)
            if (arg.Key == "WireName" && arg.Value.Value is string name)
                return name;

        return null;
    }

    private static bool IsUnsigned(AttributeData prop)
    {
        foreach (var arg in prop.NamedArguments)
            if (arg.Key == "Flags" && arg.Value.Value is int flags)
                return (flags & FlagUnsigned) != 0;

        return false;
    }

    /// <summary>The stated kind, or null for <c>[TeProp]</c>, whose kind comes from the type.</summary>
    private static int? StatedKind(AttributeData prop) =>
        prop.ConstructorArguments.Length > 0 && prop.ConstructorArguments[0].Value is int kind ? kind : null;

    private static string? NamedString(AttributeData prop, string name)
    {
        foreach (var arg in prop.NamedArguments)
            if (arg.Key == name && arg.Value.Value is string value)
                return value;

        return null;
    }

    private static int NamedInt(AttributeData prop, string name)
    {
        foreach (var arg in prop.NamedArguments)
            if (arg.Key == name && arg.Value.Value is int value)
                return value;

        return 0;
    }

    /// <summary>
    /// What a member is, or a diagnostic. The type decides, as TePropAttribute.Resolve's does;
    /// only a nested table and an array need the stated kind as well, and each of those must
    /// be stated.
    /// </summary>
    private static bool Classify(INamedTypeSymbol owner, IFieldSymbol field, AttributeData prop,
                                 ImmutableArray<Diagnostic>.Builder diagnostics,
                                 out Shape shape, out int runLength)
    {
        shape = default;
        runLength = 0;

        var where = field.Locations.FirstOrDefault();
        int? stated = StatedKind(prop);

        if (stated == KindDataTable)
        {
            if (field.Type is not INamedTypeSymbol { IsValueType: true } nested || !IsDeclaration(nested)
                || string.IsNullOrEmpty(NamedString(prop, "SubTable")))
            {
                diagnostics.Add(Diagnostic.Create(BadTable, where, owner.Name, field.Name));
                return false;
            }

            shape = Shape.Nested;
            return true;
        }

        if (field.Type is INamedTypeSymbol run
            && run.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == InlineArray) is { } inline)
        {
            int length = inline.ConstructorArguments.Length > 0 && inline.ConstructorArguments[0].Value is int n ? n : 0;

            var element = run.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic);

            if (stated != KindArray || element?.Type.ToDisplayString() != VectorType || length <= 0
                || NamedInt(prop, "Elems") != length)
            {
                diagnostics.Add(Diagnostic.Create(BadRun, where, owner.Name, field.Name));
                return false;
            }

            shape = Shape.Run;
            runLength = length;
            return true;
        }

        if (stated == KindArray)
        {
            diagnostics.Add(Diagnostic.Create(BadRun, where, owner.Name, field.Name));
            return false;
        }

        Shape? simple = ShapeOf(field.Type);

        if (simple is null)
        {
            diagnostics.Add(Diagnostic.Create(Unsupported, where, owner.Name, field.Name, field.Type.ToDisplayString()));
            return false;
        }

        shape = simple.Value;
        return true;
    }

    private static Shape? ShapeOf(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return Shape.Bool;
            case SpecialType.System_Byte:    return Shape.Byte;
            case SpecialType.System_Int16:   return Shape.Short;
            case SpecialType.System_Int32:   return Shape.Int;
            case SpecialType.System_Single:  return Shape.Float;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
        {
            // The blob's integers are one, two and four bytes wide.
            return underlying.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64
                ? null
                : Shape.Enum;
        }

        return type.ToDisplayString() switch
        {
            VectorType => Shape.Vector,
            AngleType  => Shape.Angle,
            ColorType  => Shape.Color,
            HandleType => Shape.Handle,
            _          => null,
        };
    }

    /// <summary>A chain's positions, root first, as TempEntRegistry.Publish walks it.</summary>
    private static void AppendChain(INamedTypeSymbol type, List<string> slots, int depth)
    {
        if (type.BaseType is { } baseType && IsDeclaration(baseType))
            AppendChain(baseType, slots, depth);

        AppendOwn(type, slots, depth);
    }

    private static void AppendOwn(INamedTypeSymbol type, List<string> slots, int depth)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field && PropOf(field) is { } prop)
            {
                bool nested = StatedKind(prop) == KindDataTable;
                AppendMember(field, prop, nested ? Shape.Nested : ShapeOf(field.Type) ?? Shape.Int, slots, depth);
            }
        }
    }

    private static void AppendMember(IFieldSymbol field, AttributeData prop, Shape shape, List<string> slots, int depth)
    {
        string wire = WireNameOf(prop) ?? field.Name;

        if (shape == Shape.Color)
        {
            slots.Add(wire + "R");
            slots.Add(wire + "G");
            slots.Add(wire + "B");
            slots.Add(wire + "A");
            return;
        }

        slots.Add(wire);

        if (shape == Shape.Nested && depth <= MaxNesting && field.Type is INamedTypeSymbol nested)
            AppendOwn(nested, slots, depth + 1);
    }

    /// <summary>How many tables deep a type's members nest.</summary>
    private static int NestingOf(INamedTypeSymbol type, int depth)
    {
        if (depth > MaxNesting)
            return depth;

        int deepest = depth;

        foreach (var member in type.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false } field && PropOf(field) is { } prop
                && StatedKind(prop) == KindDataTable && field.Type is INamedTypeSymbol nested)
            {
                deepest = Math.Max(deepest, NestingOf(nested, depth + 1));
            }
        }

        return deepest;
    }

    //-------------------------------------------------------------------------

    private static string Emit(Described d)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/> by SourceSharp.Generators.TempEntGenerator.");
        sb.AppendLine("// Edit the declaration's [TeProp] members, not this. Their ORDER is the wire.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (d.Namespace is not null)
            sb.Append("namespace ").Append(d.Namespace).AppendLine(";").AppendLine();

        foreach (var container in d.Containers)
            sb.Append("partial ").Append(container).AppendLine().AppendLine("{");

        bool factory = !d.IsStruct && !d.IsAbstract && d.HasDefaultConstructor;

        sb.Append("[global::SourceSharp.Abi.TeGenerated(").Append(Literal(d.ClassName));
        foreach (string slot in d.Slots)
            sb.Append(", ").Append(Literal(slot));
        sb.AppendLine(")]");

        sb.Append("partial ").Append(d.Keyword).Append(' ').Append(d.TypeName);
        if (factory)
            sb.Append(" : global::SourceSharp.Abi.ITempEntDeclaration<").Append(d.TypeName).Append('>');
        sb.AppendLine();
        sb.AppendLine("{");

        string hides = d.BaseIsDeclaration ? "new " : "";

        sb.AppendLine("    /// <summary>The name the two sides pair on.</summary>");
        sb.Append("    public ").Append(hides).Append("const string WireClassName = ").Append(Literal(d.ClassName)).AppendLine(";");
        sb.AppendLine();

        if (!d.IsStruct)
        {
            sb.AppendLine("    /// <inheritdoc/>");
            sb.AppendLine("    public override string ClassName => WireClassName;");
            sb.AppendLine();
        }

        if (factory)
        {
            sb.AppendLine("    /// <summary>A new one, with every member its chain declares filled from the blob.</summary>");
            sb.Append("    public static ").Append(hides).Append(d.TypeName).Append(" ReadFrom(in ").Append(Reader).AppendLine(" te)");
            sb.AppendLine("    {");
            sb.Append("        var state = new ").Append(d.TypeName).AppendLine("();");
            sb.AppendLine("        state.ReadMembers(in te);");
            sb.AppendLine("        return state;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        EmitRead(sb, d);
        sb.AppendLine();
        EmitWrite(sb, d);

        sb.AppendLine("}");

        foreach (var _ in d.Containers)
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Literal(string text) =>
        SymbolDisplay.FormatLiteral(text, quote: true);

    /// <summary>The position of a member's first slot, as an expression.</summary>
    private static string At(Described d, int slot) =>
        d.IsStruct ? $"first + {slot}" : (d.FirstSlot + slot).ToString();

    private static void EmitRead(StringBuilder sb, Described d)
    {
        sb.AppendLine("    /// <summary>Fills the members, root of the chain first; answers how many.</summary>");

        if (d.IsStruct)
            sb.Append("    public int ReadMembers(in ").Append(Reader).AppendLine(" te, int first = 0)");
        else
            sb.Append("    public override int ReadMembers(in ").Append(Reader).AppendLine(" te)");

        sb.AppendLine("    {");
        sb.Append("        int filled = ").Append(d.BaseIsDeclaration ? "base.ReadMembers(in te)" : "0").AppendLine(";");

        foreach (var m in d.Members)
        {
            string at = At(d, m.Slot);
            string self = "this." + m.Name;
            string number = m.Unsigned ? "SlotUInt" : "SlotInt";

            switch (m.Shape)
            {
                case Shape.Vector: sb.AppendLine($"        {self} = te.SlotVector({at}); filled++;"); break;
                case Shape.Angle:  sb.AppendLine($"        {self} = te.SlotQAngle({at}); filled++;"); break;
                case Shape.Float:  sb.AppendLine($"        {self} = te.SlotFloat({at}); filled++;"); break;
                case Shape.Bool:   sb.AppendLine($"        {self} = te.SlotBool({at}); filled++;"); break;
                case Shape.Byte:   sb.AppendLine($"        {self} = unchecked((byte)te.{number}({at})); filled++;"); break;
                case Shape.Short:  sb.AppendLine($"        {self} = unchecked((short)te.{number}({at})); filled++;"); break;
                case Shape.Int:    sb.AppendLine($"        {self} = te.{number}({at}); filled++;"); break;
                case Shape.Handle: sb.AppendLine($"        {self} = te.SlotHandle({at}); filled++;"); break;
                case Shape.Enum:   sb.AppendLine($"        {self} = unchecked(({m.TypeName})te.{number}({at})); filled++;"); break;
                case Shape.Color:
                    sb.AppendLine($"        {self} = new global::SourceSharp.Abi.Color32(te.SlotUInt({At(d, m.Slot)}), te.SlotUInt({At(d, m.Slot + 1)}), te.SlotUInt({At(d, m.Slot + 2)}), te.SlotUInt({At(d, m.Slot + 3)})); filled++;");
                    break;
                case Shape.Run:
                    sb.AppendLine($"        for (int i = 0; i < {m.RunLength}; i++)");
                    sb.AppendLine($"            {self}[i] = te.SlotVectorAt({at}, i);");
                    sb.AppendLine("        filled++;");
                    break;
                case Shape.Nested:
                    sb.AppendLine($"        filled += {self}.ReadMembers(in te, {At(d, m.Slot + 1)});");
                    break;
            }
        }

        sb.AppendLine("        return filled;");
        sb.AppendLine("    }");
    }

    private static void EmitWrite(StringBuilder sb, Described d)
    {
        sb.AppendLine("    /// <summary>Writes the members, root of the chain first; answers how many went down.</summary>");

        if (d.IsStruct)
            sb.Append("    public readonly int WriteMembers(ref ").Append(Writer).AppendLine(" writer, int first = 0)");
        else
            sb.Append("    public override int WriteMembers(ref ").Append(Writer).AppendLine(" writer)");

        sb.AppendLine("    {");
        sb.Append("        int written = ").Append(d.BaseIsDeclaration ? "base.WriteMembers(ref writer)" : "0").AppendLine(";");

        foreach (var m in d.Members)
        {
            string at = At(d, m.Slot);
            string self = "this." + m.Name;

            switch (m.Shape)
            {
                case Shape.Vector: sb.AppendLine($"        if (writer.SlotVector({at}, {self})) written++;"); break;
                case Shape.Angle:  sb.AppendLine($"        if (writer.SlotQAngle({at}, {self})) written++;"); break;
                case Shape.Float:  sb.AppendLine($"        if (writer.SlotFloat({at}, {self})) written++;"); break;
                case Shape.Bool:   sb.AppendLine($"        if (writer.SlotInt({at}, {self} ? 1 : 0)) written++;"); break;
                case Shape.Byte:
                case Shape.Short:
                case Shape.Int:    sb.AppendLine($"        if (writer.SlotInt({at}, {self})) written++;"); break;
                case Shape.Handle: sb.AppendLine($"        if (writer.SlotHandle({at}, {self})) written++;"); break;
                case Shape.Enum:   sb.AppendLine($"        if (writer.SlotInt({at}, unchecked((int){self}))) written++;"); break;
                case Shape.Color:
                    sb.AppendLine($"        if (writer.SlotInt({At(d, m.Slot)}, {self}.R) & writer.SlotInt({At(d, m.Slot + 1)}, {self}.G) & writer.SlotInt({At(d, m.Slot + 2)}, {self}.B) & writer.SlotInt({At(d, m.Slot + 3)}, {self}.A)) written++;");
                    break;
                case Shape.Run:
                    sb.AppendLine("        {");
                    sb.AppendLine("            bool ok = true;");
                    sb.AppendLine($"            for (int i = 0; i < {m.RunLength}; i++)");
                    sb.AppendLine($"                ok &= writer.SlotVectorAt({at}, i, {self}[i]);");
                    sb.AppendLine("            if (ok) written++;");
                    sb.AppendLine("        }");
                    break;
                case Shape.Nested:
                    sb.AppendLine($"        written += {self}.WriteMembers(ref writer, {At(d, m.Slot + 1)});");
                    break;
            }
        }

        sb.AppendLine("        return written;");
        sb.AppendLine("    }");
    }

    //-------------------------------------------------------------------------
    // Diagnostics. Each is a declaration that would otherwise read or write wrong.
    //-------------------------------------------------------------------------

    private static readonly DiagnosticDescriptor NotPartial = new(
        "SSTE001", "A temp entity declaration must be partial",
        "'{0}' must be partial, as must every type containing a declaration, for its read and write to be generated",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Generic = new(
        "SSTE002", "A temp entity declaration cannot be generic",
        "'{0}' is generic; a declaration is one table on the wire, so it has one set of members",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BadBase = new(
        "SSTE003", "A temp entity class must derive from TempEntState or another declaration",
        "'{0}' derives from neither TempEntState nor a [TempEntity] class; the registry walks a chain of declarations, and a class outside one would have members nobody reads",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BadName = new(
        "SSTE004", "A temp entity declaration needs a name to pair on",
        "'{0}' does not begin with 'Te', so its wire names cannot be derived; name it TeXxx or say both names in [TempEntity]",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotAField = new(
        "SSTE005", "A [TeProp] member must be a public instance field",
        "{0}.{1} carries [TeProp] but is not a public, non-readonly instance field; it could not be read back and would stay at its default",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Unsupported = new(
        "SSTE006", "A [TeProp] member's type has no wire form",
        "{0}.{1} is a {2}; use Vector, QAngle, Color32, float, bool, byte, short, int, EntityHandle, an enum of at most four bytes, a nested table (TePropKind.DataTable) or an [InlineArray] of Vector (TePropKind.Array)",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BadTable = new(
        "SSTE007", "A nested table must be a [TempEntity] struct and name its table",
        "{0}.{1} is TePropKind.DataTable; its type must be a [TempEntity] struct, and SubTable must name that struct's table",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BadRun = new(
        "SSTE008", "An array member must be an [InlineArray] of Vector",
        "{0}.{1}: an array member is TePropKind.Array on an [InlineArray] struct of Vector, with Elems equal to the array's length; the engine sends every element",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TooDeep = new(
        "SSTE009", "Temp entity tables nest too deep",
        "{0}.{1} nests tables more than {2} deep; the registry flattens no further",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoDeclaration = new(
        "SSTE010", "A [TeProp] member belongs on a [TempEntity] type",
        "{0}.{1} carries [TeProp], but {0} has no [TempEntity]; the registry does not find it and nothing reads or writes the member",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
