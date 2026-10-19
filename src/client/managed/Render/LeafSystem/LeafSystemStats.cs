using System.Diagnostics;
using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.LeafSystem;

/// <summary>
/// The leaf system's cost, the L3 instrument of docs/plans/c7_leaf_system.md:
/// each call the forwarder makes, by kind, with the managed time it took (the
/// native batches it made inside included), and the batches it asked native
/// (<see cref="LeafCallCounts"/>). PreRender runs once a frame, so its count is
/// the frames the interval covers. Read and reset by <c>cs_cl_leaf_stats</c>.
/// </summary>
public sealed class LeafSystemStats
{
    public enum Kind
    {
        PreRender,
        TranslucentLeaf,
        BuildList,
        ViewModels,
        // The forwarder's queue handed over (one call a batch), or a
        // RenderableChanged that reached managed on its own.
        Queued,
        AddOrRemove,
        Shadows,
        Detail,
        Other,
    }

    private readonly long[] _calls = new long[Enum.GetValues<Kind>().Length];
    private readonly long[] _ticks = new long[Enum.GetValues<Kind>().Length];

    /// <summary>The native batches' counts, when the native side is the forwarder library's.</summary>
    public LeafCallCounts? Outbound { get; set; }

    public long Calls(Kind kind) => _calls[(int)kind];

    public double Milliseconds(Kind kind) => _ticks[(int)kind] * 1000.0 / Stopwatch.Frequency;

    public void Add(Kind kind, long startTicks)
    {
        _calls[(int)kind]++;
        _ticks[(int)kind] += Stopwatch.GetTimestamp() - startTicks;
    }

    public void Reset()
    {
        Array.Clear(_calls);
        Array.Clear(_ticks);
        Outbound?.Reset();
    }

    public string Report()
    {
        long frames = Math.Max(1, Calls(Kind.PreRender));
        var sb = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        sb.Append(c, $"[SourceSharp/Client] leaf system: {Calls(Kind.PreRender)} frame(s) (PreRender calls)\n");
        double totalMs = 0;
        foreach (Kind kind in Enum.GetValues<Kind>())
        {
            totalMs += Milliseconds(kind);
            sb.Append(c, $"[SourceSharp/Client]   in   {kind,-18} {Calls(kind),9} calls  {(double)Calls(kind) / frames,9:F2}/frame  {Milliseconds(kind) / frames * 1000.0,9:F1} us/frame\n");
        }
        sb.Append(c, $"[SourceSharp/Client]   in   total managed           {totalMs / frames * 1000.0,9:F1} us/frame\n");
        if (Outbound is { } outbound)
        {
            foreach (LeafCall call in Enum.GetValues<LeafCall>())
                sb.Append(c, $"[SourceSharp/Client]   out  {call,-18} {outbound.Calls(call),9} batches {(double)outbound.Calls(call) / frames,9:F2}/frame  {(double)outbound.Items(call) / frames,9:F1} items/frame\n");
        }
        return sb.ToString();
    }
}

/// <summary><c>cs_cl_leaf_stats [reset]</c>: the leaf system's calls, time and native batches per frame since the last reset.</summary>
[ConCommand("cs_cl_leaf_stats", Help = "cs_cl_leaf_stats [reset]: the client leaf system's calls, managed time and native batches per frame since the last reset.")]
public sealed class LeafSystemStatsCommand(ILog log, LeafSystemStats stats) : IConCommand
{
    public void Execute(string args)
    {
        log.Msg(stats.Report());
        if (args.Trim() == "reset")
        {
            stats.Reset();
            log.Msg("[SourceSharp/Client] leaf system: counters reset\n");
        }
    }
}
