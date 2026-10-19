using System.Runtime.InteropServices;
using System.Text;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Native memory a fact owns: NUL-terminated names (which a token table holds
/// by pointer) and buffers, all freed with the fact.
/// </summary>
internal sealed unsafe class SaveArena : IDisposable
{
    private readonly List<nint> _blocks = [];

    public byte* Name(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte* p = (byte*)Alloc(bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        p[bytes.Length] = 0;
        return p;
    }

    public void* Alloc(int bytes)
    {
        void* p = NativeMemory.AllocZeroed((nuint)Math.Max(bytes, 1));
        _blocks.Add((nint)p);
        return p;
    }

    /// <summary>A CSaveRestoreSegment-style token table of <paramref name="count"/> slots.</summary>
    public SaveTokenTable Tokens(int count = 0xfff) => new((byte**)Alloc(count * sizeof(nint)), count);

    public void Dispose()
    {
        foreach (nint p in _blocks)
            NativeMemory.Free((void*)p);
        _blocks.Clear();
    }
}

/// <summary>
/// The engine's own save header, as the shape the facts encode (the native
/// codec's facts used the same one, shell_savegame_test.cpp).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SaveHeaderShape
{
    public int SkillLevel;
    public int ConnectionCount;
    public int LightStyleCount;
    public int MapVersion;
    public float Time;
    public fixed byte MapName[32];
    public fixed byte SkyName[32];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LightStyleShape
{
    public int Index;
    public fixed byte Style[64];
}

/// <summary>One field of every type the codec carries, for the round-trips.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct EveryTypeShape
{
    public int Integer;
    public short Short;
    public short Pad;
    public float Time;
    public float VecX, VecY, VecZ;
    public float PosX, PosY, PosZ;
    public fixed byte Chars[20];
}

/// <summary>
/// The managed save codec's record layout (plan_server_native_v2 T4c), from
/// stock CSave::WriteFields / CRestore::ReadFields and the native codec it
/// replaces on the server (shell_savegame.h, whose facts these port).
/// </summary>
public sealed unsafe class SaveBlockCodecTests : IDisposable
{
    private const float BaseTime = 100.0f;
    private readonly SaveArena _arena = new();

    public void Dispose() => _arena.Dispose();

    private static int Off<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    private SaveFieldDesc[] HeaderFields() =>
    [
        new(_arena.Name("skillLevel"), SaveFieldCodec.FieldInteger, 1, Off<SaveHeaderShape>("SkillLevel")),
        new(_arena.Name("connectionCount"), SaveFieldCodec.FieldInteger, 1, Off<SaveHeaderShape>("ConnectionCount")),
        new(_arena.Name("lightStyleCount"), SaveFieldCodec.FieldInteger, 1, Off<SaveHeaderShape>("LightStyleCount")),
        new(_arena.Name("mapVersion"), SaveFieldCodec.FieldInteger, 1, Off<SaveHeaderShape>("MapVersion")),
        new(_arena.Name("time"), SaveFieldCodec.FieldTime, 1, Off<SaveHeaderShape>("Time")),
        new(_arena.Name("mapName"), SaveFieldCodec.FieldCharacter, 32, Off<SaveHeaderShape>("MapName")),
        new(_arena.Name("skyName"), SaveFieldCodec.FieldCharacter, 32, Off<SaveHeaderShape>("SkyName")),
    ];

    private SaveFieldDesc[] LightStyleFields() =>
    [
        new(_arena.Name("index"), SaveFieldCodec.FieldInteger, 1, Off<LightStyleShape>("Index")),
        new(_arena.Name("style"), SaveFieldCodec.FieldCharacter, 64, Off<LightStyleShape>("Style")),
    ];

    // The count record, four int records, the time record and two 32-byte strings.
    private const int HeaderBlock = (4 + 4) + 4 * (4 + 4) + (4 + 4) + 2 * (4 + 32);

    private static void SetText(byte* dest, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        bytes.CopyTo(new Span<byte>(dest, bytes.Length));
        dest[bytes.Length] = 0;
    }

    private static string Text(byte* p) => Marshal.PtrToStringUTF8((nint)p) ?? "";

    private static SaveHeaderShape AHeader()
    {
        var h = new SaveHeaderShape { SkillLevel = 2, LightStyleCount = 15, MapVersion = 4, Time = 123.5f };
        SetText(h.MapName, "dm_lockdown");
        SetText(h.SkyName, "sky_day01_01");
        return h;
    }

    private int WriteHeader(byte* buffer, int bytes, SaveTokenTable tokens, string block, out int omitted)
    {
        SaveHeaderShape header = AHeader();
        var cursor = new SaveCursor(buffer, bytes);
        return SaveBlockCodec.WriteFieldBlock(ref cursor, tokens, _arena.Name(block), (byte*)&header,
                                              HeaderFields(), BaseTime, out omitted);
    }

    private static SaveHeaderShape Poisoned()
    {
        SaveHeaderShape s = default;
        new Span<byte>(&s, sizeof(SaveHeaderShape)).Fill(0xcd);
        return s;
    }

    private static bool StillPoisoned(SaveHeaderShape s)
        => !new ReadOnlySpan<byte>(&s, sizeof(SaveHeaderShape)).ContainsAnyExcept((byte)0xcd);

    //-------------------------------------------------------------------------
    // The block format.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABlockOpensWithAFourByteCountRecordNamingTheBlock()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();

        Assert.True(WriteHeader(buf, 512, tokens, "Save Header", out _) > 0);

        Assert.Equal(4, *(short*)buf);
        Assert.Equal("Save Header", Text(tokens.FromSymbol(*(short*)(buf + 2))));
        Assert.Equal(7, *(int*)(buf + 4));
    }

    [Fact]
    public void EveryFieldRecordCarriesItsOwnWidthAndItsOwnName()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        var light = new LightStyleShape { Index = 7 };
        SetText(light.Style, "mmnmmommommnonmmonqnmmo");

        var cursor = new SaveCursor(buf, 512);
        Assert.True(SaveBlockCodec.WriteFieldBlock(ref cursor, tokens, _arena.Name("LIGHTSTYLE"), (byte*)&light,
                                                   LightStyleFields(), BaseTime, out _) > 0);

        Assert.Equal(4, *(short*)(buf + 8));
        Assert.Equal("index", Text(tokens.FromSymbol(*(short*)(buf + 10))));
        Assert.Equal(64, *(short*)(buf + 16));
        Assert.Equal("style", Text(tokens.FromSymbol(*(short*)(buf + 18))));
    }

    [Fact]
    public void TheBlockIsExactlyItsRecordsAndNothingMore()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveHeaderShape header = AHeader();
        var cursor = new SaveCursor(buf, 512);

        Assert.Equal(HeaderBlock, SaveBlockCodec.WriteFieldBlock(ref cursor, _arena.Tokens(), _arena.Name("Save Header"),
                                                                 (byte*)&header, HeaderFields(), BaseTime, out _));
        Assert.Equal(HeaderBlock, cursor.Pos);
    }

    [Fact]
    public void TheEnginesSaveHeaderComesBackFieldForField()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteHeader(buf, 512, tokens, "Save Header", out _);
        Assert.Equal(HeaderBlock, written);

        SaveHeaderShape output = Poisoned();
        var cursor = new SaveCursor(buf, written);
        int read = SaveBlockCodec.ReadFieldBlock(ref cursor, tokens, _arena.Name("Save Header"), (byte*)&output,
                                                 HeaderFields(), BaseTime, out string? refusal);

        Assert.Equal(written, read);
        Assert.Null(refusal);
        SaveHeaderShape input = AHeader();
        Assert.Equal(input.SkillLevel, output.SkillLevel);
        Assert.Equal(input.ConnectionCount, output.ConnectionCount);
        Assert.Equal(input.LightStyleCount, output.LightStyleCount);
        Assert.Equal(input.MapVersion, output.MapVersion);
        Assert.Equal(input.Time, output.Time);
        Assert.Equal("dm_lockdown", Text(output.MapName));
        Assert.Equal("sky_day01_01", Text(output.SkyName));
    }

    [Fact]
    public void AFixedWidthStringCarriesItsWholeArrayAndNotJustTheText()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        var light = new LightStyleShape { Index = 7 };
        SetText(light.Style, "mmnmmommommnonmmonqnmmo");
        light.Style[60] = (byte)'Z';

        var wr = new SaveCursor(buf, 512);
        int written = SaveBlockCodec.WriteFieldBlock(ref wr, tokens, _arena.Name("LIGHTSTYLE"), (byte*)&light,
                                                     LightStyleFields(), BaseTime, out _);
        Assert.True(written > 0);

        LightStyleShape output = default;
        var rd = new SaveCursor(buf, written);
        Assert.Equal(written, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, _arena.Name("LIGHTSTYLE"), (byte*)&output,
                                                            LightStyleFields(), BaseTime, out _));
        Assert.Equal(7, output.Index);
        Assert.Equal("mmnmmommommnonmmonqnmmo", Text(output.Style));
        Assert.Equal((byte)'Z', output.Style[60]);
    }

    [Fact]
    public void BlocksWrittenBackToBackReadBackInTheSameOrder()
    {
        byte* buf = (byte*)_arena.Alloc(8192);
        SaveTokenTable tokens = _arena.Tokens();
        SaveFieldDesc[] header = HeaderFields();
        SaveFieldDesc[] light = LightStyleFields();
        byte* lightName = _arena.Name("LIGHTSTYLE");

        var wr = new SaveCursor(buf, 8192);
        SaveHeaderShape h = AHeader();
        Assert.True(SaveBlockCodec.WriteFieldBlock(ref wr, tokens, _arena.Name("Save Header"), (byte*)&h, header, BaseTime, out _) > 0);
        for (int i = 0; i < 15; i++)
        {
            var ls = new LightStyleShape { Index = i };
            ls.Style[0] = (byte)('a' + i);
            Assert.True(SaveBlockCodec.WriteFieldBlock(ref wr, tokens, lightName, (byte*)&ls, light, BaseTime, out _) > 0);
        }

        int total = wr.Pos;
        var rd = new SaveCursor(buf, total);
        SaveHeaderShape got = default;
        Assert.True(SaveBlockCodec.ReadFieldBlock(ref rd, tokens, _arena.Name("Save Header"), (byte*)&got, header, BaseTime, out _) > 0);
        Assert.Equal("dm_lockdown", Text(got.MapName));
        for (int i = 0; i < 15; i++)
        {
            LightStyleShape ls = default;
            Assert.True(SaveBlockCodec.ReadFieldBlock(ref rd, tokens, lightName, (byte*)&ls, light, BaseTime, out _) > 0);
            Assert.Equal(i, ls.Index);
            Assert.Equal((byte)('a' + i), ls.Style[0]);
        }
        Assert.Equal(total, rd.Pos);
    }

    //-------------------------------------------------------------------------
    // A round-trip per field type the codec carries.
    //-------------------------------------------------------------------------

    private EveryTypeShape RoundTripOne(EveryTypeShape input, int type, int size, string member,
                                        float writeBase = BaseTime, float readBase = BaseTime)
    {
        byte* buf = (byte*)_arena.Alloc(256);
        SaveTokenTable tokens = _arena.Tokens();
        SaveFieldDesc[] fields = [new(_arena.Name(member), type, size, Off<EveryTypeShape>(member))];
        byte* block = _arena.Name("ONE");

        var wr = new SaveCursor(buf, 256);
        int written = SaveBlockCodec.WriteFieldBlock(ref wr, tokens, block, (byte*)&input, fields, writeBase, out int omitted);
        Assert.Equal(0, omitted);
        Assert.Equal(8 + 4 + SaveFieldCodec.Bytes(type, size), written);

        EveryTypeShape output = default;
        var rd = new SaveCursor(buf, written);
        Assert.Equal(written, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, block, (byte*)&output, fields, readBase, out string? refusal));
        Assert.Null(refusal);
        return output;
    }

    [Fact]
    public void AnIntegerRoundTrips()
        => Assert.Equal(-123456789, RoundTripOne(new EveryTypeShape { Integer = -123456789 }, SaveFieldCodec.FieldInteger, 1, "Integer").Integer);

    [Fact]
    public void AShortRoundTripsInTwoBytes()
    {
        EveryTypeShape output = RoundTripOne(new EveryTypeShape { Short = -1234, Pad = 77 }, SaveFieldCodec.FieldShort, 1, "Short");
        Assert.Equal(-1234, output.Short);
        Assert.Equal(0, output.Pad);   // the two bytes after it did not travel
    }

    [Fact]
    public void ATimeRoundTripsRebasedOntoTheLoadTime()
        => Assert.Equal(37.5f, RoundTripOne(new EveryTypeShape { Time = 130.0f }, SaveFieldCodec.FieldTime, 1, "Time",
                                            writeBase: 100.0f, readBase: 7.5f).Time);

    [Fact]
    public void AVectorRoundTripsAllThreeComponents()
    {
        EveryTypeShape output = RoundTripOne(new EveryTypeShape { VecX = 1.5f, VecY = -2.25f, VecZ = 1e6f }, SaveFieldCodec.FieldVector, 1, "VecX");
        Assert.Equal((1.5f, -2.25f, 1e6f), (output.VecX, output.VecY, output.VecZ));
    }

    [Fact]
    public void APositionVectorRoundTripsAllThreeComponents()
    {
        EveryTypeShape output = RoundTripOne(new EveryTypeShape { PosX = 64f, PosY = -32f, PosZ = 8.5f }, SaveFieldCodec.FieldPositionVector, 1, "PosX");
        Assert.Equal((64f, -32f, 8.5f), (output.PosX, output.PosY, output.PosZ));
    }

    [Fact]
    public void ACharArrayRoundTripsItsWholeWidth()
    {
        var input = new EveryTypeShape();
        for (int i = 0; i < 20; i++)
            input.Chars[i] = (byte)(i + 1);
        EveryTypeShape output = RoundTripOne(input, SaveFieldCodec.FieldCharacter, 20, "Chars");
        for (int i = 0; i < 20; i++)
            Assert.Equal((byte)(i + 1), output.Chars[i]);
    }

    //-------------------------------------------------------------------------
    // The field widths, and what is refused.
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData(SaveFieldCodec.FieldInteger, 1, 4)]
    [InlineData(SaveFieldCodec.FieldTime, 1, 4)]
    [InlineData(SaveFieldCodec.FieldShort, 1, 2)]
    [InlineData(SaveFieldCodec.FieldVector, 1, 12)]
    [InlineData(SaveFieldCodec.FieldPositionVector, 1, 12)]
    [InlineData(SaveFieldCodec.FieldCharacter, 32, 32)]
    [InlineData(SaveFieldCodec.FieldInteger, 2, -1)]   // an array of a scalar is refused, not guessed at
    [InlineData(SaveFieldCodec.FieldTime, 0, -1)]      // a non-positive count is refused
    [InlineData(1, 1, -1)]                             // FIELD_FLOAT: not one of the six
    [InlineData(13, 1, -1)]                            // FIELD_EHANDLE
    public void EachTypeHasItsMeasuredWidth(int type, int size, int bytes)
        => Assert.Equal(bytes, SaveFieldCodec.Bytes(type, size));

    [Fact]
    public void TheSixTypeNumbersAreDatamapsOwn()
    {
        // datamap.h's fieldtype_t, counted from FIELD_VOID = 0.
        Assert.Equal(3, SaveFieldCodec.FieldVector);
        Assert.Equal(5, SaveFieldCodec.FieldInteger);
        Assert.Equal(7, SaveFieldCodec.FieldShort);
        Assert.Equal(8, SaveFieldCodec.FieldCharacter);
        Assert.Equal(15, SaveFieldCodec.FieldPositionVector);
        Assert.Equal(16, SaveFieldCodec.FieldTime);
    }

    [Fact]
    public void OneUnknownFieldRefusesTheWholeBlocksSize()
    {
        SaveFieldDesc[] fields = [.. HeaderFields(), new(_arena.Name("ehandle"), 13, 1, 0)];
        Assert.Equal(-1, SaveFieldCodec.BlockBytes(fields));
        Assert.Equal(HeaderBlock - 8 - 7 * 4, SaveFieldCodec.BlockBytes(HeaderFields()));
    }

    //-------------------------------------------------------------------------
    // Times and positions.
    //-------------------------------------------------------------------------

    [Fact]
    public void ATimeIsStoredAsADeltaFromTheSavesBaseTime()
    {
        float stored = SaveFieldCodec.EncodeTime(130.0f, 100.0f);
        Assert.Equal(30.0f, stored);
        Assert.Equal(130.0f, SaveFieldCodec.DecodeTime(stored, 100.0f));
        Assert.Equal(30.0f, SaveFieldCodec.DecodeTime(stored, 0.0f));
    }

    [Fact]
    public void AZeroTimeRestoresAsZeroAndNotAsTheMomentOfTheSave()
    {
        float stored = SaveFieldCodec.EncodeTime(0.0f, 100.0f);
        Assert.NotEqual(0.0f, stored);
        Assert.Equal(0.0f, SaveFieldCodec.DecodeTime(stored, 100.0f));
        Assert.Equal(0.0f, SaveFieldCodec.DecodeTime(stored, 7.0f));
    }

    [Fact]
    public void TheTwoTimeSentinelsAreNotRebased()
    {
        Assert.Equal(SaveFieldCodec.InvalidTime, SaveFieldCodec.EncodeTime(SaveFieldCodec.InvalidTime, 100.0f));
        Assert.Equal(SaveFieldCodec.InvalidTime, SaveFieldCodec.DecodeTime(SaveFieldCodec.InvalidTime, 100.0f));
        Assert.Equal(float.MaxValue, SaveFieldCodec.EncodeTime(float.MaxValue, 100.0f));
        Assert.Equal(float.MaxValue, SaveFieldCodec.DecodeTime(float.MaxValue, 100.0f));
    }

    [Fact]
    public void TheTimeSentinelsAreStocksFltMaxMultiples()
    {
        // saverestore.h: ZERO_TIME ((FLT_MAX*-0.5)), INVALID_TIME (FLT_MAX * -1.0).
        Assert.Equal(float.MaxValue * -0.5f, SaveFieldCodec.ZeroTime);
        Assert.Equal(-float.MaxValue, SaveFieldCodec.InvalidTime);
    }

    [Fact]
    public void ARealTimeIsNeverAllowedToRebaseToZero()
    {
        float stored = SaveFieldCodec.EncodeTime(100.0f, 100.0f);
        Assert.NotEqual(0.0f, stored);
        Assert.InRange(SaveFieldCodec.DecodeTime(stored, 100.0f), 99.99f, 100.01f);
    }

    [Fact]
    public void APositionIsRebasedOntoTheLandmarkItIsLoadedWith()
    {
        var stored = SaveFieldCodec.EncodePosition(110f, 220f, 330f, 10f, 20f, 30f);
        Assert.Equal((100f, 200f, 300f), stored);
        Assert.Equal((105f, 205f, 305f), SaveFieldCodec.DecodePosition(stored.X, stored.Y, stored.Z, 5f, 5f, 5f));
    }

    [Fact]
    public void AnInvalidPositionSurvivesAChangedLandmark()
    {
        float m = float.MaxValue;
        Assert.Equal((m, m, m), SaveFieldCodec.EncodePosition(m, m, m, 10f, 20f, 30f));
        Assert.Equal((m, m, m), SaveFieldCodec.DecodePosition(m, m, m, 10f, 20f, 30f));
    }

    [Fact]
    public void TheInvalidPositionIsAllThreeComponentsAndNotOne()
    {
        Assert.True(SaveFieldCodec.IsInvalidPosition(float.MaxValue, float.MaxValue, float.MaxValue));
        Assert.False(SaveFieldCodec.IsInvalidPosition(float.MaxValue, 0f, float.MaxValue));
        Assert.Equal((float.MaxValue - 1f, -1f, float.MaxValue - 1f),
                     SaveFieldCodec.EncodePosition(float.MaxValue, 0f, float.MaxValue, 1f, 1f, 1f));
    }

    //-------------------------------------------------------------------------
    // Omission and refusal.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFieldTypeThisBuildCannotEncodeIsOmittedAndCounted()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        SaveHeaderShape header = AHeader();
        SaveFieldDesc[] fields = [.. HeaderFields(), new(_arena.Name("ehandle"), 13, 1, 0), new(_arena.Name("floats"), 1, 4, 0)];

        var cursor = new SaveCursor(buf, 512);
        int written = SaveBlockCodec.WriteFieldBlock(ref cursor, tokens, _arena.Name("Save Header"), (byte*)&header,
                                                     fields, BaseTime, out int omitted);

        Assert.Equal(2, omitted);
        Assert.Equal(HeaderBlock, written);
        Assert.Equal(7, *(int*)(buf + 4));   // the count is the records written, not the fields declared
    }

    [Fact]
    public void ABlockWrittenUnderAnotherNameIsRefusedAndTheCursorDoesNotMove()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteHeader(buf, 512, tokens, "Save Header", out _);

        SaveHeaderShape dest = Poisoned();
        var rd = new SaveCursor(buf, written);
        Assert.Equal(-1, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, _arena.Name("LIGHTSTYLE"), (byte*)&dest,
                                                       HeaderFields(), BaseTime, out string? refusal));
        Assert.Equal("Save Header", refusal);
        Assert.True(StillPoisoned(dest));
        Assert.Equal(0, rd.Pos);
    }

    [Fact]
    public void BytesThatAreNotABlockHeaderAtAllAreRefused()
    {
        byte* buf = (byte*)_arena.Alloc(64);
        new Span<byte>(buf, 64).Fill(0x5a);
        SaveHeaderShape dest = Poisoned();
        var rd = new SaveCursor(buf, 64);

        Assert.Equal(-1, SaveBlockCodec.ReadFieldBlock(ref rd, _arena.Tokens(), _arena.Name("Save Header"), (byte*)&dest,
                                                       HeaderFields(), BaseTime, out string? refusal));
        Assert.NotNull(refusal);
        Assert.True(StillPoisoned(dest));
        Assert.Equal(0, rd.Pos);
    }

    [Fact]
    public void EveryTruncationOfABlockIsRefusedAndNeverHalfFilled()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteHeader(buf, 512, tokens, "Save Header", out _);
        Assert.Equal(HeaderBlock, written);
        byte* block = _arena.Name("Save Header");
        SaveFieldDesc[] fields = HeaderFields();

        for (int cut = 0; cut < written; cut++)
        {
            SaveHeaderShape output = Poisoned();
            var rd = new SaveCursor(buf, cut);
            Assert.Equal(-1, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, block, (byte*)&output, fields, BaseTime, out string? refusal));
            Assert.NotNull(refusal);
            Assert.True(StillPoisoned(output), $"a {cut}-byte prefix touched the destination");
        }

        SaveHeaderShape whole = default;
        var all = new SaveCursor(buf, written);
        Assert.Equal(written, SaveBlockCodec.ReadFieldBlock(ref all, tokens, block, (byte*)&whole, fields, BaseTime, out _));
        Assert.Equal("dm_lockdown", Text(whole.MapName));
    }

    [Fact]
    public void AFieldTheFileCarriesAndThisBuildDoesNotIsSkippedByItsOwnWidth()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        SaveHeaderShape header = AHeader();
        SaveFieldDesc[] wide = [new(_arena.Name("extra"), SaveFieldCodec.FieldCharacter, 9, 0), .. HeaderFields()];
        byte* block = _arena.Name("Save Header");

        var wr = new SaveCursor(buf, 512);
        int written = SaveBlockCodec.WriteFieldBlock(ref wr, tokens, block, (byte*)&header, wide, BaseTime, out _);

        SaveHeaderShape output = default;
        var rd = new SaveCursor(buf, written);
        Assert.Equal(written, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, block, (byte*)&output, HeaderFields(), BaseTime, out _));
        Assert.Equal(4, output.MapVersion);
        Assert.Equal("sky_day01_01", Text(output.SkyName));
    }

    [Fact]
    public void AFieldThisBuildHasAndTheFileDoesNotRestoresAsZero()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        SaveHeaderShape header = AHeader();
        SaveFieldDesc[] narrow = HeaderFields()[1..];
        byte* block = _arena.Name("Save Header");

        var wr = new SaveCursor(buf, 512);
        int written = SaveBlockCodec.WriteFieldBlock(ref wr, tokens, block, (byte*)&header, narrow, BaseTime, out _);

        SaveHeaderShape output = Poisoned();
        var rd = new SaveCursor(buf, written);
        Assert.Equal(written, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, block, (byte*)&output, HeaderFields(), BaseTime, out _));
        Assert.Equal(0, output.SkillLevel);
        Assert.Equal(15, output.LightStyleCount);
    }

    [Fact]
    public void ARecordOfTheRightNameAndTheWrongWidthIsSkipped()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        SaveHeaderShape header = AHeader();
        byte* block = _arena.Name("B");
        SaveFieldDesc[] asShort = [new(_arena.Name("skillLevel"), SaveFieldCodec.FieldShort, 1, 0)];

        var wr = new SaveCursor(buf, 512);
        int written = SaveBlockCodec.WriteFieldBlock(ref wr, tokens, block, (byte*)&header, asShort, BaseTime, out _);

        SaveHeaderShape output = Poisoned();
        var rd = new SaveCursor(buf, written);
        Assert.Equal(written, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, block, (byte*)&output, HeaderFields()[..1], BaseTime, out _));
        Assert.Equal(0, output.SkillLevel);   // zeroed, and the two-byte record did not land in a four-byte field
    }

    [Fact]
    public void ANegativeFieldCountOffAFileIsRefusedRatherThanLoopedOn()
    {
        byte* buf = (byte*)_arena.Alloc(512);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteHeader(buf, 512, tokens, "Save Header", out _);
        *(int*)(buf + 4) = -3;

        SaveHeaderShape output = Poisoned();
        var rd = new SaveCursor(buf, written);
        Assert.Equal(-1, SaveBlockCodec.ReadFieldBlock(ref rd, tokens, _arena.Name("Save Header"), (byte*)&output,
                                                       HeaderFields(), BaseTime, out string? refusal));
        Assert.Contains("negative field count", refusal);
        Assert.Equal(0, rd.Pos);
    }

    [Fact]
    public void ANullStructIsRefusedRatherThanDereferenced()
    {
        byte* buf = (byte*)_arena.Alloc(64);
        var rd = new SaveCursor(buf, 64);
        Assert.Equal(-1, SaveBlockCodec.ReadFieldBlock(ref rd, _arena.Tokens(), _arena.Name("X"), null,
                                                       HeaderFields(), BaseTime, out string? refusal));
        Assert.NotNull(refusal);
        var wr = new SaveCursor(buf, 64);
        Assert.Equal(-1, SaveBlockCodec.WriteFieldBlock(ref wr, _arena.Tokens(), _arena.Name("X"), null,
                                                        HeaderFields(), BaseTime, out _));
    }

    //-------------------------------------------------------------------------
    // The cursor and the names.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnOverrunPoisonsTheCursorRatherThanClamping()
    {
        byte* buf = (byte*)_arena.Alloc(8);
        var cursor = new SaveCursor(buf, 8);
        Assert.True(cursor.WriteInt32(1));
        Assert.False(cursor.Write(new byte[5]));
        Assert.True(cursor.Overflowed);
        Assert.False(cursor.WriteInt16(1));   // even though two bytes would fit
        Assert.Equal(4, cursor.Pos);
    }

    [Fact]
    public void ANullBufferIsOverflowedFromTheStart()
    {
        var cursor = new SaveCursor(null, 100);
        Assert.True(cursor.Overflowed);
        Assert.False(cursor.WriteInt16(0));
    }

    [Fact]
    public void AWriteThatWouldOverrunWritesNothingAtAll()
    {
        byte* buf = (byte*)_arena.Alloc(4);
        new Span<byte>(buf, 4).Fill(0xee);
        var cursor = new SaveCursor(buf, 3);
        Assert.False(cursor.WriteInt32(0));
        Assert.False(new ReadOnlySpan<byte>(buf, 4).ContainsAnyExcept((byte)0xee));
    }

    [Fact]
    public void FieldNamesMatchCaseInsensitivelyBecauseCRestoreDoes()
    {
        Assert.True(SaveBlockCodec.NamesMatch(_arena.Name("mapName"), _arena.Name("MAPNAME")));
        Assert.False(SaveBlockCodec.NamesMatch(_arena.Name("mapName"), _arena.Name("mapNam")));
        Assert.False(SaveBlockCodec.NamesMatch(_arena.Name("mapName"), null));
    }

    //-------------------------------------------------------------------------
    // The token table: CSaveRestoreSegment's.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheTokenHashIsCSaveRestoreSegmentsRotateAndXor()
    {
        // HashString("ab"): 0 -> ror4 ^ 'a' = 0x61 -> ror4 = 0x10000006 ^ 'b' = 0x10000064.
        Assert.Equal(0x10000064u, SaveTokenTable.HashString(_arena.Name("ab")));
        // A high-bit byte is sign-extended, as (int)(signed char) does.
        Assert.Equal(0xFFFFFFFFu, SaveTokenTable.HashString(Byte0xFF()));
    }

    private byte* Byte0xFF()
    {
        byte* p = (byte*)_arena.Alloc(2);
        p[0] = 0xff;
        return p;
    }

    [Fact]
    public void ANameInternsAtItsHashSlotAndAgainAtTheSameOne()
    {
        SaveTokenTable tokens = _arena.Tokens(17);
        byte* name = _arena.Name("Save Header");
        ushort token = tokens.FindCreate(name);

        Assert.Equal(SaveTokenTable.HashString(name) % 17u, token);
        Assert.True(tokens.FromSymbol(token) == name);   // the pointer, not a copy
        Assert.Equal(token, tokens.FindCreate(_arena.Name("Save Header")));   // a strcmp, not a pointer compare
    }

    [Fact]
    public void ACollisionProbesToTheNextSlot()
    {
        SaveTokenTable tokens = _arena.Tokens(1 + 1);
        byte* a = _arena.Name("a");   // 0x61 % 2 = 1
        byte* c = _arena.Name("c");   // 0x63 % 2 = 1
        Assert.Equal(1, tokens.FindCreate(a));
        Assert.Equal(0, tokens.FindCreate(c));
    }

    [Fact]
    public void ATokenOffAFileThatNamesNothingAnswersNull()
    {
        // Six slots of memory, a table of four: the two either side of it hold
        // names, so a bound read one past either end would find one.
        byte** slots = (byte**)_arena.Alloc(6 * sizeof(nint));
        slots[0] = _arena.Name("before");
        slots[5] = _arena.Name("after");
        slots[2] = _arena.Name("inside");
        var tokens = new SaveTokenTable(slots + 1, 4);

        Assert.True(tokens.FromSymbol(-1) == null);
        Assert.True(tokens.FromSymbol(4) == null);
        Assert.True(tokens.FromSymbol(0) == null);   // an empty slot inside it
        Assert.Equal("inside", Text(tokens.FromSymbol(1)));   // the control: a defined slot answers
    }

    //-------------------------------------------------------------------------
    // The blob block.
    //-------------------------------------------------------------------------

    private int WriteBlob(byte* buf, int bytes, SaveTokenTable tokens, byte[] payload, string block = "SSENTITY")
    {
        var cursor = new SaveCursor(buf, bytes);
        return SaveBlockCodec.WriteBlobBlock(ref cursor, tokens, _arena.Name(block), _arena.Name("BLOB"), payload);
    }

    private static byte[] Payload(int bytes)
    {
        var payload = new byte[bytes];
        for (int i = 0; i < bytes; i++)
            payload[i] = (byte)(i * 7 + 3);
        return payload;
    }

    [Fact]
    public void ABlobComesBackByteForByte()
    {
        byte* buf = (byte*)_arena.Alloc(1024);
        SaveTokenTable tokens = _arena.Tokens();
        byte[] payload = Payload(300);
        int written = WriteBlob(buf, 1024, tokens, payload);
        Assert.Equal(8 + 4 + 300, written);

        var output = new byte[300];
        var rd = new SaveCursor(buf, written);
        Assert.Equal(300, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, _arena.Name("SSENTITY"), output, out string? refusal));
        Assert.Null(refusal);
        Assert.Equal(payload, output);
        Assert.Equal(written, rd.Pos);
    }

    [Fact]
    public void APayloadWiderThanARecordIsChunkedAndStillExact()
    {
        int bytes = SaveBlockCodec.BlobChunk * 2 + 17;
        byte* buf = (byte*)_arena.Alloc(bytes + 64);
        SaveTokenTable tokens = _arena.Tokens();
        byte[] payload = Payload(bytes);
        int written = WriteBlob(buf, bytes + 64, tokens, payload);

        Assert.Equal(8 + 3 * 4 + bytes, written);
        Assert.Equal(3, *(int*)(buf + 4));

        var output = new byte[bytes];
        var rd = new SaveCursor(buf, written);
        Assert.Equal(bytes, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, _arena.Name("SSENTITY"), output, out _));
        Assert.Equal(payload, output);
    }

    [Fact]
    public void SizingABlobDoesNotConsumeIt()
    {
        byte* buf = (byte*)_arena.Alloc(1024);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteBlob(buf, 1024, tokens, Payload(100));

        var rd = new SaveCursor(buf, written);
        Assert.Equal(100, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, _arena.Name("SSENTITY"), Span<byte>.Empty, out _));
        Assert.Equal(0, rd.Pos);
    }

    [Fact]
    public void ASaveWithNoBlobIsNotAnError()
    {
        byte* buf = (byte*)_arena.Alloc(16);
        var rd = new SaveCursor(buf, 0);
        Assert.Equal(SaveBlockCodec.NoBlock, SaveBlockCodec.ReadBlobBlock(ref rd, _arena.Tokens(), _arena.Name("SSENTITY"),
                                                                          Span<byte>.Empty, out string? refusal));
        Assert.Null(refusal);
    }

    [Fact]
    public void AnotherBlocksHeaderIsLeftWhereItWas()
    {
        byte* buf = (byte*)_arena.Alloc(1024);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteHeader(buf, 1024, tokens, "Save Header", out _);

        var rd = new SaveCursor(buf, written);
        Assert.Equal(SaveBlockCodec.NoBlock, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, _arena.Name("SSENTITY"),
                                                                          new byte[64], out _));
        Assert.Equal(0, rd.Pos);
    }

    [Fact]
    public void EveryTruncationOfABlobIsRefusedAndNothingIsCopied()
    {
        byte* buf = (byte*)_arena.Alloc(1024);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteBlob(buf, 1024, tokens, Payload(40));
        byte* block = _arena.Name("SSENTITY");

        for (int cut = 8; cut < written; cut++)
        {
            var output = new byte[40];
            var rd = new SaveCursor(buf, cut);
            Assert.Equal(-1, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, block, output, out string? refusal));
            Assert.NotNull(refusal);
            Assert.Equal(0, rd.Pos);
            Assert.DoesNotContain(output, b => b != 0);
        }
    }

    [Fact]
    public void ANegativeChunkCountIsRefused()
    {
        byte* buf = (byte*)_arena.Alloc(1024);
        SaveTokenTable tokens = _arena.Tokens();
        int written = WriteBlob(buf, 1024, tokens, Payload(40));
        *(int*)(buf + 4) = -1;

        var rd = new SaveCursor(buf, written);
        Assert.Equal(-1, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, _arena.Name("SSENTITY"), new byte[40], out string? refusal));
        Assert.Contains("negative chunk count", refusal);
    }

    [Fact]
    public void AnEmptyBlobIsACountRecordOfZero()
    {
        byte* buf = (byte*)_arena.Alloc(64);
        SaveTokenTable tokens = _arena.Tokens();
        Assert.Equal(8, WriteBlob(buf, 64, tokens, []));
        var rd = new SaveCursor(buf, 8);
        Assert.Equal(0, SaveBlockCodec.ReadBlobBlock(ref rd, tokens, _arena.Name("SSENTITY"), Span<byte>.Empty, out _));
    }
}
