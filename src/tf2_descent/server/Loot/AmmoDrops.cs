using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Level;
using TF2.Server.Entities.Items;
using TF2.Shared;

namespace TF2.Descent.Server.Loot;

/// <summary>Where a robot's ammo pack goes: TF2's own dropped pack in the world (a fake in facts).</summary>
public interface IAmmoPackWorld
{
    /// <summary>Drops a TF2 ammo pack of <paramref name="pack"/> (<see cref="AmmoPacks"/>) at <paramref name="at"/>; answers it, or none when the engine refused.</summary>
    EntityHandle Drop(Vector at, string pack);
}

/// <summary>
/// The pack as TF2 drops one (<c>CTFPlayer::DropAmmoPack</c>, tf_player.cpp:13103-13107):
/// a <c>tf_ammo_pack</c> (<see cref="TFAmmoPack"/>), created, placed, its model named
/// before the spawn, spawned, then filled. Owned by nobody, so any hero may take it at once;
/// it fills the picker's primary and secondary by its pack's share of their maxima (the
/// map packs' ratios, <see cref="Ratio"/>) and is gone after TF2's
/// <see cref="TFAmmoPack.Lifetime"/>, as every dropped pack is.
/// </summary>
[SideService(Side.Server, As = [typeof(IAmmoPackWorld)])]
public sealed class EngineAmmoPackWorld(IEngineEntities entities, IEntityDirectory directory) : IAmmoPackWorld
{
    public EntityHandle Drop(Vector at, string pack)
    {
        var handle = entities.Create(TFAmmoPack.Class);
        if (handle == EntityHandle.None)
            return EntityHandle.None;

        string model = Model(pack);
        entities.SetOrigin(handle, at);
        entities.SetKeyValue(handle, "model", model);
        if (directory.Get(handle) is TFAmmoPack chosen)
            chosen.ChooseModel(model);
        entities.Spawn(handle);

        if (directory.Get(handle) is not TFAmmoPack made)
            return handle;

        float ratio = Ratio(pack);
        made.Init(EntityHandle.None, 0, TFClass.Undefined, 0, 0, Metal(ratio), ammoRatio: ratio);
        return handle;
    }

    /// <summary>The map packs' shares (<c>PackRatios</c>, tf_powerup.cpp): small a fifth, medium a half, full all.</summary>
    public static float Ratio(string pack) => pack switch
    {
        AmmoPacks.Small => 0.2f,
        AmmoPacks.Medium => 0.5f,
        _ => 1f,
    };

    /// <summary>The map packs' models (<c>item_ammopack_small</c>, <c>_medium</c>, <c>_full</c>).</summary>
    public static string Model(string pack) => pack switch
    {
        AmmoPacks.Small => "models/items/ammopack_small.mdl",
        AmmoPacks.Medium => "models/items/ammopack_medium.mdl",
        _ => "models/items/ammopack_large.mdl",
    };

    /// <summary>The metal a pack carries: its share of an engineer's 200, which the dropped pack clamps to 5..100.</summary>
    public static int Metal(float ratio) => (int)MathF.Ceiling(200 * ratio);
}

/// <summary>One kill's ammo drop: the pack's size, where it lies, and its entity.</summary>
public readonly record struct AmmoDrop(uint Kill, string Pack, Vector At, EntityHandle Entity);

/// <summary>
/// Killing things sometimes drops ammo (the user, 2026-11-15): on every robot death
/// (<see cref="IRobotKillHook"/>) beside the loot (<see cref="LootRewards"/>), the rules'
/// roll (<see cref="LootRules.Ammo"/>, seeded like the loot) may drop a TF2 ammo pack of its
/// size class's size on the floor where the robot fell (<see cref="ILootGround"/>; where it
/// died when there is no floor in reach). Not loot: no ledger, no seat, nothing kept, and the
/// kill's loot, Australium and XP are untouched.
/// </summary>
[SideService(Side.Server)]
public sealed class AmmoDrops(TF2DescentRules rules, ILevelData level, ILootGround ground, IAmmoPackWorld world, ILog log)
    : IRobotKillHook, IOrderedHook
{
    /// <summary>Beside the loot (<see cref="LootRewards.Order"/>, First + 310), after it.</summary>
    public const int Order = ModStages.First + 315;

    public int HookOrder => Order;

    /// <summary>The drops this level, and the last.</summary>
    public long Dropped { get; private set; }

    public AmmoDrop? Last { get; private set; }

    public void OnRobotKilled(in RobotKill kill) => Drop(kill.Sequence, LootKey.Of(kill.Template, kill.Affixes).ToString(), kill.Position);

    /// <summary>The pack kill <paramref name="sequence"/> of <paramref name="robotKey"/> at <paramref name="at"/> drops; null when the roll gives none.</summary>
    public AmmoDrop? Drop(uint sequence, string robotKey, Vector at)
    {
        int depth = level.Current is { Depth: > 0 } l ? l.Depth : 1;
        ulong seed = level.Current?.Seed ?? 0;
        if (rules.Loot.Ammo(seed, sequence, robotKey, depth, rules.Salt) is not { } pack)
            return null;

        var place = ground.TryGround(at, out var g) ? g : at;
        var entity = world.Drop(place, pack);
        if (entity == EntityHandle.None)
        {
            log.Warning($"[TF2Descent/Ammo] kill {sequence}: the engine refused a {pack} ammo pack\n");
            return null;
        }

        var drop = new AmmoDrop(sequence, pack, place, entity);
        Last = drop;
        Dropped++;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Ammo] kill {sequence} ({robotKey}): a {pack} ammo pack (entity {entity.EntryIndex}) at ({place.X:0} {place.Y:0} {place.Z:0})\n"));
        return drop;
    }
}
