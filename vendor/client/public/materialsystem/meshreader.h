//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// This is used to read vertex and index data out of already-created meshes.
// vertex data.
class CBaseMeshReader : protected MeshDesc_t
{
	// Initialization.
public:
	CBaseMeshReader();
	~CBaseMeshReader();

	// Resets the mesh builder so it points to the start of everything again
	void Reset();

protected:
	IMesh* m_pMesh;
	int m_MaxVertices;
	int m_MaxIndices;
};

// A bunch of accessors for the data that CBaseMeshReader sets up.
class CMeshReader : public CBaseMeshReader
{
public:
	// Access to vertex data.
public:
	unsigned short Index(int index) const;

	unsigned int Color(int iVertex) const;

	const Vector& Normal(int iVertex) const;
	void Normal(int iVertex, Vector& vNormal) const;
};

// CBaseMeshReader implementation.

inline CBaseMeshReader::CBaseMeshReader()
{
	m_pMesh = NULL;
}

inline CBaseMeshReader::~CBaseMeshReader()
{
	Assert(!m_pMesh);
}

inline void CBaseMeshReader::Reset() {}

// CMeshReader implementation.

inline unsigned short CMeshReader::Index(int index) const
{
	Assert((index >= 0) && (index < m_MaxIndices));
	return m_pIndices[index * m_nIndexSize];
}

inline unsigned int CMeshReader::Color(int iVertex) const
{
	Assert(iVertex >= 0 && iVertex < m_MaxVertices);
	unsigned char* pColor = m_pColor + iVertex * m_VertexSize_Color;
	return (pColor[0] << 16) | (pColor[1] << 8) | (pColor[2]) | (pColor[3] << 24);
}

inline const Vector& CMeshReader::Normal(int iVertex) const
{
	Assert(iVertex >= 0 && iVertex < m_MaxVertices);
	return *(const Vector*)(const float*)((char*)m_pNormal + iVertex * m_VertexSize_Normal);
}

inline void CMeshReader::Normal(int iVertex, Vector& vNormal) const
{
	Assert(iVertex >= 0 && iVertex < m_MaxVertices);
	const float* p = (const float*)((char*)m_pNormal + iVertex * m_VertexSize_Normal);
	vNormal.Init(p[0], p[1], p[2]);
}
