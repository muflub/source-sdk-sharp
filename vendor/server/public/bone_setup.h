//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "studio.h"

class CIKContext;

class CBoneBitList : public CBitVec<MAXSTUDIOBONES>
{
public:
};

// ik info
class CIKTarget
{
public:
	void SetOwner( int entindex, const Vector &pos, const QAngle &angles );

	int chain;
	int type;
	// accumulated offset from ideal footplant location
public:
	struct x2
	{
		char *pAttachmentName;
		Vector pos;
		Quaternion q;
	} offset;

private:
	struct x3
	{
		Vector pos;
		Quaternion q;
	} ideal;

public:
	struct x4
	{
		float latched;
		float release;
		float height;
		float floor;
		float radius;
		float flTime;
		float flWeight;
		Vector pos;
		Quaternion q;
		bool onWorld;
	} est; // estimate contact position

	struct x5
	{
		float hipToFoot; // distance from hip
		float hipToKnee; // distance from hip to knee
		float kneeToFoot; // distance from knee to foot
		Vector hip; // location of hip
		Vector closest; // closest valid location from hip to foot that the foot can move to
		Vector knee; // pre-ik location of knee
		Vector farthest; // farthest valid location from hip to foot that the foot can move to
		Vector lowest; // lowest position directly below hip that the foot can drop to
	} trace;

private:
	// internally latched footset, position
	struct x1
	{
		// matrix3x4_t		worldTarget;
		bool bNeedsLatch;
		bool bHasLatch;
		float influence;
		int iFramecounter;
		int owner;
		Vector absOrigin;
		QAngle absAngles;
		Vector pos;
		Quaternion q;
		Vector deltaPos; // acculated error
		Quaternion deltaQ;
		Vector debouncePos;
		Quaternion debounceQ;
	} latched;

	struct x6
	{
		float flTime; // time last error was detected
		float flErrorTime;
		float ramp;
		bool bInError;
	} error;

	friend class CIKContext;
};

struct ikchainresult_t
{
	// accumulated offset from ideal footplant location
	int target;
	Vector pos;
	Quaternion q;
	float flWeight;
};

struct ikcontextikrule_t
{
	int index;

	int type;
	int chain;

	int bone;

	int slot; // iktarget slot.  Usually same as chain.
	float height;
	float radius;
	float floor;
	Vector pos;
	Quaternion q;

	float start; // beginning of influence
	float peak; // start of full influence
	float tail; // end of full influence
	float end; // end of all influence

	float top;
	float drop;

	float commit; // frame footstep target should be committed
	float release; // frame ankle should end rotation from latched orientation

	float flWeight; // processed version of start-end cycle
	float flRuleWeight; // blending weight
	float latched; // does the IK rule use a latched value?
	char *szLabel;

	Vector kneeDir;
	Vector kneePos;

	ikcontextikrule_t() { memset( this, 0, sizeof( *this ) ); }

private:
	// No copy constructors allowed
	ikcontextikrule_t( const ikcontextikrule_t &vOther );
};

class CIKContext
{
public:
	CIKContext();
	void Init( const CStudioHdr *pStudioHdr, const QAngle &angles, const Vector &pos, float flTime, int iFramecounter,
			   int boneMask );

	CUtlVectorFixed<CIKTarget, 12> m_target;

private:
	CStudioHdr const *m_pStudioHdr;

	// virtual IK rules, filtered and combined from each sequence
	CUtlVector<CUtlVector<ikcontextikrule_t>> m_ikChainRule;
	CUtlVector<ikcontextikrule_t> m_ikLock;
	matrix3x4_t m_rootxform;

	int m_iFramecounter;
	float m_flTime;
	int m_boneMask;
};

// void Studio_Movement( const CStudioHdr *pStudioHdr, int iSequence, const float poseParameter[], Vector *pVec );

FORWARD_DECLARE_HANDLE( memhandle_t );

struct bonecacheparams_t
{
	CStudioHdr *pStudioHdr;
	matrix3x4_t *pBoneToWorld;
	float curtime;
	int boneMask;
};

class CBoneCache
{
public:
	unsigned int Size() { return m_size; }

	CBoneCache();

	// was constructor, but placement new is messy wrt memdebug - so cast & init instead
	void Init( const bonecacheparams_t &params, unsigned int size, short *pStudioToCached, short *pCachedToStudio,
			   int cachedBoneCount );

	bool IsValid( float curtime, float dt = 0.1f );

public:
	float m_timeValid;
	int m_boneMask;

private:
	unsigned int m_size;
	unsigned short m_cachedBoneCount;
	unsigned short m_matrixOffset;
	unsigned short m_cachedToStudioOffset;
	unsigned short m_boneOutOffset;
};
