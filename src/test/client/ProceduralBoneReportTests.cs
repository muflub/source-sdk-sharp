using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two probes that make procedural bones verifiable at all.
///
/// <para>
/// The defect they answer is not in the bone code: it is that a model with no
/// procedural bones draws identically whether the feature works or not, and
/// most models have none. So "is it working" and "can this model tell" shared
/// one answer, and a screenshot of the wrong model said the feature was fine
/// either way. <c>cs_procbones</c> separates them and <c>cs_playseq</c> bends
/// the model far enough from its reference pose that a helper bone has
/// something to do.
/// </para>
/// </summary>
public class ProceduralBoneReportTests
{
    private static readonly EntityHandle Bony = new(0x0100_0050u);
    private static readonly EntityHandle Plain = new(0x0100_0051u);
    private static readonly EntityHandle Gone = new(0x0100_0052u);

    [Fact]
    public void AModelWithProceduralBonesIsNamedWithItsCounts()
    {
        var world = new Models()
            .Wearing(Bony, "ss_barrel", "models/gman_high.mdl", bones: 70, computed: 14, jiggle: 0);

        var lines = ClientProceduralBones.Report(world.Live(), world, string.Empty);

        Assert.True(lines.Any(l => l.Contains("models/gman_high.mdl") && l.Contains("14 procedural")),
                    "the model with procedural bones is named, with how many it has");
        Assert.True(lines.Any(l => l.Contains("1 with procedural bone(s)")),
                    "and counted in the tally");
    }

    [Fact]
    public void AModelWithNoneIsCountedButNotListed()
    {
        var world = new Models()
            .Wearing(Plain, "ss_barrel", "models/props_c17/oildrum001.mdl", bones: 1, computed: 0, jiggle: 0);

        var lines = ClientProceduralBones.Report(world.Live(), world, string.Empty);

        Assert.True(!lines.Any(l => l.Contains("oildrum")),
                    "a model with no such bones is not worth a line of its own");
        Assert.True(lines.Any(l => l.Contains("1 with none of them")),
                    "but is counted, so the report says how much it looked at");
    }

    /// <summary>
    /// The line that matters most, and the one a report without it invites
    /// somebody to get wrong.
    /// </summary>
    [Fact]
    public void ALevelWithNothingToShowSaysSoOutLoud()
    {
        var world = new Models()
            .Wearing(Plain, "ss_barrel", "models/props_c17/oildrum001.mdl", bones: 1, computed: 0, jiggle: 0);

        var lines = ClientProceduralBones.Report(world.Live(), world, string.Empty);

        Assert.True(lines.Any(l => l.Contains("cannot tell working from broken")),
                    "a level with no procedural bones in it says that a screenshot here proves nothing");
    }

    [Fact]
    public void ALevelWithSomethingToShowDoesNotSaySo()
    {
        var world = new Models()
            .Wearing(Bony, "ss_barrel", "models/gman_high.mdl", bones: 70, computed: 14, jiggle: 0);

        var lines = ClientProceduralBones.Report(world.Live(), world, string.Empty);

        Assert.True(!lines.Any(l => l.Contains("cannot tell working from broken")),
                    "and a level that does have one does not");
    }

    /// <summary>
    /// An IK chain is invisible on a model with none, exactly as a procedural
    /// bone is, so the report answers both questions about the same models.
    /// </summary>
    [Fact]
    public void AModelWithIkChainsAndNoProceduralBonesIsStillWorthALine()
    {
        var world = new Models()
            .Wearing(Bony, "ss_barrel", "models/humans/group01/male_04.mdl",
                     bones: 68, computed: 0, jiggle: 0, ik: 4);

        var lines = ClientProceduralBones.Report(world.Live(), world, string.Empty);

        Assert.True(lines.Any(l => l.Contains("4 IK chain(s)")),
                    "a model whose only special bones are IK chains is named, with how many");
        Assert.True(!lines.Any(l => l.Contains("cannot tell working from broken")),
                    "and the level is not written off as unable to show anything");
    }

    [Fact]
    public void ADormantEntityIsNotAskedAboutItsModel()
    {
        var world = new Models()
            .Wearing(Gone, "ss_barrel", "models/gman_high.mdl", bones: 70, computed: 14, jiggle: 0)
            .Dormant(Gone);

        var lines = ClientProceduralBones.Report(world.Live(), world, string.Empty);

        Assert.True(world.BoneAsks == 0,
                    "a dormant entity answers about the last model that reached it, which is a " +
                    "bone count for the wrong file");
        Assert.True(lines.Any(l => l.Contains("0 live entit")),
                    "and is not counted as looked at");
    }

    [Fact]
    public void PlayingASequenceByNameReachesTheRenderer()
    {
        var world = new Models()
            .Wearing(Bony, "ss_barrel", "models/gman_high.mdl", bones: 70, computed: 14, jiggle: 0)
            .Carrying(Bony, "lintpick", 4);

        var lines = ClientProceduralBones.Play(world.Live(), world, "ss_barrel", "lintpick", 1f);

        Assert.True(world.Played.Count == 1 && world.Played[0].sequence == 4,
                    "the name was resolved and the sequence handed to the renderer");
        Assert.True(world.Played[0].restart,
                    "asked to RESTART, because a probe saying 'play this' twice means twice");
        Assert.True(lines.Any(l => l.Contains("as sequence 4")), "and the report says which");
    }

    /// <summary>
    /// The failure that cost this project months, said out loud rather than
    /// returned silently.
    /// </summary>
    [Fact]
    public void ASequenceNameNoModelCarriesIsReportedRatherThanSwallowed()
    {
        var world = new Models()
            .Wearing(Plain, "ss_barrel", "models/props_c17/oildrum001.mdl", bones: 1, computed: 0, jiggle: 0);

        var lines = ClientProceduralBones.Play(world.Live(), world, "ss_barrel", "lintpick", 1f);

        Assert.True(world.Played.Count == 0, "nothing was played");
        Assert.True(lines.Any(l => l.Contains("NO SEQUENCE 'lintpick'")),
                    "and the report says which name nothing answered to");
    }

    /// <summary>A world of models, written by this test.</summary>
    private sealed class Models : IEngineEntityRender, IClientStandalones
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public string? GetModelKeyValueText(string modelName) => null;

        private readonly Dictionary<uint, (string cls, string model, int bones, int computed, int jiggle, int ik)> _wearing = new();
        private readonly Dictionary<(uint, string), int> _sequences = new();
        private readonly HashSet<uint> _dormant = new();
        private readonly List<ClientStandaloneEntity> _live = new();

        public readonly List<(EntityHandle entity, int sequence, float rate, bool restart)> Played = new();

        public int BoneAsks;

        public Models Wearing(EntityHandle entity, string className, string model,
                              int bones, int computed, int jiggle, int ik = 0)
        {
            _wearing[entity.Value] = (className, model, bones, computed, jiggle, ik);
            _live.Add(new Stub(this, className) { Handle = entity });
            return this;
        }

        public Models Carrying(EntityHandle entity, string sequenceName, int sequence)
        {
            _sequences[(entity.Value, sequenceName)] = sequence;
            return this;
        }

        public Models Dormant(EntityHandle entity)
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

        public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle,
                                   out int ikChains)
        {
            BoneAsks++;
            computed = jiggle = ikChains = 0;

            if (!_wearing.TryGetValue(entity.Value, out var worn))
                return 0;

            computed = worn.computed;
            jiggle = worn.jiggle;
            ikChains = worn.ik;
            return worn.bones;
        }

        public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart) => throw Refuse(nameof(SetAnimLayer));
        public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle) => throw Refuse(nameof(AnimLayer));

        public string GetModelName(EntityHandle entity)
            => _wearing.TryGetValue(entity.Value, out var worn) ? worn.model : string.Empty;

        public int LookupSequence(EntityHandle entity, string name)
            => _sequences.TryGetValue((entity.Value, name), out int s) ? s : -1;

        public void PlaySequenceAt(EntityHandle entity, int sequence, float playbackRate, float cycle) => throw Refuse(nameof(PlaySequenceAt));

        public void PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart)
            => Played.Add((entity, sequence, playbackRate, restart));

        public void SetCycle(EntityHandle entity, float cycle) { }

        private sealed class Stub : ClientStandaloneEntity
        {
            private readonly string _className;

            public Stub(IEngineEntityRender render, string className) : base(render)
                => _className = className;

            public override string ClassName => _className;
        }

        private static Exception Refuse(string member)
            => new InvalidOperationException($"the report read {member}, which is not its business");

        public int GetTextureFrame(EntityHandle entity) => throw Refuse(nameof(GetTextureFrame));
        public int LookupSequenceByActivity(EntityHandle entity, string activityName) => throw Refuse(nameof(LookupSequenceByActivity));
        public float SequenceDuration(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceDuration));
        public float SequenceGroundSpeed(EntityHandle entity, int sequence) => throw Refuse(nameof(SequenceGroundSpeed));
        public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts) => throw Refuse(nameof(SequenceState));
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
        public void SetSkin(EntityHandle entity, int skin) => throw Refuse(nameof(SetSkin));
        public void SetTextureFrame(EntityHandle entity, int frame) => throw Refuse(nameof(SetTextureFrame));
        public void SetTransmitAlways(EntityHandle entity, bool always) => throw Refuse(nameof(SetTransmitAlways));
        public bool DetectInSkybox(EntityHandle entity) => throw Refuse(nameof(DetectInSkybox));
        public void SetVisible(EntityHandle entity, bool visible) => throw Refuse(nameof(SetVisible));
    }
}
