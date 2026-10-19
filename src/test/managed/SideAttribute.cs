using System.Reflection;

namespace SourceSharp.Tests;

/// <summary>
/// An attribute read off either side's assembly as this suite's type.
///
/// <para>
/// The attributes the game declares with (<c>ConVarAttribute</c> and the like)
/// are shared interop, and each side compiles its own copy of it
/// (plan_shim_areas_split phase 3). In this cross-side suite the unaliased
/// type is the server's, so <c>GetCustomAttribute&lt;T&gt;</c> on a client type
/// finds nothing: the client's declaration is of the client's
/// <c>ConVarAttribute</c>. This finds the attribute by full name instead and
/// rebuilds it as <typeparamref name="T"/> from the arguments it was declared
/// with, so a fact compares both sides' declarations as the same data.
/// </para>
/// </summary>
internal static class SideAttribute
{
    /// <summary>The attribute named like <typeparamref name="T"/> on <paramref name="member"/>, as a <typeparamref name="T"/>; null when there is none.</summary>
    public static T? Get<T>(MemberInfo member) where T : Attribute
    {
        var data = member.GetCustomAttributesData()
                         .FirstOrDefault(d => d.AttributeType.FullName == typeof(T).FullName);
        if (data is null)
            return null;

        // The constructor with the same parameter types, by full name.
        string[] wanted = data.Constructor.GetParameters().Select(p => p.ParameterType.FullName!).ToArray();
        var ctor = typeof(T).GetConstructors()
                            .Single(c => c.GetParameters().Select(p => p.ParameterType.FullName!).SequenceEqual(wanted));
        var parameters = ctor.GetParameters();
        object?[] args = data.ConstructorArguments
                             .Select((a, k) => Convert(a, parameters[k].ParameterType))
                             .ToArray();
        var attribute = (T)ctor.Invoke(args);

        foreach (var named in data.NamedArguments)
        {
            if (named.IsField)
            {
                var field = typeof(T).GetField(named.MemberName)!;
                field.SetValue(attribute, Convert(named.TypedValue, field.FieldType));
            }
            else
            {
                var property = typeof(T).GetProperty(named.MemberName)!;
                property.SetValue(attribute, Convert(named.TypedValue, property.PropertyType));
            }
        }

        return attribute;
    }

    // A recorded argument as the target's type: an enum by its underlying
    // value (each side's copy of a shared enum has the same values), an array
    // element by element, anything else as it is.
    private static object? Convert(CustomAttributeTypedArgument argument, Type target)
    {
        if (argument.Value is null)
            return null;
        if (target.IsEnum)
            return Enum.ToObject(target, argument.Value);
        if (target.IsArray && argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> items)
        {
            Type element = target.GetElementType()!;
            var array = Array.CreateInstance(element, items.Count);
            int k = 0;
            foreach (var item in items)
                array.SetValue(Convert(item, element), k++);
            return array;
        }
        return argument.Value;
    }
}
