using SourceSharp.Shared;
using System.Text;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Writing an edited entity list back into a map.
///
/// This is the only thing in the development API that modifies something a build
/// produced, so it is the thing most worth being sure about. Two properties
/// matter above all: what comes back out is what went in, and nothing except the
/// entity lump is disturbed.
/// </summary>
public class BspWriteTests
{

    [Fact]
    public void Serialising()
    {
        const string lump = """
            {
            "classname" "worldspawn"
            "skyname" "sky_day01_01"
            }
            {
            "classname" "logic_relay"
            "targetname" "relay"
            "origin" "-64 128 32.5"
            "StartDisabled" "0"
            "OnTrigger" "door,Open,,0.5,-1"
            "OnTrigger" "light,TurnOn,,0,1"
            }
            """;

        var original = BspFile.ParseEntities(lump);
        var round = BspFile.ParseEntities(BspWriter.Serialize(original));

        Assert.True(round.Count == original.Count, "every entity survives a round trip");
        Assert.True(round[0].ClassName == "worldspawn", "the world survives");

        var relay = round[1];

        Assert.True(relay.ClassName == EntityNames.LogicRelay, "the classname survives");
        Assert.True(relay.TargetName == "relay", "the targetname survives");
        Assert.True(relay.Origin[0] == -64f && relay.Origin[2] == 32.5f, "the origin survives");
        Assert.True(relay.KeyValues.Any(kv => kv.Key == "StartDisabled" && kv.Value == "0"), "an ordinary key survives");

        Assert.True(relay.Connections.Count == 2, "both connections survive");
        Assert.True(relay.Connections[0].Target == "door" && relay.Connections[0].Input == "Open"
            && Math.Abs(relay.Connections[0].Delay - 0.5f) < 0.001f, "a connection survives whole");
        Assert.True(relay.Connections[1].TimesToFire == 1, "the second connection's repeat count survives");

        // The connections came in as keyvalues AND as a parsed list. Writing
        // both would give the entity every output twice, so each would fire
        // twice — which is exactly the sort of bug a map author would blame on
        // their own logic.
        int written = BspWriter.Serialize(original).Split("\"OnTrigger\"").Length - 1;
        Assert.True(written == 2, "a connection is written once, not twice");

        // An edit adds to the parsed list and not to the keyvalues, so the two
        // disagree by design; the parsed list is the one that counts.
        var edited = original[1] with
        {
            Connections = original[1].Connections
                .Append(new MapConnection("OnTrigger", "new", "Kill", "", 0f, -1)).ToArray(),
        };

        var afterEdit = BspFile.ParseEntities(BspWriter.Serialize([original[0], edited]))[1];
        Assert.True(afterEdit.Connections.Count == 3, "an added connection is written");
        Assert.True(afterEdit.Connections.Any(c => c.Target == "door"), "and the original ones are still there");

        // The lump format has no escape for a quote, so one inside a value would
        // end the token early and shift every key after it.
        var risky = BspFile.ParseEntities(BspWriter.Serialize([
            original[0],
            new MapEntity(1, EntityNames.InfoTarget, "a", [0, 0, 0], [0, 0, 0], "",
                [new MapKeyValue("classname", EntityNames.InfoTarget),
                 new MapKeyValue("message", "he said \"hi\"")],
                []),
        ]));

        Assert.True(risky.Count == 2 && risky[1].KeyValues.Any(kv => kv.Key == "message"), "a quote in a value cannot break the lump");

        Assert.True(BspWriter.Serialize(original).EndsWith('\0'), "the lump is null-terminated");
    }

    [Fact]
    public void Writing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ss-bsp-" + Environment.ProcessId);
        Directory.CreateDirectory(directory);

        try
        {
            string source = Path.Combine(directory, "fixture.bsp");
            var geometry = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            File.WriteAllBytes(source, Fixture("""
                {
                "classname" "worldspawn"
                }
                """, geometry));

            var entities = BspFile.ParseEntities("""
                {
                "classname" "worldspawn"
                }
                {
                "classname" "logic_relay"
                "targetname" "added"
                }
                """);

            string destination = Path.Combine(directory, "written.bsp");
            var report = BspWriter.Write(source, destination, entities);

            Assert.True(report.EntityCount == 2 && report.Bytes > 0, "the write reports what it wrote");
            Assert.True(report.BackupPath is null, "writing elsewhere takes no backup");

            var back = BspFile.Read(destination, withGeometry: false);

            Assert.True(back.Entities.Count == 2, "the new entity list is read back");
            Assert.True(back.Entities[1].TargetName == "added", "the added entity is there");

            // The whole point of appending the lump rather than re-laying the
            // file out: every other lump is exactly where it was, byte for byte.
            var written = File.ReadAllBytes(destination);
            var lump3 = ReadLump(written, 3);

            Assert.True(lump3.SequenceEqual(geometry), "another lump is untouched");

            // The original must not be disturbed by writing somewhere else.
            Assert.True(File.ReadAllBytes(source).SequenceEqual(Fixture("""
                    {
                    "classname" "worldspawn"
                    }
                    """, geometry)), "the source file is unchanged");

            // Overwriting keeps a numbered copy in a subdirectory, so the second
            // save cannot destroy the last copy of the original.
            File.Copy(destination, Path.Combine(directory, "over.bsp"));

            var first = BspWriter.Write(source, Path.Combine(directory, "over.bsp"), entities);
            var second = BspWriter.Write(source, Path.Combine(directory, "over.bsp"), entities);

            Assert.True(first.BackupPath is not null, "overwriting backs the file up");
            Assert.True(first.BackupPath!.Contains(BspWriter.BackupDirectory), "the backup goes in its own directory");
            Assert.True(second.BackupPath != first.BackupPath && File.Exists(first.BackupPath), "a second save does not overwrite the first backup");
            Assert.True(first.BackupPath.Contains("0001") && second.BackupPath!.Contains("0002"), "backups are numbered in order");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Refusals()
    {
        var world = BspFile.ParseEntities("{\n\"classname\" \"worldspawn\"\n}");
        string path = Path.Combine(Path.GetTempPath(), "ss-refuse.bsp");

        Assert.True(Throws(() => BspWriter.Write(path, path, [])), "an empty list is refused");

        // A map with no worldspawn will not load at all, and building a filtered
        // list that leaves it out is an easy mistake to make.
        Assert.True(Throws(() => BspWriter.Write(path, path,
                BspFile.ParseEntities("{\n\"classname\" \"logic_relay\"\n}"))), "a list with no worldspawn is refused");

        Assert.True(Throws(() =>
        {
            File.WriteAllText(path, "not a bsp at all, not even close");
            BspWriter.Write(path, path, world);
        }), "a file that is not a BSP is refused");

        try { File.Delete(path); } catch { }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; }
    }

    /// <summary>A BSP with just enough in it to be read and written.</summary>
    private static byte[] Fixture(string entityLump, byte[] geometry)
    {
        var entities = Encoding.UTF8.GetBytes(entityLump + "\0");
        int header = 8 + 64 * 16;

        var file = new byte[header + entities.Length + geometry.Length];

        Encoding.ASCII.GetBytes("VBSP").CopyTo(file, 0);
        BitConverter.GetBytes(19).CopyTo(file, 4);

        BitConverter.GetBytes(header).CopyTo(file, 8);
        BitConverter.GetBytes(entities.Length).CopyTo(file, 12);
        entities.CopyTo(file, header);

        int at = 8 + 3 * 16;
        BitConverter.GetBytes(header + entities.Length).CopyTo(file, at);
        BitConverter.GetBytes(geometry.Length).CopyTo(file, at + 4);
        geometry.CopyTo(file, header + entities.Length);

        return file;
    }

    private static byte[] ReadLump(byte[] file, int lump)
    {
        int at = 8 + lump * 16;
        int offset = BitConverter.ToInt32(file, at);
        int length = BitConverter.ToInt32(file, at + 4);

        return file.Skip(offset).Take(length).ToArray();
    }
}
