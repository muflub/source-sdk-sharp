using SourceSharp.Abi;

using TF2.Server.Entities.Ctf;
using TF2.Shared;
using TF2.Shared.Ctf;

namespace TF2.Server.Objectives;

/// <summary>
/// The CTF half of the feed (<see cref="ITFObjectiveSource"/>): one Flag row per
/// <see cref="ItemTeamFlag"/>, one CaptureZone row per
/// <see cref="FuncCaptureZone"/>. The flag's owner is the flag's team and the
/// team that may act on it is the other one; its position is the home spot
/// while it waits there, the entity's own place once carried; its progress is
/// the home/carried pair, and its timer is the dropped flag's return
/// countdown.
/// </summary>
[SideService(Side.Server)]
public sealed class TF2CtfObjectiveSource(IEntityDirectory directory, IEngineEntities entities, IGameClock clock)
    : ITFObjectiveSource, IOrderedHook
{
    /// <summary>The chain member's order in TF2's objectives band.</summary>
    public const int Order = TFObjectivesOrders.CtfSource;

    private readonly IEntityDirectory _directory = directory;
    private readonly IEngineEntities _entities = entities;
    private readonly IGameClock _clock = clock;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public void Collect(List<TFObjective> into)
    {
        foreach (ItemTeamFlag flag in _directory.OfClass(ItemTeamFlag.MapClass).OfType<ItemTeamFlag>())
        {
            EntityHandle carrier = flag.Carrier;

            into.Add(new TFObjective(
                flag.Handle.EntryIndex, TFObjectiveKind.Flag, flag.Team, OtherTeam(flag.Team),
                StateOf(flag.Status),
                flag.IsHome ? flag.HomeOrigin : _entities.GetOrigin(flag.Handle),
                flag.IsHome ? 0f : 1f,
                carrier != EntityHandle.None ? carrier.EntryIndex : 0,
                UnlockIn: 0f,
                TimerLeftOf(flag)));
        }

        foreach (FuncCaptureZone zone in _directory.OfClass(FuncCaptureZone.MapClass).OfType<FuncCaptureZone>())
        {
            into.Add(new TFObjective(
                zone.Handle.EntryIndex, TFObjectiveKind.CaptureZone, zone.Team, TFTeam.Unassigned,
                zone.IsDisabled ? TFObjectiveState.Locked : TFObjectiveState.Open,
                _entities.GetOrigin(zone.Handle), zone.IsDisabled ? 0f : 1f,
                CarrierIndex: 0, UnlockIn: 0f, TimerLeft: 0f));
        }
    }

    private static TFObjectiveState StateOf(TFFlagStatus status) => status switch
    {
        TFFlagStatus.Stolen => TFObjectiveState.Carried,
        TFFlagStatus.Dropped => TFObjectiveState.Dropped,
        _ => TFObjectiveState.Home,
    };

    /// <summary>The enemy of a game team; an unassigned flag is open to both.</summary>
    private static TFTeam OtherTeam(TFTeam team) => team switch
    {
        TFTeam.Red => TFTeam.Blue,
        TFTeam.Blue => TFTeam.Red,
        _ => TFTeam.Unassigned,
    };

    private float TimerLeftOf(ItemTeamFlag flag)
    {
        float now = _clock.Now;
        float when = flag.ResetTime;
        return when > now ? when - now : 0f;
    }
}
