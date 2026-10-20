/*
 * How an animation kind becomes a sequence index — the resolution the fire
 * animation bug lived in.
 *
 * The live disease: WeaponAnimNames tried sequence NAMES only, and no HL2 v_
 * model calls its fire 'fire' with one exception — the SMG's is 'fire01', the
 * pulse rifle's 'IR_fire', the crowbar has no fire at all. Every primary
 * lookup answered -1, SendAnim sent nothing, and no log said so. The fix
 * resolves ACTIVITIES first ('ACT_VM_PRIMARYATTACK'), which every model tags,
 * through a deterministic first-match native lookup; names stay as fallback.
 *
 * What these tests hold still is the RESOLUTION ORDER and the fallbacks —
 * the parts that, wrong, are one silent misprediction per trigger pull.
 */

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

public class WeaponAnimResolveTests
{
    private static readonly EntityHandle ViewModel = new(0x0100_0040u);

    //-------------------------------------------------------------------------
    // A viewmodel as a pair of lookup tables. Only the two lookups answer;
    // everything else on the render interface throws, because a resolver that
    // reads anything else is a resolver this test must fail.
    //-------------------------------------------------------------------------
    private sealed class SequenceTable : IEngineEntityRender
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public string? GetModelKeyValueText(string modelName) => null;

        public readonly Dictionary<string, int> Activities = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Names = new(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> ActivityAsks = new();
        public readonly List<string> NameAsks = new();

        public int LookupSequenceByActivity(EntityHandle entity, string activityName)
        {
            ActivityAsks.Add(activityName);
            return Activities.TryGetValue(activityName, out int sequence) ? sequence : -1;
        }

        public int LookupSequence(EntityHandle entity, string name)
        {
            NameAsks.Add(name);
            return Names.TryGetValue(name, out int sequence) ? sequence : -1;
        }

        private static Exception Refuse(string member)
            => new InvalidOperationException($"the resolver read {member}, which is not a lookup");

        public string GetModelName(EntityHandle entity) => throw Refuse(nameof(GetModelName));
        public int GetTextureFrame(EntityHandle entity) => throw Refuse(nameof(GetTextureFrame));
        public float SequenceDuration(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceDuration));
        public float SequenceGroundSpeed(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceGroundSpeed));
        public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts) => throw Refuse(nameof(SequenceState));
        public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains) => throw Refuse(nameof(ProceduralBones));
        public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart) => throw Refuse(nameof(SetAnimLayer));
        public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle) => throw Refuse(nameof(AnimLayer));
        public int LookupAttachment(EntityHandle entity, string name) => throw Refuse(nameof(LookupAttachment));
        public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles) => throw Refuse(nameof(GetAttachment));
        public int AttachmentCount(EntityHandle entity) => throw Refuse(nameof(AttachmentCount));
        public string AttachmentName(EntityHandle entity, int attachment) => throw Refuse(nameof(AttachmentName));
        public int BoneCount(EntityHandle entity, out int cached) => throw Refuse(nameof(BoneCount));
        public StandaloneBones? BoneMatrices(EntityHandle entity) => throw Refuse(nameof(BoneMatrices));
        public bool IsPoseable(EntityHandle entity) => throw Refuse(nameof(IsPoseable));
        public string StudioName(EntityHandle entity) => throw Refuse(nameof(StudioName));
        public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) => throw Refuse(nameof(SetCustomDraw));
        public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) => throw Refuse(nameof(SetBlending));
        public void SetShadowCast(EntityHandle entity, ShadowCastChoice choice) => throw Refuse(nameof(SetShadowCast));
        public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked) => throw Refuse(nameof(ShadowCast));
        public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f) => throw Refuse(nameof(SetFade));
        public void SetModel(EntityHandle entity, string model) => throw Refuse(nameof(SetModel));
        public void SetOverlayModel(EntityHandle entity, int modelIndex) => throw Refuse(nameof(SetOverlayModel));
        public void SetModelScale(EntityHandle entity, float scale) => throw Refuse(nameof(SetModelScale));
        public void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex) => throw Refuse(nameof(SetBoneMergeSource));
        public void SetSequence(EntityHandle entity, int sequence) => throw Refuse(nameof(SetSequence));
        public void SetViewModel(EntityHandle entity, bool enable) => throw Refuse(nameof(SetViewModel));
        public void PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart) => throw Refuse(nameof(PlaySequence));
        public void SetSkin(EntityHandle entity, int skin) => throw Refuse(nameof(SetSkin));
        public void SetTextureFrame(EntityHandle entity, int frame) => throw Refuse(nameof(SetTextureFrame));
        public void SetTransmitAlways(EntityHandle entity, bool always) => throw Refuse(nameof(SetTransmitAlways));
        public bool DetectInSkybox(EntityHandle entity) => throw Refuse(nameof(DetectInSkybox));
        public void SetVisible(EntityHandle entity, bool visible) => throw Refuse(nameof(SetVisible));
        public int PoseParameterCount(EntityHandle entity) => throw Refuse(nameof(PoseParameterCount));
        public int LookupPoseParameter(EntityHandle entity, string name) => throw Refuse(nameof(LookupPoseParameter));
        public string PoseParameterName(EntityHandle entity, int parameter) => throw Refuse(nameof(PoseParameterName));
        public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end) => throw Refuse(nameof(PoseParameterRange));
        public float SetPoseParameter(EntityHandle entity, int parameter, float value) => throw Refuse(nameof(SetPoseParameter));
        public float GetPoseParameter(EntityHandle entity, int parameter) => throw Refuse(nameof(GetPoseParameter));
        public bool IsDormant(EntityHandle entity) => throw Refuse(nameof(IsDormant));
        public int SequenceEventCount(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceEventCount));
        public bool SequenceEvent(EntityHandle entity, int sequence, int index, out float cycle, out int eventId, out int type, out string name, out string options) => throw Refuse(nameof(SequenceEvent));
    }

    //-------------------------------------------------------------------------
    // The order.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheActivityWinsOverTheName()
    {
        // v_shotgun in person: 'fire01' is the primary (1), but the model ALSO
        // has a stray sequence NAMED 'fire' (13, tagged ACT_RANGE_ATTACK_SHOTGUN,
        // a world animation). Resolving names first would play the stray.
        var model = new SequenceTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 1;
        model.Names["fire"] = 13;

        Assert.True(WeaponAnimNames.Resolve(model, ViewModel, WeaponAnim.Primary) == 1,
            "the activity-tagged primary wins over a sequence merely NAMED fire");
    }

    [Fact]
    public void TheCrowbarsSwingResolvesThroughTheHitcenterFallback()
    {
        // v_crowbar has no ACT_VM_PRIMARYATTACK and no fire-shaped name at
        // all; its swings are tagged ACT_VM_HITCENTER. Without this fallback
        // the crowbar's trigger is exactly as silent as the bug being fixed.
        var model = new SequenceTable();
        model.Activities["ACT_VM_HITCENTER"] = 4;
        model.Activities["ACT_VM_IDLE"] = 0;

        Assert.True(WeaponAnimNames.Resolve(model, ViewModel, WeaponAnim.Primary) == 4,
            "a model with no PRIMARYATTACK resolves its swing through ACT_VM_HITCENTER");
    }

    [Fact]
    public void TheGrenadesThrowResolvesThroughTheThrowFallback()
    {
        var model = new SequenceTable();
        model.Activities["ACT_VM_THROW"] = 3;

        Assert.True(WeaponAnimNames.Resolve(model, ViewModel, WeaponAnim.Primary) == 3,
            "a model whose primary is a throw resolves through ACT_VM_THROW");
    }

    [Fact]
    public void AnUntaggedModelStillResolvesByName()
    {
        // The fallback the old behaviour becomes: a model with no activity
        // tags at all keeps working exactly as before the fix.
        var model = new SequenceTable();
        model.Names["reload"] = 7;

        Assert.True(WeaponAnimNames.Resolve(model, ViewModel, WeaponAnim.Reload) == 7,
            "a model with untagged sequences resolves by sequence name");
    }

    [Fact]
    public void NothingAnywhereIsMinusOne()
    {
        var model = new SequenceTable();

        Assert.True(WeaponAnimNames.Resolve(model, ViewModel, WeaponAnim.Primary) == -1,
            "a model with neither the activity nor the name answers -1, the value the machine survives");
    }

    //-------------------------------------------------------------------------
    // The lists themselves, pinned: these strings are half of a contract whose
    // other half is compiled into nine shipped models.
    //-------------------------------------------------------------------------

    [Fact]
    public void ThePrimaryActivityListIsStocksInOrder()
    {
        Assert.True(WeaponAnimNames.ActivitiesFor(WeaponAnim.Primary)
                .SequenceEqual(new[] { "ACT_VM_PRIMARYATTACK", "ACT_VM_HITCENTER", "ACT_VM_THROW" }),
            "primary tries PRIMARYATTACK, then the crowbar's HITCENTER, then the grenade's THROW — " +
            "most common first, because the first hit ends the walk");
    }

    [Fact]
    public void EveryAnimationKindHasAnActivity()
    {
        foreach (WeaponAnim anim in Enum.GetValues<WeaponAnim>())
        {
            Assert.True(WeaponAnimNames.ActivitiesFor(anim).Length > 0,
                $"{anim} resolves through at least one activity");
        }
    }
}
