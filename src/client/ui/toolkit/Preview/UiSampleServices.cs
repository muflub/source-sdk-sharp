using System.Reflection;

using SourceSharp.Client.Ui;

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

    /// <summary>
    /// The HUD-art library this build seeds: the preview run's --content
    /// library, so a sample's art layers draw the decoded pixels. Without one
    /// the stateless <see cref="None"/> below answers and the panels draw
    /// their fallbacks. The seed wins over any registration — a sample's own
    /// fake would hide the --content art the run was asked for — so a build
    /// that wants its own art (a fact's recording fake) sets this over it.
    /// </summary>
    public IUiTextureLibrary? HudArt { get; set; }

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
        if (type == typeof(IUiTextureLibrary))
            return HudArt ?? (object)new None();
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

    /// <summary>The seedless stand-in: every name misses, so screens draw their fallbacks. Made per resolution, holding nothing.</summary>
    private sealed class None : IUiTextureLibrary
    {
        public int Find(string name) => 0;
        public int Material(string name) => 0;

        public UiTextureInfo? About(string name) => null;
        public UiTextureIcon? Icon(string name) => null;
    }
}
