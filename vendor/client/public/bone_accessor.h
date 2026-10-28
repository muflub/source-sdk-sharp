//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "studio.h"

class C_BaseAnimating;

class CBoneAccessor
{
public:
	
	CBoneAccessor();
	CBoneAccessor( matrix3x4_t *pBones ); // This can be used to allow access to all bones.
	
	// Initialize.
	void Init( const C_BaseAnimating *pAnimating, matrix3x4_t *pBones );
	
	// Get bones for read or write access.
	const matrix3x4_t&	GetBone( int iBone ) const;
	const matrix3x4_t&	operator[]( int iBone ) const;
	matrix3x4_t&		GetBoneForWrite( int iBone );

private:

#if defined( _DEBUG )
	void SanityCheckBone( int iBone, bool bReadable ) const;
#endif

	// Only used in the client DLL for debug verification.
	const C_BaseAnimating *m_pAnimating;

	matrix3x4_t *m_pBones;

	int m_ReadableBones;		// Which bones can be read.
	int m_WritableBones;		// Which bones can be written.
};

inline CBoneAccessor::CBoneAccessor()
{
	m_pAnimating = NULL;
	m_pBones = NULL;
	m_ReadableBones = m_WritableBones = 0;
}

inline CBoneAccessor::CBoneAccessor( matrix3x4_t *pBones )
{
	m_pAnimating = NULL;
	m_pBones = pBones;
}

	inline void CBoneAccessor::Init( const C_BaseAnimating *pAnimating, matrix3x4_t *pBones )
	{
		m_pAnimating = pAnimating;
		m_pBones = pBones;
	}

inline const matrix3x4_t& CBoneAccessor::GetBone( int iBone ) const
{
#if defined( _DEBUG )
	SanityCheckBone( iBone, true );
#endif
	return m_pBones[iBone];
}

inline const matrix3x4_t& CBoneAccessor::operator[]( int iBone ) const
{
#if defined( _DEBUG )
	SanityCheckBone( iBone, true );
#endif
	return m_pBones[iBone];
}

inline matrix3x4_t& CBoneAccessor::GetBoneForWrite( int iBone )
{
#if defined( _DEBUG )
	SanityCheckBone( iBone, false );
#endif
	return m_pBones[iBone];
}
