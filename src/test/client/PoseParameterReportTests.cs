using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client pose-parameter report and setter (plan_baseentity.md Phase 5).
///
/// A pose parameter is the one animation capability whose whole output is a
/// picture, so the report is the only part of it a person can read and the only
/// part a test can hold still. Five of its decisions are load-bearing and each
/// has a case below:
///
/// - DORMANT is printed, because an entity out of PVS answers every query with
///   the last value that arrived and does so without complaint;
/// - a model with no pose parameters is LISTED rather than skipped, because
///   "declares none" and "was never asked" are the two answers the report
///   exists to tell apart;
/// - every parameter is looked up through the NAME the report printed, not
///   through the index the loop already had, so the round trip a real caller
///   makes is what is exercised;
/// - a lookup that disagrees with the index is called out on the line, because
///   an index that means a different dial on a different model is the exact bug
///   that reaches a player as a model bending the wrong way;
/// - the setter reports what was STORED against what was asked, because a
///   looping parameter wraps and a non-looping one clamps, and a setter that
///   answered nothing would leave a caller unable to tell either from a model
///   that has no such parameter at all.
/// </summary>
public class PoseParameterReportTests
{
    private sealed class Entity : ClientStandaloneEntity
    {
        // No handle: ClientStandaloneEntity.Handle is set by the registry and
        // is not writable from here. Nothing in the report depends on its
        // VALUE -- it goes straight through to the engine double, which answers
        // by member name.
        public Entity(IEngineEntityRender render, string className) : base(render)
        {
            ClassName = className;
        }

        public override string ClassName { get; }
    }

    /// <summary>
    /// An engine double answering for a model with ONE pose parameter, which is
    /// the shape every case below starts from. One rather than several because
    /// the double answers per MEMBER rather than per call, so a second
    /// parameter would answer the first one's name and value and assert
    /// nothing extra.
    /// </summary>
    private static RecordingEngine OneParameterModel(string name, float value,
                                                     float start, float end,
                                                     bool dormant = false)
    {
        var engine = Fake();

        engine.Returns("EntityRender.GetModelName", "models/player/male_anims.mdl");
        engine.Returns("EntityRender.IsDormant", dormant);
        engine.Returns("EntityRender.PoseParameterCount", 1);
        engine.Returns("EntityRender.PoseParameterName", name);
        engine.Returns("EntityRender.LookupPoseParameter", 0);
        engine.Returns("EntityRender.GetPoseParameter", value);
        engine.Returns("EntityRender.PoseParameterRange", true);
        engine.Returns("EntityRender.PoseParameterRange.start", start);
        engine.Returns("EntityRender.PoseParameterRange.end", end);

        return engine;
    }

    private static List<ClientStandaloneEntity> One(IEngineEntityRender render, string className)
        => new() { new Entity(render, className) };

    //-------------------------------------------------------------------------
    // The report.
    //-------------------------------------------------------------------------

    [Fact]
    public void ADormantEntitySaysSoOnItsLine()
    {
        var engine = OneParameterModel("aim_yaw", 0f, -180f, 180f, dormant: true);
        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Report(One(render, "ss_cycler"), render, string.Empty);

        Assert.Contains("DORMANT", lines[0]);
    }

    [Fact]
    public void ALiveEntitySaysThatInstead()
    {
        // The other half of the same clause, and it is not ceremony: a report
        // that printed DORMANT unconditionally would pass the case above while
        // telling every reader that every entity is stale.
        var engine = OneParameterModel("aim_yaw", 0f, -180f, 180f, dormant: false);
        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Report(One(render, "ss_cycler"), render, string.Empty);

        Assert.Contains("live", lines[0]);
        Assert.DoesNotContain("DORMANT", lines[0]);
    }

    [Fact]
    public void AModelWithNoPoseParametersIsStillListed()
    {
        var engine = Fake();
        engine.Returns("EntityRender.GetModelName", "models/props_junk/wood_crate001a.mdl");
        engine.Returns("EntityRender.IsDormant", false);
        engine.Returns("EntityRender.PoseParameterCount", 0);

        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Report(One(render, "prop_physics"), render, string.Empty);

        Assert.Contains(lines, l => l.Contains("prop_physics") && l.Contains("no pose parameters"));
        Assert.Equal("1 entit(ies), 0 with pose parameters", lines[^1]);
    }

    [Fact]
    public void TheLineCarriesTheValueAndTheDeclaredRange()
    {
        // The range is what makes the value readable. "0.5" says nothing; the
        // range says whether the model is looking straight ahead or hard left,
        // and a report of bare normalised numbers is one nobody can act on.
        var engine = OneParameterModel("aim_pitch", 45f, -90f, 90f);
        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Report(One(render, "ss_cycler"), render, string.Empty);

        Assert.Contains("aim_pitch", lines[1]);
        Assert.Contains("45.000", lines[1]);
        Assert.Contains("-90.0", lines[1]);
        Assert.Contains("90.0", lines[1]);
    }

    [Fact]
    public void EachParameterIsLookedUpThroughTheNameTheReportPrinted()
    {
        var engine = OneParameterModel("aim_yaw", 0f, -180f, 180f);
        var render = (IEngineEntityRender)engine;

        ClientPoseParameters.Report(One(render, "ss_cycler"), render, string.Empty);

        Assert.Equal(1, engine.CountOf("EntityRender.LookupPoseParameter"));
        Assert.Equal("aim_yaw", engine.Last("EntityRender.LookupPoseParameter")!.Value.Args[1]);
    }

    [Fact]
    public void ALookupThatDisagreesWithTheIndexIsCalledOut()
    {
        // An index is meaningful only against the model it came from. If the
        // name the report just printed resolves to a DIFFERENT number, one of
        // the two is reading a dial that is not the one it names, and the
        // symptom in the game is a model bending on the wrong axis.
        var engine = OneParameterModel("aim_yaw", 0f, -180f, 180f);
        engine.Returns("EntityRender.LookupPoseParameter", 3);

        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Report(One(render, "ss_cycler"), render, string.Empty);

        Assert.Contains("lookup said 3, not 0", lines[1]);
    }

    [Fact]
    public void AFilterThatNamesNothingReportsAnEmptyTally()
    {
        var engine = OneParameterModel("aim_yaw", 0f, -180f, 180f);
        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Report(One(render, "ss_cycler"), render, "weapon_");

        Assert.Single(lines);
        Assert.Equal("0 entit(ies), 0 with pose parameters", lines[0]);
    }

    //-------------------------------------------------------------------------
    // The setter, which is the half a report cannot verify for itself.
    //-------------------------------------------------------------------------

    [Fact]
    public void SettingAParameterTheModelDoesNotHaveIsNamedRatherThanIgnored()
    {
        var engine = Fake();
        engine.Returns("EntityRender.GetModelName", "models/props_junk/wood_crate001a.mdl");
        engine.Returns("EntityRender.LookupPoseParameter", -1);

        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Apply(One(render, "prop_physics"), render,
                                               string.Empty, "aim_yaw", 60f);

        Assert.Contains(lines, l => l.Contains("no pose parameter named 'aim_yaw'"));
        Assert.Equal("0 set, 1 without that parameter", lines[^1]);

        // And nothing was written. A setter that named the failure and set it
        // anyway would be worse than one that did neither.
        Assert.False(engine.Called("EntityRender.SetPoseParameter"));
    }

    [Fact]
    public void AValueTheEngineChangedIsReportedAgainstWhatWasAsked()
    {
        // An aim_yaw of 190 degrees on a looping -180..180 dial comes back as
        // -170: the model turned the short way round, which is right and is
        // also not what the caller typed. Saying so is the difference between
        // a caller trusting the number and a caller debugging the renderer.
        var engine = Fake();
        engine.Returns("EntityRender.LookupPoseParameter", 0);
        engine.Returns("EntityRender.SetPoseParameter", -170f);

        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Apply(One(render, "ss_cycler"), render,
                                               string.Empty, "aim_yaw", 190f);

        Assert.Contains("asked 190, stored -170", lines[0]);
        Assert.Equal("1 set, 0 without that parameter", lines[^1]);
    }

    [Fact]
    public void AValueThatSurvivedIntactCarriesNoNote()
    {
        // The other half, and the reason the comparison is a tolerance rather
        // than an equality: the value makes a round trip through the normalised
        // 0..1 form bone_setup wants and back, so 45 comes home as 45.000004.
        // An exact test would put the "changed" note on every single call and
        // the note would stop meaning anything.
        var engine = Fake();
        engine.Returns("EntityRender.LookupPoseParameter", 0);
        engine.Returns("EntityRender.SetPoseParameter", 45.000004f);

        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Apply(One(render, "ss_cycler"), render,
                                               string.Empty, "aim_yaw", 45f);

        Assert.DoesNotContain("asked", lines[0]);
        Assert.Contains("aim_yaw (0) = 45", lines[0]);
    }

    [Fact]
    public void ASetterWithNoParameterNameAnswersWithItsUsage()
    {
        var engine = Fake();
        var render = (IEngineEntityRender)engine;

        var lines = ClientPoseParameters.Apply(One(render, "ss_cycler"), render,
                                               string.Empty, string.Empty, 0f);

        Assert.Single(lines);
        Assert.Contains("cs_setpose", lines[0]);

        // And it did not walk the entity list to say so.
        Assert.False(engine.Called("EntityRender.LookupPoseParameter"));
    }
}
