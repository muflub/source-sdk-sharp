//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// tools/probes/client/c5proxies_probes_debug.cpp -- lane c5-proxies' oracle
// probe for the material proxies (plan_client_novalve.md §2 cluster 1, §5.1
// step 1: record from the code being REPLACED).
//
// Compiled only into a debug client.so configured with -DSS_CLIENT_PROBES=ON
// (tools/probes/c0-session says how to run). Every line goes to
// SOURCESHARP_PROBE_LOG, tagged "px".
//
// NO STOCK TU IS PATCHED. The proxies are found by name through client.so's
// CreateInterface registry, so at static init (this file is linked after every
// registration) each "*_IMaterialProxy003" entry's create function is swapped
// for a trampoline that wraps the stock proxy in CProbeProxy:
//
//   Init    logs the proxy's KeyValues block flattened (each key's type and
//           its GetString/GetFloat/GetInt answers, plus what sscanf("%f")
//           reads from it), then runs stock's Init with the material's
//           FindVar logged (the name asked, complain, found, the var's name):
//           the material's vptr is swapped for a copy whose FindVar slot logs,
//           for the duration of the call only.
//   OnBind  (while cs_cl_probe_proxies is 1, at most cs_cl_probe_proxies_cap
//           binds per proxy name) logs the bind's inputs -- curtime,
//           frametime, the bind argument resolved as stock resolves it, the
//           view origin, the water LOD params, the world's bounds -- and the
//           state of every var the proxy's Init found; then runs stock's
//           OnBind with every one of those vars' vptrs swapped for a copy
//           whose setter slots log the call (the exact virtual and its
//           arguments) before forwarding; then logs the vars' state after.
//           The engine's uniform stream is wrapped for the run, so every draw
//           a bind makes (random and randomgaussian's) is logged in order.
//
// THE SCENE: cs_cl_probe_pxscene binds, once per call, a set of real content
// materials and a set of synthetic ones (materials->CreateMaterial with a
// Proxies block) that carry every proxy name client.so registers, with their
// source vars swept before each bind and with entity, null and player-index
// bind arguments. The session calls it once per frame for a few seconds.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialproxy.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/itexture.h"
#include "materialsystem/imaterialsystem.h"
#include "mathlib/vmatrix.h"
#include "tier1/KeyValues.h"
#include "tier1/interface.h"
#include "vstdlib/random.h"
#include "sharedInterface.h"
#include "view.h"
#include "iviewrender.h"
#include "c_world.h"
#include "materialproxy_facts.h"
#include "vtable_hook.h"
#include "shell_cliententitylist.h"

#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "tier0/memdbgon.h"

namespace
{

int PxProbeFd()
{
	static int s_fd = -2;
	if ( s_fd == -2 )
	{
		const char *pPath = getenv( "SOURCESHARP_PROBE_LOG" );
		s_fd = ( pPath && *pPath ) ? open( pPath, O_WRONLY | O_CREAT | O_APPEND | O_CLOEXEC, 0644 ) : -1;
	}
	return s_fd;
}

void PxLog( const char *pFmt, ... ) __attribute__(( format( printf, 1, 2 ) ));
void PxLog( const char *pFmt, ... )
{
	int fd = PxProbeFd();
	if ( fd < 0 )
		return;
	timespec ts;
	clock_gettime( CLOCK_MONOTONIC, &ts );
	char buf[ 4096 ];
	int n = snprintf( buf, sizeof( buf ), "%lld %d px ",
	                  (long long)ts.tv_sec * 1000000000LL + ts.tv_nsec, (int)getpid() );
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

// A token with no whitespace, for a name in a tab-free line: spaces become '?'.
const char *Tok( const char *p, char *buf, int cap )
{
	if ( !p )
		return "<null>";
	if ( !*p )
		return "<empty>";
	int i = 0;
	for ( ; p[ i ] && i < cap - 1; ++i )
		buf[ i ] = ( p[ i ] == ' ' || p[ i ] == '\t' || p[ i ] == '\n' ) ? '?' : p[ i ];
	buf[ i ] = 0;
	return buf;
}

ConVar cs_cl_probe_proxies( "cs_cl_probe_proxies", "0", 0, "c5-proxies probe (debug): log material proxy binds" );
ConVar cs_cl_probe_proxies_cap( "cs_cl_probe_proxies_cap", "400", 0, "c5-proxies probe (debug): binds logged per proxy name" );

__thread int t_nInBind = 0;
// Non-zero while a getter stock called runs: a set made then is the material
// system's own (GetTextureValue on a string var loads the texture and makes the
// var a texture), logged as px-iset rather than as something the proxy did.
__thread int t_nInGetter = 0;

//-----------------------------------------------------------------------------
// Swapped vtables. Every var is a CMaterialVar and every material a CMaterial,
// so one copy of each class's table serves every object; the copy is longer
// than the interface so a derived virtual called through the swapped object
// still lands on the original.
//-----------------------------------------------------------------------------
const int kVarSlots = 96;
const int kMatSlots = 320;

void **g_pVarOrig = NULL;        // the address point of CMaterialVar's table
void *g_VarCopy[ kVarSlots + 2 ];
void **g_pMatOrig = NULL;
void *g_MatCopy[ kMatSlots + 2 ];

template < typename Fn >
int Slot( Fn pmf )
{
	VTableHook::Member m = VTableHook::MemberOf( pmf );
	Assert( m.isVirtual && m.adjust == 0 );
	return m.slot;
}

// The protected getters, named through a derived class.
struct VarPeek : public IMaterialVar
{
	static int IntSlot() { return Slot( &VarPeek::GetIntValueInternal ); }
	static int FloatSlot() { return Slot( &VarPeek::GetFloatValueInternal ); }
};

template < typename R, typename... A >
R CallOrig( void **pTable, int nSlot, void *pThis, A... a )
{
	typedef R ( *Fn )( void *, A... );
	return ( (Fn)pTable[ nSlot ] )( pThis, a... );
}

int g_nProbeProxySeq = 0;
int g_nCurrentProxy = -1;

// The vars stock's Init found, collected by the logged FindVar.
const int kMaxVars = 64;
struct FoundVars_t
{
	IMaterialVar *pVars[ kMaxVars ];
	int nVars;
};
__thread FoundVars_t *t_pFound = NULL;

const char *VarName( IMaterialVar *pVar, char *buf, int cap )
{
	// GetName is stable and reads no state the bind changes.
	return Tok( CallOrig< const char * >( g_pVarOrig, Slot( &IMaterialVar::GetName ), pVar ), buf, cap );
}

#define PX_SET_PREFIX( pThis ) \
	char nb[ 128 ]; const char *pN = VarName( (IMaterialVar *)pThis, nb, sizeof( nb ) ); \
	const char *pSetTag = t_nInGetter ? "px-iset" : "px-set";

void Set_Float( void *pThis, float v )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s float %.9g", pSetTag, g_nCurrentProxy, pN, v ); }
	CallOrig< void, float >( g_pVarOrig, Slot( &IMaterialVar::SetFloatValue ), pThis, v );
}

void Set_Int( void *pThis, int v )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s int %d", pSetTag, g_nCurrentProxy, pN, v ); }
	CallOrig< void, int >( g_pVarOrig, Slot( &IMaterialVar::SetIntValue ), pThis, v );
}

void Set_String( void *pThis, const char *v )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); char sb[ 256 ]; PxLog( "%s %d %s string %s", pSetTag, g_nCurrentProxy, pN, Tok( v, sb, sizeof( sb ) ) ); }
	CallOrig< void, const char * >( g_pVarOrig, Slot( &IMaterialVar::SetStringValue ), pThis, v );
}

void Set_VecN( void *pThis, const float *v, int n )
{
	if ( t_nInBind )
	{
		PX_SET_PREFIX( pThis );
		float c[ 4 ] = { 0, 0, 0, 0 };
		for ( int i = 0; i < n && i < 4; ++i )
			c[ i ] = v[ i ];
		PxLog( "%s %d %s vecn %d %.9g %.9g %.9g %.9g", pSetTag, g_nCurrentProxy, pN, n, c[ 0 ], c[ 1 ], c[ 2 ], c[ 3 ] );
	}
	CallOrig< void, const float *, int >( g_pVarOrig, Slot( static_cast< void ( IMaterialVar::* )( const float *, int ) >( &IMaterialVar::SetVecValue ) ), pThis, v, n );
}

void Set_Vec2( void *pThis, float x, float y )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s vec2 %.9g %.9g", pSetTag, g_nCurrentProxy, pN, x, y ); }
	CallOrig< void, float, float >( g_pVarOrig, Slot( static_cast< void ( IMaterialVar::* )( float, float ) >( &IMaterialVar::SetVecValue ) ), pThis, x, y );
}

void Set_Vec3( void *pThis, float x, float y, float z )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s vec3 %.9g %.9g %.9g", pSetTag, g_nCurrentProxy, pN, x, y, z ); }
	CallOrig< void, float, float, float >( g_pVarOrig, Slot( static_cast< void ( IMaterialVar::* )( float, float, float ) >( &IMaterialVar::SetVecValue ) ), pThis, x, y, z );
}

void Set_Vec4( void *pThis, float x, float y, float z, float w )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s vec4 %.9g %.9g %.9g %.9g", pSetTag, g_nCurrentProxy, pN, x, y, z, w ); }
	CallOrig< void, float, float, float, float >( g_pVarOrig, Slot( static_cast< void ( IMaterialVar::* )( float, float, float, float ) >( &IMaterialVar::SetVecValue ) ), pThis, x, y, z, w );
}

void Set_Texture( void *pThis, ITexture *t )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); char tb[ 256 ]; PxLog( "%s %d %s texture %s", pSetTag, g_nCurrentProxy, pN, Tok( t ? t->GetName() : NULL, tb, sizeof( tb ) ) ); }
	CallOrig< void, ITexture * >( g_pVarOrig, Slot( &IMaterialVar::SetTextureValue ), pThis, t );
}

void Set_Material( void *pThis, IMaterial *m )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s material", pSetTag, g_nCurrentProxy, pN ); }
	CallOrig< void, IMaterial * >( g_pVarOrig, Slot( &IMaterialVar::SetMaterialValue ), pThis, m );
}

void Set_Undefined( void *pThis )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s undefined", pSetTag, g_nCurrentProxy, pN ); }
	CallOrig< void >( g_pVarOrig, Slot( &IMaterialVar::SetUndefined ), pThis );
}

void Set_Matrix( void *pThis, const VMatrix &m )
{
	if ( t_nInBind )
	{
		PX_SET_PREFIX( pThis );
		const float *f = m.Base();
		PxLog( "%s %d %s matrix %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g",
		       pSetTag, g_nCurrentProxy, pN, f[ 0 ], f[ 1 ], f[ 2 ], f[ 3 ], f[ 4 ], f[ 5 ], f[ 6 ], f[ 7 ],
		       f[ 8 ], f[ 9 ], f[ 10 ], f[ 11 ], f[ 12 ], f[ 13 ], f[ 14 ], f[ 15 ] );
	}
	CallOrig< void, const VMatrix & >( g_pVarOrig, Slot( &IMaterialVar::SetMatrixValue ), pThis, m );
}

void Set_CopyFrom( void *pThis, IMaterialVar *p )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s copyfrom", pSetTag, g_nCurrentProxy, pN ); }
	CallOrig< void, IMaterialVar * >( g_pVarOrig, Slot( &IMaterialVar::CopyFrom ), pThis, p );
}

void Set_Autodetect( void *pThis, const char *v )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); char sb[ 256 ]; PxLog( "%s %d %s autodetect %s", pSetTag, g_nCurrentProxy, pN, Tok( v, sb, sizeof( sb ) ) ); }
	CallOrig< void, const char * >( g_pVarOrig, Slot( &IMaterialVar::SetValueAutodetectType ), pThis, v );
}

void Set_VecComp( void *pThis, float v, int c )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s veccomp %d %.9g", pSetTag, g_nCurrentProxy, pN, c, v ); }
	CallOrig< void, float, int >( g_pVarOrig, Slot( &IMaterialVar::SetVecComponentValue ), pThis, v, c );
}

void Set_FourCC( void *pThis, IMaterialVar::FourCC t, void *p )
{
	if ( t_nInBind ) { PX_SET_PREFIX( pThis ); PxLog( "%s %d %s fourcc", pSetTag, g_nCurrentProxy, pN ); }
	CallOrig< void, IMaterialVar::FourCC, void * >( g_pVarOrig, Slot( &IMaterialVar::SetFourCCValue ), pThis, t, p );
}

ITexture *Get_Texture( void *pThis )
{
	if ( !t_nInBind )
		return CallOrig< ITexture * >( g_pVarOrig, Slot( &IMaterialVar::GetTextureValue ), pThis );
	t_nInGetter++;
	ITexture *t = CallOrig< ITexture * >( g_pVarOrig, Slot( &IMaterialVar::GetTextureValue ), pThis );
	t_nInGetter--;
	char nb[ 128 ], tb[ 256 ];
	// A get made inside another get is the material system's own (loading a
	// string var's texture reads the var again): px-iget, not the proxy's.
	PxLog( "%s %d %s texture %s %d", t_nInGetter ? "px-iget" : "px-get", g_nCurrentProxy, VarName( (IMaterialVar *)pThis, nb, sizeof( nb ) ),
	       Tok( t ? t->GetName() : NULL, tb, sizeof( tb ) ), t ? t->GetNumAnimationFrames() : -1 );
	return t;
}

void BuildVarCopy( IMaterialVar *pVar )
{
	if ( g_pVarOrig )
		return;
	void **vp = *(void ***)pVar;
	g_pVarOrig = vp;
	memcpy( g_VarCopy, vp - 2, sizeof( g_VarCopy ) );
	void **c = g_VarCopy + 2;
	c[ Slot( &IMaterialVar::GetTextureValue ) ] = (void *)&Get_Texture;
	c[ Slot( &IMaterialVar::SetFloatValue ) ] = (void *)&Set_Float;
	c[ Slot( &IMaterialVar::SetIntValue ) ] = (void *)&Set_Int;
	c[ Slot( &IMaterialVar::SetStringValue ) ] = (void *)&Set_String;
	c[ Slot( &IMaterialVar::SetFourCCValue ) ] = (void *)&Set_FourCC;
	c[ Slot( static_cast< void ( IMaterialVar::* )( const float *, int ) >( &IMaterialVar::SetVecValue ) ) ] = (void *)&Set_VecN;
	c[ Slot( static_cast< void ( IMaterialVar::* )( float, float ) >( &IMaterialVar::SetVecValue ) ) ] = (void *)&Set_Vec2;
	c[ Slot( static_cast< void ( IMaterialVar::* )( float, float, float ) >( &IMaterialVar::SetVecValue ) ) ] = (void *)&Set_Vec3;
	c[ Slot( static_cast< void ( IMaterialVar::* )( float, float, float, float ) >( &IMaterialVar::SetVecValue ) ) ] = (void *)&Set_Vec4;
	c[ Slot( &IMaterialVar::SetTextureValue ) ] = (void *)&Set_Texture;
	c[ Slot( &IMaterialVar::SetMaterialValue ) ] = (void *)&Set_Material;
	c[ Slot( &IMaterialVar::SetUndefined ) ] = (void *)&Set_Undefined;
	c[ Slot( &IMaterialVar::SetMatrixValue ) ] = (void *)&Set_Matrix;
	c[ Slot( &IMaterialVar::CopyFrom ) ] = (void *)&Set_CopyFrom;
	c[ Slot( &IMaterialVar::SetValueAutodetectType ) ] = (void *)&Set_Autodetect;
	c[ Slot( &IMaterialVar::SetVecComponentValue ) ] = (void *)&Set_VecComp;
}

//-----------------------------------------------------------------------------
// FindVar, logged during stock's Init.
//-----------------------------------------------------------------------------
IMaterialVar *Mat_FindVar( void *pThis, const char *pName, bool *pFound, bool bComplain )
{
	bool bFound = false;
	IMaterialVar *pVar = CallOrig< IMaterialVar *, const char *, bool *, bool >(
		g_pMatOrig, Slot( &IMaterial::FindVar ), pThis, pName, &bFound, bComplain );
	if ( pFound )
		*pFound = bFound;
	char nb[ 128 ], vb[ 128 ];
	if ( pVar )
	{
		BuildVarCopy( pVar );
		if ( t_pFound )
		{
			bool bHave = false;
			for ( int i = 0; i < t_pFound->nVars; ++i )
				bHave = bHave || t_pFound->pVars[ i ] == pVar;
			if ( !bHave && t_pFound->nVars < kMaxVars )
				t_pFound->pVars[ t_pFound->nVars++ ] = pVar;
		}
	}
	PxLog( "px-findvar %d %s %d %d %s %d", g_nCurrentProxy, Tok( pName, nb, sizeof( nb ) ), bComplain ? 1 : 0,
	       bFound ? 1 : 0, pVar ? VarName( pVar, vb, sizeof( vb ) ) : "<null>", pFound ? 1 : 0 );
	return pVar;
}

void BuildMatCopy( IMaterial *pMat )
{
	if ( g_pMatOrig )
		return;
	void **vp = *(void ***)pMat;
	g_pMatOrig = vp;
	memcpy( g_MatCopy, vp - 2, sizeof( g_MatCopy ) );
	g_MatCopy[ 2 + Slot( &IMaterial::FindVar ) ] = (void *)&Mat_FindVar;
}

//-----------------------------------------------------------------------------
// The engine's uniform stream, wrapped so a bind's draws are logged.
//-----------------------------------------------------------------------------
class CLogUniform : public IUniformRandomStream
{
public:
	IUniformRandomStream *m_pInner = NULL;
	virtual void SetSeed( int iSeed ) OVERRIDE { m_pInner->SetSeed( iSeed ); }
	virtual float RandomFloat( float flMinVal = 0.0f, float flMaxVal = 1.0f ) OVERRIDE
	{
		float v = m_pInner->RandomFloat( flMinVal, flMaxVal );
		if ( t_nInBind )
			PxLog( "px-rnd %d f %.9g %.9g %.9g", g_nCurrentProxy, flMinVal, flMaxVal, v );
		return v;
	}
	virtual int RandomInt( int iMinVal, int iMaxVal ) OVERRIDE
	{
		int v = m_pInner->RandomInt( iMinVal, iMaxVal );
		if ( t_nInBind )
			PxLog( "px-rnd %d i %d %d %d", g_nCurrentProxy, iMinVal, iMaxVal, v );
		return v;
	}
	virtual float RandomFloatExp( float flMinVal = 0.0f, float flMaxVal = 1.0f, float flExponent = 1.0f ) OVERRIDE
	{
		float v = m_pInner->RandomFloatExp( flMinVal, flMaxVal, flExponent );
		if ( t_nInBind )
			PxLog( "px-rnd %d e %.9g %.9g %.9g %.9g", g_nCurrentProxy, flMinVal, flMaxVal, flExponent, v );
		return v;
	}
};

CLogUniform g_LogUniform;

void InstallRandom()
{
	if ( g_LogUniform.m_pInner || !::random )
		return;
	g_LogUniform.m_pInner = ::random;
	::random = &g_LogUniform;
	if ( randomgaussian )
		randomgaussian->AttachToStream( &g_LogUniform );
	PxLog( "px-random installed" );
}

//-----------------------------------------------------------------------------
// The wrapper.
//-----------------------------------------------------------------------------
const int kMaxRegs = 128;
InstantiateInterfaceFn g_pOrigCreate[ kMaxRegs ];
const char *g_pRegName[ kMaxRegs ];
int g_nRegs = 0;
int g_nBindsByReg[ kMaxRegs ];

void VarState( const char *pTag, int nId, IMaterialVar *pVar )
{
	char nb[ 128 ], tb[ 256 ];
	const int nType = (int)pVar->GetType();
	float v[ 4 ] = { 0, 0, 0, 0 };
	pVar->GetVecValue( v, 4 );
	int nFrames = -1;
	const char *pTex = NULL;
	if ( nType == MATERIAL_VAR_TYPE_TEXTURE )
	{
		ITexture *pT = pVar->GetTextureValue();
		if ( pT )
		{
			nFrames = pT->GetNumAnimationFrames();
			pTex = pT->GetName();
		}
	}
	float m[ 16 ];
	memset( m, 0, sizeof( m ) );
	if ( nType == MATERIAL_VAR_TYPE_MATRIX )
		memcpy( m, pVar->GetMatrixValue().Base(), sizeof( m ) );
	PxLog( "%s %d %s %d %d %d %.9g %.9g %.9g %.9g %.9g %d %s"
	       " %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g",
	       pTag, nId, VarName( pVar, nb, sizeof( nb ) ), nType, pVar->VectorSize(), pVar->GetIntValue(),
	       pVar->GetFloatValue(), v[ 0 ], v[ 1 ], v[ 2 ], v[ 3 ], nFrames, Tok( pTex, tb, sizeof( tb ) ),
	       m[ 0 ], m[ 1 ], m[ 2 ], m[ 3 ], m[ 4 ], m[ 5 ], m[ 6 ], m[ 7 ],
	       m[ 8 ], m[ 9 ], m[ 10 ], m[ 11 ], m[ 12 ], m[ 13 ], m[ 14 ], m[ 15 ] );
}

void RunSceneTick();
int g_nScenePending = 0;
int g_nSceneFrame = -1;
__thread int t_nInScene = 0;

class CProbeProxy : public IMaterialProxy
{
public:
	CProbeProxy( int nReg, IMaterialProxy *pInner )
		: m_nReg( nReg ), m_nId( ++g_nProbeProxySeq ), m_pInner( pInner ), m_pMaterial( NULL )
	{
		m_Found.nVars = 0;
	}

	virtual bool Init( IMaterial *pMaterial, KeyValues *pKeyValues ) OVERRIDE
	{
		char nb[ 256 ], mb[ 256 ];
		m_pMaterial = pMaterial;
		PxLog( "px-init %d %s %s", m_nId, g_pRegName[ m_nReg ],
		       Tok( pMaterial ? pMaterial->GetName() : NULL, mb, sizeof( mb ) ) );
		if ( pKeyValues )
		{
			// RAW, BY TYPE: KeyValues::GetString converts a FLOAT or INT key to
			// a STRING in place ("%f", "%d"), so asking it before stock's Init
			// would change what stock's Init reads. Each key is read only
			// through the accessor that does not convert it.
			for ( KeyValues *k = pKeyValues->GetFirstSubKey(); k; k = k->GetNextKey() )
			{
				char sb[ 256 ];
				const int nType = (int)k->GetDataType();
				const char *pStr = "<none>";
				float flRaw = 0.0f, flScan = 0.0f, flAtof = 0.0f;
				int nRaw = 0, nScan = 0, nAtoi = 0;
				if ( nType == KeyValues::TYPE_STRING )
				{
					pStr = k->GetString();
					nScan = sscanf( pStr, "%f", &flScan );
					flAtof = (float)atof( pStr );
					nAtoi = atoi( pStr );
				}
				else if ( nType == KeyValues::TYPE_FLOAT )
					flRaw = k->GetFloat();
				else if ( nType == KeyValues::TYPE_INT )
					nRaw = k->GetInt();
				PxLog( "px-key %d %s %d %s %.9g %d %d %.9g %.9g %d", m_nId, Tok( k->GetName(), nb, sizeof( nb ) ),
				       nType, Tok( pStr, sb, sizeof( sb ) ), flRaw, nRaw, nScan, flScan, flAtof, nAtoi );
			}
		}

		const int nPrev = g_nCurrentProxy;
		g_nCurrentProxy = m_nId;
		void **pSaved = NULL;
		if ( pMaterial )
		{
			BuildMatCopy( pMaterial );
			pSaved = *(void ***)pMaterial;
			*(void ***)pMaterial = g_MatCopy + 2;
		}
		m_Found.nVars = 0;
		t_pFound = &m_Found;
		const bool bOk = m_pInner->Init( pMaterial, pKeyValues );
		t_pFound = NULL;
		if ( pMaterial )
			*(void ***)pMaterial = pSaved;
		g_nCurrentProxy = nPrev;
		PxLog( "px-initok %d %d", m_nId, bOk ? 1 : 0 );
		return bOk;
	}

	virtual void OnBind( void *pArg ) OVERRIDE
	{
		// The scene runs from inside a bind the renderer made, once a frame:
		// CurrentViewOrigin (which lampbeam and lamphalo read) answers only
		// while a view renders.
		if ( g_nScenePending > 0 && !t_nInScene && gpGlobals->framecount != g_nSceneFrame )
		{
			g_nSceneFrame = gpGlobals->framecount;
			g_nScenePending--;
			t_nInScene++;
			RunSceneTick();
			t_nInScene--;
		}
		const bool bLog = cs_cl_probe_proxies.GetBool() && g_nBindsByReg[ m_nReg ] < cs_cl_probe_proxies_cap.GetInt();
		if ( !bLog )
		{
			m_pInner->OnBind( pArg );
			return;
		}
		g_nBindsByReg[ m_nReg ]++;
		InstallRandom();

		// The bind argument as stock reads it. PlayerLogo's is a player index,
		// every other one's a renderable (or NULL).
		// A material bound with a player index (decals/playerlogoNN) hands the
		// index to every proxy it carries, not only PlayerLogo: an argument
		// below 64K is an index, never a renderable, and is not dereferenced.
		const bool bIndexArg = !Q_strcmp( g_pRegName[ m_nReg ], "PlayerLogo" ) || ( pArg && (uintp)pArg < 65536 ) || ( (intp)pArg < 0 && (intp)pArg > -65536 );
		int nEnt = -2;
		Vector vOrigin( 0, 0, 0 );
		int nAlpha = -9, nNpc = -9;
		int nPlayer = 0;
		unsigned int nCrc = 0;
		if ( bIndexArg )
		{
			nPlayer = (int)(intp)pArg;
			player_info_t info;
			if ( nPlayer > 0 && nPlayer <= gpGlobals->maxClients && engine->GetPlayerInfo( nPlayer, &info ) )
				nCrc = (unsigned int)info.customFiles[ 0 ];
		}
		else if ( pArg )
		{
			IClientRenderable *pRend = (IClientRenderable *)pArg;
			IClientUnknown *pUnk = pRend->GetIClientUnknown();
			IClientEntity *pEnt = pUnk ? pUnk->GetIClientEntity() : NULL;
			nEnt = pEnt ? pEnt->entindex() : -1;
			if ( pEnt )
				vOrigin = pEnt->GetAbsOrigin();
			nAlpha = SourceSharp_RenderAlphaOfUnknown( pUnk );
			nNpc = SourceSharp_RenderableIsNpc( pArg );
		}
		const Vector &vView = CurrentViewOrigin();
		float flStart = 0, flEnd = 0;
		if ( view )
			view->GetWaterLODParams( flStart, flEnd );
		Vector vMins( 0, 0, 0 ), vMaxs( 0, 0, 0 );
		int nWorld = 0;
		if ( C_World *pWorld = GetClientWorldEntity() )
		{
			nWorld = 1;
			vMins = pWorld->GetWorldMins();
			vMaxs = pWorld->GetWorldMaxs();
		}
		PxLog( "px-bind %d %s %d %.9g %.9g %d %d %d %u %d %.9g %.9g %.9g %d %d %.9g %.9g %.9g %.9g %.9g %d %.9g %.9g %.9g %.9g %.9g %.9g",
		       m_nId, g_pRegName[ m_nReg ], g_nBindsByReg[ m_nReg ], gpGlobals->curtime, gpGlobals->frametime,
		       bIndexArg ? 1 : ( pArg ? 2 : 0 ), nPlayer, gpGlobals->maxClients, nCrc,
		       nEnt, vOrigin.x, vOrigin.y, vOrigin.z, nAlpha, nNpc,
		       vView.x, vView.y, vView.z, flStart, flEnd,
		       nWorld, vMins.x, vMins.y, vMins.z, vMaxs.x, vMaxs.y, vMaxs.z );

		for ( int i = 0; i < m_Found.nVars; ++i )
			VarState( "px-before", m_nId, m_Found.pVars[ i ] );

		void **pSaved[ kMaxVars ] = {};
		for ( int i = 0; i < m_Found.nVars; ++i )
		{
			pSaved[ i ] = *(void ***)m_Found.pVars[ i ];
			*(void ***)m_Found.pVars[ i ] = g_VarCopy + 2;
		}
		const int nPrev = g_nCurrentProxy;
		g_nCurrentProxy = m_nId;
		t_nInBind++;
		m_pInner->OnBind( pArg );
		t_nInBind--;
		g_nCurrentProxy = nPrev;
		for ( int i = 0; i < m_Found.nVars; ++i )
			*(void ***)m_Found.pVars[ i ] = pSaved[ i ];

		for ( int i = 0; i < m_Found.nVars; ++i )
			VarState( "px-after", m_nId, m_Found.pVars[ i ] );
		PxLog( "px-end %d", m_nId );
	}

	virtual void Release( void ) OVERRIDE
	{
		PxLog( "px-release %d", m_nId );
		m_pInner->Release();
		delete this;
	}

	virtual IMaterial *GetMaterial() OVERRIDE
	{
		return m_pInner->GetMaterial();
	}

private:
	int m_nReg;
	int m_nId;
	IMaterialProxy *m_pInner;
	IMaterial *m_pMaterial;
	FoundVars_t m_Found;
};

template < int N >
void *PxCreate()
{
	IMaterialProxy *pInner = (IMaterialProxy *)g_pOrigCreate[ N ]();
	if ( !pInner )
		return NULL;
	return static_cast< IMaterialProxy * >( new CProbeProxy( N, pInner ) );
}

template < int N >
struct PxTable
{
	static void Fill( InstantiateInterfaceFn *pOut )
	{
		PxTable< N - 1 >::Fill( pOut );
		pOut[ N - 1 ] = &PxCreate< N - 1 >;
	}
};

template <>
struct PxTable< 0 >
{
	static void Fill( InstantiateInterfaceFn * ) {}
};

InstantiateInterfaceFn g_pPxTramp[ kMaxRegs ];

// Linked after every registration (CMakeLists adds the probes last), so the
// list is complete when this runs.
struct CPxInstall
{
	CPxInstall()
	{
		PxTable< kMaxRegs >::Fill( g_pPxTramp );
		const char *pSuffix = IMATERIAL_PROXY_INTERFACE_VERSION;
		const int nSuffix = (int)strlen( pSuffix );
		for ( InterfaceReg *r = InterfaceReg::s_pInterfaceRegs; r; r = r->m_pNext )
		{
			const int n = (int)strlen( r->m_pName );
			if ( n <= nSuffix || strcmp( r->m_pName + n - nSuffix, pSuffix ) || g_nRegs >= kMaxRegs )
				continue;
			char *pName = new char[ n - nSuffix + 1 ];
			memcpy( pName, r->m_pName, n - nSuffix );
			pName[ n - nSuffix ] = 0;
			g_pRegName[ g_nRegs ] = pName;
			g_pOrigCreate[ g_nRegs ] = r->m_CreateFn;
			r->m_CreateFn = g_pPxTramp[ g_nRegs ];
			++g_nRegs;
		}
	}
} g_PxInstall;

//-----------------------------------------------------------------------------
// The scene.
//-----------------------------------------------------------------------------
struct SceneMaterial_t
{
	const char *pName;   // a content material, or "__c5p_<n>" for a synthetic one
	IMaterial *pMaterial;
	int nArgKind;        // 0 null, 1 entities, 2 player indices
};

CUtlVector< SceneMaterial_t > g_Scene;

// Proxies the scene makes itself, through the wrapped registration, because no
// material can ask for them: the material system looks a proxy up by its block's
// KeyValues name, and that name is the symbol table's first spelling of it --
// "health" here, which the case-sensitive registry does not answer (the console
// says `proxy "health" not found!`). Stock's code is the same either way.
struct DirectProxy_t
{
	const char *pName;
	const char *pKeys;     // the block's body
	IMaterialProxy *pProxy;
	IMaterial *pMaterial;
};
DirectProxy_t g_Direct[] = {
	{ "Health", "scale 2 resultVar $r2", NULL, NULL },
	{ "Health", "resultVar \"$rv3[1]\"", NULL, NULL },
};
int g_nSceneTick = 0;

// Real content materials that carry the proxies dm_lockdown may not bind
// (tools/probes/c5proxies-survey: the shipped VPKs' .vmt Proxies blocks).
const char *g_ppContent[] = {
	"effects/combineshield/comshieldwall3",
	"effects/combineshield/comshieldwall2faded",
	"dev/dev_tvmonitor1a",
	"effects/tp_eyefx/tpeye3",
	"models/weapons/v_smg1/texture5",
	"dev/ocean",
	"dev/oceanbeneath",
	"models/shadertest/vertexlittranslucentbumpmappedtexture",
	"models/shadertest/vertexlitselfillummaskenvmaptexdet",
	"sprites/glow_test02",
	"sprites/glow_test01b",
	"sprites/lamphalo",
	"sprites/lamphalo1",
	"effects/ar2_altfire1",
	"halflife/+alab1_w6",
	"halflife/+0~generic86r",
	"shadertest/unlittwotexture",
	"dev/dev_camo",
	"nature/cloud001c",
	"models/weapons/v_slam/new light1",
	"decals/playerlogo01",
	"models/combine_helicopter/combine_helicopter_new_bladeblur",
};

// Synthetic materials: every proxy name, over vars of every type. $f float,
// $g float, $i int, $j int, $v2 vec2, $v3 vec3, $v4 vec4, $m matrix (via
// TextureTransform), $basetexture an animated texture, $frame int.
struct Synthetic_t
{
	int nArgKind;
	const char *pProxies;   // a KeyValues text for the Proxies block's body
};

const Synthetic_t g_Synth[] = {
	{ 0, "Add { srcVar1 $f srcVar2 $g resultVar $r } Add { srcVar1 $v3 srcVar2 $w3 resultVar $rv3 } Add { srcVar1 $i srcVar2 $j resultVar $ri }"
	     " Add { srcVar1 $f srcVar2 $g resultVar \"$rv3[1]\" } Add { srcVar1 $v2 srcVar2 $v2 resultVar $rv2 } Add { srcVar1 $f srcVar2 $g resultVar $u1 }" },
	{ 0, "Subtract { srcVar1 $f srcVar2 $g resultVar $r } Subtract { srcVar1 $v3 srcVar2 $w3 resultVar $rv3 } Subtract { srcVar1 $i srcVar2 $j resultVar $ri } Subtract { srcVar1 $v3 srcVar2 $w3 resultVar $u2 }" },
	{ 0, "Multiply { srcVar1 $f srcVar2 $g resultVar $r } Multiply { srcVar1 $v3 srcVar2 $w3 resultVar $rv3 } Multiply { srcVar1 $i srcVar2 $j resultVar $ri } Multiply { srcVar1 $f srcVar2 $g resultVar $rv3 }" },
	{ 0, "Divide { srcVar1 $f srcVar2 $g resultVar $r } Divide { srcVar1 $v3 srcVar2 $w3 resultVar $rv3 } Divide { srcVar1 $i srcVar2 $j resultVar $ri } Divide { srcVar1 $f srcVar2 $z resultVar $r2 } Divide { srcVar1 $i srcVar2 $zi resultVar $ri2 }" },
	{ 0, "Clamp { srcVar1 $f min 0.25 max 0.75 resultVar $r } Clamp { srcVar1 $v3 min $g max -0.5 resultVar $rv3 } Clamp { srcVar1 $i min 1 max 3 resultVar $ri } Clamp { srcVar1 $f min \"$v3[2]\" max 2 resultVar \"$rv4[3]\" }" },
	{ 0, "Sine { sinePeriod 2.5 sineMin -1 sineMax 3 resultVar $r } Sine { sinePeriod $g sineMin $f resultVar $rv3 } Sine { sinePeriod 0 timeOffset 0.3 resultVar \"$rv3[0]\" } Sine { resultVar $ri }" },
	{ 0, "Equals { srcVar1 $f resultVar $r } Equals { srcVar1 $v3 resultVar $rv3 } Equals { srcVar1 $i resultVar $ri } Equals { srcVar1 $v4 resultVar $u3 } Equals { srcVar1 $f resultVar \"$rv2[1]\" }" },
	{ 0, "Frac { srcVar1 $f resultVar $r } Frac { srcVar1 $v3 resultVar $rv3 } Frac { srcVar1 $i resultVar $ri } Int { srcVar1 $f resultVar $r2 } Int { srcVar1 $v3 resultVar $rv4 } Int { srcVar1 $i resultVar $ri2 }" },
	{ 0, "LinearRamp { rate 0.75 initialValue -2 resultVar $r } LinearRamp { rate $g initialValue $f resultVar $rv3 } LinearRamp { resultVar \"$rv4[2]\" } CurrentTime { resultVar $r2 } CurrentTime { resultVar $rv2 }" },
	{ 0, "UniformNoise { minVal -2 maxVal 5 resultVar $r } UniformNoise { resultVar $rv3 } GaussianNoise { mean 1 halfwidth 0.5 minVal 0 maxVal 1.5 resultVar $r2 } GaussianNoise { resultVar \"$rv4[1]\" } GaussianNoise { minVal 3 maxVal -3 resultVar $ri }" },
	{ 0, "Exponential { srcVar1 $f scale 0.5 offset -1 minVal 0 maxVal 10 resultVar $r } Exponential { srcVar1 $g resultVar $rv3 } Abs { srcVar1 $f resultVar $r2 } Abs { srcVar1 $v3 resultVar $rv4 } Abs { srcVar1 $i resultVar $ri }" },
	{ 0, "LessOrEqual { srcVar1 $f srcVar2 $g lessEqualVar $v3 greaterVar $w3 resultVar $rv3 } LessOrEqual { srcVar1 $f srcVar2 $g lessEqualVar $i greaterVar $j resultVar $ri } LessOrEqual { srcVar1 $g srcVar2 $f lessEqualVar $f greaterVar $g resultVar $r }"
	     " LessOrEqual { srcVar1 $f srcVar2 $g lessEqualVar $v3 greaterVar $w3 resultVar $u4 }" },
	{ 0, "WrapMinMax { srcVar1 $f minVal -1 maxVal 2 resultVar $r } WrapMinMax { srcVar1 $f minVal 3 maxVal 1 resultVar $r2 } WrapMinMax { srcVar1 $g minVal $f maxVal 4 resultVar \"$rv3[2]\" }"
	     " SelectFirstIfNonZero { srcVar1 $f srcVar2 $g resultVar $r3 } SelectFirstIfNonZero { srcVar1 $v3 srcVar2 $w3 resultVar $rv4 } SelectFirstIfNonZero { srcVar1 $i srcVar2 $j resultVar $ri }" },
	{ 0, "TextureTransform { centerVar $v2 scaleVar $s2 rotateVar $f translateVar $t2 resultVar $basetexturetransform } TextureTransform { resultVar $m2 } TextureTransform { rotateVar $g resultVar $m3 }"
	     " MatrixRotate { axisVar $v3 angle $f resultVar $m4 } MatrixRotate { angle 30 resultVar $m5 } MatrixRotate { axisVar $zv angle 45 resultVar $m6 }" },
	{ 0, "TextureScroll { textureScrollVar $t2 textureScrollRate 0.4 textureScrollAngle 30 } TextureScroll { textureScrollVar $m2 textureScrollRate $g textureScrollAngle $f textureScale 2 } TextureScroll { textureScrollVar $rv3 textureScrollRate -1.5 textureScrollAngle 200 }" },
	{ 0, "AnimatedTexture { animatedTextureVar $basetexture animatedTextureFrameNumVar $frame animatedTextureFrameRate 7 } AnimatedTexture { animatedTextureVar $basetexture animatedTextureFrameNumVar $ri animationNoWrap 1 animatedTextureFrameRate 3 }"
	     " AnimateSpecificTexture { animatedTextureVar $basetexture animatedTextureFrameNumVar $ri2 onlyAnimateOnTexture effects/tp_eyefx/tpeye3 } AnimateSpecificTexture { animatedTextureVar $basetexture animatedTextureFrameNumVar $ri2 onlyAnimateOnTexture other }"
	     " AnimatedTexture { animatedTextureVar $f animatedTextureFrameNumVar $frame }" },
	{ 1, "AnimatedEntityTexture { animatedTextureVar $basetexture animatedTextureFrameNumVar $frame animatedTextureFrameRate 5 } AnimatedOffsetTexture { animatedTextureVar $basetexture animatedTextureFrameNumVar $ri animatedTextureFrameRate 9 }"
	     " ToggleTexture { toggleTextureVar $basetexture toggleTextureFrameNumVar $ri2 toggleShouldWrap 0 }" },
	{ 1, "EntityOrigin { } EntityOriginAlyx { } Ep1IntroVortRefract { } Alpha { } IsNPC { scale 0.5 resultVar $r } Health { scale 2 resultVar $r2 } lampbeam { } Pupil { TextureVar $basetexture TextureFrameNumVar $frame } Camo { }" },
	{ 1, "lamphalo { } WaterLOD { } WorldDims { } Dummy { } Empty { } Shield { } HeliBlade { } VortEmissive { }" },
	{ 2, "PlayerLogo { }" },
};

void SweepVars( IMaterial *pMat, int k )
{
	bool b;
	IMaterialVar *v;
	const float fk = (float)k;
	if ( ( v = pMat->FindVar( "$f", &b, false ) ) && b ) v->SetFloatValue( sinf( fk * 0.37f ) * 3.0f + ( k % 5 == 0 ? 0.0f : 0.5f ) );
	if ( ( v = pMat->FindVar( "$g", &b, false ) ) && b ) v->SetFloatValue( ( k % 7 ) * 0.61f - 1.8f );
	if ( ( v = pMat->FindVar( "$i", &b, false ) ) && b ) v->SetIntValue( ( k % 9 ) - 3 );
	if ( ( v = pMat->FindVar( "$j", &b, false ) ) && b ) v->SetIntValue( ( k % 4 ) - 1 );
	if ( ( v = pMat->FindVar( "$v2", &b, false ) ) && b ) v->SetVecValue( 0.25f + fk * 0.01f, 0.75f - fk * 0.02f );
	if ( ( v = pMat->FindVar( "$v3", &b, false ) ) && b ) v->SetVecValue( cosf( fk * 0.23f ) * 2.0f, ( k % 3 ) * 1.5f - 1.0f, fk * 0.05f - 2.0f );
	if ( ( v = pMat->FindVar( "$w3", &b, false ) ) && b ) v->SetVecValue( ( k % 4 ) * 0.5f - 0.5f, 1.25f, ( k % 2 ) ? 0.0f : -3.0f );
	if ( ( v = pMat->FindVar( "$v4", &b, false ) ) && b ) v->SetVecValue( 1.0f, fk * 0.1f, -2.0f, 0.5f );
	if ( ( v = pMat->FindVar( "$s2", &b, false ) ) && b ) v->SetVecValue( 1.0f + ( k % 3 ) * 0.5f, 2.0f );
	if ( ( v = pMat->FindVar( "$zv", &b, false ) ) && b ) v->SetVecValue( 0.0f, 0.0f, ( k % 2 ) ? 0.0f : 1e-4f );
}

void BuildScene()
{
	if ( g_Scene.Count() )
		return;
	for ( int i = 0; i < (int)ARRAYSIZE( g_ppContent ); ++i )
	{
		SceneMaterial_t s;
		s.pName = g_ppContent[ i ];
		s.pMaterial = materials->FindMaterial( g_ppContent[ i ], TEXTURE_GROUP_OTHER, false );
		s.nArgKind = !Q_strncmp( g_ppContent[ i ], "decals/playerlogo", 17 ) ? 2 : 1;
		if ( s.pMaterial && !s.pMaterial->IsErrorMaterial() )
		{
			s.pMaterial->IncrementReferenceCount();
			g_Scene.AddToTail( s );
			PxLog( "px-scene content %s", g_ppContent[ i ] );
		}
		else
		{
			PxLog( "px-scene missing %s", g_ppContent[ i ] );
		}
	}
	for ( int i = 0; i < (int)ARRAYSIZE( g_Synth ); ++i )
	{
		char szName[ 64 ];
		Q_snprintf( szName, sizeof( szName ), "__c5p_%d", i );
		char szText[ 4096 ];
		Q_snprintf( szText, sizeof( szText ),
		            "UnlitGeneric { $basetexture effects/tp_eyefx/tpeye3 $frame 0 $f 0.5 $g 2 $z 0 $i 3 $j 2 $zi 0"
		            " $v2 \"[0.5 0.5]\" $s2 \"[1 1]\" $t2 \"[0 0]\" $v3 \"[1 2 3]\" $w3 \"[0.5 0.5 0.5]\" $v4 \"[1 2 3 4]\" $zv \"[0 0 0]\""
		            " $r 0 $r2 0 $r3 0 $ri 0 $ri2 0 $rv2 \"[0 0]\" $rv3 \"[0 0 0]\" $rv4 \"[0 0 0 0]\""
		            " $m2 \"center .5 .5 scale 1 1 rotate 0 translate 0 0\" $m3 \"center .5 .5 scale 1 1 rotate 0 translate 0 0\""
		            " $m4 \"center .5 .5 scale 1 1 rotate 0 translate 0 0\" $m5 \"center .5 .5 scale 1 1 rotate 0 translate 0 0\""
		            " $m6 \"center .5 .5 scale 1 1 rotate 0 translate 0 0\" $entityorigin \"[0 0 0]\" $refractamount 0.3 $alpha 1"
		            " $lighting 0.5 $CHEAPWATERSTARTDISTANCE 0 $CHEAPWATERENDDISTANCE 0 $world_mins \"[0 0 0]\" $world_maxs \"[0 0 0]\""
		            " Proxies { %s } }", g_Synth[ i ].pProxies );
		KeyValues *pKV = new KeyValues( "UnlitGeneric" );
		if ( !pKV->LoadFromBuffer( szName, szText ) )
		{
			PxLog( "px-scene badkv %s", szName );
			pKV->deleteThis();
			continue;
		}
		// LoadFromBuffer reads the outer block's name as this key's name and
		// its body as the subkeys: CreateMaterial wants the shader as the name.
		SceneMaterial_t s;
		s.pName = strdup( szName );
		s.pMaterial = materials->CreateMaterial( szName, pKV );
		s.nArgKind = g_Synth[ i ].nArgKind;
		if ( s.pMaterial )
		{
			s.pMaterial->IncrementReferenceCount();
			g_Scene.AddToTail( s );
			PxLog( "px-scene synthetic %s", szName );
		}
	}
}

CON_COMMAND( cs_cl_probe_pxscene, "c5-proxies probe (debug): bind the probe scene's materials once a frame for <frames> frames, from inside the renderer's binds" )
{
	g_nScenePending = args.ArgC() > 1 ? atoi( args[ 1 ] ) : 1;
	PxLog( "px-scene pending %d", g_nScenePending );
}

void RunSceneTick()
{
	if ( !materials )
		return;
	BuildScene();
	const int k = g_nSceneTick++;
	// The bind arguments: NULL, the local player, and every live entity below
	// 64 in turn, one per tick (a different one each tick).
	IClientRenderable *pRends[ 4 ] = { NULL, NULL, NULL, NULL };
	int nRends = 0;
	pRends[ nRends++ ] = NULL;
	IClientEntity *pLocal = SourceSharp_ClientEntityFromIndex( engine->GetLocalPlayer() );
	if ( pLocal && pLocal->GetClientRenderable() )
		pRends[ nRends++ ] = pLocal->GetClientRenderable();
	for ( int tries = 0; tries < 64; ++tries )
	{
		const int e = 1 + ( ( k * 7 + tries ) % 63 );
		IClientEntity *pEnt = SourceSharp_ClientEntityFromIndex( e );
		if ( pEnt && pEnt->GetClientRenderable() )
		{
			pRends[ nRends++ ] = pEnt->GetClientRenderable();
			break;
		}
	}
	for ( int d = 0; d < (int)ARRAYSIZE( g_Direct ); ++d )
	{
		DirectProxy_t &dp = g_Direct[ d ];
		if ( !dp.pProxy )
		{
			// The last synthetic material carries every var these name.
			dp.pMaterial = g_Scene.Count() ? g_Scene.Tail().pMaterial : NULL;
			for ( int r = 0; r < g_nRegs && dp.pMaterial; ++r )
			{
				if ( Q_strcmp( g_pRegName[ r ], dp.pName ) )
					continue;
				dp.pProxy = (IMaterialProxy *)g_pPxTramp[ r ]();
				char szText[ 512 ];
				Q_snprintf( szText, sizeof( szText ), "%s { %s }", dp.pName, dp.pKeys );
				KeyValues *pKV = new KeyValues( dp.pName );
				pKV->LoadFromBuffer( dp.pName, szText );
				if ( !dp.pProxy->Init( dp.pMaterial, pKV ) )
				{
					dp.pProxy->Release();
					dp.pProxy = NULL;
					dp.pMaterial = NULL;
				}
				pKV->deleteThis();
				break;
			}
			PxLog( "px-scene direct %s %s", dp.pName, dp.pProxy ? "ok" : "failed" );
		}
		if ( dp.pProxy )
		{
			for ( int r = 0; r < nRends; ++r )
				dp.pProxy->OnBind( pRends[ r ] );
		}
	}
	for ( int i = 0; i < g_Scene.Count(); ++i )
	{
		SceneMaterial_t &s = g_Scene[ i ];
		if ( !Q_strncmp( s.pName, "__c5p_", 6 ) )
			SweepVars( s.pMaterial, k );
		if ( s.nArgKind == 2 )
		{
			for ( int p = -1; p <= gpGlobals->maxClients + 1; ++p )
				s.pMaterial->CallBindProxy( (void *)(intp)p );
		}
		else if ( s.nArgKind == 1 )
		{
			for ( int r = 0; r < nRends; ++r )
				s.pMaterial->CallBindProxy( pRends[ r ] );
		}
		else
		{
			s.pMaterial->CallBindProxy( ( k & 1 ) ? pRends[ nRends - 1 ] : NULL );
		}
	}
}

CON_COMMAND( cs_cl_probe_pxregs, "c5-proxies probe (debug): log the proxy registrations the probe wrapped" )
{
	for ( int i = 0; i < g_nRegs; ++i )
		PxLog( "px-reg %d %s", i, g_pRegName[ i ] );
}

} // namespace
