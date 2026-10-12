//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the last tenants of evicted client files ===========//
// SS_REASON g  leaf math over mdl/bsp data
//
// shell_entity_orphans.cpp did this for the server's map-entity files. This is
// the same move for the client: files whose every registration lost its server
// send table (checked against server.so's ServerClassInit instantiations, not
// against source), each held in the build by free functions that callers
// elsewhere still name.
//
//   FormatViewModelAttachment        c_baseviewmodel.cpp, wanted by
//                                    particle_property.cpp and
//                                    particles_localspace.cpp. See below: it is
//                                    the one tenant here that is NOT a stub.
//
// Found by removing the files and reading the linker — the two type-shaped
// refusals from the same link (steamshare.cpp's dynamic_cast and
// basemodelpanel.cpp's `new C_SceneEntity`) could not be lifted as functions
// and carry an #ifndef SOURCESHARP at the call site instead.
//
// FIVE TENANTS HAVE LEFT, and the rule that let them in is the rule that let
// them out: their callers were evicted after they were, so the linker stopped
// naming them.
//
//   CommentaryModeShouldSwallowInput  was wanted by C_BasePlayer::CreateMove
//   NPC_IsImportantNPC                by C_BaseAnimating::BecomeRagdollOnClient
//   UseHWMorphVCDs                    by c_baseflex.cpp's GetHWMExpressionFileName
//   C_BaseAnimating::LookupAttachment by c_rope.cpp's CreateFromKeyValues; the
//                                    file left with the C2 rope port, and the
//                                    model-cables arm it held is C#'s
//                                    (RopeSimulation.CreateFromKeyValues)
//   IsInCommentaryMode               by clientmode_shared.cpp; the file left with
//                                    the C3 client mode (lane ui), and no
//                                    commentary node could exist anyway
//
// None of those callers is on the client link line any more. Measured
// with `tools/shim-orphan-reach shell_client_orphans.o` over all 253 client
// objects the ninja graph names -- `nm -C -u` against `nm -C --defined-only`
// with a T/B/D filter, so the weak inline emissions this file picks up from
// cbase.h are not counted as its own definitions. The tool REFUSES when an
// object on the graph is missing from disk, because an unbuilt tree answers
// "nobody imports anything", which reads exactly like "delete this file".
//
// Every survivor is a free function or a non-virtual member of a class with no
// vtable on the link line, so none can be reached through a vtable and the
// reading is exact rather than a floor.
//
//=============================================================================//

#include "cbase.h"
#include "view.h"
#include "view_shared.h"
#include "iviewrender.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

class C_BasePlayer;
struct cplane_t;

// c_func_reflective_glass.cpp scanned its linked list of glass entities every
// frame for viewrender.cpp; the list has been empty since the server table
// left, so this is the answer it was already giving.
bool IsReflectiveGlassInView( const CViewSetup& view, cplane_t &plane )
{
	return false;
}

// env_detail_controller.cpp held a static the constructor filled and this
// returned; the class is C# on both sides now (Phase 4). NULL makes
// CDetailObjectSystem::LevelInitPostEntity restore cl_detailfade and
// cl_detaildist to the player's defaults each level — which is exactly the
// baseline the managed client half (ClientWorldState.cs) clamps down from
// when the entity's blob arrives.
class C_EnvDetailController;

C_EnvDetailController * GetDetailController()
{
	return NULL;
}

//-----------------------------------------------------------------------------
// THE ONE TENANT THAT IS NOT A STUB.
//
// c_baseviewmodel.cpp's FREE function -- nothing to do with C_BaseViewModel, and
// the sole reason client_csharp.vpc's earlier cliff pass refused to evict that
// file at all ("evicting the file means LIFTING it into a shim -- keeping the
// code and moving it -- which buys nothing the eviction was for", the comment
// beside the REFUSED IN THE SAME PASS block). Under the alias that arithmetic
// inverts: leaving the two viewmodel TUs in the build costs 37 member-access
// sites that each need a hand port onto IGameEntity, against the ~45 lines
// below. So the lift is now the cheap side and the refusal is superseded.
//
// THE BODY IS STOCK'S, character for character. It converts between viewmodel-
// FOV space and world space out of view->GetPlayerViewSetup(), and it is live:
// particle_property.cpp:628 calls it every frame a particle system is attached
// to a viewmodel, and the managed executor's attachment space
// (shell_managed_particles.cpp, CLocalSpaceEmitter's FLE_VIEWMODEL draw, which
// particles_localspace.cpp:97 made until lane c5-particles) for a view-model
// muzzle flash. (c_vguiscreen.cpp:132 is the third stock caller and is not in this
// build.) Each declares it `extern` locally rather than through a header, which
// is why moving the definition needs no caller edited.
//
// NOT A STUB, and the difference matters: every other function in this file
// answers for a thing that cannot exist. The managed ss_viewmodel DOES exist --
// a C_StandaloneEntity with m_bViewModel set -- so a particle attached to it is
// a live path that would silently draw in the wrong space if this returned
// early. Stock's arithmetic, kept.
//-----------------------------------------------------------------------------
void FormatViewModelAttachment( Vector &vOrigin, bool bInverse )
{
	// Presumably, SetUpView has been called so we know our FOV and render origin.
	const CViewSetup *pViewSetup = view->GetPlayerViewSetup();

	float worldx = tan( pViewSetup->fov * M_PI/360.0 );
	float viewx = tan( pViewSetup->fovViewmodel * M_PI/360.0 );

	// aspect ratio cancels out, so only need one factor
	// the difference between the screen coordinates of the 2 systems is the ratio
	// of the coefficients of the projection matrices (tan (fov/2) is that coefficient)
	// NOTE: viewx was coming in as 0 when folks set their viewmodel_fov to 0 and show their weapon.
	float factorX = viewx ? ( worldx / viewx ) : 0.0f;
	float factorY = factorX;

	// Get the coordinates in the viewer's space.
	Vector tmp = vOrigin - pViewSetup->origin;
	Vector vTransformed( MainViewRight().Dot( tmp ), MainViewUp().Dot( tmp ), MainViewForward().Dot( tmp ) );

	// Now squash X and Y.
	if ( bInverse )
	{
		if ( factorX != 0 && factorY != 0 )
		{
			vTransformed.x /= factorX;
			vTransformed.y /= factorY;
		}
		else
		{
			vTransformed.x = 0.0f;
			vTransformed.y = 0.0f;
		}
	}
	else
	{
		vTransformed.x *= factorX;
		vTransformed.y *= factorY;
	}

	// Transform back to world space.
	Vector vOut = (MainViewRight() * vTransformed.x) + (MainViewUp() * vTransformed.y) + (MainViewForward() * vTransformed.z);
	vOrigin = pViewSetup->origin + vOut;
}
