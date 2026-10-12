using TF2.Descent.Shared;
using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>Sends TF2.Descent.Shared's messages to one client (by entity index) or to all (0).</summary>
[SideService(Side.Server)]
public sealed class ClientMessages(IUserMessageSender messages)
{
    public void Notice(int entIndex, string text)
        => messages.Send(new Shared.Notice { Text = text }, entIndex);

    /// <summary>
    /// Tells clients the next level's map and its download URL; false when it
    /// does not fit a message or <paramref name="sha256"/> is neither empty nor
    /// a SHA-256.
    /// </summary>
    public bool NextLevel(int entIndex, int depth, string map, string url, ReadOnlySpan<byte> sha256 = default)
    {
        if (sha256.Length is not (0 or Shared.NextLevel.HashBytes))
            return false;

        return messages.Send(new Shared.NextLevel
        {
            Depth = Shared.Instance.DepthOf(depth),
            Map = map,
            Url = url,
            Sha256 = sha256.ToArray(),
        }, entIndex);
    }

    /// <summary>Asks the client for a fresh Steam ticket; false when the nonce cannot travel back in a reply.</summary>
    public bool Reauth(int entIndex, string nonce, ulong gameServerSteamId)
        => ReauthMessage.Of(nonce, gameServerSteamId) is { } payload && messages.Send(payload, entIndex);

    public void Instance(int entIndex, string kind, int depth)
        => messages.Send(new Shared.Instance { Kind = kind, Depth = Shared.Instance.DepthOf(depth) }, entIndex);
}
