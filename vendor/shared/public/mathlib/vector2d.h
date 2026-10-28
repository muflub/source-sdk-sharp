//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <float.h>

// For vec_t, put this somewhere else?

// For rand(). We really need a library!

#include "tier0/dbg.h"
#include "mathlib/math_pfns.h"

// 2D Vector2D

class Vector2D
{
public:
	// Members
	vec_t x, y;

	// Construction/destruction
#ifdef _DEBUG
	Vector2D();
#else
	Vector2D() = default;
#endif
	Vector2D( vec_t X, vec_t Y );
	Vector2D( const float *pFloat );

	// Initialization
	void Init( vec_t ix = 0.0f, vec_t iy = 0.0f );

	// Got any nasty NAN's?
	bool IsValid() const;

	// array access...
	vec_t operator[]( int i ) const;
	vec_t &operator[]( int i );

	// Base address...
	vec_t *Base();
	vec_t const *Base() const;

	// Initialization methods
	void Random( float minVal, float maxVal );

	Vector2D &operator/=( float s );

	// Get the Vector2D's magnitude.
	vec_t Length() const;

	// Get the Vector2D's magnitude squared.
	vec_t LengthSqr() const;

	// Get the distance from this Vector2D to the other one.
	vec_t DistTo( const Vector2D &vOther ) const;

	// Get the distance from this Vector2D to the other one squared.
	vec_t DistToSqr( const Vector2D &vOther ) const;

	// Multiply, add, and assign to this (ie: *this = a + b * scalar). This
	// is about 12% faster than the actual Vector2D equation (because it's done per-component
	// rather than per-Vector2D).
	void MulAdd( const Vector2D &a, const Vector2D &b, float scalar );

	// Dot product.
	vec_t Dot( const Vector2D &vOther ) const;

	// assignment
	Vector2D &operator=( const Vector2D &vOther );

	// copy constructors
	Vector2D( const Vector2D &vOther );

	Vector2D operator*( float fl ) const;

	// Returns a Vector2D with the min or max in X, Y, and Z.
	Vector2D Min( const Vector2D &vOther ) const;
	Vector2D Max( const Vector2D &vOther ) const;
};

// Vector2D related operations

// Vector2D arithmetic
void Vector2DAdd( const Vector2D &a, const Vector2D &b, Vector2D &result );
void Vector2DSubtract( const Vector2D &a, const Vector2D &b, Vector2D &result );
void Vector2DMultiply( const Vector2D &a, vec_t b, Vector2D &result );
void Vector2DMultiply( const Vector2D &a, const Vector2D &b, Vector2D &result );
void Vector2DDivide( const Vector2D &a, vec_t b, Vector2D &result );
void Vector2DDivide( const Vector2D &a, const Vector2D &b, Vector2D &result );
void Vector2DMA( const Vector2D &start, float s, const Vector2D &dir, Vector2D &result );

// Normalization
vec_t Vector2DNormalize( Vector2D &v );

// Length
vec_t Vector2DLength( const Vector2D &v );

// Dot Product
vec_t DotProduct2D( const Vector2D &a, const Vector2D &b );

// Inlined Vector2D methods

// constructors

#ifdef _DEBUG
inline Vector2D::Vector2D()
{
	// Initialize to NAN to catch errors
	x = y = VEC_T_NAN;
}
#endif

inline Vector2D::Vector2D( vec_t X, vec_t Y )
{
	x = X;
	y = Y;
	Assert( IsValid() );
}

inline Vector2D::Vector2D( const float *pFloat )
{
	Assert( pFloat );
	x = pFloat[0];
	y = pFloat[1];
	Assert( IsValid() );
}

// copy constructor

inline Vector2D::Vector2D( const Vector2D &vOther )
{
	Assert( vOther.IsValid() );
	x = vOther.x;
	y = vOther.y;
}

// initialization

inline void Vector2D::Init( vec_t ix, vec_t iy )
{
	x = ix;
	y = iy;
	Assert( IsValid() );
}

inline void Vector2D::Random( float minVal, float maxVal )
{
	x = minVal + ( (float)rand() / VALVE_RAND_MAX ) * ( maxVal - minVal );
	y = minVal + ( (float)rand() / VALVE_RAND_MAX ) * ( maxVal - minVal );
}

// assignment

inline Vector2D &Vector2D::operator=( const Vector2D &vOther )
{
	Assert( vOther.IsValid() );
	x = vOther.x;
	y = vOther.y;
	return *this;
}

// Array access

inline vec_t &Vector2D::operator[]( int i )
{
	Assert( ( i >= 0 ) && ( i < 2 ) );
	return ( (vec_t *)this )[i];
}

inline vec_t Vector2D::operator[]( int i ) const
{
	Assert( ( i >= 0 ) && ( i < 2 ) );
	return ( (vec_t *)this )[i];
}

// Base address...

inline vec_t *Vector2D::Base()
{
	return (vec_t *)this;
}

inline vec_t const *Vector2D::Base() const
{
	return (vec_t const *)this;
}

// IsValid?

inline bool Vector2D::IsValid() const
{
	return IsFinite( x ) && IsFinite( y );
}

// comparison

// Copy

// standard math operations

inline Vector2D &Vector2D::operator/=( float fl )
{
	Assert( fl != 0.0f );
	float oofl = 1.0f / fl;
	x *= oofl;
	y *= oofl;
	Assert( IsValid() );
	return *this;
}

inline void Vector2DAdd( const Vector2D &a, const Vector2D &b, Vector2D &c )
{
	Assert( a.IsValid() && b.IsValid() );
	c.x = a.x + b.x;
	c.y = a.y + b.y;
}

inline void Vector2DSubtract( const Vector2D &a, const Vector2D &b, Vector2D &c )
{
	Assert( a.IsValid() && b.IsValid() );
	c.x = a.x - b.x;
	c.y = a.y - b.y;
}

inline void Vector2DMultiply( const Vector2D &a, vec_t b, Vector2D &c )
{
	Assert( a.IsValid() && IsFinite( b ) );
	c.x = a.x * b;
	c.y = a.y * b;
}

inline void Vector2DMultiply( const Vector2D &a, const Vector2D &b, Vector2D &c )
{
	Assert( a.IsValid() && b.IsValid() );
	c.x = a.x * b.x;
	c.y = a.y * b.y;
}

inline void Vector2DDivide( const Vector2D &a, vec_t b, Vector2D &c )
{
	Assert( a.IsValid() );
	Assert( b != 0.0f );
	vec_t oob = 1.0f / b;
	c.x = a.x * oob;
	c.y = a.y * oob;
}

inline void Vector2DDivide( const Vector2D &a, const Vector2D &b, Vector2D &c )
{
	Assert( a.IsValid() );
	Assert( ( b.x != 0.0f ) && ( b.y != 0.0f ) );
	c.x = a.x / b.x;
	c.y = a.y / b.y;
}

inline void Vector2DMA( const Vector2D &start, float s, const Vector2D &dir, Vector2D &result )
{
	Assert( start.IsValid() && IsFinite( s ) && dir.IsValid() );
	result.x = start.x + s * dir.x;
	result.y = start.y + s * dir.y;
}

// FIXME: Remove
// For backwards compatability
inline void Vector2D::MulAdd( const Vector2D &a, const Vector2D &b, float scalar )
{
	x = a.x + b.x * scalar;
	y = a.y + b.y * scalar;
}

// dot, cross
inline vec_t DotProduct2D( const Vector2D &a, const Vector2D &b )
{
	Assert( a.IsValid() && b.IsValid() );
	return ( a.x * b.x + a.y * b.y );
}

// for backwards compatability
inline vec_t Vector2D::Dot( const Vector2D &vOther ) const
{
	return DotProduct2D( *this, vOther );
}

// length
inline vec_t Vector2DLength( const Vector2D &v )
{
	Assert( v.IsValid() );
	return (vec_t)FastSqrt( v.x * v.x + v.y * v.y );
}

inline vec_t Vector2D::LengthSqr() const
{
	Assert( IsValid() );
	return ( x * x + y * y );
}

inline vec_t Vector2D::Length() const
{
	return Vector2DLength( *this );
}

// Normalization
inline vec_t Vector2DNormalize( Vector2D &v )
{
	Assert( v.IsValid() );
	vec_t l = v.Length();
	if ( l != 0.0f )
	{
		v /= l;
	}
	else
	{
		v.x = v.y = 0.0f;
	}
	return l;
}

// Get the distance from this Vector2D to the other one
inline vec_t Vector2D::DistTo( const Vector2D &vOther ) const
{
	Vector2D delta;
	Vector2DSubtract( *this, vOther, delta );
	return delta.Length();
}

inline vec_t Vector2D::DistToSqr( const Vector2D &vOther ) const
{
	Vector2D delta;
	Vector2DSubtract( *this, vOther, delta );
	return delta.LengthSqr();
}

// Slow methods

// Returns a Vector2D with the min or max in X, Y, and Z.

inline Vector2D Vector2D::Min( const Vector2D &vOther ) const
{
	return Vector2D( x < vOther.x ? x : vOther.x, y < vOther.y ? y : vOther.y );
}

inline Vector2D Vector2D::Max( const Vector2D &vOther ) const
{
	return Vector2D( x > vOther.x ? x : vOther.x, y > vOther.y ? y : vOther.y );
}

// arithmetic operations

inline Vector2D Vector2D::operator*( float fl ) const
{
	Vector2D res;
	Vector2DMultiply( *this, fl, res );
	return res;
}
