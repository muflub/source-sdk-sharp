// Readers for the managed detail props' answer buffers. Each buffer is int32
// words, word 0 its length in words; a model pointer is two words (low first),
// a float its bits. A reader that would step past the length stops.
//
//   LevelInitPreEntity  [flags] [objects] objects x (model lo, hi, origin xyz,
//                       angles xyz) [runs] runs x (leaf, first, count, fast)
//   BuildLists          [build] [radius] [proxies] proxies x
//                       (lo, hi) [models] models x (alpha | setAngles << 8,
//                       angles xyz, colour xyz)
//   Prepare             [material] [alphas] alphas x (object, alpha)
//   Emit                commands: Begin quads | Vertices n, n x (x, y, z,
//                       r | g << 8 | b << 16 | a << 24, s, t) | EndDraw |
//                       ModelAlpha object, alpha | End
#pragma once

namespace ss_detail
{

enum
{
	kEnd = 0,
	kBegin = 1,
	kVertices = 2,
	kEndDraw = 3,
	kModelAlpha = 4,
};

enum
{
	kLevelPrecache = 1,
	kModelSetAngles = 1 << 8,
	kVertexWords = 6,
};

// The material a prepared draw binds.
enum
{
	kMaterialNone = 0,
	kMaterialSprite = 1,
	kMaterialWireframe = 2,
};

class CWords
{
public:
	explicit CWords(const void* p)
		: m_p((const int32_t*)p)
		, m_n(p ? ((const int32_t*)p)[0] : 0)
		, m_at(1)
		, m_bBad(!p || m_n < 1)
	{
	}

	bool Has(int n) const { return !m_bBad && n >= 0 && m_at + n <= m_n; }

	int32_t Int()
	{
		if (!Has(1))
		{
			m_bBad = true;
			return 0;
		}
		return m_p[m_at++];
	}

	float Float()
	{
		const int32_t w = Int();
		float f;
		memcpy(&f, &w, sizeof(f));
		return f;
	}

	void* Pointer()
	{
		const uint32_t lo = (uint32_t)Int();
		const uint32_t hi = (uint32_t)Int();
		return (void*)(uintptr_t)((uint64_t)lo | ((uint64_t)hi << 32));
	}

	void Vec(float* pOut)
	{
		pOut[0] = Float();
		pOut[1] = Float();
		pOut[2] = Float();
	}

	bool Bad() const { return m_bBad; }

private:
	const int32_t* m_p;
	int32_t m_n;
	int32_t m_at;
	bool m_bBad;
};

// LevelInitPreEntity's answer, handed to LEVEL: Precache( bool ),
// Object( index, model, origin, angles ), Run( leaf, first, count, fast ).
// False when the buffer is malformed (what was handed over stays handed).
template <class LEVEL>
bool ReadLevel(const void* p, LEVEL& level)
{
	CWords w(p);
	if (w.Bad())
		return false;
	const int32_t flags = w.Int();
	const int32_t nObjects = w.Int();
	if (w.Bad() || nObjects < 0)
		return false;
	level.Precache((flags & kLevelPrecache) != 0);
	for (int32_t i = 0; i < nObjects; ++i)
	{
		void* pModel = w.Pointer();
		float origin[3], angles[3];
		w.Vec(origin);
		w.Vec(angles);
		if (w.Bad())
			return false;
		level.Object(i, pModel, origin, angles);
	}
	const int32_t nRuns = w.Int();
	if (w.Bad() || nRuns < 0)
		return false;
	for (int32_t i = 0; i < nRuns; ++i)
	{
		const int32_t leaf = w.Int(), first = w.Int(), count = w.Int(), fast = w.Int();
		if (w.Bad())
			return false;
		level.Run(leaf, first, count, fast != 0);
	}
	return true;
}

// BuildLists' answer, handed to VIEW: Proxy( model ), then Model( ordinal,
// alpha, setAngles, angles, colour ). False when the view builds nothing (or
// the buffer is malformed); otherwise the sphere's radius in *pRadius.
template <class VIEW>
bool ReadView(const void* p, VIEW& view, float* pRadius)
{
	CWords w(p);
	if (w.Bad())
		return false;
	const int32_t build = w.Int();
	const float radius = w.Float();
	if (w.Bad() || !build)
		return false;
	const int32_t nProxies = w.Int();
	if (w.Bad() || nProxies < 0)
		return false;
	for (int32_t i = 0; i < nProxies; ++i)
	{
		void* pModel = w.Pointer();
		if (w.Bad())
			return false;
		view.Proxy(pModel);
	}
	const int32_t nModels = w.Int();
	if (w.Bad() || nModels < 0)
		return false;
	for (int32_t i = 0; i < nModels; ++i)
	{
		const int32_t state = w.Int();
		float angles[3], color[3];
		w.Vec(angles);
		w.Vec(color);
		if (w.Bad())
			return false;
		view.Model(i, (unsigned char)(state & 0xff), (state & kModelSetAngles) != 0, angles, color);
	}
	*pRadius = radius;
	return true;
}

// Prepare's answer: its model alphas handed to SINK.ModelAlpha( object,
// alpha ); answers the material (kMaterialNone when there is no draw).
template <class SINK>
int ReadPrepare(const void* p, SINK& sink)
{
	CWords w(p);
	if (w.Bad())
		return kMaterialNone;
	const int32_t material = w.Int();
	const int32_t nAlphas = w.Int();
	if (w.Bad() || nAlphas < 0)
		return kMaterialNone;
	for (int32_t i = 0; i < nAlphas; ++i)
	{
		const int32_t object = w.Int(), alpha = w.Int();
		if (w.Bad())
			return kMaterialNone;
		sink.ModelAlpha(object, (unsigned char)alpha);
	}
	return material;
}

// Emit's commands, carried out by MESH: Begin( quads ), Vertex( xyz, rgba,
// st ), EndDraw(), ModelAlpha( object, alpha ). False when the buffer is
// malformed or ends without kEnd.
template <class MESH>
bool RunEmit(const void* p, MESH& mesh)
{
	CWords w(p);
	if (w.Bad())
		return false;
	for (;;)
	{
		const int32_t op = w.Int();
		if (w.Bad())
			return false;
		switch (op)
		{
			case kEnd:
				return true;
			case kBegin:
			{
				const int32_t quads = w.Int();
				if (w.Bad())
					return false;
				mesh.Begin(quads);
				break;
			}
			case kVertices:
			{
				const int32_t n = w.Int();
				if (w.Bad() || n < 0 || !w.Has(n * kVertexWords))
					return false;
				for (int32_t i = 0; i < n; ++i)
				{
					float xyz[3];
					w.Vec(xyz);
					const uint32_t rgba = (uint32_t)w.Int();
					const unsigned char color[4] = {(unsigned char)(rgba & 0xff), (unsigned char)((rgba >> 8) & 0xff),
													(unsigned char)((rgba >> 16) & 0xff), (unsigned char)(rgba >> 24)};
					float st[2];
					st[0] = w.Float();
					st[1] = w.Float();
					mesh.Vertex(xyz, color, st);
				}
				break;
			}
			case kEndDraw:
				mesh.EndDraw();
				break;
			case kModelAlpha:
			{
				const int32_t object = w.Int(), alpha = w.Int();
				if (w.Bad())
					return false;
				mesh.ModelAlpha(object, (unsigned char)alpha);
				break;
			}
			default:
				return false;
		}
	}
}

} // namespace ss_detail
