using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// A hero standing in a level's way down or up (map commit 218175fc0: the cage buttons and the
/// walked ways are <c>trigger_multiple</c>s with <c>"wait" "3"</c>, so they ask again every 3
/// seconds) is told a refusal once per stay: again only when its words change, or after he has
/// left and come back. A stay is a run of asks each within <see cref="StaySeconds"/> of the one
/// before it. The asks themselves all go through (a host that said "wait" may say "go" next).
/// </summary>
[SideService(Side.Server)]
public sealed class WayStays(ClientMessages messages, IGameClock clock)
{
    /// <summary>The ways' wait (3 s) and room for a slow frame: a longer gap is a new stay.</summary>
    public const float StaySeconds = 4.5f;

    private readonly Dictionary<int, float> _lastAsk = [];

    /// <summary>Runs one ask of a way by <paramref name="player"/>, its notices told once per stay.</summary>
    public void Ask(int player, Action ask)
    {
        float now = clock.Now;
        // A clock that went back (a new level starts it again) is a new stay.
        bool same = _lastAsk.TryGetValue(player, out float last) && now >= last && now - last <= StaySeconds;
        _lastAsk[player] = now;
        messages.BeginWay(player, same);
        try
        {
            ask();
        }
        finally
        {
            messages.EndWay();
        }
    }
}
