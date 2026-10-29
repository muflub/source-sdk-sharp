//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: - defines SIMD "structure of arrays" classes and functions.
#pragma once

#if defined(GNUC)
#define USE_STDC_FOR_SIMD 0
#else
#define USE_STDC_FOR_SIMD 0
#endif

// I thought about defining a class/union for the SIMD packed floats instead of using fltx4,
// but decided against it because (a) the nature of SIMD code which includes comparisons is to blur
// the relationship between packed floats and packed integer types and (b) not sure that the
// compiler would handle generating good code for the intrinsics.

#if USE_STDC_FOR_SIMD

typedef union
{
	float m128_f32[4];
	uint32 m128_u32[4];
} fltx4;

typedef fltx4 i32x4;
typedef fltx4 u32x4;

typedef fltx4 bi32x4;

#else

typedef __m128 fltx4;
typedef __m128 i32x4;
typedef __m128 u32x4;
typedef __m128 bi32x4;
typedef __m128i shortx8;

#endif

// The FLTX4 type is a fltx4 used as a parameter to a function.
// On the PC, the best way is to pass by const reference.
// The compiler will sometimes, but not always, replace a pass-by-const-ref
// explicitly use a FLTX4 as the parameter type.
typedef const fltx4& FLTX4;

// A 16-byte aligned int32 datastructure
// (for use when writing out fltx4's as SIGNED
// ints).
struct ALIGN16 intx4
{
	int32 m_i32[4];

	inline int& operator[](int which) { return m_i32[which]; }

	inline int32* Base() { return m_i32; }

	inline const int32* Base() const { return m_i32; }

} ALIGN16_POST;

// useful constants in SIMD packed float format:
// but are manufactured directly in one or two
// instructions, saving a load and possible L2
// miss.)
extern const fltx4 Four_Zeros; // 0 0 0 0
extern const fltx4 Four_Ones; // 1 1 1 1
extern const fltx4 Four_Twos; // 2 2 2 2
extern const fltx4 Four_Threes; // 3 3 3 3
extern const fltx4 Four_Fours; // guess.
extern const fltx4 Four_Point225s; // .225 .225 .225 .225
extern const fltx4 Four_PointFives; // .5 .5 .5 .5
extern const fltx4 Four_Epsilons; // FLT_EPSILON FLT_EPSILON FLT_EPSILON FLT_EPSILON
extern const fltx4 Four_2ToThe23s; // (1<<23)..

// external aligned integer constants
extern const ALIGN16 uint32 g_SIMD_clear_signmask[] ALIGN16_POST; // 0x7fffffff x 4
extern const ALIGN16 uint32 g_SIMD_signmask[] ALIGN16_POST; // 0x80000000 x 4
extern const ALIGN16 uint32 g_SIMD_lsbmask[] ALIGN16_POST; // 0xfffffffe x 4
extern const ALIGN16 uint32 g_SIMD_clear_wmask[] ALIGN16_POST; // -1 -1 -1 0
extern const ALIGN16 uint32 g_SIMD_ComponentMask
	[4][4] ALIGN16_POST; // [0xFFFFFFFF 0 0 0], [0 0xFFFFFFFF 0 0], [0 0 0xFFFFFFFF 0], [0 0 0 0xFFFFFFFF]

// Define prefetch macros.
// The characteristics of cache and prefetch are completely
// different between the different platforms, so you DO NOT
// want to just define one macro that maps to every platform
// intrinsic under the hood -- you need to prefetch at different
// intervals between x86 and PPC, for example, and that is
// a higher level code change.
// all over the place, so this is just a nop on Intel, PS3.
// nothing

#if USE_STDC_FOR_SIMD

// Standard C (fallback/Linux) implementation (only there for compat - slow)

FORCEINLINE float SubFloat(const fltx4& a, int idx)
{
	return a.m128_f32[idx];
}

FORCEINLINE float& SubFloat(fltx4& a, int idx)
{
	return a.m128_f32[idx];
}

FORCEINLINE uint32 SubInt(const fltx4& a, int idx)
{
	return a.m128_u32[idx];
}

FORCEINLINE uint32& SubInt(fltx4& a, int idx)
{
	return a.m128_u32[idx];
}

FORCEINLINE fltx4 LoadZeroSIMD()
{
	return Four_Zeros;
}

FORCEINLINE fltx4 LoadOneSIMD()
{
	return Four_Ones;
}

FORCEINLINE fltx4 SplatXSIMD(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = SubFloat(a, 0);
	SubFloat(retVal, 1) = SubFloat(a, 0);
	SubFloat(retVal, 2) = SubFloat(a, 0);
	SubFloat(retVal, 3) = SubFloat(a, 0);
	return retVal;
}

FORCEINLINE fltx4 SplatYSIMD(fltx4 a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = SubFloat(a, 1);
	SubFloat(retVal, 1) = SubFloat(a, 1);
	SubFloat(retVal, 2) = SubFloat(a, 1);
	SubFloat(retVal, 3) = SubFloat(a, 1);
	return retVal;
}

FORCEINLINE fltx4 SplatZSIMD(fltx4 a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = SubFloat(a, 2);
	SubFloat(retVal, 1) = SubFloat(a, 2);
	SubFloat(retVal, 2) = SubFloat(a, 2);
	SubFloat(retVal, 3) = SubFloat(a, 2);
	return retVal;
}

FORCEINLINE fltx4 SplatWSIMD(fltx4 a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = SubFloat(a, 3);
	SubFloat(retVal, 1) = SubFloat(a, 3);
	SubFloat(retVal, 2) = SubFloat(a, 3);
	SubFloat(retVal, 3) = SubFloat(a, 3);
	return retVal;
}

FORCEINLINE fltx4 SetXSIMD(const fltx4& a, const fltx4& x)
{
	fltx4 result = a;
	SubFloat(result, 0) = SubFloat(x, 0);
	return result;
}

FORCEINLINE fltx4 SetYSIMD(const fltx4& a, const fltx4& y)
{
	fltx4 result = a;
	SubFloat(result, 1) = SubFloat(y, 1);
	return result;
}

FORCEINLINE fltx4 SetZSIMD(const fltx4& a, const fltx4& z)
{
	fltx4 result = a;
	SubFloat(result, 2) = SubFloat(z, 2);
	return result;
}

FORCEINLINE fltx4 SetWSIMD(const fltx4& a, const fltx4& w)
{
	fltx4 result = a;
	SubFloat(result, 3) = SubFloat(w, 3);
	return result;
}

FORCEINLINE fltx4 SetComponentSIMD(const fltx4& a, int nComponent, float flValue)
{
	fltx4 result = a;
	SubFloat(result, nComponent) = flValue;
	return result;
}

// a b c d -> b c d a
FORCEINLINE fltx4 RotateLeft(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = SubFloat(a, 1);
	SubFloat(retVal, 1) = SubFloat(a, 2);
	SubFloat(retVal, 2) = SubFloat(a, 3);
	SubFloat(retVal, 3) = SubFloat(a, 0);
	return retVal;
}

// a b c d -> c d a b
FORCEINLINE fltx4 RotateLeft2(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = SubFloat(a, 2);
	SubFloat(retVal, 1) = SubFloat(a, 3);
	SubFloat(retVal, 2) = SubFloat(a, 0);
	SubFloat(retVal, 3) = SubFloat(a, 1);
	return retVal;
}

#define BINOP(op)                                             \
	fltx4 retVal;                                             \
	SubFloat(retVal, 0) = (SubFloat(a, 0) op SubFloat(b, 0)); \
	SubFloat(retVal, 1) = (SubFloat(a, 1) op SubFloat(b, 1)); \
	SubFloat(retVal, 2) = (SubFloat(a, 2) op SubFloat(b, 2)); \
	SubFloat(retVal, 3) = (SubFloat(a, 3) op SubFloat(b, 3)); \
	return retVal;

#define IBINOP(op)                                      \
	fltx4 retVal;                                       \
	SubInt(retVal, 0) = (SubInt(a, 0) op SubInt(b, 0)); \
	SubInt(retVal, 1) = (SubInt(a, 1) op SubInt(b, 1)); \
	SubInt(retVal, 2) = (SubInt(a, 2) op SubInt(b, 2)); \
	SubInt(retVal, 3) = (SubInt(a, 3) op SubInt(b, 3)); \
	return retVal;

FORCEINLINE fltx4 AddSIMD(const fltx4& a, const fltx4& b)
{
	BINOP(+);
}

FORCEINLINE fltx4 SubSIMD(const fltx4& a, const fltx4& b) // a-b
{
	BINOP(-);
};

FORCEINLINE fltx4 MulSIMD(const fltx4& a, const fltx4& b) // a*b
{
	BINOP(*);
}

FORCEINLINE fltx4 DivSIMD(const fltx4& a, const fltx4& b) // a/b
{
	BINOP(/);
}

FORCEINLINE fltx4 MaddSIMD(const fltx4& a, const fltx4& b, const fltx4& c) // a*b + c
{
	return AddSIMD(MulSIMD(a, b), c);
}

FORCEINLINE fltx4 MsubSIMD(const fltx4& a, const fltx4& b, const fltx4& c) // c - a*b
{
	return SubSIMD(c, MulSIMD(a, b));
};

FORCEINLINE fltx4 SinSIMD(const fltx4& radians)
{
	fltx4 result;
	SubFloat(result, 0) = sin(SubFloat(radians, 0));
	SubFloat(result, 1) = sin(SubFloat(radians, 1));
	SubFloat(result, 2) = sin(SubFloat(radians, 2));
	SubFloat(result, 3) = sin(SubFloat(radians, 3));
	return result;
}

FORCEINLINE void SinCos3SIMD(fltx4& sine, fltx4& cosine, const fltx4& radians)
{
	SinCos(SubFloat(radians, 0), &SubFloat(sine, 0), &SubFloat(cosine, 0));
	SinCos(SubFloat(radians, 1), &SubFloat(sine, 1), &SubFloat(cosine, 1));
	SinCos(SubFloat(radians, 2), &SubFloat(sine, 2), &SubFloat(cosine, 2));
}

FORCEINLINE void SinCosSIMD(fltx4& sine, fltx4& cosine, const fltx4& radians)
{
	SinCos(SubFloat(radians, 0), &SubFloat(sine, 0), &SubFloat(cosine, 0));
	SinCos(SubFloat(radians, 1), &SubFloat(sine, 1), &SubFloat(cosine, 1));
	SinCos(SubFloat(radians, 2), &SubFloat(sine, 2), &SubFloat(cosine, 2));
	SinCos(SubFloat(radians, 3), &SubFloat(sine, 3), &SubFloat(cosine, 3));
}

FORCEINLINE fltx4 ArcSinSIMD(const fltx4& sine)
{
	fltx4 result;
	SubFloat(result, 0) = asin(SubFloat(sine, 0));
	SubFloat(result, 1) = asin(SubFloat(sine, 1));
	SubFloat(result, 2) = asin(SubFloat(sine, 2));
	SubFloat(result, 3) = asin(SubFloat(sine, 3));
	return result;
}

FORCEINLINE fltx4 ArcCosSIMD(const fltx4& cs)
{
	fltx4 result;
	SubFloat(result, 0) = acos(SubFloat(cs, 0));
	SubFloat(result, 1) = acos(SubFloat(cs, 1));
	SubFloat(result, 2) = acos(SubFloat(cs, 2));
	SubFloat(result, 3) = acos(SubFloat(cs, 3));
	return result;
}

// tan^1(a/b) .. ie, pass sin in as a and cos in as b
FORCEINLINE fltx4 ArcTan2SIMD(const fltx4& a, const fltx4& b)
{
	fltx4 result;
	SubFloat(result, 0) = atan2(SubFloat(a, 0), SubFloat(b, 0));
	SubFloat(result, 1) = atan2(SubFloat(a, 1), SubFloat(b, 1));
	SubFloat(result, 2) = atan2(SubFloat(a, 2), SubFloat(b, 2));
	SubFloat(result, 3) = atan2(SubFloat(a, 3), SubFloat(b, 3));
	return result;
}

FORCEINLINE fltx4 MaxSIMD(const fltx4& a, const fltx4& b) // max(a,b)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = max(SubFloat(a, 0), SubFloat(b, 0));
	SubFloat(retVal, 1) = max(SubFloat(a, 1), SubFloat(b, 1));
	SubFloat(retVal, 2) = max(SubFloat(a, 2), SubFloat(b, 2));
	SubFloat(retVal, 3) = max(SubFloat(a, 3), SubFloat(b, 3));
	return retVal;
}

FORCEINLINE fltx4 MinSIMD(const fltx4& a, const fltx4& b) // min(a,b)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = min(SubFloat(a, 0), SubFloat(b, 0));
	SubFloat(retVal, 1) = min(SubFloat(a, 1), SubFloat(b, 1));
	SubFloat(retVal, 2) = min(SubFloat(a, 2), SubFloat(b, 2));
	SubFloat(retVal, 3) = min(SubFloat(a, 3), SubFloat(b, 3));
	return retVal;
}

FORCEINLINE fltx4 AndSIMD(const fltx4& a, const fltx4& b) // a & b
{
	IBINOP(&);
}

FORCEINLINE fltx4 AndNotSIMD(const fltx4& a, const fltx4& b) // ~a & b
{
	fltx4 retVal;
	SubInt(retVal, 0) = ~SubInt(a, 0) & SubInt(b, 0);
	SubInt(retVal, 1) = ~SubInt(a, 1) & SubInt(b, 1);
	SubInt(retVal, 2) = ~SubInt(a, 2) & SubInt(b, 2);
	SubInt(retVal, 3) = ~SubInt(a, 3) & SubInt(b, 3);
	return retVal;
}

FORCEINLINE fltx4 XorSIMD(const fltx4& a, const fltx4& b) // a ^ b
{
	IBINOP(^);
}

FORCEINLINE fltx4 OrSIMD(const fltx4& a, const fltx4& b) // a | b
{
	IBINOP(|);
}

FORCEINLINE fltx4 NegSIMD(const fltx4& a) // negate: -a
{
	fltx4 retval;
	SubFloat(retval, 0) = -SubFloat(a, 0);
	SubFloat(retval, 1) = -SubFloat(a, 1);
	SubFloat(retval, 2) = -SubFloat(a, 2);
	SubFloat(retval, 3) = -SubFloat(a, 3);

	return retval;
}

FORCEINLINE bool IsAllZeros(const fltx4& a) // all floats of a zero?
{
	return (SubFloat(a, 0) == 0.0) && (SubFloat(a, 1) == 0.0) && (SubFloat(a, 2) == 0.0) && (SubFloat(a, 3) == 0.0);
}

// for branching when a.xyzw > b.xyzw
FORCEINLINE bool IsAllGreaterThan(const fltx4& a, const fltx4& b)
{
	return SubFloat(a, 0) > SubFloat(b, 0) && SubFloat(a, 1) > SubFloat(b, 1) && SubFloat(a, 2) > SubFloat(b, 2) &&
		SubFloat(a, 3) > SubFloat(b, 3);
}

// for branching when a.xyzw >= b.xyzw
FORCEINLINE bool IsAllGreaterThanOrEq(const fltx4& a, const fltx4& b)
{
	return SubFloat(a, 0) >= SubFloat(b, 0) && SubFloat(a, 1) >= SubFloat(b, 1) && SubFloat(a, 2) >= SubFloat(b, 2) &&
		SubFloat(a, 3) >= SubFloat(b, 3);
}

// For branching if all a.xyzw == b.xyzw
FORCEINLINE bool IsAllEqual(const fltx4& a, const fltx4& b)
{
	return SubFloat(a, 0) == SubFloat(b, 0) && SubFloat(a, 1) == SubFloat(b, 1) && SubFloat(a, 2) == SubFloat(b, 2) &&
		SubFloat(a, 3) == SubFloat(b, 3);
}

FORCEINLINE int TestSignSIMD(const fltx4& a) // mask of which floats have the high bit set
{
	int nRet = 0;

	nRet |= (SubInt(a, 0) & 0x80000000) >> 31; // sign(x) -> bit 0
	nRet |= (SubInt(a, 1) & 0x80000000) >> 30; // sign(y) -> bit 1
	nRet |= (SubInt(a, 2) & 0x80000000) >> 29; // sign(z) -> bit 2
	nRet |= (SubInt(a, 3) & 0x80000000) >> 28; // sign(w) -> bit 3

	return nRet;
}

FORCEINLINE bool IsAnyNegative(const fltx4& a) // (a.x < 0) || (a.y < 0) || (a.z < 0) || (a.w < 0)
{
	return (0 != TestSignSIMD(a));
}

FORCEINLINE fltx4 CmpEqSIMD(const fltx4& a, const fltx4& b) // (a==b) ? ~0:0
{
	fltx4 retVal;
	SubInt(retVal, 0) = (SubFloat(a, 0) == SubFloat(b, 0)) ? ~0 : 0;
	SubInt(retVal, 1) = (SubFloat(a, 1) == SubFloat(b, 1)) ? ~0 : 0;
	SubInt(retVal, 2) = (SubFloat(a, 2) == SubFloat(b, 2)) ? ~0 : 0;
	SubInt(retVal, 3) = (SubFloat(a, 3) == SubFloat(b, 3)) ? ~0 : 0;
	return retVal;
}

FORCEINLINE fltx4 CmpGtSIMD(const fltx4& a, const fltx4& b) // (a>b) ? ~0:0
{
	fltx4 retVal;
	SubInt(retVal, 0) = (SubFloat(a, 0) > SubFloat(b, 0)) ? ~0 : 0;
	SubInt(retVal, 1) = (SubFloat(a, 1) > SubFloat(b, 1)) ? ~0 : 0;
	SubInt(retVal, 2) = (SubFloat(a, 2) > SubFloat(b, 2)) ? ~0 : 0;
	SubInt(retVal, 3) = (SubFloat(a, 3) > SubFloat(b, 3)) ? ~0 : 0;
	return retVal;
}

FORCEINLINE fltx4 CmpGeSIMD(const fltx4& a, const fltx4& b) // (a>=b) ? ~0:0
{
	fltx4 retVal;
	SubInt(retVal, 0) = (SubFloat(a, 0) >= SubFloat(b, 0)) ? ~0 : 0;
	SubInt(retVal, 1) = (SubFloat(a, 1) >= SubFloat(b, 1)) ? ~0 : 0;
	SubInt(retVal, 2) = (SubFloat(a, 2) >= SubFloat(b, 2)) ? ~0 : 0;
	SubInt(retVal, 3) = (SubFloat(a, 3) >= SubFloat(b, 3)) ? ~0 : 0;
	return retVal;
}

FORCEINLINE fltx4 CmpLtSIMD(const fltx4& a, const fltx4& b) // (a<b) ? ~0:0
{
	fltx4 retVal;
	SubInt(retVal, 0) = (SubFloat(a, 0) < SubFloat(b, 0)) ? ~0 : 0;
	SubInt(retVal, 1) = (SubFloat(a, 1) < SubFloat(b, 1)) ? ~0 : 0;
	SubInt(retVal, 2) = (SubFloat(a, 2) < SubFloat(b, 2)) ? ~0 : 0;
	SubInt(retVal, 3) = (SubFloat(a, 3) < SubFloat(b, 3)) ? ~0 : 0;
	return retVal;
}

FORCEINLINE fltx4 CmpLeSIMD(const fltx4& a, const fltx4& b) // (a<=b) ? ~0:0
{
	fltx4 retVal;
	SubInt(retVal, 0) = (SubFloat(a, 0) <= SubFloat(b, 0)) ? ~0 : 0;
	SubInt(retVal, 1) = (SubFloat(a, 1) <= SubFloat(b, 1)) ? ~0 : 0;
	SubInt(retVal, 2) = (SubFloat(a, 2) <= SubFloat(b, 2)) ? ~0 : 0;
	SubInt(retVal, 3) = (SubFloat(a, 3) <= SubFloat(b, 3)) ? ~0 : 0;
	return retVal;
}

FORCEINLINE fltx4 CmpInBoundsSIMD(const fltx4& a, const fltx4& b) // (a <= b && a >= -b) ? ~0 : 0
{
	fltx4 retVal;
	SubInt(retVal, 0) = (SubFloat(a, 0) <= SubFloat(b, 0) && SubFloat(a, 0) >= -SubFloat(b, 0)) ? ~0 : 0;
	SubInt(retVal, 1) = (SubFloat(a, 1) <= SubFloat(b, 1) && SubFloat(a, 1) >= -SubFloat(b, 1)) ? ~0 : 0;
	SubInt(retVal, 2) = (SubFloat(a, 2) <= SubFloat(b, 2) && SubFloat(a, 2) >= -SubFloat(b, 2)) ? ~0 : 0;
	SubInt(retVal, 3) = (SubFloat(a, 3) <= SubFloat(b, 3) && SubFloat(a, 3) >= -SubFloat(b, 3)) ? ~0 : 0;
	return retVal;
}

FORCEINLINE fltx4 MaskedAssign(const fltx4& ReplacementMask, const fltx4& NewValue, const fltx4& OldValue)
{
	return OrSIMD(AndSIMD(ReplacementMask, NewValue), AndNotSIMD(ReplacementMask, OldValue));
}

// the same as MaskedAssign, only 5 instructions instead of 6
FORCEINLINE fltx4 SelectSIMD(const fltx4& ReplacementMask, const fltx4& NewValue, const fltx4& OldValue)
{
	return XorSIMD(AndSIMD(XorSIMD(NewValue, OldValue), ReplacementMask), OldValue);
}

FORCEINLINE fltx4 ReplicateX4(float flValue) //  a,a,a,a
{
	fltx4 retVal;
	SubFloat(retVal, 0) = flValue;
	SubFloat(retVal, 1) = flValue;
	SubFloat(retVal, 2) = flValue;
	SubFloat(retVal, 3) = flValue;
	return retVal;
}

/// replicate a single 32 bit integer value to all 4 components of an m128
FORCEINLINE fltx4 ReplicateIX4(int nValue)
{
	fltx4 retVal;
	SubInt(retVal, 0) = nValue;
	SubInt(retVal, 1) = nValue;
	SubInt(retVal, 2) = nValue;
	SubInt(retVal, 3) = nValue;
	return retVal;
}

// Round towards positive infinity
FORCEINLINE fltx4 CeilSIMD(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = ceil(SubFloat(a, 0));
	SubFloat(retVal, 1) = ceil(SubFloat(a, 1));
	SubFloat(retVal, 2) = ceil(SubFloat(a, 2));
	SubFloat(retVal, 3) = ceil(SubFloat(a, 3));
	return retVal;
}

// Round towards negative infinity
FORCEINLINE fltx4 FloorSIMD(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = floor(SubFloat(a, 0));
	SubFloat(retVal, 1) = floor(SubFloat(a, 1));
	SubFloat(retVal, 2) = floor(SubFloat(a, 2));
	SubFloat(retVal, 3) = floor(SubFloat(a, 3));
	return retVal;
}

FORCEINLINE fltx4 SqrtEstSIMD(const fltx4& a) // sqrt(a), more or less
{
	fltx4 retVal;
	SubFloat(retVal, 0) = sqrt(SubFloat(a, 0));
	SubFloat(retVal, 1) = sqrt(SubFloat(a, 1));
	SubFloat(retVal, 2) = sqrt(SubFloat(a, 2));
	SubFloat(retVal, 3) = sqrt(SubFloat(a, 3));
	return retVal;
}

FORCEINLINE fltx4 SqrtSIMD(const fltx4& a) // sqrt(a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = sqrt(SubFloat(a, 0));
	SubFloat(retVal, 1) = sqrt(SubFloat(a, 1));
	SubFloat(retVal, 2) = sqrt(SubFloat(a, 2));
	SubFloat(retVal, 3) = sqrt(SubFloat(a, 3));
	return retVal;
}

FORCEINLINE fltx4 ReciprocalSqrtEstSIMD(const fltx4& a) // 1/sqrt(a), more or less
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / sqrt(SubFloat(a, 0));
	SubFloat(retVal, 1) = 1.0 / sqrt(SubFloat(a, 1));
	SubFloat(retVal, 2) = 1.0 / sqrt(SubFloat(a, 2));
	SubFloat(retVal, 3) = 1.0 / sqrt(SubFloat(a, 3));
	return retVal;
}

FORCEINLINE fltx4 ReciprocalSqrtEstSaturateSIMD(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / sqrt(SubFloat(a, 0) != 0.0f ? SubFloat(a, 0) : FLT_EPSILON);
	SubFloat(retVal, 1) = 1.0 / sqrt(SubFloat(a, 1) != 0.0f ? SubFloat(a, 1) : FLT_EPSILON);
	SubFloat(retVal, 2) = 1.0 / sqrt(SubFloat(a, 2) != 0.0f ? SubFloat(a, 2) : FLT_EPSILON);
	SubFloat(retVal, 3) = 1.0 / sqrt(SubFloat(a, 3) != 0.0f ? SubFloat(a, 3) : FLT_EPSILON);
	return retVal;
}

FORCEINLINE fltx4 ReciprocalSqrtSIMD(const fltx4& a) // 1/sqrt(a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / sqrt(SubFloat(a, 0));
	SubFloat(retVal, 1) = 1.0 / sqrt(SubFloat(a, 1));
	SubFloat(retVal, 2) = 1.0 / sqrt(SubFloat(a, 2));
	SubFloat(retVal, 3) = 1.0 / sqrt(SubFloat(a, 3));
	return retVal;
}

FORCEINLINE fltx4 ReciprocalEstSIMD(const fltx4& a) // 1/a, more or less
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / SubFloat(a, 0);
	SubFloat(retVal, 1) = 1.0 / SubFloat(a, 1);
	SubFloat(retVal, 2) = 1.0 / SubFloat(a, 2);
	SubFloat(retVal, 3) = 1.0 / SubFloat(a, 3);
	return retVal;
}

FORCEINLINE fltx4 ReciprocalSIMD(const fltx4& a) // 1/a
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / SubFloat(a, 0);
	SubFloat(retVal, 1) = 1.0 / SubFloat(a, 1);
	SubFloat(retVal, 2) = 1.0 / SubFloat(a, 2);
	SubFloat(retVal, 3) = 1.0 / SubFloat(a, 3);
	return retVal;
}

/// 1/x for all 4 values.
/// 1/0 will result in a big but NOT infinite result
FORCEINLINE fltx4 ReciprocalEstSaturateSIMD(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / (SubFloat(a, 0) == 0.0f ? FLT_EPSILON : SubFloat(a, 0));
	SubFloat(retVal, 1) = 1.0 / (SubFloat(a, 1) == 0.0f ? FLT_EPSILON : SubFloat(a, 1));
	SubFloat(retVal, 2) = 1.0 / (SubFloat(a, 2) == 0.0f ? FLT_EPSILON : SubFloat(a, 2));
	SubFloat(retVal, 3) = 1.0 / (SubFloat(a, 3) == 0.0f ? FLT_EPSILON : SubFloat(a, 3));
	return retVal;
}

FORCEINLINE fltx4 ReciprocalSaturateSIMD(const fltx4& a)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = 1.0 / (SubFloat(a, 0) == 0.0f ? FLT_EPSILON : SubFloat(a, 0));
	SubFloat(retVal, 1) = 1.0 / (SubFloat(a, 1) == 0.0f ? FLT_EPSILON : SubFloat(a, 1));
	SubFloat(retVal, 2) = 1.0 / (SubFloat(a, 2) == 0.0f ? FLT_EPSILON : SubFloat(a, 2));
	SubFloat(retVal, 3) = 1.0 / (SubFloat(a, 3) == 0.0f ? FLT_EPSILON : SubFloat(a, 3));
	return retVal;
}

// 2^x for all values (the antilog)
FORCEINLINE fltx4 ExpSIMD(const fltx4& toPower)
{
	fltx4 retVal;
	SubFloat(retVal, 0) = powf(2, SubFloat(toPower, 0));
	SubFloat(retVal, 1) = powf(2, SubFloat(toPower, 1));
	SubFloat(retVal, 2) = powf(2, SubFloat(toPower, 2));
	SubFloat(retVal, 3) = powf(2, SubFloat(toPower, 3));

	return retVal;
}

FORCEINLINE fltx4 Dot3SIMD(const fltx4& a, const fltx4& b)
{
	float flDot = SubFloat(a, 0) * SubFloat(b, 0) + SubFloat(a, 1) * SubFloat(b, 1) + SubFloat(a, 2) * SubFloat(b, 2);
	return ReplicateX4(flDot);
}

FORCEINLINE fltx4 Dot4SIMD(const fltx4& a, const fltx4& b)
{
	float flDot = SubFloat(a, 0) * SubFloat(b, 0) + SubFloat(a, 1) * SubFloat(b, 1) + SubFloat(a, 2) * SubFloat(b, 2) +
		SubFloat(a, 3) * SubFloat(b, 3);
	return ReplicateX4(flDot);
}

// Clamps the components of a vector to a specified minimum and maximum range.
FORCEINLINE fltx4 ClampVectorSIMD(FLTX4 in, FLTX4 min, FLTX4 max)
{
	return MaxSIMD(min, MinSIMD(max, in));
}

// Squelch the w component of a vector to +0.0.
// Most efficient when you say a = SetWToZeroSIMD(a) (avoids a copy)
FORCEINLINE fltx4 SetWToZeroSIMD(const fltx4& a)
{
	fltx4 retval;
	retval = a;
	SubFloat(retval, 0) = 0;
	return retval;
}

FORCEINLINE fltx4 LoadUnalignedSIMD(const void* pSIMD)
{
	return *(reinterpret_cast<const fltx4*>(pSIMD));
}

FORCEINLINE fltx4 LoadUnaligned3SIMD(const void* pSIMD)
{
	return *(reinterpret_cast<const fltx4*>(pSIMD));
}

FORCEINLINE fltx4 LoadAlignedSIMD(const void* pSIMD)
{
	return *(reinterpret_cast<const fltx4*>(pSIMD));
}

// for the transitional class -- load a 3-by VectorAligned and squash its w component
FORCEINLINE fltx4 LoadAlignedSIMD(const VectorAligned& pSIMD)
{
	fltx4 retval = LoadAlignedSIMD(pSIMD.Base());
	// squelch w
	SubInt(retval, 3) = 0;
	return retval;
}

FORCEINLINE void StoreAlignedSIMD(float* pSIMD, const fltx4& a)
{
	*(reinterpret_cast<fltx4*>(pSIMD)) = a;
}

FORCEINLINE void StoreUnalignedSIMD(float* pSIMD, const fltx4& a)
{
	*(reinterpret_cast<fltx4*>(pSIMD)) = a;
}

FORCEINLINE void StoreUnaligned3SIMD(float* pSIMD, const fltx4& a)
{
	*pSIMD = SubFloat(a, 0);
	*(pSIMD + 1) = SubFloat(a, 1);
	*(pSIMD + 2) = SubFloat(a, 2);
}

// strongly typed -- syntactic castor oil used for typechecking as we transition to SIMD
FORCEINLINE void StoreAligned3SIMD(VectorAligned* RESTRICT pSIMD, const fltx4& a)
{
	StoreAlignedSIMD(pSIMD->Base(), a);
}

FORCEINLINE void TransposeSIMD(fltx4& x, fltx4& y, fltx4& z, fltx4& w)
{
#define SWAP_FLOATS(_a_, _ia_, _b_, _ib_)          \
	{                                              \
		float tmp = SubFloat(_a_, _ia_);           \
		SubFloat(_a_, _ia_) = SubFloat(_b_, _ib_); \
		SubFloat(_b_, _ib_) = tmp;                 \
	}
	SWAP_FLOATS(x, 1, y, 0);
	SWAP_FLOATS(x, 2, z, 0);
	SWAP_FLOATS(x, 3, w, 0);
	SWAP_FLOATS(y, 2, z, 1);
	SWAP_FLOATS(y, 3, w, 1);
	SWAP_FLOATS(z, 3, w, 2);
}

// find the lowest component of a.x, a.y, a.z,
// and replicate it to the whole return value.
FORCEINLINE fltx4 FindLowestSIMD3(const fltx4& a)
{
	float lowest = min(min(SubFloat(a, 0), SubFloat(a, 1)), SubFloat(a, 2));
	return ReplicateX4(lowest);
}

// find the highest component of a.x, a.y, a.z,
// and replicate it to the whole return value.
FORCEINLINE fltx4 FindHighestSIMD3(const fltx4& a)
{
	float highest = max(max(SubFloat(a, 0), SubFloat(a, 1)), SubFloat(a, 2));
	return ReplicateX4(highest);
}

// Fixed-point conversion and save as SIGNED INTS.
// pDest->x = Int (vSrc.x)
// note: some architectures have means of doing
// fixed point conversion when the fix depth is
// specified as an immediate.. but there is no way
// to guarantee an immediate as a parameter to function
// like this.
FORCEINLINE void ConvertStoreAsIntsSIMD(intx4* RESTRICT pDest, const fltx4& vSrc)
{
	(*pDest)[0] = SubFloat(vSrc, 0);
	(*pDest)[1] = SubFloat(vSrc, 1);
	(*pDest)[2] = SubFloat(vSrc, 2);
	(*pDest)[3] = SubFloat(vSrc, 3);
}

// INTEGER SIMD OPERATIONS.
// splat all components of a vector to a signed immediate int number.
FORCEINLINE fltx4 IntSetImmediateSIMD(int nValue)
{
	fltx4 retval;
	SubInt(retval, 0) = SubInt(retval, 1) = SubInt(retval, 2) = SubInt(retval, 3) = nValue;
	return retval;
}

// Load 4 aligned words into a SIMD register
FORCEINLINE i32x4 LoadAlignedIntSIMD(const void* RESTRICT pSIMD)
{
	return *(reinterpret_cast<const i32x4*>(pSIMD));
}

// Load 4 unaligned words into a SIMD register
FORCEINLINE i32x4 LoadUnalignedIntSIMD(const void* RESTRICT pSIMD)
{
	return *(reinterpret_cast<const i32x4*>(pSIMD));
}

// save into four words, 16-byte aligned
FORCEINLINE void StoreAlignedIntSIMD(int32* pSIMD, const fltx4& a)
{
	*(reinterpret_cast<i32x4*>(pSIMD)) = a;
}

FORCEINLINE void StoreAlignedIntSIMD(intx4& pSIMD, const fltx4& a)
{
	*(reinterpret_cast<i32x4*>(pSIMD.Base())) = a;
}

FORCEINLINE void StoreUnalignedIntSIMD(int32* pSIMD, const fltx4& a)
{
	*(reinterpret_cast<i32x4*>(pSIMD)) = a;
}

// Take a fltx4 containing fixed-point uints and
// return them as single precision floats. No
// fixed point conversion is done.
FORCEINLINE fltx4 UnsignedIntConvertToFltSIMD(const u32x4& vSrcA)
{
	Assert(0); /* pc has no such operation */
	fltx4 retval;
	SubFloat(retval, 0) = ((float)SubInt(vSrcA, 0));
	SubFloat(retval, 1) = ((float)SubInt(vSrcA, 1));
	SubFloat(retval, 2) = ((float)SubInt(vSrcA, 2));
	SubFloat(retval, 3) = ((float)SubInt(vSrcA, 3));
	return retval;
}

#if 0 /* pc has no such op */
// Take a fltx4 containing fixed-point sints and 
// return them as single precision floats. No 
// fixed point conversion is done.
FORCEINLINE fltx4 SignedIntConvertToFltSIMD( const i32x4 &vSrcA )
{
	fltx4 retval;
	SubFloat( retval, 0 ) = ( (float) (reinterpret_cast<int32 *>(&vSrcA.m128_s32[0])) );
	SubFloat( retval, 1 ) = ( (float) (reinterpret_cast<int32 *>(&vSrcA.m128_s32[1])) );
	SubFloat( retval, 2 ) = ( (float) (reinterpret_cast<int32 *>(&vSrcA.m128_s32[2])) );
	SubFloat( retval, 3 ) = ( (float) (reinterpret_cast<int32 *>(&vSrcA.m128_s32[3])) );
	return retval;
}

/*
  works on fltx4's as if they are four uints.
  the first parameter contains the words to be shifted,
  the second contains the amount to shift by AS INTS

  for i = 0 to 3
  shift = vSrcB_i*32:(i*32)+4
  vReturned_i*32:(i*32)+31 = vSrcA_i*32:(i*32)+31 << shift
*/
FORCEINLINE i32x4 IntShiftLeftWordSIMD(const i32x4 &vSrcA, const i32x4 &vSrcB)
{
	i32x4 retval;
	SubInt(retval, 0) = SubInt(vSrcA, 0) << SubInt(vSrcB, 0);
	SubInt(retval, 1) = SubInt(vSrcA, 1) << SubInt(vSrcB, 1);
	SubInt(retval, 2) = SubInt(vSrcA, 2) << SubInt(vSrcB, 2);
	SubInt(retval, 3) = SubInt(vSrcA, 3) << SubInt(vSrcB, 3);

	return retval;
}
#endif

#else

// Intel/SSE implementation

FORCEINLINE void StoreAlignedSIMD(float* RESTRICT pSIMD, const fltx4& a)
{
	_mm_store_ps(pSIMD, a);
}

FORCEINLINE void StoreUnalignedSIMD(float* RESTRICT pSIMD, const fltx4& a)
{
	_mm_storeu_ps(pSIMD, a);
}

FORCEINLINE fltx4 RotateLeft(const fltx4& a);
FORCEINLINE fltx4 RotateLeft2(const fltx4& a);

#define IS_FLOAT_TYPE(T) (sizeof(T) == sizeof(float) || sizeof(T) == sizeof(fltx4))

template <typename T>
FORCEINLINE fltx4 LoadAlignedSIMD(const T* pSIMD)
{
	static_assert(IS_FLOAT_TYPE(T), "You are doing something wrong.");
	return _mm_load_ps(reinterpret_cast<const float*>(pSIMD));
}

FORCEINLINE fltx4 AndSIMD(const fltx4& a, const fltx4& b) // a & b
{
	return _mm_and_ps(a, b);
}

FORCEINLINE fltx4 AndNotSIMD(const fltx4& a, const fltx4& b) // ~a & b
{
	return _mm_andnot_ps(a, b);
}

FORCEINLINE fltx4 XorSIMD(const fltx4& a, const fltx4& b) // a ^ b
{
	return _mm_xor_ps(a, b);
}

FORCEINLINE fltx4 OrSIMD(const fltx4& a, const fltx4& b) // a | b
{
	return _mm_or_ps(a, b);
}

// Squelch the w component of a vector to +0.0.
// Most efficient when you say a = SetWToZeroSIMD(a) (avoids a copy)
FORCEINLINE fltx4 SetWToZeroSIMD(const fltx4& a)
{
	return AndSIMD(a, LoadAlignedSIMD(g_SIMD_clear_wmask));
}

// for the transitional class -- load a 3-by VectorAligned and squash its w component
FORCEINLINE fltx4 LoadAlignedSIMD(const VectorAligned& pSIMD)
{
	return SetWToZeroSIMD(LoadAlignedSIMD(pSIMD.Base()));
}

template <typename T>
NO_ASAN_FORCEINLINE fltx4 LoadUnalignedSIMD(const T* pSIMD)
{
	static_assert(IS_FLOAT_TYPE(T), "You are doing something wrong.");
	return _mm_loadu_ps(reinterpret_cast<const float*>(pSIMD));
}

/// replicate a single 32 bit integer value to all 4 components of an m128
FORCEINLINE fltx4 ReplicateIX4(int i)
{
	fltx4 value = _mm_set_ss(*((float*)&i));
	;
	return _mm_shuffle_ps(value, value, 0);
}

FORCEINLINE fltx4 ReplicateX4(float flValue)
{
	__m128 value = _mm_set_ss(flValue);
	return _mm_shuffle_ps(value, value, 0);
}

FORCEINLINE float SubFloat(const fltx4& a, int idx)
{
	// NOTE: if the output goes into a register, this causes a Load-Hit-Store stall (don't mix fpu/vpu math!)
#if !defined(POSIX) && !defined(__clang__)
	return a.m128_f32[idx];
#else
	return (reinterpret_cast<float const*>(&a))[idx];
#endif
}

FORCEINLINE float& SubFloat(fltx4& a, int idx)
{
#if !defined(POSIX) && !defined(__clang__)
	return a.m128_f32[idx];
#else
	return (reinterpret_cast<float*>(&a))[idx];
#endif
}

FORCEINLINE uint32 SubInt(const fltx4& a, int idx)
{
#if !defined(POSIX) && !defined(__clang__)
	return a.m128_u32[idx];
#else
	return (reinterpret_cast<uint32 const*>(&a))[idx];
#endif
}

FORCEINLINE uint32& SubInt(fltx4& a, int idx)
{
#if !defined(POSIX) && !defined(__clang__)
	return a.m128_u32[idx];
#else
	return (reinterpret_cast<uint32*>(&a))[idx];
#endif
}

FORCEINLINE fltx4 LoadZeroSIMD()
{
	return Four_Zeros;
}

FORCEINLINE fltx4 MaskedAssign(const fltx4& ReplacementMask, const fltx4& NewValue, const fltx4& OldValue)
{
	return OrSIMD(AndSIMD(ReplacementMask, NewValue), AndNotSIMD(ReplacementMask, OldValue));
}

// the same as MaskedAssign, only 5 instructions instead of 6
FORCEINLINE fltx4 SelectSIMD(const fltx4& ReplacementMask, const fltx4& NewValue, const fltx4& OldValue)
{
	return XorSIMD(AndSIMD(XorSIMD(NewValue, OldValue), ReplacementMask), OldValue);
}

// remember, the SSE numbers its words 3 2 1 0
// The way we want to specify shuffles is backwards from the default
// MM_SHUFFLE_REV is in array index order (default is reversed)
#define MM_SHUFFLE_REV(a, b, c, d) _MM_SHUFFLE(d, c, b, a)

FORCEINLINE fltx4 SplatXSIMD(fltx4 const& a)
{
	return _mm_shuffle_ps(a, a, MM_SHUFFLE_REV(0, 0, 0, 0));
}

FORCEINLINE fltx4 SplatYSIMD(fltx4 const& a)
{
	return _mm_shuffle_ps(a, a, MM_SHUFFLE_REV(1, 1, 1, 1));
}

FORCEINLINE fltx4 SplatZSIMD(fltx4 const& a)
{
	return _mm_shuffle_ps(a, a, MM_SHUFFLE_REV(2, 2, 2, 2));
}

FORCEINLINE fltx4 SetWSIMD(const fltx4& a, const fltx4& w)
{
	fltx4 result = MaskedAssign(LoadAlignedSIMD(g_SIMD_ComponentMask[3]), w, a);
	return result;
}

// a b c d -> b c d a
FORCEINLINE fltx4 RotateLeft(const fltx4& a)
{
	return _mm_shuffle_ps(a, a, MM_SHUFFLE_REV(1, 2, 3, 0));
}

// a b c d -> c d a b
FORCEINLINE fltx4 RotateLeft2(const fltx4& a)
{
	return _mm_shuffle_ps(a, a, MM_SHUFFLE_REV(2, 3, 0, 1));
}

// a b c d -> d a b c
FORCEINLINE fltx4 RotateRight(const fltx4& a)
{
	return _mm_shuffle_ps(a, a, _MM_SHUFFLE(0, 3, 2, 1));
}

FORCEINLINE fltx4 AddSIMD(const fltx4& a, const fltx4& b) // a+b
{
	return _mm_add_ps(a, b);
};

FORCEINLINE fltx4 SubSIMD(const fltx4& a, const fltx4& b) // a-b
{
	return _mm_sub_ps(a, b);
};

FORCEINLINE fltx4 MulSIMD(const fltx4& a, const fltx4& b) // a*b
{
	return _mm_mul_ps(a, b);
};

FORCEINLINE fltx4 DivSIMD(const fltx4& a, const fltx4& b) // a/b
{
	return _mm_div_ps(a, b);
};

FORCEINLINE fltx4 MaddSIMD(const fltx4& a, const fltx4& b, const fltx4& c) // a*b + c
{
	return AddSIMD(MulSIMD(a, b), c);
}

FORCEINLINE fltx4 Dot3SIMD(const fltx4& a, const fltx4& b)
{
	fltx4 m = MulSIMD(a, b);
	float flDot = SubFloat(m, 0) + SubFloat(m, 1) + SubFloat(m, 2);
	return ReplicateX4(flDot);
}

FORCEINLINE fltx4 Dot4SIMD(const fltx4& a, const fltx4& b)
{
	fltx4 m = MulSIMD(a, b);
	float flDot = SubFloat(m, 0) + SubFloat(m, 1) + SubFloat(m, 2) + SubFloat(m, 3);
	return ReplicateX4(flDot);
}

FORCEINLINE fltx4 NegSIMD(const fltx4& a) // negate: -a
{
	return SubSIMD(LoadZeroSIMD(), a);
}

FORCEINLINE int TestSignSIMD(const fltx4& a) // mask of which floats have the high bit set
{
	return _mm_movemask_ps(a);
}

FORCEINLINE bool IsAnyNegative(const fltx4& a) // (a.x < 0) || (a.y < 0) || (a.z < 0) || (a.w < 0)
{
	return (0 != TestSignSIMD(a));
}

FORCEINLINE fltx4 CmpEqSIMD(const fltx4& a, const fltx4& b) // (a==b) ? ~0:0
{
	return _mm_cmpeq_ps(a, b);
}

FORCEINLINE fltx4 CmpGtSIMD(const fltx4& a, const fltx4& b) // (a>b) ? ~0:0
{
	return _mm_cmpgt_ps(a, b);
}

FORCEINLINE fltx4 CmpGeSIMD(const fltx4& a, const fltx4& b) // (a>=b) ? ~0:0
{
	return _mm_cmpge_ps(a, b);
}

FORCEINLINE fltx4 CmpLtSIMD(const fltx4& a, const fltx4& b) // (a<b) ? ~0:0
{
	return _mm_cmplt_ps(a, b);
}

FORCEINLINE fltx4 CmpLeSIMD(const fltx4& a, const fltx4& b) // (a<=b) ? ~0:0
{
	return _mm_cmple_ps(a, b);
}

FORCEINLINE fltx4 MinSIMD(const fltx4& a, const fltx4& b) // min(a,b)
{
	return _mm_min_ps(a, b);
}

FORCEINLINE fltx4 MaxSIMD(const fltx4& a, const fltx4& b) // max(a,b)
{
	return _mm_max_ps(a, b);
}

fltx4 fabs(const fltx4& x);

inline bool IsAllZeros(const fltx4& var)
{
	return TestSignSIMD(CmpEqSIMD(var, Four_Zeros)) == 0xF;
}

FORCEINLINE fltx4 SqrtEstSIMD(const fltx4& a) // sqrt(a), more or less
{
	return _mm_sqrt_ps(a);
}

FORCEINLINE fltx4 ReciprocalSqrtEstSIMD(const fltx4& a) // 1/sqrt(a), more or less
{
	return _mm_rsqrt_ps(a);
}

/// uses newton iteration for higher precision results than ReciprocalSqrtEstSIMD
FORCEINLINE fltx4 ReciprocalSqrtSIMD(const fltx4& a) // 1/sqrt(a)
{
	fltx4 guess = ReciprocalSqrtEstSIMD(a);
	// newton iteration for 1/sqrt(a) : y(n+1) = 1/2 (y(n)*(3-a*y(n)^2));
	guess = MulSIMD(guess, SubSIMD(Four_Threes, MulSIMD(a, MulSIMD(guess, guess))));
	guess = MulSIMD(Four_PointFives, guess);
	return guess;
}

FORCEINLINE fltx4 ReciprocalEstSIMD(const fltx4& a) // 1/a, more or less
{
	return _mm_rcp_ps(a);
}

/// 1/x for all 4 values, more or less
/// 1/0 will result in a big but NOT infinite result
FORCEINLINE fltx4 ReciprocalEstSaturateSIMD(const fltx4& a)
{
	fltx4 zero_mask = CmpEqSIMD(a, Four_Zeros);
	fltx4 ret = OrSIMD(a, AndSIMD(Four_Epsilons, zero_mask));
	ret = ReciprocalEstSIMD(ret);
	return ret;
}

/// 1/x for all 4 values. uses reciprocal approximation instruction plus newton iteration.
/// No error checking!
FORCEINLINE fltx4 ReciprocalSIMD(const fltx4& a) // 1/a
{
	fltx4 ret = ReciprocalEstSIMD(a);
	// newton iteration is: Y(n+1) = 2*Y(n)-a*Y(n)^2
	ret = SubSIMD(AddSIMD(ret, ret), MulSIMD(a, MulSIMD(ret, ret)));
	return ret;
}

/// 1/x for all 4 values.
/// 1/0 will result in a big but NOT infinite result
FORCEINLINE fltx4 ReciprocalSaturateSIMD(const fltx4& a)
{
	fltx4 zero_mask = CmpEqSIMD(a, Four_Zeros);
	fltx4 ret = OrSIMD(a, AndSIMD(Four_Epsilons, zero_mask));
	ret = ReciprocalSIMD(ret);
	return ret;
}

FORCEINLINE void TransposeSIMD(fltx4& x, fltx4& y, fltx4& z, fltx4& w)
{
	_MM_TRANSPOSE4_PS(x, y, z, w);
}

// INTEGER SIMD OPERATIONS.

#if 0 /* pc does not have these ops */
// splat all components of a vector to a signed immediate int number.
FORCEINLINE fltx4 IntSetImmediateSIMD(int to)
{
	//CHRISG: SSE2 has this, but not SSE1. What to do?
	fltx4 retval;
	SubInt( retval, 0 ) = to;
	SubInt( retval, 1 ) = to;
	SubInt( retval, 2 ) = to;
	SubInt( retval, 3 ) = to;
	return retval;
}
#endif

// CHRISG: the conversion functions all seem to operate on m64's only...
// how do we make them work here?

/*
  works on fltx4's as if they are four uints.
  the first parameter contains the words to be shifted,
  the second contains the amount to shift by AS INTS

  for i = 0 to 3
  shift = vSrcB_i*32:(i*32)+4
  vReturned_i*32:(i*32)+31 = vSrcA_i*32:(i*32)+31 << shift
*/

// Fixed-point conversion and save as SIGNED INTS.
// pDest->x = Int (vSrc.x)
// note: some architectures have means of doing
// fixed point conversion when the fix depth is
// specified as an immediate.. but there is no way
// to guarantee an immediate as a parameter to function
// like this.
FORCEINLINE void ConvertStoreAsIntsSIMD(intx4* RESTRICT pDest, const fltx4& vSrc)
{
#if defined(COMPILER_MSVC64)

	(*pDest)[0] = SubFloat(vSrc, 0);
	(*pDest)[1] = SubFloat(vSrc, 1);
	(*pDest)[2] = SubFloat(vSrc, 2);
	(*pDest)[3] = SubFloat(vSrc, 3);

#else
	__m64 bottom = _mm_cvttps_pi32(vSrc);
	__m64 top = _mm_cvttps_pi32(_mm_movehl_ps(vSrc, vSrc));

	*reinterpret_cast<__m64*>(&(*pDest)[0]) = bottom;
	*reinterpret_cast<__m64*>(&(*pDest)[2]) = top;

	_mm_empty();
#endif
}

#endif

/// class FourVectors stores 4 independent vectors for use in SIMD processing. These vectors are
/// stored in the format x x x x y y y y z z z z so that they can be efficiently SIMD-accelerated.
class ALIGN16 FourVectors
{
public:
	fltx4 x, y, z;

	FORCEINLINE void DuplicateVector(Vector const& v) //< set all 4 vectors to the same vector value
	{
		x = ReplicateX4(v.x);
		y = ReplicateX4(v.y);
		z = ReplicateX4(v.z);
	}

	FORCEINLINE fltx4 const& operator[](int idx) const { return *((&x) + idx); }

	FORCEINLINE fltx4& operator[](int idx) { return *((&x) + idx); }

	FORCEINLINE void operator+=(FourVectors const& b) //< add 4 vectors to another 4 vectors
	{
		x = AddSIMD(x, b.x);
		y = AddSIMD(y, b.y);
		z = AddSIMD(z, b.z);
	}

	FORCEINLINE void operator-=(FourVectors const& b) //< subtract 4 vectors from another 4
	{
		x = SubSIMD(x, b.x);
		y = SubSIMD(y, b.y);
		z = SubSIMD(z, b.z);
	}

	FORCEINLINE void operator*=(FourVectors const& b) //< scale all four vectors per component scale
	{
		x = MulSIMD(x, b.x);
		y = MulSIMD(y, b.y);
		z = MulSIMD(z, b.z);
	}

	FORCEINLINE void operator*=(const fltx4& scale) //< scale
	{
		x = MulSIMD(x, scale);
		y = MulSIMD(y, scale);
		z = MulSIMD(z, scale);
	}

	FORCEINLINE void operator*=(float scale) //< uniformly scale all 4 vectors
	{
		fltx4 scalepacked = ReplicateX4(scale);
		*this *= scalepacked;
	}

	FORCEINLINE fltx4 operator*(FourVectors const& b) const //< 4 dot products
	{
		fltx4 dot = MulSIMD(x, b.x);
		dot = MaddSIMD(y, b.y, dot);
		dot = MaddSIMD(z, b.z, dot);
		return dot;
	}

	FORCEINLINE fltx4 operator*(Vector const& b) const //< dot product all 4 vectors with 1 vector
	{
		fltx4 dot = MulSIMD(x, ReplicateX4(b.x));
		dot = MaddSIMD(y, ReplicateX4(b.y), dot);
		dot = MaddSIMD(z, ReplicateX4(b.z), dot);
		return dot;
	}

	FORCEINLINE void VProduct(FourVectors const& b) //< component by component mul
	{
		x = MulSIMD(x, b.x);
		y = MulSIMD(y, b.y);
		z = MulSIMD(z, b.z);
	}

	FORCEINLINE void MakeReciprocalSaturate(void) //< (x,y,z)=(1/x,1/y,1/z), 1/0=1.0e23
	{
		x = ReciprocalSaturateSIMD(x);
		y = ReciprocalSaturateSIMD(y);
		z = ReciprocalSaturateSIMD(z);
	}

	// X(),Y(),Z() - get at the desired component of the i'th (0..3) vector.
	FORCEINLINE const float& X(int idx) const
	{
		// NOTE: if the output goes into a register, this causes a Load-Hit-Store stall (don't mix fpu/vpu math!)
		return SubFloat((fltx4&)x, idx);
	}

	FORCEINLINE const float& Y(int idx) const { return SubFloat((fltx4&)y, idx); }

	FORCEINLINE const float& Z(int idx) const { return SubFloat((fltx4&)z, idx); }

	FORCEINLINE float& X(int idx) { return SubFloat(x, idx); }

	FORCEINLINE float& Y(int idx) { return SubFloat(y, idx); }

	FORCEINLINE float& Z(int idx) { return SubFloat(z, idx); }

	FourVectors() = default;

	FourVectors(FourVectors const& src)
	{
		x = src.x;
		y = src.y;
		z = src.z;
	}

	FORCEINLINE void operator=(FourVectors const& src)
	{
		x = src.x;
		y = src.y;
		z = src.z;
	}

	/// LoadAndSwizzle - load 4 Vectors into a FourVectors, performing transpose op
	FORCEINLINE void LoadAndSwizzle(Vector const& a, Vector const& b, Vector const& c, Vector const& d)
	{
		// use an unfolded implementation here
		x = LoadUnalignedSIMD(&(a.x));
		y = LoadUnalignedSIMD(&(b.x));
		z = LoadUnalignedSIMD(&(c.x));
		fltx4 w = LoadUnalignedSIMD(&(d.x));
		// now, matrix is:
		// x y z ?
		// x y z ?
		// x y z ?
		// x y z ?
		TransposeSIMD(x, y, z, w);
	}

	/// LoadAndSwizzleAligned - load 4 Vectors into a FourVectors, performing transpose op.
	/// all 4 vectors must be 128 bit boundary
	FORCEINLINE void LoadAndSwizzleAligned(const float* RESTRICT a, const float* RESTRICT b, const float* RESTRICT c,
										   const float* RESTRICT d)
	{
		x = LoadAlignedSIMD(a);
		y = LoadAlignedSIMD(b);
		z = LoadAlignedSIMD(c);
		fltx4 w = LoadAlignedSIMD(d);
		// now, matrix is:
		// x y z ?
		// x y z ?
		// x y z ?
		// x y z ?
		TransposeSIMD(x, y, z, w);
	}

	FORCEINLINE void LoadAndSwizzleAligned(Vector const& a, Vector const& b, Vector const& c, Vector const& d)
	{
		LoadAndSwizzleAligned(&a.x, &b.x, &c.x, &d.x);
	}

	/// return the squared length of all 4 vectors
	FORCEINLINE fltx4 length2() const { return (*this) * (*this); }

	/// return the approximate length of all 4 vectors. uses the sqrt approximation instruction
	FORCEINLINE fltx4 length() const { return SqrtEstSIMD(length2()); }

	/// normalize all 4 vectors in place. not mega-accurate (uses reciprocal approximation instruction)
	FORCEINLINE void VectorNormalizeFast()
	{
		fltx4 mag_sq = (*this) * (*this); // length^2
		(*this) *= ReciprocalSqrtEstSIMD(mag_sq); // *(1.0/sqrt(length^2))
	}

	/// normalize all 4 vectors in place.
	FORCEINLINE void VectorNormalize()
	{
		fltx4 mag_sq = (*this) * (*this); // length^2
		(*this) *= ReciprocalSqrtSIMD(mag_sq); // *(1.0/sqrt(length^2))
	}

	/// construct a FourVectors from 4 separate Vectors
	FORCEINLINE FourVectors(Vector const& a, Vector const& b, Vector const& c, Vector const& d)
	{
		LoadAndSwizzle(a, b, c, d);
	}

	/// construct a FourVectors from 4 separate Vectors
	FORCEINLINE FourVectors(VectorAligned const& a, VectorAligned const& b, VectorAligned const& c,
							VectorAligned const& d)
	{
		LoadAndSwizzleAligned(a, b, c, d);
	}

	FORCEINLINE fltx4 DistToSqr(FourVectors const& pnt)
	{
		fltx4 fl4dX = SubSIMD(pnt.x, x);
		fltx4 fl4dY = SubSIMD(pnt.y, y);
		fltx4 fl4dZ = SubSIMD(pnt.z, z);
		return AddSIMD(MulSIMD(fl4dX, fl4dX), AddSIMD(MulSIMD(fl4dY, fl4dY), MulSIMD(fl4dZ, fl4dZ)));
	}
};

/// component-by-componentwise MAX operator
inline FourVectors maximum(const FourVectors& a, const FourVectors& b)
{
	FourVectors ret;
	ret.x = MaxSIMD(a.x, b.x);
	ret.y = MaxSIMD(a.y, b.y);
	ret.z = MaxSIMD(a.z, b.z);
	return ret;
}

/// quick, low quality perlin-style noise() function suitable for real time use.
/// return value is -1..1. Only reliable around +/- 1 million or so.
fltx4 NoiseSIMD(const fltx4& x, const fltx4& y, const fltx4& z);

/// calculate the absolute value of a packed single
inline fltx4 fabs(const fltx4& x)
{
	return AndSIMD(x, LoadAlignedSIMD(g_SIMD_clear_signmask));
}

fltx4 Pow_FixedPoint_Exponent_SIMD(const fltx4& x, int exponent);

// PowSIMD - raise a SIMD register to a power.  This is analogous to the C pow() function, with some
// restictions: fractional exponents are only handled with 2 bits of precision. Basically,
// fractions of 0,.25,.5, and .75 are handled. PowSIMD(x,.30) will be the same as PowSIMD(x,.25).
// negative and fractional powers are handled by the SIMD reciprocal and square root approximation
// instructions and so are not especially accurate ----Note that this routine does not raise
// numeric exceptions because it uses SIMD--- This routine is O(log2(exponent)).
inline fltx4 PowSIMD(const fltx4& x, float exponent)
{
	return Pow_FixedPoint_Exponent_SIMD(x, (int)(4.0 * exponent));
}

// random number generation - generate 4 random numbers quickly.

fltx4 RandSIMD(int nContext = 0); // return 4 numbers in the 0..1 range

// SIMD versions of mathlib simplespline functions
// hermite basis function for smooth interpolation
// Similar to Gain() above, but very cheap to call
// value should be between 0 & 1 inclusive
inline fltx4 SimpleSpline(const fltx4& value)
{
	// Arranged to avoid a data dependency between these two MULs:
	fltx4 valueDoubled = MulSIMD(value, Four_Twos);
	fltx4 valueSquared = MulSIMD(value, value);

	// Nice little ease-in, ease-out spline-like curve
	return SubSIMD(MulSIMD(Four_Threes, valueSquared), MulSIMD(valueDoubled, valueSquared));
}

FORCEINLINE fltx4 Mod2SIMDPositiveInput(const fltx4& val)
{
	fltx4 ival = SubSIMD(AndSIMD(LoadAlignedSIMD(g_SIMD_lsbmask), AddSIMD(val, Four_2ToThe23s)), Four_2ToThe23s);
	ival = MaskedAssign(CmpGtSIMD(ival, val), SubSIMD(ival, Four_Twos), ival);
	return SubSIMD(val, ival);
}

// approximate sin of an angle, with -1..1 representing the whole sin wave period instead of -pi..pi.
// no range reduction is done - for values outside of 0..1 you won't like the results
FORCEINLINE fltx4 _SinEst01SIMD(const fltx4& val)
{
	// really rough approximation - x*(4-x*4) - a parabola. s(0) = 0, s(.5) = 1, s(1)=0, smooth in-between.
	// sufficient for simple oscillation.
	return MulSIMD(val, SubSIMD(Four_Fours, MulSIMD(val, Four_Fours)));
}

FORCEINLINE fltx4 _Sin01SIMD(const fltx4& val)
{
	// not a bad approximation : parabola always over-estimates. Squared parabola always
	// underestimates. So lets blend between them:  goodsin = badsin + .225*( badsin^2-badsin)
	fltx4 fl4BadEst = MulSIMD(val, SubSIMD(Four_Fours, MulSIMD(val, Four_Fours)));
	return AddSIMD(MulSIMD(Four_Point225s, SubSIMD(MulSIMD(fl4BadEst, fl4BadEst), fl4BadEst)), fl4BadEst);
}

// Schlick style Bias approximation see graphics gems 4 : bias(t,a)= t/( (1/a-2)*(1-t)+1)
