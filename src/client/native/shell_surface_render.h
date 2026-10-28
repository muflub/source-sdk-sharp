//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Draws a broken func_breakable_surf pane: holds the materials managed code
// named and, when the engine hands it the brush surface, builds the plan's quads
// with the surface's texture and lightmap coordinates.
#pragma once

#include "materialsystem/imaterialvar.h"

#include "materialsystem/itexture.h"

class CMeshBuilder;

// Stock's values; they pick which of twelve edge materials draws and which way
// its texture runs. PaneSide and PaneEdge in C# must match.
enum SurfaceWinSide_t
{
	SURF_SIDE_BOTTOM,
	SURF_SIDE_RIGHT,
	SURF_SIDE_TOP,
	SURF_SIDE_LEFT,
};

enum SurfaceWinEdge_t
{
	SURF_EDGE_NOT = -1, // No edge at all
	SURF_EDGE_NONE, /* No edge on either side   /##\ */
	SURF_EDGE_FULL, // Edge on both             |##|
	SURF_EDGE_LEFT, /* Left only                |##\ */
	SURF_EDGE_RIGHT, // Right only               /##|
};

#define SOURCESHARP_SURFACE_EDGE_TYPES 4
#define SOURCESHARP_SURFACE_EDGE_STYLES 3

// The plan: a header of eight floats' bits (normal, corner, pane width and
// height), then entries of four words (kind, column, row, argument). A solid
// run's argument is its length in rows; an edge's or highlight's packs the side
// (bits 0-1), edge shape (bits 2-3) and style (bits 4 and up). Must match C#.
enum
{
	SURFACE_PLAN_HEADER_WORDS = 8,
	SURFACE_PLAN_ENTRY_WORDS = 4,
};

enum
{
	SURFACE_PLAN_SOLID = 0,
	SURFACE_PLAN_EDGE = 1,
	SURFACE_PLAN_HIGHLIGHT = 2,
};

// The materials SurfaceSetMaterials names, in order: the pieces', the solid
// block's, then the twelve edges' (edge * 3 + style).
#define SOURCESHARP_SURFACE_MATERIAL_NAMES ( 2 + SOURCESHARP_SURFACE_EDGE_TYPES * SOURCESHARP_SURFACE_EDGE_STYLES )

// SurfaceSetMaterials' flags.
enum
{
	SURFACE_MATERIALS_SWAPS_DETAIL = 1 << 0, // the pieces' material takes the detail texture
	SURFACE_MATERIALS_DEBUG = 1 << 1, // the pieces' material is the debug stand-in
};

struct SurfaceEdgeTexture_t
{
	CMaterialReference m_pMaterialEdge;
	CTextureReference m_pMaterialEdgeTexture;
};

// One broken surface's draw, owned by the shell and made when managed code first
// hands it materials or a plan.
class CSourceSharpSurfaceRenderer : public IBrushRenderer
{
public:
	CSourceSharpSurfaceRenderer();
	virtual ~CSourceSharpSurfaceRenderer();

	void SetMaterials( const char *const *ppNames, int nFlags );

	// What to draw, replacing the last plan; an empty one draws nothing.
	void SetPlan( const int32_t *pWords, int nWords );

	// The engine hands each brush surface to RenderBrushModelSurface while installed.
	void BeginDraw();
	void EndDraw();

	virtual bool RenderBrushModelSurface( IClientEntity *pBaseEntity, IBrushSurface *pBrushSurface ) OVERRIDE;

private:
	void SetDetailTexture( ITexture *pTexture );
	void Steps( Vector *pWidthStep, Vector *pHeightStep ) const;
	void DrawQuad( IBrushSurface *pBrushSurface, IMesh *pMesh, CMeshBuilder *pBuilder, const Vector &vStart,
				   const Vector &vWStep, const Vector &vHStep, const float ( *pCorners )[2], bool bGrout );

	Vector m_vNormal;
	Vector m_vCorner;
	float m_flPanelWidth;
	float m_flPanelHeight;
	CUtlVector<int32_t> m_Plan;

	CMaterialReference m_pCrackedMaterial;
	CMaterialReference m_pMaterialBox;
	CTextureReference m_pMaterialBoxTexture;
	IMaterialVar *m_pDetailVar;

	SurfaceEdgeTexture_t m_pEdge[SOURCESHARP_SURFACE_EDGE_TYPES][SOURCESHARP_SURFACE_EDGE_STYLES];

	bool m_bMaterialsReady;
	bool m_bDrawReported;
};
