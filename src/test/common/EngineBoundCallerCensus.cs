using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SourceSharp.Tests;

/// <summary>
/// Who calls the side's bound slots (<c>EngineBound</c>) in a product
/// assembly, read from its IL: only the acquisition layer may. Everything
/// else is handed the engine interface it calls by constructor, as the
/// composition registered it (EngineServices.AddForwarders), so it never
/// looks one up through the side.
/// </summary>
internal static class EngineBoundCallerCensus
{
    /// <summary>
    /// The acquisition layer: the slots themselves, the composition that
    /// acquires and registers each interface, and the side, whose own
    /// services (its model names and break models) read its own slot.
    /// </summary>
    public static readonly string[] Allowed =
    [
        "SourceSharp.Abi.EngineBound",
        "SourceSharp.Abi.EngineBoundSet",
        "SourceSharp.Abi.EngineServices",
        "SourceSharp.Abi.EngineSide",
    ];

    private const string Bound = "SourceSharp.Abi.EngineBound";

    // Every one-byte and two-byte opcode, by value, for the operand sizes.
    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

    /// <summary>
    /// The types of <paramref name="assemblies"/> whose IL calls a member of
    /// <c>EngineBound</c>, each named by its outermost type (a lambda's closure
    /// counts against the type it was written in): those outside
    /// <see cref="Allowed"/>, and all of them, so a scan that saw none is visible.
    /// </summary>
    public static (string[] Outside, string[] All) Callers(params Assembly[] assemblies)
    {
        var all = new SortedSet<string>(StringComparer.Ordinal);
        const BindingFlags every = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodBase method in type.GetMethods(every).Cast<MethodBase>().Concat(type.GetConstructors(every)))
                {
                    if (CallsBound(method))
                        all.Add(Outermost(type).FullName!);
                }
            }
        }

        return (all.Except(Allowed).ToArray(), all.ToArray());
    }

    private static bool CallsBound(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            return false;

        for (int i = 0; i < il.Length;)
        {
            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            if (!OpCodesByValue.TryGetValue(value, out OpCode op))
                return false;   // not IL we understand: say nothing rather than guess
            i += op.Size;

            if (op.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, i);
                if (Resolve(method.Module, token)?.DeclaringType?.FullName == Bound)
                    return true;
            }

            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }

        return false;
    }

    private static MethodBase? Resolve(Module module, int token)
    {
        try
        {
            return module.ResolveMethod(token);
        }
        catch (ArgumentException)
        {
            return null;   // a generic member needing its context: not EngineBound's, which has none
        }
    }

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer)
            type = outer;
        return type;
    }
}
