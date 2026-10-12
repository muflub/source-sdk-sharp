using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_physbox_multiplayer, which is func_physbox under a second name.
///
/// A subclass that adds nothing is exactly the sort of thing that gets written
/// and never registered, so what these check is that the classname reaches a
/// managed entity at all and that the base's behaviour arrives with it — the
/// keyvalues are declared as private fields on FuncPhysbox, and private fields
/// of a base class are the ones reflection quietly drops.
/// </summary>
public class PhysboxMultiplayerTests
{

    private static FuncPhysboxMultiplayer Spawn(uint index, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        registry.Services = Ignoring("Entities.GetHealth", "EntityPhysics.InitPhysics");

        var handle = new EntityHandle(index);

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.FuncPhysboxMultiplayer, key, value);

        registry.Spawn(handle, EntityNames.FuncPhysboxMultiplayer);
        return (FuncPhysboxMultiplayer)registry.Get(handle)!;
    }

    [Fact]
    public void ClassnameResolves()
    {
        // The registration, not the class: a [ScriptedEntity] nobody scanned is
        // a native classname with no factory, which is a silent "entity not
        // found" at map load.
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        registry.Services = Ignoring("Entities.GetHealth", "EntityPhysics.InitPhysics");

        var handle = new EntityHandle(40);
        registry.Spawn(handle, EntityNames.FuncPhysboxMultiplayer);

        Assert.True(registry.Get(handle) is FuncPhysboxMultiplayer, "func_physbox_multiplayer resolves to a managed entity");

        // And it is genuinely the base class, which is the whole claim the port
        // makes. Nothing else about it is observable.
        Assert.True(registry.Get(handle) is FuncPhysbox, "and it is a func_physbox");
    }

    [Fact]
    public void InheritsTheDamageRules()
    {
        // "health" and "minhealthdmg" are private fields on FuncPhysbox, so
        // this fails outright if the keyvalue walk stops at the derived type.
        var box = Spawn(41, ("health", "60"), ("minhealthdmg", "20"));

        Assert.True(box.OnDamaged(EntityHandle.None, 5f, DamageType.Bullet) == 0, "a base-class keyvalue still reaches the subclass");

        Assert.True(box.OnDamaged(EntityHandle.None, 25f, DamageType.Bullet) == 25, "and damage over the minimum is taken");

        Assert.True(box.Damaged.FireCount == 1, "the base's output fired from the subclass");
    }

    [Fact]
    public void DeclaresNoStateOfItsOwn()
    {
        // The standalone blob is one size shared by every class on the
        // hierarchy, so a networked field here is charged to every entity in
        // the map. Stock networked m_iPhysicsMode and m_fMass for an interface
        // nothing in this build calls, and this asserts the decision not to
        // follow it — if someone adds them later, they should have to change
        // this line and say why.
        var declared = typeof(FuncPhysboxMultiplayer).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly);

        Assert.True(declared.Length == 0, "func_physbox_multiplayer adds no fields of its own");
    }
}
