using SourceSharp.Abi;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// A room's furniture across sleep and wake, and the dev commands (plan_descent_logic WO3): every
/// state survives a room's sleep, the list command prints every row, the use and hurt commands
/// take the paths +USE and a hit take.
/// </summary>
public sealed class FurnitureCommandTests
{
    [Fact]
    public async Task Sleep_and_wake_keep_every_rows_state_and_look()
    {
        var rig = new FurnitureRig(FurnitureRig.SeedWith((FurnitureKind.Shrine, null), (FurnitureKind.Container, null),
            (FurnitureKind.Explosive, null), (FurnitureKind.Dispenser, null)));
        var shrine = rig.First(FurnitureKind.Shrine);
        var container = rig.Service.Rows.First(r => r.Kind == FurnitureKind.Container && r.Placed.KeyId is null);
        var barrel = rig.First(FurnitureKind.Explosive);
        var dispenser = rig.First(FurnitureKind.Dispenser);
        await rig.Service.UseAs(shrine, rig.Stand(FurnitureRig.SlotA, shrine));
        await rig.Service.UseAs(container, rig.Stand(FurnitureRig.SlotA, container));
        if (barrel.Variant == "barrel")
            rig.Service.HurtRow(barrel, EntityHandle.None, 10);
        rig.Stand(FurnitureRig.SlotA, dispenser, new Vector(20, 0, 0));
        rig.Heroes.LifeOf[FurnitureRig.SlotA] = 50;
        rig.Service.Dispense(100, 1);
        var before = rig.Service.Rows.Select(r => (r.StateText(), rig.Service.Look(r))).ToList();
        var rooms = rig.Service.Rows.Select(r => r.Room).Distinct().ToList();

        rig.Rooms.Asleep.UnionWith(rooms);
        rig.Service.CheckRooms();
        bool allData = rig.Service.Rows.All(r => !r.OnEntity);
        rig.Rooms.Asleep.Clear();
        rig.Service.CheckRooms();

        Assert.True(allData);
        Assert.Equal(before, rig.Service.Rows.Select(r => (r.StateText(), rig.Service.Look(r))).ToList());
        Assert.All(rig.Service.Rows, r => Assert.Equal(rig.Service.Look(r).Model, r.Entity!.Model));
        Assert.Equal(FurnitureFlags.Spent, (FurnitureFlags)shrine.Entity!.State.Flags);
        Assert.Equal(FurnitureFlags.Opened, (FurnitureFlags)container.Entity!.State.Flags);
    }

    [Fact]
    public void The_list_command_prints_every_row_with_its_room_entity_and_state()
    {
        var rig = new FurnitureRig();
        int room = rig.Service.Rows[0].Room;
        rig.Rooms.Asleep.Add(room);
        rig.Service.CheckRooms();

        new FurnitureCommand(rig.Service, rig.Log).Execute();

        string text = rig.Log.Lines[^1].Text;
        Assert.All(rig.Service.Rows, r => Assert.Contains($"#{r.Index} {r.Kind.ToString().ToLowerInvariant()} {r.Variant} on {r.Placed.Marker}", text));
        Assert.Contains($"room {room} asleep data", text);
        Assert.Contains("awake entity #", text);
    }

    [Fact]
    public void The_list_command_with_a_room_prints_that_rooms_rows_alone()
    {
        var rig = new FurnitureRig();
        int room = rig.Service.Rows[0].Room;

        new FurnitureCommand(rig.Service, rig.Log).Execute(room);

        string text = rig.Log.Lines[^1].Text;
        Assert.All(rig.Service.Rows, r => Assert.Equal(r.Room == room, text.Contains($"#{r.Index} ")));
    }

    [Fact]
    public void The_use_command_uses_a_shrine_as_use_does()
    {
        var rig = new FurnitureRig(FurnitureRig.SeedWith((FurnitureKind.Shrine, null)));
        var shrine = rig.First(FurnitureKind.Shrine);
        rig.Stand(FurnitureRig.SlotA, shrine);

        new FurnitureUseCommand(rig.Service, rig.Heroes, rig.Log).Execute(shrine.Index, FurnitureRig.SlotA);

        Assert.True(shrine.Shrine!.Used);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains($"row {shrine.Index}, player {FurnitureRig.SlotA}: Used"));
    }

    [Fact]
    public void The_hurt_command_sets_an_explosive_off_in_the_players_name()
    {
        var rig = new FurnitureRig(FurnitureRig.SeedWith((FurnitureKind.Explosive, null)));
        var barrel = rig.First(FurnitureKind.Explosive);
        var hero = rig.Stand(FurnitureRig.SlotA, barrel, new Vector(3000, 0, 0));

        new FurnitureHurtCommand(rig.Service, rig.Heroes, rig.Log).Execute(barrel.Index, 1000, FurnitureRig.SlotA);
        rig.Think();

        Assert.True(barrel.Went);
        Assert.Equal(hero.Player, Assert.Single(rig.World.Blasts).Attacker);
    }

    [Fact]
    public void The_hurt_command_below_its_health_leaves_the_entity_with_what_is_left()
    {
        var rig = new FurnitureRig(FurnitureRig.SeedWith((FurnitureKind.Explosive, "barrel")));
        var barrel = rig.First(FurnitureKind.Explosive, "barrel");

        new FurnitureHurtCommand(rig.Service, rig.Heroes, rig.Log).Execute(barrel.Index, 15, 0);

        Assert.Equal((int)FurnitureRig.Rules.Content.Furniture.Explosive("barrel").Health - 15, barrel.Entity!.EngineHealth);
    }
}
