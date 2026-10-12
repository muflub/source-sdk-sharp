# C7 step 3: viewrender.cpp, view.cpp and viewdebug.cpp leave client.so

**Reviewed and approved by the user 2026-10-12** ("Plan is good, commit").
Phase 5, the eviction, may proceed once its preconditions hold.

This plan was drafted 2026-10-12 on client-r-c7 d3f9b96f2 (client-novalve
d4c33a7a4 plus two C7 commits) and revised with the integrator as it ran:
phase 3 became an installed, delegating wrapper; the live stock path left one
phase early, behind a frozen-frame gate (phase 3a).

The user ruled on 2026-10-12: "freeze recordings". The stock path leaves
outright. `cs_cl_render_plan 0` goes, and there is no stock build option. The
five render-stock frames, the 20 post-stock frames and the synthetic facts are
the reference.

## What is still imported

Measured with `nm -C -u` over every client_sourcesharp object except the three
TUs. These are floors: virtual calls through `view` (IViewRender) do not appear
in nm.

| name | importers |
|---|---|
| `CurrentViewOrigin` / `Forward` / `Up` / `Right` / `Angles` / `ID`, `CurrentWorldToViewMatrix` | 13 shell TUs and c_pixel_visibility.cpp |
| `MainViewOrigin` / `Forward` / `Up` / `Right` / `Angles` | 8 shell TUs |
| `view` (IViewRender) | 12 shell TUs; the methods called are listed below |
| `SetupCurrentView`, `FinishCurrentView`, `AllowCurrentViewAccess`, `DrawingShadowDepthView` | the executor; particlemgr.cpp |
| `CRendering3dView`: Setup, BuildWorldRenderLists, BuildRenderableRenderLists, PruneWorldListInfo, ReleaseLists, UpdateRenderablesOpacity, DrawOpaqueRenderables, DrawTranslucentRenderables, DrawNoZBufferTranslucentRenderables, DrawTranslucentWorldInLeaves, GetDrawFlags, and the vtable | the executor's CPlanView, which derives from it |
| `g_DefaultViewRender` | shell_clientdll.cpp |
| `v_viewmodel_fov` | shell_client.cpp |
| `CDebugViewRender`, `g_bDumpRenderTargets`, `DumpTGAofRenderTarget`, `DoEnginePostProcessing`, `DoImageSpaceMotionBlur` | viewrender.cpp only (they go with it) |

The `view->` methods called outside viewrender.cpp and view.cpp:
- SetCurrentlyDrawingEntity and GetCurrentlyDrawingEntity
- GetFrustum
- GetWaterLODParams
- GetPlayerViewSetup and GetViewSetup
- Set/GetScreenOverlayMaterial
- SetCheapWater{Start,End}Distance
- RenderView and Render
- GetDrawFlags
- WriteSaveGameScreenshot(OfSize)
- UpdateShadowDepthTexture
- Start/StopPitchDrift
- Init, Shutdown, LevelInit, LevelShutdown, OnRenderStart
- GetScreenFadeDistances
- FreezeFrame
- BuildWorldListsNumber

## Constraints

- **Nothing can be added beside the three TUs under their names first.** They
  define `g_DefaultViewRender`, the current-view functions and
  CRendering3dView, so a replacement under the same names would collide.
  Preparatory commits build the replacements under other names (the wrapper
  is CShellViewRender; `view` is only a pointer, so it can be ours early). The eviction commit swaps the names
  and removes the TUs in one step, as the viewpostprocess.cpp and
  view_effects.cpp evictions did.
- **The fork stays untouched.** The replacements must reach the current-view
  functions and `view` exactly as the fork's remaining callers expect them:
  c_pixel_visibility.cpp, particlemgr.cpp, clientshadowmgr.cpp and the
  engine's callbacks into the client.

## Phases

Revised 2026-10-12 with the integrator: phase 2's CShellViewRender is installed
as a delegating wrapper and moved off stock one method at a time, each checked
live, instead of being built uninstalled and switched in one step.

1. **The executor stops deriving from CRendering3dView.** Done (f724a3133).
   - CPlanView is the executor's own class; its list builds call the engine
     and leaf system directly with the plan's inputs.
   - The view programs are the only draw path; a declined program draws
     nothing and is counted (ViewProgramsDeclined, 0 at every gate pose).
2. **What stock's RenderView still drew, planned** (done):
   - the freeze frame (81102349a: FreezeFrameDraw / FreezeFrameCapture);
   - the shadow-depth view (c7035e651: UpdateShadowDepthTexture's
     CShadowDepthView, RenderShadowDepthPlan; no flashlight shadow exists in
     this build, so facts only);
   - view.cpp's frame decisions (c779a6d37, ab90bb63e): ViewFrame decides
     SetUpViews' planes and FOV and Render's eye, clear and draw flags and the
     level overview. Until the eviction stock's Render is the live oracle:
     every plan frame's RenderView and SetUpViews are compared field by field
     (/render's viewDecisions and viewDecisionMismatches; the render-plan
     gate steps require 0 mismatches).
3. **CShellViewRender, our IViewRender, installed as a delegating wrapper.**
   - `view` is CShellViewRender from client init. Every method not yet
     ported forwards to stock's g_DefaultViewRender, which stays linked
     until the eviction.
   - The wrapper counts its forwarding methods and reports the count and
     their names (/render's viewRenderForwards; cs_cl_viewrender_forwards).
     Each port commit lowers it; the eviction requires 0.
   - It must survive cs_cl_reload and a map reload; both are run live after
     installing.
   - One commit per method or group, each with its live check (render,
     render-plan, view-camera and the steps that reach the method), in this
     order: Render and SetUpViews (carrying out ViewFrame's decisions; the
     demo free camera managed, reading the input service's key state); the
     state accessors (view setups, cheap water, screen overlay, currently
     drawing entity, fade distances, frustum, draw flags, vis, list
     numbers); RenderView and UpdateShadowDepthTexture (the plan directly;
     the slot patches go); the save-game screenshot (its FOV decided by
     managed); the freeze frame (state managed, test_freezeframe a managed
     command); Init, LevelInit, LevelShutdown, Shutdown; pitch drift.
   - Progress (2026-10-12): 35 forwards at f444eea11, 18 at 1039317ae
     (Render and DisableVis; four accessors; pitch drift and replay; cheap
     water and the screen overlay, mirrored into stock's object while the
     stock path reads its own members).
   - **Ordering constraint:** OnRenderStart (SetUpViews) and
     GetPlayerViewSetup cannot move before the eviction. view.cpp's
     MainView* readers answer static globals only stock's SetUpViews writes,
     so those two port with phase 4's current-view state, in the eviction
     commit. Init, LevelInit, LevelShutdown and Shutdown go with them.
3a. **The live stock path leaves before the render-view group** (integrator,
   2026-10-12). Stock's RenderView reads its own members directly
   (m_CurrentView, the list numbers, the active renderer and frustum, the
   currently drawing entity), so porting the render-view group while the
   stock path still ran would have needed every one mirrored both ways --
   throwaway code kept only for an oracle the user had already ruled frozen.
   - **The gate replacement lands first, as its own commit** (c57272b85):
     render/compare?mutate=frozen:PATH holds the next plan frame to a frozen
     stock frame, each frame's entities numbered by first appearance (two
     sessions number them apart). render-plan runs it at 1280x720 at the
     stand and both poses against dm_lockdown-pose1/2/3.txt with the
     input-noise retry rules (scenes differing by a renderable wait for item
     respawns), and the DrawLights control parts them at pose 1. Proven with
     render-plan --passes 3 before anything stock was removed.
   - **Then the removal, its own commit:** cs_cl_render_plan, the stock arm
     of View_Render, the same-run compare and /render's stock times go;
     render-plan-sandbox becomes plan-only invariants (the plan's frame, not
     declined, opening with the main view, every pushed view popped). The
     view-decision, SetUpViews and accessor oracles keep running (their
     comparisons counted on /render as viewOracleChecks). Stock's RenderView
     still draws a frame the planner declines (reflective glass, never in
     view in this build) until the eviction.
   - Done 2026-10-12 (the commit after 477d6511b): live, render-plan held
     the three dm_lockdown poses to their frozen frames (172, 150 and 213
     calls) with the control parting at DrawLights, render-plan-sandbox's
     frames held their invariants, and the oracles ran (4398 comparisons at
     dm_lockdown, 228 at ss_sandbox, 0 differed).
   - The render-view group then ports with no mirrors: RenderView,
     GetDrawFlags, GetFrustum, GetViewSetup, BuildWorldListsNumber, the
     currently drawing entity, UpdateShadowDepthTexture, the save-game
     screenshot and FreezeFrame; SS_MIRROR on cheap water and the overlay
     goes with the stock path's readers.
4. **The current-view state** (SetupCurrentView, the CurrentView* and
   MainView* readers, the world-to-view matrices, AllowCurrentViewAccess),
   built under internal names and, until the eviction, computed beside
   stock's and compared each frame the same way as the view decisions. It is
   native state 20 native readers use in-process, (d)-like, and stays native
   until those readers go.
   - Done 2026-10-12: the view state is CShellViewRender's, computed beside
     stock's and compared bit for bit each frame (0 differed live) until the
     eviction.
5. **The eviction**, once the forward count is 0. Done 2026-10-12: the six
   methods the ordering constraint held (Init, LevelInit, LevelShutdown,
   Shutdown, OnRenderStart, GetPlayerViewSetup) ported in the eviction commit
   itself, with the view state's stock names; the oracles went with stock.
   - `g_DefaultViewRender`, `v_viewmodel_fov` and the current-view functions
     become ours; viewrender.cpp, view.cpp and viewdebug.cpp leave, and their
     convars become managed declarations.
   - The stock oracles of phases 2 and 4 go (cs_cl_render_plan, the stock
     arm and the same-run compare already left at phase 3a; the gate's
     render-plan steps already hold plan frames to the frozen recordings and
     the sandbox to plan-only invariants).
   - shell_viewpostprocess_client.cpp and shell_vieweffects_client.cpp lose
     their stock-path importers and shrink or go. Done 2026-10-12: both went;
     their surviving importers call managed or the names' writer directly
     (docs/client_evictions.md).
   - Ledgers move as before: sdk-links, client_evictions, census, globals,
     shim (x).

## Open questions for review

- **The live compare's replacement** (answered by the integrator): the
  dm_lockdown render-plan step holds plan frames to the frozen frames at the
  same poses with the input-noise retry rules, a difference with identical
  scene inputs failing; the sandbox step checks plan-only invariants (views
  balanced, no declined program, the crossing budget). If dm_lockdown proves
  noisy too, numbers come back before it drops to plan-only.
- **viewdebug.cpp's stock-path entry points** go with viewrender.cpp. The plan
  path already draws the overlays (kinds 111-121); docs/todo.md queues their
  later move to Debug.Api.
- **Reflective glass** is the one frame the plan still hands back:
  IsReflectiveGlassInView answers false in this build (no client glass list),
  so after the eviction that arm cannot be reached. Its views port when the
  client glass does (docs/todo.md).

## Size

- Phases 1 and 2: done.
- Phase 3: the wrapper, then about a dozen method commits, two days.
- Phase 4: small, with its oracle.
- Phase 5: one large commit and its ledgers, a day.
