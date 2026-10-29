//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "datamodel/dmattributetypes.h"
#include "tier1/utlvector.h"
#include "tier1/utlrbtree.h"
#include "tier1/utlsymbol.h"
#include "tier1/mempool.h"
#include "tier1/UtlSortVector.h"
#include "dmxloader/dmxattribute.h"

// Sort functor class for attributes
class CDmxAttributeLess
{
public:
	bool Less( const CDmxAttribute *pAttribute1, const CDmxAttribute *pAttribute2, void *pContext )
	{
		return pAttribute1->GetNameSymbol() < pAttribute2->GetNameSymbol();
	}
};

// Used to unpack elements into a structure. Does not recurse
// Also does not work with arrays.
struct DmxElementUnpackStructure_t
{
	const char *m_pAttributeName;
	const char *m_pDefaultString;
	DmAttributeType_t m_AttributeType;
	int m_nOffset;
	int m_nSize;
	const void *m_pUserData; // If you want to associate some app-specific data with each field
};

#define DECLARE_DMXELEMENT_UNPACK() \
	template <typename T>           \
	friend DmxElementUnpackStructure_t *DmxElementUnpackInit( T * );

#define DMXELEMENT_UNPACK_FIELD( _attributeName, _defaultString, _type, _varName ) \
	{ _attributeName,                                                              \
	  _defaultString,                                                              \
	  CDmAttributeInfo<_type>::AttributeType(),                                    \
	  (int)offsetof( DestStructType_t, _varName ),                                 \
	  sizeof( ( (DestStructType_t *)0 )->_varName ),                               \
	  NULL },

// Element used to read dmx files from mod code. Similar to keyvalues.
class CDmxElement
{
	DECLARE_DMX_ALLOCATOR();

public:
	CDmxAttribute *GetAttribute( const char *pAttributeName );
	const CDmxAttribute *GetAttribute( const char *pAttributeName ) const;
	CDmxAttribute *GetAttribute( int nIndex );
	const CDmxAttribute *GetAttribute( int nIndex ) const;
	CUtlSymbol GetType() const;
	const char *GetName() const;
	const DmObjectId_t &GetId() const;

	// Add+remove+rename can only occur during lock
	// NOTE: AddAttribute will find or add; returning an existing attribute if
	// one with the appropriate name exists
	void LockForChanges( bool bLock );
	CDmxAttribute *AddAttribute( const char *pAttributeName );

	template <class T>
	const T &GetValue( const char *pAttributeName ) const;
	template <class T>
	const T &GetValue( const char *pAttributeName, const T &defaultValue ) const;

private:
	typedef CUtlSortVector<CDmxAttribute *, CDmxAttributeLess> AttributeList_t;

	CDmxElement( const char *pType );
	~CDmxElement();

	// Finds an attribute by name
	int FindAttribute( const char *pAttributeName ) const;
	int FindAttribute( CUtlSymbol attributeName ) const;

	alignas( 8 ) unsigned char unused_m_Attributes[66];
	char m_nLockCount;
	mutable bool m_bResortNeeded : 1;
	bool m_bIsMarkedForDeletion : 1;

	friend class CDmxSerializer;
	friend class CDmxSerializerKeyValues2;
};

// inline methods

template <class T>
inline const T &CDmxElement::GetValue( const char *pAttributeName ) const
{
	const CDmxAttribute *pAttribute = GetAttribute( pAttributeName );
	if ( pAttribute )
		return pAttribute->GetValue<T>();

	static T defaultValue;
	CDmAttributeInfo<T>::SetDefaultValue( defaultValue );
	return defaultValue;
}

template <class T>
inline const T &CDmxElement::GetValue( const char *pAttributeName, const T &defaultValue ) const
{
	const CDmxAttribute *pAttribute = GetAttribute( pAttributeName );
	if ( pAttribute )
		return pAttribute->GetValue<T>();
	return defaultValue;
}

// Helper class to lock elements for changes
class CDmxElementModifyScope
{
public:
	CDmxElementModifyScope( CDmxElement *pElement )
		: m_pElement( pElement )
	{
		m_pElement->LockForChanges( true );
	}

	~CDmxElementModifyScope() { Release(); }

	void Release()
	{
		if ( m_pElement )
		{
			m_pElement->LockForChanges( false );
			m_pElement = NULL;
		}
	}

private:
	CDmxElement *m_pElement;
};
