extern alias client;

using System.Linq;
using CAbi = client::SourceSharp.Abi;

/// <summary><see cref="DeclaredConVars"/> for the client's registry, in a project that holds both sides.</summary>
internal static class DeclaredClientConVars
{
    /// <summary>The instance <paramref name="convars"/> declared; a missing declaration fails the fact.</summary>
    public static T Declared<T>(this CAbi.ConVarRegistry convars) where T : CAbi.ConVarBase
        => convars.All.OfType<T>().First();

    /// <summary>A client convar held at its declared default, in a registry of its own with no engine behind it.</summary>
    public static T Fresh<T>() where T : CAbi.ConVarBase
    {
        var convars = new CAbi.ConVarRegistry(new NoEngine());
        convars.RegisterTypes([typeof(T)]);
        return convars.Declared<T>();
    }

    /// <summary>A client convar holding <paramref name="value"/>, as though the engine had it.</summary>
    public static T Holding<T>(string? value) where T : CAbi.ConVarBase
    {
        T convar = Fresh<T>();
        if (value is not null)
            convar.NotifyChanged(value, convar.RawValue);
        return convar;
    }

    /// <summary>What a change from the engine does to a convar: the fact's way to move one.</summary>
    public static void Hold(this CAbi.ConVarBase convar, string value) => convar.NotifyChanged(value, convar.RawValue);

    /// <summary>A class built by a container holding the convars its constructor names, each with its value in <paramref name="values"/> or its default.</summary>
    public static T Holder<T>(params (string Name, string Value)[] values) where T : class
    {
        var named = values.ToDictionary(v => v.Name, v => v.Value);
        var convars = new CAbi.ConVarRegistry(new Named(named));
        var declare = typeof(T).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => typeof(CAbi.ConVarBase).IsAssignableFrom(t));
        convars.RegisterTypes(declare.ToList());
        var services = new CAbi.ServiceContainer();
        convars.PublishTo(services);
        return services.Create<T>();
    }

    private sealed class Named(Dictionary<string, string> values) : CAbi.IConVarBackend
    {
        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max) => 0;
        public void Set(int id, string value) { }
        public string Get(int id) => string.Empty;
        public string? ValueOf(string name) => values.TryGetValue(name, out string? value) ? value : null;
    }

    private sealed class NoEngine : CAbi.IConVarBackend
    {
        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max) => -1;
        public void Set(int id, string value) { }
        public string Get(int id) => string.Empty;
        public string? ValueOf(string name) => null;
    }
}
