// Model precache without an entity, and the window in which precaching is legal.
#pragma once
#define PRECACHE_MODEL_H

#include "ishim_areas.h"

class IVEngineServer;
class IVModelInfo;
struct CManagedAreas;

class CServerModelPrecache final : public IShimPrecache
{
public:
	CServerModelPrecache( IVEngineServer *pEngine, IVModelInfo *pModelInfo, const CManagedAreas &managed )
		: m_pEngine( pEngine )
		, m_pModelInfo( pModelInfo )
		, m_Managed( managed )
	{
	}

	CServerModelPrecache( const CServerModelPrecache & ) = delete;
	CServerModelPrecache &operator=( const CServerModelPrecache & ) = delete;

	// Adds a model to the level precache list and returns its model index, or -1.
	int Model( const char *name, bool bPreload = true );

	// The slot: a model's index, or -1 if it would not load (0 is a real index).
	int32_t PrecacheModel( const char *pszModel ) override;

	// Whether precaching is legal right now. Callers bracket a block of precaching with it
	// and put the old value back.
	bool IsAllowed() const { return m_bAllowed; }

	void SetAllowed( bool bAllowed ) { m_bAllowed = bAllowed; }

private:
	// Precaches the sounds, particle systems and anim-event assets of an already-precached
	// studio model. A no-op for a non-studio model.
	void ModelComponents( int nModelIndex );

	IVEngineServer *m_pEngine;
	IVModelInfo *m_pModelInfo;

	// Read when called: a managed reload replaces the areas.
	const CManagedAreas &m_Managed;

	bool m_bAllowed = false;
};
