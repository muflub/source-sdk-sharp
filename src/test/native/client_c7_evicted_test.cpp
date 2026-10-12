// plan_client_novalve C7 step 2: the view TUs that left client.so, and the
// names that stayed in our own files. Read from the farm, which holds only
// what client.so compiles (tests run with cwd farm/<config>/src); a file that
// stays proves the root resolved.

#include "gtest/gtest.h"

#include <fstream>
#include <sstream>
#include <string>
#include <algorithm>
#include <regex>
#include <vector>

namespace
{
std::string Read( const char *pszRel )
{
	std::ifstream f( pszRel );
	if ( !f.good() )
		return std::string();
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

const char kViewScene[] = "sourcesharp/game/client/shell_view_scene_client.cpp";
} // namespace

TEST( ClientC7Evicted, TheRootResolves )
{
	ASSERT_FALSE( Read( kViewScene ).empty() ) << "the working directory is not farm/<config>/src";
}

// view_scene.cpp's four imported names are shell_view_scene_client.cpp's.
TEST( ClientC7Evicted, ViewSceneLeftAndItsFourNamesStayed )
{
	EXPECT_TRUE( Read( "game/client/view_scene.cpp" ).empty() ) << "view_scene.cpp is back in the farm";
	const std::string ours = Read( kViewScene );
	ASSERT_FALSE( ours.empty() );
	EXPECT_NE( std::string::npos, ours.find( "int ScreenTransform( const Vector &point, Vector &screen )" ) );
	EXPECT_NE( std::string::npos, ours.find( "void UpdateFullScreenDepthTexture( void )" ) );
	EXPECT_NE( std::string::npos, ours.find( "ConVar r_updaterefracttexture(" ) );
	EXPECT_NE( std::string::npos, ours.find( "int g_viewscene_refractUpdateFrame = 0;" ) );
}

// C7 post-processing: viewpostprocess.cpp left, and its names' stand-in,
// shell_viewpostprocess_client.cpp, went once viewrender.cpp did. The tonemap
// controller's globals are defined by their writer, ResetToneMapping's native
// half is the level init's own call, and the proxies are managed (the
// trampoline registers MotionBlur and engine_post).
TEST( ClientC7Evicted, ViewPostProcessLeftAndItsStandInWent )
{
	EXPECT_TRUE( Read( "game/client/viewpostprocess.cpp" ).empty() ) << "viewpostprocess.cpp is back in the farm";
	EXPECT_TRUE( Read( "sourcesharp/game/client/shell_viewpostprocess_client.cpp" ).empty() )
		<< "shell_viewpostprocess_client.cpp is back in the farm";
	const std::string world = Read( "sourcesharp/game/client/shell_worldrender.cpp" );
	ASSERT_FALSE( world.empty() );
	EXPECT_NE( std::string::npos, world.find( "float g_flCustomBloomScaleMinimum = 0.0f;" ) );
	const std::string dll = Read( "sourcesharp/game/client/shell_clientdll.cpp" );
	ASSERT_FALSE( dll.empty() );
	EXPECT_NE( std::string::npos, dll.find( "pRenderContext->ResetToneMappingScale( 1.0f );" ) );
	const std::string proxies = Read( "sourcesharp/game/client/shell_material_proxy_client.cpp" );
	ASSERT_FALSE( proxies.empty() );
	EXPECT_NE( std::string::npos, proxies.find( "X( 44, MotionBlur ) X( 45, engine_post )" ) );
}

// C7: view_effects.cpp left, and `vieweffects`, its stand-in, went with
// shell_vieweffects_client.cpp: the callers ask managed's ViewEffects directly.
TEST( ClientC7Evicted, ViewEffectsLeftAndItsCallersAskManaged )
{
	EXPECT_TRUE( Read( "game/client/view_effects.cpp" ).empty() ) << "view_effects.cpp is back in the farm";
	EXPECT_TRUE( Read( "sourcesharp/game/client/shell_vieweffects_client.cpp" ).empty() )
		<< "shell_vieweffects_client.cpp is back in the farm";
	const std::string client = Read( "sourcesharp/game/client/shell_client.cpp" );
	const std::string dll = Read( "sourcesharp/game/client/shell_clientdll.cpp" );
	const std::string exec = Read( "sourcesharp/game/client/shell_view_executor_client.cpp" );
	ASSERT_FALSE( client.empty() || dll.empty() || exec.empty() );
	EXPECT_NE( std::string::npos, client.find( "p->ViewEffectsCalcShake( gpGlobals->curtime, gpGlobals->frametime, out );" ) );
	EXPECT_NE( std::string::npos, exec.find( "p->ViewEffectsFadeParams( gpGlobals->curtime, fade );" ) );
	EXPECT_NE( std::string::npos, dll.find( "p->ViewEffectsFade( sf.duration, sf.holdTime, sf.fadeFlags, sf.r, sf.g, sf.b, sf.a, gpGlobals->curtime );" ) );
	EXPECT_NE( std::string::npos, dll.find( "p->ViewEffectsReset( 0 );" ) );
	for ( const std::string *pText : { &client, &dll, &exec } )
		EXPECT_EQ( std::string::npos, pText->find( "vieweffects->" ) ) << "a caller still goes through vieweffects";
}

// C7 step 3: view.cpp, viewrender.cpp and viewdebug.cpp left, and the names
// they gave client.so that other TUs still call are shell_viewrender_client.cpp's.
TEST( ClientC7Evicted, TheViewTusLeftAndTheirImportedNamesStayed )
{
	EXPECT_TRUE( Read( "game/client/view.cpp" ).empty() ) << "view.cpp is back in the farm";
	EXPECT_TRUE( Read( "game/client/viewrender.cpp" ).empty() ) << "viewrender.cpp is back in the farm";
	EXPECT_TRUE( Read( "game/client/viewdebug.cpp" ).empty() ) << "viewdebug.cpp is back in the farm";
	const std::string ours = Read( "sourcesharp/game/client/shell_viewrender_client.cpp" );
	ASSERT_FALSE( ours.empty() );
	const char *kNames[] =
	{
		"IViewRender *view = NULL;",
		"const Vector &MainViewOrigin()",
		"const Vector &PrevMainViewOrigin()",
		"const VMatrix &MainWorldToViewMatrix()",
		"const Vector &CurrentViewForward()",
		"const VMatrix &CurrentWorldToViewMatrix()",
		"view_id_t CurrentViewID()",
		"bool DrawingShadowDepthView( void )",
		"void SetupCurrentView( const Vector &vecOrigin, const QAngle &angles, view_id_t viewID )",
		"void FinishCurrentView()",
		"void AllowCurrentViewAccess( bool allow )",
		"bool IsCurrentViewAccessAllowed()",
		"bool R_CullSphere( VPlane const *pPlanes, int nPlanes, Vector const *pCenter, float radius )",
		"ConVar mat_wireframe( \"mat_wireframe\"",
		"const ConVar *sv_cheats = NULL;",
		"ConVar r_DrawDetailProps( \"r_DrawDetailProps\"",
		"ConVar cl_drawshadowtexture( \"cl_drawshadowtexture\"",
	};
	for ( const char *pszName : kNames )
		EXPECT_NE( std::string::npos, ours.find( pszName ) ) << "shell_viewrender_client.cpp no longer defines " << pszName;
}

// Nothing is stock's any more: no object to forward to, no forwarding.
TEST( ClientC7Evicted, TheClientsIViewRenderForwardsNothing )
{
	const std::string ours = Read( "sourcesharp/game/client/shell_viewrender_client.cpp" );
	ASSERT_FALSE( ours.empty() );
	EXPECT_NE( std::string::npos, ours.find( "class CShellViewRender final : public IViewRender" ) );
	EXPECT_EQ( std::string::npos, ours.find( "g_DefaultViewRender" ) );
	EXPECT_EQ( std::string::npos, ours.find( "SS_FORWARD" ) );
}
