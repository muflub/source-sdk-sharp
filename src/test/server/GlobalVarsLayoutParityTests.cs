using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That <see cref="CGlobalVarsRaw"/> is still the engine's <c>CGlobalVars</c>.
///
/// <para>
/// WHY THIS EXISTS. <see cref="Globals"/> reads engine-owned memory through a
/// pointer, which is the only way to be current — <c>CGlobalVars</c> changes
/// every frame and a copy taken at load would be stale everywhere. The price is
/// that a mirror whose fields sit at different offsets than the C++ class does
/// not fail: it reads the neighbouring field's bytes and returns a plausible
/// number. A <c>curtime</c> that is really <c>frametime</c> looks like a game
/// running very slowly, and nothing anywhere says so.
/// </para>
///
/// <para>
/// So the C side is the authority and the numbers all come from it. The table
/// lives in src/sourcesharp/game/shared/globalvars_layout.h, where every row is
/// a <c>static_assert</c> against the real <c>offsetof(CGlobalVars, ...)</c> —
/// the compiler refuses the header if a row is wrong — and this reads those rows
/// as text, the same way <see cref="AbiParityTests"/> reads sourcesharp.h and
/// <see cref="TierParityTests"/> reads generic_nettable.h. C# contributes no
/// number to the comparison, which is what stops it being a check that cannot
/// fail.
/// </para>
///
/// <para>
/// BOTH C# LAYOUTS ARE CHECKED, and that is not belt and braces. A C# struct has
/// two: the runtime layout, which is what <c>_p-&gt;curtime</c> actually reads
/// through a pointer, and the marshalled layout, which is what
/// <see cref="Marshal.OffsetOf"/> describes. For a blittable struct they are the
/// same, and a single <c>bool</c> field makes them differ — one byte in the
/// runtime layout, four under the marshaller. Checking only the marshalled one
/// would be checking a layout the game never uses; checking only the runtime one
/// would let the mirror drift into a shape whose two answers disagree. So
/// <see cref="TheMirrorsRuntimeLayoutIsTheHeadersTable"/> pokes bytes at the
/// header's offsets and reads them back through a pointer — the real path — and
/// <see cref="TheMirrorsMarshalledLayoutIsTheHeadersTable"/> holds the other
/// answer to the same table.
/// </para>
/// </summary>
public class GlobalVarsLayoutParityTests
{
    /// <summary>One row of globalvars_layout.h's table.</summary>
    private readonly record struct Row(string Name, int Offset, int Size, string Reach);

    /// <summary>
    /// The mirror's fields are the header's rows, in the header's order.
    ///
    /// <para>
    /// The check the offset comparisons cannot make. Offsets are compared per
    /// NAME, so a field the mirror never declared is simply not looked at — and
    /// a missing field in the middle shifts every field after it, which is the
    /// exact failure the private tail invites (leave out <c>m_bClient</c> and
    /// its two neighbours and <c>mapname</c> moves from 72 to 56).
    /// </para>
    /// </summary>
    [Fact]
    public void TheMirrorHasExactlyTheHeadersFieldsInOrder()
    {
        List<Row>? rows = HeaderRows();
        if (rows is null)
            return;

        string[] header = rows.Select(r => r.Name).ToArray();
        string[] mirror = MirrorFields().Select(f => f.Name).ToArray();

        Assert.True(header.SequenceEqual(mirror),
            $"CGlobalVarsRaw declares the header's fields in the header's order "
            + $"(header {Show(header)}, mirror {Show(mirror)})");
    }

    /// <summary>
    /// The runtime layout — what a pointer read actually reads — is the header's.
    ///
    /// <para>
    /// This is the load-bearing one, and it is a measurement rather than a
    /// description. A buffer is filled with bytes that differ everywhere, each
    /// field's expected value is cut out of that buffer at the offset the C++
    /// compiler asserted, and the whole buffer is then read as a
    /// <see cref="CGlobalVarsRaw"/> through a pointer — the same operation
    /// <see cref="Globals"/> performs against engine memory. A field one byte out
    /// of place reads bytes that belong to its neighbour and no longer matches.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void TheMirrorsRuntimeLayoutIsTheHeadersTable()
    {
        List<Row>? rows = HeaderRows();
        if (rows is null)
            return;

        int size = HeaderSizeOf()!.Value;
        byte[] memory = PatternedBytes(size);

        fixed (byte* raw = memory)
        {
            CGlobalVarsRaw read = *(CGlobalVarsRaw*)raw;

            foreach (Row row in rows)
            {
                ulong expected = LittleEndian(memory, row.Offset, row.Size);
                ulong actual = RawBits(MirrorField(row.Name).GetValue(read)!);

                Assert.True(expected == actual,
                    $"CGlobalVarsRaw.{row.Name} reads the {row.Size} bytes at offset {row.Offset} "
                    + $"(wanted 0x{expected:X}, read 0x{actual:X})");
            }
        }
    }

    /// <summary>
    /// The marshalled layout is the header's too, which is how a stray
    /// <c>bool</c> is caught.
    ///
    /// <para>
    /// <see cref="Marshal.OffsetOf"/> describes the struct as the interop
    /// marshaller sees it. Declaring <c>bMapLoadFailed</c> as C# <c>bool</c>
    /// rather than <c>byte</c> widens it to four bytes here and to one byte in
    /// the runtime layout, so the mirror stops having a single answer to "where
    /// is <c>maxEntities</c>" — and the runtime layout, which the game uses, may
    /// still be right, so nothing else would report it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMirrorsMarshalledLayoutIsTheHeadersTable()
    {
        List<Row>? rows = HeaderRows();
        if (rows is null)
            return;

        foreach (Row row in rows)
        {
            int actual = (int)Marshal.OffsetOf<CGlobalVarsRaw>(row.Name);

            Assert.True(row.Offset == actual,
                $"CGlobalVarsRaw.{row.Name} marshals at the header's offset {row.Offset}, not {actual}");
        }
    }

    /// <summary>
    /// Each field is the width the header says.
    ///
    /// <para>
    /// Offsets alone do not pin a width. A four-byte field declared as
    /// <see cref="long"/> would still start where the header says and would read
    /// its neighbour along with itself, and — the case that matters here — a
    /// <c>string_t</c> mirrored as <see cref="int"/> rather than
    /// <see cref="nint"/> is right about <c>mapname</c>'s offset and reads half
    /// of a pointer.
    /// </para>
    /// </summary>
    [Fact]
    public void EachMirroredFieldIsTheHeadersWidth()
    {
        List<Row>? rows = HeaderRows();
        if (rows is null)
            return;

        foreach (Row row in rows)
        {
            int actual = Marshal.SizeOf(MirrorField(row.Name).FieldType);

            Assert.True(row.Size == actual,
                $"CGlobalVarsRaw.{row.Name} is the header's {row.Size} bytes, not {actual}");
        }
    }

    /// <summary>
    /// The whole struct is the size the header says, in both layouts.
    ///
    /// <para>
    /// Per-field offsets can every one of them agree while a trailing field is
    /// missing from the mirror entirely — <c>serverCount</c> could be dropped and
    /// nothing above would notice. And the two sizes agreeing with each other is
    /// the statement that the mirror is blittable: that the pointer read and the
    /// marshaller describe one struct rather than two.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMirrorIsTheHeadersSizeInBothLayouts()
    {
        int? size = HeaderSizeOf();
        if (size is null)
            return;

        Assert.True(Unsafe.SizeOf<CGlobalVarsRaw>() == size.Value,
            $"CGlobalVarsRaw's runtime size is the header's {size.Value}, not {Unsafe.SizeOf<CGlobalVarsRaw>()}");

        Assert.True(Marshal.SizeOf<CGlobalVarsRaw>() == size.Value,
            $"CGlobalVarsRaw's marshalled size is the header's {size.Value}, not {Marshal.SizeOf<CGlobalVarsRaw>()}");

        Assert.True(!RuntimeHelpers.IsReferenceOrContainsReferences<CGlobalVarsRaw>(),
            "CGlobalVarsRaw holds no managed references, so a pointer to engine memory can be read as one");
    }

    /// <summary>
    /// The mirror describes x64, asserted rather than assumed.
    ///
    /// <para>
    /// <c>pSaveData</c> is a pointer, so every offset after it — which is the
    /// whole of <c>CGlobalVars</c>'s own half — depends on the pointer width.
    /// globalvars_layout.h refuses to compile at all when
    /// <c>sizeof(void *) != 8</c>; this is the other half of that, and it is
    /// worth its own fact because a 32-bit managed host would read the table's
    /// numbers happily and be wrong from offset 48 on.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMirrorIsX64Only()
    {
        Assert.True(IntPtr.Size == 8,
            $"the managed host is 64-bit, which every offset from pSaveData on assumes (pointers are {IntPtr.Size} bytes)");

        string? text = HeaderText();
        if (text is null)
            return;

        Assert.Matches(new Regex(@"static_assert\(\s*sizeof\(\s*void\s*\*\s*\)\s*==\s*8"), text);
    }

    /// <summary>
    /// <see cref="Globals"/> reads the live struct rather than a copy of it.
    ///
    /// <para>
    /// The one behavioural claim the type makes. A <c>Globals</c> is built once
    /// and the memory behind it is then changed, exactly as the engine changes it
    /// every frame; the properties have to report the new values. An
    /// implementation that copied the struct into itself would pass every layout
    /// fact above and fail here, and in the game it would be one frame stale
    /// everywhere with nothing to show for it.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void GlobalsReadsTheLiveStructRatherThanACopy()
    {
        CGlobalVarsRaw live = default;
        live.curtime = 1.0f;
        live.tickcount = 66;

        var globals = new Globals(&live);

        Assert.True(globals.CurTime == 1.0f, "a fresh Globals reads the value the struct holds");

        live.curtime = 2.5f;
        live.frametime = 0.015f;
        live.tickcount = 67;
        live.maxClients = 24;

        Assert.True(globals.CurTime == 2.5f, "CurTime follows the engine's struct after it changes");
        Assert.True(globals.FrameTime == 0.015f, "FrameTime follows the engine's struct after it changes");
        Assert.True(globals.TickCount == 67, "TickCount follows the engine's struct after it changes");
        Assert.True(globals.MaxClients == 24, "MaxClients follows the engine's struct after it changes");
    }

    /// <summary>
    /// The engine's bytes become <c>bool</c> at the boundary and nowhere earlier.
    ///
    /// <para>
    /// The mirror holds these as <see cref="byte"/> so the struct stays
    /// blittable; the conversion belongs here, and it is <c>!= 0</c> rather than
    /// <c>== 1</c> because a C++ <c>bool</c> written through a cast can hold any
    /// non-zero byte and <c>if (gpGlobals-&gt;coop)</c> takes all of them.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void GlobalsTurnsTheEnginesBytesIntoBools()
    {
        CGlobalVarsRaw live = default;
        var globals = new Globals(&live);

        Assert.True(!globals.Deathmatch && !globals.Coop && !globals.Teamplay && !globals.MapLoadFailed && !globals.IsClient,
            "a zeroed struct reads as false everywhere");

        live.deathmatch = 1;
        live.coop = 2;          // not 1, and still true
        live.teamplay = 0xFF;
        live.bMapLoadFailed = 1;
        live.m_bClient = 1;

        Assert.True(globals.Deathmatch, "deathmatch is true for a 1 byte");
        Assert.True(globals.Coop, "coop is true for any non-zero byte, not only 1");
        Assert.True(globals.Teamplay, "teamplay is true for a 0xFF byte");
        Assert.True(globals.MapLoadFailed, "bMapLoadFailed is true for a 1 byte");
        Assert.True(globals.IsClient, "m_bClient is true for a 1 byte");
    }

    /// <summary>
    /// The four consecutive bools do not run into <c>maxEntities</c>.
    ///
    /// <para>
    /// The narrowest version of the whole file: four one-byte fields in a row
    /// followed by an <c>int</c> is precisely where a widened C# <c>bool</c> puts
    /// <c>maxEntities</c> twelve bytes late and hands the game a max entity count
    /// read out of the middle of nothing.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void TheFourBoolsDoNotSpillIntoMaxEntities()
    {
        CGlobalVarsRaw live = default;
        live.bMapLoadFailed = 0xFF;
        live.deathmatch = 0xFF;
        live.coop = 0xFF;
        live.teamplay = 0xFF;
        live.maxEntities = 2048;
        live.serverCount = 7;

        var globals = new Globals(&live);

        Assert.True(globals.MaxEntities == 2048, "maxEntities survives four filled bool bytes in front of it");
        Assert.True(globals.ServerCount == 7, "serverCount survives them too");
    }

    /// <summary>
    /// <see cref="Globals"/> does not expose <c>mapname</c> or <c>startspot</c>.
    ///
    /// <para>
    /// They are <c>string_t</c>: in this tree a pointer with an engine lifetime,
    /// and in other builds of Source a pooled handle that is a number. Either way
    /// what a caller wants from them is a string, and neither form is one —
    /// putting a <c>MapName</c> on this type would hand managed code something
    /// that reads like a string and is a raw address. The map name comes through
    /// MapLoad, which already carries it.
    /// </para>
    ///
    /// <para>
    /// Written as a check on the members rather than as a comment, because the
    /// tempting fix when someone wants the map name is to add exactly the
    /// property this argues against, and a comment does not fail.
    /// </para>
    /// </summary>
    [Fact]
    public void GlobalsDoesNotExposeTheStringHandles()
    {
        PropertyInfo[] properties = typeof(Globals)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (PropertyInfo property in properties)
        {
            Assert.True(!property.Name.Contains("Name", StringComparison.OrdinalIgnoreCase),
                $"Globals.{property.Name} does not exist -- mapname is a string_t, an engine pointer rather than a string");

            Assert.True(!property.Name.Contains("StartSpot", StringComparison.OrdinalIgnoreCase),
                $"Globals.{property.Name} does not exist -- startspot is a string_t");
        }

        Assert.True(!properties.Any(p => p.PropertyType == typeof(string)),
            "no property on Globals is a string: nothing in CGlobalVars is one");

        // And the mirror still carries both handles, because leaving them out
        // would move every field after them rather than merely hiding them.
        Assert.True(MirrorFields().Any(f => f.Name == "mapname") && MirrorFields().Any(f => f.Name == "startspot"),
            "CGlobalVarsRaw still mirrors mapname and startspot: they hold sixteen bytes of the layout");
    }

    // ---- the header, read as text ------------------------------------------

    /// <summary>
    /// The rows of <c>SOURCESHARP_GLOBALVARS_LAYOUT</c>, in declaration order.
    /// Null when the source tree is not next to the test binary, matching
    /// <see cref="AbiParityTests"/>: the suite still has to run from a directory
    /// with no checkout in it.
    /// </summary>
    private static List<Row>? HeaderRows()
    {
        string? text = HeaderText();
        if (text is null)
            return null;

        // Only the table's own body: the macro is expanded further down the file
        // with a different F, and matching the whole file would read the
        // expansion site as rows.
        var table = Regex.Match(text, @"#define\s+SOURCESHARP_GLOBALVARS_LAYOUT\((.*?)^\s*$", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(table.Success, "globalvars_layout.h declares SOURCESHARP_GLOBALVARS_LAYOUT");

        var rows = new List<Row>();

        foreach (Match m in Regex.Matches(table.Value, @"F\(\s*(\w+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(PUBLIC|PRIVATE)\s*\)"))
            rows.Add(new Row(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value));

        Assert.True(rows.Count > 0, "the layout table in globalvars_layout.h parsed into at least one row");

        // The private tail is the part a mirror written from the public fields
        // alone leaves out, so its presence in the table is worth asserting
        // rather than assuming: without these rows every check above passes on a
        // mirror that is sixteen bytes short from offset 56 on.
        Assert.True(rows.Any(r => r.Reach == "PRIVATE"),
            "the table carries CGlobalVarsBase's private tail, which is part of the layout");

        return rows;
    }

    /// <summary>The struct's total size, per the header.</summary>
    private static int? HeaderSizeOf()
    {
        string? text = HeaderText();
        if (text is null)
            return null;

        var m = Regex.Match(text, @"#define\s+SOURCESHARP_GLOBALVARS_SIZEOF\s+(\d+)");
        Assert.True(m.Success, "globalvars_layout.h declares SOURCESHARP_GLOBALVARS_SIZEOF");

        return int.Parse(m.Groups[1].Value);
    }

    private static string? HeaderText()
    {
        string? path = FindHeader();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>The layout header in the compat farm.</summary>
    private static string? FindHeader()
    {
        string relative = Path.Combine("src", "sourcesharp", "game", "shared", "globalvars_layout.h");

        return RepoLayout.FindSdkFile(relative);
    }

    // ---- the mirror, read by reflection ------------------------------------

    private static FieldInfo[] MirrorFields() =>
        typeof(CGlobalVarsRaw).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private static FieldInfo MirrorField(string name)
    {
        FieldInfo? field = typeof(CGlobalVarsRaw).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.True(field is not null, $"CGlobalVarsRaw declares the header's field {name}");

        return field!;
    }

    /// <summary>A field's value as its raw bits, whatever its type.</summary>
    private static ulong RawBits(object value) => value switch
    {
        float f => BitConverter.SingleToUInt32Bits(f),
        int i => (uint)i,
        byte b => b,
        nint n => (ulong)(long)n,
        _ => throw new InvalidOperationException(
            $"CGlobalVarsRaw holds a {value.GetType()}, which this test does not know how to compare. "
            + "Every field of CGlobalVars is a float, an int, a bool or a pointer."),
    };

    /// <summary><paramref name="size"/> bytes at <paramref name="offset"/>, little-endian.</summary>
    private static ulong LittleEndian(byte[] memory, int offset, int size)
    {
        ulong value = 0;

        for (int i = size - 1; i >= 0; i--)
            value = (value << 8) | memory[offset + i];

        return value;
    }

    /// <summary>
    /// Bytes that differ everywhere, so a field read one byte out of place reads
    /// a different number. Deliberately not random: a failure has to reproduce.
    /// The multiplier keeps every four-byte window distinct and keeps the float
    /// fields out of NaN territory, which matters because two NaNs never compare
    /// equal and the comparison here is on bits rather than on values anyway.
    /// </summary>
    private static byte[] PatternedBytes(int size)
    {
        var memory = new byte[size];

        for (int i = 0; i < size; i++)
            memory[i] = (byte)(i * 7 + 1);

        return memory;
    }

    private static string Show(IEnumerable<string> names) => "{" + string.Join(", ", names) + "}";
}
