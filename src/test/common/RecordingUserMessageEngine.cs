using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// A user-message engine for facts: records every send it is asked for, and
/// holds the payload a test gives each message handle (the routing tests hand
/// it to the receiver as a BitBlock, as native does).
/// </summary>
internal sealed class RecordingUserMessageEngine : IUserMessageOutbox
{
    public List<(int Id, byte[] Payload, int Player, bool Reliable)> Sent { get; } = new();
    public Dictionary<nint, byte[]> Payloads { get; } = new();
    public HashSet<int> EmptySlots { get; } = new();

    public bool Send(int id, ReadOnlySpan<byte> payload, int player, bool reliable)
    {
        if (player > 0 && EmptySlots.Contains(player))
            return false;

        Sent.Add((id, payload.ToArray(), player, reliable));
        return true;
    }
}
