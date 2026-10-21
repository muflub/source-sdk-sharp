using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed half of animation layers: naming them, pushing them, and reading
/// back what the shell really has.
///
/// <para>
/// The ORDER rule and the active/inactive rule live in
/// <c>anim_layer_rule.h</c> and are pinned by <c>anim_layer_rule_test</c> —
/// there is no engine in them and a gtest can hold them still. What is pinned
/// here is the part that is C#'s: refusing a slot that does not exist, saying a
/// sequence name out loud when nothing answers to it, and READING BACK rather
/// than reporting what was pushed.
/// </para>
/// </summary>
public class AnimationLayerTests
{
    private static readonly EntityHandle Actor = new(0x0100_0060u);

    /// <summary>
    /// The slot count the command offers is the one the skeleton holds: the
    /// native header it was compared with went with the native skeleton (plan
    /// physics_bones_native_zero 1.2), and the table is AnimLayerRule's now.
    /// </summary>
    [Fact]
    public void TheSlotCountAgreesWithTheNativeHeader()
    {
        Assert.Equal(SourceSharp.Shared.Bones.AnimLayerRule.MaxLayers, ClientAnimationLayers.MaxLayers);
    }

    [Fact]
    public void ASequenceIsResolvedByNameAndPushedIntoTheSlot()
    {
        var world = new Actors().Carrying(Actor, "gesture_wave", 12);

        var lines = ClientAnimationLayers.Set(world.Live(), world, "ss_barrel",
                                              slot: 2, "gesture_wave", weight: 0.5f, order: 3);

        Assert.True(world.Pushed.Count == 1, "one layer was pushed");
        Assert.True(world.Pushed[0].slot == 2 && world.Pushed[0].sequence == 12,
                    "into the slot asked for, carrying the sequence the name resolved to");
        Assert.True(world.Pushed[0].weight == 0.5f && world.Pushed[0].order == 3,
                    "with the weight and order asked for");
        Assert.True(lines.Any(l => l.Contains("as sequence 12")), "and the report says which");
    }

    [Fact]
    public void OffClearsTheSlotWithoutKnowingWhatWasInIt()
    {
        var world = new Actors().Carrying(Actor, "gesture_wave", 12);

        ClientAnimationLayers.Set(world.Live(), world, "ss_barrel", slot: 2, "off",
                                  weight: 1f, order: 0);

        Assert.True(world.Pushed.Count == 1 && world.Pushed[0].sequence < 0,
                    "a negative sequence is what turns a slot off, and 'off' does not need a lookup");
        Assert.True(world.Lookups.Count == 0,
                    "so no sequence name was resolved for it");
    }

    [Fact]
    public void ASlotThatDoesNotExistIsRefusedOutLoud()
    {
        var world = new Actors().Carrying(Actor, "gesture_wave", 12);

        var lines = ClientAnimationLayers.Set(world.Live(), world, "ss_barrel",
                                              slot: 99, "gesture_wave", weight: 1f, order: 0);

        Assert.True(world.Pushed.Count == 0, "nothing was pushed");
        Assert.True(lines.Any(l => l.Contains("no such layer slot 99")),
                    "and the refusal says so — the shell would refuse it silently, and a gesture " +
                    "that never appears is how that would present");
    }

    [Fact]
    public void ASequenceNameNoModelCarriesIsReportedRatherThanSwallowed()
    {
        var world = new Actors();      // knows no sequences at all

        var lines = ClientAnimationLayers.Set(world.Live(), world, "ss_barrel",
                                              slot: 0, "gesture_wave", weight: 1f, order: 0);

        Assert.True(world.Pushed.Count == 0, "nothing was pushed");
        Assert.True(lines.Any(l => l.Contains("NO SEQUENCE 'gesture_wave'")),
                    "and the name nothing answered to is named");
    }

    /// <summary>
    /// The whole reason the read-back slot exists.
    /// </summary>
    [Fact]
    public void TheReportReadsTheSHELLRatherThanWhatWasPushed()
    {
        var world = new Actors().Holding(Actor, slot: 1, sequence: 7, weight: 0.25f,
                                         order: 2, cycle: 0.4f);

        var lines = ClientAnimationLayers.Report(world.Live(), world, string.Empty);

        Assert.True(lines.Any(l => l.Contains("sequence 7") && l.Contains("cycle 0.400")),
                    "the report prints what the shell holds, including a cycle nothing pushed");
        Assert.True(world.Pushed.Count == 0,
                    "and nothing was pushed at all, so this cannot be an echo of a push");
    }

    [Fact]
    public void ALevelWhereNothingLayersSaysSoOutLoud()
    {
        var world = new Actors().Carrying(Actor, "gesture_wave", 12);

        var lines = ClientAnimationLayers.Report(world.Live(), world, string.Empty);

        Assert.True(lines.Any(l => l.Contains("nothing on this hierarchy fills a layer slot yet")),
                    "because a screenshot of a level where nothing is layering proves nothing " +
                    "about layers, and somebody will take one");
    }

    [Fact]
    public void ADormantEntityIsNotAskedWhatItIsCarrying()
    {
        var world = new Actors()
            .Holding(Actor, slot: 0, sequence: 7, weight: 1f, order: 0, cycle: 0.1f)
            .Dormant(Actor);

        ClientAnimationLayers.Report(world.Live(), world, string.Empty);

        Assert.True(world.Reads == 0,
                    "a dormant entity answers with the last values that reached it, which is a " +
                    "layer table for a model nobody is holding");
    }

    /// <summary>A shell that holds layers, written by this test.</summary>
    private sealed class Actors : IEngineEntityRender, IClientStandalones
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public string? GetModelKeyValueText(string modelName) => null;

        private readonly Dictionary<(uint, string), int> _sequences = new();
        private readonly Dictionary<(uint, int), (int sequence, float weight, int order, float cycle)> _held = new();
        private readonly HashSet<uint> _dormant = new();
        private readonly List<ClientStandaloneEntity> _live = new();

        public readonly List<(EntityHandle entity, int slot, int sequence, float weight, float rate, int order, bool restart)> Pushed = new();
        public readonly List<string> Lookups = new();

        public int Reads;

        public Actors()
        {
            _live.Add(new Stub(this, "ss_barrel") { Handle = Actor });
        }

        public Actors Carrying(EntityHandle entity, string sequenceName, int sequence)
        {
            _sequences[(entity.Value, sequenceName)] = sequence;
            return this;
        }

        public Actors Holding(EntityHandle entity, int slot, int sequence, float weight,
                              int order, float cycle)
        {
            _held[(entity.Value, slot)] = (sequence, weight, order, cycle);
            return this;
        }

        public Actors Dormant(EntityHandle entity)
        {
            _dormant.Add(entity.Value);
            return this;
        }

        public IReadOnlyList<ClientStandaloneEntity> Live() => _live;

        // Row C4's index lookup. These doubles are about animation and the HUD,
        // not about what a gib hit, so they answer "not one of ours" for every
        // index -- which is the honest answer for a table nothing was put in.
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;

        public bool IsDormant(EntityHandle entity) => _dormant.Contains(entity.Value);

        public int LookupSequence(EntityHandle entity, string name)
        {
            Lookups.Add(name);
            return _sequences.TryGetValue((entity.Value, name), out int s) ? s : -1;
        }

        public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight,
                                 float playbackRate, int order, bool restart)
            => Pushed.Add((entity, slot, sequence, weight, playbackRate, order, restart));

        public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight,
                             out int order, out float cycle)
        {
            Reads++;
            sequence = -1;
            weight = 0f;
            order = 0;
            cycle = 0f;

            if (_held.TryGetValue((entity.Value, slot), out var held))
            {
                sequence = held.sequence;
                weight = held.weight;
                order = held.order;
                cycle = held.cycle;
            }

            int active = 0;

            foreach (var ((who, _), row) in _held)
            {
                if (who == entity.Value && row.sequence >= 0)
                    active++;
            }

            return active;
        }

        public string GetModelName(EntityHandle entity) => "models/gman_high.mdl";

        private sealed class Stub : ClientStandaloneEntity
        {
            private readonly string _className;

            public Stub(IEngineEntityRender render, string className) : base(render)
                => _className = className;

            public override string ClassName => _className;
        }

        private static Exception Refuse(string member)
            => new InvalidOperationException($"the layer policy read {member}, which is not its business");

        public int GetTextureFrame(EntityHandle entity) => throw Refuse(nameof(GetTextureFrame));
        public int LookupSequenceByActivity(EntityHandle entity, string activityName) => throw Refuse(nameof(LookupSequenceByActivity));
        public float SequenceDuration(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceDuration));
        public float SequenceGroundSpeed(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceGroundSpeed));
        public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts) => throw Refuse(nameof(SequenceState));
        public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains) => throw Refuse(nameof(ProceduralBones));
        public int LookupAttachment(EntityHandle entity, string name) => throw Refuse(nameof(LookupAttachment));
        public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles) => throw Refuse(nameof(GetAttachment));
        public int AttachmentCount(EntityHandle entity) => throw Refuse(nameof(AttachmentCount));
        public string AttachmentName(EntityHandle entity, int attachment) => throw Refuse(nameof(AttachmentName));
        public int BoneCount(EntityHandle entity, out int cached) => throw Refuse(nameof(BoneCount));
        public StandaloneBones? BoneMatrices(EntityHandle entity) => throw Refuse(nameof(BoneMatrices));
        public bool IsPoseable(EntityHandle entity) => throw Refuse(nameof(IsPoseable));
        public string StudioName(EntityHandle entity) => throw Refuse(nameof(StudioName));
        public int PoseParameterCount(EntityHandle entity) => throw Refuse(nameof(PoseParameterCount));
        public int LookupPoseParameter(EntityHandle entity, string name) => throw Refuse(nameof(LookupPoseParameter));
        public string PoseParameterName(EntityHandle entity, int parameter) => throw Refuse(nameof(PoseParameterName));
        public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end) => throw Refuse(nameof(PoseParameterRange));
        public float SetPoseParameter(EntityHandle entity, int parameter, float value) => throw Refuse(nameof(SetPoseParameter));
        public float GetPoseParameter(EntityHandle entity, int parameter) => throw Refuse(nameof(GetPoseParameter));
        public int SequenceEventCount(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceEventCount));
        public bool SequenceEvent(EntityHandle entity, int sequence, int index, out float cycle, out int eventId, out int type, out string name, out string options) => throw Refuse(nameof(SequenceEvent));
        public void SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) => throw Refuse(nameof(SetCustomDraw));
        public void SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) => throw Refuse(nameof(SetBlending));
        public void SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha = 0f) => throw Refuse(nameof(SetFade));
        public void SetModel(EntityHandle entity, string model) => throw Refuse(nameof(SetModel));
        // Added when the shadow lane extended IEngineEntityRender; these doubles
        // predate it and none of their tests reach a shadow.
        public ShadowCast ShadowCast(EntityHandle entity, out ShadowCastChoice asked) => throw Refuse(nameof(ShadowCast));
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
    }
}
