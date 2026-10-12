//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// <c>CEventAction::s_iNextIDStamp</c>: a side's next output-connection
/// id stamp, which stock pre-increments, so the first connection ever parsed
/// is 1 and 0 stays available as "no output".
///
/// <para>
/// PLUMBING (rule c), split out of EventAction (EntityIo.cs) in P1 run 3,
/// lifetime ruling (b): the stamps of a level's connections are saved with
/// them (SavedEntityCodec) and restored across a managed reload, so a counter
/// that restarted at 0 in the reloaded copy would hand a new connection a
/// stamp a restored one already holds. It outlives the collectible context
/// on the side's <see cref="SideState"/>, which the Bootstrap keeps.
/// </para>
///
/// <para>
/// ONE COUNTER PER SIDE STATE (statics to zero): a side's connections are only
/// ever restored into that side, so uniqueness within the side (and within one
/// game instance of it) is what the stamps need. The entity directory is handed
/// its side's counter by the registry that builds it.
/// </para>
/// </summary>
public sealed class EventIdStamps
{
    private int _next;

    /// <summary>The next stamp; the first is 1.</summary>
    public int Next() => ++_next;
}
