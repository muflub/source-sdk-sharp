using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TePropPlanner"/> against stock's arithmetic, case for case with
/// the gtests that hold tempent_schema.h (tempent_schema_test.cpp,
/// tempent_plan_test.cpp): the client still plans its RecvProps with that
/// header, so the server's managed plan and the client's native one must give
/// the same answer to the same description, or a join begins and stops.
/// </summary>
public class TePropPlannerTests
{
    internal static TeField F(string name, TePropKind kind, int offset, int size, int bits, TeFlags flags,
                              TeBits from = TeBits.Literal, int elems = 0, int stride = 0,
                              string? subTable = null, float low = 0f, float high = 0f)
        => new(name, kind, offset, size, bits, from, flags, low, high, subTable, elems, stride);

    internal static TeClass C(string className, string tableName, string? baseTable, int blobBytes,
                              params TeField[] fields)
        => new(className, tableName, baseTable, blobBytes, true, typeof(object), fields);

    private static readonly TeFlags CoordElem = TeFlags.Coord | TeFlags.VectorElem;

    [Fact]
    public void SymbolicWidthsResolveToWhatStockSpells()
    {
        Assert.Equal(11, TePropPlanner.ResolveBits(TeBits.MaxEdict, 0));
        Assert.Equal(13, TePropPlanner.ResolveBits(TeBits.ModelIndex, 0));
        Assert.Equal(10, TePropPlanner.ResolveBits(TeBits.EfMax, 0));
        Assert.Equal(10, TePropPlanner.ResolveBits(TeBits.AnimationSkin, 0));
        Assert.Equal(8, TePropPlanner.ResolveBits(TeBits.EffectFlag, 0));
        Assert.Equal(10, TePropPlanner.ResolveBits(TeBits.EffectDispatchString, 0));
        Assert.Equal(21, TePropPlanner.ResolveBits(TeBits.NetworkedEHandle, 0));
        // Q_log2(33) is the floor of log2: five, not the six bits 33 needs.
        Assert.Equal(5, TePropPlanner.ResolveBits(TeBits.MaxPlayersLog2, 0));
        Assert.Equal(4, TePropPlanner.ResolveBits(TeBits.Literal, 4));
        Assert.Equal(24, TePropPlanner.ResolveBits(TeBits.Literal, 24));
        Assert.Equal(-1, TePropPlanner.ResolveBits((TeBits)9, 8));
        Assert.Equal(-1, TePropPlanner.ResolveBits((TeBits)99, 8));
    }

    [Fact]
    public void CoordBeatsNoScaleBecauseTheEncoderChecksItFirst()
    {
        Assert.Equal(22, TePropPlanner.FloatWireBits(-1, TeFlags.Coord | TeFlags.NoScale));
        Assert.Equal(22, TePropPlanner.FloatWireBits(-1, TeFlags.Coord));
        Assert.Equal(32, TePropPlanner.FloatWireBits(-1, TeFlags.NoScale));
    }

    [Fact]
    public void EveryFloatEncodingCostsWhatBitbufSpends()
    {
        Assert.Equal(22, TePropPlanner.FloatWireBits(-1, TeFlags.Coord));
        Assert.Equal(22, TePropPlanner.FloatWireBits(-1, TeFlags.CoordMp));
        Assert.Equal(20, TePropPlanner.FloatWireBits(-1, TeFlags.CoordMpLowPrecision));
        Assert.Equal(17, TePropPlanner.FloatWireBits(-1, TeFlags.CoordMpIntegral));
        Assert.Equal(12, TePropPlanner.FloatWireBits(-1, TeFlags.Normal));
        Assert.Equal(8, TePropPlanner.FloatWireBits(8, TeFlags.RoundDown));
        Assert.Equal(10, TePropPlanner.FloatWireBits(10, TeFlags.RoundUp));
        Assert.Equal(16, TePropPlanner.FloatWireBits(16, TeFlags.None));
        Assert.Equal(32, TePropPlanner.FloatWireBits(0, TeFlags.None));
        Assert.Equal(32, TePropPlanner.FloatWireBits(33, TeFlags.None));
    }

    [Fact]
    public void ANormalVectorIsTwoFloatsAndASignBit()
    {
        Assert.Equal(66, TePropPlanner.VectorWireBits(-1, TeFlags.Coord));
        Assert.Equal(25, TePropPlanner.VectorWireBits(-1, TeFlags.Normal));
        Assert.Equal(96, TePropPlanner.VectorWireBits(-1, TeFlags.NoScale));
    }

    [Fact]
    public void SparksFieldsCostWhatPartSixSays()
    {
        TeField origin = F("m_vecOrigin", TePropKind.Float, 0, 4, -1, CoordElem, elems: 3, stride: 4);
        TeField magnitude = F("m_nMagnitude", TePropKind.Int, 12, 4, 4, TeFlags.Unsigned);
        TeField dir = F("m_vecDir", TePropKind.Vector, 20, 12, -1, TeFlags.Coord);

        Assert.Equal(22, TePropPlanner.PropValueBits(origin));
        Assert.Equal(66, TePropPlanner.FieldValueBits(origin));
        Assert.Equal(4, TePropPlanner.FieldValueBits(magnitude));
        Assert.Equal(66, TePropPlanner.FieldValueBits(dir));
    }

    [Fact]
    public void AnIntWiderThanAWordOrWithNoWidthCannotBePriced()
    {
        Assert.Equal(-1, TePropPlanner.PropValueBits(F("m_n", TePropKind.Int, 0, 4, 0, TeFlags.None)));
        Assert.Equal(-1, TePropPlanner.PropValueBits(F("m_n", TePropKind.Int, 0, 4, 33, TeFlags.None)));
        Assert.Equal(-1, TePropPlanner.FieldValueBits(F("m_n", TePropKind.Int, 0, 4, 33, TeFlags.None)));
        Assert.Equal(32, TePropPlanner.PropValueBits(F("m_n", TePropKind.Int, 0, 4, 32, TeFlags.None)));
        Assert.Equal(0, TePropPlanner.PropValueBits(F("m_t", TePropKind.DataTable, 0, 0, 0, TeFlags.None)));
        Assert.Equal(-1, TePropPlanner.PropValueBits(null));
    }

    [Fact]
    public void TheSplineArrayCostsEveryElementPlusItsLength()
    {
        TeField points = F("m_vecPoints", TePropKind.Array, 4, 0, -1, TeFlags.Coord, elems: 16, stride: 12);

        // 16 COORD vectors of 66 bits, and BitsFor(16) = 5 for the length.
        Assert.Equal(16 * 66 + 5, TePropPlanner.PropValueBits(points));
        Assert.Equal(1, TePropPlanner.FieldPropCount(points));
        Assert.Equal(16 * 12, TePropPlanner.FieldBlobBytes(points));
        Assert.Equal(-1, TePropPlanner.PropValueBits(points with { Elems = 0 }));
    }

    [Fact]
    public void StorageSizeMustBeOneTheProxiesKnow()
    {
        TeField f = F("m_nCount", TePropKind.Int, 0, 1, 8, TeFlags.Unsigned);

        Assert.Equal(1, TePropPlanner.FieldBlobBytes(f));
        Assert.Equal(2, TePropPlanner.FieldBlobBytes(f with { SizeBytes = 2 }));
        Assert.Equal(4, TePropPlanner.FieldBlobBytes(f with { SizeBytes = 4 }));
        Assert.Equal(-1, TePropPlanner.FieldBlobBytes(f with { SizeBytes = 3 }));
        Assert.Equal(-1, TePropPlanner.FieldBlobBytes(f with { SizeBytes = 8 }));
    }

    [Fact]
    public void OneFieldCanBeThreePropsAndCostsThreePropsWorth()
    {
        TeField origin = F("m_vecOrigin", TePropKind.Float, 0, 4, -1, CoordElem, elems: 3, stride: 4);

        Assert.Equal(3, TePropPlanner.FieldPropCount(origin));
        Assert.Equal(22, TePropPlanner.PropValueBits(origin));
        Assert.Equal(66, TePropPlanner.FieldValueBits(origin));
        Assert.Equal(12, TePropPlanner.FieldBlobBytes(origin));

        TeField colour = F("m_uchFrontColor", TePropKind.Int, 0, 1, 8, TeFlags.Unsigned, elems: 3, stride: 1);

        Assert.Equal(3, TePropPlanner.FieldPropCount(colour));
        Assert.Equal(24, TePropPlanner.FieldValueBits(colour));
        Assert.Equal(3, TePropPlanner.FieldBlobBytes(colour));
        Assert.Equal(-1, TePropPlanner.FieldBlobBytes(colour with { ElemStride = 0 }));

        TeField single = F("m_nMagnitude", TePropKind.Int, 0, 4, 4, TeFlags.Unsigned);

        Assert.Equal(1, TePropPlanner.FieldPropCount(single));
        Assert.Equal(4, TePropPlanner.FieldBlobBytes(single));
    }

    [Fact]
    public void AVectorIsTwelveBytesAndAFloatIsFour()
    {
        TeField v = F("m_vecDir", TePropKind.Vector, 0, 12, -1, TeFlags.Coord);

        Assert.Equal(12, TePropPlanner.FieldBlobBytes(v));
        Assert.Equal(-1, TePropPlanner.FieldBlobBytes(v with { SizeBytes = 4 }));

        TeField f = F("m_fScale", TePropKind.Float, 0, 4, 8, TeFlags.RoundDown);

        Assert.Equal(4, TePropPlanner.FieldBlobBytes(f));
        Assert.Equal(-1, TePropPlanner.FieldBlobBytes(f with { SizeBytes = 12 }));
    }

    [Fact]
    public void ExpandsTheWayStocksOwnTablesSpellIt()
    {
        Assert.Equal("m_vecOrigin[0]", TePropPlanner.PropName("m_vecOrigin", 0, true));
        Assert.Equal("m_vecOrigin[2]", TePropPlanner.PropName("m_vecOrigin", 2, true));
        Assert.Equal("m_nMagnitude", TePropPlanner.PropName("m_nMagnitude", 0, false));
        Assert.Equal("m_nMagnitude", TePropPlanner.PropName("m_nMagnitude", 7, false));
    }

    [Fact]
    public void CountsPastNineTheRightWayRound()
    {
        Assert.Equal("m_vecPoints[10]", TePropPlanner.PropName("m_vecPoints", 10, true));
        Assert.Equal("m_vecPoints[15]", TePropPlanner.PropName("m_vecPoints", 15, true));
        Assert.Equal("x[12]", TePropPlanner.PropName("x", 12, true));
        Assert.Equal("x[0]", TePropPlanner.PropName("x", -3, true));
    }

    [Fact]
    public void TruncatesToWhatTheNameBufferHolds()
    {
        string longName = new('a', 200);

        Assert.Equal(TePropPlanner.PropNameMax - 1, TePropPlanner.PropName(longName, 1, true).Length);
        Assert.Equal(new string('a', 127), TePropPlanner.PropName(longName, 0, false));

        string nearly = new('b', 124);

        // 124 + "[12]" is 128: the closing bracket is the byte that does not fit.
        Assert.Equal(nearly + "[12", TePropPlanner.PropName(nearly, 12, true));
        Assert.Equal(string.Empty, TePropPlanner.PropName(null, 0, true));
    }

    [Fact]
    public void TheBaseclassPropIsTheOnlyOneThatMayLiveAtOffsetZero()
    {
        TeField nested = F("m_DataEntry", TePropKind.DataTable, 4, 0, 0, TeFlags.None, subTable: "DT_EffectData");
        var plan = TePropPlanner.Plan(C("CTEEffectDispatch", "DT_TEEffectDispatch", "DT_BaseTempEntity", 8, nested))!;

        Assert.Equal(2, plan.Count);
        Assert.Equal("baseclass", plan[0].Name);
        Assert.Equal(TePropKind.DataTable, plan[0].Kind);
        Assert.Equal(0, plan[0].Offset);
        Assert.Equal(-1, plan[0].Field);
        Assert.Equal("DT_BaseTempEntity", plan[0].SubTable);
        Assert.Equal("m_DataEntry", plan[1].Name);
        Assert.Equal(TePropKind.DataTable, plan[1].Kind);
        Assert.Equal(4, plan[1].Offset);
        Assert.Equal(4, plan[1].DeclareOffset);
        Assert.Equal("DT_EffectData", plan[1].SubTable);
        Assert.Equal(0, plan[1].Field);

        var bad = TePropPlanner.Plan(C("CTEBad", "DT_TEBad", null, 8, nested with { Offset = 0 }))!;

        Assert.Single(bad);
        Assert.Equal(0, bad[0].Offset);
        Assert.Equal(0, bad[0].Field);
    }

    [Fact]
    public void CountsTheBaseAndTheExpansionsAndTheArrayPair()
    {
        TeField origin = F("m_vecOrigin", TePropKind.Float, 0, 4, -1, CoordElem, elems: 3, stride: 4);

        Assert.Equal(4, TePropPlanner.PlanCount(C("CTEParticleSystem", "DT_TEParticleSystem", "DT_BaseTempEntity", 12, origin)));
        Assert.Equal(0, TePropPlanner.PlanCount(C("CBaseTempEntity", "DT_BaseTempEntity", null, 0)));

        TeField[] spline =
        [
            F("m_nPoints", TePropKind.Int, 0, 4, 5, TeFlags.Unsigned),
            F("m_vecPoints", TePropKind.Array, 4, 0, -1, TeFlags.Coord, elems: 16, stride: 12),
        ];

        Assert.Equal(3, TePropPlanner.PlanCount(C("CTEBeamSpline", "DT_TEBeamSpline", null, 4 + 16 * 12, spline)));
        Assert.Equal(-1, TePropPlanner.PlanCount(null));
    }

    [Fact]
    public void AVectorElemFieldIsThreePropsWithNegatedSendOffsets()
    {
        TeField origin = F("m_vecOrigin", TePropKind.Float, 0, 4, -1, CoordElem, elems: 3, stride: 4);
        var plan = TePropPlanner.Plan(C("CTEParticleSystem", "DT_TEParticleSystem", "DT_BaseTempEntity", 12, origin))!;

        Assert.Equal(4, plan.Count);
        Assert.Equal("baseclass", plan[0].Name);
        Assert.Equal(0, plan[0].DeclareOffset);
        Assert.Equal("m_vecOrigin[0]", plan[1].Name);
        Assert.Equal("m_vecOrigin[1]", plan[2].Name);
        Assert.Equal("m_vecOrigin[2]", plan[3].Name);

        for (int i = 1; i <= 3; i++)
        {
            Assert.Equal(TePropKind.Float, plan[i].Kind);
            Assert.Equal(4 * (i - 1), plan[i].Offset);
            Assert.Equal(-4 * (i - 1), plan[i].DeclareOffset);
            Assert.Equal(4, plan[i].SizeBytes);
            Assert.Equal(-1, plan[i].Bits);
            Assert.Equal(0, plan[i].Field);
            Assert.Equal(i - 1, plan[i].Elem);
            Assert.Equal(TeFlags.Coord, plan[i].Flags);
        }
    }

    [Fact]
    public void SparksPlansTheSixPropsTheJoinMeasured()
    {
        var plan = TePropPlanner.Plan(C("CTESparks", "DT_TESparks", "DT_TEParticleSystem", 32,
            F("m_nMagnitude", TePropKind.Int, 12, 4, 4, TeFlags.Unsigned),
            F("m_nTrailLength", TePropKind.Int, 16, 4, 4, TeFlags.Unsigned),
            F("m_vecDir", TePropKind.Vector, 20, 12, -1, TeFlags.Coord)))!;

        Assert.Equal(4, plan.Count);
        Assert.Equal("DT_TEParticleSystem", plan[0].SubTable);
        Assert.Equal(("m_nMagnitude", TePropKind.Int, 12, 12, 4, 4, TeFlags.Unsigned),
                     (plan[1].Name, plan[1].Kind, plan[1].Offset, plan[1].DeclareOffset, plan[1].Bits, plan[1].SizeBytes, plan[1].Flags));
        Assert.Equal(("m_nTrailLength", 16, 4), (plan[2].Name, plan[2].Offset, plan[2].Bits));
        Assert.Equal(("m_vecDir", TePropKind.Vector, 20, 20, TeFlags.Coord, -1),
                     (plan[3].Name, plan[3].Kind, plan[3].Offset, plan[3].DeclareOffset, plan[3].Flags, plan[3].Bits));
    }

    [Fact]
    public void AnArrayIsATemplateThenTheArrayPropOfTheSameName()
    {
        var plan = TePropPlanner.Plan(C("CTEBeamSpline", "DT_TEBeamSpline", null, 4 + 16 * 12,
            F("m_nPoints", TePropKind.Int, 0, 4, 5, TeFlags.Unsigned),
            F("m_vecPoints", TePropKind.Array, 4, 12, -1, TeFlags.Coord, elems: 16, stride: 12)))!;

        Assert.Equal(3, plan.Count);
        Assert.Equal("m_nPoints", plan[0].Name);
        Assert.Equal(("m_vecPoints", TePropKind.Vector, 4, TeFlags.Coord, true, 12),
                     (plan[1].Name, plan[1].Kind, plan[1].Offset, plan[1].Flags, plan[1].ArrayElement, plan[1].SizeBytes));
        Assert.Equal(("m_vecPoints", TePropKind.Array, 16, 12, 4, false),
                     (plan[2].Name, plan[2].Kind, plan[2].Elems, plan[2].ElemStride, plan[2].Offset, plan[2].ArrayElement));
        Assert.False(plan[0].ArrayElement);
    }

    [Fact]
    public void TheArrayTemplateIsNamedForItsFirstElementOnTheRecvSideOnly()
    {
        var plan = TePropPlanner.Plan(C("CTEBeamSpline", "DT_TEBeamSpline", null, 4 + 16 * 12,
            F("m_nPoints", TePropKind.Int, 0, 4, 5, TeFlags.Unsigned),
            F("m_vecPoints", TePropKind.Array, 4, 12, -1, TeFlags.Coord, elems: 16, stride: 12)))!;

        Assert.Equal("m_nPoints", TePropPlanner.RecvPropName(plan[0]));
        Assert.Equal("m_vecPoints[0]", TePropPlanner.RecvPropName(plan[1]));
        Assert.Equal("m_vecPoints", TePropPlanner.RecvPropName(plan[2]));
    }

    [Fact]
    public void SymbolicWidthsAreResolvedBeforeEitherSideSeesThem()
    {
        var plan = TePropPlanner.Plan(C("CTEFizz", "DT_TEFizz", null, 4,
            F("m_nEntity", TePropKind.Int, 0, 4, 0, TeFlags.Unsigned, TeBits.MaxEdict)))!;

        Assert.Single(plan);
        Assert.Equal(11, plan[0].Bits);
    }

    [Fact]
    public void AnUnknownWidthSymbolIsRefusedRatherThanGuessed()
        => Assert.Null(TePropPlanner.Plan(C("CTEBad", "DT_TEBad", null, 4,
            F("m_nWhat", TePropKind.Int, 0, 4, 8, TeFlags.None, (TeBits)9))));

    [Fact]
    public void AFieldWithNoNameIsRefused()
        => Assert.Null(TePropPlanner.Plan(C("CTEBad", "DT_TEBad", null, 4,
            F("", TePropKind.Int, 0, 4, 8, TeFlags.None))));

    [Fact]
    public void ANestedTableWithNoNameIsRefused()
        => Assert.Null(TePropPlanner.Plan(C("CTEBad", "DT_TEBad", null, 4,
            F("m_Data", TePropKind.DataTable, 0, 0, 0, TeFlags.None))));

    [Fact]
    public void AnArrayWithNoElementsOrStrideIsRefused()
    {
        TeField points = F("m_vecPoints", TePropKind.Array, 0, 12, -1, TeFlags.Coord, elems: 16, stride: 12);

        Assert.NotNull(TePropPlanner.Plan(C("CTEBeamSpline", "DT_TEBeamSpline", null, 16 * 12, points)));
        Assert.Null(TePropPlanner.Plan(C("CTEBeamSpline", "DT_TEBeamSpline", null, 16 * 12, points with { Elems = 0 })));
        Assert.Null(TePropPlanner.Plan(C("CTEBeamSpline", "DT_TEBeamSpline", null, 16 * 12, points with { ElemStride = 0 })));
    }

    [Fact]
    public void EveryReceiveOffsetIsPositiveAndEverySendOffsetIsItOrItsNegation()
    {
        var plan = TePropPlanner.Plan(C("CTEEverything", "DT_TEEverything", "DT_BaseTempEntity", 20 + 4 * 12,
            F("m_vecOrigin", TePropKind.Float, 0, 4, -1, CoordElem, elems: 3, stride: 4),
            F("m_clrColour", TePropKind.Int, 12, 1, 8, TeFlags.Unsigned, elems: 3, stride: 1),
            F("m_angRotation", TePropKind.Angle, 16, 4, 8, TeFlags.None),
            F("m_vecPoints", TePropKind.Array, 20, 12, -1, TeFlags.Coord, elems: 4, stride: 12)))!;

        Assert.Equal(10, plan.Count);

        foreach (TePropPlan prop in plan)
        {
            Assert.True(prop.Offset >= 0, prop.Name);
            Assert.True(prop.DeclareOffset == prop.Offset || prop.DeclareOffset == -prop.Offset, prop.Name);
            Assert.NotEqual(string.Empty, prop.Name);
        }

        Assert.Equal(("m_clrColour[0]", 12, 12), (plan[4].Name, plan[4].Offset, plan[4].DeclareOffset));
        Assert.Equal(("m_clrColour[2]", 14, 14), (plan[6].Name, plan[6].Offset, plan[6].DeclareOffset));
    }

    [Fact]
    public void TheCountIsExactlyWhatThePlanWrites()
    {
        TeField[] fields =
        [
            F("m_vecOrigin", TePropKind.Float, 0, 4, -1, CoordElem, elems: 3, stride: 4),
            F("m_nMagnitude", TePropKind.Int, 12, 4, 4, TeFlags.Unsigned),
            F("m_vecPoints", TePropKind.Array, 16, 12, -1, TeFlags.Coord, elems: 8, stride: 12),
        ];
        string?[] bases = [null, "DT_BaseTempEntity", "DT_TEParticleSystem"];
        int checkedCount = 0;

        foreach (string? b in bases)
        {
            for (int n = 0; n <= fields.Length; n++)
            {
                TeClass declaration = C("CTEX", "DT_TEX", b, 16 + 8 * 12, fields[..n]);

                Assert.Equal(TePropPlanner.PlanCount(declaration), TePropPlanner.Plan(declaration)!.Count);
                checkedCount++;
            }
        }

        Assert.Equal(12, checkedCount);
    }
}
