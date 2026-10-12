//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the predictable list, out of c_baseentity.cpp =====//
//
// The client's list of predicted and client-created entities: the concrete
// CPredictableList behind game/shared/predictable_entity.h's IPredictableList
// interface and its `predictables` pointer.
//
// WHY IT NEEDED A HEADER AT ALL. The interface and the pointer have always been
// in predictable_entity.h, but the CONCRETE class, its four method bodies and
// the g_Predictables instance were all inside game/client/c_baseentity.cpp with
// no header anywhere -- every user was in that one translation unit, so a
// declaration was never needed.
//
// Step 6 splits them, and the split is not symmetric. Four call sites are
// C_BaseEntity members (AddToPredictableList / RemoveFromPredictablesList,
// c_baseentity.cpp:1157, :4380, :4399, :4788) and die with the class.
// EntityRender_AddVisibleEntities walks the list once per frame from
// cdll_client_int.cpp:2305, which SURVIVES step 6 and compiles perfectly clean
// armed -- so that function was evacuated to c_baseentity_statics.cpp, and it
// took the list it walks with it.
//
// FOUND BY THE PROBE, NOT BY READING. Moving AddVisibleEntities on its own made
// `predictables` a NEW link-only symbol: the reference used to be inside the
// same translation unit as the definition, so it cancelled out and no
// measurement could see it. src/evict_consumers named it on the very next run.
// A cross-translation-unit reference to a symbol still inside the removal set is
// the same defect one level down, and the only instrument that reports it is a
// real link with the removal set evicted.
//
// THE ALIAS GUARD CAME WITH THE CLASS and is unchanged -- it is one of the 578,
// it has simply moved file. C++ has no `friend class X;` for an alias; C++11's
// class-key-less `friend X;` does take a typedef-name, which is the alias arm.
//
// CLIENT ONLY, and behind NO_ENTITY_PREDICTION exactly as it was in
// c_baseentity.cpp.
//
//=============================================================================//
#ifndef ENTITY_PREDICTABLE_LIST_H
#define ENTITY_PREDICTABLE_LIST_H
#ifdef _WIN32
#pragma once
#endif

#include "predictable_entity.h"

#if !defined( NO_ENTITY_PREDICTION )

//-----------------------------------------------------------------------------
// Purpose: Maintains a list of predicted or client created entities
//-----------------------------------------------------------------------------
class CPredictableList : public IPredictableList
{
public:
	virtual C_BaseEntity *GetPredictable( int slot );
	virtual int GetPredictableCount( void );

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

// The singleton. Defined in c_baseentity_statics.cpp, next to the four method
// bodies and to EntityRender_AddVisibleEntities, which is the only reader of it
// that survives step 6. It was `static` while every user was in one translation
// unit; it is not any more, because they are in two.
extern CPredictableList g_Predictables;

#endif // !NO_ENTITY_PREDICTION

#endif // ENTITY_PREDICTABLE_LIST_H
