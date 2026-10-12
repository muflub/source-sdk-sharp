using System.Reflection;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A value of a shared type, carried from one side's copy of the type to the
/// other's (plan_side_isolation P7: the shared source compiles into each side,
/// so <c>SourceSharp.Shared.FogState</c> is two types in a project that sees
/// both). In the game the value crosses as bytes; a cross-side test that built
/// it once hands the other side its own copy, member by member, the way the
/// wire would.
/// </summary>
internal static class SideCopy
{
    /// <summary><paramref name="from"/>, rebuilt as <typeparamref name="T"/>.</summary>
    public static T To<T>(object from) => (T)Convert(from, typeof(T))!;

    private static object? Convert(object? value, Type to)
    {
        if (value is null || to.IsInstanceOfType(value))
            return value;

        Type from = value.GetType();

        if (to.IsEnum)
            return Enum.ToObject(to, System.Convert.ToInt64(value));

        // Only the same type from the other side: anything else is a test that
        // asked for a conversion the wire does not make.
        Assert.True(from.FullName == to.FullName,
                    $"{from.FullName} is not the other side's {to.FullName}");

        var readable = from.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                           .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                           .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        // A record's primary constructor: every parameter is a property.
        ConstructorInfo? ctor = to.GetConstructors()
                                  .Where(c => c.GetParameters().Length > 0
                                           && c.GetParameters().All(p => readable.ContainsKey(p.Name!)))
                                  .OrderByDescending(c => c.GetParameters().Length)
                                  .FirstOrDefault();
        if (ctor is not null)
        {
            object?[] args = ctor.GetParameters()
                                 .Select(p => Convert(readable[p.Name!].GetValue(value), p.ParameterType))
                                 .ToArray();
            return ctor.Invoke(args);
        }

        object made = Activator.CreateInstance(to)!;
        int copied = 0;
        foreach (PropertyInfo target in to.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                          .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            if (readable.TryGetValue(target.Name, out PropertyInfo? source))
            {
                target.SetValue(made, Convert(source.GetValue(value), target.PropertyType));
                copied++;
            }
        }

        Assert.True(copied > 0, $"nothing of {from.FullName} was carried across");
        return made;
    }
}
