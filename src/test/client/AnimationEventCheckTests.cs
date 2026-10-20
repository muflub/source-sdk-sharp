using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The animation-event check (plan_baseentity.md Phase 5).
///
/// <para>
/// The bug this answers cost months. Weapon animations were resolved by
/// sequence NAME — <c>fire</c>, <c>attack1</c>, <c>shoot</c> — and no HL2 v_
/// model uses any of them: the SMG's is <c>fire01</c>, the pulse rifle's is
/// <c>IR_fire</c>, the crowbar has only <c>hitcenter1</c>. Every lookup
/// answered -1, <c>SendAnim</c> returned silently, and nothing anywhere said
/// so. The same shape of assumption sat underneath the EVENTS — managed code
/// believing a model fires <c>AE_MUZZLEFLASH</c> without ever reading the
/// model.
/// </para>
///
/// <para>
/// <b>What these tests can and cannot prove, said plainly.</b> They pin the
/// CHECKER: given a table of what a model carries, does the comparison reach
/// the right verdict and say it out loud. They prove nothing whatever about any
/// .mdl on disk — the model table here is one this file wrote. Proving
/// something about the real models is <c>cs_animcheck</c>'s job, live, and the
/// declarations in <see cref="WeaponAnimEvents"/> were dumped from the .mdl
/// files rather than guessed. A test that stubbed a model AND asserted the
/// declaration against the same stub would be the twelfth check that cannot
/// fail.
/// </para>
/// </summary>
public class AnimationEventCheckTests
{
    private static readonly EntityHandle ViewModel = new(0x0100_0040u);

    /// <summary>
    /// A view model as its sequence table and its event table, the way
    /// <c>WeaponAnimResolveTests.SequenceTable</c> does it — everything the
    /// check does not legitimately need throws, so a check that reads anything
    /// else is one this test must fail.
    /// </summary>
    private sealed class ModelTable : IEngineEntityRender
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public string? GetModelKeyValueText(string modelName) => null;

        public readonly Dictionary<string, int> Activities = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Names = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sequence index -> the events that sequence fires.</summary>
        public readonly Dictionary<int, List<ModelAnimationEvent>> Events = new();

        public ModelTable Fires(int sequence, string name, float cycle = 0f, string options = "")
        {
            Add(sequence, new ModelAnimationEvent(0, cycle, 0,
                                                  AnimationEventTypes.NewEventSystem, name, options));
            return this;
        }

        public ModelTable FiresNumbered(int sequence, int eventId, float cycle = 0f)
        {
            Add(sequence, new ModelAnimationEvent(0, cycle, eventId, 0, string.Empty, string.Empty));
            return this;
        }

        /// <summary>A sequence that exists and fires nothing — the crowbar's whole table.</summary>
        public ModelTable FiresNothing(int sequence)
        {
            Events.TryAdd(sequence, new List<ModelAnimationEvent>());
            return this;
        }

        private void Add(int sequence, ModelAnimationEvent ev)
        {
            if (!Events.TryGetValue(sequence, out var list))
                Events[sequence] = list = new List<ModelAnimationEvent>();

            list.Add(ev with { Index = list.Count });
        }

        public int LookupSequenceByActivity(EntityHandle entity, string activityName)
            => Activities.TryGetValue(activityName, out int s) ? s : -1;

        public int LookupSequence(EntityHandle entity, string name)
            => Names.TryGetValue(name, out int s) ? s : -1;

        public int SequenceEventCount(EntityHandle entity, int sequence)
            => Events.TryGetValue(sequence, out var list) ? list.Count : 0;

        public bool SequenceEvent(EntityHandle entity, int sequence, int index,
                                  out float cycle, out int eventId, out int type,
                                  out string name, out string options)
        {
            cycle = 0f; eventId = 0; type = 0; name = string.Empty; options = string.Empty;

            if (!Events.TryGetValue(sequence, out var list) || index < 0 || index >= list.Count)
                return false;

            var ev = list[index];
            cycle = ev.Cycle; eventId = ev.EventId; type = ev.Type;
            name = ev.Name; options = ev.Options;
            return true;
        }

        private static Exception Refuse(string member)
            => new InvalidOperationException($"the check read {member}, which is not a model fact");

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
        public int PoseParameterCount(EntityHandle entity) => throw Refuse(nameof(PoseParameterCount));
        public int LookupPoseParameter(EntityHandle entity, string name) => throw Refuse(nameof(LookupPoseParameter));
        public string PoseParameterName(EntityHandle entity, int parameter) => throw Refuse(nameof(PoseParameterName));
        public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end) => throw Refuse(nameof(PoseParameterRange));
        public float SetPoseParameter(EntityHandle entity, int parameter, float value) => throw Refuse(nameof(SetPoseParameter));
        public float GetPoseParameter(EntityHandle entity, int parameter) => throw Refuse(nameof(GetPoseParameter));
        public bool IsDormant(EntityHandle entity) => throw Refuse(nameof(IsDormant));
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
    }

    private static IReadOnlyList<string> Verify(ModelTable model,
                                                params AnimationEventExpectation[] expectations)
        => ModelAnimationCheck.Verify(model, ViewModel, "weapon_test", expectations);

    //-------------------------------------------------------------------------
    // THE CASE THAT WOULD HAVE CAUGHT THE ORIGINAL BUG.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnAnimationThatResolvesToNoSequenceIsCalledOutBeforeAnyEventIs()
    {
        // v_smg1 as it looked to the OLD name-based resolver: the model has a
        // primary attack, tagged ACT_VM_PRIMARYATTACK, and none of the names
        // that resolver tried. Here the table simply has neither, which is
        // what -1 looked like from the inside: a silent nothing.
        var model = new ModelTable();

        var lines = Verify(model, AnimationEventExpectation.Named(WeaponAnim.Primary,
                                                                  WeaponAnimEvents.MuzzleFlash));

        Assert.Contains(lines, l => l.Contains("MISSING SEQUENCE"));
        Assert.True(ModelAnimationCheck.AnyUnmet(lines));

        // And it names what it looked for, so a reader can go and check the
        // model rather than guessing which spelling was tried. This is the line
        // whose absence cost the months.
        Assert.Contains(lines, l => l.Contains("ACT_VM_PRIMARYATTACK"));
    }

    [Fact]
    public void AMissingSequenceIsNotReportedAsAMissingEvent()
    {
        // Two diseases, two names. "The animation never plays" and "the
        // animation plays and fires nothing" have different causes and
        // different fixes, and a report that called both MISSING would send a
        // reader to the wrong one half the time.
        var model = new ModelTable();

        var lines = Verify(model, AnimationEventExpectation.Named(WeaponAnim.Primary, "AE_MUZZLEFLASH"));

        Assert.DoesNotContain(lines, l => l.Contains("MISSING EVENT"));
    }

    //-------------------------------------------------------------------------
    // The event half.
    //-------------------------------------------------------------------------

    [Fact]
    public void ANamedEventThatIsPresentPasses()
    {
        // v_smg1's real shape: fire01 at sequence 1, tagged
        // ACT_VM_PRIMARYATTACK, firing AE_MUZZLEFLASH with 'SMG1 MUZZLE'.
        var model = new ModelTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 1;
        model.Fires(1, "AE_MUZZLEFLASH", 0f, "SMG1 MUZZLE");

        var lines = Verify(model, AnimationEventExpectation.Named(WeaponAnim.Primary, "AE_MUZZLEFLASH"));

        Assert.False(ModelAnimationCheck.AnyUnmet(lines));
        Assert.Contains(lines, l => l.Contains("SMG1 MUZZLE"));
        Assert.Contains(lines, l => l.Contains("0 unmet expectation(s) of 1"));
    }

    [Fact]
    public void ANamedEventTheSequenceDoesNotCarryIsCalledOut()
    {
        // The crowbar's real shape: sequences that exist and fire nothing at
        // all. An expectation of a muzzle flash on it is wrong, and this is
        // what being wrong looks like.
        var model = new ModelTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 1;
        model.FiresNothing(1);

        var lines = Verify(model, AnimationEventExpectation.Named(WeaponAnim.Primary, "AE_MUZZLEFLASH"));

        Assert.Contains(lines, l => l.Contains("MISSING EVENT"));
        Assert.Contains(lines, l => l.Contains("no events"));
        Assert.Contains(lines, l => l.Contains("1 unmet expectation(s) of 1"));
    }

    [Fact]
    public void AnOldSystemEventIsMatchedByNumberRatherThanReportedMissing()
    {
        // v_rpg's real shape, and the reason the expectation type has two
        // constructors. Its primary fires a bare 5001 with no name at all: a
        // check that only understood named events would report a false MISSING
        // here and somebody would go looking for a broken model.
        var model = new ModelTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 2;
        model.FiresNumbered(2, WeaponAnimEvents.RpgFireEvent);

        var lines = Verify(model, AnimationEventExpectation.Numbered(WeaponAnim.Primary,
                                                                     WeaponAnimEvents.RpgFireEvent));

        Assert.False(ModelAnimationCheck.AnyUnmet(lines));
        Assert.Contains(lines, l => l.Contains("#5001"));
    }

    [Fact]
    public void ANamedExpectationDoesNotMatchAnUnnamedEvent()
    {
        // The other side of the same distinction. An old-system event carries
        // no name, so it cannot satisfy a name — matching it anyway would make
        // every named expectation pass against any event at all, which is the
        // check quietly ceasing to check.
        var model = new ModelTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 2;
        model.FiresNumbered(2, 5001);

        var lines = Verify(model, AnimationEventExpectation.Named(WeaponAnim.Primary, "AE_MUZZLEFLASH"));

        Assert.Contains(lines, l => l.Contains("MISSING EVENT"));
    }

    [Fact]
    public void AnEventNameMatchesRegardlessOfCase()
    {
        var model = new ModelTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 1;
        model.Fires(1, "AE_MuzzleFlash");

        var lines = Verify(model, AnimationEventExpectation.Named(WeaponAnim.Primary, "ae_muzzleflash"));

        Assert.False(ModelAnimationCheck.AnyUnmet(lines));
    }

    [Fact]
    public void EachExpectationOnTheSameAnimationIsJudgedSeparately()
    {
        // v_357: a muzzle flash on the primary AND a sound on the reload. One
        // met, one not, and the report has to say which — a single verdict for
        // the weapon would hide the half that is fine and the half that is not.
        var model = new ModelTable();
        model.Activities["ACT_VM_PRIMARYATTACK"] = 1;
        model.Activities["ACT_VM_RELOAD"] = 3;
        model.Fires(1, "AE_MUZZLEFLASH");
        model.FiresNothing(3);

        var lines = Verify(model,
                           AnimationEventExpectation.Named(WeaponAnim.Primary, "AE_MUZZLEFLASH"),
                           AnimationEventExpectation.Named(WeaponAnim.Reload, "AE_CL_PLAYSOUND"));

        Assert.Contains(lines, l => l.Contains("Primary") && l.Contains("ok"));
        Assert.Contains(lines, l => l.Contains("Reload") && l.Contains("MISSING EVENT"));
        Assert.Contains(lines, l => l.Contains("1 unmet expectation(s) of 2"));
    }

    //-------------------------------------------------------------------------
    // "Nobody looked" is not "all clear".
    //-------------------------------------------------------------------------

    [Fact]
    public void AWeaponNobodyHasDeclaredSaysSoRatherThanPassing()
    {
        var lines = ClientAnimEventCheck.Describe(new ModelTable(), ViewModel, "weapon_unknown", null);

        Assert.Contains(lines, l => l.Contains("NOT DECLARED"));
    }

    [Fact]
    public void AWeaponDECLAREDToCarryNothingSaysThatInstead()
    {
        // The crowbar and the crossbow. An empty set is a real answer -- the
        // model was dumped and it fires nothing -- and reading identically to
        // "nobody looked" would throw away the work of looking.
        var lines = ClientAnimEventCheck.Describe(new ModelTable(), ViewModel, "weapon_crowbar",
                                                  Array.Empty<AnimationEventExpectation>());

        Assert.Contains(lines, l => l.Contains("declared as carrying no events"));
        Assert.DoesNotContain(lines, l => l.Contains("NOT DECLARED"));
    }

    //-------------------------------------------------------------------------
    // The declarations themselves. These assert SHAPE, not truth: that the
    // table says something about every weapon the arsenal ships, and that the
    // two known old-system weapons are written as numbers. Whether 5001 is
    // really what v_rpg fires is a question only the .mdl can answer, and
    // cs_animcheck is what asks it.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryShippedWeaponHasBeenLookedAt()
    {
        var undeclared = WeaponTypes.All
            .Where(w => WeaponAnimEvents.For(w.Name) is null)
            .Select(w => w.Name)
            .ToList();

        Assert.True(undeclared.Count == 0,
                    "every weapon in the arsenal has had its model dumped; these have not: " +
                    string.Join(", ", undeclared));
    }

    [Fact]
    public void TheTwoOldSystemWeaponsAreDeclaredAsNumbersRatherThanNames()
    {
        // Written down because it is the detail a later editor would "tidy":
        // v_rpg and v_grenade carry no named events at all, so an expectation
        // spelled as a name would be a check that always failed and would read
        // as a broken model rather than as a wrong expectation.
        foreach (string weapon in new[] { EntityNames.WeaponRpg, EntityNames.WeaponFrag })
        {
            var declared = WeaponAnimEvents.For(weapon);

            Assert.NotNull(declared);
            Assert.All(declared!, e => Assert.True(e.EventName.Length == 0,
                                                   $"{weapon} is declared by number, not name"));
        }
    }
}
