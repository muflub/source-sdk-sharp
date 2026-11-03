using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// A short public label per player that the scoreboard shows beside the
/// player's name (user ruling 2026-11-01: a bot's skill). A mod sets it; the
/// player resource networks it (<c>PlayerResourceNetState.Tag</c>), so the
/// core names no mod's type and nothing but the label leaves the server.
/// </summary>
public interface IScoreboardTags
{
    /// <summary>Gives <paramref name="player"/> its label; empty clears it.</summary>
    void Set(EntityHandle player, string tag);

    /// <summary><paramref name="player"/>'s label, empty for none.</summary>
    string TagOf(EntityHandle player);
}

/// <summary>The labels by player handle; a handle that left reads none.</summary>
public sealed class ScoreboardTags : IScoreboardTags
{
    private readonly Dictionary<uint, string> _tags = new();

    public void Set(EntityHandle player, string tag)
    {
        if (string.IsNullOrEmpty(tag))
            _tags.Remove(player.Value);
        else
            _tags[player.Value] = tag;
    }

    public string TagOf(EntityHandle player) => _tags.TryGetValue(player.Value, out string? tag) ? tag : string.Empty;
}
