//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// The material proxy trampoline. The material system finds a proxy by name through
// CreateInterface ("<Name>_IMaterialProxy003") and holds it for the material's life.
// CManagedMaterialProxy, registered once per name, asks managed what the stock proxy of
// that name decided and carries it out.
//
// Native holds the engine's pointers (material, vars, textures); managed holds their slot
// numbers. A bind hands managed every held var's state and gets back the setter calls to
// make, in order. The handles and pointers survive a managed reload.

#include "vstdlib/random.h"
#include "utlvector.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "materialsystem/imaterialproxy.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/itexture.h"
#include "materialsystem/imaterialsystem.h"
#include "tier1/KeyValues.h"
#include "tier1/interface.h"
#include "tier1/utlmap.h"
#include "view.h"
#include "iviewrender.h"
#include "client_world.h"
#include "cl_mat_stub.h"
#include "materialsystem/imaterialsystemstub.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "tier1/utlrbtree.h"
#include "tier1/utlvector.h"
#include "toolframework/itoolentity.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "const.h"
#include "eiface.h"
#include "globalvars_base.h"
#include "icliententity.h"
#include "icliententitylist.h"
#include "iclientnetworkable.h"
#include "iclientrenderable.h"
#include "iclientunknown.h"
#include "particlemgr.h"
#include "shell_particle_mgr.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "shell_standalone_client.h"
#include "slot_table.h"
#include "shell_diagnostics.h"

#include <stdio.h>
#include <stdlib.h>

#define SS_PROXY_NAMES( X ) \
	X( 0, Abs ) X( 1, Add ) X( 2, Alpha ) X( 3, AnimateSpecificTexture ) X( 4, AnimatedEntityTexture ) \
	X( 5, AnimatedOffsetTexture ) X( 6, AnimatedTexture ) X( 7, Camo ) X( 8, Clamp ) X( 9, CurrentTime ) \
	X( 10, Divide ) X( 11, Dummy ) X( 12, Empty ) X( 13, EntityOrigin ) X( 14, EntityOriginAlyx ) \
	X( 15, Ep1IntroVortRefract ) X( 16, Equals ) X( 17, Exponential ) X( 18, Frac ) X( 19, GaussianNoise ) \
	X( 20, Health ) X( 21, HeliBlade ) X( 22, Int ) X( 23, IsNPC ) X( 24, LessOrEqual ) \
	X( 25, LinearRamp ) X( 26, MatrixRotate ) X( 27, Multiply ) X( 28, PlayerLogo ) X( 29, Pupil ) \
	X( 30, SelectFirstIfNonZero ) X( 31, Shield ) X( 32, Sine ) X( 33, Subtract ) X( 34, TextureScroll ) \
	X( 35, TextureTransform ) X( 36, ToggleTexture ) X( 37, UniformNoise ) X( 38, VortEmissive ) X( 39, WaterLOD ) \
	X( 40, WorldDims ) X( 41, WrapMinMax ) X( 42, lampbeam ) X( 43, lamphalo ) \
	X( 44, MotionBlur ) X( 45, engine_post ) X( 46, ParticleSphereProxy ) \
	X( 47, Shadow ) X( 48, ShadowModel )

#define SS_PROXY_NAME( i, name ) #name,
static const char *const s_ProxyNames[] = { SS_PROXY_NAMES( SS_PROXY_NAME ) };
#undef SS_PROXY_NAME

// The most setter calls one bind may ask for.
static const int kMaxProxyOps = 16;
enum
{
	kProxyOpFloat = 1,
	kProxyOpInt = 2,
	kProxyOpVecN = 3,
	kProxyOpVec3 = 4,
	kProxyOpVecComponent = 5,
	kProxyOpMatrix = 6,
	kProxyOpTexture = 7,
	// 8 is reserved; never reuse it.
};

class CManagedMaterialProxy;

// Handles managed knows the live proxies by: never reused, so a stale one finds nothing.
class CManagedProxyTable
{
public:
	CManagedProxyTable() : m_Proxies( DefLessFunc( int32_t ) ), m_nNext( 0 ) {}

	int32_t Add( CManagedMaterialProxy *pProxy )
	{
		const int32_t h = ++m_nNext;
		m_Proxies.Insert( h, pProxy );
		return h;
	}

	void Remove( int32_t h ) { m_Proxies.Remove( h ); }

	CManagedMaterialProxy *Find( int32_t h ) const
	{
		const unsigned short i = m_Proxies.Find( h );
		return i == m_Proxies.InvalidIndex() ? NULL : m_Proxies[ i ];
	}

private:
	CUtlMap< int32_t, CManagedMaterialProxy * > m_Proxies;
	int32_t m_nNext;
};

static CManagedProxyTable s_ManagedProxies;

class CManagedMaterialProxy : public IMaterialProxy
{
public:
	explicit CManagedMaterialProxy( int nName )
		: m_nName( nName ), m_hProxy( 0 ), m_pMaterial( NULL ), m_pBindArg( NULL ), m_pBindVars( NULL )
	{
		m_hProxy = s_ManagedProxies.Add( this );
	}

	virtual bool Init( IMaterial *pMaterial, KeyValues *pKeyValues ) OVERRIDE
	{
		m_pMaterial = pMaterial;

		IManagedMaterialProxyClient *pManaged = ManagedMaterialProxyClient();
		if ( !pManaged || !pMaterial )
		{
			Warning( "[SourceSharp] material proxy \"%s\" on \"%s\": no managed client to ask; refused\n",
			         s_ProxyNames[ m_nName ], pMaterial ? pMaterial->GetName() : "(null)" );
			return false;
		}

		// Raw: no key is read through an accessor that converts it.
		CUtlVector< SourceSharpProxyKey_t > keys;
		if ( pKeyValues )
		{
			for ( KeyValues *k = pKeyValues->GetFirstSubKey(); k; k = k->GetNextKey() )
			{
				SourceSharpProxyKey_t key;
				V_memset( &key, 0, sizeof( key ) );
				key.Name = k->GetName();
				key.String = "";
				key.Type = (int32_t)k->GetDataType();
				if ( key.Type == KeyValues::TYPE_STRING )
				{
					key.String = k->GetString();
					key.ScanCount = sscanf( key.String, "%f", &key.ScanValue );
					key.Atof = (float)atof( key.String );
					key.Atoi = atoi( key.String );
				}
				else if ( key.Type == KeyValues::TYPE_FLOAT )
				{
					key.Float = k->GetFloat();
				}
				else if ( key.Type == KeyValues::TYPE_INT )
				{
					key.Int = k->GetInt();
				}
				keys.AddToTail( key );
			}
		}

		return pManaged->ProxyInit( m_hProxy, s_ProxyNames[ m_nName ], pMaterial->GetName(),
		                            keys.Base(), keys.Count() ) != 0;
	}

	virtual void OnBind( void *pArg ) OVERRIDE
	{
		IManagedMaterialProxyClient *pManaged = ManagedMaterialProxyClient();
		if ( !pManaged )
			return;

		SourceSharpProxyBindIn_t in;
		in.CurTime = gpGlobals->curtime;
		in.FrameTime = gpGlobals->frametime;
		in.MaxClients = gpGlobals->maxClients;
		in.FrameCount = gpGlobals->framecount;
		in.VarCount = m_Vars.Count();
		in.Reserved = 0;
		in.Arg = pArg;

		CUtlVectorFixedGrowable< SourceSharpProxyVar_t, 16 > vars;
		vars.SetCount( m_Vars.Count() );
		for ( int i = 0; i < m_Vars.Count(); ++i )
			ReadVar( m_Vars[ i ], vars[ i ] );

		SourceSharpProxyOp_t ops[ kMaxProxyOps ];
		m_pBindArg = pArg;
		m_pBindVars = vars.Base();
		const int32_t nOps = pManaged->ProxyBind( m_hProxy, &in, vars.Base(), ops, kMaxProxyOps );
		m_pBindArg = NULL;
		m_pBindVars = NULL;

		for ( int i = 0; i < nOps && i < kMaxProxyOps; ++i )
			Apply( ops[ i ] );
	}

	virtual void Release() OVERRIDE
	{
		if ( IManagedMaterialProxyClient *pManaged = ManagedMaterialProxyClient() )
			pManaged->ProxyRelease( m_hProxy );
		for ( int i = 0; i < m_Textures.Count(); ++i )
			m_Textures[ i ]->DecrementReferenceCount();
		m_Textures.RemoveAll();
		s_ManagedProxies.Remove( m_hProxy );
		delete this;
	}

	virtual IMaterial *GetMaterial() OVERRIDE
	{
		return m_pMaterial;
	}

	int32_t FindVar( const char *pszName, bool bComplain, int32_t *pFound )
	{
		bool bFound = false;
		IMaterialVar *pVar = m_pMaterial ? m_pMaterial->FindVar( pszName, &bFound, bComplain ) : NULL;
		if ( pFound )
			*pFound = bFound ? 1 : 0;
		if ( !pVar )
			return -1;
		return m_Vars.AddToTail( pVar );
	}

	int32_t FindTexture( const char *pszName, const char *pszGroup, bool bComplain, int32_t *pIsError )
	{
		ITexture *pTexture = materials->FindTexture( pszName, pszGroup, bComplain );
		if ( pIsError )
			*pIsError = ( pTexture && IsErrorTexture( pTexture ) ) ? 1 : 0;
		if ( !pTexture )
			return -1;
		pTexture->IncrementReferenceCount();
		return m_Textures.AddToTail( pTexture );
	}

	int32_t VarTexture( int32_t nSlot, char *pOutName, int32_t nOutSize )
	{
		if ( pOutName && nOutSize > 0 )
			pOutName[ 0 ] = 0;
		if ( !m_Vars.IsValidIndex( nSlot ) )
			return -1;
		IMaterialVar *pVar = m_Vars[ nSlot ];
		ITexture *pTexture = pVar->GetTextureValue();

		// GetTextureValue loads a string var's texture and makes the var a texture; managed
		// reads the var as it is now for the rest of this bind.
		if ( m_pBindVars )
			ReadVar( pVar, m_pBindVars[ nSlot ] );

		if ( !pTexture )
			return -1;
		if ( pOutName && nOutSize > 0 )
			V_strncpy( pOutName, pTexture->GetName(), nOutSize );
		return pTexture->GetNumAnimationFrames();
	}

	int32_t BindEntity( SourceSharpProxyEntity_t *pOut );

private:
	static void ReadVar( IMaterialVar *pVar, SourceSharpProxyVar_t &out )
	{
		float v[ 4 ] = { 0, 0, 0, 0 };
		pVar->GetVecValue( v, 4 );
		out.Type = (int32_t)pVar->GetType();
		out.VecSize = pVar->VectorSize();
		out.Int = pVar->GetIntValue();
		out.Float = pVar->GetFloatValue();
		out.V0 = v[ 0 ];
		out.V1 = v[ 1 ];
		out.V2 = v[ 2 ];
		out.V3 = v[ 3 ];
	}

	void Apply( const SourceSharpProxyOp_t &op )
	{
		if ( !m_Vars.IsValidIndex( op.Slot ) )
			return;
		IMaterialVar *pVar = m_Vars[ op.Slot ];
		switch ( op.Kind )
		{
		case kProxyOpFloat:
			pVar->SetFloatValue( op.Values[ 0 ] );
			break;
		case kProxyOpInt:
			pVar->SetIntValue( op.Arg );
			break;
		case kProxyOpVecN:
			pVar->SetVecValue( op.Values, op.Arg );
			break;
		case kProxyOpVec3:
			pVar->SetVecValue( op.Values[ 0 ], op.Values[ 1 ], op.Values[ 2 ] );
			break;
		case kProxyOpVecComponent:
			pVar->SetVecComponentValue( op.Values[ 0 ], op.Arg );
			break;
		case kProxyOpMatrix:
		{
			VMatrix mat;
			V_memcpy( mat.Base(), op.Values, sizeof( op.Values ) );
			pVar->SetMatrixValue( mat );
			break;
		}
		case kProxyOpTexture:
			if ( m_Textures.IsValidIndex( op.Arg ) )
				pVar->SetTextureValue( m_Textures[ op.Arg ] );
			break;
		}
	}

	int m_nName;
	int32_t m_hProxy;
	IMaterial *m_pMaterial;
	CUtlVector< IMaterialVar * > m_Vars;
	CUtlVector< ITexture * > m_Textures;
	void *m_pBindArg;
	SourceSharpProxyVar_t *m_pBindVars;
};

// The bind argument as stock's entity proxies read it, with the render alpha and FL_NPC
// read through the slot table.
int32_t CManagedMaterialProxy::BindEntity( SourceSharpProxyEntity_t *pOut )
{
	V_memset( pOut, 0, sizeof( *pOut ) );
	pOut->EntIndex = -1;
	pOut->RenderAlpha = -1;
	pOut->IsNpc = -1;
	if ( !m_pBindArg )
		return 0;

	IClientRenderable *pRenderable = (IClientRenderable *)m_pBindArg;
	IClientUnknown *pUnknown = pRenderable->GetIClientUnknown();
	if ( !pUnknown )
		return 0;
	pOut->HasUnknown = 1;

	if ( IClientEntity *pEntity = pUnknown->GetIClientEntity() )
	{
		const Vector &origin = pEntity->GetAbsOrigin();
		pOut->EntIndex = pEntity->entindex();
		pOut->OriginXYZ[ 0 ] = origin.x;
		pOut->OriginXYZ[ 1 ] = origin.y;
		pOut->OriginXYZ[ 2 ] = origin.z;
	}

	if ( C_StandaloneEntity *pShell = SourceSharp_ClientShellFromHandleEntity( pUnknown ) )
	{
		pOut->RenderAlpha = (int32_t)pShell->Block().Client.RenderColorA;
		pOut->IsNpc = ( pShell->Block().Head.Flags & FL_NPC ) ? 1 : 0;
	}
	return 1;
}

// One registration per name, each with its own create function, so the trampoline knows
// which proxy it is without a lookup.
template < int N >
static void *CreateManagedMaterialProxy()
{
	return static_cast< IMaterialProxy * >( new CManagedMaterialProxy( N ) );
}

#define SS_PROXY_REG( i, name ) \
	static InterfaceReg s_ManagedProxyReg##i( &CreateManagedMaterialProxy< i >, #name IMATERIAL_PROXY_INTERFACE_VERSION );
SS_PROXY_NAMES( SS_PROXY_REG )
#undef SS_PROXY_REG

int32_t SourceSharp_ProxyFindVar( int32_t hProxy, const char *pszName, int32_t bComplain, int32_t *pFound )
{
	CManagedMaterialProxy *pProxy = s_ManagedProxies.Find( hProxy );
	if ( pFound )
		*pFound = 0;
	return pProxy && pszName ? pProxy->FindVar( pszName, bComplain != 0, pFound ) : -1;
}

int32_t SourceSharp_ProxyFindTexture( int32_t hProxy, const char *pszName, const char *pszGroup,
                                      int32_t bComplain, int32_t *pIsError )
{
	CManagedMaterialProxy *pProxy = s_ManagedProxies.Find( hProxy );
	if ( pIsError )
		*pIsError = 0;
	return pProxy && pszName && pszGroup ? pProxy->FindTexture( pszName, pszGroup, bComplain != 0, pIsError ) : -1;
}

int32_t SourceSharp_ProxyVarTexture( int32_t hProxy, int32_t nSlot, char *pOutName, int32_t nOutSize )
{
	CManagedMaterialProxy *pProxy = s_ManagedProxies.Find( hProxy );
	return pProxy ? pProxy->VarTexture( nSlot, pOutName, nOutSize ) : -1;
}

int32_t SourceSharp_ProxyBindEntity( int32_t hProxy, SourceSharpProxyEntity_t *pOut )
{
	CManagedMaterialProxy *pProxy = s_ManagedProxies.Find( hProxy );
	if ( !pProxy || !pOut )
		return 0;
	return pProxy->BindEntity( pOut );
}

// 0: CurrentViewOrigin (3). 1: view->GetWaterLODParams (2). 2: the world's mins and maxs
// (6), 0 with no world. 3: the particle manager's directional light (position, colour,
// intensity: 7) and model-view matrix (16, row-major).
int32_t SourceSharp_ProxyReadView( int32_t nKind, float *pOut, int32_t nFloats )
{
	if ( !pOut )
		return 0;
	if ( nKind == 0 && nFloats >= 3 )
	{
		const Vector &v = CurrentViewOrigin();
		pOut[ 0 ] = v.x;
		pOut[ 1 ] = v.y;
		pOut[ 2 ] = v.z;
		return 1;
	}
	if ( nKind == 1 && nFloats >= 2 )
	{
		float flStart = 0.0f, flEnd = 0.0f;
		if ( view )
			view->GetWaterLODParams( flStart, flEnd );
		pOut[ 0 ] = flStart;
		pOut[ 1 ] = flEnd;
		return 1;
	}
	if ( nKind == 2 && nFloats >= 6 )
	{
		C_World *pWorld = GetClientWorldEntity();
		if ( !pWorld )
			return 0;
		const Vector vecMins = pWorld->GetWorldMins();
		const Vector vecMaxs = pWorld->GetWorldMaxs();
		pOut[ 0 ] = vecMins.x;
		pOut[ 1 ] = vecMins.y;
		pOut[ 2 ] = vecMins.z;
		pOut[ 3 ] = vecMaxs.x;
		pOut[ 4 ] = vecMaxs.y;
		pOut[ 5 ] = vecMaxs.z;
		return 1;
	}
	if ( nKind == 3 && nFloats >= 23 )
	{
		CParticleLightInfo info;
		ShellParticleDraw_GetLight( info );
		const VMatrix &mModelView = ShellParticleDraw_ModelView();
		pOut[ 0 ] = info.m_vPos.x;
		pOut[ 1 ] = info.m_vPos.y;
		pOut[ 2 ] = info.m_vPos.z;
		pOut[ 3 ] = info.m_vColor.x;
		pOut[ 4 ] = info.m_vColor.y;
		pOut[ 5 ] = info.m_vColor.z;
		pOut[ 6 ] = info.m_flIntensity;
		for ( int i = 0; i < 16; ++i )
			pOut[ 7 + i ] = mModelView.m[ i / 4 ][ i % 4 ];
		return 1;
	}
	return 0;
}

int32_t SourceSharp_ProxyPlayerCustomFile( int32_t nPlayer, uint32_t *pCrc )
{
	if ( pCrc )
		*pCrc = 0;
	player_info_t info;
	if ( !engine->GetPlayerInfo( nPlayer, &info ) )
		return 0;
	if ( pCrc )
		*pCrc = (uint32_t)info.customFiles[ 0 ];
	return 1;
}

// The not-shipped ledger keeps the name and reason pointers it is given, so a managed
// refusal's text is interned here once per name for the process.
class CProxyRefusalNames
{
public:
	void Reach( const char *pszCapability, const char *pszReason )
	{
		int i = 0;
		for ( ; i < m_Names.Count(); ++i )
		{
			if ( !V_strcmp( m_Names[ i ], pszCapability ) )
				break;
		}
		if ( i == m_Names.Count() )
		{
			m_Names.AddToTail( V_strdup( pszCapability ) );
			m_Reasons.AddToTail( V_strdup( pszReason ) );
		}
		SourceSharp_NotShipped( m_Names[ i ], m_Reasons[ i ] );
	}

private:
	CUtlVector< char * > m_Names;
	CUtlVector< char * > m_Reasons;
};

static CProxyRefusalNames s_ProxyRefusalNames;

void SourceSharp_ProxyNotShipped( const char *pszCapability, const char *pszReason )
{
	if ( pszCapability && pszReason )
		s_ProxyRefusalNames.Reach( pszCapability, pszReason );
}

// While mat_stub is on (managed's convar), the frame renders through the material system stub.
CMatStubHandler::CMatStubHandler()
{
	m_pOldMaterialSystem = NULL;
	IManagedMaterialProxyClient *pManaged = ManagedMaterialProxyClient();
	if ( !pManaged || !pManaged->MatStubEnabled() || !materials_stub )
		return;

	m_pOldMaterialSystem = materials;
	materials_stub->SetRealMaterialSystem( materials );
	materials->SetInStubMode( true );
	materials = materials_stub;
	engine->Mat_Stub( materials );
}

CMatStubHandler::~CMatStubHandler()
{
	End();
}

void CMatStubHandler::End()
{
	if ( !m_pOldMaterialSystem )
		return;

	materials = m_pOldMaterialSystem;
	materials->SetInStubMode( false );
	engine->Mat_Stub( materials );
	m_pOldMaterialSystem = NULL;
	materials->ClearBuffers( true, true );
}
