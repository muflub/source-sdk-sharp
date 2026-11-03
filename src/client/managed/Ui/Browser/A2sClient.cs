using SourceSharp.Client.Ui;

using System.Net;
using System.Net.Sockets;

namespace SourceSharp.Client.Vgui;

/// <summary>Datagrams to and from servers: a UDP socket in the game, a fake in facts.</summary>
public interface IDatagramTransport
{
    void Send(ServerAddress to, ReadOnlySpan<byte> data);

    /// <summary>To every host on the local network, at <paramref name="port"/>.</summary>
    void Broadcast(ushort port, ReadOnlySpan<byte> data);

    /// <summary>One waiting datagram into <paramref name="buffer"/>: its length, or -1 when none waits.</summary>
    int Receive(Span<byte> buffer, out ServerAddress from);
}

/// <summary>A non-blocking UDP socket, opened on first use; every failure is a datagram not sent or not received.</summary>
public sealed class UdpDatagramTransport : IDatagramTransport, IDisposable
{
    private Socket? _socket;

    private Socket? Socket()
    {
        if (_socket is not null)
            return _socket;
        try
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false, EnableBroadcast = true };
            s.Bind(new IPEndPoint(IPAddress.Any, 0));
            _socket = s;
        }
        catch (SocketException)
        {
        }
        return _socket;
    }

    public void Send(ServerAddress to, ReadOnlySpan<byte> data)
        => SendTo(new IPEndPoint(new IPAddress(ToNetwork(to.Ip)), to.Port), data);

    public void Broadcast(ushort port, ReadOnlySpan<byte> data) => SendTo(new IPEndPoint(IPAddress.Broadcast, port), data);

    private void SendTo(IPEndPoint to, ReadOnlySpan<byte> data)
    {
        try
        {
            Socket()?.SendTo(data, to);
        }
        catch (SocketException)
        {
        }
    }

    public int Receive(Span<byte> buffer, out ServerAddress from)
    {
        from = default;
        var s = Socket();
        if (s is null || s.Available <= 0)
            return -1;
        try
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int n = s.ReceiveFrom(buffer, SocketFlags.None, ref remote);
            if (remote is IPEndPoint ip && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                Span<byte> b = stackalloc byte[4];
                ip.Address.TryWriteBytes(b, out _);
                from = new ServerAddress((uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]), (ushort)ip.Port);
            }
            return n;
        }
        catch (SocketException)
        {
            return -1;
        }
    }

    // IPAddress(long) takes the address in network order as stored in memory.
    private static long ToNetwork(uint ip)
        => (long)(uint)((ip >> 24) | ((ip >> 8) & 0xFF00) | ((ip << 8) & 0xFF0000) | (ip << 24));

    public void Dispose() => _socket?.Dispose();
}

/// <summary>
/// The browser's queries (stock's ServerBrowser.so asked through Steam; this
/// speaks A2S itself): A2S_INFO to one server or broadcast on the LAN ports,
/// A2S_PLAYER to one, each challenge answered by sending again with it.
/// Polled once a frame while the browser is open; no thread. A server that
/// has not answered in <see cref="TimeoutSeconds"/> is given up.
/// </summary>
public sealed class A2sClient(IDatagramTransport transport, TimeProvider time)
{
    public const double TimeoutSeconds = 2.5;

    /// <summary>Stock's LAN tab scanned these query ports.</summary>
    public const ushort LanFirstPort = 27015, LanLastPort = 27020;

    private enum Want : byte { Info = 1, Players = 2 }

    private readonly Dictionary<ServerAddress, (Want Want, long SentAt)> _pending = [];
    private readonly byte[] _buffer = new byte[1400];
    private long _broadcastAt = long.MinValue;

    /// <summary>A server answered A2S_INFO, with its ping in ms.</summary>
    public event Action<ServerAddress, A2sInfo, int>? InfoReceived;

    /// <summary>A server answered A2S_PLAYER.</summary>
    public event Action<ServerAddress, IReadOnlyList<ServerPlayer>>? PlayersReceived;

    /// <summary>A queried server did not answer in time.</summary>
    public event Action<ServerAddress>? TimedOut;

    /// <summary>Servers asked and not yet answered.</summary>
    public int Pending => _pending.Count;

    /// <summary>Whether a LAN broadcast is still within its answer window.</summary>
    public bool Broadcasting => _broadcastAt != long.MinValue && Elapsed(_broadcastAt) < TimeoutSeconds;

    public void QueryInfo(ServerAddress server)
    {
        Ask(server, Want.Info);
        transport.Send(server, A2s.InfoRequest());
    }

    public void QueryPlayers(ServerAddress server)
    {
        Ask(server, Want.Players);
        transport.Send(server, A2s.PlayerRequest(A2s.NoChallenge));
    }

    /// <summary>A2S_INFO to the whole LAN on every port stock's LAN tab scanned.</summary>
    public void BroadcastInfo()
    {
        _broadcastAt = time.GetTimestamp();
        byte[] request = A2s.InfoRequest();
        for (int port = LanFirstPort; port <= LanLastPort; port++)
            transport.Broadcast((ushort)port, request);
    }

    /// <summary>Forgets every query (a refresh starting over, the tab changing).</summary>
    public void Cancel()
    {
        _pending.Clear();
        _broadcastAt = long.MinValue;
    }

    public void Poll()
    {
        int n;
        while ((n = transport.Receive(_buffer, out var from)) >= 0)
            Take(from, _buffer.AsSpan(0, n));

        if (_pending.Count == 0)
            return;
        List<ServerAddress>? late = null;
        foreach (var (server, p) in _pending)
            if (Elapsed(p.SentAt) >= TimeoutSeconds)
                (late ??= []).Add(server);
        if (late is null)
            return;
        foreach (var server in late)
        {
            _pending.Remove(server);
            TimedOut?.Invoke(server);
        }
    }

    private void Take(ServerAddress from, ReadOnlySpan<byte> data)
    {
        var reply = A2s.Parse(data, from.Port);
        _pending.TryGetValue(from, out var p);
        switch (reply.Kind)
        {
            case A2sReplyKind.Challenge:
                // The challenge is for whatever is waiting on this server; a
                // broadcast answer with one is a LAN server asking for it.
                if ((p.Want & Want.Info) != 0 || (p.Want == 0 && Broadcasting))
                    transport.Send(from, A2s.InfoRequest(reply.Challenge));
                if ((p.Want & Want.Players) != 0)
                    transport.Send(from, A2s.PlayerRequest(reply.Challenge));
                break;
            case A2sReplyKind.Info when reply.Info is { } info:
                bool asked = (p.Want & Want.Info) != 0;
                if (!asked && !Broadcasting)
                    return;    // nobody asked, or the broadcast's window has closed
                long sent = asked ? p.SentAt : _broadcastAt;
                Answered(from, Want.Info);
                InfoReceived?.Invoke(from, info, (int)Math.Round(Elapsed(sent) * 1000));
                break;
            case A2sReplyKind.Players:
                if ((p.Want & Want.Players) == 0)
                    return;
                Answered(from, Want.Players);
                PlayersReceived?.Invoke(from, reply.Players);
                break;
        }
    }

    private void Ask(ServerAddress server, Want want)
    {
        _pending.TryGetValue(server, out var p);
        _pending[server] = (p.Want | want, time.GetTimestamp());
    }

    private void Answered(ServerAddress server, Want want)
    {
        if (!_pending.TryGetValue(server, out var p))
            return;
        var left = p.Want & ~want;
        if (left == 0)
            _pending.Remove(server);
        else
            _pending[server] = (left, p.SentAt);
    }

    private double Elapsed(long since) => time.GetElapsedTime(since).TotalSeconds;
}
