using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="SendPropPlan"/> against dt_send.cpp's SendProp constructors. The
/// multipliers are stock's AssignRangeMultiplier run natively (clang 22, -O0 and
/// -O2 agreeing, the out-of-range width at run time as BuildOneProp passes it),
/// written here as the float's bits so a rounding difference is a failure.
/// </summary>
public class SendPropPlanTests
{
    private static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

    [Theory]
    [InlineData(8, 360f, 0x3f355555u)]
    [InlineData(13, 360f, 0x41b605b0u)]
    [InlineData(10, 1f, 0x447fc000u)]
    [InlineData(16, 1024f, 0x427fff00u)]
    [InlineData(24, 4096f, 0x457fffffu)]
    [InlineData(20, 16384f, 0x427ffff0u)]
    [InlineData(2, 1f, 0x40400000u)]
    [InlineData(5, 31f, 0x3f800000u)]
    [InlineData(10, 256f, 0x407fc000u)]
    [InlineData(11, 2048f, 0x3f7fe000u)]
    [InlineData(7, 100f, 0x3fa28f5cu)]
    [InlineData(8, 256f, 0x3f7f0000u)]
    [InlineData(8, 0.99609375f, 0x43800000u)]
    [InlineData(12, 2f, 0x44fff000u)]
    public void TheMultiplierIsStocksForAnOrdinaryRange(int bits, float range, uint expected)
        => Assert.Equal(expected, Bits(SendPropPlan.AssignRangeMultiplier(bits, range)));

    [Fact]
    public void AMultiplierThatWouldOverflowBacksOff()
    {
        // 0xFFFFFFFE / 360 rounds up as a float and overflows the 32 bits, so
        // stock backs off by 0.9999; the 31-bit case is the same at range 1.
        Assert.Equal(0x4b3606b8u, Bits(SendPropPlan.AssignRangeMultiplier(32, 360f)));
        Assert.Equal(0x4efff972u, Bits(SendPropPlan.AssignRangeMultiplier(31, 1f)));
        Assert.Equal(0x4efff972u, Bits(SendPropPlan.AssignRangeMultiplier(32, 2f)));
    }

    [Fact]
    public void ARangeOfNothingTakesTheWholeWidth()
    {
        Assert.Equal(0x4f800000u, Bits(SendPropPlan.AssignRangeMultiplier(32, 0f)));
        Assert.Equal(0x3f800000u, Bits(SendPropPlan.AssignRangeMultiplier(1, 0f)));
        // CloseEnough's epsilon, not an exact zero.
        Assert.Equal(0x457ff000u, Bits(SendPropPlan.AssignRangeMultiplier(12, 0.0005f)));
        Assert.Equal(0u, Bits(SendPropPlan.AssignRangeMultiplier(0, 5f)));
    }

    [Fact]
    public void AnOutOfRangeWidthShiftsTheWayX86Does()
    {
        // Stock's SendPropFloat(..., -1, SPROP_COORD) reaches AssignRangeMultiplier
        // with -1: (1 << -1) is 1 << 31 on x86-64, and INT_MIN - 1 wraps to INT_MAX.
        Assert.Equal(0x4f000000u, Bits(SendPropPlan.AssignRangeMultiplier(-1, 0f)));
        Assert.Equal(0x4ab606b8u, Bits(SendPropPlan.AssignRangeMultiplier(-1, 360f)));
    }

    [Fact]
    public void AFloatWithNoWidthIsNoScaleAndLosesItsRange()
    {
        SendPropRow coord = SendPropPlan.Float("m_vecOrigin[0]", -4, -1, SendPropPlan.Coord, 5f, 9f);

        Assert.Equal(SendPropType.Float, coord.Type);
        Assert.Equal(-4, coord.Offset);
        Assert.Equal(SendPropPlan.Coord | SendPropPlan.NoScale, coord.Flags);
        Assert.Equal((0f, 0f), (coord.Low, coord.High));
        Assert.Equal(0, coord.Bits);
        Assert.Equal(0x4f000000u, Bits(coord.HighLowMul));
        Assert.Equal(SendProxyId.FloatToFloat, coord.Proxy);
        Assert.Equal((1, -1, -1), (coord.Elements, coord.ElementStride, coord.DataTable));

        SendPropRow whole = SendPropPlan.Float("m_flWhole", 8, 32, 0, 1f, 2f);

        Assert.Equal(SendPropPlan.NoScale, whole.Flags);
        Assert.Equal(0, whole.Bits);
        Assert.Equal(0x4f800000u, Bits(whole.HighLowMul));
    }

    [Fact]
    public void AFloatWithAWidthKeepsItAndDefaultsItsHighValue()
    {
        SendPropRow scaled = SendPropPlan.Float("m_flScale", 12, 8, 0, 0f, SendPropPlan.HighDefault);

        Assert.Equal(0, scaled.Flags);
        Assert.Equal(8, scaled.Bits);
        Assert.Equal((0f, 256f), (scaled.Low, scaled.High));
        Assert.Equal(0x3f7f0000u, Bits(scaled.HighLowMul));
    }

    [Fact]
    public void RoundDownTrimsTheHighValueAndRoundUpTheLow()
    {
        SendPropRow down = SendPropPlan.Float("m_flDown", 0, 8, SendPropPlan.RoundDown, 0f, 1f);

        Assert.Equal((0f, 0.99609375f), (down.Low, down.High));
        Assert.Equal(0x43800000u, Bits(down.HighLowMul));

        SendPropRow up = SendPropPlan.Float("m_flUp", 0, 8, SendPropPlan.RoundUp, 0f, 1f);

        Assert.Equal((0.00390625f, 1f), (up.Low, up.High));
        Assert.Equal(0x43800000u, Bits(up.HighLowMul));
    }

    [Fact]
    public void AVectorIsNoScaleOnlyAtThirtyTwoBitsAndAWidthlessOneSendsNoWidth()
    {
        SendPropRow coord = SendPropPlan.Vector("m_vecDir", 20, -1, SendPropPlan.Coord, 0f, 0f);

        Assert.Equal(SendPropType.Vector, coord.Type);
        Assert.Equal(SendPropPlan.Coord, coord.Flags);
        Assert.Equal(0, coord.Bits);
        Assert.Equal(0x4f000000u, Bits(coord.HighLowMul));
        Assert.Equal(SendProxyId.VectorToVector, coord.Proxy);

        SendPropRow whole = SendPropPlan.Vector("m_vecWhole", 0, 32, 0, 0f, 2f);

        Assert.Equal(SendPropPlan.NoScale, whole.Flags);
        Assert.Equal(0, whole.Bits);
        Assert.Equal(0x4efff972u, Bits(whole.HighLowMul));

        SendPropRow normal = SendPropPlan.Vector("m_vecNormal", 0, 12, SendPropPlan.Normal, -1f, 1f);

        Assert.Equal(0, normal.Bits);
        Assert.Equal(0x44fff000u, Bits(normal.HighLowMul));

        SendPropRow ranged = SendPropPlan.Vector("m_vecRanged", 0, 12, 0, -1f, 1f);

        Assert.Equal(12, ranged.Bits);
        // No HIGH_DEFAULT rule for a vector: the range is what it was given.
        Assert.Equal((-1f, 1f), (ranged.Low, ranged.High));
    }

    [Fact]
    public void AnAngleIsAZeroTo360FloatThatKeepsItsWidth()
    {
        SendPropRow angle = SendPropPlan.Angle("m_flYaw", 4, 13, 0);

        Assert.Equal(SendPropType.Float, angle.Type);
        Assert.Equal((0f, 360f), (angle.Low, angle.High));
        Assert.Equal(13, angle.Bits);
        Assert.Equal(0x41b605b0u, Bits(angle.HighLowMul));
        Assert.Equal(SendProxyId.AngleToFloat, angle.Proxy);

        SendPropRow whole = SendPropPlan.Angle("m_flYaw", 4, 32, 0);

        Assert.Equal(SendPropPlan.NoScale, whole.Flags);
        Assert.Equal(32, whole.Bits);
        Assert.Equal(0x4b3606b8u, Bits(whole.HighLowMul));
    }

    [Fact]
    public void QAnglesAreAnglesOnAVector()
    {
        SendPropRow angles = SendPropPlan.QAngles("m_angRotation", 12, -1, SendPropPlan.Coord);

        Assert.Equal(SendPropType.Vector, angles.Type);
        Assert.Equal(SendPropPlan.Coord, angles.Flags);
        Assert.Equal(-1, angles.Bits);
        Assert.Equal((0f, 360f), (angles.Low, angles.High));
        Assert.Equal(0x4ab606b8u, Bits(angles.HighLowMul));
        Assert.Equal(SendProxyId.QAngles, angles.Proxy);
        Assert.Equal(SendPropPlan.NoScale, SendPropPlan.QAngles("m_ang", 0, 32, 0).Flags);
    }

    [Theory]
    [InlineData(1, 0, 0, SendProxyId.Int8ToInt32, 8)]
    [InlineData(2, 0, 0, SendProxyId.Int16ToInt32, 16)]
    [InlineData(4, 0, 0, SendProxyId.Int32ToInt32, 32)]
    [InlineData(1, 5, SendPropPlan.Unsigned, SendProxyId.UInt8ToInt32, 5)]
    [InlineData(2, 12, SendPropPlan.Unsigned, SendProxyId.UInt16ToInt32, 12)]
    [InlineData(4, -1, SendPropPlan.Unsigned, SendProxyId.UInt32ToInt32, 32)]
    [InlineData(3, 0, 0, SendProxyId.Int8ToInt32, 24)]
    [InlineData(3, 0, SendPropPlan.Unsigned, SendProxyId.UInt8ToInt32, 24)]
    public void AnIntsProxyIsItsStorageAndSignedness(int size, int bits, int flags, SendProxyId proxy, int stored)
    {
        SendPropRow row = SendPropPlan.Int("m_n", 16, size, bits, flags);

        Assert.Equal((SendPropType.Int, 16, flags, stored, proxy), (row.Type, row.Offset, row.Flags, row.Bits, row.Proxy));
        Assert.Equal((0f, 0f, 1, -1, -1), (row.Low, row.High, row.Elements, row.ElementStride, row.DataTable));
    }

    [Fact]
    public void ANestedTableAtZeroIsCollapsibleInsteadOfAlwaysYes()
    {
        SendPropRow based = SendPropPlan.DataTable("baseclass", 0, 3);

        Assert.Equal((SendPropType.DataTable, 0, SendPropPlan.Collapsible, 3, SendProxyId.DataTableToDataTable),
                     (based.Type, based.Offset, based.Flags, based.DataTable, based.Proxy));

        SendPropRow nested = SendPropPlan.DataTable("m_EffectData", 4, 7);

        Assert.Equal((SendPropPlan.ProxyAlwaysYes, 7), (nested.Flags, nested.DataTable));
    }

    [Fact]
    public void AnArrayIsItsCountAndStrideBehindTheEmptyProxy()
    {
        SendPropRow array = SendPropPlan.Array("m_vecPoints", 16, 12);

        Assert.Equal((SendPropType.Array, 0, 0, 0, 16, 12, SendProxyId.Empty, -1),
                     (array.Type, array.Offset, array.Flags, array.Bits, array.Elements, array.ElementStride, array.Proxy, array.DataTable));
    }

    [Fact]
    public void APlannedPropIsBuiltTheWayBuildOnePropBuiltIt()
    {
        var plan = TePropPlanner.Plan(TePropPlannerTests.C("CTESparks", "DT_TESparks", "DT_TEParticleSystem", 32,
            TePropPlannerTests.F("m_vecOrigin", TePropKind.Float, 0, 4, -1, TeFlags.Coord | TeFlags.VectorElem, elems: 3, stride: 4),
            TePropPlannerTests.F("m_nMagnitude", TePropKind.Int, 12, 4, 4, TeFlags.Unsigned),
            TePropPlannerTests.F("m_angRot", TePropKind.QAngles, 16, 12, 13, TeFlags.None),
            TePropPlannerTests.F("m_vecPoints", TePropKind.Array, 28, 12, -1, TeFlags.Coord, elems: 2, stride: 12)))!;

        SendPropRow?[] rows = plan.Select((p, i) => SendPropPlan.FromPlan(p, i == 0 ? 5 : -1)).ToArray();

        Assert.Equal(8, rows.Length);
        Assert.Equal((SendPropType.DataTable, 5, SendPropPlan.Collapsible), (rows[0]!.Value.Type, rows[0]!.Value.DataTable, rows[0]!.Value.Flags));
        Assert.Equal(("m_vecOrigin[1]", SendPropType.Float, -4, SendPropPlan.Coord | SendPropPlan.NoScale),
                     (rows[2]!.Value.Name, rows[2]!.Value.Type, rows[2]!.Value.Offset, rows[2]!.Value.Flags));
        Assert.Equal(("m_nMagnitude", 12, 4, SendProxyId.UInt32ToInt32),
                     (rows[4]!.Value.Name, rows[4]!.Value.Offset, rows[4]!.Value.Bits, rows[4]!.Value.Proxy));
        Assert.Equal((SendPropType.Vector, 13, SendProxyId.QAngles), (rows[5]!.Value.Type, rows[5]!.Value.Bits, rows[5]!.Value.Proxy));
        Assert.Equal((SendPropType.Vector, 28, SendPropPlan.Coord), (rows[6]!.Value.Type, rows[6]!.Value.Offset, rows[6]!.Value.Flags));
        Assert.Equal((SendPropType.Array, 2, 12), (rows[7]!.Value.Type, rows[7]!.Value.Elements, rows[7]!.Value.ElementStride));
        Assert.Null(SendPropPlan.FromPlan(plan[0] with { Kind = (TePropKind)42 }));
    }
}
