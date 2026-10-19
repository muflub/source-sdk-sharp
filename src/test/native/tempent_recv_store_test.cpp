//========= SourceSharp — the stored client temp entity plan ==================//
//
// shell_tempents_store_client.cpp: the block one managed plan crosses in, read
// with the client's row size and refused when malformed, and one row stored into
// one RecvProp with nothing decided -- every field the row carries is the field
// the RecvProp holds, and the proxy is the real standard recv proxy the engine
// compares pointers with. Linked with the recv builders (engineabi_recv*.cpp)
// and nothing else.
//
//=============================================================================//

#include <string.h>

#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "shell_tempents_store_client.h"

namespace
{

SourceSharpRecvPropRow_t Row( int nType, int nProxy, int nOffset = 0, int nFlags = 0 )
{
	SourceSharpRecvPropRow_t row;
	memset( &row, 0, sizeof( row ) );
	row.nType          = nType;
	row.nProxy         = nProxy;
	row.nOffset        = nOffset;
	row.nFlags         = nFlags;
	row.nElements      = 1;
	row.nElementStride = -1;
	row.hDataTable     = -1;
	return row;
}

// A block as TempEntRecvBlock.Write lays it out: the bit count, the header, the
// rows, then the names.
std::vector< unsigned char > Block( const char *pClass, const char *pTable,
                                    std::vector< SourceSharpRecvPropRow_t > rows,
                                    const std::vector< std::string > &names )
{
	const int nFixed = (int)sizeof( SourceSharpTeTableHeader_t )
	                 + (int)( rows.size() * sizeof( SourceSharpRecvPropRow_t ) );
	std::vector< unsigned char > pool;

	auto Name = [ & ]( const std::string &text )
	{
		const int at = nFixed + (int)pool.size();
		pool.insert( pool.end(), text.begin(), text.end() );
		pool.push_back( 0 );
		return at;
	};

	SourceSharpTeTableHeader_t header;
	memset( &header, 0, sizeof( header ) );
	header.nProps         = (int32_t)rows.size();
	header.bRegisterClass = 1;
	header.oClassName     = Name( pClass );
	header.oTableName     = Name( pTable );
	header.nShapeHash     = 0x5678u;

	for ( size_t i = 0; i < rows.size(); i++ )
		rows[ i ].oName = Name( names[ i ] );

	const int32_t nBits = ( nFixed + (int)pool.size() ) * 8;
	std::vector< unsigned char > block( sizeof( nBits ) + nFixed + pool.size() );
	memcpy( block.data(), &nBits, sizeof( nBits ) );
	memcpy( block.data() + sizeof( nBits ), &header, sizeof( header ) );
	if ( !rows.empty() )
		memcpy( block.data() + sizeof( nBits ) + sizeof( header ), rows.data(), rows.size() * sizeof( rows[0] ) );
	memcpy( block.data() + sizeof( nBits ) + nFixed, pool.data(), pool.size() );
	return block;
}

TEST( TempEntRecvStore, ABlockReadsBackItsHeaderRowsAndNames )
{
	const std::vector< unsigned char > block =
		Block( "CTEEnergySplash", "DT_TEEnergySplash",
		       { Row( DPT_Vector, SOURCESHARP_RECVPROXY_VECTOR_TO_VECTOR, 0, SPROP_COORD ),
		         Row( DPT_Int, SOURCESHARP_RECVPROXY_INT32_TO_INT8, 24, SPROP_UNSIGNED ) },
		       { "Position", "Explosive" } );

	SourceSharpTeTableBlock_t read;
	ASSERT_TRUE( SourceSharpTe_ReadRecvTableBlock( block.data(), (int32_t)block.size(), &read ) );

	EXPECT_STREQ( "CTEEnergySplash", read.pClassName );
	EXPECT_STREQ( "DT_TEEnergySplash", read.pTableName );
	EXPECT_EQ( 2, read.header.nProps );
	EXPECT_EQ( 0x5678u, read.header.nShapeHash );

	const SourceSharpRecvPropRow_t row = SourceSharpTe_RecvBlockRow( read, 1 );
	EXPECT_EQ( 24, row.nOffset );
	EXPECT_EQ( SPROP_UNSIGNED, row.nFlags );
	EXPECT_EQ( SOURCESHARP_RECVPROXY_INT32_TO_INT8, row.nProxy );
	EXPECT_STREQ( "Explosive", SourceSharpTe_BlockName( read, row.oName ) );
}

TEST( TempEntRecvStore, AMalformedBlockIsRefused )
{
	const std::vector< unsigned char > good =
		Block( "CTEX", "DT_TEX", { Row( DPT_Int, SOURCESHARP_RECVPROXY_INT32_TO_INT32 ) }, { "Count" } );
	SourceSharpTeTableBlock_t read;

	// The control: the unmangled block reads.
	ASSERT_TRUE( SourceSharpTe_ReadRecvTableBlock( good.data(), (int32_t)good.size(), &read ) );

	EXPECT_FALSE( SourceSharpTe_ReadRecvTableBlock( NULL, 64, &read ) );
	EXPECT_FALSE( SourceSharpTe_ReadRecvTableBlock( good.data(), 3, &read ) ) << "shorter than its prefix";
	EXPECT_FALSE( SourceSharpTe_ReadRecvTableBlock( good.data(), (int32_t)good.size() - 1, &read ) )
		<< "a bit count longer than the bytes given";

	std::vector< unsigned char > tooMany = good;
	SourceSharpTeTableHeader_t header;
	memcpy( &header, tooMany.data() + 4, sizeof( header ) );
	header.nProps = 9;
	memcpy( tooMany.data() + 4, &header, sizeof( header ) );
	EXPECT_FALSE( SourceSharpTe_ReadRecvTableBlock( tooMany.data(), (int32_t)tooMany.size(), &read ) )
		<< "more rows than the payload holds at the client's row size";

	EXPECT_FALSE( SourceSharpTe_ReadTableBlock( good.data(), (int32_t)good.size(), 0, &read ) )
		<< "no row size";
}

TEST( TempEntRecvStore, AScalarRowIsStoredFieldForField )
{
	const SourceSharpRecvPropRow_t row = Row( DPT_Vector, SOURCESHARP_RECVPROXY_VECTOR_TO_VECTOR, 12, SPROP_COORD );
	RecvProp prop;

	ASSERT_TRUE( SourceSharpTe_StoreRecvProp( row, "Direction", NULL, &prop ) );

	EXPECT_STREQ( "Direction", prop.GetName() );
	EXPECT_EQ( DPT_Vector, prop.GetType() );
	EXPECT_EQ( 12, prop.GetOffset() );
	EXPECT_EQ( SPROP_COORD, prop.GetFlags() );
	EXPECT_EQ( 1, prop.GetNumElements() );
	EXPECT_EQ( -1, prop.GetElementStride() );
	EXPECT_EQ( (RecvVarProxyFn)RecvProxy_VectorToVector, prop.GetProxyFn() );
	EXPECT_EQ( NULL, prop.GetDataTable() );
}

TEST( TempEntRecvStore, EachIntProxyIdIsTheStandardNarrowingWrite )
{
	EXPECT_EQ( (RecvVarProxyFn)RecvProxy_Int32ToInt8,  SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_INT32_TO_INT8 ) );
	EXPECT_EQ( (RecvVarProxyFn)RecvProxy_Int32ToInt16, SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_INT32_TO_INT16 ) );
	EXPECT_EQ( (RecvVarProxyFn)RecvProxy_Int32ToInt32, SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_INT32_TO_INT32 ) );
	EXPECT_EQ( (RecvVarProxyFn)RecvProxy_FloatToFloat, SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_FLOAT_TO_FLOAT ) );
	EXPECT_EQ( NULL, SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_STATIC_DATATABLE ) );
	EXPECT_EQ( NULL, SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_NONE ) );
	EXPECT_EQ( NULL, SourceSharpTe_RecvVarProxy( SOURCESHARP_RECVPROXY_COUNT ) );
}

TEST( TempEntRecvStore, ADataTableRowNestsTheTableItIsHandedAndNeedsOne )
{
	RecvTable nested( NULL, 0, "DT_TEMetalSparks" );
	const SourceSharpRecvPropRow_t row = Row( DPT_DataTable, SOURCESHARP_RECVPROXY_STATIC_DATATABLE );
	RecvProp prop;

	EXPECT_FALSE( SourceSharpTe_StoreRecvProp( row, "baseclass", NULL, &prop ) ) << "no table under it";
	ASSERT_TRUE( SourceSharpTe_StoreRecvProp( row, "baseclass", &nested, &prop ) );

	EXPECT_EQ( DPT_DataTable, prop.GetType() );
	EXPECT_EQ( &nested, prop.GetDataTable() );
	EXPECT_EQ( (DataTableRecvVarProxyFn)DataTableRecvProxy_StaticDataTable, prop.GetDataTableProxyFn() );
	EXPECT_EQ( 0, prop.GetOffset() );
}

TEST( TempEntRecvStore, AnArrayRowIsInternalRecvPropArraysShape )
{
	SourceSharpRecvPropRow_t row = Row( DPT_Array, SOURCESHARP_RECVPROXY_NONE );
	row.nElements      = 16;
	row.nElementStride = 12;
	RecvProp prop;

	ASSERT_TRUE( SourceSharpTe_StoreRecvProp( row, "Spline", NULL, &prop ) );

	const RecvProp stock = InternalRecvPropArray( 16, 12, "Spline", NULL );

	EXPECT_EQ( stock.GetType(), prop.GetType() );
	EXPECT_EQ( stock.GetNumElements(), prop.GetNumElements() );
	EXPECT_EQ( stock.GetElementStride(), prop.GetElementStride() );
	EXPECT_EQ( stock.GetOffset(), prop.GetOffset() );
	EXPECT_EQ( stock.GetFlags(), prop.GetFlags() );
	EXPECT_EQ( stock.GetArrayLengthProxy(), prop.GetArrayLengthProxy() );
	EXPECT_EQ( stock.GetProxyFn(), prop.GetProxyFn() );
	EXPECT_STREQ( "Spline", prop.GetName() );
}

TEST( TempEntRecvStore, ARowWhoseProxyDoesNotFitItsTypeIsRefusedAndLeavesThePropAlone )
{
	RecvProp prop;
	prop.m_pVarName = "untouched";

	EXPECT_FALSE( SourceSharpTe_StoreRecvProp( Row( DPT_Int, SOURCESHARP_RECVPROXY_STATIC_DATATABLE ), "a", NULL, &prop ) );
	EXPECT_FALSE( SourceSharpTe_StoreRecvProp( Row( DPT_Array, SOURCESHARP_RECVPROXY_INT32_TO_INT32 ), "b", NULL, &prop ) );
	EXPECT_FALSE( SourceSharpTe_StoreRecvProp( Row( DPT_Float, SOURCESHARP_RECVPROXY_NONE ), "c", NULL, &prop ) );
	EXPECT_FALSE( SourceSharpTe_StoreRecvProp( Row( DPT_Float, SOURCESHARP_RECVPROXY_FLOAT_TO_FLOAT ), NULL, NULL, &prop ) );
	EXPECT_STREQ( "untouched", prop.m_pVarName );

	// The control: the same prop with a fitting proxy is stored.
	EXPECT_TRUE( SourceSharpTe_StoreRecvProp( Row( DPT_Float, SOURCESHARP_RECVPROXY_FLOAT_TO_FLOAT ), "d", NULL, &prop ) );
	EXPECT_STREQ( "d", prop.m_pVarName );
}

} // namespace
