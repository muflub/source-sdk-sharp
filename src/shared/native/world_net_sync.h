// worldspawn's networked fields, declared in C#, with blob offsets asked of the managed
// side rather than copied. Compiled into both DLLs so CWorld and C_World share one body.
#pragma once

#include "generic_payload.h"

#ifndef CLIENT_DLL
class IManagedNetState;
typedef IManagedNetState NetStateArea_t;
#else
class IManagedStandaloneClient;
typedef IManagedStandaloneClient NetStateArea_t;
#endif
#include "mathlib/vector.h"

#include <string.h>

// The map classname, factory name and [StandaloneNetState] key at once; a misspelling
// leaves every field unresolved.
#define SOURCESHARP_WORLD_NETCLASS "worldspawn"

// Blob-relative offsets; each side adds its own base. StartDark and ColdWorld share one
// packed word, so they carry a placement rather than an offset.
#ifdef CLIENT_DLL
// The client's managed generation (CManagedSide::Generation); see standalone_player_sync.h.
unsigned int SourceSharp_PlayerSyncGeneration();
#endif

struct SourceSharpWorldFields_t
{
	unsigned int nGeneration; // 0 = never resolved

	int nWaveHeight;
	int nMaxOccludeeArea;
	int nMinOccluderArea;
	int nMaxPropScreenSpaceWidth;
	int nMinPropScreenSpaceWidth;
	int nWorldMins;
	int nWorldMaxs;
	int nDetailSpriteMaterial;

	SourceSharpPackedField_t StartDark;
	SourceSharpPackedField_t ColdWorld;

	SourceSharpWorldFields_t()
	{
		memset( this, 0, sizeof( *this ) );
		nWaveHeight = -1;
		nMaxOccludeeArea = -1;
		nMinOccluderArea = -1;
		nMaxPropScreenSpaceWidth = -1;
		nMinPropScreenSpaceWidth = -1;
		nWorldMins = -1;
		nWorldMaxs = -1;
		nDetailSpriteMaterial = -1;
	}

	// Shares the player sync's generation: one republish invalidates every native-held
	// offset at once.
	bool IsCurrent( unsigned int nCurrent ) const { return nGeneration != 0 && nGeneration == nCurrent; }
};

// Asks the managed side for all ten placements. False, with one warning, when
// worldspawn is not published on this side; the caller retries on its next read.
bool SourceSharpWorldFields_Resolve( SourceSharpWorldFields_t *pFields, unsigned int nGeneration,
									 NetStateArea_t *pArea );

// Safe against an unresolved field and a NULL blob; they answer zero, as a fresh
// stock world would.
float SourceSharpWorld_ReadFloat( const unsigned char *pBlob, int nOffset );
Vector SourceSharpWorld_ReadVector( const unsigned char *pBlob, int nOffset );
bool SourceSharpWorld_ReadFlag( const unsigned char *pBlob, const SourceSharpPackedField_t &field );

// The detail sprite material, as a pointer into the blob that lives as long as the
// world object. Never NULL: "" for an unresolved field or a missing terminator.
const char *SourceSharpWorld_ReadString( const unsigned char *pBlob, int nBlobBytes, int nOffset );

// Prints all ten values on one line per side, once per level load. The values are
// read by the renderer and fail silently as plausible zeros, so this is the only live
// evidence they arrive; one body keeps both sides' lines comparable.
void SourceSharpWorld_Report( const char *pszSide, const unsigned char *pBlob, int nBlobBytes,
							  const SourceSharpWorldFields_t &fields, unsigned int nGeneration );
