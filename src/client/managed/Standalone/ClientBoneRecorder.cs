using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The six facts the live gate reads about a skinned skeleton, recorded from
/// managed code — the replacement for <c>ClientBoneProbe::ReportSkinned</c>.
///
/// <para>
/// <b>Why it prints what it prints.</b> The rows are not a report for a person;
/// they are a wire format <c>tools/live-gate</c> parses. It looks for the
/// literal <c>SS6CGATE\t</c> anywhere in a console line, slices from there,
/// splits on tabs, and takes field 2 as the fact name and field 3 as its value,
/// keeping the LAST occurrence of each. So the rows are emitted bare, one fact
/// per line, with no prefix and no verdict — exactly as the native probe
/// emitted them. A row that said "3 of 56 posed" would be unreadable to it.
/// </para>
///
/// <para>
/// <b>And why they go through <see cref="Log"/>.</b> The gate reads
/// <c>game/mod_sharp/console.log</c>, not the dev API log. <c>Log.Msg</c>
/// crosses to the engine's own <c>Msg</c>, which is what lands in that file;
/// anything written only on the managed side would leave the gate reading an
/// empty row set and reporting that the probe printed nothing.
/// </para>
///
/// <para>
/// <b>The subject is one entity, and choosing it is the load-bearing part.</b>
/// Every fact below is a fact about whichever entity was picked, so this must
/// pick the one the native probe picked: the POSEABLE entity with the most
/// bones. "Poseable" is asked across the boundary
/// (<see cref="IEngineEntityRender.IsPoseable"/>) rather than guessed from the
/// model descriptor, because the only thing managed could ask — whether the
/// model declares a sequence — is TRUE for a static prop that carries one, and
/// the static-prop branch writes the entity's own transform into every bone.
/// Picking one of those would report <c>posed 0</c> on a healthy build, which
/// reads as a broken pose branch rather than as a bad choice of subject.
/// </para>
/// </summary>
public static class ClientBoneRecorder
{
    /// <summary>
    /// The tag every row carries, and the one the gate searches for.
    /// </summary>
    public const string Tag = "SS6CGATE";

    /// <summary>
    /// The row kind. The gate reads two — <c>skinned</c>, which is this — and
    /// <c>merge</c>, which is still the native probe's.
    /// </summary>
    public const string Kind = "skinned";

    /// <summary>
    /// An attachment counts as moved when it is more than this far from the
    /// entity's own origin, squared — the native probe's <c>LengthSqr() &gt;
    /// 1.0f</c>. One unit, because a skeleton nothing posed puts every
    /// attachment exactly ON the origin and the question is only whether any of
    /// them left it.
    /// </summary>
    public const float MovedThresholdSquared = 1.0f;

    /// <summary>
    /// The entity the report is about: the poseable one carrying the most
    /// bones, first past the post on a tie.
    ///
    /// <para>
    /// Takes the two questions as delegates rather than an engine, because the
    /// choice is the part worth testing on its own and the engine double
    /// answers by member name — one value for every entity — which cannot
    /// express "three entities with different bone counts" at all.
    /// </para>
    ///
    /// <para>
    /// Strictly greater, matching the native loop: on a tie the FIRST entity in
    /// list order wins, and a recorder that took the last would drift from the
    /// probe on any map carrying two of the same model.
    /// </para>
    /// </summary>
    public static ClientStandaloneEntity? PickSubject(
        IReadOnlyList<ClientStandaloneEntity> live,
        Func<ClientStandaloneEntity, bool> isPoseable,
        Func<ClientStandaloneEntity, int> boneCount)
    {
        ClientStandaloneEntity? best = null;
        int bestBones = 0;

        foreach (var entity in live)
        {
            if (!isPoseable(entity))
                continue;

            int bones = boneCount(entity);

            if (bones > bestBones)
            {
                bestBones = bones;
                best = entity;
            }
        }

        return best;
    }

    /// <summary>
    /// How many of an entity's attachments have left its origin.
    ///
    /// <para>
    /// The origin is taken from the bone cache's own
    /// <see cref="StandaloneBones.EntityToWorld"/> rather than from a separate
    /// query: that matrix is <c>BoneLibrary_PlacementToWorld</c> of the
    /// entity's placement, and the scale it applies touches only the rotation —
    /// so its translation column IS the origin the native probe compares
    /// against, byte for byte, with no second crossing and no chance of the two
    /// being sampled a frame apart.
    /// </para>
    /// </summary>
    public static int CountMovedAttachments(StandaloneBones cache, IReadOnlyList<Vector> attachments)
    {
        if (cache.EntityToWorld.Length < StandaloneBones.FloatsPerMatrix)
            return 0;

        // Column 3 of a row-major matrix3x4_t: row * 4 + 3.
        float originX = cache.EntityToWorld[3];
        float originY = cache.EntityToWorld[7];
        float originZ = cache.EntityToWorld[11];

        int moved = 0;

        foreach (Vector at in attachments)
        {
            float dx = at.X - originX;
            float dy = at.Y - originY;
            float dz = at.Z - originZ;

            if ((dx * dx) + (dy * dy) + (dz * dz) > MovedThresholdSquared)
                moved++;
        }

        return moved;
    }

    /// <summary>
    /// One row, in the shape the gate parses.
    /// </summary>
    public static string Row(string fact, string value)
        => $"{Tag}\t{Kind}\t{fact}\t{value}";

    /// <summary>
    /// The row a run with no subject emits, and the only row it emits.
    ///
    /// <para>
    /// A bare <c>bones 0</c>, which is what the native probe printed in the
    /// same two cases: no poseable entity, and a bone build that failed. It is
    /// four fields so the gate parses it rather than skipping it, and it fails
    /// the gate's <c>bones &gt; 1</c> check — said out loud rather than left as
    /// an empty row set, because "the probe printed nothing" and "the probe
    /// found nothing to measure" are different failures.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> NoSubject()
        => new[] { Row("bones", "0") };

    /// <summary>
    /// The six rows for one gathered subject, in the native probe's order.
    /// </summary>
    public static IReadOnlyList<string> Rows(string model, int bones, StandaloneBones cache,
                                             int attachments, int movedAttachments)
        => new[]
        {
            Row("model", model),
            Row("bones", bones.ToString()),
            Row("written", cache.WrittenCount().ToString()),
            Row("posed", cache.PosedCount().ToString()),
            Row("attachments", attachments.ToString()),
            Row("movedattachments", movedAttachments.ToString()),
        };

    /// <summary>
    /// Pick the subject, gather its six facts, and render the rows.
    ///
    /// <para>
    /// The bone cache is asked for FIRST, before the attachments, because
    /// asking for it is what forces the bone build — the slot's body runs
    /// <c>SetupBones</c> with the renderer's own mask — and an attachment read
    /// before that build would be a position from whatever frame last drew the
    /// entity. The native probe orders the two the same way and for the same
    /// reason.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Report(IReadOnlyList<ClientStandaloneEntity> live,
                                               IEngineEntityRender render)
    {
        var subject = PickSubject(live, e => render.IsPoseable(e.Handle),
                                  e => render.BoneCount(e.Handle, out _));

        if (subject is null)
            return NoSubject();

        StandaloneBones? cache = render.BoneMatrices(subject.Handle);

        if (cache is null)
            return NoSubject();

        int attachments = render.AttachmentCount(subject.Handle);
        var positions = new List<Vector>(attachments < 0 ? 0 : attachments);

        // One-based, which is the engine's attachment convention and the one
        // LookupAttachment answers in.
        for (int i = 1; i <= attachments; i++)
        {
            render.GetAttachment(subject.Handle, i, out Vector origin, out _);
            positions.Add(origin);
        }

        // StudioName, NOT GetModelName. The two name the same file and are
        // different strings -- "Humans\Group03\Male_01.mdl" against
        // "models/humans/group03/male_01.mdl" -- and this row is the recorder's
        // SUBJECT IDENTITY: the other five facts are numbers two entities
        // wearing one model agree on exactly, so this is the only row that can
        // show the recorder and the native probe having picked different
        // entities. tools/7b-bonerecord-agree measured the difference on a live
        // join, which is how it came to be a slot.
        return Rows(render.StudioName(subject.Handle),
                    render.BoneCount(subject.Handle, out _),
                    cache,
                    attachments,
                    CountMovedAttachments(cache, positions));
    }
}

/// <summary>
/// The console name for <see cref="ClientBoneRecorder"/>, and the one
/// <c>tools/live-gate</c>'s skinned step drives in place of
/// <c>cs_cl_boneprobe report</c>.
/// </summary>
[ConCommand("cs_cl_bonerecord",
            Help = "SourceSharp: record the skinned skeleton's six facts — model, bones, " +
                   "written, posed, attachments, movedattachments — as SS6CGATE rows.")]
public sealed class BoneRecordCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public BoneRecordCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        // BARE, with no "[SourceSharp/Client]" prefix, unlike every other
        // command in this directory. The gate slices from the tag so a prefix
        // would parse -- but the native probe printed these rows bare, and a
        // recorder whose output is byte-identical is one a reader can diff
        // against the probe's without allowing for the difference.
        foreach (string line in ClientBoneRecorder.Report(_standalones.Live(), _render))
            _log.Msg($"{line}\n");
    }
}
