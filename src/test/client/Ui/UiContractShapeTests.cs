using System.Reflection;
using System.Runtime.CompilerServices;

using SourceSharp.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The UI contract in SourceSharp.Client.Interface is data only
/// (interface-assemblies-data-only): every type in its <c>SourceSharp.Client.Ui</c>
/// namespace is an interface without default bodies, an enum, or a struct or
/// record whose methods are all compiler-generated (accessors, equality,
/// ToString, Deconstruct). Lane "routes" adds the assembly-wide fact; this one
/// holds the UI half from the day it lands.
/// </summary>
public class UiContractShapeTests
{
    private static IEnumerable<Type> ContractTypes()
        => typeof(UiDrawOp).Assembly.GetTypes().Where(t => t.Namespace == "SourceSharp.Client.Ui");

    /// <summary>The method bodies a type carries that a compiler did not write.</summary>
    internal static IEnumerable<string> HandWrittenBodies(Type t)
    {
        if (t.IsEnum)
            yield break;

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (t.IsInterface)
        {
            foreach (var m in t.GetMethods(all).Where(m => !m.IsAbstract))
                yield return $"{t.Name}.{m.Name} (default interface body)";
            yield break;
        }

        foreach (var m in t.GetMethods(all))
        {
            if (m.IsSpecialName || m.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
                continue;

            // A record's Equals(T), GetHashCode, ToString, PrintMembers and
            // Deconstruct carry no attribute: they are the record's by its
            // EqualityContract (classes) or by name on a record struct.
            if (IsRecordMember(t, m))
                continue;

            yield return $"{t.Name}.{m.Name}";
        }

        foreach (var c in t.GetConstructors(all))
        {
            // A positional record's primary constructor only assigns its parameters.
            if (c.GetParameters().Length > 0 && IsRecord(t))
                continue;
            if (c.GetParameters().Length == 0)
                continue;
            yield return $"{t.Name}..ctor";
        }
    }

    private static bool IsRecord(Type t)
        => t.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static readonly string[] RecordMembers =
        ["Equals", "GetHashCode", "ToString", "PrintMembers", "Deconstruct", "<Clone>$"];

    private static bool IsRecordMember(Type t, MethodInfo m)
        => IsRecord(t) && RecordMembers.Contains(m.Name);

    [Fact]
    public void Every_ui_contract_type_is_data_or_an_interface()
    {
        var types = ContractTypes().ToArray();
        Assert.True(types.Length >= 30, $"only {types.Length} UI contract types found: the scan missed the namespace");

        var bodies = types.SelectMany(HandWrittenBodies).ToArray();
        Assert.True(bodies.Length == 0, "behaviour in the UI contract:\n" + string.Join("\n", bodies));
    }

    // The planted half: the rule sees a hand-written method on a record struct
    // and a default interface body, so a clean report above means something.
    private readonly record struct PlantedRecord(int A)
    {
        public int Doubled() => A * 2;
    }

    private interface IPlanted
    {
        int Answer() => 42;
    }

    [Fact]
    public void A_planted_method_body_is_reported()
    {
        Assert.Contains("PlantedRecord.Doubled", HandWrittenBodies(typeof(PlantedRecord)));
        Assert.Contains("IPlanted.Answer (default interface body)", HandWrittenBodies(typeof(IPlanted)));
        Assert.Empty(HandWrittenBodies(typeof(UiQuad)));
    }
}
