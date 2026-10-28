//========= Copyright Valve Corporation, All rights reserved. ============//

//========= SourceSharp's edit of c_te_legacytempents.h (vendor/README.md) =====//
// ITempEnts and CTempEnts, the client's temp entity factory, which
// shell_legacy_tempents_client.cpp and shell_te_pool_client.cpp implement:
// moved out of the fork's edited game/client/c_te_legacytempents.h (client.so
// on unmodified Valve headers, user 2026-10-25).
#pragma once

#include "igameentity.h"

// FizzEffect TAKES AN IClientRenderable, NOT AN IGameEntity. It reads exactly
// two things off the entity a fizz was fired at -- the model, for its bounds,
// and the render angles, for which way the current drags -- and both are pure
// virtuals on IClientRenderable that EVERY client entity implements. Typed as
// IGameEntity the call needed a relay and a cast through the one class in this
// DLL that implements that interface; typed this way it needs neither and works
// for an entity of either hierarchy.
class IClientRenderable;

class C_LocalTempEntity;
class C_PhysPropClientside;
struct model_t;

#include "mempool.h"

// particlemgr.h's handle to a particle material.
class CParticleSubTexture;
typedef CParticleSubTexture *PMaterialHandle;
#include "utllinkedlist.h"

// Purpose: Interface for lecacy temp entities
abstract_class ITempEnts
{
public:
	virtual						~ITempEnts() {}

	virtual void				Init() = 0;
	virtual void				Shutdown() = 0;
	virtual void				LevelInit() = 0;
	virtual void				LevelShutdown() = 0;

	virtual void				Update() = 0;
	virtual void				Clear() = 0;

	virtual C_LocalTempEntity	*FindTempEntByID( int nID, int nSubID ) = 0;

	virtual void				BloodSprite( const Vector &org, int r, int g, int b, int a, int modelIndex, int modelIndex2, float size ) = 0;
	virtual void				RicochetSprite( const Vector &pos, model_t *pmodel, float duration, float scale ) = 0;
	virtual void				MuzzleFlash( int type, ClientEntityHandle_t hEntity, int attachmentIndex, bool firstPerson ) = 0;
	virtual void				MuzzleFlash( const Vector &pos1, const QAngle &angles, int type, ClientEntityHandle_t hEntity, bool firstPerson ) = 0;
	virtual void				EjectBrass( const Vector& pos1, const QAngle& angles, const QAngle& gunAngles, int type ) = 0;
	virtual C_LocalTempEntity   *SpawnTempModel( const model_t *pModel, const Vector &vecOrigin, const QAngle &vecAngles, const Vector &vecVelocity, float flLifeTime, int iFlags ) = 0;
	virtual void				BreakModel( const Vector &pos, const QAngle &angles, const Vector &size, const Vector &dir, float random, float life, int count, int modelIndex, char flags) = 0;
	virtual void				Bubbles( const Vector &mins, const Vector &maxs, float height, int modelIndex, int count, float speed ) = 0;
	virtual void				BubbleTrail( const Vector &start, const Vector &end, float flWaterZ, int modelIndex, int count, float speed ) = 0;
	virtual void				Sprite_Explode( C_LocalTempEntity *pTemp, float scale, int flags ) = 0;
	virtual void				FizzEffect( IClientRenderable *pent, int modelIndex, int density, int current ) = 0;
	virtual C_LocalTempEntity	*DefaultSprite( const Vector &pos, int spriteIndex, float framerate ) = 0;
	virtual void				Sprite_Smoke( C_LocalTempEntity *pTemp, float scale ) = 0;
	virtual C_LocalTempEntity	*TempSprite( const Vector &pos, const Vector &dir, float scale, int modelIndex, int rendermode, int renderfx, float a, float life, int flags, const Vector &normal = vec3_origin ) = 0;
	virtual void				AttachTentToPlayer( int client, int modelIndex, float zoffset, float life ) = 0;
	virtual void				KillAttachedTents( int client ) = 0;
	virtual void				Sprite_Spray( const Vector &pos, const Vector &dir, int modelIndex, int count, int speed, int iRand ) = 0;
	virtual void				Sprite_Trail( const Vector &vecStart, const Vector &vecEnd, int modelIndex, int nCount, float flLife, float flSize, float flAmplitude, int nRenderamt, float flSpeed ) = 0;
	virtual void				RocketFlare( const Vector& pos ) = 0;
	virtual void				HL1EjectBrass( const Vector &vecPosition, const QAngle &angAngles, const Vector &vecVelocity, int nType ) = 0;
	virtual void				CSEjectBrass( const Vector &vecPosition, const QAngle &angVelocity, int nType, int nShellType, CBasePlayer *pShooter ) = 0;
	
	virtual void				PlaySound ( C_LocalTempEntity *pTemp, float damp ) = 0;
	virtual void				PhysicsProp( int modelindex, int skin, const Vector& pos, const QAngle &angles, const Vector& vel, int flags, int effects = 0 ) = 0;
	virtual C_LocalTempEntity	*ClientProjectile( const Vector& vecOrigin, const Vector& vecVelocity, const Vector& vecAccel, int modelindex, int lifetime, IGameEntity *pOwner, const char *pszImpactEffect = NULL, const char *pszParticleEffect = NULL ) = 0;
	virtual C_PhysPropClientside		*PhysicsProp( const model_t *pModel, int skin, const Vector& pos, const QAngle &angles, const Vector& vel, int flags, int effects = 0, int nModelIndex = -1 ) = 0;
};

// Purpose: Default implementation of the temp entity interface
class CTempEnts : public ITempEnts
{
// Construction
public:
							CTempEnts();
	virtual					~CTempEnts();
// Exposed interface
public:
	virtual void			Init();
	virtual void			Shutdown();

	virtual void			LevelInit();

	virtual void			LevelShutdown();

	virtual void			Update();
	virtual void			Clear();

	virtual C_LocalTempEntity	*FindTempEntByID( int nID, int nSubID );

	// Legacy temp entities still supported
	virtual void			BloodSprite( const Vector &org, int r, int g, int b, int a, int modelIndex, int modelIndex2, float size );
	virtual void			RicochetSprite( const Vector &pos, model_t *pmodel, float duration, float scale );

	virtual void			MuzzleFlash( int type, ClientEntityHandle_t hEntity, int attachmentIndex, bool firstPerson );
	virtual void			MuzzleFlash( const Vector &pos1, const QAngle &angles, int type, ClientEntityHandle_t hEntity, bool firstPerson = false );
	
	virtual void			BreakModel(const Vector &pos, const QAngle &angles, const Vector &size, const Vector &dir, float random, float life, int count, int modelIndex, char flags);
	virtual void			Bubbles( const Vector &mins, const Vector &maxs, float height, int modelIndex, int count, float speed );
	virtual void			BubbleTrail( const Vector &start, const Vector &end, float height, int modelIndex, int count, float speed );
	virtual void			Sprite_Explode( C_LocalTempEntity *pTemp, float scale, int flags );
	virtual void			FizzEffect( IClientRenderable *pent, int modelIndex, int density, int current );
	virtual C_LocalTempEntity		*DefaultSprite( const Vector &pos, int spriteIndex, float framerate );
	virtual void			Sprite_Smoke( C_LocalTempEntity *pTemp, float scale );
	virtual C_LocalTempEntity		*TempSprite( const Vector &pos, const Vector &dir, float scale, int modelIndex, int rendermode, int renderfx, float a, float life, int flags, const Vector &normal = vec3_origin );
	virtual void			AttachTentToPlayer( int client, int modelIndex, float zoffset, float life );
	virtual void			KillAttachedTents( int client );
	virtual void			Sprite_Spray( const Vector &pos, const Vector &dir, int modelIndex, int count, int speed, int iRand );
	void					Sprite_Trail( const Vector &vecStart, const Vector &vecEnd, int modelIndex, int nCount, float flLife, float flSize, float flAmplitude, int nRenderamt, float flSpeed );

	virtual void			PlaySound ( C_LocalTempEntity *pTemp, float damp );
	virtual void			EjectBrass( const Vector &pos1, const QAngle &angles, const QAngle &gunAngles, int type );
	virtual C_LocalTempEntity		*SpawnTempModel( const model_t *pModel, const Vector &vecOrigin, const QAngle &vecAngles, const Vector &vecVelocity, float flLifeTime, int iFlags );
	void					RocketFlare( const Vector& pos );
	void					HL1EjectBrass( const Vector &vecPosition, const QAngle &angAngles, const Vector &vecVelocity, int nType );
	void					CSEjectBrass( const Vector &vecPosition, const QAngle &angAngles, int nType, int nShellType, CBasePlayer *pShooter );
	void					PhysicsProp( int modelindex, int skin, const Vector& pos, const QAngle &angles, const Vector& vel, int flags, int effects = 0 );
	C_LocalTempEntity		*ClientProjectile( const Vector& vecOrigin, const Vector& vecVelocity, const Vector& vecAcceleration, int modelindex, int lifetime, IGameEntity *pOwner, const char *pszImpactEffect = NULL, const char *pszParticleEffect = NULL );
	C_PhysPropClientside	*PhysicsProp( const model_t *pModel, int skin, const Vector& pos, const QAngle &angles, const Vector& vel, int flags, int effects = 0, int modelindex = -1 );

// Data
public:
	enum
	{ 
		MAX_TEMP_ENTITIES = 500,
		MAX_TEMP_ENTITY_SPRITES = 200,
		MAX_TEMP_ENTITY_STUDIOMODEL = 50,
	};

private:
	// Global temp entity pool
	CClassMemoryPool< C_LocalTempEntity >	m_TempEntsPool;
	CUtlLinkedList< C_LocalTempEntity *, unsigned short >	m_TempEnts;

	// Muzzle flash sprites
	struct model_t			*m_pSpriteMuzzleFlash[10];
	struct model_t			*m_pSpriteAR2Flash[4];
	struct model_t			*m_pShells[3];
	struct model_t			*m_pSpriteCombineFlash[2];

// Internal methods also available to children
protected:

	// SourceSharp -- the two things the MANAGED pool needs from this class
	// that it cannot reach any other way (plan_tmp.md Part 5).
	// The pool's bookkeeping is C#'s: the active list, the free list, the
	// priority classes, the eviction, the lifetime and the update loop all live
	// in SourceSharp.Client/TempEnts/ClientTempEntPool.cs. What stays here is
	// the OBJECT -- a C_LocalTempEntity is already the draw shell -- and these
	// two are the only members of this class the slot table in
	// sourcesharp/game/client/shell_te_pool_client.cpp cannot get at from
	// outside, because m_TempEntsPool and AddVisibleTempEntity are private.
	// PUBLIC RATHER THAN FRIENDED, deliberately: a friend declaration here
	// would name a SourceSharp translation unit inside a stock header, and the
	// stock client compiles this header with SOURCESHARP undefined.
public:
	void					 PoolFreeObject( C_LocalTempEntity *pTemp );
	int						 PoolAddVisible( C_LocalTempEntity *pTemp );

	// C-Phase 5's third, and the same reason as the first two: the no-argument
	// TempEntAlloc() below is private, and a MANAGED spawn helper has to be
	// able to ask for the object after the managed pool has already reserved
	// its slot. A member, so it can reach the private one; defined in
	// shell_te_pool_client.cpp with the other two.
	C_LocalTempEntity		*PoolCreateObject();
private:

// Material handle caches
private:

	PMaterialHandle			m_Material_MuzzleFlash_Player[4];
	PMaterialHandle			m_Material_MuzzleFlash_NPC[4];
	PMaterialHandle			m_Material_Combine_MuzzleFlash_Player[2];
	PMaterialHandle			m_Material_Combine_MuzzleFlash_NPC[2];

// Internal methods
private:
	CTempEnts( const CTempEnts & );

	C_LocalTempEntity		*TempEntAlloc();	

	int						AddVisibleTempEntity( C_LocalTempEntity *pEntity );

};

extern ITempEnts *tempents;
