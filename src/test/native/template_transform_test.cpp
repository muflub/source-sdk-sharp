//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the golden table for template placement ============//
//
// point_template's arithmetic moved to C#. This is the half of the agreement
// that faces NATIVE: every row of template_transform_cases.txt is recomputed
// here with the real mathlib VMatrix, exactly as CPointTemplate::AddTemplateNamed
// and ::CreateInstance did, and asserted against what the file says.
//
// WHY THIS EXISTS AND NOT JUST THE C# TEST. A fixture that stands in for native
// drifts silently: the managed test would agree with the table, the table would
// agree with nothing, and a wrong number would be checked against itself
// forever. Here the table is pinned to the arithmetic it was taken from — so
// the managed test on the other side of it is a real comparison rather than a
// self-portrait. See docs/writing-tests.md, "A fixture that stands in for
// native must be checked against it".
//
// This is the only test in this tree that links mathlib. It has to: the whole
// claim is about what mathlib computes, and a hand-written stand-in for VMatrix
// would be the very thing the table is guarding against.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "mathlib/vmatrix.h"

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include "mathlib_test_env.h"

namespace
{

struct Row
{
	std::string name;
	float tOrg[3], tAng[3];
	float eOrg[3], eAng[3];
	float nOrg[3], nAng[3];
	float outOrg[3], outAng[3];
};

//-----------------------------------------------------------------------------
// The table, found by walking up from wherever the test binary was run.
//
// Not an absolute path and not a compiled-in copy: a compiled-in copy would be
// a SECOND record of the numbers and the two would part company, which is the
// failure this whole file exists to prevent.
//-----------------------------------------------------------------------------
std::string FindCases()
{
	const char* kRelative = "sourcesharp/test/template_transform_cases.txt";
	std::string prefix;

	for (int i = 0; i < 8; i++)
	{
		std::string candidate = prefix + kRelative;
		if (FILE* f = fopen(candidate.c_str(), "r"))
		{
			fclose(f);
			return candidate;
		}
		prefix += "../";
	}

	return std::string();
}

std::vector<Row> LoadCases()
{
	std::vector<Row> rows;

	const std::string path = FindCases();
	if (path.empty())
		return rows;

	FILE* f = fopen(path.c_str(), "r");
	if (!f)
		return rows;

	char line[1024];
	while (fgets(line, sizeof(line), f))
	{
		if (line[0] == '#' || line[0] == '\n' || line[0] == '\r')
			continue;

		char name[128];
		Row r;
		int n = sscanf(line, "%127s %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f", name,
					   &r.tOrg[0], &r.tOrg[1], &r.tOrg[2], &r.tAng[0], &r.tAng[1], &r.tAng[2], &r.eOrg[0], &r.eOrg[1],
					   &r.eOrg[2], &r.eAng[0], &r.eAng[1], &r.eAng[2], &r.nOrg[0], &r.nOrg[1], &r.nOrg[2], &r.nAng[0],
					   &r.nAng[1], &r.nAng[2], &r.outOrg[0], &r.outOrg[1], &r.outOrg[2], &r.outAng[0], &r.outAng[1],
					   &r.outAng[2]);

		if (n != 25)
			continue;

		r.name = name;
		rows.push_back(r);
	}

	fclose(f);
	return rows;
}

//-----------------------------------------------------------------------------
// The two halves of the pipeline, copied verbatim from the C++ that is going.
//-----------------------------------------------------------------------------
void Place(const Row& r, Vector& vecOut, QAngle& angOut)
{
	VMatrix matTemplateToWorld, matWorldToTemplate, matEntityToWorld, matEntityToTemplate;

	matTemplateToWorld.SetupMatrixOrgAngles(Vector(r.tOrg[0], r.tOrg[1], r.tOrg[2]),
											QAngle(r.tAng[0], r.tAng[1], r.tAng[2]));
	matTemplateToWorld.InverseTR(matWorldToTemplate);
	matEntityToWorld.SetupMatrixOrgAngles(Vector(r.eOrg[0], r.eOrg[1], r.eOrg[2]),
										  QAngle(r.eAng[0], r.eAng[1], r.eAng[2]));
	MatrixMultiply(matWorldToTemplate, matEntityToWorld, matEntityToTemplate);

	VMatrix matNewTemplateToWorld, matStoredLocalToWorld;
	matNewTemplateToWorld.SetupMatrixOrgAngles(Vector(r.nOrg[0], r.nOrg[1], r.nOrg[2]),
											   QAngle(r.nAng[0], r.nAng[1], r.nAng[2]));
	MatrixMultiply(matNewTemplateToWorld, matEntityToTemplate, matStoredLocalToWorld);

	vecOut = matStoredLocalToWorld.GetTranslation();
	MatrixToAngles(matStoredLocalToWorld, angOut);
}

//-----------------------------------------------------------------------------
// The file has to be FOUND, and it has to hold every row the managed side
// expects. A table that silently loaded as empty would pass the loop below
// without executing it once — the shape of "check that cannot fail" this tree
// keeps finding.
//-----------------------------------------------------------------------------
TEST(TemplateTransform, TheTableIsThereAndHasEveryRow)
{
	const std::vector<Row> rows = LoadCases();

	ASSERT_FALSE(rows.empty()) << "template_transform_cases.txt was not found next to the test";
	EXPECT_EQ(13u, rows.size()) << "the table has the rows both sides were written against";
}

//-----------------------------------------------------------------------------
// Every row, against the arithmetic it was taken from.
//-----------------------------------------------------------------------------
TEST(TemplateTransform, EveryRowIsWhatVMatrixComputes)
{
	const std::vector<Row> rows = LoadCases();
	ASSERT_FALSE(rows.empty());

	for (size_t i = 0; i < rows.size(); i++)
	{
		const Row& r = rows[i];

		Vector vecOut;
		QAngle angOut;
		Place(r, vecOut, angOut);

		EXPECT_NEAR(r.outOrg[0], vecOut.x, 0.001f) << r.name << " origin x";
		EXPECT_NEAR(r.outOrg[1], vecOut.y, 0.001f) << r.name << " origin y";
		EXPECT_NEAR(r.outOrg[2], vecOut.z, 0.001f) << r.name << " origin z";

		EXPECT_NEAR(r.outAng[0], angOut[0], 0.001f) << r.name << " pitch";
		EXPECT_NEAR(r.outAng[1], angOut[1], 0.001f) << r.name << " yaw";
		EXPECT_NEAR(r.outAng[2], angOut[2], 0.001f) << r.name << " roll";
	}
}

//-----------------------------------------------------------------------------
// The property the table cannot state on its own, and the one a mapper relies
// on: instancing a template AT the template's own place puts every source back
// exactly where it was drawn.
//
// Checked against VMatrix here for the same reason as the rows — so the managed
// side's copy of this claim is compared with something rather than believed.
//-----------------------------------------------------------------------------
TEST(TemplateTransform, InstancingAtHomePutsASourceBackWhereItWas)
{
	Row r;
	r.name = "at-home";
	r.tOrg[0] = 12.f;
	r.tOrg[1] = -34.f;
	r.tOrg[2] = 56.f;
	r.tAng[0] = 7.f;
	r.tAng[1] = 111.f;
	r.tAng[2] = -23.f;
	r.eOrg[0] = -90.f;
	r.eOrg[1] = 200.f;
	r.eOrg[2] = 5.f;
	r.eAng[0] = 3.f;
	r.eAng[1] = -47.f;
	r.eAng[2] = 61.f;
	memcpy(r.nOrg, r.tOrg, sizeof(r.nOrg));
	memcpy(r.nAng, r.tAng, sizeof(r.nAng));

	Vector vecOut;
	QAngle angOut;
	Place(r, vecOut, angOut);

	EXPECT_NEAR(r.eOrg[0], vecOut.x, 0.01f);
	EXPECT_NEAR(r.eOrg[1], vecOut.y, 0.01f);
	EXPECT_NEAR(r.eOrg[2], vecOut.z, 0.01f);

	EXPECT_NEAR(r.eAng[0], angOut[0], 0.01f);
	EXPECT_NEAR(r.eAng[1], angOut[1], 0.01f);
	EXPECT_NEAR(r.eAng[2], angOut[2], 0.01f);
}

} // namespace
