using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Weapons;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFPDAItemSwitch"/>: the PDA's trigger hands the owner back
/// through the core's arsenal — <c>CTFWeaponPDA::PrimaryAttack</c>'s one
/// <c>SelectLastItem</c> (<c>shared/tf/tf_weapon_pda.cpp:89-98</c>) over the
/// last-item history <c>SwitchLast</c> rides. The fact arms a real arsenal
/// with two carried weapons and asks the owner to make the walk, so a switch
/// that never reaches this player — the handle asked for is not his — fails it.
/// </summary>
public sealed class TFPDAItemSwitchTests
{
    private const int Owner = TFWeaponWorld.Owner;

    private static readonly EntityHandle Player = new(0x0001_0000u | Owner);
    private static readonly EntityHandle SomeoneElse = new(0x0001_0000u | (Owner + 1));

    /// <summary>What is in the hands, by classname — the same list the core's selection facts read.</summary>
    private static string Held(Arsenal arsenal)
    {
        foreach (var (className, _, active) in arsenal.Holdings(Player))
            if (active)
                return className;

        return "";
    }

    /// <summary>
    /// An arsenal over the shipped TF rows with two weapons carried and the
    /// secondary in the hands, which is the state the PDA sits on: the primary
    /// was held, then the secondary became the last switch.
    /// </summary>
    private static (TFPDAItemSwitch OwnerOf, Arsenal Arsenal, TFWeaponWorld World) Carried()
    {
        var world = new TFWeaponWorld();

        var engine = TestServices.Mock()
            .Returns("Entities.IsValid", true).Returns("Entities.GetIndex", Owner)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            // Where a dropped weapon lands; switching reaches the trace either way.
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f), new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("Clock.Now", 10f)
            .Ignores("EntityRender.LookupSequenceByActivity");

        // Only THIS player lives, and the players line maps only his index to a
        // handle: a switch asked for on anyone else's index is refused rather
        // than quietly landing on someone's hands.
        engine.ReturnsEachCall("Players.IsAlive", () => engine.Calls[^1].ArgAs<EntityHandle>(0) == Player);
        engine.ReturnsEachCall("Players.GetHandle", () => engine.Calls[^1].ArgAs<int>(0) == Owner ? Player : SomeoneElse);

        // The arsenal's carried pair, spawned where the arsenal's Create will
        // name them: the pair is made on the first deploy and reused by every
        // switch after it (the weapon entity's state is rewritten each time).
        var weapon = new EntityHandle(0x0500u);
        var viewModel = new EntityHandle(0x0501u);
        var created = new Queue<EntityHandle>([weapon, viewModel]);
        engine.ReturnsEachCall("Entities.Create", () => created.Dequeue());

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = TestServices.Services(out _);
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        registry.Spawn(weapon, EntityNames.SsWeapon);
        registry.Spawn(viewModel, EntityNames.PredictedViewModel);

        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(),
                                  weapons: world.Catalog, ammo: world.Ammo);

        Assert.True(arsenal.GiveWeapon(Player, "tf_weapon_scattergun"), "the arsenal gives the scattergun");
        Assert.True(arsenal.GiveWeapon(Player, "tf_weapon_pistol"), "and the pistol");
        Assert.True(arsenal.SwitchSlot(Player, 1) == SwitchOutcome.Switched, "the pistol is in the hands");
        Assert.Equal("tf_weapon_pistol", Held(arsenal));

        return (new TFPDAItemSwitch(arsenal, engine), arsenal, world);
    }

    private static WeaponAttackContext PdaAttack(TFWeaponWorld world)
    {
        var (w, vm) = world.Armed("tf_weapon_pda_spy", clip: -1, reserve: 0);
        return world.Attack(w, vm, now: 10f);
    }

    [Fact]
    public void The_trigger_sends_the_owner_back_to_the_item_held_before_the_pda()
    {
        var (owner, arsenal, world) = Carried();

        owner.SelectLast(PdaAttack(world), WeaponFixtures.Parse("tf_weapon_pda_spy"));

        Assert.Equal("tf_weapon_scattergun", Held(arsenal));
    }

    /// <summary>
    /// The walk belongs to this owner alone: a second player with their own
    /// hands is untouched by the first one's trigger (the handle the switch
    /// asks for comes from the owner index, not from anyone else's).
    /// </summary>
    [Fact]
    public void The_trigger_switches_the_owners_hands_and_nobody_elses()
    {
        var (owner, arsenal, world) = Carried();

        // The pistol went back to the scattergun...
        owner.SelectLast(PdaAttack(world), WeaponFixtures.Parse("tf_weapon_pda_spy"));
        Assert.Equal("tf_weapon_scattergun", Held(arsenal));

        // ...and back again: the trigger toggles, as the lastinv bind does.
        owner.SelectLast(PdaAttack(world), WeaponFixtures.Parse("tf_weapon_pda_spy"));
        Assert.Equal("tf_weapon_pistol", Held(arsenal));
    }
}
