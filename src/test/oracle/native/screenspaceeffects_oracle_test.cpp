//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE SCREEN-SPACE EFFECTS ORACLE (C7, ScreenSpaceEffects.cpp): stock's
// CScreenSpaceEffectManager -- the registration list's order (every
// ADD_SCREENSPACE_EFFECT pushes onto the head), InitScreenSpaceEffects (the
// -filmgrain parameter, then each effect's enabled flag kept across its Init),
// ShutdownScreenSpaceEffects, GetScreenSpaceEffect's case-blind lookup and its
// warning, SetScreenSpaceEffectParams / Enable / Disable by name and by
// effect, DisableAllScreenSpaceEffects and RenderEffects -- and the one effect
// the file registers, CExampleEffect, run offline from the PRISTINE
// game/client/ScreenSpaceEffects.cpp and written down as the golden
// src/test/client/Fixtures/screenspaceeffects-stock.tsv.
//
// THE SUBJECT IS INCLUDED, NOT LINKED, so its registration and the harness's
// recording effects are constructed in one translation unit in declaration
// order: two before it (alpha, and beta which starts enabled), CExampleEffect
// in it, and two after it (filmgrain, and a registration with no effect).
// sse_stub/sse_harness.h says what is stock and what is stand-in.
//
// THE GOLDEN CARRIES ITS STIMULUS: each step is an `S` row, so the managed
// replay (ScreenSpaceEffectManagerOracleTests) drives the same calls and holds
// every row after it. Regenerating the golden to make this test green deletes
// the check: it changes only when the script does (touch
// screenspaceeffects_golden.regen beside this file, run, delete the marker, and
// say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>

#include "gtest/gtest.h"

#include "sse_harness.h"
#include "tier0/icommandline.h"
#include "ScreenSpaceEffects.h"

IMaterialSystem *materials = NULL;

namespace
{
std::string g_Log;

void LogLine( const char *pszLine )
{
	g_Log += pszLine;
	g_Log += '\n';
}
} // namespace

void SseLog( const char *pFmt, ... )
{
	char sz[1024];
	va_list ap;
	va_start( ap, pFmt );
	vsnprintf( sz, sizeof( sz ), pFmt, ap );
	va_end( ap );
	LogLine( sz );
}

void SseWarning( const char *pFmt, ... )
{
	char sz[1024];
	va_list ap;
	va_start( ap, pFmt );
	vsnprintf( sz, sizeof( sz ), pFmt, ap );
	va_end( ap );
	// One row per line the subject warns with, its newline dropped.
	size_t n = strlen( sz );
	if ( n && sz[n - 1] == '\n' )
		sz[n - 1] = 0;
	SseLog( "W\t%s", sz );
}

void UpdateScreenEffectTexture( int textureIndex, int x, int y, int w, int h, bool, Rect_t * )
{
	SseLog( "UpdateScreenEffectTexture\t%d\t%d\t%d\t%d\t%d", textureIndex, x, y, w, h );
}

ITexture *GetFullFrameFrameBufferTexture( int textureIndex )
{
	SseLog( "GetFullFrameFrameBufferTexture\t%d", textureIndex );
	return NULL;
}

//-----------------------------------------------------------------------------
// The recording effects: every call on them is a row.
//-----------------------------------------------------------------------------
class CRecordingEffect : public IScreenSpaceEffect
{
public:
	CRecordingEffect( const char *pszName, bool bStartEnabled )
		: m_pszName( pszName )
		, m_bEnabled( bStartEnabled )
	{
	}

	void Init() override { SseLog( "E\t%s\tInit", m_pszName ); }

	void Shutdown() override { SseLog( "E\t%s\tShutdown", m_pszName ); }

	void SetParameters( KeyValues *params ) override
	{
		SseLog( "E\t%s\tSetParameters\t%s\t%s", m_pszName, params ? params->GetName() : "(null)",
				params ? params->GetString( "value", "(none)" ) : "(null)" );
	}

	void Render( int x, int y, int w, int h ) override
	{
		SseLog( "E\t%s\tRender\t%d\t%d\t%d\t%d", m_pszName, x, y, w, h );
	}

	void Enable( bool bEnable ) override
	{
		SseLog( "E\t%s\tEnable\t%d", m_pszName, bEnable ? 1 : 0 );
		m_bEnabled = bEnable;
	}

	bool IsEnabled() override
	{
		SseLog( "E\t%s\tIsEnabled\t%d", m_pszName, m_bEnabled ? 1 : 0 );
		return m_bEnabled;
	}

	const char *m_pszName;
	bool m_bEnabled;
};

// Two registered before the subject...
CRecordingEffect alpha_effect( "alpha", false );
CScreenSpaceEffectRegistration alpha_reg( "alpha", &alpha_effect );
CRecordingEffect beta_effect( "beta", true );
CScreenSpaceEffectRegistration beta_reg( "beta", &beta_effect );

// ...the subject, whose ADD_SCREENSPACE_EFFECT registers exampleeffect here...
#include "ScreenSpaceEffects.cpp"

// ...and two after it: the effect -filmgrain names, and a registration with no effect.
CRecordingEffect filmgrain_effect( "filmgrain", false );
CScreenSpaceEffectRegistration filmgrain_reg( "filmgrain", &filmgrain_effect );
CScreenSpaceEffectRegistration hollow_reg( "hollow", NULL );

namespace
{

// The effect a step names, as the harness holds it (never through the
// subject's lookup); "-" is no effect.
IScreenSpaceEffect *EffectNamed( const std::string &name )
{
	if ( name == "alpha" )
		return &alpha_effect;
	if ( name == "beta" )
		return &beta_effect;
	if ( name == "filmgrain" )
		return &filmgrain_effect;
	if ( name == "exampleeffect" )
		return &exampleeffect_effect;
	return NULL;
}

const char *NameOf( IScreenSpaceEffect *p )
{
	if ( !p )
		return "(null)";
	if ( p == &alpha_effect )
		return "alpha";
	if ( p == &beta_effect )
		return "beta";
	if ( p == &filmgrain_effect )
		return "filmgrain";
	if ( p == &exampleeffect_effect )
		return "exampleeffect";
	return "(unknown)";
}

// One step: its S row, then what the subject did.
void Step( const std::string &step )
{
	SseLog( "S\t%s", step.c_str() );
	std::vector<std::string> f;
	std::istringstream in( step );
	std::string c;
	while ( std::getline( in, c, '\t' ) )
		f.push_back( c );

	IScreenSpaceEffectManager *m = g_pScreenSpaceEffects;
	const std::string &verb = f[0];
	if ( verb == "init" )
	{
		CommandLine()->CreateCmdLine( f.size() > 1 ? f[1].c_str() : "game" );
		m->InitScreenSpaceEffects();
	}
	else if ( verb == "shutdown" )
		m->ShutdownScreenSpaceEffects();
	else if ( verb == "get" )
		SseLog( "G\t%s", NameOf( m->GetScreenSpaceEffect( f[1].c_str() ) ) );
	else if ( verb == "setparams-name" || verb == "setparams-effect" )
	{
		// f[2] the KeyValues' name, f[3] its "value" (a string), f[4] whether example_param is set as one.
		KeyValues *kv = f[2] == "-" ? NULL : new KeyValues( f[2].c_str() );
		if ( kv )
		{
			kv->SetString( "value", f[3].c_str() );
			if ( f.size() > 4 && f[4] == "1" )
				kv->SetString( "example_param", "x" );
		}
		if ( verb == "setparams-name" )
			m->SetScreenSpaceEffectParams( f[1].c_str(), kv );
		else
			m->SetScreenSpaceEffectParams( EffectNamed( f[1] ), kv );
		if ( kv )
			kv->deleteThis();
	}
	else if ( verb == "enable-name" )
		m->EnableScreenSpaceEffect( f[1].c_str() );
	else if ( verb == "enable-effect" )
		m->EnableScreenSpaceEffect( EffectNamed( f[1] ) );
	else if ( verb == "disable-name" )
		m->DisableScreenSpaceEffect( f[1].c_str() );
	else if ( verb == "disable-effect" )
		m->DisableScreenSpaceEffect( EffectNamed( f[1] ) );
	else if ( verb == "disableall" )
		m->DisableAllScreenSpaceEffects();
	else if ( verb == "render" )
		m->RenderEffects( atoi( f[1].c_str() ), atoi( f[2].c_str() ), atoi( f[3].c_str() ), atoi( f[4].c_str() ) );
	else if ( verb == "example-enabled" )
		SseLog( "X\t%d", exampleeffect_effect.IsEnabled() ? 1 : 0 );
	else
		ADD_FAILURE() << "no step " << verb;
}

const char *const kScript[] = {
	"init",
	"get\talpha",
	"get\tALPHA",
	"get\tExampleEffect",
	"get\tmissing",
	"get\thollow",
	"setparams-name\talpha\tkv1\tone",
	"setparams-name\tmissing\tkv2\ttwo",
	"setparams-name\thollow\tkv3\tthree",
	"setparams-effect\tbeta\tkv4\tfour",
	"setparams-effect\t-\tkv5\tfive",
	"setparams-name\texampleeffect\tkv6\tsix\t1",
	"setparams-effect\tfilmgrain\t-\t-",
	"enable-name\talpha",
	"enable-name\tmissing",
	"enable-effect\tbeta",
	"enable-effect\t-",
	"enable-name\texampleeffect",
	"example-enabled",
	"render\t1\t2\t640\t480",
	"disable-name\talpha",
	"disable-name\tmissing",
	"disable-effect\tbeta",
	"disable-effect\t-",
	"enable-name\tfilmgrain",
	"disableall",
	"render\t0\t0\t1280\t720",
	"shutdown",
	"init\tgame -filmgrain",
	"render\t0\t0\t1280\t720",
	"shutdown",
};

// The subject's registrations are file statics, so the script runs ONCE per
// process and every test reads that one run.
std::string RunScript()
{
	static std::string s_Run;
	static bool s_bRan = false;
	if ( !s_bRan )
	{
		s_bRan = true;
		g_Log.clear();
		for ( const char *step : kScript )
			Step( step );
		s_Run = g_Log;
	}
	return s_Run;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoFile( "src/test/client/Fixtures/screenspaceeffects-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoFile( "src/test/oracle/native/screenspaceeffects_golden.regen" );
}

} // namespace

TEST( ScreenSpaceEffectsOracle, StockProducesTheGolden )
{
	const std::string run = RunScript();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	std::ifstream in( GoldenPath() );
	ASSERT_TRUE( in.good() ) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();
	ASSERT_GT( golden.size(), 1000u ) << "the golden is empty or truncated";

	std::istringstream a( run ), b( golden );
	std::string la, lb;
	int n = 0;
	while ( true )
	{
		const bool ha = (bool)std::getline( a, la );
		const bool hb = (bool)std::getline( b, lb );
		++n;
		if ( !ha && !hb )
			break;
		ASSERT_TRUE( ha && hb && la == lb )
			<< "line " << n << ": stock now writes\n  " << ( ha ? la : "<end>" ) << "\nand the golden has\n  "
			<< ( hb ? lb : "<end>" ) << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

// Each arm the script exists to reach, found in the run.
TEST( ScreenSpaceEffectsOracle, TheScriptReachesEveryArm )
{
	const std::string run = RunScript();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		// The list is newest-first: filmgrain (registered last of the four effects) is walked first.
		{ "S\tinit\nE\tfilmgrain\tIsEnabled\t0\nE\tfilmgrain\tInit\nE\tfilmgrain\tEnable\t0\n",
		  "Init walks the newest registration first, keeping its flag across Init" },
		{ "E\tbeta\tIsEnabled\t1\nE\tbeta\tInit\nE\tbeta\tEnable\t1\n",
		  "an effect enabled before Init is enabled after it" },
		{ "S\tget\tALPHA\nG\talpha\n", "the lookup ignores case" },
		{ "S\tget\tExampleEffect\nG\texampleeffect\n", "the lookup finds the subject's own effect" },
		{ "S\tget\tmissing\nW\tCould not find screen space effect missing\nG\t(null)\n",
		  "an unknown name warns and answers nothing" },
		{ "S\tget\thollow\nG\t(null)\n", "a registration with no effect answers nothing, without a warning" },
		{ "S\tsetparams-name\talpha\tkv1\tone\nE\talpha\tSetParameters\tkv1\tone\n",
		  "parameters by name reach the effect" },
		{ "S\tsetparams-effect\tbeta\tkv4\tfour\nE\tbeta\tSetParameters\tkv4\tfour\n",
		  "parameters by effect reach it" },
		{ "S\tsetparams-effect\t-\tkv5\tfive\nS\t", "parameters for no effect do nothing" },
		{ "S\tsetparams-effect\tfilmgrain\t-\t-\nE\tfilmgrain\tSetParameters\t(null)\t(null)\n",
		  "no parameters are passed on as none" },
		{ "S\tenable-name\texampleeffect\nS\texample-enabled\nX\t0\n", "the example effect cannot be enabled" },
		{ "S\trender\t1\t2\t640\t480\nE\tfilmgrain\tRender\t1\t2\t640\t480\nE\tbeta\tRender\t1\t2\t640\t480\nE\talpha\tRender\t1\t2\t640\t480\n",
		  "every effect renders, enabled or not, newest first" },
		{ "S\tdisableall\nE\tfilmgrain\tEnable\t0\nE\tbeta\tEnable\t0\nE\talpha\tEnable\t0\n",
		  "disabling all disables each" },
		{ "S\tshutdown\nE\tfilmgrain\tShutdown\nE\tbeta\tShutdown\nE\talpha\tShutdown\n", "shutdown reaches each" },
		{ "S\tinit\tgame -filmgrain\nE\tfilmgrain\tEnable\t1\nE\tfilmgrain\tIsEnabled\t1\n",
		  "-filmgrain enables filmgrain before the walk" },
	};
	for ( const Arm_t &arm : kArms )
		EXPECT_NE( run.find( arm.pszNeedle ), std::string::npos ) << arm.pszWhat << ": no";

	// The example effect's enabled arm is unreachable: nothing it would draw with was asked for.
	EXPECT_EQ( run.find( "UpdateScreenEffectTexture" ), std::string::npos );
	EXPECT_EQ( run.find( "GetFullFrameFrameBufferTexture" ), std::string::npos );
}
