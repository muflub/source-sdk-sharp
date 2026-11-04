using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;

/// <summary>
/// A test's way to a declared convar. Production classes are handed theirs by
/// the container; a fact asks its registry here, either for what the registry
/// must already hold or, for a rig, for a convar declared on the spot. In the
/// global namespace so a fact reaches it whichever side's Abi it imports.
/// </summary>
internal static class DeclaredConVars
{
    /// <summary>The instance <paramref name="convars"/> declared; a missing declaration fails the fact.</summary>
    public static T Declared<T>(this ConVarRegistry convars) where T : ConVarBase
        => convars.All.OfType<T>().First();

    /// <summary>The instance <paramref name="convars"/> declared, declaring it first when it has not.</summary>
    public static T Declare<T>(this ConVarRegistry convars) where T : ConVarBase
    {
        if (convars.All.OfType<T>().FirstOrDefault() is { } held)
            return held;

        convars.RegisterTypes([typeof(T)]);
        return convars.Declared<T>();
    }

    /// <summary>A convar held at its declared default, in a registry of its own with no engine behind it.</summary>
    public static T Fresh<T>() where T : ConVarBase => new ConVarRegistry(new NoEngineConVars()).Declare<T>();

    /// <summary>A convar holding <paramref name="value"/>, as though the engine had it, in a registry of its own.</summary>
    public static T Holding<T>(string? value) where T : ConVarBase
    {
        T convar = Fresh<T>();
        if (value is not null)
            convar.Hold(value);
        return convar;
    }

    /// <summary>What a change from the engine does to a convar: the fact's way to move one.</summary>
    public static void Hold(this ConVarBase convar, string value) => convar.NotifyChanged(value, convar.RawValue);

    /// <summary>The same, held at <paramref name="raw"/>, as if the engine had been told so.</summary>
    public static T FreshAt<T>(string raw) where T : ConVarBase
    {
        T convar = Fresh<T>();
        convar.NotifyChanged(raw, convar.DefaultValue);
        return convar;
    }

    /// <summary>
    /// A class built by a container holding this registry's convars, with each
    /// convar its constructor names declared first: how a fact builds a data
    /// holder such as <c>GameRulesConVars</c>.
    /// </summary>
    public static T Holder<T>(this ConVarRegistry convars) where T : class
    {
        var declare = typeof(T).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => typeof(ConVarBase).IsAssignableFrom(t) && !convars.All.Any(c => c.GetType() == t));
        convars.RegisterTypes(declare.ToList());

        var services = new ServiceContainer();
        convars.PublishTo(services);
        return services.Create<T>();
    }
}

/// <summary>An engine that holds these values by name, and takes every declaration.</summary>
internal sealed class NamedConVars(IReadOnlyDictionary<string, string> values) : IConVarBackend
{
    private readonly List<string> _names = [];
    private readonly Dictionary<string, string> _held = new(values, StringComparer.OrdinalIgnoreCase);

    public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
    {
        _names.Add(name);
        _held.TryAdd(name, defaultValue);
        return _names.Count - 1;
    }

    public void Set(int id, string value) => _held[_names[id]] = value;
    public string Get(int id) => _held[_names[id]];
    public string? ValueOf(string name) => _held.GetValueOrDefault(name);
    public void SetNamed(string name, string value) => _held[name] = value;
}

/// <summary>What a side with no module answers a declaration: every registration refused.</summary>
internal sealed class NoEngineConVars : IConVarBackend
{
    public int Register(string name, string defaultValue, string help, int flags, float? min, float? max) => -1;
    public void Set(int id, string value) { }
    public string Get(int id) => string.Empty;
    public string? ValueOf(string name) => null;
}
