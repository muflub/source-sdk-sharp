using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The SERVER's answer to the question <c>cs_attachments</c> asks the client:
/// where is every entity's attachment, in world space.
///
/// <para>
/// It exists to be compared. Until Phase 5's last piece the server had no
/// skeleton at all and every attachment slot was wired only under
/// <c>#ifdef CLIENT_DLL</c>, so the same managed call answered 0 and false here
/// — silently, because every guarded call in <c>Engine.cs</c> skips a null
/// pointer. Now both sides answer, from two skeletons computed independently
/// out of the same model file, and <b>that independence is the whole value</b>:
/// run this beside <c>cs_attachments</c> and the two listings are two readers
/// that share no implementation. A number that matches is evidence. A report
/// that agreed with itself would not be.
/// </para>
///
/// <para>
/// SIDE-NESS: server-only code reading a server-authoritative skeleton, bucket
/// 4. The skeleton it reads exists so that a child parented to an attachment
/// can be placed for EVERYBODY — its output leaves this side as a position,
/// not as a clock, which is why nothing here networks a cycle. The client's
/// skeleton answers a different question (bucket 3, per-viewer) and the two are
/// not required to agree frame by frame; what they must agree about is a
/// STANDING entity, where neither clock is moving anything.
/// </para>
/// </summary>
public static class ServerAttachments
{
    /// <summary>
    /// Every matching entity's attachments, one line each.
    ///
    /// <para>
    /// Deliberately the same line shape as <c>ClientAttachments.Report</c>, so
    /// the two can be read side by side or diffed. A format that drifted
    /// between the sides would make the comparison a manual one, and a manual
    /// comparison is one nobody does twice.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Report(IEnumerable<ScriptedEntity> live,
                                               IEngineEntityRender render)
    {
        var lines = new List<string>();
        int matched = 0;
        int withAttachments = 0;

        foreach (var entity in live)
        {
            matched++;

            int count = render.AttachmentCount(entity.Handle);
            string model = render.GetModelName(entity.Handle);

            // Both numbers, as the client's report gives them and for the same
            // reason: the model's count says whether this is a one-bone prop or
            // a real skeleton, and the cache's says whether anything has ever
            // asked this entity to pose. A cached 0 beside a model 15 is an
            // entity nothing has needed a frame from, which looks exactly like
            // a broken bone setup if only one number is printed.
            int bones = render.BoneCount(entity.Handle, out int cachedBones);
            string skeleton = bones > 0 ? $"{bones} bone(s), {cachedBones} cached" : "no skeleton";

            if (count <= 0)
            {
                lines.Add($"{entity.ClassName}  {Describe(model)}  {skeleton}  no attachments");
                continue;
            }

            withAttachments++;
            lines.Add($"{entity.ClassName}  {Describe(model)}  {skeleton}  {count} attachment(s)");

            for (int i = 1; i <= count; i++)
            {
                string name = render.AttachmentName(entity.Handle, i);

                // Through the NAME rather than the index the loop already had,
                // because that is the round trip a real caller makes and the
                // one an off-by-one in the engine's one-based convention would
                // show up in.
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

    private static string Describe(string model)
        => model.Length > 0 ? model : "(no model)";
}

/// <summary>
/// The console name for <see cref="ServerAttachments"/>. Named to pair with
/// <c>cs_attachments</c> rather than to replace it — the point is running both.
/// </summary>
[ConCommand("cs_sv_attachments",
            Help = "cs_sv_attachments <classname>. Every server entity of that class, its " +
                   "skeleton and its attachment points in world space, in the shape " +
                   "cs_attachments prints on the client. Run both and compare.")]
public sealed class ServerAttachmentsCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEntityDirectory _entities;
    private readonly IEngineEntityRender _render;

    public ServerAttachmentsCommand(ILog log, IEntityDirectory entities, IEngineEntityRender render)
    {

        _log = log;
        _entities = entities;
        _render = render;
    }

    public void Execute(string args)
    {
        string className = args.Trim();

        // An EXACT classname, where the client's report takes a substring, and
        // the asymmetry is the directory's rather than a preference:
        // IEntityDirectory answers OfClass and has no "everything" -- the
        // server's live table is the registry's own and is not enumerable from
        // out here. Asked for rather than guessed at, because the comparison
        // this exists for is always about a named entity anyway.
        if (className.Length == 0)
        {
            _log.Msg("[SourceSharp/Server] cs_sv_attachments <classname>\n");
            return;
        }

        foreach (string line in ServerAttachments.Report(_entities.OfClass(className), _render))
            _log.Msg($"[SourceSharp/Server] {line}\n");
    }
}
