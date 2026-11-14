using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;

namespace TF2.Descent.Client.Ui;

/// <summary>How long a server tick is, in seconds: what the downed clock's ticks are measured in.</summary>
public interface IClientTickInterval
{
    /// <summary>Seconds a tick (<c>gpGlobals-&gt;interval_per_tick</c>); 0 before the engine hands its globals over.</summary>
    float Seconds { get; }
}

/// <summary><see cref="IClientTickInterval"/> over the engine's globals.</summary>
[SideService(Side.Client, As = [typeof(IClientTickInterval)])]
public sealed class EngineClientTickInterval(IEngineEnvironment environment) : IClientTickInterval
{
    public float Seconds => environment.Globals.IsAvailable ? environment.Globals.IntervalPerTick : 0f;
}

/// <summary>
/// The downed overlay (<see cref="IDescentDowned"/>, plan_descent_logic UI7)
/// over the local hero's networked state (WO6): down or not; the bleed-out's
/// seconds left and total from its ticks against the client's tick, as the
/// cast bar measures its charge; the reviver's name (the engine's player
/// info) and how far through his hold he is; and, when nobody is reviving,
/// the nearest party member up and his distance (the party frames' own
/// numbers). Up, dead, or no hero: not down, and every value empty.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentDowned)])]
public sealed class DownedSource(IHeroStates heroes, IDescentParty party, IPlayerNames names, IClientTicks ticks,
                                 IClientTickInterval interval) : IDescentDowned
{
    private HeroState? Down => heroes.Local() is { Downed: true } hero ? hero : null;

    public bool Downed => Down is not null;

    public float BleedOutSecondsLeft =>
        Down is { } h ? Math.Max(0, h.DownedEndTick - ticks.Now) * interval.Seconds : 0f;

    public float BleedOutTotal =>
        Down is { } h ? Math.Max(0, h.DownedEndTick - h.DownedStartTick) * interval.Seconds : 0f;

    public bool BeingRevived => Down is { ReviverIndex: > 0 };

    public string ReviverName =>
        Down is { ReviverIndex: > 0 } h && names.TryGet(h.ReviverIndex, out var info) ? info.Name : "";

    public float ReviveFraction =>
        Down is { ReviverIndex: > 0 } h && h.ReviveEndTick > h.ReviveStartTick
            ? Math.Clamp((ticks.Now - h.ReviveStartTick) / (float)(h.ReviveEndTick - h.ReviveStartTick), 0f, 1f)
            : 0f;

    public string NearestMemberName => Nearest()?.Name ?? "";

    public int NearestMemberDistance => Nearest()?.DistanceUnits ?? 0;

    /// <summary>The nearest other member up, by the party frames' distance; null when down alone (or not down).</summary>
    private DescentPartyMember? Nearest()
    {
        if (!Downed)
            return null;
        DescentPartyMember? best = null;
        int count = party.MemberCount;
        for (int i = 0; i < count; i++)
        {
            var m = party.Member(i);
            if (m.Local || m.Downed || m.Life <= 0)
                continue;
            if (best is not { } b || m.DistanceUnits < b.DistanceUnits)
                best = m;
        }
        return best;
    }
}
