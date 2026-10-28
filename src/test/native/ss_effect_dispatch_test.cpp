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

} // namespace

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
