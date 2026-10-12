//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
// SS_REASON a  a vtable the engine calls through
//
// The out-of-line half of entity_bases.h.
//
// Three of these four bodies were in baseentity.cpp and the fourth was in
// subs.cpp. baseentity.cpp is deleted with baseentity.h, so the three would
// have gone with it and left three pure-virtual-in-practice classes behind;
// CPointEntity::Spawn came along so that the four helper bases have one home
// rather than two, and so that subs.cpp -- which the eviction survey has been
// trying to empty for some time -- carries one fewer reason to exist.
//
// The bodies are unchanged. See entity_bases.h for why any of this moved.
//
// WHY THE BODIES CARRY BOTH ARMS AND THE DECLARATIONS DO NOT
//
// entity_bases.h keeps all four classes armed -- "the flip does not delete this
// class: 138 files name one of these four as a base, and the ones that override
// ObjectCaps call BaseClass::ObjectCaps() on it". The same argument holds for
// KeyValue, which derived classes chain to the same way, so the declarations
// stay and therefore the definitions must exist. Only the BODIES have anything
// the alias takes away, and each of the three things it takes has one answer:
//
//   SetSolid( SOLID_NONE )   Solidity is a CBaseEntity field with an engine-side
//                            consequence. Armed, whether something collides is
//                            asked, not stored: IGameEntity::ShouldCollide is
//                            one of the six and managed policy owns it. There
//                            is nothing for this line to set and nothing to
//                            forward it to, so the armed Spawn is empty. That
//                            is the value it already had -- SOLID_NONE is what
//                            "do not collide" spells, and ShouldCollide
//                            answering for itself is the same statement.
//
//   GetClassname()           Not on IGameEntity, deliberately: igameentity.h
//                            lists it among the members removed because
//                            "IServerNetworkable::GetClassName is the same
//                            string" and "every caller holds, or is handed, the
//                            IServerEntity it resolved this interface from".
//                            `this` here is not one, so the armed warning drops
//                            the name rather than reaching for a face this
//                            object does not have. The warning itself stays:
//                            what it reports -- a map key these classes refuse
//                            -- is still true and still worth printing.
//
//   BaseClass::KeyValue()    Armed, BaseClass is IGameEntity (CPointEntity) or
//                            CServerOnlyEntity (the other two), and neither
//                            parses map keys: placement over there is
//                            StandaloneEntity_ApplyMapKey, reached from
//                            mapentities.cpp, never through this chain. So
//                            there is no base handler to fall through to and
//                            `false` -- "this class did not handle that key" --
//                            is the truthful answer rather than a stub. It is
//                            also what the unarmed chain returns for every key
//                            CBaseEntity::KeyValue does not recognise.
//
//=============================================================================//

#include "cbase.h"


// Landmark class
void CPointEntity::Spawn( void )
{
}

bool CPointEntity::KeyValue( const char *szKeyName, const char *szValue ) 
{
	if ( FStrEq( szKeyName, "mins" ) || FStrEq( szKeyName, "maxs" ) )
	{
		Warning("Warning! Can't specify mins/maxs for point entities!\n");
		return true;
	}

	return false;
}						 

bool CServerOnlyPointEntity::KeyValue( const char *szKeyName, const char *szValue ) 
{
	if ( FStrEq( szKeyName, "mins" ) || FStrEq( szKeyName, "maxs" ) )
	{
		Warning("Warning! Can't specify mins/maxs for point entities!\n");
		return true;
	}

	return false;
}

bool CLogicalEntity::KeyValue( const char *szKeyName, const char *szValue ) 
{
	if ( FStrEq( szKeyName, "mins" ) || FStrEq( szKeyName, "maxs" ) )
	{
		Warning("Warning! Can't specify mins/maxs for point entities!\n");
		return true;
	}

	return false;
}
