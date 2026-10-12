using System.Globalization;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed half of the template-placement agreement.
///
/// <para>
/// <c>sourcesharp/test/template_transform_cases.txt</c> records what mathlib's
/// VMatrix computed for thirteen placements, and a gtest beside it keeps that
/// record true of native. This asserts <see cref="Transform"/> reproduces the
/// same table — so a sign error, a transposed row or a rotation composed in the
/// wrong order fails here rather than putting a spawned crate somewhere
/// plausible in a map nobody is looking at.
/// </para>
/// </summary>
public class TemplateTransformTests
{
    private const float Tolerance = 0.001f;

    /// <summary>
    /// A POINT moves with the frame's origin. A direction does not.
    ///
    /// <para>
    /// <c>VectorTransform</c> and <c>VectorRotate</c> are one line apart and the
    /// difference is invisible at the world origin, which is where a test
    /// written without thinking puts everything. A caller that picked the wrong
    /// one gets an answer that is right at zero and drifts with distance — a
    /// crate five hundred units out throwing its debris back toward the middle
    /// of the map.
    /// </para>
    /// </summary>
    [Fact]
    public void TransformingAPointRotatesItAndMovesIt()
    {
        var frame = Transform.FromOriginAngles(new Vector(100f, 200f, 300f),
                                               new QAngle(0f, 90f, 0f));

        var placed = frame.TransformPoint(new Vector(10f, 0f, 4f));

        // Ninety degrees of yaw takes +X to +Y, and then the origin is added.
        Assert.True(MathF.Abs(placed.X - 100f) < Tolerance
                 && MathF.Abs(placed.Y - 210f) < Tolerance
                 && MathF.Abs(placed.Z - 304f) < Tolerance,
                    "a point is turned by the frame and then moved by it");
    }

    /// <summary>
    /// And it is the same arithmetic the composition does, which is what makes
    /// it safe to use instead of composing — the numbers above are hand-checked,
    /// this says the shortcut is the long way.
    /// </summary>
    [Fact]
    public void TransformingAPointIsComposingATranslationUnderTheFrame()
    {
        var frame = Transform.FromOriginAngles(new Vector(-40f, 15f, 7f),
                                               new QAngle(23f, -114f, 61f));

        var point = new Vector(3f, -9f, 12f);

        var composed = (frame * Transform.FromOriginAngles(point, new QAngle(0f, 0f, 0f))).Translation;
        var direct = frame.TransformPoint(point);

        Assert.True(MathF.Abs(composed.X - direct.X) < Tolerance
                 && MathF.Abs(composed.Y - direct.Y) < Tolerance
                 && MathF.Abs(composed.Z - direct.Z) < Tolerance,
                    "the shortcut agrees with the composition it replaces, at a rotation with all three angles set");
    }

    /// <summary>
    /// One row of the table: six numbers describing a capture and an instance,
    /// and the six the arithmetic must produce.
    /// </summary>
    private readonly record struct Case(
        string Name,
        Vector TemplateOrigin, QAngle TemplateAngles,
        Vector EntityOrigin, QAngle EntityAngles,
        Vector InstanceOrigin, QAngle InstanceAngles,
        Vector ExpectedOrigin, QAngle ExpectedAngles);

    /// <summary>
    /// The table is not a fixture this file maintains — it is a record of
    /// native, found on disk. Not finding it must be a FAILURE and not an empty
    /// pass: a loop over nothing is green, and that is the exact shape of check
    /// this tree keeps finding.
    /// </summary>
    private static List<Case> LoadCases()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("sourcesharp", "test", "template_transform_cases.txt"));
        Assert.True(path is not null, "the golden table is found next to the tests");

        var cases = new List<Case>();

        foreach (string line in File.ReadAllLines(path!))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 25)
                continue;

            float N(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);

            cases.Add(new Case(
                f[0],
                new Vector(N(1), N(2), N(3)),   new QAngle(N(4), N(5), N(6)),
                new Vector(N(7), N(8), N(9)),   new QAngle(N(10), N(11), N(12)),
                new Vector(N(13), N(14), N(15)), new QAngle(N(16), N(17), N(18)),
                new Vector(N(19), N(20), N(21)), new QAngle(N(22), N(23), N(24))));
        }

        return cases;
    }

    [Fact]
    public void TheGoldenTableIsThereAndHasEveryRow()
    {
        var cases = LoadCases();

        Assert.True(cases.Count == 13,
            "the table has the thirteen placements both sides were written against");
    }

    [Fact]
    public void EveryRowOfTheGoldenTableComesOutOfTheManagedTransform()
    {
        var cases = LoadCases();
        Assert.True(cases.Count > 0, "there is a table to check against");

        foreach (var c in cases)
        {
            var stored = Transform.Relative(c.TemplateOrigin, c.TemplateAngles,
                                            c.EntityOrigin, c.EntityAngles);

            var (origin, angles) = stored.Placed(c.InstanceOrigin, c.InstanceAngles);

            Assert.True(Near(origin.X, c.ExpectedOrigin.X), $"{c.Name} lands at native's x");
            Assert.True(Near(origin.Y, c.ExpectedOrigin.Y), $"{c.Name} lands at native's y");
            Assert.True(Near(origin.Z, c.ExpectedOrigin.Z), $"{c.Name} lands at native's z");

            Assert.True(Near(angles.Pitch, c.ExpectedAngles.Pitch), $"{c.Name} faces native's pitch");
            Assert.True(Near(angles.Yaw,   c.ExpectedAngles.Yaw),   $"{c.Name} faces native's yaw");
            Assert.True(Near(angles.Roll,  c.ExpectedAngles.Roll),  $"{c.Name} faces native's roll");
        }
    }

    /// <summary>
    /// The claim a mapper relies on and the table cannot state on its own:
    /// instancing a template exactly where it stands puts every source back
    /// where it was drawn. Held for an awkward template and an awkward source,
    /// both rotated about all three axes — an identity pair would pass on a
    /// transform that did nothing at all.
    /// </summary>
    [Fact]
    public void InstancingAtHomePutsASourceBackWhereItWas()
    {
        var templateOrigin = new Vector(12f, -34f, 56f);
        var templateAngles = new QAngle(7f, 111f, -23f);
        var entityOrigin = new Vector(-90f, 200f, 5f);
        var entityAngles = new QAngle(3f, -47f, 61f);

        var stored = Transform.Relative(templateOrigin, templateAngles, entityOrigin, entityAngles);
        var (origin, angles) = stored.Placed(templateOrigin, templateAngles);

        Assert.True(MathF.Abs(origin.X - entityOrigin.X) < 0.01f, "the source is back at its own x");
        Assert.True(MathF.Abs(origin.Y - entityOrigin.Y) < 0.01f, "the source is back at its own y");
        Assert.True(MathF.Abs(origin.Z - entityOrigin.Z) < 0.01f, "the source is back at its own z");

        Assert.True(MathF.Abs(angles.Pitch - entityAngles.Pitch) < 0.01f, "and at its own pitch");
        Assert.True(MathF.Abs(angles.Yaw - entityAngles.Yaw) < 0.01f, "and its own yaw");
        Assert.True(MathF.Abs(angles.Roll - entityAngles.Roll) < 0.01f, "and its own roll");
    }

    /// <summary>
    /// A transform composed with its own inverse is the identity. Not a
    /// tautology over the implementation: <see cref="Transform.InverseTR"/>
    /// transposes the rotation and rotates the negated translation, and getting
    /// either of those two halves wrong leaves the other looking right — which
    /// is why this checks the TRANSLATION column as well as the rotation.
    /// </summary>
    [Fact]
    public void ATransformComposedWithItsInverseIsTheIdentity()
    {
        var t = Transform.FromOriginAngles(new Vector(400f, -32f, 91f), new QAngle(15f, -80f, 42f));
        var identity = t.InverseTR() * t;

        Assert.True(Near(identity.M00, 1f) && Near(identity.M11, 1f) && Near(identity.M22, 1f),
            "the rotation cancels on the diagonal");
        Assert.True(Near(identity.M01, 0f) && Near(identity.M02, 0f) && Near(identity.M10, 0f)
                 && Near(identity.M12, 0f) && Near(identity.M20, 0f) && Near(identity.M21, 0f),
            "and off it");
        Assert.True(Near(identity.M03, 0f) && Near(identity.M13, 0f) && Near(identity.M23, 0f),
            "and the translation cancels, which is the half a transpose alone would miss");
    }

    /// <summary>
    /// Composition applies the right-hand transform FIRST. Checked with a
    /// translation and a quarter turn, which do not commute — with two
    /// translations, or with anything at the origin, both orders agree and the
    /// test would pass on a multiply written backwards.
    /// </summary>
    [Fact]
    public void CompositionAppliesTheRightHandTransformFirst()
    {
        var turn = Transform.FromOriginAngles(Vector.Zero, new QAngle(0f, 90f, 0f));
        var step = Transform.FromOriginAngles(new Vector(64f, 0f, 0f), QAngle.Zero);

        // Step out along +X, then turn: the step happened in world space, so the
        // translation is still along +X.
        Assert.True(Near((turn * step).Translation.X, 0f) && Near((turn * step).Translation.Y, 64f),
            "turning after stepping swings the step round to +Y");

        Assert.True(Near((step * turn).Translation.X, 64f) && Near((step * turn).Translation.Y, 0f),
            "stepping after turning leaves the step where the caller put it");
    }

    /// <summary>
    /// An angle survives the round trip through a matrix. Three non-zero
    /// components, because a facing with one is right under several wrong
    /// compositions.
    /// </summary>
    [Fact]
    public void AnAngleSurvivesTheRoundTripThroughAMatrix()
    {
        var angles = new QAngle(20f, -140f, 65f);
        var back = Transform.FromOriginAngles(Vector.Zero, angles).ToAngles();

        Assert.True(MathF.Abs(back.Pitch - angles.Pitch) < 0.01f, "the pitch comes back");
        Assert.True(MathF.Abs(back.Yaw - angles.Yaw) < 0.01f, "the yaw comes back");
        Assert.True(MathF.Abs(back.Roll - angles.Roll) < 0.01f, "the roll comes back");
    }

    /// <summary>
    /// Straight up and straight down have no yaw of their own to read, and stock
    /// answers the roll as zero there rather than as a coin toss. A copy that
    /// left the general branch in would give a spawned entity a random spin
    /// about its own axis whenever a mapper pointed a template at the floor.
    /// </summary>
    [Fact]
    public void LookingStraightDownGivesUpItsRollRatherThanGuessing()
    {
        var straightDown = Transform.FromOriginAngles(Vector.Zero, new QAngle(90f, 30f, 0f))
                         * Transform.FromOriginAngles(Vector.Zero, new QAngle(0f, 0f, 60f));

        var angles = straightDown.ToAngles();

        Assert.True(MathF.Abs(angles.Pitch - 90f) < 0.01f, "it is still looking straight down");
        Assert.True(angles.Roll == 0f, "and the roll is zero rather than whatever atan2 of two zeroes said");
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < Tolerance;
}
