//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================//
// SS_REASON a  a vtable the engine calls through
//
// The world, as a PERMANENT native adapter (plan_baseentity.md Phase 12).
//
// worldspawn is the one entity in this plan that stays a C++ object because of
// a property of the ENGINE rather than a piece of work outstanding: the engine
// names it by index, not by handle. It is edict 0, it owns brush model 0,
// every trace that hits nothing hits it, and GetWorldEntity() is called from
// fifteen files that are staying native. A player also lives at a fixed index,
// but everything the engine asks of a player arrives through an edict_t* the
// engine HANDS the game; the world's index is one the engine ASSUMES.
//
// So this file is not an unfinished port and is deliberately not in the stock
// tree: it is CWorld lifted whole out of game/server/world.cpp, which leaves
// the build with it -- along with the ~340 lines of CDecal/CProjectedDecal
// that file still compiled after both registrations went managed. The class
// keeps its name: renaming CWorld would touch every GetWorldEntity() caller
// and change nothing the engine sees.
//
// Its KEYS are C#'s to read. Of the thirteen keys CWorld parses, exactly one
// drives a game decision rather than engine plumbing -- chaptertitle, held
// by ServerGameDll.WorldKeyValue and decided in ChapterTitle.cs. The rest are
// engine surface: skyname sets a convar (managed sets it, since
// plan_server_native_v2 T4d), newunit talks to the save system, world_mins/maxs
// and the occlusion and detail-sprite parameters ride DT_WORLD to the client
// renderer. Every key is offered to C#; none is parsed here.
//
// What CWorld::Precache does is ENGINE BOOTSTRAP, not game logic: game rules
// installation, the sound entity, the body queue, sentence and particle
// tables, the light style table, AI networks and schedules for the AI that
// remains, and the shared activity/event lists. Each of those either leaves
// with its own phase or stays with the engine surface it serves.
//
//=============================================================================//

// On the R5 include set (plan_server_novalve.md P5, lane E): public/ headers
// and our own, no cbase.h. What the game/ headers used to declare is below.
#include "ss_util.h"            // engine, ss::FStrEq, ss::INDEXENT, ss::UTIL_TraceLine
#include "ss_shareddefs.h"      // ss::kLanguageEnglish / kLanguageGerman, ss::kDmgBullet
#include "ss_gamestringpool.h"  // AllocPooledString
#include "engine/ivmodelinfo.h"
#include "shell_tempents.h"
#include "ss_soundent.h"
#include "ss_decals.h"
#include "igamerules.h"
#include "isaverestore.h"
#include "ss_world.h"
#include "mempool.h"
#include "engine/IEngineSound.h"
#include "engine/IStaticPropMgr.h"

// The engine interfaces and globals this file reads, defined beside the other
// engine-boundary globals.
extern IVModelInfo  *modelinfo;
extern CGlobalVars  *gpGlobals;

// Defined in our TUs, and declared until now by game/ headers this file no
// longer includes: enginecallback.h, util.h, client.h and ai_utils.h.
void PrecacheParticleSystem( const char *pParticleSystemName );
void SENTENCEG_Init();

#include "precache_model.h"
#include "shell_server.h"

// SourceSharp_NextWorldSerial, for the world's slot-table binding.
#include "slot_table.h"

// CWorld::ImpactTrace, below: the effect the world dispatches for a bullet hole
// on one of its own surfaces. CBaseEntity::ImpactTrace reached these through
// cbase.h's server prelude; this class reaches them by name.
#include "ss_effect_dispatch.h"

// The two crossings this file makes for the world's networked state: the handle
// C# binds its state to, and the table C# hands the key to. world.h brings in
// shell_world_nettable.h and world_net_sync.h, which are the other half.
#include "shell_resolve.h"
#include "managed_runtime_server.h"

// The lifted precache/tick/prediction/tracer statics. They used to be
// reached as CBaseEntity::Foo() through one-line forwarders in
// game/shared/baseentity_shared_forwarders.cpp; that quarantine is retired
// and the calls below name the free functions directly.
#include "entity_shared_statics.h"

#include "native_command_body.h"
#include "ss_recipient_filter.h"
#include "ss_entityfactory.h"   // ss::IEntityFactory, for CWorldFactory
// g_Language's English write and the gibs it picks are managed's world
// precache since plan_server_native_v2 T4e; s_Language went with them.


// g_pLastSpawn, g_fGameOver and g_iszFuncBrushClassname went
// (plan_server_native_v2 T6): the world's Precache wrote each and nothing read
// one (spawn rotation, game-over and func_brush lookups are managed's).

//-----------------------------------------------------------------------------
// The global sprite indexes, transplanted from basecombatweapon.cpp when that
// file left the build (Phase 10). They were never the weapon class's — they
// are the smoke, blood and explosion sprites every TE_* dispatch and every
// explosion names by index — and their precache was always called from HERE,
// by the world's own Precache. The weapon-script database load that W_Precache
// also did is gone with the scripts' readers: the per-weapon data is a managed
// table now.
//-----------------------------------------------------------------------------
short		g_sModelIndexSmoke;			// holds the index for the smoke cloud

// THE ONE WHOSE INDEX NATIVE CODE READS (UTIL_Smoke in server_util_library.cpp,
// through ss::Effects). The other three are still precached, which fills the
// engine's table, but their indices had no reader (UTIL_Bubbles and
// UTIL_BubbleTrail are gone), so they are not stored (plan_server_native_v2
// T6). The rest of stock's W_Precache -- the
// fireball, the underwater fireball, the laser and its dot (globals nothing
// read), the three blood-impact particle systems and the bubble trail -- is
// managed's world precache (plan_server_native_v2 T4e). These go when their
// reader does.
static void W_Precache(void)
{
	g_sModelIndexSmoke = Precache_Model("sprites/steam1.vmt");// smoke
	Precache_Model("sprites/bubble.vmt");//bubbles
	Precache_Model("sprites/bloodspray.vmt"); // initial blood
	Precache_Model("sprites/blood.vmt"); // splattered blood
}

//=======================
// CWorld
//
// This spawns first when each level begins.
//=======================
//
// NOT LINK_ENTITY_TO_CLASS ANY MORE, and the reason is one line of util.h.
//
// That macro declares a CEntityFactory<CWorld>, whose Create() is:
//
//     T *pEnt = _CreateEntityTemplate( (T*)NULL, pClassName );
//     return pEnt->NetworkProp();
//
// and _CreateEntityTemplate calls pEnt->PostConstructor(). Both of those are
// CBaseEntity's, and neither is anything worldspawn wanted: PostConstructor
// gives an entity a classname and an edict, and this class has a constant
// classname and claims edict 0 itself; NetworkProp() is the network property
// this object does not have, asked for only to reach the IServerNetworkable
// the factory returns -- WHICH THIS CLASS ALREADY IS.
//
// So the factory is four lines and returns `this`. The dictionary's interface
// is IServerNetworkable all the way through -- InstallFactory, Create, Destroy
// and FindFactory never mention CBaseEntity -- which is why a class off that
// hierarchy can be registered in it at all. That was already true; the SDK's
// template just never used it.
//
// Destroy() is Release(), unchanged, which for this class does nothing: the
// world is destroyed by the entity purge at level shutdown and a second owner
// would be a double free.
//
// THE FACTORY OBJECT IS A FILE-SCOPE STATIC AND THAT IS THE SAME EXEMPTION
// LINK_ENTITY_TO_CLASS ITSELF TAKES -- the macro expands to exactly one, and
// every entity class in the build has one. It has to be: the dictionary keeps
// the pointer for the life of the process, so the object cannot live on a
// stack, and its constructor IS the registration.
//
namespace
{

class CWorldFactory : public ss::IEntityFactory
{
public:
	CWorldFactory()
	{
		ss::EntityFactoryDictionary()->InstallFactory( this, "worldspawn" );
	}

	virtual IServerNetworkable *Create( const char * ) OVERRIDE
	{
		// ONE WORLD, AND THE REFUSAL IS NOT DEFENSIVE PROGRAMMING.
		//
		// CWorld's constructor claims the singleton AND TAKES EDICT 0. A second
		// construction therefore does not produce a second entity that is
		// merely unwanted -- it re-points g_WorldEntity, re-points edict 0's
		// networkable, and re-registers slot 0 in the entity list, silently,
		// leaving the real world unreachable while still alive.
		//
		// It is reachable. CreateEntityByName( "worldspawn" ) resolves the
		// factory's answer to a CBaseEntity, gets NULL for this class, and then
		// -- correctly for its own contract -- calls Destroy() on it. Destroy
		// is Release(), and Release for this class does nothing, because the
		// world is freed by the entity purge. So that path would build a world,
		// take edict 0 with it, and drop the pointer.
		//
		// The legitimate creation is MapEntity_ParseWorld's, through
		// CreateNetworkableEntityByName, which does not resolve or destroy.
		if ( GetWorldEntity() != NULL )
		{
			Warning( "[SourceSharp] worldspawn: a second one was asked for while "
			         "one exists. REFUSED -- it would take edict 0 from the world "
			         "that is already in the level.\n" );
			return NULL;
		}

		// The one place `new` is used for this class, exactly as
		// _CreateEntityTemplate is for every other -- and the reason CWorld
		// declares its own operator new: the blob past the object.
		//
		// THE MEMBER IS WHAT COMES BACK, not the CWorld. This factory's return
		// type is IServerNetworkable * and CWorld is not one any more: it HAS
		// one. Destroy() below calls Release() on whatever this handed out, and
		// CWorldNetworkable::Release is `delete m_pOuter` -- so the object that
		// gets freed is still the world, through the one owner it has.
		CWorld *pWorld = new CWorld;

		return pWorld->GetNetworkable();
	}

	virtual void Destroy( IServerNetworkable *pNetworkable ) OVERRIDE
	{
		if ( pNetworkable )
			pNetworkable->Release();
	}

	virtual size_t GetEntitySize() OVERRIDE { return sizeof( CWorld ); }
};

CWorldFactory g_WorldFactory;

} // namespace

//-----------------------------------------------------------------------------
// THERE IS NO BEGIN_DATADESC( CWorld ), AND THE TWELVE ROWS IT HELD ARE
// DISPOSED OF ONE AT A TIME BELOW.
//
// The owner's ruling: "Remove BEGIN_DATADESC( CWorld ) completely. If any of
// those fields still need parsing, special-case them in the method." CWorld
// declares no datamap now and inherits CBaseEntity's, which is what still
// answers every key that was never the world's -- targetname, origin, angles --
// through CBaseEntity::KeyValue at the end of CWorld::KeyValue.
//
// THE BLOCK AT b7a78a3a, RE-DERIVED RATHER THAN QUOTED: three DEFINE_FIELD and
// nine DEFINE_KEYFIELD, two of the nine for a console this build does not target.
//
// THE THREE DEFINE_FIELDs ARE SAVE/RESTORE ONLY, AND NOTHING REACHES THEM.
// m_flWaveHeight, m_WorldMins and m_WorldMaxs had no key: a plain DEFINE_FIELD
// is a row in the save block and nothing else. This build refuses the save
// cluster wholesale -- Part 7, settled by the engine's own answer
// ("Can't save multiplayer games", docs/divergences.md:6874) -- and
// shell_gamedll.cpp refuses all fifteen of IServerGameDLL's save methods
// through ShellGameDll_Unreachable, whose counter reads 0 after a played map,
// a level change, a reload, a disconnect and a shutdown (Phase 9). So the three
// rows described a serialisation that cannot happen. They are dropped with no
// loss, and the three VALUES did not go with them: all three are networked and
// all three live in the blob.
//
// THE NINE KEYS ARE PARSED IN CWorld::KeyValue, and the split is measured:
//
//   world_mins, world_maxs, startdark, maxoccludeearea, minoccluderarea,
//   maxpropscreenwidth, minpropscreenwidth, detailmaterial, coldworld
//       -- eight of these were keyfields and world_mins/world_maxs were
//          already hand-parsed here; all of them are handed to the managed
//          declaration that owns the value, by NAME AND RAW TEXT. That is the
//          "C++ calling into managed for set" half of Part B2, and it moves the
//          PARSING as well as the storage: world_mins is three floats because
//          C# says so, not because an sscanf here says so.
//
//   chaptertitle -- held by ServerGameDll.WorldKeyValue since
//          plan_server_native_v2 T4d, for ChapterTitle.cs, which announces it
//          per player on first spawn (see docs/divergences.md, "The chapter
//          title shows on first spawn"). It is NOT networked and never was, so
//          it does not belong in WorldNetState. skyname and newunit went to the
//          same seam in the same step: every key reaches C#, none is parsed here.
//
//   gametitle -- NOT PARSED AT ALL ANY MORE, and this is the one thing this
//          edit loses: the member it filled had no reader in this build
//          (GetDisplayTitle() had zero callers; stock's was CBasePlayer's
//          first-spawn title path, player.cpp:7003-7008, not in this build).
//          docs/divergences.md has the entry.
//
// The occlusion pair is one key each on this platform, and the managed side
// names the one the shipped game read.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THERE IS NO IMPLEMENT_SERVERCLASS_ST HERE, AND THAT IS THE POINT OF THE PORT.
//
// Stock's macro built a static ServerClass g_CWorld_ClassReg and a DT_WORLD of
// ten typed SendProps addressed by SENDINFO -- i.e. by offsetof into this class.
// Both are gone. shell_world_nettable.cpp builds the ServerClass and every one
// of its props from the C# declaration, and CWorld::GetServerClass below is
// what hands it to the engine.
//
// The ten props' names, encodings and order are all C#'s now. What that costs
// is written down rather than glossed: stock sent m_WorldMins/m_WorldMaxs
// SPROP_COORD and m_flWaveHeight quantised to eight bits, and the payload sends
// every word unscaled. The values the client reads are the same values; the
// bits are more of them.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// WHERE THE BLOB SITS INSIDE A CWorld.
//
// Here rather than in shell_world_nettable.cpp for the reason
// SourceSharp_StandaloneBlobOffset lives beside the concrete standalone shell
// rather than beside its tables: sizeof() is a fact of the translation unit
// that has the class definition, and the table file deliberately does not
// include world.h.
//
// Sixteen-aligned so a generated prop lands on a natural boundary rather than
// wherever the class happens to end -- the same rule and the same reason.
//
// UNLIKE THE HIERARCHY'S, THIS OFFSET SERVES ONE CLASS. The standalone offset
// has to clear the LARGEST shell on its hierarchy because one number serves
// every entity there; nothing derives from CWorld, so this one is exactly past
// this one class and there is no headroom to price.
//-----------------------------------------------------------------------------
int SourceSharp_WorldBlobOffset()
{
	return (int)ALIGN_VALUE( sizeof( CWorld ), 16 );
}

//-----------------------------------------------------------------------------
// Allocated the way the engine allocates an entity, plus the blob.
//
// PvAllocEntPrivateData returns ZEROED memory and the SDK depends on that
// everywhere -- which is also what gives the ten networked values the same
// defaults they had as members: every one of stock's was zero, and the only
// constructor assignment among them was `m_bColdWorld = false`, which is the
// zero it already was.
//
// nBytes is ignored deliberately: it is sizeof( CWorld ), and the offset the
// props were resolved against is a constant computed from that same sizeof.
// Asserted rather than trusted, because the failure would be silent.
//
// THE LENGTH IS C#'s AND MAY BE ZERO. A build whose managed side never declared
// gets an entity with no blob and a shim that networks as its base and says so;
// it must not get an entity whose props read past the end of its allocation.
//-----------------------------------------------------------------------------
static void *AllocWorld( size_t nBytes )
{
	Assert( (int)nBytes <= SourceSharp_WorldBlobOffset() );

	int nBlobBytes = SourceSharp_WorldBlobBytes();

	if ( nBlobBytes < 0 )
		nBlobBytes = 0;

	return engine->PvAllocEntPrivateData( SourceSharp_WorldBlobOffset() + nBlobBytes );
}

void *CWorld::operator new( size_t stAllocateBlock )
{
	return AllocWorld( stAllocateBlock );
}

//-----------------------------------------------------------------------------
// And back the way it came. CWorld has no base with an operator delete, so
// without these `delete m_pOuter` (CWorldNetworkable::Release) reached the
// global delete: server.so's allocator given the engine's block. memoverride
// routed both to tier0, which hid it; the allocation checker did not.
//-----------------------------------------------------------------------------
void CWorld::operator delete( void *pMem )
{
	engine->FreeEntPrivateData( pMem );
}

unsigned char *CWorld::DataBlob()
{
	return (unsigned char *)this + SourceSharp_WorldBlobOffset();
}

int CWorld::BlobBytes() const
{
	return SourceSharp_WorldBlobBytes();
}

//-----------------------------------------------------------------------------
// The class the engine asks for, and it is the managed one.
//
// CServerNetworkProperty::GetServerClass caches this after the first ask, so
// the answer has to be right the first time and the table has to exist by then.
// It does: SourceSharp_WorldNetDeclare runs inside DLL init, from the managed
// publish pass, and the first CWorld is made when the first map is parsed.
//
// THE FALLBACK IS A REAL ANSWER AND NOT A GUARD. With no managed declaration
// there is no CWorld table, and NULL here is a null dereference inside the
// engine on the first entity it packs.
//
// IT USED TO BE `BaseClass::GetServerClass()` -- DT_BaseEntity, "a degraded
// world and not a crash". THAT FALLBACK IS NOT MERELY UNAVAILABLE NOW, IT WOULD
// BE WRONG, and the difference matters enough to spell out.
//
// DT_BaseEntity's props carry CBaseEntity's MEMBER OFFSETS. The engine encodes
// a table against edict->GetUnknown(), which for this class is the CWorld
// object -- so every one of those props would read whatever happens to sit at a
// CBaseEntity offset inside a class that is not one, and send it to every
// client as the world's origin, model index and render colour. That is not a
// degraded world; it is arbitrary memory on the wire, and it would look like a
// rendering bug rather than like what it is.
//
// So the fallback is a NAMED FATAL rather than a quieter wrong answer. It fires
// only in a build whose managed side never called WorldNetDeclare -- which is a
// build that has no world state at all -- and the alternative to stopping is
// NULL, which is a null dereference inside the engine on the first entity it
// packs. Between two ways of not working, the one that says why is better.
//-----------------------------------------------------------------------------
ServerClass *CWorld::GetServerClass()
{
	if ( ServerClass *pClass = SourceSharp_WorldServerClass() )
		return pClass;

	Error( "[SourceSharp] worldspawn has no ServerClass: the managed side never "
	       "declared SourceSharp.Shared.WorldNetState. There is no base table to "
	       "fall back to -- CWorld is not a CBaseEntity, so DT_BaseEntity's props "
	       "would read this object at offsets that belong to a different class.\n" );

	return NULL;
}

//-----------------------------------------------------------------------------
// Asks C# where its ten fields are, once, and again after a managed reload has
// moved them.
//
// False means the declaration is not published on this side, which is a state
// the shim can be in legitimately: the world is constructed during the map
// parse and the managed module is up long before that, but a build whose C#
// failed to load reaches here too. Every reader below answers a zero in that
// case rather than reading offset zero, which is the vtable pointer.
//-----------------------------------------------------------------------------
bool CWorld::ResolveFields()
{
	if ( m_Fields.IsCurrent() )
		return true;

	return SourceSharpWorldFields_Resolve( &m_Fields );
}

//
// Just to ignore the "wad" field.
//
bool CWorld::KeyValue( const char *szKeyName, const char *szValue )
{
	// EVERY KEY, HANDED TO THE SIDE THAT OWNS ITS MEANING (plan_server_native_v2
	// T4d). "skyname", "newunit" and "chaptertitle" were answered here; they are
	// ServerGameDll.WorldKeyValue's now, beside WorldNet's networked keys. The
	// shim does not know which keys C# answers to, and a list on this side
	// would be a second copy of the declaration.
	// E1: the managed-built runtime's slot, not a ManagedApi one.
	IManagedRuntimeServer *pRuntime = ManagedRuntimeServer();

	if ( pRuntime != NULL
	  && pRuntime->OnWorldKeyValue(
	         SourceSharp_HandleFromAnyEntity( static_cast< IServerEntity * >( this ) ),
	         szKeyName, szValue ) != 0 )
	{
		return true;
	}

	// `return BaseClass::KeyValue( szKeyName, szValue )` STOOD HERE, AND WHAT
	// IT ANSWERED IS WORTH NAMING BEFORE IT GOES.
	//
	// CBaseEntity::KeyValue is the SDK's generic handler: origin, angles,
	// targetname, classname, spawnflags, rendercolor and the rest, plus the
	// datamap walk. On worldspawn every one of them is either meaningless (it
	// is at the origin, unrotated, and named by index rather than by
	// targetname) or already answered above -- and this class has had no
	// datamap since the owner's second ruling deleted BEGIN_DATADESC( CWorld ).
	//
	// So the honest answer is FALSE: "this key is not mine". The engine's own
	// behaviour for an unhandled worldspawn key is to ignore it, which is what
	// it did for every key that reached the datamap walk and matched nothing.
	return false;
}


static CWorld *g_WorldEntity = NULL;

CWorld* GetWorldEntity()
{
	return g_WorldEntity;
}

//-----------------------------------------------------------------------------
// THE TWO MEMBERS' CONSTRUCTORS, and the only thing worth saying about them is
// WHEN they run: they are subobjects, so they are fully constructed before the
// CWorld constructor body's first statement. m_pOuter is a constructor
// argument rather than a later Attach() call for exactly that reason -- there
// is no window in which one of these exists with a null owner, and no way for a
// later edit to open one.
//-----------------------------------------------------------------------------
CWorldCollideable::CWorldCollideable( CWorld *pOuter )
{
	m_pOuter = pOuter;

	// SOLID_BSP, and it is the only honest answer: this object's shape IS the
	// level. MOVETYPE_NONE has no member here at all -- a move type is a
	// question CBaseEntity's physics asks itself each frame about what to do
	// with an entity, and worldspawn was answering "nothing" to a loop it is no
	// longer in.
	m_nSolidType  = SOLID_BSP;
	m_nSolidFlags = 0;

	m_vecOrigin   = vec3_origin;
	m_angRotation = vec3_angle;
	SetIdentityMatrix( m_Transform );

	// Replaced at CWorld::Spawn by the model's real box. Something coherent
	// until then, because the engine may ask before the map is placed.
	m_vecMins = vec3_origin;
	m_vecMaxs = vec3_origin;
}

CWorldNetworkable::CWorldNetworkable( CWorld *pOuter )
{
	m_pOuter = pOuter;

	// The engine fills this. Zeroed here so AreaNum answers a number rather
	// than whatever the allocation held, on the first send after a level load
	// and before the engine has computed anything.
	memset( &m_PVSInfo, 0, sizeof( m_PVSInfo ) );
}

#if defined( _MSC_VER )
// 'this' in the initialiser list below is deliberate. The build disables C4355
// (/wd4355, as Valve's VPC does), but tier1/convar.h ends with
// `#pragma warning( default : 4355 )`, which turns it back on for the rest of
// any file that includes it.
#pragma warning( disable : 4355 )
#endif

//-----------------------------------------------------------------------------
// THE WORLD ITSELF. The two members above are named in the initialiser list,
// which is what makes their owner pointer valid before the body below runs --
// and the body hands &m_Networkable to edict 0.
//-----------------------------------------------------------------------------
CWorld::CWorld( )
	: m_Collideable( this )
	, m_Networkable( this )
{
	// THE SINGLETON IS CLAIMED HERE, AND STOCK CLAIMED IT IN Precache().
	//
	// FOUND BY THE LIVE RUN AND NOT BY READING, which is the whole reason
	// SourceSharpWorld_Report ships. With the assignment in Precache, this is
	// the sequence:
	//
	//   CreateEntityByName( "worldspawn" )      <- the object exists
	//   ParseMapData -> CWorld::KeyValue x N    <- the keys arrive HERE
	//   DispatchSpawn -> Spawn -> Precache      <- g_WorldEntity = this
	//
	// so during the whole of the key parse GetWorldEntity() answered NULL --
	// and SourceSharp_EntityGetDataBlob finds the world's blob by identity
	// against exactly that. Every key was refused with
	// "entity 0 ('worldspawn') offers 0", the world networked its
	// zero-initialised blob, and the client dutifully decoded ten zeroes. No
	// crash, no assert, and both sides agreeing about nothing.
	//
	// Moved rather than duplicated: two writes of one fact is the shape this
	// tree keeps paying for. It is also strictly more correct on its own terms
	// -- the world entity exists from construction, and stock's window in which
	// GetWorldEntity() answers NULL while a CWorld is alive was a latent hole
	// nothing happened to look through.
	g_WorldEntity = this;

	// THE STATE, SET HERE BECAUSE IT NEVER CHANGES AGAIN.
	//
	// `AddEFlags( EFL_NO_AUTO_EDICT_ATTACH | EFL_KEEP_ON_RECREATE_ENTITIES )`
	// STOOD HERE AND IS GONE WITH ITS SUBJECT. Both flags were instructions to
	// CBaseEntity's own machinery -- "do not let PostConstructor give me an
	// edict, I am claiming a specific one" and "survive RecreateEntities" --
	// and there is no CBaseEntity machinery to instruct. The first is now true
	// by construction: nothing but AttachToEdictZero below ever gives this
	// object an edict.
	m_pEdict       = NULL;
	m_nModelIndex  = 0;
	m_iszModelName = NULL_STRING;

	// THE SOLID TYPE, THE BOX, THE TRANSFORM AND THE PVS INFO ARE NOT SET HERE
	// ANY MORE, and they are not lost: each one is state that only one of the
	// two engine interfaces ever read, so it went inside the member that
	// answers for it. Both members are constructed before this body runs.

	AttachToEdictZero();

	// THE ACTIVITY AND EVENT REGISTRIES ARE GONE -- plan_shim.md Phase 2d.
	//
	// ActivityList_Init() and EventList_Init() opened two native name tables
	// that a per-level crossing then filled from managed code, so that
	// game/shared/animation.cpp could write an ORDINAL into mdl memory for a
	// later native reader to compare. Nothing compares ordinals now:
	// precache_model.cpp reads event NAMES and the sequence/activity lookups
	// are one managed rule over a ModelDescriptor. The tables, the registry
	// (shell_animlists.cpp), its header, animation.cpp and the 2,048-line
	// ActivityCatalog.cs that fed it all went together.
	//
	// The names are not lost: EventCatalog.cs keeps the fifty event names,
	// which is the half managed code reads itself.

	// `m_bColdWorld = false;` STOOD HERE AND IS NOT LOST, IT IS ALREADY TRUE.
	// The blob is part of the allocation and PvAllocEntPrivateData returns
	// zeroed memory, so every one of the ten networked values starts at the zero
	// stock's members started at -- and this was the only constructor assignment
	// among them, to the value zero already is. Writing it here now would be a
	// blob write before the field offsets have been asked for.
}

CWorld::~CWorld( )
{
	// Nothing to free: the activity and event registries went in Phase 2d.
	// See the note where they used to be opened.
	// THE LEVEL-SHUTDOWN EVENT IS NOT SENT FROM HERE, AND DELIBERATELY SO.
	//
	// This used to read `g_pGameRules->LevelShutdown();` and that call did
	// nothing: CGameRules::LevelShutdown is `{ return; }` in gamerules.h:265 and
	// no rules class in this build overrides it. It was a dead native virtual,
	// not a signal anything acted on.
	//
	// The live one goes to C#, from CSourceSharpSystem::LevelShutdownPostEntity,
	// which resolves every IGameLifecycle out of the container and calls them in
	// sequence. It is NOT moved here, because here is the wrong moment:
	// CServerGameDLL::LevelShutdown (gameinterface.cpp:1489) runs the pre-entity
	// systems, then gEntList.Clear() -- which is what destroys this object and
	// runs this destructor -- and only then the post-entity ones. Dispatching
	// from here would land the managed teardown IN THE MIDDLE of the entity
	// purge, with ServerModule.LevelShutdown clearing the managed entity
	// registry while native entities are still being destroyed and still
	// calling back into it. Post-entity is after the list is empty, which is
	// what makes dropping the registry safe.
	//
	// What is left here is the rules object's LIFETIME, which really is this
	// destructor's business: the world owns it and nothing else frees it.
	if ( g_pGameRules )
	{
		delete g_pGameRules;
		g_pGameRules = NULL;
	}

	// THE REGISTRATIONS AttachToEdictZero MADE, UNDONE -- and until this class
	// un-derived, ~CBaseEntity and ~CServerNetworkProperty did every line of it.
	//
	// Last in the body, after the rules object, because everything above may
	// still name this entity and nothing below can: a destructor that tears its
	// own handle down first is one that cannot be looked up from the code it
	// then runs.
	//
	// Mirrors ~CStandaloneEntity (shell_standalone_base.cpp:705), in its order,
	// with ONE difference and it is the edict:

	// THE HANDLE GOES AND NOTHING ELSE HAS TO BE TOLD -- plan_entity.md §3.3,
	// lane entlist-server. This read
	//
	//     <the entity list>->RemoveEntity( m_RefEHandle );
	//     m_RefEHandle.Term();
	//
	// where the first line was what stopped the next map's
	// AddNetworkableEntity( ..., 0 ) adding over a live entry. There is no entry
	// to free now: the world is not in the slot table and is not in the list,
	// and the only thing that resolved this handle was
	// SourceSharp_WorldFromHandle comparing it against this member. Terminating
	// it is therefore the whole of the removal -- every handle to the dead world
	// stops matching on the same instruction, which is what RemoveEntity's
	// serial bump bought and this gets for free.
	m_RefEHandle.Term();

	// The clusters BuildEntityClusterList allocated. ~CServerNetworkProperty
	// makes exactly this call and the world is FL_EDICT_ALWAYS, so it has them.
	engine->CleanUpEntityClusterList( m_Networkable.PVSInfo() );

	if ( m_pEdict )
	{
		// THE POINTERS GO AND THE EDICT STAYS, which is where this parts from
		// CServerNetworkProperty::DetachEdict -- that calls engine->RemoveEdict
		// unconditionally.
		//
		// Edict 0 is not this object's to return. AttachToEdictZero took it with
		// INDEXENT( 0 ) rather than engine->CreateEdict(), so nothing here
		// allocated it, and handing it to the engine's free list would let the
		// next CreateEdict hand THE WORLD'S INDEX to an ordinary entity.
		// CStandaloneEntity draws the same line with m_bOwnsEdict and cites the
		// same precedent: a player's edict is the engine's, and stock's
		// ClientDisconnect unhooks the pointers and leaves the slot alone.
		//
		// SetEdict( NULL ) BEFORE the networkable, and both before the object
		// goes: the edict outlives this and FreeContainingEntity dereferences
		// whatever it still holds.
		m_pEdict->SetEdict( NULL, false );
		m_pEdict->m_pNetworkable = NULL;
		m_pEdict = NULL;
	}

	g_WorldEntity = NULL;
}


//------------------------------------------------------------------------------
// Purpose : Add a decal to the world
// Input   :
// Output  :
//------------------------------------------------------------------------------
void CWorld::DecalTrace( trace_t *pTrace, char const *decalName)
{
	int index = ss::DecalIndexForName( decalName );
	if ( index < 0 )
		return;

	ss::CBroadcastRecipientFilter filter;


	if ( pTrace->hitbox != 0 )
	{
		TE_Decal( filter, 0.0f, &pTrace->endpos, &pTrace->startpos, 0, pTrace->hitbox, index );
	}
	else
	{
		TE_WorldDecal( filter, 0.0, &pTrace->endpos, index );
	}
}


//-----------------------------------------------------------------------------
// EDICT 0, TAKEN DIRECTLY -- which is verdict 2's ground 1, and it turns out to
// be four lines.
//
// The verdict called this "the single hardest blocker", and its reasoning was
// exact for the shape it was reasoning about: `NetworkProp()->AttachEdict(
// INDEXENT( 0 ) )` reaches CServerNetworkProperty, the parallel hierarchy has
// no NetworkProp at all, and CStandaloneEntity::PostConstruct calls
// engine->CreateEdict() -- so it "can take AN edict but has no route to THE one
// the engine assumes".
//
// WHAT DISSOLVES IT IS THAT THE WORLD IS ON NEITHER HIERARCHY. It does not have
// to reach edict 0 through CServerNetworkProperty and it does not have to reach
// it through CreateEdict; it names the edict it wants. INDEXENT( 0 ) is the
// address of a slot the engine has already allocated, and every one of the four
// things AttachEdict does to it is a plain assignment:
//
//   * SetEdict( this, true )       -- the edict's IServerUnknown, FL_EDICT_FULL
//   * m_EdictIndex                 -- the index, which is 0 by construction
//   * m_pNetworkable               -- the SECOND, separate pointer, which
//                                     CBaseEdict::AreaNum and GetClassName
//                                     read UNGUARDED. Missing it is a jump to
//                                     address zero deep in the engine the
//                                     moment anything is sent; CStandaloneEntity
//                                     records the same line costing an afternoon
//   * AddNetworkableEntity( this, 0 ) -- the entity list, which is what makes a
//                                     handle to the world resolve at all
//
// The verdict's ground was true of the two routes it compared. It was never a
// property of edict 0.
//
// FL_EDICT_ALWAYS, SET HERE AND NOT LEFT TO ShouldTransmit. CBaseEntity decides
// transmission each frame through CServerNetworkProperty; this object has no
// such property, and CServerGameEnts::CheckTransmit's arm for an edict that is
// not a CBaseEntity says so in as many words -- it is "decided on visibility
// alone, because that is all such an entity has to offer". For worldspawn the
// answer is always yes, so the flag is the whole of the decision.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE WORLD'S SERIAL COUNTER -- the storage; the rule is
// SourceSharp_NextWorldSerial in slot_table.h.
//
// One function-local static, which is plan.md §1a's shape for "one object,
// owned by the code that hands it out" and the same shape
// SourceSharp_SlotTable() uses.
//
// IT DOES NOT RESET BETWEEN MAPS, and that is deliberate rather than an
// omission. CGlobalEntityList never re-seeded either: it drew every entry's
// serial once at static construction and bumped on removal, so the world's
// serial climbed by one per map for the life of the process. A counter that
// went back to its start on a level change would hand map N+1's world the same
// handle map N's had -- and a client that had not yet been told otherwise would
// resolve the dead one.
//-----------------------------------------------------------------------------
static int SourceSharp_WorldIssueSerial()
{
	static unsigned short s_nWorldSerial = 0;

	s_nWorldSerial = SourceSharp_NextWorldSerial( s_nWorldSerial );

	return (int)s_nWorldSerial;
}

void CWorld::AttachToEdictZero()
{
	m_pEdict = ss::INDEXENT( RequiredEdictIndex() );

	if ( !m_pEdict )
	{
		Warning( "[SourceSharp] worldspawn: edict %d does not exist. The world "
		         "will not be sent and nothing will resolve a handle to it.\n",
			RequiredEdictIndex() );
		return;
	}

	m_pEdict->SetEdict( static_cast< IServerUnknown * >( this ), true );
	m_pEdict->m_EdictIndex   = RequiredEdictIndex();

	// THE SECOND POINTER, AND IT IS THE MEMBER NOW. CBaseEdict::AreaNum and
	// GetClassName read this field unguarded, so what it points at has to be a
	// live IServerNetworkable for as long as the edict names this world --
	// which is exactly the member's life, because the member IS this object.
	// ~CWorld nulls it again before the member goes.
	m_pEdict->m_pNetworkable = &m_Networkable;

	// Always sent. See the comment above for why this is a flag rather than a
	// per-frame answer.
	m_pEdict->m_fStateFlags &= ~( FL_EDICT_DONTSEND | FL_EDICT_PVSCHECK | FL_EDICT_FULLCHECK );
	m_pEdict->m_fStateFlags |= FL_EDICT_ALWAYS;

	// THE WORLD ISSUES ITS OWN HANDLE -- plan_entity.md §3.3, lane
	// entlist-server. This read
	//
	//     SetRefEHandle( <the entity list>->AddNetworkableEntity( this, 0 ) )
	//
	// and it was one of the three registrations that made CGlobalEntityList the
	// port's handle table rather than the SDK's list. What the list contributed
	// was a serial and a slot; the slot is edict 0 and always has been, and the
	// serial is below.
	//
	// NOT THE SLOT TABLE, and world_subject.h has the reason in full: the table
	// binds SHELLS -- one type in and that type out, which is what makes its
	// single static_cast sound -- and this object is a CWorld. One entity at a
	// fixed index does not need a table; it needs a handle, which it already
	// stores, and SourceSharp_WorldFromHandle compares against it.
	SetRefEHandle( CBaseHandle( RequiredEdictIndex(), SourceSharp_WorldIssueSerial() ) );
}

//-----------------------------------------------------------------------------
// The brush the whole level is. Asked through modelinfo exactly as
// CBaseEntity::GetModel() did, because the index is the same index -- and the
// index is the OWNER'S, so there is one place it lives.
//
// One of the two collideable methods a live session ever enters, and both of
// its callers are in this file: CWorld::Spawn asks for the model's name and for
// its bounds. The engine never asks.
//-----------------------------------------------------------------------------
const model_t *CWorldCollideable::GetCollisionModel()
{
	return modelinfo->GetModel( GetCollisionModelIndex() );
}

//-----------------------------------------------------------------------------
void CWorld::Spawn( void )
{
	// The origin and the angles are the constructor's and are not re-set here:
	// `SetLocalOrigin( vec3_origin )` and `SetLocalAngles( vec3_angle )` were
	// telling CBaseEntity's transform machinery about a frame that never moves,
	// and there is no such machinery to tell. The members already hold those
	// values and the transform is already the identity.

	// NOTE:  SHOULD NEVER BE ANYTHING OTHER THAN 1!!!
	SetModelIndex( 1 );

	// world model
	m_iszModelName = AllocPooledString(
		modelinfo->GetModelName( m_Collideable.GetCollisionModel() ) );

	// `AddFlag( FL_WORLDBRUSH )` STOOD HERE AND HAS NO SUBJECT LEFT. m_fFlags
	// is CBaseEntity's, and FL_WORLDBRUSH's readers -- all of them -- ask a
	// CBaseEntity for it. Measured over the 150 .cpp this server compiles,
	// FL_WORLDBRUSH is named in ZERO of them, so the flag was already being set
	// for nobody. Recorded rather than carried: adding a member to hold a bit
	// nothing reads would be state pretending to be behaviour.

	// The box, from the model the index names. This is what an ICollideable is
	// obliged to answer and the constructor could not know yet, so it is handed
	// to the member that answers for it.
	if ( const model_t *pModel = m_Collideable.GetCollisionModel() )
	{
		Vector vecMins, vecMaxs;
		modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );
		m_Collideable.SetBounds( vecMins, vecMaxs );
	}

	// THE PER-LEVEL FLUSH OF THE ENTITY I/O QUEUE, WHICH IS MANAGED NOW.
	//
	// Stock's call here was one line -- Init() is Clear() -- and it cleared the
	// native queue that cbase.cpp owns. That file leaves the vpc armed, and so
	// does every producer that could post into it, so there is no native queue
	// left to flush.
	//
	// The queue that replaced it is the managed directory's, and it IS cleared
	// per level: the managed level shutdown clears the directory, which clears
	// its I/O queue in turn. Both of the transitions this game makes -- a map
	// change and a map reload -- run that shutdown on the OUTGOING level, so the
	// incoming level always starts empty, and the first map after a DLL load
	// starts empty by construction.
	//
	// ONE ASYMMETRY, RECORDED RATHER THAN FIXED. Stock cleared at level ENTRY
	// and the managed side clears at level EXIT. That is equivalent for every
	// transition the game can make, and it is deliberately NOT "fixed" by
	// clearing at level init: clearing the directory there was tried and was
	// wrong, because map entities spawn DURING the load and a level-init clear
	// throws away everything the map has just bound. A queue-only clear at that
	// point has a narrower version of the same bug -- an entity with a delayed
	// spawn output queues during the load and would be discarded.
	Precache( );

	// THE TWO PLATFORM GLOBALS STOOD HERE and are seeded managed-side now, in
	// GlobalStateTable.OnLevelInitPreEntity:
	//
	//     GlobalEntity_Add( "is_console", mapname, IsConsole()  ? ON : OFF );
	//     GlobalEntity_Add( "is_pc",      mapname, !IsConsole() ? ON : OFF );
	//
	// They were the LAST live caller of game/server/globalstate.cpp -- eleven
	// forwarders over an ABI boundary to a table that has been managed since
	// plan_entity.md's Phase 4 exit, and these two names were the only traffic
	// on it. Moving the declaration to the table's own level-init stage is what
	// let that translation unit leave the build.
	//
	// SAME MOMENT AND SAME VALUES. GlobalStateTable.Add returns the existing
	// index for a name it already holds and changes nothing about the entry, so
	// declaring them once per level init is what stock did and is idempotent the
	// second time exactly as it was here. IsConsole() is a preprocessor constant
	// and false for every platform this port builds for.

	// WHAT THE MAP ACTUALLY SET, ONCE PER LEVEL LOAD.
	//
	// HERE and not earlier, because here is after the keys: mapentities.cpp
	// parses worldspawn's keyvalues -- which is where CWorld::KeyValue hands
	// each of them to C# -- and only then calls DispatchSpawn on the world
	// (mapentities.cpp:665). Printing from the constructor would print ten
	// zeroes and read like a working world with an empty map.
	//
	// It is the SERVER half of the only live evidence these values move at all;
	// C_World::OnDataChanged prints the other. See SourceSharpWorld_Report for
	// why a shipped line rather than a probe.
	ReportNetState();
}

//-----------------------------------------------------------------------------
// The ten, through the shared formatter both sides print through.
//
// A method rather than two call sites, because the console command below asks
// the same question CWorld::Spawn does and a second copy of the arithmetic is
// how the two answers would come to differ.
//-----------------------------------------------------------------------------
void CWorld::ReportNetState()
{
	ResolveFields();
	SourceSharpWorld_Report( "Server", DataBlob(), BlobBytes(), m_Fields );
}


void CWorld::Precache( void )
{
	// `g_WorldEntity = this;` STOOD HERE AND IS IN THE CONSTRUCTOR NOW. See
	// there for why: the map's keys arrive before this runs, and the blob they
	// are stored in is found by identity against this pointer.
	// Set up game rules. The native rules object stays while native code reads
	// g_pGameRules; its Init answered true and did nothing, and is no longer
	// called (plan_server_native_v2 T4e).
	Assert( !g_pGameRules );
	if (g_pGameRules)
	{
		delete g_pGameRules;
	}

	InstallGameRules();
	Assert( g_pGameRules );

	ss::SoundEnt_Init();

	// THE REST OF STOCK'S SEQUENCE IS MANAGED'S (plan_server_native_v2 T4e):
	// ServerGameDll.MapLoadPreEntity sets sv_stepsize and room_type, opens the
	// precache window, runs the pre-entity pass -- which stock reaches from right
	// here, inside the world entity's own precache, and whose first row,
	// CPhysicsHook, creates physenv -- and then precaches what the world always
	// had: the sprites and particles W_Precache named, the player model
	// ClientGamePrecache did, the gibs, the water sounds and the six effect
	// sounds the precache register's one registrant did. The no-op rules calls
	// (CreateStandardEntities, Precache, InitDefaultAIRelationships) are not
	// made. Native keeps what native state holds: the sentence file's
	// once-per-process read and the four sprites whose indices the native
	// effect helpers read.
	SourceSharp_ManagedLevelInitPreEntity( STRING( gpGlobals->mapname ) );

	// init sentence group playback stuff from sentences.txt.
	// ok to call this multiple times, calls after first are ignored.
	SENTENCEG_Init();

	W_Precache();
}

// cs_sv_world (the world's networked state, on demand) is a diagnostic: it
// lives in world_adapter_debug.cpp, which only a debug build compiles.
// cs_sv_trace_subject (which hierarchy a trace came back with) went at
// plan_server_native_v2 T4f with the native trace filter it borrowed, and
// GetWorldBounds and ImpactCount, which only it read, with it.

// CPRECACHE_REGISTER WAS HERE (game/shared/precache_register.cpp, folded in at
// plan_server_all.md Wave 2): a static-init list of "call me at level load"
// callbacks with ONE registrant, shell_absent.cpp's EffectsPrecache (six game
// sounds). The six are managed's world precache since plan_server_native_v2
// T4e, so the list, its walk and its header went with them.
