using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Phase 9c3: the prediction schema is the SAME declaration the send table is
/// built from, asked per offset rather than shipped (Step 7's rule), with a
/// per-field [Predicted] tolerance whose default is stock's — exact, because
/// predictioncopy.cpp:736 engages a tolerance only when it is positive.
///
/// <para>
/// Every case here is a wrong console line being made impossible: a byte
/// attributed to the wrong half of a packed word names the wrong field in the
/// misprediction report, and nothing else would ever notice.
/// </para>
/// </summary>
public class PredictionSchemaTests
{
    // This class's own catalog of the shared declarations (statics to zero).
    private readonly NetStateCatalog _catalog = new(TestServices.ServerGame, new RecordingLog());

    /// <summary>
    /// Where a class's own state begins in the blob. The ENGINE BLOCK
    /// (plan_entity.md §3.2) is in front of it, so a byte offset handed to
    /// <c>DescribeOffset</c> is a blob offset and the declared fields are all
    /// past this.
    /// </summary>
    private const int Block = EngineBlock.SizeBytes;

    private static uint BarrelTypeId()
    {
        // Discovery populates the by-type-id map the describe reads; the same
        // walk both modules run at startup, over the same shared assembly.
        StandaloneNetStates.Discover(typeof(BarrelNetState).Assembly, new RecordingLog());
        return NetSchema.TypeIdOf(EntityNames.SsBarrel);
    }

    [Fact]
    public void ThePredictableSetIsADeclarationWithADefaultOfNo()
    {
        // The 9c5 gate: an entity rings and replays exactly when its class
        // declares [Predicted] fields. The player says yes, the beam says no
        // (nothing predicts a beam), and an unknown type id is refused rather
        // than defaulted either way.
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());

        Assert.True(_catalog.PredictedFieldCount(NetSchema.TypeIdOf(EntityNames.SsPlayer)) > 0,
            "the player must declare itself predictable or nothing replays it");

        Assert.Equal(0, _catalog.PredictedFieldCount(NetSchema.TypeIdOf(EntityNames.SsBeam)));

        Assert.Equal(-1, _catalog.PredictedFieldCount(0xDEADBEEF));
    }

    [Fact]
    public void AFloatFieldIsNamedWithItsDeclaredTolerance()
    {
        int answer = _catalog.DescribeOffset(BarrelTypeId(), Block + 0,
            out string name, out int start, out int bytes, out int kind, out float tolerance);

        Assert.Equal(1, answer);
        Assert.Equal("LastDamage", name);
        Assert.Equal(Block + 0, start);
        Assert.Equal(4, bytes);
        Assert.Equal(1, kind); // TypeCodeOf(Float) — the schema-hash numbering
        Assert.Equal(0.5f, tolerance); // [Predicted(0.5f)], stock's fall-velocity number
    }

    [Fact]
    public void APackedWordAttributesEachByteToItsOwnField()
    {
        // Thinks (16 bits at shift 0) and Broken (1 bit at shift 16) share the
        // barrel's dword at offset 4 OF ITS OWN STATE -- the engine block is in
        // front of that, which is what Block adds. Byte 5 is Thinks' high byte;
        // byte 6 is the byte Broken's bit lives in; byte 7 belongs to neither.
        uint typeId = BarrelTypeId();

        Assert.Equal(1, _catalog.DescribeOffset(typeId, Block + 5,
            out string thinks, out _, out _, out int thinksKind, out float thinksTol));
        Assert.Equal("Thinks", thinks);
        Assert.Equal(0, thinksKind); // TypeCodeOf(Int)
        Assert.Equal(0f, thinksTol); // no [Predicted]: stock's default, exact

        Assert.Equal(1, _catalog.DescribeOffset(typeId, Block + 6,
            out string broken, out _, out _, out int brokenKind, out _));
        Assert.Equal("Broken", broken);
        Assert.Equal(3, brokenKind); // TypeCodeOf(Bool)

        Assert.Equal(0, _catalog.DescribeOffset(typeId, Block + 7,
            out string nobody, out _, out _, out _, out _));
        Assert.Equal("", nobody);
    }

    [Fact]
    public void AVectorAnswersItsWholeExtent()
    {
        int answer = _catalog.DescribeOffset(BarrelTypeId(), Block + 13,
            out string name, out int start, out int bytes, out int kind, out _);

        Assert.Equal(1, answer);
        Assert.Equal("SpawnOrigin", name);
        Assert.Equal(Block + 8, start);
        Assert.Equal(12, bytes);
        Assert.Equal(2, kind); // TypeCodeOf(Vector)
    }

    [Fact]
    public void PaddingAndStrangersAreTheTwoDistinctNoes()
    {
        // Past the last declared byte is padding of a KNOWN type — answer 0 —
        // while a type id nothing declares is -1. The meter treats them
        // differently: padding is named as padding in the report, an unknown
        // type means the whole judgement falls back to raw bytes.
        Assert.Equal(0, _catalog.DescribeOffset(BarrelTypeId(), Block + 120,
            out _, out _, out _, out _, out _));

        Assert.Equal(-1, _catalog.DescribeOffset(0xDEADBEEF, 0,
            out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TheDefaultToleranceIsStocks()
    {
        // Quoted rather than invented: predictioncopy.cpp:736 reads
        // `bool usetolerance = tolerance > 0.0f`, so an undeclared tolerance
        // is zero and zero means exact.
        Assert.Equal(0f, new PredictedAttribute().Tolerance);
    }
}
