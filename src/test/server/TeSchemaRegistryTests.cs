using SourceSharp.Abi;

using Xunit;

using static SourceSharp.Tests.TePropPlannerTests;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TeSchemaRegistry"/> against tempent_schema.h's
/// <c>CSourceSharpTeRegistry</c>, case for case with tempent_schema_test.cpp's
/// TempEntRegistry and TempEntCeiling gtests. The registry's refusals are the
/// ones no running game shows: a duplicate class name makes a client stop
/// joining with nothing in any log, and a worst case over the event cap is a
/// Host_Error mid-game.
/// </summary>
public class TeSchemaRegistryTests
{
    private static TeField Magnitude(int offset = 0) => F("m_nMagnitude", TePropKind.Int, offset, 4, 4, TeFlags.Unsigned);

    private static TeClass ParticleSystem()
        => C("CTEParticleSystem", "DT_TEParticleSystem", null, 12,
             F("m_vecOrigin", TePropKind.Float, 0, 4, -1, TeFlags.Coord, elems: 3, stride: 4)) with { RegistersClass = false };

    [Fact]
    public void TakesAWellFormedClass()
    {
        var registry = new TeSchemaRegistry();
        TeClass sparks = C("CTESparks", "DT_TESparks", null, 20,
            Magnitude(0),
            F("m_nTrailLength", TePropKind.Int, 4, 4, 4, TeFlags.Unsigned),
            F("m_vecDir", TePropKind.Vector, 8, 12, -1, TeFlags.Coord));

        Assert.Equal(0, registry.Add(sparks, out TeRefusal why));
        Assert.Equal(TeRefusal.Accepted, why);
        Assert.Equal(1, registry.Count);
        Assert.Equal(0, registry.FindByClassName("CTESparks"));
        Assert.Equal(0, registry.FindByTableName("DT_TESparks"));
        Assert.Equal(-1, registry.FindByClassName("CTEBubbles"));
        Assert.Same(sparks, registry.At(0));
    }

    [Fact]
    public void RefusesASecondClassOfTheSameName()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(0, registry.Add(C("CTESparks", "DT_TESparks", null, 4, Magnitude()), out TeRefusal why));
        Assert.Equal(TeRefusal.Accepted, why);
        Assert.Equal(-1, registry.Add(C("CTESparks", "DT_TESparks2", null, 4, Magnitude()), out why));
        Assert.Equal(TeRefusal.DuplicateClass, why);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void RefusesASecondTableOfTheSameName()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(0, registry.Add(C("CTESparks", "DT_TESparks", null, 4, Magnitude()), out _));
        Assert.Equal(-1, registry.Add(C("CTESparks2", "DT_TESparks", null, 4, Magnitude()), out TeRefusal why));
        Assert.Equal(TeRefusal.DuplicateTable, why);
    }

    [Fact]
    public void RefusesTwoPropsOfOneNameInOneTable()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(-1, registry.Add(C("CTEThing", "DT_TEThing", null, 8,
            F("m_nCount", TePropKind.Int, 0, 4, 8, TeFlags.Unsigned),
            F("m_nCount", TePropKind.Int, 4, 4, 8, TeFlags.Unsigned)), out TeRefusal why));
        Assert.Equal(TeRefusal.DuplicateField, why);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RefusesAnythingAfterTheWindowShuts()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(0, registry.Add(C("CTESparks", "DT_TESparks", null, 4, Magnitude()), out _));
        Assert.False(registry.IsClosed);

        registry.Close();

        Assert.True(registry.IsClosed);
        Assert.Equal(-1, registry.Add(C("CTESmoke", "DT_TESmoke", null, 4, Magnitude()), out TeRefusal why));
        Assert.Equal(TeRefusal.Closed, why);
        Assert.Equal(1, registry.Count);
        Assert.Equal(-1, registry.FindByClassName("CTESmoke"));
    }

    [Fact]
    public void RefusesAFieldPastTheEndOfTheBlob()
    {
        var registry = new TeSchemaRegistry();
        TeField[] fields = [Magnitude(0), F("m_vecDir", TePropKind.Vector, 4, 12, -1, TeFlags.Coord)];

        Assert.Equal(-1, registry.Add(C("CTESparks", "DT_TESparks", null, 12, fields), out TeRefusal why));
        Assert.Equal(TeRefusal.Offset, why);
        Assert.Equal(0, registry.Add(C("CTESparks", "DT_TESparks", null, 16, fields), out why));
        Assert.Equal(TeRefusal.Accepted, why);
        Assert.Equal(-1, registry.Add(C("CTEBad", "DT_TEBad", null, 16,
            F("m_fBad", TePropKind.Float, -4, 4, 8, TeFlags.None)), out why));
        Assert.Equal(TeRefusal.Offset, why);
    }

    [Fact]
    public void RefusesAFieldWhoseShapeCannotBeBuilt()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(-1, registry.Add(C("CTEA", "DT_TEA", null, 8, F("m_n", TePropKind.Int, 0, 3, 8, TeFlags.None)), out TeRefusal why));
        Assert.Equal(TeRefusal.FieldShape, why);
        Assert.Equal(-1, registry.Add(C("CTEB", "DT_TEB", null, 8, F("m_n", TePropKind.Int, 0, 4, 0, TeFlags.None)), out why));
        Assert.Equal(TeRefusal.FieldShape, why);
        Assert.Equal(-1, registry.Add(C("CTEC", "DT_TEC", null, 8, F("", TePropKind.Int, 0, 4, 8, TeFlags.None)), out why));
        Assert.Equal(TeRefusal.FieldShape, why);
        Assert.Equal(-1, registry.Add(C("CTED", "DT_TED", null, 8, F("m_t", TePropKind.DataTable, 4, 0, 0, TeFlags.None)), out why));
        Assert.Equal(TeRefusal.FieldShape, why);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RefusesNoNamesAndTooManyFields()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(-1, registry.Add(null, out TeRefusal why));
        Assert.Equal(TeRefusal.Null, why);
        Assert.Equal(-1, registry.Add(C("", "DT_TEA", null, 4, Magnitude()), out why));
        Assert.Equal(TeRefusal.Null, why);
        Assert.Equal(-1, registry.Add(C("CTEA", "", null, 4, Magnitude()), out why));
        Assert.Equal(TeRefusal.Null, why);

        TeField[] many = Enumerable.Range(0, TePropPlanner.MaxFields + 1)
            .Select(i => F("m_n" + i, TePropKind.Int, i * 4, 4, 8, TeFlags.None)).ToArray();

        Assert.Equal(-1, registry.Add(C("CTEMany", "DT_TEMany", null, many.Length * 4, many), out why));
        Assert.Equal(TeRefusal.FieldCount, why);
        Assert.Equal(0, registry.Add(C("CTEMany", "DT_TEMany", null, many.Length * 4, many[..TePropPlanner.MaxFields]), out why));
        Assert.Equal(TeRefusal.Accepted, why);
    }

    [Fact]
    public void RefusesTheClassPastTheLastSlot()
    {
        var registry = new TeSchemaRegistry();

        for (int i = 0; i < TePropPlanner.MaxClasses; i++)
            Assert.Equal(i, registry.Add(C("CTE" + i, "DT_TE" + i, null, 4, Magnitude()), out _));

        Assert.Equal(-1, registry.Add(C("CTELast", "DT_TELast", null, 4, Magnitude()), out TeRefusal why));
        Assert.Equal(TeRefusal.Full, why);
    }

    [Fact]
    public void RefusesABaseOrNestedTableNobodyHas()
    {
        TeClass derived = C("CTESparks", "DT_TESparks", "DT_TEParticleSystem", 16, Magnitude(12));

        var lonely = new TeSchemaRegistry();

        Assert.Equal(-1, lonely.Add(derived, out TeRefusal why));
        Assert.Equal(TeRefusal.Base, why);
        Assert.Equal(-1, lonely.Add(C("CTEEffectDispatch", "DT_TEEffectDispatch", null, 8,
            F("m_EffectData", TePropKind.DataTable, 4, 0, 0, TeFlags.None, subTable: "DT_EffectData")), out why));
        Assert.Equal(TeRefusal.Base, why);

        var registry = new TeSchemaRegistry();

        Assert.Equal(0, registry.Add(ParticleSystem(), out _));
        Assert.Equal(1, registry.Add(derived, out why));
        Assert.Equal(TeRefusal.Accepted, why);
        Assert.False(registry.At(0)!.RegistersClass);
        Assert.True(registry.At(1)!.RegistersClass);
    }

    [Fact]
    public void SparksIsPricedExactly()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(0, registry.Add(ParticleSystem(), out _));
        Assert.Equal(1, registry.Add(C("CTESparks", "DT_TESparks", "DT_TEParticleSystem", 32,
            Magnitude(12),
            F("m_nTrailLength", TePropKind.Int, 16, 4, 4, TeFlags.Unsigned),
            F("m_vecDir", TePropKind.Vector, 20, 12, -1, TeFlags.Coord)), out _));

        // 32 overhead + 3 x (7 + 22) origin + (7 + 4) x 2 + (7 + 66) dir.
        Assert.Equal(214, registry.WorstCaseBits(1));
        Assert.True(registry.WorstCaseBits(1) < TePropPlanner.MaxEventBits);
    }

    [Fact]
    public void ANestedTableIsPricedAsItsOwnPropsWithoutASecondOverhead()
    {
        var registry = new TeSchemaRegistry();

        Assert.Equal(0, registry.Add(C("CEffectData", "DT_EffectData", null, 8,
            F("m_nA", TePropKind.Int, 0, 4, 8, TeFlags.None),
            F("m_nB", TePropKind.Int, 4, 4, 8, TeFlags.None)) with { RegistersClass = false }, out _));
        Assert.Equal(1, registry.Add(C("CTEEffectDispatch", "DT_TEEffectDispatch", null, 12,
            F("m_EffectData", TePropKind.DataTable, 4, 0, 0, TeFlags.None, subTable: "DT_EffectData")), out _));

        Assert.Equal(32 + 2 * (7 + 8), registry.WorstCaseBits(0));
        Assert.Equal(32 + 2 * (7 + 8), registry.WorstCaseBits(1));
        Assert.Equal(-1, registry.WorstCaseBits(2));
    }

    [Fact]
    public void TheSplineFitsPackedAndWouldNotAsPlainFloats()
    {
        TeField[] fields =
        [
            F("m_nPoints", TePropKind.Int, 0, 4, 5, TeFlags.Unsigned),
            F("m_vecPoints", TePropKind.Array, 4, 0, -1, TeFlags.Coord, elems: 16, stride: 12),
        ];

        var packed = new TeSchemaRegistry();

        Assert.Equal(0, packed.Add(C("CTEBeamSpline", "DT_TEBeamSpline", null, 4 + 16 * 12, fields), out _));
        Assert.Equal(1112, packed.WorstCaseBits(0));

        var plain = new TeSchemaRegistry();
        fields[1] = fields[1] with { Flags = TeFlags.NoScale };

        Assert.Equal(-1, plain.Add(C("CTEBeamSpline", "DT_TEBeamSpline", null, 4 + 16 * 12, fields), out TeRefusal why));
        Assert.Equal(TeRefusal.TooBig, why);
        Assert.Equal(0, plain.Count);
        Assert.Equal(-1, plain.FindByTableName("DT_TEBeamSpline"));
    }

    [Fact]
    public void RefusalsHaveWordsOnThem()
    {
        Assert.Equal("accepted", TeSchemaRegistry.RefusalText(TeRefusal.Accepted));

        for (TeRefusal why = TeRefusal.Closed; why <= TeRefusal.TooBig; why++)
            Assert.NotEqual("refused", TeSchemaRegistry.RefusalText(why));

        Assert.Equal("refused", TeSchemaRegistry.RefusalText((TeRefusal)4242));
        Assert.Equal("worst case over the engine's 192-byte event cap", TeSchemaRegistry.RefusalText(TeRefusal.TooBig));
    }
}
