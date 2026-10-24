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
/// Writes a user message's wire code from its PAYLOAD declaration: a
/// <c>partial</c> struct or class whose <c>partial</c> properties are the
/// members, in declaration order, held apart from the handler the way a net
/// state is held apart from its entity.
///
/// <para>
/// WHAT THE AUTHOR WRITES is two types. The payload:
/// <code>
/// public partial struct Notice
/// {
///     [UserMessageString(MaxLength = 200)]
///     public partial string Text { get; set; }
/// }
/// </code>
/// and the handler, which names it:
/// <code>
/// [UserMessage("notice")]
/// public sealed class NoticeMessage(IHooks&lt;INoticeHook&gt; hooks) : IUserMessage&lt;Notice&gt;
/// {
///     public void OnReceived(Notice message) => ...;
/// }
/// </code>
/// WHAT THIS WRITES, on the payload: the properties' storage, an instance
/// <c>Write(Span&lt;byte&gt;)</c> that answers the length or -1, a static
/// <c>Read(ref UserMessageReader)</c>, and the handler the payload is sent
/// under (<c>IUserMessagePayload&lt;T&gt;.Handler</c>), which is where the
/// sender finds the key. The untyped dispatch into the typed
/// <c>OnReceived</c> is <c>IUserMessage&lt;T&gt;</c>'s own default method.
/// </para>
///
/// <para>
/// ONE WIRE CONVENTION for every message: declaration order, no tags, no names
/// and no header; fixed-width little-endian (bool and byte 1, short and ushort
/// 2, int, uint and float 4, Color32 4 as r g b a, Vector 12); a string
/// NUL-terminated, which is what Valve's READ_STRING takes, so a stock message
/// a native HUD element also reads keeps its layout; an array a count byte
/// then its elements.
/// </para>
///
/// <para>
/// THE PAYLOAD IS FOUND THROUGH ITS HANDLER. A payload carries no attribute of
/// its own; a <c>[UserMessage]</c> class implementing
/// <c>IUserMessage&lt;T&gt;</c> is what makes T a payload. So a payload must
/// be declared in the handler's assembly, and one payload has one handler:
/// both are diagnosed here, because a payload is sent under its handler's key.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class UserMessagePayloadGenerator : IIncrementalGenerator
{
    private const string MessageAttribute = "SourceSharp.Abi.UserMessageAttribute";
    private const string StringAttribute  = "SourceSharp.Abi.UserMessageStringAttribute";
    private const string ArrayAttribute   = "SourceSharp.Abi.UserMessageArrayAttribute";
    private const string VectorType       = "SourceSharp.Abi.Vector";
    private const string ColorType        = "SourceSharp.Abi.Color32";

    /// <summary>MAX_USER_MSG_DATA, as UserMessageSender refuses it.</summary>
    internal const int MaxPayloadBytes = 255;

    /// <summary>One wire member.</summary>
    private sealed record Field(
        string Name,
        string TypeName,
        string Kind,
        string? ElementKind,
        string? ElementType,
        int MaxLength,
        int MaxCount,
        string Accessibility,
        string GetAccessor,
        string SetAccessor);

    /// <summary>One handler and the payload it names.</summary>
    private sealed record Described(
        string PayloadKey,
        string? Namespace,
        ImmutableArray<string> Containers,
        string TypeKeyword,
        string TypeName,
        bool IsStruct,
        string Handler,
        ImmutableArray<Field> Fields,
        ImmutableArray<Diagnostic> Diagnostics,
        Location? HandlerLocation,
        bool Emit);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = context.SyntaxProvider.ForAttributeWithMetadataName(
            MessageAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => Describe(ctx));

        context.RegisterSourceOutput(handlers.Collect(), static (spc, all) =>
        {
            foreach (var group in all.Where(d => d is not null).GroupBy(d => d!.PayloadKey))
            {
                var list = group.ToList();

                foreach (var d in list)
                    foreach (var diagnostic in d!.Diagnostics)
                        spc.ReportDiagnostic(diagnostic);

                // ONE PAYLOAD, ONE HANDLER: the payload is sent under its
                // handler's key, so a second handler would make the key a
                // guess. Every handler is named, and the payload is still
                // written once so the error is this and not a cascade.
                if (list.Count > 1)
                {
                    string names = string.Join(", ", list.Select(d => d!.Handler).OrderBy(n => n, StringComparer.Ordinal));
                    foreach (var d in list)
                        spc.ReportDiagnostic(Diagnostic.Create(TwoHandlers, d!.HandlerLocation, group.Key, names));
                }

                var first = list[0]!;
                if (!first.Emit)
                    continue;

                string hint = first.PayloadKey.Replace("global::", "");
                spc.AddSource($"{hint}.UserMessagePayload.g.cs", SourceText.From(EmitPayload(first), Encoding.UTF8));
            }
        });
    }

    //-------------------------------------------------------------------------

    private static Described? Describe(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol handler)
            return null;

        var named = handler.AllInterfaces
            .Where(i => i.OriginalDefinition.MetadataName == "IUserMessage`1"
                        && i.OriginalDefinition.ContainingNamespace.ToDisplayString() == "SourceSharp.Abi")
            .ToList();

        // No IUserMessage<T>: a declaration with no payload model, which is
        // the honest statement for a message only native code reads.
        if (named.Count == 0)
            return null;

        var location = ctx.TargetNode is ClassDeclarationSyntax cls ? cls.Identifier.GetLocation() : handler.Locations.FirstOrDefault();
        string handlerName = handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (named.Count > 1)
        {
            diagnostics.Add(Diagnostic.Create(TwoPayloads, location, handler.Name,
                string.Join(", ", named.Select(i => i.TypeArguments[0].Name))));
        }

        if (named[0].TypeArguments[0] is not INamedTypeSymbol payload || payload.TypeKind == TypeKind.Error)
            return null;

        string payloadKey = payload.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        Described Refused() => new(payloadKey, null, ImmutableArray<string>.Empty, "", payload.Name, false,
                                   handlerName, ImmutableArray<Field>.Empty, diagnostics.ToImmutable(), location, Emit: false);

        if (payload.DeclaringSyntaxReferences.IsEmpty
            || !SymbolEqualityComparer.Default.Equals(payload.ContainingAssembly, handler.ContainingAssembly))
        {
            diagnostics.Add(Diagnostic.Create(ForeignPayload, location, payload.Name, handler.Name));
            return Refused();
        }

        var payloadLocation = payload.Locations.FirstOrDefault();

        if (payload.TypeKind is not (TypeKind.Struct or TypeKind.Class) || payload.IsGenericType
            || payload.IsAbstract || payload.IsStatic || (payload.IsValueType && payload.IsReadOnly)
            || !IsPartial(payload))
        {
            diagnostics.Add(Diagnostic.Create(NotPartial, payloadLocation, payload.Name));
            return Refused();
        }

        var containers = ImmutableArray.CreateBuilder<string>();
        for (var outer = payload.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (!IsPartial(outer))
            {
                diagnostics.Add(Diagnostic.Create(NotPartial, payloadLocation, outer.Name));
                return Refused();
            }
            containers.Insert(0, Keyword(outer) + " " + outer.Name);
        }

        var fields = ImmutableArray.CreateBuilder<Field>();
        int fixedBytes = 0;
        bool variable = false;

        foreach (var member in payload.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared)
                continue;

            if (member is IFieldSymbol field)
            {
                // Not a wire member and not written by Read: a payload's state
                // is its partial properties, nothing else.
                diagnostics.Add(Diagnostic.Create(NotAWireMember, field.Locations.FirstOrDefault(), field.Name));
                continue;
            }

            if (member is not IPropertySymbol property || property.IsIndexer)
                continue;

            if (!property.IsPartialDefinition)
            {
                // A computed property is fine. A settable one that is not
                // partial would be state that never travels.
                if (property.SetMethod is not null)
                    diagnostics.Add(Diagnostic.Create(NotAWireMember, property.Locations.FirstOrDefault(), property.Name));
                continue;
            }

            if (property.GetMethod is null || property.SetMethod is null)
            {
                diagnostics.Add(Diagnostic.Create(NeedsGetAndSet, property.Locations.FirstOrDefault(), property.Name));
                continue;
            }

            int maxLength = NamedInt(property, StringAttribute, "MaxLength");
            int maxCount  = NamedInt(property, ArrayAttribute, "MaxCount");

            string? kind;
            string? elementKind = null;
            string? elementType = null;

            if (property.Type is IArrayTypeSymbol array)
            {
                kind = array.Rank == 1 ? "Array" : null;
                elementKind = kind is null ? null : KindOf(array.ElementType);
                elementType = array.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (kind is null || elementKind is null)
                {
                    diagnostics.Add(Diagnostic.Create(UnsupportedType, property.Locations.FirstOrDefault(),
                        property.Name, property.Type.ToDisplayString()));
                    continue;
                }

                if (maxCount < 1 || maxCount > 255)
                {
                    diagnostics.Add(Diagnostic.Create(ArrayNeedsCount, property.Locations.FirstOrDefault(), property.Name));
                    continue;
                }
            }
            else
            {
                kind = KindOf(property.Type);
                if (kind is null)
                {
                    diagnostics.Add(Diagnostic.Create(UnsupportedType, property.Locations.FirstOrDefault(),
                        property.Name, property.Type.ToDisplayString()));
                    continue;
                }
            }

            // The worst case of every fixed part: a string's terminator, an
            // array's count byte and its elements' fixed parts at the most it
            // may carry.
            if (kind == "Array")
            {
                variable = true;
                fixedBytes += 1 + maxCount * WidthOf(elementKind!);
            }
            else
            {
                if (kind == "String")
                    variable = true;
                fixedBytes += WidthOf(kind);
            }

            fields.Add(new Field(
                property.Name,
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
                kind, elementKind, elementType, maxLength, maxCount,
                AccessibilityOf(property.DeclaredAccessibility),
                AccessorPrefix(property, property.GetMethod) + "get",
                AccessorPrefix(property, property.SetMethod) + (property.SetMethod.IsInitOnly ? "init" : "set")));
        }

        if (fixedBytes > MaxPayloadBytes)
            diagnostics.Add(Diagnostic.Create(OverCap, payloadLocation, payload.Name, fixedBytes, MaxPayloadBytes));

        // [UserMessage(Size = N)] is what the engine checks a fixed-size
        // message against, and it DROPS one that does not match. A payload of
        // fixed members states its size; the two must agree.
        int declaredSize = -1;
        var attribute = ctx.Attributes.FirstOrDefault();
        if (attribute is not null)
        {
            foreach (var arg in attribute.NamedArguments)
                if (arg.Key == "Size" && arg.Value.Value is int size)
                    declaredSize = size;
        }

        if (declaredSize >= 0 && (variable || declaredSize != fixedBytes))
        {
            diagnostics.Add(Diagnostic.Create(SizeMismatch, location, handler.Name, declaredSize, payload.Name,
                variable ? "variable" : fixedBytes.ToString()));
        }

        return new Described(
            payloadKey,
            payload.ContainingNamespace.IsGlobalNamespace ? null : payload.ContainingNamespace.ToDisplayString(),
            containers.ToImmutable(),
            Keyword(payload),
            payload.Name,
            payload.IsValueType,
            handlerName,
            fields.ToImmutable(),
            diagnostics.ToImmutable(),
            location,
            Emit: true);
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static string Keyword(INamedTypeSymbol type) =>
        (type.IsRecord, type.IsValueType) switch
        {
            (true, true)   => "record struct",
            (true, false)  => "record",
            (false, true)  => "struct",
            (false, false) => type.TypeKind == TypeKind.Interface ? "interface" : "class",
        };

    private static int NamedInt(IPropertySymbol property, string attribute, string name)
    {
        foreach (var a in property.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != attribute)
                continue;
            foreach (var arg in a.NamedArguments)
                if (arg.Key == name && arg.Value.Value is int value)
                    return value;
        }
        return -1;
    }

    /// <summary>The wire kind of a scalar type, or null when there is none.</summary>
    private static string? KindOf(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return "Bool";
            case SpecialType.System_Byte:    return "Byte";
            case SpecialType.System_Int16:   return "Short";
            case SpecialType.System_UInt16:  return "UShort";
            case SpecialType.System_Int32:   return "Int";
            case SpecialType.System_UInt32:  return "UInt";
            case SpecialType.System_Single:  return "Float";
            case SpecialType.System_String:  return "String";
        }

        return type.ToDisplayString().TrimEnd('?') switch
        {
            VectorType => "Vector",
            ColorType  => "Color",
            _          => null,
        };
    }

    /// <summary>A kind's fixed bytes: a string's is its terminator.</summary>
    internal static int WidthOf(string kind) => kind switch
    {
        "Bool" or "Byte" or "String"    => 1,
        "Short" or "UShort"             => 2,
        "Int" or "UInt" or "Float"      => 4,
        "Color"                         => 4,
        "Vector"                        => 12,
        _ => throw new InvalidOperationException($"no width for kind {kind}"),
    };

    private static string AccessibilityOf(Accessibility a) => a switch
    {
        Accessibility.Public               => "public",
        Accessibility.Internal             => "internal",
        Accessibility.Private              => "private",
        Accessibility.Protected            => "protected",
        Accessibility.ProtectedOrInternal  => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _                                  => "",
    };

    private static string AccessorPrefix(IPropertySymbol property, IMethodSymbol accessor) =>
        accessor.DeclaredAccessibility == property.DeclaredAccessibility
            ? ""
            : AccessibilityOf(accessor.DeclaredAccessibility) + " ";

    //-------------------------------------------------------------------------

    private static string EmitPayload(Described d)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/> by SourceSharp.Generators.UserMessagePayloadGenerator.");
        sb.AppendLine("// Edit the payload's partial properties, not this. Their ORDER is the layout.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (d.Namespace is not null)
            sb.Append("namespace ").Append(d.Namespace).AppendLine(";").AppendLine();

        foreach (var container in d.Containers)
            sb.Append("partial ").Append(container).AppendLine().AppendLine("{");

        string self = d.TypeName;

        sb.Append("partial ").Append(d.TypeKeyword).Append(' ').Append(self)
          .Append(" : global::SourceSharp.Abi.IUserMessagePayload<").Append(self).AppendLine(">");
        sb.AppendLine("{");

        //---------------------------------------------------------------------
        // Storage and the partial properties' implementations.
        //---------------------------------------------------------------------
        foreach (var f in d.Fields)
        {
            sb.Append("    private ").Append(f.TypeName).Append(' ').Append(Backing(f));

            // A class's reference members start empty rather than null; a
            // struct cannot carry an initialiser without a constructor, and
            // Write reads its null as empty.
            if (!d.IsStruct && f.Kind == "String")
                sb.Append(" = string.Empty");
            else if (!d.IsStruct && f.Kind == "Array")
                sb.Append(" = global::System.Array.Empty<").Append(f.ElementType).Append(">()");

            sb.AppendLine(";");

            sb.Append("    ").Append(f.Accessibility).Append(" partial ").Append(f.TypeName).Append(' ').Append(f.Name)
              .Append(" { ").Append(f.GetAccessor).Append(" => ").Append(Backing(f)).Append("; ")
              .Append(f.SetAccessor).Append(" => ").Append(Backing(f)).AppendLine(" = value; }");
            sb.AppendLine();
        }

        sb.AppendLine("    /// <summary>The handler this payload is sent under: its [UserMessage] key is the key.</summary>");
        sb.Append("    static global::System.Type global::SourceSharp.Abi.IUserMessagePayload<").Append(self)
          .Append(">.Handler => typeof(").Append(d.Handler).AppendLine(");");
        sb.AppendLine();

        //---------------------------------------------------------------------
        // Write.
        //---------------------------------------------------------------------
        sb.AppendLine("    /// <summary>Writes the members in declaration order; the length, or -1 when they do not fit.</summary>");
        sb.AppendLine("    public int Write(global::System.Span<byte> into)");
        sb.AppendLine("    {");
        sb.AppendLine("        var writer = new global::SourceSharp.Abi.UserMessageWriter(into);");
        foreach (var f in d.Fields)
            EmitWrite(sb, f, "        ");
        sb.AppendLine("        return writer.Overran ? -1 : writer.Length;");
        sb.AppendLine("    }");
        sb.AppendLine();

        //---------------------------------------------------------------------
        // Read.
        //---------------------------------------------------------------------
        sb.AppendLine("    /// <summary>Reads the members back. Check <c>reader.Overran</c>: a short or malformed payload is not a message.</summary>");
        sb.Append("    public static ").Append(self).AppendLine(" Read(ref global::SourceSharp.Abi.UserMessageReader reader)");
        sb.AppendLine("    {");
        sb.Append("        var message = new ").Append(self).AppendLine("();");
        foreach (var f in d.Fields)
            EmitRead(sb, f, "        ");
        sb.AppendLine("        return message;");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        foreach (var _ in d.Containers)
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Backing(Field f) => "__um" + f.Name;

    private static void EmitWrite(StringBuilder sb, Field f, string indent)
    {
        string v = Backing(f);

        if (f.Kind == "Array")
        {
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).Append("    var items = ").Append(v).Append(" ?? global::System.Array.Empty<").Append(f.ElementType).AppendLine(">();");
            sb.Append(indent).Append("    int count = global::System.Math.Min(items.Length, ").Append(f.MaxCount).AppendLine(");");
            sb.Append(indent).AppendLine("    writer.WriteByte((byte)count);");
            sb.Append(indent).AppendLine("    for (int i = 0; i < count; i++)");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).Append("        ").AppendLine(WriteScalar(f.ElementKind!, "items[i]", f.MaxLength));
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");
            return;
        }

        sb.Append(indent).AppendLine(WriteScalar(f.Kind, v, f.MaxLength));
    }

    private static string WriteScalar(string kind, string v, int maxLength) => kind switch
    {
        "Bool"   => $"writer.WriteByte({v} ? (byte)1 : (byte)0);",
        "Byte"   => $"writer.WriteByte({v});",
        "Short"  => $"writer.WriteShort({v});",
        "UShort" => $"writer.WriteShort(unchecked((short){v}));",
        "Int"    => $"writer.WriteInt({v});",
        "UInt"   => $"writer.WriteInt(unchecked((int){v}));",
        "Float"  => $"writer.WriteFloat({v});",
        "Vector" => $"writer.WriteFloat({v}.X); writer.WriteFloat({v}.Y); writer.WriteFloat({v}.Z);",
        "Color"  => $"writer.WriteColor(unchecked((byte){v}.R), unchecked((byte){v}.G), unchecked((byte){v}.B), unchecked((byte){v}.A));",

        // [UserMessageString(MaxLength = N)] cuts at N UTF-16 code units on
        // write, and only on write: a reader takes what arrived.
        "String" => maxLength >= 0
            ? $"{{ string text = {v} ?? string.Empty; writer.WriteString(text.Length > {maxLength} ? text.Substring(0, {maxLength}) : text); }}"
            : $"writer.WriteString({v} ?? string.Empty);",

        _ => throw new InvalidOperationException($"no writer for kind {kind}"),
    };

    private static void EmitRead(StringBuilder sb, Field f, string indent)
    {
        string v = "message." + Backing(f);

        if (f.Kind == "Array")
        {
            // A count over the declared most is a payload this build did not
            // write: refused, not read part-way.
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).AppendLine("    int count = reader.ReadByte();");
            sb.Append(indent).Append("    if (count > ").Append(f.MaxCount).AppendLine(")");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        reader.Refuse();");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("    else");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).Append("        var items = new ").Append(f.ElementType).AppendLine("[count];");
            sb.Append(indent).AppendLine("        for (int i = 0; i < count; i++)");
            sb.Append(indent).Append("            items[i] = ").Append(ReadScalar(f.ElementKind!)).AppendLine(";");
            sb.Append(indent).Append("        ").Append(v).AppendLine(" = items;");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");
            return;
        }

        sb.Append(indent).Append(v).Append(" = ").Append(ReadScalar(f.Kind)).AppendLine(";");
    }

    private static string ReadScalar(string kind) => kind switch
    {
        "Bool"   => "reader.ReadByte() != 0",
        "Byte"   => "reader.ReadByte()",
        "Short"  => "reader.ReadShort()",
        "UShort" => "unchecked((ushort)reader.ReadShort())",
        "Int"    => "reader.ReadInt()",
        "UInt"   => "unchecked((uint)reader.ReadInt())",
        "Float"  => "reader.ReadFloat()",
        "String" => "reader.ReadString()",
        "Vector" => "new global::SourceSharp.Abi.Vector(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat())",
        "Color"  => "new global::SourceSharp.Abi.Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte())",
        _ => throw new InvalidOperationException($"no reader for kind {kind}"),
    };

    //-------------------------------------------------------------------------
    // Diagnostics. Each is a payload that would otherwise travel wrong or not
    // at all.
    //-------------------------------------------------------------------------

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        "SSMSG001", "Unsupported user message member type",
        "'{0}' is a {1}, which a user message payload cannot carry; use bool, byte, short, ushort, int, uint, float, string, Vector, Color32, or a one-dimensional array of one of those",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NeedsGetAndSet = new(
        "SSMSG002", "A user message member needs a getter and a setter",
        "'{0}' has only one of a getter and a setter; Write reads a member and Read fills it, so a payload member needs both",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TwoHandlers = new(
        "SSMSG003", "A user message payload has more than one handler",
        "payload {0} is named by more than one handler ({1}); a payload is sent under its handler's key, so it has exactly one",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TwoPayloads = new(
        "SSMSG004", "A user message handler names more than one payload",
        "{0} implements IUserMessage<T> for {1}; one [UserMessage] key carries one payload",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ForeignPayload = new(
        "SSMSG005", "A user message payload must be declared beside its handler",
        "payload {0} of {1} is not declared in the handler's assembly; declare it there so its wire code is generated once, beside the key it is sent under",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotPartial = new(
        "SSMSG006", "A user message payload must be a partial, non-generic struct or class",
        "'{0}' must be a partial, non-generic, non-readonly struct or class (and every type containing it partial) for its wire code to be generated",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ArrayNeedsCount = new(
        "SSMSG007", "A user message array member must state its most",
        "'{0}' is an array; give it [UserMessageArray(MaxCount = N)] with N from 1 to 255, the count byte's range",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor OverCap = new(
        "SSMSG008", "A user message payload cannot fit the engine's limit",
        "payload {0}'s fixed members need {1} bytes at their most, over the engine's {2}-byte user message limit",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SizeMismatch = new(
        "SSMSG009", "A fixed user message size disagrees with its payload",
        "{0} declares Size = {1}, but payload {2} is {3} bytes; the engine drops a fixed-size message whose length differs",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotAWireMember = new(
        "SSMSG010", "A user message payload holds state that does not travel",
        "'{0}' is settable state on a payload but not a partial property, so it would never be written or read; make it a partial property, or computed",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
