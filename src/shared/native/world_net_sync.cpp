//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets

#include "world_net_sync.h"
#include "sourcesharp.h"
#include "standalone_player_sync.h"

// One warning per failed resolve, naming the class, not one per field.
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

	// The C# property names, not stock's member spellings.
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

// memcpy per field: the blob's layout is C#'s, so no two fields are assumed adjacent.
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

// The terminator is checked rather than trusted: an unterminated run would send the
// material system off the end of the allocation. Answers "" then.
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

	return "";
}

// Two lines, so the console width does not cut the string.
void SourceSharpWorld_Report( const char *pszSide, const unsigned char *pBlob,
                              int nBlobBytes, const SourceSharpWorldFields_t &fields )
{
	if ( !pszSide )
		pszSide = "?";

	// An unresolved set would print ten zeros indistinguishable from a real empty map.
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
