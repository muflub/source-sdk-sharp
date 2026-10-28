//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
// $NoKeywords: $
//
//=============================================================================//

#ifndef DATATABLE_SEND_H
#define DATATABLE_SEND_H

#ifdef _WIN32
#pragma once
#endif

#include "dt_common.h"
#include "tier0/dbg.h"
#include "const.h"
#include "bitvec.h"


// ------------------------------------------------------------------------ //
// Send proxies can be used to convert a variable into a networkable type 
// (a good example is converting an edict pointer into an integer index).

// These allow you to translate data. For example, if you had a user-entered 
// string number like "10" (call the variable pUserStr) and wanted to encode 
// it as an integer, you would use a SendPropInt32 and write a proxy that said:
// pOut->m_Int = atoi(pUserStr);

// pProp       : the SendProp that has the proxy
// pStructBase : the base structure (like CBaseEntity*).
// pData       : the address of the variable to proxy.
// pOut        : where to output the proxied value.
// iElement    : the element index if this data is part of an array (or 0 if not).
// objectID    : entity index for debugging purposes.

// Return false if you don't want the engine to register and send a delta to
// the clients for this property (regardless of whether it actually changed or not).
// ------------------------------------------------------------------------ //
typedef void (*SendVarProxyFn)( const SendProp *pProp, const void *pStructBase, const void *pData, DVariant *pOut, int iElement, int objectID );

// Return the pointer to the data for the datatable.
// If the proxy returns null, it's the same as if pRecipients->ClearAllRecipients() was called.
class CSendProxyRecipients;

typedef void* (*SendTableProxyFn)( 
	const SendProp *pProp, 
	const void *pStructBase, 
	const void *pData, 
	CSendProxyRecipients *pRecipients, 
	int objectID );


class CNonModifiedPointerProxy
{
public:
	CNonModifiedPointerProxy( SendTableProxyFn fn );

public:
	
	SendTableProxyFn m_Fn;
	CNonModifiedPointerProxy *m_pNext;
};


// This tells the engine that the send proxy will not modify the pointer
// - it only plays with the recipients. This must be set on proxies that work
// this way, otherwise the engine can't track which properties changed
// in NetworkStateChanged().



class CStandardSendProxiesV1
{
public:
	CStandardSendProxiesV1();

	SendVarProxyFn m_Int8ToInt32;
	SendVarProxyFn m_Int16ToInt32;
	SendVarProxyFn m_Int32ToInt32;

	SendVarProxyFn m_UInt8ToInt32;
	SendVarProxyFn m_UInt16ToInt32;
	SendVarProxyFn m_UInt32ToInt32;

	SendVarProxyFn m_FloatToFloat;
	SendVarProxyFn m_VectorToVector;

#ifdef SUPPORTS_INT64
	SendVarProxyFn m_Int64ToInt64;
	SendVarProxyFn m_UInt64ToInt64;
#endif
};
	
class CStandardSendProxies : public CStandardSendProxiesV1
{
public:
	CStandardSendProxies();
	
	SendTableProxyFn m_DataTableToDataTable;
	SendTableProxyFn m_SendLocalDataTable;
	CNonModifiedPointerProxy **m_ppNonModifiedPointerProxies;
};

extern CStandardSendProxies g_StandardSendProxies;


// Max # of datatable send proxies you can have in a tree.


// ------------------------------------------------------------------------ //
// Datatable send proxies are used to tell the engine where the datatable's 
// data is and to specify which clients should get the data. 
//
// pRecipients is the object that allows you to specify which clients will
// receive the data.
// ------------------------------------------------------------------------ //
class CSendProxyRecipients
{
public:


	// Clear all recipients and set only the specified one.
	void	SetOnly( int iClient );

public:
	// Make sure we have enough room for the max possible player count
	CBitVec< ABSOLUTE_PLAYER_LIMIT >	m_Bits;
};





inline void CSendProxyRecipients::SetOnly( int iClient )
{
	m_Bits.ClearAll();
	m_Bits.Set( iClient );
}



// ------------------------------------------------------------------------ //
// ArrayLengthSendProxies are used when you want to specify an array's length
// dynamically.
// ------------------------------------------------------------------------ //
typedef int (*ArrayLengthSendProxyFn)( const void *pStruct, int objectID );



class RecvProp;
class SendTable;
class CSendTablePrecalc;


// -------------------------------------------------------------------------------------------------------------- //
// SendProp.
// -------------------------------------------------------------------------------------------------------------- //

// If SendProp::GetDataTableProxyIndex() returns this, then the proxy is one that always sends
// the data to all clients, so we don't need to store the results.



class SendProp
{
public:
						SendProp();
	virtual				~SendProp();

	void				Clear();

	int					GetOffset() const;
	void				SetOffset( int i );

	SendVarProxyFn		GetProxyFn() const;
	void				SetProxyFn( SendVarProxyFn f );
	
	SendTableProxyFn	GetDataTableProxyFn() const;
	void				SetDataTableProxyFn( SendTableProxyFn f );
	
	SendTable*			GetDataTable() const;
	void				SetDataTable( SendTable *pTable );

	

	const char*			GetName() const;

	
	

	SendProp*			GetArrayProp() const;

	// Arrays only.
	void					SetArrayLengthProxy( ArrayLengthSendProxyFn fn );
	ArrayLengthSendProxyFn	GetArrayLengthProxy() const;




	SendPropType		GetType() const;

	int					GetFlags() const;
	void				SetFlags( int flags );	


public:

	RecvProp		*m_pMatchingRecvProp;	// This is temporary and only used while precalculating
												// data for the decoders.

	SendPropType	m_Type;
	int				m_nBits;
	float			m_fLowValue;
	float			m_fHighValue;
	
	SendProp		*m_pArrayProp;					// If this is an array, this is the property that defines each array element.
	ArrayLengthSendProxyFn	m_ArrayLengthProxy;	// This callback returns the array length.
	
	int				m_nElements;		// Number of elements in the array (or 1 if it's not an array).
	int				m_ElementStride;	// Pointer distance between array elements.

	const char *m_pExcludeDTName;			// If this is an exclude prop, then this is the name of the datatable to exclude a prop from.
	const char *m_pParentArrayPropName;

	const char		*m_pVarName;
	float			m_fHighLowMul;
	
private:

	int					m_Flags;				// SPROP_ flags.

	SendVarProxyFn		m_ProxyFn;				// NULL for DPT_DataTable.
	SendTableProxyFn	m_DataTableProxyFn;		// Valid for DPT_DataTable.
	
	SendTable			*m_pDataTable;
	
	// SENDPROP_VECTORELEM makes this negative to start with so we can detect that and
	// set the SPROP_IS_VECTOR_ELEM flag.
	int					m_Offset;

	// Extra data bound to this property.
	const void			*m_pExtraData;
};


inline int SendProp::GetOffset() const
{
	return m_Offset; 
}

inline void SendProp::SetOffset( int i )
{
	m_Offset = i; 
}

inline SendVarProxyFn SendProp::GetProxyFn() const
{
	Assert( m_Type != DPT_DataTable );
	return m_ProxyFn; 
}

inline void SendProp::SetProxyFn( SendVarProxyFn f )
{
	m_ProxyFn = f; 
}

inline SendTableProxyFn SendProp::GetDataTableProxyFn() const
{
	Assert( m_Type == DPT_DataTable );
	return m_DataTableProxyFn; 
}

inline void SendProp::SetDataTableProxyFn( SendTableProxyFn f )
{
	m_DataTableProxyFn = f; 
}

inline SendTable* SendProp::GetDataTable() const
{
	return m_pDataTable;
}

inline void SendProp::SetDataTable( SendTable *pTable )
{
	m_pDataTable = pTable; 
}




inline const char* SendProp::GetName() const
{
	return m_pVarName; 
}







inline SendProp* SendProp::GetArrayProp() const
{
	return m_pArrayProp;
}
	 
inline void SendProp::SetArrayLengthProxy( ArrayLengthSendProxyFn fn )
{
	m_ArrayLengthProxy = fn;
}

inline ArrayLengthSendProxyFn SendProp::GetArrayLengthProxy() const
{
	return m_ArrayLengthProxy;
}
	 



inline SendPropType SendProp::GetType() const
{
	return m_Type; 
}

inline int SendProp::GetFlags() const
{
	return m_Flags;
}

inline void SendProp::SetFlags( int flags )
{
	// Make sure they're using something from the valid set of flags.
	Assert( !( flags & ~((1 << SPROP_NUMFLAGBITS) - 1) ) );
	m_Flags = flags;
}




// -------------------------------------------------------------------------------------------------------------- //
// SendTable.
// -------------------------------------------------------------------------------------------------------------- //

class SendTable
{
public:


				SendTable();
				SendTable( SendProp *pProps, int nProps, const char *pNetTableName );
				~SendTable();

	void		Construct( SendProp *pProps, int nProps, const char *pNetTableName );

	const char*	GetName() const;
	
	int			GetNumProps() const;
	SendProp*	GetProp( int i );




public:

	SendProp	*m_pProps;
	int			m_nProps;

	const char	*m_pNetTableName;	// The name matched between client and server.

	// The engine hooks the SendTable here.
	CSendTablePrecalc	*m_pPrecalc;


protected:		
	bool		m_bInitialized : 1;	
	bool		m_bHasBeenWritten : 1;		
	bool		m_bHasPropsEncodedAgainstCurrentTickCount : 1; // m_flSimulationTime and m_flAnimTime, e.g.
};


inline const char* SendTable::GetName() const
{
	return m_pNetTableName;
}


inline int SendTable::GetNumProps() const
{
	return m_nProps;
}


inline SendProp* SendTable::GetProp( int i )
{
	Assert( i >= 0 && i < m_nProps );
	return &m_pProps[i];
}











// ------------------------------------------------------------------------------------------------------ //
// Use BEGIN_SEND_TABLE if you want to declare a SendTable and have it inherit all the properties from
// its base class. There are two requirements for this to work:

// 1. Its base class must have a static SendTable pointer member variable called m_pClassSendTable which
//    points to its send table. The DECLARE_SERVERCLASS and IMPLEMENT_SERVERCLASS macros do this automatically.

// 2. Your class must typedef its base class as BaseClass. So it would look like this:
//    class Derived : public CBaseEntity
//    {
//    typedef CBaseEntity BaseClass;
//    };

// If you don't want to interit a base class's properties, use BEGIN_SEND_TABLE_NOBASE.
// ------------------------------------------------------------------------------------------------------ //
#define BEGIN_SEND_TABLE(className, tableName) \
	BEGIN_SEND_TABLE_NOBASE(className, tableName) \
		SendPropDataTable("baseclass", 0, className::BaseClass::m_pClassSendTable, SendProxy_DataTableToDataTable),

#define BEGIN_SEND_TABLE_NOBASE(className, tableName) \
	template <typename T> int ServerClassInit(T *); \
	namespace tableName { \
		struct ignored; \
	} \
	template <> int ServerClassInit<tableName::ignored>(tableName::ignored *); \
	namespace tableName { \
		SendTable g_SendTable;\
		int g_SendTableInit = ServerClassInit((tableName::ignored *)NULL); \
	} \
	template <> int ServerClassInit<tableName::ignored>(tableName::ignored *) \
	{ \
		typedef className currentSendDTClass; \
		static const char *g_pSendTableName = #tableName; \
		SendTable &sendTable = tableName::g_SendTable; \
		static SendProp g_SendProps[] = { \
			SendPropInt("should_never_see_this", 0, sizeof(int)),		// It adds a dummy property at the start so you can define "empty" SendTables.

#define END_SEND_TABLE() \
		};\
		sendTable.Construct(g_SendProps+1, sizeof(g_SendProps) / sizeof(SendProp) - 1, g_pSendTableName);\
		return 1; \
	} 

// Normal offset of is invalid on non-array-types, this is dubious as hell. The rest of the codebase converted to the
// legit offsetof from the C headers, so we'll use the old impl here to avoid exposing temptation to others
#define _hacky_dtsend_offsetof(s,m)	( (size_t)&(((s *)0x1000000)->m) - 0x1000000u )

// These can simplify creating the variables.
// Note: currentSendDTClass::MakeANetworkVar_##varName equates to currentSendDTClass. It's
// there as a check to make sure all networked variables use the CNetworkXXXX macros in network_var.h.
#define SENDINFO(varName)					#varName, _hacky_dtsend_offsetof(currentSendDTClass::MakeANetworkVar_##varName, varName), sizeof(((currentSendDTClass*)0)->varName)





// NOTE: Be VERY careful to specify any other vector elems for the same vector IN ORDER and 
// right after each other, otherwise it might miss the Y or Z component in SP.
//
// Note: this macro specifies a negative offset so the engine can detect it and setup m_pNext





// Use this when you're not using a CNetworkVar to represent the data you're sending.




#define SENDINFO_NAME(varName,remoteVarName)			#remoteVarName, _hacky_dtsend_offsetof(currentSendDTClass, varName), sizeof(((currentSendDTClass*)0)->varName)

// ------------------------------------------------------------------------ //
// Built-in proxy types.
// See the definition of SendVarProxyFn for information about these.
// ------------------------------------------------------------------------ //
void SendProxy_QAngles			( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
void SendProxy_AngleToFloat		( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
void SendProxy_FloatToFloat		( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
void SendProxy_VectorToVector	( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
#if 0 // We can't ship this since it changes the size of DTVariant to be 20 bytes instead of 16 and that breaks MODs!!!
void SendProxy_QuaternionToQuaternion( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
#endif

void SendProxy_Int8ToInt32		( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
void SendProxy_Int16ToInt32		( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
void SendProxy_Int32ToInt32		( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
#ifdef SUPPORTS_INT64
void SendProxy_Int64ToInt64		( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );
#endif
void SendProxy_StringToString	( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut, int iElement, int objectID );

// pData is the address of a data table.
void* SendProxy_DataTableToDataTable( const SendProp *pProp, const void *pStructBase, const void *pData, CSendProxyRecipients *pRecipients, int objectID );


// Used on player entities - only sends the data to the local player (objectID-1).
void* SendProxy_SendLocalDataTable( const SendProp *pProp, const void *pStruct, const void *pVarData, CSendProxyRecipients *pRecipients, int objectID );



SendProp SendPropVector(
	const char *pVarName,
	int offset,
	int sizeofVar=SIZEOF_IGNORE,
	int nBits=32,					// Number of bits (for each floating-point component) to use when encoding.
	int flags=SPROP_NOSCALE,
	float fLowValue=0.0f,			// For floating point, low and high values.
	float fHighValue=HIGH_DEFAULT,	// High value. If HIGH_DEFAULT, it's (1<<nBits).
	SendVarProxyFn varProxy=SendProxy_VectorToVector
	);


#if 0 // We can't ship this since it changes the size of DTVariant to be 20 bytes instead of 16 and that breaks MODs!!!
SendProp SendPropQuaternion(
	const char *pVarName,
	int offset,
	int sizeofVar=SIZEOF_IGNORE,
	int nBits=32,					// Number of bits (for each floating-point component) to use when encoding.
	int flags=SPROP_NOSCALE,
	float fLowValue=0.0f,			// For floating point, low and high values.
	float fHighValue=HIGH_DEFAULT,	// High value. If HIGH_DEFAULT, it's (1<<nBits).
	SendVarProxyFn varProxy=SendProxy_QuaternionToQuaternion
	);
#endif



SendProp SendPropInt(
	const char *pVarName,
	int offset,
	int sizeofVar=SIZEOF_IGNORE,	// Handled by SENDINFO macro.
	int nBits=-1,					// Set to -1 to automatically pick (max) number of bits based on size of element.
	int flags=0,
	SendVarProxyFn varProxy=0
	);


SendProp SendPropString(
	const char *pVarName,
	int offset,
	int bufferLen,
	int flags=0,
	SendVarProxyFn varProxy=SendProxy_StringToString);

// The data table encoder looks at DVariant::m_pData.
SendProp SendPropDataTable(
	const char *pVarName,
	int offset,
	SendTable *pTable, 
	SendTableProxyFn varProxy=SendProxy_DataTableToDataTable
	);




// Use the macro to let it automatically generate a table name. You shouldn't 
// ever need to reference the table name. If you want to exclude this array, then
// reference the name of the variable in varTemplate.
SendProp InternalSendPropArray(
	const int elementCount,
	const int elementStride,
	const char *pName,
	ArrayLengthSendProxyFn proxy
	);


// Use this and pass the array name and it will figure out the count and stride automatically.


//
// Use this when you want to send a variable-length array of data but there is no physical array you can point it at.
// You need to provide:
// 1. A proxy function that returns the current length of the array.
// 2. The maximum length the array will ever be.
// 2. A SendProp describing what the elements are comprised of.
// 3. In the SendProp, you'll want to specify a proxy function so you can go grab the data from wherever it is.
// 4. A property name that matches the definition on the client.
//



#define SendPropVariableLengthArray( arrayLengthSendProxy, varTemplate, arrayName )	\
	varTemplate,										\
	InternalSendPropArray(								\
		sizeof(((currentSendDTClass*)0)->arrayName) / PROPSIZEOF(currentSendDTClass, arrayName[0]), \
		PROPSIZEOF(currentSendDTClass, arrayName[0]),	\
		#arrayName,										\
		arrayLengthSendProxy							\
		)

// Use this one to specify the element count and stride manually.



	



#endif // DATATABLE_SEND_H
