using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// A client effect handled in managed code: what a stock
/// <c>DECLARE_CLIENT_EFFECT</c> registered by name, moved with the subsystem
/// that owned it.
/// </summary>
public interface IClientEffectHandler
{
    /// <summary>The dispatch name it answers (matched as stock matches, case-insensitively).</summary>
    string EffectName { get; }

    void Dispatch(in EffectData data);
}

/// <summary>The managed client effects, asked before the native registry.</summary>
public interface IClientEffectHandlers
{
    /// <summary>Runs the handler for <paramref name="name"/> and answers true, or answers false when none is managed.</summary>
    bool TryDispatch(string name, in EffectData data);

    /// <summary>Dispatches each name has handled.</summary>
    IReadOnlyDictionary<string, int> Counts { get; }
}

/// <summary><see cref="IClientEffectHandlers"/> over a fixed list.</summary>
public sealed class ClientEffectHandlers : IClientEffectHandlers
{
    private readonly Dictionary<string, IClientEffectHandler> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public ClientEffectHandlers(IEnumerable<IClientEffectHandler> handlers)
    {
        foreach (IClientEffectHandler h in handlers)
            _byName[h.EffectName] = h;
    }

    /// <summary>
    /// One more handler, for an effect whose subsystem is composed after the
    /// list (BoltImpact needs the tempent decisions). Before the first dispatch.
    /// </summary>
    public void Add(IClientEffectHandler handler) => _byName[handler.EffectName] = handler;

    public IReadOnlyDictionary<string, int> Counts => _counts;

    public bool TryDispatch(string name, in EffectData data)
    {
        if (!_byName.TryGetValue(name, out IClientEffectHandler? handler))
            return false;

        handler.Dispatch(data);
        _counts[name] = _counts.TryGetValue(name, out int n) ? n + 1 : 1;
        return true;
    }
}
