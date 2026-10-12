using System.Reflection;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The container samples build screens with (plan_client_ui §4): register a
/// fake per data interface, then <see cref="Create{T}"/> the screen; its
/// constructor's parameters are resolved from the registrations, and a
/// parameter of a concrete type (a sub-panel) is built the same way, once.
/// The game's own container does the same in game; this one exists so the
/// preview and the tests build screens exactly as DI will, with nothing
/// new()'d by hand. Not a per-frame path.
/// </summary>
public sealed class UiSampleServices
{
    private readonly Dictionary<Type, object> _instances = new();

    /// <summary>Registers <paramref name="instance"/> as <typeparamref name="T"/>. One registration per interface.</summary>
    public UiSampleServices Add<T>(T instance) where T : class
    {
        if (!_instances.TryAdd(typeof(T), instance))
            throw new InvalidOperationException($"{typeof(T).Name} is registered twice");
        return this;
    }

    public T Create<T>() where T : class => (T)Create(typeof(T));

    /// <summary>Whether <typeparamref name="T"/> is registered (or already built).</summary>
    public bool Has<T>() => _instances.ContainsKey(typeof(T));

    public object Create(Type type)
    {
        if (_instances.TryGetValue(type, out var known))
            return known;
        if (type.IsInterface || type.IsAbstract)
            throw new InvalidOperationException($"no sample registered for {type.Name}: add s.Add<{type.Name}>(...)");

        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                       .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault()
                   ?? throw new InvalidOperationException($"{type.Name} has no public constructor");
        // An optional interface nobody registered (a screen's actions, which a
        // sample has none of) takes its default.
        var args = ctor.GetParameters()
            .Select(p => p.HasDefaultValue && p.ParameterType.IsInterface && !_instances.ContainsKey(p.ParameterType)
                ? p.DefaultValue : Create(p.ParameterType))
            .ToArray();
        var built = ctor.Invoke(args);
        _instances[type] = built;
        return built;
    }
}
