//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// <c>DT_TETFBlood</c> — the blood hit's payload, from <c>te_tfblood.cpp:47</c>:
/// </summary>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST( CTETFBlood, DT_TETFBlood )
///     SendPropFloat( SENDINFO_NOCHECK( m_vecOrigin[0] ), -1, SPROP_COORD_MP_INTEGRAL ),
///     SendPropFloat( SENDINFO_NOCHECK( m_vecOrigin[1] ), -1, SPROP_COORD_MP_INTEGRAL ),
///     SendPropFloat( SENDINFO_NOCHECK( m_vecOrigin[2] ), -1, SPROP_COORD_MP_INTEGRAL ),
///     SendPropVector( SENDINFO_NOCHECK( m_vecNormal ), 6, 0, -1.0f, 1.0f ),
///     SendPropInt( SENDINFO_NAME( m_nEntIndex, entindex ), MAX_EDICT_BITS, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
/// <para>
/// THE FIRST TF2 DECLARATION, so the two things every later one inherits are
/// said here. THE NAMES ARE EXPLICIT: the bare <c>[TempEntity]</c> derives
/// <c>CTe...</c> / <c>DT_...</c> from the C# name and would pair these two
/// sides on names stock never used — stock spells them
/// <c>CTETFBlood</c> and <c>DT_TETFBlood</c>. AND THE WIRE NAMES ARE THE
/// MEMBER NAMES, dropped like every converted declaration's: both RecvTables
/// are gone, both halves build their props from this one class, and stock's
/// own spelling (<c>m_nEntIndex</c> sent as <c>entindex</c>) stays in the
/// quoted table above, which is where the mapping is readable.
/// </para>
///
/// <para>
/// THE NORMAL IS NOT SPROP_NORMAL. Stock's flags argument is a literal
/// <c>0</c> and the range is explicit <c>[-1, 1]</c>: six quantized bits per
/// component, not the engine's compressed unit vector. <see cref="TeEffectData"/>'s
/// Normal, four tables away in spirit, IS a normal — copying the more famous
/// encoding here would put three plausible-looking components on the wire that
/// decode to something else.
/// </para>
/// </summary>
[TempEntity("CTETFBlood", "DT_TETFBlood")]
public class TeTFBlood : TeBaseTempEntity
{
    /// <summary>
    /// Where the hit happened. Three floats over the multiplayer INTEGRAL
    /// coordinate form, spelled per element in stock (<c>m_vecOrigin[0..2]</c>,
    /// no <c>SENDINFO_VECTORELEM</c>), so like <see cref="TeEffectData.Origin"/>
    /// there is no <see cref="TeFlags.VectorElem"/> on it.
    /// </summary>
    [TeProp(TePropKind.Float, Bits = -1,
            Flags = TeFlags.CoordMpIntegral, Elems = 3, ElemStride = 4)]
    public Vector Origin;

    /// <summary>
    /// The hit normal, six bits per component over the explicit <c>[-1, 1]</c>.
    /// </summary>
    [TeProp(Bits = 6, Low = -1f, High = 1f)]
    public Vector Normal;

    /// <summary>
    /// The edict the blood belongs to — the player that bled. The client
    /// resolves it back to an entity to ask its water level (stock's
    /// <c>RecvProxy_BloodEntIndex</c> turns a negative into
    /// <c>INVALID_EHANDLE</c>, which an unsigned wire field cannot carry).
    /// </summary>
    [TeProp(BitsFrom = TeBits.MaxEdict, Flags = TeFlags.Unsigned)]
    public int EntIndex;
}
