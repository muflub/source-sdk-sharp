//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
//
// Native temp-entity entry points (TE_*) for the native callers that remain. Each body
// only marshals its arguments, in declaration order, to SourceSharpTe_Forward; the
// managed TeForwardDispatch decides the class and fields. Stock's te_*.cpp files are not
// compiled, since their ServerClasses would clash with the managed ones.

#include "edict.h"
#include "networkstringtabledefs.h"

#include "tempent_forwards.h"
#include "ss_recipient_filter.h"
#include "shell_tempents.h"

#include "ss_effect_dispatch.h"

// The engine's globals and the EffectDispatch string table.
extern CGlobalVars *gpGlobals;
extern INetworkStringTable *g_pStringTableEffectDispatch;
#include "ss_string.h"

// The name's index in the EffectDispatch string table. AddString is idempotent, so a
// name keeps its index for the level, which the client's GetString relies on.
static int EffectNameIndex( const char *pName )
{
	if ( !g_pStringTableEffectDispatch || !pName || !pName[0] )
		return 0;

	return g_pStringTableEffectDispatch->AddString( true, pName );
}

// m_vecOrigin is DT_TEParticleSystem's, nested at offset zero in the same blob.
void TE_BloodStream( IRecipientFilter &filter, float delay, const Vector *org, const Vector *dir, int r, int g, int b,
					 int a, int amount )
{
	const float f[] = {
		org->x, org->y, org->z, dir->x, dir->y, dir->z,
	};

	const int i[] = {
		r, g, b, a, amount,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_BLOOD_STREAM, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

// The client re-traces from m_vecStart to m_vecOrigin, so a missing start is refused.
void TE_Decal( IRecipientFilter &filter, float delay, const Vector *pos, const Vector *start, int entity, int hitbox,
			   int index )
{
	Assert( pos && start );

	// Stock returns here; C# never sees a null pointer.
	if ( !pos || !start )
		return;

	const float f[] = {
		pos->x, pos->y, pos->z, start->x, start->y, start->z,
	};

	const int i[] = {
		entity,
		hitbox,
		index,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_DECAL, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

// Every field lives in the nested DT_EffectData table at offset zero. `pos` is stock's
// and ignored; data.m_vOrigin is the position.
void TE_DispatchEffect( IRecipientFilter &filter, float delay, const Vector &pos, const char *pName,
						const ss::CEffectData &data )
{
	const float f[] = {
		data.m_vOrigin.x,
		data.m_vOrigin.y,
		data.m_vOrigin.z,
		data.m_vStart.x,
		data.m_vStart.y,
		data.m_vStart.z,
		data.m_vAngles.x,
		data.m_vAngles.y,
		data.m_vAngles.z,
		data.m_vNormal.x,
		data.m_vNormal.y,
		data.m_vNormal.z,
		data.m_flMagnitude,
		data.m_flScale,
		data.m_flRadius,
		data.m_CustomColors.m_vecColor1.x,
		data.m_CustomColors.m_vecColor1.y,
		data.m_CustomColors.m_vecColor1.z,
		data.m_CustomColors.m_vecColor2.x,
		data.m_CustomColors.m_vecColor2.y,
		data.m_CustomColors.m_vecColor2.z,
		data.m_ControlPoint1.m_vecOffset.x,
		data.m_ControlPoint1.m_vecOffset.y,
		data.m_ControlPoint1.m_vecOffset.z,
	};

	const int i[] = {
		data.m_fFlags,
		data.m_nAttachmentIndex,
		data.m_nSurfaceProp + 1,
		EffectNameIndex( pName ),
		data.m_nMaterial,
		data.m_nDamageType,
		data.m_nHitBox,
		data.m_nEntIndex,
		data.m_nColor,
		data.m_bCustomColors ? 1 : 0,
		data.m_bControlPoint1 ? 1 : 0,
		(int)data.m_ControlPoint1.m_eParticleAttachment,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_TE_DISPATCH_EFFECT, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

// UTIL_Smoke reaches this through ss::Effects().
void TE_Smoke( IRecipientFilter &filter, float delay, const Vector *pos, int modelindex, float scale, int framerate )
{
	const float f[] = {
		pos ? pos->x : 0.0f,
		pos ? pos->y : 0.0f,
		pos ? pos->z : 0.0f,
		scale,
	};

	const int i[] = {
		modelindex,
		framerate,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SMOKE, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

// A NULL direction is sent as zero, stock's "no particular direction".
void TE_Sparks( IRecipientFilter &filter, float delay, const Vector *pos, int nMagnitude, int nTrailLength,
				const Vector *pDir )
{
	const float f[] = {
		pos ? pos->x : 0.0f,   pos ? pos->y : 0.0f,   pos ? pos->z : 0.0f,
		pDir ? pDir->x : 0.0f, pDir ? pDir->y : 0.0f, pDir ? pDir->z : 0.0f,
	};

	const int i[] = {
		nMagnitude,
		nTrailLength,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SPARKS, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

void TE_WorldDecal( IRecipientFilter &filter, float delay, const Vector *pos, int index )
{
	// Stock returns here; C# never sees a null pointer.
	if ( !pos )
		return;

	const float f[] = {
		pos->x,
		pos->y,
		pos->z,
	};

	const int i[] = {
		index,
	};

	SourceSharpTe_Forward( SOURCESHARP_TE_FWD_WORLD_DECAL, filter, delay, f, ARRAYSIZE( f ), i, ARRAYSIZE( i ) );
}

// The two entry points the rest of the server calls (te_effect_dispatch.cpp).
static void DispatchEffect( const char *pName, const ss::CEffectData &data, ss::CRecipientFilter &filter )
{
	TE_DispatchEffect( filter, 0.0, data.m_vOrigin, pName, data );
}

void DispatchEffect( const char *pName, const ss::CEffectData &data )
{
	ss::CPASFilter filter( data.m_vOrigin );
	DispatchEffect( pName, data, filter );
}

// The server's ss::IEffects: Smoke and Sparks, over the forwards above.

#include "ss_effects.h"

class CEffectsServer : public ss::IEffects
{
public:
	CEffectsServer() {}

	virtual ~CEffectsServer() {}

	virtual void Smoke( const Vector &origin, int mModel, float flScale, float flFramerate )
	{
		ss::CPVSFilter filter( origin );

		// flScale * 0.1f is stock's.
		TE_Smoke( filter, 0.0, &origin, mModel, flScale * 0.1f, flFramerate );
	}

	virtual void Sparks( const Vector &position, int nMagnitude = 1, int nTrailLength = 1,
						 const Vector *pvecDir = NULL )
	{
		ss::CPVSFilter filter( position );

		TE_Sparks( filter, 0.0, &position, nMagnitude, nTrailLength, pvecDir );
	}
};

static CEffectsServer s_EffectServer;

ss::IEffects *ss::Effects()
{
	return &s_EffectServer;
}
