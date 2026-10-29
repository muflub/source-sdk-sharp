// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The tier2 names particles.a links against, stubbed: they draw nothing and count their
// calls, which client.so reads through ISSParticleLib::Counters.

#include "ss_particles/iparticlelib.h"

#include "Color.h"

#include "tier2/beamsegdraw.h"

extern SSParticleLibCounters_t g_SSParticleLibCounters;

// renderutils.h is not in the farm; these are the declarations particles.a
// was compiled against.
void RenderWireframeSphere(const Vector& vCenter, float flRadius, int nTheta, int nPhi, Color c, bool bZBuffer);
void RenderWireframeBox(const Vector& vOrigin, const QAngle& angles, const Vector& vMins, const Vector& vMaxs, Color c,
						bool bZBuffer);

void CBeamSegDraw::Start(IMatRenderContext* pRenderContext, int nSegs, IMaterial*, CMeshBuilder* pMeshBuilder,
						 int nMeshVertCount)
{
	m_pRenderContext = pRenderContext;
	m_pMeshBuilder = pMeshBuilder;
	m_nMeshVertCount = nMeshVertCount;
	m_nTotalSegs = nSegs;
	m_nSegsDrawn = 0;
	g_SSParticleLibCounters.nBeamStrips++;
}

// The class's key function: its vtable is emitted here, which particles.a
// also imports.
void CBeamSegDraw::NextSeg(BeamSeg_t*)
{
	m_nSegsDrawn++;
}

void CBeamSegDraw::End()
{
	m_pMeshBuilder = NULL;
}

void RenderWireframeSphere(const Vector&, float, int, int, Color, bool)
{
	g_SSParticleLibCounters.nWireframeSpheres++;
}

void RenderWireframeBox(const Vector&, const QAngle&, const Vector&, const Vector&, Color, bool)
{
	g_SSParticleLibCounters.nWireframeBoxes++;
}
