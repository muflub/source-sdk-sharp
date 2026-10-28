//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <math.h>
#include <stdlib.h>		// For rand(). We really need a library!
#include <float.h>
#include <xmmintrin.h>	// For SSE
#include "basetypes.h"	// For vec_t, put this somewhere else?
#include "tier0/dbg.h"
#include "mathlib/math_pfns.h"

// forward declarations
class Vector;
class Vector2D;

// 4D Vector4D

class Vector4D					
{
public:
	// Members
	vec_t x, y, z, w;

	// Construction/destruction
#ifdef _DEBUG
	Vector4D();
#else
	Vector4D() = default;
#endif
	Vector4D(vec_t X, vec_t Y, vec_t Z, vec_t W);
	Vector4D(const float *pFloat);

	// Initialization
	void Init(vec_t ix=0.0f, vec_t iy=0.0f, vec_t iz=0.0f, vec_t iw=0.0f);

	// Got any nasty NAN's?
	bool IsValid() const;

	// array access...
	vec_t operator[](int i) const;
	vec_t& operator[](int i);

	// Base address...
	inline vec_t* Base();
	inline vec_t const* Base() const;

	// Cast to Vector and Vector2D...
	Vector& AsVector3D();

	// Initialization methods
	void Random( vec_t minVal, vec_t maxVal );

	// equality
	bool operator==(const Vector4D& v) const;
	bool operator!=(const Vector4D& v) const;	

	// arithmetic operations
	Vector4D&	operator+=(const Vector4D &v);			
	Vector4D&	operator-=(const Vector4D &v);		
	Vector4D&	operator*=(const Vector4D &v);			
	Vector4D&	operator*=(float s);
	Vector4D&	operator/=(const Vector4D &v);		
	Vector4D&	operator/=(float s);				

	Vector4D	operator-( void ) const;
	Vector4D	operator*( float fl ) const;
	Vector4D	operator/( float fl ) const;
	Vector4D	operator*( const Vector4D& v ) const;
	Vector4D	operator+( const Vector4D& v ) const;
	Vector4D	operator-( const Vector4D& v ) const;

	// Get the Vector4D's magnitude.
	vec_t	Length() const;

	// No copy constructors allowed if we're in optimal mode
public:
	Vector4D(Vector4D const& vOther);

	// No assignment operators either...
	Vector4D& operator=( Vector4D const& src );
};

// SSE optimized routines

class ALIGN16 Vector4DAligned : public Vector4D
{
public:
	Vector4DAligned(void) {}
	Vector4DAligned( vec_t X, vec_t Y, vec_t Z, vec_t W );

	inline void Set( vec_t X, vec_t Y, vec_t Z, vec_t W );

	inline __m128 &AsM128() { return *(__m128*)&x; }
	inline const __m128 &AsM128() const { return *(const __m128*)&x; } 

private:
	// No copy constructors allowed if we're in optimal mode
	Vector4DAligned( Vector4DAligned const& vOther );

} ALIGN16_POST;

// Vector4D related operations

// Copy
void Vector4DCopy( Vector4D const& src, Vector4D& dst );

// Vector4D arithmetic
void Vector4DAdd( Vector4D const& a, Vector4D const& b, Vector4D& result );
void Vector4DSubtract( Vector4D const& a, Vector4D const& b, Vector4D& result );
void Vector4DMultiply( Vector4D const& a, vec_t b, Vector4D& result );
void Vector4DMultiply( Vector4D const& a, Vector4D const& b, Vector4D& result );
void Vector4DDivide( Vector4D const& a, vec_t b, Vector4D& result );

// Length
vec_t Vector4DLength( Vector4D const& v );

// Inlined Vector4D methods

// constructors

#ifdef _DEBUG
inline Vector4D::Vector4D(void)									
{ 
	// Initialize to NAN to catch errors
	x = y = z = w = VEC_T_NAN;
}
#endif

inline Vector4D::Vector4D(vec_t X, vec_t Y, vec_t Z, vec_t W )
{ 
	x = X; y = Y; z = Z; w = W;
	Assert( IsValid() );
}

inline Vector4D::Vector4D(const float *pFloat)					
{
	Assert( pFloat );
	x = pFloat[0]; y = pFloat[1]; z = pFloat[2]; w = pFloat[3];	
	Assert( IsValid() );
}

// copy constructor

inline Vector4D::Vector4D(const Vector4D &vOther)					
{ 
	Assert( vOther.IsValid() );
	x = vOther.x; y = vOther.y; z = vOther.z; w = vOther.w;
}

// initialization

inline void Vector4D::Init( vec_t ix, vec_t iy, vec_t iz, vec_t iw )
{ 
	x = ix; y = iy; z = iz;	w = iw;
	Assert( IsValid() );
}

inline void Vector4D::Random( vec_t minVal, vec_t maxVal )
{
	x = minVal + ((vec_t)rand() / VALVE_RAND_MAX) * (maxVal - minVal);
	y = minVal + ((vec_t)rand() / VALVE_RAND_MAX) * (maxVal - minVal);
	z = minVal + ((vec_t)rand() / VALVE_RAND_MAX) * (maxVal - minVal);
	w = minVal + ((vec_t)rand() / VALVE_RAND_MAX) * (maxVal - minVal);
}

// assignment

// Array access

inline vec_t& Vector4D::operator[](int i)
{
	Assert( (i >= 0) && (i < 4) );
	return ((vec_t*)this)[i];
}

inline vec_t Vector4D::operator[](int i) const
{
	Assert( (i >= 0) && (i < 4) );
	return ((vec_t*)this)[i];
}

// Cast to Vector and Vector2D...

inline Vector& Vector4D::AsVector3D()
{
	return *(Vector*)this;
}

// Base address...

inline vec_t* Vector4D::Base()
{
	return (vec_t*)this;
}

inline vec_t const* Vector4D::Base() const
{
	return (vec_t const*)this;
}

// IsValid?

inline bool Vector4D::IsValid() const
{
	return IsFinite(x) && IsFinite(y) && IsFinite(z) && IsFinite(w);
}

// comparison

// Copy

inline void Vector4DCopy( Vector4D const& src, Vector4D& dst )
{
	Assert( src.IsValid() );
	dst.x = src.x;
	dst.y = src.y;
	dst.z = src.z;
	dst.w = src.w;
}

// standard math operations

inline Vector4D& Vector4D::operator/=(float fl)	
{
	Assert( fl != 0.0f );
	float oofl = 1.0f / fl;
	x *= oofl;
	y *= oofl;
	z *= oofl;
	w *= oofl;
	Assert( IsValid() );
	return *this;
}

inline Vector4D Vector4D::operator*(float fl) const	
{ 
	Vector4D res;
	Vector4DMultiply( *this, fl, res );
	return res;	
}

inline void Vector4DAdd( Vector4D const& a, Vector4D const& b, Vector4D& c )
{
	Assert( a.IsValid() && b.IsValid() );
	c.x = a.x + b.x;
	c.y = a.y + b.y;
	c.z = a.z + b.z;
	c.w = a.w + b.w;
}

inline void Vector4DSubtract( Vector4D const& a, Vector4D const& b, Vector4D& c )
{
	Assert( a.IsValid() && b.IsValid() );
	c.x = a.x - b.x;
	c.y = a.y - b.y;
	c.z = a.z - b.z;
	c.w = a.w - b.w;
}

inline void Vector4DMultiply( Vector4D const& a, vec_t b, Vector4D& c )
{
	Assert( a.IsValid() && IsFinite(b) );
	c.x = a.x * b;
	c.y = a.y * b;
	c.z = a.z * b;
	c.w = a.w * b;
}

inline void Vector4DMultiply( Vector4D const& a, Vector4D const& b, Vector4D& c )
{
	Assert( a.IsValid() && b.IsValid() );
	c.x = a.x * b.x;
	c.y = a.y * b.y;
	c.z = a.z * b.z;
	c.w = a.w * b.w;
}

inline void Vector4DDivide( Vector4D const& a, vec_t b, Vector4D& c )
{
	Assert( a.IsValid() );
	Assert( b != 0.0f );
	vec_t oob = 1.0f / b;
	c.x = a.x * oob;
	c.y = a.y * oob;
	c.z = a.z * oob;
	c.w = a.w * oob;
}

// dot, cross

// length

inline vec_t Vector4DLength( Vector4D const& v )
{				   
	Assert( v.IsValid() );
	return (vec_t)FastSqrt(v.x*v.x + v.y*v.y + v.z*v.z + v.w*v.w);		
}

inline vec_t Vector4D::Length(void) const	
{
	return Vector4DLength( *this );
}

// Normalization

// Get the distance from this Vector4D to the other one 

// Vector4DAligned routines

inline Vector4DAligned::Vector4DAligned( vec_t X, vec_t Y, vec_t Z, vec_t W )
{ 
	x = X; y = Y; z = Z; w = W;
	Assert( IsValid() );
}

inline void Vector4DAligned::Set( vec_t X, vec_t Y, vec_t Z, vec_t W )
{ 
	x = X; y = Y; z = Z; w = W;
	Assert( IsValid() );
}
