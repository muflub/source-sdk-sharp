// ss::TePrediction is what server.so kept of IPredictionSystem: the temp entity
// channel's suppress host. Stock's GetSuppressHost answered NULL while
// filtering was disabled and the stored host otherwise; nothing on the server
// can disable filtering (no CDisablePredictionFiltering in server.so -- nm), so
// the stored host is the whole answer. These pin that answer.

#include "gtest/gtest.h"

#include "../game/server/ss_te_prediction.h"

namespace
{

// Stand-ins: the host is only ever stored and compared, never dereferenced.
IGameEntity *Host( int n )
{
	static char s_Hosts[ 4 ];
	return reinterpret_cast< IGameEntity * >( &s_Hosts[ n ] );
}

} // namespace

TEST( SsTePrediction, AFreshChannelHasNoSuppressHost )
{
	const ss::TePrediction te;
	EXPECT_EQ( nullptr, te.GetSuppressHost() );
}

TEST( SsTePrediction, TheHostSetIsTheHostAnswered )
{
	ss::TePrediction te;
	te.SetSuppressHost( Host( 1 ) );
	EXPECT_EQ( Host( 1 ), te.GetSuppressHost() );

	te.SetSuppressHost( Host( 2 ) );
	EXPECT_EQ( Host( 2 ), te.GetSuppressHost() );
}

// ApplyMultiDamage's use: read, clear across the damage, put back.
TEST( SsTePrediction, ClearingAndRestoringPutsTheSameHostBack )
{
	ss::TePrediction te;
	te.SetSuppressHost( Host( 3 ) );

	const IGameEntity *pSaved = te.GetSuppressHost();
	te.SetSuppressHost( nullptr );
	EXPECT_EQ( nullptr, te.GetSuppressHost() );

	te.SetSuppressHost( const_cast< IGameEntity * >( pSaved ) );
	EXPECT_EQ( Host( 3 ), te.GetSuppressHost() );
}
