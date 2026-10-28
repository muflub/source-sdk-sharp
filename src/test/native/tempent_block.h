// A temp entity plan's block, laid out as managed's TempEntBlock.Write lays it out, for the
// tests of what reads and builds it.
#pragma once

#include <string.h>

#include <string>
#include <vector>

#include "shell_tempents_store.h"

namespace tempent_test
{

inline SourceSharpSendPropRow_t Row( int nType, int nProxy )
{
	SourceSharpSendPropRow_t row;
	memset( &row, 0, sizeof( row ) );
	row.nType = nType;
	row.nProxy = nProxy;
	row.nElements = 1;
	row.nElementStride = -1;
	row.hDataTable = -1;
	return row;
}

// A block as TempEntBlock.Write lays it out: the bit count, the header, the
// rows, then the names.
inline std::vector<unsigned char> Block( const char *pClass, const char *pTable,
										 std::vector<SourceSharpSendPropRow_t> rows,
										 const std::vector<std::string> &names )
{
	const int nFixed =
		(int)sizeof( SourceSharpTeTableHeader_t ) + (int)( rows.size() * sizeof( SourceSharpSendPropRow_t ) );
	std::vector<unsigned char> pool;

	auto Name = [&]( const std::string &text )
	{
		const int at = nFixed + (int)pool.size();
		pool.insert( pool.end(), text.begin(), text.end() );
		pool.push_back( 0 );
		return at;
	};

	SourceSharpTeTableHeader_t header;
	memset( &header, 0, sizeof( header ) );
	header.nProps = (int32_t)rows.size();
	header.bRegisterClass = 1;
	header.oClassName = Name( pClass );
	header.oTableName = Name( pTable );
	header.nShapeHash = 0x1234u;

	for ( size_t i = 0; i < rows.size(); i++ )
		rows[i].oName = Name( names[i] );

	const int32_t nBits = ( nFixed + (int)pool.size() ) * 8;
	std::vector<unsigned char> block( sizeof( nBits ) + nFixed + pool.size() );
	memcpy( block.data(), &nBits, sizeof( nBits ) );
	memcpy( block.data() + sizeof( nBits ), &header, sizeof( header ) );
	if ( !rows.empty() )
		memcpy( block.data() + sizeof( nBits ) + sizeof( header ), rows.data(), rows.size() * sizeof( rows[0] ) );
	memcpy( block.data() + sizeof( nBits ) + nFixed, pool.data(), pool.size() );
	return block;
}

} // namespace tempent_test
