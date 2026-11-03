namespace SourceSharp.Client.Ui;

/// <summary>
/// The game's UI localisation as the side adapters read it: a <c>#Token</c> to
/// its text through the mod's own language file and the engine's tables, null
/// when there is none. The UI host answers it (its GameUiLocaliser); a side
/// adapter holds it by constructor rather than naming the UI's own localiser,
/// which lives in an assembly a reload swaps.
/// </summary>
public interface IUiLocalisation
{
    /// <summary>The token's text, or null when no table holds it (the caller's fallback arm decides).</summary>
    string? Find(string token);
}
