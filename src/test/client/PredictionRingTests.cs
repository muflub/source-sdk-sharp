using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE PREDICTION RING AND THE JUDGE, which are now the only ones.
///
/// <para>
/// They were twins: a native <c>SourceSharpPredictionRing</c>
/// (<c>prediction_ring.h</c>) with twenty gtests of its own, and this managed
/// copy carrying the same two rules. plan_entity.md step 6f deleted the native
/// one — the client's ring is owned by the managed player and the tracked-class
/// dev commands that were the native ring's only other caller went with it — so
/// the gtests were PORTED here rather than dropped. Every fact below that names
/// a rule is one of them, restated against this API.
/// </para>
///
/// <para>
/// The two rules, because their absence is a silent wrong answer rather than an
/// error. (1) A slot remembers WHICH command it holds: the ring is indexed
/// modulo 90, so command 95 lands where command 5 did, and a compare against a
/// slot holding a different command is two unrelated states reported as a
/// misprediction. (2) A length that disagrees is a refusal and never a
/// truncation: a compare at the wrong length silently checks half the state and
/// calls the rest a hold.
/// </para>
/// </summary>
public class PredictionRingTests
{
    // This class's own catalog of the shared declarations (statics to zero).
    private readonly NetStateCatalog _catalog = new(TestServices.ClientGame, new RecordingLog());

    static PredictionRingTests()
    {
        StandaloneNetStates.Discover(typeof(WeaponNetState).Assembly, new RecordingLog());
    }

    [Fact]
    public void ASlotRemembersWhichCommandItHolds()
    {
        var ring = new PredictionRing();
        ring.Init(4);

        byte[] five = { 5, 5, 5, 5 };
        Assert.True(ring.SaveCommand(5, five));

        // Command 95 lands in slot 5's place; command 5's bytes must not
        // answer for it — that would be two unrelated states compared.
        Assert.True(ring.CommandBytes(95).IsEmpty);
        Assert.False(ring.CommandBytes(5).IsEmpty);

        Assert.True(ring.SaveCommand(95, new byte[] { 9, 9, 9, 9 }));
        Assert.True(ring.CommandBytes(5).IsEmpty, "the newer command evicted the slot");
    }

    [Fact]
    public void AWrongLengthIsARefusalNotATruncation()
    {
        var ring = new PredictionRing();
        ring.Init(4);

        Assert.False(ring.SaveCommand(1, new byte[3]));
        Assert.False(ring.SaveCommand(1, new byte[5]));
        Assert.False(ring.SavePristine(new byte[8]));

        // Re-init at a new length forgets everything.
        Assert.True(ring.SaveCommand(1, new byte[4]));
        ring.Init(8);
        Assert.True(ring.CommandBytes(1).IsEmpty);
    }

    [Fact]
    public void ThePokeDamagesExactlyOneByteOfOneSlot()
    {
        var ring = new PredictionRing();
        ring.Init(4);
        ring.SaveCommand(7, new byte[] { 1, 2, 3, 4 });

        Assert.True(ring.CorruptCommand(7, 2));
        Assert.Equal(new byte[] { 1, 2, unchecked((byte)(3 ^ 0xFF)), 4 },
                     ring.CommandBytes(7).ToArray());

        Assert.False(ring.CorruptCommand(8, 0), "a command the ring does not hold");
        Assert.False(ring.CorruptCommand(7, 4), "an offset off the end");
    }

    [Fact]
    public void TheJudgeToleratesAFloatInsideItsDeclaredTolerance()
    {
        // The weapon's timers carry TD_MSECTOLERANCE (0.001): a difference
        // smaller than a millisecond is two runs of the same arithmetic.
        var state = StandaloneNetStates.Of(typeof(WeaponNetState));
        int size = state.Size;
        uint typeId = NetSchema.TypeIdOf(EntityNames.SsWeapon);

        byte[] predicted = new byte[size];
        byte[] arrived = new byte[size];

        int at = state["NextPrimaryAttack"].Offset;
        BitConverter.GetBytes(10.0000f).CopyTo(predicted, at);
        BitConverter.GetBytes(10.0005f).CopyTo(arrived, at);

        int standing = PredictionJudge.Judge(_catalog, typeId, predicted, arrived,
            out string fields, out int tolerated);

        Assert.Equal(0, standing);
        Assert.Equal(1, tolerated);
        Assert.Contains("NextPrimaryAttack (tolerated)", fields);

        // Past the tolerance it stands, by name.
        BitConverter.GetBytes(10.01f).CopyTo(arrived, at);
        standing = PredictionJudge.Judge(_catalog, typeId, predicted, arrived, out fields, out _);

        Assert.True(standing > 0);
        Assert.Contains("NextPrimaryAttack", fields);
        Assert.DoesNotContain("(tolerated)", fields);
    }

    [Fact]
    public void TheJudgeNeverToleratesAnInteger()
    {
        // Clip differs by one: an integer either matches or it does not,
        // whatever tolerance anything declares.
        var state = StandaloneNetStates.Of(typeof(WeaponNetState));
        uint typeId = NetSchema.TypeIdOf(EntityNames.SsWeapon);

        byte[] predicted = new byte[state.Size];
        byte[] arrived = new byte[state.Size];

        int at = state["Clip"].Offset;
        BitConverter.GetBytes(17).CopyTo(predicted, at);
        BitConverter.GetBytes(18).CopyTo(arrived, at);

        int standing = PredictionJudge.Judge(_catalog, typeId, predicted, arrived,
            out string fields, out _);

        Assert.True(standing > 0);
        Assert.Contains("Clip", fields);
    }

    //=========================================================================
    // The rules, ported from prediction_ring_test.cpp.
    //=========================================================================

    /// <summary>Bytes that differ from a command's, in a way a walk can find.</summary>
    private static byte[] BlobFor(int command, int bytes)
    {
        byte[] blob = new byte[bytes];

        for (int i = 0; i < bytes; i++)
            blob[i] = (byte)((command * 31 + i) & 0xFF);

        return blob;
    }

    [Fact]
    public void ANewRingHoldsNothingUntilItIsSized()
    {
        var ring = new PredictionRing();

        Assert.Equal(0, ring.BlobBytes);
        Assert.False(ring.SaveCommand(1, BlobFor(1, 16)));

        ring.Init(16);
        Assert.Equal(16, ring.BlobBytes);
        Assert.True(ring.SaveCommand(1, BlobFor(1, 16)));
    }

    [Fact]
    public void SizingToNothingLeavesTheRingRefusingEverything()
    {
        var ring = new PredictionRing();
        ring.Init(0);

        Assert.Equal(0, ring.BlobBytes);
        Assert.False(ring.SaveCommand(1, Array.Empty<byte>()));
        Assert.True(ring.CommandBytes(1).IsEmpty);
    }

    [Fact]
    public void ASavedCommandComparesEqualToTheBytesItWasSavedFrom()
    {
        var ring = new PredictionRing();
        ring.Init(64);

        byte[] blob = BlobFor(42, 64);
        Assert.True(ring.SaveCommand(42, blob));

        Assert.True(ring.CommandBytes(42).SequenceEqual(blob));
    }

    [Fact]
    public void AMispredictionIsLocatedAtItsFirstDifferingByte()
    {
        var ring = new PredictionRing();
        ring.Init(64);

        byte[] predicted = BlobFor(42, 64);
        ring.SaveCommand(42, predicted);

        byte[] arrived = (byte[])predicted.Clone();
        arrived[5] ^= 0x01;
        arrived[60] ^= 0x80;

        var record = PredictionErrorAnalysis.Describe(_catalog, 0, 0, 42,
            ring.CommandBytes(42), arrived, DateTime.UtcNow);

        Assert.NotNull(record);
        Assert.Equal(5, record!.FirstDiffOffset);
        Assert.Equal(2, record.StandingBytes);
    }

    [Fact]
    public void ModuloAliasingAnswersNoBasisAndNeverAWrongCommandsBytes()
    {
        // Command 5 and command 95 share a slot. Asking about the one the slot
        // does not hold must be "no basis" — two unrelated states compared is
        // not a misprediction report.
        var ring = new PredictionRing();
        ring.Init(32);

        ring.SaveCommand(5, BlobFor(5, 32));

        Assert.True(ring.CommandBytes(5 + PredictionRing.Slots).IsEmpty);
    }

    [Fact]
    public void TheLaterCommandEvictsTheEarlierOneFromTheirSharedSlot()
    {
        var ring = new PredictionRing();
        ring.Init(32);

        int alias = 5 + PredictionRing.Slots;
        ring.SaveCommand(5, BlobFor(5, 32));
        Assert.True(ring.SaveCommand(alias, BlobFor(alias, 32)));

        Assert.True(ring.CommandBytes(5).IsEmpty);
        Assert.True(ring.CommandBytes(alias).SequenceEqual(BlobFor(alias, 32)));
    }

    [Fact]
    public void EverySlotHoldsItsOwnCommand()
    {
        // Ninety saves, ninety independent reads: no slot bleeds into another.
        var ring = new PredictionRing();
        ring.Init(24);

        for (int c = 100; c < 100 + PredictionRing.Slots; c++)
            Assert.True(ring.SaveCommand(c, BlobFor(c, 24)));

        for (int c = 100; c < 100 + PredictionRing.Slots; c++)
            Assert.True(ring.CommandBytes(c).SequenceEqual(BlobFor(c, 24)), $"command {c}");
    }

    [Fact]
    public void ReSizingAtTheSameLengthStillForgets()
    {
        // The reload case where the class happened NOT to be reshaped: the
        // bytes would still fit, and that is exactly why forgetting must not
        // depend on the length moving — they were written by an assembly that
        // no longer exists.
        var ring = new PredictionRing();
        ring.Init(64);
        ring.SaveCommand(3, BlobFor(3, 64));
        ring.SavePristine(BlobFor(3, 64));

        ring.Init(64);

        Assert.True(ring.CommandBytes(3).IsEmpty);
        Assert.True(ring.Pristine.IsEmpty);
        Assert.Equal(64, ring.BlobBytes);
    }

    [Fact]
    public void ResetForgetsButKeepsTheAllocation()
    {
        var ring = new PredictionRing();
        ring.Init(64);
        ring.SaveCommand(1, BlobFor(1, 64));
        ring.SavePristine(BlobFor(1, 64));

        ring.Reset();

        Assert.Equal(64, ring.BlobBytes);
        Assert.True(ring.CommandBytes(1).IsEmpty);
        Assert.True(ring.Pristine.IsEmpty);
    }

    [Fact]
    public void NegativeCommandsAreRefused()
    {
        var ring = new PredictionRing();
        ring.Init(16);

        Assert.False(ring.SaveCommand(-1, BlobFor(0, 16)));
        Assert.True(ring.CommandBytes(-1).IsEmpty);
        Assert.False(ring.CorruptCommand(-1, 0));
    }

    [Fact]
    public void ThePristineCopyIsSavedAndReadBackWhole()
    {
        var ring = new PredictionRing();
        ring.Init(40);

        Assert.True(ring.Pristine.IsEmpty);

        byte[] fromServer = BlobFor(77, 40);
        Assert.True(ring.SavePristine(fromServer));
        Assert.True(ring.Pristine.SequenceEqual(fromServer));
    }

    //=========================================================================
    // The judge's own rules, ported from PredictionFieldTolerated's six gtests.
    //=========================================================================

    [Fact]
    public void AZeroToleranceNeverForgives()
    {
        byte[] was = BitConverter.GetBytes(1.0f);
        byte[] now = BitConverter.GetBytes(1.0000001f);

        Assert.False(PredictionJudge.Tolerated(1, 0f, was, now));
    }

    [Fact]
    public void AVectorIsJudgedPerComponentAndNotByItsLength()
    {
        // Stock's CompareVector: each component against the tolerance, not the
        // magnitude of the difference. A vector out by 0.6 on ONE axis is out.
        byte[] was = new byte[12];
        byte[] now = new byte[12];

        BitConverter.GetBytes(1.0f).CopyTo(was, 0);
        BitConverter.GetBytes(2.0f).CopyTo(was, 4);
        BitConverter.GetBytes(3.0f).CopyTo(was, 8);

        BitConverter.GetBytes(1.4f).CopyTo(now, 0);
        BitConverter.GetBytes(2.0f).CopyTo(now, 4);
        BitConverter.GetBytes(3.0f).CopyTo(now, 8);

        Assert.True(PredictionJudge.Tolerated(2, 0.5f, was, now));

        BitConverter.GetBytes(3.6f).CopyTo(now, 8);
        Assert.False(PredictionJudge.Tolerated(2, 0.5f, was, now));
    }

    [Fact]
    public void AQAngleToleratesLikeAVector()
    {
        byte[] was = new byte[12];
        byte[] now = new byte[12];

        BitConverter.GetBytes(10.0f).CopyTo(was, 4);
        BitConverter.GetBytes(10.1f).CopyTo(now, 4);

        Assert.True(PredictionJudge.Tolerated(7, 0.25f, was, now));

        BitConverter.GetBytes(10.5f).CopyTo(now, 4);
        Assert.False(PredictionJudge.Tolerated(7, 0.25f, was, now));
    }

    [Fact]
    public void AnIntegerKindIsNeverTolerated()
    {
        // Whatever tolerance is declared. An integer either matches or it does
        // not — the same rule the WeaponNetState fact above reaches through the
        // whole judge, held here on the predicate itself.
        byte[] was = BitConverter.GetBytes(100);
        byte[] now = BitConverter.GetBytes(101);

        Assert.False(PredictionJudge.Tolerated(0, 100f, was, now));
        Assert.False(PredictionJudge.Tolerated(3, 100f, was, now));
        Assert.False(PredictionJudge.Tolerated(4, 100f, was, now));
    }

    [Fact]
    public void AFieldTooShortForItsKindIsRefusedRatherThanReadPastItsEnd()
    {
        // A vector needs twelve bytes. Eight is a schema that has moved under
        // the judge, and forgiving it would forgive whatever follows the field.
        byte[] was = new byte[8];
        byte[] now = new byte[8];
        now[0] = 1;

        Assert.False(PredictionJudge.Tolerated(2, 100f, was, now));
    }
}
