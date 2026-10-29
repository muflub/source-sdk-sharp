// CServerDLLSharedAppSystems: the two DLLs the engine loads as app systems for the game,
// as stock's gameinterface.cpp names them.

#include "gtest/gtest.h"

#include "shell_sharedappsystems.h"

#include <string>

TEST( ServerDllSharedAppSystems, TheSoundEmitterAndSceneFileCacheAreAnsweredInStocksOrder )
{
	CServerDLLSharedAppSystems systems;

	ASSERT_EQ( 2, systems.Count() );
	EXPECT_EQ( std::string( "soundemittersystem" DLL_EXT_STRING ), systems.GetDllName( 0 ) );
	EXPECT_EQ( std::string( "VSoundEmitter002" ), systems.GetInterfaceName( 0 ) );
	EXPECT_EQ( std::string( "scenefilecache" DLL_EXT_STRING ), systems.GetDllName( 1 ) );
	EXPECT_EQ( std::string( "SceneFileCache002" ), systems.GetInterfaceName( 1 ) );
}
