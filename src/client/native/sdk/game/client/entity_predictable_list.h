//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The concrete CPredictableList behind predictable_entity.h's IPredictableList.
#pragma once

#include "predictable_entity.h"

// Maintains a list of predicted or client-created entities.
class CPredictableList : public IPredictableList
{
public:
	virtual C_BaseEntity *GetPredictable( int slot );
	virtual int GetPredictableCount();

protected:
	void	AddToPredictableList( ClientEntityHandle_t add );
	void	RemoveFromPredictablesList( ClientEntityHandle_t remove );

private:
	CUtlVector< ClientEntityHandle_t >	m_Predictables;

	// C++ has no `friend class X;` for an alias; C++11's class-key-less
	// `friend X;` does take a typedef-name, so that is the alias arm.
	#ifndef SS_CBASEENTITY_ALIAS
	friend class C_BaseEntity;
	#else
	friend C_BaseEntity;
	#endif
};

// Defined in c_baseentity_statics.cpp.
extern CPredictableList g_Predictables;
