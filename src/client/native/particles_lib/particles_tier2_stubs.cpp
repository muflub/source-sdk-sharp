//========= SourceSharp — what particles.a imports from tier2, stubbed
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// User ruling 2026-10-12: "stub the things particles needs from tier2 and
// remove the library". It was the last importer of tier2.a once the client's
// own objects stopped (plan_client_novalve C9): C_OP_RenderRope::RenderUnsorted
// draws through CBeamSegDraw, and the debug Render() of C_INIT_CreateWithinBox,
// C_INIT_CreateWithinSphere, C_INIT_PositionOffset and C_OP_ControlpointLight
// through RenderWireframeBox and RenderWireframeSphere.
//
// plan_particles_so.md: particles.a moved from client.so into libss_particles.so
// and this file moved with it; the names particles.a links against are the
// library's now, local to it.
//
// These are the names and signatures particles.a links against, and they draw
// nothing. Each counts its calls; client.so reads the counts through
// ISSParticleLib::Counters into the effect ledger (cs_cl_effects_report), so a
// non-zero count says a particle system really asked for a rope segment or a
// debug wireframe. At 2026-10-12 the only shipped systems with a render_rope
// operator are HL2's vortigaunt_fx.pcf and the blood_impact.pcf synth arcs,
// which nothing in this mod or its maps starts. If that changes, the stub
// forwards to managed BeamSegDraw instead (raised with the user).
//
// Exit: real tier2 ports in the library, or particles.a's rope and debug
// wireframe draws through managed.
//=============================================================================//

#include "ss_particles/iparticlelib.h"

#include "Color.h"
#include "mathlib/vector.h"
#include "tier2/beamsegdraw.h"

extern SSParticleLibCounters_t g_SSParticleLibCounters;

// renderutils.h is not in the farm; these are the declarations particles.a
// was compiled against.
void RenderWireframeSphere( const Vector &vCenter, float flRadius, int nTheta, int nPhi, Color c, bool bZBuffer );
void RenderWireframeBox( const Vector &vOrigin, const QAngle &angles, const Vector &vMins, const Vector &vMaxs, Color c,
                         bool bZBuffer );

void CBeamSegDraw::Start( IMatRenderContext *pRenderContext, int nSegs, IMaterial *, CMeshBuilder *pMeshBuilder,
                          int nMeshVertCount )
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
void CBeamSegDraw::NextSeg( BeamSeg_t * )
{
	m_nSegsDrawn++;
}

void CBeamSegDraw::End()
{
	m_pMeshBuilder = NULL;
}

void RenderWireframeSphere( const Vector &, float, int, int, Color, bool )
{
	g_SSParticleLibCounters.nWireframeSpheres++;
}

void RenderWireframeBox( const Vector &, const QAngle &, const Vector &, const Vector &, Color, bool )
{
	g_SSParticleLibCounters.nWireframeBoxes++;
}
