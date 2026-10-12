using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Beams;

/// <summary>
/// cs_cl_beam_endpoint: what a beam would attach to at an entity index, and
/// which hierarchy answered (behaviour ledger row C8). Native's command,
/// moved with the endpoint's route; the facts are native's, the answer is
/// <see cref="ClientBeamSystem.EntityPosition"/>'s.
/// </summary>
[ConCommand("cs_cl_beam_endpoint", Flags = ConVarFlags.Cheat,
            Help = "Behaviour ledger row C8: what a beam would attach to at an entity index, and which hierarchy " +
                   "answered.")]
public sealed class BeamEndpointCommand(ILog log, IClientBeamExecutor executor, ClientBeamSystem beams) : IConCommand
{
    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || !int.TryParse(parts[0], out int index))
        {
            log.Msg("usage: cs_cl_beam_endpoint <entity> [attachment] [hitbox 0|1]\n");
            return;
        }

        int attachment = parts.Length > 1 && int.TryParse(parts[1], out int a) ? a : 0;
        bool hitbox = parts.Length > 2 && int.TryParse(parts[2], out int h) && h != 0;

        EntityHandle entity = executor.EntityAt(index);
        Span<float> facts9 = stackalloc float[9];
        var facts = (BeamEntityFacts)executor.EntityFacts(entity, attachment, !hitbox, facts9);

        string route = (facts & BeamEntityFacts.Resolved) == 0 ? "nothing (unresolved)"
                     : (facts & BeamEntityFacts.Sdk) != 0 ? "SDK entity -> not answered (stock's cut body)"
                     : (facts & BeamEntityFacts.Renderable) != 0 ? "MANAGED -> IClientRenderable / ICollideable"
                     : "nothing (neither an entity nor a renderable)";

        Vector point = default;
        bool answered = beams.EntityPosition(entity, attachment, hitbox, ref point);

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp] cs_cl_beam_endpoint: entity {index} attachment {attachment} hitbox {(hitbox ? 1 : 0)}\n" +
            $"  route     : {route}\n" +
            $"  player    : {((facts & BeamEntityFacts.Player) != 0 ? "yes (C_StandalonePlayer)" : "no")}\n" +
            $"  answered  : {(answered ? "yes" : "NO -- the beam keeps its previous point, as stock does")}\n" +
            $"  point     : ({point.X:F2} {point.Y:F2} {point.Z:F2})\n"));

        if ((facts & BeamEntityFacts.Renderable) != 0)
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"  renderorg : ({facts9[6]:F2} {facts9[7]:F2} {facts9[8]:F2})\n"));
    }
}
