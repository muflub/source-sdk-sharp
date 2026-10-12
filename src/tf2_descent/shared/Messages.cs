using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client
// (plan_side_isolation §2.4), so both sides declare these messages under the
// same keys (TF2.Descent:notice, TF2.Descent:instance).

namespace TF2.Descent.Shared;

//=============================================================================
// What the server tells a TF2Descent client (plan_host_sdk §2.1). Each message
// is a payload (a partial struct whose partial properties are its members,
// written and read by the generated code) and a handler both sides load; on
// the client the handler hands what it read to the hook interfaces below,
// implemented by TF2.Descent.Client's services (plan_host_sdk §2.11). The
// server sends a payload: messages.Send(new Notice { Text = ... }, entIndex).
//=============================================================================

/// <summary>A line from the server or the hosting service to show on the HUD.</summary>
public interface INoticeHook
{
    void OnNotice(string text);
}

/// <summary>Which instance the player is on: the town, or a level and its depth.</summary>
public interface IInstanceHook
{
    void OnInstance(string kind, int depth);
}

/// <summary>A notice's payload: one line of text, cut to <see cref="MaxText"/>.</summary>
public partial struct Notice
{
    /// <summary>The longest text a notice carries; longer is cut.</summary>
    public const int MaxText = 200;

    [UserMessageString(MaxLength = MaxText)]
    public partial string Text { get; set; }
}

[UserMessage(Name)]
public sealed class NoticeMessage(IHooks<INoticeHook>? hooks) : IUserMessage<Notice>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public NoticeMessage() : this(null) { }

    public const string Name = "notice";

    public void OnReceived(Notice message)
    {
        string text = message.Text;
        hooks?.Each(nameof(INoticeHook.OnNotice), h => h.OnNotice(text));
    }
}

/// <summary>Which instance the player is on: its kind and its depth (never negative).</summary>
public partial struct Instance
{
    /// <summary>The longest kind a payload carries ("town", "level"): bounded, so a send cannot fail.</summary>
    public const int MaxKind = 64;

    [UserMessageString(MaxLength = MaxKind)]
    public partial string Kind { get; set; }
    public partial short Depth { get; set; }

    /// <summary>A depth as the payload carries it: clamped to 0..short.MaxValue.</summary>
    public static short DepthOf(int depth) => (short)Math.Clamp(depth, 0, short.MaxValue);
}

[UserMessage(Name)]
public sealed class InstanceMessage(IHooks<IInstanceHook>? hooks) : IUserMessage<Instance>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public InstanceMessage() : this(null) { }

    public const string Name = "instance";

    public void OnReceived(Instance message)
    {
        string kind = message.Kind;
        int depth = message.Depth;
        hooks?.Each(nameof(IInstanceHook.OnInstance), h => h.OnInstance(kind, depth));
    }
}
/// <summary>The client is told which level comes next, so it can fetch the map before the hop.</summary>
public interface INextLevelHook
{
    /// <param name="sha256">The unpacked .bsp's SHA-256, or null when the host sent none.</param>
    void OnNextLevel(int depth, string map, string url, byte[]? sha256);
}

/// <summary>The next level's payload. <see cref="Sha256"/> is empty or a SHA-256.</summary>
public partial struct NextLevel
{
    /// <summary>The length of a SHA-256 digest; the hash member is this or empty.</summary>
    public const int HashBytes = 32;

    public partial short Depth { get; set; }
    public partial string Map { get; set; }
    public partial string Url { get; set; }

    [UserMessageArray(MaxCount = HashBytes)]
    public partial byte[] Sha256 { get; set; }
}

/// <summary>
/// The party's next level down (the host's NextLevel, D-H14): its map and the
/// URL of its <c>maps/&lt;map&gt;.bsp.bz2</c> on the fastdl, and the unpacked
/// map's SHA-256 when the host knows it, so the client can download and check
/// it in the background. Stock clients only download at connect.
/// </summary>
[UserMessage(Name)]
public sealed class NextLevelMessage(IHooks<INextLevelHook>? hooks) : IUserMessage<NextLevel>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public NextLevelMessage() : this(null) { }

    public const string Name = "next_level";

    public void OnReceived(NextLevel message)
    {
        // A hash that is neither empty nor a SHA-256 is not one this build
        // sends: dropped rather than checked against.
        byte[]? sha256 = message.Sha256 is { Length: > 0 } hash ? hash : null;
        if (sha256 is not null && sha256.Length != NextLevel.HashBytes)
            return;

        int depth = message.Depth;
        string map = message.Map, url = message.Url;
        hooks?.Each(nameof(INextLevelHook.OnNextLevel), h => h.OnNextLevel(depth, map, url, sha256));
    }
}

/// <summary>The server asks this client for a fresh Steam auth session ticket.</summary>
public interface IReauthHook
{
    /// <param name="nonce">The host's nonce, echoed back with the ticket.</param>
    /// <param name="gameServerSteamId">The Steam game server the ticket is minted for (the gateway's).</param>
    void OnReauth(string nonce, ulong gameServerSteamId);
}

/// <summary>
/// The hosting gateway restarted and lost this player's Steam session (host
/// plan_steam_auth S-20): the client mints a fresh ticket for
/// <c>gameServerSteamId</c> and sends it back as a <see cref="TicketReply"/>.
/// The player keeps playing meanwhile.
/// </summary>
/// <summary>
/// A reauth's payload: the host's nonce and the Steam game server's id as
/// decimal text, two NUL-terminated strings (the bytes main's hand-written
/// writer produced).
/// </summary>
public partial struct Reauth
{
    /// <summary>The longest nonce: <see cref="TicketReply.IsNonce"/>'s 64.</summary>
    public const int MaxNonce = 64;

    /// <summary>The longest server id: a ulong's 20 digits.</summary>
    public const int MaxServer = 20;

    [UserMessageString(MaxLength = MaxNonce)]
    public partial string Nonce { get; set; }

    [UserMessageString(MaxLength = MaxServer)]
    public partial string Server { get; set; }
}

[UserMessage(Name)]
public sealed class ReauthMessage(IHooks<IReauthHook>? hooks) : IUserMessage<Reauth>
{
    /// <summary>For a side that registers the declaration without dispatching it.</summary>
    public ReauthMessage() : this(null) { }

    public const string Name = "reauth";

    /// <summary>The payload, or null when the nonce cannot travel back (<see cref="TicketReply.IsNonce"/>).</summary>
    public static Reauth? Of(string nonce, ulong gameServerSteamId)
        => TicketReply.IsNonce(nonce)
               ? new Reauth { Nonce = nonce, Server = gameServerSteamId.ToString(System.Globalization.CultureInfo.InvariantCulture) }
               : null;

    public void OnReceived(Reauth message)
    {
        string nonce = message.Nonce;
        if (!TicketReply.IsNonce(nonce)
            || !ulong.TryParse(message.Server, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong id))
            return;

        hooks?.Each(nameof(IReauthHook.OnReauth), h => h.OnReauth(nonce, id));
    }
}

/// <summary>
/// The ticket's way back: one SourceSharp net message from the client
/// (IEngineNetworking.SendServerMessage) under <see cref="Key"/>. Payload: the
/// nonce's length (one byte), the nonce, then the ticket's bytes. Both sides
/// use this one format.
/// </summary>
public static class TicketReply
{
    public const string Key = "tf2_descent:ticket";

    /// <summary>A ticket's most bytes: Steam's buffer.</summary>
    public const int MaxTicket = 1024;

    /// <summary>The payload, or null when the nonce or ticket cannot travel.</summary>
    public static byte[]? Write(string nonce, ReadOnlySpan<byte> ticket)
    {
        if (!IsNonce(nonce) || ticket.IsEmpty || ticket.Length > MaxTicket)
            return null;

        var payload = new byte[1 + nonce.Length + ticket.Length];
        payload[0] = (byte)nonce.Length;
        System.Text.Encoding.ASCII.GetBytes(nonce, payload.AsSpan(1));
        ticket.CopyTo(payload.AsSpan(1 + nonce.Length));
        return payload;
    }

    /// <summary>The nonce and a copy of the ticket, or false for a payload that is not one.</summary>
    public static bool TryRead(ReadOnlySpan<byte> payload, out string nonce, out byte[] ticket)
    {
        nonce = "";
        ticket = [];
        if (payload.Length < 2)
            return false;

        int n = payload[0];
        if (n == 0 || payload.Length <= 1 + n || payload.Length - 1 - n > MaxTicket)
            return false;

        var text = payload.Slice(1, n);
        foreach (byte b in text)
            if (b >= 0x80)
                return false;
        string read = System.Text.Encoding.ASCII.GetString(text);
        if (!IsNonce(read))
            return false;

        nonce = read;
        ticket = payload[(1 + n)..].ToArray();
        return true;
    }

    /// <summary>A nonce this format carries: 1-64 of [A-Za-z0-9_-] (the host's is 32 hex).</summary>
    public static bool IsNonce(string nonce) =>
        nonce.Length is > 0 and <= 64 && nonce.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
