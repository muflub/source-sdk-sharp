//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — drawing a shattered pane of glass ==================//
//
// The client half of func_breakable_surf, off C_BaseEntity.
//
// Stock's renderer is a C_BaseEntity that also implements IBrushRenderer, and
// nothing on the parallel hierarchy is a C_BaseEntity — so it could not come
// along unchanged. It did not need to be one: what it actually asks for is to
// be an IClientRenderable (for Bind) and to have the brush model drawn with
// itself installed as the surface renderer, and the standalone shell is the
// first thing here that can supply both.
//
// So this is NOT an entity. It is a renderer the shell owns, one per surface,
// which reads the class's replicated state out of the entity's blob and draws
// the panes that are left. Everything about how the mesh is built is stock's,
// nearly line for line, because how glass is drawn was never the thing worth
// changing.
//
// WHICH pieces are left is not decided here at all. That is the rule, it is in
// C#, and it arrives over the wire.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_SURFACE_RENDER_H
#define SOURCESHARP_SHELL_SURFACE_RENDER_H
#ifdef _WIN32
#pragma once
#endif

#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/imesh.h"
#include "materialsystem/itexture.h"
#include "ivrenderview.h"
#include "utlvector.h"
#include "generic_payload.h"   // SourceSharpPackedField_t

class C_StandaloneEntity;
class CMeshBuilder;

// The one class this renderer draws.
//
// A native literal, and the only one left: a renderer written in C++ has to
// know which class it is for, and there is nothing to derive that from. It must
// match EntityNames.FuncBreakableSurf, which is where every other copy went.
#define SOURCESHARP_SURFACE_CLASSNAME "func_breakable_surf"

// Must match BreakableSurfaceNetState.MaxPanels. The grid is 16 on a side and
// the wire carries 16 x 16 bits whatever any one surface uses.
#define SOURCESHARP_SURFACE_MAX_PANELS 16

//-----------------------------------------------------------------------------
// Which side of a pane an edge is on, and what shape that edge is. Stock's,
// unchanged — these choose which of twelve edge materials draws.
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
// One edge queued to be drawn.
//-----------------------------------------------------------------------------
struct SurfacePanel_t
{
	char m_nWidth;
	char m_nHeight;
	char m_nSide;
	char m_nEdgeType;
	char m_nStyle;
};

struct SurfaceEdgeTexture_t
{
	int                 m_nRenderIndex;
	int                 m_nStyle;
	CMaterialReference  m_pMaterialEdge;
	CTextureReference   m_pMaterialEdgeTexture;
};

//-----------------------------------------------------------------------------
// A pane of glass, drawn.
//-----------------------------------------------------------------------------
class CSourceSharpSurfaceRenderer : public IBrushRenderer
{
public:
	CSourceSharpSurfaceRenderer();
	virtual ~CSourceSharpSurfaceRenderer();

	// Reads the replicated state out of the entity's blob and brings the edge
	// list up to date. Called on every update, because which panes are left is
	// the whole of what changes.
	void Update( C_StandaloneEntity *pEntity, bool bCreated );

	// True once the sheet has broken. Before that there is no grid and the
	// brush draws exactly as any other brush does.
	bool IsBroken() const { return m_bIsBroken; }

	// The cracked edges to draw, decided in C#. Four bytes each; see
	// SurfaceSetEdges in sourcesharp.h for the packing.
	void SetEdges( const unsigned char *pEdges, int nCount );

	// Installs itself for the duration of one brush draw.
	void BeginDraw();
	void EndDraw();

	// --- IBrushRenderer ------------------------------------------------------
	virtual bool RenderBrushModelSurface( IClientEntity *pBaseEntity,
	                                      IBrushSurface *pBrushSurface ) OVERRIDE;

private:

	bool InLegalRange( int w, int h ) const;
	bool IsPanelSolid( int w, int h ) const;
	void SetPanelSolid( int w, int h, bool bValue );

	// A packed field's whole placement, by name. Static because it asks C# and
	// keeps nothing: the answer is a property of the build, not of a surface.
	static SourceSharpPackedField_t ResolvePacked( const char *pClass, const char *pField );

	void InitMaterials( const model_t *pModel );
	void InitMaterial( SurfaceWinEdge_t nEdgeType, int nStyle, const char *pMaterialName );

	void DrawSolidBlocks( IBrushSurface *pBrushSurface );
	void DrawRenderList( IBrushSurface *pBrushSurface );
	void DrawRenderListHighlights( IBrushSurface *pBrushSurface );

	void DrawOneBlock( IBrushSurface *pBrushSurface, IMesh *pMesh, CMeshBuilder *pBuilder,
	                   const Vector &vPos, const Vector &vWidth, const Vector &vHeight );
	void DrawOneEdge( IBrushSurface *pBrushSurface, IMesh *pMesh, CMeshBuilder *pBuilder,
	                  const Vector &vStart, const Vector &vWStep, const Vector &vHStep,
	                  SurfaceWinSide_t nEdge );
	void DrawOneHighlight( IBrushSurface *pBrushSurface, IMesh *pMesh, CMeshBuilder *pBuilder,
	                       const Vector &vStart, const Vector &vWStep, const Vector &vHStep,
	                       SurfaceWinSide_t nEdge );

	// The detail texture the cracked material draws with, set directly rather
	// than through a material proxy. Stock's proxy resolves the bind argument
	// to a C_BaseEntity, which this is not — so it would quietly do nothing.
	void SetDetailTexture( ITexture *pTexture );

	void Steps( Vector *pWidthStep, Vector *pHeightStep ) const;

	// --- replicated state, decoded from the blob -----------------------------
	int    m_nNumWide;
	int    m_nNumHigh;
	float  m_flPanelWidth;
	float  m_flPanelHeight;
	Vector m_vNormal;
	Vector m_vCorner;
	bool   m_bIsBroken;
	int    m_nSurfaceType;

	// --- local ---------------------------------------------------------------
	//
	// Which panes are still there, which is all that is left of the state this
	// class used to derive: the edges are decided in C# and arrive through
	// SetEdges, and the crack styles and staleness went with them.
	bool m_bPaneSolid[ SOURCESHARP_SURFACE_MAX_PANELS ][ SOURCESHARP_SURFACE_MAX_PANELS ];

	CUtlVector< SurfacePanel_t > m_RenderList;

	CMaterialReference m_pCrackedMaterial;
	CMaterialReference m_pMaterialBox;
	CTextureReference  m_pMaterialBoxTexture;
	IMaterialVar      *m_pDetailVar;

	SurfaceEdgeTexture_t m_pEdge[ SOURCESHARP_SURFACE_EDGE_TYPES ][ SOURCESHARP_SURFACE_EDGE_STYLES ];

	bool m_bMaterialsReady;

	// Whether the surface's material named a cracked variant. Decides whether
	// the detail texture is swapped at all — see InitMaterials.
	bool m_bHasCrackedMaterial;

	// Whether the first draw has been reported. See BeginDraw.
	bool m_bDrawReported;

	// Where each field sits in the entity, resolved once by NAME from the table
	// C# described. Not written down here: the offsets are C#'s, and a second
	// copy of them is the drift this whole design removes.
	//
	// Three states rather than two, because a FAILED lookup has to be cached as
	// well: it runs per entity per update, and one that retried would walk the
	// prop list and warn again every time.
	enum
	{
		RESOLVE_PENDING,
		RESOLVE_DONE,
		RESOLVE_FAILED,
	};

	int m_nResolved;

	// PLACEMENTS, not offsets, since Part 10's packing (Step 8). Wide, High,
	// Broken and SurfaceType share one word now, so the offset alone names the
	// word rather than the field and reading a byte at it returns whichever
	// field starts there. The four that pack carry a shift and a width beside
	// the offset; the rest are whole storage and keep a plain offset, because a
	// Vector and a float are read as themselves and always were.
	SourceSharpPackedField_t m_fldWide, m_fldHigh, m_fldBroken, m_fldSurfaceType;

	int  m_nOffPanelWidth, m_nOffPanelHeight;
	int  m_nOffNormal, m_nOffCorner;
	int  m_nOffPaneBits[ 8 ];
};

// The object-relative offset of a described field, or -1. See the definition:
// this is what lets native read C#-declared state without a second copy of the
// layout.
int SourceSharp_StandaloneFieldOffset( const char *pClassName, const char *pFieldName );

// Where inside that offset's dword the field's bits are: shift in the low byte,
// width above it, -1 for unknown. The other half of a placement since packing —
// see SourceSharpPayload_Place.
int SourceSharp_StandaloneFieldBits( const char *pClassName, const char *pFieldName );

#endif // SOURCESHARP_SHELL_SURFACE_RENDER_H
