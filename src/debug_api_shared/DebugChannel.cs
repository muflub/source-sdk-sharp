using System.Buffers.Binary;
using System.Text;


namespace Debug.Api;

/// <summary>
/// THE CLIENT-TO-SERVER DEBUG CHANNEL, as both sides agree it works.
///
/// <para>
/// The problem it exists for: the development API is the SERVER's. There is one
/// <c>DebugApiServer</c> per process, only <c>ServerModule</c> builds it, and its
/// socket is named for that process' pid. On a LISTEN server that is invisible,
/// because both halves of the game are one process and a client-side ring in
/// <see cref="PredictionErrors"/> is the same storage location the server reads.
/// On a DEDICATED server with a separate client process it is fatal to the whole
/// idea: the client's debug data has no route to the API at all, and
/// <c>/prediction</c> answers "no client in this process" forever.
/// </para>
///
/// <para>
/// So the server offers a channel and the client dials it. Everything about the
/// shape below is chosen so that "a server tells a client to open a control
/// channel" stays a development tool and does not become a lever.
/// </para>
///
/// <para>
/// <b>THE INVITATION NAMES NO ADDRESS, AND THAT IS THE WHOLE SECURITY DESIGN.</b>
/// The obvious protocol — the server sends a host and a port and the client
/// dials what it was sent — hands every server a user joins the ability to aim
/// that user's command-and-control socket at any host it likes. It is a redirect
/// primitive with a game server on one end. What travels instead is an
/// invitation: a protocol number, a PORT, and a NONCE. The client decides for
/// itself what host that port is on, from its OWN command line, and refuses an
/// invitation that tries to tell it. See
/// <see cref="DebugChannelInvitation.TryRead"/>, which enforces it structurally
/// rather than by convention.
/// </para>
/// </summary>
public static class DebugChannelProtocol
{
    /// <summary>
    /// The wire version. Bumped when the framing changes; a mismatch is refused
    /// on both sides rather than guessed at, because the two halves of a running
    /// game can be different builds during a managed reload.
    /// </summary>
    public const int Version = 1;

    /// <summary>The user message that carries an invitation. Declared in <c>UserMessages.cs</c>.</summary>
    public const string MessageName = "DebugChannel";

    /// <summary>
    /// THE CLIENT'S OWN OPT-IN, and it is checked first and independently of
    /// anything the server said. No <c>-debugui</c> on this process' command line
    /// and the client installs no listener for the invitation, so there is no
    /// code path from an arriving message to a socket.
    /// </summary>
    public const string ClientParam = "-debugui";

    /// <summary>
    /// Where the client believes the server it joined is, when it is not the
    /// loopback default.
    ///
    /// <para>
    /// ON THE CLIENT'S OWN COMMAND LINE, never in the invitation. That is the
    /// point: a host named here was named by the person running the client, and
    /// is by construction an address they already trusted enough to join. A host
    /// named by the server would be an address they never chose.
    /// </para>
    /// </summary>
    public const string ClientHostParam = "-debuguihost";

    /// <summary>Loopback, which is right for a listen server and for a server on this machine.</summary>
    public const string DefaultHost = "127.0.0.1";

    /// <summary>THE SERVER'S OWN OPT-IN. Off unless this is on, independently of the client's.</summary>
    public const string ServerParam = "-debuguiserve";

    /// <summary>Overrides <see cref="DefaultPort"/>.</summary>
    public const string ServerPortParam = "-debuguiport";

    /// <summary>
    /// Binds the channel beyond loopback. ITS OWN FLAG, deliberately: a
    /// development channel reachable from the network is a different decision
    /// from a development channel reachable from this machine, and a server
    /// that only ever meant the second must not get the first by accident.
    /// </summary>
    public const string ServerPublicParam = "-debuguipublic";

    /// <summary>
    /// The channel's default TCP port.
    ///
    /// <para>
    /// <b>27120, AND THE TWO NUMBERS BELOW IT ARE WHY.</b> 27100 is the dev
    /// API's own TCP fallback (<c>DebugApiServer.DefaultPort</c>, and
    /// <c>debug-api-proxy</c>'s <c>FALLBACK_TCP_PORT</c>); 27110 is what
    /// <c>./debug-api ui</c> serves the web interface on
    /// (<c>debug-api-proxy</c>'s <c>DEFAULT_PORT</c>). This was 27110 until a live
    /// run said "could not listen: Address already in use" — a peer's web UI was
    /// holding it, and the channel simply did not come up.
    /// </para>
    /// </summary>
    public const int DefaultPort = 27120;

    /// <summary>The HTTP path the client requests. Fixed; there is nothing to route.</summary>
    public const string Path = "/sourcesharp-debug";

    /// <summary>The header the nonce travels in, so the upgrade is refused before a WebSocket exists.</summary>
    public const string NonceHeader = "X-SourceSharp-Nonce";

    /// <summary>
    /// The header a resuming client presents its slot and resume token in
    /// (<c>slot:token</c>), in place of a nonce; and the header the server's
    /// 101 hands every player slot's session its next token in, same form.
    /// See <see cref="DebugChannelResume"/>.
    /// </summary>
    public const string ResumeHeader = "X-SourceSharp-Resume";

    /// <summary>A nonce is 16 random bytes as lower-case hex.</summary>
    public const int NonceChars = 32;

    /// <summary>Enough for the invitation's three fields with the nonce at full length.</summary>
    public const int MaxInvitationBytes = 64;

    /// <summary>
    /// True when a string is shaped like a nonce this protocol minted.
    ///
    /// <para>
    /// Checked as WELL as looked up, not instead of: the lookup is the real
    /// gate, and this only keeps an arbitrary blob out of the table's keys.
    /// </para>
    /// </summary>
    public static bool IsWellFormedNonce(string? nonce)
    {
        if (nonce is null || nonce.Length != NonceChars)
            return false;

        foreach (char c in nonce)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;
        }

        return true;
    }
}

/// <summary>
/// What the server offers a joining client: a protocol number, a port, and a
/// single-use nonce.
///
/// <para>
/// <b>THERE IS NO HOST FIELD AND THERE MUST NEVER BE ONE.</b> If you are here to
/// add one — because a server behind NAT advertises a different address, because
/// a proxy needs naming, because it would be convenient — that field is the
/// vulnerability this whole design is shaped around. Read
/// <see cref="TryRead"/>'s refusal rule before you do.
/// </para>
/// </summary>
/// <param name="Protocol">The sender's <see cref="DebugChannelProtocol.Version"/>.</param>
/// <param name="Port">A TCP port ON THE SERVER THE CLIENT IS ALREADY TALKING TO.</param>
/// <param name="Nonce">Single-use, server-minted, presented at the handshake.</param>
public readonly record struct DebugChannelInvitation(int Protocol, int Port, string Nonce)
{
    /// <summary>
    /// Reads an invitation, or refuses it and says why.
    ///
    /// <para>
    /// <b>THE REFUSAL RULE, AND WHY IT IS A LENGTH CHECK.</b> After the three
    /// declared fields the payload must be EXACTLY EXHAUSTED. A server that
    /// appends anything — most obviously a hostname or an IP, which is the field
    /// the next person to touch this will be tempted to add — leaves bytes over
    /// and the whole invitation is refused, nonce and all. That is deliberately
    /// stronger than ignoring an unknown trailer: an ignored field is one edit
    /// away from being an honoured field, whereas a payload that cannot carry
    /// one at all is a property of the code rather than of somebody's restraint.
    /// </para>
    ///
    /// <para>
    /// It also means the client never has to decide whether an address it was
    /// sent is acceptable, because it is never handed one. The dial host comes
    /// from <see cref="DebugChannelProtocol.ClientHostParam"/> on the CLIENT's
    /// own command line, or from loopback. Extending the protocol is still
    /// possible — bump <see cref="DebugChannelProtocol.Version"/> — and that is
    /// the point: a new field is a version change somebody has to make on both
    /// sides, not a trailer a server can smuggle past an old client.
    /// </para>
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out DebugChannelInvitation invitation,
                               out string refusal)
    {
        var reader = new ByteReader(bytes);
        return TryRead(ref reader, out invitation, out refusal);
    }

    /// <summary>
    /// Writes the invitation's bytes (two little-endian ints and the nonce with
    /// its NUL, as UserMessageWriter writes them) into <paramref name="into"/>.
    /// Returns the length, or -1 when it does not fit and nothing is written.
    /// </summary>
    public int Write(Span<byte> into)
    {
        int nonceBytes = Encoding.UTF8.GetByteCount(Nonce);
        int length = 4 + 4 + nonceBytes + 1;
        if (length > into.Length)
            return -1;

        BinaryPrimitives.WriteInt32LittleEndian(into, Protocol);
        BinaryPrimitives.WriteInt32LittleEndian(into[4..], Port);
        Encoding.UTF8.GetBytes(Nonce, into[8..]);
        into[8 + nonceBytes] = 0;
        return length;
    }

    private static bool TryRead(ref ByteReader reader,
                               out DebugChannelInvitation invitation,
                               out string refusal)
    {
        invitation = default;

        int protocol = reader.ReadInt();
        int port     = reader.ReadInt();
        string nonce = reader.ReadString();

        if (reader.Overran)
        {
            refusal = "the invitation ended mid-field";
            return false;
        }

        // THE NO-ADDRESS RULE. See the remark above; do not relax this.
        if (reader.Remaining != 0)
        {
            refusal = $"the invitation carried {reader.Remaining} byte(s) past its three declared " +
                      "fields. Nothing may follow them — in particular no host and no address: " +
                      "the client dials the server it already joined, and a server does not get " +
                      "to say where that is";
            return false;
        }

        if (protocol != DebugChannelProtocol.Version)
        {
            refusal = $"protocol {protocol}, and this side speaks {DebugChannelProtocol.Version}";
            return false;
        }

        if (port is <= 0 or > 65535)
        {
            refusal = $"port {port} is not a port";
            return false;
        }

        if (!DebugChannelProtocol.IsWellFormedNonce(nonce))
        {
            refusal = "the nonce is not 32 hex characters";
            return false;
        }

        invitation = new DebugChannelInvitation(protocol, port, nonce);
        refusal = "";
        return true;
    }
}

/// <summary>
/// What a client carries across its own managed reload so the reloaded module
/// can redial the session the old one had, without a new invitation.
///
/// <para>
/// <b>WHY.</b> The server invites a client once, at its join, and the nonce is
/// single-use. <c>cs_cl_reload</c> tears the client's channel down with the old
/// module, so the reloaded module had nothing to dial with and
/// <c>/clients</c> went from [1] to []. The server hands each player slot's
/// session a RESUME TOKEN in its 101 (<see cref="DebugChannelProtocol.ResumeHeader"/>);
/// the client saves port, slot and token in the reload store as these bytes,
/// and the reloaded client presents slot and token at the upgrade.
/// </para>
///
/// <para>
/// <b>THE SAME NO-ADDRESS RULE AS THE INVITATION.</b> No host travels here
/// either: the reloaded client dials the host it would have dialled for an
/// invitation. The token is single-use, bound to its slot, and lives in the
/// listener that issued it, so a server reload (a new listener on a new port)
/// leaves the carried data dead and the server's fresh invitation wins.
/// </para>
/// </summary>
/// <param name="Protocol">The writer's <see cref="DebugChannelProtocol.Version"/>.</param>
/// <param name="Port">The listener's port, on the server the client already joined.</param>
/// <param name="Slot">The player slot the token was issued for.</param>
/// <param name="Token">Single-use, nonce-shaped.</param>
public readonly record struct DebugChannelResume(int Protocol, int Port, int Slot, string Token)
{
    /// <summary>The header value: <c>slot:token</c>.</summary>
    public static string HeaderValue(int slot, string token) => $"{slot}:{token}";

    /// <summary>Reads a header value; false for anything not <c>slot:token</c> with a positive slot and a well-formed token.</summary>
    public static bool TryReadHeader(string? value, out int slot, out string token)
    {
        slot = 0;
        token = "";

        if (value is null)
            return false;

        int colon = value.IndexOf(':');
        if (colon <= 0 || !int.TryParse(value.AsSpan(0, colon), System.Globalization.NumberStyles.None,
                                        System.Globalization.CultureInfo.InvariantCulture, out int read) || read <= 0)
            return false;

        string rest = value[(colon + 1)..];
        if (!DebugChannelProtocol.IsWellFormedNonce(rest))
            return false;

        slot = read;
        token = rest;
        return true;
    }

    /// <summary>The bytes the reload store carries: three little-endian ints and the token with its NUL.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[12 + Encoding.UTF8.GetByteCount(Token) + 1];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, Protocol);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Port);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Slot);
        Encoding.UTF8.GetBytes(Token, bytes.AsSpan(12));
        return bytes;
    }

    /// <summary>Reads carried bytes, or refuses them and says why. Exhausted exactly, as the invitation is.</summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out DebugChannelResume resume, out string refusal)
    {
        resume = default;

        var reader = new ByteReader(bytes);
        int protocol = reader.ReadInt();
        int port     = reader.ReadInt();
        int slot     = reader.ReadInt();
        string token = reader.ReadString();

        if (reader.Overran)
        {
            refusal = "the resume data ended mid-field";
            return false;
        }

        if (reader.Remaining != 0)
        {
            refusal = $"the resume data carried {reader.Remaining} byte(s) past its four fields";
            return false;
        }

        if (protocol != DebugChannelProtocol.Version)
        {
            refusal = $"protocol {protocol}, and this side speaks {DebugChannelProtocol.Version}";
            return false;
        }

        if (port is <= 0 or > 65535)
        {
            refusal = $"port {port} is not a port";
            return false;
        }

        if (slot <= 0)
        {
            refusal = $"slot {slot} is not a player slot";
            return false;
        }

        if (!DebugChannelProtocol.IsWellFormedNonce(token))
        {
            refusal = "the token is not 32 hex characters";
            return false;
        }

        resume = new DebugChannelResume(protocol, port, slot, token);
        refusal = "";
        return true;
    }
}

/// <summary>
/// Reads the invitation's fields with the rules the game's UserMessageReader
/// applied (little-endian ints, NUL-terminated UTF-8, an overrun flag rather
/// than an exception), so moving the codec here changed no refusal.
/// </summary>
internal ref struct ByteReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos;

    public readonly int Remaining => _data.Length - _pos;

    public bool Overran { get; private set; }

    public int ReadInt()
    {
        if (_pos + 4 > _data.Length) { Overran = true; return 0; }
        int v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    public string ReadString()
    {
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != 0)
            _pos++;

        var text = Encoding.UTF8.GetString(_data.Slice(start, _pos - start));

        if (_pos < _data.Length) _pos++;    // consume the terminator
        else                     Overran = true;

        return text;
    }
}

/// <summary>
/// EVERYTHING THE SERVER MAY ASK THE CLIENT TO DO. An enumeration, on purpose.
///
/// <para>
/// "Command and control" could have meant a pipe that runs console commands, and
/// that is exactly what this is not. The channel opens because a server invited
/// it, and a server that can run arbitrary commands in a client is a server that
/// owns the machine. What it can ask for instead is this list: four named
/// operations, each with a bounded effect on the client's own debug state, none
/// of which touches the game, the filesystem, or the console.
/// </para>
///
/// <para>
/// If arbitrary command execution is wanted later, it is a SEPARATE and
/// SEPARATELY-FLAGGED decision — a different opt-in from
/// <see cref="DebugChannelProtocol.ClientParam"/>, made by somebody who has
/// weighed it. It is not an extra case in this enum.
/// </para>
/// </summary>
public enum DebugChannelCommand : byte
{
    /// <summary>Answered with <see cref="DebugChannelMessage.Pong"/>. Proves the channel is live.</summary>
    Ping = 1,

    /// <summary>Clears the client's prediction ring, for a reader who wants a clean window.</summary>
    ResetPredictionRing = 2,

    /// <summary>Sends whatever the ring holds now rather than waiting for the next poll.</summary>
    SendPredictionNow = 3,

    /// <summary>Asks the client to close the channel. It does, and does not redial.</summary>
    Disconnect = 4,
}

/// <summary>The kind byte every framed message on the channel begins with.</summary>
public enum DebugChannelMessage : byte
{
    /// <summary>Client to server, first message: which protocol, which build.</summary>
    Hello = 1,

    /// <summary>Client to server: a batch of prediction error records.</summary>
    Prediction = 2,

    /// <summary>Server to client: one <see cref="DebugChannelCommand"/>.</summary>
    Command = 3,

    /// <summary>Client to server, in answer to <see cref="DebugChannelCommand.Ping"/>.</summary>
    Pong = 4,

    /// <summary>
    /// Server to client: one read-only question about the client's own state,
    /// from the closed set <see cref="DebugChannelQuery"/>, under a request id
    /// (plan_side_isolation §2.6: the server's dev API proxies /client/{slot}/...).
    /// </summary>
    Query = 5,

    /// <summary>Client to server: the answer to one <see cref="Query"/>, by its request id.</summary>
    Reply = 6,
}

/// <summary>
/// The questions a server may ask a client over the channel. A CLOSED SET, for
/// the reason <see cref="DebugChannelCommand"/> is one: every case is a
/// read of the client's own state, and there is no case that runs anything.
/// </summary>
public enum DebugChannelQuery : byte
{
    /// <summary>The client's build stamp and its crossing counters, as JSON.</summary>
    Status = 1,

    /// <summary>The client's own prediction report: its ring's totals and records (P3b).</summary>
    Prediction = 2,

    /// <summary>The client's frame counters, frame times over the last second and pose (plan_client_novalve §4.4).</summary>
    Frame = 3,

    /// <summary>The client's own entity list; the argument filters by class (plan_client_novalve §4.4).</summary>
    Entities = 4,

    /// <summary>The client's recv tables and its send/recv agreement (plan_client_novalve §4.4).</summary>
    NetTables = 5,

    /// <summary>One client entity by index, the argument: its [NetState] blocks and interpolated values (plan_client_novalve §4.4).</summary>
    Entity = 6,

    /// <summary>The client's last managed reload: sections, participants, the leak arm (plan_client_novalve §4.4).</summary>
    Reload = 7,

    /// <summary>A WRITE: queue a client reload at the next frame boundary, as cs_cl_reload does. Cheat-gated by the client.</summary>
    ReloadTrigger = 8,

    /// <summary>What CNetGraphPanel drew: the net channel, its flows and recent packets; the argument is how many (plan_client_novalve §4.4).</summary>
    NetGraph = 9,

    /// <summary>The not-shipped and refused rows raised this run (plan_client_novalve §4.4).</summary>
    Absences = 10,

    /// <summary>The UI's last frame: screen, cost, the draw ledger; argument "1" adds the render list as SVG (plan_client_ui U2).</summary>
    Ui = 11,

    /// <summary>The fonts the UI made, with the surface's metrics and ASCII advances (plan_client_ui U2).</summary>
    UiFonts = 12,

    /// <summary>The UI's menu layer: as Ui, with the open screens and whether the menu root has the input (plan_client_ui U3).</summary>
    UiMenu = 13,

    /// <summary>A WRITE: one input event into the menu layer, "kind,x,y,code,char,delta" (plan_client_ui U3).</summary>
    UiEvent = 14,

    /// <summary>The material proxies: registered names, binds per frame per name, last output per bound material; the argument filters (plan_client_novalve §4.4).</summary>
    Proxies = 15,
    /// <summary>The debug camera's state; argument "0"/"1"/"2" is a WRITE of cs_cl_overview, cheat-gated by the client (lane camproto).</summary>
    ViewCam = 16,

    /// <summary>The render plan: the path in force, the last plan's ops, the last same-run A/B (plan_client_novalve C7).</summary>
    Render = 17,

    /// <summary>A WRITE (sv_cheats): record the next frame from stock and the one after from the plan; "1" drops one op from the plan frame (C7).</summary>
    RenderCompare = 18,

    /// <summary>The CNewParticleEffect instances the client started and the definition reads (plan_client_novalve §4.4).</summary>
    Particles = 19,
    /// <summary>The input: key states, the mouse's accumulators, the sensitivity chain, the last 16 commands, the controller mapping (lane c6-input).</summary>
    Input = 20,

    /// <summary>A WRITE, cheats only: "dx,dy" mouse delta or "+button" for the next command (lane c6-input).</summary>
    InputInject = 21,
}

/// <summary>
/// The framing, both ways.
///
/// <para>
/// BINARY AND HAND-ROLLED, for the reason the rest of this tree's wire code is:
/// a serializer would pull reflection and a dependency into the CLIENT's
/// assembly, which is loaded in every game whether or not anybody asked for a
/// debug channel. The records it carries are flat, and a round trip is a fact a
/// test can hold.
/// </para>
///
/// <para>
/// EVERY LENGTH IS BOUNDED ON READ. These bytes arrive over a socket. A count
/// read off the wire and used to size an allocation is how a debug tool becomes
/// a way to exhaust a process' memory, so each one is clamped against the same
/// constant the writer honours.
/// </para>
/// </summary>
public static class DebugChannelCodec
{
    /// <summary>Records in one batch. A client polling four times a second never fills it.</summary>
    public const int MaxRecordsPerBatch = 64;

    /// <summary>Matches what a writer can produce — <see cref="PredictionErrorAnalysis.MaxFieldsPerRecord"/>.</summary>
    public const int MaxFieldsPerRecord = 32;

    /// <summary>A vector is three; nothing this protocol carries has more.</summary>
    public const int MaxComponentsPerField = 3;

    /// <summary>Longest string on the wire — a class name or a field name.</summary>
    public const int MaxStringBytes = 256;

    /// <summary>A whole frame. Sized so a full batch of full records fits with room over.</summary>
    public const int MaxFrameBytes = 1024 * 1024;

    //-------------------------------------------------------------------------
    // Writing.
    //-------------------------------------------------------------------------

    /// <summary>The client's opening message: which protocol it speaks and what it was built from.</summary>
    public static byte[] Hello(string build)
    {
        var bytes = new List<byte>(64) { (byte)DebugChannelMessage.Hello };
        WriteInt(bytes, DebugChannelProtocol.Version);
        WriteString(bytes, build);
        return bytes.ToArray();
    }

    /// <summary>One command, which is one byte of payload and no more.</summary>
    public static byte[] Command(DebugChannelCommand command)
        => new[] { (byte)DebugChannelMessage.Command, (byte)command };

    /// <summary>The answer to a ping.</summary>
    public static byte[] Pong() => new[] { (byte)DebugChannelMessage.Pong };

    /// <summary>
    /// One question, under a request id the reply carries back, with its
    /// argument (an entity index, a class filter) when it has one. A question
    /// without one writes the same bytes it always did: the argument is a
    /// length-prefixed string after the kind byte, present only when non-empty,
    /// and cut to <see cref="MaxStringBytes"/>.
    /// </summary>
    public static byte[] Query(int requestId, DebugChannelQuery query, string? argument = null)
    {
        var bytes = new List<byte>(6) { (byte)DebugChannelMessage.Query };
        WriteInt(bytes, requestId);
        bytes.Add((byte)query);
        if (!string.IsNullOrEmpty(argument))
            WriteString(bytes, argument);
        return bytes.ToArray();
    }

    /// <summary>
    /// The questions a client answers: the closed set, enforced where the
    /// bytes are read (<see cref="TryReadQuery"/>). A kind the enum names but
    /// this list does not is refused like an unknown byte.
    /// </summary>
    public static readonly IReadOnlyList<DebugChannelQuery> Answerable =
    [
        DebugChannelQuery.Status,
        DebugChannelQuery.Prediction,
        DebugChannelQuery.Frame,
        DebugChannelQuery.Entities,
        DebugChannelQuery.NetTables,
        DebugChannelQuery.Entity,
        DebugChannelQuery.Reload,
        DebugChannelQuery.ReloadTrigger,
        DebugChannelQuery.NetGraph,
        DebugChannelQuery.Absences,
        DebugChannelQuery.Ui,
        DebugChannelQuery.UiFonts,
        DebugChannelQuery.UiMenu,
        DebugChannelQuery.UiEvent,
        DebugChannelQuery.Proxies,
        DebugChannelQuery.ViewCam,
        DebugChannelQuery.Render,
        DebugChannelQuery.RenderCompare,
        DebugChannelQuery.Particles,
        DebugChannelQuery.Input,
        DebugChannelQuery.InputInject,
    ];

    /// <summary>
    /// One answer: the request id, an HTTP-style status, then the body as the
    /// rest of the frame (UTF-8), cut to fit <see cref="MaxFrameBytes"/>.
    /// </summary>
    public static byte[] Reply(int requestId, int status, string body)
    {
        var bytes = new List<byte>(16) { (byte)DebugChannelMessage.Reply };
        WriteInt(bytes, requestId);
        WriteInt(bytes, status);

        byte[] utf8 = Encoding.UTF8.GetBytes(body);
        int room = MaxFrameBytes - bytes.Count;
        bytes.AddRange(utf8.Length <= room ? utf8 : utf8[..room]);
        return bytes.ToArray();
    }

    /// <summary>
    /// A batch of records. Writes no more than
    /// <see cref="MaxRecordsPerBatch"/>; the caller sends the rest next time,
    /// which is what the sequence numbers are for.
    /// </summary>
    public static byte[] PredictionBatch(IReadOnlyList<WirePredictionRecord> records)
    {
        int count = Math.Min(records.Count, MaxRecordsPerBatch);

        var bytes = new List<byte>(256) { (byte)DebugChannelMessage.Prediction };
        WriteInt(bytes, count);

        for (int i = 0; i < count; i++)
            WriteRecord(bytes, records[i]);

        return bytes.ToArray();
    }

    private static void WriteRecord(List<byte> bytes, WirePredictionRecord record)
    {
        WriteLong(bytes, record.Sequence);
        WriteInt(bytes, record.Command);
        WriteInt(bytes, unchecked((int)record.TypeId));
        WriteString(bytes, record.ClassName);
        WriteInt(bytes, record.BlobBytes);
        WriteInt(bytes, record.StandingBytes);
        WriteInt(bytes, record.FirstDiffOffset);
        WriteInt(bytes, record.ToleratedFields);
        WriteLong(bytes, record.TimeUtc.Ticks);

        int fields = Math.Min(record.Fields.Count, MaxFieldsPerRecord);
        WriteInt(bytes, fields);

        for (int i = 0; i < fields; i++)
        {
            var field = record.Fields[i];

            WriteString(bytes, field.Name);
            WriteInt(bytes, field.Kind);
            WriteInt(bytes, field.Offset);
            WriteInt(bytes, field.Bytes);
            WriteInt(bytes, BitConverter.SingleToInt32Bits(field.Tolerance));
            bytes.Add(field.Tolerated ? (byte)1 : (byte)0);
            WriteInt(bytes, field.DifferingBytes);

            int components = Math.Min(field.Components.Count, MaxComponentsPerField);
            WriteInt(bytes, components);

            for (int c = 0; c < components; c++)
            {
                var component = field.Components[c];
                WriteLong(bytes, BitConverter.DoubleToInt64Bits(component.Predicted));
                WriteLong(bytes, BitConverter.DoubleToInt64Bits(component.Server));
                WriteLong(bytes, BitConverter.DoubleToInt64Bits(component.Delta));
            }
        }
    }

    //-------------------------------------------------------------------------
    // Reading. Nothing here throws on bad input: a malformed frame from a socket
    // is an ordinary event and the answer to it is to drop the frame and say so,
    // not to take down the side that received it.
    //-------------------------------------------------------------------------

    /// <summary>The frame's kind, or null when there is not even one byte.</summary>
    public static DebugChannelMessage? KindOf(ReadOnlySpan<byte> frame)
        => frame.Length == 0 ? null : (DebugChannelMessage)frame[0];

    /// <summary>Reads a <see cref="Hello"/>: its protocol number and its build string.</summary>
    public static bool TryReadHello(ReadOnlySpan<byte> frame, out int protocol, out string build)
    {
        protocol = 0;
        build = "";

        int at = 1;

        return KindOf(frame) == DebugChannelMessage.Hello
            && TryReadInt(frame, ref at, out protocol)
            && TryReadString(frame, ref at, out build);
    }

    /// <summary>
    /// Reads a <see cref="Command"/>.
    ///
    /// <para>
    /// A VALUE THAT IS NOT ONE OF THE DECLARED OPERATIONS IS REFUSED, rather
    /// than passed through for a switch to ignore. The bound on what a server
    /// may ask for is only a bound if it is enforced where the bytes are
    /// understood.
    /// </para>
    /// </summary>
    public static bool TryReadQuery(ReadOnlySpan<byte> frame, out int requestId, out DebugChannelQuery query,
                                    out string argument)
    {
        query = default;
        argument = "";
        int at = 1;

        if (KindOf(frame) != DebugChannelMessage.Query
            || !TryReadInt(frame, ref at, out requestId)
            || at + 1 > frame.Length)
        {
            requestId = 0;
            return false;
        }

        var candidate = (DebugChannelQuery)frame[at++];

        // The argument, when the frame carries one, is exactly one bounded
        // string and nothing after it.
        if (at != frame.Length && (!TryReadString(frame, ref at, out argument) || at != frame.Length))
        {
            requestId = 0;
            argument = "";
            return false;
        }

        if (!Answerable.Contains(candidate))
            return false;

        query = candidate;
        return true;
    }

    public static bool TryReadReply(ReadOnlySpan<byte> frame, out int requestId, out int status, out string body)
    {
        status = 0;
        body = "";
        int at = 1;

        if (KindOf(frame) != DebugChannelMessage.Reply
            || !TryReadInt(frame, ref at, out requestId)
            || !TryReadInt(frame, ref at, out status))
        {
            requestId = 0;
            return false;
        }

        body = Encoding.UTF8.GetString(frame[at..]);
        return true;
    }

    public static bool TryReadCommand(ReadOnlySpan<byte> frame, out DebugChannelCommand command)
    {
        command = default;

        if (KindOf(frame) != DebugChannelMessage.Command || frame.Length < 2)
            return false;

        var candidate = (DebugChannelCommand)frame[1];

        if (candidate is not (DebugChannelCommand.Ping
                           or DebugChannelCommand.ResetPredictionRing
                           or DebugChannelCommand.SendPredictionNow
                           or DebugChannelCommand.Disconnect))
        {
            return false;
        }

        command = candidate;
        return true;
    }

    /// <summary>Reads a batch of records, or refuses the whole frame.</summary>
    public static bool TryReadPredictionBatch(ReadOnlySpan<byte> frame,
                                              out List<WirePredictionRecord> records)
    {
        records = new List<WirePredictionRecord>();

        if (KindOf(frame) != DebugChannelMessage.Prediction)
            return false;

        int at = 1;

        if (!TryReadInt(frame, ref at, out int count) || count < 0 || count > MaxRecordsPerBatch)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!TryReadRecord(frame, ref at, out var record))
            {
                records.Clear();
                return false;
            }

            records.Add(record);
        }

        return true;
    }

    private static bool TryReadRecord(ReadOnlySpan<byte> frame, ref int at,
                                      out WirePredictionRecord record)
    {
        record = null!;

        if (!TryReadLong(frame, ref at, out long sequence)) return false;
        if (!TryReadInt(frame, ref at, out int command)) return false;
        if (!TryReadInt(frame, ref at, out int typeId)) return false;
        if (!TryReadString(frame, ref at, out string className)) return false;
        if (!TryReadInt(frame, ref at, out int blobBytes)) return false;
        if (!TryReadInt(frame, ref at, out int standingBytes)) return false;
        if (!TryReadInt(frame, ref at, out int firstDiff)) return false;
        if (!TryReadInt(frame, ref at, out int toleratedFields)) return false;
        if (!TryReadLong(frame, ref at, out long ticks)) return false;
        if (!TryReadInt(frame, ref at, out int fieldCount)) return false;

        if (fieldCount < 0 || fieldCount > MaxFieldsPerRecord)
            return false;

        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return false;

        var fields = new List<WirePredictionField>(fieldCount);

        for (int i = 0; i < fieldCount; i++)
        {
            if (!TryReadString(frame, ref at, out string name)) return false;
            if (!TryReadInt(frame, ref at, out int kind)) return false;
            if (!TryReadInt(frame, ref at, out int offset)) return false;
            if (!TryReadInt(frame, ref at, out int bytes)) return false;
            if (!TryReadInt(frame, ref at, out int toleranceBits)) return false;

            if (at >= frame.Length) return false;
            bool tolerated = frame[at++] != 0;

            if (!TryReadInt(frame, ref at, out int differing)) return false;
            if (!TryReadInt(frame, ref at, out int componentCount)) return false;

            if (componentCount < 0 || componentCount > MaxComponentsPerField)
                return false;

            var components = new WirePredictionComponent[componentCount];

            for (int c = 0; c < componentCount; c++)
            {
                if (!TryReadLong(frame, ref at, out long predicted)) return false;
                if (!TryReadLong(frame, ref at, out long server)) return false;
                if (!TryReadLong(frame, ref at, out long delta)) return false;

                components[c] = new WirePredictionComponent(
                    BitConverter.Int64BitsToDouble(predicted),
                    BitConverter.Int64BitsToDouble(server),
                    BitConverter.Int64BitsToDouble(delta));
            }

            fields.Add(new WirePredictionField(name, kind, offset, bytes,
                BitConverter.Int32BitsToSingle(toleranceBits), tolerated, differing, components));
        }

        record = new WirePredictionRecord(sequence, command, unchecked((uint)typeId), className,
            blobBytes, standingBytes, firstDiff, toleratedFields,
            new DateTime(ticks, DateTimeKind.Utc), fields);

        return true;
    }

    //-------------------------------------------------------------------------
    // The primitives.
    //-------------------------------------------------------------------------
    private static void WriteInt(List<byte> bytes, int value)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(four, value);
        bytes.AddRange(four);
    }

    private static void WriteLong(List<byte> bytes, long value)
    {
        Span<byte> eight = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(eight, value);
        bytes.AddRange(eight);
    }

    private static void WriteString(List<byte> bytes, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);

        if (utf8.Length > MaxStringBytes)
            utf8 = utf8[..MaxStringBytes];

        WriteInt(bytes, utf8.Length);
        bytes.AddRange(utf8);
    }

    private static bool TryReadInt(ReadOnlySpan<byte> frame, ref int at, out int value)
    {
        value = 0;

        if (at + 4 > frame.Length)
            return false;

        value = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(at, 4));
        at += 4;
        return true;
    }

    private static bool TryReadLong(ReadOnlySpan<byte> frame, ref int at, out long value)
    {
        value = 0;

        if (at + 8 > frame.Length)
            return false;

        value = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(at, 8));
        at += 8;
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<byte> frame, ref int at, out string value)
    {
        value = "";

        if (!TryReadInt(frame, ref at, out int length))
            return false;

        // THE BOUND IS CHECKED BEFORE THE SLICE, not after. A length off the
        // wire is not a fact about the sender's intentions.
        if (length < 0 || length > MaxStringBytes || at + length > frame.Length)
            return false;

        value = Encoding.UTF8.GetString(frame.Slice(at, length));
        at += length;
        return true;
    }
}
