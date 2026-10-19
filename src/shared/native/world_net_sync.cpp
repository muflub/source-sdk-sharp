//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — worldspawn's networked state, in one place =========//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// See the header. Compiled into both DLLs: the field names, the ask and the
// byte arithmetic have to be the same code on the two sides, or one spelling is
// compared against the other and the difference is a rendering bug with no log
// line.
//
//=============================================================================//

#include "world_net_sync.h"
#include "sourcesharp.h"
#include "standalone_player_sync.h"



//-----------------------------------------------------------------------------
// The ask. ONE warning per failed resolve, naming the class: the interesting
// fact is "worldspawn is not published on this side", not ten spellings of it.
//-----------------------------------------------------------------------------
static int ResolveOffset( const SourceSharpNetStateAsk_t &ask, const char *pField, bool *pbAll )
{
	const int nOffset = ask.pfnFieldOffset( SOURCESHARP_WORLD_NETCLASS, pField );

	if ( nOffset < 0 )
		*pbAll = false;

	return nOffset;
}

static SourceSharpPackedField_t ResolvePacked( const SourceSharpNetStateAsk_t &ask,
                                               const char *pField, bool *pbAll )
{
	const int nOffset = ask.pfnFieldOffset( SOURCESHARP_WORLD_NETCLASS, pField );
	const int nBits   = ask.pfnFieldBits( SOURCESHARP_WORLD_NETCLASS, pField );

	SourceSharpPackedField_t field = SourceSharpPayload_Place( nOffset, nBits );

	if ( !field.IsResolved() )
		*pbAll = false;

	return field;
}

bool SourceSharpWorldFields_Resolve( SourceSharpWorldFields_t *pFields )
{
	SourceSharpNetStateAsk_t ask;
	if ( !pFields || !SourceSharp_NetStateAsk( &ask ) )
		return false;

	bool bAll = true;

	// THE NAMES ARE THE C# PROPERTY NAMES, not stock's member spellings. A
	// declaration is keyed by what it declares, and WorldNetState calls them
	// WaveHeight and WorldMins where the old CNetworkVars were m_flWaveHeight
	// and m_WorldMins. Changing one without the other resolves to -1 and reads
	// as nothing, which is why the resolve reports rather than assuming.
	pFields->nWaveHeight              = ResolveOffset( ask, "WaveHeight", &bAll );
	pFields->nMaxOccludeeArea         = ResolveOffset( ask, "MaxOccludeeArea", &bAll );
	pFields->nMinOccluderArea         = ResolveOffset( ask, "MinOccluderArea", &bAll );
	pFields->nMaxPropScreenSpaceWidth = ResolveOffset( ask, "MaxPropScreenSpaceWidth", &bAll );
	pFields->nMinPropScreenSpaceWidth = ResolveOffset( ask, "MinPropScreenSpaceWidth", &bAll );
	pFields->nWorldMins               = ResolveOffset( ask, "WorldMins", &bAll );
	pFields->nWorldMaxs               = ResolveOffset( ask, "WorldMaxs", &bAll );
	pFields->nDetailSpriteMaterial    = ResolveOffset( ask, "DetailSpriteMaterial", &bAll );

	pFields->StartDark = ResolvePacked( ask, "StartDark", &bAll );
	pFields->ColdWorld = ResolvePacked( ask, "ColdWorld", &bAll );

	if ( !bAll )
	{
		Warning( "[SourceSharp] world sync: worldspawn's fields did not all resolve — is "
		         "SourceSharp.Shared.WorldNetState declared on this side?\n" );
		return false;
	}

	pFields->nGeneration = SourceSharp_PlayerSyncGeneration();
	return true;
}

//-----------------------------------------------------------------------------
// The byte movers. memcpy per field rather than a struct, for
// standalone_player_sync.cpp's reason: the blob's layout is C#'s and nothing
// here may assume two fields are adjacent.
//-----------------------------------------------------------------------------
float SourceSharpWorld_ReadFloat( const unsigned char *pBlob, int nOffset )
{
	float flValue = 0.0f;

	if ( pBlob && nOffset >= 0 )
		memcpy( &flValue, pBlob + nOffset, sizeof( flValue ) );

	return flValue;
}

Vector SourceSharpWorld_ReadVector( const unsigned char *pBlob, int nOffset )
{
	float xyz[3] = { 0.0f, 0.0f, 0.0f };

	if ( pBlob && nOffset >= 0 )
		memcpy( xyz, pBlob + nOffset, sizeof( xyz ) );

	return Vector( xyz[0], xyz[1], xyz[2] );
}

bool SourceSharpWorld_ReadFlag( const unsigned char *pBlob,
                                const SourceSharpPackedField_t &field )
{
	if ( !pBlob || !field.IsResolved() )
		return false;

	return SourceSharpPayload_ReadPacked( pBlob, field ) != 0;
}

//-----------------------------------------------------------------------------
// The string, in place.
//
// THE TERMINATOR IS CHECKED RATHER THAN TRUSTED. These bytes came off the wire
// on the client and out of a managed write on the server, and both do terminate
// — but a run with no NUL in it would send every reader of the returned pointer
// off the end of the entity's allocation, and the reader is the material system.
// An unterminated field answers "" and says so once.
//-----------------------------------------------------------------------------
const char *SourceSharpWorld_ReadString( const unsigned char *pBlob, int nBlobBytes,
                                         int nOffset )
{
	if ( !pBlob || nOffset < 0 || nOffset >= nBlobBytes )
		return "";

	for ( int i = nOffset; i < nBlobBytes; i++ )
	{
		if ( pBlob[i] == 0 )
			return (const char *)( pBlob + nOffset );
	}

	// Deliberately not a warning-per-call: this is reached from the detail
	// sprite path, which asks once per level, and from the dev console.
	return "";
}

//-----------------------------------------------------------------------------
// The ten, printed. See the header for why this ships rather than being a probe.
//
// Two lines rather than one: a single Msg would run past the console's width and
// the interesting half -- the string -- would be the half that got cut.
//-----------------------------------------------------------------------------
void SourceSharpWorld_Report( const char *pszSide, const unsigned char *pBlob,
                              int nBlobBytes, const SourceSharpWorldFields_t &fields )
{
	if ( !pszSide )
		pszSide = "?";

	// SAID RATHER THAN LEFT AS ZEROES. An unresolved field set reads as ten
	// defaults, which is exactly what a working world with an empty map would
	// also read as -- so the two are told apart here and not by the reader.
	if ( !fields.IsCurrent() )
	{
		Warning( "[SourceSharp/%s] worldspawn: field offsets did not resolve, so the ten "
		         "values below would all read zero. NOT PRINTING THEM.\n", pszSide );
		return;
	}

	const Vector vecMins = SourceSharpWorld_ReadVector( pBlob, fields.nWorldMins );
	const Vector vecMaxs = SourceSharpWorld_ReadVector( pBlob, fields.nWorldMaxs );

	Msg( "[SourceSharp/%s] worldspawn: mins (%g %g %g) maxs (%g %g %g) wave %g\n",
		pszSide,
		vecMins.x, vecMins.y, vecMins.z,
		vecMaxs.x, vecMaxs.y, vecMaxs.z,
		SourceSharpWorld_ReadFloat( pBlob, fields.nWaveHeight ) );

	Msg( "[SourceSharp/%s] worldspawn: occludee %g occluder %g propwidth %g..%g "
	     "startdark %d coldworld %d detail '%s'\n",
		pszSide,
		SourceSharpWorld_ReadFloat( pBlob, fields.nMaxOccludeeArea ),
		SourceSharpWorld_ReadFloat( pBlob, fields.nMinOccluderArea ),
		SourceSharpWorld_ReadFloat( pBlob, fields.nMinPropScreenSpaceWidth ),
		SourceSharpWorld_ReadFloat( pBlob, fields.nMaxPropScreenSpaceWidth ),
		SourceSharpWorld_ReadFlag( pBlob, fields.StartDark ) ? 1 : 0,
		SourceSharpWorld_ReadFlag( pBlob, fields.ColdWorld ) ? 1 : 0,
		SourceSharpWorld_ReadString( pBlob, nBlobBytes, fields.nDetailSpriteMaterial ) );
}
