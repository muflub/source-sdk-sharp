using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// What dials a client entity's model declares, where each one is set, and a
/// way to turn one by hand.
///
/// <para>
/// A sequence says which animation is playing; a POSE PARAMETER says which way
/// the model is looking while it plays. Until Phase 5's last piece landed, the
/// array this hierarchy handed <c>bone_setup</c> was a zeroed stack local — and
/// zero is not neutral. bone_setup takes the pose normalised across each
/// parameter's declared range, so zero is the range's START: for the
/// <c>aim_yaw</c> of -180..180 that half of Source's animating models carry,
/// zero draws a model craning as far left as its skeleton bends. Every entity
/// of this hierarchy wearing such a model was drawn that way, and nothing said
/// so, because a wrong pose is a picture and not a log line.
/// </para>
///
/// <para>
/// So this is the report that makes it visible, and — unlike a listing that
/// only reads — it can also WRITE, because a pose parameter is exactly the kind
/// of capability that a report agreeing with itself cannot verify. Set an
/// entity's <c>aim_yaw</c> to 60 from the console and the model turns its head
/// on screen, or it does not; that is a check no oracle can fake.
/// </para>
/// </summary>
public static class ClientPoseParameters
{
    /// <summary>
    /// Every matching entity's pose parameters, one line each.
    ///
    /// <para>
    /// Separate from the command so it can be tested: the command is a console
    /// name and a write, and everything that decides what the lines SAY is
    /// here.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Report(IReadOnlyList<ClientStandaloneEntity> live,
                                               IEngineEntityRender render,
                                               string filter)
    {
        var lines = new List<string>();
        int matched = 0;
        int withParameters = 0;

        foreach (var entity in live)
        {
            if (!Matches(entity, filter))
                continue;

            matched++;

            int count = render.PoseParameterCount(entity.Handle);
            string model = Describe(render.GetModelName(entity.Handle));

            // DORMANT, out loud, and first among the things a reader checks.
            // An entity out of PVS answers every query below with the last
            // value that arrived rather than the current one, and it does so
            // without complaint — which is how a viewmodel culled for having
            // its bounds below the floor kept reporting plausible numbers.
            string state = render.IsDormant(entity.Handle) ? "DORMANT" : "live";

            if (count <= 0)
            {
                // Said rather than skipped, for the same reason cs_attachments
                // says it: "this model declares no pose parameters" and "this
                // entity was never asked" look identical in a listing that
                // prints only the interesting rows.
                lines.Add($"{entity.ClassName}  {model}  {state}  no pose parameters");
                continue;
            }

            withParameters++;
            lines.Add($"{entity.ClassName}  {model}  {state}  {count} pose parameter(s)");

            for (int i = 0; i < count; i++)
            {
                string name = render.PoseParameterName(entity.Handle, i);

                // By NAME rather than by the index just used, so the round trip
                // a real caller makes is what is exercised. A listing that
                // reads back the index it counted with proves nothing about the
                // lookup, and the lookup is the only way a caller can address
                // one of these safely across a model change.
                int looked = render.LookupPoseParameter(entity.Handle, name);

                float value = render.GetPoseParameter(entity.Handle, i);

                string range = render.PoseParameterRange(entity.Handle, i, out float start, out float end)
                    ? $"[{start,7:0.0} ..{end,7:0.0}]"
                    : "[no range]";

                string agreement = looked == i ? string.Empty : $"  [lookup said {looked}, not {i}]";

                lines.Add($"    {i,2} {name,-20} {value,9:0.000} {range}{agreement}");
            }
        }

        lines.Add($"{matched} entit(ies), {withParameters} with pose parameters");

        return lines;
    }

    /// <summary>
    /// Turns one named dial on every matching entity, and says what actually
    /// happened to each.
    ///
    /// <para>
    /// The report is the point rather than a courtesy. A pose parameter WRAPS
    /// when the model declares it looping (an <c>aim_yaw</c> of 190 degrees is
    /// -170, not a clamp to 180) and CLAMPS when it does not, so what the
    /// engine stored is frequently not what the caller asked for — and a
    /// setter that answered nothing would leave the caller unable to tell a
    /// wrap from a model that has no such parameter at all.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Apply(IReadOnlyList<ClientStandaloneEntity> live,
                                              IEngineEntityRender render,
                                              string filter,
                                              string name,
                                              float value)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
            return new[] { "cs_setpose <classname-filter> <parameter> <value>" };

        int applied = 0;
        int refused = 0;

        foreach (var entity in live)
        {
            if (!Matches(entity, filter))
                continue;

            int parameter = render.LookupPoseParameter(entity.Handle, name);

            if (parameter < 0)
            {
                // Named rather than silently skipped: "this model has no
                // aim_yaw" is the single most likely reason a caller sees
                // nothing move, and a setter that says nothing leaves them
                // debugging the renderer instead of the model.
                refused++;
                lines.Add($"{entity.ClassName}  {Describe(render.GetModelName(entity.Handle))}  " +
                          $"no pose parameter named '{name}'");
                continue;
            }

            float stored = render.SetPoseParameter(entity.Handle, parameter, value);

            applied++;

            string note = NearlyEqual(stored, value)
                ? string.Empty
                : $"  [asked {value:0.###}, stored {stored:0.###}]";

            lines.Add($"{entity.ClassName}  {name} ({parameter}) = {stored:0.###}{note}");
        }

        lines.Add($"{applied} set, {refused} without that parameter");

        return lines;
    }

    /// <summary>
    /// Whether a filter names this entity. Empty and "*" mean everything, as
    /// they do in cs_attachments — the two reports are read side by side and a
    /// filter that meant different things in each would be a trap.
    /// </summary>
    private static bool Matches(ClientStandaloneEntity entity, string filter)
        => filter.Length == 0 || filter == "*" ||
           entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>A model name, or a stand-in for an entity wearing none.</summary>
    private static string Describe(string model)
        => model.Length > 0 ? model : "(no model)";

    /// <summary>
    /// Whether the engine stored what was asked for, to the precision the line
    /// is printed at.
    ///
    /// <para>
    /// Not exact equality: the value makes a round trip through the normalised
    /// 0..1 form bone_setup wants and back, so a request of 45 degrees on a
    /// -180..180 dial comes home as 45.000004. Flagging that as a clamp would
    /// make the note fire on every single call and mean nothing.
    /// </para>
    /// </summary>
    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) < 0.001f;
}

/// <summary>
/// The console name for <see cref="ClientPoseParameters.Report"/>.
/// </summary>
[ConCommand("cs_poseparams",
            Help = "List every client entity's pose parameters, with their values and " +
                   "declared ranges. Optional filter matches part of a classname.")]
public sealed class PoseParametersCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public PoseParametersCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        foreach (string line in ClientPoseParameters.Report(_standalones.Live(), _render, args.Trim()))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}

/// <summary>
/// The console name for <see cref="ClientPoseParameters.Apply"/> — the half of
/// the verification that a report cannot do for itself.
/// </summary>
[ConCommand("cs_setpose",
            Help = "cs_setpose <classname-filter> <parameter> <value>. Turns one named pose " +
                   "parameter on every matching client entity, in the model's own units.")]
public sealed class SetPoseCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public SetPoseCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3 || !float.TryParse(parts[2], out float value))
        {
            _log.Msg("[SourceSharp/Client] cs_setpose <classname-filter> <parameter> <value>\n");
            return;
        }

        foreach (string line in ClientPoseParameters.Apply(_standalones.Live(), _render,
                                                           parts[0], parts[1], value))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}
