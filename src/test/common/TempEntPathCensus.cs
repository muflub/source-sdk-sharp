using System.Reflection;

namespace SourceSharp.Tests;

/// <summary>
/// What a temp entity's per-event path reaches, read from IL: from given roots out through
/// every call into our own assemblies, the reflection anything on the way calls. A call through
/// an interface or an abstract member stops there, so a root must be given per implementation.
/// </summary>
internal static class TempEntPathCensus
{
    /// <summary>A call that reads the metadata, or makes an object from it.</summary>
    private static bool IsReflective(MethodBase callee)
    {
        Type? owner = callee.DeclaringType;

        if (owner is null)
            return false;

        if (owner.Namespace == "System.Reflection")
            return true;

        if (owner == typeof(Attribute))
            return callee.Name is "GetCustomAttribute" or "GetCustomAttributes" or "IsDefined";

        if (owner == typeof(Activator))
            return true;

        if (owner == typeof(Enum))
            return callee.Name is "ToObject" or "GetValues" or "GetNames" or "GetName";

        if (owner == typeof(Type) || owner.FullName == "System.RuntimeType")
            return callee.Name.StartsWith("GetField", StringComparison.Ordinal)
                || callee.Name.StartsWith("GetMember", StringComparison.Ordinal)
                || callee.Name.StartsWith("GetMethod", StringComparison.Ordinal)
                || callee.Name.StartsWith("GetPropert", StringComparison.Ordinal)
                || callee.Name is "GetCustomAttributes" or "IsDefined" or "InvokeMember";

        return false;
    }

    private static bool IsOurs(MethodBase method)
    {
        string? name = method.DeclaringType?.Assembly.GetName().Name;

        return name is not null && (name.StartsWith("SourceSharp", StringComparison.Ordinal)
                                    || name.StartsWith("TF2", StringComparison.Ordinal));
    }

    private static string Name(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    /// <summary>Everything the roots reach, and every call to reflection among it as "caller -> callee".</summary>
    public static (HashSet<string> Walked, SortedSet<string> Reflective) From(IEnumerable<MethodBase> roots)
    {
        var walked = new HashSet<string>(StringComparer.Ordinal);
        var reflective = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<MethodBase>();
        var pending = new Queue<MethodBase>(roots);

        while (pending.Count > 0)
        {
            MethodBase method = pending.Dequeue();

            if (!seen.Add(method))
                continue;

            walked.Add(Name(method));

            foreach (MethodBase callee in IlCallCensus.CalleesOf(method))
            {
                if (IsReflective(callee))
                    reflective.Add($"{Name(method)} -> {Name(callee)}");
                else if (IsOurs(callee))
                    pending.Enqueue(callee);
            }
        }

        return (walked, reflective);
    }

    /// <summary>Every method of a type named one of <paramref name="names"/>, whatever its access.</summary>
    public static IEnumerable<MethodBase> Methods(Type type, params string[] names)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                         | BindingFlags.Static | BindingFlags.DeclaredOnly)
               .Where(m => names.Length == 0 || names.Contains(m.Name));
}
