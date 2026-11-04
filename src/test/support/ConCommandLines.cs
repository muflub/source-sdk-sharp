using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// A typed command run from a console line, for a fact: through the
/// <see cref="ConCommandBinding"/> the console would use. A line the binding refuses
/// fails the fact unless it passed <paramref name="print"/> to see the refusal, so
/// a mistyped line cannot pass as a command that did nothing.
/// </summary>
public static class ConCommandLines
{
    public static void RunLine(this object command, string line, Action<string>? print = null)
    {
        string name = command.GetType().GetCustomAttribute<ConCommandAttribute>()?.Name ?? command.GetType().Name;
        Action<string> refused = print ?? (text => throw new InvalidOperationException($"{name} refused '{line}': {text}"));
        ConCommandBinding binding = ConCommandBinding.For(name, command, refused, out string? failure)
                                    ?? throw new InvalidOperationException($"{name} cannot be bound: {failure}");
        binding.Execute(line);
    }

    /// <summary>
    /// The typed commands of an assembly this side compiled against, with how many there
    /// are, that the registry would skip with a warning at load: a parameter a line cannot
    /// say, a completion method that is not one. Nothing else notices a command gone that way.
    /// </summary>
    public static (int Typed, IReadOnlyList<string> Unbound) Census(Assembly assembly)
    {
        int typed = 0;
        var unbound = new List<string>();
        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || typeof(IConCommand).IsAssignableFrom(type)
                || type.GetCustomAttribute<ConCommandAttribute>() is not { } declared)
                continue;

            typed++;
            object made = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            if (ConCommandBinding.For(declared.Name, made, _ => { }, out string? failure) is null)
                unbound.Add($"{declared.Name} ({type.FullName}): {failure}");
        }
        return (typed, unbound);
    }

    /// <summary>What the line's suggestions are, past the command's name.</summary>
    public static IReadOnlyList<string> SuggestFor(this object command, string line)
    {
        string name = command.GetType().GetCustomAttribute<ConCommandAttribute>()?.Name ?? command.GetType().Name;
        ConCommandBinding binding = ConCommandBinding.For(name, command, _ => { }, out string? failure)
                                    ?? throw new InvalidOperationException($"{name} cannot be bound: {failure}");
        return binding.Suggest($"{name} {line}");
    }
}
