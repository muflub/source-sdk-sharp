//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The ConVar and ConCommand carriers server.so builds. The engine's ICvar calls them through
// convar.h's vtables, so every virtual member is here with tier1's behaviour. A value string
// is allocated and freed only here, never by the engine's allocator.

#include "tier1/convar.h"
#include "tier1/strtools.h"
#include "tier1/tier1.h"
#include "icvar.h"
#include "tier0/dbg.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "ss_string.h"
#include "engine_heap.h"
#include "engine_utlvector.h"

// tier1 built this file without NDEBUG in debug configurations, where a
// development-only flag is dropped as a variable is created. Same switch.
#ifndef NDEBUG
#define SS_ALLOW_DEVELOPMENT_CVARS 1
#else
#define SS_ALLOW_DEVELOPMENT_CVARS 0
#endif

static int StripDevelopmentOnly( int nFlags )
{
	return SS_ALLOW_DEVELOPMENT_CVARS ? ( nFlags & ~FCVAR_DEVELOPMENTONLY ) : nFlags;
}

// Carriers built before ConVar_Register wait on a list; after it, they register at once.
ConCommandBase *ConCommandBase::s_pConCommandBases = NULL;
IConCommandBaseAccessor *ConCommandBase::s_pAccessor = NULL;

namespace
{

int s_nRegisterFlags = 0;
CVarDLLIdentifier_t s_nDLLIdentifier = -1;
bool s_bRegistered = false;

// Without a caller's accessor, a carrier is handed to the engine's list.
class CEngineListAccessor : public IConCommandBaseAccessor
{
public:
	virtual bool RegisterConCommandBase( ConCommandBase *pVar )
	{
		g_pCVar->RegisterConCommand( pVar );
		return true;
	}
};

CEngineListAccessor s_EngineListAccessor;

}

void ConVar_Register( int nCVarFlag, IConCommandBaseAccessor *pAccessor )
{
	if ( s_bRegistered || !g_pCVar )
		return;

	s_bRegistered = true;
	s_nRegisterFlags = nCVarFlag;
	s_nDLLIdentifier = g_pCVar->AllocateDLLIdentifier();
	ConCommandBase::s_pAccessor = pAccessor ? pAccessor : &s_EngineListAccessor;

	// Each Init may hand the carrier to code that links it elsewhere, so the
	// next pointer is read first.
	ConCommandBase *pWaiting = ConCommandBase::s_pConCommandBases;
	while ( pWaiting )
	{
		ConCommandBase *pAfter = pWaiting->m_pNext;
		pWaiting->AddFlags( s_nRegisterFlags );
		pWaiting->Init();
		pWaiting = pAfter;
	}

	g_pCVar->ProcessQueuedMaterialThreadConVarSets();
	ConCommandBase::s_pConCommandBases = NULL;
}

void ConVar_Unregister()
{
	if ( !s_bRegistered || !g_pCVar )
		return;

	g_pCVar->UnregisterConCommands( s_nDLLIdentifier );
	s_nDLLIdentifier = -1;
	s_bRegistered = false;
}

ConCommandBase::ConCommandBase( void )
	: m_pNext( NULL ), m_bRegistered( false ), m_pszName( NULL ), m_pszHelpString( NULL ), m_nFlags( 0 )
{
}

ConCommandBase::~ConCommandBase( void )
{
}

bool ConCommandBase::IsCommand( void ) const
{
	return true;
}

CVarDLLIdentifier_t ConCommandBase::GetDLLIdentifier() const
{
	return s_nDLLIdentifier;
}

void ConCommandBase::CreateBase( const char *pName, const char *pHelpString, int flags )
{
	Assert( pName );
	m_bRegistered = false;
	m_pszName = pName;
	m_pszHelpString = pHelpString ? pHelpString : "";
	m_nFlags = StripDevelopmentOnly( flags );

	// An unregistered carrier never joins the waiting list.
	if ( m_nFlags & FCVAR_UNREGISTERED )
	{
		m_pNext = NULL;
	}
	else
	{
		m_pNext = s_pConCommandBases;
		s_pConCommandBases = this;
	}

	// Built after ConVar_Register: register now.
	if ( s_pAccessor )
		Init();
}

void ConCommandBase::Init()
{
	if ( s_pAccessor )
		s_pAccessor->RegisterConCommandBase( this );
}

const char *ConCommandBase::GetName( void ) const
{
	return m_pszName;
}

bool ConCommandBase::IsFlagSet( int flag ) const
{
	return ( m_nFlags & flag ) != 0;
}

void ConCommandBase::AddFlags( int flags )
{
	m_nFlags = StripDevelopmentOnly( m_nFlags | flags );
}

const char *ConCommandBase::GetHelpText( void ) const
{
	return m_pszHelpString;
}

bool ConCommandBase::IsRegistered( void ) const
{
	return m_bRegistered;
}

ConCommand::ConCommand( const char *pName, ICommandCallback *pCallback, const char *pHelpString, int flags, ICommandCompletionCallback *pCompletionCallback )
{
	m_pCommandCallback = pCallback;
	m_bUsingNewCommandCallback = false;
	m_bUsingCommandCallbackInterface = true;
	m_pCommandCompletionCallback = pCompletionCallback;
	m_bHasCompletionCallback = pCompletionCallback != NULL;
	BaseClass::CreateBase( pName, pHelpString, flags );
}

ConCommand::~ConCommand( void )
{
}

bool ConCommand::IsCommand( void ) const
{
	return true;
}

void ConCommand::Dispatch( const CCommand &command )
{
	// Exactly one of the three callback forms is in use.
	if ( m_bUsingNewCommandCallback )
	{
		if ( m_fnCommandCallback )
			return m_fnCommandCallback( command );
	}
	else if ( m_bUsingCommandCallbackInterface )
	{
		if ( m_pCommandCallback )
			return m_pCommandCallback->CommandCallback( command );
	}
	else if ( m_fnCommandCallbackV1 )
	{
		return m_fnCommandCallbackV1();
	}

	AssertMsg( 0, "Encountered ConCommand '%s' without a callback!\n", GetName() );
}

int ConCommand::AutoCompleteSuggest( const char *partial, CUtlVector< CUtlString > &commands )
{
	if ( m_bUsingCommandCallbackInterface )
		return m_pCommandCompletionCallback ? m_pCommandCompletionCallback->CommandCompletionCallback( partial, commands ) : 0;

	if ( !m_fnCompletionCallback )
		return 0;

	char suggestions[ COMMAND_COMPLETION_MAXITEMS ][ COMMAND_COMPLETION_ITEM_LENGTH ];
	int nCount = m_fnCompletionCallback( partial, suggestions );

	// The engine owns `commands` and frees it with its own allocator, so suggestions are
	// appended with that allocator too.
	ss::IEngineHeap *pHeap = ss::FindEngineHeap();
	ss::EngineUtlVectorLayout< ss::EngineUtlString > &vec =
		ss::EngineUtlVectorLayoutOf< ss::EngineUtlString >( commands );
	for ( int i = 0; i < nCount; i++ )
	{
		if ( !ss::EngineUtlStringVectorAppend( pHeap, vec, suggestions[i] ) )
			return i;
	}
	return nCount;
}

bool ConCommand::CanAutoComplete( void )
{
	return m_bHasCompletionCallback;
}

// A variable's state lives on its parent (itself, for every variable this DLL builds).
ConVar::ConVar( const char *pName, const char *pDefaultValue, int flags )
{
	Create( pName, pDefaultValue, flags );
}

ConVar::ConVar( const char *pName, const char *pDefaultValue, int flags, const char *pHelpString, bool bMin, float fMin, bool bMax, float fMax )
{
	Create( pName, pDefaultValue, flags, pHelpString, bMin, fMin, bMax, fMax );
}

ConVar::~ConVar( void )
{
	delete[] m_pszString;
	m_pszString = NULL;
}

bool ConVar::IsFlagSet( int flag ) const
{
	return ( m_pParent->m_nFlags & flag ) != 0;
}

const char *ConVar::GetHelpText( void ) const
{
	return m_pParent->m_pszHelpString;
}

void ConVar::AddFlags( int flags )
{
	m_pParent->m_nFlags = StripDevelopmentOnly( m_pParent->m_nFlags | flags );
}

bool ConVar::IsRegistered( void ) const
{
	return m_pParent->m_bRegistered;
}

const char *ConVar::GetName( void ) const
{
	return m_pParent->m_pszName;
}

bool ConVar::IsCommand( void ) const
{
	return false;
}

void ConVar::Init()
{
	BaseClass::Init();
}

// A value the material thread must set is queued while it may not.
static bool QueuedForMaterialThread( ConVar *pVar )
{
	return pVar->IsFlagSet( FCVAR_MATERIAL_THREAD_MASK ) && g_pCVar && !g_pCVar->IsMaterialThreadSetAllowed();
}

void ConVar::InternalSetValue( const char *value )
{
	if ( QueuedForMaterialThread( this ) )
	{
		g_pCVar->QueueMaterialThreadSetValue( this, value );
		return;
	}

	Assert( m_pParent == this );

	const float flOldValue = m_fValue;
	float flNewValue = value ? (float)atof( value ) : 0.0f;

	// A clamped value is stored as the clamped number's text, not as given.
	char clampedText[32];
	const char *pStore = value;
	if ( ClampValue( flNewValue ) )
	{
		snprintf( clampedText, sizeof( clampedText ), "%f", flNewValue );
		pStore = clampedText;
	}

	m_fValue = flNewValue;
	m_nValue = (int)flNewValue;

	if ( !( m_nFlags & FCVAR_NEVER_AS_STRING ) )
		ChangeStringValue( pStore, flOldValue );
}

void ConVar::ChangeStringValue( const char *tempVal, float flOldValue )
{
	Assert( !( m_nFlags & FCVAR_NEVER_AS_STRING ) );

	// The old text is kept for the callbacks: the buffer may be replaced.
	char *pOld = (char *)stackalloc( m_StringLength );
	memcpy( pOld, m_pszString, m_StringLength );

	if ( !tempVal )
	{
		m_pszString[0] = '\0';
	}
	else
	{
		const int nNeeded = (int)strlen( tempVal ) + 1;
		if ( nNeeded > m_StringLength )
		{
			delete[] m_pszString;
			m_pszString = new char[nNeeded];
			m_StringLength = nNeeded;
		}
		memcpy( m_pszString, tempVal, nNeeded );
	}

	// Only a change of text is announced.
	if ( strcmp( pOld, m_pszString ) != 0 )
	{
		if ( m_fnChangeCallback )
			m_fnChangeCallback( this, pOld, flOldValue );
		g_pCVar->CallGlobalChangeCallbacks( this, pOld, flOldValue );
	}

	stackfree( pOld );
}

bool ConVar::ClampValue( float &value )
{
	// Competitive bounds are the tighter ones, so they are tried first; with
	// neither bound set, competitive mode pins the default.
	if ( m_bCompetitiveRestrictions )
	{
		if ( m_bHasCompMin && value < m_fCompMinVal )
		{
			value = m_fCompMinVal;
			return true;
		}
		if ( m_bHasCompMax && value > m_fCompMaxVal )
		{
			value = m_fCompMaxVal;
			return true;
		}
		if ( !m_bHasCompMin && !m_bHasCompMax )
		{
			const float flDefault = ss::AtoF( m_pszDefaultValue );
			if ( fabs( value - flDefault ) > 0.0001f )
			{
				value = flDefault;
				return true;
			}
		}
	}

	if ( m_bHasMin && value < m_fMinVal )
	{
		value = m_fMinVal;
		return true;
	}
	if ( m_bHasMax && value > m_fMaxVal )
	{
		value = m_fMaxVal;
		return true;
	}
	return false;
}

void ConVar::InternalSetFloatValue( float fNewValue )
{
	InternalSetFloatValue2( fNewValue );
}

void ConVar::InternalSetFloatValue2( float fNewValue, bool bForce )
{
	if ( !bForce && fNewValue == m_fValue )
		return;

	if ( QueuedForMaterialThread( this ) )
	{
		g_pCVar->QueueMaterialThreadSetValue( this, fNewValue );
		return;
	}

	Assert( m_pParent == this );

	ClampValue( fNewValue );

	const float flOldValue = m_fValue;
	m_fValue = fNewValue;
	m_nValue = (int)m_fValue;

	if ( m_nFlags & FCVAR_NEVER_AS_STRING )
	{
		Assert( !m_fnChangeCallback );
		return;
	}

	char text[32];
	snprintf( text, sizeof( text ), "%f", m_fValue );
	ChangeStringValue( text, flOldValue );
}

void ConVar::InternalSetIntValue( int nValue )
{
	if ( nValue == m_nValue )
		return;

	if ( QueuedForMaterialThread( this ) )
	{
		g_pCVar->QueueMaterialThreadSetValue( this, nValue );
		return;
	}

	Assert( m_pParent == this );

	float flValue = (float)nValue;
	if ( ClampValue( flValue ) )
		nValue = (int)flValue;

	const float flOldValue = m_fValue;
	m_fValue = flValue;
	m_nValue = nValue;

	if ( m_nFlags & FCVAR_NEVER_AS_STRING )
	{
		Assert( !m_fnChangeCallback );
		return;
	}

	char text[32];
	snprintf( text, sizeof( text ), "%d", m_nValue );
	ChangeStringValue( text, flOldValue );
}

void ConVar::Create_Vtbl( const char *pName, const char *pDefaultValue, int flags, const char *pHelpString,
                          bool bMin, float fMin, bool bMax, float fMax, FnChangeCallback_t callback )
{
	Create( pName, pDefaultValue, flags, pHelpString, bMin, fMin, bMax, fMax, false, 0.0f, false, 0.0f, callback );
}

void ConVar::Create( const char *pName, const char *pDefaultValue, int flags, const char *pHelpString,
                     bool bMin, float fMin, bool bMax, float fMax, bool bCompMin, float fCompMin,
                     bool bCompMax, float fCompMax, FnChangeCallback_t callback )
{
	m_pParent = this;
	SetDefault( pDefaultValue );

	// The value starts as a copy of the default text.
	m_StringLength = (int)strlen( m_pszDefaultValue ) + 1;
	m_pszString = new char[m_StringLength];
	memcpy( m_pszString, m_pszDefaultValue, m_StringLength );

	m_fnChangeCallback = callback;
	m_bCompetitiveRestrictions = false;

	// The four bounds, each a flag and its value.
	m_bHasMin = bMin;         m_fMinVal = fMin;
	m_bHasMax = bMax;         m_fMaxVal = fMax;
	m_bHasCompMin = bCompMin; m_fCompMinVal = fCompMin;
	m_bHasCompMax = bCompMax; m_fCompMaxVal = fCompMax;

	// The int is parsed from the text, not truncated from the float.
	m_fValue = (float)atof( m_pszString );
	m_nValue = atoi( m_pszString );

	Assert( !m_bHasMin || m_fValue >= m_fMinVal );
	Assert( !m_bHasMax || m_fValue <= m_fMaxVal );

	BaseClass::CreateBase( pName, pHelpString, flags );
}

void ConVar::SetValue( const char *value )
{
	static_cast< ConVar * >( m_pParent )->InternalSetValue( value );
}

void ConVar::SetValue( float value )
{
	static_cast< ConVar * >( m_pParent )->InternalSetFloatValue2( value );
}

void ConVar::SetValue( int value )
{
	static_cast< ConVar * >( m_pParent )->InternalSetIntValue( value );
}

bool ConVar::GetMin( float &minVal ) const
{
	minVal = m_pParent->m_fMinVal;
	return m_pParent->m_bHasMin;
}

bool ConVar::GetMax( float &maxVal ) const
{
	maxVal = m_pParent->m_fMaxVal;
	return m_pParent->m_bHasMax;
}

const char *ConVar::GetDefault( void ) const
{
	return m_pParent->m_pszDefaultValue;
}

void ConVar::SetDefault( const char *pszDefault )
{
	m_pszDefaultValue = pszDefault ? pszDefault : "";
}

// ConVarRef: an unknown name resolves to a shared stand-in that reads zero and ignores
// writes; IsValid tells the two apart.
namespace
{

class CMissingConVar : public ConVar
{
public:
	CMissingConVar() : ConVar( "", "0" ) {}
	virtual void SetValue( const char * ) {}
	virtual void SetValue( float ) {}
	virtual void SetValue( int ) {}
	virtual const char *GetName( void ) const { return ""; }
	virtual bool IsFlagSet( int ) const { return false; }
};

CMissingConVar s_MissingConVar;

}

ConVarRef::ConVarRef( const char *pName )
{
	Init( pName, false );
}

void ConVarRef::Init( const char *pName, bool bIgnoreMissing )
{
	IConVar *pFound = g_pCVar ? g_pCVar->FindVar( pName ) : NULL;
	m_pConVar = pFound ? pFound : &s_MissingConVar;
	m_pConVarState = static_cast< ConVar * >( m_pConVar );

	if ( IsValid() )
		return;

	// Before the console exists, one warning; after, one per miss.
	static bool s_bWarnedWithoutConsole = false;
	if ( g_pCVar || !s_bWarnedWithoutConsole )
	{
		if ( !bIgnoreMissing )
			Warning( "ConVarRef %s doesn't point to an existing ConVar\n", pName );
		s_bWarnedWithoutConsole = true;
	}
}

ConVarRef::ConVarRef( IConVar *pConVar )
{
	m_pConVar = pConVar ? pConVar : &s_MissingConVar;
	m_pConVarState = static_cast< ConVar * >( m_pConVar );
}

bool ConVarRef::IsValid() const
{
	return m_pConVar != &s_MissingConVar;
}
