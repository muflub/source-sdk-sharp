//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

// Debug builds only. Prints the temp entity tables as the engine holds them,
// after InitTable, including every field of every prop (TEW| lines) for diffing.

#include "tier1/convar.h"

#include "server_class.h"
#include "shell_tempents_internal.h"
#include "native_command_body.h"
#include "ss_string.h"

static const char *PropTypeName( int nType )
{
	switch ( nType )
	{
		case DPT_Int:
			return "int";
		case DPT_Float:
			return "float";
		case DPT_Vector:
			return "vector";
		case DPT_VectorXY:
			return "vectorxy";
		case DPT_String:
			return "string";
		case DPT_Array:
			return "array";
		case DPT_DataTable:
			return "datatable";
	}

	return "?";
}

static int CountTablesNamed( const char *pName )
{
	int nFound = 0;

	for ( ServerClass *pClass = g_pServerClassHead; pClass; pClass = pClass->m_pNext )
	{
		// Only the class's own table: nested tables are shared between classes.
		if ( pClass->m_pTable && pClass->m_pTable->GetName() && !strcmp( pClass->m_pTable->GetName(), pName ) )
			nFound++;
	}

	return nFound;
}

// dt_send.cpp's unsigned proxies, which dt_send.h does not declare.
void SendProxy_UInt8ToInt32( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut,
							 int iElement, int objectID );
void SendProxy_UInt16ToInt32( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut,
							  int iElement, int objectID );
void SendProxy_UInt32ToInt32( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut,
							  int iElement, int objectID );

// The engine's encoder compares proxy pointers with g_StandardSendProxies.
static const char *VarProxyName( SendVarProxyFn fn )
{
	if ( !fn )
		return "null";
	if ( fn == SendProxy_Int8ToInt32 )
		return "Int8ToInt32";
	if ( fn == SendProxy_Int16ToInt32 )
		return "Int16ToInt32";
	if ( fn == SendProxy_Int32ToInt32 )
		return "Int32ToInt32";
	if ( fn == SendProxy_UInt8ToInt32 )
		return "UInt8ToInt32";
	if ( fn == SendProxy_UInt16ToInt32 )
		return "UInt16ToInt32";
	if ( fn == SendProxy_UInt32ToInt32 )
		return "UInt32ToInt32";
	if ( fn == SendProxy_FloatToFloat )
		return "FloatToFloat";
	if ( fn == SendProxy_VectorToVector )
		return "VectorToVector";
	if ( fn == SendProxy_AngleToFloat )
		return "AngleToFloat";
	if ( fn == SendProxy_QAngles )
		return "QAngles";
	if ( fn == SendProxy_StringToString )
		return "StringToString";
	return "other";
}

// Floats are printed as bits so a diff is exact.
static unsigned int TeWireFloatBits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

static void DumpBuiltWire()
{
	for ( int i = 0; i < SourceSharpTe_BuiltCount(); i++ )
	{
		SendTable *pTable = SourceSharpTe_BuiltTable( i );

		if ( !pTable )
			continue;

		ServerClass *pClass = SourceSharpTe_BuiltClass( i );

		Msg( "TEWT|%d|%s|%s|%d\n", i, pTable->GetName(), pClass ? pClass->GetName() : "", pTable->GetNumProps() );

		for ( int iProp = 0; iProp < pTable->GetNumProps(); iProp++ )
		{
			const SendProp *pProp = pTable->GetProp( iProp );
			const SendTable *pNested = pProp->GetDataTable();

			Msg( "TEW|%s|%d|%s|%s|off %d|flags 0x%x|bits %d|low %08x|high %08x|mul %08x"
				 "|elems %d|stride %d|proxy %s|dtproxy %s|dt %s|arrayprop %s|lenproxy %s\n",
				 pTable->GetName(), iProp, pProp->GetName() ? pProp->GetName() : "", PropTypeName( pProp->m_Type ),
				 pProp->GetOffset(), pProp->GetFlags(), pProp->m_nBits, TeWireFloatBits( pProp->m_fLowValue ),
				 TeWireFloatBits( pProp->m_fHighValue ), TeWireFloatBits( pProp->m_fHighLowMul ), pProp->m_nElements,
				 pProp->m_ElementStride, pProp->m_Type == DPT_DataTable ? "-" : VarProxyName( pProp->GetProxyFn() ),
				 // Only a datatable prop has one: the constructor leaves the
				 // member unset on every other kind, and the accessor asserts.
				 pProp->m_Type != DPT_DataTable ? "-"
					 : pProp->GetDataTableProxyFn() == NULL
					 ? "null"
					 : ( pProp->GetDataTableProxyFn() == SendProxy_DataTableToDataTable ? "DataTableToDataTable"
																						: "other" ),
				 ( pNested && pNested->GetName() ) ? pNested->GetName() : "",
				 pProp->GetArrayProp() && pProp->GetArrayProp()->GetName() ? pProp->GetArrayProp()->GetName() : "",
				 pProp->GetArrayLengthProxy() ? "set" : "null" );
		}
	}
}

SS_NATIVE_COMMAND( cs_sv_te_dump )
{
	const char *pWanted = ( args.ArgC() > 1 ) ? args[1] : NULL;
	const int nBuilt = SourceSharpTe_BuiltCount();

	Msg( "[SourceSharp] --- temp entities: %d table(s), registration %s ---\n", nBuilt,
		 SourceSharpTe_RegistrationClosed() ? "closed" : "OPEN" );

	for ( int i = 0; i < nBuilt; i++ )
	{
		SendTable *pTable = SourceSharpTe_BuiltTable( i );
		ServerClass *pClass = SourceSharpTe_BuiltClass( i );

		if ( !pTable )
			continue;

		if ( pWanted && ss::StrICmp( pTable->GetName(), pWanted ) &&
			 ( !pClass || ss::StrICmp( pClass->GetName(), pWanted ) ) )
			continue;

		Msg( "%2d %-28s %-28s %s, %d prop(s)\n", i, pClass ? pClass->GetName() : "(table only)", pTable->GetName(),
			 pClass ? "ServerClass" : "table only", pTable->GetNumProps() );

		const int nSameName = CountTablesNamed( pTable->GetName() );

		if ( nSameName > 1 )
		{
			Msg( "     NOTE: %d ServerClass(es) publish a table named '%s'\n", nSameName, pTable->GetName() );
		}

		for ( int iProp = 0; iProp < pTable->GetNumProps(); iProp++ )
		{
			SendProp *pProp = pTable->GetProp( iProp );
			const int nFlags = pProp->GetFlags();

			Msg( "     %-24s %-9s off %5d bits %2d flags 0x%05x%s\n", pProp->GetName(), PropTypeName( pProp->m_Type ),
				 pProp->GetOffset(), pProp->m_nBits, nFlags,
				 ( nFlags & SPROP_IS_A_VECTOR_ELEM )
					 ? "  [vector elem, engine flipped it]"
					 : ( pProp->GetOffset() < 0 ? "  [vector elem, STILL NEGATIVE: tables not built yet]" : "" ) );

			// A collapsible nested table (not the base) is applied at the object base, so
			// its props overwrite offset 0: the vtable pointer of a native shell.
			if ( pProp->m_Type == DPT_DataTable && ( nFlags & SPROP_COLLAPSIBLE ) &&
				 strcmp( pProp->GetName(), "baseclass" ) )
			{
				Warning( "     *** NESTED TABLE IS COLLAPSIBLE: the receiver will apply it "
						 "at the object base and write over offset 0 ***\n" );
			}

			Msg( "TE|%s|%d|%s|%s|%d|%d|0x%x|%s\n", pTable->GetName(), iProp, pProp->GetName(),
				 PropTypeName( pProp->m_Type ), pProp->GetOffset(), pProp->m_nBits, nFlags,
				 ( pProp->m_Type == DPT_DataTable && pProp->GetDataTable() && pProp->GetDataTable()->GetName() )
					 ? pProp->GetDataTable()->GetName()
					 : "" );
		}
	}

	if ( !pWanted )
		DumpBuiltWire();

	if ( !SourceSharpTe_RegistrationClosed() )
		Warning( "     registration is still OPEN, which it should not be after DLL init\n" );
}
