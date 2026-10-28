//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: particle system definitions

#pragma once

#include "dmxloader/dmxelement.h"

#include "tier1/utlobjectreference.h"
#include "tier1/UtlStringMap.h"

#include "materialsystem/MaterialSystemUtil.h"
class CBaseTrace;
#include "tier1/utlsoacontainer.h"

// Forward declarations
struct DmxElementUnpackStructure_t;
class CParticleSystemDefinition;
class CParticleCollection;
class CParticleOperatorInstance;
class CParticleSystemDictionary;
class CUtlBuffer;
class IParticleOperatorDefinition;
class CSheet;
class CMeshBuilder;
extern float s_pRandomFloats[];

// Random numbers
#define MAX_RANDOM_FLOATS 4096
#define RANDOM_FLOAT_MASK ( MAX_RANDOM_FLOATS - 1 )

// Particle attributes
#define MAX_PARTICLE_ATTRIBUTES 32

#define DEFPARTICLE_ATTRIBUTE( name, bit )                     \
	const int PARTICLE_ATTRIBUTE_##name##_MASK = ( 1 << bit ); \
	const int PARTICLE_ATTRIBUTE_##name = bit;

// required
DEFPARTICLE_ATTRIBUTE( XYZ, 0 );

// particle lifetime (duration) of particle as a float.
DEFPARTICLE_ATTRIBUTE( LIFE_DURATION, 1 );

// prev coordinates for verlet integration
DEFPARTICLE_ATTRIBUTE( PREV_XYZ, 2 );

// radius of particle
DEFPARTICLE_ATTRIBUTE( RADIUS, 3 );

// rotation angle of particle
DEFPARTICLE_ATTRIBUTE( ROTATION, 4 );

// rotation speed of particle
DEFPARTICLE_ATTRIBUTE( ROTATION_SPEED, 5 );

// tint of particle
DEFPARTICLE_ATTRIBUTE( TINT_RGB, 6 );

// alpha tint of particle
DEFPARTICLE_ATTRIBUTE( ALPHA, 7 );

// creation time stamp (relative to particle system creation)
DEFPARTICLE_ATTRIBUTE( CREATION_TIME, 8 );

// sequnece # (which animation sequence number this particle uses )
DEFPARTICLE_ATTRIBUTE( SEQUENCE_NUMBER, 9 );

// length of the trail
DEFPARTICLE_ATTRIBUTE( TRAIL_LENGTH, 10 );

// unique particle identifier
DEFPARTICLE_ATTRIBUTE( PARTICLE_ID, 11 );

// unique rotation around up vector
DEFPARTICLE_ATTRIBUTE( YAW, 12 );

// second sequnece # (which animation sequence number this particle uses )
DEFPARTICLE_ATTRIBUTE( SEQUENCE_NUMBER1, 13 );

// hit box index
DEFPARTICLE_ATTRIBUTE( HITBOX_INDEX, 14 );

DEFPARTICLE_ATTRIBUTE( HITBOX_RELATIVE_XYZ, 15 );

DEFPARTICLE_ATTRIBUTE( ALPHA2, 16 );

// particle trace caching fields
DEFPARTICLE_ATTRIBUTE( TRACE_P0, 17 ); // start pnt of trace
DEFPARTICLE_ATTRIBUTE( TRACE_P1, 18 ); // end pnt of trace
DEFPARTICLE_ATTRIBUTE( TRACE_HIT_T, 19 ); // 0..1 if hit
DEFPARTICLE_ATTRIBUTE( TRACE_HIT_NORMAL, 20 ); // 0 0 0 if no hit

// target cp index
DEFPARTICLE_ATTRIBUTE( CONTROL_POINT_INDEX, 21 );

#define MAX_PARTICLE_CONTROL_POINTS 64

// Set this to 1 or 0 to enable or disable particle profiling.
// Note that this profiling is expensive on Linux, and some anti-virus
// products can make this *extremely* expensive on Windows.
#define MEASURE_PARTICLE_PERF 0

// Particle function types
enum ParticleFunctionType_t
{
	FUNCTION_RENDERER = 0,
	FUNCTION_OPERATOR,
	FUNCTION_INITIALIZER,
	FUNCTION_EMITTER,
	FUNCTION_CHILDREN, // NOTE: This one is a fake function type, only here to help eliminate a ton of duplicated code in the editor
	FUNCTION_FORCEGENERATOR,
	FUNCTION_CONSTRAINT,
	PARTICLE_FUNCTION_COUNT
};

struct ModelHitBoxInfo_t
{
	Vector m_vecBoxMins;
	Vector m_vecBoxMaxes;
	matrix3x4_t m_Transform;
};

class CModelHitBoxesInfo
{
public:
	float m_flLastUpdateTime;
	alignas( 4 ) unsigned char unused_m_flPrevLastUpdateTime[4];
	int m_nNumHitBoxes;
	int m_nNumPrevHitBoxes;
	ModelHitBoxInfo_t *m_pHitBoxes;
	ModelHitBoxInfo_t *m_pPrevBoxes;

	CModelHitBoxesInfo()
	{
		m_flLastUpdateTime = -1;
		m_nNumHitBoxes = 0;
		m_nNumPrevHitBoxes = 0;
		m_pHitBoxes = NULL;
		m_pPrevBoxes = NULL;
	}

	~CModelHitBoxesInfo()
	{
		if ( m_pHitBoxes )
			delete[] m_pHitBoxes;
		if ( m_pPrevBoxes )
			delete[] m_pPrevBoxes;
	}
};

// ParticlesCheckFloat

// Interface to allow the particle system to call back into the client

#define PARTICLE_SYSTEM_QUERY_INTERFACE_VERSION "VParticleSystemQuery001"

class IParticleSystemQuery : public IAppSystem
{
public:
	virtual void GetLightingAtPoint( const Vector &vecOrigin, Color &tint ) = 0;
	virtual void TraceLine( const Vector &vecAbsStart, const Vector &vecAbsEnd, unsigned int mask,
							const class IHandleEntity *ignore, int collisionGroup, CBaseTrace *ptr ) = 0;

	// given a possible spawn point, tries to movie it to be on or in the source object. returns
	// true if it succeeded
	virtual bool MovePointInsideControllingObject( CParticleCollection *pParticles, void *pObject, Vector *pPnt )
	{
		return true;
	}

	virtual bool IsPointInControllingObjectHitBox( CParticleCollection *pParticles, int nControlPointNumber,
												   Vector vecPos, bool bBBoxOnly = false )
	{
		return true;
	}

	virtual int GetCollisionGroupFromName( const char *pszCollisionGroupName )
	{
		return 0; // == COLLISION_GROUP_NONE
	}

	virtual void GetRandomPointsOnControllingObjectHitBox( CParticleCollection *pParticles, int nControlPointNumber,
														   int nNumPtsOut, float flBBoxScale,
														   int nNumTrysToGetAPointInsideTheModel, Vector *pPntsOut,
														   Vector vecDirectionBias,
														   Vector *pHitBoxRelativeCoordOut = NULL,
														   int *pHitBoxIndexOut = NULL ) = 0;

	virtual int GetControllingObjectHitBoxInfo( CParticleCollection *pParticles, int nControlPointNumber,
												int nBufSize, // # of output slots available
												ModelHitBoxInfo_t *pHitBoxOutputBuffer )
	{
		// returns number of hit boxes output
		return 0;
	}

	virtual Vector GetLocalPlayerPos() { return vec3_origin; }

	virtual void GetLocalPlayerEyeVectors( Vector *pForward, Vector *pRight = NULL, Vector *pUp = NULL )
	{
		*pForward = vec3_origin;
		*pRight = vec3_origin;
		*pUp = vec3_origin;
	}

	virtual float GetPixelVisibility( int *pQueryHandle, const Vector &vecOrigin, float flScale ) = 0;

	virtual void SetUpLightingEnvironment( const Vector &pos ) {}
};

// Particle system manager. Using a class because tools need it that way
// so the SFM and PET tools can share managers despite being linked to
// separate particle system .libs
typedef int ParticleSystemHandle_t;

class CParticleSystemMgr
{
public:
	// Constructor, destructor
	CParticleSystemMgr();
	~CParticleSystemMgr();

	// Initialize the particle system
	bool Init( IParticleSystemQuery *pQuery );

	// methods to add builtin operators. If you don't call these at startup, you won't be able to sim or draw. These are done separately from Init, so that
	// the server can omit the code needed for rendering/simulation, if desired.
	void AddBuiltinSimulationOperators();
	void AddBuiltinRenderingOperators();

	// Read a particle config file, add it to the list of particle configs
	bool ReadParticleConfigFile( const char *pFileName, bool bPrecache, bool bDecommitTempMemory = true );
	void DecommitTempMemory();

	// Returns the name of the specified particle system.
	const char *GetParticleSystemNameFromIndex( ParticleSystemHandle_t iIndex );

	// Return the number of particle systems in our dictionary
	int GetParticleSystemCount();

	// Particle sheet management
	void ShouldLoadSheets( bool bLoadSheets );

	// Render cache used to render opaque particle collections
	void ResetRenderCache();
	void AddToRenderCache( CParticleCollection *pParticles );
	void DrawRenderCache( bool bShadowDepth );

	// WARNING: the pointer returned by this function may be invalidated
	// *at any time* by the editor, so do not ever cache it.
	CParticleSystemDefinition *FindParticleSystem( const char *pName );

	void CommitProfileInformation( bool bCommit ); // call after simulation, if you want
	// sim time recorded. if oyu pass
	// flase, info will be thrown away and
	// uncomitted time reset.  Having this
	// function lets you only record
	// profile data for slow frames if
	// desired.

	void DumpProfileInformation(); // write particle_profile.csv

	// Cache/uncache materials used by particle systems
	void PrecacheParticleSystem( const char *pName );
	void UncacheAllParticleSystems();

	// Sets the last simulation time, used for particle system sleeping logic
	void SetLastSimulationTime( float flTime );
	float GetLastSimulationTime() const;

	bool Debug_FrameWarningNeededTestAndReset();
	float ParticleThrottleScaling() const; // Returns 1.0 = not restricted, 0.0 = fully restricted (i.e. don't draw!)

private:
	struct RenderCache_t
	{
		IMaterial *m_pMaterial;
		CUtlVector<CParticleCollection *> m_ParticleCollections;
	};

	struct BatchStep_t
	{
		CParticleCollection *m_pParticles;
		CParticleOperatorInstance *m_pRenderer;
		void *m_pContext;
		int m_nFirstParticle;
		int m_nParticleCount;
		int m_nVertCount;
	};

	struct Batch_t
	{
		int m_nVertCount;
		int m_nIndexCount;
		CUtlVector<BatchStep_t> m_BatchStep;
	};

	// Known operators
	CUtlVector<IParticleOperatorDefinition *> m_ParticleOperators[PARTICLE_FUNCTION_COUNT];

	// Particle system dictionary
	CParticleSystemDictionary *m_pParticleSystemDictionary;

	// typedef CUtlMap< ITexture *, CSheet* > SheetsCache;
	typedef CUtlStringMap<CSheet *> SheetsCache_t;
	alignas( 8 ) unsigned char unused_m_SheetList[144];
	IParticleSystemQuery *m_pQuery;
	CUtlVector<RenderCache_t> m_RenderCache;
	IMaterial *m_pShadowDepthMaterial;
	float m_flLastSimulationTime;

	bool m_bDidInit;
	bool m_bUsingDefaultQuery;
	bool m_bShouldLoadSheets;

	int m_nNumFramesMeasured;

	enum
	{
		c_nNumFramesTracked = 10
	};

	int m_nParticleVertexCountHistory[c_nNumFramesTracked];
	float m_fParticleCountScaling;
	int m_nParticleIndexCount;
	int m_nParticleVertexCount;
	bool m_bFrameWarningNeeded;

	friend class CParticleSystemDefinition;
	friend class CParticleCollection;
};

extern CParticleSystemMgr *g_pParticleSystemMgr;

// A particle system can only have 1 operator using a particular ID
enum ParticleOperatorId_t
{
	// Generic IDs
	OPERATOR_GENERIC = -2, // Can have as many of these as you want
	OPERATOR_SINGLETON = -1, // Can only have 1 operator with the same name as this one

	// Renderer operator IDs

	// Operator IDs

	// Initializer operator IDs
	OPERATOR_PI_POSITION, // Particle initializer: position (can only have 1 position setter)
	OPERATOR_PI_RADIUS,
	OPERATOR_PI_ALPHA,
	OPERATOR_PI_TINT_RGB,
	OPERATOR_PI_ROTATION,
	OPERATOR_PI_YAW,

	// Emitter IDs

	OPERATOR_ID_COUNT,
};

// Class factory for particle operators
class IParticleOperatorDefinition
{
public:
	virtual const char *GetName() const = 0;
	virtual CParticleOperatorInstance *CreateInstance( const DmObjectId_t &id ) const = 0;
	//	virtual void DestroyInstance( CParticleOperatorInstance *pInstance ) const = 0;
	virtual const DmxElementUnpackStructure_t *GetUnpackStructure() const = 0;
	virtual ParticleOperatorId_t GetId() const = 0;
	virtual bool IsObsolete() const = 0;
	virtual size_t GetClassSize() const = 0;

#if MEASURE_PARTICLE_PERF
	// performance monitoring
	float m_flMaxExecutionTime;
	float m_flTotalExecutionTime;
	float m_flUncomittedTime;

	FORCEINLINE void RecordExecutionTime( float flETime )
	{
		m_flUncomittedTime += flETime;
		m_flMaxExecutionTime = MAX( m_flMaxExecutionTime, flETime );
	}

	FORCEINLINE float TotalRecordedExecutionTime() const { return m_flTotalExecutionTime; }

	FORCEINLINE float MaximumRecordedExecutionTime() const { return m_flMaxExecutionTime; }
#endif
};

// Particle operators
class CParticleOperatorInstance
{
public:
	// unpack structure will be applied by creator. add extra initialization needed here
	virtual void unused_InitParams() = 0;

	virtual void unused_GetRequiredContextBytes() = 0;

	virtual void unused_InitializeContextData() = 0;

	virtual void unused_GetWrittenAttributes() = 0;
	virtual void unused_GetReadAttributes() = 0;
	virtual void unused_GetReadControlPointMask() = 0;

	//  Used when an operator needs to read the attributes of a particle at spawn time
	virtual void unused_GetReadInitialAttributes() = 0;

	// a particle simulator does this
	virtual void unused_Operate() = 0;

	// a renderer overrides this
	virtual void Render( IMatRenderContext *pRenderContext, CParticleCollection *pParticles, void *pContext ) const {}

	virtual void unused_IsBatchable() = 0;

	virtual void unused_RenderUnsorted() = 0;

	// Returns the number of verts + indices to render
	virtual void unused_GetParticlesToRender() = 0;

	// emitters over-ride this. Return a mask of what fields you initted
	virtual uint32 Emit( CParticleCollection *pParticles, float flOpCurStrength, void *pContext ) const { return 0; }

	// emitters over-ride this.
	virtual void StopEmission( CParticleCollection *pParticles, void *pContext, bool bInfiniteOnly = false ) const {}

	virtual void unused_StartEmission() = 0;
	virtual void unused_Restart() = 0;

	// initters over-ride this
	virtual void unused_InitParticleSystem() = 0;

	// a force generator does this. It accumulates in the force array
	virtual void unused_AddForces() = 0;

	// this is called for each constarint every frame. It can set up data like nearby world traces,
	// etc
	virtual void unused_SetupConstraintPerFrameData() = 0;

	// a constraint overrides this. It shold return a true if it did anything
	virtual void unused_EnforceConstraint() = 0;

	// should the constraint be run only once after all other constraints?
	virtual void unused_IsFinalConstraint() = 0;

	// determines if a mask needs to be initialized multiple times.
	virtual void unused_InitMultipleOverride() = 0;

	// Indicates if this initializer is scrub-safe (initializers don't use random numbers, for example)
	virtual void unused_IsScrubSafe() = 0;

	// particle-initters over-ride this
	virtual void InitNewParticlesScalar( CParticleCollection *pParticles, int nFirstParticle, int n_particles,
										 int attribute_write_mask, void *pContext ) const
	{
	}

	// init new particles in blocks of 4. initters that have sse smarts should over ride this. the scalar particle initter will still be cllaed for head/tail.
	virtual void unused_InitNewParticlesBlock() = 0;

	// this function is queried to determine if a particle system is over and doen with. A particle
	// system is done with when it has noparticles and no operators intend to create any more
	virtual void unused_MayCreateMoreParticles() = 0;

	virtual void unused_ShouldRunBeforeEmitters() = 0;

	// Does this operator require that particles remain in the order they were emitted?
	virtual void unused_RequiresOrderInvariance() = 0;

	// Called when the SFM wants to skip forward in time
	virtual void unused_SkipToTime() = 0;

	// Returns a unique ID for this definition
	const DmObjectId_t &GetId() { return m_Id; }

	// Used for editing + debugging to visualize the operator in 3D
	virtual void Render( CParticleCollection *pParticles ) const {}

	// Used as a debugging mechanism to prevent bogus calls to RandomInt or RandomFloat inside operators
	// Use CParticleCollection::RandomInt/RandomFloat instead
	int RandomInt( int nMin, int nMax )
	{
		// NOTE: Use CParticleCollection::RandomInt!
		Assert( 0 );
		return 0;
	}

	float RandomFloat( float flMinVal = 0.0f, float flMaxVal = 1.0f )
	{
		// NOTE: Use CParticleCollection::RandomFloat!
		Assert( 0 );
		return 0.0f;
	}

	float m_flOpStartFadeInTime;
	float m_flOpEndFadeInTime;
	float m_flOpStartFadeOutTime;
	float m_flOpEndFadeOutTime;
	float m_flOpFadeOscillatePeriod;

	virtual ~CParticleOperatorInstance()
	{
		// so that sheet references, etc can be cleaned up
	}

protected:
private:
	friend class CParticleCollection;

	const IParticleOperatorDefinition *m_pDef;

	void SetDefinition( const IParticleOperatorDefinition *pDef, const DmObjectId_t &id )
	{
		m_pDef = pDef;
		CopyUniqueId( id, &m_Id );
	}

	DmObjectId_t m_Id;

	template <typename T>
	friend class CParticleOperatorDefinition;
};

// Helper macro for creating particle operator factories
template <class T>
class CParticleOperatorDefinition : public IParticleOperatorDefinition
{
public:
	CParticleOperatorDefinition( const char *pFactoryName, ParticleOperatorId_t id, bool bIsObsolete )
		: m_pFactoryName( pFactoryName )
		, m_Id( id )
	{
#if MEASURE_PARTICLE_PERF
		m_flTotalExecutionTime = 0.0f;
		m_flMaxExecutionTime = 0.0f;
		m_flUncomittedTime = 0.0f;
#endif
		m_bIsObsolete = bIsObsolete;
	}

	virtual const char *GetName() const { return m_pFactoryName; }

	virtual ParticleOperatorId_t GetId() const { return m_Id; }

	virtual CParticleOperatorInstance *CreateInstance( const DmObjectId_t &id ) const
	{
		CParticleOperatorInstance *pOp = new T;
		pOp->SetDefinition( this, id );
		return pOp;
	}

	virtual const DmxElementUnpackStructure_t *GetUnpackStructure() const { return m_pUnpackParams; }

	// Editor won't display obsolete operators
	virtual bool IsObsolete() const { return m_bIsObsolete; }

	virtual size_t GetClassSize() const { return sizeof( T ); }

private:
	const char *m_pFactoryName;
	ParticleOperatorId_t m_Id;
	bool m_bIsObsolete;
	static DmxElementUnpackStructure_t *m_pUnpackParams;
};

//	DMXELEMENT_UNPACK_FIELD( "Visibility Use Bounding Box for Proxy", "0", bool, VisibilityInputs.m_bUseBBox )
//	DMXELEMENT_UNPACK_FIELD( "Visibility Bounding Box Scale", "1.0", float, VisibilityInputs.m_flBBoxScale )

// need to think about particle constraints in terms of segregating affected particles so as to
// run multi-pass constraints on only a subset

// flags for particle systems
enum
{
	PCFLAGS_FIRST_FRAME = 0x1,
	PCFLAGS_PREV_CONTROL_POINTS_INITIALIZED = 0x2,
};

// sorting functionality for rendering. Call GetRenderList( bool bSorted ) to get the list of
// particles to render (sorted or not, including children).
// **do not casually change this structure**. The sorting code treats it interchangably as an SOA
// and accesses it using sse. Any changes to this struct need the sort code updated.**
struct ParticleRenderData_t
{
	float m_flSortKey; // what we sort by
	int m_nIndex; // index or fudged index (for child particles)
	float m_flRadius; // effective radius, using visibility
#if VALVE_LITTLE_ENDIAN
	uint8 m_nAlpha; // effective alpha, combining alpha and alpha2 and vis. 0 - 255
	uint8 m_nAlphaPad[3]; // this will be written to
#else
	uint8 m_nAlphaPad[3]; // this will be written to
	uint8 m_nAlpha; // effective alpha, combining alpha and alpha2 and vis. 0 - 255
#endif
};

typedef struct ALIGN16 _FourInts
{
	int32 m_nValue[4];
} ALIGN16_POST FourInts;

struct CParticleControlPoint
{
	Vector m_Position = vec3_origin;
	alignas( 4 ) unsigned char unused_m_PrevPosition[12];

	// orientation
	Vector m_ForwardVector = vec3_origin;
	Vector m_UpVector = vec3_origin;
	Vector m_RightVector = vec3_origin;

	Vector m_vVelocity = vec3_origin;

	float m_flRadius = 0.;
	float m_flDensity = 1.;
	float m_flDuration = 0.;

	// reference to entity or whatever this control point comes from
	void *m_pObject = nullptr;

	// parent for hierarchies
	int m_nParent = 0;
};

#define NUM_COLLISION_CACHE_MODES 4

// CParticleCollection
class CParticleCollection
{
public:
	~CParticleCollection();

	void SetControlPoint( int nWhichPoint, const Vector &v );
	void SetControlPointObject( int nWhichPoint, void *pObject );

	void SetControlPointOrientation( int nWhichPoint, const Vector &forward, const Vector &right, const Vector &up );
	void SetControlPointParent( int nWhichPoint, int n );

	fltx4 *GetM128AttributePtrForWrite( int nAttribute, size_t *pStrideOut );

	void Simulate( float dt, bool updateBboxOnly );

	// the camera objetc may be compared for equality against control point objects
	void Render( IMatRenderContext *pRenderContext, bool bTranslucentOnly = false, void *pCameraObject = NULL );

	bool IsValid() const;
	const char *GetName() const;

	// IsFinished returns true when a system has no particles and won't be creating any more
	bool IsFinished();

	void StopEmission( bool bInfiniteOnly = false, bool bRemoveAllParticles = false, bool bWakeOnStop = false );
	void SetDormant( bool bDormant );

	const Vector &GetControlPointAtCurrentTime( int nControlPoint ) const;
	void GetControlPointOrientationAtCurrentTime( int nControlPoint, Vector *pForward, Vector *pRight,
												  Vector *pUp ) const;
	int GetControlPointParent( int nControlPoint ) const;

	int GetHighestControlPoint() const;

	// Has this particle moved recently (since the last simulation?)
	bool HasMoved() const;

	// Control point accessed:
	// NOTE: Unlike the definition's version of these methods,
	// these OR-in the masks of their children.
	bool ReadsControlPoint( int nPoint ) const;

	// Used by particle systems to generate random numbers. Do not call these methods - use sse
	// code
	int RandomInt( int nMin, int nMax );
	float RandomFloat( float flMin, float flMax );

	// NOTE: These versions will produce the *same random numbers* if you give it the same random
	// sample id. do not use these methods.
	int RandomInt( int nRandomSampleId, int nMin, int nMax );
	float RandomFloat( int nRandomSampleId, float flMin, float flMax );

	fltx4 RandomFloat( const FourInts &ParticleID, int nRandomSampleOffset );

	// Does the particle system use the power of two frame buffer texture (refraction?)
	bool UsesPowerOfTwoFrameBufferTexture( bool bThisFrame ) const;

	// Does the particle system use the full frame buffer texture (soft particles)
	bool UsesFullFrameBufferTexture( bool bThisFrame ) const;

	// Is the particle system translucent?
	bool IsTranslucent() const;

	// Is the particle system two-pass?
	bool IsTwoPass() const;

	CParticleCollection *GetNextCollectionUsingSameDef() { return m_pNextDef; }

	CUtlReference<CSheet> m_Sheet;

protected:
	CParticleCollection();

	// Used by client code
	bool Init( const char *pParticleSystemName );
	bool Init( CParticleSystemDefinition *pDef );

	// Bloat the bounding box by bounds around the control point
	void BloatBoundsUsingControlPoint();

private:
	void Init( CParticleSystemDefinition *pDef, float flDelay, int nRandomSeed );

public:
	alignas( 16 ) unsigned char unused_m_fl4CurTime[16];

	int m_nPaddedActiveParticles; // # of groups of 4 particles
	alignas( 4 ) unsigned char unused_m_flCurTime[4];

	int m_nActiveParticles; // # of active particles
	alignas( 4 ) unsigned char unused_m_flDt[8];
	float m_flNextSleepTime; // time to go to sleep if not drawn

	CUtlReference<CParticleSystemDefinition> m_pDef;
	int m_nAllocatedParticles;
	int m_nMaxAllowedParticles;
	alignas( 8 ) unsigned char unused_m_bDormant[16];

	// control point data.  Don't set these directly, or they won't propagate down to children
	// particle control points can act as emitter centers, repulsions points, etc.  what they are
	// used for depends on what operators and parameters your system has.
	CParticleControlPoint m_ControlPoints[MAX_PARTICLE_CONTROL_POINTS];

	alignas( 8 ) unsigned char unused_m_ControlPointHitBoxes[2056];
	CParticleCollection *m_pNext; // for linking children together
	alignas( 8 ) unsigned char unused_m_pPrev[48];

	CUtlIntrusiveDList<CParticleCollection> m_Children; // list for all child particle systems

	void *operator new( size_t nSize );
	void operator delete( void *pData );

protected:
	// current bounds for the particle system
	alignas( 16 ) unsigned char unused_m_bBoundsValid[4];
	Vector m_MinBounds;
	Vector m_MaxBounds;
	int m_nHighestCP; //Highest CP set externally.  Needs to assert if a system calls to an unassigned CP.
	int m_nTargetCP; // Active CP set externally.

private:
	alignas( 8 ) unsigned char unused_m_pParticleMemory[24];

	int m_nPerParticleInitializedAttributeMask;
	int m_nPerParticleUpdatedAttributeMask;
	int m_nPerParticleReadInitialAttributeMask; // What fields do operators want to see initial attribute values for?
	float *m_pParticleAttributes[MAX_PARTICLE_ATTRIBUTES];
	float *m_pParticleInitialAttributes[MAX_PARTICLE_ATTRIBUTES];
	size_t m_nParticleFloatStrides[MAX_PARTICLE_ATTRIBUTES];
	size_t m_nParticleInitialFloatStrides[MAX_PARTICLE_ATTRIBUTES];

	float *m_pConstantAttributes;

	uint64 m_nControlPointReadMask; // Mask indicating which control points have been accessed
	int m_nParticleFlags; // PCFLAGS_xxx
	bool m_bIsScrubbable : 1;
	bool m_bIsRunningInitializers : 1;
	bool m_bIsRunningOperators : 1;
	bool m_bIsTranslucent : 1;
	bool m_bIsTwoPass : 1;
	bool m_bAnyUsesPowerOfTwoFrameBufferTexture : 1; // whether or not we or any children use this
	bool m_bAnyUsesFullFrameBufferTexture : 1;
	bool m_bIsBatchable : 1;

	bool m_bUsesPowerOfTwoFrameBufferTexture; // whether or not we use this, _not_ our children
	bool m_bUsesFullFrameBufferTexture;

	// How many frames have we drawn?
	int m_nDrawnFrames;
	int m_nSimulatedFrames;

	Vector m_Center; // average of particle centers

	// Used to assign unique ids to each particle
	int m_nUniqueParticleId;

	// Used to generate random numbers
	int m_nRandomQueryCount;
	int m_nRandomSeed;
	int m_nOperatorRandomSampleOffset;

	alignas( 4 ) unsigned char unused_m_flMinDistSqr[24];
	float m_flLastMinDistSqr;
	float m_flLastMaxDistSqr;

	// Particle collection kill list. set up by particle system mgr
	int m_nNumParticlesToKill;
	int *m_pParticleKillList;

	// Used to build a list of all particle collections that have the same particle def
	CParticleCollection *m_pNextDef;
	CParticleCollection *m_pPrevDef;

	// For debugging
	CParticleOperatorInstance *m_pRenderOp;
	friend class CParticleSystemMgr;
	friend class CParticleOperatorInstance;
};

class CM128AttributeWriteIterator : public CStridedPtr<fltx4>
{
public:
	FORCEINLINE CM128AttributeWriteIterator() {}

	FORCEINLINE void Init( int nAttribute, CParticleCollection *pParticles )
	{
		m_pData = pParticles->GetM128AttributePtrForWrite( nAttribute, &m_nStride );
	}

	FORCEINLINE CM128AttributeWriteIterator( int nAttribute, CParticleCollection *pParticles )
	{
		Init( nAttribute, pParticles );
	}
};

// Inline methods of CParticleCollection

inline bool CParticleCollection::ReadsControlPoint( int nPoint ) const
{
	return ( m_nControlPointReadMask & ( 1ULL << nPoint ) ) != 0;
}

inline void CParticleCollection::SetControlPoint( int nWhichPoint, const Vector &v )
{
	Assert( ( nWhichPoint >= 0 ) && ( nWhichPoint < MAX_PARTICLE_CONTROL_POINTS ) );
	m_nHighestCP = MAX( m_nHighestCP, nWhichPoint );
	m_ControlPoints[nWhichPoint].m_Position = v;
	for ( CParticleCollection *i = m_Children.m_pHead; i; i = i->m_pNext )
	{
		i->SetControlPoint( nWhichPoint, v );
	}
}

inline void CParticleCollection::SetControlPointObject( int nWhichPoint, void *pObject )
{
	Assert( ( nWhichPoint >= 0 ) && ( nWhichPoint < MAX_PARTICLE_CONTROL_POINTS ) );
	m_ControlPoints[nWhichPoint].m_pObject = pObject;
	for ( CParticleCollection *i = m_Children.m_pHead; i; i = i->m_pNext )
	{
		i->SetControlPointObject( nWhichPoint, pObject );
	}
}

inline void CParticleCollection::SetControlPointOrientation( int nWhichPoint, const Vector &forward,
															 const Vector &right, const Vector &up )
{
	Assert( ( nWhichPoint >= 0 ) && ( nWhichPoint < MAX_PARTICLE_CONTROL_POINTS ) );

	// check perpendicular
	if ( fabs( DotProduct( forward, up ) ) <= 0.1f && fabs( DotProduct( forward, right ) ) <= 0.1f &&
		 fabs( DotProduct( right, up ) ) <= 0.1f )
	{
		m_ControlPoints[nWhichPoint].m_ForwardVector = forward;
		m_ControlPoints[nWhichPoint].m_UpVector = up;
		m_ControlPoints[nWhichPoint].m_RightVector = right;

		// make sure all children are finished
		for ( CParticleCollection *i = m_Children.m_pHead; i; i = i->m_pNext )
		{
			i->SetControlPointOrientation( nWhichPoint, forward, right, up );
		}
	}
	else
	{
		Warning( "Attempt to set particle collection %s to invalid orientation matrix\n", GetName() );
	}
}

inline void CParticleCollection::SetControlPointParent( int nWhichPoint, int n )
{
	Assert( ( nWhichPoint >= 0 ) && ( nWhichPoint < MAX_PARTICLE_CONTROL_POINTS ) );
	m_ControlPoints[nWhichPoint].m_nParent = n;
	for ( CParticleCollection *i = m_Children.m_pHead; i; i = i->m_pNext )
	{
		i->SetControlPointParent( nWhichPoint, n );
	}
}

// Used by particle systems to generate random numbers
inline int CParticleCollection::RandomInt( int nRandomSampleId, int nMin, int nMax )
{
	// do not call
	float flRand = s_pRandomFloats[( m_nRandomSeed + nRandomSampleId ) & RANDOM_FLOAT_MASK];
	flRand *= ( nMax + 1 - nMin );
	int nRand = (int)flRand + nMin;
	return nRand;
}

inline float CParticleCollection::RandomFloat( int nRandomSampleId, float flMin, float flMax )
{
	// do not call
	float flRand = s_pRandomFloats[( m_nRandomSeed + nRandomSampleId ) & RANDOM_FLOAT_MASK];
	flRand *= ( flMax - flMin );
	flRand += flMin;
	return flRand;
}

inline fltx4 CParticleCollection::RandomFloat( const FourInts &ParticleID, int nRandomSampleOffset )
{
	fltx4 Retval;
	int nOfs = m_nRandomSeed + nRandomSampleOffset;
	SubFloat( Retval, 0 ) = s_pRandomFloats[( nOfs + ParticleID.m_nValue[0] ) & RANDOM_FLOAT_MASK];
	SubFloat( Retval, 1 ) = s_pRandomFloats[( nOfs + ParticleID.m_nValue[1] ) & RANDOM_FLOAT_MASK];
	SubFloat( Retval, 2 ) = s_pRandomFloats[( nOfs + ParticleID.m_nValue[2] ) & RANDOM_FLOAT_MASK];
	SubFloat( Retval, 3 ) = s_pRandomFloats[( nOfs + ParticleID.m_nValue[3] ) & RANDOM_FLOAT_MASK];
	return Retval;
}

// Used by particle systems to generate random numbers
inline int CParticleCollection::RandomInt( int nMin, int nMax )
{
	// do not call
	return RandomInt( m_nRandomQueryCount++, nMin, nMax );
}

inline float CParticleCollection::RandomFloat( float flMin, float flMax )
{
	// do not call
	return RandomFloat( m_nRandomQueryCount++, flMin, flMax );
}

inline fltx4 *CParticleCollection::GetM128AttributePtrForWrite( int nAttribute, size_t *pStrideOut )
{
	// NOTE: If you hit this assertion, it means your particle operator isn't returning
	// the appropriate fields in the RequiredAttributesMask call
	if ( !HushAsserts() )
	{
		Assert( !m_bIsRunningInitializers || ( m_nPerParticleInitializedAttributeMask & ( 1 << nAttribute ) ) );
		Assert( !m_bIsRunningOperators || ( m_nPerParticleUpdatedAttributeMask & ( 1 << nAttribute ) ) );
		Assert( m_nParticleFloatStrides[nAttribute] != 0 );
	}

	*( pStrideOut ) = m_nParticleFloatStrides[nAttribute] / 4;
	return reinterpret_cast<fltx4 *>( m_pParticleAttributes[nAttribute] );
}

// Data associated with children particle systems
struct ParticleChildrenInfo_t
{
	DmObjectId_t m_Id;
	CUtlString m_Name;
	bool m_bUseNameBasedLookup;
	float m_flDelay; // How much to delay this system after the parent starts
};

// A template describing how a particle system will function
class CParticleSystemDefinition
{
	DECLARE_DMXELEMENT_UNPACK();
	DECLARE_REFERENCED_CLASS( CParticleSystemDefinition );

public:
	CParticleSystemDefinition();
	~CParticleSystemDefinition();

	// Serialization, unserialization
	void Read( CDmxElement *pElement );
	CDmxElement *Write();

	IMaterial *GetMaterial() const;
	const char *GetName() const;
	const DmObjectId_t &GetId() const;

	// Should we always precache this?
	bool ShouldAlwaysPrecache() const;

	// Is the particle system rendered on the viewmodel?
	bool IsViewModelEffect() const;

	// Used to iterate over all particle collections using the same def
	CParticleCollection *FirstCollection();

	// What's the effective cull size + fill cost?
	// Used for early retirement
	float GetCullRadius() const;
	float GetCullFillCost() const;
	int GetCullControlPoint() const;
	const char *GetCullReplacementDefinition() const;

	bool ReadsControlPoint( int nPoint ) const;

private:
	void Precache();

	void UnlinkAllCollections();

private:
	alignas( 8 ) unsigned char unused_m_nInitialParticles[4];
	int m_nPerParticleUpdatedAttributeMask;
	int m_nPerParticleInitializedAttributeMask;
	int m_nInitialAttributeReadMask;
	int m_nAttributeReadMask;
	uint64 m_nControlPointReadMask;
	alignas( 8 ) unsigned char unused_m_BoundingBoxMin[24];
	char m_pszMaterialName[MAX_PATH];
	alignas( 8 ) unsigned char unused_m_Material[8];
	CParticleCollection *m_pFirstCollection;
	char m_pszCullReplacementName[128];
	float m_flCullRadius;
	float m_flCullFillCost;
	int m_nCullControlPoint;
	int m_nRetireCheckFrame;

	// Default attribute values
	alignas( 8 ) unsigned char unused_m_ConstantColor[24];
	int m_nGroupID;
	alignas( 4 ) unsigned char unused_m_flMaximumTimeStep[16];

	// Is the particle system rendered on the viewmodel?
	bool m_bViewModelEffect;

	alignas( 8 ) unsigned char unused_m_nContextDataSize[8];
	DmObjectId_t m_Id;

public:
	float m_flMaxDrawDistance; // distance at which to not draw.
	float m_flNoDrawTimeToGoToSleep; // after not beeing seen for this long, the system will sleep

	alignas( 8 ) unsigned char unused_m_nMaxParticles[4];
	int m_nSkipRenderControlPoint; // if the camera is attached to the
	// object associated with this control
	// point, don't render the system

	alignas( 8 ) unsigned char unused_m_Name[8];

	CUtlVector<CParticleOperatorInstance *> m_Operators;
	CUtlVector<CParticleOperatorInstance *> m_Renderers;
	CUtlVector<CParticleOperatorInstance *> m_Initializers;
	CUtlVector<CParticleOperatorInstance *> m_Emitters;
	CUtlVector<CParticleOperatorInstance *> m_ForceGenerators;
	CUtlVector<CParticleOperatorInstance *> m_Constraints;
	alignas( 8 ) unsigned char unused_m_Children[224];

	// profiling information
	float m_flTotalSimTime;
	alignas( 4 ) unsigned char unused_m_flUncomittedTotalSimTime[4];
	float m_flMaxMeasuredSimTime;
	int m_nMaximumActiveParticles;
	bool m_bShouldSort;
	bool m_bShouldBatch;
	bool m_bIsPrecached : 1;
	bool m_bAlwaysPrecache : 1;

	friend class CParticleCollection;
	friend class CParticleSystemMgr;
};

// Inline methods
inline CParticleSystemDefinition::CParticleSystemDefinition()
{
	m_nControlPointReadMask = 0;
	m_nInitialAttributeReadMask = 0;
	m_nPerParticleInitializedAttributeMask = 0;
	m_nPerParticleUpdatedAttributeMask = 0;
	m_nAttributeReadMask = 0;
	m_flTotalSimTime = 0.0;
	m_flMaxMeasuredSimTime = 0.0;
	m_nMaximumActiveParticles = 0;
	m_bIsPrecached = false;
	m_bAlwaysPrecache = false;
	m_bShouldBatch = false;
	m_bShouldSort = true;
	m_pFirstCollection = NULL;
	m_flCullRadius = 0.0f;
	m_flCullFillCost = 1.0f;
	m_nRetireCheckFrame = 0;
}

inline CParticleSystemDefinition::~CParticleSystemDefinition()
{
	UnlinkAllCollections();
	m_Operators.PurgeAndDeleteElements();
	m_Renderers.PurgeAndDeleteElements();
	m_Initializers.PurgeAndDeleteElements();
	m_Emitters.PurgeAndDeleteElements();
	m_ForceGenerators.PurgeAndDeleteElements();
	m_Constraints.PurgeAndDeleteElements();
}

// Used to iterate over all particle collections using the same def
inline CParticleCollection *CParticleSystemDefinition::FirstCollection()
{
	return m_pFirstCollection;
}

inline float CParticleSystemDefinition::GetCullRadius() const
{
	return m_flCullRadius;
}

inline float CParticleSystemDefinition::GetCullFillCost() const
{
	return m_flCullFillCost;
}

inline const char *CParticleSystemDefinition::GetCullReplacementDefinition() const
{
	return m_pszCullReplacementName;
}

inline int CParticleSystemDefinition::GetCullControlPoint() const
{
	return m_nCullControlPoint;
}

inline bool CParticleSystemDefinition::ReadsControlPoint( int nPoint ) const
{
	return ( m_nControlPointReadMask & ( 1ULL << nPoint ) ) != 0;
}

inline bool CParticleSystemDefinition::IsViewModelEffect() const
{
	return m_bViewModelEffect;
}

inline const DmObjectId_t &CParticleSystemDefinition::GetId() const
{
	return m_Id;
}

FORCEINLINE const Vector &CParticleCollection::GetControlPointAtCurrentTime( int nControlPoint ) const
{
	Assert( nControlPoint <= GetHighestControlPoint() );
	Assert( m_pDef->ReadsControlPoint( nControlPoint ) );
	return m_ControlPoints[nControlPoint].m_Position;
}

FORCEINLINE void CParticleCollection::GetControlPointOrientationAtCurrentTime( int nControlPoint, Vector *pForward,
																			   Vector *pRight, Vector *pUp ) const
{
	Assert( nControlPoint <= GetHighestControlPoint() );
	Assert( m_pDef->ReadsControlPoint( nControlPoint ) );

	// FIXME: Use quaternion lerp to get control point transform at time
	*pForward = m_ControlPoints[nControlPoint].m_ForwardVector;
	*pRight = m_ControlPoints[nControlPoint].m_RightVector;
	*pUp = m_ControlPoints[nControlPoint].m_UpVector;
}

FORCEINLINE int CParticleCollection::GetControlPointParent( int nControlPoint ) const
{
	Assert( nControlPoint <= GetHighestControlPoint() );
	Assert( m_pDef->ReadsControlPoint( nControlPoint ) );
	return m_ControlPoints[nControlPoint].m_nParent;
}

FORCEINLINE bool CParticleCollection::IsValid() const
{
	return ( m_pDef != NULL && m_pDef->GetMaterial() );
}

// Floating point validation

ALIGN16_POST;
