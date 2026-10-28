//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier0/platform.h"
#include "tier1/utlsymbol.h"
#include "mathlib/vector4d.h"
class IMaterial;
class VMatrix;
class ITexture;

// This fourcc is reserved.

// Various material var types
enum MaterialVarType_t
{
	MATERIAL_VAR_TYPE_FLOAT = 0,
	MATERIAL_VAR_TYPE_STRING,
	MATERIAL_VAR_TYPE_VECTOR,
	MATERIAL_VAR_TYPE_TEXTURE,
	MATERIAL_VAR_TYPE_INT,
	MATERIAL_VAR_TYPE_FOURCC,
	MATERIAL_VAR_TYPE_UNDEFINED,
	MATERIAL_VAR_TYPE_MATRIX,
	MATERIAL_VAR_TYPE_MATERIAL,
};

class IMaterialVar
{
public:
protected:
	// base data and accessors
	alignas( 8 ) unsigned char unused_m_pStringVal[8];
	int m_intVal;
	Vector4D m_VecVal;

	// member data. total = 4 bytes
	uint8 m_Type : 4;
	uint8 m_nNumVectorComps : 3;
	uint8 m_bFakeMaterialVar : 1;
	uint8 m_nTempIndex;
	CUtlSymbol m_Name;

public:
	virtual ITexture *GetTextureValue() = 0;

	virtual char const *GetName() const = 0;
	virtual void unused_GetNameAsSymbol() = 0;

	virtual void SetFloatValue( float val ) = 0;

	virtual void SetIntValue( int val ) = 0;

	virtual void SetStringValue( char const *val ) = 0;
	virtual char const *GetStringValue() const = 0;

	// Use FourCC values to pass app-defined data structures between
	// the proxy and the shader. The shader should ignore the data if
	// its FourCC type not correct.
	virtual void unused_SetFourCCValue() = 0;
	virtual void unused_GetFourCCValue() = 0;

	// Vec (dim 2-4)
	virtual void SetVecValue( float const *val, int numcomps ) = 0;
	virtual void SetVecValue( float x, float y ) = 0;
	virtual void SetVecValue( float x, float y, float z ) = 0;
	virtual void SetVecValue( float x, float y, float z, float w ) = 0;
	virtual void unused_GetLinearVecValue() = 0;

	// revisit: is this a good interface for textures?
	virtual void SetTextureValue( ITexture * ) = 0;

	virtual void unused_GetMaterialValue() = 0;
	virtual void unused_SetMaterialValue() = 0;

	virtual bool IsDefined() const = 0;
	virtual void unused_SetUndefined() = 0;

	// Matrix
	virtual void SetMatrixValue( VMatrix const &matrix ) = 0;
	virtual void unused_GetMatrixValue() = 0;
	virtual void unused_MatrixIsIdentity() = 0;

	// Copy....
	virtual void CopyFrom( IMaterialVar *pMaterialVar ) = 0;

	virtual void unused_SetValueAutodetectType() = 0;

	virtual void unused_GetOwningMaterial() = 0;

	//set just 1 component
	virtual void SetVecComponentValue( float fVal, int nComponent ) = 0;

protected:
	virtual int GetIntValueInternal() const = 0;
	virtual float GetFloatValueInternal() const = 0;
	virtual float const *GetVecValueInternal() const = 0;
	virtual void GetVecValueInternal( float *val, int numcomps ) const = 0;
	virtual int VectorSizeInternal() const = 0;

public:
	FORCEINLINE MaterialVarType_t GetType() const { return (MaterialVarType_t)m_Type; }

	FORCEINLINE operator ITexture *() { return GetTextureValue(); }

	// NOTE: Fast methods should only be called in thread-safe situations
	FORCEINLINE int GetIntValueFast() const
	{
		// Set methods for float and vector update this
		return m_intVal;
	}

	FORCEINLINE int GetIntValue() const { return GetIntValueInternal(); }

	FORCEINLINE float GetFloatValue() const { return GetFloatValueInternal(); }

	FORCEINLINE void GetVecValue( float *val, int numcomps ) const { return GetVecValueInternal( val, numcomps ); }

	FORCEINLINE int VectorSize() const { return VectorSizeInternal(); }

private:
};
