// The render plan's executor: View_Render's body, carried out as managed code decides each frame.
#pragma once

struct vrect_t;
class CViewSetup;

// CShellClientDLL::View_Render's body, after its empty-rect guard.
void SourceSharp_ViewRender( vrect_t *rect );

// The client's IViewRender, which client init sets `view` to.
class IViewRender;
IViewRender *ShellViewRender();

bool ShellViewRender_ForceNoVis();

// The view a plan frame is drawing (GetViewSetup's answer), and the list build numbers.
void ShellViewRender_SetCurrentView( const CViewSetup &setup );
void ShellViewRender_IncWorldListsNumber();
void ShellViewRender_IncRenderablesListsNumber();
int ShellViewRender_RenderablesListsNumber();

// RenderView through the plan (false when there is no plan), a shadow-depth
// view likewise, and the draw flags of the view being drawn.
class ITexture;
bool SourceSharp_RenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw );
bool SourceSharp_ShadowDepthView( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &shadowView );
int SourceSharp_ActiveViewDrawFlags();

void SourceSharp_FreezeFrame( float flFreezeTime );

// The view state stock kept in statics: the main view SetUpViews made (and the
// last frame's) and the view being drawn now. MainViewOrigin and the other
// stock names answer from it.
#include "materialsystem/imaterialvar.h"
#include "bsptreedata.h"


struct ShellCamera_t
{
	Vector origin;
	QAngle angles;
	Vector forward, right, up;
	VMatrix worldToView;
};
// view.cpp's ComputeCameraVariables: the view's bases and its world-to-view matrix.
void ShellView_ComputeCamera( const Vector &vecOrigin, const QAngle &angles, ShellCamera_t &out );
// SetUpViews' end: this frame's main view, the last one kept as the previous.
void ShellView_SetMainView( const Vector &vecOrigin, const QAngle &angles );
const ShellCamera_t &ShellView_Main();
const Vector &ShellView_PrevMainOrigin();
const QAngle &ShellView_PrevMainAngles();
// viewrender.cpp's SetupCurrentView, FinishCurrentView and AllowCurrentViewAccess.
// bEffects also hands the engine the screen fade range and the depth-to-alpha
// parameter, as stock's SetupCurrentView did.
void ShellView_SetupCurrentView( const Vector &vecOrigin, const QAngle &angles, int nViewID, bool bEffects );
void ShellView_FinishCurrentView();
void ShellView_AllowCurrentViewAccess( bool bAllow );
bool ShellView_CurrentViewAccessAllowed();
const ShellCamera_t &ShellView_Current();
int ShellView_CurrentViewID();

// The save-game screenshot's view of the player's eye; false with no managed client.
bool SourceSharp_ScreenshotView( const CViewSetup &eye, int nWidth, int nHeight, CViewSetup &out );

// SetUpViews' planes and FOV as managed code decides them; false with no managed client.
bool SourceSharp_SetUpView( float &flZFar, float &flZFarViewmodel, float &flZNear, float &flZNearViewmodel, float &flFov,
                            float &flViewModelFovOffset );

// GetZNear and GetZFar's planes as managed code decides them. False with no managed client.
bool SourceSharp_AskViewPlanes( float &flZNear, float &flZFar );

// This frame's view for CShellViewRender::Render: the eye RenderView draws, its
// clear and draw flags, gl_clear_randomcolor's colour (pRandomClear: on, r, g, b)
// and the level overview's green clear. False when managed code made none.
bool SourceSharp_FrameViewDecision( CViewSetup &eye, int &nClearFlags, int &nDrawFlags, int *pRandomClear, bool &bOverview );
