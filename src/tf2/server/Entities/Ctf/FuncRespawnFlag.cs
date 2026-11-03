//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// func_respawnflag: a volume that sends a flag home (stock's
/// <c>CFuncRespawnFlagZone</c>, server/tf/func_respawnflag.cpp). A carrier
/// who walks in drops it and it resets; a flag dropped inside resets
/// (<see cref="ICtfRules.PointInRespawnFlagZone"/>, asked by the flag's Drop).
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncRespawnFlag(IGameClock clock, ICtfRules rules, ICtfPlayers players) : TriggerVolume(clock)
{
    public const string MapClass = "func_respawnflag";

    /// <summary>Stock's Touch is every frame of the overlap.</summary>
    public override bool TouchEveryTick => true;

    /// <summary>SF_TRIGGER_ALLOW_ALL: the spawnflags name no kind.</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.None;

    protected override bool Passes(EntityHandle toucher) => PassesFilter(toucher);

    public override void OnTouch(EntityHandle toucher)
    {
        base.OnTouch(toucher);

        if (!Disabled && Passes(toucher))
            Touch(toucher);
    }

    protected override void Fire(EntityHandle toucher) { }

    /// <summary>Touch: a carrier drops the flag, and it goes home.</summary>
    public void Touch(EntityHandle other)
    {
        if (Disabled || !players.IsPlayer(other))
            return;

        if (rules.CarriedBy(other) is { } flag)
        {
            flag.DropFlag(other);
            flag.ResetFlag();
        }
    }

    /// <summary>
    /// PointIsWithin, as the zone's box: stock clips a point ray against the
    /// brush; the box is the brush's own for the axis-aligned volumes the
    /// zones are drawn as (docs/divergences.md).
    /// </summary>
    public bool Contains(Vector point)
    {
        if (Disabled)
            return false;

        Vector origin = Origin;
        var (mins, maxs) = Entities.GetBounds(Handle);
        return point.X >= origin.X + mins.X && point.X <= origin.X + maxs.X
            && point.Y >= origin.Y + mins.Y && point.Y <= origin.Y + maxs.Y
            && point.Z >= origin.Z + mins.Z && point.Z <= origin.Z + maxs.Z;
    }
}
