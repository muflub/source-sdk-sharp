//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#define VEHICLE_MAX_AXLE_COUNT	4
#define VEHICLE_MAX_GEAR_COUNT	6

struct vehicle_controlparams_t
{
	float	throttle;
	float	steering;
	float	brake;
	float	boost;
	bool	handbrake;
	bool	handbrakeLeft;
	bool	handbrakeRight;
	bool	brakepedal;
	bool	bHasBrakePedal;
	bool	bAnalogSteering;
};

struct vehicle_operatingparams_t
{
	DECLARE_SIMPLE_DATADESC();

	float	speed;
	float	engineRPM;
	int		gear;
	float	boostDelay;
	int		boostTimeLeft;
	float	skidSpeed;
	int		skidMaterial;
	float	steeringAngle;
	int		wheelsNotInContact;
	int		wheelsInContact;
	bool	isTorqueBoosting;
};

// Debug!

struct vehicleparams_t;

class IPhysicsVehicleController
{
public:
	virtual ~IPhysicsVehicleController() {}
	// call this from the game code with the control parameters
	virtual void Update( float dt, vehicle_controlparams_t &controls ) = 0;
	virtual void unused_GetOperatingParams() = 0;
	virtual void unused_GetVehicleParams() = 0;
	virtual void unused_GetVehicleParamsForChange() = 0;
	virtual void unused_UpdateBooster() = 0;
	virtual void unused_GetWheelCount() = 0;
	virtual void unused_GetWheel() = 0;
	virtual void unused_GetWheelContactPoint() = 0;
	virtual void SetSpringLength(int wheelIndex, float length) = 0;
	virtual void unused_SetWheelFriction() = 0;

	virtual void unused_OnVehicleEnter() = 0;
	virtual void unused_OnVehicleExit() = 0;

	virtual void unused_SetEngineDisabled() = 0;
	virtual void unused_IsEngineDisabled() = 0;

	// Debug
	virtual void unused_GetCarSystemDebugData() = 0;
	virtual void unused_VehicleDataReload() = 0;
};

// parameters for the body object control of the vehicle
struct vehicle_bodyparams_t
{
	DECLARE_SIMPLE_DATADESC();

	Vector		massCenterOverride;		// leave at vec3_origin for no override
	float		massOverride;			// leave at 0 for no override
	float		addGravity;				// keeps car down
	float		tiltForce;				// keeps car down when not on flat ground
	float		tiltForceHeight;		// where the tilt force pulls relative to center of mass
	float		counterTorqueFactor;
	float		keepUprightTorque;
	float		maxAngularVelocity;		// clamp the car angular velocity separately from other objects to keep stable
};

// wheel objects are created by vphysics, these are the parameters for those objects
// NOTE: They are paired, so only one set of parameters is necessary per axle
struct vehicle_wheelparams_t
{
	DECLARE_SIMPLE_DATADESC();

	float		radius;
	float		mass;
	float		inertia;
	float		damping;		// usually 0
	float		rotdamping;		// usually 0
	float		frictionScale;	// 1.5 front, 1.8 rear
	int			materialIndex;
	int			brakeMaterialIndex;
	int			skidMaterialIndex;
	float		springAdditionalLength;	// 0 means the spring is at it's rest length
};

struct vehicle_suspensionparams_t
{
	DECLARE_SIMPLE_DATADESC();

	float		springConstant;
	float		springDamping;
	float		stabilizerConstant;
	float		springDampingCompression;
	float		maxBodyForce;
};

// NOTE: both raytrace and wheel data here because jetski uses both.
struct vehicle_axleparams_t
{
	DECLARE_SIMPLE_DATADESC();

	Vector						offset;					// center of this axle in vehicle object space
	Vector						wheelOffset;			// offset to wheel (assume other wheel is symmetric at -wheelOffset) from axle center
	Vector						raytraceCenterOffset;	// offset to center of axle for the raytrace data.
	Vector						raytraceOffset;			// offset to raytrace for non-wheel (some wheeled) vehicles
	vehicle_wheelparams_t		wheels;
	vehicle_suspensionparams_t	suspension;
	float						torqueFactor;		// normalized to 1 across all axles 
													// e.g. 0,1 for rear wheel drive - 0.5,0.5 for 4 wheel drive
	float						brakeFactor;		// normalized to 1 across all axles
};

struct vehicle_steeringparams_t
{
	DECLARE_SIMPLE_DATADESC();

	float		degreesSlow;			// angle in degrees of steering at slow speed
	float		degreesFast;			// angle in degrees of steering at fast speed
	float		degreesBoost;			// angle in degrees of steering at fast speed
	float		steeringRateSlow;		// this is the speed the wheels are steered when the vehicle is slow
	float		steeringRateFast;		// this is the speed the wheels are steered when the vehicle is "fast"
	float		steeringRestRateSlow;	// this is the speed at which the wheels move toward their resting state (straight ahead) at slow speed
	float		steeringRestRateFast;	// this is the speed at which the wheels move toward their resting state (straight ahead) at fast speed
	float		speedSlow;				// this is the max speed of "slow"
	float		speedFast;				// this is the min speed of "fast"
	float		turnThrottleReduceSlow;		// this is the amount of throttle reduction to apply at the maximum steering angle
	float		turnThrottleReduceFast;		// this is the amount of throttle reduction to apply at the maximum steering angle
	float		brakeSteeringRateFactor;	// this scales the steering rate when the brake/handbrake is down
	float		throttleSteeringRestRateFactor;	// this scales the steering rest rate when the throttle is down
	float		powerSlideAccel;		// scale of speed to acceleration
	float		boostSteeringRestRateFactor;	// this scales the steering rest rate when boosting
	float		boostSteeringRateFactor;	// this scales the steering rest rate when boosting
	float		steeringExponent;		// this makes the steering response non-linear.  The steering function is linear, then raised to this power

	bool		isSkidAllowed;			// true/false skid flag
	bool		dustCloud;				// flag for creating a dustcloud behind vehicle
};

struct vehicle_engineparams_t
{
	DECLARE_SIMPLE_DATADESC();

	float		horsepower;
	float		maxSpeed;
	float		maxRevSpeed;
	float		maxRPM;					// redline RPM limit
	float		axleRatio;				// ratio of engine rev to axle rev
	float		throttleTime;			// time to reach full throttle in seconds

	// transmission
	int			gearCount;				// gear count - max 10
	float		gearRatio[VEHICLE_MAX_GEAR_COUNT];	// ratio for each gear

	// automatic transmission (simple auto-shifter - switches at fixed RPM limits)
	float		shiftUpRPM;				// max RPMs to switch to a higher gear
	float		shiftDownRPM;			// min RPMs to switch to a lower gear
	float		boostForce;
	float		boostDuration;
	float		boostDelay;
	float		boostMaxSpeed;
	float		autobrakeSpeedGain;
	float		autobrakeSpeedFactor;
	bool		torqueBoost;
	bool		isAutoTransmission;		// true for auto, false for manual
};

struct vehicleparams_t
{
	DECLARE_SIMPLE_DATADESC();

	int							axleCount;
	int							wheelsPerAxle;
	vehicle_bodyparams_t		body;
	vehicle_axleparams_t		axles[VEHICLE_MAX_AXLE_COUNT];
	vehicle_engineparams_t		engine;
	vehicle_steeringparams_t	steering;
};

		// An invalid seat
