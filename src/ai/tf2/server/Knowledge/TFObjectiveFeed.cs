using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Entities.Ctf;
using TF2.Server.Objectives;
using TF2.Server.Rules;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// The map's objectives as every TF2 HUD shows them (plan_bots_teams_tf2 C.6, P6 step 5): the
/// mod's feed (<see cref="ITFObjectives"/>: flags home, carried or dropped, capture zones, points,
/// carts) in the core's kinds and the game's team numbers, with the round's setup. Rebuilt only when
/// the feed's version or the setup changes, so every bot of a tick reads the same object. A capture
/// zone is a brush: its place is the middle of its volume, where a carrier walks to, not the
/// brush entity's origin (often the world's).
/// </summary>
[SideService(Side.Server)]
public sealed class TFObjectiveFeed(ITFObjectives feed, ITFRoundFacts round, IEntityDirectory directory, IEngineEntities entities)
{
    private ObjectiveKnowledge? _current;
    private int _version = -1;
    private bool _setup;

    /// <summary>The objectives now.</summary>
    public ObjectiveKnowledge Current
    {
        get
        {
            int version = feed.Version;
            bool setup = round.InSetup;
            if (_current is null || version != _version || setup != _setup)
            {
                _current = Build(feed.All, version, setup, ZoneCentre);
                (_version, _setup) = (version, setup);
            }

            return _current;
        }
    }

    private AiVector? ZoneCentre(int id)
    {
        foreach (FuncCaptureZone zone in directory.OfClass(FuncCaptureZone.MapClass).OfType<FuncCaptureZone>())
        {
            if (zone.Handle.EntryIndex == id)
            {
                Vector c = entities.GetWorldSpaceCenter(zone.Handle);
                return new AiVector(c.X, c.Y, c.Z);
            }
        }

        return null;
    }

    /// <summary>
    /// The core's objectives for the feed's <paramref name="all"/>: each record one objective of
    /// the same id, in the same order; a capture zone at <paramref name="zoneCentre"/>'s place when
    /// it knows one.
    /// </summary>
    public static ObjectiveKnowledge Build(ReadOnlySpan<TFObjective> all, int version, bool setup, Func<int, AiVector?> zoneCentre)
    {
        var objectives = ImmutableArray.CreateBuilder<Objective>(all.Length);
        foreach (ref readonly TFObjective o in all)
        {
            AiVector at = new(o.Position.X, o.Position.Y, o.Position.Z);
            if (o.Kind == TFObjectiveKind.CaptureZone && zoneCentre(o.Id) is { } centre)
                at = centre;
            objectives.Add(new Objective(o.Id, KindOf(o.Kind), (int)o.Owner, MayActOf(o.MayAct), StateOf(o.State), at,
                                         o.Progress, o.CarrierIndex, o.UnlockIn, o.TimerLeft, setup));
        }

        return new ObjectiveKnowledge(objectives.MoveToImmutable(), version);
    }

    /// <summary>A flag is carried, its zone delivered to, a point captured, a cart pushed, a hill held.</summary>
    public static ObjectiveKind KindOf(TFObjectiveKind kind) => kind switch
    {
        TFObjectiveKind.Flag => ObjectiveKind.Carry,
        TFObjectiveKind.CaptureZone => ObjectiveKind.Deliver,
        TFObjectiveKind.ControlPoint => ObjectiveKind.Capture,
        TFObjectiveKind.Cart => ObjectiveKind.Push,
        TFObjectiveKind.KothPoint => ObjectiveKind.Hold,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "the feed has no such kind"),
    };

    public static ObjectiveState StateOf(TFObjectiveState state) => state switch
    {
        TFObjectiveState.Locked => ObjectiveState.Locked,
        TFObjectiveState.Open => ObjectiveState.Open,
        TFObjectiveState.Capturing => ObjectiveState.Capturing,
        TFObjectiveState.Carried => ObjectiveState.Carried,
        TFObjectiveState.Dropped => ObjectiveState.Dropped,
        TFObjectiveState.Home => ObjectiveState.Home,
        TFObjectiveState.Moving => ObjectiveState.Moving,
        TFObjectiveState.RollingBack => ObjectiveState.RollingBack,
        TFObjectiveState.Won => ObjectiveState.Won,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "the feed has no such state"),
    };

    /// <summary>The teams that may act, one bit per team number; the feed's unassigned is both of TF2's teams.</summary>
    public static int MayActOf(TFTeam team) => team switch
    {
        TFTeam.Red or TFTeam.Blue => 1 << (int)team,
        _ => (1 << (int)TFTeam.Red) | (1 << (int)TFTeam.Blue),
    };
}
