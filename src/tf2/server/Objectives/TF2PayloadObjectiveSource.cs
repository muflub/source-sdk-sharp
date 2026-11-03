using SourceSharp.Abi;

using TF2.Server.Entities;
using TF2.Shared;

namespace TF2.Server.Objectives;

/// <summary>
/// The payload half of the feed (<see cref="ITFObjectiveSource"/>): one Cart row
/// per <see cref="TeamTrainWatcher"/>. The cart's owner is the pushing team the
/// watcher answers for, and that same team may act on it; its state is the
/// speed pip — Moving forward, RollingBack under the recede, Open at a stand,
/// Locked when disabled; its progress is the watcher's path fraction and its
/// position the cart's own place.
/// </summary>
[SideService(Side.Server)]
public sealed class TF2PayloadObjectiveSource(IEntityDirectory directory, IEngineEntities entities)
    : ITFObjectiveSource, IOrderedHook
{
    /// <summary>The chain member's order in TF2's objectives band.</summary>
    public const int Order = TFObjectivesOrders.PayloadSource;

    private readonly IEntityDirectory _directory = directory;
    private readonly IEngineEntities _entities = entities;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public void Collect(List<TFObjective> into)
    {
        foreach (TeamTrainWatcher watcher in _directory.OfClass(TeamTrainWatcher.MapClass).OfType<TeamTrainWatcher>())
        {
            into.Add(new TFObjective(
                watcher.Handle.EntryIndex, TFObjectiveKind.Cart, OwnerOf(watcher), OwnerOf(watcher),
                StateOf(watcher),
                _entities.GetOrigin(watcher.GetTrainEntity()?.Handle ?? watcher.Handle),
                watcher.GetTrainProgress(),
                CarrierIndex: 0, UnlockIn: 0f, TimerLeft: 0f));
        }
    }

    private static TFTeam OwnerOf(TeamTrainWatcher watcher) => (TFTeam)watcher.Vars.TeamNum;

    /// <summary>The HUD's speed pip, one word: a disabled cart is locked, the rest read the level's sign.</summary>
    private static TFObjectiveState StateOf(TeamTrainWatcher watcher) => watcher.IsDisabled
        ? TFObjectiveState.Locked
        : watcher.TrainSpeedLevel switch
        {
            < 0 => TFObjectiveState.RollingBack,
            > 0 => TFObjectiveState.Moving,
            _ => TFObjectiveState.Open,
        };
}
