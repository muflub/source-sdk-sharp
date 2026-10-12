using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>
/// THE FORM GENERATOR (plan_client_ui §6): a <c>[UiFormModel]</c> partial class's
/// <c>[Field]</c> fields become its IUiFormModel implementation (typed
/// accessors by index, copies, comparisons) and a <c>Fields</c> class of index
/// constants, read once at build time, so a form never reflects.
/// Kinds by C# type: bool a toggle, an enum a choice, int or float a slider
/// (Min and Max required), string a text field, KeyBinding a key capture.
/// SSUI010 reports a field it cannot draw.
/// </summary>
[Generator]
public sealed class UiFormGenerator : IIncrementalGenerator
{
    private const string ModelAttribute = "SourceSharp.Client.Ui.Toolkit.UiFormModelAttribute";
    private const string FieldAttribute = "SourceSharp.Client.Ui.Toolkit.FieldAttribute";
    private const string KeyBindingType = "SourceSharp.Client.Ui.Toolkit.KeyBinding";

    public static readonly DiagnosticDescriptor Unsupported = new(
        "SSUI010", "A form field the generator cannot draw",
        "Form field '{0}' is a {1}: a form field is a bool, an enum, a string, a KeyBinding, or an int or float with Min and Max",
        "SourceSharp.Ui", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed record FieldModel(string Name, string Type, string Kind, string Label, float Min, float Max,
                                     float Step, string? Section, ImmutableArray<string> Choices, bool Whole);

    private sealed record ClassModel(string? Namespace, ImmutableArray<string> Containers, string Name,
                                     ImmutableArray<FieldModel> Fields, ImmutableArray<Diagnostic> Problems);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
            ModelAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => Describe((INamedTypeSymbol)ctx.TargetSymbol));

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            foreach (var d in model.Problems)
                spc.ReportDiagnostic(d);
            if (model.Problems.IsEmpty)
                spc.AddSource(string.Join(".", model.Containers.Add(model.Name)) + ".UiForm.g.cs", Emit(model));
        });
    }

    private static ClassModel Describe(INamedTypeSymbol type)
    {
        var fields = ImmutableArray.CreateBuilder<FieldModel>();
        var problems = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
        {
            var attr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FieldAttribute);
            if (attr is null)
                continue;

            string label = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string ?? "" : "";
            float min = float.NaN, max = float.NaN, step = float.NaN;
            string? section = null;
            foreach (var named in attr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Min": min = System.Convert.ToSingle(named.Value.Value, CultureInfo.InvariantCulture); break;
                    case "Max": max = System.Convert.ToSingle(named.Value.Value, CultureInfo.InvariantCulture); break;
                    case "Step": step = System.Convert.ToSingle(named.Value.Value, CultureInfo.InvariantCulture); break;
                    case "Section": section = named.Value.Value as string; break;
                }
            }

            var t = member.Type;
            string typeName = t.ToDisplayString();
            string? kind = t.SpecialType switch
            {
                SpecialType.System_Boolean => "Toggle",
                SpecialType.System_String => "Text",
                SpecialType.System_Int32 or SpecialType.System_Single when !float.IsNaN(min) && !float.IsNaN(max) => "Slider",
                _ => t.TypeKind == TypeKind.Enum ? "Choice" : typeName == KeyBindingType ? "Capture" : null,
            };
            if (kind is null)
            {
                problems.Add(Diagnostic.Create(Unsupported, member.Locations.FirstOrDefault(), member.Name, typeName));
                continue;
            }

            bool whole = t.SpecialType == SpecialType.System_Int32;
            if (whole && float.IsNaN(step))
                step = 1f;
            var choices = kind == "Choice"
                ? ((INamedTypeSymbol)t).GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToImmutableArray()
                : ImmutableArray<string>.Empty;
            fields.Add(new FieldModel(member.Name, t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), kind,
                                      label, min, max, step, section, choices, whole));
        }

        string? ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var containers = new List<string>();
        for (var c = type.ContainingType; c is not null; c = c.ContainingType)
            containers.Insert(0, c.Name);
        return new ClassModel(ns, containers.ToImmutableArray(), type.Name, fields.ToImmutable(), problems.ToImmutable());
    }

    /// <summary>An enum member's name as words: CustomAndPower is "Custom and power".</summary>
    internal static string Words(string name)
    {
        var w = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                w.Append(' ').Append(char.ToLowerInvariant(c));
            else
                w.Append(c);
        }
        return w.ToString();
    }

    private static string F(float v)
        => float.IsNaN(v) ? "float.NaN" : v.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static string Q(string? s) => s is null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Emit(ClassModel m)
    {
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/> by UiFormGenerator (plan_client_ui §6) from this class's [Field]s.");
        s.AppendLine("#nullable enable");
        if (m.Namespace is not null)
            s.Append("namespace ").Append(m.Namespace).AppendLine(";");
        // A model nested in its form: the form (and anything around it) is partial too.
        foreach (string c in m.Containers)
            s.Append("partial class ").Append(c).AppendLine(" {");
        s.Append("partial class ").Append(m.Name).Append(" : global::SourceSharp.Client.Ui.Toolkit.IUiFormModel<")
         .Append(m.Name).AppendLine(">");
        s.AppendLine("{");

        s.AppendLine("    /// <summary>Each field's index, for bindings and validation.</summary>");
        s.AppendLine("    public static class Fields");
        s.AppendLine("    {");
        for (int i = 0; i < m.Fields.Length; i++)
            s.Append("        public const int ").Append(m.Fields[i].Name).Append(" = ").Append(i).AppendLine(";");
        s.AppendLine("    }");

        s.Append("    public int FieldCount => ").Append(m.Fields.Length).AppendLine(";");

        s.AppendLine("    public global::SourceSharp.Client.Ui.Toolkit.UiFieldInfo Field(int i) => i switch");
        s.AppendLine("    {");
        for (int i = 0; i < m.Fields.Length; i++)
        {
            var f = m.Fields[i];
            s.Append("        ").Append(i).Append(" => new(").Append(Q(f.Name)).Append(", ").Append(Q(f.Label))
             .Append(", global::SourceSharp.Client.Ui.Toolkit.UiFieldKind.").Append(f.Kind).Append(", ")
             .Append(F(f.Min)).Append(", ").Append(F(f.Max)).Append(", ").Append(F(f.Step)).Append(", ")
             .Append(Q(f.Section)).Append(", ").Append(f.Choices.Length).Append(", ").Append(f.Whole ? "true" : "false")
             .AppendLine("),");
        }
        s.AppendLine("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(i)),");
        s.AppendLine("    };");

        Accessor(s, m, "bool", "Bool", "Toggle", f => f.Name, f => f.Name + " = value;", "false");
        Accessor(s, m, "float", "Number", "Slider", f => f.Whole ? "(float)" + f.Name : f.Name,
                 f => f.Name + " = " + (f.Whole ? "(int)global::System.MathF.Round(value)" : "value") + ";", "0f");
        Accessor(s, m, "int", "Choice", "Choice", f => "(int)" + f.Name, f => f.Name + " = (" + f.Type + ")value;", "0");
        Accessor(s, m, "string", "Text", "Text", f => f.Name + " ?? \"\"", f => f.Name + " = value;", "\"\"");
        Accessor(s, m, "global::" + KeyBindingType, "Key", "Capture", f => f.Name, f => f.Name + " = value;", "default");

        s.AppendLine("    public string ChoiceName(int i, int choice) => (i, choice) switch");
        s.AppendLine("    {");
        for (int i = 0; i < m.Fields.Length; i++)
            for (int c = 0; c < m.Fields[i].Choices.Length; c++)
                s.Append("        (").Append(i).Append(", ").Append(c).Append(") => ").Append(Q(Words(m.Fields[i].Choices[c]))).AppendLine(",");
        s.AppendLine("        _ => \"\",");
        s.AppendLine("    };");

        s.Append("    public void CopyFrom(").Append(m.Name).AppendLine(" other)");
        s.AppendLine("    {");
        foreach (var f in m.Fields)
            s.Append("        ").Append(f.Name).Append(" = other.").Append(f.Name).AppendLine(";");
        s.AppendLine("    }");

        s.Append("    public bool Same(int i, ").Append(m.Name).AppendLine(" other) => i switch");
        s.AppendLine("    {");
        for (int i = 0; i < m.Fields.Length; i++)
            s.Append("        ").Append(i).Append(" => global::System.Collections.Generic.EqualityComparer<")
             .Append(m.Fields[i].Type).Append(">.Default.Equals(").Append(m.Fields[i].Name).Append(", other.")
             .Append(m.Fields[i].Name).AppendLine("),");
        s.AppendLine("        _ => true,");
        s.AppendLine("    };");

        s.AppendLine("}");
        foreach (string _ in m.Containers)
            s.AppendLine("}");
        return s.ToString();
    }

    private static void Accessor(StringBuilder s, ClassModel m, string type, string suffix, string kind,
                                 System.Func<FieldModel, string> get, System.Func<FieldModel, string> set, string none)
    {
        s.Append("    public ").Append(type).Append(" Get").Append(suffix).AppendLine("(int i) => i switch");
        s.AppendLine("    {");
        for (int i = 0; i < m.Fields.Length; i++)
            if (m.Fields[i].Kind == kind)
                s.Append("        ").Append(i).Append(" => ").Append(get(m.Fields[i])).AppendLine(",");
        s.Append("        _ => ").Append(none).AppendLine(",");
        s.AppendLine("    };");
        s.Append("    public void Set").Append(suffix).Append("(int i, ").Append(type).AppendLine(" value)");
        s.AppendLine("    {");
        s.AppendLine("        switch (i)");
        s.AppendLine("        {");
        for (int i = 0; i < m.Fields.Length; i++)
            if (m.Fields[i].Kind == kind)
                s.Append("            case ").Append(i).Append(": ").Append(set(m.Fields[i])).AppendLine(" break;");
        s.AppendLine("            default: break;");
        s.AppendLine("        }");
        s.AppendLine("    }");
    }
}
