using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TempEntDeclarationReader"/> — the client half of the declaration
/// form, and the inverse of <see cref="TempEntDeclarationWriter"/>.
///
/// <para>
/// THE ROUND TRIP IS A SOUND GATE HERE, which it is NOT for the writer alone.
/// The warning on the writer's own tests is that a round trip resolves both of
/// its sides from the same <see cref="TeClass"/> and would pass with every
/// offset in that class wrong together. That is still true — and it is why the
/// writer is pinned separately against literal golden bytes. With the writer
/// anchored to bytes that cannot move, a value that survives write-then-read is
/// anchored too: the blob in the middle is the one the golden tests pinned.
/// </para>
///
/// <para>
/// The one claim a round trip cannot make on its own is the SIGNEDNESS one, so
/// that gets a test with a literal in it.
/// </para>
/// </summary>
public class TempEntDeclarationReaderTests
{
    /// <summary>
    /// Componentwise, because <see cref="Vector"/> declares no <c>==</c>. Exact
    /// rather than within an epsilon: these values make one round trip through a
    /// blob that stores them as the same floats, so anything but equality is a
    /// layout fault and not a rounding one.
    /// </summary>
    private static bool Same(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static TempEntRegistry Registry()
    {
        var engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    private static TempEntType Registered(string className) => Registry().Find(className)!;

    /// <summary>The blob a state packs, through the writer this reader inverts.</summary>
    private static byte[] Packed(TempEntState state, TempEntType type) => state.ToBlob(type);

    [Fact]
    public void ASparksComesBackWithEveryFieldItWentInWith()
    {
        TempEntType type = Registered("CTESparks");

        var sent = new TeSparks
        {
            Origin      = new Vector(-2932f, 3218f, 60f),
            Magnitude   = 6,
            TrailLength = 3,
            Direction   = new Vector(0f, 0f, 1f),
        };

        var back = TempEntDeclarationReader.Read<TeSparks>(
                       new TempEntReader(type, Packed(sent, type)));

        // The BASE's field as well as its own: Origin is DT_TEParticleSystem's,
        // and a reader that walked only the leaf would leave it at zero — the
        // silent-default failure this whole form exists to remove.
        Assert.True(Same(back.Origin, sent.Origin),
            $"Origin is the BASE table's field and came back {back.Origin}, sent {sent.Origin}");

        Assert.True(back.Magnitude == sent.Magnitude, $"Magnitude {back.Magnitude}");
        Assert.True(back.TrailLength == sent.TrailLength, $"TrailLength {back.TrailLength}");
        Assert.True(Same(back.Direction, sent.Direction), $"Direction {back.Direction}");
    }

    /// <summary>
    /// THE SIGNEDNESS HAZARD, MEASURED RATHER THAN ASSERTED — and it is a
    /// hazard of the form being replaced, not of the one replacing it.
    ///
    /// <para>
    /// I first wrote this test claiming the declaration reader fixes a signed/
    /// unsigned bug, and mutating the reader to always call <c>Int</c> left it
    /// GREEN. The reason is worth keeping: <c>Int</c> and <c>UInt</c> differ only
    /// for a one- or two-byte field, and the declaration form puts the value back
    /// in the MEMBER it came from — a <c>byte</c> — so the narrowing cast
    /// restores the bits whichever accessor ran. The declaration form is immune
    /// by construction.
    /// </para>
    ///
    /// <para>
    /// The by-name form is not, because it reads into an <c>int</c> local and
    /// nothing narrows it back. That is what the first assertion below pins, with
    /// the actual number: this is the drift being removed.
    /// </para>
    /// </summary>
    [Fact]
    public void AByteFieldReadTheOldWayCanComeBackNegativeAndThroughTheDeclarationCannot()
    {
        TempEntType type = Registered("CTEShatterSurface");

        var sent = new TeShatterSurface
        {
            Origin      = new Vector(1f, 2f, 3f),
            FrontColorR = 200,
            FrontColorG = 201,
            BackColorR  = 202,
        };

        byte[] blob = Packed(sent, type);
        var    te   = new TempEntReader(type, blob);

        // THE HAZARD, as a number. A call site that reaches for Int on a
        // one-byte field gets a colour of -56, and nothing anywhere says so.
        Assert.True(te.Int("FrontColorR") == -56,
            $"an unsigned colour byte of 200 read through te.Int is -56, which is what makes "
          + $"the accessor choice load-bearing at a call site; it read {te.Int("FrontColorR")}");

        Assert.True(te.UInt("FrontColorR") == 200,
            $"and 200 through te.UInt — the same byte, {te.UInt("FrontColorR")}");

        // AND THE DECLARATION FORM CANNOT LAND ON THE WRONG ONE, because the
        // value goes back into the byte it came out of.
        var back = TempEntDeclarationReader.Read<TeShatterSurface>(
                       new TempEntReader(type, blob));

        Assert.True(back.FrontColorR == 200 && back.FrontColorG == 201 && back.BackColorR == 202,
            $"the declaration read [{back.FrontColorR}, {back.FrontColorG}, {back.BackColorR}], "
          + "sent [200, 201, 202]");
    }

    /// <summary>
    /// The one nested table in the inventory, read through its own declaration
    /// and its own wire names — the arm without which the outer declaration
    /// reads nothing at all and every value the effect carries is a zero.
    /// </summary>
    [Fact]
    public void TheNestedEffectDataIsReadThroughItsOwnDeclaration()
    {
        TempEntType type = Registered("CTEEffectDispatch");

        var sent = new TeEffectDispatch
        {
            EffectData = new TeEffectData
            {
                Origin      = new Vector(64f, -128f, 32f),
                Start       = new Vector(1f, 2f, 3f),
                Scale       = 2.5f,
                Magnitude   = 7f,
                EffectName  = 41,
            },
        };

        var back = TempEntDeclarationReader.Read<TeEffectDispatch>(
                       new TempEntReader(type, Packed(sent, type)));

        Assert.True(Same(back.EffectData.Origin, sent.EffectData.Origin),
            $"the nested Origin came back {back.EffectData.Origin}, sent {sent.EffectData.Origin}");

        Assert.True(Same(back.EffectData.Start, sent.EffectData.Start),
            $"and Start {back.EffectData.Start}");

        Assert.True(back.EffectData.EffectName == 41,
            $"and EffectName {back.EffectData.EffectName} — the index the dispatch resolves its "
          + "name from, which is the first thing the client reads and the one that decides "
          + "whether anything is drawn at all");
    }

    /// <summary>
    /// The inventory's one array, element for element. Reflection cannot see
    /// past element zero of an <c>[InlineArray]</c>, so this is the shape most
    /// likely to come back as one point and fifteen origins.
    /// </summary>
    [Fact]
    public void TheSplineRunComesBackElementForElement()
    {
        TempEntType type = Registered("CTEBeamSpline");

        static Vector Point(int i) => new(i * 1f, i * 10f, i * 100f);

        var sent = new TeBeamSpline { Points = 6 };

        for (int i = 0; i < TeBeamSpline.MaxSplinePoints; i++)
            sent.Spline[i] = Point(i);

        var back = TempEntDeclarationReader.Read<TeBeamSpline>(
                       new TempEntReader(type, Packed(sent, type)));

        Assert.True(back.Points == 6, $"the count came back {back.Points}");

        for (int i = 0; i < TeBeamSpline.MaxSplinePoints; i++)
            Assert.True(Same(back.Spline[i], Point(i)),
                $"point {i} came back {back.Spline[i]}, sent {Point(i)} — an array read through "
              + "element zero only would have every one of these but the first at the origin");

        // AND THE FAR END IS REALLY THERE: two all-zero runs would compare equal
        // at every index and prove nothing, so the last one is asserted by value.
        Assert.True(Same(back.Spline[15], new Vector(15f, 150f, 1500f)),
            $"point fifteen is at the far end of the run and came back {back.Spline[15]}");
    }

    [Fact]
    public void ItReportsHowManyMembersItFilledSoASilentZeroCannotPass()
    {
        TempEntType type = Registered("CTESparks");

        var sent = new TeSparks
        {
            Origin = Vector.Zero, Magnitude = 1, TrailLength = 1, Direction = Vector.Zero,
        };

        var into = new TeSparks();
        int filled = TempEntDeclarationReader.ReadChain(
                         new TempEntReader(type, Packed(sent, type)), into);

        // Four: Origin from DT_TEParticleSystem, and Magnitude, TrailLength and
        // Direction of its own. An exact number rather than "more than zero",
        // because the failure being guarded is a reader that quietly stops after
        // the leaf and the count for that is three.
        Assert.True(filled == 4,
            $"the chain has four [TeProp] members across two levels and the reader filled "
          + $"{filled}; three means it read the leaf and skipped the base");
    }

    /// <summary>
    /// THE DECLARATION FORM AND THE BY-NAME FORM READ THE SAME BLOB THE SAME
    /// WAY, which is what makes converting a call site a refactor rather than a
    /// change. Written against the by-name calls the client actually made, so
    /// that this fails if the declaration and the hand-written reads disagree
    /// about any of the three: the field, its type, or its signedness.
    /// </summary>
    [Fact]
    public void TheDeclarationReadsWhatTheByNameCallsItReplacesRead()
    {
        TempEntType type = Registered("CTEEffectDispatch");

        var sent = new TeEffectDispatch
        {
            EffectData = new TeEffectData
            {
                Origin     = new Vector(-2932f, 3218f, 60f),
                Start      = new Vector(11f, 22f, 33f),
                Scale      = 1.5f,
                EffectName = 41,
            },
        };

        byte[] blob = Packed(sent, type);

        // The old form, verbatim: a name and a hand-picked accessor per field.
        var byName = new TempEntReader(type, blob);
        int    nameIndex = byName.UInt("EffectName");
        Vector origin    = byName.Vector("Origin");
        Vector start     = byName.Vector("Start");
        float  scale     = byName.Float("Scale");

        // The new one: one object, one hand-over.
        var byDeclaration = TempEntDeclarationReader.Read<TeEffectDispatch>(
                                new TempEntReader(type, blob));

        Assert.True(byDeclaration.EffectData.EffectName == nameIndex,
            $"EffectName: {byDeclaration.EffectData.EffectName} through the declaration, "
          + $"{nameIndex} through te.UInt");

        Assert.True(Same(byDeclaration.EffectData.Origin, origin),
            $"Origin: {byDeclaration.EffectData.Origin} against {origin}");

        Assert.True(Same(byDeclaration.EffectData.Start, start),
            $"Start: {byDeclaration.EffectData.Start} against {start}");

        Assert.True(byDeclaration.EffectData.Scale == scale,
            $"Scale: {byDeclaration.EffectData.Scale} against {scale}");
    }
}
