//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "simple_physics.h"
#include "networkvar.h"

class CRopeSpring
{
public:
	Vector* m_pNode1;
	Vector* m_pNode2;
};

class CBaseRopePhysics : public CSimplePhysics::IHelper
{
public:
	DECLARE_CLASS_NOBASE(CBaseRopePhysics);

	CBaseRopePhysics(CSimplePhysics::CNode* pNodes, int nNodes, CRopeSpring* pSprings, float* flSpringDistsSqr);

public:
	virtual void GetNodeForces(CSimplePhysics::CNode* pNodes, int iNode, Vector* pAccel);
	virtual void ApplyConstraints(CSimplePhysics::CNode* pNodes, int nNodes);

private:
protected:
	CSimplePhysics::IHelper* m_pDelegate;

	CSimplePhysics::CNode* m_pNodes;
	int m_nNodes;

	CRopeSpring* m_pSprings;

	float m_flSpringDist;
	float m_flSpringDistSqr;

	// Spring lengths per node
	float* m_flNodeSpringDistsSqr;

	CSimplePhysics m_Physics;
};

template <int NUM_NODES>
class CRopePhysics : public CBaseRopePhysics
{
public:
	CRopePhysics();

	CSimplePhysics::CNode m_Nodes[NUM_NODES];
	CRopeSpring m_Springs[NUM_NODES - 1];
	float m_SpringDistsSqr[NUM_NODES - 1];
};

template <int NUM_NODES>
CRopePhysics<NUM_NODES>::CRopePhysics()
	: CBaseRopePhysics(m_Nodes, NUM_NODES, m_Springs, m_SpringDistsSqr)
{
}
