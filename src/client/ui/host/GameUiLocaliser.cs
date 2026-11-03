using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The UI's localiser in game: the mod's own language file (the one the
/// previews parse, with the files mods.yaml lists as <c>localisation</c>
/// chained into it), then the engine's tables for everything else (a map's
/// <c>#Valve_*</c> hints). Each token is asked of the engine once.
///
/// <para>
/// It answers <see cref="IUiLocalisation"/> too, and is registered as it from
/// <c>ClientUi.Build</c>: stock's client had ONE <c>g_pVGuiLocalize</c> and
/// both the chat HUD and the panels read it, so a notice token reaches the
/// chat in the same words the panels would show it in.
/// </para>
/// </summary>
public sealed class GameUiLocaliser(UiLanguageFile? mod, IEngineHud screen) : IUiLocaliser, IUiLocalisation
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
