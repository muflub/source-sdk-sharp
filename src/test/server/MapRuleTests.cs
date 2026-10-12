using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The three map rules that answer a question rather than do something: who is
/// standing in a volume, how many bodies a map will tolerate, and one name that
/// has never meant anything.
///
/// The parts worth testing are the two decisions. A zone that gets its box test
/// subtly wrong still counts players and still fires its outputs — it just
/// counts the wrong ones, and the map looks like it has a broken door. A ragdoll
/// budget that reads its DX8 key at the wrong moment is invisible on the machine
/// the developer is sitting at.
/// </summary>
public class MapRuleTests
{
    private static readonly EntityHandle Ent = new(0x0031_0031u);

    [Fact]
    public void ZoneOverlap()
    {
        var zoneMins = new Vector(-64f, -64f, 0f);
        var zoneMaxs = new Vector(64f, 64f, 128f);

        // A player standing in the middle of it.
        Assert.True(GameZonePlayer.Overlaps(zoneMins, zoneMaxs,
                new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f)), "a player inside the zone overlaps it");

        // Out on one axis only, which is the case a test written with a single
        // distance would pass and a box test must not.
        Assert.True(!GameZonePlayer.Overlaps(zoneMins, zoneMaxs,
                new Vector(96f, -16f, 0f), new Vector(128f, 16f, 72f)), "a player past the zone on one axis is outside it");

        // Above it, standing on the roof of the volume rather than in it.
        Assert.True(!GameZonePlayer.Overlaps(zoneMins, zoneMaxs,
                new Vector(-16f, -16f, 129f), new Vector(16f, 16f, 201f)), "a player above the zone is outside it");

        // Touching counts, which is what the stock trace answers: a hull that
        // just reaches the boundary starts solid.
        Assert.True(GameZonePlayer.Overlaps(zoneMins, zoneMaxs,
                new Vector(64f, -16f, 0f), new Vector(96f, 16f, 72f)), "a player exactly on the boundary is inside it");

        // Only a corner of the player is in, which is still in.
        Assert.True(GameZonePlayer.Overlaps(zoneMins, zoneMaxs,
                new Vector(56f, 56f, 120f), new Vector(88f, 88f, 192f)), "a player overlapping by a corner is inside it");

        // A zone with NO extent, which is what the entity was working with for
        // as long as Engine.EntityGetBounds answered only for a CBaseEntity: the
        // zone is on the parallel hierarchy, the call came back zeroed, and the
        // box collapsed onto the entity's own origin.
        //
        // Worth pinning as its own case rather than trusting the arithmetic,
        // because the failure is completely silent from up here. Every output
        // still fires, every count is still reported, and every player is
        // reported OUTSIDE — which reads as a map with a broken door and not as
        // a zone that has lost its shape.
        var pointMins = Vector.Zero;
        var pointMaxs = Vector.Zero;

        Assert.True(!GameZonePlayer.Overlaps(pointMins, pointMaxs,
                new Vector(8f, -16f, 0f), new Vector(40f, 16f, 72f)), "a zone with no extent misses a player standing beside it");

        // And the same player, against the box the zone actually has. This pair
        // is the whole difference the native fix makes: one answer per side of
        // it, from the same player standing in the same place.
        Assert.True(GameZonePlayer.Overlaps(zoneMins, zoneMaxs,
                new Vector(8f, -16f, 0f), new Vector(40f, 16f, 72f)), "and the box it really has finds them");
    }

    [Fact]
    public void ZoneCounting()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(GameZonePlayer).Assembly);
        registry.Services = Ignoring("Entities.GetBounds", "Players.GetHandle", "Entities.GetOrigin", "Entities.IsValid");
        registry.Spawn(Ent, EntityNames.GameZonePlayer);

        var zone = (GameZonePlayer)registry.Get(Ent)!;

        registry.Input(Ent, "CountPlayersInZone", "", 0f, EntityHandle.None);

        // No engine, so no player occupies any slot. The counts must still be
        // reported: a map wired to PlayersInCount expects a number every time it
        // asks, and silence on an empty server reads as the entity being broken.
        Assert.True(zone.PlayersInCount.FireCount == 1, "counting reports how many are inside");
        Assert.True(zone.PlayersOutCount.FireCount == 1, "and how many are outside");
        Assert.True(zone.PlayersInCount.LastValue == "0", "an empty server counts nobody in");
        Assert.True(zone.PlayersOutCount.LastValue == "0", "and nobody out");

        // The per-player outputs are per PLAYER, not per count: with nobody
        // there they must not fire at all.
        Assert.True(zone.OnPlayerInZone.FireCount == 0, "nobody in the zone fires no per-player output");
        Assert.True(zone.OnPlayerOutZone.FireCount == 0, "and nobody outside it either");

        // Asked twice, answered twice. A map polls this from a timer.
        registry.Input(Ent, "CountPlayersInZone", "", 0f, EntityHandle.None);
        Assert.True(zone.PlayersInCount.FireCount == 2, "asking again answers again");
    }

    [Fact]
    public void RagdollBudget()
    {
        const int ShaderModel3 = 90;
        const int ShaderModel2 = 81;

        Assert.True(GameRagdollManager.Budget(ShaderModel3, 4, 1) == 4, "a capable machine uses the plain budget");

        Assert.True(GameRagdollManager.Budget(ShaderModel2, 4, 1) == 1, "a weak machine uses the DX8 budget");

        // The case the >= 0 test exists for: a map that set one budget meant it
        // for everybody, and falling through to -1 would remove the limit on
        // exactly the machine that needed it most.
        Assert.True(GameRagdollManager.Budget(ShaderModel2, 4, -1) == 4, "a weak machine with no DX8 budget keeps the plain one");

        // Zero is a budget, not an absence: it forbids ragdolls outright.
        Assert.True(GameRagdollManager.Budget(ShaderModel2, 4, 0) == 0, "a DX8 budget of zero is honoured, not ignored");

        Assert.True(GameRagdollManager.Budget(ShaderModel3, -1, -1) == -1, "no keys at all means no limit of this entity's own");
    }

    /// <summary>
    /// The body registry <c>game_ragdoll_manager</c> writes its budget into.
    ///
    /// REAL RATHER THAN A DOUBLE, because what is being tested is that the
    /// write LANDS somewhere a reader will look, and a double would answer the
    /// assertion whether or not the entity had been wired to anything.
    /// </summary>
    private sealed class NoDirectory : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => Array.Empty<ScriptedEntity>();
        public IEnumerable<ScriptedEntity> All() => Array.Empty<ScriptedEntity>();
        public bool IsRegistered(string className) => false;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => Array.Empty<ScriptedClassInfo>();
    }

    [Fact]
    public void RagdollKeysAndInputs()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(GameRagdollManager).Assembly);

        // THE BUDGET'S DESTINATION IS A CONSTRUCTOR ARGUMENT NOW, not a console
        // command. plan_shim.md §4a item 3 deleted the native LRU and
        // g_ragdoll_maxcount with it, so `ExecuteServerCommand("g_ragdoll_maxcount N")`
        // would name a convar nobody declares -- reached by name, failing in
        // silence, invisible to any link-line census.
        var services = Services(out var engine);
        engine.Ignores("Entities.IsValid");
        engine.Ignores("Entities.GetBounds", "Players.GetHandle");

        var corpses = new Corpses(new RecordingLog(), engine, engine, new NoDirectory(), engine);
        services.AddInstance(corpses);
        registry.Services = services;

        registry.KeyValue(Ent, EntityNames.GameRagdollManager, "MaxRagdollCount", "4");
        registry.KeyValue(Ent, EntityNames.GameRagdollManager, "MaxRagdollCountDX8", "1");
        registry.KeyValue(Ent, EntityNames.GameRagdollManager, "SaveImportant", "1");
        registry.Spawn(Ent, EntityNames.GameRagdollManager);

        var manager = (GameRagdollManager)registry.Get(Ent)!;

        // No engine to answer mat_dxlevel, which is the dedicated-server case:
        // the capable path, so the map's plain budget is what stands.
        Assert.True(manager.CurrentMaxRagdollCount == 4, "the map's budget is in force after spawn");
        Assert.True(manager.CurrentMaxRagdollCount != 1, "an unanswered dx level does not apply the DX8 budget");
        Assert.True(manager.SaveImportant, "SaveImportant is read from the map");

        registry.Input(Ent, "SetMaxRagdollCount", "2", 2f, EntityHandle.None);
        Assert.True(manager.CurrentMaxRagdollCount == 2, "an input replaces the budget");

        // Setting the DX8 budget on a capable machine changes nothing visible,
        // which is the point: the two are separate numbers and only one applies.
        registry.Input(Ent, "SetMaxRagdollCountDX8", "0", 0f, EntityHandle.None);
        Assert.True(manager.CurrentMaxRagdollCount == 2, "setting the DX8 budget leaves the one in force alone");
    }

    /// <summary>
    /// The budget reaches the thing that retires bodies.
    ///
    /// <para>
    /// THE HALF <see cref="RagdollKeysAndInputs"/> CANNOT SEE.
    /// <c>CurrentMaxRagdollCount</c> is this entity's own record of what the map
    /// asked for, and it moved correctly for the whole time the write went to a
    /// convar that no longer existed. What has to be asserted is the WRITE, at
    /// the registry that actually caps the corpses.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRagdollBudgetLandsOnTheCorpseRegistry()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(GameRagdollManager).Assembly);

        var services = Services(out var engine);
        engine.Ignores("Entities.IsValid");
        engine.Ignores("Entities.GetBounds", "Players.GetHandle");

        var corpses = new Corpses(new RecordingLog(), engine, engine, new NoDirectory(), engine);
        int before = corpses.Limit;
        services.AddInstance(corpses);
        registry.Services = services;

        registry.KeyValue(Ent, EntityNames.GameRagdollManager, "MaxRagdollCount", "4");
        registry.Spawn(Ent, EntityNames.GameRagdollManager);

        Assert.True(before != 4, "the registry's default is already 4, so this fact could not fail");
        Assert.True(corpses.Limit == 4, "the map's budget is what caps the bodies");

        registry.Input(Ent, "SetMaxRagdollCount", "2", 2f, EntityHandle.None);
        Assert.True(corpses.Limit == 2, "an input moves the cap the bodies are trimmed to");
    }

    /// <summary>
    /// -1 is "no limit of mine" and leaves the registry's own default alone.
    ///
    /// Stamping a value over it would turn silence into a decision, which is the
    /// half of the old convar write that was right and had to survive the move.
    /// </summary>
    [Fact]
    public void ARagdollManagerWithNoBudgetLeavesTheRegistrysDefaultAlone()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(GameRagdollManager).Assembly);

        var services = Services(out var engine);
        engine.Ignores("Entities.IsValid");
        engine.Ignores("Entities.GetBounds", "Players.GetHandle");

        var corpses = new Corpses(new RecordingLog(), engine, engine, new NoDirectory(), engine);
        int before = corpses.Limit;
        services.AddInstance(corpses);
        registry.Services = services;

        registry.Spawn(Ent, EntityNames.GameRagdollManager);

        Assert.True(corpses.Limit == before, "a map that set no budget did not change the cap");
    }

    [Fact]
    public void ProximityIsInert()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(LogicProximity).Assembly);
        registry.Services = Ignoring("Entities.GetBounds", "Players.GetHandle", "Entities.IsValid");

        Assert.True(registry.IsRegistered(EntityNames.LogicProximity), "logic_proximity is a name the game answers to");

        registry.Spawn(Ent, EntityNames.LogicProximity);
        Assert.True(registry.Get(Ent) is LogicProximity, "and it spawns");

        // Asserted rather than assumed: stock binds this name to a bare
        // CPointEntity, and anything appearing here later would be an entity
        // somebody invented rather than ported.
        var described = registry.DescribeClasses()
            .First(c => c.ClassName == EntityNames.LogicProximity);

        // Kill, AddOutput, and nothing else. Neither is one of ITS inputs —
        // stock declares both on CBaseEntity, so a bare CPointEntity answers to
        // them too, and ScriptedEntity.OnKill and OnAddOutput are where they
        // live here.
        string[] universal = { "Kill", "AddOutput", "DisableDamageForces", "EnableDamageForces" };

        Assert.True(described.Inputs.Where(i => !universal.Contains(i, StringComparer.OrdinalIgnoreCase)).Count() == 0,
            "it takes no inputs of its own");
        Assert.True(described.Outputs.Count == 0, "it sends no outputs");
        Assert.True(described.Vars.Count == 0, "and it reads no keys");
    }
}
