using System.Collections.Immutable;
using System.Collections.Generic;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// WHAT ONE MODEL ANSWERS, ON WHICHEVER SIDE IS ASKING, and what the
/// descriptor crossing cost to find out.
///
/// <para>
/// This exists to be run on BOTH sides of one live join and diffed, which is
/// the only way two questions get answered:
/// </para>
///
/// <para>
/// <b>1. Do the sides agree?</b> Before plan_shim.md Phase 2d they did not.
/// The client's <c>SourceSharp_EntityLookupSequence</c> called native
/// <c>::LookupSequence</c>, which on a label miss falls through to an activity
/// lookup resolved by <c>SelectWeightedSequence</c> — a weighted RANDOM pick
/// among the sequences sharing that activity — while the server's called
/// <c>BoneLibrary_LookupSequence</c>, which refuses that fallback and answers
/// -1. A sequence index is PREDICTED state. Running this on both sides with an
/// <c>ACT_</c> name is the reproduction, and after the crossing both sides run
/// one managed body and must print the same number.
/// </para>
///
/// <para>
/// <b>2. What does the crossing cost?</b> A level precaches hundreds of
/// models; the descriptor is fetched only for the models managed code ASKS
/// about, which is a different and much smaller number. The report prints
/// both the crossings and the bytes, so the answer is measured rather than
/// expected.
/// </para>
/// </summary>
public static class ModelDescriptorReport
{
    /// <summary>
    /// One entity's model, the sequences a set of names resolves to, and the
    /// running cost of <paramref name="models"/>, the asking side's own cache.
    /// </summary>
    public static IReadOnlyList<string> Describe(IEngineEntityRender render,
                                                 ModelDescriptors models,
                                                 EntityHandle entity,
                                                 IReadOnlyList<string> names)
    {
        var lines = new List<string>();

        string model = render.GetModelName(entity);
        ModelDescriptor d = models.For(entity);

        lines.Add($"model {(model.Length > 0 ? model : "(none)")}  " +
                  $"{d.Sequences.Count} sequence(s)  " +
                  $"{d.Attachments.Count} attachment(s)  {d.BoneCount} bone(s)");

        foreach (string name in names)
        {
            int byLabel = d.LookupSequence(name);
            int byActivity = d.LookupSequenceByActivity(name);

            // BOTH ANSWERS, because the defect was that one entry point gave
            // the activity answer on one side and the label answer on the
            // other. Printing them separately is what makes a disagreement
            // legible rather than a single number nobody can attribute.
            lines.Add($"  {name,-28} label={byLabel,4}  activity={byActivity,4}" +
                      DescribeSequence(d, byActivity >= 0 ? byActivity : byLabel));
        }

        lines.Add($"  cache: {models.CachedCount} model(s), " +
                  $"{models.Reads} read from the studio headers since level load");

        return lines;
    }

    private static string DescribeSequence(ModelDescriptor d, int sequence)
    {
        if (sequence < 0 || sequence >= d.Sequences.Count)
            return string.Empty;

        ModelSequence s = d.Sequences[sequence];

        return $"  -> '{s.Label}' ({(s.ActivityName.Length > 0 ? s.ActivityName : "no activity")})" +
               $" {s.DefaultDuration:0.000}s {s.Events.Count} event(s)";
    }

    /// <summary>
    /// The names worth asking a viewmodel about: the activity every HL2 v_
    /// model tags its fire with, and a label none of them share.
    /// </summary>
    public static readonly ImmutableArray<string> ViewModelProbes =
    [
        "ACT_VM_PRIMARYATTACK",
        "ACT_VM_IDLE",
        "ACT_VM_DRAW",
        "fire01",
        "idle",
    ];
}
