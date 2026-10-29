//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class IPhysicsPlayerController
{
public:
	virtual ~IPhysicsPlayerController() {}

	virtual void Update(const Vector& position, const Vector& velocity, float secondsToArrival, bool onground,
						IPhysicsObject* ground) = 0;
	virtual void unused_SetEventHandler() = 0;
	virtual bool IsInContact() = 0;
	virtual void unused_MaxSpeed() = 0;

	// allows game code to change collision models
	virtual void SetObject(IPhysicsObject* pObject) = 0;
	// UNDONE: Refactor this and shadow controllers into a single class/interface through IPhysicsObject
	virtual int GetShadowPosition(Vector* position, QAngle* angles) = 0;
	virtual void StepUp(float height) = 0;
	virtual void Jump() = 0;
	virtual void GetShadowVelocity(Vector* velocity) = 0;
	virtual IPhysicsObject* GetObject() = 0;
	virtual void GetLastImpulse(Vector* pOut) = 0;

	virtual void SetPushMassLimit(float maxPushMass) = 0;
	virtual void SetPushSpeedLimit(float maxPushSpeed) = 0;

	virtual void unused_GetPushMassLimit() = 0;
	virtual void unused_GetPushSpeedLimit() = 0;
	virtual bool WasFrozen() = 0;
};
