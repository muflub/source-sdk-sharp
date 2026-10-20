using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two entities that let a map type at a console.
///
/// Almost all of both is one engine call, and the part that is not is the part
/// worth a test: point_servercommand's gate. Reading that convar wrong in the
/// permissive direction hands an untrusted map the server, and it would not
/// look like a bug from inside the game — the command would simply run, which
/// is exactly what the map asked for.
/// </summary>
public class PointCommandTests
{
    private static readonly EntityHandle Ent = new(0x0031_0031u);

    /// <summary>
    /// That a permitted command WAITS rather than running inside the input.
    ///
    /// The reason is the one command maps actually use this for. Engine.
    /// ExecuteServerCommand runs its argument immediately — deliberately, because
    /// the development API needs the effect to have landed by the time it answers
    /// — so a changelevel run straight from the input handler tears down the
    /// entity list while AcceptInput is still walking it, and frees the entity
    /// that asked along with everything after it in the same output's target
    /// list. Stock never had the problem because engine->ServerCommand only
    /// queues.
    ///
    /// Nothing about that is visible from inside the game until it crashes, and
    /// then it crashes somewhere else, so it is checked here instead.
    /// </summary>
    [Fact]
    public void TheWait()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointServerCommand).Assembly);
        registry.Services = Ignoring("Console.FindConVarValue", "Entities.IsPlayer");
        registry.Spawn(Ent, EntityNames.PointServercommand);

        var entity = (PointServerCommand)registry.Get(Ent)!;

        // The gate first, from the input side. With no game the convar cannot be
        // read, which is a refusal — and a refusal must leave nothing behind to
        // run later, or the wait would have turned the fail-closed direction into
        // a delayed fail-open.
        registry.Input(Ent, "Command", "changelevel dm_lockdown", 0f, EntityHandle.None);
        Assert.True(entity.PendingCount == 0, "a refused command queues nothing");

        // Past the gate, which needs the entity's own entry point: see Take.
        entity.Take("changelevel dm_lockdown");
        Assert.True(entity.PendingCount == 1, "a permitted command waits instead of running");

        entity.Take("say second");
        Assert.True(entity.PendingCount == 2, "a second command in the same frame is kept too");

        var ran = new List<string>();
        entity.Drain(ran.Add);

        Assert.True(ran.Count == 2, "the think runs both");

        // In order, which is what the engine's own command buffer promises and
        // what a map that wrote two lines is entitled to.
        Assert.True(ran.Count == 2 && ran[0] == "changelevel dm_lockdown" && ran[1] == "say second", "in the order they arrived");

        Assert.True(entity.PendingCount == 0, "and the queue is empty afterwards");

        // A think with nothing waiting is ordinary — the engine may run one after
        // the queue has already been drained — and must not misbehave.
        entity.Drain(ran.Add);
        Assert.True(ran.Count == 2, "a think with nothing waiting runs nothing");

        registry.Clear();
    }

    [Fact]
    public void TheGate()
    {
        Assert.True(PointServerCommand.Allows("always"), "\"always\" permits a map to run server commands");

        // Map and config data is inconsistent about case, and the native
        // comparison this replaces was V_strcasecmp.
        Assert.True(PointServerCommand.Allows("Always"), "and so does \"Always\"");

        Assert.True(!PointServerCommand.Allows("disallow"), "\"disallow\" does not");

        // Stock's third value, and the branch that reads it is compiled only
        // into TF — the one game with an IsValveMap to ask. Everywhere else
        // stock's else-branch catches the word and refuses, which is what this
        // has to keep doing: a build that read "official" as permission would
        // be more permissive than the stock it replaced.
        Assert.True(!PointServerCommand.Allows("official"), "neither does \"official\", outside the game that understands it");

        Assert.True(!PointServerCommand.Allows("yes") && !PointServerCommand.Allows(""), "nor anything else the setting might say");

        // The direction that matters. A convar that cannot be read at all is a
        // refusal, not a default: guessing wrong here runs commands nobody
        // permitted, and there is no symptom to notice afterwards.
        Assert.True(!PointServerCommand.Allows(null), "an unreadable setting is a refusal, not a default");
    }

    /// <summary>
    /// That both entities answer to "Command" and nothing else.
    ///
    /// The input name is the whole interface either one has, and a map fires it
    /// by string — so a handler bound under a name no map uses would register,
    /// spawn and accept its keys with nothing whatsoever going wrong until
    /// somebody wondered why the command never ran.
    /// </summary>
    [Fact]
    public void Binding()
    {
        foreach (string className in new[] { EntityNames.PointServercommand,
                                             EntityNames.PointClientcommand })
        {
            var registry = new ScriptedEntityRegistry(TestEngines.Server());
            registry.RegisterAll(typeof(PointServerCommand).Assembly);
            registry.Services = Ignoring("Console.FindConVarValue", "Entities.IsPlayer");

            Assert.True(registry.IsRegistered(className), $"{className} is registered");

            registry.Spawn(Ent, className);

            // Taken, not carried out: there is no engine here, so the command
            // goes nowhere. What is being asked is whether the name reached a
            // handler at all.
            Assert.True(registry.Input(Ent, "Command", "say hello", 0f, EntityHandle.None), $"{className} takes a Command input");

            // NOT "Kill": every scripted entity answers to that one now, because
            // stock declares it on CBaseEntity and it is part of what "an
            // entity" means to a mapper (ScriptedEntity.OnKill). A negative
            // probe has to be a name nothing declares, or it stops being one the
            // day something sensible starts declaring it.
            Assert.True(!registry.Input(Ent, "Detonate", "", 0f, EntityHandle.None), $"{className} claims nothing else");

            registry.Clear();
        }
    }
}
