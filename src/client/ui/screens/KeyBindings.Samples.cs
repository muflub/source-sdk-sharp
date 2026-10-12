using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// Binds in a dictionary: the key bindings' sample store, and its facts'. A
/// key holds one command, as the engine's do; names are the engine's
/// (inputsystem's ButtonCodeToString), upper case.
/// </summary>
public sealed class MemoryKeyBindingStore : IUiKeyBindingStore
{
    private readonly List<(string Key, string Command)> _binds = [];

    /// <summary>Every Bind and Unbind, in order: <c>bind KEY command</c> or <c>unbind KEY</c>.</summary>
    public List<string> Writes { get; } = [];

    public MemoryKeyBindingStore With(string key, string command)
    {
        Put(key, command);
        return this;
    }

    public string? CommandOn(string key)
    {
        foreach (var (k, c) in _binds)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    public string? KeyFor(string command)
    {
        foreach (var (k, c) in _binds)
            if (string.Equals(c, command, StringComparison.OrdinalIgnoreCase))
                return k;
        return null;
    }

    public void Bind(string key, string command)
    {
        Put(key, command);
        Writes.Add($"bind {key} {command}");
    }

    public void Unbind(string key)
    {
        _binds.RemoveAll(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        Writes.Add($"unbind {key}");
    }

    public string? KeyName(int code) => code switch
    {
        >= 1 and <= 10 => ((char)('0' + code - 1)).ToString(),
        >= 11 and <= 36 => ((char)('A' + code - 11)).ToString(),
        64 => "ENTER",
        65 => "SPACE",
        67 => "TAB",
        79 => "SHIFT",
        81 => "ALT",
        83 => "CTRL",
        >= 107 and <= 111 => "MOUSE" + (code - 106),
        UiContext.WheelUpButton => "MWHEELUP",
        UiContext.WheelDownButton => "MWHEELDOWN",
        _ => null,
    };

    private void Put(string key, string command)
    {
        _binds.RemoveAll(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        _binds.Add((key, command));
    }
}

public static class KeyBindingsSamples
{
    /// <summary>Stock's default binds (cfg/config_default.cfg) for the form's commands.</summary>
    public static MemoryKeyBindingStore Stock() => new MemoryKeyBindingStore()
        .With("W", "+forward").With("S", "+back").With("A", "+moveleft").With("D", "+moveright")
        .With("SPACE", "+jump").With("CTRL", "+duck").With("SHIFT", "+speed")
        .With("MOUSE1", "+attack").With("MOUSE2", "+attack2").With("R", "+reload").With("E", "+use")
        .With("Q", "lastinv").With("Y", "messagemode").With("U", "messagemode2").With("K", "+voicerecord")
        .With("TAB", "+showscores");

    private static UiScreen Build(UiSampleServices s, MemoryKeyBindingStore store)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IUiKeyBindingStore>(store);
        return s.Create<KeyBindings>();
    }

    [UiSample("stock binds")]
    private static UiScreen Stock(UiSampleServices s) => Build(s, Stock());

    [UiSample("some unbound")]
    private static UiScreen Unbound(UiSampleServices s)
    {
        var store = Stock();
        store.Unbind("K");
        store.Unbind("Q");
        return Build(s, store);
    }
}
