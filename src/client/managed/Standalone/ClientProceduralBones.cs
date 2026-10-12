using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Which of the models in this level have bones the POSE does not decide.
///
/// <para>
/// A procedural bone is worked out from the bones around it — a forearm twist
/// that follows the wrist halfway, a knee helper that keeps a kneecap out of
/// the leg, an aim-at bone that points a barrel — and a jiggle bone is a
/// spring. Neither comes out of the animation, and until this piece of work
/// this hierarchy drew both from the animation anyway.
/// </para>
///
/// <para>
/// <b>This report exists because that defect and its absence look identical.</b>
/// A model with no procedural bones draws exactly the same whether the feature
/// works or not, and most models have none — so "is it working" and "does this
/// model even have one" shared a single answer, and every screenshot of the
/// wrong model said the feature was fine. The report separates them: it names
/// the models that CAN show a difference, which is the first thing anybody
/// verifying this needs and the last thing a log line about the feature itself
/// would tell them.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 3, the client's, throughout. Procedural bones decide
/// pixels for one viewer; the server does not run them and has no version of
/// the question.
/// </para>
/// </summary>
public static class ClientProceduralBones
{
    /// <summary>
    /// Every live entity whose model has a procedural or jiggle bone, with the
    /// counts, and a tally of the ones that have neither.
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

        int looked = 0;
        int plain = 0;
        int withComputed = 0;
        int withJiggle = 0;
        int withIk = 0;

        foreach (var entity in live)
        {
            if (filter.Length > 0 && filter != "*" &&
                !entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            // A dormant entity answers about the last model that reached it,
            // which is a different .mdl from the one it is wearing now. Skipped
            // rather than reported, because a bone count is a fact about a
            // model file and a stale one is a fact about the wrong file.
            if (render.IsDormant(entity.Handle))
                continue;

            looked++;

            int bones = render.ProceduralBones(entity.Handle, out int computed, out int jiggle,
                                               out int ikChains);

            if (bones <= 0)
                continue;

            if (computed == 0 && jiggle == 0 && ikChains == 0)
            {
                plain++;
                continue;
            }

            if (computed > 0)
                withComputed++;

            if (jiggle > 0)
                withJiggle++;

            if (ikChains > 0)
                withIk++;

            string model = render.GetModelName(entity.Handle);

            lines.Add($"{entity.ClassName}  {(model.Length > 0 ? model : "(no model)")}  " +
                      $"{bones} bone(s): {computed} procedural, {jiggle} jiggle, " +
                      $"{ikChains} IK chain(s)");
        }

        lines.Add($"{looked} live entit(ies) looked at: {withComputed} with procedural bone(s), " +
                  $"{withJiggle} with jiggle bone(s), {withIk} with IK chain(s), " +
                  $"{plain} with none of them");

        if (withComputed == 0 && withJiggle == 0 && withIk == 0)
        {
            // Said out loud, because it is the answer that most needs saying.
            // Somebody checking this feature against a level of models that
            // have none of these bones will photograph an unchanged screen and
            // conclude the feature is broken, or that it works — the screen
            // supports both.
            lines.Add("nothing in this level has a bone the pose does not decide — " +
                      "a screenshot here cannot tell working from broken");
        }

        return lines;
    }

    /// <summary>
    /// Plays one named sequence on every matching entity, and says what
    /// happened to each.
    ///
    /// <para>
    /// A sequence NAME that no model carries answers -1, and that is reported
    /// rather than swallowed — the bug that cost this project months was
    /// exactly a sequence name nothing answered to, returning quietly.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Play(IReadOnlyList<ClientStandaloneEntity> live,
                                             IEngineEntityRender render,
                                             string filter, string sequenceName, float rate)
    {
        var lines = new List<string>();
        int played = 0;

        foreach (var entity in live)
        {
            if (filter.Length > 0 && filter != "*" &&
                !entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            int sequence = render.LookupSequence(entity.Handle, sequenceName);

            if (sequence < 0)
            {
                lines.Add($"{entity.ClassName}  {render.GetModelName(entity.Handle)}  " +
                          $"NO SEQUENCE '{sequenceName}'");
                continue;
            }

            // restart: true, so asking twice for the same sequence starts it
            // again rather than doing nothing, which is what a probe means by
            // "play this".
            render.PlaySequence(entity.Handle, sequence, rate, restart: true);
            played++;

            lines.Add($"{entity.ClassName}  {render.GetModelName(entity.Handle)}  " +
                      $"playing '{sequenceName}' as sequence {sequence} at rate {rate}");
        }

        lines.Add($"{played} entit(ies) now playing '{sequenceName}'");

        return lines;
    }

}

/// <summary>
/// The console name for <see cref="ClientProceduralBones"/>.
/// </summary>
[ConCommand("cs_procbones",
            Help = "Which live models have procedural or jiggle bones — the ones where " +
                   "a screenshot can tell the feature working from broken.")]
public sealed class ProceduralBonesCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public ProceduralBonesCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        foreach (string line in ClientProceduralBones.Report(_standalones.Live(), _render, args.Trim()))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}

/// <summary>
/// Puts one named sequence on every matching client entity, client-side.
///
/// <para>
/// The sibling of <c>cs_setpose</c>, and here for the same reason: a bone that
/// is worked out from the bones around it agrees with the reference pose by
/// construction, so a model standing at cycle 0 draws identically whether
/// procedural bones are computed or not. Something has to bend before there is
/// anything to see, and nothing on this hierarchy but the view model networks a
/// sequence.
/// </para>
///
/// <para>
/// CLIENT-LOCAL, and deliberately: the cycle is client-local already (see
/// ViewModelNetState), so a sequence pushed here moves this viewer's pixels and
/// nothing else. It is a probe, not a mechanism — what makes an entity animate
/// in play is its own class deciding to.
/// </para>
/// </summary>
[ConCommand("cs_playseq",
            Help = "cs_playseq <classname-filter> <sequence-name> [rate]. Plays one named " +
                   "sequence on every matching client entity, for this viewer only.")]
public sealed class PlaySequenceCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public PlaySequenceCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            _log.Msg("[SourceSharp/Client] cs_playseq <classname-filter> <sequence-name> [rate]\n");
            return;
        }

        float rate = parts.Length > 2 && float.TryParse(parts[2], out float r) ? r : 1f;

        foreach (string line in ClientProceduralBones.Play(_standalones.Live(), _render,
                                                           parts[0], parts[1], rate))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}
