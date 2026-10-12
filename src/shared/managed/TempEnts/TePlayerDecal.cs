using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEPlayerDecal</c> — a player's spray, which the client paints with
/// that player's own logo material rather than with a decal index.
///
/// <para>
/// From <c>te_playerdecal.cpp:95</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEPlayerDecal, DT_TEPlayerDecal)
///     SendPropVector( SENDINFO(m_vecOrigin), -1, SPROP_COORD),
///     SendPropInt( SENDINFO(m_nEntity), MAX_EDICT_BITS, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO(m_nPlayer), Q_log2( MAX_PLAYERS ), SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// DECLARATION ORDER HERE IS THE TABLE'S, NOT THE C++ CLASS'S.
/// <c>CTEPlayerDecal</c> declares <c>m_nPlayer</c> FIRST as a member and LAST
/// as a prop, and it is the prop order that goes on the wire — which is why the
/// port takes its order from <c>IMPLEMENT_SERVERCLASS_ST</c> and never from the
/// class body. The blob offsets are ours on both sides (plan 2A), so following
/// the table costs nothing and following the members would have silently
/// reordered three props.
/// </para>
///
/// <para>
/// <c>m_nPlayer</c> is <c>Q_log2( MAX_PLAYERS )</c>, which is
/// <see cref="TeBits.MaxPlayersLog2"/> and resolved natively from
/// <c>shareddefs.h</c>. Six bits for this build's 33 players. Restating it as a
/// 6 in C# would be a second copy of a width, and this is the port's one rule
/// with no exceptions (plan 3.1).
/// </para>
/// </summary>
[TempEntity]
public class TePlayerDecal : TeBaseTempEntity
{
    /// <summary>Where the spray lands.</summary>
    [TeProp(Bits = -1, Flags = TeFlags.Coord)]
    public Vector Origin;

    /// <summary>The edict sprayed on, or 0 for the world.</summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int Entity;

    /// <summary>Whose logo. A player SLOT, not an edict — hence its own width.</summary>
    [TeProp(BitsFrom = TeBits.MaxPlayersLog2, Flags = TeFlags.Unsigned)]
    public int Player;
}
