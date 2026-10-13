using SourceSharp.Shared;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Reading a compiled map.
///
/// The entity lump is the half of the debugger that needs no game at all, which
/// makes it the half worth testing hardest: everything the connection graph
/// draws comes out of this parser, and an edge it invents or drops is a wrong
/// answer to the one question the graph exists to answer.
/// </summary>
public class BspTests
{
    /// <summary>The separator older maps join output fields with, instead of a comma.</summary>
    private const string Esc = "";

    [Fact]
    public void Entities()
    {
        const string lump = """
            {
            "classname" "worldspawn"
            "skyname" "sky_day01_01"
            }
            {
            "origin" "-64 128 32.5"
            "targetname" "door_one"
            "angles" "0 270 0"
            "model" "*12"
            "classname" "func_door"
            }
            """;

        var entities = BspFile.ParseEntities(lump);

        Assert.True(entities.Count == 2, "both entities are read");
        Assert.True(entities[0].ClassName == "worldspawn", "the first is the world");
        Assert.True(entities[0].KeyValues[0].Key == "classname", "keys keep the order the map wrote them");

        var door = entities[1];

        Assert.True(door.TargetName == "door_one", "a targetname is picked out");
        Assert.True(door.Origin[0] == -64f && door.Origin[2] == 32.5f, "an origin is parsed");
        Assert.True(door.Angles[1] == 270f, "angles are parsed");

        // A brush entity's geometry is inside the BSP as "*N" rather than a
        // file, and it is drawn as a volume rather than a point because of it.
        Assert.True(door.BrushModel == 12, "a brush model index is picked out");
        Assert.True(BspFile.ParseEntities("{\n\"model\" \"models/x.mdl\"\n}")[0].BrushModel == -1, "a studio model is not a brush model");

        // Ids are what the selection and the graph hold on to.
        Assert.True(entities[0].Id == 0 && entities[1].Id == 1, "entities are numbered in file order");

        Assert.True(BspFile.ParseEntities("").Count == 0, "an empty lump is no entities");

        // Real lumps end with a null and stray whitespace; neither is an entity.
        Assert.True(BspFile.ParseEntities("\n\n{\n\"classname\" \"a\"\n}\n\n\0").Count == 1, "junk between entities is skipped");
    }

    [Fact]
    public void Connections()
    {
        var relay = BspFile.ParseEntities("""
            {
            "targetname" "relay"
            "classname" "logic_relay"
            "OnTrigger" "door_one,Open,,0.5,-1"
            "OnTrigger" "light_one,TurnOn,,0,1"
            "OnUser1" "thing,Kill,,0,-1"
            "rendercolor" "255,255,255"
            "spawnflags" "0"
            }
            """)[0];

        Assert.True(relay.Connections.Count == 3, "every connection is found");

        var first = relay.Connections[0];

        Assert.True(first.Output == "OnTrigger", "a connection carries its output");
        Assert.True(first.Target == "door_one", "and its target");
        Assert.True(first.Input == "Open", "and its input");
        Assert.True(Math.Abs(first.Delay - 0.5f) < 0.001f, "and its delay");
        Assert.True(first.TimesToFire == -1, "and how many times it may fire");
        Assert.True(relay.Connections[1].TimesToFire == 1, "a repeat count of one is kept");

        // Two connections from the same output is ordinary, and the reason a
        // connection cannot be keyed by its output name.
        Assert.True(relay.Connections.Count(c => c.Output == "OnTrigger") == 2, "one output may connect twice");

        Assert.True(!relay.Connections.Any(c => c.Output == "rendercolor"), "an ordinary comma value is not a connection");

        // Shape, not name: math_counter's OutValue is a real output that no
        // "starts with On" rule would ever find.
        var counter = BspFile.ParseEntities("""
            {
            "classname" "math_counter"
            "OutValue" "spawner,SetMaxLiveChildren,,0,-1"
            }
            """)[0];

        Assert.True(counter.Connections.Count == 1 && counter.Connections[0].Input == "SetMaxLiveChildren", "an output that does not start with On is still found");

        // Five comma-separated numbers look exactly like a connection until the
        // second field is read, and an edge drawn for one would be invented.
        var colours = BspFile.ParseEntities("""
            {
            "classname" "light"
            "_light" "255,255,255,0,1"
            }
            """)[0];

        Assert.True(colours.Connections.Count == 0, "five numbers are not mistaken for a connection");

        // Older maps join with an escape character instead of a comma, and a map
        // using the other one would otherwise show no connections at all.
        var escaped = BspFile.ParseEntities(
            "{\n\"classname\" \"logic_relay\"\n\"OnTrigger\" \"door" + Esc + "Open" + Esc + Esc
            + "0" + Esc + "-1\"\n}")[0];

        Assert.True(escaped.Connections.Count == 1 && escaped.Connections[0].Target == "door", "the escape separator is understood");

        // A parameter can contain a comma, which pushes the field count past
        // five without changing what the connection means.
        var withComma = BspFile.ParseEntities("""
            {
            "classname" "logic_relay"
            "OnTrigger" "thing,AddOutput,a,b,0,-1"
            }
            """)[0];

        Assert.True(withComma.Connections.Count == 1 && withComma.Connections[0].Input == "AddOutput", "a parameter containing a comma still parses");
    }

    [Fact]
    public void Vectors()
    {
        Assert.True(BspFile.ParseVector("1 2 3")[1] == 2f, "a vector parses");
        Assert.True(BspFile.ParseVector("-4098.2 4539.7 119")[0] == -4098.2f, "negatives and decimals parse");

        // Invariant culture throughout: a decimal comma here would read every
        // position in the map as zero.
        Assert.True(BspFile.ParseVector("5")[2] == 0f, "a short vector is zero-filled rather than throwing");

        Assert.True(BspFile.ParseVector("x y z")[0] == 0f, "nonsense reads as zero");
        Assert.True(BspFile.ParseVector("  7   8   9 ")[2] == 9f, "extra spacing is tolerated");
    }

    [Fact]
    public void MapNames()
    {
        var source = new MapSource("/nowhere");

        Assert.True(source.PathFor("dm_lockdown") == Path.Combine("/nowhere", "dm_lockdown.bsp"), "a map name is joined onto the directory");

        // A map name arrives from a query string, and joining one onto a
        // directory is the classic way to end up reading a file elsewhere.
        foreach (string escape in new[] { "../secrets", "a/b", "a\\b", ".." })
        {
            bool refused = false;

            try { source.Read(escape, withGeometry: false); }
            catch (ArgumentException) { refused = true; }
            catch { /* a name that got past the check would fail on open instead */ }

            Assert.True(refused, $"'{escape}' is refused as a map name");
        }

        Assert.True(source.Available().Count == 0, "a directory that is not there is no maps rather than a throw");
    }
}
