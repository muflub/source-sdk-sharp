namespace SourceSharp.Client.Interface;

/// <summary>One flow of the net channel: what CNetGraphPanel drew for it.</summary>
public sealed record DebugClientNetFlow(
    string Name, float LatencyMs, float AvgLatencyMs, float AvgLoss, float AvgChoke, float AvgBytesPerSecond,
    float AvgPacketsPerSecond, int TotalBytes, int Sequence);

/// <summary>One incoming packet: whether it arrived, its bytes by message group, and its timing.</summary>
public sealed record DebugClientNetPacket(
    int Sequence, bool Valid, float Time, float CommandInterpolation, int ResponseLatencyMs, int Choke,
    int TotalBytes, IReadOnlyList<DebugCount> Groups);

/// <summary>
/// What <c>CNetGraphPanel</c> showed, as <c>/client/{slot}/netgraph</c>
/// reports it (plan_client_novalve §4.4). Connected is false with no channel,
/// and everything else is then empty.
/// </summary>
public sealed record DebugClientNetGraph(
    bool Connected, float TimeConnected, int DataRate, bool Loopback, bool TimingOut, float SinceLastReceivedMs,
    float RemoteFrameTimeMs, float RemoteFrameTimeDeviationMs, float InterpMs,
    IReadOnlyList<DebugClientNetFlow> Flows, IReadOnlyList<DebugClientNetPacket> Packets);

/// <summary>The client's net channel (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientNetGraph
{
    /// <summary>The channel, both flows, and up to <paramref name="frames"/> incoming packets, newest first.</summary>
    DebugClientNetGraph Read(int frames);
}

/// <summary>One absence: a capability this build does not ship, or a method it refuses, and how often it was reached.</summary>
/// <param name="Ledger">"not-shipped" or "refused".</param>
public sealed record DebugClientAbsence(string Ledger, string Name, string Reason, int Count);

/// <summary>
/// The absences raised this run, as <c>/client/{slot}/absences</c> reports
/// them (plan_client_novalve §4.4). A row is a finding; zero is the evidence.
/// </summary>
public sealed record DebugClientAbsences(int NotShippedTotal, int RefusedTotal, IReadOnlyList<DebugClientAbsence> Rows);

/// <summary>The client's absence ledgers (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientAbsences
{
    DebugClientAbsences Read();
}
