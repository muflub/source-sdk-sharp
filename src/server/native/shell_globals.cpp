//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — five files, eight symbols, no decisions ============//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_server_all.md section 9, Wave 8: "the tiny globals (globals.cpp,
// init_factory.cpp, variant_t.cpp, server_class.cpp, networkvar.cpp — nine
// symbols between them, and one file can hold all nine) move under
// src/sourcesharp/ as the permanent native support library."
//
// This is that file. Five translation units become one, and the meter falls by
// four for 102 lines of code that were never going to be anything else.
//
// WHY THIS IS ALLOWED WHILE ALMOST NOTHING ELSE IS. The eviction graph
// (docs/server_eviction_graph.md) says eighty-one of the remaining stock files
// are one mutually entangled knot: no member can have its $File line removed
// until essentially all of them do. Four of the five below are IN that knot.
//
// But a RELOCATION is not an eviction. Nothing stops naming these symbols and
// nothing has to: the definitions move and the link graph is unchanged. The
// closure arithmetic constrains what can be DELETED, not what can be MOVED,
// and that distinction is the only reason this commit exists.
//
// WHY THESE FIVE AND NOT OTHERS. Section 2 sets two tests for a relocation and
// both have to be met by name rather than by "this is engine-ish":
//
//   1. an impossibility, not a preference. g_pServerClassHead is the head of
//      the linked list the engine walks to find the game's ServerClasses --
//      section 3's first case, a pointer the engine follows into a C++ ABI.
//      g_bUseNetworkVars is read by the _DEBUG network-var assertions compiled
//      into every entity header. factorylist_t is how the engine's factories
//      are handed across the DLL boundary at load. None of the three can be a
//      managed object; they are the boundary itself.
//
//   2. the external contract must contain NO DECISION -- no branch a mod would
//      ever want to change. Read them: three uninitialised globals, a struct
//      copied in and copied out, one field assignment, one pointer, one bool.
//      There is not a conditional among them.
//
// g_vecAttackDir, g_iSkillLevel and g_fGameOver are the weakest of the five on
// test 1 -- they are plain state, and if anything ever reads them for a
// decision that decision belongs in C#. They are here because they are three
// definitions in a 27-line file whose only other content is a comment saying
// they are here for convenient centralisation, and because leaving one file
// behind to hold three uninitialised globals would be the whole cost of the
// merge for none of the benefit. If C# comes to own the skill level or the
// game-over flag, they leave from here. g_vecAttackDir and g_iSkillLevel left
// at plan_server_native_v2 T5: no server object imported either (nm U).
//
// SERVER_CLASS.CPP AND NETWORKVAR.CPP STAY ON DISK, and not for the same
// reason -- which is worth spelling out because "the client compiles both" is
// the obvious summary and it is half wrong:
//
//   networkvar.cpp    named by client_base.vpc AND server_base.vpc, and the
//                     client really does compile it.
//   server_class.cpp  named by server_base.vpc ONLY. The client does not
//                     compile it. What keeps it on disk is the stock server
//                     project, which this port does not build but must not
//                     break.
//
// Only the SourceSharp server's two $File lines went, and this file supplies
// its copies. globals.cpp, init_factory.cpp and variant_t.cpp are server-only
// and are deleted from the tree, because a .cpp left behind for later is a .cpp
// that cannot be put back once Phase 13 makes CBaseEntity a typedef -- the same
// reasoning bmodels.cpp went out on.
//
//=============================================================================//

#include "ss_init_factory.h"
#include "ss_variant.h"
#include "slot_table.h"   // SourceSharp_EntityFromHandle: the read CHandle::Get made
#include "handle_cross.h" // ...and the crossing it made from there
#include "server_class.h"

// StandaloneEntity_Name and the shared find-by-name, for the conversion's handle
// arms; the resolver, for turning that find's handle back into an object.
#include "standalone_relays.h"
#include "shell_resolve.h"

// The shell class itself, for the armed arm of the string-to-handle conversion:
// the resolver hands back a shell pointer and the upcast to the entity boundary
// needs the complete type, not the resolver's forward declaration.
#include "shell_standalone.h"
#include "ss_string.h"


//-----------------------------------------------------------------------------
// game/server/globals.cpp
//
// Stock's own comment: "DLL-wide global variable definitions. They're all
// defined here, for convenient centralization. Source files that need them
// should `extern ...` declare each variable, to better document what globals
// they care about."
//-----------------------------------------------------------------------------
// g_fGameOver went in plan_server_native_v2 T6: CWorld::Precache wrote it and
// no server object read it (nm U).

//-----------------------------------------------------------------------------
// game/server/init_factory.cpp
//
// The engine hands the game DLL its factories at load and asks for them back.
// One struct, stored and returned; s_factories is file-local and always was.
//-----------------------------------------------------------------------------
static ss::factorylist_t s_factories;

// Store off the factories
void ss::FactoryList_Store( const ss::factorylist_t &sourceData )
{
	s_factories = sourceData;
}

// retrieve the stored factories
void ss::FactoryList_Retrieve( ss::factorylist_t &destData )
{
	destData = s_factories;
}

//-----------------------------------------------------------------------------
// game/server/variant_t.cpp
//
// Out of line because variant_t.h cannot name CBaseEntity -- it is included
// from the engine prelude. The whole file was this one assignment.
//
// ss::variant_t (ss_variant.h, P5 lane E) holds its entity as a CBaseHandle
// where the fork's class held the typed handle over IGameEntity, so the two
// things that template did are spelled out here, unchanged:
//
//   the write, the typed handle's Set, was SourceSharp_EntityHandle's
//   game-entity arm (public/networkvar.h): there is no route from an
//   IGameEntity back to a handle, so a live pointer is refused loudly and
//   the slot holds the invalid handle;
//   the read, the typed handle's Get, was baseentity.h's specialisation:
//   SourceSharp_CrossHandleToGameEntity (handle_cross.h) over CBaseHandle::Get,
//   which on the server is SourceSharp_EntityFromHandle.
//
// The refusal's once-per-pointer limiter is this file's own now, not shared
// with networkvar.h's inline. Nothing reaches either arm today: no TU builds
// a variant_t with an entity in it (nm: no caller of SetEntity, Set or
// Convert outside this file, and DispatchInput converts only to a float).
//-----------------------------------------------------------------------------

namespace
{
	CBaseHandle HandleFromGameEntity( const IGameEntity *pEntity )
	{
		if ( pEntity )
		{
			static const IGameEntity *s_pLastComplaint = NULL;
			if ( s_pLastComplaint != pEntity )
			{
				s_pLastComplaint = pEntity;
				Warning( "[SourceSharp] network handle: %p was set from a GAME ENTITY "
				         "pointer. There is no route from that face back to a handle "
				         "(plan_entity.md 3.3): the slot table resolves handles to "
				         "shells and not the other way. Hold an IHandleEntity, whose "
				         "GetRefEHandle() answers directly. This slot will not "
				         "replicate.\n", (const void *)pEntity );
				AssertMsg( false, "SourceSharp_EntityHandle: no reverse route from IGameEntity" );
			}
		}

		return CBaseHandle();
	}
}

namespace ss
{

void variant_t::SetEntity( IGameEntity *val )
{
	eVal = HandleFromGameEntity( val );
	fieldType = FIELD_EHANDLE;
}

IGameEntity *variant_t::Entity() const
{
	if ( fieldType != FIELD_EHANDLE )
		return NULL;

	return SourceSharp_CrossHandleToGameEntity< IGameEntity, IServerEntity >( SourceSharp_EntityFromHandle( eVal ) );
}

} // namespace ss

//-----------------------------------------------------------------------------
// public/server_class.cpp -- the SERVER's copy. The file stays on disk and the
// client still compiles it.
//
// The head of the linked list every ServerClass adds itself to from its own
// static constructor, and the list the engine walks to discover what this DLL
// networks. Zero-initialised as a constant, so it is in .bss and is already
// NULL before any of those constructors run -- which is what makes the order
// of static initialisation across translation units not matter here.
//-----------------------------------------------------------------------------
ServerClass *g_pServerClassHead = 0;

//-----------------------------------------------------------------------------
// public/networkvar.cpp -- the SERVER's copy. Same arrangement.
//
// Read by the _DEBUG network-var assertions in baseentity.h and
// baseanimating.h: TWENTY-SIX objects name it in a debug build and ZERO do in
// release. That asymmetry is section 8's trap 4 and the reason evict_probe
// takes a MODE argument.
//
// The guard is stock's, kept verbatim rather than dropped as obviously true
// here: it is what makes the same source legal in a statically linked build.
//-----------------------------------------------------------------------------
#if !defined(_STATIC_LINKED) || defined(_SHARED_LIB)

bool g_bUseNetworkVars = true;

#endif


//=============================================================================//
// THE VALUE AN ENTITY OUTPUT CARRIES -- three more of variant_t's out-of-line
// members, moved out of game/server/cbase.cpp by the armed link, to sit beside
// variant_t::SetEntity, which the server-globals merge put here earlier.
//
// HERE AND NOT IN A RESTORED game/server/variant_t.cpp, which is where they
// would naturally have gone: that file was DELETED by that merge and a gtest
// pins it deleted, on the argument that one merged file is better than several
// and two copies of a definition is a question with no answer. Bringing it back
// to hold three bodies would have re-opened a decision somebody already made
// for good reasons. This lane tried it and the tree said no, by name.
//
// NOT ENTITY CODE, which is why they fit here. All three switch on the variant's
// own field type and touch its own members. The only thing any of them reaches
// outside itself is the entity-name relay and the shared find-by-name -- and the
// find-by-name is ASKED, never copied, so this file adds nothing to the count
// that measures how much of the stock list the shim still names. Verified: that
// count is unchanged.
//
// THE ORDER OF THE TWO BIG ONES IS LOAD-BEARING: a gtest reads this file and
// slices the text BETWEEN the conversion and the to-string body to count the
// conversion's arms. Convert must stay above ToString.
//=============================================================================//

namespace ss
{

void variant_t::Set( fieldtype_t ftype, void *data )
{
	fieldType = ftype;

	switch ( ftype )
	{
	case FIELD_BOOLEAN:		bVal = *((bool *)data);				break;
	case FIELD_CHARACTER:	iVal = *((char *)data);				break;
	case FIELD_SHORT:		iVal = *((short *)data);			break;
	case FIELD_INTEGER:		iVal = *((int *)data);				break;
	case FIELD_STRING:		iszVal = *((string_t *)data);		break;
	case FIELD_FLOAT:		flVal = *((float *)data);			break;
	case FIELD_COLOR32:		rgbaVal = *((color32 *)data);		break;

	case FIELD_VECTOR:
	case FIELD_POSITION_VECTOR:
	{
		vecVal[0] = ((float *)data)[0];
		vecVal[1] = ((float *)data)[1];
		vecVal[2] = ((float *)data)[2];
		break;
	}

	// §1e, AND THE SWEEP NAMES THIS SITE. This read the field through the SDK's
	// typed handle typedef -- the one that resolves to a template handle over
	// the entity class -- which is a TYPED handle in a shim .cpp, and
	// typed_handle_sweep_test's directory walk forbids that outright.
	//
	// THE NAMES ARE SPELLED AROUND ON PURPOSE. That walk reads the file as TEXT
	// and does not strip comments, so a comment naming the typedef counts as a
	// site: the first version of this note took the count from 1 to 2 and
	// reddened the very fact it was explaining. Prose counts as code to that
	// matcher; sourcesharp/test/typed_handle_sweep_test.cpp carries the name.
	//
	// The value in a datamap FIELD_EHANDLE slot is an index and a serial and
	// nothing else, so it is read as the UNTYPED handle it is and assigned to
	// eVal's own CBaseHandle subobject: same bytes, no typed spelling, and above
	// all no CBaseHandle::Get() here -- Get() reads the entity list, which is
	// the table this plan deletes.
	case FIELD_EHANDLE:		static_cast< CBaseHandle & >( eVal ) = *((CBaseHandle *)data);	break;
	case FIELD_CLASSPTR:	eVal = HandleFromGameEntity( *((IGameEntity **)data) );		break;
	case FIELD_VOID:		
	default:
		iVal = 0; fieldType = FIELD_VOID;	
		break;
	}
}


//-----------------------------------------------------------------------------
// Purpose: Converts the variant to a new type. This function defines which I/O
//			types can be automatically converted between. Connections that require
//			an unsupported conversion will cause an error message at runtime.
// Input  : newType - the type to convert to
// Output : Returns true on success, false if the conversion is not legal
//-----------------------------------------------------------------------------
bool variant_t::Convert( fieldtype_t newType )
{
	if ( newType == fieldType )
	{
		return true;
	}

	//
	// Converting to a null value is easy.
	//
	if ( newType == FIELD_VOID )
	{
		Set( FIELD_VOID, NULL );
		return true;
	}

	//
	// FIELD_INPUT accepts the variant type directly.
	//
	if ( newType == FIELD_INPUT )
	{
		return true;
	}

	switch ( fieldType )
	{
		case FIELD_INTEGER:
		{
			switch ( newType )
			{
				case FIELD_FLOAT:
				{
					SetFloat( (float) iVal );
					return true;
				}

				case FIELD_BOOLEAN:
				{
					SetBool( iVal != 0 );
					return true;
				}
			}
			break;
		}

		case FIELD_FLOAT:
		{
			switch ( newType )
			{
				case FIELD_INTEGER:
				{
					SetInt( (int) flVal );
					return true;
				}

				case FIELD_BOOLEAN:
				{
					SetBool( flVal != 0 );
					return true;
				}
			}
			break;
		}

		//
		// Everyone must convert from FIELD_STRING if possible, since
		// parameter overrides are always passed as strings.
		//
		case FIELD_STRING:
		{
			switch ( newType )
			{
				case FIELD_INTEGER:
				{
					if (iszVal != NULL_STRING)
					{
						SetInt(atoi(STRING(iszVal)));
					}
					else
					{
						SetInt(0);
					}
					return true;
				}

				case FIELD_FLOAT:
				{
					if (iszVal != NULL_STRING)
					{
						SetFloat(atof(STRING(iszVal)));
					}
					else
					{
						SetFloat(0);
					}
					return true;
				}

				case FIELD_BOOLEAN:
				{
					if (iszVal != NULL_STRING)
					{
						SetBool( atoi(STRING(iszVal)) != 0 );
					}
					else
					{
						SetBool(false);
					}
					return true;
				}

				case FIELD_VECTOR:
				{
					Vector tmpVec = vec3_origin;
					if (sscanf(STRING(iszVal), "[%f %f %f]", &tmpVec[0], &tmpVec[1], &tmpVec[2]) == 0)
					{
						// Try sucking out 3 floats with no []s
						sscanf(STRING(iszVal), "%f %f %f", &tmpVec[0], &tmpVec[1], &tmpVec[2]);
					}
					SetVector3D( tmpVec );
					return true;
				}

				case FIELD_COLOR32:
				{
					int nRed = 0;
					int nGreen = 0;
					int nBlue = 0;
					int nAlpha = 255;

					sscanf(STRING(iszVal), "%d %d %d %d", &nRed, &nGreen, &nBlue, &nAlpha);
					SetColor32( nRed, nGreen, nBlue, nAlpha );
					return true;
				}

				case FIELD_EHANDLE:
				{
					// convert the string to an entity by locating it by name.
					// NULL, always, and it always was: the one find-by-name it
					// asked, SourceSharp_FindBaseEntityByName, answered the
					// invalid handle because no stock-hierarchy entity is ever
					// registered on the server (plan_server_native_v2 T2 deleted
					// it). A name resolves in the managed directory, which
					// Convert cannot reach -- and nothing calls Convert (above).
					IGameEntity *ent = NULL;
					SetEntity( ent );
					return true;
				}
			}
		
			break;
		}

		case FIELD_EHANDLE:
		{
			switch ( newType )
			{
				case FIELD_STRING:
				{
					// take the entities targetname as the string
					if ( Entity() != NULL )
					{
						// ARMED, THE NAME COMES FROM THE RELAY. Same answer,
						// pooled the same way: StandaloneEntity_Name returns the
						// mapper's name as a const char *, and MAKE_STRING is how
						// the shell already pools one (shell_standalone_base.cpp
						// :1738 does exactly this before firing an output). An
						// unnamed entity yields the empty string, which is what
						// GetEntityName's NULL_STRING converts to as well.
						SetString( MAKE_STRING( StandaloneEntity_Name( Entity() ) ) );
					}
					return true;
				}
			}
			break;
		}
	}

	// invalid conversion
	return false;
}


//-----------------------------------------------------------------------------
// Purpose: All types must be able to display as strings for debugging purposes.
// Output : Returns a pointer to the string that represents this value.
//
//			NOTE: The returned pointer should not be stored by the caller as
//				  subsequent calls to this function will overwrite the contents
//				  of the buffer!
//-----------------------------------------------------------------------------
const char *variant_t::ToString( void ) const
{
	static char szBuf[512];

	switch (fieldType)
	{
	case FIELD_STRING:
		{
			return(STRING(iszVal));
		}

	case FIELD_BOOLEAN:
		{
			if (bVal == 0)
			{
				ss::StrCopy(szBuf, "false",sizeof(szBuf));
			}
			else
			{
				ss::StrCopy(szBuf, "true",sizeof(szBuf));
			}
			return(szBuf);
		}

	case FIELD_INTEGER:
		{
			ss::Format( szBuf, sizeof( szBuf ), "%i", iVal );
			return(szBuf);
		}

	case FIELD_FLOAT:
		{
			ss::Format(szBuf,sizeof(szBuf), "%g", flVal);
			return(szBuf);
		}

	case FIELD_COLOR32:
		{
			ss::Format(szBuf,sizeof(szBuf), "%d %d %d %d", (int)rgbaVal.r, (int)rgbaVal.g, (int)rgbaVal.b, (int)rgbaVal.a);
			return(szBuf);
		}

	case FIELD_VECTOR:
		{
			ss::Format(szBuf,sizeof(szBuf), "[%g %g %g]", (double)vecVal[0], (double)vecVal[1], (double)vecVal[2]);
			return(szBuf);
		}

	case FIELD_VOID:
		{
			szBuf[0] = '\0';
			return(szBuf);
		}

	case FIELD_EHANDLE:
		{
			// ARMED, THE NAME COMES FROM THE RELAY -- see Convert() above. The
			// "<<null entity>>" arm is untouched and still means what it says.
			const char *pszName = (Entity()) ? StandaloneEntity_Name( Entity() ) : "<<null entity>>";
			ss::StrCopy( szBuf, pszName, 512 );
			return (szBuf);
		}
	}

	return("No conversion to string");
}

} // namespace ss
