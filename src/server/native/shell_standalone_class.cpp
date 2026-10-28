//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
//
// The classnames the standalone hierarchy hosts: one factory per class C# declares, its
// override mask, the blob offset, and the entity factory dictionary.

#include "shell_standalone_player.h"
#include "entity_op.h"

#include "generic_nettable.h"
#include "entity_factory_table.h"

#include <map>

#include <unordered_map>

#include "ss_entityfactory.h"
#include "shell_entity_lifecycle.h"
#include "server_entity_context.h"

void SourceSharp_LogEntityCreate( const char *pClassName );
void SourceSharp_LogEntityCreateFailed( const char *pClassName );
void SourceSharp_LogEntityDestroy( const char *pClassName );

// One registered class and its factory. Lives as long as the lifecycle that installed it;
// entities hold pointers to its descriptor and interned strings.
class CStandaloneScriptedClass final : public ss::IEntityFactory
{
public:
	CStandaloneScriptedClass( const char *pClassName, const char **ppOutputNames, int nOutputCount, bool bServerOnly,
							  SolidType_t nSolidType, int nSolidFlags, bool bTouchEveryTick, bool bPlayer )
	{
		memset( &m_Desc, 0, sizeof( m_Desc ) );
		Describe( pClassName, ppOutputNames, nOutputCount, bServerOnly, nSolidType, nSolidFlags, bTouchEveryTick,
				  bPlayer );
	}

	// Re-described, not re-installed, when C# reloads: the dictionary cannot remove an
	// entry and the engine keeps the factory pointer it was given.
	void Describe( const char *pClassName, const char **ppOutputNames, int nOutputCount, bool bServerOnly,
				   SolidType_t nSolidType, int nSolidFlags, bool bTouchEveryTick, bool bPlayer )
	{
		// Outputs are managed; the desc's output fields stay empty.
		(void)ppOutputNames;
		(void)nOutputCount;

		// Claimed once: it is the key InstallFactory used and what GetClassName answers.
		if ( !m_Desc.pClassName )
			m_Desc.pClassName = Keep( pClassName );

		m_Desc.ppOutputNames = NULL;
		m_Desc.nOutputCount = 0;
		m_Desc.bServerOnly = bServerOnly;
		m_Desc.nSolidType = nSolidType;
		m_Desc.nSolidFlags = nSolidFlags;
		m_Desc.bTouchEveryTick = bTouchEveryTick;
		m_Desc.bPlayer = bPlayer;

		// Types are published after registration, so the tier is looked up on first Create.
		m_Desc.iTier = -1;

		// All ones until RegisterClassOps arrives, and reset on every re-describe so a reload
		// that dropped an override does not keep the old mask.
		m_Desc.nOverrideMask = ENTITY_OP_MASK_ALL;
	}

	// ENTITY_OP_ALWAYS_MASK is OR'd in here so a wrong managed mask can never switch off
	// the bookkeeping ops (spawn, keyvalue, removed, filter).
	void SetOps( uint32_t nOpMask ) { m_Desc.nOverrideMask = nOpMask | ENTITY_OP_ALWAYS_MASK; }

	uint32_t Ops() const { return m_Desc.nOverrideMask; }

	const char *GetClassName() const { return m_Desc.pClassName; }

	// The tier this class's entities are allocated at, looked up late because types are
	// published after registration. A miss is not cached: pinning tier 0 would let C# write
	// a larger layout into a short blob. Tier 0 for a class never published.
	int ResolveTier()
	{
		if ( m_Desc.iTier >= 0 )
			return m_Desc.iTier;

		const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ByName( m_Desc.pClassName );

		// Nothing published yet: answer the floor and DO NOT remember it.
		if ( !pType || pType->iTier < 0 )
			return 0;

		m_Desc.iTier = pType->iTier;

		return m_Desc.iTier;
	}

	IServerNetworkable *Create( const char *pClassName, CServerEntityContext &context ) OVERRIDE
	{
		SourceSharp_LogEntityCreate( pClassName );

		// One read feeds both the allocation length and the instance's tier.
		const int iTier = ResolveTier();

		// The player is the one subclass, chosen here where the class is known.
		CStandaloneEntity *pEnt = m_Desc.bPlayer ? new ( SourceSharp_TierBytes( iTier ) ) CStandalonePlayer( context )
												 : new ( SourceSharp_TierBytes( iTier ) ) CStandaloneEntity( context );

		if ( !pEnt )
		{
			SourceSharp_LogEntityCreateFailed( pClassName );
			return NULL;
		}

		// Before the map hands it a single key (see PostConstruct).
		pEnt->PostConstruct( &m_Desc );

		return pEnt;
	}

	void Destroy( IServerNetworkable *pNetworkable ) OVERRIDE
	{
		SourceSharp_LogEntityDestroy( m_Desc.pClassName );

		if ( pNetworkable )
			pNetworkable->Release();
	}

	size_t GetEntitySize() OVERRIDE
	{
		return m_Desc.bPlayer ? sizeof( CStandalonePlayer ) : sizeof( CStandaloneEntity );
	}

private:
	// Interned for the life of this object.
	const char *Keep( const char *psz ) { return m_Kept.Intern( psz ); }

	StandaloneClassDesc_t m_Desc;
	ss::StringPool m_Kept;
};

// The blob offset: past the concrete shell, 16-aligned.
int SourceSharp_StandaloneBlobOffset()
{
	// The largest shell, since the player shares the tiers' absolute prop offsets.
	const size_t nLargest = MAX( sizeof( CStandaloneEntity ), sizeof( CStandalonePlayer ) );

	return (int)ALIGN_VALUE( nLargest, 16 );
}

// The entity factory dictionary. Native because Create is called synchronously from the
// map parse and CreateEntityByName. The name table is entity_factory_table.h.

namespace
{

// Refusals are printed once per classname and tallied for dumpentityfactories; the
// tally survives map changes.
class CSourceSharpFactoryDictionary : public ss::IEntityFactoryDictionary
{
public:
	explicit CSourceSharpFactoryDictionary( CServerEntityContext &entities )
		: m_Entities( entities )
	{
	}

	virtual void InstallFactory( ss::IEntityFactory *pFactory, const char *pClassName ) OVERRIDE
	{
		switch ( m_Table.Install( pFactory, pClassName ) )
		{
			case kEntityFactoryInstalled:
			case kEntityFactoryAlreadyInstalled:
				// A reload re-declaring the same factory object; silent.
				return;

			case kEntityFactoryNameTaken:
				// The first is kept: the engine may hold instances made through it.
				Warning( "[SourceSharp] two entity factories claim the classname '%s'; "
						 "keeping the first, the second will never be reached\n",
						 pClassName ? pClassName : "(null)" );
				return;

			case kEntityFactoryNoName:
				Warning( "[SourceSharp] an entity factory was installed with no classname; refused\n" );
				return;
		}
	}

	virtual ss::IEntityFactory *FindFactory( const char *pClassName ) OVERRIDE { return m_Table.Find( pClassName ); }

	virtual const char *GetCannonicalName( const char *pClassName ) OVERRIDE
	{
		return m_Table.CanonicalName( pClassName );
	}

	virtual IServerNetworkable *Create( const char *pClassName ) OVERRIDE
	{
		ss::IEntityFactory *pFactory = m_Table.Find( pClassName );

		if ( !pFactory )
		{
			Refuse( pClassName );
			return NULL;
		}

		return pFactory->Create( pClassName, m_Entities );
	}

	virtual void Destroy( const char *pClassName, IServerNetworkable *pNetworkable ) OVERRIDE
	{
		ss::IEntityFactory *pFactory = m_Table.Find( pClassName );

		if ( !pFactory )
		{
			// Not counted as a refusal: the caller holds something this dictionary did not make.
			Warning( "[SourceSharp] asked to destroy a '%s', which no factory here made\n",
					 pClassName ? pClassName : "(null)" );
			return;
		}

		pFactory->Destroy( pNetworkable );
	}

	CEntityFactoryTable<ss::IEntityFactory> m_Table;

	CServerEntityContext &m_Entities;

	// Classname to refusal count, case-blind; m_RefusedOrder is first-refusal order.
	typedef std::map<std::string, int, ss::CaselessLess> Refused_t;
	Refused_t m_Refused;
	std::vector<Refused_t::value_type *> m_RefusedOrder;

private:
	void Refuse( const char *pClassName )
	{
		if ( !pClassName || !pClassName[0] )
		{
			Warning( "[SourceSharp] REFUSED: an entity was asked for with no classname\n" );
			return;
		}

		Refused_t::iterator it = m_Refused.find( pClassName );

		if ( it != m_Refused.end() )
		{
			it->second++;
			return;
		}

		m_RefusedOrder.push_back( &*m_Refused.emplace( pClassName, 1 ).first );

		Warning( "[SourceSharp] REFUSED: no entity factory answers to '%s'. Nothing of "
				 "that class will be created, in this map or any other, until one is "
				 "registered -- see `dumpentityfactories`\n",
				 pClassName );
	}
};

} // namespace

std::unique_ptr<ss::IEntityFactory> SourceSharp_NewWorldFactory();

struct CServerEntityLifecycle::Impl
{
	explicit Impl( CServerEntityContext &entities )
		: m_Factories( entities )
	{
	}

	CSourceSharpFactoryDictionary m_Factories;

	// The classes this hierarchy hosts, by factory, case-blind. Other shells register
	// factories too, so "has a factory" is not the same question.
	std::unordered_map<std::string, std::unique_ptr<CStandaloneScriptedClass>, ss::CaselessHash, ss::CaselessEqual>
		m_Classes;

	std::unique_ptr<ss::IEntityFactory> m_pWorldFactory;

	CStandaloneScriptedClass *Find( const char *pClassName )
	{
		auto it = m_Classes.find( pClassName );
		return ( it != m_Classes.end() ) ? it->second.get() : NULL;
	}
};

CServerEntityLifecycle::CServerEntityLifecycle( CServerEntityContext &entities )
	: m_Entities( entities )
	, m_pImpl( new Impl( entities ), &CServerEntityLifecycle::DeleteImpl )
{
	m_pImpl->m_pWorldFactory = SourceSharp_NewWorldFactory();
	m_pImpl->m_Factories.InstallFactory( m_pImpl->m_pWorldFactory.get(), "worldspawn" );
}

void CServerEntityLifecycle::DeleteImpl( Impl *pImpl )
{
	delete pImpl;
}

ss::IEntityFactoryDictionary &CServerEntityLifecycle::Factories()
{
	return m_pImpl->m_Factories;
}

bool CServerEntityLifecycle::InstallStandaloneClass( const char *pClassName, const char **ppOutputNames,
													 int nOutputCount, bool bServerOnly, int nSolidType,
													 int nSolidFlags, bool bTouchEveryTick, bool bPlayer )
{
	if ( !pClassName || !pClassName[0] )
		return false;

	if ( CStandaloneScriptedClass *pExisting = m_pImpl->Find( pClassName ) )
	{
		// Take the new declaration without installing a second time.
		pExisting->Describe( pClassName, ppOutputNames, nOutputCount, bServerOnly, (SolidType_t)nSolidType, nSolidFlags,
							 bTouchEveryTick, bPlayer );
		return true;
	}

	auto pClass =
		std::make_unique<CStandaloneScriptedClass>( pClassName, ppOutputNames, nOutputCount, bServerOnly,
													(SolidType_t)nSolidType, nSolidFlags, bTouchEveryTick, bPlayer );
	CStandaloneScriptedClass *pInstalled = pClass.get();
	m_pImpl->m_Classes.emplace( pInstalled->GetClassName(), std::move( pClass ) );

	m_pImpl->m_Factories.InstallFactory( pInstalled, pInstalled->GetClassName() );
	return true;
}

bool CServerEntityLifecycle::SetStandaloneClassOps( const char *pClassName, uint32_t nOpMask )
{
	if ( !pClassName || !pClassName[0] )
		return false;

	CStandaloneScriptedClass *pClass = m_pImpl->Find( pClassName );

	if ( !pClass )
		return false;

	pClass->SetOps( nOpMask );
	return true;
}

void CServerEntityLifecycle::RegisterClassOps( const char *pClassName, uint32_t nOpMask )
{
	SetStandaloneClassOps( pClassName, nOpMask );
}

// Enumeration, for the console dumps and the managed side.
int32_t CServerEntityLifecycle::EntityFactoryCount()
{
	return m_pImpl->m_Factories.m_Table.Count();
}

const char *CServerEntityLifecycle::EntityFactoryNameAt( int32_t i )
{
	return m_pImpl->m_Factories.m_Table.NameAt( i );
}

int32_t CServerEntityLifecycle::EntityFactoryRefusalCount()
{
	return (int32_t)m_pImpl->m_Factories.m_RefusedOrder.size();
}

const char *CServerEntityLifecycle::EntityFactoryRefusalNameAt( int32_t i )
{
	CSourceSharpFactoryDictionary &d = m_pImpl->m_Factories;

	if ( i < 0 || i >= (int)d.m_RefusedOrder.size() )
		return NULL;

	return d.m_RefusedOrder[i]->first.c_str();
}

int32_t CServerEntityLifecycle::EntityFactoryRefusalTimesAt( int32_t i )
{
	CSourceSharpFactoryDictionary &d = m_pImpl->m_Factories;

	if ( i < 0 || i >= (int)d.m_RefusedOrder.size() )
		return 0;

	return d.m_RefusedOrder[i]->second;
}

size_t CServerEntityLifecycle::EntityFactorySizeAt( int32_t i )
{
	ss::IEntityFactory *pFactory = m_pImpl->m_Factories.m_Table.FactoryAt( i );
	return pFactory ? pFactory->GetEntitySize() : 0;
}
