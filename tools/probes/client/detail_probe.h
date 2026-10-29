//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// tools/probes/client/detail_probe.h -- lane C7 detail's A/B probe, for the
// probe copies of game/client/detailobjectsystem.cpp (the base) and of
// src/client/native/shell_detail_props.cpp (the port), applied by
// detailobjectsystem.cpp.probe.diff and shell_detail_props.cpp.probe.diff
// (probe_copies.cmake, SS_DT_PROBES=ON). Both copies log the same lines, so the
// two recordings of one camera compare as text:
//
//   cs_cl_probe_detail <frames>   arm: log the next <frames> frames
//
//   f <framecount>                 a new frame
//   c <call>                       which draw: fastlist, oldlist, fastleaf,
//                                  oldleaf (the stock function's half)
//   b <quads>                      CMeshBuilder::Begin
//   v x y z rgba s t               a vertex (floats as %.9g)
//   e                              CMeshBuilder::End
//   m <model> o.. a.. alpha rgb    a model prop's DrawModel: its model's name,
//                                  origin, angles, alpha and colour modulation
//
// The log is /tmp/do-dt-probe.<pid>.log. Nothing here exists in a default
// build.

#pragma once

#include "materialsystem/imesh.h"
#include "tier1/convar.h"
#include <stdio.h>
#include <unistd.h>

inline int& DetailProbeFrames()
{
	static int s_nFrames = 0;
	return s_nFrames;
}

inline FILE* DetailProbeFile()
{
	static FILE* s_pFile = NULL;
	if (!s_pFile)
	{
		char szPath[128];
		snprintf(szPath, sizeof(szPath), "/tmp/do-dt-probe.%d.log", (int)getpid());
		s_pFile = fopen(szPath, "w");
	}
	return s_pFile;
}

// Whether this frame logs: the first call in a new frame spends one of the
// armed frames and writes its line.
inline bool DetailProbeOn()
{
	static int s_nLastFrame = -1;
	if (DetailProbeFrames() <= 0 && s_nLastFrame < 0)
		return false;
	const int nFrame = gpGlobals->framecount;
	if (nFrame != s_nLastFrame)
	{
		if (DetailProbeFrames() <= 0)
		{
			s_nLastFrame = -1;
			if (FILE* f = DetailProbeFile())
				fflush(f);
			return false;
		}
		--DetailProbeFrames();
		s_nLastFrame = nFrame;
		if (FILE* f = DetailProbeFile())
			fprintf(f, "f %d\n", nFrame);
	}
	return DetailProbeFile() != NULL;
}

inline void DetailProbeLine(const char* pszLine)
{
	if (DetailProbeOn())
		fprintf(DetailProbeFile(), "%s\n", pszLine);
}

inline void DetailProbeModel(const char* pszModel, const Vector& o, const QAngle& a, int alpha, const float* rgb)
{
	if (!DetailProbeOn())
		return;
	fprintf(DetailProbeFile(), "m %s %.9g %.9g %.9g %.9g %.9g %.9g %d %.9g %.9g %.9g\n", pszModel, o.x, o.y, o.z, a.x,
			a.y, a.z, alpha, rgb[0], rgb[1], rgb[2]);
}

class CDetailProbeMeshBuilder : public CMeshBuilder
{
public:
	void Begin(IMesh* pMesh, MaterialPrimitiveType_t type, int numPrimitives)
	{
		if (DetailProbeOn())
			fprintf(DetailProbeFile(), "b %d\n", numPrimitives);
		CMeshBuilder::Begin(pMesh, type, numPrimitives);
	}

	void End(bool bSpewData = false, bool bDraw = false)
	{
		if (DetailProbeOn())
			fprintf(DetailProbeFile(), "e\n");
		CMeshBuilder::End(bSpewData, bDraw);
	}

	void Position3f(float x, float y, float z)
	{
		m_p[0] = x;
		m_p[1] = y;
		m_p[2] = z;
		CMeshBuilder::Position3f(x, y, z);
	}

	void Position3fv(const float* v) { Position3f(v[0], v[1], v[2]); }

	void Color4ubv(unsigned char const* c)
	{
		m_c[0] = c[0];
		m_c[1] = c[1];
		m_c[2] = c[2];
		m_c[3] = c[3];
		CMeshBuilder::Color4ubv(c);
	}

	void TexCoord2f(int stage, float s, float t)
	{
		m_t[0] = s;
		m_t[1] = t;
		CMeshBuilder::TexCoord2f(stage, s, t);
	}

	void TexCoord2fv(int stage, const float* st) { TexCoord2f(stage, st[0], st[1]); }

	void AdvanceVertex()
	{
		if (DetailProbeOn())
			fprintf(DetailProbeFile(), "v %.9g %.9g %.9g %02x%02x%02x%02x %.9g %.9g\n", m_p[0], m_p[1], m_p[2], m_c[0],
					m_c[1], m_c[2], m_c[3], m_t[0], m_t[1]);
		CMeshBuilder::AdvanceVertex();
	}

private:
	float m_p[3] = {};
	unsigned char m_c[4] = {};
	float m_t[2] = {};
};

#define DETAIL_PROBE_COMMAND()                                                                      \
	CON_COMMAND(cs_cl_probe_detail, "C7 detail probe (debug): log the next N frames' detail draws") \
	{                                                                                               \
		DetailProbeFrames() = args.ArgC() > 1 ? atoi(args[1]) : 1;                                  \
		Msg("detail probe: %d frame(s)\n", DetailProbeFrames());                                    \
	}
