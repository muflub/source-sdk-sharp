using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client attachment report (plan_baseentity.md Phase 5).
///
/// The report is the only thing about a bone cache that a person can read, so
/// what it SAYS is worth pinning. Three of its decisions are load-bearing and
/// each has a case below:
///
/// - an entity with no attachments is listed rather than skipped, because
///   "has none" and "was never asked" are the two answers the report exists to
///   tell apart;
/// - every point is fetched through the NAME, not through the index the loop
///   already had, so the listing exercises the round trip a real caller makes;
/// - a disagreement between the two is called out on the line rather than
///   quietly preferring one, because an off-by-one in the engine's one-based
///   attachment convention is exactly the bug that reaches a player as a
///   muzzle flash out of the wrong end.
/// </summary>
public class AttachmentReportTests
{
    private sealed class Entity : ClientStandaloneEntity
    {
        // No handle: ClientStandaloneEntity.Handle is set by the registry and is
        // not writable from here. Nothing in the report depends on its VALUE --
        // it is passed straight through to the engine double, which answers by
        // member name -- and a test that set one would be asserting the
        // registry's job rather than the report's.
        public Entity(IEngineEntityRender render, string className) : base(render)
        {
            ClassName = className;
        }

        public override string ClassName { get; }
    }

    [Fact]
    public void AnEntityWithNoAttachmentsIsStillListed()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "models/props_junk/wood_crate001a.mdl");

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity> { new Entity(render, "prop_physics") };

        var lines = ClientAttachments.Report(live, render, string.Empty);

        Assert.True(lines.Any(l => l.Contains("prop_physics") && l.Contains("no attachments")),
                    "a crate with no attachments is reported as having none, not omitted");
        Assert.True(lines.Last() == "1 entit(ies), 0 with attachments",
                    "and the tally counts it as seen but not as answering");
    }

    [Fact]
    public void AnEntityWithNoModelSaysSoRatherThanLeavingABlank()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", string.Empty);

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity> { new Entity(render, "env_beam") };

        var lines = ClientAttachments.Report(live, render, string.Empty);

        Assert.True(lines[0].Contains("(no model)"),
                    "a beam wearing no model is described as wearing none");
    }

    [Fact]
    public void EachPointIsFetchedThroughTheNameTheReportPrinted()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 2);
        engine.Returns("EntityRender.GetModelName", "models/weapons/w_rocket_launcher.mdl");
        engine.Returns("EntityRender.AttachmentName", "muzzle");
        engine.Returns("EntityRender.LookupAttachment", 1);
        engine.Returns("EntityRender.GetAttachment", true);
        engine.Returns("EntityRender.GetAttachment.origin", new Vector(10f, 20f, 30f));
        engine.Returns("EntityRender.GetAttachment.angles", new Vector(0f, 90f, 0f));

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity> { new Entity(render, "weapon_rpg") };

        var lines = ClientAttachments.Report(live, render, string.Empty);

        // The lookup is the call a real caller makes, and a report that skipped
        // it would be reporting the loop counter rather than the engine.
        Assert.True(engine.CountOf("EntityRender.LookupAttachment") == 2,
                    "every listed attachment is looked up by name, once each");
        Assert.True(engine.CountOf("EntityRender.GetAttachment") == 2,
                    "and each looked-up number is read back as a point");
        Assert.True(lines.Any(l => l.Contains("muzzle") && l.Contains("10.0")),
                    "the world position is what the line carries");
    }

    [Fact]
    public void ALookupThatDisagreesWithTheIndexIsCalledOut()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 1);
        engine.Returns("EntityRender.GetModelName", "models/gibs/hgibs.mdl");
        engine.Returns("EntityRender.AttachmentName", "eyes");

        // The off-by-one this convention invites: the name is the first
        // attachment, the lookup answers zero-based.
        engine.Returns("EntityRender.LookupAttachment", 0);
        engine.Returns("EntityRender.GetAttachment", false);
        engine.Returns("EntityRender.GetAttachment.origin", new Vector(0f, 0f, 0f));
        engine.Returns("EntityRender.GetAttachment.angles", new Vector(0f, 0f, 0f));

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity> { new Entity(render, "gib") };

        var lines = ClientAttachments.Report(live, render, string.Empty);

        Assert.True(lines.Any(l => l.Contains("lookup said 0, not 1")),
                    "a lookup that disagrees with the index is named on the line");
        Assert.True(lines.Any(l => l.Contains("[no answer]")),
                    "and a query that could not answer says so rather than printing a plausible zero");
    }

    [Fact]
    public void ASkeletonIsReportedWithBothCountsBecauseTheyAnswerDifferentQuestions()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 60);

        // Nothing has drawn it yet, which is the state that looks exactly like a
        // broken bone setup if only the model's count is printed.
        engine.Returns("EntityRender.BoneCount.cached", 0);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "models/player/male_01.mdl");

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity> { new Entity(render, "ss_barrel") };

        var lines = ClientAttachments.Report(live, render, string.Empty);

        Assert.True(lines[0].Contains("60 bone(s), 0 cached"),
                    "a real skeleton nothing has posed yet reports both numbers, and they differ");
    }

    [Fact]
    public void AnEntityWithNoSkeletonSaysSoRatherThanReportingZeroBones()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 0);
        engine.Returns("EntityRender.BoneCount.cached", 0);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "*63");

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity> { new Entity(render, "func_breakable_surf") };

        var lines = ClientAttachments.Report(live, render, string.Empty);

        Assert.True(lines[0].Contains("no skeleton"),
                    "a brush model has no skeleton and is described that way, not as zero bones");
    }

    [Fact]
    public void TheFilterMatchesPartOfAClassnameAndExcludesTheRest()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "models/props_c17/oildrum001.mdl");

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity>
        {
            new Entity(render, "prop_physics"),
            new Entity(render, "env_sprite"),
        };

        var lines = ClientAttachments.Report(live, render, "prop");

        Assert.True(lines.Last() == "1 entit(ies), 0 with attachments",
                    "the filter narrows the listing to the classnames containing it");
        Assert.True(!lines.Any(l => l.Contains("env_sprite")),
                    "and the ones it does not match are not listed");
    }

    [Fact]
    public void AStarOrAnEmptyFilterTakesEverything()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "models/props_c17/oildrum001.mdl");

        var render = (IEngineEntityRender)engine;
        var live = new List<ClientStandaloneEntity>
        {
            new Entity(render, "prop_physics"),
            new Entity(render, "env_sprite"),
        };

        Assert.True(ClientAttachments.Report(live, render, "*").Last() == "2 entit(ies), 0 with attachments",
                    "a star takes every entity");
        Assert.True(ClientAttachments.Report(live, render, string.Empty).Last() == "2 entit(ies), 0 with attachments",
                    "and so does no filter at all");
    }
}
