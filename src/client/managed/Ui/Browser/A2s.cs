using System.Buffers.Binary;
using System.Globalization;
using System.Text;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// A server's query address, as Steam's servernetadr_t holds it: the IPv4
/// address in host order (a.b.c.d with a in the high byte) and a port.
/// </summary>
public readonly record struct ServerAddress(uint Ip, ushort Port)
{
    /// <summary>The port a Source server answers on when none is given.</summary>
    public const ushort DefaultPort = 27015;

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Ip >> 24}.{(Ip >> 16) & 0xFF}.{(Ip >> 8) & 0xFF}.{Ip & 0xFF}:{Port}");

    /// <summary>"a.b.c.d" or "a.b.c.d:port"; false for anything else (names are not looked up).</summary>
    public static bool TryParse(string? text, out ServerAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        ushort port = DefaultPort;
        int colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            if (!ushort.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port == 0)
                return false;
            text = text[..colon];
        }
        var parts = text.Split('.');
        if (parts.Length != 4)
            return false;
        uint ip = 0;
        foreach (var part in parts)
        {
            if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out byte b))
                return false;
            ip = (ip << 8) | b;
        }
        address = new ServerAddress(ip, port);
        return true;
    }
}

/// <summary>A server's A2S_INFO answer, the parts the browser shows; <see cref="GamePort"/> is where to connect.</summary>
public sealed record A2sInfo(string Name, string Map, string Folder, string Game, int Players, int MaxPlayers, int Bots,
                             bool Password, bool Secure, ushort GamePort);

/// <summary>What one datagram from a server was.</summary>
public enum A2sReplyKind : byte
{
    Unknown,
    Info,
    Players,
    Challenge,
}

/// <summary>One parsed datagram: an info answer, a player list, or a challenge to send back.</summary>
public readonly record struct A2sReply(A2sReplyKind Kind, A2sInfo? Info, IReadOnlyList<ServerPlayer> Players, int Challenge);

/// <summary>
/// The Source server query protocol (A2S) as the browser speaks it: the
/// requests, and the replies parsed. Single-packet replies only (a split
/// reply's 0xFFFFFFFE header is Unknown), which an info answer always is.
/// </summary>
public static class A2s
{
    private const int Simple = -1;   // 0xFFFFFFFF: one packet

    private const byte InfoRequestHeader = 0x54, PlayerRequestHeader = 0x55;
    private const byte InfoReplyHeader = 0x49, PlayerReplyHeader = 0x44, ChallengeHeader = 0x41;
    private static ReadOnlySpan<byte> InfoPayload => "Source Engine Query\0"u8;

    /// <summary>The challenge a player request carries before the server has given one.</summary>
    public const int NoChallenge = -1;

    /// <summary>A2S_INFO: the header and payload, then the challenge when the server asked for one.</summary>
    public static byte[] InfoRequest(int? challenge = null)
    {
        var packet = new byte[5 + InfoPayload.Length + (challenge is null ? 0 : 4)];
        BinaryPrimitives.WriteInt32LittleEndian(packet, Simple);
        packet[4] = InfoRequestHeader;
        InfoPayload.CopyTo(packet.AsSpan(5));
        if (challenge is { } c)
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5 + InfoPayload.Length), c);
        return packet;
    }

    /// <summary>A2S_PLAYER with a challenge (<see cref="NoChallenge"/> asks for one).</summary>
    public static byte[] PlayerRequest(int challenge)
    {
        var packet = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(packet, Simple);
        packet[4] = PlayerRequestHeader;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(5), challenge);
        return packet;
    }

    public static A2sReply Parse(ReadOnlySpan<byte> data, ushort queryPort)
    {
        if (data.Length < 5 || BinaryPrimitives.ReadInt32LittleEndian(data) != Simple)
            return default;
        var r = new Reader(data[5..]);
        try
        {
            switch (data[4])
            {
                case ChallengeHeader:
                    return new A2sReply(A2sReplyKind.Challenge, null, [], r.Int32());
                case InfoReplyHeader:
                    return new A2sReply(A2sReplyKind.Info, ParseInfo(ref r, queryPort), [], 0);
                case PlayerReplyHeader:
                    int count = r.Byte();
                    var players = new List<ServerPlayer>(count);
                    for (int i = 0; i < count; i++)
                    {
                        r.Byte();                                     // index
                        string name = r.String();
                        int score = r.Int32();
                        float seconds = r.Single();
                        players.Add(new ServerPlayer(name, score, (int)(seconds / 60f)));
                    }
                    return new A2sReply(A2sReplyKind.Players, null, players, 0);
                default:
                    return default;
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return default;    // a truncated reply is no reply
        }
    }

    private static A2sInfo ParseInfo(ref Reader r, ushort queryPort)
    {
        r.Byte();                                                     // protocol
        string name = r.String(), map = r.String(), folder = r.String(), game = r.String();
        ushort appId = r.UInt16();
        int players = r.Byte(), max = r.Byte(), bots = r.Byte();
        r.Byte();                                                     // server type
        r.Byte();                                                     // environment
        bool password = r.Byte() != 0;
        bool secure = r.Byte() != 0;
        if (appId == 2400)                                            // The Ship: mode, witnesses, duration
        {
            r.Byte();
            r.Byte();
            r.Byte();
        }
        ushort gamePort = queryPort;
        if (r.Left > 0)
        {
            r.String();                                               // version
            if (r.Left > 0 && (r.Byte() is var edf) && (edf & 0x80) != 0)
                gamePort = r.UInt16();
        }
        return new A2sInfo(name, map, folder, game, players, max, bots, password, secure, gamePort);
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _at;

        public readonly int Left => _data.Length - _at;

        public byte Byte() => _data[_at++];

        public ushort UInt16()
        {
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_at, 2));
            _at += 2;
            return v;
        }

        public int Int32()
        {
            if (Left < 4)
                throw new IndexOutOfRangeException();
            int v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_at, 4));
            _at += 4;
            return v;
        }

        public float Single() => BitConverter.Int32BitsToSingle(Int32());

        public string String()
        {
            int end = _data[_at..].IndexOf((byte)0);
            if (end < 0)
                throw new IndexOutOfRangeException();
            string s = Encoding.UTF8.GetString(_data.Slice(_at, end));
            _at += end + 1;
            return s;
        }
    }
}
