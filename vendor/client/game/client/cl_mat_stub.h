//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class IMaterialSystem;
class IManagedMaterialProxyClient;

// To stub out the material system in a block of code (if mat_stub is 1),
// make an instance of this class. You can unstub it by calling End() or
// it will automatically unstub in its destructor.
class CMatStubHandler
{
public:
	explicit CMatStubHandler(IManagedMaterialProxyClient* pManaged);
	~CMatStubHandler();

	void End();

public:
	IMaterialSystem* m_pOldMaterialSystem;
};
