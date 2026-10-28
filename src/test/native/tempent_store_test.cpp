//========= SourceSharp — the stored temp entity plan (plan_server_native_v2 T4b)
//
// shell_tempents_store.cpp: the block one managed plan crosses in, read and
// refused when malformed, and one row stored into one SendProp with nothing
// decided -- every field the row carries is the field the SendProp holds, and
// the proxy is the real dt_send.cpp function the engine compares pointers with.
// Linked with dt_send.cpp (ss_cpptest_dtobjs) and nothing else.
//
//=============================================================================//

#include <string.h>

#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "mathlib_test_env.h"

#include "shell_tempents_store.h"
#include "tempent_block.h"

void SendProxy_UInt8ToInt32( const SendProp *pProp, const void *pStruct, const void *pData, DVariant *pOut,
							 int iElement, int objectID );

namespace
{

using tempent_test::Block;
using tempent_test::Row;

TEST( TempEntStore, ABlockReadsBackItsHeaderRowsAndNames )
{
	SourceSharpSendPropRow_t magnitude = Row( DPT_Int, SOURCESHARP_SENDPROXY_UINT32_TO_INT32 );
	magnitude.nOffset = 12;
	magnitude.nBits = 4;

	const std::vector<unsigned char> block =
		Block( "CTESparks", "DT_TESparks", { Row( DPT_Vector, SOURCESHARP_SENDPROXY_VECTOR_TO_VECTOR ), magnitude },
			   { "Direction", "Magnitude" } );

	SourceSharpTeTableBlock_t read;
	ASSERT_TRUE( SourceSharpTe_ReadTableBlock( block.data(), (int32_t)block.size(), &read ) );

	EXPECT_STREQ( "CTESparks", read.pClassName );
	EXPECT_STREQ( "DT_TESparks", read.pTableName );
	EXPECT_EQ( 2, read.header.nProps );
	EXPECT_EQ( 1, read.header.bRegisterClass );
	EXPECT_EQ( 0x1234u, read.header.nShapeHash );

	const SourceSharpSendPropRow_t row = SourceSharpTe_BlockRow( read, 1 );
	EXPECT_EQ( 12, row.nOffset );
	EXPECT_EQ( 4, row.nBits );
	EXPECT_STREQ( "Magnitude", SourceSharpTe_BlockName( read, row.oName ) );
}

TEST( TempEntStore, AMalformedBlockIsRefused )
{
	const std::vector<unsigned char> good =
		Block( "CTESparks", "DT_TESparks", { Row( DPT_Int, SOURCESHARP_SENDPROXY_INT32_TO_INT32 ) }, { "Count" } );
	SourceSharpTeTableBlock_t read;

	// The control: the unmangled block reads.
	ASSERT_TRUE( SourceSharpTe_ReadTableBlock( good.data(), (int32_t)good.size(), &read ) );

	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( NULL, 64, &read ) );
	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( good.data(), 3, &read ) ) << "shorter than its prefix";
	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( good.data(), (int32_t)good.size() - 1, &read ) )
		<< "a bit count longer than the bytes given";

	std::vector<unsigned char> odd = good;
	int32_t nBits = 0;
	memcpy( &nBits, odd.data(), 4 );
	nBits -= 3;
	memcpy( odd.data(), &nBits, 4 );
	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( odd.data(), (int32_t)odd.size(), &read ) ) << "not whole bytes";

	std::vector<unsigned char> many = good;
	SourceSharpTeTableHeader_t header;
	memcpy( &header, many.data() + 4, sizeof( header ) );
	header.nProps = 2;
	memcpy( many.data() + 4, &header, sizeof( header ) );
	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( many.data(), (int32_t)many.size(), &read ) )
		<< "more rows than the payload holds";

	std::vector<unsigned char> lost = good;
	memcpy( &header, lost.data() + 4, sizeof( header ) );
	header.oTableName = 100000;
	memcpy( lost.data() + 4, &header, sizeof( header ) );
	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( lost.data(), (int32_t)lost.size(), &read ) )
		<< "a name outside the payload";

	// The last name is the row's: without its terminator the block still
	// reads, and the name is refused where the store asks for it.
	std::vector<unsigned char> open = good;
	open.back() = 'x';
	ASSERT_TRUE( SourceSharpTe_ReadTableBlock( open.data(), (int32_t)open.size(), &read ) );
	EXPECT_EQ( NULL, SourceSharpTe_BlockName( read, SourceSharpTe_BlockRow( read, 0 ).oName ) )
		<< "an unterminated name";
}

TEST( TempEntStore, ANameIsRefusedWhenEmptyOrUnterminated )
{
	const std::vector<unsigned char> block =
		Block( "CTEA", "DT_TEA", { Row( DPT_Int, SOURCESHARP_SENDPROXY_INT32_TO_INT32 ) }, { "x" } );
	SourceSharpTeTableBlock_t read;
	ASSERT_TRUE( SourceSharpTe_ReadTableBlock( block.data(), (int32_t)block.size(), &read ) );

	const int32_t oX = SourceSharpTe_BlockRow( read, 0 ).oName;
	ASSERT_STREQ( "x", SourceSharpTe_BlockName( read, oX ) );

	EXPECT_EQ( NULL, SourceSharpTe_BlockName( read, oX + 1 ) ) << "the terminator alone is an empty name";
	EXPECT_EQ( NULL, SourceSharpTe_BlockName( read, -1 ) );
	EXPECT_EQ( NULL, SourceSharpTe_BlockName( read, read.nPayload ) );

	SourceSharpTeTableBlock_t cut = read;
	cut.nPayload = oX + 1; // ends after 'x', before its terminator
	EXPECT_EQ( NULL, SourceSharpTe_BlockName( cut, oX ) );
}

TEST( TempEntStore, AFloatRowIsStoredFieldForField )
{
	SourceSharpSendPropRow_t row = Row( DPT_Float, SOURCESHARP_SENDPROXY_FLOAT_TO_FLOAT );
	row.nOffset = -8;
	row.nFlags = SPROP_COORD | SPROP_NOSCALE;
	row.nBits = 0;
	row.flLowValue = 1.5f;
	row.flHighValue = 9.25f;
	row.flHighLowMul = 2147483648.0f;

	SendProp prop;
	ASSERT_TRUE( SourceSharpTe_StoreProp( row, "m_vecOrigin[2]", NULL, &prop ) );

	EXPECT_EQ( DPT_Float, prop.m_Type );
	EXPECT_STREQ( "m_vecOrigin[2]", prop.GetName() );
	EXPECT_EQ( -8, prop.GetOffset() );
	EXPECT_EQ( SPROP_COORD | SPROP_NOSCALE, prop.GetFlags() );
	EXPECT_EQ( 0, prop.m_nBits );
	EXPECT_EQ( 1.5f, prop.m_fLowValue );
	EXPECT_EQ( 9.25f, prop.m_fHighValue );
	EXPECT_EQ( 2147483648.0f, prop.m_fHighLowMul );
	EXPECT_EQ( 1, prop.m_nElements );
	EXPECT_EQ( -1, prop.m_ElementStride );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_FloatToFloat, prop.GetProxyFn() );
}

TEST( TempEntStore, EveryVarProxyIdIsTheRealFunction )
{
	EXPECT_EQ( (SendVarProxyFn)SendProxy_Int8ToInt32, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_INT8_TO_INT32 ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_Int16ToInt32, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_INT16_TO_INT32 ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_Int32ToInt32, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_INT32_TO_INT32 ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_UInt8ToInt32, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_UINT8_TO_INT32 ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_VectorToVector,
			   SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_VECTOR_TO_VECTOR ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_AngleToFloat, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_ANGLE_TO_FLOAT ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_QAngles, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_QANGLES ) );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_StringToString,
			   SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_STRING_TO_STRING ) );

	EXPECT_EQ( NULL, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_NONE ) );
	EXPECT_EQ( NULL, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_DATATABLE_TO_DATATABLE ) );
	EXPECT_EQ( NULL, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_EMPTY ) );
	EXPECT_EQ( NULL, SourceSharpTe_VarProxy( SOURCESHARP_SENDPROXY_COUNT ) );
}

TEST( TempEntStore, AnUnsignedIntKeepsItsUnsignedProxy )
{
	SourceSharpSendPropRow_t row = Row( DPT_Int, SOURCESHARP_SENDPROXY_UINT8_TO_INT32 );
	row.nFlags = SPROP_UNSIGNED;
	row.nBits = 8;

	SendProp prop;
	ASSERT_TRUE( SourceSharpTe_StoreProp( row, "ColorR", NULL, &prop ) );

	EXPECT_EQ( DPT_Int, prop.m_Type );
	EXPECT_EQ( 8, prop.m_nBits );
	EXPECT_EQ( (SendVarProxyFn)SendProxy_UInt8ToInt32, prop.GetProxyFn() );
	EXPECT_EQ( 0.0f, prop.m_fHighLowMul ) << "an int's multiplier is the row's, never left unset";
}

TEST( TempEntStore, ANestedTableNeedsItsTableAndTheIdentityProxy )
{
	SendTable nested( NULL, 0, "DT_EffectData" );
	SourceSharpSendPropRow_t row = Row( DPT_DataTable, SOURCESHARP_SENDPROXY_DATATABLE_TO_DATATABLE );
	row.nOffset = 4;
	row.nFlags = SPROP_PROXY_ALWAYS_YES;

	SendProp prop;
	ASSERT_TRUE( SourceSharpTe_StoreProp( row, "m_EffectData", &nested, &prop ) );

	EXPECT_EQ( DPT_DataTable, prop.m_Type );
	EXPECT_EQ( &nested, prop.GetDataTable() );
	EXPECT_EQ( (SendTableProxyFn)SendProxy_DataTableToDataTable, prop.GetDataTableProxyFn() );
	EXPECT_EQ( SPROP_PROXY_ALWAYS_YES, prop.GetFlags() );

	SendProp untouched;
	untouched.SetOffset( 77 );
	EXPECT_FALSE( SourceSharpTe_StoreProp( row, "m_EffectData", NULL, &untouched ) ) << "no table to nest";
	EXPECT_EQ( 77, untouched.GetOffset() );

	SourceSharpSendPropRow_t wrong = row;
	wrong.nProxy = SOURCESHARP_SENDPROXY_INT32_TO_INT32;
	EXPECT_FALSE( SourceSharpTe_StoreProp( wrong, "m_EffectData", &nested, &untouched ) );
}

TEST( TempEntStore, AnArrayIsTheArrayConstructorsOwnProp )
{
	SourceSharpSendPropRow_t row = Row( DPT_Array, SOURCESHARP_SENDPROXY_EMPTY );
	row.nElements = 16;
	row.nElementStride = 12;

	SendProp prop;
	ASSERT_TRUE( SourceSharpTe_StoreProp( row, "m_vecPoints", NULL, &prop ) );

	EXPECT_EQ( DPT_Array, prop.m_Type );
	EXPECT_EQ( 16, prop.GetNumElements() );
	EXPECT_EQ( 12, prop.GetElementStride() );
	EXPECT_STREQ( "m_vecPoints", prop.GetName() );
	EXPECT_TRUE( prop.GetProxyFn() != NULL ) << "InternalSendPropArray's SendProxy_Empty";
	EXPECT_EQ( NULL, prop.GetArrayProp() ) << "the engine links it to the prop before it";
	EXPECT_EQ( NULL, prop.GetArrayLengthProxy() );

	SourceSharpSendPropRow_t wrong = row;
	wrong.nProxy = SOURCESHARP_SENDPROXY_VECTOR_TO_VECTOR;
	EXPECT_FALSE( SourceSharpTe_StoreProp( wrong, "m_vecPoints", NULL, &prop ) );
}

TEST( TempEntStore, AnUnknownProxyIsRefusedRatherThanStoredAsNull )
{
	SendProp prop;
	EXPECT_FALSE( SourceSharpTe_StoreProp( Row( DPT_Float, SOURCESHARP_SENDPROXY_NONE ), "f", NULL, &prop ) );
	EXPECT_FALSE( SourceSharpTe_StoreProp( Row( DPT_Float, 99 ), "f", NULL, &prop ) );
	EXPECT_FALSE( SourceSharpTe_StoreProp( Row( DPT_Float, SOURCESHARP_SENDPROXY_FLOAT_TO_FLOAT ), NULL, NULL, &prop ) )
		<< "no name";
	EXPECT_TRUE( SourceSharpTe_StoreProp( Row( DPT_Float, SOURCESHARP_SENDPROXY_FLOAT_TO_FLOAT ), "f", NULL, &prop ) )
		<< "the control";
}

} // namespace
