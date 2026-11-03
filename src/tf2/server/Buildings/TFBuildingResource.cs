//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Buildings;
using TF2.Shared.Player;

namespace TF2.Server.Buildings;

/// <summary>
/// The building HUD's columns, written into <see cref="TFBuildingResourceNetState"/>
/// from the live buildings (<see cref="ITFBuildings"/>): stock networks each
/// building and <c>C_BuildingStatusItem</c> reads the entity; ours has no
/// building on the wire, so the server carries what hud_obj_*.res draw — the
/// health bar pair (<c>CBuildingHealthBar</c>,
/// tf_hud_building_status.cpp:56-68), <c>GetPercentageConstructed</c>
/// (tf_obj.cpp) for <c>BuildingProgress</c>, the sentry's shells
/// (hud_obj_sentrygun.res:342-358) and the dispenser's metal
/// (hud_obj_dispenser.res:274-290) — and the columns this lane added: the
/// kills and assists (<c>KillsLabel</c>, hud_obj_sentrygun.res:307-324), the
/// upgrade metal against <see cref="TFBaseObject.RequiredUpgradeMetal"/>
/// riding <see cref="TFBuildingResourceNetState.BuildProgress"/> once built,
/// and the entrance's recharge clock and trips
/// (<see cref="TFBuildingResourceTeleporter"/>). Each pass clears every row
/// first, the way stock's <c>UpdateDisconnectedPlayer</c> inits.
/// </summary>
[SideService(Side.Server)]
public sealed class TFBuildingResourceRows(ITFBuildings buildings, IEnginePlayers players)
{
    private readonly int[] _slots = new int[TFBuildingResourceNetState.Slots];

    /// <summary>Every row, at <paramref name="now"/>.</summary>
    public void Update(TFBuildingResourceNetState state, float now)
    {
        for (int i = 0; i < TFBuildingResourceNetState.Rows; i++)
            state.Flags[i] = 0;

        foreach (TFBaseObject building in buildings.OwnedBy())
        {
            int kind = KindOf(building);
            if (kind < 0)
                continue;

            int slot = SlotOf(building.Builder);
            if (slot <= 0)
                continue;

            int row = slot * TFBuildingResourceNetState.Kinds + kind;
            byte flags = TFBuildingResourceNetState.Present;
            if (building.IsBuilding)
                flags |= TFBuildingResourceNetState.Building;
            if (building.IsBuilt)
                flags |= TFBuildingResourceNetState.Built;
            if (building.HasSapper)
                flags |= TFBuildingResourceNetState.Sapped;
            if (building.IsCarried)
                flags |= TFBuildingResourceNetState.Carried;
            if (building is TFTeleporter { Mode: 0 } entrance && entrance.State == TFTeleporterState.Recharging)
                flags |= TFBuildingResourceNetState.Recharging;

            // The one progress column: construction while building, the
            // upgrade's metal once built — hud_obj_*.res never shows the two
            // together (BuildingPanel against RunningPanel), the same spare
            // the sentry's pod rides.
            int maxHealth = building.MaxHealthForCurrentLevel;
            int progress = building.IsBuilt
                ? building.Level < TFBaseObject.MaxUpgradeLevel
                    ? Math.Clamp(building.UpgradeMetal * 100 / Math.Max(1, building.RequiredUpgradeMetal), 0, 100)
                    : 100
                : building.IsBuilding
                    ? Math.Clamp((building.Health - (int)TFBaseObject.ConstructionStartHealth) * 100
                                 / Math.Max(1, maxHealth - (int)TFBaseObject.ConstructionStartHealth), 0, 100)
                    : 0;   // placing: not paid for, the HUD's not-built panel owns it

            state.Flags[row] = flags;
            state.Level[row] = (byte)building.Level;
            state.BuildProgress[row] = (byte)progress;
            state.Health[row] = (byte)Math.Min(building.Health, byte.MaxValue);
            state.MaxHealth[row] = (byte)Math.Min(maxHealth, byte.MaxValue);
            state.Kills[row] = (byte)Math.Min(building.Kills, byte.MaxValue);
            state.Assists[row] = (byte)Math.Min(building.Assists, byte.MaxValue);

            (state.Ammo[row], state.MaxAmmo[row]) = building switch
            {
                TFSentryGun sentry => (
                    (short)TFBuildingResourceAmmo.Pack(sentry.Shells, sentry.Rockets),
                    (short)TFBuildingResourceAmmo.Pack(sentry.MaxShells, sentry.MaxRockets)),
                TFDispenser dispenser => ((short)dispenser.Metal, (short)TFDispenser.MaxMetal),

                // The entrance's clock: the recharge percentage while
                // TF_OBJ_STATE_RECHARGING (hud_obj_tele_entrance's ChargingPanel
                // against FullyChargedPanel), the times-used packed beside it.
                TFTeleporter teleporter => EntranceColumns(teleporter, now, flags),
                _ => ((short)0, (short)0),
            };
        }
    }

    /// <summary>The entrance's packed ammo: the recharge percentage — stock's
    /// remaining-charge fraction against the level's recharge alone
    /// (hud_obj_tele_entrance's <c>Recharge</c>), the fades stock leaves out
    /// of the denominator making the early read run past full, clamped —
    /// and the trip count for <c>TimesUsedLabel</c>.</summary>
    private static (short Ammo, short MaxAmmo) EntranceColumns(TFTeleporter entrance, float now, byte flags)
    {
        int progress = 0;
        if ((flags & TFBuildingResourceNetState.Recharging) != 0)
        {
            float whole = TFTeleporter.RechargeTimeForLevel(entrance.Level);
            float remains = Math.Max(0f, entrance.StateEndsAt - now);
            progress = Math.Clamp((int)MathF.Round((1f - remains / whole) * 100f), 0, 100);
        }

        return ((short)TFBuildingResourceTeleporter.Pack(progress, entrance.TimesUsed), 100);
    }

    /// <summary>The row kind, or -1 for a building the HUD has no row for (the sapper).</summary>
    private static int KindOf(TFBaseObject building) => building.Type switch
    {
        TFObjectType.Sentrygun => TFBuildingResourceNetState.Sentry,
        TFObjectType.Dispenser => TFBuildingResourceNetState.Dispenser,
        TFObjectType.Teleporter => building.Mode == 0 ? TFBuildingResourceNetState.Entrance : TFBuildingResourceNetState.Exit,
        _ => -1,
    };

    /// <summary>The builder's slot: the player whose handle the builder is (stock keeps <c>m_hBuilder</c> on the entity).</summary>
    private int SlotOf(EntityHandle builder)
    {
        if (builder == EntityHandle.None)
            return 0;

        for (int i = 1; i < TFBuildingResourceNetState.Slots; i++)
        {
            if (players.GetHandle(i) == builder)
                return i;
        }

        return 0;
    }
}

/// <summary>
/// <c>tf_building_resource</c>: <see cref="TFBuildingResourceNetState"/> on the
/// hierarchy, always transmitted, its rows refreshed every
/// <see cref="ThinkInterval"/> seconds (stock's buildings push on change; a
/// carrier pushes on the tick, and 0.5 s is the stock building HUD's own
/// data cadence). Made by <see cref="TFBuildingResourceSpawner"/> at level
/// init, never by a map.
/// </summary>
[ScriptedEntity(TFEntityNames.BuildingResource, ScriptedEntityKind.Effect)]
public sealed class TFBuildingResourceEntity(TFBuildingResourceRows rows, IEngineEntityRender render, IGameClock clock)
    : ScriptedEntity
{
    /// <summary>The carrier's cadence; the HUD's own tick is stock's 0.1 s client-side.</summary>
    public const float ThinkInterval = 0.5f;

    /// <summary>What every client is told. Bound to this entity by the registry.</summary>
    [NetState] public TFBuildingResourceNetState State = new();

    public override void OnSpawn()
    {
        render.SetTransmitAlways(Handle, true);
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        rows.Update(State, clock.Now);
        SetNextThink(ThinkInterval);
    }
}

/// <summary>Creates <c>tf_building_resource</c> as each level loads.</summary>
[SideService(Side.Server)]
public sealed class TFBuildingResourceSpawner(IEngineEntities entities, ILog log) : IGameLifecycle
{
    public int LifecycleOrder => TFPlayerOrders.BuildingResource;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        var entity = entities.Create(TFEntityNames.BuildingResource);
        if (entity == EntityHandle.None)
        {
            log.Warning($"[TF2/Buildings] could not create {TFEntityNames.BuildingResource}; the building status reads no columns\n");
            return;
        }

        entities.Spawn(entity);
        log.Msg($"[TF2/Buildings] {TFEntityNames.BuildingResource} up\n");
    }
}
