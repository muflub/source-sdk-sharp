using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Engineer's own building panel, as his HUD shows it (plan_bots_teams_tf2 P7, the Engineer research's §4.9.5): for
/// each of his four buildings (sentry, dispenser, teleporter entrance, exit) whether it stands, its panel flags (building,
/// built, sapped, carried: <see cref="TFEngineerPanel"/>), its level, health, construction or upgrade progress and its
/// ammo (a sentry's shells), out of sight as much as in it: the tray stock's <c>CHudBuildingStatusContainer</c> draws
/// (tf_hud_building_status.cpp), the same columns <see cref="TFBuildingResourceRows"/> networks. Its place too: he put it
/// there, so he knows where it stands. And whether a blueprint of his is out (<c>engineer.placing</c>, the type plus one)
/// or he is carrying a building (<c>engineer.carrying</c>): what his hands show.
///
/// <para>Honest by construction: it reads only the bot's own buildings (<see cref="ITFBuildings.OwnedBy"/> with the bot as
/// the builder), never another player's, and nothing the panel and his own hands do not show.</para>
/// </summary>
/// <para>Registered as itself: a side service joins every hook chain it implements (the <see cref="ITFHudMeters"/> chain
/// <see cref="TFHudRegistrar"/> adds), and the medigun's reader already claims the interface, so an <c>As</c> claim of it
/// refuses the side's composition. A chain allows one member at an order (StageUniqueness, at its first call): the medigun's
/// is 0, this one <see cref="Order"/>.</para>
[SideService(Side.Server)]
public sealed class TFEngineerMeters(ITFBuildings buildings, IEngineEntities entities) : ITFHudMeters, IOrderedHook
{
    /// <summary>The chain order: 100 plus the class's number (the coordinator's notice 1), so each class lane's reader has its own.</summary>
    public const int Order = 100 + (int)TFClass.Engineer;

    public int HookOrder => Order;

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass != TFClass.Engineer)
            return;
        int placing = 0;
        bool carrying = false;
        foreach (TFBaseObject b in buildings.OwnedBy(bot, null, null))
        {
            if (b.IsPlacing && !b.IsCarried)
            {
                placing = (int)b.Type + 1;
                continue;
            }

            int kind = TFEngineerPanel.KindOf(b.Type, b.Mode);
            if (kind < 0)
                continue;
            carrying |= b.IsCarried;
            int flags = TFEngineerPanel.Present
                        | (b.IsBuilding ? TFEngineerPanel.Building : 0)
                        | (b.IsBuilt ? TFEngineerPanel.Built : 0)
                        | (b.HasSapper || b.IsPlasmaDisabled ? TFEngineerPanel.Sapped : 0)
                        | (b.IsCarried ? TFEngineerPanel.Carried : 0)
                        | (b.IsMiniBuilding ? TFEngineerPanel.Mini : 0)
                        | (b.IsUpgrading ? TFEngineerPanel.Upgrading : 0);
            int progress = b.IsBuilt
                ? b.Level < TFBaseObject.MaxUpgradeLevel ? Math.Clamp(b.UpgradeMetal * 100 / Math.Max(1, b.RequiredUpgradeMetal), 0, 100) : 100
                : b.IsBuilding ? Math.Clamp(b.Health * 100 / Math.Max(1, b.MaxHealthForCurrentLevel), 0, 100) : 0;
            Vector o = entities.GetOrigin(b.Handle);
            string k = TFEngineerPanel.NameOf(kind);
            into.Add(new TFMeter(k + TFEngineerPanel.Flags, flags));
            into.Add(new TFMeter(k + TFEngineerPanel.Level, b.Level));
            into.Add(new TFMeter(k + TFEngineerPanel.Health, b.Health));
            into.Add(new TFMeter(k + TFEngineerPanel.MaxHealth, b.MaxHealthForCurrentLevel));
            into.Add(new TFMeter(k + TFEngineerPanel.Progress, progress));
            into.Add(new TFMeter(k + TFEngineerPanel.Ammo, b is TFSentryGun s ? s.Shells : 0));
            into.Add(new TFMeter(k + TFEngineerPanel.Kills, b.Kills));
            into.Add(new TFMeter(k + TFEngineerPanel.X, o.X));
            into.Add(new TFMeter(k + TFEngineerPanel.Y, o.Y));
            into.Add(new TFMeter(k + TFEngineerPanel.Z, o.Z));
        }

        into.Add(new TFMeter(TFEngineerPanel.Placing, placing));
        into.Add(new TFMeter(TFEngineerPanel.Carrying, carrying ? 1f : 0f));
    }
}

/// <summary>The Engineer's panel meters' names and flags (<see cref="TFEngineerMeters"/>), and one building's row read back off a HUD.</summary>
public static class TFEngineerPanel
{
    /// <summary>The four rows, the panel's order.</summary>
    public const int Sentry = 0, Dispenser = 1, Entrance = 2, Exit = 3, Kinds = 4;

    /// <summary>Each row's meter prefix.</summary>
    public static string NameOf(int kind) => kind switch
    {
        Sentry => "engineer.sentry",
        Dispenser => "engineer.dispenser",
        Entrance => "engineer.entrance",
        _ => "engineer.exit",
    };

    public const string Flags = ".flags", Level = ".level", Health = ".health", MaxHealth = ".maxhealth", Progress = ".progress",
                        Ammo = ".ammo", Kills = ".kills", X = ".x", Y = ".y", Z = ".z";

    /// <summary>The blueprint out (its <see cref="TFObjectType"/> plus one; 0 none), and whether a building rides in his arms (1 or 0).</summary>
    public const string Placing = "engineer.placing", Carrying = "engineer.carrying";

    public const int Present = 1, Building = 2, Built = 4, Sapped = 8, Carried = 16, Mini = 32, Upgrading = 64;

    /// <summary>The panel's row for a building of <paramref name="type"/> and <paramref name="mode"/>, or -1 (the sapper has none).</summary>
    public static int KindOf(TFObjectType type, int mode) => type switch
    {
        TFObjectType.Sentrygun => Sentry,
        TFObjectType.Dispenser => Dispenser,
        TFObjectType.Teleporter => mode == 0 ? Entrance : Exit,
        _ => -1,
    };

    /// <summary>Row <paramref name="kind"/> as <paramref name="hud"/> shows it; <see cref="TFOwnBuilding.Exists"/> false when it shows none.</summary>
    public static TFOwnBuilding Read(TFHud? hud, int kind)
    {
        if (hud is null || (uint)kind >= Kinds)
            return default;
        string k = NameOf(kind);
        int flags = (int)hud.Meter(k + Flags, 0f);
        if ((flags & Present) == 0)
            return default;
        return new TFOwnBuilding(kind, flags, (int)hud.Meter(k + Level, 1f), (int)hud.Meter(k + Health, 0f), (int)hud.Meter(k + MaxHealth, 1f),
                                 (int)hud.Meter(k + Progress, 0f), (int)hud.Meter(k + Ammo, 0f), (int)hud.Meter(k + Kills, 0f),
                                 new AiVector(hud.Meter(k + X, 0f), hud.Meter(k + Y, 0f), hud.Meter(k + Z, 0f)));
    }
}

/// <summary>One of the Engineer's own buildings as his panel shows it (<see cref="TFEngineerPanel.Read"/>).</summary>
public readonly record struct TFOwnBuilding(int Kind, int Flags, int Level, int Health, int MaxHealth, int Progress, int Ammo, int Kills, AiVector At)
{
    public bool Exists => (Flags & TFEngineerPanel.Present) != 0;
    public bool UnderConstruction => (Flags & TFEngineerPanel.Building) != 0;
    public bool Built => (Flags & TFEngineerPanel.Built) != 0;
    public bool Sapped => (Flags & TFEngineerPanel.Sapped) != 0;
    public bool Carried => (Flags & TFEngineerPanel.Carried) != 0;
    public bool Mini => (Flags & TFEngineerPanel.Mini) != 0;
    public bool Hurt => Health < MaxHealth;
}
