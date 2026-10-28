//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

#pragma once

// An entity's engine model instance, held with the model it was made for. The
// engine draws an instance's decal list without asking whether the renderable
// still has that model, so a model change drops it (stock's SetModelPointer).
template <class Handle, Handle kInvalid>
class CStandaloneModelInstance
{
public:
	Handle Get() const { return m_hInstance; }

	template <class Render>
	void DropIfNotFor( Render *pRender, const void *pModel )
	{
		if ( m_pModel != pModel )
			Destroy( pRender );
	}

	template <class Render, class Renderable>
	Handle Ensure( Render *pRender, Renderable *pRenderable, const void *pModel )
	{
		DropIfNotFor( pRender, pModel );
		if ( m_hInstance == kInvalid )
		{
			m_hInstance = pRender->CreateInstance( pRenderable );
			m_pModel = pModel;
		}
		return m_hInstance;
	}

	template <class Render>
	void Destroy( Render *pRender )
	{
		if ( m_hInstance != kInvalid )
			pRender->DestroyInstance( m_hInstance );
		m_hInstance = kInvalid;
		m_pModel = nullptr;
	}

private:
	Handle m_hInstance = kInvalid;
	const void *m_pModel = nullptr;
};
