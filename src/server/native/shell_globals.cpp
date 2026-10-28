//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
//
// Small native support definitions the engine boundary needs: the factory list, the
// ServerClass list head, g_bUseNetworkVars, and variant_t's out-of-line members.

#include "server_state.h"
#include "ss_init_factory.h"
#include "ss_variant.h"
#include "slot_table.h"
#include "handle_cross.h"

#include "standalone_relays.h"

#include "shell_standalone.h"
#include "ss_string.h"

// variant_t holds its entity as a CBaseHandle. There is no route from an IGameEntity
// back to a handle, so setting one from a live pointer is refused loudly.

namespace
{
CBaseHandle HandleFromGameEntity( const IGameEntity *pEntity )
{
	if ( pEntity )
	{
		const void *&pLastComplaint = ShellGameDll_Dll()->m_pLastHandleComplaint;
		if ( pLastComplaint != pEntity )
		{
			pLastComplaint = pEntity;
			Warning( "[SourceSharp] network handle: %p was set from a GAME ENTITY "
					 "pointer. There is no route from that face back to a handle "
					 "(plan_entity.md 3.3): the slot table resolves handles to "
					 "shells and not the other way. Hold an IHandleEntity, whose "
					 "GetRefEHandle() answers directly. This slot will not "
					 "replicate.\n",
					 (const void *)pEntity );
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

	return SourceSharp_CrossHandleToGameEntity<IGameEntity, IServerEntity>( SourceSharp_EntityFromHandle( eVal ) );
}

} // namespace ss

// The list every ServerClass adds itself to from its static constructor, walked by the
// engine. Constant-initialised, so it is NULL before any of those constructors run.
ServerClass *g_pServerClassHead = 0;

// Read by the _DEBUG network-var assertions.

bool g_bUseNetworkVars = true;

// variant_t's conversions. A gtest slices the text between Convert and ToString, so
// Convert must stay above ToString.

namespace ss
{

void variant_t::Set( fieldtype_t ftype, void *data )
{
	fieldType = ftype;

	switch ( ftype )
	{
		case FIELD_BOOLEAN:
			bVal = *( (bool *)data );
			break;
		case FIELD_CHARACTER:
			iVal = *( (char *)data );
			break;
		case FIELD_SHORT:
			iVal = *( (short *)data );
			break;
		case FIELD_INTEGER:
			iVal = *( (int *)data );
			break;
		case FIELD_STRING:
			iszVal = *( (string_t *)data );
			break;
		case FIELD_FLOAT:
			flVal = *( (float *)data );
			break;
		case FIELD_COLOR32:
			rgbaVal = *( (color32 *)data );
			break;

		case FIELD_VECTOR:
		case FIELD_POSITION_VECTOR:
		{
			vecVal[0] = ( (float *)data )[0];
			vecVal[1] = ( (float *)data )[1];
			vecVal[2] = ( (float *)data )[2];
			break;
		}

		// The slot holds an index and serial; read it as the untyped handle. Never call Get(),
		// which reads the entity list.
		case FIELD_EHANDLE:
			static_cast<CBaseHandle &>( eVal ) = *( (CBaseHandle *)data );
			break;
		case FIELD_CLASSPTR:
			eVal = HandleFromGameEntity( *( (IGameEntity **)data ) );
			break;
		case FIELD_VOID:
		default:
			iVal = 0;
			fieldType = FIELD_VOID;
			break;
	}
}

// Converts the variant to a new type. This function defines which I/O types can be
// automatically converted between. Returns false if the conversion is not legal.
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
					SetFloat( (float)iVal );
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
					SetInt( (int)flVal );
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
					if ( iszVal != NULL_STRING )
					{
						SetInt( atoi( STRING( iszVal ) ) );
					}
					else
					{
						SetInt( 0 );
					}
					return true;
				}

				case FIELD_FLOAT:
				{
					if ( iszVal != NULL_STRING )
					{
						SetFloat( atof( STRING( iszVal ) ) );
					}
					else
					{
						SetFloat( 0 );
					}
					return true;
				}

				case FIELD_BOOLEAN:
				{
					if ( iszVal != NULL_STRING )
					{
						SetBool( atoi( STRING( iszVal ) ) != 0 );
					}
					else
					{
						SetBool( false );
					}
					return true;
				}

				case FIELD_VECTOR:
				{
					Vector tmpVec = vec3_origin;
					if ( sscanf( STRING( iszVal ), "[%f %f %f]", &tmpVec[0], &tmpVec[1], &tmpVec[2] ) == 0 )
					{
						// Try sucking out 3 floats with no []s
						sscanf( STRING( iszVal ), "%f %f %f", &tmpVec[0], &tmpVec[1], &tmpVec[2] );
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

					sscanf( STRING( iszVal ), "%d %d %d %d", &nRed, &nGreen, &nBlue, &nAlpha );
					SetColor32( nRed, nGreen, nBlue, nAlpha );
					return true;
				}

				case FIELD_EHANDLE:
				{
					// convert the string to an entity by locating it by name.
					// Names resolve in the managed directory, which Convert cannot reach.
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
						// An unnamed entity yields the empty string.
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

// All types must be able to display as strings for debugging purposes. The returned
// pointer is a static buffer overwritten by the next call.
const char *variant_t::ToString() const
{
	char *szBuf = ShellGameDll_Dll()->m_szVariantString;

	switch ( fieldType )
	{
		case FIELD_STRING:
		{
			return ( STRING( iszVal ) );
		}

		case FIELD_BOOLEAN:
		{
			if ( bVal == 0 )
			{
				ss::StrCopy( szBuf, "false", kVariantBytes );
			}
			else
			{
				ss::StrCopy( szBuf, "true", kVariantBytes );
			}
			return ( szBuf );
		}

		case FIELD_INTEGER:
		{
			ss::Format( szBuf, kVariantBytes, "%i", iVal );
			return ( szBuf );
		}

		case FIELD_FLOAT:
		{
			ss::Format( szBuf, kVariantBytes, "%g", flVal );
			return ( szBuf );
		}

		case FIELD_COLOR32:
		{
			ss::Format( szBuf, kVariantBytes, "%d %d %d %d", (int)rgbaVal.r, (int)rgbaVal.g, (int)rgbaVal.b,
						(int)rgbaVal.a );
			return ( szBuf );
		}

		case FIELD_VECTOR:
		{
			ss::Format( szBuf, kVariantBytes, "[%g %g %g]", (double)vecVal[0], (double)vecVal[1], (double)vecVal[2] );
			return ( szBuf );
		}

		case FIELD_VOID:
		{
			szBuf[0] = '\0';
			return ( szBuf );
		}

		case FIELD_EHANDLE:
		{
			const char *pszName = ( Entity() ) ? StandaloneEntity_Name( Entity() ) : "<<null entity>>";
			ss::StrCopy( szBuf, pszName, 512 );
			return ( szBuf );
		}
	}

	return ( "No conversion to string" );
}

} // namespace ss
