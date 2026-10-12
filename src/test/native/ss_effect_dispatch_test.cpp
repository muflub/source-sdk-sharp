// ss_effect_dispatch.h respells two numbers the client reads off the wire:
// shareddefs.h's FX_WATER_IN_SLIME (a CEffectData::m_fFlags bit) and
// particle_parse.h's PATTACH_ABSORIGIN (the control point's default
// attachment). A local copy of a number drifts, so each is read back out of
// its fork header's text. And ss::CEffectData's defaults are stock's: what a
// caller that sets nothing sends.

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <string>

#include "gtest/gtest.h"

#include "../game/server/ss_effect_dispatch.h"

namespace
{

std::string ReadFile( const char *pPath )
{
	FILE *fp = fopen( pPath, "rb" );
	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 4096 ];
	size_t n;
	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );
	fclose( fp );
	return out;
}

// `NAME = <number>,` inside an enum, as an int; -1 when absent in that shape.
// The name must be followed by whitespace or '=', so a longer enumerator that
// starts with this one is not taken for it.
int EnumValue( const std::string &header, const char *pszName )
{
	const std::string name( pszName );
	for ( size_t at = header.find( name ); at != std::string::npos; at = header.find( name, at + 1 ) )
	{
		const size_t after = at + name.size();
		if ( after >= header.size() )
			break;
		if ( at > 0 && ( isalnum( (unsigned char)header[ at - 1 ] ) || header[ at - 1 ] == '_' ) )
			continue;
		size_t p = after;
		while ( p < header.size() && ( header[ p ] == ' ' || header[ p ] == '\t' ) )
			p++;
		if ( p >= header.size() || header[ p ] != '=' )
			continue;
		return (int)strtol( header.c_str() + p + 1, NULL, 0 );
	}
	return -1;
}

} // namespace

TEST( SsEffectDispatch, TheSlimeFlagIsSharedDefsOwnValue )
{
	const std::string header = ReadFile( "game/shared/shareddefs.h" );
	ASSERT_FALSE( header.empty() ) << "game/shared/shareddefs.h could not be read";

	const int nValve = EnumValue( header, "FX_WATER_IN_SLIME" );
	ASSERT_NE( -1, nValve ) << "FX_WATER_IN_SLIME is not `NAME = n` in shareddefs.h";
	EXPECT_EQ( nValve, (int)ss::kEffectFlagWaterInSlime );
}

TEST( SsEffectDispatch, TheDefaultAttachmentIsParticleParsesOwnValue )
{
	const std::string header = ReadFile( "game/shared/particle_parse.h" );
	ASSERT_FALSE( header.empty() ) << "game/shared/particle_parse.h could not be read";

	const int nValve = EnumValue( header, "PATTACH_ABSORIGIN" );
	ASSERT_NE( -1, nValve ) << "PATTACH_ABSORIGIN is not `NAME = n` in particle_parse.h";
	EXPECT_EQ( nValve, (int)ss::kEffectAttachAbsOrigin );
}

TEST( SsEffectDispatch, ARecordNobodyFilledSendsStocksDefaults )
{
	const ss::CEffectData data;

	EXPECT_EQ( 0, data.m_fFlags );
	EXPECT_EQ( 0, data.m_nEntIndex );
	EXPECT_EQ( 1.0f, data.m_flScale );
	EXPECT_EQ( 0.0f, data.m_flMagnitude );
	EXPECT_EQ( 0.0f, data.m_flRadius );
	EXPECT_EQ( 0, data.m_nAttachmentIndex );
	EXPECT_EQ( 0, data.m_nSurfaceProp );
	EXPECT_EQ( 0, data.m_nMaterial );
	EXPECT_EQ( 0, data.m_nDamageType );
	EXPECT_EQ( 0, data.m_nHitBox );
	EXPECT_EQ( 0, data.m_nColor );
	EXPECT_FALSE( data.m_bCustomColors );
	EXPECT_FALSE( data.m_bControlPoint1 );
	EXPECT_EQ( (int)ss::kEffectAttachAbsOrigin, data.m_ControlPoint1.m_eParticleAttachment );
	EXPECT_EQ( 0.0f, data.m_vOrigin.x );
	EXPECT_EQ( 0.0f, data.m_vNormal.z );
	EXPECT_EQ( 0.0f, data.m_vAngles.y );
	EXPECT_EQ( 0.0f, data.m_CustomColors.m_vecColor2.x );
	EXPECT_EQ( 0.0f, data.m_ControlPoint1.m_vecOffset.z );
}
