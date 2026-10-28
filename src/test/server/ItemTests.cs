using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A stand-in for a real item, so what is under test is the shared pickup
/// behaviour rather than any particular item's contents.
/// </summary>
[ScriptedEntity("test_item", ScriptedEntityKind.Item)]
public sealed class TestItem(IEngineWorldQuery world, PlacedObjects placed) : PickupItem(world, placed)
{
    protected override string Model => "models/test.mdl";
    protected override float RespawnDelay => Delay;

    /// <summary>Set by a test to decide whether there is anything to give.</summary>
    public static bool HasSomethingToGive = true;

    /// <summary>Set by a test; zero means gone for good.</summary>
    public static float Delay = 20f;

    public static int GiveAttempts;

    protected override bool TryGive(EntityHandle player)
    {
        GiveAttempts++;
        return HasSomethingToGive;
    }

    public static void Reset()
    {
        HasSomethingToGive = true;
        Delay = 20f;
        GiveAttempts = 0;
    }
}

public class ItemTests
{
    private static readonly EntityHandle Item = new(0x0007_0007u);
    private static readonly EntityHandle Player = new(0x0008_0008u);

    private static (ScriptedEntityRegistry, TestItem) Fresh(int toucherHealth = 100, bool seen = true)
    {
        TestItem.Reset();

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ItemTests).Assembly);
        registry.Services = Services(out var engine);

        // Items refuse dead touchers, so every touch asks the toucher's
        // health first; the default here is a living player.
        engine.Returns("Entities.GetHealth", toucherHealth);

        // CItem::ItemTouch asks for a player with a clear line to it
        // (UTIL_ItemCanBeTouchedByPlayer); the default here has one.
        engine.Returns("Entities.IsPlayer", true)
              .Returns("WorldQuery.EntityTraceHull", seen
                  ? new EntityHullTrace(Vector.Zero, false, false, Fraction: 1f)
                  : new EntityHullTrace(Vector.Zero, false, false, new EntityHandle(0x0002_0099u), 0.4f))
              .Ignores("Entities.GetSolidType", "Entities.GetWorldSpaceCenter", "Entities.GetEyePosition",
                       "Entities.UseTriggerBounds");

        // A taken item goes back where the map put it (CItem::Respawn).
        engine.Ignores("Entities.GetSpawnOrigin", "Entities.GetSpawnAngles", "EntityPhysics.InitPhysics");

        registry.Spawn(Item, "test_item");

        return (registry, (TestItem)registry.Get(Item)!);
    }

    //-------------------------------------------------------------------------
    // The rule every health and armour item shares: top up to a cap, and report
    // whether anything was actually given.
    //-------------------------------------------------------------------------
    [Fact]
    public void TopUpArithmetic()
    {
        int applied = -1;

        Assert.True(PickupItem.TopUpForTest(50, 25, 100, v => applied = v) && applied == 75, "topping up below the cap gives");

        // Clamped, not overshot: a medkit on a nearly-full player must not push
        // them past the maximum.
        applied = -1;
        Assert.True(PickupItem.TopUpForTest(90, 25, 100, v => applied = v) && applied == 100, "topping up clamps to the cap");

        // Nothing to give leaves the item in the world, which is how every
        // Source item behaves — it is not consumed for no benefit.
        applied = -1;
        Assert.True(!PickupItem.TopUpForTest(100, 25, 100, v => applied = v) && applied == -1, "a full player is given nothing");

        Assert.True(!PickupItem.TopUpForTest(150, 25, 100, _ => { }), "over the cap also gives nothing");
    }

    [Fact]
    public void An_item_behind_a_wall_is_not_offered_to_the_player()
    {
        // UTIL_ItemCanBeTouchedByPlayer: the bloated reach goes through thin
        // walls, and the sight line is what stops it.
        var (registry, item) = Fresh(seen: false);

        registry.Touch(Item, Player);

        Assert.Equal((0, 0), (TestItem.GiveAttempts, item.OnPlayerTouch.FireCount));
    }

    [Fact]
    public void TakingAndRespawning()
    {
        var (registry, item) = Fresh();

        Assert.True(item.OnPlayerTouch.FireCount == 0, "an item starts unfired");

        registry.Touch(Item, Player);
        Assert.True(item.OnPlayerTouch.FireCount == 1, "touching takes the item");

        // A trigger reports every tick the player stands inside it, so being
        // taken has to be idempotent — this is the case that would otherwise
        // hand out an item sixty times a second.
        registry.Touch(Item, Player);
        registry.Touch(Item, Player);
        Assert.True(item.OnPlayerTouch.FireCount == 1, "standing in a taken item takes nothing more");
        Assert.True(TestItem.GiveAttempts == 1, "and stops asking it for contents");

        // The respawn think puts it back.
        registry.Think(Item);
        registry.Touch(Item, Player);
        Assert.True(item.OnPlayerTouch.FireCount == 2, "respawning makes it available again");

        // An item with nothing to give is not consumed, so it stays takeable.
        var (registry2, item2) = Fresh();
        TestItem.HasSomethingToGive = false;

        registry2.Touch(Item, Player);
        Assert.True(item2.OnPlayerTouch.FireCount == 0, "an item with nothing to give is not taken");

        TestItem.HasSomethingToGive = true;
        registry2.Touch(Item, Player);
        Assert.True(item2.OnPlayerTouch.FireCount == 1, "and can still be taken once it is useful");
    }

    [Fact]
    public void ADeadToucherTakesNothing()
    {
        // The live bug: a death-dropped weapon landed on its own corpse and
        // the dead body's touch took it straight back. A corpse's health is
        // 0 or below by the same rule that killed it.
        var (registry, item) = Fresh(toucherHealth: -150);

        registry.Touch(Item, Player);

        Assert.True(item.OnPlayerTouch.FireCount == 0, "a dead toucher takes nothing");
        Assert.True(TestItem.GiveAttempts == 0, "and the item was never even asked for its contents");
    }

    //-------------------------------------------------------------------------
    // The runtime pickup's entity, which owns nothing itself: it relays the
    // touch to the directory that spawned it. What is worth proving is the
    // ARGUMENTS — a relay that swapped the pickup and the toucher would
    // compile, run, and try to give health to the medkit.
    //-------------------------------------------------------------------------
    private sealed class RecordingDirectory : IPickupDirectory
    {
        public EntityHandle TouchedEntity, TouchedBy;
        public float TouchedAt;
        public int Calls;

        public int LiveCount => 0;
        public int PendingCount => 0;
        public EntityHandle Spawn(PickupKind kind, Vector origin) => EntityHandle.None;
        public bool IsLive(EntityHandle entity) => false;

        public bool OnTouched(EntityHandle entity, EntityHandle other, float now)
        {
            Calls++;
            TouchedEntity = entity;
            TouchedBy = other;
            TouchedAt = now;
            return false;
        }

    }

    /// <summary>
    /// A level's first frames place nothing of their own: stock HL2MP puts no
    /// pickup beside a spawn point, and dm_lockdown's census had ten more
    /// entities than stock's for it (2026-10-27).
    /// </summary>
    [Fact]
    public void A_level_with_spawn_points_gets_no_pickups_of_ours()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Entities.FindAllByClassname", new[] { new EntityHandle(0x0002_0001u) })
              .Returns("Entities.GetOrigin", new Vector(64f, 0f, 0f));
        var pickups = new Pickups(new RecordingLog(), engine, engine, engine, engine);

        ((IGameLifecycle)pickups).OnLevelInit("dm_lockdown");
        ((IFrameLifecycle)pickups).OnThink(0.015f);
        ((IFrameLifecycle)pickups).OnThink(0.015f);

        Assert.False(engine.Called("Entities.Create"));
    }

    [Fact]
    public void RuntimePickupRelaysToItsDirectory()
    {
        var directory = new RecordingDirectory();

        var services = Services(out var engine);
        engine.Returns("Clock.Now", 42f);
        services.AddInstance<IPickupDirectory>(directory);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Pickups).Assembly);
        registry.Services = services;
        registry.Spawn(Item, EntityNames.SsPickup);

        Assert.IsType<SsPickup>(registry.Get(Item));

        registry.Touch(Item, Player);

        Assert.Equal(1, directory.Calls);

        // The pickup is the ENTITY and the player is the TOUCHER. Asserted by
        // value because the swapped form also typechecks.
        Assert.Equal(Item, directory.TouchedEntity);
        Assert.Equal(Player, directory.TouchedBy);
        Assert.Equal(42f, directory.TouchedAt);
    }
}
