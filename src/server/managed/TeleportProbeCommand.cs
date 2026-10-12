using System;
using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_teleport_probe &lt;move|jump&gt; &lt;targetname&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>
/// — the two arms of <c>tools/defect-teleport-gate</c>, driven from the console.
///
/// <para>
/// <b>THE CONTROL ARM AND THE TREATMENT ARM ARE ONE COMMAND</b>, and that is
/// the whole reason this exists rather than the gate reaching for two unrelated
/// ones. The two arms must differ in EXACTLY ONE THING — whether the move was
/// declared a jump — and a driver that used <c>ent_teleport</c> for one and
/// something else for the other would be comparing two code paths, two entity
/// states and two distances at once. Same entity, same destination, same
/// distance, same frame budget; <c>move</c> writes the origin and <c>jump</c>
/// teleports, and nothing else about the two calls differs.
/// </para>
///
/// <para>
/// <b>What each arm must produce, measured on the CLIENT</b> by
/// <c>cs_cl_teleport_report</c>:
/// </para>
/// <list type="bullet">
///   <item><c>move</c> — the drawn pose lags the delivered one for several
///   rendered frames, which is the defect's own symptom and the proof the
///   instrument can see it. A zero here means the measurement is broken, not
///   that the build is good.</item>
///   <item><c>jump</c> — zero lagging frames: the entity is drawn where it
///   went on the first frame the new pose arrives on.</item>
/// </list>
///
/// <para>
/// <b>NOT AN <c>sv_cheats</c> COMMAND</b>, deliberately: it is an instrument and
/// not a player capability, and the gate that drives it would otherwise be
/// measuring whether cheats were on. <c>DevCommand</c>'s issuer rule does not
/// apply either — this acts on a named entity, not on whoever typed it, so it
/// works from a headless server console with nobody connected.
/// </para>
///
/// <para>
/// DEBT, WITH A REMOVAL OWNER: lane <c>defect-teleport</c>'s driver, read by
/// <c>tools/defect-teleport-gate</c> and by nothing else. It goes when that
/// gate's rows are folded into <c>tools/live-gate</c>, the same booking
/// <c>8e-entfire</c>'s gate carries.
/// </para>
/// </summary>
[ConCommand("cs_sv_teleport_probe",
    Help = "<move|jump> <targetname> <x> <y> <z>: put the named entity there, either "
         + "by writing its origin or by teleporting it. The gate's two arms.")]
public sealed class TeleportProbeCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineEntities _entities;

    public TeleportProbeCommand(ILog log, IEngineEntities entities) 

    {

        _log = log;

        _entities = entities;

    }

    public void Execute(string args)
    {
        string[] words = args.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 5)
        {
            _log.Msg("[SourceSharp/TeleportProbe] usage: cs_sv_teleport_probe "
                  + "<move|jump> <targetname> <x> <y> <z>\n");
            return;
        }

        bool jump = string.Equals(words[0], "jump", StringComparison.OrdinalIgnoreCase);

        if (!jump && !string.Equals(words[0], "move", StringComparison.OrdinalIgnoreCase))
        {
            _log.Msg($"[SourceSharp/TeleportProbe] '{words[0]}' is neither 'move' nor 'jump'\n");
            return;
        }

        if (!Number(words[2], out float x) || !Number(words[3], out float y)
            || !Number(words[4], out float z))
        {
            _log.Msg("[SourceSharp/TeleportProbe] x, y and z must be numbers\n");
            return;
        }

        var found = _entities.FindAllByName(words[1]);

        if (found.Count == 0)
        {
            _log.Msg($"[SourceSharp/TeleportProbe] nothing is called '{words[1]}'\n");
            return;
        }

        var handle = found[0];
        var destination = new Vector(x, y, z);
        var from = _entities.GetOrigin(handle);

        if (jump)
            _entities.Teleport(handle, destination, angles: null, zeroVelocity: true);
        else
            _entities.SetOrigin(handle, destination);

        // THE LINE THE GATE READS, and it carries the arm as a word. A number
        // that can be read without knowing which arm produced it is a number the
        // harness can attribute to the wrong one -- which is how a control arm
        // stops being a control.
        _log.Msg(string.Format(CultureInfo.InvariantCulture,
            "[SourceSharp/TeleportProbe] {0} '{1}' index={2} from {3:F1} {4:F1} {5:F1} "
          + "to {6:F1} {7:F1} {8:F1} distance {9:F1}\n",
            jump ? "jump" : "move", words[1], _entities.GetIndex(handle),
            from.X, from.Y, from.Z, destination.X, destination.Y, destination.Z,
            Distance(from, destination)));
    }

    private static bool Number(string word, out float value)
        => float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static float Distance(Vector a, Vector b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
