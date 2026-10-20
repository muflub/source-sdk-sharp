using System.Buffers.Binary;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The capture behind the prediction error visualiser.
///
/// <para>
/// WHAT THIS EXISTS TO FIX, stated once so every case below reads against it:
/// the console line says "10 byte(s) differ, first at offset 56: Origin,
/// Velocity" and that sentence is the same sentence whether the player is out
/// by a hair or by a metre. A byte count is not a distance, it is not a
/// direction, and it says nothing about the tolerance the field declared. Every
/// fact here is one of those missing quantities being made available, or one way
/// of losing it being made impossible.
/// </para>
///
/// <para>
/// The real <see cref="PlayerNetState"/> declaration is used rather than a
/// fixture, deliberately: Origin's 0.0625 and Velocity's 0.5 are the numbers in
/// the report a reader is holding, and a test against invented tolerances would
/// agree with itself while the tool disagreed with the game.
/// </para>
/// </summary>
public class PredictionErrorsTests
{
    // This class's own catalog of the shared declarations (statics to zero).
    private readonly NetStateCatalog _catalog = NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog());

    private static uint PlayerTypeId()
    {
        // Discovery fills the by-type-id map DescribeOffset reads — the same
        // walk both modules run at startup, over the same shared assembly.
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());
        return NetSchema.TypeIdOf(EntityNames.SsPlayer);
    }

    private static NetFieldLayout Field(string name)
    {
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());
        return StandaloneNetStates.Of(typeof(PlayerNetState))[name];
    }

    /// <summary>A blob of the player's declared size, all zero.</summary>
    private static byte[] Blob()
    {
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());
        return new byte[StandaloneNetStates.Of(typeof(PlayerNetState)).Size];
    }

    private static void PutFloat(byte[] blob, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            blob.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

    /// <summary>Writes a three-float vector at a field's offset.</summary>
    private static void PutVector(byte[] blob, int offset, float x, float y, float z)
    {
        PutFloat(blob, offset + 0, x);
        PutFloat(blob, offset + 4, y);
        PutFloat(blob, offset + 8, z);
    }

    private PredictionErrorRecord Describe(byte[] predicted, byte[] arrived)
    {
        var record = PredictionErrorAnalysis.Describe(_catalog, 0, PlayerTypeId(), 100,
            predicted, arrived, DateTime.UnixEpoch);

        Assert.NotNull(record);
        return record;
    }

    private static PredictionFieldError FieldOf(PredictionErrorRecord record, string name)
        => Assert.Single(record.Fields, field => field.Name == name);

    //-------------------------------------------------------------------------
    // Costing nothing when nothing mispredicts.
    //-------------------------------------------------------------------------

    [Fact]
    public void TwoIdenticalImagesProduceNoRecordAtAll()
    {
        // The whole cheapness claim rests on this: a prediction that held
        // allocates nothing and leaves no record to page through.
        Assert.Null(PredictionErrorAnalysis.Describe(_catalog, 0, PlayerTypeId(), 100,
            Blob(), Blob(), DateTime.UnixEpoch));
    }

    [Fact]
    public void AnEmptyCompareProducesNoRecord()
    {
        // A zero-length blob is a compare with nothing to stand on, not a
        // misprediction of everything.
        Assert.Null(PredictionErrorAnalysis.Describe(_catalog, 0, PlayerTypeId(), 100,
            Array.Empty<byte>(), Array.Empty<byte>(), DateTime.UnixEpoch));
    }

    //-------------------------------------------------------------------------
    // The distance the console line could not print.
    //-------------------------------------------------------------------------

    [Fact]
    public void AVectorReportsEachComponentSeparately()
    {
        // "Origin differs" cannot say that x and y held and z is the one that
        // moved. Three components is the difference between a diagnosis and a
        // hint.
        int origin = Field("Origin").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin, 100f, 200f, 300f);
        PutVector(arrived,   origin, 100f, 200f, 308f);

        var field = FieldOf(Describe(predicted, arrived), "Origin");

        Assert.Equal(3, field.Components.Count);
        Assert.Equal(0d, field.Components[0].Delta);
        Assert.Equal(0d, field.Components[1].Delta);
        Assert.Equal(-8d, field.Components[2].Delta, 4);
    }

    [Fact]
    public void ACommponentCarriesBothSidesValuesAndNotOnlyTheGap()
    {
        // A delta of 8 units means something different at the origin than at the
        // far end of the map, and a reader chasing a bug wants the two numbers
        // that produced it.
        int origin = Field("Origin").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin, 0f, 0f, 300f);
        PutVector(arrived,   origin, 0f, 0f, 308f);

        var component = FieldOf(Describe(predicted, arrived), "Origin").Components[2];

        Assert.Equal(300d, component.Predicted, 4);
        Assert.Equal(308d, component.Server, 4);
    }

    [Fact]
    public void TheDeltaIsSignedSoASystematicDriftIsDistinguishableFromNoise()
    {
        // A drift always in the same direction is a bug in the movement code; a
        // delta that alternates is rounding. Taking the magnitude here would
        // erase exactly the difference between them.
        int origin = Field("Origin").Offset;

        byte[] under = Blob(), over = Blob(), server = Blob();
        PutVector(server, origin, 0f, 0f, 100f);
        PutVector(under,  origin, 0f, 0f, 90f);
        PutVector(over,   origin, 0f, 0f, 110f);

        Assert.Equal(-10d, FieldOf(Describe(under, server), "Origin").Components[2].Delta, 4);
        Assert.Equal(10d,  FieldOf(Describe(over,  server), "Origin").Components[2].Delta, 4);
    }

    //-------------------------------------------------------------------------
    // Against the declared tolerance, which is the reading that decides whether
    // any of it matters.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFieldCarriesTheToleranceItsDeclarationActuallyStates()
    {
        // Not a number this test chose: [Predicted(0.0625f)] on Origin, which is
        // stock's coordTolerance. If the declaration moves, this says so.
        byte[] predicted = Blob(), arrived = Blob();
        int origin = Field("Origin").Offset;
        PutVector(predicted, origin, 0f, 0f, 0f);
        PutVector(arrived,   origin, 0f, 0f, 9f);

        Assert.Equal(0.0625f, FieldOf(Describe(predicted, arrived), "Origin").Tolerance);
    }

    [Fact]
    public void TheToleranceRatioSaysHowManyTimesOutAFieldWent()
    {
        // THE NUMBER THE LOG LINE CANNOT PRINT. A ratio is comparable across
        // fields whose units are not, so Origin and Velocity can be ranked
        // against each other rather than only listed together.
        int origin = Field("Origin").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin, 0f, 0f, 0f);
        PutVector(arrived,   origin, 0f, 0f, 0.5f);   // 8 x 0.0625

        Assert.Equal(8d, FieldOf(Describe(predicted, arrived), "Origin").ToleranceRatio!.Value, 4);
    }

    [Fact]
    public void AFieldWithNoDeclaredToleranceHasNoRatioRatherThanAZeroOne()
    {
        // LadderNormal is [Predicted] with no number, which is stock's "exact".
        // Dividing by that tolerance would produce an infinity, and reporting a
        // ratio of zero would read as a field comfortably inside a limit it does
        // not have.
        int ladder = Field("LadderNormal").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, ladder, 0f, 0f, 0f);
        PutVector(arrived,   ladder, 0f, 0f, 1f);

        var field = FieldOf(Describe(predicted, arrived), "LadderNormal");

        Assert.Equal(0f, field.Tolerance);
        Assert.Null(field.ToleranceRatio);
    }

    [Fact]
    public void ADifferenceInsideToleranceIsMarkedForgivenAndStandsForNothing()
    {
        // predictioncopy.cpp's rule: inside the tolerance is not an error. The
        // record must agree with the meter about that or the two disagree in
        // public.
        int origin = Field("Origin").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin, 0f, 0f, 0f);
        PutVector(arrived,   origin, 0f, 0f, 0.03f);   // under 0.0625

        var record = Describe(predicted, arrived);

        Assert.True(FieldOf(record, "Origin").Tolerated, "a difference under the declared tolerance is forgiven");
        Assert.Equal(0, record.StandingBytes);
        Assert.False(record.Mispredicted);
    }

    [Fact]
    public void AToleratedOnlyCompareIsStillKeptBecauseGrowingDriftIsTheWarning()
    {
        // The early-warning rule. A capture that only fired once a difference
        // had already broken through its tolerance could never show the run-up
        // to it, which is the shape the user's six log lines hint at and cannot
        // establish.
        int origin = Field("Origin").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin, 0f, 0f, 0f);
        PutVector(arrived,   origin, 0f, 0f, 0.03f);

        var ring = new PredictionErrorRing(8, _catalog);
        var record = ring.Observe(PlayerTypeId(), 100, predicted, arrived, DateTime.UnixEpoch);

        Assert.NotNull(record);
        Assert.Equal(1, ring.Count);
        Assert.Equal(1, ring.ToleratedOnly);
        Assert.Equal(0, ring.Mispredicted);
    }

    [Fact]
    public void AnIntegerFieldIsReportedAsUntolerableEvenThoughItDiffers()
    {
        // "No tolerance declared" and "could not have one" are different facts.
        // Only the float-shaped kinds are tolerable, per component, exactly as
        // stock's CompareVector is — an integer either matches or it does not.
        int buttons = Field("OldButtons").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        BinaryPrimitives.WriteInt32LittleEndian(predicted.AsSpan(buttons, 4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(arrived.AsSpan(buttons, 4), 6);

        var field = FieldOf(Describe(predicted, arrived), "OldButtons");

        Assert.False(field.Tolerable, "an integer kind can never be tolerated");
        Assert.False(field.Tolerated);
    }

    [Fact]
    public void AnIntegerFieldStillReportsBothValuesAsNumbers()
    {
        // Untolerable is not unreadable: which button mask was predicted and
        // which arrived is the diagnosis for a whole family of these.
        int buttons = Field("OldButtons").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        BinaryPrimitives.WriteInt32LittleEndian(predicted.AsSpan(buttons, 4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(arrived.AsSpan(buttons, 4), 6);

        var component = Assert.Single(FieldOf(Describe(predicted, arrived), "OldButtons").Components);

        Assert.Equal(4d, component.Predicted);
        Assert.Equal(6d, component.Server);
    }

    [Fact]
    public void AStringGetsNoNumericComponentsRatherThanAMeaninglessSubtraction()
    {
        // Subtracting text produces a number, and a reader shown "delta 47" for
        // a name would be misled by their own tool.
        Assert.Empty(PredictionErrorAnalysis.Components(4, new byte[] { 1, 2, 3, 4 },
                                                           new byte[] { 5, 6, 7, 8 }));
    }

    //-------------------------------------------------------------------------
    // Agreeing with the meter it visualises.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheStandingByteCountMatchesTheJudgeTheConsoleLinePrints()
    {
        // THE INVARIANT THAT MAKES THIS TOOL TRUSTWORTHY. If the visualiser and
        // the console meter can disagree about how many bytes stand, the tool is
        // worse than nothing — a reader would have two numbers and no way to
        // pick. They agree because they call the same tolerance function over
        // the same declaration, and this is what says so.
        int origin = Field("Origin").Offset;
        int velocity = Field("Velocity").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin,   0f, 0f, 0f);
        PutVector(arrived,   origin,   0f, 0f, 4f);
        PutVector(predicted, velocity, 0f, 0f, 0f);
        PutVector(arrived,   velocity, 0f, 0f, 0.1f);  // inside Velocity's 0.5

        int judged = PredictionJudge.Judge(_catalog, PlayerTypeId(), predicted, arrived,
                                           out _, out int toleratedFields);

        var record = Describe(predicted, arrived);

        Assert.Equal(judged, record.StandingBytes);
        Assert.Equal(toleratedFields, record.ToleratedFields);
    }

    [Fact]
    public void TheFirstDifferingOffsetMatchesTheOneTheLogLineNames()
    {
        // "first at offset 56" is the reader's only anchor into the blob today,
        // so the record has to land on the same byte or the two views cannot be
        // read side by side.
        int origin = Field("Origin").Offset;
        int velocity = Field("Velocity").Offset;

        // TWO differing bytes, in two fields, and the assertion is about the
        // EARLIER one. With a single difference "first" and "last" are the same
        // byte and the case could not tell them apart — it would pass under an
        // implementation that reported whichever it saw most recently, which is
        // precisely the mistake worth catching.
        Assert.True(velocity > origin,
            "this case needs Velocity to sit after Origin for 'first' to mean anything");

        // The fields' FIRST bytes, poked directly. Floats chosen for their
        // values would not do: 5.0f is 00 00 A0 40, whose leading bytes match a
        // zero, so the difference would land mid-field and the case would be
        // asserting an accident of IEEE754 rather than the rule.
        byte[] predicted = Blob(), arrived = Blob();
        arrived[origin] = 0xFF;
        arrived[velocity] = 0xFF;

        var record = Describe(predicted, arrived);

        Assert.Equal(origin, record.FirstDiffOffset);
        Assert.Equal(new[] { "Origin", "Velocity" }, record.Fields.Select(field => field.Name));
    }

    [Fact]
    public void ARecordNamesTheClassAndNotOnlyItsTypeId()
    {
        // A type id is what crosses the ABI; a classname is what a reader
        // recognises in a list beside a console log.
        int origin = Field("Origin").Offset;

        byte[] predicted = Blob(), arrived = Blob();
        PutVector(predicted, origin, 0f, 0f, 0f);
        PutVector(arrived,   origin, 9f, 0f, 0f);

        Assert.Equal(EntityNames.SsPlayer, Describe(predicted, arrived).ClassName);
    }

    [Fact]
    public void AnUndeclaredTypeIdNamesNoClassRatherThanAPlaceholder()
    {
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());

        Assert.Equal("", _catalog.ClassNameOf(0xDEADBEEF));
    }

    [Fact]
    public void AByteNoFieldClaimsIsLabelledAsPaddingWithItsOffset()
    {
        // The judge's own label, kept identical: a difference in padding is a
        // layout disagreement rather than a misprediction, and it must not be
        // silently attributed to whichever field happens to sit nearby.
        //
        // THE BARREL AND NOT THE PLAYER, because the player's layout has no
        // padding byte in it — every byte of it is claimed. Reaching for a
        // declaration that does have one is what keeps this case able to fail.
        StandaloneNetStates.Discover(typeof(BarrelNetState).Assembly, new RecordingLog());

        uint typeId = NetSchema.TypeIdOf(EntityNames.SsBarrel);
        var state = StandaloneNetStates.Of(typeof(BarrelNetState));
        int padding = -1;

        for (int offset = 0; offset < state.Size; offset++)
        {
            if (_catalog.DescribeOffset(typeId, offset,
                    out _, out _, out _, out _, out _) == 0)
            {
                padding = offset;
                break;
            }
        }

        Assert.True(padding >= 0, "the barrel's layout must contain a padding byte for this case to mean anything");

        byte[] predicted = new byte[state.Size], arrived = new byte[state.Size];
        arrived[padding] = 0xFF;

        var record = PredictionErrorAnalysis.Describe(_catalog, 0, typeId, 100,
            predicted, arrived, DateTime.UnixEpoch);

        Assert.NotNull(record);
        Assert.Equal($"(padding@{padding})", Assert.Single(record.Fields).Name);

        // A padding difference stands: nothing declared a tolerance for a byte
        // nothing declared at all.
        Assert.Equal(1, record.StandingBytes);
    }

    //-------------------------------------------------------------------------
    // The ring.
    //-------------------------------------------------------------------------

    private static PredictionErrorRecord Row(long sequence, int command, int standing = 1)
        => new(sequence, command, 1, "ss_player", 16, standing, 0, 0,
               DateTime.UnixEpoch, Array.Empty<PredictionFieldError>());

    [Fact]
    public void TheRingKeepsTheNewestAndForgetsTheOldest()
    {
        var ring = new PredictionErrorRing(3);

        for (int i = 0; i < 5; i++)
            ring.Add(Row(i, 1000 + i));

        var held = ring.Since(0);

        Assert.Equal(3, held.Count);
        Assert.Equal(1002, held[0].Command);
        Assert.Equal(1004, held[2].Command);
    }

    [Fact]
    public void TheCountersSurviveWhatTheRingHasAlreadyForgotten()
    {
        // A rate computed from what a 256-deep ring still holds would read as
        // though the game had only ever done 256 compares. The totals are kept
        // apart from the records for exactly that reason.
        var ring = new PredictionErrorRing(3);

        for (int i = 0; i < 10; i++)
            ring.Add(Row(i, 1000 + i));

        Assert.Equal(3, ring.Count);
        Assert.Equal(10, ring.Observed);
    }

    [Fact]
    public void TheRingCountsMissesApartFromDifferencesThatWereForgiven()
    {
        // Two very different states of health, and a single "compares that found
        // something" total would hide which one the game is in.
        var ring = new PredictionErrorRing(8);

        ring.Add(Row(0, 1, standing: 4));
        ring.Add(Row(1, 2, standing: 0));
        ring.Add(Row(2, 3, standing: 0));

        Assert.Equal(3, ring.Observed);
        Assert.Equal(1, ring.Mispredicted);
        Assert.Equal(2, ring.ToleratedOnly);
    }

    [Fact]
    public void AReaderCanAskForOnlyWhatArrivedAfterWhatTheyAlreadySaw()
    {
        var ring = new PredictionErrorRing(8);

        for (int i = 0; i < 5; i++)
            ring.Add(Row(i, 1000 + i));

        var since = ring.Since(3);

        Assert.Equal(2, since.Count);
        Assert.Equal(1003, since[0].Command);
    }

    [Fact]
    public void AReaderFurtherBehindThanTheRingIsDeepGetsWhatIsLeft()
    {
        // Losing the oldest is what a ring is for; answering an error instead
        // would break a follower that merely paused.
        var ring = new PredictionErrorRing(2);

        for (int i = 0; i < 6; i++)
            ring.Add(Row(i, 1000 + i));

        Assert.Equal(2, ring.Since(0).Count);
    }

    [Fact]
    public void AResetClearsTheTotalsAndNotOnlyTheRecords()
    {
        // A reader asking for a clean window wants the rate to restart too; a
        // reset that left the totals would report a rate over a window that no
        // longer exists.
        var ring = new PredictionErrorRing(8);

        ring.Add(Row(0, 1));
        ring.Add(Row(1, 2));
        ring.Reset();

        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Observed);
        Assert.Empty(ring.Since(0));
    }

    [Fact]
    public void TheRingRefusesToBeBuiltWithNoRoomAtAll()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredictionErrorRing(0));
    }

    [Fact]
    public void ObservingTwoIdenticalImagesLeavesTheRingEmptyAndTheTotalsAlone()
    {
        // The cheapness claim again, this time at the ring's own door: a hold
        // must not advance a counter, or the misprediction rate the tab prints
        // would be diluted by every packet that arrived correct.
        var ring = new PredictionErrorRing(8);

        Assert.Null(ring.Observe(PlayerTypeId(), 100, Blob(), Blob(), DateTime.UnixEpoch));
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Observed);
    }
}
