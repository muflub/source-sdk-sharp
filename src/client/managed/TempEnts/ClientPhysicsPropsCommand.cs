using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>cs_cl_physprops</c>: every clientside physics prop (CTEPhysicsProp's),
/// one <c>SSCLPHYSPROP</c> tab row each after a prose line: slot, origin,
/// asleep. A live check reads it over time to see a prop fall and settle.
/// </summary>
[ConCommand("cs_cl_physprops", Help = "cs_cl_physprops: the clientside physics props -- slot, origin, asleep.")]
public sealed class ClientPhysicsPropsCommand(ILog log, ClientPhysicsProps? props = null,
                                               Physics.ClientContactTouches? touches = null) : IConCommand
{
    public void Execute(string args)
    {
        if (props is null)
        {
            log.Msg("[SourceSharp/Client] cs_cl_physprops: no clientside physics props (no client vphysics)\n");
            return;
        }

        log.Msg($"[SourceSharp/Client] cs_cl_physprops: {props.Count} prop(s)\n");
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"SSCLPHYSTOUCH\tstarts={touches?.Starts ?? 0}\tworld_starts={touches?.WorldStarts ?? 0}" +
            $"\tdispatched={touches?.Dispatched ?? 0}\tby_world={touches?.DispatchedByWorld ?? 0}" +
            $"\tends={touches?.Ends ?? 0}\theld={touches?.Held ?? 0}\tprop_touches={props.Touches}" +
            $"\tworld_touches={props.WorldTouches}\tsurface_hits={props.SurfaceHits}\tblood={props.BloodSplats}\n"));
        foreach ((int slot, Vector origin, bool asleep) in props.Report())
        {
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"SSCLPHYSPROP\t{slot}\t{origin.X:F2}\t{origin.Y:F2}\t{origin.Z:F2}\t{(asleep ? 1 : 0)}\n"));
        }
    }
}
