using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The SERVER attachment report (plan_baseentity.md Phase 5, the server half).
///
/// <para>
/// The report exists to be compared against the client's, so what these cases
/// pin is the things that would make the comparison meaningless: the same line
/// shape, both bone counts, the round trip through the NAME, and the
/// one-based index called out when the two disagree. A report whose format
/// drifted from <c>ClientAttachments</c>'s would turn a diff into a reading
/// exercise, and a reading exercise is one nobody does twice.
/// </para>
///
/// <para>
/// <b>What these cannot prove.</b> That the server's skeleton agrees with the
/// client's is not a thing a unit test can establish — both sides here would be
/// the same stubbed double, which is the eleventh check that cannot fail wearing
/// a new hat. The agreement is checked live, by running <c>cs_sv_attachments</c>
/// and <c>cs_attachments</c> on one entity and comparing two numbers computed by
/// two binaries that share no code.
/// </para>
/// </summary>
public class ServerAttachmentReportTests
{
    private sealed class Entity : ScriptedEntity
    {
        // ClassName is a settable property on ScriptedEntity, written by the
        // registry rather than overridden by the class -- the opposite of the
        // client's ClientStandaloneEntity, where it is abstract. Set here
        // directly, because the report reads it and nothing else about the
        // entity matters to these cases.
        public Entity(string className) => ClassName = className;
    }

    private static List<ScriptedEntity> One(string className) => new() { new Entity(className) };

    [Fact]
    public void AnEntityWithNoAttachmentsIsStillListed()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 1);
        engine.Returns("EntityRender.BoneCount.cached", 1);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "models/props_junk/wood_crate001a.mdl");

        var render = (IEngineEntityRender)engine;

        var lines = ServerAttachments.Report(One("prop_physics"), render);

        Assert.Contains(lines, l => l.Contains("prop_physics") && l.Contains("no attachments"));
        Assert.Equal("1 entit(ies), 0 with attachments", lines[^1]);
    }

    [Fact]
    public void BothBoneCountsAreOnTheLine()
    {
        // The pair is the point. A model count of 15 beside a CACHED count of 0
        // is an entity nothing has needed a frame from yet -- which on the
        // server is the normal state, because nothing poses a server skeleton
        // until something asks it for an attachment. Printing only one number
        // would make that indistinguishable from a broken bone setup.
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 15);
        engine.Returns("EntityRender.BoneCount.cached", 0);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", "models/combine_camera/combine_camera.mdl");

        var render = (IEngineEntityRender)engine;

        var lines = ServerAttachments.Report(One("ss_barrel"), render);

        Assert.Contains("15 bone(s), 0 cached", lines[0]);
    }

    [Fact]
    public void AnEntityWithNoSkeletonSaysSoRatherThanPrintingZero()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 0);
        engine.Returns("EntityRender.BoneCount.cached", 0);
        engine.Returns("EntityRender.AttachmentCount", 0);
        engine.Returns("EntityRender.GetModelName", string.Empty);

        var render = (IEngineEntityRender)engine;

        var lines = ServerAttachments.Report(One("ss_beam"), render);

        Assert.Contains("no skeleton", lines[0]);
        Assert.Contains("(no model)", lines[0]);
    }

    [Fact]
    public void EachPointIsFetchedThroughTheNameTheReportPrinted()
    {
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 8);
        engine.Returns("EntityRender.BoneCount.cached", 8);
        engine.Returns("EntityRender.AttachmentCount", 2);
        engine.Returns("EntityRender.GetModelName", "models/weapons/w_rocket_launcher.mdl");
        engine.Returns("EntityRender.AttachmentName", "muzzle");
        engine.Returns("EntityRender.LookupAttachment", 1);
        engine.Returns("EntityRender.GetAttachment", true);
        engine.Returns("EntityRender.GetAttachment.origin", new Vector(10f, 20f, 30f));
        engine.Returns("EntityRender.GetAttachment.angles", new Vector(0f, 90f, 0f));

        var render = (IEngineEntityRender)engine;

        ServerAttachments.Report(One("weapon_rpg"), render);

        Assert.Equal(2, engine.CountOf("EntityRender.LookupAttachment"));
        Assert.Equal(2, engine.CountOf("EntityRender.GetAttachment"));
    }

    [Fact]
    public void ALookupThatDisagreesWithTheIndexIsCalledOut()
    {
        // The engine's attachment convention is ONE-BASED, on both sides, and
        // the two sides differing by one here is a trail coming out of the
        // wrong end of a rocket. Saying it on the line is what makes the
        // client/server comparison able to catch it.
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 8);
        engine.Returns("EntityRender.BoneCount.cached", 8);
        engine.Returns("EntityRender.AttachmentCount", 1);
        engine.Returns("EntityRender.GetModelName", "models/weapons/w_rocket_launcher.mdl");
        engine.Returns("EntityRender.AttachmentName", "muzzle");
        engine.Returns("EntityRender.LookupAttachment", 0);
        engine.Returns("EntityRender.GetAttachment", false);
        engine.Returns("EntityRender.GetAttachment.origin", new Vector(0f, 0f, 0f));
        engine.Returns("EntityRender.GetAttachment.angles", new Vector(0f, 0f, 0f));

        var render = (IEngineEntityRender)engine;

        var lines = ServerAttachments.Report(One("weapon_rpg"), render);

        Assert.Contains(lines, l => l.Contains("lookup said 0, not 1"));
        Assert.Contains(lines, l => l.Contains("[no answer]"));
    }

    [Fact]
    public void TheLineShapeMatchesTheClientsSoTheTwoCanBeDiffed()
    {
        // Not a formatting preference: the whole purpose of this report is to
        // be held beside cs_attachments, and two formats would make that a
        // reading exercise instead of a diff. The header line's field order and
        // the point line's column widths are what this pins.
        var engine = Fake();
        engine.Returns("EntityRender.BoneCount", 8);
        engine.Returns("EntityRender.BoneCount.cached", 8);
        engine.Returns("EntityRender.AttachmentCount", 1);
        engine.Returns("EntityRender.GetModelName", "models/weapons/w_rocket_launcher.mdl");
        engine.Returns("EntityRender.AttachmentName", "muzzle");
        engine.Returns("EntityRender.LookupAttachment", 1);
        engine.Returns("EntityRender.GetAttachment", true);
        engine.Returns("EntityRender.GetAttachment.origin", new Vector(10f, 20f, 30f));
        engine.Returns("EntityRender.GetAttachment.angles", new Vector(0f, 90f, 0f));

        var render = (IEngineEntityRender)engine;

        var lines = ServerAttachments.Report(One("weapon_rpg"), render);

        Assert.Equal("weapon_rpg  models/weapons/w_rocket_launcher.mdl  8 bone(s), 8 cached  1 attachment(s)",
                     lines[0]);
        Assert.Equal("     1 muzzle               (    10.0     20.0     30.0) (    0.0    90.0     0.0)",
                     lines[1]);
    }
}
