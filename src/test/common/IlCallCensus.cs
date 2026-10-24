using System.Reflection;
using System.Reflection.Emit;

namespace SourceSharp.Tests;

/// <summary>
/// Who calls what in a product assembly, read from its IL: the outermost
/// type of every method whose body calls a member the predicate picks. A
/// lambda's closure counts against the type it was written in, so a call
/// moved into a lambda is still seen.
/// </summary>
internal static class IlCallCensus
{
    // Every one-byte and two-byte opcode, by value, for the operand sizes.
    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

    /// <summary>
    /// The outermost types of <paramref name="assemblies"/> whose IL calls a
    /// method <paramref name="isCallee"/> picks, sorted by full name.
    /// </summary>
    public static string[] Callers(Func<MethodBase, bool> isCallee, params Assembly[] assemblies)
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
                    if (Calls(method, isCallee))
                        all.Add(Outermost(type).FullName!);
                }
            }
        }

        return all.ToArray();
    }

    private static bool Calls(MethodBase method, Func<MethodBase, bool> isCallee)
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
                if (Resolve(method, token) is { } callee && isCallee(callee))
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

    // The token in the caller's generic context, so a call inside a generic
    // type or method resolves too.
    private static MethodBase? Resolve(MethodBase caller, int token)
    {
        try
        {
            Type[]? typeArgs = caller.DeclaringType is { IsGenericType: true } t ? t.GetGenericArguments() : null;
            Type[]? methodArgs = caller.IsGenericMethod ? caller.GetGenericArguments() : null;
            return caller.Module.ResolveMethod(token, typeArgs, methodArgs);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer)
            type = outer;
        return type;
    }
}
