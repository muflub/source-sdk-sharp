using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientNetGraph"/> over the engine's net channel
/// (plan_client_novalve §4.4): the reader decides which packets (the newest
/// <c>frames</c> incoming, by sequence number) and how to name the groups.
/// </summary>
public sealed class ClientDebugNetGraph(IClientNetChannel channel) : IDebugClientNetGraph
{
    /// <summary>The most packets one answer carries: the panel's own history.</summary>
    public const int MaxFrames = 64;

    /// <summary>INetChannelInfo's message groups by number, in its order (13 is the total).</summary>
    private static string GroupName(int group) => group switch
    {
        0 => "generic", 1 => "localPlayer", 2 => "otherPlayers", 3 => "entities", 4 => "sounds",
        5 => "events", 6 => "userMessages", 7 => "entMessages", 8 => "voice", 9 => "stringTable",
        10 => "move", 11 => "stringCmd", 12 => "signon", _ => $"group{group}",
    };

    private const int Incoming = 1;

    public DebugClientNetGraph Read(int frames)
    {
        if (channel.Channel() is not { } c)
            return new DebugClientNetGraph(false, 0, 0, false, false, 0, 0, 0, 0, [], []);

        var flows = new List<DebugClientNetFlow>(2);
        foreach (var (flow, name) in new[] { (0, "outgoing"), (Incoming, "incoming") })
        {
            if (channel.Flow(flow) is { } f)
                flows.Add(new DebugClientNetFlow(name, f.Latency * 1000f, f.AvgLatency * 1000f, f.AvgLoss, f.AvgChoke,
                                                 f.AvgData, f.AvgPackets, f.TotalData, f.Sequence));
        }

        var packets = new List<DebugClientNetPacket>();
        if (channel.Flow(Incoming) is { } incoming)
        {
            int count = Math.Clamp(frames, 0, Math.Min(MaxFrames, Math.Max(c.BufferSize, 0)));
            for (int i = 0; i < count; i++)
            {
                int sequence = incoming.Sequence - i;
                if (sequence < 0 || channel.Packet(Incoming, sequence) is not { } p)
                    break;
                var groups = new List<DebugCount>();
                for (int g = 0; g < p.GroupBytes.Length - 1; g++)
                {
                    if (p.GroupBytes[g] != 0)
                        groups.Add(new DebugCount(GroupName(g), p.GroupBytes[g]));
                }
                packets.Add(new DebugClientNetPacket(sequence, p.Valid, p.Time, p.CommandInterpolation,
                                                     p.ResponseLatencyMs, p.Choke, p.GroupBytes[^1], groups));
            }
        }

        return new DebugClientNetGraph(true, c.TimeConnected, c.DataRate, c.Loopback, c.TimingOut,
                                       c.SinceLastReceived * 1000f, c.RemoteFrameTime * 1000f,
                                       c.RemoteFrameTimeDeviation * 1000f, c.InterpAmount * 1000f, flows, packets);
    }
}

/// <summary><see cref="IDebugClientAbsences"/> over the not-shipped and refusal ledgers.</summary>
public sealed class ClientDebugAbsences(IClientDiagnosticsLedgers ledgers) : IDebugClientAbsences
{
    public DebugClientAbsences Read()
    {
        var (notShipped, shippedRows) = ledgers.NotShipped();
        var (refused, refusedRows) = ledgers.Refusals();
        var rows = shippedRows.Select(r => new DebugClientAbsence("not-shipped", r.Name, r.Reason, r.Count))
                              .Concat(refusedRows.Select(r => new DebugClientAbsence("refused", r.Name, r.Reason, r.Count)))
                              .ToArray();
        return new DebugClientAbsences(notShipped, refused, rows);
    }
}
