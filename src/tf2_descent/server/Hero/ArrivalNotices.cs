using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// Notices held across a level change (<see cref="ClientMessages.NoticeAfterArrival"/>; the whole
/// run's F28): unhosted, the way down's, the way up's, the portal's and the town cage's notices
/// were sent just before this process's <c>changelevel</c> and never reached the HUD. On the
/// next level each is sent to its player once he has stood alive there for
/// <see cref="DelaySeconds"/>, so it lands after his HUD is up rather than in the load.
/// </summary>
[SideService(Side.Server)]
public sealed class ArrivalNotices(Heroes heroes, IDownedWorld players, ClientMessages messages)
    : IGameLifecycle, IFrameLifecycle
{
    /// <summary>In TF2 Descent's downed block, after the corpses.</summary>
    public const int Order = ModStages.First + 957;

    /// <summary>How long a player stands alive on the new level before his held notices are sent.</summary>
    public const float DelaySeconds = 3f;

    public int LifecycleOrder => Order;

    public int FrameOrder => Order;

    private readonly Dictionary<int, float> _alive = [];

    public void OnLevelInit(string mapName)
    {
        messages.LevelBegan();
        _alive.Clear();
    }

    public void OnThink(float frameTime)
    {
        if (!messages.ArrivalNoticesDue)
            return;
        foreach (var hero in heroes.All)
        {
            int index = hero.PlayerIndex;
            if (!players.IsAlive(hero.Player))
            {
                _alive.Remove(index);
                continue;
            }
            float alive = _alive[index] = _alive.GetValueOrDefault(index) + frameTime;
            if (alive >= DelaySeconds)
                messages.SendArrivalNotices(index);
        }
    }
}
