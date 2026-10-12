using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>
/// cs_cl_rope_spans: is there a rope behind every span, and is it hanging?
/// The tally lies the way tallies do, so it prints the shape too: how far the
/// end nodes are from where the span was told to hang (both near zero is the
/// lock working), the chord, and the sag (zero is a taut rope, the failure the
/// initial hang exists to prevent).
/// </summary>
[ConCommand("cs_cl_rope_spans",
            Help = "the managed rope spans, the ropes behind them, and the owners in front of them")]
public sealed class RopeSpansCommand(ILog log, IClientRopes ropes) : IConCommand
{
    public void Execute(string args)
    {
        IReadOnlyList<RopeSpanReport> rows = ropes.Report();

        log.Msg($"[SourceSharp] cs_cl_rope_spans: {rows.Count} span(s), {ropes.Steps} step(s), " +
                $"{ropes.Strips} strip(s) drawn, {ropes.Configures} update(s)\n");

        for (int i = 0; i < rows.Count; i++)
        {
            RopeSpanReport r = rows[i];
            log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  [{i}] {r.Owner} {r.Nodes} nodes {(r.Initted ? "hung" : "NOT SIMULATING YET")}  " +
                $"start ({r.Start.X:F1} {r.Start.Y:F1} {r.Start.Z:F1}) end ({r.End.X:F1} {r.End.Y:F1} {r.End.Z:F1})  " +
                $"e0 {r.StartError:F2} e1 {r.EndError:F2}  chord {r.Chord:F2}  sag {r.Sag:F2}  " +
                $"owner {r.OwnerError:F2}  thinks {r.Thinks} draws {r.Draws}\n"));
        }

        // Spans that have never once reached the executor look exactly like a
        // map with no ropes on screen; say so.
        if (rows.Count > 0 && ropes.Strips == 0)
            log.Warning("[SourceSharp] WARNING: not one span has drawn a strip this level. Either none is in " +
                        "view, or the rope draw (ClientRopeSystem.Draw -> RopeDraw) has stopped reaching " +
                        "shell_rope_render.cpp.\n");
    }
}
