using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// What the client's entities have to hang things off, and where those points
/// are right now.
///
/// <para>
/// The one question a bone cache exists to answer, asked out loud. Until Phase 5
/// the standalone client shell had no cache, so every attachment query answered
/// -1 and false on purpose — which is a capability that is invisible whether it
/// works or not. This is the report that makes it visible: a listing of every
/// live entity, its model, and every attachment on it in world space.
/// </para>
///
/// <para>
/// It is also the only honest way to VERIFY the cache. A bone cache's failure
/// mode is a skeleton that was right one frame ago, and the symptom is a model
/// that looks correct standing still — so the test that matters is asking the
/// same entity twice while it moves and seeing the answer follow. Run it, walk
/// past a moving entity, run it again.
/// </para>
/// </summary>
public static class ClientAttachments
{
    /// <summary>
    /// Every live entity's attachments, one line each, most specific first.
    ///
    /// <para>
    /// Separate from the command so it can be tested: the command is a console
    /// name and a write, and everything that decides what the lines SAY is here.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Report(IReadOnlyList<ClientStandaloneEntity> live,
                                               IEngineEntityRender render,
                                               string filter)
    {
        var lines = new List<string>();
        int matched = 0;
        int withAttachments = 0;

        foreach (var entity in live)
        {
            if (filter.Length > 0 && filter != "*" &&
                !entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            matched++;

            int count = render.AttachmentCount(entity.Handle);
            string model = render.GetModelName(entity.Handle);

            // The skeleton, which is what the attachments hang off and what
            // Phase 5 is actually about. Both numbers, because they answer
            // different questions: the model's count says whether this is a
            // one-bone prop or a real skeleton, and the cache's says whether
            // anything has ever asked this entity to pose. A cached count of 0
            // beside a model count of 60 is an entity nothing has drawn — which
            // looks exactly like a broken bone setup if only one is reported.
            int bones = render.BoneCount(entity.Handle, out int cachedBones);
            string skeleton = bones > 0 ? $"{bones} bone(s), {cachedBones} cached" : "no skeleton";

            if (count <= 0)
            {
                // Said rather than skipped. "This entity has no attachments" and
                // "this entity was never asked" look identical in a listing that
                // only prints the interesting rows, and the whole point of the
                // report is telling those two apart.
                lines.Add($"{entity.ClassName}  {Describe(model)}  {skeleton}  no attachments");
                continue;
            }

            withAttachments++;
            lines.Add($"{entity.ClassName}  {Describe(model)}  {skeleton}  {count} attachment(s)");

            for (int i = 1; i <= count; i++)
            {
                string name = render.AttachmentName(entity.Handle, i);

                // By NAME rather than by the index just used, so the round trip
                // is what is reported: a listing that reads back the index it
                // counted with proves nothing about the lookup, and the lookup
                // is what every caller will actually use.
                int looked = render.LookupAttachment(entity.Handle, name);

                bool found = render.GetAttachment(entity.Handle, looked,
                                                  out Vector origin, out Vector angles);

                string agreement = looked == i ? string.Empty : $"  [lookup said {looked}, not {i}]";

                lines.Add($"    {i,2} {name,-20} " +
                          $"({origin.X,8:0.0} {origin.Y,8:0.0} {origin.Z,8:0.0}) " +
                          $"({angles.X,7:0.0} {angles.Y,7:0.0} {angles.Z,7:0.0})" +
                          $"{(found ? string.Empty : "  [no answer]")}{agreement}");
            }
        }

        lines.Add($"{matched} entit(ies), {withAttachments} with attachments");

        return lines;
    }

    /// <summary>A model name, or a stand-in for an entity that is wearing none.</summary>
    private static string Describe(string model)
        => model.Length > 0 ? model : "(no model)";
}

/// <summary>
/// The console name for <see cref="ClientAttachments"/>.
/// </summary>
[ConCommand("cs_attachments",
            Help = "List every client entity's attachment points, in world space. " +
                   "Optional filter matches part of a classname.")]
public sealed class AttachmentsCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public AttachmentsCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        foreach (string line in ClientAttachments.Report(_standalones.Live(), _render, args.Trim()))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}
