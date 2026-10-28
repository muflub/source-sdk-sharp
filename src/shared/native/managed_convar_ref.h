//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A by-name handle on a convar declared in managed code. ConVarRef caches a miss
// forever, and a managed convar registers after DLL load, so this retries the lookup
// until it resolves. Never also declare a native ConVar of the same name.

#pragma once

#include "convar.h"

// Read accessors mirror ConVar's; reads answer zero until the convar exists.
class CManagedConVarRef
{
public:
	explicit CManagedConVarRef( const char *pName )
		: m_pName( pName )
		, m_Ref( (IConVar *)NULL )
		, m_bResolved( false )
	{
		// Not resolved here: construction at DLL load precedes managed registration.
	}

	// False while the engine does not have the convar.
	bool IsValid() const { return Resolve(); }

	bool GetBool() const { return GetInt() != 0; }

	int GetInt() const { return Resolve() ? m_Ref.GetInt() : 0; }

	float GetFloat() const { return Resolve() ? m_Ref.GetFloat() : 0.0f; }

	const char *GetString() const { return Resolve() ? m_Ref.GetString() : ""; }

	void SetValue( int nValue )
	{
		if ( Resolve() )
			m_Ref.SetValue( nValue );
	}

	void SetValue( float flValue )
	{
		if ( Resolve() )
			m_Ref.SetValue( flValue );
	}

	void SetValue( const char *pV )
	{
		if ( Resolve() )
			m_Ref.SetValue( pV );
	}

	const char *GetName() const { return m_pName; }

private:
	// bIgnoreMissing is true: a miss is expected before managed startup.
	bool Resolve() const
	{
		if ( m_bResolved )
			return true;

		m_Ref.Init( m_pName, true );

		if ( !m_Ref.IsValid() )
			return false;

		m_bResolved = true;
		return true;
	}

	const char *m_pName;
	mutable ConVarRef m_Ref;
	mutable bool m_bResolved;
};
