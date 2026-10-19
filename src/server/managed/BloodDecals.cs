//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <b>The mark blood leaves on a wall.</b> Stock's <c>UTIL_BloodDecalTrace</c>
/// (util_shared.cpp) over a trace its caller ran: nothing when the violence
/// ConVar for the colour is off, nothing when the trace hit nothing, and
/// otherwise the world's decal from the "Blood" or "YellowBlood" group at the
/// hit, which is what <c>CWorld::DecalTrace</c> painted.
///
/// <para>
/// Native's copy discarded every hit: its entity resolver answered NULL, so
/// no gib and no env_blood left a mark. It moved here, and the
/// <c>BloodDecal</c> slot went, in plan_server_native_v2 T2.
/// </para>
/// </summary>
public sealed class BloodDecals
{
    private readonly IEngineWorldQuery _world;
    private readonly IServerDecals _decals;
    private readonly IEngineEffects _effects;
    private readonly IEngineConsole _console;

    public BloodDecals(IEngineWorldQuery world, IServerDecals decals, IEngineEffects effects, IEngineConsole console)
    {
        _world = world;
        _decals = decals;
        _effects = effects;
        _console = console;
    }

    /// <summary>
    /// Traces from <paramref name="start"/> to <paramref name="end"/> through
    /// brushes only, as stock's callers did, and paints. True when a decal
    /// was placed.
    /// </summary>
    public bool Trace(Vector start, Vector end, BloodColor color, EntityHandle ignore = default)
    {
        TraceResult tr = _world.TraceLine(start, end, TraceMask.Brushes, ignore);

        if (!Violence.ShouldShowBlood(color, _console) || tr.Fraction == 1f)
            return false;

        int index = _decals.IndexForName(color == BloodColor.Red ? "Blood" : "YellowBlood");
        return index >= 0
            && _effects.PaintDecal(index, tr.End, angles: null, projectDistance: 0f,
                                   permanent: false, lowPriority: false);
    }
}
