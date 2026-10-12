using SourceSharp.Abi;

namespace SourceSharp.Client.Beams;

/// <summary>
/// cs_cl_beams: the managed beam system's tally since the level began, and one
/// line per live beam. The live gate's beam-ring step reads the tally after
/// firing a ring: a ring that was created but drew no strip is a beam the
/// executor never reached.
/// </summary>
[ConCommand("cs_cl_beams", Help = "the managed beams: created, refused, drawn, and each live one")]
public sealed class BeamsCommand(ILog log, IClientBeams beams, ClientBeamSystem system) : IConCommand
{
    public void Execute(string args)
    {
        BeamCounters c = beams.Counters;

        log.Msg($"[SourceSharp] cs_cl_beams: {c.Active} active, {c.Created} created, {c.Refused} refused, " +
                $"{c.Updates} update(s), {c.Draws} draw(s), {c.Strips} strip(s), {c.Meshes} mesh(es)\n");

        IReadOnlyList<Beam> active = system.Bookkeeping.Active;
        for (int i = 0; i < active.Count; i++)
        {
            Beam b = active[i];
            log.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  [{i}] {b.Type} model {b.ModelIndex} halo {b.HaloIndex} flags 0x{(int)b.Flags:x} " +
                $"die {b.Die:F2} segments {b.Segments} renderable {b.Renderable} " +
                $"from ({b.Attachment[0].X:F0} {b.Attachment[0].Y:F0} {b.Attachment[0].Z:F0}) " +
                $"delta ({b.Delta.X:F0} {b.Delta.Y:F0} {b.Delta.Z:F0})\n"));
        }
    }
}
