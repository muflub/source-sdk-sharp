using System.Globalization;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>A fact's way to the convars a holder such as RenderConVars names.</summary>
internal static class RenderConVarsKit
{
    /// <summary>A holder whose every convar reads zero, a text one nothing.</summary>
    public static T Zeroed<T>() where T : class
    {
        T holder = new ConVarRegistry(new NoEngineConVars()).Holder<T>();
        foreach (ConVarBase convar in ConVarsOf(holder))
            convar.Hold(convar is StringConVar ? string.Empty : "0");
        return holder;
    }

    /// <summary>A holder answering what a frame's recording says, by name.</summary>
    public static T Recorded<T>(IReadOnlyDictionary<string, string> values) where T : class
        => new ConVarRegistry(new NamedConVars(values)).Holder<T>();

    /// <summary>Moves the convar of this console name, as a change from the engine does.</summary>
    public static void Set(this object holder, string name, float value)
        => ConVarsOf(holder).First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                            .Hold(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Moves the text convar of this console name.</summary>
    public static void SetText(this object holder, string name, string value)
        => ConVarsOf(holder).First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).Hold(value);

    private static IEnumerable<ConVarBase> ConVarsOf(object holder)
        => holder.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                 .Where(f => typeof(ConVarBase).IsAssignableFrom(f.FieldType))
                 .Select(f => (ConVarBase)f.GetValue(holder)!);
}
