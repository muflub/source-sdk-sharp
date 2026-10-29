//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class IVideoMaterial;

// Forward declarations
class IMaterial;
class IMaterialVar;
typedef struct wrect_s wrect_t;

// Purpose: Sprite Models
class CEngineSprite
{
	// NOTE: don't define a constructor or destructor so that this can be allocated
	// as before.
public:
	IMaterial *GetMaterial( RenderMode_t nRenderMode ) { return m_material[nRenderMode]; }

	IMaterial *GetMaterial( RenderMode_t nRenderMode, int nFrame );
	void SetFrame( RenderMode_t nRenderMode, int nFrame );
	bool Init( const char *name );
	void Shutdown();
	void UnloadMaterial();
	void SetColor( float r, float g, float b );
	int GetOrientation();

	float GetUp() { return up; }

	float GetDown() { return down; }

	float GetLeft() { return left; }

	float GetRight() { return right; }

	bool IsVideo();
	void GetTexCoordRange( float *pMinU, float *pMinV, float *pMaxU, float *pMaxV );

private:
	IVideoMaterial *m_VideoMaterial;
	int m_width;
	int m_height;
	int m_numFrames;
	IMaterial *m_material[kRenderModeCount];
	int m_orientation;
	alignas( 4 ) unsigned char unused_m_hudSpriteColor[12];
	float up, down, left, right;
};
