//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// constraint groups
struct constraint_groupparams_t
{
	int		additionalIterations;		// additional solver iterations make the constraint system more stable
	int		minErrorTicks;				// minimum number of ticks with an error before it's reported
	float	errorTolerance;				// error tolerance in HL units

};

// Breakable constraints;
//	forceLimit	- kg * in / s limit (N * conversion(in/m))
//	torqueLimit - kg * in^2 / s (Nm * conversion(in^2/m^2))

// strength 0 - 1
struct constraint_breakableparams_t
{
	float		strength;				// strength of the constraint 0.0 - 1.0
	float		forceLimit;				// constraint force limit to break (0 means never break)
	float		torqueLimit;			// constraint torque limit to break (0 means never break)
	float		bodyMassScale[2];		// scale applied to mass of reference/attached object before solving constriant
	bool		isActive;

};

// Purpose: constraint limit on a single rotation axis
struct constraint_axislimit_t
{
	float		minRotation;
	float		maxRotation;
	float		angularVelocity;		// desired angular velocity around hinge
	float		torque;					// torque to achieve angular velocity (use 0, torque for "friction")

};

// Purpose: special limited ballsocket constraint for ragdolls.
//			Has axis limits for all 3 axes.
struct constraint_ragdollparams_t
{
	constraint_breakableparams_t constraint;
	matrix3x4_t			constraintToReference;// xform constraint space to refobject space
	matrix3x4_t			constraintToAttached;	// xform constraint space to attached object space
	int					parentIndex;				// NOTE: only used for parsing.  NEED NOT BE SET for create
	int					childIndex;					// NOTE: only used for parsing.  NEED NOT BE SET for create
	
	constraint_axislimit_t	axes[3];
	bool				onlyAngularLimits;			// only angular limits (not translation as well?)
	bool				isActive;
	bool				useClockwiseRotations;		// HACKHACK: Did this wrong in version one.  Fix in the future.

};

// Purpose: Used to init a hinge restricting the relative position and orientation
//			of two objects to rotation around a single axis
struct constraint_hingeparams_t
{
	Vector							worldPosition;			// position in world space on the hinge axis
	Vector							worldAxisDirection;		// unit direction vector of the hinge axis in world space
	constraint_axislimit_t			hingeAxis;
	constraint_breakableparams_t	constraint;

};

// Purpose: Used to init a constraint that fixes the position and orientation
//			of two objects relative to each other (like glue)
struct constraint_fixedparams_t
{
	matrix3x4_t						attachedRefXform;	// xform attached object space to ref object space
	constraint_breakableparams_t	constraint;
	
};

// Purpose: Same parameters as fixed constraint, but torqueLimit has no effect
struct constraint_ballsocketparams_t
{
	Vector							constraintPosition[2];		// position of the constraint in each object's space 
	constraint_breakableparams_t	constraint;

};

struct constraint_slidingparams_t
{
	matrix3x4_t						attachedRefXform;	// xform attached object space to ref object space
	Vector							slideAxisRef;				// unit direction vector of the slide axis in ref object space
	constraint_breakableparams_t	constraint;
	// NOTE: if limitMin == limitMax there is NO limit set!
	float							limitMin;				// minimum limit coordinate refAxisDirection space
	float							limitMax;				// maximum limit coordinate refAxisDirection space
	float							friction;				// friction on sliding
	float							velocity;				// desired velocity

};

struct constraint_pulleyparams_t
{
	constraint_breakableparams_t	constraint;
	Vector	pulleyPosition[2];		// These are the pulley positions for the reference and attached objects in world space
	Vector	objectPosition[2];		// local positions of attachments to the ref,att objects
	float	totalLength;			// total rope length (include gearing!)
	float	gearRatio;				// gearing affects attached object ALWAYS
	bool	isRigid;

};

struct constraint_lengthparams_t
{
	constraint_breakableparams_t	constraint;
	Vector	objectPosition[2];		// These are the positions for the reference and attached objects in local space
	float	totalLength;		// Length of rope/spring/constraint.  Distance to maintain
	float	minLength;			// if rigid, objects are not allowed to move closer than totalLength either

};

class IPhysicsConstraint
{
public:
	virtual ~IPhysicsConstraint() {}
	
	// NOTE: Constraints are active when created.  You can temporarily enable/disable them with these functions
	virtual void			Activate() = 0;
	virtual void			Deactivate() = 0;

	// set a pointer to the game object
	virtual void SetGameData( void *gameData ) = 0;

	// get a pointer to the game object
	virtual void *GetGameData() const = 0;

	// Get the parent/referenced object
	virtual IPhysicsObject *GetReferenceObject() const = 0;

	// Get the attached object
	virtual IPhysicsObject *GetAttachedObject() const = 0;

	virtual void			SetLinearMotor( float speed, float maxLinearImpulse ) = 0;
	virtual void			SetAngularMotor( float rotSpeed, float maxAngularImpulse ) = 0;

	virtual void unused_UpdateRagdollTransforms() = 0;
	virtual void unused_GetConstraintTransform() = 0;
	virtual void unused_GetConstraintParams() = 0;

	virtual void unused_OutputDebugInfo() = 0;
};

class IPhysicsConstraintGroup
{
public:
	virtual ~IPhysicsConstraintGroup() {}
	virtual void Activate() = 0;
	virtual bool IsInErrorState() = 0;
	virtual void ClearErrorState() = 0;
	virtual void unused_GetErrorParams() = 0;
	virtual void unused_SetErrorParams() = 0;
	virtual void unused_SolvePenetration() = 0;
};
