//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — drawing a shattered pane of glass ==================//
//
// The native half of func_breakable_surf's draw, off C_BaseEntity.
//
// Stock's renderer is a C_BaseEntity that also implements IBrushRenderer, and
// nothing on the parallel hierarchy is a C_BaseEntity — so it could not come
// along unchanged. It did not need to be one: what it actually asks for is to
// have the brush model drawn with itself installed as the surface renderer,
// and the standalone shell can supply that.
//
// So this is NOT an entity, and since lane M1-A it decides nothing. Managed's
// ClientBreakableSurf (BreakableSurfaceDraw) chooses the materials and makes
// the plan: which panes still stand, in which runs, which edges are cracked
// and in what order everything draws. This holds the material references it
// named and, when the engine hands it the brush surface, builds the plan's
// quads: their corners in the surface's frame and the surface's own texture
// and lightmap coordinates at each vertex.
//
//=============================================================================//
#pragma once

#include "materialsystem/imaterialvar.h"
#include "materialsystem/imesh.h"
#include "materialsystem/itexture.h"
#include "ivrenderview.h"
#include "tier1/utlvector.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "tier0/platform.h"
#include "collisionproperty.h"
#include "icliententitylist.h"

class CMeshBuilder;

//-----------------------------------------------------------------------------
// Which side of a pane an edge is on, and what shape that edge is. Stock's,
// unchanged — these choose which of twelve edge materials draws and which
// way its texture runs. PaneSide and PaneEdge in C# are the same numbers.
//-----------------------------------------------------------------------------
enum SurfaceWinSide_t
{
	SURF_SIDE_BOTTOM,
	SURF_SIDE_RIGHT,
	SURF_SIDE_TOP,
	SURF_SIDE_LEFT,
};

enum SurfaceWinEdge_t
{
	SURF_EDGE_NOT = -1,     // No edge at all
	SURF_EDGE_NONE,         /* No edge on either side   /##\ */
	SURF_EDGE_FULL,         // Edge on both             |##|
	SURF_EDGE_LEFT,         /* Left only                |##\ */
	SURF_EDGE_RIGHT,        // Right only               /##|
};

#define SOURCESHARP_SURFACE_EDGE_TYPES  4
#define SOURCESHARP_SURFACE_EDGE_STYLES 3

//-----------------------------------------------------------------------------
// The plan's words, as BreakableSurfaceDraw writes them (the C# constants of
// the same names; BreakableSurfaceDrawTests pins the pair): a header of eight
// floats' bits (the normal, the corner, the pane width and height), then
// entries of four words (kind, column, row, argument). A solid run's argument
// is its length in rows; an edge's or a highlight's packs the side (bits 0-1),
// the edge shape (bits 2-3) and the style (bits 4 and up).
//-----------------------------------------------------------------------------
enum
{
	SURFACE_PLAN_HEADER_WORDS = 8,
	SURFACE_PLAN_ENTRY_WORDS  = 4,
};

enum
{
	SURFACE_PLAN_SOLID     = 0,
	SURFACE_PLAN_EDGE      = 1,
	SURFACE_PLAN_HIGHLIGHT = 2,
};

// The materials SurfaceSetMaterials names, in order: the pieces', the solid
// block's, then the twelve edges' (edge * 3 + style).
#define SOURCESHARP_SURFACE_MATERIAL_NAMES ( 2 + SOURCESHARP_SURFACE_EDGE_TYPES * SOURCESHARP_SURFACE_EDGE_STYLES )

// SurfaceSetMaterials' flags.
enum
{
	SURFACE_MATERIALS_SWAPS_DETAIL = 1 << 0,  // the pieces' material takes the detail texture
	SURFACE_MATERIALS_DEBUG        = 1 << 1,  // the pieces' material is the debug stand-in
};

struct SurfaceEdgeTexture_t
{
	CMaterialReference  m_pMaterialEdge;
	CTextureReference   m_pMaterialEdgeTexture;
};

//-----------------------------------------------------------------------------
// One broken surface's draw. Owned by the shell (C_StandaloneEntity::
// SurfaceRenderer), made when managed first hands it materials or a plan.
//-----------------------------------------------------------------------------
class CSourceSharpSurfaceRenderer : public IBrushRenderer
{
public:
	CSourceSharpSurfaceRenderer();
	virtual ~CSourceSharpSurfaceRenderer();

	// The materials managed named, referenced.
	void SetMaterials( const char *const *ppNames, int nFlags );

	// What to draw, replacing the last plan; an empty one draws nothing.
	void SetPlan( const int32_t *pWords, int nWords );

	// Installed around the brush model's draw: the engine hands each brush
	// surface to RenderBrushModelSurface while it is installed.
	void BeginDraw();
	void EndDraw();

	virtual bool RenderBrushModelSurface( IClientEntity *pBaseEntity,
	                                      IBrushSurface *pBrushSurface ) OVERRIDE;

private:
	void SetDetailTexture( ITexture *pTexture );
	void Steps( Vector *pWidthStep, Vector *pHeightStep ) const;
	void DrawQuad( IBrushSurface *pBrushSurface, IMesh *pMesh, CMeshBuilder *pBuilder,
	               const Vector &vStart, const Vector &vWStep, const Vector &vHStep,
	               const float ( *pCorners )[2], bool bGrout );

	// The plan's header and entries.
	Vector m_vNormal;
	Vector m_vCorner;
	float  m_flPanelWidth;
	float  m_flPanelHeight;
	CUtlVector< int32_t > m_Plan;

	CMaterialReference m_pCrackedMaterial;
	CMaterialReference m_pMaterialBox;
	CTextureReference  m_pMaterialBoxTexture;
	IMaterialVar      *m_pDetailVar;

	SurfaceEdgeTexture_t m_pEdge[ SOURCESHARP_SURFACE_EDGE_TYPES ][ SOURCESHARP_SURFACE_EDGE_STYLES ];

	bool m_bMaterialsReady;
	bool m_bDrawReported;
};
