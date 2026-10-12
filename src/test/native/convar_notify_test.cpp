//========= SourceSharp ========================================================//
//
// convar_notify_test.cpp -- lane `defect-convar-notify`, backlog item 10.
//
// WHAT WAS WRONG, AND WHY THE REPORTED DIAGNOSIS WAS ONLY HALF OF IT.
//
// `policy-knobs` moved 43 ConVar declarations into managed code and measured six
// of them holding a STALE MANAGED CACHE after a console `set`: violence_hblood,
// violence_hgibs, violence_ablood, violence_agibs, sv_alternateticks and
// sv_unlockedchapters. It reported the cause as "SourceSharp_OnConVarChanged
// matches BY POINTER, so a name registered from two DLLs never notifies the
// other side", and flagged sv_unlockedchapters as unexplained because it
// registers only once.
//
// THE POINTER MATCH WAS REAL AND WAS NOT THE WHOLE MECHANISM.
// tier1 links a duplicate name as a CHILD of the first registration
// (CCvar::RegisterConCommand, in vstdlib), and what it does with the
// child's change callback is asymmetric:
//
//     if ( pChildVar->m_fnChangeCallback ) {
//         if ( !pParentVar->m_fnChangeCallback )
//             pParentVar->m_fnChangeCallback = pChildVar->m_fnChangeCallback;
//         else
//             Warning( "Convar %s has multiple different change callbacks\n" );
//     }
//
// So a per-ConVar callback on a name someone else owns fails TWO ways:
//
//   * parent has no callback -- ours is slammed onto the PARENT, and
//     ConVar::ChangeStringValue calls it with `this` == the parent. A pointer
//     comparison against our own m_ConVar misses. A NAME match fixes this one.
//   * parent already has one -- ours is DROPPED and never called at all. A name
//     match fixes NOTHING here, because the function is not reached.
//
// AND sv_unlockedchapters IS NOT A SEPARATE PUZZLE. It registers once in OUR
// list and twice in the PROCESS: the ENGINE declares it too. Measured over the
// mod's own binaries, `strings ... | grep -cx <name>`:
//
//     violence_hblood     engine=1 gameui=1      cs_sv_greeting            0  0
//     violence_hgibs      engine=1 gameui=1      cs_sv_spawn_health        0  0
//     violence_ablood     engine=1 gameui=1      sv_strict_notarget        0  0
//     violence_agibs      engine=1 gameui=1      ai_shot_stats             0  0
//     sv_alternateticks   engine=1 gameui=0      ai_post_frame_navigation  0  0
//     sv_unlockedchapters engine=1 gameui=1
//
// Six of six stale knobs are declared outside the game DLLs; zero of the five
// controls that routed correctly are. ONE mechanism, not five plus a mystery,
// and the distinguishing property is "someone else already owns the name" --
// not "two game DLLs", which is why the reported framing could not account for
// the sixth.
//
// THE FIX, therefore, is not the reported one-liner. It is a GLOBAL change
// callback -- ConVar::ChangeStringValue calls g_pCVar->CallGlobalChangeCallbacks
// unconditionally, and the engine keeps those in a VECTOR, one entry per
// installer, so every DLL that installed one is called for every change -- plus
// a match by NAME, because `this` is still the parent.
//
// WHAT THIS FILE CAN AND CANNOT CHECK. The mechanism lives in the engine's
// closed CCvar; a fake ICvar reproducing the parent/child linkage would be a
// fixture standing in for native, which drifts silently. So the behaviour is
// proved by a LIVE differential instead -- set a convar from the console and
// read the MANAGED cache back through debug-api /convar -- and what is pinned here
// are the four decisions that differential rests on, so that a tree which
// quietly returns to a per-ConVar pointer-matched callback fails here rather
// than passing everything and going stale again.
//
// COMMENTS AND STRING LITERALS ARE STRIPPED FIRST, and this file is the reason:
// the comment this lane added to sourcesharp_gamesystem.cpp QUOTES
// `m_fnChangeCallback`, `InstallGlobalChangeCallback` and the callback's own
// name, so a raw grep would find every needle below in prose about the needle.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <fstream>
#include <sstream>
#include <string>

namespace
{

const char *kBridge = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";

// Same shape as section6_rulings_test.cpp's, and it cannot be shared: these
// tests each link exactly one .cpp.
const char *SrcRoot()
{
	static std::string s_strRoot;
	if ( !s_strRoot.empty() )
		return s_strRoot.c_str();

	std::string strPath = ".";
	for ( int i = 0; i < 8; i++ )
	{
		std::ifstream probe( ( strPath + "/" + kBridge ).c_str() );
		if ( probe.good() )
		{
			s_strRoot = strPath;
			return s_strRoot.c_str();
		}
		strPath += "/..";
	}
	return NULL;
}

std::string ReadRaw( const std::string &strRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + strRel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string StripCommentsAndStrings( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	enum { CODE, LINE_COMMENT, BLOCK_COMMENT, STRING, CHARLIT } state = CODE;

	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		const char n = ( i + 1 < s.size() ) ? s[ i + 1 ] : '\0';
		switch ( state )
		{
		case CODE:
			if ( c == '/' && n == '/' ) { state = LINE_COMMENT; i++; }
			else if ( c == '/' && n == '*' ) { state = BLOCK_COMMENT; i++; }
			else if ( c == '"' ) { state = STRING; out += ' '; }
			else if ( c == '\'' ) { state = CHARLIT; out += ' '; }
			else out += c;
			break;
		case LINE_COMMENT:
			if ( c == '\n' ) { state = CODE; out += '\n'; }
			break;
		case BLOCK_COMMENT:
			if ( c == '*' && n == '/' ) { state = CODE; i++; }
			else if ( c == '\n' ) out += '\n';
			break;
		case STRING:
			if ( c == '\\' ) i++;
			else if ( c == '"' ) state = CODE;
			break;
		case CHARLIT:
			if ( c == '\\' ) i++;
			else if ( c == '\'' ) state = CODE;
			break;
		}
	}
	return out;
}

// The body of CSourceSharpConVar's constructor initialiser list, which is where
// a per-ConVar callback would be passed. Bounded at the class's closing brace so
// that a callback mentioned anywhere else in the file cannot satisfy it.
std::string BridgeCode()
{
	return StripCommentsAndStrings( ReadRaw( kBridge ) );
}

} // namespace


TEST( ConVarNotify, TheBridgeConVarCarriesNoPerConVarChangeCallback )
{
	// RED BEFORE THE FIX: the m_ConVar initialiser passed
	// SourceSharp_OnConVarChanged as its ninth argument.
	//
	// It has to go rather than merely be supplemented. tier1 hands a per-ConVar
	// callback to the PARENT of a linked pair, so keeping it would either fire
	// twice for a name we alone declare or be dropped with a warning for a name
	// the engine also declares -- and neither is a thing to reason about when
	// the global callback already covers both.
	const std::string code = BridgeCode();

	const size_t ctor = code.find( "CSourceSharpConVar( int nId" );
	ASSERT_NE( std::string::npos, ctor );
	const size_t endOfClass = code.find( "};", ctor );
	ASSERT_NE( std::string::npos, endOfClass );

	const std::string body = code.substr( ctor, endOfClass - ctor );
	EXPECT_EQ( std::string::npos, body.find( "SourceSharp_OnConVarChanged" ) )
		<< "CSourceSharpConVar still installs a per-ConVar change callback; on "
		<< "any name the engine already declares tier1 will either re-point it "
		<< "at the parent or drop it";
}

TEST( ConVarNotify, TheChangeCallbackIsInstalledGlobally )
{
	// RED BEFORE THE FIX: the file never named InstallGlobalChangeCallback.
	//
	// This is the half a name match alone cannot supply. ChangeStringValue calls
	// CallGlobalChangeCallbacks unconditionally and the engine keeps a VECTOR of
	// them, so both DLLs are called even when one side's per-ConVar callback
	// would have been dropped.
	const std::string code = BridgeCode();

	const size_t install = code.find( "InstallGlobalChangeCallback" );
	ASSERT_NE( std::string::npos, install )
		<< "nothing installs a global change callback, so a convar the engine "
		<< "also declares cannot reach managed code at all";

	// And it installs OUR callback, not some other function.
	const size_t line = code.rfind( '\n', install );
	const size_t eol = code.find( ';', install );
	ASSERT_NE( std::string::npos, eol );
	EXPECT_NE( std::string::npos,
	           code.substr( line, eol - line ).find( "SourceSharp_OnConVarChanged" ) )
		<< "a global callback is installed but it is not SourceSharp_OnConVarChanged";
}

TEST( ConVarNotify, TheGlobalCallbackIsInstalledOnlyOnce )
{
	// The installer is reached from every convar registration, and
	// CCvar::InstallGlobalChangeCallback asserts the callback is not already in
	// its vector -- so an unguarded install would assert in a debug engine and
	// notify N times per change in a release one.
	const std::string code = BridgeCode();

	const size_t fn = code.find( "void SourceSharp_InstallConVarChangeCallback" );
	ASSERT_NE( std::string::npos, fn );

	const size_t install = code.find( "InstallGlobalChangeCallback", fn );
	ASSERT_NE( std::string::npos, install );

	const std::string body = code.substr( fn, install - fn );
	EXPECT_NE( std::string::npos, body.find( "g_bSourceSharpConVarCallbackInstalled" ) )
		<< "the install is not gated on the already-installed flag";
	EXPECT_NE( std::string::npos, body.find( "return" ) )
		<< "the installer has no early return, so the flag cannot be gating it";
}

TEST( ConVarNotify, TheCallbackMatchesTheConVarByNameAndNotByPointer )
{
	// RED BEFORE THE FIX: the loop read
	//     if ( !g_SourceSharpConVars[i]->Owns( pVar ) ) continue;
	// with Owns() being `pVar == &m_ConVar`. The object the engine hands a
	// change callback is the PARENT, which is not ours whenever anyone else
	// registered the name first -- and for six of this mod's knobs the engine
	// did.
	const std::string code = BridgeCode();

	// ANCHORED ON THE OPENING BRACE, not on the name. The first version of this
	// fact searched for the signature alone and found the FORWARD DECLARATION
	// forty lines above -- so `body` was the CSourceSharpConVar class, every
	// needle missed, and the fact was red for a reason that had nothing to do
	// with its subject. It is written down because the same shape would have
	// made it silently GREEN if the needles had been ones the class happens to
	// contain.
	const size_t cb = code.find(
		"SourceSharp_OnConVarChanged( IConVar *pVar, const char *pOldValue, "
		"float flOldValue )\n{" );
	ASSERT_NE( std::string::npos, cb )
		<< "the callback's definition is not where this fact can find it";
	const size_t def = code.find( '{', cb );
	const size_t end = code.find( "\n}", def );
	ASSERT_NE( std::string::npos, end );
	const std::string body = code.substr( def, end - def );

	// The definition, and not the one-line forward declaration: a body that
	// short would mean the anchor slipped again.
	EXPECT_LT( 200u, body.size() );

	EXPECT_NE( std::string::npos, body.find( "IsNamed" ) )
		<< "the callback does not match its convar by name";
	EXPECT_EQ( std::string::npos, body.find( "Owns" ) )
		<< "the pointer match is back; it cannot see a convar whose name the "
		<< "engine registered first";
	EXPECT_NE( std::string::npos, body.find( "pVar->GetName()" ) )
		<< "the callback does not take the name from the object the engine "
		<< "handed it, so it is assuming which object that is -- which is the "
		<< "assumption that was wrong";
}

TEST( ConVarNotify, TheNameMatchIsWholeAndCaseInsensitive )
{
	// Convar names are matched case-insensitively everywhere else in this file
	// (the re-registration scan uses ss::StrICmp, tier1's V_stricmp restated
	// in ss_string.h), and the engine's own FindVar
	// is case-insensitive too. A case-sensitive or prefix match here would
	// notify the wrong convar or none.
	const std::string code = BridgeCode();

	const size_t fn = code.find( "IsNamed" );
	ASSERT_NE( std::string::npos, fn );
	const size_t end = code.find( '}', fn );
	ASSERT_NE( std::string::npos, end );
	const std::string body = code.substr( fn, end - fn );

	EXPECT_NE( std::string::npos, body.find( "ss::StrICmp" ) )
		<< "IsNamed is not a case-insensitive whole-string compare";
	EXPECT_NE( std::string::npos, body.find( "m_Name.c_str()" ) )
		<< "IsNamed does not compare against the name this convar registered under";
}
