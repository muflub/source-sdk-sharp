//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "mat_stub_swap.h"

class IMaterialSystem;
class IManagedMaterialProxyClient;
class CClientEngineInterfaces;
class IMaterialSystemStub;
class IVEngineClient;

// To stub out the material system in a block of code (if mat_stub is 1),
// make an instance of this class. You can unstub it by calling End() or
// it will automatically unstub in its destructor.
class CMatStubHandler
{
public:
	CMatStubHandler(IManagedMaterialProxyClient* pManaged, CClientEngineInterfaces& engines);
	~CMatStubHandler();

	void End();

private:
	IMaterialSystem** m_ppSlots[2];
	CMatStubSwap<IMaterialSystem, IMaterialSystemStub, IVEngineClient> m_Swap;
};
