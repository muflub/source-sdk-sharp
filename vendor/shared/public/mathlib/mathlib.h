//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "minmax.h"

#include "mathlib/vector.h"

// For MMX intrinsics

// XXX remove me
#undef clamp

// Uncomment this to enable FP exceptions in parts of the code.
// This can help track down FP bugs. However the code is not
// FP exception clean so this not a turnkey operation.
//#define FP_EXCEPTIONS_ENABLED

// FPExceptionDisabler and FPExceptionEnabler taken from my blog post
// at http://www.altdevblogaday.com/2012/04/20/exceptional-floating-point/

// Declare an object of this type in a scope in order to suppress
// all floating-point exceptions temporarily. The old exception
// state will be reset at the end.
class FPExceptionDisabler
{
public:
	FPExceptionDisabler() {}

	~FPExceptionDisabler() {}

private:
	// Make the copy constructor and assignment operator private
	// and unimplemented to prohibit copying.
	FPExceptionDisabler(const FPExceptionDisabler&);
};

// Declare an object of this type in a scope in order to enable a
// specified set of floating-point exceptions temporarily. The old
// exception state will be reset at the end.
// This class can be nested.
class FPExceptionEnabler
{
public:
	// Overflow, divide-by-zero, and invalid-operation are the FP
	// exceptions most frequently associated with bugs.
	FPExceptionEnabler(unsigned int enableBits = 0) {}

	~FPExceptionEnabler() {}

private:
	// Make the copy constructor and assignment operator private
	// and unimplemented to prohibit copying.
	FPExceptionEnabler(const FPExceptionEnabler&);
};

#ifdef DEBUG // stop crashing edit-and-continue
FORCEINLINE float clamp(float val, float minVal, float maxVal)
{
	if (maxVal < minVal)
		return maxVal;
	else if (val < minVal)
		return minVal;
	else if (val > maxVal)
		return maxVal;
	else
		return val;
}
#else // DEBUG
FORCEINLINE float clamp(float val, float minVal, float maxVal)
{
#if defined(PLATFORM_INTEL)
	_mm_store_ss(&val, _mm_min_ss(_mm_max_ss(_mm_load_ss(&val), _mm_load_ss(&minVal)), _mm_load_ss(&maxVal)));
#else
	val = fpmax(minVal, val);
	val = fpmin(maxVal, val);
#endif
	return val;
}
#endif // DEBUG

// Returns a clamped value in the range [min, max].
template <class T>
inline T clamp(T const& val, T const& minVal, T const& maxVal)
{
	if (maxVal < minVal)
		return maxVal;
	else if (val < minVal)
		return minVal;
	else if (val > maxVal)
		return maxVal;
	else
		return val;
}

// plane_t structure
// !!! if this is changed, it must be changed in asm code too !!!
// FIXME: does the asm code even exist anymore?
// FIXME: this should move to a different file
struct cplane_t
{
	Vector normal;
	float dist;
	byte type; // for fast side tests
	byte signbits; // signx + (signy<<1) + (signz<<1)
	byte pad[2];
};

// structure offset for asm code

// 0-2 are axial planes
#define PLANE_X 0

// 3-5 are non-axial planes snapped to the nearest

#define PLANE_ANYZ 5

// Frustum plane indices.
// WARNING: there is code that depends on these values

enum
{
	FRUSTUM_RIGHT = 0,
	FRUSTUM_LEFT = 1,
	FRUSTUM_TOP = 2,
	FRUSTUM_BOTTOM = 3,
	FRUSTUM_NEARZ = 4,
	FRUSTUM_FARZ = 5,
	FRUSTUM_NUMPLANES = 6
};

extern int SignbitsForPlane(cplane_t* out);

class Frustum_t
{
public:
	void SetPlane(int i, int nType, const Vector& vecNormal, float dist)
	{
		m_Plane[i].normal = vecNormal;
		m_Plane[i].dist = dist;
		m_Plane[i].type = nType;
		m_Plane[i].signbits = SignbitsForPlane(&m_Plane[i]);
		m_AbsNormal[i].Init(fabs(vecNormal.x), fabs(vecNormal.y), fabs(vecNormal.z));
	}

	inline const cplane_t* GetPlane(int i) const { return &m_Plane[i]; }

private:
	cplane_t m_Plane[FRUSTUM_NUMPLANES];
	Vector m_AbsNormal[FRUSTUM_NUMPLANES];
};

// Computes Y fov from an X fov and a screen aspect ratio + X from Y
float CalcFovY(float flFovX, float flScreenAspect);

void GeneratePerspectiveFrustum(const Vector& origin, const Vector& forward, const Vector& right, const Vector& up,
								float flZNear, float flZFar, float flFovX, float flFovY, Frustum_t& frustum);

struct matrix3x4_t
{
	matrix3x4_t() = default;

	matrix3x4_t(float m00, float m01, float m02, float m03, float m10, float m11, float m12, float m13, float m20,
				float m21, float m22, float m23)
	{
		m_flMatVal[0][0] = m00;
		m_flMatVal[0][1] = m01;
		m_flMatVal[0][2] = m02;
		m_flMatVal[0][3] = m03;
		m_flMatVal[1][0] = m10;
		m_flMatVal[1][1] = m11;
		m_flMatVal[1][2] = m12;
		m_flMatVal[1][3] = m13;
		m_flMatVal[2][0] = m20;
		m_flMatVal[2][1] = m21;
		m_flMatVal[2][2] = m22;
		m_flMatVal[2][3] = m23;
	}

	// Creates a matrix where the X axis = forward
	// the Y axis = left, and the Z axis = up
	void Init(const Vector& xAxis, const Vector& yAxis, const Vector& zAxis, const Vector& vecOrigin)
	{
		m_flMatVal[0][0] = xAxis.x;
		m_flMatVal[0][1] = yAxis.x;
		m_flMatVal[0][2] = zAxis.x;
		m_flMatVal[0][3] = vecOrigin.x;
		m_flMatVal[1][0] = xAxis.y;
		m_flMatVal[1][1] = yAxis.y;
		m_flMatVal[1][2] = zAxis.y;
		m_flMatVal[1][3] = vecOrigin.y;
		m_flMatVal[2][0] = xAxis.z;
		m_flMatVal[2][1] = yAxis.z;
		m_flMatVal[2][2] = zAxis.z;
		m_flMatVal[2][3] = vecOrigin.z;
	}

	// Creates a matrix where the X axis = forward
	// the Y axis = left, and the Z axis = up
	matrix3x4_t(const Vector& xAxis, const Vector& yAxis, const Vector& zAxis, const Vector& vecOrigin)
	{
		Init(xAxis, yAxis, zAxis, vecOrigin);
	}

	float* operator[](int i)
	{
		Assert((i >= 0) && (i < 3));
		return m_flMatVal[i];
	}

	const float* operator[](int i) const
	{
		Assert((i >= 0) && (i < 3));
		return m_flMatVal[i];
	}

	float* Base() { return &m_flMatVal[0][0]; }

	const float* Base() const { return &m_flMatVal[0][0]; }

	float m_flMatVal[3][4];
};

#ifndef M_PI
#define M_PI 3.14159265358979323846 // matches value in gcc v2 math.h
#endif

#define M_PI_F ((float)(M_PI)) // Shouldn't collide with anything.

// NJS: Inlined to prevent floats from being autopromoted to doubles, as with the old system.
#ifndef RAD2DEG
#define RAD2DEG(x) ((float)(x) * (float)(180.f / M_PI_F))
#endif

#ifndef DEG2RAD
#define DEG2RAD(x) ((float)(x) * (float)(M_PI_F / 180.f))
#endif

// Used to represent sides of things like planes.
#define SIDE_FRONT 0
#define SIDE_BACK 1
#define SIDE_ON 2
// necessary for polylib.c

// necessary for vvis (flow.c) -- again look into moving later!
#define EQUAL_EPSILON 0.001 // necessary for vbsp (faces.c) -- should look into moving it there?

extern bool s_bMathlibInitialized;

extern const Vector vec3_origin;
extern const QAngle vec3_angle;
extern const int nanmask;

#define IS_NAN(x) (((*(int*)&x) & nanmask) == nanmask)

FORCEINLINE vec_t DotProduct(const vec_t* v1, const vec_t* v2)
{
	return v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2];
}

FORCEINLINE void VectorCopy(const vec_t* a, vec_t* b)
{
	b[0] = a[0];
	b[1] = a[1];
	b[2] = a[2];
}

FORCEINLINE void VectorScale(const float* in, vec_t scale, float* out)
{
	out[0] = in[0] * scale;
	out[1] = in[1] * scale;
	out[2] = in[2] * scale;
}

//#define VectorMaximum(a)		( max( (a)[0], max( (a)[1], (a)[2] ) ) )

// NJS: Some functions in VBSP still need to use these for dealing with mixing vec4's and shorts with vec_t's.
// remove when no longer needed.

FORCEINLINE void VectorMAInline(const float* start, float scale, const float* direction, float* dest)
{
	dest[0] = start[0] + direction[0] * scale;
	dest[1] = start[1] + direction[1] * scale;
	dest[2] = start[2] + direction[2] * scale;
}

FORCEINLINE void VectorMAInline(const Vector& start, float scale, const Vector& direction, Vector& dest)
{
	dest.x = start.x + direction.x * scale;
	dest.y = start.y + direction.y * scale;
	dest.z = start.z + direction.z * scale;
}

FORCEINLINE void VectorMA(const Vector& start, float scale, const Vector& direction, Vector& dest)
{
	VectorMAInline(start, scale, direction, dest);
}

#define SIN_TABLE_SIZE 256
#define FTOIBIAS 12582912.f
extern float SinCosTable[SIN_TABLE_SIZE];

// return the smallest power of two >= x.
// returns 0 if x == 0 or x > 0x80000000 (ie numbers that would be negative if x was signed)
// NOTE: the old code took an int, and if you pass in an int of 0x80000000 casted to a uint,
//       you'll get 0x80000000, which is correct for uints, instead of 0, which was correct for ints
FORCEINLINE uint SmallestPowerOfTwoGreaterOrEqual(uint x)
{
	x -= 1;
	x |= x >> 1;
	x |= x >> 2;
	x |= x >> 4;
	x |= x >> 8;
	x |= x >> 16;
	return x + 1;
}

// return the largest power of two <= x. Will return 0 if passed 0
FORCEINLINE uint LargestPowerOfTwoLessThanOrEqual(uint x)
{
	if (x >= 0x80000000)
		return 0x80000000;

	return SmallestPowerOfTwoGreaterOrEqual(x + 1) >> 1;
}

int GreatestCommonDivisor(int i1, int i2);

// MOVEMENT INFO
enum
{
	PITCH = 0, // up / down
	YAW, // left / right
	ROLL // fall over
};

void MatrixAngles(const matrix3x4_t& matrix, float* angles); // !!!!
void MatrixVectors(const matrix3x4_t& matrix, Vector* pForward, Vector* pRight, Vector* pUp);
void VectorTransform(const float* in1, const matrix3x4_t& in2, float* out);
void VectorITransform(const float* in1, const matrix3x4_t& in2, float* out);
void VectorRotate(const float* in1, const matrix3x4_t& in2, float* out);
void VectorIRotate(const float* in1, const matrix3x4_t& in2, float* out);

void MatrixCopy(const matrix3x4_t& in, matrix3x4_t& out);
void MatrixInvert(const matrix3x4_t& in, matrix3x4_t& out);

void MatrixGetColumn(const matrix3x4_t& in, int column, Vector& out);
void MatrixSetColumn(const Vector& in, int column, matrix3x4_t& out);

void ConcatTransforms(const matrix3x4_t& in1, const matrix3x4_t& in2, matrix3x4_t& out);

void QuaternionSlerp(const Quaternion& p, const Quaternion& q, float t, Quaternion& qt);
void QuaternionSlerpNoAlign(const Quaternion& p, const Quaternion& q, float t, Quaternion& qt);
void QuaternionBlendNoAlign(const Quaternion& p, const Quaternion& q, float t, Quaternion& qt);
void QuaternionScale(const Quaternion& p, float t, Quaternion& q);
void QuaternionAlign(const Quaternion& p, const Quaternion& q, Quaternion& qt);
float QuaternionDotProduct(const Quaternion& p, const Quaternion& q);
void QuaternionConjugate(const Quaternion& p, Quaternion& q);
float QuaternionNormalize(Quaternion& q);
void QuaternionMult(const Quaternion& p, const Quaternion& q, Quaternion& qt);
void QuaternionMatrix(const Quaternion& q, matrix3x4_t& matrix);
void QuaternionAngles(const Quaternion& q, QAngle& angles);
void AngleQuaternion(const QAngle& angles, Quaternion& qt);
void QuaternionAngles(const Quaternion& q, RadianEuler& angles);
void AngleQuaternion(RadianEuler const& angles, Quaternion& qt);
void QuaternionAxisAngle(const Quaternion& q, Vector& axis, float& angle);

int __cdecl BoxOnPlaneSide(const float* emins, const float* emaxs, const cplane_t* plane);

inline float anglemod(float a)
{
	a = (360.f / 65536) * ((int)(a * (65536.f / 360.0f)) & 65535);
	return a;
}

// Returns A + (B-A)*flPercent.
// float Lerp( float flPercent, float A, float B );
template <class T>
FORCEINLINE T Lerp(float flPercent, T const& A, T const& B)
{
	return A + (B - A) * flPercent;
}

// 5-argument floating point linear interpolation.
// FLerp(f1,f2,i1,i2,x)=
//    f1 at x=i1
//    f2 at x=i2
//   smooth lerp between f1 and f2 at x>i1 and x<i2
//   extrapolation for x<i1 or x>i2
//   If you know a function f(x)'s value (f1) at position i1, and its value (f2) at position i2,
//   the function can be linearly interpolated with FLerp(f1,f2,i1,i2,x)
//    i2=i1 will cause a divide by zero.
static inline float FLerp(float f1, float f2, float i1, float i2, float x)
{
	return f1 + (f2 - f1) * (x - i1) / (i2 - i1);
}

// YWB:  Specialization for interpolating euler angles via quaternions...
template <>
FORCEINLINE QAngle Lerp<QAngle>(float flPercent, const QAngle& q1, const QAngle& q2)
{
	// Avoid precision errors
	if (q1 == q2)
		return q1;

	Quaternion src, dest;

	// Convert to quaternions
	AngleQuaternion(q1, src);
	AngleQuaternion(q2, dest);

	Quaternion result;

	// Slerp
	QuaternionSlerp(src, dest, flPercent, result);

	// Convert to euler
	QAngle output;
	QuaternionAngles(result, output);
	return output;
}

/// Same as swap(), but won't cause problems with std::swap
template <class T>
FORCEINLINE void V_swap(T& x, T& y)
{
	T temp = x;
	x = y;
	y = temp;
}

// number of elements in an array of static size
#define NELEMS(x) ARRAYSIZE(x)

// XYZ macro, for printf type functions - ex printf("%f %f %f",XYZ(myvector));
#define XYZ(v) (v).x, (v).y, (v).z

// FIXME: Vector versions.... the float versions will go away hopefully soon!

void AngleVectors(const QAngle& angles, Vector* forward);
void AngleVectors(const QAngle& angles, Vector* forward, Vector* right, Vector* up);
void AngleMatrix(const QAngle& angles, matrix3x4_t& mat);
void AngleMatrix(const QAngle& angles, const Vector& position, matrix3x4_t& mat);
void AngleMatrix(const RadianEuler& angles, matrix3x4_t& mat);
void AngleIMatrix(const QAngle& angles, matrix3x4_t& mat);
void VectorAngles(const Vector& forward, QAngle& angles);
void VectorVectors(const Vector& forward, Vector& right, Vector& up);
void SetIdentityMatrix(matrix3x4_t& mat);
void SetScaleMatrix(float x, float y, float z, matrix3x4_t& dst);
void MatrixBuildRotationAboutAxis(const Vector& vAxisOfRot, float angleDegrees, matrix3x4_t& dst);

inline void PositionMatrix(const Vector& position, matrix3x4_t& mat)
{
	MatrixSetColumn(position, 3, mat);
}

inline void MatrixPosition(const matrix3x4_t& matrix, Vector& position)
{
	MatrixGetColumn(matrix, 3, position);
}

inline void VectorRotate(const Vector& in1, const matrix3x4_t& in2, Vector& out)
{
	VectorRotate(&in1.x, in2, &out.x);
}

inline void VectorIRotate(const Vector& in1, const matrix3x4_t& in2, Vector& out)
{
	VectorIRotate(&in1.x, in2, &out.x);
}

inline void MatrixAngles(const matrix3x4_t& matrix, QAngle& angles)
{
	MatrixAngles(matrix, &angles.x);
}

inline void MatrixAngles(const matrix3x4_t& matrix, QAngle& angles, Vector& position)
{
	MatrixAngles(matrix, angles);
	MatrixPosition(matrix, position);
}

inline void MatrixAngles(const matrix3x4_t& matrix, RadianEuler& angles)
{
	MatrixAngles(matrix, &angles.x);

	angles.Init(DEG2RAD(angles.z), DEG2RAD(angles.x), DEG2RAD(angles.y));
}

inline void VectorTransform(const Vector& in1, const matrix3x4_t& in2, Vector& out)
{
	VectorTransform(&in1.x, in2, &out.x);
}

inline void VectorITransform(const Vector& in1, const matrix3x4_t& in2, Vector& out)
{
	VectorITransform(&in1.x, in2, &out.x);
}

/*
inline void DecomposeRotation( const matrix3x4_t &mat, Vector &out )
{
	DecomposeRotation( mat, &out.x );
}
*/

inline int BoxOnPlaneSide(const Vector& emins, const Vector& emaxs, const cplane_t* plane)
{
	return BoxOnPlaneSide(&emins.x, &emaxs.x, plane);
}

// Helpers for bounding box construction

void ClearBounds(Vector& mins, Vector& maxs);
void AddPointToBounds(const Vector& v, Vector& mins, Vector& maxs);

// COLORSPACE/GAMMA CONVERSION STUFF
void BuildGammaTable(float gamma, float texGamma, float brightness, int overbright);

// convert texture to linear 0..1 value
inline float TexLightToLinear(int c, int exponent)
{
	extern float power2_n[256];
	Assert(exponent >= -128 && exponent <= 127);
	return (float)c * power2_n[exponent + 128];
}

// compressed color format
struct ColorRGBExp32
{
	byte r, g, b;
	signed char exponent;
};

// solves for "a, b, c" where "a x^2 + b x + c = y", return true if solution exists
bool SolveInverseQuadratic(float x1, float y1, float x2, float y2, float x3, float y3, float& a, float& b, float& c);

// solves for a,b,c specified as above, except that it always creates a monotonically increasing or
// decreasing curve if the data is monotonically increasing or decreasing. In order to enforce the
// monoticity condition, it is possible that the resulting quadratic will only approximate the data
// instead of interpolating it. This code is not especially fast.
bool SolveInverseQuadraticMonotonic(float x1, float y1, float x2, float y2, float x3, float y3, float& a, float& b,
									float& c);

// rotate a vector around the Z axis (YAW)
void VectorYawRotate(const Vector& in, float flYaw, Vector& out);

// Bias takes an X value between 0 and 1 and returns another value between 0 and 1
// The curve is biased towards 0 or 1 based on biasAmt, which is between 0 and 1.
// Lower values of biasAmt bias the curve towards 0 and higher values bias it towards 1.
// For example, with biasAmt = 0.2, the curve looks like this:
// 1
// |				  *
// |				  *
// |			     *
// |			   **
// |			 **
// |	  	 ****
// |*********
// |___________________
// 0                   1
// With biasAmt = 0.8, the curve looks like this:
// 1
// | 	**************
// |  **
// | *
// | *
// |*
// |*
// |*
// |___________________
// 0                   1
// With a biasAmt of 0.5, Bias returns X.
float Bias(float x, float biasAmt);

// Gain is similar to Bias, but biasAmt biases towards or away from 0.5.
// Lower bias values bias towards 0.5 and higher bias values bias away from it.
// For example, with biasAmt = 0.2, the curve looks like this:
// 1
// | 				  *
// | 				 *
// | 				**
// |  ***************
// | **
// | *
// |*
// |___________________
// 0                   1
// With biasAmt = 0.8, the curve looks like this:
// 1
// |  		    *****
// |  		 ***
// |  		*
// | 		*
// | 		*
// |   	 ***
// |*****
// |___________________
// 0                   1
float Gain(float x, float biasAmt);

// SmoothCurve maps a 0-1 value into another 0-1 value based on a cosine wave
// where the derivatives of the function at 0 and 1 (and 0.5) are 0. This is useful for
// any fadein/fadeout effect where it should start and end smoothly.
// The curve looks like this:
// 1
// |  		**
// | 	   *  *
// | 	  *	   *
// | 	  *	   *
// | 	 *		*
// |   **		 **
// |***			   ***
// |___________________
// 0                   1
float SmoothCurve(float x);

//float ExponentialDecay( float halflife, float dt );
//float ExponentialDecay( float decayTo, float decayTime, float dt );

// hermite basis function for smooth interpolation
// Similar to Gain() above, but very cheap to call
// value should be between 0 & 1 inclusive
inline float SimpleSpline(float value)
{
	float valueSquared = value * value;

	// Nice little ease-in, ease-out spline-like curve
	return (3 * valueSquared - 2 * valueSquared * value);
}

FORCEINLINE int RoundFloatToInt(float f)
{
#if defined(PLATFORM_INTEL)
	return _mm_cvtss_si32(_mm_load_ss(&f));
#else
#error Unknown architecture
#endif
}

FORCEINLINE unsigned char RoundFloatToByte(float f)
{
	int nResult = RoundFloatToInt(f);
#ifdef Assert
	Assert((nResult & ~0xFF) == 0);
#endif
	return (unsigned char)nResult;
}

FORCEINLINE unsigned long RoundFloatToUnsignedLong(float f)
{
#if defined(PLATFORM_WINDOWS_PC64)
	uint nRet = (uint)f;
	if (nRet & 1)
	{
		if ((f - floor(f) >= 0.5))
		{
			nRet++;
		}
	}
	else
	{
		if ((f - floor(f) > 0.5))
		{
			nRet++;
		}
	}
	return nRet;
#else // PLATFORM_WINDOWS_PC64
	unsigned char nResult[8];

#if defined(_WIN32)
	__asm
	{
			fld f
			fistp       qword ptr nResult
	}
#elif POSIX
	__asm __volatile__("fistpl %0;" : "=m"(nResult) : "t"(f) : "st");
#endif

	return *((unsigned long*)nResult);
#endif // PLATFORM_WINDOWS_PC64
}

// Fast, accurate ftol:
FORCEINLINE int Float2Int(float a)
{
	// Rely on compiler to generate CVTTSS2SI on x86
	return (int)a;
}

// Over 15x faster than: (int)floor(value)
inline int Floor2Int(float a)
{
	int RetVal;
#if defined(PLATFORM_INTEL)
	// Convert to int and back, compare, subtract one if too big
	__m128 a128 = _mm_set_ss(a);
	RetVal = _mm_cvtss_si32(a128);
	__m128 rounded128 = _mm_cvt_si2ss(_mm_setzero_ps(), RetVal);
	RetVal -= _mm_comigt_ss(rounded128, a128);
#else
	RetVal = static_cast<int>(floor(a));
#endif
	return RetVal;
}

// Fast color conversion from float to unsigned char
FORCEINLINE unsigned int FastFToC(float c)
{
#if VALVE_LITTLE_ENDIAN
	// IEEE float bit manipulation works for values between [0, 1<<23)
	union
	{
		float f;
		int i;
	} convert = {c * 255.0f + (float)(1 << 23)};

	return convert.i & 255;
#else
	// consoles CPUs suffer from load-hit-store penalty
	return Float2Int(c * 255.0f);
#endif
}

// Fast conversion from float to integer with magnitude less than 2**22
FORCEINLINE int FastFloatToSmallInt(float c)
{
#if VALVE_LITTLE_ENDIAN
	// IEEE float bit manipulation works for values between [-1<<22, 1<<22)
	union
	{
		float f;
		int i;
	} convert = {c + (float)(3 << 22)};

	return (convert.i & ((1 << 23) - 1)) - (1 << 22);
#else
	// consoles CPUs suffer from load-hit-store penalty
	return Float2Int(c);
#endif
}

// Over 15x faster than: (int)ceil(value)
inline int Ceil2Int(float a)
{
	int RetVal;
#if defined(PLATFORM_INTEL)
	// Convert to int and back, compare, add one if too small
	__m128 a128 = _mm_load_ss(&a);
	RetVal = _mm_cvtss_si32(a128);
	__m128 rounded128 = _mm_cvt_si2ss(_mm_setzero_ps(), RetVal);
	RetVal += _mm_comilt_ss(rounded128, a128);
#else
	RetVal = static_cast<int>(ceil(a));
#endif
	return RetVal;
}

// Regular signed area of triangle

// This version doesn't premultiply by 0.5f, so it's the area of the rectangle instead
#define TriArea2DTimesTwo(A, B, C) (((B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x)))

extern float GammaToLinearFullRange(float gamma);
extern float LinearToGammaFullRange(float linear);

extern float SrgbGammaToLinear(float flSrgbGammaValue);

// Interpolate a Catmull-Rom spline.
// t is a [0,1] value and interpolates a curve between p2 and p3.
void Catmull_Rom_Spline(const Vector& p1, const Vector& p2, const Vector& p3, const Vector& p4, float t,
						Vector& output);

// area under the curve [0..t]
void Catmull_Rom_Spline_Integral(const Vector& p1, const Vector& p2, const Vector& p3, const Vector& p4, float t,
								 Vector& output);

// Interpolate a Hermite spline.
// t is a [0,1] value and interpolates a curve between p1 and p2 with the deltas d1 and d2.
void Hermite_Spline(const Vector& p1, const Vector& p2, const Vector& d1, const Vector& d2, float t, Vector& output);

float Hermite_Spline(float p1, float p2, float d1, float d2, float t);

float Hermite_Spline(float p0, float p1, float p2, float t);

// See http://en.wikipedia.org/wiki/Kochanek-Bartels_curves
// Tension:  -1 = Round -> 1 = Tight
// Bias:     -1 = Pre-shoot (bias left) -> 1 = Post-shoot (bias right)
// Continuity: -1 = Box corners -> 1 = Inverted corners
// If T=B=C=0 it's the same matrix as Catmull-Rom.
// If T=1 & B=C=0 it's the same as Cubic.
// If T=B=0 & C=-1 it's just linear interpolation
// See http://news.povray.org/povray.binaries.tutorials/attachment/%3CXns91B880592482seed7@povray.org%3E/Splines.bas.txt
// for example code and descriptions of various spline types...
void Kochanek_Bartels_Spline(float tension, float bias, float continuity, const Vector& p1, const Vector& p2,
							 const Vector& p3, const Vector& p4, float t, Vector& output);

// See link at Kochanek_Bartels_Spline for info on the basis matrix used
void Cubic_Spline(const Vector& p1, const Vector& p2, const Vector& p3, const Vector& p4, float t, Vector& output);

// See link at Kochanek_Bartels_Spline for info on the basis matrix used
void BSpline(const Vector& p1, const Vector& p2, const Vector& p3, const Vector& p4, float t, Vector& output);

// See link at Kochanek_Bartels_Spline for info on the basis matrix used
void Parabolic_Spline(const Vector& p1, const Vector& p2, const Vector& p3, const Vector& p4, float t, Vector& output);

// Get the minimum distance from vOrigin to the bounding box defined by [mins,maxs]
// using voronoi regions.
// 0 is returned if the origin is inside the box.
float CalcSqrDistanceToAABB(const Vector& mins, const Vector& maxs, const Vector& point);

// Get the closest point from P to the (infinite) line through vLineA and vLineB and
// calculate the shortest distance from P to the line.
// If you pass in a value for t, it will tell you the t for (A + (B-A)t) to get the closest point.
// If the closest point lies on the segment between A and B, then 0 <= t <= 1.
void CalcClosestPointOnLine(const Vector& P, const Vector& vLineA, const Vector& vLineB, Vector& vClosest,
							float* t = 0);

// The same three functions as above, except now the line is closed between A and B.
void CalcClosestPointOnLineSegment(const Vector& P, const Vector& vLineA, const Vector& vLineB, Vector& vClosest,
								   float* t = 0);

// The above functions in 2D
void CalcClosestPointOnLine2D(Vector2D const& P, Vector2D const& vLineA, Vector2D const& vLineB, Vector2D& vClosest,
							  float* t = 0);
void CalcClosestPointOnLineSegment2D(Vector2D const& P, Vector2D const& vLineA, Vector2D const& vLineB,
									 Vector2D& vClosest, float* t = 0);

// Init the mathlib
void MathLib_Init(float gamma = 2.2f, float texGamma = 2.2f, float brightness = 0.0f, int overbright = 2.0f,
				  bool bAllow3DNow = true, bool bAllowSSE = true, bool bAllowSSE2 = true, bool bAllowMMX = true);

float AngleDiff(float destAngle, float srcAngle);

// Transforms a AABB into another space; which will inherently grow the box.
void TransformAABB(const matrix3x4_t& in1, const Vector& vecMinsIn, const Vector& vecMaxsIn, Vector& vecMinsOut,
				   Vector& vecMaxsOut);

FORCEINLINE unsigned int* PackNormal_SHORT2(float nx, float ny, float nz, unsigned int* pPackedNormal,
											float binormalSign = +1.0f)
{
	// Pack a vector (ASSUMED TO BE NORMALIZED) into Jason's 4-byte (SHORT2) format.
	// This simply reconstructs Z from X & Y. It uses the sign bits of the X & Y coords
	// to reconstruct the sign of Z and, if this is a tangent vector, the sign of the
	// binormal (this is needed because tangent/binormal vectors are supposed to follow
	// UV gradients, but shaders reconstruct the binormal from the tangent and normal
	// assuming that they form a right-handed basis).

	nx += 1; // [-1,+1] -> [0,2]
	ny += 1;
	nx *= 16384.0f; // [ 0, 2] -> [0,32768]
	ny *= 16384.0f;

	// '0' and '32768' values are invalid encodings
	nx = max(nx, 1.0f); // Make sure there are no zero values
	ny = max(ny, 1.0f);
	nx = min(nx, 32767.0f); // Make sure there are no 32768 values
	ny = min(ny, 32767.0f);

	if (nz < 0.0f)
		nx = -nx; // Set the sign bit for z

	ny *= binormalSign; // Set the sign bit for the binormal (use when encoding a tangent vector)

	short sX = (short)nx; // signed short [1,32767]
	short sY = (short)ny;

	*pPackedNormal =
		(sX & 0x0000FFFF) | (sY << 16); // NOTE: The mask is necessary (if sX is negative and cast to an int...)

	return pPackedNormal;
}

FORCEINLINE unsigned int* PackNormal_SHORT2(const float* pNormal, unsigned int* pPackedNormal,
											float binormalSign = +1.0f)
{
	return PackNormal_SHORT2(pNormal[0], pNormal[1], pNormal[2], pPackedNormal, binormalSign);
}

// See: http://www.oroboro.com/rafael/docserv.php/index/programming/article/unitv2
// UBYTE4 encoding, using per-octant projection onto x+y+z=1
// Assume input vector is already unit length
// binormalSign specifies 'sign' of binormal, stored in t sign bit of tangent
// (lets the shader know whether norm/tan/bin form a right-handed basis)
// bIsTangent is used to specify which WORD of the output to store the data
// The expected usage is to call once with the normal and once with
// the tangent and binormal sign flag, bitwise OR'ing the returned DWORDs
FORCEINLINE unsigned int* PackNormal_UBYTE4(float nx, float ny, float nz, unsigned int* pPackedNormal,
											bool bIsTangent = false, float binormalSign = +1.0f)
{
	float xSign = nx < 0.0f ? -1.0f : 1.0f; // -1 or 1 sign
	float ySign = ny < 0.0f ? -1.0f : 1.0f;
	float zSign = nz < 0.0f ? -1.0f : 1.0f;
	float tSign = binormalSign;
	Assert((binormalSign == +1.0f) || (binormalSign == -1.0f));

	float xSignBit = 0.5f * (1 - xSign); // [-1,+1] -> [1,0]
	float ySignBit = 0.5f * (1 - ySign); // 1 is negative bit (like slt instruction)
	float zSignBit = 0.5f * (1 - zSign);
	float tSignBit = 0.5f * (1 - binormalSign);

	float absX = xSign * nx; // 0..1 range (abs)
	float absY = ySign * ny;
	float absZ = zSign * nz;

	float xbits = absX / (absX + absY + absZ); // Project onto x+y+z=1 plane
	float ybits = absY / (absX + absY + absZ);

	xbits *= 63; // 0..63
	ybits *= 63;

	xbits = xbits * xSign - xSignBit; // -64..63 range
	ybits = ybits * ySign - ySignBit;
	xbits += 64.0f; // 0..127 range
	ybits += 64.0f;

	xbits = xbits * zSign - zSignBit; // Negate based on z and t
	ybits = ybits * tSign - tSignBit; // -128..127 range

	xbits += 128.0f; // 0..255 range
	ybits += 128.0f;

	unsigned char cX = (unsigned char)xbits;
	unsigned char cY = (unsigned char)ybits;

	if (!bIsTangent)
		*pPackedNormal = (cX << 0) | (cY << 8); // xy for normal
	else
		*pPackedNormal = (cX << 16) | (cY << 24); // zw for tangent

	return pPackedNormal;
}

FORCEINLINE unsigned int* PackNormal_UBYTE4(const float* pNormal, unsigned int* pPackedNormal, bool bIsTangent = false,
											float binormalSign = +1.0f)
{
	return PackNormal_UBYTE4(pNormal[0], pNormal[1], pNormal[2], pPackedNormal, bIsTangent, binormalSign);
}

// Fast version of pow and log

float FastLog2(float i); // log2( i )
float FastPow2(float i); // 2^i

// For testing float equality

inline bool CloseEnough(float a, float b, float epsilon = EQUAL_EPSILON)
{
	return fabs(a - b) <= epsilon;
}

// Fast compare
// maxUlps is the maximum error in terms of Units in the Last Place. This
// specifies how big an error we are willing to accept in terms of the value
// of the least significant digit of the floating point number�s
// representation. maxUlps can also be interpreted in terms of how many
// representable floats we are willing to accept between A and B.
// This function will allow maxUlps-1 floats between A and B.
bool AlmostEqual(float a, float b, int maxUlps = 10);
