using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// <c>cs_cl_boneprobe</c>: the two arms of the client skeleton fixture the live
/// gate's viewmodel-merge step drives, managed since the skeleton is (plan
/// physics_bones_native_zero 1.2, client half). The native probe's dump and
/// replay arms were an oracle for the native skeleton and went with it; its
/// layer, scale and merge-by-index arms are cs_setlayer, cs_setscale and
/// cs_bonemerge.
///
/// <para>
/// <c>mergeauto &lt;own&gt; &lt;source&gt;</c>: the LAST client standalone whose
/// studio name contains <c>own</c> merges from the one before it (the same
/// model twice: the viewmodel case, every bone name matching), or from the
/// last one matching <c>source</c> for two different models -- the native
/// arm's rule, by the studio header's own name.
/// </para>
///
/// <para>
/// <c>report</c>: three SS6CGATE merge rows about the merging entity with the
/// most bones -- its bone count, how many its map matched by name, and how
/// many hold the source's matrix exactly. Numbers, no verdict: the thresholds
/// are tools/live-gate's.
/// </para>
/// </summary>
[ConCommand("cs_cl_boneprobe",
    Help = "SourceSharp: the client skeleton fixture -- `mergeauto <ownModel> <srcModel>`, `report`")]
public sealed class ClientBoneProbeCommand(ClientSkeletons skeletons, IClientStandalones standalones, ILog log)
    : IConCommand
{
    public void Execute(string args)
    {
        string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length >= 3 && words[0].Equals("mergeauto", StringComparison.OrdinalIgnoreCase))
        {
            if (!MergeAuto(words[1], words[2]))
                log.Msg($"[SourceSharp] cs_cl_boneprobe mergeauto: no '{words[1]}' and '{words[2]}' pair on this side\n");
            return;
        }

        if (words.Length >= 1 && words[0].Equals("report", StringComparison.OrdinalIgnoreCase))
        {
            ReportMerge();
            return;
        }

        log.Msg("usage: cs_cl_boneprobe mergeauto <ownModel> <srcModel> | report\n");
    }

    private bool MergeAuto(string own, string source)
    {
        ClientStandaloneEntity? ownEnt = null;
        ClientStandaloneEntity? srcEnt = null;
        bool sameModel = string.Equals(own, source, StringComparison.OrdinalIgnoreCase);

        foreach (ClientStandaloneEntity entity in standalones.Live())
        {
            string name = skeletons.StudioName(entity.Handle);

            if (name.Length == 0)
                continue;

            if (name.Contains(own, StringComparison.OrdinalIgnoreCase))
            {
                // The previous `own` becomes the source when the two patterns
                // name the same model: the two the caller just spawned, in the
                // order they arrived.
                if (sameModel)
                    srcEnt = ownEnt;

                ownEnt = entity;
                continue;
            }

            if (name.Contains(source, StringComparison.OrdinalIgnoreCase))
                srcEnt = entity;
        }

        if (ownEnt is null || srcEnt is null || ownEnt.Handle == srcEnt.Handle)
            return false;

        skeletons.SetBoneMergeSource(ownEnt.Handle, srcEnt.Index);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp] cs_cl_boneprobe mergeauto: entity {ownEnt.Index} ('{own}') merges from {srcEnt.Index}\n"));
        return true;
    }

    private void ReportMerge()
    {
        // THE FOLLOWER WITH THE MOST BONES: the map's own merging entity (the
        // local view model, one bone) comes first in list order and is not the
        // pair the step set up.
        ClientStandaloneEntity? follower = null;
        BoneEntityState? state = null;
        int bones = 0;

        foreach (ClientStandaloneEntity entity in standalones.Live())
        {
            if (!skeletons.States.TryGet(entity.Handle, out BoneEntityState? s) || s is null || s.BoneMergeSource < 0
                || !skeletons.IsPoseable(entity.Handle))
                continue;

            StandaloneBones? built = skeletons.BoneMatrices(entity.Handle);

            if (built is not null && built.Count > bones)
            {
                bones = built.Count;
                follower = entity;
                state = s;
            }
        }

        if (follower is null || state is null)
        {
            log.Msg("SS6CGATE\tmerge\tbones\t0\n");
            return;
        }

        StandaloneBones? own = skeletons.BoneMatrices(follower.Handle);
        EntityHandle? sourceHandle = standalones.GetByIndex(state.BoneMergeSource)?.Handle;
        StandaloneBones? source = sourceHandle is { } h ? skeletons.BoneMatrices(h) : null;

        if (own is null)
        {
            log.Msg("SS6CGATE\tmerge\tbones\t0\n");
            return;
        }

        int matched = 0, follows = 0;

        for (int i = 0; i < state.MergeBoneMap.Length; i++)
        {
            int sourceBone = state.MergeBoneMap[i];

            if (sourceBone < 0 || sourceBone >= StudioConstants.MAXSTUDIOBONES)
                continue;

            matched++;

            if (source is not null && i < own.Count && sourceBone < source.Count
                && own.BoneToWorld.AsSpan(i * 12, 12).SequenceEqual(source.BoneToWorld.AsSpan(sourceBone * 12, 12)))
                follows++;
        }

        log.Msg($"SS6CGATE\tmerge\tbones\t{own.Count}\n");
        log.Msg($"SS6CGATE\tmerge\tmatched\t{matched}\n");
        log.Msg($"SS6CGATE\tmerge\tfollows\t{follows}\n");
    }
}
