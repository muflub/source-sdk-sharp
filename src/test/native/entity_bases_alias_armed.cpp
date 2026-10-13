//========= SourceSharp — entity_bases.h against the REAL interface ==========//
//
// The other half of entity_bases_alias_test.cpp; read that file's header first.
//
// This translation unit is the one the whole exercise was about. It includes
// sourcesharp/game/server/entity_bases.h against the REAL public/igameentity.h,
// so it compiles the header exactly as all 197 server TUs see it, and would
// fail to compile at all if those four classes stopped fitting the interface.
//
// That "would fail to compile" is most of the value here, and it is the part a
// green run does NOT show. The mutation that proves it: put
// `BaseClass::ObjectCaps()` back in CPointEntity::ObjectCaps in entity_bases.h
// and this file stops building, at exactly the diagnostic
// docs/typedef-dryrun/floor_server_armed.txt recorded against 180 server TUs.
//
// No stand-in on this side. IGameEntity is small and engine-free enough to
// include for real, which is why buildcpptests builds this with mathlib --
// igameentity.h needs mathlib/vector.h and nothing else. The stand-in is on the
// OTHER side, where it is the instrument rather than a compromise: it supplies
// an ObjectCaps and a bServerOnly constructor that the real interface does not
// have, so that "the derived classes stopped asking their base" is a number
// rather than an argument.
//
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE FILE NAME. THERE IS NO ALIAS ANY MORE.
//-----------------------------------------------------------------------------
//
// This file used to build with `-DSS_CBASEENTITY_ALIAS` and a local
// `using CBaseEntity = ::IGameEntity;`, because entity_bases.h carried three
// `#ifndef SS_CBASEENTITY_ALIAS` / `#else` pairs and this was the TU that
// compiled the armed side of them.
//
// the-close 3/N (0bc727ed9) deleted public/ss_cbaseentity_alias.h. `CBaseEntity`
// is not a name in the compiled tree, entity_bases.h carries no guards, and the
// local `using` has been deleted with them -- it aliased a name nothing here
// spells, which is dead text that would read as live.
//
// THE -D ON THIS ROW IS NOW INERT AND IS KEPT ON PURPOSE. buildcpptests still
// passes -DSS_CBASEENTITY_ALIAS to this binary and BOTH vpcs still define the
// macro (server_csharp.vpc:69, client_csharp.vpc:74); 0bc727ed9 left them alone
// deliberately and no finish-line row asks for their removal. So this TU
// compiles with the macro DEFINED and the header compiles identically either
// way, which is what "selects nothing in any compiled file" means -- and the
// #error below keeps the row and this file agreeing about that, rather than
// letting the define quietly disappear from one of them.
//
//=============================================================================//

#ifndef SS_CBASEENTITY_ALIAS
#error "buildcpptests builds entity_bases_alias_armed.cpp with \
-DSS_CBASEENTITY_ALIAS and both game vpcs still define the macro. It selects \
nothing -- there are no arms left for it to choose between -- and this file \
exists partly to keep that true and visible. If the define is being retired, \
retire it from the vpcs and from buildcpptests' row for this binary, and take \
this #error with it."
#endif

#include <fstream>
#include <string>

#include "gtest/gtest.h"

#include "mathlib_test_env.h"

#include "igameentity.h"

// game/shared/baseentity_shared.h:21 and :26, same two as the unarmed TU.
#define FCAP_ACROSS_TRANSITION 0x00000002
#define FCAP_IMPULSE_USE 0x00000010

#define DECLARE_CLASS( className, baseClassName ) \
	typedef baseClassName BaseClass;              \
	typedef className ThisClass;

namespace armed
{

// The header names ::IGameEntity directly since 0bc727ed9, so nothing is
// aliased here any more. It is included inside a namespace so that its four
// classes cannot collide at link time with the stand-in-based copy in
// entity_bases_alias_test.cpp's binary, which is the reason the pair was two
// TUs in the first place.
#include "../game/server/entity_bases.h"

void CPointEntity::Spawn( void ) {}
bool CPointEntity::KeyValue( const char *, const char * ) { return true; }
bool CServerOnlyPointEntity::KeyValue( const char *, const char * ) { return true; }
bool CLogicalEntity::KeyValue( const char *, const char * ) { return true; }

// Armed, these four derive from an abstract interface, so a leaf that answers
// IGameEntity's six is needed before anything can be instantiated. That is the
// shape the flip leaves them in and it is not a defect of the test.
template < typename TBase >
class CLeaf : public TBase
{
public:
	virtual bool ShouldCollide( int, int ) const { return true; }
	virtual int GetCollisionGroup() const { return 0; }
	virtual void Touch( const Toucher_t & ) {}
	virtual void StartTouch( const Toucher_t & ) {}
	virtual void EndTouch( const Toucher_t & ) {}
	virtual void VPhysicsUpdate( IPhysicsObject * ) {}
};

}  // namespace armed

namespace
{

// Does T answer ObjectCaps()? A compile-time question, asked of the REAL
// IGameEntity rather than of anything this file wrote.
template < typename T >
struct HasObjectCaps
{
	template < typename U >
	static auto Probe( int ) -> decltype( ( (U *)0 )->ObjectCaps(), char() );
	template < typename U >
	static long Probe( ... );
	// constexpr, so inline (C++17): EXPECT_TRUE takes it by reference, and a
	// static const member a reference binds to needs a definition at -O0.
	static constexpr bool value = sizeof( Probe< T >( 0 ) ) == sizeof( char );
};

//-----------------------------------------------------------------------------
// READING THE INTERFACE AS TEXT, WHICH THE PROBE ABOVE CANNOT SUBSTITUTE FOR.
//
// HasObjectCaps asks the question the way a CALLER asks it, and a caller can
// only see PUBLIC members. A mutation proved the gap rather than an argument
// predicting it: inserting `virtual int ObjectCaps( void ) { return 0; }` into
// igameentity.h immediately above `virtual ~IGameEntity() {}` -- which is under
// `protected:` at :291 -- left this binary GREEN. The member was there, the
// membership rule was broken, and the probe could not see it, because
// substitution failure on an inaccessible member is indistinguishable from
// substitution failure on an absent one.
//
// entity_bases_alias_test.cpp's TheStandInDiffersOnlyWhereItSaysSo does catch
// it, because it reads the header. That is a DIFFERENT BINARY, so relying on it
// would make this fact's completeness depend on a file nothing here links to.
// The text read is 15 lines; it lives beside the probe instead.
//
// buildcpptests runs each binary with src/ as the working directory; the other
// candidates are so that running this by hand from the repo root also works.
//-----------------------------------------------------------------------------
std::string InterfaceHeaderText()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::ifstream file(
			( std::string( kCandidates[ i ] ) + "/public/igameentity.h" ).c_str() );
		if ( !file.is_open() )
			continue;

		std::string out, line;
		while ( std::getline( file, line ) )
		{
			// Comments only, and whole-line only: igameentity.h's prose names
			// the members it does NOT have, at length, and a raw match would
			// read one of those sentences as a declaration.
			const size_t at = line.find_first_not_of( " \t" );
			if ( at != std::string::npos && line.compare( at, 2, "//" ) == 0 )
				continue;
			out += line;
			out += "\n";
		}
		return out;
	}
	return std::string();
}

}  // namespace

//-----------------------------------------------------------------------------
// WHAT THE ARMED ARM ANSWERS.
//
// Zero, and entity_bases.h's own comment derives it: unarmed the expression is
// `CBaseEntity::ObjectCaps() & ~FCAP_ACROSS_TRANSITION`, whose only non-zero
// outcome is a PARENT's forwarded +USE bits, and this hierarchy has no parent
// to forward from (igameentity.h, under GetMoveParent).
//-----------------------------------------------------------------------------
TEST( EntityBasesArmed, PointEntityAnswersNoCaps )
{
	armed::CLeaf< armed::CPointEntity > ent;
	EXPECT_EQ( 0, ent.ObjectCaps() );
}

TEST( EntityBasesArmed, ServerOnlyEntityAnswersNoCaps )
{
	armed::CLeaf< armed::CServerOnlyEntity > ent;
	EXPECT_EQ( 0, ent.ObjectCaps() );
}

TEST( EntityBasesArmed, ServerOnlyEntityStillDefaultConstructs )
{
	// The deleted `: CBaseEntity( true )` is gone for good; what must survive is
	// that the two classes deriving from it -- and they are in the prelude too
	// -- still have a default constructor to call.
	armed::CLeaf< armed::CServerOnlyPointEntity > pt;
	armed::CLeaf< armed::CLogicalEntity > logical;
	EXPECT_EQ( 0, pt.ObjectCaps() );
	EXPECT_EQ( 0, logical.ObjectCaps() );
}

//-----------------------------------------------------------------------------
// THE REASON THE ZERO ABOVE IS THE RIGHT ANSWER AND NOT JUST THE CURRENT ONE.
//
// If IGameEntity ever grew an ObjectCaps, the DELETED text -- `BaseClass::
// ObjectCaps() & ~FCAP_ACROSS_TRANSITION` -- would compile again, and the
// answer for these classes would silently become "whatever the interface says"
// rather than the derived zero above. It has not and it must not: igameentity.h's
// membership rule keeps the interface at six members the engine initiates, and
// FCAP_* is SDK save/restore and +USE bookkeeping the engine never asks for.
//-----------------------------------------------------------------------------
TEST( EntityBasesArmed, TheInterfaceStillHasNoObjectCaps )
{
	EXPECT_FALSE( HasObjectCaps< ::IGameEntity >::value )
		<< "IGameEntity has acquired an ObjectCaps. Either it belongs there -- "
		   "in which case entity_bases.h's two overrides should call it and this "
		   "test should go -- or it does not, and igameentity.h's membership "
		   "rule has been broken.";

	// The same probe, aimed at something that DOES have the member, so that a
	// probe which has quietly stopped detecting anything cannot read as a pass.
	EXPECT_TRUE( HasObjectCaps< armed::CPointEntity >::value );

	// AND THE SAME QUESTION, ACCESS-BLIND. See InterfaceHeaderText above: the
	// probe is a caller's view and a PROTECTED ObjectCaps is invisible to it --
	// measured, by a mutation that added one and left this binary green.
	const std::string iface = InterfaceHeaderText();
	ASSERT_FALSE( iface.empty() )
		<< "could not read public/igameentity.h, so the access-blind half of "
		   "this fact is measuring nothing";
	EXPECT_NE( iface.find( "abstract_class IGameEntity" ), std::string::npos )
		<< "public/igameentity.h no longer declares IGameEntity; the absence "
		   "below would be an absence from some other file";
	EXPECT_EQ( iface.find( "ObjectCaps" ), std::string::npos )
		<< "public/igameentity.h declares an ObjectCaps somewhere the probe "
		   "above cannot reach -- protected, private, or behind a conditional. "
		   "The membership rule keeps this interface at six members the engine "
		   "initiates, and FCAP_* is SDK save/restore and +USE bookkeeping the "
		   "engine never asks for.";
}
