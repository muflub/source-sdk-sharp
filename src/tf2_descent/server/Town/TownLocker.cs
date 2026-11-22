//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Town;

/// <summary>What the town's resupply locker does to a hero who touches it: everything to full, and its sound to him.</summary>
public interface ITownResupply
{
    /// <summary>
    /// The hero in <paramref name="player"/> is resupplied as TF2's locker resupplies: life and mana
    /// to full (his own refill), every ammunition pool to its maximum and each weapon's clip to its
    /// default, the conditions a TF2 resupply ends (burning, bleeding, jarate...) ended, and the
    /// locker's sound played to him alone. False, and nothing done, when no living hero is there.
    /// </summary>
    bool Resupply(int player);
}

/// <summary>
/// The engine's <see cref="ITownResupply"/>: the hero's own refill
/// (<see cref="HeroComponent.RefillLifeAndMana"/>), TF2's ammunition maxima and clip reset
/// (<see cref="ITFPlayerResupply"/>, <see cref="ITFFreshWeapons"/>), its conditions and its
/// pickup sound.
/// </summary>
[SideService(Side.Server, As = [typeof(ITownResupply)])]
public sealed class EngineTownResupply(Heroes heroes, IEngineEntities entities, IEnginePlayers players, ITFPlayerResupply resupply,
                                       ITFFreshWeapons fresh, ITFAmmoPurse purse, Arsenal arsenal, ITFPlayerConditions conditions,
                                       ITFPickupNotices notices) : ITownResupply
{
    /// <summary>The pools a TF2 class carries, each filled to its maximum.</summary>
    private static ReadOnlySpan<TFAmmo> Pools => [TFAmmo.Primary, TFAmmo.Secondary, TFAmmo.Metal, TFAmmo.Grenades1, TFAmmo.Grenades2, TFAmmo.Grenades3];

    /// <summary>What TF2's resupply ends (<c>TFPlayerResupply.EndedByResupply</c>, stock's <c>Regenerate( true )</c>).</summary>
    private static ReadOnlySpan<TFCond> Ended =>
        [TFCond.Burning, TFCond.Urine, TFCond.MadMilk, TFCond.Gas, TFCond.Bleeding, TFCond.EnergyBuff, TFCond.Phase, TFCond.ParachuteActive, TFCond.Plague];

    public bool Resupply(int player)
    {
        if (heroes.Of(player) is not { Sheet: not null } hero)
            return false;
        EntityHandle p = hero.Player;
        if (p == EntityHandle.None || !entities.IsValid(p) || !players.IsAlive(p))
            return false;

        hero.RefillLifeAndMana();

        foreach (var cond in Ended)
        {
            if (!conditions.InCond(p, cond))
                continue;
            conditions.Remove(p, cond);
            if (cond == TFCond.EnergyBuff && conditions.InCond(p, TFCond.CannotSwitchFromMelee))
                conditions.Remove(p, TFCond.CannotSwitchFromMelee);
        }

        // The locker's own fill is the raw pool's, as TF2's resupply gives it (no pickup's metal toll).
        var pools = purse.Pools(p);
        foreach (var ammo in Pools)
        {
            string name = TFAmmoNames.Name(ammo);
            int missing = resupply.MaxAmmo(p, ammo) - pools.GetValueOrDefault(name);
            if (missing > 0)
                players.GiveAmmo(p, missing, name);
        }

        foreach (var (weapon, _, _) in arsenal.Holdings(p))
            fresh.Ready(p, weapon);

        notices.Sound(p, TownLocker.Sound);
        return true;
    }
}

/// <summary>
/// THE TOWN'S RESUPPLY LOCKER (the user, 2026-11-15: "the medical locker in town should work"): the
/// stash stall (<see cref="TownStalls"/>' <c>stash</c>, TF2's resupply locker) works as TF2's
/// <c>func_regenerate</c> beside its locker does. A hero who touches it, in town, is resupplied in
/// full (<see cref="ITownResupply"/>: life, mana, ammunition), its doors open and close again a
/// second before the next use, and he is not resupplied again for TF2's three seconds. Using it (+USE)
/// still opens the stash (<see cref="TownService.Use"/>).
/// <para>
/// The locker is solid, so a hero touches it by standing against it: his box within
/// <see cref="TouchSkin"/> of the locker's on every side. The cooldowns and the door's close are
/// stamped on the game clock, which starts again each level: both go at the level's end.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TownLocker(TownService town, ITownWorld world, ITownResupply resupply, Heroes heroes, ILog log)
    : IGameLifecycle, IFrameLifecycle, IClientLifecycle, IOrderedHook
{
    /// <summary>In TF2 Descent's town block, after the stalls (<see cref="TownService.Order"/>).</summary>
    public const int Order = ModStages.First + 1216;

    public int HookOrder => Order;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    public int ClientLifecycleOrder => Order;

    /// <summary>The stall that is the resupply locker.</summary>
    public const string Stall = TownService.LostAndFoundStall;

    /// <summary>TF2's <c>TF_REGENERATE_SOUND</c>.</summary>
    public const string Sound = "Regenerate.Touch";

    /// <summary>TF2's <c>TF_REGENERATE_NEXT_USE_TIME</c>: seconds before a hero is resupplied again.</summary>
    public const double NextUseTime = 3.0;

    /// <summary>The doors close this long before the next use (stock queues "close" a second short of it).</summary>
    public const double CloseAfter = NextUseTime - 1.0;

    /// <summary>How far from the locker's box a hero's box may stand and still touch it (it is solid: he stands against it).</summary>
    public const float TouchSkin = 16f;

    /// <summary>The locker's sequences (TF2's resupply_locker.mdl, which a map's func_regenerate sets).</summary>
    public const string OpenSequence = "open", CloseSequence = "close";

    private readonly Dictionary<int, double> _next = [];
    private double _closeAt = -1;
    private int _closeEntity;

    /// <summary>How many resupplies the locker has given on this level.</summary>
    public int Given { get; private set; }

    /// <summary>When the hero in <paramref name="player"/> may next be resupplied; null when he may now.</summary>
    public double? NextFor(int player) => _next.TryGetValue(player, out double next) ? next : null;

    public void OnLevelShutdown()
    {
        _next.Clear();
        _closeAt = -1;
        _closeEntity = 0;
        Given = 0;
    }

    public void OnClientDisconnected(int entIndex) => _next.Remove(entIndex);

    public void OnPostThink(float frameTime) => Check();

    /// <summary>Each hero touching the locker is resupplied unless he is cooling down; the doors close when their time comes.</summary>
    public void Check()
    {
        // Only the town places stalls: anywhere else there is no locker.
        if (Locker() is not { } locker)
            return;

        double now = world.Now;
        foreach (var hero in heroes.All)
        {
            int player = hero.PlayerIndex;
            if (player <= 0 || world.Feet(player) is not { } feet || !Touches(feet, locker.At))
                continue;
            if (_next.TryGetValue(player, out double next) && next > now && next - now <= NextUseTime)
                continue;
            if (!resupply.Resupply(player))
                continue;

            _next[player] = now + NextUseTime;
            Given++;
            world.Animate(locker.Entity, OpenSequence);
            _closeAt = now + CloseAfter;
            _closeEntity = locker.Entity;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Town] {player} touched the resupply locker: resupplied (life, mana, ammo), next at {now + NextUseTime:0.##}\n"));
        }

        if (_closeAt >= 0 && now >= _closeAt)
        {
            world.Animate(_closeEntity, CloseSequence);
            _closeAt = -1;
        }
    }

    private (int Entity, Vector At)? Locker()
    {
        int index = TownStalls.IndexOf(Stall);
        foreach (var placed in town.Placed)
            if (placed.Stall == index)
                return (placed.Entity, placed.At);
        return null;
    }

    /// <summary>Whether a hero whose feet are at <paramref name="feet"/> touches the locker standing at <paramref name="at"/>.</summary>
    public static bool Touches(Vector feet, Vector at)
    {
        var stall = TownStalls.All[TownStalls.IndexOf(Stall)];
        float reachX = Math.Max(-stall.Mins.X, stall.Maxs.X) + TownStalls.PlayerMaxs.X + TouchSkin;
        float reachY = Math.Max(-stall.Mins.Y, stall.Maxs.Y) + TownStalls.PlayerMaxs.Y + TouchSkin;
        float dz = feet.Z - at.Z;
        return MathF.Abs(feet.X - at.X) <= reachX && MathF.Abs(feet.Y - at.Y) <= reachY
               && dz >= stall.Mins.Z - TownStalls.PlayerMaxs.Z - TouchSkin && dz <= stall.Maxs.Z + TouchSkin;
    }
}
