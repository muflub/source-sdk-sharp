using SourceSharp.Abi;
using SourceSharp.Shared.Bones;
using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// The heavy's arms as the shipped <c>c_heavy_arms.mdl</c> actually tags it:
/// the minigun's poses, read through the production first-match activity
/// lookup (<see cref="ModelDescriptor.LookupSequenceByActivity"/>), are the
/// indexes both sides of the crossing must answer — idle <c>m_idle</c>, the
/// looping fire pose <c>m_fire</c>, the held spin pose <c>m_spool_idle</c>,
/// and the wind-up/settle pair <c>m_spool_up</c>/<c>m_spool_down</c>. The
/// tags are the role-prefixed ones (<c>ACT_PRIMARY_*</c>,
/// <c>ACT_PRIMARY_ATTACK_STAND_*</c>), which is why the viewmodel's rule
/// translates the plain <c>ACT_VM_*</c> ask first, exactly like stock's
/// <c>TranslateViewmodelHandActivity</c>.
/// </summary>
public sealed class HeavyArmsSequences
{
    private const string Mdl = "models/weapons/c_models/c_heavy_arms.mdl";

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_idle_ask_answers_the_heavys_idle_pose()
    {
        Assert.Equal(20, Descriptor().LookupSequenceByActivity("ACT_PRIMARY_VM_IDLE"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_fire_ask_answers_the_looping_fire_pose()
    {
        Assert.Equal(21, Descriptor().LookupSequenceByActivity("ACT_PRIMARY_VM_PRIMARYATTACK"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_secondary_ask_answers_the_held_spin_pose()
    {
        Assert.Equal(25, Descriptor().LookupSequenceByActivity("ACT_PRIMARY_VM_SECONDARYATTACK"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_windup_and_settle_asks_answer_the_spool_up_and_spool_down_poses()
    {
        ModelDescriptor descriptor = Descriptor();
        Assert.Equal(22, descriptor.LookupSequenceByActivity("ACT_PRIMARY_ATTACK_STAND_PREFIRE"));
        Assert.Equal(23, descriptor.LookupSequenceByActivity("ACT_PRIMARY_ATTACK_STAND_POSTFIRE"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void An_untranslated_ask_answers_nothing_so_the_role_rule_is_load_bearing()
    {
        // The mdl tags the minigun's poses ACT_PRIMARY_* only; a plain ACT_VM_*
        // ask (a side that skipped the rule's translate) must answer none, not
        // a different pose — the same -1 on both sides is what keeps the
        // predicted sequence index a pure function of the model.
        Assert.Equal(-1, Descriptor().LookupSequenceByActivity("ACT_VM_PRIMARYATTACK"));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_miniguns_poses_are_plain_single_cell_anims()
    {
        StudioHdr hdr = LoadHeavyArms();

        // No DELTA (accumulate onto the caller's pose), no POST (blend after
        // the main pose), no AUTOPLAY, and no autolayers: each of the six
        // answers one pose cell, which is why a plain single-cell decode is
        // what both sides need, and why the gun's lift rides the sequence's
        // own frames rather than a layer.
        for (int i = 20; i <= 25; i++)
        {
            MstudioSeqDesc d = hdr.SeqDesc(i);
            Assert.Equal(0, d.Flags & (StudioConstants.STUDIO_DELTA
                                       | StudioConstants.STUDIO_POST
                                       | StudioConstants.STUDIO_AUTOPLAY));
            Assert.Equal(0, d.NumAutoLayers);
            Assert.Equal(1, d.NumBlends);
            Assert.Equal(1, d.GroupSize0);
            Assert.Equal(1, d.GroupSize1);
        }
    }

    private static ModelDescriptor Descriptor()
    {
        StudioHdr hdr = LoadHeavyArms();

        var sequences = new List<ModelSequence>(hdr.NumSequences);
        for (int i = 0; i < hdr.NumSequences; i++)
        {
            MstudioSeqDesc d = hdr.SeqDesc(i);
            sequences.Add(new ModelSequence(i, d.Label, d.ActivityName, d.Flags, 0f,
                                            Array.Empty<ModelSequenceEvent>(), d.ActWeight));
        }

        return ModelDescriptor.Create(0, Array.Empty<string>(), Array.Empty<Vector>(), sequences);
    }

    private static StudioHdr LoadHeavyArms()
    {
        string content = ShippedContent.Dir;
        byte[] bytes = ShippedContent.ReadModel(content, Mdl)
                       ?? throw new InvalidOperationException($"no {Mdl} under {content}");

        // The shipped arms mdl chains to its includes; resolve them through the
        // same GAME-path lookup so TryCreate sees what the engine's filesystem
        // would hand it.
        if (!StudioHdr.TryCreate(bytes,
                name => ShippedContent.ReadModel(content,
                    name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? name : name + ".mdl"),
                out StudioHdr? hdr) || hdr is null)
            throw new InvalidOperationException($"{Mdl}: StudioHdr refused it");

        return hdr;
    }
}
