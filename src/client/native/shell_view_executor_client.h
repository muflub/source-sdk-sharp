//========= SourceSharp — the render plan's executor ==========================//
//
// plan_client_novalve.md §3.3 / C7: View_Render's body. Each frame managed
// says which path draws it (IManagedRenderClient::RenderFrame) and whether the
// frame is recorded; shell_view_executor_client.cpp carries that out.
//
//=============================================================================//
#pragma once

struct vrect_t;
class CViewSetup;

// CShellClientDLL::View_Render's body, after its empty-rect guard.
void SourceSharp_ViewRender( vrect_t *rect );

// C7 step 3: the client's IViewRender, shell_viewrender_client.cpp's
// CShellViewRender, which client init sets `view` to.
class IViewRender;
IViewRender *ShellViewRender();

// CShellViewRender's DisableVis, for the plan's inputs.
bool ShellViewRender_ForceNoVis();

// CShellViewRender's state the executor writes: the view a plan frame is
// drawing (GetViewSetup's answer), and the list build numbers.
void ShellViewRender_SetCurrentView( const CViewSetup &setup );
void ShellViewRender_IncWorldListsNumber();
void ShellViewRender_IncRenderablesListsNumber();
int ShellViewRender_RenderablesListsNumber();

// The executor's draws CShellViewRender carries out: RenderView through the
// plan (false when there is no plan to draw it with), a shadow-depth view the
// same, and the draw flags of the view being drawn (GetDrawFlags).
class ITexture;
bool SourceSharp_RenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw );
bool SourceSharp_ShadowDepthView( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &shadowView );
int SourceSharp_ActiveViewDrawFlags();

// CViewRender::FreezeFrame, into managed's freeze state (the plan reads it).
void SourceSharp_FreezeFrame( float flFreezeTime );

// C7 step 3: the view state view.cpp and viewrender.cpp kept in statics -- the
// main view SetUpViews made (and the last frame's), and the view being drawn
// now -- as CShellViewRender's. The stock names (MainViewOrigin,
// CurrentViewForward, SetupCurrentView, ...) answer from it.
#include "materialsystem/imaterialvar.h"
#include "bsptreedata.h"
#include "cdll_int.h"
#include "collisionproperty.h"
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
// bEffects: also hand the engine the screen fade range and the depth-to-alpha
// parameter, as stock's SetupCurrentView did.
void ShellView_SetupCurrentView( const Vector &vecOrigin, const QAngle &angles, int nViewID, bool bEffects );
void ShellView_FinishCurrentView();
void ShellView_AllowCurrentViewAccess( bool bAllow );
bool ShellView_CurrentViewAccessAllowed();
const ShellCamera_t &ShellView_Current();
int ShellView_CurrentViewID();

// The save-game screenshot's view of the player's eye at nWidth by nHeight,
// as managed decides it (ViewFrame.Screenshot). False with no managed client.
bool SourceSharp_ScreenshotView( const CViewSetup &eye, int nWidth, int nHeight, CViewSetup &out );

// SetUpViews' planes and FOV as managed decides them (one ViewSetUp crossing).
// False with no managed client.
bool SourceSharp_SetUpView( float &flZFar, float &flZFarViewmodel, float &flZNear, float &flZNearViewmodel, float &flFov,
                            float &flViewModelFovOffset );

// This frame's view decision (managed's ViewFrame, asked at View_Render),
// for CShellViewRender::Render to carry out: the eye RenderView draws, its
// clear and draw flags, gl_clear_randomcolor's colour (pRandomClear: on, r,
// g, b) and the level overview's green clear. False when managed made none.
// SetUpViews' planes as managed decides them now (one ViewSetUp crossing):
// GetZNear and GetZFar's answer. False with no managed client.
bool SourceSharp_AskViewPlanes( float &flZNear, float &flZFar );

bool SourceSharp_FrameViewDecision( CViewSetup &eye, int &nClearFlags, int &nDrawFlags, int *pRandomClear, bool &bOverview );
