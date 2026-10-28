//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Public interfaces to vphysics DLL

#pragma once

#include "tier1/interface.h"
#include "appframework/IAppSystem.h"
#include "mathlib/vector.h"
#include "mathlib/vector4d.h"
#include "vcollide.h"

// UNITS:
// NOTE:  Coordinates are in HL units.  1 unit == 1 inch.  X is east (forward), Y is north (left), Z is up (up)
// QAngle are pitch (around y), Yaw (around Z), Roll (around X)
// AngularImpulse are exponetial maps (an axis in HL units scaled by a "twist" angle in degrees)
//		They can be transformed like normals/covectors and added linearly
// mass is kg, volume is in^3, acceleration is in/s^2, velocity is in/s

// density is kg/m^3 (water ~= 998 at room temperature)
// preferably, these would be in kg/in^3, but the range of those numbers makes them not very human readable
// having water be about 1000 is really convenient for data entry.
// Since volume is in in^3 and density is in kg/m^3: 
//	density = (mass / volume) * CUBIC_METERS_PER_CUBIC_INCH
// Force is applied using impulses (kg*in/s)
// Torque is applied using impulses (kg*degrees/s)

#define METERS_PER_INCH					(0.0254f)
#define CUBIC_METERS_PER_CUBIC_INCH		(METERS_PER_INCH*METERS_PER_INCH*METERS_PER_INCH)
// 2.2 lbs / kg

// convert from pounds to kg

class IPhysicsObject;
class IPhysicsEnvironment;
class IPhysicsSurfaceProps;
class IPhysicsConstraint;
class IPhysicsConstraintGroup;
class IPhysicsFluidController;
class IPhysicsSpring;
class IPhysicsVehicleController;
class IConvexInfo;
class IPhysicsCollisionSet;
class IPhysicsPlayerController;
class IPhysicsFrictionSnapshot;

struct Ray_t;
struct constraint_ragdollparams_t;
struct constraint_hingeparams_t;
struct constraint_fixedparams_t;
struct constraint_ballsocketparams_t;
struct constraint_slidingparams_t;
struct constraint_pulleyparams_t;
struct constraint_lengthparams_t;
struct constraint_groupparams_t;

struct vehicleparams_t;
struct matrix3x4_t;

struct fluidparams_t;
struct springparams_t;
struct objectparams_t;
class CGameTrace;
typedef CGameTrace trace_t;
struct physics_performanceparams_t;

//enum PhysInterfaceId_t;
struct physsaveparams_t;
struct physrestoreparams_t;

enum PhysInterfaceId_t 
{
	PIID_UNKNOWN,
	PIID_IPHYSICSOBJECT,
	PIID_IPHYSICSFLUIDCONTROLLER,
	PIID_IPHYSICSSPRING,
	PIID_IPHYSICSCONSTRAINTGROUP,
	PIID_IPHYSICSCONSTRAINT,
	PIID_IPHYSICSSHADOWCONTROLLER,
	PIID_IPHYSICSPLAYERCONTROLLER,
	PIID_IPHYSICSMOTIONCONTROLLER,
	PIID_IPHYSICSVEHICLECONTROLLER,
	PIID_IPHYSICSGAMETRACE,

	PIID_NUM_TYPES
};

class ISave;
class IRestore;

#define VPHYSICS_INTERFACE_VERSION	"VPhysics031"

abstract_class IPhysics : public IAppSystem
{
public:
	virtual	IPhysicsEnvironment		*CreateEnvironment( void ) = 0;
	virtual void DestroyEnvironment( IPhysicsEnvironment * ) = 0;
	virtual void unused_GetActiveEnvironmentByIndex() = 0;

	// Creates a fast hash of pairs of objects
	// Useful for maintaining a table of object relationships like pairs that do not collide.
	virtual void unused_CreateObjectPairHash() = 0;
	virtual void unused_DestroyObjectPairHash() = 0;

	// holds a cache of these by id.  So you can get by id to search for the previously created set
	// UNDONE: Sets are currently limited to 32 elements.  More elements will return NULL in create.
	// NOTE: id is not allowed to be zero.
	virtual IPhysicsCollisionSet		*FindOrCreateCollisionSet( unsigned int id, int maxElementCount ) = 0;
	virtual IPhysicsCollisionSet		*FindCollisionSet( unsigned int id ) = 0;
	virtual void						DestroyAllCollisionSets() = 0;
};

class IVPhysicsKeyParser;

// UNDONE: Find a better place for this?  Should be in collisionutils, but it's needs VPHYSICS' solver.
struct truncatedcone_t
{
	Vector	origin;
	Vector	normal;
	float	h;			// height of the cone (hl units)
	float	theta;		// cone angle (degrees)
};

#define VPHYSICS_COLLISION_INTERFACE_VERSION	"VPhysicsCollision007"

abstract_class IPhysicsCollision
{
public:
	virtual ~IPhysicsCollision( void ) {}

	// produce a convex element from verts (convex hull around verts)
	virtual void unused_ConvexFromVerts() = 0;
	// produce a convex element from planes (csg of planes)
	virtual void unused_ConvexFromPlanes() = 0;
	// calculate volume of a convex element
	virtual void unused_ConvexVolume() = 0;

	virtual void unused_ConvexSurfaceArea() = 0;
	// store game-specific data in a convex solid
	virtual void unused_SetConvexGameData() = 0;
	// If not converted, free the convex elements with this call
	virtual void unused_ConvexFree() = 0;
	virtual void unused_BBoxToConvex() = 0;
	// produce a convex element from a convex polyhedron
	virtual void unused_ConvexFromConvexPolyhedron() = 0;
	// produce a set of convex triangles from a convex polygon, normal is assumed to be on the side with forward point ordering, which should be clockwise, output will need to be able to hold exactly (iPointCount-2) convexes
	virtual void unused_ConvexesFromConvexPolygon() = 0;

	// concave objects
	// create a triangle soup
	virtual void unused_PolysoupCreate() = 0;
	// destroy the container and memory
	virtual void unused_PolysoupDestroy() = 0;
	// add a triangle to the soup
	virtual void unused_PolysoupAddTriangle() = 0;
	// convert the convex into a compiled collision model
	virtual void unused_ConvertPolysoupToCollide() = 0;
	
	// Convert an array of convex elements to a compiled collision model (this deletes the convex elements)
	virtual void unused_ConvertConvexToCollide() = 0;
	virtual void unused_ConvertConvexToCollideParams() = 0;
	// Free a collide that was created with ConvertConvexToCollide()
	virtual void unused_DestroyCollide() = 0;

	// Get the memory size in bytes of the collision model for serialization
	virtual void unused_CollideSize() = 0;
	// serialize the collide to a block of memory
	virtual void unused_CollideWrite() = 0;
	// unserialize the collide from a block of memory
	virtual void unused_UnserializeCollide() = 0;

	// compute the volume of a collide
	virtual void unused_CollideVolume() = 0;
	// compute surface area for tools
	virtual void unused_CollideSurfaceArea() = 0;

	// Get the support map for a collide in the given direction
	virtual Vector			CollideGetExtent( const CPhysCollide *pCollide, const Vector &collideOrigin, const QAngle &collideAngles, const Vector &direction ) = 0;

	// Get an AABB for an oriented collision model
	virtual void			CollideGetAABB( Vector *pMins, Vector *pMaxs, const CPhysCollide *pCollide, const Vector &collideOrigin, const QAngle &collideAngles ) = 0;

	virtual void unused_CollideGetMassCenter() = 0;
	virtual void unused_CollideSetMassCenter() = 0;
	// get the approximate cross-sectional area projected orthographically on the bbox of the collide
	// NOTE: These are fractional areas - unitless.  Basically this is the fraction of the OBB on each axis that
	// would be visible if the object were rendered orthographically.
	// NOTE: This has been precomputed when the collide was built or this function will return 1,1,1
	virtual void unused_CollideGetOrthographicAreas() = 0;
	virtual void unused_CollideSetOrthographicAreas() = 0;

	// query the vcollide index in the physics model for the instance
	virtual void unused_CollideIndex() = 0;

	// Convert a bbox to a collide
	virtual CPhysCollide	*BBoxToCollide( const Vector &mins, const Vector &maxs ) = 0;
	virtual void unused_GetConvexesUsedInCollideable() = 0;

	// Trace an AABB against a collide
	virtual void TraceBox( const Vector &start, const Vector &end, const Vector &mins, const Vector &maxs, const CPhysCollide *pCollide, const Vector &collideOrigin, const QAngle &collideAngles, trace_t *ptr ) = 0;
	virtual void TraceBox( const Ray_t &ray, const CPhysCollide *pCollide, const Vector &collideOrigin, const QAngle &collideAngles, trace_t *ptr ) = 0;
	virtual void TraceBox( const Ray_t &ray, unsigned int contentsMask, IConvexInfo *pConvexInfo, const CPhysCollide *pCollide, const Vector &collideOrigin, const QAngle &collideAngles, trace_t *ptr ) = 0;

	// Trace one collide against another
	virtual void unused_TraceCollide() = 0;

	// relatively slow test for box vs. truncated cone
	virtual void unused_IsBoxIntersectingCone() = 0;

	// loads a set of solids into a vcollide_t
	virtual void unused_VCollideLoad() = 0;
	// destroyts the set of solids created by VCollideLoad
	virtual void unused_VCollideUnload() = 0;

	// begins parsing a vcollide.  NOTE: This keeps pointers to the text
	// If you free the text and call members of IVPhysicsKeyParser, it will crash
	virtual IVPhysicsKeyParser	*VPhysicsKeyParserCreate( const char *pKeyData ) = 0;
	// Free the parser created by VPhysicsKeyParserCreate
	virtual void			VPhysicsKeyParserDestroy( IVPhysicsKeyParser *pParser ) = 0;

	// creates a list of verts from a collision mesh
	virtual void unused_CreateDebugMesh() = 0;
	// destroy the list of verts created by CreateDebugMesh
	virtual void unused_DestroyDebugMesh() = 0;

	// create a queryable version of the collision model
	virtual void unused_CreateQueryModel() = 0;
	// destroy the queryable version
	virtual void unused_DestroyQueryModel() = 0;

	virtual void unused_ThreadContextCreate() = 0;
	virtual void unused_ThreadContextDestroy() = 0;

	virtual void unused_CreateVirtualMesh() = 0;
	virtual bool			SupportsVirtualMesh() = 0;

	virtual void unused_GetBBoxCacheSize() = 0;

	// extracts a polyhedron that defines a CPhysConvex's shape
	virtual void unused_PolyhedronFromConvex() = 0;

	// dumps info about the collide to Msg()
	virtual void unused_OutputDebugInfo() = 0;
	virtual void unused_ReadStat() = 0;
};

// Purpose: Ray traces from game engine.
abstract_class IPhysicsGameTrace
{
public:
	virtual void unused_VehicleTraceRay() = 0;
	virtual void unused_VehicleTraceRayWithWater() = 0;
	virtual void unused_VehiclePointInWater() = 0;
};

// The caller should implement this to return contents masks per convex on a collide
abstract_class IConvexInfo
{
public:
	virtual unsigned int GetContents( int convexGameData ) = 0;
};

abstract_class IPhysicsCollisionData
{
public:
	virtual void GetSurfaceNormal( Vector &out ) = 0;		// normal points toward second object (object index 1)
	virtual void GetContactPoint( Vector &out ) = 0;		// contact point of collision (in world space)
	virtual void unused_GetContactSpeed() = 0;		// speed of surface 1 relative to surface 0 (in world space)
};

struct vcollisionevent_t
{
	IPhysicsObject	*pObjects[2];
	int				surfaceProps[2];
	bool			isCollision;
	bool			isShadowCollision;
	float			deltaCollisionTime;

	float			collisionSpeed;				// only valid at postCollision
	IPhysicsCollisionData *pInternalData;		// may change pre/post collision
};

abstract_class IPhysicsCollisionEvent
{
public:
	// returns the two objects that collided, time between last collision of these objects
	// and an opaque data block of collision information
	// NOTE: PreCollision/PostCollision ALWAYS come in matched pairs!!!
	virtual void PreCollision( vcollisionevent_t *pEvent ) = 0;
	virtual void PostCollision( vcollisionevent_t *pEvent ) = 0;

	// This is a scrape event.  The object has scraped across another object consuming the indicated energy
	virtual void Friction( IPhysicsObject *pObject, float energy, int surfaceProps, int surfacePropsHit, IPhysicsCollisionData *pData ) = 0;

	virtual void StartTouch( IPhysicsObject *pObject1, IPhysicsObject *pObject2, IPhysicsCollisionData *pTouchData ) = 0;
	virtual void EndTouch( IPhysicsObject *pObject1, IPhysicsObject *pObject2, IPhysicsCollisionData *pTouchData ) = 0;

	virtual void FluidStartTouch( IPhysicsObject *pObject, IPhysicsFluidController *pFluid ) = 0;
	virtual void FluidEndTouch( IPhysicsObject *pObject, IPhysicsFluidController *pFluid ) = 0;

	virtual void PostSimulationFrame() = 0;

	virtual void ObjectEnterTrigger( IPhysicsObject *pTrigger, IPhysicsObject *pObject ) {}
	virtual void ObjectLeaveTrigger( IPhysicsObject *pTrigger, IPhysicsObject *pObject ) {}
};

abstract_class IPhysicsObjectEvent
{
public:
	// these can be used to optimize out queries on sleeping objects
	// Called when an object is woken after sleeping
	virtual void ObjectWake( IPhysicsObject *pObject ) = 0;
	// called when an object goes to sleep (no longer simulating)
	virtual void ObjectSleep( IPhysicsObject *pObject ) = 0;
};

abstract_class IPhysicsConstraintEvent
{
public:
	// the constraint is now inactive, the game code is required to delete it or re-activate it.
	virtual void ConstraintBroken( IPhysicsConstraint * ) = 0;
};

// UNDONE: At some point allow this to be parameterized using hlshadowcontrol_params_t.
// All of the infrastructure is in place to do that.
abstract_class IPhysicsShadowController
{
public:
	virtual ~IPhysicsShadowController( void ) {}

	virtual void Update( const Vector &position, const QAngle &angles, float timeOffset ) = 0;
	virtual void unused_MaxSpeed() = 0;
	virtual void StepUp( float height ) = 0;
	
	// If the teleport distance is non-zero, the object will be teleported to 
	// the target location when the error exceeds this quantity.
	virtual void unused_SetTeleportDistance() = 0;
	virtual void unused_AllowsTranslation() = 0;
	virtual void unused_AllowsRotation() = 0;

	// There are two classes of shadow objects:
	// 1) Game physics controlled, shadow follows game physics (this is the default)
	// 2) Physically controlled - shadow position is a target, but the game hasn't guaranteed that the space can be occupied by this object
	virtual void unused_SetPhysicallyControlled() = 0;
	virtual bool IsPhysicallyControlled() = 0;
	virtual void GetLastImpulse( Vector *pOut ) = 0;
	virtual void unused_UseShadowMaterial() = 0;
	virtual void unused_ObjectMaterialChanged() = 0;

	//Basically get the last inputs to IPhysicsShadowController::Update(), returns last input to timeOffset in Update()
	virtual void unused_GetTargetPosition() = 0;
	
	virtual void unused_GetTeleportDistance() = 0;
	virtual void unused_GetMaxSpeed() = 0;
};

class IPhysicsMotionController;

// Callback for simulation
class IMotionEvent
{
public:
	// These constants instruct the simulator as to how to apply the values copied to linear & angular
	// GLOBAL/LOCAL refer to the coordinate system of the values, whereas acceleration/force determine whether or not
	// mass is divided out (forces must be divided by mass to compute acceleration)
	enum simresult_e { SIM_NOTHING = 0, SIM_LOCAL_ACCELERATION, SIM_LOCAL_FORCE, SIM_GLOBAL_ACCELERATION, SIM_GLOBAL_FORCE };
	virtual simresult_e	Simulate( IPhysicsMotionController *pController, IPhysicsObject *pObject, float deltaTime, Vector &linear, AngularImpulse &angular ) = 0;
};

abstract_class IPhysicsMotionController
{
public:
	virtual ~IPhysicsMotionController( void ) {}
	virtual void unused_SetEventHandler() = 0;
	virtual void AttachObject( IPhysicsObject *pObject, bool checkIfAlreadyAttached ) = 0;
	virtual void DetachObject( IPhysicsObject *pObject ) = 0;

	// returns the number of objects currently attached to the controller
	virtual void unused_CountObjects() = 0;
	// NOTE: pObjectList is an array with at least CountObjects() allocated
	virtual void unused_GetObjects() = 0;
	// detaches all attached objects
	virtual void unused_ClearObjects() = 0;
	// wakes up all attached objects
	virtual void WakeObjects( void ) = 0;

	enum priority_t
	{
		LOW_PRIORITY = 0,
		MEDIUM_PRIORITY = 1,
		HIGH_PRIORITY = 2,
	};
	virtual void unused_SetPriority() = 0;
};

// Collision filter function.  Return 0 if objects should not be tested for collisions, nonzero otherwise
// Install with IPhysicsEnvironment::SetCollisionFilter()
abstract_class IPhysicsCollisionSolver
{
public:
	virtual int ShouldCollide( IPhysicsObject *pObj0, IPhysicsObject *pObj1, void *pGameData0, void *pGameData1 ) = 0;
	virtual int ShouldSolvePenetration( IPhysicsObject *pObj0, IPhysicsObject *pObj1, void *pGameData0, void *pGameData1, float dt ) = 0;
	
	// pObject has already done the max number of collisions this tick, should we freeze it to save CPU?
	virtual bool ShouldFreezeObject( IPhysicsObject *pObject ) = 0;

	// The system has already done too many collision checks, performance will suffer.
	// How many more should it do?
	virtual int AdditionalCollisionChecksThisTick( int currentChecksDone ) = 0;

	// This list of objects is in a connected contact graph that is too large to solve quickly
	// return true to freeze the system, false to solve it
	virtual bool ShouldFreezeContacts( IPhysicsObject **pObjectList, int objectCount ) = 0;
};

abstract_class IPhysicsTraceFilter
{
public:
	virtual void unused_ShouldHitObject() = 0;
	virtual void unused_GetTraceType() = 0;
};

abstract_class IPhysicsEnvironment
{
public:
	virtual ~IPhysicsEnvironment( void ) {}

	virtual void SetDebugOverlay( CreateInterfaceFn debugOverlayFactory ) = 0;
	virtual void unused_GetDebugOverlay() = 0;

	// gravity is a 3-vector in in/s^2
	virtual void			SetGravity( const Vector &gravityVector ) = 0;
	virtual void			GetGravity( Vector *pGravityVector ) const = 0;

	// air density is in kg / m^3 (water is 1000)
	// This controls drag, air that is more dense has more drag.
	virtual void			SetAirDensity( float density ) = 0;
	virtual float			GetAirDensity( void ) const = 0;
	
	// object creation
	// create a polygonal object.  pCollisionModel was created by the physics builder DLL in a pre-process.
	virtual IPhysicsObject	*CreatePolyObject( const CPhysCollide *pCollisionModel, int materialIndex, const Vector &position, const QAngle &angles, objectparams_t *pParams ) = 0;
	// same as above, but this one cannot move or rotate (infinite mass/inertia)
	virtual IPhysicsObject	*CreatePolyObjectStatic( const CPhysCollide *pCollisionModel, int materialIndex, const Vector &position, const QAngle &angles, objectparams_t *pParams ) = 0;
	// Create a perfectly spherical object
	virtual void unused_CreateSphereObject() = 0;
	// destroy an object created with CreatePolyObject() or CreatePolyObjectStatic()
	virtual void DestroyObject( IPhysicsObject * ) = 0;

	// Create a polygonal fluid body out of the specified collision model
	// This object will affect any other objects that collide with the collision model
	virtual IPhysicsFluidController	*CreateFluidController( IPhysicsObject *pFluidObject, fluidparams_t *pParams ) = 0;
	// Destroy an object created with CreateFluidController()
	virtual void unused_DestroyFluidController() = 0;

	// Create a simulated spring that connects 2 objects
	virtual IPhysicsSpring	*CreateSpring( IPhysicsObject *pObjectStart, IPhysicsObject *pObjectEnd, springparams_t *pParams ) = 0;
	virtual void DestroySpring( IPhysicsSpring * ) = 0;

	// Create a constraint in the space of pReferenceObject which is attached by the constraint to pAttachedObject
	virtual IPhysicsConstraint	*CreateRagdollConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_ragdollparams_t &ragdoll ) = 0;
	virtual IPhysicsConstraint	*CreateHingeConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_hingeparams_t &hinge ) = 0;
	virtual IPhysicsConstraint	*CreateFixedConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_fixedparams_t &fixed ) = 0;
	virtual IPhysicsConstraint	*CreateSlidingConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_slidingparams_t &sliding ) = 0;
	virtual IPhysicsConstraint	*CreateBallsocketConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_ballsocketparams_t &ballsocket ) = 0;
	virtual IPhysicsConstraint *CreatePulleyConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_pulleyparams_t &pulley ) = 0;
	virtual IPhysicsConstraint *CreateLengthConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject, IPhysicsConstraintGroup *pGroup, const constraint_lengthparams_t &length ) = 0;

	virtual void DestroyConstraint( IPhysicsConstraint * ) = 0;

	virtual IPhysicsConstraintGroup *CreateConstraintGroup( const constraint_groupparams_t &groupParams ) = 0;
	virtual void DestroyConstraintGroup( IPhysicsConstraintGroup *pGroup ) = 0;

	virtual void unused_CreateShadowController() = 0;
	virtual void unused_DestroyShadowController() = 0;

	virtual IPhysicsPlayerController	*CreatePlayerController( IPhysicsObject *pObject ) = 0;
	virtual void						DestroyPlayerController( IPhysicsPlayerController * ) = 0;

	virtual IPhysicsMotionController	*CreateMotionController( IMotionEvent *pHandler ) = 0;
	virtual void						DestroyMotionController( IPhysicsMotionController *pController ) = 0;

	virtual void unused_CreateVehicleController() = 0;
	virtual void unused_DestroyVehicleController() = 0;

	// install a function to filter collisions/penentration
	virtual void			SetCollisionSolver( IPhysicsCollisionSolver *pSolver ) = 0;

	// run the simulator for deltaTime seconds
	virtual void			Simulate( float deltaTime ) = 0;
	// true if currently running the simulator (i.e. in a callback during physenv->Simulate())
	virtual void unused_IsInSimulation() = 0;

	// Manage the timestep (period) of the simulator.  The main functions are all integrated with
	// this period as dt.
	virtual float			GetSimulationTimestep() const = 0;
	virtual void			SetSimulationTimestep( float timestep ) = 0;

	// returns the current simulation clock's value.  This is an absolute time.
	virtual void unused_GetSimulationTime() = 0;
	virtual void			ResetSimulationClock() = 0;
	// returns the current simulation clock's value at the next frame.  This is an absolute time.
	virtual void unused_GetNextFrameTime() = 0;

	// Collision callbacks (game code collision response)
	virtual void			SetCollisionEventHandler( IPhysicsCollisionEvent *pCollisionEvents ) = 0;
	virtual void			SetObjectEventHandler( IPhysicsObjectEvent *pObjectEvents ) = 0;
	virtual	void			SetConstraintEventHandler( IPhysicsConstraintEvent *pConstraintEvents ) = 0;

	virtual void			SetQuickDelete( bool bQuick ) = 0;

	virtual int				GetActiveObjectCount() const = 0;
	virtual void			GetActiveObjects( IPhysicsObject **pOutputObjectList ) const = 0;
	virtual const IPhysicsObject **GetObjectList( int *pOutputObjectCount ) const = 0;
	virtual void unused_TransferObject() = 0;

	virtual void			CleanupDeleteList( void ) = 0;
	virtual void			EnableDeleteQueue( bool enable ) = 0;

	// Save/Restore methods
	virtual bool			Save( const physsaveparams_t &params ) = 0;
	virtual void unused_PreRestore() = 0;
	virtual bool			Restore( const physrestoreparams_t &params ) = 0;
	virtual void unused_PostRestore() = 0;

	// Debugging:
	virtual void unused_IsCollisionModelUsed() = 0;
	
	// Physics world version of the enginetrace API:
	virtual void TraceRay( const Ray_t &ray, unsigned int fMask, IPhysicsTraceFilter *pTraceFilter, trace_t *pTrace ) = 0;
	virtual void unused_SweepCollideable() = 0;

	// performance tuning
	virtual void unused_GetPerformanceSettings() = 0;
	virtual void SetPerformanceSettings( const physics_performanceparams_t *pSettings ) = 0;

	// perf/cost statistics
	virtual void unused_ReadStats() = 0;
	virtual void unused_ClearStats() = 0;

	virtual void unused_GetObjectSerializeSize() = 0;
	virtual void unused_SerializeObjectToBuffer() = 0;
	virtual void unused_UnserializeObjectFromBuffer() = 0;

	virtual void EnableConstraintNotify( bool bEnable ) = 0;
	virtual void DebugCheckContacts(void) = 0;
};

abstract_class IPhysicsObject
{
public:
	virtual ~IPhysicsObject( void ) {}

	// returns true if this object is static/unmoveable
	// NOTE: returns false for objects that are not created static, but set EnableMotion(false);
	// Call IsMoveable() to find if the object is static OR has motion disabled
	virtual bool			IsStatic() const = 0;
	virtual bool			IsAsleep() const = 0;
	virtual bool			IsTrigger() const = 0;
	virtual void unused_IsFluid() = 0;		// fluids are special triggers with fluid controllers attached, they return true to IsTrigger() as well!
	virtual void unused_IsHinged() = 0;
	virtual bool			IsCollisionEnabled() const = 0;
	virtual void unused_IsGravityEnabled() = 0;
	virtual void unused_IsDragEnabled() = 0;
	virtual void unused_IsMotionEnabled() = 0;
	virtual bool			IsMoveable() const = 0;	 // legacy: IsMotionEnabled() && !IsStatic()
	virtual void unused_IsAttachedToConstraint() = 0;

	// Enable / disable collisions for this object
	virtual void			EnableCollisions( bool enable ) = 0;
	// Enable / disable gravity for this object
	virtual void			EnableGravity( bool enable ) = 0;
	// Enable / disable air friction / drag for this object
	virtual void unused_EnableDrag() = 0;
	// Enable / disable motion (pin / unpin the object)
	virtual void			EnableMotion( bool enable ) = 0;

	// Game can store data in each object (link back to game object)
	virtual void			SetGameData( void *pGameData ) = 0;
	virtual void			*GetGameData( void ) const = 0;
	// This flags word can be defined by the game as well
	virtual void			SetGameFlags( unsigned short userFlags ) = 0;
	virtual unsigned short	GetGameFlags( void ) const = 0;
	virtual void			SetGameIndex( unsigned short gameIndex ) = 0;
	virtual void unused_GetGameIndex() = 0;

	// setup various callbacks for this object
	virtual void			SetCallbackFlags( unsigned short callbackflags ) = 0;
	// get the current callback state for this object
	virtual unsigned short	GetCallbackFlags( void ) const = 0;

	// "wakes up" an object
	// NOTE: ALL OBJECTS ARE "Asleep" WHEN CREATED
	virtual void			Wake( void ) = 0;
	virtual void			Sleep( void ) = 0;
	// call this when the collision filter conditions change due to this 
	// object's state (e.g. changing solid type or collision group)
	virtual void			RecheckCollisionFilter() = 0;
	// NOTE: Contact points aren't updated when collision rules change, call this to force an update
	// UNDONE: Force this in RecheckCollisionFilter() ?
	virtual void			RecheckContactPoints() = 0;

	// mass accessors
	virtual void			SetMass( float mass ) = 0;
	virtual float			GetMass( void ) const = 0;
	// get 1/mass (it's cached)
	virtual void unused_GetInvMass() = 0;
	virtual Vector			GetInertia( void ) const = 0;
	virtual void unused_GetInvInertia() = 0;
	virtual void unused_SetInertia() = 0;

	virtual void unused_SetDamping() = 0;
	virtual void unused_GetDamping() = 0;

	// coefficients are optional, pass either
	virtual void unused_SetDragCoefficient() = 0;
	virtual void unused_SetBuoyancyRatio() = 0;			// Override bouyancy

	// material index
	virtual int				GetMaterialIndex() const = 0;
	virtual void unused_SetMaterialIndex() = 0;

	// contents bits
	virtual unsigned int	GetContents() const = 0;
	virtual void			SetContents( unsigned int contents ) = 0;

	// Get the radius if this is a sphere object (zero if this is a polygonal mesh)
	virtual float			GetSphereRadius() const = 0;
	virtual void unused_GetEnergy() = 0;
	virtual void unused_GetMassCenterLocalSpace() = 0;

	// NOTE: This will teleport the object
	virtual void			SetPosition( const Vector &worldPosition, const QAngle &angles, bool isTeleport ) = 0;
	virtual void			SetPositionMatrix( const matrix3x4_t&matrix, bool isTeleport ) = 0;

	virtual void			GetPosition( Vector *worldPosition, QAngle *angles ) const = 0;
	virtual void			GetPositionMatrix( matrix3x4_t *positionMatrix ) const = 0;
	// force the velocity to a new value
	// NOTE: velocity is in worldspace, angularVelocity is relative to the object's 
	// local axes (just like pev->velocity, pev->avelocity)
	virtual void			SetVelocity( const Vector *velocity, const AngularImpulse *angularVelocity ) = 0;

	// like the above, but force the change into the simulator immediately
	virtual void unused_SetVelocityInstantaneous() = 0;

	// NOTE: velocity is in worldspace, angularVelocity is relative to the object's 
	// local axes (just like pev->velocity, pev->avelocity)
	virtual void			GetVelocity( Vector *velocity, AngularImpulse *angularVelocity ) const = 0;

	// NOTE: These are velocities, not forces.  i.e. They will have the same effect regardless of
	// the object's mass or inertia
	virtual void			AddVelocity( const Vector *velocity, const AngularImpulse *angularVelocity ) = 0;
	// gets a velocity in the object's local frame of reference at a specific point
	virtual void			GetVelocityAtPoint( const Vector &worldPosition, Vector *pVelocity ) const = 0;
	// gets the velocity actually moved by the object in the last simulation update
	virtual void unused_GetImplicitVelocity() = 0;
	// NOTE:	These are here for convenience, but you can do them yourself by using the matrix
	//			returned from GetPositionMatrix()
	// convenient coordinate system transformations (params - dest, src)
	virtual void			LocalToWorld( Vector *worldPosition, const Vector &localPosition ) const = 0;
	virtual void			WorldToLocal( Vector *localPosition, const Vector &worldPosition ) const = 0;

	// transforms a vector (no translation) from object-local to world space
	virtual void			LocalToWorldVector( Vector *worldVector, const Vector &localVector ) const = 0;
	// transforms a vector (no translation) from world to object-local space
	virtual void			WorldToLocalVector( Vector *localVector, const Vector &worldVector ) const = 0;
	
	// push on an object
	// force vector is direction & magnitude of impulse kg in / s
	virtual void			ApplyForceCenter( const Vector &forceVector ) = 0;
	virtual void			ApplyForceOffset( const Vector &forceVector, const Vector &worldPosition ) = 0;
	// apply torque impulse.  This will change the angular velocity on the object.
	// HL Axes, kg degrees / s
	virtual void			ApplyTorqueCenter( const AngularImpulse &torque ) = 0;

	// Calculates the force/torque on the center of mass for an offset force impulse (pass output to ApplyForceCenter / ApplyTorqueCenter)
	virtual void unused_CalculateForceOffset() = 0;
	// Calculates the linear/angular velocities on the center of mass for an offset force impulse (pass output to AddVelocity)
	virtual void			CalculateVelocityOffset( const Vector &forceVector, const Vector &worldPosition, Vector *centerVelocity, AngularImpulse *centerAngularVelocity ) const = 0;
	// calculate drag scale
	virtual float			CalculateLinearDrag( const Vector &unitDirection ) const = 0;
	virtual float			CalculateAngularDrag( const Vector &objectSpaceRotationAxis ) const = 0;

	// returns true if the object is in contact with another object
	// if true, puts a point on the contact surface in contactPoint, and
	// a pointer to the object in contactObject
	// NOTE: You can pass NULL for either to avoid computations
	// BUGBUG: Use CreateFrictionSnapshot instead of this - this is a simple hack
	virtual bool			GetContactPoint( Vector *contactPoint, IPhysicsObject **contactObject ) const = 0;

	// refactor this a bit - move some of this to IPhysicsShadowController
	virtual void unused_SetShadow() = 0;
	virtual void unused_UpdateShadow() = 0;
	
	// returns number of ticks since last Update() call
	virtual int				GetShadowPosition( Vector *position, QAngle *angles ) const = 0;
	virtual IPhysicsShadowController *GetShadowController( void ) const = 0;
	virtual void			RemoveShadowController() = 0;
	// applies the math of the shadow controller to this object.
	// for use in your own controllers
	// returns the new value of secondsToArrival with dt time elapsed
	virtual void unused_ComputeShadowControl() = 0;

	virtual const CPhysCollide	*GetCollide( void ) const = 0;
	virtual const char			*GetName() const = 0;

	virtual void			BecomeTrigger() = 0;
	virtual void			RemoveTrigger() = 0;

	// sets the object to be hinged.  Fixed it place, but able to rotate around one axis.
	virtual void unused_BecomeHinged() = 0;
	// resets the object to original state
	virtual void unused_RemoveHinged() = 0;

	// used to iterate the contact points of an object
	virtual IPhysicsFrictionSnapshot *CreateFrictionSnapshot() = 0;
	virtual void DestroyFrictionSnapshot( IPhysicsFrictionSnapshot *pSnapshot ) = 0;

	// dumps info about the object to Msg()
	virtual void unused_OutputDebugInfo() = 0;

	virtual void unused_GetBuoyancyRatio() = 0;
};

abstract_class IPhysicsSpring
{
public:
	virtual ~IPhysicsSpring( void ) {}
	virtual void unused_GetEndpoints() = 0;
	virtual void			SetSpringConstant( float flSpringContant) = 0;
	virtual void			SetSpringDamping( float flSpringDamping) = 0;
	virtual void			SetSpringLength( float flSpringLenght) = 0;

	// Get the starting object
	virtual void unused_GetStartObject() = 0;

	// Get the end object
	virtual void unused_GetEndObject() = 0;
};

// Purpose: These properties are defined per-material.  This is accessible at 
//			each triangle in a collision mesh
struct surfacephysicsparams_t
{
// vphysics physical properties
	float			friction;
	float			elasticity;				// collision elasticity - used to compute coefficient of restitution
	float			density;				// physical density (in kg / m^3)
	float			thickness;				// material thickness if not solid (sheet materials) in inches
	float			dampening;
};

struct surfaceaudioparams_t
{
// sounds / audio data
	alignas( 4 ) unsigned char unused_reflectivity[ 4 ];
	float			hardnessFactor;	// like elasticity, but only affects impact sound choices
	alignas( 4 ) unsigned char unused_roughnessFactor[ 8 ];
	float			hardThreshold;	// surface hardness > this causes "hard" impacts, < this causes "soft" impacts
	float			hardVelocityThreshold;	// collision velocity > this causes "hard" impacts, < this causes "soft" impacts
									// NOTE: Hard impacts must meet both hardnessFactor AND velocity thresholds
};

struct surfacesoundnames_t
{
	alignas( 2 ) unsigned char unused_stepleft[ 4 ];

	unsigned short	impactSoft;
	unsigned short	impactHard;

	unsigned short	scrapeSmooth;
	unsigned short	scrapeRough;

	unsigned short	bulletImpact;
	unsigned short	rolling;

	unsigned short	breakSound;
	unsigned short	strainSound;
};

struct surfacesoundhandles_t
{
	short	stepleft;
	short	stepright;

	short	impactSoft;
	short	impactHard;

	short	scrapeSmooth;
	short	scrapeRough;

	short	bulletImpact;
	short	rolling;

	short	breakSound;
	short	strainSound;
};

struct surfacegameprops_t
{
// game movement data
	alignas( 4 ) unsigned char unused_maxSpeedFactor[ 8 ];
// Game-specific data
	unsigned short	material;
	// Indicates whether or not the player is on a ladder.
	unsigned char	climbable;
	unsigned char	pad;
};

// Purpose: Each different material has an entry like this
struct surfacedata_t
{
	alignas( 4 ) unsigned char unused_physics[ 20 ];
	surfaceaudioparams_t	audio;		// audio parameters
	surfacesoundnames_t		sounds;		// names of linked sounds
	surfacegameprops_t		game;		// Game data / properties

	surfacesoundhandles_t		soundhandles;
};

#define VPHYSICS_SURFACEPROPS_INTERFACE_VERSION	"VPhysicsSurfaceProps001"
abstract_class IPhysicsSurfaceProps
{
public:
	virtual ~IPhysicsSurfaceProps( void ) {}

	// parses a text file containing surface prop keys
	virtual int		ParseSurfaceData( const char *pFilename, const char *pTextfile ) = 0;
	// current number of entries in the database
	virtual int		SurfacePropCount( void ) const = 0;

	virtual int		GetSurfaceIndex( const char *pSurfacePropName ) const = 0;
	virtual void	GetPhysicsProperties( int surfaceDataIndex, float *density, float *thickness, float *friction, float *elasticity ) const = 0;

	virtual surfacedata_t	*GetSurfaceData( int surfaceDataIndex ) = 0;
	virtual const char		*GetString( unsigned short stringTableIndex ) const = 0;

	virtual const char		*GetPropName( int surfaceDataIndex ) const = 0;

	// sets the global index table for world materials
	// UNDONE: Make this per-CPhysCollide
	virtual void	SetWorldMaterialIndexTable( int *pMapArray, int mapSize ) = 0;

	// NOTE: Same as GetPhysicsProperties, but maybe more convenient
	virtual void unused_GetPhysicsParameters() = 0;
};

abstract_class IPhysicsFluidController
{
public:
	virtual ~IPhysicsFluidController( void ) {}

	virtual void	SetGameData( void *pGameData ) = 0;
	virtual void	*GetGameData( void ) const = 0;

	virtual void	GetSurfacePlane( Vector *pNormal, float *pDist ) const = 0;
	virtual float	GetDensity() const = 0;
	virtual void unused_WakeAllSleepingObjects() = 0;
	virtual int		GetContents() const = 0;
};

// Purpose: parameter block for creating fluid dynamic motion
// UNDONE: Expose additional fluid model paramters?
struct fluidparams_t
{
	Vector4D	surfacePlane;	// x,y,z normal, dist (plane constant) fluid surface
	Vector		currentVelocity; // velocity of the current in inches/second
	float		damping;		// damping factor for buoyancy (tweak)
	float		torqueFactor;
	float		viscosityFactor;
	void		*pGameData;
	bool		useAerodynamics;// true if this controller should calculate surface pressure
	int			contents;

	fluidparams_t() = default;
	fluidparams_t( fluidparams_t const& src )
	{
		Vector4DCopy( src.surfacePlane, surfacePlane );
		VectorCopy( src.currentVelocity, currentVelocity );
		damping = src.damping;
		torqueFactor = src.torqueFactor;
		viscosityFactor = src.viscosityFactor;
		contents = src.contents;
		useAerodynamics = src.useAerodynamics;
		pGameData = nullptr;
	}
};

// Purpose: parameter block for creating linear springs
// UNDONE: Expose additional spring model paramters?
struct springparams_t
{
	springparams_t()
	{
		memset( this, 0, sizeof(*this) );
	}
	float	constant;		// spring constant
	float	naturalLength;// relaxed length
	float	damping;		// damping factor
	float	relativeDamping;	// relative damping (damping proportional to the change in the relative position of the objects)
	Vector	startPosition;
	Vector	endPosition;
	bool	useLocalPositions;	// start & end Position are in local space to start and end objects if this is true
	bool	onlyStretch;		// only apply forces when the length is greater than the natural length
};

// Purpose: parameter block for creating polygonal objects
struct objectparams_t
{
	Vector		*massCenterOverride;
	float		mass;
	float		inertia;
	float		damping;
	float		rotdamping;
	float		rotInertiaLimit;
	const char	*pName;				// used only for debugging
	void		*pGameData;
	float		volume;
	float		dragCoefficient;
	bool		enableCollisions;
};

// Physics interface IDs
// Note that right now the order of the enum also defines the order of save/load

// Purpose: parameter blocks for save and load operations
struct physsaveparams_t
{
	ISave 				*pSave;
	void 				*pObject;
	PhysInterfaceId_t 	type;
};

struct physrestoreparams_t
{
	IRestore 			*pRestore;
	void 				**ppObject;
	PhysInterfaceId_t 	type;
	void 				*pGameData;
	const char			*pName;				// used only for debugging
	const CPhysCollide 	*pCollisionModel;
	IPhysicsEnvironment *pEnvironment;
	IPhysicsGameTrace	*pGameTrace;
};

#define DEFINE_PIID( type, enumval ) \
	template <> inline PhysInterfaceId_t GetPhysIID<type>( type ** ) { return enumval; }

template <class PHYSPTR> inline PhysInterfaceId_t GetPhysIID(PHYSPTR **); // will get link error if no match

DEFINE_PIID( IPhysicsObject, 			PIID_IPHYSICSOBJECT );
DEFINE_PIID( IPhysicsFluidController, 	PIID_IPHYSICSFLUIDCONTROLLER );
DEFINE_PIID( IPhysicsSpring, 			PIID_IPHYSICSSPRING );
DEFINE_PIID( IPhysicsConstraintGroup, 	PIID_IPHYSICSCONSTRAINTGROUP );
DEFINE_PIID( IPhysicsConstraint, 		PIID_IPHYSICSCONSTRAINT );
DEFINE_PIID( IPhysicsShadowController, 	PIID_IPHYSICSSHADOWCONTROLLER );
DEFINE_PIID( IPhysicsPlayerController,	PIID_IPHYSICSPLAYERCONTROLLER );
DEFINE_PIID( IPhysicsMotionController,	PIID_IPHYSICSMOTIONCONTROLLER );
DEFINE_PIID( IPhysicsVehicleController,	PIID_IPHYSICSVEHICLECONTROLLER );
DEFINE_PIID( IPhysicsGameTrace,			PIID_IPHYSICSGAMETRACE );
