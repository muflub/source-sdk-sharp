using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// <see cref="IBotNameSource"/> over the game's files: <see cref="Path"/>,
/// read once through the engine's file seam when a name is first wanted.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotNameSource)])]
public sealed class GameFileBotNames(IEngineFiles files) : IBotNameSource
{
    public const string Path = "scripts/ai/hl2mp_bot_names.json";

    private BotNamePools? _pools;
    private string? _refusal;

    public BotNamePools? Pools
    {
        get
        {
            if (_pools is null && _refusal is null)
                Load();
            return _pools;
        }
    }

    public string Refusal => _refusal ?? "";

    private void Load()
    {
        string? json = files.ReadGameFile(Path);
        if (json is null)
        {
            _refusal = $"no bot names: expected {Path}";
            return;
        }

        try
        {
            _pools = BotNamePools.Parse(json);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException)
        {
            _refusal = $"{Path} cannot be read: {e.Message}";
        }
    }
}
