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

/// <summary>What a side with no module answers a declaration: every registration refused.</summary>
internal sealed class NoEngineConVars : IConVarBackend
{
    public int Register(string name, string defaultValue, string help, int flags, float? min, float? max) => -1;
    public void Set(int id, string value) { }
    public string Get(int id) => string.Empty;
    public string? ValueOf(string name) => null;
}
