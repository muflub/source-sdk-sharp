//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// ss::variant_t: the typed value an entity input carries, server.so's own.
//
// The engine never sees one. It travels from the input relays
// (standalone_relays.cpp) to CStandaloneEntity::AcceptInput and
// OnManagedInput, whose bodies hand its text and its number to C#. So under
// plan_server_novalve.md 3.1's naming rule it is not the fork's
// game/server/variant_t.h class: it lives in namespace ss, and every legacy
// TU that reaches the fork's ::variant_t through cbase.h sees a different
// type, never a second body under the same name.
//
// The storage is the fork class's, field for field, so the copy and the
// conversions compile to the same code. The entity slot is a plain
// CBaseHandle where the fork held the typed handle over IGameEntity. Both are
// one uint32, and the reads and writes the typed handle made are spelled out
// in shell_globals.cpp. Members nothing in server.so called are not declared:
// the save tables, Bool/Int/StringID/Color32 and the position-vector setter.
//=============================================================================//

#ifndef SS_VARIANT_H
#define SS_VARIANT_H
#ifdef _WIN32
#pragma once
#endif

#include "basehandle.h"
#include "basetypes.h"
#include "datamap.h"
#include "string_t.h"
#include "mathlib/vector.h"

#include "igameentity_fwd.h"

namespace ss
{

class variant_t
{
	// One value at a time, tagged by fieldType. The handle cannot share the
	// union because CBaseHandle has a constructor.
	union
	{
		bool     bVal;
		string_t iszVal;
		int      iVal;
		float    flVal;
		float    vecVal[ 3 ];
		color32  rgbaVal;
	};
	CBaseHandle eVal;
	fieldtype_t fieldType;

public:
	variant_t() : fieldType( FIELD_VOID ), iVal( 0 ) {}

	fieldtype_t FieldType() { return fieldType; }

	// Readers. A string read of a non-string value formats it (ToString);
	// a float read of anything else is 0.
	const char *String() const { return fieldType == FIELD_STRING ? STRING( iszVal ) : ToString(); }
	float Float() const        { return fieldType == FIELD_FLOAT ? flVal : 0; }

	// The entity a FIELD_EHANDLE value names, or NULL for any other type or a
	// stale handle. Resolved the way the typed handle's Get did.
	IGameEntity *Entity() const;

	// Writers. Each sets the value and its tag together.
	void SetBool( bool b )          { bVal = b;     fieldType = FIELD_BOOLEAN; }
	void SetString( string_t str )  { iszVal = str; fieldType = FIELD_STRING; }
	void SetInt( int val )          { iVal = val;   fieldType = FIELD_INTEGER; }
	void SetFloat( float val )      { flVal = val;  fieldType = FIELD_FLOAT; }
	void SetVector3D( const Vector &val )
	{
		vecVal[ 0 ] = val[ 0 ];
		vecVal[ 1 ] = val[ 1 ];
		vecVal[ 2 ] = val[ 2 ];
		fieldType = FIELD_VECTOR;
	}
	void SetColor32( int r, int g, int b, int a )
	{
		rgbaVal.r = r;
		rgbaVal.g = g;
		rgbaVal.b = b;
		rgbaVal.a = a;
		fieldType = FIELD_COLOR32;
	}
	void SetEntity( IGameEntity *val );

	// Out of line, in shell_globals.cpp.
	void Set( fieldtype_t ftype, void *data );
	bool Convert( fieldtype_t newType );

protected:
	const char *ToString() const;
};

} // namespace ss

#endif // SS_VARIANT_H
