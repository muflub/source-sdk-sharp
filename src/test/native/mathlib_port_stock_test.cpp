//========= SourceSharp — server.so's mathlib port, bit for bit against stock ==//
//
// server.so links no libmathlib.a: mathlib_port.cpp defines the mathlib
// functions it calls, each forwarding to an ss_mathlib:: inline in
// mathlib_port.h. This binary links the real libmathlib.a, so the global names
// below are STOCK, and calls each ss_mathlib:: twin on the same inputs in the
// same process: one CPU, one libm, one set of flags for the sine and the
// arctangent. Every output float must have the same bits. A port that rounds
// one product differently, swaps a branch or drops a sign is red here.
//
// The inputs are a fixed list of edge cases (the axes, straight up and down,
// the 0.001 gimbal threshold MatrixAngles switches at, signed zeros) and a few
// thousand pseudo-random ones from a fixed seed, so a failure reproduces.
//
// Linux only, as every mathlib binary is (ss_cpptest_mathlib): MathLib_Init
// needs a tier0 the SDK ships only for Linux. mathlib_port_test runs the port
// alone on both platforms.
//=============================================================================//

#include "mathlib_test_env.h"

#include "gtest/gtest.h"

#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib_port.h"

#include <stdint.h>
#include <string.h>

#include <vector>

namespace
{

uint32_t Bits(float f)
{
	uint32_t u;
	memcpy(&u, &f, sizeof(u));
	return u;
}

// A fixed-seed generator: the same inputs on every run and every machine.
class Inputs
{
public:
	float Uniform(float lo, float hi)
	{
		m_state = m_state * 6364136223846793005ull + 1442695040888963407ull;
		const float unit = (float)(m_state >> 40) / (float)(1ull << 24);
		return lo + (hi - lo) * unit;
	}

private:
	uint64_t m_state = 0x5eed5eedull;
};

std::vector<QAngle> Angles()
{
	std::vector<QAngle> out = {
		QAngle(0, 0, 0),        QAngle(90, 0, 0),
		QAngle(-90, 0, 0),      QAngle(0, 90, 0),
		QAngle(0, 180, 0),      QAngle(0, -180, 0),
		QAngle(0, 0, 90),       QAngle(45, 45, 45),
		QAngle(-30, 270, 15),   QAngle(89.9f, 10, 5),
		QAngle(90, 33, 77),     QAngle(-90, -33, 12),
		QAngle(360, 720, -360), QAngle(-0.0f, -0.0f, -0.0f),
	};
	Inputs rng;
	for (int i = 0; i < 2000; ++i)
		out.push_back(QAngle(rng.Uniform(-720, 720), rng.Uniform(-720, 720), rng.Uniform(-720, 720)));
	return out;
}

std::vector<Vector> Vectors()
{
	std::vector<Vector> out = {
		Vector(0, 0, 0),
		Vector(1, 0, 0),
		Vector(-1, 0, 0),
		Vector(0, 1, 0),
		Vector(0, -1, 0),
		Vector(0, 0, 1),
		Vector(0, 0, -1),
		Vector(1, 1, 1),
		Vector(-3, 4, -5),
		Vector(1e-7f, 0, 1),
		Vector(0, -1e-7f, -1),
		Vector(-0.0f, 0.0f, -0.0f),
		Vector(16384, -16384, 4096),
	};
	Inputs rng;
	for (int i = 0; i < 2000; ++i)
		out.push_back(Vector(rng.Uniform(-4096, 4096), rng.Uniform(-4096, 4096), rng.Uniform(-4096, 4096)));
	return out;
}

// Rotations AngleMatrix builds, with a translation, and a few hand-made
// matrices: the identity, a pure translation, and forward along +Z and -Z,
// with horizontal lengths either side of MatrixAngles' 0.001 threshold.
std::vector<matrix3x4_t> Matrices()
{
	std::vector<matrix3x4_t> out;
	matrix3x4_t m;
	SetIdentityMatrix(m);
	out.push_back(m);
	m[0][3] = 10;
	m[1][3] = -20;
	m[2][3] = 30;
	out.push_back(m);
	const float nearVertical[] = {0.0f, 0.0005f, 0.000999f, 0.001001f, 0.002f};
	for (float h : nearVertical)
	{
		for (float up : {1.0f, -1.0f})
		{
			matrix3x4_t g(h, 0.6f, 0.0f, 1.0f, 0.0f, 0.8f, 0.0f, 2.0f, -up, 0.0f, 0.3f, 3.0f);
			out.push_back(g);
		}
	}
	Inputs rng;
	for (const QAngle& a : Angles())
	{
		const Vector at(rng.Uniform(-4096, 4096), rng.Uniform(-4096, 4096), rng.Uniform(-4096, 4096));
		AngleMatrix(a, at, m);
		out.push_back(m);
	}
	return out;
}

::testing::AssertionResult SameBits(const float* port, const float* stock, int n, const char* what, int index)
{
	for (int i = 0; i < n; ++i)
	{
		if (Bits(port[i]) != Bits(stock[i]))
		{
			return ::testing::AssertionFailure()
				<< what << " input #" << index << " component " << i << ": port " << port[i] << " (0x" << std::hex
				<< Bits(port[i]) << ") stock " << stock[i] << " (0x" << Bits(stock[i]) << ")";
		}
	}
	return ::testing::AssertionSuccess();
}

::testing::AssertionResult SameMatrix(const matrix3x4_t& port, const matrix3x4_t& stock, const char* what, int index)
{
	return SameBits(port.Base(), stock.Base(), 12, what, index);
}

TEST(MathlibPortStock, TheInputListsReachEveryBranch)
{
	// The stimulus: both arms of MatrixAngles' threshold and of VectorAngles'
	// vertical case are in the lists, or the comparisons below prove less.
	int gimbal = 0, upright = 0, vertical = 0, horizontal = 0;
	for (const matrix3x4_t& m : Matrices())
	{
		const float h = sqrtf(m[0][0] * m[0][0] + m[1][0] * m[1][0]);
		(h > 0.001f ? upright : gimbal)++;
	}
	for (const Vector& v : Vectors())
		(v.x == 0 && v.y == 0 ? vertical : horizontal)++;
	EXPECT_GE(gimbal, 4);
	EXPECT_GE(upright, 2000);
	EXPECT_GE(vertical, 3);
	EXPECT_GE(horizontal, 2000);
}

TEST(MathlibPortStock, Vec3OriginAndVec3AngleAreZero)
{
	// The port defines its own; stock's are these two.
	EXPECT_EQ(vec3_origin, Vector(0, 0, 0));
	EXPECT_EQ(vec3_angle, QAngle(0, 0, 0));
}

TEST(MathlibPortStock, AngleMatrixMatchesStock)
{
	const std::vector<QAngle> angles = Angles();
	for (int i = 0; i < (int)angles.size(); ++i)
	{
		matrix3x4_t port, stock;
		ss_mathlib::AngleMatrix(angles[i], port);
		AngleMatrix(angles[i], stock);
		ASSERT_TRUE(SameMatrix(port, stock, "AngleMatrix", i));

		const Vector at(1.5f * i, -2.5f * i, 3.5f);
		ss_mathlib::AngleMatrix(angles[i], at, port);
		AngleMatrix(angles[i], at, stock);
		ASSERT_TRUE(SameMatrix(port, stock, "AngleMatrix(position)", i));
	}
}

TEST(MathlibPortStock, AngleVectorsForwardMatchesStock)
{
	const std::vector<QAngle> angles = Angles();
	for (int i = 0; i < (int)angles.size(); ++i)
	{
		Vector port, stock;
		ss_mathlib::AngleVectors(angles[i], &port);
		AngleVectors(angles[i], &stock);
		ASSERT_TRUE(SameBits(port.Base(), stock.Base(), 3, "AngleVectors(forward)", i));
	}
}

TEST(MathlibPortStock, AngleVectorsBasisMatchesStock)
{
	const std::vector<QAngle> angles = Angles();
	for (int i = 0; i < (int)angles.size(); ++i)
	{
		Vector pf, pr, pu, sf, sr, su;
		ss_mathlib::AngleVectors(angles[i], &pf, &pr, &pu);
		AngleVectors(angles[i], &sf, &sr, &su);
		ASSERT_TRUE(SameBits(pf.Base(), sf.Base(), 3, "AngleVectors forward", i));
		ASSERT_TRUE(SameBits(pr.Base(), sr.Base(), 3, "AngleVectors right", i));
		ASSERT_TRUE(SameBits(pu.Base(), su.Base(), 3, "AngleVectors up", i));
	}
}

TEST(MathlibPortStock, VectorAnglesMatchesStock)
{
	const std::vector<Vector> dirs = Vectors();
	for (int i = 0; i < (int)dirs.size(); ++i)
	{
		QAngle port, stock;
		ss_mathlib::VectorAngles(dirs[i], port);
		VectorAngles(dirs[i], stock);
		ASSERT_TRUE(SameBits(port.Base(), stock.Base(), 3, "VectorAngles", i));
	}
}

TEST(MathlibPortStock, MatrixAnglesMatchesStock)
{
	const std::vector<matrix3x4_t> mats = Matrices();
	for (int i = 0; i < (int)mats.size(); ++i)
	{
		float port[3], stock[3];
		ss_mathlib::MatrixAngles(mats[i], port);
		MatrixAngles(mats[i], stock);
		ASSERT_TRUE(SameBits(port, stock, 3, "MatrixAngles", i));
	}
}

TEST(MathlibPortStock, SetIdentityMatrixMatchesStock)
{
	matrix3x4_t port, stock;
	memset(port.Base(), 0x7f, sizeof(port));
	memset(stock.Base(), 0x7f, sizeof(stock));
	ss_mathlib::SetIdentityMatrix(port);
	SetIdentityMatrix(stock);
	EXPECT_TRUE(SameMatrix(port, stock, "SetIdentityMatrix", 0));
}

TEST(MathlibPortStock, MatrixGetColumnMatchesStock)
{
	const std::vector<matrix3x4_t> mats = Matrices();
	for (int i = 0; i < (int)mats.size(); ++i)
	{
		for (int column = 0; column < 4; ++column)
		{
			Vector port, stock;
			ss_mathlib::MatrixGetColumn(mats[i], column, port);
			MatrixGetColumn(mats[i], column, stock);
			ASSERT_TRUE(SameBits(port.Base(), stock.Base(), 3, "MatrixGetColumn", i));
		}
	}
}

TEST(MathlibPortStock, TheThreeTransformsMatchStock)
{
	const std::vector<matrix3x4_t> mats = Matrices();
	const std::vector<Vector> points = Vectors();
	for (int i = 0; i < (int)mats.size(); ++i)
	{
		const Vector& in = points[i % points.size()];
		float port[3], stock[3];

		ss_mathlib::VectorTransform(in.Base(), mats[i], port);
		VectorTransform(in.Base(), mats[i], stock);
		ASSERT_TRUE(SameBits(port, stock, 3, "VectorTransform", i));

		ss_mathlib::VectorITransform(in.Base(), mats[i], port);
		VectorITransform(in.Base(), mats[i], stock);
		ASSERT_TRUE(SameBits(port, stock, 3, "VectorITransform", i));

		ss_mathlib::VectorIRotate(in.Base(), mats[i], port);
		VectorIRotate(in.Base(), mats[i], stock);
		ASSERT_TRUE(SameBits(port, stock, 3, "VectorIRotate", i));
	}
}

TEST(MathlibPortStock, CalcSqrDistanceToAABBMatchesStock)
{
	// Points inside, on a face, and past each face on one, two and three axes.
	const std::vector<Vector> points = Vectors();
	const Vector mins(-100, -200, -300), maxs(100, 200, 300);
	for (int i = 0; i < (int)points.size(); ++i)
	{
		const float port = ss_mathlib::CalcSqrDistanceToAABB(mins, maxs, points[i]);
		const float stock = CalcSqrDistanceToAABB(mins, maxs, points[i]);
		ASSERT_TRUE(SameBits(&port, &stock, 1, "CalcSqrDistanceToAABB", i));
	}
	const Vector onFace(100, 0, 0);
	EXPECT_EQ(Bits(ss_mathlib::CalcSqrDistanceToAABB(mins, maxs, onFace)),
			  Bits(CalcSqrDistanceToAABB(mins, maxs, onFace)));
}

} // namespace

//-----------------------------------------------------------------------------
// client.so's members (plan_client_novalve C9): every one beside stock's.
//-----------------------------------------------------------------------------

// color_conversion.cpp's table; mathlib.h declares it only inside an inline.
extern float power2_n[256];

namespace
{

std::vector<Quaternion> Quaternions()
{
	std::vector<Quaternion> out = {Quaternion(0, 0, 0, 1), Quaternion(1, 0, 0, 0),
								   Quaternion(0, 0.7071068f, 0, 0.7071068f)};
	for (const QAngle& a : Angles())
	{
		Quaternion q;
		AngleQuaternion(a, q);
		out.push_back(q);
	}
	return out;
}

std::vector<VMatrix> VMatrices()
{
	std::vector<VMatrix> out;
	VMatrix m;
	MatrixSetIdentity(m);
	out.push_back(m);
	// A singular one (MatrixInverseGeneral's refusal) and a near-singular one.
	out.push_back(VMatrix(1, 2, 3, 4, 2, 4, 6, 8, 0, 0, 1, 0, 0, 0, 0, 1));
	out.push_back(VMatrix(1e-6f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
	// A pivot just above the 0.00001 refusal: inverted, not refused.
	out.push_back(VMatrix(5e-5f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
	Inputs rng;
	for (int i = 0; i < 2000; ++i)
	{
		for (int r = 0; r < 4; ++r)
			for (int c = 0; c < 4; ++c)
				m[r][c] = rng.Uniform(-10, 10);
		out.push_back(m);
	}
	return out;
}

::testing::AssertionResult SameVMatrix(const VMatrix& port, const VMatrix& stock, const char* what, int index)
{
	return SameBits(port.Base(), stock.Base(), 16, what, index);
}

::testing::AssertionResult SameFrustum(const Frustum_t& port, const Frustum_t& stock, const char* what, int index)
{
	for (int p = 0; p < FRUSTUM_NUMPLANES; ++p)
	{
		const cplane_t &a = *port.GetPlane(p), &b = *stock.GetPlane(p);
		::testing::AssertionResult r = SameBits(a.normal.Base(), b.normal.Base(), 3, what, index);
		if (!r)
			return r;
		r = SameBits(&a.dist, &b.dist, 1, what, index);
		if (!r)
			return r;
		r = SameBits(port.GetAbsNormal(p).Base(), stock.GetAbsNormal(p).Base(), 3, what, index);
		if (!r)
			return r;
		if (a.type != b.type || a.signbits != b.signbits)
			return ::testing::AssertionFailure()
				<< what << " input #" << index << " plane " << p << ": type/signbits differ";
	}
	return ::testing::AssertionSuccess();
}

} // namespace

TEST(MathlibPortStock, CrossProductRotateAndColumnsMatchStock)
{
	const std::vector<Vector> v = Vectors();
	const std::vector<matrix3x4_t> mats = Matrices();
	for (int i = 0; i + 1 < (int)v.size(); ++i)
	{
		Vector p, s;
		ss_mathlib::CrossProduct(v[i].Base(), v[i + 1].Base(), p.Base());
		CrossProduct(v[i].Base(), v[i + 1].Base(), s.Base());
		ASSERT_TRUE(SameBits(p.Base(), s.Base(), 3, "CrossProduct", i));

		const matrix3x4_t& m = mats[i % mats.size()];
		ss_mathlib::VectorRotate(v[i].Base(), m, p.Base());
		VectorRotate(v[i].Base(), m, s.Base());
		ASSERT_TRUE(SameBits(p.Base(), s.Base(), 3, "VectorRotate", i));

		matrix3x4_t pm = m, sm = m;
		ss_mathlib::MatrixSetColumn(v[i], i % 4, pm);
		MatrixSetColumn(v[i], i % 4, sm);
		ASSERT_TRUE(SameMatrix(pm, sm, "MatrixSetColumn", i));
		ss_mathlib::MatrixCopy(m, pm);
		MatrixCopy(m, sm);
		ASSERT_TRUE(SameMatrix(pm, sm, "MatrixCopy", i));

		Vector pf, pr, pu, sf, sr, su;
		ss_mathlib::MatrixVectors(m, &pf, &pr, &pu);
		MatrixVectors(m, &sf, &sr, &su);
		ASSERT_TRUE(SameBits(pr.Base(), sr.Base(), 3, "MatrixVectors", i));
	}
}

TEST(MathlibPortStock, MatrixInvertMatchesStockInAndOutOfPlace)
{
	const std::vector<matrix3x4_t> mats = Matrices();
	for (int i = 0; i < (int)mats.size(); ++i)
	{
		matrix3x4_t p, s;
		ss_mathlib::MatrixInvert(mats[i], p);
		MatrixInvert(mats[i], s);
		ASSERT_TRUE(SameMatrix(p, s, "MatrixInvert", i));
		p = mats[i];
		s = mats[i];
		ss_mathlib::MatrixInvert(p, p);
		MatrixInvert(s, s);
		ASSERT_TRUE(SameMatrix(p, s, "MatrixInvert(in place)", i));
	}
}

TEST(MathlibPortStock, AngleIMatrixMatchesStock)
{
	const std::vector<QAngle> angles = Angles();
	for (int i = 0; i < (int)angles.size(); ++i)
	{
		matrix3x4_t p, s;
		ss_mathlib::AngleIMatrix(angles[i], p);
		AngleIMatrix(angles[i], s);
		ASSERT_TRUE(SameMatrix(p, s, "AngleIMatrix", i));
		const Vector at(1.5f * i, -2.5f * i, 3.5f);
		ss_mathlib::AngleIMatrix(angles[i], at, p);
		AngleIMatrix(angles[i], at, s);
		ASSERT_TRUE(SameMatrix(p, s, "AngleIMatrix(position)", i));
	}
}

TEST(MathlibPortStock, ConcatTransformsMatchesStockIncludingNegativeZeros)
{
	const std::vector<matrix3x4_t> mats = Matrices();
	int negativeZeros = 0;
	for (int i = 0; i + 1 < (int)mats.size(); ++i)
	{
		matrix3x4_t p, s;
		ss_mathlib::ConcatTransforms(mats[i], mats[i + 1], p);
		ConcatTransforms(mats[i], mats[i + 1], s);
		ASSERT_TRUE(SameMatrix(p, s, "ConcatTransforms", i));
	}
	// A product that sums to -0: stock's vector add makes it +0.
	matrix3x4_t a(-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0), b(0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
	matrix3x4_t p, s;
	ss_mathlib::ConcatTransforms(a, b, p);
	ConcatTransforms(a, b, s);
	EXPECT_TRUE(SameMatrix(p, s, "ConcatTransforms(-0)", 0));
	for (int k = 0; k < 12; ++k)
		negativeZeros += (Bits(((float*)a.Base())[k] * 0.0f) == 0x80000000u) ? 1 : 0;
	EXPECT_GT(negativeZeros, 0); // the stimulus had a negative product
}

TEST(MathlibPortStock, QuaternionsMatchStock)
{
	const std::vector<Quaternion> qs = Quaternions();
	for (int i = 0; i < (int)qs.size(); ++i)
	{
		matrix3x4_t pm, sm;
		ss_mathlib::QuaternionMatrix(qs[i], pm);
		QuaternionMatrix(qs[i], sm);
		ASSERT_TRUE(SameMatrix(pm, sm, "QuaternionMatrix", i));
		QAngle pa, sa;
		ss_mathlib::QuaternionAngles(qs[i], pa);
		QuaternionAngles(qs[i], sa);
		ASSERT_TRUE(SameBits(pa.Base(), sa.Base(), 3, "QuaternionAngles", i));
	}
	const std::vector<QAngle> angles = Angles();
	for (int i = 0; i < (int)angles.size(); ++i)
	{
		Quaternion p, s;
		ss_mathlib::AngleQuaternion(angles[i], p);
		AngleQuaternion(angles[i], s);
		ASSERT_TRUE(SameBits(p.Base(), s.Base(), 4, "AngleQuaternion", i));
		Vector f, r, u;
		AngleVectors(angles[i], &f, &r, &u);
		ss_mathlib::BasisToQuaternion(f, r, u, p);
		BasisToQuaternion(f, r, u, s);
		ASSERT_TRUE(SameBits(p.Base(), s.Base(), 4, "BasisToQuaternion", i));
	}
}

TEST(MathlibPortStock, VectorVectorsAndThreeArgumentVectorAnglesMatchStock)
{
	const std::vector<Vector> v = Vectors();
	for (int i = 0; i + 1 < (int)v.size(); ++i)
	{
		Vector pr, pu, sr, su;
		ss_mathlib::VectorVectors(v[i], pr, pu);
		VectorVectors(v[i], sr, su);
		ASSERT_TRUE(SameBits(pr.Base(), sr.Base(), 3, "VectorVectors(right)", i));
		ASSERT_TRUE(SameBits(pu.Base(), su.Base(), 3, "VectorVectors(up)", i));
		QAngle pa, sa;
		ss_mathlib::VectorAngles(v[i], v[i + 1], pa);
		VectorAngles(v[i], v[i + 1], sa);
		ASSERT_TRUE(SameBits(pa.Base(), sa.Base(), 3, "VectorAngles(pseudoup)", i));
	}
}

TEST(MathlibPortStock, BoundsMatchStock)
{
	const std::vector<Vector> v = Vectors();
	Vector pmin, pmax, smin, smax;
	ss_mathlib::ClearBounds(pmin, pmax);
	ClearBounds(smin, smax);
	ASSERT_TRUE(SameBits(pmin.Base(), smin.Base(), 3, "ClearBounds", 0));
	ASSERT_TRUE(SameBits(pmax.Base(), smax.Base(), 3, "ClearBounds", 0));
	for (int i = 0; i < (int)v.size(); ++i)
	{
		ss_mathlib::AddPointToBounds(v[i], pmin, pmax);
		AddPointToBounds(v[i], smin, smax);
		ASSERT_TRUE(SameBits(pmin.Base(), smin.Base(), 3, "AddPointToBounds", i));
		ASSERT_TRUE(SameBits(pmax.Base(), smax.Base(), 3, "AddPointToBounds", i));
	}
	const std::vector<matrix3x4_t> mats = Matrices();
	for (int i = 0; i + 1 < (int)v.size(); ++i)
	{
		const Vector lo = v[i].Min(v[i + 1]), hi = v[i].Max(v[i + 1]);
		Vector p0, p1, s0, s1;
		ss_mathlib::TransformAABB(mats[i % mats.size()], lo, hi, p0, p1);
		TransformAABB(mats[i % mats.size()], lo, hi, s0, s1);
		ASSERT_TRUE(SameBits(p0.Base(), s0.Base(), 3, "TransformAABB(mins)", i));
		ASSERT_TRUE(SameBits(p1.Base(), s1.Base(), 3, "TransformAABB(maxs)", i));
	}
}

TEST(MathlibPortStock, ApproachAndCatmullRomMatchStock)
{
	Inputs rng;
	const std::vector<Vector> v = Vectors();
	for (int i = 0; i + 3 < (int)v.size(); ++i)
	{
		const float target = rng.Uniform(-10, 10), value = rng.Uniform(-10, 10), speed = rng.Uniform(-1, 5);
		const float p = ss_mathlib::Approach(target, value, speed), s = Approach(target, value, speed);
		ASSERT_TRUE(SameBits(&p, &s, 1, "Approach", i));
		const float t = rng.Uniform(0, 1);
		Vector po, so;
		ss_mathlib::Catmull_Rom_Spline(v[i], v[i + 1], v[i + 2], v[i + 3], t, po);
		Catmull_Rom_Spline(v[i], v[i + 1], v[i + 2], v[i + 3], t, so);
		ASSERT_TRUE(SameBits(po.Base(), so.Base(), 3, "Catmull_Rom_Spline", i));
	}
}

TEST(MathlibPortStock, BoxOnPlaneSideMatchesStockForEveryPlaneKind)
{
	Inputs rng;
	const std::vector<Vector> v = Vectors();
	int kinds[11] = {};
	for (int i = 0; i + 1 < (int)v.size(); ++i)
	{
		const Vector lo = v[i].Min(v[i + 1]), hi = v[i].Max(v[i + 1]);
		cplane_t plane;
		plane.normal = Vector(rng.Uniform(-1, 1), rng.Uniform(-1, 1), rng.Uniform(-1, 1));
		plane.dist = rng.Uniform(-4096, 4096);
		plane.type = i % 4 == 0 ? (i / 4) % 3 : PLANE_ANYZ;
		plane.signbits = SignbitsForPlane(&plane);
		ASSERT_EQ(ss_mathlib::SignbitsForPlane(&plane), plane.signbits) << i;
		ASSERT_EQ(ss_mathlib::BoxOnPlaneSide(&lo.x, &hi.x, &plane), BoxOnPlaneSide(&lo.x, &hi.x, &plane)) << i;
		kinds[plane.type < 3 ? plane.type : 3 + plane.signbits]++;
	}
	for (int k = 0; k < 11; ++k)
		EXPECT_GT(kinds[k], 0) << "plane kind " << k << " was never tested";
}

TEST(MathlibPortStock, FrustumsMatchStock)
{
	Inputs rng;
	const std::vector<QAngle> angles = Angles();
	const std::vector<Vector> v = Vectors();
	for (int i = 0; i < (int)angles.size(); ++i)
	{
		const float fov = i % 7 == 0 ? 200.0f : rng.Uniform(10, 170), aspect = rng.Uniform(0.5f, 2.5f);
		const float pf = ss_mathlib::CalcFovY(fov, aspect), sf = CalcFovY(fov, aspect);
		ASSERT_TRUE(SameBits(&pf, &sf, 1, "CalcFovY", i));
		Frustum_t p, s;
		ss_mathlib::GeneratePerspectiveFrustum(v[i % v.size()], angles[i], 7.0f, 28000.0f, fov, aspect, p);
		GeneratePerspectiveFrustum(v[i % v.size()], angles[i], 7.0f, 28000.0f, fov, aspect, s);
		ASSERT_TRUE(SameFrustum(p, s, "GeneratePerspectiveFrustum", i));
		const Vector lo = v[i % v.size()].Min(v[(i + 1) % v.size()]), hi = v[i % v.size()].Max(v[(i + 1) % v.size()]);
		ASSERT_EQ(ss_mathlib::R_CullBox(lo, hi, s), R_CullBox(lo, hi, s)) << i;
	}
}

TEST(MathlibPortStock, GammaAndThePower2TableMatchStock)
{
	for (int i = 0; i <= 4096; ++i)
	{
		const float x = i / 4096.0f;
		const float p = ss_mathlib::LinearToGammaFullRange(x), s = LinearToGammaFullRange(x);
		ASSERT_TRUE(SameBits(&p, &s, 1, "LinearToGammaFullRange", i));
	}
	for (int i = 0; i < 256; ++i)
	{
		const float p = ss_mathlib::kPower2N.v[i];
		ASSERT_TRUE(SameBits(&p, &power2_n[i], 1, "power2_n", i));
	}
}

TEST(MathlibPortStock, VMatrixMembersMatchStock)
{
	const std::vector<VMatrix> ms = VMatrices();
	const std::vector<Vector> v = Vectors();
	int refused = 0;
	for (int i = 0; i + 1 < (int)ms.size(); ++i)
	{
		VMatrix p, s;
		ss_mathlib::MatrixMultiply(ms[i], ms[i + 1], p);
		MatrixMultiply(ms[i], ms[i + 1], s);
		ASSERT_TRUE(SameVMatrix(p, s, "MatrixMultiply", i));
		ASSERT_TRUE(SameVMatrix(ms[i] * ms[i + 1], s, "VMatrix::operator*", i));

		ss_mathlib::MatrixTranspose(ms[i], p);
		MatrixTranspose(ms[i], s);
		ASSERT_TRUE(SameVMatrix(p, s, "MatrixTranspose", i));
		ASSERT_TRUE(SameVMatrix(ss_mathlib::Transposed(ms[i]), ms[i].Transpose(), "VMatrix::Transpose", i));

		p = ms[i + 1];
		s = ms[i + 1];
		const bool pok = ss_mathlib::MatrixInverseGeneral(ms[i], p), sok = MatrixInverseGeneral(ms[i], s);
		ASSERT_EQ(pok, sok) << i;
		ASSERT_TRUE(SameVMatrix(p, s, "MatrixInverseGeneral", i));
		refused += pok ? 0 : 1;

		const Vector& x = v[i % v.size()];
		Vector pv, sv;
		ss_mathlib::Vector3DMultiply(ms[i], x, pv);
		Vector3DMultiply(ms[i], x, sv);
		ASSERT_TRUE(SameBits(pv.Base(), sv.Base(), 3, "Vector3DMultiply", i));
		ss_mathlib::Vector3DMultiplyPositionProjective(ms[i], x, pv);
		Vector3DMultiplyPositionProjective(ms[i], x, sv);
		ASSERT_TRUE(SameBits(pv.Base(), sv.Base(), 3, "Vector3DMultiplyPositionProjective", i));

		Vector pmin, pmax, smin, smax;
		ss_mathlib::CalculateAABBFromProjectionMatrix(ms[i], &pmin, &pmax);
		CalculateAABBFromProjectionMatrix(ms[i], &smin, &smax);
		ASSERT_TRUE(SameBits(pmin.Base(), smin.Base(), 3, "CalculateAABBFromProjectionMatrix", i));
		ASSERT_TRUE(SameBits(pmax.Base(), smax.Base(), 3, "CalculateAABBFromProjectionMatrix", i));

		const float a = 10.0f + (i % 150), b = 10.0f + ((i * 7) % 150);
		ss_mathlib::MatrixBuildPerspective(p, a, b, 4.0f, 2000.0f + i);
		MatrixBuildPerspective(s, a, b, 4.0f, 2000.0f + i);
		ASSERT_TRUE(SameVMatrix(p, s, "MatrixBuildPerspective", i));
		ss_mathlib::MatrixBuildScale(p, x.x, x.y, x.z);
		MatrixBuildScale(s, x.x, x.y, x.z);
		ASSERT_TRUE(SameVMatrix(p, s, "MatrixBuildScale", i));
	}
	VMatrix p, s;
	ss_mathlib::MatrixSetIdentity(p);
	MatrixSetIdentity(s);
	EXPECT_TRUE(SameVMatrix(p, s, "MatrixSetIdentity", 0));
	EXPECT_GT(refused, 0); // the singular input reached MatrixInverseGeneral's refusal
}
