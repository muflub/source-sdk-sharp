using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>A dictionary of convars: a form's sample store, and its facts'.</summary>
public sealed class MemoryConVarStore : IUiConVarStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every Set, in order: what a form wrote.</summary>
    public List<(string Name, string Value)> Writes { get; } = [];

    public MemoryConVarStore With(string name, string value)
    {
        _values[name] = value;
        return this;
    }

    public string? Get(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public void Set(string name, string value)
    {
        _values[name] = value;
        Writes.Add((name, value));
    }
}

public static class MouseOptionsSamples
{
    public static MemoryConVarStore Stock() => new MemoryConVarStore()
        .With("sensitivity", "3").With("m_rawinput", "1").With("m_filter", "0")
        .With("m_customaccel", "0").With("m_pitch", "0.022");

    private static UiScreen Build(UiSampleServices s, MemoryConVarStore store)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IUiConVarStore>(store);
        return s.Create<MouseOptions>();
    }

    [UiSample("stock settings")]
    private static UiScreen Stock(UiSampleServices s) => Build(s, Stock());

    [UiSample("reversed, filtered, accelerated")]
    private static UiScreen Tuned(UiSampleServices s) => Build(s, Stock()
        .With("sensitivity", "7.5").With("m_pitch", "-0.022").With("m_filter", "1").With("m_customaccel", "3"));

    [UiSample("too slow (invalid)")]
    private static UiScreen TooSlow(UiSampleServices s) => Build(s, Stock().With("sensitivity", "0.3"));
}
