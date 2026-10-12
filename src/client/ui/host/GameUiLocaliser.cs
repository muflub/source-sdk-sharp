using SourceSharp.Abi;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The UI's localiser in game: the mod's own language file (the one the
/// previews parse), then the engine's tables for everything else (a map's
/// <c>#Valve_*</c> hints). Each token is asked of the engine once.
/// </summary>
public sealed class GameUiLocaliser(UiLanguageFile? mod, IEngineHud screen) : IUiLocaliser
{
    private readonly Dictionary<string, string?> _engine = new(StringComparer.Ordinal);

    public string? Find(string token)
    {
        if (mod?.Find(token) is { } text)
            return text;
        if (_engine.TryGetValue(token, out var known))
            return known;
        string found = screen.Localise(token);
        known = found.Length == 0 || found == token ? null : found;
        _engine[token] = known;
        return known;
    }
}
