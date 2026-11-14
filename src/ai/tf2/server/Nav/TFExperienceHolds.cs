using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using TF2.Server.Objectives;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// TF2's spot kinds, as the experience store's <c>spot</c> rows number them (plan_bots_teams_tf2_part_g G.3's list, in its
/// order). Only <see cref="Hold"/> is gathered so far (lane G4b); the rest are P7's classes'.
/// </summary>
public enum TFSpotKind
{
    Sentry = 1,
    Dispenser,
    TeleporterIn,
    TeleporterOut,
    SniperHold,
    StickyTrap,
    Hold,
}

/// <summary>
/// Where TF2 counts a hold (lane G4b, <see cref="IExperienceHolds"/>): within <see cref="Reach"/> of the side's own flag at
/// home (where it stood when the HUD last showed it home: a flag's row is where the flag is now), or of a control point
/// the side owns. Read on the game's thread, where the HUD's rows are.
/// </summary>
[SideService(Side.Server, As = [typeof(IExperienceHolds)])]
public sealed class TFExperienceHolds(ITFObjectives objectives) : IExperienceHolds
{
    /// <summary>How near its objective a hold counts: the flag room and the ways into it on ctf_2fort.</summary>
    public const float Reach = 1200f;

    private readonly Dictionary<int, AiVector> _flagHomes = new();
    private readonly List<(int Role, AiVector At)> _points = new();
    private int _version = -1;

    public int HoldKind => (int)TFSpotKind.Hold;

    public bool NearOwn(int role, AiVector at)
    {
        Read();
        float r2 = Reach * Reach;
        if (_flagHomes.TryGetValue(role, out AiVector home) && (home - at).LengthSquared <= r2)
            return true;
        foreach ((int owner, AiVector point) in _points)
        {
            if (owner == role && (point - at).LengthSquared <= r2)
                return true;
        }

        return false;
    }

    /// <summary>The flags' homes and the owned points, kept as the HUD's rows change.</summary>
    private void Read()
    {
        if (objectives.Version == _version)
            return;
        _version = objectives.Version;
        _points.Clear();
        foreach (TFObjective o in objectives.All)
        {
            var at = new AiVector(o.Position.X, o.Position.Y, o.Position.Z);
            if (o.Kind == TFObjectiveKind.Flag && o.State == TFObjectiveState.Home)
                _flagHomes[(int)o.Owner] = at;
            else if (o.Kind == TFObjectiveKind.ControlPoint && o.Owner is TFTeam.Red or TFTeam.Blue)
                _points.Add(((int)o.Owner, at));
        }
    }
}
