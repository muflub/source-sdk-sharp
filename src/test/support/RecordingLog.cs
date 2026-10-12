namespace SourceSharp.Abi;

/// <summary>
/// An <see cref="ILog"/> for a fact: keeps every line it is handed, and
/// passes each to <see cref="Line"/> if one is set. A test hands one to the
/// subject it builds and reads what that subject (and nothing else) said.
/// </summary>
public sealed class RecordingLog : ILog
{
    private readonly List<(string Text, bool Warning)> _lines = new();

    /// <summary>Called with every line as it arrives.</summary>
    public Action<string, bool>? Line { get; set; }

    /// <summary>Every line so far, in order.</summary>
    public IReadOnlyList<(string Text, bool Warning)> Lines
    {
        get
        {
            lock (_lines)
                return _lines.ToArray();
        }
    }

    /// <summary>Every line's text, joined.</summary>
    public string Text => string.Concat(Lines.Select(l => l.Text));

    public void Msg(string message) => Add(message, false);

    public void Warning(string message) => Add(message, true);

    private void Add(string message, bool warning)
    {
        lock (_lines)
            _lines.Add((message, warning));
        Line?.Invoke(message, warning);
    }
}
