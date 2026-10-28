//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "materialsystem/imesh.h"
#include "particledraw.h"
#include "particlemgr.h"
#include "cdll_client_int.h"
#include "timedevent.h"

// Render a quad on the screen where you pass in color and size.
// Color and alpha range is 0 to 254.9
inline void RenderParticle_Color255Size(
	ParticleDraw* pDraw,									
	const Vector &pos,
	const Vector &color,
	const float alpha,
	const float size)
{
	// Don't render totally transparent particles.
	if( alpha < 0.5f )
		return;

	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if( !pBuilder )
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt( color.x );
	ubColor[1] = (unsigned char)RoundFloatToInt( color.y );
	ubColor[2] = (unsigned char)RoundFloatToInt( color.z );
	ubColor[3] = (unsigned char)RoundFloatToInt( alpha );

	// Add the 4 corner vertices.
	pBuilder->Position3f( pos.x-size, pos.y-size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x-size, pos.y+size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x+size, pos.y+size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x+size, pos.y-size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
 	pBuilder->AdvanceVertex();
}

// Render a quad on the screen where you pass in color and size.
// Color and alpha range is 0 to 254.9
// Angle is in radians.
inline void RenderParticle_Color255SizeNormalAngle(
	ParticleDraw* pDraw,									
	const Vector &pos,
	const Vector &color,
	const float alpha,
	const float size,
	const Vector &vNormal,
	const float angle )
{
	// Don't render totally transparent particles.
	if( alpha < 0.5f )
		return;

	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if( !pBuilder )
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt( color.x );
	ubColor[1] = (unsigned char)RoundFloatToInt( color.y );
	ubColor[2] = (unsigned char)RoundFloatToInt( color.z );
	ubColor[3] = (unsigned char)RoundFloatToInt( alpha );

	float ca = (float)cos(angle);
	float sa = (float)sin(angle);

	// Add the 4 corner vertices.
	pBuilder->Position3f( pos.x + (-ca + sa) * size, pos.y + (-sa - ca) * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
	pBuilder->Normal3fv( (float*)&vNormal );
	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + (-ca - sa) * size, pos.y + (-sa + ca) * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
	pBuilder->Normal3fv( (float*)&vNormal );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + (ca - sa)  * size, pos.y + (sa + ca)  * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
	pBuilder->Normal3fv( (float*)&vNormal );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + (ca + sa)  * size, pos.y + (sa - ca)  * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
	pBuilder->Normal3fv( (float*)&vNormal );
 	pBuilder->AdvanceVertex();
}

// Render a quad on the screen where you pass in color and size.
inline void RenderParticle_ColorSize(
	ParticleDraw* pDraw,									
	const Vector &pos,
	const Vector &color,
	const float alpha,
	const float size
	)
{
	// Don't render totally transparent particles.
	if( alpha < 0.001f )
		return;

	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if( !pBuilder )
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt( color.x * 254.9f );
	ubColor[1] = (unsigned char)RoundFloatToInt( color.y * 254.9f );
	ubColor[2] = (unsigned char)RoundFloatToInt( color.z * 254.9f );
	ubColor[3] = (unsigned char)RoundFloatToInt( alpha * 254.9f );

	// Add the 4 corner vertices.
	pBuilder->Position3f( pos.x-size, pos.y-size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x-size, pos.y+size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x+size, pos.y+size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x+size, pos.y-size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
 	pBuilder->AdvanceVertex();
}

inline void RenderParticle_ColorSizeAngle(
	ParticleDraw* pDraw,									
	const Vector &pos,
	const Vector &color,
	const float alpha,
	const float size,
	const float angle)
{
	// Don't render totally transparent particles.
	if(alpha < 0.001f)
		return;

	CMeshBuilder *pBuilder = pDraw->GetMeshBuilder();
	if( !pBuilder )
		return;

	unsigned char ubColor[4];
	ubColor[0] = (unsigned char)RoundFloatToInt( color.x * 254.9f );
	ubColor[1] = (unsigned char)RoundFloatToInt( color.y * 254.9f );
	ubColor[2] = (unsigned char)RoundFloatToInt( color.z * 254.9f );
	ubColor[3] = (unsigned char)RoundFloatToInt( alpha * 254.9f );

	float sa, ca;
	SinCos(angle, &sa, &ca );

	pBuilder->Position3f( pos.x + (-ca + sa) * size, pos.y + (-sa - ca) * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + (-ca - sa) * size, pos.y + (-sa + ca) * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMins[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + (ca - sa)  * size, pos.y + (sa + ca)  * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMins[1] );
 	pBuilder->AdvanceVertex();

	pBuilder->Position3f( pos.x + (ca + sa)  * size, pos.y + (sa - ca)  * size, pos.z );
	pBuilder->Color4ubv( ubColor );
	pBuilder->TexCoord2f( 0, pDraw->m_pSubTexture->m_tCoordMaxs[0], pDraw->m_pSubTexture->m_tCoordMaxs[1] );
 	pBuilder->AdvanceVertex();
}

inline float GetAlphaDistanceFade(
	const Vector &pos,
	const float fadeNearDist,
	const float fadeFarDist)
{
	if(-pos.z > fadeFarDist)
	{
		return 1;
	}
	else if(-pos.z > fadeNearDist)
	{
		return (-pos.z - fadeNearDist) / (fadeFarDist - fadeNearDist);
	}
	else
	{
		return 0;
	}
}

inline Vector WorldGetLightForPoint(const Vector &vPos, bool bClamp)
{
	#if defined(PARTICLEPROTOTYPE_APP)
		return Vector(1,1,1);
	#else
		return engine->GetLightForPoint(vPos, bClamp);
	#endif
}
