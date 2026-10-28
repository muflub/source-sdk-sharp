//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The members of tier1's KeyValues and ILocalize that client.so calls.
// SS_REASON d  a layout the engine reads at fixed offsets
//
// A KeyValues crosses into the engine and the material system, which read it by tier1's
// layout, so this is tier1's class with tier1's behaviour.
//
// Keys are allocated through vstdlib's KeyValuesSystem, as tier1's operator new does, so
// either side can free them. Value strings live on the engine heap because the material
// system frees them with tier0's delete[]. KeyValuesSystem is looked up at each use.

#include "tier1/ilocalize.h"
#include "tier1/KeyValues.h"
#include "vstdlib/IKeyValuesSystem.h"
#include "tier0/platform.h"
#include "engine_heap.h"
#include "loaded_symbol.h"
#include "ss_string.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

int ILocalize::ConvertUnicodeToANSI( const wchar_t *unicode, char *ansi, int ansiBufferSize )
{
	return ss::WideToUTF8( unicode, ansi, ansiBufferSize );
}

namespace
{
IKeyValuesSystem *KVSystem()
{
	typedef IKeyValuesSystem *( *KeyValuesSystemFn )();
	KeyValuesSystemFn pfn = (KeyValuesSystemFn)ss::FindLoadedSymbol( "libvstdlib.so", "KeyValuesSystem" );
	if ( !pfn )
	{
		fprintf( stderr, "[SourceSharp] KeyValues: no libvstdlib.so KeyValuesSystem in this process\n" );
		abort();
	}
	return pfn();
}

char *NewValue( size_t nBytes )
{
	ss::IEngineHeap *pHeap = ss::FindEngineHeap();
	if ( !pHeap )
	{
		fprintf( stderr, "[SourceSharp] KeyValues: no libtier0.so heap for a value string\n" );
		abort();
	}
	return (char *)pHeap->Alloc( nBytes );
}

// A string value: a copy on the engine heap, "" for NULL.
char *CopyValue( const char *psz )
{
	if ( !psz )
		psz = "";
	const size_t n = strlen( psz ) + 1;
	char *p = NewValue( n );
	memcpy( p, psz, n );
	return p;
}
} // namespace

void *KeyValues::operator new( size_t iAllocSize )
{
	return KVSystem()->AllocKeyValuesMemory( (int)iAllocSize );
}

void KeyValues::operator delete( void *pMem )
{
	KVSystem()->FreeKeyValuesMemory( pMem );
}

KeyValues::KeyValues( const char *setName )
{
	Init();
	SetName( setName );
}

KeyValues::KeyValues( const char *setName, const char *firstKey, int firstValue, const char *secondKey, int secondValue )
{
	Init();
	SetName( setName );
	SetInt( firstKey, firstValue );
	SetInt( secondKey, secondValue );
}

// tier1's starting state: no name, no value, no links; escapes off and
// conditionals on, as a parse would inherit them.
void KeyValues::Init()
{
	m_pSub = m_pPeer = m_pChain = NULL;
	m_pValue = NULL;
	m_sValue = NULL;
	m_wsValue = NULL;
	m_iDataType = TYPE_NONE;
	m_iKeyName = INVALID_KEY_SYMBOL;
	m_bEvaluateConditionals = 1;
	m_bHasEscapeSequences = 0;
	memset( unused, 0, sizeof( unused ) );
}

KeyValues::~KeyValues()
{
	RemoveEverything();
}

// Frees the children, then the peers after this key (a peer list that loops
// back here stops at it), then the value strings.
void KeyValues::RemoveEverything()
{
	auto freeList = []( KeyValues *pFirst, const KeyValues *pStop ) {
		while ( pFirst && pFirst != pStop )
		{
			KeyValues *pNext = pFirst->m_pPeer;
			pFirst->m_pPeer = NULL;
			delete pFirst;
			pFirst = pNext;
		}
	};
	freeList( m_pSub, NULL );
	freeList( m_pPeer, this );

	ss::EngineHeapFree( m_sValue );
	ss::EngineHeapFree( m_wsValue );
	m_sValue = NULL;
	m_wsValue = NULL;
}

void KeyValues::deleteThis()
{
	delete this;
}

const char *KeyValues::GetName( void ) const
{
	return KVSystem()->GetStringForSymbol( m_iKeyName );
}

void KeyValues::SetName( const char *setName )
{
	m_iKeyName = KVSystem()->GetSymbolForString( setName, true );
}

void KeyValues::UsesEscapeSequences( bool state )
{
	m_bHasEscapeSequences = state;
}

void KeyValues::UsesConditionals( bool state )
{
	m_bEvaluateConditionals = state;
}

// A key by name, "a/b" walking down, NULL or "" for this one; made at the
// end of the list when bCreate (and this key becomes a block). A missing
// key is looked for in the chain, never made there.
KeyValues *KeyValues::FindKey( const char *keyName, bool bCreate )
{
	if ( !keyName || !keyName[0] )
		return this;

	char szBuf[256] = { 0 };
	const char *subStr = strchr( keyName, '/' );
	const char *searchStr = keyName;
	if ( subStr )
	{
		int size = (int)( subStr - keyName + 1 );
		if ( size > (int)sizeof( szBuf ) )
			size = (int)sizeof( szBuf );
		ss::StrCopy( szBuf, keyName, size );
		searchStr = szBuf;
	}

	HKeySymbol iSearchStr = KVSystem()->GetSymbolForString( searchStr, bCreate );
	if ( iSearchStr == INVALID_KEY_SYMBOL )
		return NULL;

	KeyValues *lastItem = NULL;
	KeyValues *dat;
	for ( dat = m_pSub; dat != NULL; dat = dat->m_pPeer )
	{
		lastItem = dat;
		if ( dat->m_iKeyName == iSearchStr )
			break;
	}

	if ( !dat && m_pChain )
		dat = m_pChain->FindKey( keyName, false );

	if ( !dat )
	{
		if ( !bCreate )
			return NULL;

		dat = new KeyValues( searchStr );
		dat->UsesEscapeSequences( m_bHasEscapeSequences != 0 );
		dat->UsesConditionals( m_bEvaluateConditionals != 0 );
		if ( lastItem )
			lastItem->m_pPeer = dat;
		else
			m_pSub = dat;
		dat->m_pPeer = NULL;
		m_iDataType = TYPE_NONE;
	}

	if ( subStr )
		return dat->FindKey( subStr + 1, bCreate );
	return dat;
}

// Appended after the last child.
void KeyValues::AddSubKey( KeyValues *pSubkey )
{
	KeyValues **ppLink = &m_pSub;
	while ( *ppLink )
		ppLink = &( *ppLink )->m_pPeer;
	*ppLink = pSubkey;
}

void KeyValues::SetNextKey( KeyValues *pDat )
{
	m_pPeer = pDat;
}

KeyValues::types_t KeyValues::GetDataType( const char *keyName )
{
	KeyValues *dat = FindKey( keyName, false );
	return dat ? (types_t)dat->m_iDataType : TYPE_NONE;
}

int KeyValues::GetInt( const char *keyName, int defaultValue )
{
	KeyValues *dat = FindKey( keyName, false );
	if ( !dat )
		return defaultValue;
	switch ( dat->m_iDataType )
	{
	case TYPE_STRING: return atoi( dat->m_sValue );
	case TYPE_WSTRING: return (int)wcstol( dat->m_wsValue, NULL, 10 );
	case TYPE_FLOAT: return (int)dat->m_flValue;
	case TYPE_UINT64: return 0;	// tier1 asserts: it would lose data
	case TYPE_INT:
	case TYPE_PTR:
	default: return dat->m_iValue;
	}
}

float KeyValues::GetFloat( const char *keyName, float defaultValue )
{
	KeyValues *dat = FindKey( keyName, false );
	if ( !dat )
		return defaultValue;
	switch ( dat->m_iDataType )
	{
	case TYPE_STRING: return (float)atof( dat->m_sValue );
	case TYPE_WSTRING: return 0.0f;	// tier1 has no wide atof off Windows
	case TYPE_FLOAT: return dat->m_flValue;
	case TYPE_INT: return (float)dat->m_iValue;
	case TYPE_UINT64: return (float)( *( (uint64 *)dat->m_sValue ) );
	case TYPE_PTR:
	default: return 0.0f;
	}
}

// The value as a string. A number or pointer is CONVERTED: the key becomes
// that string, as tier1 makes it.
const char *KeyValues::GetString( const char *keyName, const char *defaultValue )
{
	KeyValues *dat = FindKey( keyName, false );
	if ( !dat )
		return defaultValue;

	char buf[64];
	switch ( dat->m_iDataType )
	{
	case TYPE_FLOAT:
		snprintf( buf, sizeof( buf ), "%f", dat->m_flValue );
		SetString( keyName, buf );
		break;
	case TYPE_PTR:
		snprintf( buf, sizeof( buf ), "%lld", (long long)CastPtrToInt64( dat->m_pValue ) );
		SetString( keyName, buf );
		break;
	case TYPE_INT:
		snprintf( buf, sizeof( buf ), "%d", dat->m_iValue );
		SetString( keyName, buf );
		break;
	case TYPE_UINT64:
		snprintf( buf, sizeof( buf ), "%lld", *( (long long *)( dat->m_sValue ) ) );
		SetString( keyName, buf );
		break;
	case TYPE_WSTRING:
	{
		char wideBuf[512];
		if ( !ss::WideToUTF8( dat->m_wsValue, wideBuf, sizeof( wideBuf ) ) )
			return defaultValue;
		SetString( keyName, wideBuf );
		break;
	}
	case TYPE_STRING:
		break;
	default:
		return defaultValue;
	}
	return dat->m_sValue;
}

void KeyValues::SetStringValue( char const *strValue )
{
	char *pNew = CopyValue( strValue );
	ss::EngineHeapFree( m_sValue );
	ss::EngineHeapFree( m_wsValue );
	m_wsValue = NULL;
	m_sValue = pNew;
	m_iDataType = TYPE_STRING;
}

void KeyValues::SetString( const char *keyName, const char *value )
{
	KeyValues *dat = FindKey( keyName, true );
	if ( !dat )
		return;
	if ( dat->m_iDataType == TYPE_STRING && dat->m_sValue == value )
		return;
	dat->SetStringValue( value );
}

void KeyValues::SetInt( const char *keyName, int value )
{
	if ( KeyValues *dat = FindKey( keyName, true ) )
	{
		dat->m_iValue = value;
		dat->m_iDataType = TYPE_INT;
	}
}

void KeyValues::SetUint64( const char *keyName, uint64 value )
{
	if ( KeyValues *dat = FindKey( keyName, true ) )
	{
		ss::EngineHeapFree( dat->m_sValue );
		ss::EngineHeapFree( dat->m_wsValue );
		dat->m_wsValue = NULL;
		dat->m_sValue = NewValue( sizeof( uint64 ) );
		memcpy( dat->m_sValue, &value, sizeof( value ) );
		dat->m_iDataType = TYPE_UINT64;
	}
}

void KeyValues::SetFloat( const char *keyName, float value )
{
	if ( KeyValues *dat = FindKey( keyName, true ) )
	{
		dat->m_flValue = value;
		dat->m_iDataType = TYPE_FLOAT;
	}
}

void KeyValues::SetPtr( const char *keyName, void *value )
{
	if ( KeyValues *dat = FindKey( keyName, true ) )
	{
		dat->m_pValue = value;
		dat->m_iDataType = TYPE_PTR;
	}
}
