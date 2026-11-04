using SourceSharp.Abi;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Resources;

namespace TF2.Server.Objectives;

/// <summary>
/// The koth half of the feed (<see cref="ITFObjectiveSource"/>): the one
/// contested point of a koth map, as a KothPoint row — only while a
/// <see cref="KothLogic"/> stands (<see cref="IKothRoundState.InKothMode"/>),
/// the same flag the rules ask. It starts Locked behind the logic's unlock,
/// and UnlockIn is the seconds left on that unlock. Once open, the holder's
/// row carries its hold: the owner's <see cref="TeamRoundTimer"/> (the koth
/// hold-the-point timer the logic keeps per team) answers TimerLeft, and the
/// progress is that timer spent — the share of the hold time the holder has
/// banked. While the timer runs, the holder moves its own last-progress tick;
/// nobody else's.
/// </summary>
[SideService(Side.Server)]
public sealed class TF2KothObjectiveSource(IEntityDirectory directory, IEngineEntities entities,
                                           IKothRoundState koth, IObjectiveResource objectives, IGameClock clock)
    : ITFObjectiveSource, IOrderedHook
{
    /// <summary>The chain member's order in TF2's objectives band.</summary>
    public const int Order = TFObjectivesOrders.KothSource;

    private readonly IEntityDirectory _directory = directory;
    private readonly IEngineEntities _entities = entities;
    private readonly IKothRoundState _koth = koth;
    private readonly IObjectiveResource _objectives = objectives;
    private readonly IGameClock _clock = clock;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public void Collect(List<TFObjective> into)
    {
        if (!_koth.InKothMode)
            return;

        int timerLength = _directory.OfClass(KothLogic.MapClass).OfType<KothLogic>()
                                    .FirstOrDefault()?.Vars.TimerLength ?? 0;

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
            TeamRoundTimer? timer = _koth.KothTeamTimer(owner);
            float timerLeft = timer?.TimeRemaining ?? 0f;

            into.Add(new TFObjective(
                point.Handle.EntryIndex, TFObjectiveKind.KothPoint, owner, MayActOf(owner, capping, locked),
                StateOf(owner, capping, locked),
                _entities.GetOrigin(point.Handle), ProgressOf(capping, owner, timerLeft, timerLength, index),
                CarrierIndex: 0, UnlockIn: UnlockInOf(index), TimerLeft: timerLeft));
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

    /// <summary>The hold fraction of the holder's team timer; the cap fraction while one caps.</summary>
    private float ProgressOf(TFTeam capping, TFTeam owner, float timerLeft, int timerLength, int index)
    {
        if (IsGameTeam(capping))
        {
            float remaining = _objectives.GetCPCapPercentage(index);
            return Math.Clamp(1f - remaining, 0f, 1f);
        }

        if (!IsGameTeam(owner))
            return 0f;

        return timerLength > 0 ? Math.Clamp(1f - timerLeft / timerLength, 0f, 1f) : 1f;
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
