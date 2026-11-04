using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TempEntType.Slots"/>: every field of a class's blob, flat and in blob order,
/// and the by-position accessors a declaration's generated read and write go through.
/// </summary>
public class TempEntSlotTests
{
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

    private static string[] Names(TempEntType type) => type.Slots.Select(s => s.Name).ToArray();

    [Fact]
    public void The_base_chain_comes_first()
    {
        Assert.Equal(["Origin", "Magnitude", "TrailLength", "Direction"], Names(Registry().Find("CTESparks")!));
    }

    [Fact]
    public void A_nested_table_is_its_prop_and_then_its_own_fields_at_their_shifted_offsets()
    {
        TempEntType type = Registry().Find("CTEEffectDispatch")!;

        Assert.Equal("EffectData", type.Slots[0].Name);
        Assert.Equal(TePropKind.DataTable, type.Slots[0].Kind);

        Assert.Equal("Origin", type.Slots[1].Name);
        Assert.Equal(4, type.Slots[1].Offset);
        Assert.Equal("ControlPointOffset", type.Slots[^1].Name);
    }

    [Fact]
    public void A_colour_is_its_four_props()
    {
        string[] names = Names(Registry().Find("CTEBloodStream")!);

        int red = Array.IndexOf(names, "ColorR");

        Assert.True(red >= 0, string.Join(",", names));
        Assert.Equal(["ColorR", "ColorG", "ColorB", "ColorA"], names[red..(red + 4)]);
    }

    /// <summary>A blob with no two bytes alike over its length, and every top bit set somewhere.</summary>
    private static byte[] Patterned(int bytes)
    {
        var blob = new byte[bytes];

        for (int i = 0; i < bytes; i++)
            blob[i] = (byte)(0xC3 + i * 37);

        return blob;
    }

    [Fact]
    public void Every_field_of_every_class_reads_the_same_by_position_as_by_name()
    {
        TempEntRegistry registry = Registry();
        int compared = 0;

        foreach (TempEntType type in registry.All)
        {
            byte[] blob = Patterned(type.BlobBytes);
            var te = new TempEntReader(type, blob);

            for (int slot = 0; slot < type.Slots.Length; slot++)
            {
                TeField field = type.Slots[slot];

                // A name two levels of a chain share resolves to the later one, and only a
                // position can address the earlier: nothing to compare the position against.
                if (type.Fields[field.Name] != field)
                    continue;

                Assert.Equal(te.Int(field.Name), te.SlotInt(slot));
                Assert.Equal(te.UInt(field.Name), te.SlotUInt(slot));
                Assert.Equal(te.Bool(field.Name), te.SlotBool(slot));
                Assert.Equal(BitConverter.SingleToInt32Bits(te.Float(field.Name)),
                             BitConverter.SingleToInt32Bits(te.SlotFloat(slot)));

                Vector named = te.Vector(field.Name), placed = te.SlotVector(slot);
                Assert.True(named.X.Equals(placed.X) && named.Y.Equals(placed.Y) && named.Z.Equals(placed.Z),
                            $"{type.Class.ClassName}.{field.Name}");

                QAngle facing = te.QAngle(field.Name), facingPlaced = te.SlotQAngle(slot);
                Assert.True(facing.Pitch.Equals(facingPlaced.Pitch) && facing.Roll.Equals(facingPlaced.Roll),
                            $"{type.Class.ClassName}.{field.Name}");

                compared++;
            }
        }

        // The stimulus reached the subject: the inventory is over three hundred fields.
        Assert.True(compared > 300, $"{compared} fields compared");
    }

    [Fact]
    public void A_read_past_the_end_of_a_short_blob_is_a_default_and_a_warning()
    {
        TempEntType type = Registry().Find("CTESparks")!;
        var log = (RecordingLog)type.Log;
        int before = log.Lines.Count;

        var te = new TempEntReader(type, new byte[4]);

        Assert.Equal(0, te.SlotInt(1));
        Assert.Equal(Vector.Zero, te.SlotVector(3));
        Assert.True(log.Lines.Count > before, "the short blob and the field past it each say so");
    }

    private static TempEntType HandleType()
    {
        var field = new TeField("Who", TePropKind.Int, 0, 4, 21, TeBits.Literal, TeFlags.Unsigned, 0f, 0f,
                                null, 0, 0, "Who");
        var declaration = new TeClass("CTEWho", "DT_TEWho", null, 4, true, typeof(TeSparks), [field]);

        return new TempEntType(declaration, 1, new Dictionary<string, TeField> { ["Who"] = field }, [field],
                               new RecordingLog());
    }

    [Fact]
    public void The_invalid_handle_reads_as_none_and_never_as_entity_zero()
    {
        var blob = new byte[4];
        var writer = new TempEntWriter(HandleType(), blob);

        Assert.True(writer.SlotHandle(0, EntityHandle.None));
        Assert.Equal(TempEntReader.InvalidHandle, BitConverter.ToInt32(blob));
        Assert.Equal(EntityHandle.None, new TempEntReader(HandleType(), blob).SlotHandle(0));
    }

    [Fact]
    public void A_handle_comes_back_with_its_entry_and_its_sent_serial()
    {
        var blob = new byte[4];
        var writer = new TempEntWriter(HandleType(), blob);

        // Entry 7, serial 3: the serial's top six bits are not sent, so a small one survives.
        Assert.True(writer.SlotHandle(0, new EntityHandle(7u | (3u << 16))));
        Assert.Equal(7 | (3 << 11), BitConverter.ToInt32(blob));
        Assert.Equal(new EntityHandle(7u | (3u << 16)), new TempEntReader(HandleType(), blob).SlotHandle(0));
    }

    [Fact]
    public void Entry_zero_is_entity_zero_and_not_the_invalid_handle()
    {
        var blob = new byte[4];

        Assert.Equal(new EntityHandle(0u), new TempEntReader(HandleType(), blob).SlotHandle(0));
        Assert.NotEqual(EntityHandle.None, new TempEntReader(HandleType(), blob).SlotHandle(0));
    }

    [Fact]
    public void Every_kind_writes_the_bytes_the_by_name_form_writes()
    {
        TempEntRegistry registry = Registry();
        TempEntType type = registry.Find("CTEBloodStream")!;

        var named = new byte[type.BlobBytes];
        var byName = new TempEntWriter(type, named);
        var placed = new byte[type.BlobBytes];
        var bySlot = new TempEntWriter(type, placed);

        for (int slot = 0; slot < type.Slots.Length; slot++)
        {
            TeField field = type.Slots[slot];

            if (field.SizeBytes is 1 or 2 or 4 && field.Elems <= 1)
            {
                Assert.Equal(byName.Int(field.Name, 0x5A + slot), bySlot.SlotInt(slot, 0x5A + slot));
            }
            else
            {
                var v = new Vector(slot, slot * 2f, slot * 3f);
                Assert.Equal(byName.Vector(field.Name, v), bySlot.SlotVector(slot, v));
            }
        }

        Assert.Equal(named, placed);
        Assert.Contains(placed, b => b != 0);
    }

    [Fact]
    public void An_array_element_is_written_and_read_by_position_and_index()
    {
        TempEntType type = Registry().Find("CTEBeamSpline")!;

        int slot = Array.IndexOf(Names(type), "Spline");
        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        Assert.True(writer.SlotVectorAt(slot, 15, new Vector(1f, 2f, 3f)));

        Assert.Equal(new Vector(1f, 2f, 3f), new TempEntReader(type, blob).VectorAt("Spline", 15));
        Assert.Equal(new Vector(1f, 2f, 3f), new TempEntReader(type, blob).SlotVectorAt(slot, 15));
        Assert.False(writer.SlotVectorAt(slot, 16, new Vector(1f, 2f, 3f)));
    }
}
