using SourceSharp.Abi;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// TF2's <see cref="IBotNameSource"/> over the tf2 mod's files: <see cref="Path"/>,
/// read once through the engine's file seam when a name is first wanted.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotNameSource)])]
public sealed class TFGameFileBotNames(IEngineFiles files) : IBotNameSource
{
    public const string Path = "scripts/ai/tf2_bot_names.json";

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
