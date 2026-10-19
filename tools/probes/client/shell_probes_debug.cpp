//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client no-Valve C0 probes (debug builds only) ===//
//
// DEBUG BUILDS ONLY, AND OPT-IN: compiled into client.so only when
// farm/debug/build is configured with -DSS_CLIENT_PROBES=ON (see
// src/client/native/CMakeLists.txt and tools/probes/c0-session). Measurement
// code for plan_client_novalve.md §6 C0 item 7; the answers are in its §8.
// Nothing here runs unless the probe log is named:
//
//   SOURCESHARP_PROBE_LOG=<file>      every probe appends one line per event:
//                                     "<monotonic ns> <pid> client <event...>"
//   SOURCESHARP_PROBE_NO_VR=1         take ClientVirtualReality001 off the
//                                     client's interface list (C0 probe 2)
//
// Probes:
//   1. iface      every CreateInterface the client's list answers, by name, in
//                 order, with the module that asked (the GameUI011 vs
//                 VClient017 order is read against tools/probes' GameUI stub,
//                 which writes the same log).
//   2. pq         a counting forwarder over the stock IParticleSystemQuery,
//                 swapped into g_pParticleSystemQuery before the particle
//                 manager's Init hands it to particles.a; dumps per-method
//                 counts every 5 s of activity and on cs_cl_probe_pq.
//   3. paint      cs_cl_probe_paint: ISurface::PaintTraverseEx counted, one
//                 line per call (the stub's UpdateProgressBar lines bracket
//                 the load).
//   4. texture    cs_cl_probe_texture: a panel showing one texture uploaded
//                 ONCE through DrawSetTextureRGBA, to be screenshotted across
//                 mat_setvideomode.
//   5. html       (removed: cs_cl_probe_html drove ISteamHTMLSurface through
//                 the native Steam API, which left client.so with Steam's move
//                 to managed; its finding is plan_client_novalve.md's item 5)
//
//=============================================================================//

#include "cbase.h"
#include "tier1/interface.h"
#include "particles/particles.h"
#include "vgui/ISurface.h"
#include "vgui_controls/Controls.h"
#include "shell_rawpanel_client.h"
#include "iclientmode.h"
#include "ienginevgui.h"
#include "vgui/IVGui.h"
#include "vgui/IPanel.h"
#include "shell_new_particles.h"
#include "view.h"

#include <dlfcn.h>
#include <link.h>
#include <execinfo.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>

#include "tier0/memdbgon.h"

namespace
{

//-----------------------------------------------------------------------------
// The log. One write(2) per line on an O_APPEND descriptor, so lines from the
// GameUI stub (same process, another module) interleave whole.
//-----------------------------------------------------------------------------
int ProbeFd()
{
	static int s_fd = -2;
	if ( s_fd == -2 )
	{
		const char *pPath = getenv( "SOURCESHARP_PROBE_LOG" );
		s_fd = ( pPath && *pPath ) ? open( pPath, O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC, 0644 ) : -1;
	}
	return s_fd;
}

bool ProbeOn()
{
	return ProbeFd() >= 0;
}

long long NowNs()
{
	timespec ts;
	clock_gettime( CLOCK_MONOTONIC, &ts );
	return (long long)ts.tv_sec * 1000000000LL + ts.tv_nsec;
}

void ProbeLog( const char *pFmt, ... ) __attribute__(( format( printf, 1, 2 ) ));
void ProbeLog( const char *pFmt, ... )
{
	int fd = ProbeFd();
	if ( fd < 0 )
		return;
	char buf[ 2048 ];
	int n = snprintf( buf, sizeof( buf ), "%lld %d client ", NowNs(), (int)getpid() );
	va_list ap;
	va_start( ap, pFmt );
	int m = vsnprintf( buf + n, sizeof( buf ) - n - 1, pFmt, ap );
	va_end( ap );
	if ( m < 0 )
		m = 0;
	n += ( m < (int)sizeof( buf ) - n - 1 ) ? m : (int)sizeof( buf ) - n - 2;
	buf[ n++ ] = '\n';
	ssize_t w = write( fd, buf, n );
	(void)w;
}

const char *ModuleOf( const void *pAddr )
{
	Dl_info info;
	if ( !pAddr || !dladdr( pAddr, &info ) || !info.dli_fname )
		return "?";
	const char *pSlash = strrchr( info.dli_fname, '/' );
	return pSlash ? pSlash + 1 : info.dli_fname;
}

// The first module on the stack that is not this one: who asked.
const char *FirstForeignCaller()
{
	void *frames[ 16 ];
	int n = backtrace( frames, 16 );
	const char *pSelf = ModuleOf( (const void *)&ModuleOf );
	for ( int i = 1; i < n; i++ )
	{
		const char *pMod = ModuleOf( frames[ i ] );
		if ( strcmp( pMod, pSelf ) )
			return pMod;
	}
	return pSelf;
}

//=============================================================================
// 1. iface: who asks the client's CreateInterface for what, in what order.
//=============================================================================
const int kMaxRegs = 64;
InstantiateInterfaceFn g_OrigCreate[ kMaxRegs ];
const char *g_RegName[ kMaxRegs ];

void HookClientDll( void *pClient );

template < int I >
void *LoggedCreate()
{
	ProbeLog( "iface %s from %s", g_RegName[ I ], FirstForeignCaller() );
	void *p = g_OrigCreate[ I ]();
	if ( p && !strcmp( g_RegName[ I ], CLIENT_DLL_INTERFACE_VERSION ) )
		HookClientDll( p );
	return p;
}

template < int... Is >
struct ThunkTable
{
	static constexpr InstantiateInterfaceFn s_Fns[ sizeof...( Is ) ] = { &LoggedCreate< Is >... };
};

template < int N, int... Is >
struct MakeThunks : MakeThunks< N - 1, N - 1, Is... > {};
template < int... Is >
struct MakeThunks< 0, Is... > { typedef ThunkTable< Is... > Type; };

typedef MakeThunks< kMaxRegs >::Type Thunks;

void InstallInterfaceProbe()
{
	int nRegs = 0;
	int nWrapped = 0;
	bool bSawVr = false;
	const bool bNoVr = getenv( "SOURCESHARP_PROBE_NO_VR" ) && atoi( getenv( "SOURCESHARP_PROBE_NO_VR" ) ) != 0;

	InterfaceReg **ppLink = &InterfaceReg::s_pInterfaceRegs;
	while ( *ppLink )
	{
		InterfaceReg *pReg = *ppLink;
		nRegs++;
		if ( !strcmp( pReg->m_pName, "ClientVirtualReality001" ) )
		{
			bSawVr = true;
			if ( bNoVr )
			{
				*ppLink = pReg->m_pNext;
				ProbeLog( "iface-unexposed %s", pReg->m_pName );
				continue;
			}
		}
		if ( nWrapped < kMaxRegs )
		{
			g_OrigCreate[ nWrapped ] = pReg->m_CreateFn;
			g_RegName[ nWrapped ] = pReg->m_pName;
			pReg->m_CreateFn = Thunks::s_Fns[ nWrapped ];
			nWrapped++;
		}
		else
		{
			ProbeLog( "iface-unwrapped %s (more than %d registrations)", pReg->m_pName, kMaxRegs );
		}
		ppLink = &pReg->m_pNext;
	}

	ProbeLog( "iface-list registrations=%d wrapped=%d vr_registered=%d no_vr=%d", nRegs, nWrapped, bSawVr ? 1 : 0, bNoVr ? 1 : 0 );

	// Proof the removal took: ask the list itself.
	int rc = -1;
	void *pVr = CreateInterface( "ClientVirtualReality001", &rc );
	ProbeLog( "iface-check ClientVirtualReality001 -> %s (rc=%d)", pVr ? "answered" : "NULL", rc );
}

//-----------------------------------------------------------------------------
// IBaseClientDLL::Init and PostInit, timed: Init is where the client brings
// the CLR up (LoadManagedRuntime), so its time against the GameUI stub's
// Initialize says whether GameUI is initialised before the managed root.
//-----------------------------------------------------------------------------
bool PatchSlot( void **pVtable, int slot, void *pNew, void **ppOld );
template < typename PMF > int VtableSlot( PMF pmf );

typedef int ( *ClientInitFn )( IBaseClientDLL *, CreateInterfaceFn, CreateInterfaceFn, CGlobalVarsBase * );
typedef void ( *ClientPostInitFn )( IBaseClientDLL * );
ClientInitFn g_pOrigClientInit;
ClientPostInitFn g_pOrigClientPostInit;

int HookClientInit( IBaseClientDLL *pThis, CreateInterfaceFn a, CreateInterfaceFn b, CGlobalVarsBase *g )
{
	ProbeLog( "client-dll Init enter" );
	int r = g_pOrigClientInit( pThis, a, b, g );
	ProbeLog( "client-dll Init exit ret=%d", r );
	return r;
}

void HookClientPostInit( IBaseClientDLL *pThis )
{
	ProbeLog( "client-dll PostInit enter" );
	g_pOrigClientPostInit( pThis );
	ProbeLog( "client-dll PostInit exit" );
}

void HookClientDll( void *pClient )
{
	if ( g_pOrigClientInit )
		return;
	void **pVtable = *(void ***)pClient;
	void *pOld = NULL;
	bool bInit = PatchSlot( pVtable, VtableSlot( &IBaseClientDLL::Init ), (void *)&HookClientInit, &pOld );
	g_pOrigClientInit = (ClientInitFn)pOld;
	bool bPost = PatchSlot( pVtable, VtableSlot( &IBaseClientDLL::PostInit ), (void *)&HookClientPostInit, &pOld );
	g_pOrigClientPostInit = (ClientPostInitFn)pOld;
	ProbeLog( "client-dll hooked Init=%d PostInit=%d", bInit ? 1 : 0, bPost ? 1 : 0 );
}

//=============================================================================
// 2. pq: which IParticleSystemQuery methods particles.a calls.
//=============================================================================
enum PqMethod
{
	PQ_Connect, PQ_Disconnect, PQ_QueryInterface, PQ_Init, PQ_Shutdown,
	PQ_GetLightingAtPoint, PQ_TraceLine, PQ_MovePointInsideControllingObject,
	PQ_IsPointInControllingObjectHitBox, PQ_GetCollisionGroupFromName,
	PQ_GetRandomPointsOnControllingObjectHitBox, PQ_GetControllingObjectHitBoxInfo,
	PQ_GetLocalPlayerPos, PQ_GetLocalPlayerEyeVectors, PQ_GetPixelVisibility,
	PQ_SetUpLightingEnvironment,
	PQ_COUNT
};

const char *const g_PqNames[ PQ_COUNT ] =
{
	"Connect", "Disconnect", "QueryInterface", "Init", "Shutdown",
	"GetLightingAtPoint", "TraceLine", "MovePointInsideControllingObject",
	"IsPointInControllingObjectHitBox", "GetCollisionGroupFromName",
	"GetRandomPointsOnControllingObjectHitBox", "GetControllingObjectHitBoxInfo",
	"GetLocalPlayerPos", "GetLocalPlayerEyeVectors", "GetPixelVisibility",
	"SetUpLightingEnvironment",
};

long long g_PqCounts[ PQ_COUNT ];
long long g_PqLastDumpNs;
long long g_PqTotalAtDump;

void PqDump( const char *pWhy )
{
	char buf[ 1536 ];
	int n = 0;
	long long total = 0;
	for ( int i = 0; i < PQ_COUNT; i++ )
	{
		total += g_PqCounts[ i ];
		n += snprintf( buf + n, sizeof( buf ) - n, " %s=%lld", g_PqNames[ i ], g_PqCounts[ i ] );
	}
	g_PqTotalAtDump = total;
	g_PqLastDumpNs = NowNs();
	ProbeLog( "pq-counts (%s) total=%lld%s", pWhy, total, buf );
}

void PqCount( PqMethod m )
{
	g_PqCounts[ m ]++;
	if ( NowNs() - g_PqLastDumpNs > 5000000000LL )
		PqDump( "periodic" );
}

class CProbeParticleQuery : public IParticleSystemQuery
{
public:
	explicit CProbeParticleQuery( IParticleSystemQuery *pInner ) : m_pInner( pInner ) {}

	virtual bool Connect( CreateInterfaceFn factory ) OVERRIDE { PqCount( PQ_Connect ); return m_pInner->Connect( factory ); }
	virtual void Disconnect() OVERRIDE { PqCount( PQ_Disconnect ); m_pInner->Disconnect(); }
	virtual void *QueryInterface( const char *pName ) OVERRIDE { PqCount( PQ_QueryInterface ); return m_pInner->QueryInterface( pName ); }
	virtual InitReturnVal_t Init() OVERRIDE { PqCount( PQ_Init ); return m_pInner->Init(); }
	virtual void Shutdown() OVERRIDE { PqCount( PQ_Shutdown ); m_pInner->Shutdown(); }

	virtual void GetLightingAtPoint( const Vector &vecOrigin, Color &tint ) OVERRIDE
	{
		PqCount( PQ_GetLightingAtPoint );
		m_pInner->GetLightingAtPoint( vecOrigin, tint );
	}
	virtual void TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
		const class IHandleEntity *ignore, int collisionGroup, CBaseTrace *ptr ) OVERRIDE
	{
		PqCount( PQ_TraceLine );
		m_pInner->TraceLine( vecAbsStart, vecAbsEnd, mask, ignore, collisionGroup, ptr );
	}
	virtual bool MovePointInsideControllingObject( CParticleCollection *pParticles, void *pObject, Vector *pPnt ) OVERRIDE
	{
		PqCount( PQ_MovePointInsideControllingObject );
		return m_pInner->MovePointInsideControllingObject( pParticles, pObject, pPnt );
	}
	virtual bool IsPointInControllingObjectHitBox( CParticleCollection *pParticles, int nControlPointNumber,
		Vector vecPos, bool bBBoxOnly ) OVERRIDE
	{
		PqCount( PQ_IsPointInControllingObjectHitBox );
		return m_pInner->IsPointInControllingObjectHitBox( pParticles, nControlPointNumber, vecPos, bBBoxOnly );
	}
	virtual int GetCollisionGroupFromName( const char *pszCollisionGroupName ) OVERRIDE
	{
		PqCount( PQ_GetCollisionGroupFromName );
		return m_pInner->GetCollisionGroupFromName( pszCollisionGroupName );
	}
	virtual void GetRandomPointsOnControllingObjectHitBox( CParticleCollection *pParticles, int nControlPointNumber,
		int nNumPtsOut, float flBBoxScale, int nNumTrysToGetAPointInsideTheModel, Vector *pPntsOut,
		Vector vecDirectionBias, Vector *pHitBoxRelativeCoordOut, int *pHitBoxIndexOut ) OVERRIDE
	{
		PqCount( PQ_GetRandomPointsOnControllingObjectHitBox );
		m_pInner->GetRandomPointsOnControllingObjectHitBox( pParticles, nControlPointNumber, nNumPtsOut, flBBoxScale,
			nNumTrysToGetAPointInsideTheModel, pPntsOut, vecDirectionBias, pHitBoxRelativeCoordOut, pHitBoxIndexOut );
	}
	virtual int GetControllingObjectHitBoxInfo( CParticleCollection *pParticles, int nControlPointNumber,
		int nBufSize, ModelHitBoxInfo_t *pHitBoxOutputBuffer ) OVERRIDE
	{
		PqCount( PQ_GetControllingObjectHitBoxInfo );
		return m_pInner->GetControllingObjectHitBoxInfo( pParticles, nControlPointNumber, nBufSize, pHitBoxOutputBuffer );
	}
	virtual Vector GetLocalPlayerPos() OVERRIDE
	{
		PqCount( PQ_GetLocalPlayerPos );
		return m_pInner->GetLocalPlayerPos();
	}
	virtual void GetLocalPlayerEyeVectors( Vector *pForward, Vector *pRight, Vector *pUp ) OVERRIDE
	{
		PqCount( PQ_GetLocalPlayerEyeVectors );
		m_pInner->GetLocalPlayerEyeVectors( pForward, pRight, pUp );
	}
	virtual float GetPixelVisibility( int *pQueryHandle, const Vector &vecOrigin, float flScale ) OVERRIDE
	{
		PqCount( PQ_GetPixelVisibility );
		return m_pInner->GetPixelVisibility( pQueryHandle, vecOrigin, flScale );
	}
	virtual void SetUpLightingEnvironment( const Vector &pos ) OVERRIDE
	{
		PqCount( PQ_SetUpLightingEnvironment );
		m_pInner->SetUpLightingEnvironment( pos );
	}

private:
	IParticleSystemQuery *m_pInner;
};

} // namespace

// particlesystemquery.cpp's global, read by particlemgr.cpp when it calls
// g_pParticleSystemMgr->Init( g_pParticleSystemQuery ) during client Init --
// after every static initialiser, so the swap below is in place by then.
extern IParticleSystemQuery *g_pParticleSystemQuery;

namespace
{

void InstallParticleQueryProbe()
{
	static CProbeParticleQuery *s_pProbe = NULL;
	if ( s_pProbe || !g_pParticleSystemQuery )
	{
		ProbeLog( "pq-install skipped (probe=%p query=%p)", (void *)s_pProbe, (void *)g_pParticleSystemQuery );
		return;
	}
	s_pProbe = new CProbeParticleQuery( g_pParticleSystemQuery );
	g_pParticleSystemQuery = s_pProbe;
	ProbeLog( "pq-install wrapped the stock query" );
}

// Runs at static init; this TU is appended after every client source (the
// debug-only list is added last), so the interface list is complete.
struct CProbeInstaller
{
	CProbeInstaller()
	{
		if ( !ProbeOn() )
			return;
		ProbeLog( "probes-start" );
		InstallInterfaceProbe();
		InstallParticleQueryProbe();
	}
	~CProbeInstaller()
	{
		if ( ProbeOn() )
			PqDump( "exit" );
	}
} g_ProbeInstaller;

//-----------------------------------------------------------------------------
// The sweep: every particle definition the manager holds, started one at a
// time in front of the view for kSweepTicks ticks, with the query calls each
// one caused. An upper bound for probe 1: what the loaded .pcf set CAN ask.
//-----------------------------------------------------------------------------
// On CSourceSharpRawPanel since the C3 UI work removed vgui::Panel from
// client.so: a raw vgui2 panel only for its tick signal.
class CProbeParticleSweep : public CSourceSharpRawPanel
{
public:
	CProbeParticleSweep( vgui::VPANEL parent, int nFirst, int nCount )
		: CSourceSharpRawPanel( "SourceSharpProbeParticleSweep" ), m_nNext( nFirst )
	{
		SetParent( parent );
		m_nEnd = g_pParticleSystemMgr->GetParticleSystemCount();
		if ( nCount > 0 && nFirst + nCount < m_nEnd )
			m_nEnd = nFirst + nCount;
		SetVisible( true );
		SetBounds( 0, 0, 1, 1 );
		SetPaintEnabled( false );
		ProbeLog( "pq-sweep-start definitions=%d range=%d..%d", g_pParticleSystemMgr->GetParticleSystemCount(), nFirst, m_nEnd );
		vgui::ivgui()->AddTickSignal( GetVPanel(), 100 );
	}

	virtual void OnTick() OVERRIDE
	{
		if ( m_nTicks++ % kSweepTicks )
			return;
		Finish();
		if ( m_nNext >= m_nEnd )
		{
			ProbeLog( "pq-sweep-done" );
			PqDump( "sweep" );
			// The panel stays (a probe build's few bytes): deleting it inside
			// its own tick is the hazard MarkForDeletion used to defer.
			vgui::ivgui()->RemoveTickSignal( GetVPanel() );
			return;
		}
		const char *pName = g_pParticleSystemMgr->GetParticleSystemNameFromIndex( m_nNext++ );
		Q_strncpy( m_szName, pName ? pName : "", sizeof( m_szName ) );
		memcpy( m_Before, g_PqCounts, sizeof( m_Before ) );
		m_pEffect = CShellNewParticleEffect::Create( m_szName );
		if ( m_pEffect.IsValid() && m_pEffect->IsValid() )
		{
			Vector fwd;
			AngleVectors( MainViewAngles(), &fwd );
			Vector pos = MainViewOrigin() + fwd * 128.0f;
			for ( int cp = 0; cp < 4; cp++ )
				m_pEffect->SetControlPoint( cp, pos );
		}
	}

private:
	void Finish()
	{
		if ( !m_szName[ 0 ] )
			return;
		char buf[ 1024 ];
		int n = 0;
		for ( int i = 0; i < PQ_COUNT; i++ )
		{
			long long d = g_PqCounts[ i ] - m_Before[ i ];
			if ( d )
				n += snprintf( buf + n, sizeof( buf ) - n, " %s=%lld", g_PqNames[ i ], d );
		}
		buf[ n ] = 0;
		ProbeLog( "pq-system %s valid=%d%s", m_szName, ( m_pEffect.IsValid() && m_pEffect->IsValid() ) ? 1 : 0, buf );
		if ( m_pEffect.IsValid() )
			m_pEffect->StopEmission( false, true );
		m_pEffect = NULL;
		m_szName[ 0 ] = 0;
	}

	static const int kSweepTicks = 8;
	int m_nNext;
	int m_nEnd = 0;
	int m_nTicks = 0;
	char m_szName[ 256 ] = {};
	long long m_Before[ PQ_COUNT ];
	CSmartPtr< CShellNewParticleEffect > m_pEffect;
};

} // namespace

CON_COMMAND( cs_cl_probe_particle_sweep, "C0 probe (debug): start every particle definition in turn, logging the query calls each causes. [first] [count]" )
{
	const vgui::VPANEL parent = enginevgui ? enginevgui->GetPanel( PANEL_CLIENTDLL ) : 0;
	if ( !parent || !g_pParticleSystemMgr || !engine->IsInGame() )
	{
		ProbeLog( "pq-sweep-refused (not in a map)" );
		return;
	}
	new CProbeParticleSweep( parent, args.ArgC() > 1 ? atoi( args.Arg( 1 ) ) : 0, args.ArgC() > 2 ? atoi( args.Arg( 2 ) ) : 0 );
}

namespace
{

//=============================================================================
// 3. paint: ISurface::PaintTraverseEx, counted.
//=============================================================================
typedef void ( *PaintTraverseExFn )( vgui::ISurface *pThis, vgui::VPANEL panel, bool paintPopups );
PaintTraverseExFn g_pOrigPaintTraverseEx;
long long g_nPaints;

void HookPaintTraverseEx( vgui::ISurface *pThis, vgui::VPANEL panel, bool paintPopups )
{
	g_nPaints++;
	ProbeLog( "paint n=%lld from %s", g_nPaints, ModuleOf( __builtin_return_address( 0 ) ) );
	g_pOrigPaintTraverseEx( pThis, panel, paintPopups );
}

// The vtable slot of a virtual member: Itanium encodes a pointer to a virtual
// member function as 1 + its byte offset in the vtable.
template < typename PMF >
int VtableSlot( PMF pmf )
{
	struct Rep { ptrdiff_t ptr; ptrdiff_t adj; } rep;
	static_assert( sizeof( pmf ) == sizeof( rep ), "Itanium member pointer" );
	memcpy( &rep, &pmf, sizeof( rep ) );
	return ( rep.ptr & 1 ) ? (int)( ( rep.ptr - 1 ) / (ptrdiff_t)sizeof( void * ) ) : -1;
}

bool PatchSlot( void **pVtable, int slot, void *pNew, void **ppOld )
{
	long page = sysconf( _SC_PAGESIZE );
	uintptr_t addr = (uintptr_t)&pVtable[ slot ];
	uintptr_t base = addr & ~(uintptr_t)( page - 1 );
	if ( mprotect( (void *)base, page * 2, PROT_READ | PROT_WRITE ) != 0 )
		return false;
	*ppOld = pVtable[ slot ];
	pVtable[ slot ] = pNew;
	return true;
}

} // namespace

CON_COMMAND( cs_cl_probe_paint, "C0 probe (debug): count ISurface::PaintTraverseEx into SOURCESHARP_PROBE_LOG" )
{
	if ( !ProbeOn() || g_pOrigPaintTraverseEx )
		return;
	vgui::ISurface *pSurface = vgui::surface();
	int slot = VtableSlot( &vgui::ISurface::PaintTraverseEx );
	void *pOld = NULL;
	bool bOk = pSurface && slot >= 0 && PatchSlot( *(void ***)pSurface, slot, (void *)&HookPaintTraverseEx, &pOld );
	g_pOrigPaintTraverseEx = (PaintTraverseExFn)pOld;
	ProbeLog( "paint-install slot=%d ok=%d surface=%p module=%s", slot, bOk ? 1 : 0, (void *)pSurface, ModuleOf( pOld ) );
}

CON_COMMAND( cs_cl_probe_pq, "C0 probe (debug): write the IParticleSystemQuery call counts to SOURCESHARP_PROBE_LOG" )
{
	PqDump( "command" );
}

//=============================================================================
// 4. texture: one DrawSetTextureRGBA upload, drawn every frame.
//=============================================================================
namespace
{

class CProbeTexturePanel : public CSourceSharpRawPanel
{
public:
	explicit CProbeTexturePanel( vgui::VPANEL parent ) : CSourceSharpRawPanel( "SourceSharpProbeTexture" )
	{
		SetParent( parent );
		SetBounds( 16, 16, kSize, kSize );
		SetVisible( true );
		Upload();
	}

	// Four solid quadrants: top-left red, top-right green, bottom-right blue,
	// bottom-left white.
	void Upload()
	{
		static unsigned char s_Rgba[ kTex * kTex * 4 ];
		for ( int y = 0; y < kTex; y++ )
		{
			for ( int x = 0; x < kTex; x++ )
			{
				unsigned char *p = &s_Rgba[ ( y * kTex + x ) * 4 ];
				bool right = x >= kTex / 2, bottom = y >= kTex / 2;
				bool white = !right && bottom;
				p[ 0 ] = ( white || ( !right && !bottom ) ) ? 255 : 0;
				p[ 1 ] = ( white || ( right && !bottom ) ) ? 255 : 0;
				p[ 2 ] = ( white || ( right && bottom ) ) ? 255 : 0;
				p[ 3 ] = 255;
			}
		}
		m_nId = vgui::surface()->CreateNewTextureID( true );
		vgui::surface()->DrawSetTextureRGBA( m_nId, s_Rgba, kTex, kTex, 0, true );
		ProbeLog( "texture-upload id=%d size=%d", m_nId, kTex );
	}

	virtual void Paint() OVERRIDE
	{
		m_nPaints++;
		if ( m_nPaints % 300 == 1 )
			ProbeLog( "texture-paint n=%lld id=%d valid=%d screen=%dx%d", m_nPaints, m_nId,
				vgui::surface()->IsTextureIDValid( m_nId ) ? 1 : 0, ScreenWidth(), ScreenHeight() );
		vgui::surface()->DrawSetColor( 255, 255, 255, 255 );
		vgui::surface()->DrawSetTexture( m_nId );
		vgui::surface()->DrawTexturedRect( 0, 0, kSize, kSize );
	}

	virtual void OnScreenSizeChanged( int oldW, int oldH ) OVERRIDE
	{
		ProbeLog( "texture-screen-size-changed old=%dx%d new=%dx%d valid=%d", oldW, oldH, ScreenWidth(), ScreenHeight(),
			vgui::surface()->IsTextureIDValid( m_nId ) ? 1 : 0 );
		SetBounds( 16, 16, kSize, kSize );
	}

	static const int kTex = 64;
	static const int kSize = 256;

private:
	int m_nId = -1;
	long long m_nPaints = 0;
};

CProbeTexturePanel *g_pTexturePanel;

} // namespace

CON_COMMAND( cs_cl_probe_texture, "C0 probe (debug): show a texture uploaded once through DrawSetTextureRGBA" )
{
	if ( g_pTexturePanel )
	{
		ProbeLog( "texture-already-shown id-valid" );
		return;
	}
	const vgui::VPANEL parent = enginevgui ? enginevgui->GetPanel( PANEL_CLIENTDLL ) : 0;
	if ( !parent )
	{
		ProbeLog( "texture-no-viewport" );
		return;
	}
	g_pTexturePanel = new CProbeTexturePanel( parent );
	g_pTexturePanel->SetZPos( 1000 );
}

//=============================================================================
// 6. menu override: does GameUI honour IGameUI::SetMainMenuOverride?
//
// A panel parented to PANEL_GAMEUIDLL and created HIDDEN, handed to whichever
// GameUI.so the engine loaded (the first one in the link map; under the
// logging stub that is the stub, which logs the call and forwards it).
// Honoured means GameUI makes it visible when the main menu shows: the
// panel's first Paint is logged, and visibility is logged on each change.
// The slot is called raw, by its index in IGameUI (common/GameUI/IGameUI.h):
// SetMainMenuOverride is the 37th virtual, slot 36; the farm carries no
// IGameUI.h to name it by.
//=============================================================================
namespace
{

class CProbeMenuPanel : public CSourceSharpRawPanel
{
public:
	explicit CProbeMenuPanel( vgui::VPANEL parent ) : CSourceSharpRawPanel( "SourceSharpProbeMainMenu" )
	{
		SetParent( parent );
		int w, h;
		vgui::surface()->GetScreenSize( w, h );
		SetBounds( 0, 0, w, h / 2 );
		SetVisible( false );
		vgui::ivgui()->AddTickSignal( GetVPanel(), 250 );
	}

	virtual void OnTick() OVERRIDE
	{
		int v = IsVisible() ? 1 : 0;
		if ( v != m_nLastVisible )
		{
			m_nLastVisible = v;
			ProbeLog( "menu-override-visible %d parent=%#lx", v, (unsigned long)vgui::ipanel()->GetParent( GetVPanel() ) );
		}
	}

	virtual void Paint() OVERRIDE
	{
		if ( m_nPaints++ == 0 )
			ProbeLog( "menu-override-first-paint" );
		vgui::surface()->DrawSetColor( 255, 0, 255, 255 );
		int w, h;
		GetSize( w, h );
		vgui::surface()->DrawFilledRect( 0, 0, w, h );
	}

private:
	long long m_nPaints = 0;
	int m_nLastVisible = -1;
};

struct FindGameUi
{
	char m_szPath[ 1024 ];
};

int FindGameUiModule( struct dl_phdr_info *pInfo, size_t, void *pData )
{
	FindGameUi *pFind = (FindGameUi *)pData;
	if ( pFind->m_szPath[ 0 ] || !pInfo->dlpi_name )
		return 0;
	const char *pSlash = strrchr( pInfo->dlpi_name, '/' );
	if ( pSlash && !strcmp( pSlash + 1, "GameUI.so" ) )
		Q_strncpy( pFind->m_szPath, pInfo->dlpi_name, sizeof( pFind->m_szPath ) );
	return 0;
}

int ListGameUiModules( struct dl_phdr_info *pInfo, size_t, void * )
{
	if ( pInfo->dlpi_name && strstr( pInfo->dlpi_name, "GameUI.so" ) )
		ProbeLog( "gameui-module %s", pInfo->dlpi_name );
	return 0;
}

CProbeMenuPanel *g_pMenuPanel;

} // namespace

CON_COMMAND( cs_cl_probe_modules, "C0 probe (debug): log every loaded GameUI.so, in link-map order" )
{
	dl_iterate_phdr( &ListGameUiModules, NULL );
}

CON_COMMAND( cs_cl_probe_menu_override, "C0 probe (debug): hand GameUI a hidden panel through SetMainMenuOverride" )
{
	if ( g_pMenuPanel )
		return;
	FindGameUi find = {};
	dl_iterate_phdr( &FindGameUiModule, &find );
	void *h = find.m_szPath[ 0 ] ? dlopen( find.m_szPath, RTLD_NOW | RTLD_NOLOAD ) : NULL;
	CreateInterfaceFn fn = h ? (CreateInterfaceFn)dlsym( h, "CreateInterface" ) : NULL;
	void *pGameUi = fn ? fn( "GameUI011", NULL ) : NULL;
	ProbeLog( "menu-override module=%s gameui=%p", find.m_szPath[ 0 ] ? find.m_szPath : "(none)", pGameUi );
	if ( !pGameUi )
		return;
	g_pMenuPanel = new CProbeMenuPanel( enginevgui->GetPanel( PANEL_GAMEUIDLL ) );
	typedef void ( *SetOverrideFn )( void *pThis, vgui::VPANEL panel );
	SetOverrideFn pfn = (SetOverrideFn)( ( *(void ***)pGameUi )[ 36 ] );
	ProbeLog( "menu-override calling slot 36 (%s) panel=%#lx", ModuleOf( (void *)pfn ), (unsigned long)g_pMenuPanel->GetVPanel() );
	pfn( pGameUi, g_pMenuPanel->GetVPanel() );
	ProbeLog( "menu-override returned; visible=%d", g_pMenuPanel->IsVisible() ? 1 : 0 );
}
