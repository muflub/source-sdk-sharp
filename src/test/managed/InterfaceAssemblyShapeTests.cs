using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_client_novalve §3.2, "Three assemblies": a <c>*.Interface</c> assembly
/// holds interfaces, enums and pure data, never behaviour (user, 2026-10-04).
/// Read from the built metadata, so a type with a method body is found however
/// it was written.
///
/// <para>
/// A type passes when it is an interface whose methods are all abstract, an
/// enum, or a record or struct whose members are fields, auto-properties,
/// compiler-generated members and constructors that only store their
/// arguments. A static class of constants, a plain class, a delegate, a
/// computed property, a helper method or a default interface method fails.
/// </para>
/// </summary>
public class InterfaceAssemblyShapeTests
{
    public static TheoryData<string> Assemblies => new()
    {
        "Debug.Api.Server.Interfaces",
        "Debug.Api.Client.Interfaces",
        "SourceSharp.Client.Interface",
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void It_holds_only_interfaces_enums_and_data(string assembly)
    {
        var shape = InterfaceShape.Read(DebugApiClosureTests.DllOf(assembly));

        // The stimulus reaches the subject: both assemblies hold interfaces and
        // data records today, so a reader that saw none read nothing.
        Assert.True(shape.Interfaces > 0, $"{assembly}: no interface was read");
        Assert.True(shape.DataTypes > 0, $"{assembly}: no record or struct was read");

        Assert.Empty(shape.Violations);
    }

    [Fact]
    public void A_type_with_behaviour_is_found()
    {
        // The negative arm, over this assembly's own types below: each kind of
        // behaviour the rule names is reported, and the data shapes are not.
        var shape = InterfaceShape.Read(typeof(InterfaceAssemblyShapeTests).Assembly.Location,
                                        t => t.StartsWith(typeof(ShapeSamples).FullName!, StringComparison.Ordinal));

        Assert.Contains(shape.Violations, v => v.Contains("ShapeSamples+WithHelper: method Twice", StringComparison.Ordinal));
        Assert.Contains(shape.Violations, v => v.Contains("ShapeSamples+WithComputed: method get_Half", StringComparison.Ordinal));
        Assert.Contains(shape.Violations, v => v.Contains("ShapeSamples+Constants: not an interface", StringComparison.Ordinal));
        Assert.Contains(shape.Violations, v => v.Contains("ShapeSamples+IWithDefault: interface method Default", StringComparison.Ordinal));
        Assert.Contains(shape.Violations, v => v.Contains("ShapeSamples+WithLogicCtor: constructor", StringComparison.Ordinal));

        Assert.DoesNotContain(shape.Violations, v => v.Contains("+Data:", StringComparison.Ordinal));
        Assert.DoesNotContain(shape.Violations, v => v.Contains("+DataStruct:", StringComparison.Ordinal));
        Assert.DoesNotContain(shape.Violations, v => v.Contains("+PlainStruct:", StringComparison.Ordinal));
        Assert.DoesNotContain(shape.Violations, v => v.Contains("+IPure:", StringComparison.Ordinal));
        Assert.DoesNotContain(shape.Violations, v => v.Contains("+Kind:", StringComparison.Ordinal));
        Assert.Equal(5, shape.Violations.Count(v => v.Contains("ShapeSamples+", StringComparison.Ordinal)));
    }
}

/// <summary>Samples for <see cref="InterfaceAssemblyShapeTests.A_type_with_behaviour_is_found"/>.</summary>
public static class ShapeSamples
{
    public sealed record Data(int A, string B, IReadOnlyList<int> C);

    public readonly record struct DataStruct(float X, float Y);

    public struct PlainStruct
    {
        public int Field;
        public int Auto { get; set; }
    }

    public interface IPure
    {
        const int Limit = 3;
        int Read();
    }

    public enum Kind { One, Two }

    public sealed record WithHelper(int A)
    {
        public int Twice() => A * 2;
    }

    public sealed record WithComputed(int A)
    {
        public int Half => A / 2;
    }

    public static class Constants
    {
        public const int Value = 1;
    }

    public interface IWithDefault
    {
        int Default() => 1;
    }

    public sealed record WithLogicCtor
    {
        public WithLogicCtor(int a) => A = a > 0 ? a : -a;

        public int A { get; }
    }
}

/// <summary>What <see cref="InterfaceAssemblyShapeTests"/> reads from one assembly's metadata.</summary>
internal sealed class InterfaceShape
{
    public int Interfaces { get; private set; }
    public int DataTypes { get; private set; }
    public List<string> Violations { get; } = [];

    public static InterfaceShape Read(string dll, Func<string, bool>? only = null)
    {
        using var stream = File.OpenRead(dll);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var shape = new InterfaceShape();

        foreach (var h in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(h);
            string name = FullName(md, type);
            if (name == "<Module>" || IsEmbeddedByCompiler(md, type))
                continue;
            if (only is not null && !only(name))
                continue;

            shape.Check(md, pe, type, name);
        }

        return shape;
    }

    private void Check(MetadataReader md, PEReader pe, TypeDefinition type, string name)
    {
        if ((type.Attributes & TypeAttributes.Interface) != 0)
        {
            Interfaces++;
            foreach (var mh in type.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if ((m.Attributes & MethodAttributes.Abstract) == 0)
                    Violations.Add($"{name}: interface method {md.GetString(m.Name)} has a body");
            }
            return;
        }

        string baseName = BaseName(md, type);
        if (baseName == "System.Enum")
            return;

        bool isStruct = baseName == "System.ValueType";
        bool isRecord = type.GetMethods().Any(mh => md.GetString(md.GetMethodDefinition(mh).Name) == "<Clone>$");
        if (!isStruct && !isRecord)
        {
            Violations.Add($"{name}: not an interface, an enum, a record or a struct ({baseName})");
            return;
        }

        DataTypes++;
        foreach (var mh in type.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            string method = md.GetString(m.Name);

            if (HasAttribute(md, m.GetCustomAttributes(), "CompilerGeneratedAttribute"))
                continue;

            if (method == ".ctor")
            {
                if (!StoresOnly(md, pe, m))
                    Violations.Add($"{name}: constructor does more than store its arguments");
                continue;
            }

            Violations.Add($"{name}: method {method} has a body");
        }
    }

    /// <summary>
    /// Whether a constructor's IL only loads arguments, stores fields and calls
    /// a base constructor: what a positional record's or a struct's is.
    /// </summary>
    private static bool StoresOnly(MetadataReader md, PEReader pe, MethodDefinition m)
    {
        if (m.RelativeVirtualAddress == 0)
            return true;

        var il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes();
        if (il is null)
            return false;

        for (int at = 0; at < il.Length;)
        {
            byte op = il[at];
            switch (op)
            {
                case 0x00: // nop
                case 0x02: case 0x03: case 0x04: case 0x05: // ldarg.0-3
                case 0x2A: // ret
                    at += 1;
                    break;
                case 0x0E: // ldarg.s
                    at += 2;
                    break;
                case 0x7D: // stfld
                    at += 5;
                    break;
                case 0x28: // call: only a constructor
                    var token = MetadataTokens.EntityHandle(BitConverter.ToInt32(il, at + 1));
                    if (!IsConstructor(md, token))
                        return false;
                    at += 5;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool IsConstructor(MetadataReader md, EntityHandle token) => token.Kind switch
    {
        HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)token).Name) == ".ctor",
        HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)token).Name) == ".ctor",
        _ => false,
    };

    private static string FullName(MetadataReader md, TypeDefinition type)
    {
        string name = md.GetString(type.Name);
        if (type.GetDeclaringType() is { IsNil: false } outer)
            return FullName(md, md.GetTypeDefinition(outer)) + "+" + name;

        string ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string BaseName(MetadataReader md, TypeDefinition type)
    {
        var b = type.BaseType;
        return b.Kind switch
        {
            HandleKind.TypeReference => Qualified(md, md.GetTypeReference((TypeReferenceHandle)b)),
            HandleKind.TypeDefinition => FullName(md, md.GetTypeDefinition((TypeDefinitionHandle)b)),
            HandleKind.TypeSpecification => "(generic base)",
            _ => "(none)",
        };
    }

    private static string Qualified(MetadataReader md, TypeReference r)
    {
        string ns = md.GetString(r.Namespace);
        string name = md.GetString(r.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>The attribute types the compiler emits into an assembly ([Embedded]), which are not ours.</summary>
    private static bool IsEmbeddedByCompiler(MetadataReader md, TypeDefinition type)
        => HasAttribute(md, type.GetCustomAttributes(), "EmbeddedAttribute");

    private static bool HasAttribute(MetadataReader md, CustomAttributeHandleCollection attributes, string name)
    {
        foreach (var ah in attributes)
        {
            var ctor = md.GetCustomAttribute(ah).Constructor;
            string? typeName = ctor.Kind switch
            {
                HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)ctor).Parent is { Kind: HandleKind.TypeReference } p
                    ? md.GetString(md.GetTypeReference((TypeReferenceHandle)p).Name)
                    : null,
                HandleKind.MethodDefinition => md.GetString(md.GetTypeDefinition(
                    md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType()).Name),
                _ => null,
            };
            if (typeName == name)
                return true;
        }

        return false;
    }
}
