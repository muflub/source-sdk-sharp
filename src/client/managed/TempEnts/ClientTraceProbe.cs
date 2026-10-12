using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// The first caller of <see cref="ClientTraceResult.SurfaceProps"/> and
/// <see cref="ClientTraceResult.SurfaceFlags"/>, and the instrument that
/// confirms they are right.
///
/// <para>
/// <b>WHY THIS EXISTS.</b> Lane CX added the client trace for
/// <c>CTEProjectedDecal</c> and put two extra outs on it for the pool's
/// collision arm — behaviour ledger rows C4 and C5 read exactly
/// <c>surface.surfaceProps</c> and <c>surface.flags</c> off their own
/// <c>trace_t</c>. CX said so in all five places those fields appear, and said
/// equally clearly that they had <b>no caller and were not live-confirmed</b>.
/// An untested out on a wired slot is the worst kind of ABI addition: it
/// compiles, it crosses, and the first real consumer inherits whatever it
/// actually contains.
/// </para>
///
/// <para>
/// <b>WHY A PROBE RATHER THAN THE COLLISION ARM ITSELF.</b> Moving the arm is a
/// design decision this lane is not able to take alone — it needs
/// <c>ClientTraceLine</c> to grow two filters it deliberately does not have
/// (see the report in <c>docs/te-legacy-pool-survey.md</c> section 6). But
/// "confirm them or report that they are wrong" does not need the arm; it needs
/// a trace whose answer can be checked against a surface somebody can name. A
/// floor is not the sky, concrete is not metal, and the console can say which
/// it thinks it hit.
/// </para>
///
/// <para>
/// <b>IT TRACES STRAIGHT DOWN AND STRAIGHT UP, and does not take an aim.</b>
/// Those are the two cases rows C4 and C5 actually care about and they are the
/// two a reader can check without a screenshot: DOWN hits the floor, whose
/// surfaceprops index must resolve to the material that floor is made of, and
/// UP hits the SKYBOX on almost every map, where <c>surfaceFlags</c> must have
/// <c>SURF_SKY</c> set. A probe that needed aiming would produce an answer
/// nobody could check against anything, which is how an unverified out stays
/// unverified through the test that was supposed to verify it.
/// </para>
///
/// <para>
/// <c>SURF_SKY</c> is not decoration here: it is the whole of what the
/// <c>FTENT_ATTACHTOTARGET</c> branch tests. A tempent that hits the sky must
/// DIE rather than stick to it, and the difference between those two is one bit
/// of this field.
/// </para>
///
[ConCommand("cs_cl_te_trace",
    Help = "Trace from the view and print every out of ClientTraceLine, including "
         + "surfaceProps and surfaceFlags. cs_cl_te_trace [distance]")]
public sealed class ClientTraceProbeCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineWorldQuery _world;
    private readonly IEngineHud _hud;

    public ClientTraceProbeCommand(ILog log, IEngineWorldQuery world, IEngineHud hud)
    {

        _log = log;
        _world = world;
        _hud = hud;
    }

    /// <summary><c>SURF_SKY</c> (bspflags.h). The one flag the pool tests.</summary>
    private const int SurfSky = 0x0004;

    public void Execute(string args)
    {
        float distance = 4096f;

        if (!string.IsNullOrWhiteSpace(args)
            && float.TryParse(args.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                              out float asked)
            && asked > 0f)
        {
            distance = asked;
        }

        Vector? where = _hud.LocalPlayerOrigin;

        if (where is null)
        {
            _log.Warning("[SourceSharp/Client] cs_cl_te_trace: there is no local player to "
                      + "trace from. Join a game first.\n");
            return;
        }

        // A little above the feet, so that tracing DOWN has somewhere to go.
        Vector start = where.Value + new Vector(0f, 0f, 32f);

        Probe("DOWN, at the floor", start, start + new Vector(0f, 0f, -distance));
        Probe("UP, at the sky",    start, start + new Vector(0f, 0f, distance));
    }

    private void Probe(string what, Vector start, Vector end)
    {
        // MASK_ALL, which is what stock's own projected decal traces with and
        // what the pool's collision arm wants: a tempent stops on anything a
        // ray stops on. A narrower mask would answer about a different surface,
        // which for a probe whose whole job is naming the surface would be the
        // bug it exists to find.
        ClientTraceResult hit = _world.ClientTraceLine(start, end, TraceMask.All);

        if (!hit.Hit)
        {
            _log.Msg($"[SourceSharp/Client] cs_cl_te_trace {what}: nothing. "
                  + $"surfaceProps {hit.SurfaceProps}, surfaceFlags 0x{hit.SurfaceFlags:X4} "
                  + "-- these are the MISS values and must not look like a real surface's.\n");
            return;
        }

        // The index turned into something a person can check, through the slot
        // the index is FOR. That is the point of asking: a wrong index answers
        // a material that is not what is on screen, and a number on its own
        // could not have been told apart from a right one.
        SurfaceMaterial material = _world.SurfacePropGameMaterial(hit.SurfaceProps);

        bool sky = (hit.SurfaceFlags & SurfSky) != 0;

        _log.Msg($"[SourceSharp/Client] cs_cl_te_trace {what}: "
              + $"fraction {hit.Fraction:F3} at ({hit.End.X:F0} {hit.End.Y:F0} {hit.End.Z:F0}) "
              + $"normal ({hit.Normal.X:F2} {hit.Normal.Y:F2} {hit.Normal.Z:F2})\n");

        _log.Msg($"[SourceSharp/Client]   entity {hit.EntityIndex}"
              + $"{(hit.EntityIndex == 0 ? " (THE WORLD -- this is DidHitWorld)" : string.Empty)}, "
              + $"hitbox {hit.Hitbox}"
              + $"{(hit.Hitbox != 0 ? " (a static prop, numbered from one)" : string.Empty)}\n");

        _log.Msg($"[SourceSharp/Client]   surfaceProps {hit.SurfaceProps} -> game material "
              + $"'{material}' ({(int)material})  |  surfaceFlags 0x{hit.SurfaceFlags:X4}"
              + $"{(sky ? "  <- SURF_SKY IS SET" : string.Empty)}\n");

        // The statements a reader has to be able to make, said here rather than
        // left to be inferred: "it printed a number" is not the same claim as
        // "the number means what rows C4 and C5 need it to mean".
        if (hit.SurfaceProps < 0)
        {
            _log.Warning("[SourceSharp/Client]   surfaceProps came back NEGATIVE on a HIT. "
                      + "The collision block hands this straight to "
                      + "CEffectData::m_nSurfaceProp, where a negative indexes nothing.\n");
        }
    }
}
