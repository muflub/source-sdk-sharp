using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What a prop and a physbox tell the map when the gravity gun (or a player)
/// takes or drops them: CPhysicsProp::OnPhysGunPickup and OnPhysGunDrop
/// (props.cpp:2727, 2768) and CPhysBox's (physobj.cpp:731, 763). Found on
/// dm_lockdown (2026-10-26): its plug, a func_physbox, wires OnPhysGunPickup
/// to the relay that pulls it, and neither class had the output.
/// </summary>
public class PhysGunOutputsTests
{
    private static readonly EntityHandle Player = new(0x0001_0001);
    private static readonly EntityHandle Subject = new(0x0002_0041);

    private static T Spawned<T>(string className, int spawnFlags) where T : ScriptedEntity
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", "")
              .Returns("EntityRender.GetModelKeyValueText", "")
              .Returns("Entities.GetSpawnFlags", spawnFlags)
              .Returns("EntityPhysics.InitPhysics", true);
        registry.Spawn(Subject, className);
        var entity = (T)registry.Get(Subject)!;
        if (entity is PropPhysics prop)
            prop.OnPhysicsCreated();
        return entity;
    }

    [Fact]
    public void A_prop_taken_by_the_gun_says_so_three_ways()
    {
        var prop = Spawned<PropPhysics>(EntityNames.PropPhysics, 0);

        ((IPhysGunTarget)prop).OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);

        Assert.Equal((1, 1, 0, 1), (prop.OnPhysGunPickup.FireCount, prop.OnPhysGunOnlyPickup.FireCount,
                                    prop.OnPhysGunPunt.FireCount, prop.OnPlayerPickup.FireCount));
    }

    [Fact]
    public void A_punted_prop_is_picked_up_and_punted_but_not_carried()
    {
        var prop = Spawned<PropPhysics>(EntityNames.PropPhysics, 0);

        ((IPhysGunTarget)prop).OnPhysGunPickup(Player, PhysGunPickup.PuntedByCannon);

        Assert.Equal((1, 0, 1, 0), (prop.OnPhysGunPickup.FireCount, prop.OnPhysGunOnlyPickup.FireCount,
                                    prop.OnPhysGunPunt.FireCount, prop.OnPlayerPickup.FireCount));
    }

    [Fact]
    public void A_prop_pinned_without_leave_from_the_gun_says_nothing()
    {
        var bolted = Spawned<PropPhysics>(EntityNames.PropPhysics, 8);
        var freed = Spawned<PropPhysics>(EntityNames.PropPhysics, 8 | 0x40);

        ((IPhysGunTarget)bolted).OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);
        ((IPhysGunTarget)freed).OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);

        Assert.Equal(0, bolted.OnPhysGunPickup.FireCount);
        Assert.Equal(1, freed.OnPhysGunPickup.FireCount);
        Assert.Equal(1, freed.OnMotionEnabled.FireCount);
    }

    [Fact]
    public void A_prop_dropped_by_the_gun_says_so()
    {
        var prop = Spawned<PropPhysics>(EntityNames.PropPhysics, 0);

        ((IPhysGunTarget)prop).OnPhysGunDrop(Player, PhysGunDrop.DroppedByCannon);

        Assert.Equal(1, prop.OnPhysGunDrop.FireCount);
    }

    [Fact]
    public void A_physbox_taken_by_the_gun_says_so_and_its_cannon_only_twin()
    {
        var box = Spawned<FuncPhysbox>(EntityNames.FuncPhysbox, 0);

        ((IPhysGunTarget)box).OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);

        Assert.Equal((1, 1, 0), (box.OnPhysGunPickup.FireCount, box.OnPhysGunOnlyPickup.FireCount,
                                 box.OnPhysGunPunt.FireCount));
    }

    [Fact]
    public void A_punted_physbox_says_punt_first_and_nothing_cannon_only()
    {
        var box = Spawned<FuncPhysbox>(EntityNames.FuncPhysbox, 0);

        ((IPhysGunTarget)box).OnPhysGunPickup(Player, PhysGunPickup.PuntedByCannon);

        Assert.Equal((1, 0, 1), (box.OnPhysGunPickup.FireCount, box.OnPhysGunOnlyPickup.FireCount,
                                 box.OnPhysGunPunt.FireCount));
    }

    [Fact]
    public void A_pinned_physbox_comes_loose_only_with_leave_from_the_gun()
    {
        var bolted = Spawned<FuncPhysbox>(EntityNames.FuncPhysbox, 0x8000);
        var freed = Spawned<FuncPhysbox>(EntityNames.FuncPhysbox, 0x8000 | 0x20000);

        ((IPhysGunTarget)bolted).OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);
        ((IPhysGunTarget)freed).OnPhysGunPickup(Player, PhysGunPickup.PickedUpByCannon);

        Assert.Equal((0, 0), (bolted.OnPhysGunPickup.FireCount, bolted.OnMotionEnabled.FireCount));
        Assert.Equal((1, 1), (freed.OnPhysGunPickup.FireCount, freed.OnMotionEnabled.FireCount));
        Assert.True(((IPhysGunTarget)freed).EnablesOnPhyscannon && !((IPhysGunTarget)bolted).EnablesOnPhyscannon);
    }

    [Fact]
    public void A_physbox_that_is_never_picked_up_refuses_the_gun_and_every_drop_says_so()
    {
        var never = Spawned<FuncPhysbox>(EntityNames.FuncPhysbox, 0x200000);
        var plain = Spawned<FuncPhysbox>(EntityNames.FuncPhysbox, 0);

        Assert.True(((IPhysGunTarget)never).PreventsPickup && !((IPhysGunTarget)plain).PreventsPickup);

        ((IPhysGunTarget)plain).OnPhysGunDrop(Player, PhysGunDrop.DroppedByCannon);
        Assert.Equal(1, plain.OnPhysGunDrop.FireCount);
    }
}
