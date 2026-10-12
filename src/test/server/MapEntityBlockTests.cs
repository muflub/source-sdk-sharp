//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed half of the map-entity-block agreement.
///
/// <para>
/// <c>sourcesharp/test/map_entity_block_cases.txt</c> records what the real
/// <c>game/shared/mapentities_shared.cpp</c> answers for thirty-one blocks, and
/// <c>sourcesharp/test/map_entity_block_test.cpp</c> keeps that record true of
/// native by recomputing every row with the compiled-in original. This asserts
/// <see cref="MapEntityBlock"/> reproduces the same table.
/// </para>
///
/// <para>
/// The rows are not decoration. A tokeniser is the sort of thing a port gets
/// 95% right and then disagrees with on a value containing a brace, an
/// unterminated quote, or a key with a trailing space — and every one of those
/// reaches this code from map text a mapper wrote, not from anything a test
/// would think to invent.
/// </para>
/// </summary>
public class MapEntityBlockTests
{
    private const char Esc = '\x1b';

    private sealed record Row(string Kind, string Name, string[] Field, int Line);

    /// <summary>
    /// The table is not a fixture this file maintains — it is a record of
    /// native, found on disk. Not finding it must be a FAILURE and not an empty
    /// pass: a loop over nothing is green, and that is the exact shape of check
    /// this tree keeps finding.
    /// </summary>
    private static List<Row> LoadRows()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("sourcesharp", "test",
                                                 "map_entity_block_cases.txt"));
        Assert.True(path is not null, "the golden table is found next to the tests");

        var rows = new List<Row>();
        string[] lines = File.ReadAllLines(path!);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] parts = line.Split('\t');
            if (parts.Length < 2)
                continue;

            var fields = new string[parts.Length - 1];
            for (int f = 1; f < parts.Length; f++)
                fields[f - 1] = Unescape(parts[f]);

            rows.Add(new Row(parts[0], fields[0], fields, i + 1));
        }

        return rows;
    }

    /// <summary>The escape scheme the gtest writes and reads. Kept identical on
    /// purpose: two spellings of one table is two tables.</summary>
    private static string Unescape(string input)
    {
        var sb = new StringBuilder(input.Length);

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != '\\' || i + 1 >= input.Length)
            {
                sb.Append(input[i]);
                continue;
            }

            sb.Append(input[++i] switch
            {
                'n' => '\n',
                't' => '\t',
                'q' => '"',
                'e' => Esc,
                's' => '~',
                '\\' => '\\',
                var other => other,
            });
        }

        return sb.ToString();
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A field the row may not carry, because a trailing empty one is
    /// lost to the tab-stripping every editor does. The gtest has the same
    /// accessor for the same reason.</summary>
    private static string At(Row row, int index) =>
        index < row.Field.Length ? row.Field[index] : string.Empty;

    private static List<Row> Of(string kind) => LoadRows().FindAll(r => r.Kind == kind);

    // ------------------------------------------------------------------------

    [Fact]
    public void TheGoldenTableIsThereAndHasEveryRow()
    {
        var rows = LoadRows();

        Assert.True(rows.Count == 44,
            "the table has the forty-four blocks both sides were written against");
    }

    [Fact]
    public void EveryTokenRowComesOutOfTheManagedTokeniser()
    {
        var rows = Of("tokens");
        Assert.True(rows.Count > 0, "there are token rows to check against");

        foreach (Row row in rows)
        {
            byte[] data = Bytes(At(row, 1));
            var stream = new List<string>();

            int at = 0;
            int guard = 0;
            while (at >= 0 && guard++ < 512)
            {
                int next = MapEntityBlock.ParseToken(data, at, out string token);
                if (next < 0 && token.Length == 0)
                    break;

                stream.Add(token);
                at = next;
            }

            Assert.True(string.Join('~', stream) == At(row, 2),
                $"'{row.Name}' tokenises the way native does");
        }
    }

    [Fact]
    public void EveryKeyCountRowComesOutOfTheManagedCounter()
    {
        var rows = Of("keys");
        Assert.True(rows.Count > 0, "there are key-count rows to check against");

        foreach (Row row in rows)
        {
            Assert.True(MapEntityBlock.CountKeys(Bytes(At(row, 1))) == int.Parse(At(row, 2)),
                $"'{row.Name}' counts the keys native counts");
        }
    }

    [Fact]
    public void EveryExtractRowComesOutOfTheManagedExtractor()
    {
        var rows = Of("extract");
        Assert.True(rows.Count > 0, "there are extraction rows to check against");

        foreach (Row row in rows)
        {
            bool found = MapEntityBlock.ExtractValue(Bytes(At(row, 1)), At(row, 2),
                                                     out string value);

            Assert.True(found == (At(row, 3) == "1"),
                $"'{row.Name}' finds the key exactly when native does");

            if (found)
            {
                Assert.True(value == At(row, 4),
                    $"'{row.Name}' reads native's value");
            }
        }
    }

    [Fact]
    public void EveryWalkRowComesOutOfTheManagedCursor()
    {
        var rows = Of("walk");
        Assert.True(rows.Count > 0, "there are cursor rows to check against");

        foreach (Row row in rows)
        {
            byte[] data = Bytes(At(row, 1));
            var block = new MapEntityBlock(data, new RecordingLog(), data.Length + 1);
            var stream = new List<string>();

            if (block.GetFirstKey(out string key, out string value))
            {
                do
                {
                    stream.Add($"{key}={value}");
                }
                while (block.GetNextKey(out key, out value));
            }

            Assert.True(string.Join('~', stream) == At(row, 2),
                $"'{row.Name}' walks the keys native walks");
        }
    }

    /// <summary>
    /// The rewrite rows, and the reason they carry TWO expected values.
    ///
    /// <para>
    /// The reconnect pass rewrites a value from inside its own walk, so
    /// <c>SetValue</c> has to leave the cursor pointing at the same place
    /// relative to the text it just changed the length of. A port that rebuilt
    /// the buffer and left the cursor alone produces the right TEXT and a walk
    /// that repeats or skips a key — so the row records what the walk still
    /// produces afterwards as well as the text.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRewriteRowComesOutOfTheManagedSetValue()
    {
        var rows = Of("walkset");
        Assert.True(rows.Count > 0, "there are rewrite rows to check against");

        foreach (Row row in rows)
        {
            int capacity = int.Parse(At(row, 2));
            string atKey = At(row, 3);
            string newValue = At(row, 4);

            var storage = new byte[capacity];
            byte[] text = Bytes(At(row, 1));
            Array.Copy(text, storage, Math.Min(text.Length, capacity - 1));

            var block = new MapEntityBlock(storage, new RecordingLog(), capacity);
            var rest = new List<string>();
            int instances = 0;

            if (block.GetFirstKey(out string key, out string value))
            {
                do
                {
                    if (atKey == key)
                    {
                        block.SetValue(key, newValue, instances);
                        instances++;
                        continue;
                    }

                    rest.Add($"{key}={value}");
                }
                while (block.GetNextKey(out key, out value));
            }

            Assert.True(string.Join('~', rest) == At(row, 5),
                $"'{row.Name}' leaves the walk where native leaves it — the " +
                "cursor adjustment is what the reconnect pass rides on");

            Assert.True(block.Text == At(row, 6),
                $"'{row.Name}' rewrites the text the way native does");
        }
    }

    /// <summary>
    /// A block built without a capacity refuses to be modified.
    ///
    /// Stock's <c>m_nEntDataSize == -1</c> guard, and the reason is stock's: the
    /// buffer belongs to somebody else and there is no room to grow into. This
    /// is not in the table because native ASSERTS on it, which a gtest over the
    /// real code cannot survive.
    /// </summary>
    [Fact]
    public void ABlockWithNoCapacityRefusesToBeRewritten()
    {
        byte[] data = Bytes("\"targetname\" \"alpha\"\n}\n");
        var block = new MapEntityBlock(data, new RecordingLog());

        Assert.False(block.SetValue("targetname", "beta"),
            "a block whose size is unknown cannot be safely modified");
        Assert.True(block.Text == "\"targetname\" \"alpha\"\n}\n",
            "and it is left exactly as it was");
    }

    /// <summary>
    /// And a rewrite that would not fit is refused rather than written past the
    /// end.
    ///
    /// <para>
    /// This is the ONE place the port does not reproduce stock: stock's
    /// <c>Q_strcat</c> was handed <c>m_nEntDataSize - (prevData - m_pEntData) + 1</c>
    /// as its limit and the store was expected to have reserved the room. C#
    /// has no way to write past the end and no reason to want one, so the
    /// answer is a refusal. The store sizes every template so this cannot
    /// happen; the row exists so that if it ever does, it is visible.
    /// </para>
    /// </summary>
    [Fact]
    public void ARewriteThatWouldNotFitIsRefused()
    {
        byte[] text = Bytes("\"targetname\" \"a\"\n}\n");
        var storage = new byte[text.Length + 1];
        Array.Copy(text, storage, text.Length);

        var block = new MapEntityBlock(storage, new RecordingLog(), storage.Length);

        Assert.False(block.SetValue("targetname", "a_much_longer_name"),
            "a replacement with no room for it is refused");
        Assert.True(block.Text == "\"targetname\" \"a\"\n}\n",
            "and the block is left exactly as it was");
    }

    //-------------------------------------------------------------------------
    // THE BLOCK WALK, WHICH IS THE SPAWN ORDER -- plan_entity.md step 2d.
    //
    // MapEntity_ParseAllEntities' own loop, and the order it produced is the
    // order every entity in every map was made in. MapEntityBlock.SplitBlocks is
    // that loop's arithmetic now, and the `spawnorder` rows of the golden table
    // are what stop it from quietly meaning something else -- every one of them
    // was recomputed by the REAL parser in map_entity_block_test.cpp.
    //-------------------------------------------------------------------------

    [Fact]
    public void EverySpawnOrderRowComesOutOfTheManagedSplitter()
    {
        var rows = Of("spawnorder");
        Assert.True(rows.Count > 0, "there are spawn-order rows to check against");

        foreach (Row row in rows)
        {
            Assert.True(SpawnOrder(At(row, 1)) == At(row, 2),
                $"'{row.Name}' splits the way native's parse loop does");
        }
    }

    /// <summary>
    /// <b>The row set is a set of DIFFERENT answers, not thirteen spellings of
    /// one.</b>
    ///
    /// <para>
    /// A table whose rows all expected the same thing would pass a splitter that
    /// ignored its input, and the fact above could not tell. This is what says
    /// the rows discriminate: the thirteen produce at least six distinct answers,
    /// and among them the empty one, the single-block one and the multi-block
    /// ones.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSpawnOrderRowsDoNotAllExpectTheSameThing()
    {
        var answers = new HashSet<string>();

        foreach (Row row in Of("spawnorder"))
            answers.Add(At(row, 2));

        Assert.True(answers.Count >= 6,
            $"the rows discriminate between outcomes; got {answers.Count} distinct answers");
    }

    /// <summary>
    /// <b>The fixture would catch a REORDERING and not merely record one.</b>
    ///
    /// <para>
    /// This is the check the gate asks for by name. A golden table can be green
    /// against a splitter that returns blocks in any order it likes, as long as
    /// the table was emitted from the same splitter — so the proof is not that
    /// the rows agree, it is that a row STOPS agreeing when the order changes.
    /// The reversal below is what a lane that "tidied" the walk would produce,
    /// and <c>order-is-map-order</c> is the row that refuses it.
    /// </para>
    /// </summary>
    [Fact]
    public void AReversedSplitWouldFailTheOrderRow()
    {
        Row row = Of("spawnorder").Find(r => r.Name == "order-is-map-order")!;

        var blocks = MapEntityBlock.SplitBlocks(Bytes(At(row, 1)));
        blocks.Reverse();

        Assert.True(ClassNames(blocks) != At(row, 2),
            "a splitter that answered in reverse would be caught by this row, "
          + "which is what makes the row a check rather than a recording");
    }

    /// <summary>
    /// And a DROPPED block, which is the other way a split goes wrong quietly:
    /// the map loads, and one entity the mapper placed simply is not there.
    /// </summary>
    [Fact]
    public void ASplitThatLostABlockWouldFailTheOrderRow()
    {
        Row row = Of("spawnorder").Find(r => r.Name == "order-is-map-order")!;

        var blocks = MapEntityBlock.SplitBlocks(Bytes(At(row, 1)));
        blocks.RemoveAt(1);

        Assert.True(ClassNames(blocks) != At(row, 2),
            "a splitter that dropped the middle block would be caught by this row");
    }

    /// <summary>
    /// The block a template is made of does NOT carry the opening brace, which
    /// is what makes it re-parseable as an entity later — stock records from
    /// just past the `{` for the same reason.
    /// </summary>
    [Fact]
    public void ABlocksBytesStartAfterTheOpeningBraceAndEndOnTheClosingOne()
    {
        var blocks = MapEntityBlock.SplitBlocks(
            Bytes("{\n\"classname\" \"a\"\n}\n{\n\"classname\" \"b\"\n}\n"));

        Assert.True(blocks.Count == 2, "two blocks");

        string first = Encoding.UTF8.GetString(blocks[0].Bytes).TrimEnd('\0');

        Assert.True(!first.StartsWith('{') && first.EndsWith('}'),
            $"the brace the walk consumed is not in the block -- got: {first}");
    }

    /// <summary>The classnames of a split, the way the golden table spells
    /// them.</summary>
    private static string ClassNames(List<MapEntityBlock.Block> blocks)
    {
        var names = new List<string>();

        foreach (var block in blocks)
        {
            names.Add(MapEntityBlock.ExtractValue(block.Bytes, "classname", out string name)
                ? name
                : "?");
        }

        return string.Join('~', names);
    }

    private static string SpawnOrder(string lump)
        => ClassNames(MapEntityBlock.SplitBlocks(Bytes(lump)));
}
