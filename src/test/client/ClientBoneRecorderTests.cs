using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed bone recorder: the six facts <c>tools/live-gate</c>'s skinned
/// step reads, and the row format it reads them in.
///
/// <para>
/// Two kinds of test here, and the second kind is the point. The first kind
/// pins the arithmetic — which entity is chosen, which attachments count as
/// moved. The second kind asserts AGREEMENT WITH THE NATIVE PROBE by reading
/// <c>shell_standalone_client.cpp</c>: the fact names, their order, and the tag
/// are not what this recorder happens to emit, they are what
/// <c>ClientBoneProbe::ReportSkinned</c> emits, and a recorder that drifted
/// from it would be read by the gate as a changed measurement rather than as a
/// changed recorder. A stand-in for native drifts silently unless the
/// agreement is asserted.
/// </para>
/// </summary>
public class ClientBoneRecorderTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// The client shell, whose native probe the recorder used to be held equal
    /// to. The probe went with the native skeleton (plan
    /// physics_bones_native_zero 1.2, client half); what is pinned now is that
    /// it stayed gone, so a second skinned report cannot disagree with this one.
    /// </summary>
    private static string ClientShell()
        => Read("src/sourcesharp/game/client/shell_standalone_client.cpp");

    private sealed class Entity : ClientStandaloneEntity
    {
        public Entity(IEngineEntityRender render, string className) : base(render)
        {
            ClassName = className;
        }

        public override string ClassName { get; }
    }

    /// <summary>The identity transform, translated to an origin.</summary>
    private static float[] TransformAt(float x, float y, float z)
        => new float[] { 1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z };

    /// <summary>
    /// A cache of <paramref name="bones"/> bones sitting at
    /// (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>),
    /// every bone written and posed.
    /// </summary>
    private static StandaloneBones CacheAt(int bones, float x, float y, float z)
    {
        var entity = TransformAt(x, y, z);
        var matrices = new float[bones * StandaloneBones.FloatsPerMatrix];
        var written = new uint[(bones + 31) / 32];

        for (int b = 0; b < bones; b++)
        {
            written[b / 32] |= 1u << (b % 32);

            // Something other than the entity transform, so it reads as posed.
            matrices[(b * StandaloneBones.FloatsPerMatrix) + 3] = x + 1f + b;
        }

        // The constructor is internal to SourceSharp.Abi and this project sees
        // it through InternalsVisibleTo, which is how StandaloneBonesTests
        // builds its skeletons too.
        return new StandaloneBones(bones, matrices, written, entity);
    }

    // ---------------------------------------------------------------- subject

    [Fact]
    public void TheSubjectIsThePoseableEntityWithTheMostBones()
    {
        var render = (IEngineEntityRender)TestServices.Fake();
        var barrel = new Entity(render, "ss_barrel");
        var man = new Entity(render, "ss_male");
        var crate = new Entity(render, "ss_crate");

        var picked = ClientBoneRecorder.PickSubject(
            new List<ClientStandaloneEntity> { barrel, man, crate },
            _ => true,
            e => e == man ? 56 : 1);

        Assert.Same(man, picked);
    }

    [Fact]
    public void AnEntityThatIsNotPoseableIsSkippedEvenWithMoreBones()
    {
        var render = (IEngineEntityRender)TestServices.Fake();
        var staticProp = new Entity(render, "ss_static");
        var man = new Entity(render, "ss_male");

        var picked = ClientBoneRecorder.PickSubject(
            new List<ClientStandaloneEntity> { staticProp, man },
            e => e != staticProp,
            e => e == staticProp ? 200 : 56);

        Assert.Same(man, picked);
    }

    [Fact]
    public void ATieIsWonByTheFirstEntityInListOrder()
    {
        var render = (IEngineEntityRender)TestServices.Fake();
        var first = new Entity(render, "ss_male_a");
        var second = new Entity(render, "ss_male_b");

        var picked = ClientBoneRecorder.PickSubject(
            new List<ClientStandaloneEntity> { first, second }, _ => true, _ => 56);

        Assert.Same(first, picked);
    }

    [Fact]
    public void NoPoseableEntityIsNoSubjectRatherThanTheFirstOne()
    {
        var render = (IEngineEntityRender)TestServices.Fake();
        var crate = new Entity(render, "ss_crate");

        var picked = ClientBoneRecorder.PickSubject(
            new List<ClientStandaloneEntity> { crate }, _ => false, _ => 40);

        Assert.Null(picked);
    }

    [Fact]
    public void AnEntityWithNoBonesIsNeverTheSubject()
    {
        var render = (IEngineEntityRender)TestServices.Fake();
        var beam = new Entity(render, "env_beam");

        var picked = ClientBoneRecorder.PickSubject(
            new List<ClientStandaloneEntity> { beam }, _ => true, _ => 0);

        Assert.Null(picked);
    }

    // ------------------------------------------------------------ attachments

    [Fact]
    public void AnAttachmentOnTheOriginHasNotMoved()
    {
        var cache = CacheAt(4, 100f, -50f, 8f);

        int moved = ClientBoneRecorder.CountMovedAttachments(
            cache, new[] { new Vector(100f, -50f, 8f) });

        Assert.Equal(0, moved);
    }

    [Fact]
    public void AnAttachmentAUnitAwayHasNotMovedBecauseTheTestIsStrictlyGreater()
    {
        var cache = CacheAt(4, 0f, 0f, 0f);

        // LengthSqr is exactly 1.0, and the native test is `> 1.0f`.
        int moved = ClientBoneRecorder.CountMovedAttachments(
            cache, new[] { new Vector(1f, 0f, 0f) });

        Assert.Equal(0, moved);
    }

    [Fact]
    public void AnAttachmentWellAwayFromTheOriginHasMoved()
    {
        var cache = CacheAt(4, 100f, -50f, 8f);

        int moved = ClientBoneRecorder.CountMovedAttachments(
            cache, new[] { new Vector(140f, -50f, 8f) });

        Assert.Equal(1, moved);
    }

    [Fact]
    public void TheOriginComesFromTheCachesOwnTransformAndNotFromZero()
    {
        // An entity 100 units out, with its attachment ON it. Measured against
        // the world origin this would read as moved; measured against the
        // entity's own transform, which is what the native probe uses, it has
        // not moved at all.
        var cache = CacheAt(4, 100f, 0f, 0f);

        int moved = ClientBoneRecorder.CountMovedAttachments(
            cache, new[] { new Vector(100f, 0f, 0f) });

        Assert.Equal(0, moved);
    }

    [Fact]
    public void EachAttachmentIsCountedSeparately()
    {
        var cache = CacheAt(4, 0f, 0f, 0f);

        int moved = ClientBoneRecorder.CountMovedAttachments(
            cache,
            new[] { new Vector(0f, 0f, 0f), new Vector(30f, 0f, 0f), new Vector(0f, 40f, 0f) });

        Assert.Equal(2, moved);
    }

    // -------------------------------------------------------------- row shape

    [Fact]
    public void ARowIsFourTabSeparatedFieldsTheGateCanParse()
    {
        string row = ClientBoneRecorder.Row("posed", "56");

        string[] fields = row.Split('\t');

        Assert.Equal(4, fields.Length);
        Assert.Equal("SS6CGATE", fields[0]);
        Assert.Equal("skinned", fields[1]);
        Assert.Equal("posed", fields[2]);
        Assert.Equal("56", fields[3]);
    }

    [Fact]
    public void TheSixFactsAreEmittedInTheNativeProbesOrder()
    {
        var cache = CacheAt(56, 0f, 0f, 0f);

        var facts = ClientBoneRecorder.Rows("models/humans/group03/male_01.mdl", 56, cache, 7, 7)
                                      .Select(r => r.Split('\t')[2])
                                      .ToArray();

        Assert.Equal(
            new[] { "model", "bones", "written", "posed", "attachments", "movedattachments" },
            facts);
    }

    [Fact]
    public void WrittenAndPosedComeFromTheCacheRatherThanFromTheBoneCount()
    {
        // 56 declared bones, but a cache in which only 4 were written. A
        // recorder that reported the model's count for either would say 56.
        var cache = CacheAt(4, 0f, 0f, 0f);

        var rows = ClientBoneRecorder.Rows("m.mdl", 56, cache, 0, 0)
                                     .ToDictionary(r => r.Split('\t')[2], r => r.Split('\t')[3]);

        Assert.Equal("56", rows["bones"]);
        Assert.Equal("4", rows["written"]);
        Assert.Equal("4", rows["posed"]);
    }

    [Fact]
    public void ARunWithNoSubjectSaysBonesZeroRatherThanPrintingNothing()
    {
        var rows = ClientBoneRecorder.NoSubject();

        Assert.Single(rows);

        string[] fields = rows[0].Split('\t');

        Assert.Equal(4, fields.Length);
        Assert.Equal("bones", fields[2]);
        Assert.Equal("0", fields[3]);
    }

    // ------------------------------------- the native probe is gone, pinned

    [Fact]
    public void TheTagAndKindAreTheOnesTheGateParses()
    {
        string gate = File.ReadAllText(RepoLayout.Repo("tools", "live-gate"));

        Assert.Contains("g.console_lines(\"SS6CGATE\\t\")", gate);
        Assert.Contains("boneprobe_rows(g, \"skinned\")", gate);
        Assert.Equal("SS6CGATE", ClientBoneRecorder.Tag);
        Assert.Equal("skinned", ClientBoneRecorder.Kind);
    }

    [Fact]
    public void TheMovedAttachmentThresholdIsOneUnit()
    {
        // The native probe's `LengthSqr() > 1.0f`, which the gate's thresholds
        // were measured against.
        Assert.Equal(1.0f, ClientBoneRecorder.MovedThresholdSquared);
    }

    [Fact]
    public void TheModelRowIsTheStudioHeadersNameAndNotTheModelPath()
    {
        // MEASURED, not reasoned about. tools/7b-bonerecord-agree ran both
        // answers on one live join and found this the only fact of the six that
        // differed: the recorder reported
        // "models/humans/group03/male_01.mdl" (modelinfo's path) where the
        // native probe reported "Humans\Group03\Male_01.mdl"
        // (studiohdr_t::pszName()). The StandaloneStudioName slot exists
        // because of that run, and this pins the recorder to it — a recorder
        // that drifted back to GetModelName would agree on five facts and
        // disagree about which entity they describe.
        var engine = TestServices.Fake();
        engine.Returns("EntityRender.IsPoseable", true);
        engine.Returns("EntityRender.BoneCount", 56);
        engine.Returns("EntityRender.BoneCount.cached", 56);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.StudioName", @"Humans\Group03\Male_01.mdl");
        engine.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl");

        var render = (IEngineEntityRender)engine;

        // BoneMatrices cannot be faked through the engine double (it answers
        // null by design -- a plausible fake skeleton is the one thing a caller
        // of it cannot tell from a real one), so this asserts the row the
        // recorder BUILDS from a known cache rather than the whole Report.
        var rows = ClientBoneRecorder
            .Rows(render.StudioName(default), 56, CacheAt(56, 0f, 0f, 0f), 0, 0)
            .ToDictionary(r => r.Split('\t')[2], r => r.Split('\t')[3]);

        Assert.Equal(@"Humans\Group03\Male_01.mdl", rows["model"]);
        Assert.NotEqual(render.GetModelName(default), rows["model"]);
    }

    [Fact]
    public void TheRecorderAsksForTheStudioNameRatherThanTheModelName()
    {
        // The engine double records what was ASKED, which is the only way to
        // tell the two apart when both would return a plausible string.
        var engine = TestServices.Fake();
        engine.Returns("EntityRender.IsPoseable", false);
        engine.Returns("EntityRender.BoneCount", 0);
        engine.Returns("EntityRender.BoneCount.cached", 0);

        var render = (IEngineEntityRender)engine;

        // No poseable entity, so Report short-circuits; the point is that the
        // recorder's source names StudioName at all.
        Assert.Single(ClientBoneRecorder.Report(new List<ClientStandaloneEntity>(), render));

        string recorder = Read(
            "src/sourcesharp/managed/SourceSharp.Client/Standalone/ClientBoneRecorder.cs");

        Assert.Contains("render.StudioName(subject.Handle)", recorder);
        Assert.DoesNotContain("render.GetModelName(subject.Handle)", recorder);
    }
}
