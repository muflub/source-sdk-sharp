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
public sealed class TestItem : PickupItem
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

    private static (ScriptedEntityRegistry, TestItem) Fresh(int toucherHealth = 100)
    {
        TestItem.Reset();

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ItemTests).Assembly);
        registry.Services = Services(out var engine);

        // Items refuse dead touchers, so every touch asks the toucher's
        // health first; the default here is a living player.
        engine.Returns("Entities.GetHealth", toucherHealth);

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

        public int PopulateFromSpawnPoints(float heightAboveSpawn = 8f) => 0;
    }

    //-------------------------------------------------------------------------
    // WHERE A SPAWN POINT'S PICKUP GOES.
    //
    // Measured live on dm_lockdown before this was a rule: all ten pickups
    // were placed eight units directly above a spawn point, and the player
    // spawns at that same X/Y. So the player stood INSIDE the health kit --
    // which gives nothing at full health, and therefore healed them back to
    // 100 the instant they took their first damage:
    //
    //   HIT  {"healthBefore":100,"healthAfter":83}
    //   log  [SourceSharp/Pickup] ss_player#1 took 'health': 83 -> 100 health
    //
    // and again every RespawnDelay seconds for as long as they stood there.
    // The rule under test is the one that makes that impossible: the pickup's
    // trigger box must not intersect the hull of whoever spawns on the point,
    // AT ANY FACING -- an offset that clears the axes and not the diagonals
    // would leave a share of a map's spawn points still doing this.
    //-------------------------------------------------------------------------

    /// <summary>Do two axis-aligned boxes, given as centre and half-extents, meet?</summary>
    private static bool Overlaps(Vector aCentre, Vector aHalf, Vector bCentre, Vector bHalf)
        => MathF.Abs(aCentre.X - bCentre.X) < (aHalf.X + bHalf.X)
        && MathF.Abs(aCentre.Y - bCentre.Y) < (aHalf.Y + bHalf.Y)
        && MathF.Abs(aCentre.Z - bCentre.Z) < (aHalf.Z + bHalf.Z);

    /// <summary>
    /// The player, as the shell places them: one unit above the point
    /// (CStandalonePlayer::Spawn's lift) and VEC_HULL_MIN/MAX tall.
    /// </summary>
    private static (Vector Centre, Vector Half) SpawningPlayerHull(Vector spawnPoint)
        => (new Vector(spawnPoint.X, spawnPoint.Y, spawnPoint.Z + 1f + 36f),
            new Vector(Pickups.PlayerHalfWidth, Pickups.PlayerHalfWidth, 36f));

    /// <summary>
    /// The pickup's trigger, as MakePickup builds it: half-extents in X and Y,
    /// but 0..SizeZ in Z -- the box sits ON the origin rather than around it
    /// (SourceSharp_EntityMakePickup, shell_entityapi.cpp:470).
    /// </summary>
    private static (Vector Centre, Vector Half) PickupBox(Vector at)
        => (new Vector(at.X, at.Y, at.Z + (Pickups.Health.SizeZ / 2f)),
            new Vector(Pickups.Health.SizeX, Pickups.Health.SizeY, Pickups.Health.SizeZ / 2f));

    [Fact]
    public void APickupNeverOverlapsTheHullOfWhoeverSpawnsOnThatPoint()
    {
        var point = new Vector(-3893.1f, 1413.9f, 8f);
        var (playerCentre, playerHalf) = SpawningPlayerHull(point);

        // Every fifteen degrees of yaw, because the diagonals are the worst
        // case and testing one facing would pass on an axis-only offset.
        for (int yaw = 0; yaw < 360; yaw += 15)
        {
            var (pickupCentre, pickupHalf) =
                PickupBox(Pickups.PlacementNear(point, new Vector(0f, yaw, 0f)));

            Assert.False(Overlaps(playerCentre, playerHalf, pickupCentre, pickupHalf),
                         $"a pickup on a spawn point facing yaw {yaw} is inside the player who spawns there");
        }
    }

    [Fact]
    public void APickupOnASpawnPointFacingStraightUpIsStillClearOfIt()
    {
        // A pitched facing has little or no XY direction to offset along, and
        // -90 has exactly none: the fallback still has to clear the hull.
        var point = new Vector(0f, 0f, 0f);
        var (playerCentre, playerHalf) = SpawningPlayerHull(point);
        var (pickupCentre, pickupHalf) =
            PickupBox(Pickups.PlacementNear(point, new Vector(-90f, 0f, 0f)));

        Assert.False(Overlaps(playerCentre, playerHalf, pickupCentre, pickupHalf),
                     "a spawn point looking at the ceiling still gets a pickup beside it, not in it");
    }

    [Fact]
    public void APickupIsPlacedAheadOfTheSpawnPointRatherThanBehindIt()
    {
        // Facing +X: the pickup goes the way the arriving player will be
        // looking, which is the direction a mapper has already pointed at the
        // open part of the room.
        var placed = Pickups.PlacementNear(new Vector(100f, 200f, 8f), new Vector(0f, 0f, 0f));

        Assert.True(placed.X > 100f, "the offset is along the facing, not against it");
        Assert.Equal(200f, placed.Y, 3);
    }

    [Fact]
    public void APickupKeepsTheHeightItWasAskedFor()
    {
        // The height is the caller's and the offset must not spend any of it:
        // a facing with pitch would otherwise push the pickup into the floor.
        var placed = Pickups.PlacementNear(new Vector(0f, 0f, 512f), new Vector(45f, 30f, 0f),
                                           heightAboveSpawn: 8f);

        Assert.Equal(520f, placed.Z, 3);
    }

    [Fact]
    public void PlacementIsTheSameDistanceFromTheSpawnPointAtEveryFacing()
    {
        // The flattened facing is RENORMALISED. Without that a pitched spawn
        // point keeps only the cosine of its offset and quietly lands closer
        // to the player than any of the checks above chose.
        var point = new Vector(0f, 0f, 0f);

        for (int pitch = -60; pitch <= 60; pitch += 30)
        {
            var placed = Pickups.PlacementNear(point, new Vector(pitch, 37f, 0f));
            float flat = MathF.Sqrt((placed.X * placed.X) + (placed.Y * placed.Y));

            Assert.Equal(Pickups.ClearanceFromSpawn, flat, 2);
        }
    }

    [Fact]
    public void RuntimePickupRelaysToItsDirectory()
    {
        var directory = new RecordingDirectory();

        var services = Services(out var engine);
        engine.Returns("Clock.Now", 42f);
        services.AddInstance<IPickupDirectory>(directory);

        var registry = new ScriptedEntityRegistry();
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
