//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//=============================================================================

#ifndef DMXATTRIBUTE_H
#define DMXATTRIBUTE_H

#ifdef _WIN32
#pragma once
#endif

#include "datamodel/dmattributetypes.h"
#include "tier1/utlvector.h"
#include "tier1/utlrbtree.h"
#include "tier1/utlsymbol.h"
#include "tier1/mempool.h"
#include "dmxloader/dmxloader.h"


//-----------------------------------------------------------------------------
// Forward declarations: 
//-----------------------------------------------------------------------------
class CDmxElement;


//-----------------------------------------------------------------------------
// Attribute info, modified for use in mod code
//-----------------------------------------------------------------------------
DECLARE_ATTRIBUTE_TYPE( CDmxElement*,		AT_ELEMENT,				"element",		value = 0; )
DECLARE_ATTRIBUTE_ARRAY_TYPE( CDmxElement*,	AT_ELEMENT_ARRAY,		"element_array" )


//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
class CDmxAttribute
{
	DECLARE_DMX_ALLOCATOR( );

public:
	// Returns attribute name and type
	DmAttributeType_t GetType() const;

	// Returns the name. NOTE: The utlsymbol
	// can be turned into a string by using g_pDataModel->String();
	const char *GetName() const;
	CUtlSymbol GetNameSymbol() const;

	// Gets values
	template< class T >	const T& GetValue( ) const;
	const char *GetValueString() const;

	// Sets values (+ type)
	template< class T > void SetValue( const T& value );
	void SetValue( const char *pString );
	void SetValue( const void *pBuffer, size_t nLen );
	void SetValue( const CDmxAttribute *pAttribute );







private:
	CDmxAttribute( const char *pAttributeName );
	CDmxAttribute( CUtlSymbol attributeName );
	~CDmxAttribute();

	// Allocate, free memory for data
	void AllocateDataMemory( DmAttributeType_t type );

	// Untyped method for setting used by unpack
	void SetValue( DmAttributeType_t type, const void *pSrc, int nLen );

	DmAttributeType_t m_Type;
	CUtlSymbol m_Name;
	void *m_pData;


	friend class CDmxElement;
};


//-----------------------------------------------------------------------------
// Inline methods 
//-----------------------------------------------------------------------------
inline DmAttributeType_t CDmxAttribute::GetType() const
{
	return m_Type;
}


inline CUtlSymbol CDmxAttribute::GetNameSymbol() const
{
	return m_Name;
}


//-----------------------------------------------------------------------------
// Sets a value in the attribute
//-----------------------------------------------------------------------------
template< class T > void CDmxAttribute::SetValue( const T& value )
{
	AllocateDataMemory( CDmAttributeInfo<T>::AttributeType() );
	CopyConstruct( (T*)m_pData, value );
}


//-----------------------------------------------------------------------------
// Returns data in the attribute
//-----------------------------------------------------------------------------
inline const char *CDmxAttribute::GetValueString() const
{
	if ( m_Type == AT_STRING )
		return *(CUtlString*)m_pData;
	return "";
}

template< class T >
inline const T& CDmxAttribute::GetValue( ) const
{
	if ( CDmAttributeInfo<T>::AttributeType() == m_Type )
		return *(T*)m_pData;

	static T defaultValue;
	CDmAttributeInfo<T>::SetDefaultValue( defaultValue );
	return defaultValue;
}



#endif // DMXATTRIBUTE_H
