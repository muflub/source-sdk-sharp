using SourceSharp.Abi;
using SourceSharp.Shared;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Reading a saved group back.
///
/// A group arrives from the browser's own store, possibly after a YAML round
/// trip and possibly after a person edited it by hand — so the parser meets
/// numbers where strings were written, vectors in two spellings, and keys that
/// would fight the spawner if they were passed through. Getting any of that
/// wrong puts entities in the wrong place with the wrong settings, which is a
/// worse failure than refusing to place them at all.
/// </summary>
public class SpawnTests
{

    [Fact]
    public void Parsing()
    {
        var (placement, player, name, entities) = EntitySpawner.Parse("""
            {
              "name": "Ticking relay",
              "placement": "look",
              "player": 2,
              "entities": [
                {
                  "classname": "logic_timer",
                  "targetname": "ticker",
                  "origin": [16, -32, 8.5],
                  "angles": [0, 90, 0],
                  "keyvalues": { "RefireTime": 2, "StartDisabled": false, "Note": "every two seconds" },
                  "connections": [
                    { "output": "OnTimer", "target": "relay", "input": "Trigger",
                      "param": "", "delay": 0.5, "times": -1 }
                  ]
                }
              ]
            }
            """);

        Assert.True(placement == Placement.Look, "the placement is read");
        Assert.True(player == 2, "the player slot is read");
        Assert.True(name == "Ticking relay", "the group name is read");
        Assert.True(entities.Count == 1, "the entity is read");

        var entity = entities[0];

        Assert.True(entity.ClassName == EntityNames.LogicTimer, "the classname is read");
        Assert.True(entity.TargetName == "ticker", "the targetname is read");
        Assert.True(entity.Origin[0] == 16f && entity.Origin[2] == 8.5f, "an array origin is read");
        Assert.True(entity.Angles[1] == 90f, "angles are read");

        // A map value is a string, but a group that went through YAML may hold a
        // number or a bool where one was written. Rendering them back beats
        // refusing a group that is perfectly well formed.
        Assert.True(entity.KeyValues.Any(kv => kv.Key == "RefireTime" && kv.Value == "2"), "a numeric keyvalue becomes its text");

        Assert.True(entity.KeyValues.Any(kv => kv.Key == "StartDisabled" && kv.Value == "0"), "a false keyvalue becomes \"0\"");

        Assert.True(entity.KeyValues.Any(kv => kv.Key == "Note" && kv.Value == "every two seconds"), "a string keyvalue is left alone");

        var connection = entity.Connections[0];

        Assert.True(connection.Output == "OnTimer" && connection.Target == "relay"
            && connection.Input == "Trigger" && Math.Abs(connection.Delay - 0.5f) < 0.001f
            && connection.TimesToFire == -1, "a connection is read whole");

        // The spawner sets these itself. Passing them through as well would have
        // ent_create fight the placement that follows it.
        var reserved = EntitySpawner.Parse("""
            {"entities":[{"classname":"logic_relay","keyvalues":
              {"origin":"1 2 3","angles":"0 0 0","targetname":"x","classname":"y","spawnflags":"0"}}]}
            """).Entities[0];

        Assert.True(reserved.KeyValues.Count == 1 && reserved.KeyValues[0].Key == "spawnflags", "the keys the spawner owns are not passed through");

        // The map's own spelling of a vector, which is what a hand-written YAML
        // file is most likely to contain.
        var stringOrigin = EntitySpawner.Parse("""
            {"entities":[{"classname":"info_target","origin":"-64 128 32.5"}]}
            """).Entities[0];

        Assert.True(stringOrigin.Origin[0] == -64f && stringOrigin.Origin[2] == 32.5f, "a string origin is read too");

        // Absent everything. A group with a bare classname is legal and should
        // land at the group's own anchor rather than throwing.
        var bare = EntitySpawner.Parse("""{"entities":[{"classname":"logic_relay"}]}""").Entities[0];

        Assert.True(bare.ClassName == EntityNames.LogicRelay
            && bare.Origin.Length == 3 && bare.Origin[0] == 0f
            && bare.KeyValues.Count == 0 && bare.Connections.Count == 0, "a bare entity parses");

        Assert.True(EntitySpawner.Parse("{}").Entities.Count == 0, "no entities is no entities");
        Assert.True(EntitySpawner.Parse("{}").Placement == Placement.Origin, "placement defaults to the saved origins");

        bool threw = false;
        try { EntitySpawner.Parse("not json"); } catch { threw = true; }
        Assert.True(threw, "nonsense is refused rather than half-read");
    }

    [Fact]
    public void Commands()
    {
        // AddOutput reads its fields separated by colons, which the engine swaps
        // back to commas — a connection written with commas here would arrive as
        // one long target name.
        var connection = new MapConnection("OnTimer", "relay", "Trigger", "", 0.5f, -1);

        Assert.True(EntitySpawner.Wire(connection) == "OnTimer relay:Trigger::0.5:-1", "a connection becomes an AddOutput value");

        Assert.True(EntitySpawner.Wire(new MapConnection("OnTrigger", "a", "Kill", "p", 0f, 1))
                == "OnTrigger a:Kill:p:0:1", "a repeat count survives");

        // The console splits on whitespace, and for ent_create that means a
        // keyvalue silently taking the wrong value and shifting every pair after
        // it along by one.
        Assert.True(EntitySpawner.Quote(EntityNames.LogicRelay) == EntityNames.LogicRelay, "a plain token is left bare");
        Assert.True(EntitySpawner.Quote("every two seconds") == "\"every two seconds\"", "a token with a space is quoted");
        Assert.True(EntitySpawner.Quote("") == "\"\"", "an empty value is quoted rather than vanishing");
    }

    //-------------------------------------------------------------------------
    // The spawner: its temporary names come from a counter of its own.
    //-------------------------------------------------------------------------

    /// <summary>
    /// An engine that runs the commands and finds nothing, so every create
    /// reports a failure. The spawner asks THIS engine whether what it found
    /// is valid (plan_abi_hotreload D8), where the handle used to ask the
    /// process's.
    /// </summary>
    private static RecordingEngine NothingFound()
        => new RecordingEngine()
            .Ignores("Console.ExecuteServerCommand")
            .Returns("Entities.FindByName", EntityHandle.None)
            .Returns("Entities.IsValid", false);

    private static SpawnEntity Relay()
        => new(EntityNames.LogicRelay, "", new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f },
               Array.Empty<MapKeyValue>(), Array.Empty<MapConnection>());

    private static string[] TempNamesAsked(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Entities.FindByName")
                       .Select(c => c.ArgAs<string>(0)).ToArray();

    [Fact]
    public void EachCreateIsLookedUpUnderAFreshTemporaryName()
    {
        var engine = NothingFound();
        var spawner = new EntityGroupSpawner(((IEngineConsole)engine).ExecuteServerCommand, new SourceSharp.Server.DebugWorld(engine, engine, engine, engine, engine, engine));

        var results = spawner.Spawn(Placement.Origin, 1, new[] { Relay(), Relay() });

        Assert.True(results.Count == 2 && results.All(r => !r.Created),
                    "an engine that finds nothing reports every create as failed");
        Assert.Equal(new[] { "__ss_spawn_1", "__ss_spawn_2" }, TempNamesAsked(engine));
    }

    [Fact]
    public void TwoSpawnersDoNotShareACounter()
    {
        // The property the static counter lacked: one spawner's creates do not
        // advance another's names.
        var engine = NothingFound();
        var first = new EntityGroupSpawner(((IEngineConsole)engine).ExecuteServerCommand, new SourceSharp.Server.DebugWorld(engine, engine, engine, engine, engine, engine));
        first.Spawn(Placement.Origin, 1, new[] { Relay(), Relay() });

        var other = NothingFound();
        var second = new EntityGroupSpawner(((IEngineConsole)other).ExecuteServerCommand, new SourceSharp.Server.DebugWorld(other, other, other, other, other, other));
        second.Spawn(Placement.Origin, 1, new[] { Relay() });

        Assert.Equal(new[] { "__ss_spawn_1" }, TempNamesAsked(other));
    }
}
