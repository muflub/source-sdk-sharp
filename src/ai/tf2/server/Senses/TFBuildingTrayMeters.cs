using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Shared.Buildings;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// The bot's own building tray, as its HUD draws it (round 2's S7): for each of its own buildings standing, whether a sapper
/// is on it (<c>tray.sentry.sapped</c>, <c>tray.dispenser.sapped</c>, <c>tray.entrance.sapped</c>, <c>tray.exit.sapped</c>,
/// 1 or 0). Stock's <c>CHudBuildingStatusContainer</c> shows a builder his own buildings and flags a sapped one
/// (<c>BUILDING_HUD_ALERT_SAPPER</c>, tf_hud_building_status.cpp), wherever he stands: so a builder knows his own buildings
/// sapped through walls, and nobody else's. It reads the rows of the bot's own slot in the carrier every client is sent
/// (<see cref="TFBuildingResourceNetState"/>), the ones its own tray draws, and no other slot's.
/// <para>Registered as itself (TFHeavyMeters' note). Its place in the chain is past every class reader's (100 + a class).</para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFBuildingTrayMeters(IEntityDirectory directory) : ITFHudMeters, IOrderedHook
{
    public const string SentrySapped = "tray.sentry.sapped", DispenserSapped = "tray.dispenser.sapped",
                        EntranceSapped = "tray.entrance.sapped", ExitSapped = "tray.exit.sapped";

    /// <summary>Past the class readers (100 + the class's number, at most 100 + <see cref="TFClass.CountAll"/>).</summary>
    public const int Order = 200;

    public int HookOrder => Order;

    /// <summary>The tray's meter of a building of <paramref name="type"/>, or null for a thing that is no building.</summary>
    public static string? MeterOf(TFThingType type) => type switch
    {
        TFThingType.Sentry => SentrySapped,
        TFThingType.Dispenser => DispenserSapped,
        TFThingType.TeleporterEntrance => EntranceSapped,
        TFThingType.TeleporterExit => ExitSapped,
        _ => null,
    };

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        int slot = bot.EntryIndex;
        if (slot <= 0 || slot >= TFBuildingResourceNetState.Slots)
            return;
        Span<byte> own = stackalloc byte[TFBuildingResourceNetState.Kinds];
        foreach (TFBuildingResourceEntity carrier in directory.OfClass(TFEntityNames.BuildingResource).OfType<TFBuildingResourceEntity>())
        {
            for (int kind = 0; kind < own.Length; kind++)
                own[kind] = carrier.State.Flags[slot * TFBuildingResourceNetState.Kinds + kind];
            Rows(own, into);
            return;
        }
    }

    /// <summary>The meter of the carrier's row kind <paramref name="kind"/> (<see cref="TFBuildingResourceNetState.Sentry"/> ...), or null.</summary>
    public static string? MeterOfRow(int kind) => kind switch
    {
        TFBuildingResourceNetState.Sentry => SentrySapped,
        TFBuildingResourceNetState.Dispenser => DispenserSapped,
        TFBuildingResourceNetState.Entrance => EntranceSapped,
        TFBuildingResourceNetState.Exit => ExitSapped,
        _ => null,
    };

    /// <summary>
    /// The meters of one slot's rows of the carrier's flags (<paramref name="own"/>, in the carrier's kind order): one per
    /// building standing (<see cref="TFBuildingResourceNetState.Present"/>), 1 with <see cref="TFBuildingResourceNetState.Sapped"/>.
    /// </summary>
    public static void Rows(ReadOnlySpan<byte> own, List<TFMeter> into)
    {
        for (int kind = 0; kind < own.Length; kind++)
        {
            if ((own[kind] & TFBuildingResourceNetState.Present) != 0 && MeterOfRow(kind) is { } meter)
                into.Add(new TFMeter(meter, (own[kind] & TFBuildingResourceNetState.Sapped) != 0 ? 1f : 0f));
        }
    }
}
