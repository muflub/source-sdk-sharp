//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEPlayerDecal</c>, played. A player's spray, painted with that player's
/// own logo material rather than with a decal index.
///
/// <para>
/// The whole of <c>c_te_playerdecal.cpp</c>'s
/// <c>C_TEPlayerDecal::PostDataUpdate</c>:
/// </para>
/// <code>
/// #ifndef _XBOX
///     if ( !r_decals.GetBool() ) return;
///     CLocalPlayerFilter filter;
///     TE_PlayerDecal( filter, 0.0f, &amp;m_vecOrigin, m_nPlayer, m_nEntity );
/// #endif
/// </code>
///
/// <para>
/// THE THREE WIRE FIELDS ARE THE ARGUMENTS AND THERE IS NO DECODE LEFT TO
/// MOVE, which is the one thing worth saying about this class. Everything
/// between the fields and the engine call is a pointer type that cannot cross
/// the ABI — <c>engine-&gt;GetPlayerInfo</c>, the custom-file hash, a download
/// check against the filesystem, a <c>FindMaterial</c> and a texture-variable
/// swap — so <c>ClientPlayerDecalShoot</c> is the whole of
/// <c>TE_PlayerDecal</c> rather than just its last line. That is not a
/// shortcut: a managed half that stopped at the numbers would have nothing to
/// decide.
/// </para>
///
/// <para>
/// <c>r_decals</c>, <c>cl_spraydisable</c> AND THE ENTITY LOOKUP ARE ALL
/// NATIVE, in the backend, where stock honours them. Checking any of them here
/// as well would be two descriptions of one rule. <c>cl_spraydisable</c> was a
/// file-static <c>ConVar</c> inside the file this class evicts, so it moved
/// down beside the code that reads it — see <c>shell_te_backends_client.cpp</c>;
/// so did the sixty-four <c>CLIENTEFFECT_MATERIAL</c> logo precaches, which
/// would otherwise have left with the file and taken a hitch-free first spray
/// with them.
/// </para>
///
/// <para>
/// THE DECLARATION ORDER IS THE TABLE'S, NOT THE C++ CLASS'S, and reading by
/// name is what makes that safe: <c>C_TEPlayerDecal</c> declares
/// <c>m_nPlayer</c> FIRST as a member and LAST as a prop. Every read here is by
/// the wire name the two sides pair on, so the member order is not something
/// this side can get wrong.
/// </para>
///
/// <para>
/// NO LEDGER ROW. This reaches <c>effects-&gt;PlayerDecalShoot</c>, the
/// engine's own decal system, and resolves its entity through
/// <c>cl_entitylist-&gt;GetClientEntity</c>, which answers for both hierarchies.
/// Stock's <c>Precache()</c> was empty; the material precaching was the
/// <c>CLIENTEFFECT</c> block, not that function.
/// </para>
/// </summary>
[ClientTempEnt("CTEPlayerDecal")]
public sealed class ClientTePlayerDecal : ClientTempEnt<TePlayerDecal>
{
    private readonly IPlayerSprays _sprays;

    public ClientTePlayerDecal(IPlayerSprays sprays)
    {
        _sprays = sprays;
    }

    public override void Play(TePlayerDecal te)
    {
        // A player SLOT, not an edict: Q_log2( MAX_PLAYERS ) bits, its own
        // width for its own reason (see TePlayerDecal.cs). Unsigned, as both
        // props are declared.

        // C_TEPlayerDecal::PostDataUpdate and TE_PlayerDecal: PlayerSprays.
        _sprays.Spray(te.Origin, te.Player, te.Entity);
    }
}
