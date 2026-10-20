extern alias client;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 11 review (user, 2026-10-20): in the interop
/// layer a constructor saves its parameters into fields and does nothing else.
/// What it would have looked up, acquired or built is handed to it, and a
/// static factory (<c>For</c>, <c>ForEngine</c>, <c>Acquire</c>) does the
/// work once, where a reader of the class can see it.
///
/// <para>
/// Read from the IL of every instance constructor of a class in the Abi and
/// both sides' interop. Four things are allowed beside storing a parameter,
/// by the same ruling:
/// </para>
/// <list type="bullet">
/// <item>the object's own empty state: a parameterless <c>new</c>, an
/// array (sized by a count), an empty collection (with a comparer), a
/// native scratch block, a handle to itself,
/// a constant read from the framework (<c>StringComparer.Ordinal</c>,
/// <c>Array.Empty</c>, <c>EntityHandle.None</c>), and, in a constructor
/// that takes nothing to build them from, a lambda or an object of the
/// layer made from constants (<c>EngineBoundSet</c>'s slots);</item>
/// <item>chaining to another constructor with a default (a static
/// null object);</item>
/// <item>argument validation: a check of a parameter that throws (locals are
/// allowed, as a debug build keeps a condition in one);</item>
/// <item>value types, attributes and exceptions, which are not checked.</item>
/// </list>
/// </summary>
public class ConstructorCensusTests
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.DeclaredOnly;

    private static Assembly[] Assemblies =>
        [typeof(ShimRoot).Assembly, typeof(EngineSide).Assembly, typeof(client::SourceSharp.Abi.EngineSide).Assembly];

    // Framework members a constructor may read for its own state: constants
    // and empty values, never anything derived from a parameter.
    private static readonly string[] OwnStateStatics =
    [
        "System.StringComparer.get_Ordinal", "System.StringComparer.get_OrdinalIgnoreCase",
        "System.Array.Empty", "System.Diagnostics.Stopwatch.StartNew", "System.TimeSpan.FromMilliseconds", "System.TimeSpan.FromSeconds",
        "System.BitConverter.Int32BitsToSingle", "System.Type.GetTypeFromHandle", "System.Enum.GetNames",
        "System.Runtime.InteropServices.NativeMemory.AllocZeroed", "System.Runtime.InteropServices.NativeMemory.Alloc",
        "SourceSharp.Abi.EntityHandle.get_None",
    ];

    private static readonly Dictionary<short, OpCode> One = new();
    private static readonly Dictionary<short, OpCode> Two = new();

    static ConstructorCensusTests()
    {
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var op = (OpCode)f.GetValue(null)!;
            if (op.Size == 1)
                One[op.Value] = op;
            else
                Two[(short)(op.Value & 0xff)] = op;
        }
    }

    /// <summary>Why <paramref name="ctor"/> does more than save its parameters, or null.</summary>
    internal static string? Work(ConstructorInfo ctor)
    {
        Type type = ctor.DeclaringType!;
        byte[]? il = ctor.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            return null;

        bool takesNothing = ctor.GetParameters().Length == 0;
        for (int i = 0; i < il.Length;)
        {
            OpCode op = il[i] == 0xFE ? Two[il[i + 1]] : One[il[i]];
            i += op.Size;
            string n = op.Name!;
            int operand = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };

            if (n is "call" or "callvirt" or "newobj")
            {
                MethodBase? m = Resolve(ctor, type, BitConverter.ToInt32(il, i));
                string? refusal = m is null ? "an unresolved call"
                                : n == "newobj" && takesNothing && OwnInitialState(m.DeclaringType) ? null
                                : Call(n, m, type);
                if (refusal is not null)
                    return refusal;
            }
            else if (n == "ldftn" && !takesNothing)
                return "a lambda over its parameters";
            else if (!(n.StartsWith("ldarg", StringComparison.Ordinal) || n.StartsWith("ldc.", StringComparison.Ordinal)
                       || n.StartsWith("stloc", StringComparison.Ordinal) || n.StartsWith("ldloc", StringComparison.Ordinal)
                       || n.StartsWith("conv.", StringComparison.Ordinal) || n.StartsWith("b", StringComparison.Ordinal)
                       || n is "stfld" or "nop" or "ret" or "dup" or "pop" or "ldnull" or "ldstr" or "ldsfld" or "throw"
                            or "ceq" or "cgt" or "clt" or "cgt.un" or "clt.un" or "newarr" or "initobj" or "sizeof"
                            or "ldtoken" or "ldftn" or "ldobj" or "ldflda"
                       || n == "stsfld" && takesNothing))     // a static lambda's cache
                return $"the instruction {n}";

            i += operand;
        }
        return null;
    }

    // A constructor that takes nothing has nothing to build from but
    // constants, so what it makes is its own initial state: a lambda, or an
    // object of the layer itself -- whose constructor this census checks too,
    // so it cannot hide work behind one.
    private static bool OwnInitialState(Type? made)
        => made is not null && (typeof(Delegate).IsAssignableFrom(made) || Assemblies.Contains(made.Assembly));

    private static MethodBase? Resolve(ConstructorInfo ctor, Type type, int token)
    {
        try
        {
            return ctor.Module.ResolveMethod(token, type.IsGenericType ? type.GetGenericArguments() : null, null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? Call(string op, MethodBase m, Type owner)
    {
        Type? declaring = m.DeclaringType;
        string name = $"{declaring?.FullName}.{m.Name}";

        if (op == "call" && m.IsConstructor && (declaring == owner || declaring == owner.BaseType || declaring == typeof(object)))
            return null;                                   // chaining, the base
        if (op == "newobj" && (m.GetParameters().Length == 0
                               || typeof(Exception).IsAssignableFrom(declaring)
                               || declaring?.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true))
            return null;                                   // own empty state, a refusal
        if (op == "call" && m.IsStatic && m.Name.StartsWith("ThrowIf", StringComparison.Ordinal))
            return null;                                   // validation
        if (op == "call" && OwnStateStatics.Contains(name))
            return null;
        if (op is "call" or "callvirt" && m.Name is "get_Count" or "get_Length")
            return null;                                   // the size of an empty array
        if (op == "newobj" && declaring is { IsGenericType: true } handle
            && handle.GetGenericTypeDefinition() == typeof(System.Runtime.InteropServices.GCHandle<>))
            return null;                                   // a handle to itself
        return $"{op} {declaring?.Name}.{m.Name}";
    }

    /// <summary>
    /// Every class constructor of the interop layer saves its parameters and
    /// does nothing else (see the class's summary for what else is allowed).
    /// The positive arm: the walk reaches the bindings' constructors, several
    /// hundred of them.
    /// </summary>
    [Fact]
    public void InteropConstructorsOnlySaveTheirParameters()
    {
        // The service container registers itself under its own two contracts
        // when it is made, so a class taking IServiceLocator can always be
        // built: its own initial state, entries of its own dictionary.
        string[] ownStateTypes = ["SourceSharp.Abi.ServiceContainer"];

        var work = new List<string>();
        int checkedCount = 0;

        foreach (Assembly assembly in Assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.OfType<Type>().ToArray();
            }

            foreach (Type type in types)
            {
                if (!type.IsClass || type.IsDefined(typeof(CompilerGeneratedAttribute)) || ownStateTypes.Contains(type.FullName)
                    || typeof(Attribute).IsAssignableFrom(type) || typeof(Exception).IsAssignableFrom(type))
                    continue;

                // A record's copy constructor is the compiler's: it copies every
                // field of the instance it is handed, which is saving it.
                bool record = type.GetMethod("<Clone>$") is not null;
                foreach (ConstructorInfo ctor in type.GetConstructors(Declared))
                {
                    if (record && ctor.GetParameters() is [{ } only] && only.ParameterType == type)
                        continue;
                    checkedCount++;
                    if (Work(ctor) is { } why)
                        work.Add($"{assembly.GetName().Name}: {type.FullName}({string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.Name))}): {why}");
                }
            }
        }

        Assert.True(checkedCount > 500, $"{checkedCount} constructors: the walk reaches the interop layer");
        Assert.True(work.Count == 0, $"{work.Count} constructors do work:\n" + string.Join("\n", work.Distinct().Order()));
    }
}
