//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The client's IViewRender (`view`). The frame's view is managed's decision
// and every view is the render plan's; this carries both out: the client
// mode's override and hooks, the engine's main view and audio, the 2D panels
// and the save-game screenshot. It holds the main and current view state, and
// the free functions fork TUs still call (MainViewOrigin, CurrentViewForward,
// R_CullSphere, ...) answer from it. Convars fork TUs link against by object
// stay native here.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"

#include "viewrender.h"

#include "iviewrender_beams.h"
#include "c_te_legacytempents.h"
#include "iinput.h"
#include "client_entity.h"
#include "clienteffectprecachesystem.h"
#include "vgui/ISurface.h"
#include "vgui_controls/Controls.h"

#include "iclientmode.h"
#include "cl_mat_stub.h"

#include "ienginevgui.h"
#include "filesystem.h"

#include "fx.h"

#include "shell_view_executor_client.h"
#include "shell_view_render.h"
#include "shell_beams_client.h"
#include "shell_viewer_facts.h"
#include "shell_client_mode.h"

#include "view_facts.h"
#include "shell_diagnostics.h"
#include "ss_image_encode.h"

CShellViewRender::CShellViewRender(const CManagedAreasClient& areas, CViewerFacts& viewerFacts,
								   CSourceSharpClientMode& clientMode, CViewExecutor& executor, CBeamsClient& beams)
	: m_Areas(areas)
	, m_ViewerFacts(viewerFacts)
	, m_ClientMode(clientMode)
	, m_Executor(executor)
	, m_Beams(beams)
{
}

void CShellViewRender::Init()
{
	m_Beams.InitBeams();
	tempents->Init();
}

void CShellViewRender::LevelInit()
{
	m_Beams.ClearBeams();
	tempents->Clear();
	m_nBuildWorldListsNumber = 0;
	m_nBuildRenderablesListsNumber = 0;
	m_Executor.FreezeFrame(0.0f);
	ReleaseScreenOverlay();
	// The screen-space effect manager is managed's.
	if (IManagedRenderClient* pManaged = m_Areas.m_pRenderClient)
		pManaged->ScreenSpaceEffectsStage(0);
}

void CShellViewRender::LevelShutdown()
{
	if (IManagedRenderClient* pManaged = m_Areas.m_pRenderClient)
		pManaged->ScreenSpaceEffectsStage(1);
	m_nCurrentViewID = VIEW_NONE;
}

void CShellViewRender::Shutdown()
{
	ReleaseScreenOverlay();
	m_Beams.ShutdownBeams();
	tempents->Shutdown();
}

void CShellViewRender::OnRenderStart()
{
	SetUpViews();
}

void CShellViewRender::RenderView(const CViewSetup& setup, int nClearFlags, int whatToDraw)
{
	if (m_Executor.RenderView(setup, nClearFlags, whatToDraw) || m_bSaidUnplanned)
		return;
	m_bSaidUnplanned = true;
	Warning("[SourceSharp] view: a view with no plan to draw it (declined, or no managed client) draws nothing\n");
}

int CShellViewRender::GetDrawFlags()
{
	return m_Executor.ActiveViewDrawFlags();
}

VPlane* CShellViewRender::GetFrustum()
{
	return m_Frustum;
}

bool CShellViewRender::ShouldDrawBrushModels()
{
	ConVarRef drawBrushModels("r_drawbrushmodels", true);
	return !drawBrushModels.IsValid() || drawBrushModels.GetInt() != 0;
}

const CViewSetup* CShellViewRender::GetPlayerViewSetup() const
{
	return &m_View;
}

const CViewSetup* CShellViewRender::GetViewSetup() const
{
	return &m_CurrentView;
}

void CShellViewRender::SetCurrentView(const CViewSetup& setup)
{
	m_CurrentView = setup;
}

void CShellViewRender::DisableVis()
{
	m_bForceNoVis = true;
}

bool CShellViewRender::ForceNoVis() const
{
	return m_bForceNoVis;
}

int CShellViewRender::BuildWorldListsNumber() const
{
	return m_nBuildWorldListsNumber;
}

void CShellViewRender::IncWorldListsNumber()
{
	++m_nBuildWorldListsNumber;
}

void CShellViewRender::IncRenderablesListsNumber()
{
	++m_nBuildRenderablesListsNumber;
}

int CShellViewRender::RenderablesListsNumber() const
{
	return m_nBuildRenderablesListsNumber;
}

void CShellViewRender::SetCheapWaterStartDistance(float flDistance)
{
	m_flCheapWaterStartDistance = flDistance;
}

void CShellViewRender::SetCheapWaterEndDistance(float flDistance)
{
	m_flCheapWaterEndDistance = flDistance;
}

void CShellViewRender::GetWaterLODParams(float& flStart, float& flEnd)
{
	flStart = m_flCheapWaterStartDistance;
	flEnd = m_flCheapWaterEndDistance;
}

void CShellViewRender::DriftPitch()
{
	SourceSharp_NotShipped("pitch drift (v_centermove)",
						   "the drift is written into the local player entity's own local angles, "
						   "and on this client the view angle is the engine's -- there is no second "
						   "author for it");
}

void CShellViewRender::SetScreenOverlayMaterial(IMaterial* pMaterial)
{
	if (pMaterial)
		pMaterial->IncrementReferenceCount();
	ReleaseScreenOverlay();
	m_pScreenOverlay = pMaterial;
}

IMaterial* CShellViewRender::GetScreenOverlayMaterial()
{
	return m_pScreenOverlay;
}

void CShellViewRender::ReleaseScreenOverlay()
{
	if (m_pScreenOverlay)
		m_pScreenOverlay->DecrementReferenceCount();
	m_pScreenOverlay = NULL;
}

void CShellViewRender::WriteSaveGameScreenshot(const char* pFilename)
{
	WriteSaveGameScreenshotOfSize(pFilename, 180, 100, false, false);
}

void CShellViewRender::WriteReplayScreenshot(WriteReplayScreenshotParams_t& params)
{
	SourceSharp_NotShipped("replay screenshot",
						   "replay is not shipped: replay/replay_screenshot.cpp is not in this "
						   "build and there is no screenshot taker to hand the frame to");
}

void CShellViewRender::QueueOverlayRenderView(const CViewSetup& setup, int nClearFlags, int whatToDraw)
{
	SourceSharp_NotShipped("overlay render view (QueueOverlayRenderView)",
						   "the render plan has no overlay-view arm, and nothing in this build queues one");
}

float CShellViewRender::GetZNear()
{
	float flNear = 0.0f, flFar = 0.0f;
	m_Executor.AskViewPlanes(flNear, flFar);
	return flNear;
}

float CShellViewRender::GetZFar()
{
	float flNear = 0.0f, flFar = 0.0f;
	m_Executor.AskViewPlanes(flNear, flFar);
	return flFar;
}

void CShellViewRender::GetScreenFadeDistances(float* pMin, float* pMax)
{
	if (pMin)
	{
		ConVarRef minSize("r_screenfademinsize", true);
		*pMin = minSize.IsValid() ? minSize.GetFloat() : 0.0f;
	}
	if (pMax)
	{
		ConVarRef maxSize("r_screenfademaxsize", true);
		*pMax = maxSize.IsValid() ? maxSize.GetFloat() : 0.0f;
	}
}

IGameEntity* CShellViewRender::GetCurrentlyDrawingEntity()
{
	return NULL;
}

bool CShellViewRender::UpdateShadowDepthTexture(ITexture* pRenderTarget, ITexture* pDepthTexture,
												const CViewSetup& shadowView)
{
	// The shadow-depth view is the plan's.
	return m_Executor.ShadowDepthView(pRenderTarget, pDepthTexture, shadowView);
}

void CShellViewRender::FreezeFrame(float flFreezeTime)
{
	m_Executor.FreezeFrame(flFreezeTime);
}

IReplayScreenshotSystem* CShellViewRender::GetReplayScreenshotSystem()
{
	return this;
}

// CViewRender::Render, mono: managed decided the eye (FOVs, viewport, aspect,
// clear and draw flags, the level overview); this carries it out and leaves the
// eye in the player view, as stock's Render did.
void CShellViewRender::Render(vrect_t* rect)
{
	CViewSetup eye;
	int nClearFlags = 0, nDrawFlags = 0;
	int randomClear[4] = {0, 0, 0, 0};
	bool bOverview = false;
	if (!m_Executor.FrameViewDecision(eye, nClearFlags, nDrawFlags, randomClear, bOverview))
	{
		if (!m_bSaidNoDecision)
		{
			m_bSaidNoDecision = true;
			Warning("[SourceSharp] view: no view decision from managed this frame; nothing is drawn\n");
		}
		return;
	}

	CMatStubHandler matStub;
	engine->EngineStats_BeginFrame();
	m_bForceNoVis = false;

	CViewSetup& player = m_View;
	render->SetMainView(player.origin, player.angles);

	m_ClientMode.PreRender(&eye);

	if (randomClear[0])
	{
		CMatRenderContextPtr pRenderContext(materials);
		pRenderContext->ClearColor3ub((unsigned char)randomClear[1], (unsigned char)randomClear[2],
									  (unsigned char)randomClear[3]);
		pRenderContext->ClearBuffers(true, false, false);
	}
	if (bOverview)
	{
		CMatRenderContextPtr pRenderContext(materials);
		pRenderContext->ClearColor4ub(0, 255, 0, 255);
	}

	player = eye;
	RenderView(eye, nClearFlags, nDrawFlags);

	m_ClientMode.PostRender();
	engine->EngineStats_EndFrame();
	matStub.End();

	// The UI drawn full screen (the escape menu and the cursor).
	CViewSetup view2d;
	view2d.x = rect->x;
	view2d.y = rect->y;
	view2d.width = rect->width;
	view2d.height = rect->height;
	render->Push2DView(view2d, 0, NULL, GetFrustum());
	render->VGui_Paint(PAINT_UIPANELS | PAINT_CURSOR);
	render->PopView(GetFrustum());
}

// CViewRender::WriteSaveGameScreenshotOfSize: the player's eye drawn through the
// plan into the top left of the back buffer, read back, and written as a TGA or
// VTF to //MOD/. Managed decides the view; this carries out the draw and the file.
void CShellViewRender::WriteSaveGameScreenshotOfSize(const char* pFilename, int width, int height,
													 bool bCreatePowerOf2Padded, bool bWriteVTF)
{
	CViewSetup viewSetup;
	if (width <= 0 || height <= 0 || !m_Executor.ScreenshotView(*GetPlayerViewSetup(), width, height, viewSetup))
	{
		Warning("[SourceSharp] view: no save-game screenshot of %s (%dx%d): no view from managed\n", pFilename, width,
				height);
		return;
	}

	CMatRenderContextPtr pRenderContext(materials);
	pRenderContext->MatrixMode(MATERIAL_PROJECTION);
	pRenderContext->PushMatrix();
	pRenderContext->MatrixMode(MATERIAL_VIEW);
	pRenderContext->PushMatrix();

	pRenderContext->PushRenderTargetAndViewport(NULL, 0, 0, width, height);

	RenderView(viewSetup, VIEW_CLEAR_DEPTH | VIEW_CLEAR_COLOR, 0);

	// The pixels cross to managed once, which pads them and encodes the file.
	const int nPixelBytes = width * height * 3;
	uint8_t* pBlock = ss::ImageEncodeBlock(nPixelBytes);
	if (!pBlock)
		Error("Couldn't write bitmap data snapshot.\n");
	pRenderContext->ReadPixels(0, 0, width, height, ss::ImageEncodePixels(pBlock), IMAGE_FORMAT_RGB888);

	int nFileBytes = 0;
	unsigned char* pFile = ss::ImageEncode(bWriteVTF ? ss::kImageVtfRgb : ss::kImageTgaRgb, width, height,
										   bCreatePowerOf2Padded, pBlock, nPixelBytes, &nFileBytes, true);
	free(pBlock);
	if (!pFile)
		Error("Couldn't write bitmap data snapshot.\n");

	// The async write takes the file's memory; it is on the engine heap because
	// the filesystem frees it with tier0's allocator.
	char szPathedFileName[MAX_PATH];
	Q_snprintf(szPathedFileName, sizeof(szPathedFileName), "//MOD/%s", pFilename);
	filesystem->AsyncWrite(szPathedFileName, pFile, nFileBytes, true);

	pRenderContext->PopRenderTargetAndViewport();
	pRenderContext->MatrixMode(MATERIAL_PROJECTION);
	pRenderContext->PopMatrix();
	pRenderContext->MatrixMode(MATERIAL_VIEW);
	pRenderContext->PopMatrix();
}

void CShellViewRender::DemoCamera(Vector& origin, QAngle& angles)
{
	IManagedRenderClient* pManaged = m_Areas.m_pRenderClient;
	if (!pManaged)
		return;
	float in[6] = {origin.x, origin.y, origin.z, angles.x, angles.y, angles.z};
	float out[6] = {0, 0, 0, 0, 0, 0};
	if (!pManaged->ViewDemoCamera(0, in, out))
		return;
	QAngle demoAngles(out[0], out[1], out[2]);
	engine->SetViewAngles(demoAngles);
	input->ExtraMouseSample(gpGlobals->absoluteframetime, true);
	engine->GetViewAngles(demoAngles);
	float move[6] = {demoAngles.x, demoAngles.y, demoAngles.z, gpGlobals->absoluteframetime, 0, 0};
	pManaged->ViewDemoCamera(1, move, out);
	origin.Init(out[0], out[1], out[2]);
	angles.Init(out[3], out[4], out[5]);
}

// CViewRender::SetUpViews, mono: managed decides the planes and the FOV; the
// client mode's override puts the viewer's eye in, a view entity overrides it,
// and a playing demo may move it. Then the viewmodel's FOV follows the
// override's zoom, and the main view and the engine's audio state take the eye.
void CShellViewRender::SetUpViews()
{
	float flViewModelFovOffset = 0.0f;
	CViewSetup& eye = m_View;
	if (!m_Executor.SetUpView(eye.zFar, eye.zFarViewmodel, eye.zNear, eye.zNearViewmodel, eye.fov,
							  flViewModelFovOffset))
		return;
	eye.m_bOrtho = false;
	eye.m_bViewToProjectionOverride = false;
	eye.m_eStereoEye = STEREO_EYE_MONO;

	::partition->SuppressLists(PARTITION_ALL_CLIENT_EDICTS, false);

	// Names the refusal when the engine says this client watches a broadcast.
	SourceSharp_HltvCameraAvailable();

	eye.origin.Init();
	eye.angles.Init();
	m_ClientMode.OverrideView(&eye);

	// Looking out of something else's eyes (point_viewcontrol, a camera): the
	// last word, after the override.
	if (!g_nKillCamMode)
	{
		Vector vecViewEntityOrigin;
		QAngle angViewEntityAngles;
		if (m_ViewerFacts.ViewEntityPose(&vecViewEntityOrigin, &angViewEntityAngles))
		{
			eye.origin = vecViewEntityOrigin;
			eye.angles = angViewEntityAngles;
		}
	}

	if (engine->IsPlayingDemo())
		DemoCamera(eye.origin, eye.angles);

	// The viewmodel's FOV moves with the override's zoom: the offset is
	// managed's (ViewSetUpOut), the zoomed fov the one operand only this has.
	eye.fovViewmodel = eye.fov + flViewModelFovOffset;

	::partition->SuppressLists(PARTITION_ALL_CLIENT_EDICTS, true);

	AudioState_t audioState;
	audioState.m_Origin = eye.origin;
	audioState.m_Angles = eye.angles;
	audioState.m_bIsUnderwater = m_ViewerFacts.ViewerAudioIsUnderwater(eye.origin);
	engine->SetAudioState(audioState);

	SetMainView(eye.origin, eye.angles);
}

void ShellView_ComputeCamera(const Vector& vecOrigin, const QAngle& angles, ShellCamera_t& out)
{
	out.origin = vecOrigin;
	out.angles = angles;
	AngleVectors(angles, &out.forward, &out.right, &out.up);
	for (int i = 0; i < 3; ++i)
	{
		out.worldToView[0][i] = out.right[i];
		out.worldToView[1][i] = out.up[i];
		out.worldToView[2][i] = -out.forward[i];
		out.worldToView[3][i] = 0.0F;
	}
	out.worldToView[0][3] = -DotProduct(out.right, vecOrigin);
	out.worldToView[1][3] = -DotProduct(out.up, vecOrigin);
	out.worldToView[2][3] = DotProduct(out.forward, vecOrigin);
	out.worldToView[3][3] = 1.0F;
}

void CShellViewRender::SetMainView(const Vector& vecOrigin, const QAngle& angles)
{
	m_vecPrevMainOrigin = m_Main.origin;
	m_angPrevMainAngles = m_Main.angles;
	ShellView_ComputeCamera(vecOrigin, angles, m_Main);
}

void CShellViewRender::SetupCurrentView(const Vector& vecOrigin, const QAngle& angles, int nViewID, bool bEffects)
{
	ShellView_ComputeCamera(vecOrigin, angles, m_Current);
	m_nCurrentViewID = nViewID;
	m_bCanAccessCurrentView = true;
	if (!bEffects)
		return;

	float flScreenFadeMinSize, flScreenFadeMaxSize;
	GetScreenFadeDistances(&flScreenFadeMinSize, &flScreenFadeMaxSize);
	modelinfo->SetViewScreenFadeRange(flScreenFadeMinSize, flScreenFadeMaxSize);

	// INT_RENDERPARM_WRITE_DEPTH_TO_DESTALPHA is 10 (public/renderparm.h, which
	// client.so does not otherwise include).
	const int kWriteDepthToDestAlpha = 10;
	CMatRenderContextPtr pRenderContext(materials);
	pRenderContext->SetIntRenderingParameter(kWriteDepthToDestAlpha,
											 (nViewID == VIEW_MAIN || nViewID == VIEW_3DSKY) ? 1 : 0);
}

// The names fork TUs still call, answered from CShellViewRender.
IViewRender* view = NULL;

const Vector& MainViewOrigin()
{
	return ShellClientDll_ViewRender().m_Main.origin;
}

const QAngle& MainViewAngles()
{
	return ShellClientDll_ViewRender().m_Main.angles;
}

const Vector& MainViewForward()
{
	return ShellClientDll_ViewRender().m_Main.forward;
}

const Vector& MainViewRight()
{
	return ShellClientDll_ViewRender().m_Main.right;
}

const Vector& MainViewUp()
{
	return ShellClientDll_ViewRender().m_Main.up;
}

const VMatrix& MainWorldToViewMatrix()
{
	return ShellClientDll_ViewRender().m_Main.worldToView;
}

const Vector& PrevMainViewOrigin()
{
	return ShellClientDll_ViewRender().m_vecPrevMainOrigin;
}

const QAngle& PrevMainViewAngles()
{
	return ShellClientDll_ViewRender().m_angPrevMainAngles;
}

const Vector& CurrentViewOrigin()
{
	const CShellViewRender& viewRender = ShellClientDll_ViewRender();
	Assert(viewRender.m_bCanAccessCurrentView);
	return viewRender.m_Current.origin;
}

const QAngle& CurrentViewAngles()
{
	const CShellViewRender& viewRender = ShellClientDll_ViewRender();
	Assert(viewRender.m_bCanAccessCurrentView);
	return viewRender.m_Current.angles;
}

const Vector& CurrentViewForward()
{
	const CShellViewRender& viewRender = ShellClientDll_ViewRender();
	Assert(viewRender.m_bCanAccessCurrentView);
	return viewRender.m_Current.forward;
}

const Vector& CurrentViewRight()
{
	const CShellViewRender& viewRender = ShellClientDll_ViewRender();
	Assert(viewRender.m_bCanAccessCurrentView);
	return viewRender.m_Current.right;
}

const Vector& CurrentViewUp()
{
	const CShellViewRender& viewRender = ShellClientDll_ViewRender();
	Assert(viewRender.m_bCanAccessCurrentView);
	return viewRender.m_Current.up;
}

const VMatrix& CurrentWorldToViewMatrix()
{
	const CShellViewRender& viewRender = ShellClientDll_ViewRender();
	Assert(viewRender.m_bCanAccessCurrentView);
	return viewRender.m_Current.worldToView;
}

view_id_t CurrentViewID()
{
	return (view_id_t)ShellClientDll_ViewRender().m_nCurrentViewID;
}

bool DrawingShadowDepthView()
{
	return ShellClientDll_ViewRender().m_nCurrentViewID == VIEW_SHADOW_DEPTH_TEXTURE;
}

void SetupCurrentView(const Vector& vecOrigin, const QAngle& angles, view_id_t viewID)
{
	ShellClientDll_ViewRender().SetupCurrentView(vecOrigin, angles, viewID, true);
}

void FinishCurrentView()
{
	ShellClientDll_ViewRender().FinishCurrentView();
}

void AllowCurrentViewAccess(bool allow)
{
	ShellClientDll_ViewRender().AllowCurrentViewAccess(allow);
}

bool IsCurrentViewAccessAllowed()
{
	return ShellClientDll_ViewRender().m_bCanAccessCurrentView;
}

bool R_CullSphere(VPlane const* pPlanes, int nPlanes, Vector const* pCenter, float radius)
{
	for (int i = 0; i < nPlanes; i++)
	{
		if (pPlanes[i].DistTo(*pCenter) < -radius)
			return true;
	}
	return false;
}

// The convars fork TUs link against by object (`extern ConVar ...`); the rest
// are managed declarations.
ConVar mat_wireframe("mat_wireframe", "0", FCVAR_CHEAT);
const ConVar* sv_cheats = NULL;
ConVar r_DrawDetailProps("r_DrawDetailProps", "1", FCVAR_NONE, "0=Off, 1=Normal, 2=Wireframe");
ConVar cl_drawshadowtexture("cl_drawshadowtexture", "0", FCVAR_CHEAT);

// cl_software_cursor: its change hands vgui the larger software cursor.
static void SoftwareCursorChanged(IConVar* pVar, const char* pOldValue, float flOldValue)
{
	vgui::surface()->SetSoftwareCursor(((ConVar*)pVar)->GetBool());
}

static ConVar cl_software_cursor("cl_software_cursor", "0", FCVAR_ARCHIVE,
								 "Switches the game to use a larger software cursor instead of the normal OS cursor",
								 SoftwareCursorChanged);

// getpos and spec_pos: the main view, or ("2") the player's own position.
static void ViewPosition(const CCommand& args, Vector& vecOrigin, QAngle& angles, bool& bPlayer)
{
	vecOrigin = MainViewOrigin();
	angles = MainViewAngles();
	bPlayer = args.ArgC() == 2 && atoi(args[1]) == 2;
	if (bPlayer)
		SourceSharp_ViewerAbsPosition(&vecOrigin, &angles);
}

CON_COMMAND(spec_pos, "dump position and angles to the console")
{
	Vector o;
	QAngle a;
	bool bPlayer;
	ViewPosition(args, o, a, bPlayer);
	Warning("spec_goto %.1f %.1f %.1f %.1f %.1f\n", o.x, o.y, o.z, a.x, a.y);
}

CON_COMMAND(getpos, "dump position and angles to the console")
{
	Vector o;
	QAngle a;
	bool bPlayer;
	ViewPosition(args, o, a, bPlayer);
	Warning("%s %f %f %f;", bPlayer ? "setpos_exact" : "setpos", o.x, o.y, o.z);
	Warning("%s %f %f %f\n", bPlayer ? "setang_exact" : "setang", a.x, a.y, a.z);
}

// centerview: pitch drift, which is not shipped.
static void CenterView()
{
	view->StartPitchDrift();
}

static ConCommand centerview("centerview", CenterView);

// r_cheapwaterstart / r_cheapwaterend: set one cheap-water distance, or print it.
static void CheapWaterCommand(const CCommand& args, bool bStart)
{
	if (args.ArgC() == 2)
	{
		const float flDistance = atof(args[1]);
		if (bStart)
			view->SetCheapWaterStartDistance(flDistance);
		else
			view->SetCheapWaterEndDistance(flDistance);
		return;
	}
	float flStart, flEnd;
	view->GetWaterLODParams(flStart, flEnd);
	Warning("%s: %f\n", bStart ? "r_cheapwaterstart" : "r_cheapwaterend", bStart ? flStart : flEnd);
}

CON_COMMAND(r_cheapwaterstart, "")
{
	CheapWaterCommand(args, true);
}

CON_COMMAND(r_cheapwaterend, "")
{
	CheapWaterCommand(args, false);
}

// r_screenoverlay: a material drawn over the frame; "off" or "0" (or a
// material that does not load) takes it away; no argument prints it.
CON_COMMAND_F(r_screenoverlay, "Draw specified material as an overlay", FCVAR_CHEAT | FCVAR_SERVER_CAN_EXECUTE)
{
	if (args.ArgC() != 2)
	{
		IMaterial* pShown = view->GetScreenOverlayMaterial();
		Warning("r_screenoverlay: %s\n", pShown ? pShown->GetName() : "off");
		return;
	}
	IMaterial* pMaterial = NULL;
	if (Q_stricmp("off", args[1]) != 0 && Q_stricmp("0", args[1]) != 0)
	{
		pMaterial = materials->FindMaterial(args[1], TEXTURE_GROUP_OTHER, false);
		if (IsErrorMaterial(pMaterial))
			pMaterial = NULL;
	}
	view->SetScreenOverlayMaterial(pMaterial);
}

// The post-processing materials stock precached for DX9 and up; when they are
// precached is managed's.
namespace
{
const char* const kPostProcessingMaterials[] = {
	"dev/blurfiltery_and_add_nohdr",
	"dev/blurfilterx",
	"dev/blurfilterx_nohdr",
	"dev/blurfiltery",
	"dev/blurfiltery_nohdr",
	"dev/bloomadd",
	"dev/downsample",
	"dev/downsample_non_hdr",
	"dev/no_pixel_write",
	"dev/lumcompare",
	"dev/floattoscreen_combine",
	"dev/copyfullframefb_vanilla",
	"dev/copyfullframefb",
	"dev/engine_post",
	"dev/motion_blur",
	"dev/upscale",
};

class CPostProcessingPrecache : public CClientEffect
{
public:
	void Cache(bool bPrecache) override
	{
		if (engine->GetDXSupportLevel() < 90)
			return;
		for (int i = 0; i < (int)ARRAYSIZE(kPostProcessingMaterials); i++)
			ReferenceMaterial(kPostProcessingMaterials[i], bPrecache);
	}
};

CPostProcessingPrecache g_PostProcessingPrecache;
} // namespace

// The screen overlay by name, the condition arms' half of r_screenoverlay: the
// material out of the client-effects group as stock's condition code asks it
// (tf_player_shared.cpp:3602), an empty name or one that does not load
// (IsErrorMaterial) leaving the standing overlay, stock's guard. The read
// copies the standing overlay's name, empty when there is none.
int32_t SourceSharp_ScreenOverlaySet(const char* pszName)
{
	if (!pszName)
		return 0;

	IMaterial* pMaterial = NULL;
	if (pszName[0] != '\0')
	{
		if (!materials)
			return 0;

		pMaterial = materials->FindMaterial(pszName, TEXTURE_GROUP_CLIENT_EFFECTS, false);
		if (IsErrorMaterial(pMaterial))
			return 0;
	}

	view->SetScreenOverlayMaterial(pMaterial);
	return 1;
}

int32_t SourceSharp_ScreenOverlayGet(char* pOutName, int32_t nNameBytes)
{
	if (!pOutName || nNameBytes <= 0)
		return 0;

	IMaterial* pMaterial = view ? view->GetScreenOverlayMaterial() : NULL;
	if (!pMaterial)
	{
		pOutName[0] = '\0';
		return 1;
	}

	Q_strncpy(pOutName, pMaterial->GetName(), nNameBytes);
	return 1;
}
