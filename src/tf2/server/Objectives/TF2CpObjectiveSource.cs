using SourceSharp.Abi;

using TF2.Server.Entities;
using TF2.Shared;
using TF2.Shared.Resources;

namespace TF2.Server.Objectives;

/// <summary>
/// The control-point half of the feed (<see cref="ITFObjectiveSource"/>): one
/// ControlPoint row per active <see cref="TeamControlPoint"/>. Everything the
/// row answers comes from what the HUD's table already holds: the owner is the
/// point's owner, the capping team and the locked flag and the cap percentage
/// are the objective table's own fields, and the position is the point's origin
/// — never a second computation of them here. While the point is locked no team
/// may act on it yet; once open, the team capping it now is the team to move,
/// else its holder; an unowned open point stands for both teams. State is the
/// one word the AI decides on: Locked until its unlock, Capturing while a team
/// is capping it, Won once a team holds it, Open while it is free. Progress is
/// the capture itself: the fraction of the cap time spent while a team caps,
/// the full point held once it is owned. UnlockIn is the seconds left on the
/// point's unlock time.
/// </summary>
[SideService(Side.Server)]
public sealed class TF2CpObjectiveSource(IEntityDirectory directory, IEngineEntities entities,
                                         IObjectiveResource objectives, IGameClock clock)
    : ITFObjectiveSource, IOrderedHook
{
    /// <summary>The chain member's order in TF2's objectives band.</summary>
    public const int Order = TFObjectivesOrders.ControlPointSource;

    private readonly IEntityDirectory _directory = directory;
    private readonly IEngineEntities _entities = entities;
    private readonly IObjectiveResource _objectives = objectives;
    private readonly IGameClock _clock = clock;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public void Collect(List<TFObjective> into)
    {
        foreach (TeamControlPoint point in _directory.OfClass(TeamControlPoint.MapClass).OfType<TeamControlPoint>())
        {
            if (!point.IsActive)
                continue;

            int index = point.PointIndex;
            if (index < 0 || index >= _objectives.NumControlPoints)
                continue;

            TFTeam owner = (TFTeam)point.Owner;
            TFTeam capping = (TFTeam)_objectives.GetCappingTeam(index);
            bool locked = _objectives.GetCPLocked(index);

            into.Add(new TFObjective(
                point.Handle.EntryIndex, TFObjectiveKind.ControlPoint, owner, MayActOf(owner, capping, locked),
                StateOf(owner, capping, locked),
                _entities.GetOrigin(point.Handle), ProgressOf(capping, owner, index),
                CarrierIndex: 0, UnlockIn: UnlockInOf(index), TimerLeft: 0f));
        }
    }

    /// <summary>Who may act on the point now: locked holds everyone, else whoever is capping it, else its holder.</summary>
    private static TFTeam MayActOf(TFTeam owner, TFTeam capping, bool locked)
    {
        if (locked)
            return TFTeam.Unassigned;

        if (IsGameTeam(capping))
            return capping;

        return IsGameTeam(owner) ? owner : TFTeam.Unassigned;
    }

    private static TFObjectiveState StateOf(TFTeam owner, TFTeam capping, bool locked)
    {
        if (locked)
            return TFObjectiveState.Locked;
        if (IsGameTeam(capping))
            return TFObjectiveState.Capturing;
        return IsGameTeam(owner) ? TFObjectiveState.Won : TFObjectiveState.Open;
    }

    /// <summary>The capture fraction: the cap time spent while capping; the whole point once held.</summary>
    private float ProgressOf(TFTeam capping, TFTeam owner, int index)
    {
        if (IsGameTeam(capping))
        {
            float remaining = _objectives.GetCPCapPercentage(index);
            return Math.Clamp(1f - remaining, 0f, 1f);
        }

        return IsGameTeam(owner) ? 1f : 0f;
    }

    /// <summary>The seconds left on the point's unlock time, 0 once unlocked.</summary>
    private float UnlockInOf(int index)
    {
        float now = _clock.Now;
        float when = _objectives.GetCPUnlockTime(index);
        return when > now ? when - now : 0f;
    }

    private static bool IsGameTeam(TFTeam team) => team >= TFTeam.Red;
}
