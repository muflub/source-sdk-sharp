//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
// $NoKeywords: $
//
//===========================================================================//

#ifndef STUDIO_H
#define STUDIO_H

#ifdef _WIN32
#pragma once
#endif

#include "basetypes.h"
#include "mathlib/vector2d.h"
#include "mathlib/vector.h"
#include "mathlib/vector4d.h"
#include "mathlib/compressed_vector.h"
#include "tier0/dbg.h"
#include "tier0/threadtools.h"
#include "mathlib/mathlib.h"
#include "utlvector.h"
#include "utlhash.h"
#include "datamap.h"
#include "generichash.h"
#include "localflexcontroller.h"
#include "utlsymbol.h"

#define STUDIO_ENABLE_PERF_COUNTERS

 
// If this is set to 1, then the activity->sequence mapping inside
// the CStudioHdr will not be initialized until the first call to 
// SelectWeightedSequence() or HaveSequenceForActivity(). If set 
// to zero, the mapping will be initialized from CStudioHdr::Init()
// (itself called from the constructor). 
// As of June 4 2007, this was set to 1 because physics, among other
// systems, extemporaneously declares CStudioHdrs inside local function
// scopes without querying their activity/sequence mapping at all.
#define STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE 1

//-----------------------------------------------------------------------------
// forward declarations
//-----------------------------------------------------------------------------

class IMaterial;
class IMesh;
class IMorph;
struct virtualmodel_t;
struct vertexFileHeader_t;
struct thinModelVertices_t;

namespace OptimizedModel
{
	struct StripHeader_t;
}


/*
==============================================================================

STUDIO MODELS

Studio models are position independent, so the cache manager can move them.
==============================================================================
*/

#define STUDIO_VERSION		48

#ifndef _XBOX
	// TODO: tune this
	// TODO: tune this
	// max number of verts that can be flexed per mesh.  TODO: tune this
#else
#define MAXSTUDIOTRIANGLES	25000
#define MAXSTUDIOVERTS		10000
#define	MAXSTUDIOFLEXVERTS	1000
#endif
		// total textures
#define MAXSTUDIOBONES		128		// total bones actually used
#define MAXSTUDIOFLEXDESC	1024	// maximum number of low level flexes (actual morph targets)
#define MAXSTUDIOFLEXCTRL	96		// maximum number of flexcontrollers (input sliders)
#define MAXSTUDIOPOSEPARAM	24
#define MAXSTUDIOBONECTRLS	4


		// NOTE: MUST MATCH MAXSTUDIOBONES

// NOTE!!! : Changing this number also changes the vtx file format!!!!!
#define MAX_NUM_BONES_PER_VERT 3

//Adrian - Remove this when we completely phase out the old event system.


struct mstudiodata_t
{
	int		count;
	int		offset;
};







// If you want to embed a pointer into one of the structures that is serialized, use this class! It will ensure that the pointers consume the 
// right amount of space and work correctly across 32 and 64 bit. It also makes sure that there is no surprise about how large the structure
// is when placed in the middle of another structure, and supports Intel's desired behavior on 64-bit that pointers are always 8-byte aligned.
#pragma pack( push, 4 )
template < class T > 
struct ALIGN4 serializedstudioptr_t
{
	T* m_pData;
#ifndef PLATFORM_64BITS
	int32 padding;
#endif

	serializedstudioptr_t() 
	{ 
		m_pData = nullptr; 
		#if _DEBUG && !defined( PLATFORM_64BITS )
			padding = 0; 
		#endif
	}

	inline operator       T*()             { return m_pData; }
	inline operator const T*() const       { return m_pData; }

	inline       T* operator->( )          { return m_pData; }
	inline const T* operator->( ) const    { return m_pData; }

	inline T* operator=( T* ptr )          { return m_pData = ptr; }
	
} ALIGN4_POST;

#pragma pack( pop )


struct mstudioaxisinterpbone_t
{
	DECLARE_BYTESWAP_DATADESC();
	int				control;// local transformation of this bone used to calc 3 point blend
	int				axis;	// axis to check
	Vector			pos[6];	// X+, X-, Y+, Y-, Z+, Z-
	Quaternion		quat[6];// X+, X-, Y+, Y-, Z+, Z-

	mstudioaxisinterpbone_t() = default;
private:
	// No copy constructors allowed
	mstudioaxisinterpbone_t(const mstudioaxisinterpbone_t& vOther);
};


struct mstudioquatinterpinfo_t
{
	DECLARE_BYTESWAP_DATADESC();
	float			inv_tolerance;	// 1 / radian angle of trigger influence
	Quaternion		trigger;	// angle to match
	Vector			pos;		// new position
	Quaternion		quat;		// new angle

	mstudioquatinterpinfo_t() = default;
private:
	// No copy constructors allowed
	mstudioquatinterpinfo_t(const mstudioquatinterpinfo_t& vOther);
};

struct mstudioquatinterpbone_t
{
	DECLARE_BYTESWAP_DATADESC();
	int				control;// local transformation to check
	int				numtriggers;
	int				triggerindex;

	mstudioquatinterpbone_t() = default;
private:
	// No copy constructors allowed
	mstudioquatinterpbone_t(const mstudioquatinterpbone_t& vOther);
};









		// simple squash and stretch sinusoid "boing"

struct mstudiojigglebone_t
{
	DECLARE_BYTESWAP_DATADESC();

	int				flags;

	// general params
	float			length;					// how from from bone base, along bone, is tip
	float			tipMass;

	// flexible params
	float			yawStiffness;
	float			yawDamping;	
	float			pitchStiffness;
	float			pitchDamping;	
	float			alongStiffness;
	float			alongDamping;	

	// angle constraint
	float			angleLimit;				// maximum deflection of tip in radians
	
	// yaw constraint
	float			minYaw;					// in radians
	float			maxYaw;					// in radians
	float			yawFriction;
	float			yawBounce;
	
	// pitch constraint
	float			minPitch;				// in radians
	float			maxPitch;				// in radians
	float			pitchFriction;
	float			pitchBounce;

	// base spring
	float			baseMass;
	float			baseStiffness;
	float			baseDamping;	
	float			baseMinLeft;
	float			baseMaxLeft;
	float			baseLeftFriction;
	float			baseMinUp;
	float			baseMaxUp;
	float			baseUpFriction;
	float			baseMinForward;
	float			baseMaxForward;
	float			baseForwardFriction;

	// boing
	float			boingImpactSpeed;
	float			boingImpactAngle;
	float			boingDampingRate;
	float			boingFrequency;
	float			boingAmplitude;

private:
	// No copy constructors allowed
	//mstudiojigglebone_t(const mstudiojigglebone_t& vOther);
};

struct mstudioaimatbone_t
{
	DECLARE_BYTESWAP_DATADESC();

	int				parent;
	int				aim;		// Might be bone or attach
	Vector			aimvector;
	Vector			upvector;
	Vector			basepos;

	mstudioaimatbone_t() = default;
private:
	// No copy constructors allowed
	mstudioaimatbone_t(const mstudioaimatbone_t& vOther);
};

// bones
struct mstudiobone_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	int		 			parent;		// parent bone
	int					bonecontroller[6];	// bone controller index, -1 == none

	// default values
	Vector				pos;
	Quaternion			quat;
	RadianEuler			rot;
	// compression scale
	Vector				posscale;
	Vector				rotscale;

	matrix3x4_t			poseToBone;
	Quaternion			qAlignment;
	int					flags;
	int					proctype;
	int					procindex;		// procedural rule
	mutable int			physicsbone;	// index into physically simulated bone
	int					surfacepropidx;	// index into string tablefor property name
	int					contents;		// See BSPFlags.h for the contents flags

	int					unused[8];		// remove as appropriate

	mstudiobone_t() = default;
private:
	// No copy constructors allowed
	mstudiobone_t(const mstudiobone_t& vOther);
};

struct mstudiolinearbone_t	
{
	DECLARE_BYTESWAP_DATADESC();

	int numbones;

	int flagsindex;
	inline int flags( int i ) const { Assert( i >= 0 && i < numbones); return *((int *)(((byte *)this) + flagsindex) + i); };

	int	parentindex;
	inline int parent( int i ) const { Assert( i >= 0 && i < numbones); return *((int *)(((byte *)this) + parentindex) + i); };

	int	posindex;
	inline Vector pos( int i ) const { Assert( i >= 0 && i < numbones); return *((Vector *)(((byte *)this) + posindex) + i); };

	int quatindex;

	int rotindex;

	int posetoboneindex;

	int	posscaleindex;

	int	rotscaleindex;

	int	qalignmentindex;

	int unused[6];

	mstudiolinearbone_t() = default;
private:
	// No copy constructors allowed
	mstudiolinearbone_t(const mstudiolinearbone_t& vOther);
};




//-----------------------------------------------------------------------------
// Component is one of Translate X, Y or Z [0,2] (StudioBoneFlexComponent_t)
//-----------------------------------------------------------------------------
struct mstudioboneflexdrivercontrol_t
{
	DECLARE_BYTESWAP_DATADESC();

	int m_nBoneComponent;		// Bone component that drives flex, StudioBoneFlexComponent_t
	int m_nFlexControllerIndex;	// Flex controller to drive
	float m_flMin;				// Min value of bone component mapped to 0 on flex controller
	float m_flMax;				// Max value of bone component mapped to 1 on flex controller

	mstudioboneflexdrivercontrol_t() = default;
private:
	// No copy constructors allowed
	mstudioboneflexdrivercontrol_t( const mstudioboneflexdrivercontrol_t &vOther );
};


//-----------------------------------------------------------------------------
// Drive flex controllers from bone components
//-----------------------------------------------------------------------------
struct mstudioboneflexdriver_t
{
	DECLARE_BYTESWAP_DATADESC();

	int m_nBoneIndex;			// Bone to drive flex controller
	int m_nControlCount;		// Number of flex controllers being driven
	int m_nControlIndex;		// Index into data where controllers are (relative to this)


	int unused[3];

	mstudioboneflexdriver_t() = default;
private:
	// No copy constructors allowed
	mstudioboneflexdriver_t( const mstudioboneflexdriver_t &vOther );
};



	// bone is physically simulated when physics are active
	// procedural when physics is active
	// bone is always procedurally animated
	// bone aligns to the screen, not constrained in motion.
	// bone aligns to the screen, constrained by it's own axis.


#define BONE_USED_BY_ANYTHING		0x0007FF00
	// bone (or child) is used by a hit box
	// bone (or child) is used by an attachment point
#define BONE_USED_BY_VERTEX_MASK	0x0003FC00
#define BONE_USED_BY_VERTEX_LOD0	0x00000400	// bone (or child) is used by the toplevel model via skinned vertex
	
  





	// bone is available for bone merge to occur against it

#define BONE_USED_BY_VERTEX_AT_LOD(lod) ( BONE_USED_BY_VERTEX_LOD0 << (lod) )


#define MAX_NUM_LODS 8


	// bone can't spin 360 degrees, all interpolation is normalized around a fixed orientation

	// Vector48
	// Quaternion64

// bone controllers
struct mstudiobonecontroller_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					bone;	// -1 == 0
	int					type;	// X, Y, Z, XR, YR, ZR, M
	float				start;
	float				end;
	int					rest;	// byte index value at rest
	int					inputfield;	// 0-3 user set controller, 4 mouth
	int					unused[8];
};

// intersection boxes
struct mstudiobbox_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					bone;
	int					group;				// intersection group
	Vector				bbmin;				// bounding box
	Vector				bbmax;	
	int					szhitboxnameindex;	// offset to the name of the hitbox.
	int					unused[8];


	mstudiobbox_t() = default;

private:
	// No copy constructors allowed
	mstudiobbox_t(const mstudiobbox_t& vOther);
};

// demand loaded sequence groups
struct mstudiomodelgroup_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					szlabelindex;	// textual name
	int					sznameindex;	// file name
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
};


// events
struct mstudioevent_t
{
	DECLARE_BYTESWAP_DATADESC();
	float				cycle;
	int					event;
	int					type;
	char				options[64];

	int					szeventindex;
};



// attachment
struct mstudioattachment_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	unsigned int		flags;
	int					localbone;
	matrix3x4_t			local; // attachment point
	int					unused[8];
};








struct mstudioikerror_t
{
	DECLARE_BYTESWAP_DATADESC();
	Vector		pos;
	Quaternion	q;

	mstudioikerror_t() = default;

private:
	// No copy constructors allowed
	mstudioikerror_t(const mstudioikerror_t& vOther);
};

union mstudioanimvalue_t;

struct mstudiocompressedikerror_t
{
	DECLARE_BYTESWAP_DATADESC();
	float	scale[6];
	short	offset[6];
	mstudiocompressedikerror_t() = default;

private:
	// No copy constructors allowed
	mstudiocompressedikerror_t(const mstudiocompressedikerror_t& vOther);
};

struct mstudioikrule_t
{
	DECLARE_BYTESWAP_DATADESC();
	int			index;

	int			type;
	int			chain;

	int			bone;

	int			slot;	// iktarget slot.  Usually same as chain.
	float		height;
	float		radius;
	float		floor;
	Vector		pos;
	Quaternion	q;

	int			compressedikerrorindex;
	int			unused2;

	int			iStart;
	int			ikerrorindex;

	float		start;	// beginning of influence
	float		peak;	// start of full influence
	float		tail;	// end of full influence
	float		end;	// end of all influence

	float		unused3;	// 
	float		contact;	// frame footstep makes ground concact
	float		drop;		// how far down the foot should drop when reaching for IK
	float		top;		// top of the foot box

	int			unused6;
	int			unused7;
	int			unused8;

	int			szattachmentindex;		// name of world attachment

	int			unused[7];

	mstudioikrule_t() = default;

private:
	// No copy constructors allowed
	mstudioikrule_t(const mstudioikrule_t& vOther);
};


struct mstudioiklock_t
{
	DECLARE_BYTESWAP_DATADESC();
	int			chain;
	float		flPosWeight;
	float		flLocalQWeight;
	int			flags;

	int			unused[4];
};


struct mstudiolocalhierarchy_t
{
	DECLARE_BYTESWAP_DATADESC();
	int			iBone;			// bone being adjusted
	int			iNewParent;		// the bones new parent

	float		start;			// beginning of influence
	float		peak;			// start of full influence
	float		tail;			// end of full influence
	float		end;			// end of all influence

	int			iStart;			// first frame 

	int			localanimindex;

	int			unused[4];
};

	

// animation frames
union mstudioanimvalue_t
{
	struct 
	{
		byte	valid;
		byte	total;
	} num;
	short		value;
};

struct mstudioanim_valueptr_t
{
	DECLARE_BYTESWAP_DATADESC();
	short	offset[3];
};

 // Vector48
#define STUDIO_ANIM_RAWROT	0x02 // Quaternion48
 // mstudioanim_valueptr_t
#define STUDIO_ANIM_ANIMROT	0x08 // mstudioanim_valueptr_t

#define STUDIO_ANIM_RAWROT2	0x20 // Quaternion64


// per bone per animation DOF and weight pointers
struct mstudioanim_t
{
	DECLARE_BYTESWAP_DATADESC();
	byte				bone;
	byte				flags;		// weighing options

	// valid for animating data only
	inline byte				*pData( void ) const { return (((byte *)this) + sizeof( struct mstudioanim_t )); };

	// valid if animation unvaring over timeline
	inline Quaternion48		*pQuat48( void ) const { return (Quaternion48 *)(pData()); };
	inline Quaternion64		*pQuat64( void ) const { return (Quaternion64 *)(pData()); };

	short				nextoffset;
	inline mstudioanim_t	*pNext( void ) const { if (nextoffset != 0) return  (mstudioanim_t *)(((byte *)this) + nextoffset); else return NULL; };
};

struct mstudiomovement_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					endframe;				
	int					motionflags;
	float				v0;			// velocity at start of block
	float				v1;			// velocity at end of block
	float				angle;		// YAW rotation at end of this blocks movement
	Vector				vector;		// movement vector relative to this blocks initial angle
	Vector				position;	// relative to start of animation???

	mstudiomovement_t() = default;
private:
	// No copy constructors allowed
	mstudiomovement_t(const mstudiomovement_t& vOther);
};

struct studiohdr_t;

// used for piecewise loading of animation data
struct mstudioanimblock_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					datastart;
	int					dataend;
};

struct mstudioanimsections_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					animblock;
	int					animindex;
};

struct mstudioanimdesc_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					baseptr;

	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }

	float				fps;		// frames per second	
	int					flags;		// looping/non-looping flags

	int					numframes;

	// piecewise movement
	int					nummovements;
	int					movementindex;

	int					unused1[6];			// remove as appropriate (and zero if loading older versions)	

	int					animblock;
	int					animindex;	 // non-zero when anim data isn't in sections

	int					numikrules;
	int					ikruleindex;	// non-zero when IK data is stored in the mdl
	int					animblockikruleindex; // non-zero when IK data is stored in animblock file

	int					numlocalhierarchy;
	int					localhierarchyindex;

	int					sectionindex;
	int					sectionframes; // number of frames used in each fast lookup section, zero if not used

	short				zeroframespan;	// frames per span
	short				zeroframecount; // number of spans
	int					zeroframeindex;
	mutable float		zeroframestalltime;		// saved during read stalls

	mstudioanimdesc_t() = default;
private:
	// No copy constructors allowed
	mstudioanimdesc_t(const mstudioanimdesc_t& vOther);
};

struct mstudioikrule_t;

struct mstudioautolayer_t
{
	DECLARE_BYTESWAP_DATADESC();
//private:
	short				iSequence;
	short				iPose;
//public:
	int					flags;
	float				start;	// beginning of influence
	float				peak;	// start of full influence
	float				tail;	// end of full influence
	float				end;	// end of all influence
};

struct mstudioactivitymodifier_t
{
	DECLARE_BYTESWAP_DATADESC();
	
	int					sznameindex;
	inline char			*pszName() { return (sznameindex) ? (char *)(((byte *)this) + sznameindex ) : NULL; }
};

// sequence descriptions
struct mstudioseqdesc_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					baseptr;

	int					szlabelindex;

	int					szactivitynameindex;

	int					flags;		// looping/non-looping flags

	int					activity;	// initialized at loadtime to game DLL values
	int					actweight;

	int					numevents;
	int					eventindex;
	
	Vector				bbmin;		// per sequence bounding box
	Vector				bbmax;		

	int					numblends;

	// Index into array of shorts which is groupsize[0] x groupsize[1] in length
	int					animindexindex;


	int					movementindex;	// [blend] float array for blended movement
	int					groupsize[2];
	int					paramindex[2];	// X, Y, Z, XR, YR, ZR
	float				paramstart[2];	// local (0..1) starting value
	float				paramend[2];	// local (0..1) ending value
	int					paramparent;

	float				fadeintime;		// ideal cross fate in time (0.2 default)
	float				fadeouttime;	// ideal cross fade out time (0.2 default)

	int					localentrynode;		// transition node at entry
	int					localexitnode;		// transition node at exit
	int					nodeflags;		// transition rules

	float				entryphase;		// used to match entry gait
	float				exitphase;		// used to match exit gait
	
	float				lastframe;		// frame that should generation EndOfSequence

	int					nextseq;		// auto advancing sequences
	int					pose;			// index of delta animation between end and nextseq

	int					numikrules;

	int					numautolayers;	//
	int					autolayerindex;

	int					weightlistindex;
	inline float		*pBoneweight( int i ) const { return ((float *)(((byte *)this) + weightlistindex) + i); };
	inline float		weight( int i ) const { return *(pBoneweight( i)); };

	// FIXME: make this 2D instead of 2x1D arrays
	int					posekeyindex;
	float				*pPoseKey( int iParam, int iAnim ) const { return (float *)(((byte *)this) + posekeyindex) + iParam * groupsize[0] + iAnim; }

	int					numiklocks;
	int					iklockindex;

	// Key values
	int					keyvalueindex;
	int					keyvaluesize;

	int					cycleposeindex;		// index of pose parameter to use as cycle index

	int					activitymodifierindex;
	int					numactivitymodifiers;

	int					unused[5];		// remove/add as appropriate (grow back to 8 ints on version change!)

	mstudioseqdesc_t() = default;
private:
	// No copy constructors allowed
	mstudioseqdesc_t(const mstudioseqdesc_t& vOther);
};


struct mstudioposeparamdesc_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	int					flags;	// ????
	float				start;	// starting value
	float				end;	// ending value
	float				loop;	// looping range, 0 for no looping, 360 for rotations, etc.
};

struct mstudioflexdesc_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					szFACSindex;
};



struct mstudioflexcontroller_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sztypeindex;
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	mutable int			localToGlobal;	// remapped at load time to master list
	float				min;
	float				max;
};


enum FlexControllerRemapType_t
{
	FLEXCONTROLLER_REMAP_PASSTHRU = 0,
	FLEXCONTROLLER_REMAP_2WAY,	// Control 0 -> ramps from 1-0 from 0->0.5. Control 1 -> ramps from 0-1 from 0.5->1
	FLEXCONTROLLER_REMAP_NWAY,	// StepSize = 1 / (control count-1) Control n -> ramps from 0-1-0 from (n-1)*StepSize to n*StepSize to (n+1)*StepSize. A second control is needed to specify amount to use 
	FLEXCONTROLLER_REMAP_EYELID
};


class CStudioHdr;
struct mstudioflexcontrollerui_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }

	// These are used like a union to save space
	// Here are the possible configurations for a UI controller
	//
	// SIMPLE NON-STEREO:	0: control	1: unused	2: unused
	// STEREO:				0: left		1: right	2: unused
	// NWAY NON-STEREO:		0: control	1: unused	2: value
	// NWAY STEREO:			0: left		1: right	2: value

	int					szindex0;
	int					szindex1;
	int					szindex2;

	inline const mstudioflexcontroller_t *pController( void ) const
	{
		return !stereo ? (mstudioflexcontroller_t *)( (char *)this + szindex0 ) : NULL;
	}

	inline const mstudioflexcontroller_t *pLeftController( void ) const
	{
		return stereo ? (mstudioflexcontroller_t *)( (char *)this + szindex0 ) : NULL;
	}

	inline const mstudioflexcontroller_t *pRightController( void ) const
	{
		return stereo ? (mstudioflexcontroller_t *)( (char *)this + szindex1 ): NULL;
	}

	inline const mstudioflexcontroller_t *pNWayValueController( void ) const
	{
		return remaptype == FLEXCONTROLLER_REMAP_NWAY ? (mstudioflexcontroller_t *)( (char *)this + szindex2 ) : NULL;
	}

	// Number of controllers this ui description contains, 1, 2 or 3
	inline int			Count() const { return ( stereo ? 2 : 1 ) + ( remaptype == FLEXCONTROLLER_REMAP_NWAY ? 1 : 0 ); }

	unsigned char		remaptype;	// See the FlexControllerRemapType_t enum
	bool				stereo;		// Is this a stereo control?
	byte				unused[2];
};


// this is the memory image of vertex anims (16-bit fixed point)
struct mstudiovertanim_t
{
	DECLARE_BYTESWAP_DATADESC();
	unsigned short		index;
	byte				speed;	// 255/max_length_in_flex
	byte				side;	// 255/left_right

protected:
	// JasonM changing this type a lot, to prefer fixed point 16 bit...
	union
	{
		short			delta[3];
		float16			flDelta[3];
	};

	union
	{
		short			ndelta[3];
		float16			flNDelta[3];
	};

public:



	class CSortByIndex
	{
	public:
		bool operator()(const mstudiovertanim_t &left, const mstudiovertanim_t & right)const
		{
			return left.index < right.index;
		}
	};
	friend class CSortByIndex;

	mstudiovertanim_t() = default;
//private:
// No copy constructors allowed, but it's needed for std::sort()
//	mstudiovertanim_t(const mstudiovertanim_t& vOther);
};


// this is the memory image of vertex anims (16-bit fixed point)
struct mstudiovertanim_wrinkle_t : public mstudiovertanim_t
{
	DECLARE_BYTESWAP_DATADESC();

	short	wrinkledelta;




};


enum StudioVertAnimType_t
{
	STUDIO_VERT_ANIM_NORMAL = 0,
	STUDIO_VERT_ANIM_WRINKLE,
};

struct mstudioflex_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					flexdesc;	// input value

	float				target0;	// zero
	float				target1;	// one
	float				target2;	// one
	float				target3;	// zero

	int					numverts;
	int					vertindex;



	int					flexpair;	// second flex desc
	unsigned char		vertanimtype;	// See StudioVertAnimType_t
	unsigned char		unusedchar[3];
	int					unused[6];
};


struct mstudioflexop_t
{
	DECLARE_BYTESWAP_DATADESC();
	int		op;
	union 
	{
		int		index;
		float	value;
	} d;
};

struct mstudioflexrule_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					flex;
	int					numops;
	int					opindex;
};

// 16 bytes
struct mstudioboneweight_t
{
	DECLARE_BYTESWAP_DATADESC();
	float	weight[MAX_NUM_BONES_PER_VERT];
	char	bone[MAX_NUM_BONES_PER_VERT]; 
	byte	numbones;

//	byte	material;
//	short	firstref;
//	short	lastref;
};

// NOTE: This is exactly 48 bytes
struct mstudiovertex_t
{
	DECLARE_BYTESWAP_DATADESC();
	mstudioboneweight_t	m_BoneWeights;
	Vector				m_vecPosition;
	Vector				m_vecNormal;
	Vector2D			m_vecTexCoord;

	mstudiovertex_t() = default;

private:
	// No copy constructors allowed
	mstudiovertex_t(const mstudiovertex_t& vOther);
};

// skin info
struct mstudiotexture_t
{
	DECLARE_BYTESWAP_DATADESC();
	int						sznameindex;
	inline char * const		pszName( void ) const { return ((char *)this) + sznameindex; }
	int						flags;
	int						used;
    int						unused1;

	mutable IMaterial		*material;  // fixme: this needs to go away . .isn't used by the engine, but is used by studiomdl
	mutable void			*clientmaterial;	// gary, replace with client material pointer if used
	
#ifdef PLATFORM_64BITS
	int						unused[8];
#else
	int						unused[10];
#endif
};

// eyeball
struct mstudioeyeball_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	int		bone;
	Vector	org;
	float	zoffset;
	float	radius;
	Vector	up;
	Vector	forward;
	int		texture;

	int		unused1;
	float	iris_scale;
	int		unused2;

	int		upperflexdesc[3];	// index of raiser, neutral, and lowerer flexdesc that is set by flex controllers
	int		lowerflexdesc[3];
	float	uppertarget[3];		// angle (radians) of raised, neutral, and lowered lid positions
	float	lowertarget[3];

	int		upperlidflexdesc;	// index of flex desc that actual lid flexes look to
	int		lowerlidflexdesc;
	int		unused[4];			// These were used before, so not guaranteed to be 0
	bool	m_bNonFACS;			// Never used before version 44
	char	unused3[3];
	int		unused4[7];

	mstudioeyeball_t() = default;
private:
	// No copy constructors allowed
	mstudioeyeball_t(const mstudioeyeball_t& vOther);
};


// ikinfo
struct mstudioiklink_t
{
	DECLARE_BYTESWAP_DATADESC();
	int		bone;
	Vector	kneeDir;	// ideal bending direction (per link, if applicable)
	Vector	unused0;	// unused

	mstudioiklink_t() = default;
private:
	// No copy constructors allowed
	mstudioiklink_t(const mstudioiklink_t& vOther);
};

struct mstudioikchain_t
{
	DECLARE_BYTESWAP_DATADESC();
	int				sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	int				linktype;
	int				numlinks;
	int				linkindex;
	inline mstudioiklink_t *pLink( int i ) const { return (mstudioiklink_t *)(((byte *)this) + linkindex) + i; };
	// FIXME: add unused entries
};


struct mstudioiface_t
{
	unsigned short a, b, c;		// Indices to vertices
};


struct mstudiomodel_t;

struct mstudio_modelvertexdata_t
{
	DECLARE_BYTESWAP_DATADESC();
	Vector				*Position( int i ) const;
	Vector				*Normal( int i ) const;
	Vector4D			*TangentS( int i ) const;
	Vector2D			*Texcoord( int i ) const;
	mstudioboneweight_t	*BoneWeights( int i ) const;
	mstudiovertex_t		*Vertex( int i ) const;
	bool				HasTangentData( void ) const;
	int					GetGlobalVertexIndex( int i ) const;
	int					GetGlobalTangentIndex( int i ) const;

	// base of external vertex data stores
	const void			*pVertexData;
	const void			*pTangentData;
};
#ifdef PLATFORM_64BITS
// 64b - match 32-bit packing
#pragma pack( push, 4 )
#endif

struct mstudio_meshvertexdata_t
{
	DECLARE_BYTESWAP_DATADESC();
	Vector				*Normal( int i ) const;
	mstudiovertex_t		*Vertex( int i ) const;
	int					GetModelVertexIndex( int i ) const;
	int					GetGlobalVertexIndex( int i ) const;

	// indirection to this mesh's model's vertex data
#ifndef PLATFORM_64BITS
	const mstudio_modelvertexdata_t	*modelvertexdata;
#else
	int unused_modelvertexdata;
#endif

	// used for fixup calcs when culling top level lods
	// expected number of mesh verts at desired lod
	int					numLODVertexes[MAX_NUM_LODS];

#ifdef PLATFORM_64BITS
	serializedstudioptr_t< const mstudio_modelvertexdata_t >	modelvertexdata;
#endif
};

struct mstudiomesh_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					material;

	int					modelindex;
	mstudiomodel_t *pModel() const; 

	int					numvertices;		// number of unique vertices/normals/texcoords
	int					vertexoffset;		// vertex mstudiovertex_t

	
	int					numflexes;			// vertex animation
	int					flexindex;

	// special codes for material operations
	int					materialtype;
	int					materialparam;

	// a unique ordinal for this mesh
	int					meshid;

	Vector				center;

	mstudio_meshvertexdata_t vertexdata;

#ifdef PLATFORM_64BITS
	int					unused[6]; // remove as appropriate
#else
	int					unused[8]; // remove as appropriate
#endif

	mstudiomesh_t() = default;
private:
	// No copy constructors allowed
	mstudiomesh_t(const mstudiomesh_t& vOther);
};

// studio models
struct mstudiomodel_t
{
	DECLARE_BYTESWAP_DATADESC();
	inline const char * pszName( void ) const { return name; }
	char				name[64];

	int					type;

	float				boundingradius;

	int					nummeshes;	
	int					meshindex;
	inline mstudiomesh_t *pMesh( int i ) const { return (mstudiomesh_t *)(((byte *)this) + meshindex) + i; };

	// cache purposes
	int					numvertices;		// number of unique vertices/normals/texcoords
	int					vertexindex;		// vertex Vector
	int					tangentsindex;		// tangents Vector

	// These functions are defined in application-specific code:
	const vertexFileHeader_t			*CacheVertexData(			void *pModelData );

	// Access thin/fat mesh vertex data (only one will return a non-NULL result)
	const mstudio_modelvertexdata_t		*GetVertexData(		void *pModelData = NULL );
	const thinModelVertices_t			*GetThinVertexData(	void *pModelData = NULL );

	int					numattachments;
	int					attachmentindex;

	int					numeyeballs;
	int					eyeballindex;

	mstudio_modelvertexdata_t vertexdata;

#ifdef PLATFORM_64BITS
	int					unused[6];		// mstudio_modelvertexdata_t has 2 naked ptrs
#else
	int					unused[8];		// remove as appropriate
#endif
};

#ifdef PLATFORM_64BITS
#pragma pack( pop )
#endif

inline bool mstudio_modelvertexdata_t::HasTangentData( void ) const 
{
	return (pTangentData != NULL);
}

inline int mstudio_modelvertexdata_t::GetGlobalVertexIndex( int i ) const
{
	mstudiomodel_t *modelptr = (mstudiomodel_t *)((byte *)this - offsetof(mstudiomodel_t, vertexdata));
	Assert( ( modelptr->vertexindex % sizeof( mstudiovertex_t ) ) == 0 );
	return ( i + ( modelptr->vertexindex / sizeof( mstudiovertex_t ) ) );
}

inline int mstudio_modelvertexdata_t::GetGlobalTangentIndex( int i ) const
{
	mstudiomodel_t *modelptr = (mstudiomodel_t *)((byte *)this - offsetof(mstudiomodel_t, vertexdata));
	Assert( ( modelptr->tangentsindex % sizeof( Vector4D ) ) == 0 );
	return ( i + ( modelptr->tangentsindex / sizeof( Vector4D ) ) );
}

inline mstudiovertex_t *mstudio_modelvertexdata_t::Vertex( int i ) const 
{
	return (mstudiovertex_t *)pVertexData + GetGlobalVertexIndex( i );
}

inline Vector *mstudio_modelvertexdata_t::Position( int i ) const 
{
	return &Vertex(i)->m_vecPosition;
}

inline Vector *mstudio_modelvertexdata_t::Normal( int i ) const 
{ 
	return &Vertex(i)->m_vecNormal;
}

inline Vector4D *mstudio_modelvertexdata_t::TangentS( int i ) const 
{
	// NOTE: The tangents vector is 16-bytes in a separate array
	// because it only exists on the high end, and if I leave it out
	// of the mstudiovertex_t, the vertex is 64-bytes (good for low end)
	return (Vector4D *)pTangentData + GetGlobalTangentIndex( i );
}

inline Vector2D *mstudio_modelvertexdata_t::Texcoord( int i ) const 
{ 
	return &Vertex(i)->m_vecTexCoord;
}

inline mstudioboneweight_t *mstudio_modelvertexdata_t::BoneWeights( int i ) const 
{
	return &Vertex(i)->m_BoneWeights;
}

inline mstudiomodel_t *mstudiomesh_t::pModel() const 
{ 
	return (mstudiomodel_t *)(((byte *)this) + modelindex); 
}




inline int mstudio_meshvertexdata_t::GetModelVertexIndex( int i ) const
{
	mstudiomesh_t *meshptr = (mstudiomesh_t *)((byte *)this - offsetof(mstudiomesh_t,vertexdata)); 
	return meshptr->vertexoffset + i;
}

inline int mstudio_meshvertexdata_t::GetGlobalVertexIndex( int i ) const
{
	return modelvertexdata->GetGlobalVertexIndex( GetModelVertexIndex( i ) );
}


inline Vector *mstudio_meshvertexdata_t::Normal( int i ) const 
{
	return modelvertexdata->Normal( GetModelVertexIndex( i ) ); 
};




inline mstudiovertex_t *mstudio_meshvertexdata_t::Vertex( int i ) const
{
	return modelvertexdata->Vertex( GetModelVertexIndex( i ) );
}



// ----------------------------------------------------------
// runtime stuff
// ----------------------------------------------------------

struct studiomeshgroup_t
{
	IMesh			*m_pMesh;
	int				m_NumStrips;
	int				m_Flags;		// see studiomeshgroupflags_t
	OptimizedModel::StripHeader_t	*m_pStripData;
	unsigned short	*m_pGroupIndexToMeshIndex;
	int				m_NumVertices;
	int				*m_pUniqueTris;	// for performance measurements
	unsigned short	*m_pIndices;
	bool			m_MeshNeedsRestore;
	short			m_ColorMeshID;
	IMorph			*m_pMorph;

};


// studio model data
struct studiomeshdata_t
{
	int					m_NumGroup;
	studiomeshgroup_t*	m_pMeshGroup;
};

struct studioloddata_t
{
	// not needed - this is really the same as studiohwdata_t.m_NumStudioMeshes
	//int					m_NumMeshes; 
	studiomeshdata_t	*m_pMeshData; // there are studiohwdata_t.m_NumStudioMeshes of these.
	float				m_SwitchPoint;
	// one of these for each lod since we can switch to simpler materials on lower lods.
	int					numMaterials; 
	IMaterial			**ppMaterials; /* will have studiohdr_t.numtextures elements allocated */
	// hack - this needs to go away.
	int					*pMaterialFlags; /* will have studiohdr_t.numtextures elements allocated */

	// For decals on hardware morphing, we must actually do hardware skinning
	// For this to work, we have to hope that the total # of bones used by
	// hw flexed verts is < than the max possible for the dx level we're running under
	int					*m_pHWMorphDecalBoneRemap;
	int					m_nDecalBoneCount;
};

struct studiohwdata_t
{
	int					m_RootLOD;	// calced and clamped, nonzero for lod culling
	int					m_NumLODs;
	studioloddata_t		*m_pLODs;
	int					m_NumStudioMeshes;

};

// ----------------------------------------------------------
// ----------------------------------------------------------

// body part index
struct mstudiobodyparts_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const pszName( void ) const { return ((char *)this) + sznameindex; }
	int					nummodels;
	int					base;
	int					modelindex; // index into models array
	inline mstudiomodel_t *pModel( int i ) const { return (mstudiomodel_t *)(((byte *)this) + modelindex) + i; };
};


struct mstudiomouth_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					bone;
	Vector				forward;
	int					flexdesc;

	mstudiomouth_t() = default;
private:
	// No copy constructors allowed
	mstudiomouth_t(const mstudiomouth_t& vOther);
};

struct mstudiohitboxset_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					sznameindex;
	inline char * const	pszName( void ) const { return ((char *)this) + sznameindex; }
	int					numhitboxes;
	int					hitboxindex;
	inline mstudiobbox_t *pHitbox( int i ) const { return (mstudiobbox_t *)(((byte *)this) + hitboxindex) + i; };
};


//-----------------------------------------------------------------------------
// Src bone transforms are transformations that will convert .dmx or .smd-based animations into .mdl-based animations
// NOTE: The operation you should apply is: pretransform * bone transform * posttransform
//-----------------------------------------------------------------------------
struct mstudiosrcbonetransform_t
{
	DECLARE_BYTESWAP_DATADESC();

	int			sznameindex;
	inline const char *pszName( void ) const { return ((char *)this) + sznameindex; }
	matrix3x4_t	pretransform;	
	matrix3x4_t	posttransform;	
};


// ----------------------------------------------------------
// Purpose: Load time results on model compositing
// ----------------------------------------------------------

class virtualgroup_t 
{
public:
	virtualgroup_t( void ) { cache = NULL; };
	// tool dependant.  In engine this is a model_t, in tool it's a direct pointer
	void *cache;

	CUtlVector< int > boneMap;				// maps global bone to local bone
	CUtlVector< int > masterBone;			// maps local bone to global bone
	CUtlVector< int > masterSeq;			// maps local sequence to master sequence
	CUtlVector< int > masterAnim;			// maps local animation to master animation
	CUtlVector< int > masterAttachment;	// maps local attachment to global
	CUtlVector< int > masterPose;			// maps local pose parameter to global
	CUtlVector< int > masterNode;			// maps local transition nodes to global
};

struct virtualsequence_t
{
#ifdef _XBOX
	short flags;
	short activity;
	short group;
	short index;
#else
	int	flags;
	int activity;
	int group;
	int index;
#endif
};

struct virtualgeneric_t
{
#ifdef _XBOX
	short group;
	short index;
#else
	int group;
	int index;
#endif
};


struct virtualmodel_t
{


    CThreadFastMutex m_Lock;

	CUtlVector< virtualsequence_t > m_seq;
	CUtlVector< virtualgeneric_t > m_anim;
	CUtlVector< virtualgeneric_t > m_attachment;
	CUtlVector< virtualgeneric_t > m_pose;
	CUtlVector< virtualgroup_t > m_group;
	CUtlVector< virtualgeneric_t > m_node;
	CUtlVector< virtualgeneric_t > m_iklock;
	CUtlVector< unsigned short > m_autoplaySequences;
};

// 'thin' vertex data, used to do model decals (see Studio_CreateThinVertexes())
struct thinModelVertices_t
{
	void Init( int numBoneInfluences, Vector *positions, unsigned short *normals, float *boneWeights, char *boneIndices )
	{
		Assert( positions != NULL );
		Assert( normals   != NULL );
		Assert( ( numBoneInfluences >= 0 ) && ( numBoneInfluences <= 3 ) );
		Assert( numBoneInfluences > 0 ? !!boneIndices : !boneIndices );
		Assert( numBoneInfluences > 1 ? !!boneWeights : !boneWeights );
		m_numBoneInfluences	= numBoneInfluences;
		m_vecPositions		= positions;
		m_vecNormals		= normals;
		m_boneWeights		= boneWeights;
		m_boneIndices		= boneIndices;
	}










private:
	void GetPosition( int vertIndex, Vector *pPosition ) const
	{
		Assert( pPosition );
		Assert( m_vecPositions );
		*pPosition = m_vecPositions[ vertIndex ];
	}

	void GetNormal( int vertIndex, Vector *pNormal ) const
	{
		Assert( pNormal );
		Assert( m_vecNormals );
		unsigned int packedNormal = 0x0000FFFF & m_vecNormals[ vertIndex ];
		UnpackNormal_UBYTE4( &packedNormal, pNormal->Base() );
	}

	void GetBoneWeights( int vertIndex, mstudioboneweight_t *pBoneWeights ) const
	{
		Assert( pBoneWeights );
		Assert( ( m_numBoneInfluences <= 1 ) || ( m_boneWeights != NULL ) );
		Assert( ( m_numBoneInfluences <= 0 ) || ( m_boneIndices != NULL ) );
		int    numStoredWeights = max( 0, ( m_numBoneInfluences - 1 ) );
		float *pBaseWeight	= m_boneWeights + vertIndex*numStoredWeights;
		char  *pBaseIndex	= m_boneIndices + vertIndex*m_numBoneInfluences;
		float  sum			= 0.0f;
		for (int i = 0;i < MAX_NUM_BONES_PER_VERT;i++)
		{
			if ( i < ( m_numBoneInfluences - 1 ) )
				pBoneWeights->weight[i] = pBaseWeight[i];
			else
				pBoneWeights->weight[i] = 1.0f - sum;
			sum += pBoneWeights->weight[i];

			pBoneWeights->bone[i] = ( i < m_numBoneInfluences ) ? pBaseIndex[i] : 0;
		}

		// Treat 'zero weights' as '100% binding to bone zero':
		pBoneWeights->numbones = m_numBoneInfluences ? m_numBoneInfluences : 1;
	}

	int				m_numBoneInfluences;// Number of bone influences per vertex, N
	float			*m_boneWeights;		// This array stores (N-1) weights per vertex (unless N is zero)
	char			*m_boneIndices;		// This array stores N indices per vertex
	Vector			*m_vecPositions;
	unsigned short	*m_vecNormals;		// Normals are compressed into 16 bits apiece (see PackNormal_UBYTE4() )
};

// ----------------------------------------------------------
// Studio Model Vertex Data File
// Position independent flat data for cache manager
// ----------------------------------------------------------

// little-endian "IDSV"
#define MODEL_VERTEX_FILE_ID		(('V'<<24)+('S'<<16)+('D'<<8)+'I')
#define MODEL_VERTEX_FILE_VERSION	4
// this id (IDCV) is used once the vertex data has been compressed (see CMDLCache::CreateThinVertexes)
#define MODEL_VERTEX_FILE_THIN_ID	(('V'<<24)+('C'<<16)+('D'<<8)+'I')

struct vertexFileHeader_t
{
	DECLARE_BYTESWAP_DATADESC();
	int		id;								// MODEL_VERTEX_FILE_ID
	int		version;						// MODEL_VERTEX_FILE_VERSION
	int		checksum;						// same as studiohdr_t, ensures sync
	int		numLODs;						// num of valid lods
	int		numLODVertexes[MAX_NUM_LODS];	// num verts for desired root lod
	int		numFixups;						// num of vertexFileFixup_t
	int		fixupTableStart;				// offset from base to fixup table
	int		vertexDataStart;				// offset from base to vertex block
	int		tangentDataStart;				// offset from base to tangent block

public:

	// Accessor to fat vertex data
	const mstudiovertex_t *GetVertexData() const
	{
		if ( ( id == MODEL_VERTEX_FILE_ID ) && ( vertexDataStart != 0 ) )
			return ( mstudiovertex_t * ) ( vertexDataStart + (byte *)this );
		else
			return NULL;
	}
	// Accessor to (fat) tangent vertex data (tangents aren't stored in compressed data)
	const Vector4D *GetTangentData() const
	{
		if ( ( id == MODEL_VERTEX_FILE_ID ) && ( tangentDataStart != 0 ) )
			return ( Vector4D * ) ( tangentDataStart + (byte *)this );
		else
			return NULL;
	}
	// Accessor to thin vertex data
	const  thinModelVertices_t *GetThinVertexData() const
	{
		if ( ( id == MODEL_VERTEX_FILE_THIN_ID ) && ( vertexDataStart != 0 ) )
			return ( thinModelVertices_t * ) ( vertexDataStart + (byte *)this );
		else
			return NULL;
	}
};

// model vertex data accessor (defined here so vertexFileHeader_t can be used)
inline const mstudio_modelvertexdata_t * mstudiomodel_t::GetVertexData( void *pModelData )
{
	const vertexFileHeader_t * pVertexHdr = CacheVertexData( pModelData );
	if ( !pVertexHdr )
	{
		vertexdata.pVertexData = NULL;
		vertexdata.pTangentData = NULL;
		return NULL;
	}

	vertexdata.pVertexData  = pVertexHdr->GetVertexData();
	vertexdata.pTangentData = pVertexHdr->GetTangentData();

	if ( !vertexdata.pVertexData )
		return NULL;

	return &vertexdata;
}

// model thin vertex data accessor (defined here so vertexFileHeader_t can be used)
inline const thinModelVertices_t * mstudiomodel_t::GetThinVertexData( void *pModelData )
{
	const vertexFileHeader_t * pVertexHdr = CacheVertexData( pModelData );
	if ( !pVertexHdr )
		return NULL;

	return pVertexHdr->GetThinVertexData();
}

// apply sequentially to lod sorted vertex and tangent pools to re-establish mesh order
struct vertexFileFixup_t
{
	DECLARE_BYTESWAP_DATADESC();
	int		lod;				// used to skip culled root lod
	int		sourceVertexID;		// absolute index from start of vertex/tangent blocks
	int		numVertexes;
};

// This flag is set if no hitbox information was specified


// NOTE:  This flag is set at loadtime, not mdl build time so that we don't have to rebuild
// models when we change materials.


// Use this when there are translucent parts to the model but we're not going to sort it 


// Use this when we want to render the opaque parts during the opaque pass
// and the translucent parts during the translucent pass


// This is set any time the .qc files has $staticprop in it
// Means there's no bones and no transforms
#define STUDIOHDR_FLAGS_STATIC_PROP							0x00000010

// NOTE:  This flag is set at loadtime, not mdl build time so that we don't have to rebuild
// models when we change materials.


// This flag is set by studiomdl.exe if a separate "$shadowlod" entry was present
//  for the .mdl (the shadow lod is the last entry in the lod list if present)


// NOTE:  This flag is set at loadtime, not mdl build time so that we don't have to rebuild
// models when we change materials.


// NOTE:  This flag is set when we should use the actual materials on the shadow LOD
// instead of overriding them with the default one (necessary for translucent shadows)


// NOTE:  This flag is set when we should use the actual materials on the shadow LOD
// instead of overriding them with the default one (necessary for translucent shadows)




// NOTE:  This flag is set at mdl build time


// NOTE:  The npc will lengthen the viseme check to always include two phonemes


// This flag is set when the .qc has $constantdirectionallight in it
// If set, we use constantdirectionallightdot to calculate light intensity
// rather than the normal directional dot product
// only valid if STUDIOHDR_FLAGS_STATIC_PROP is also set
#define STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT		0x00002000

// Flag to mark delta flexes as already converted from disk format to memory format


// Indicates the studiomdl was built in preview mode


// Ambient boost (runtime flag)


// Don't cast shadows from this model (useful on first-person models)


// alpha textures should cast shadows in vrad on this model (ONLY prop_static!)



// flagged on load to indicate no animation events on this model
#define STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE			0x00200000

// NOTE! Next time we up the .mdl file format, remove studiohdr2_t
// and insert all fields in this structure into studiohdr_t.
struct studiohdr2_t
{
	// NOTE: For forward compat, make sure any methods in this struct
	// are also available in studiohdr_t so no leaf code ever directly references
	// a studiohdr2_t structure
	DECLARE_BYTESWAP_DATADESC();
	int numsrcbonetransform;
	int srcbonetransformindex;

	int	illumpositionattachmentindex;
	inline int			IllumPositionAttachmentIndex() const { return illumpositionattachmentindex; }

	float flMaxEyeDeflection;
	inline float		MaxEyeDeflection() const { return flMaxEyeDeflection != 0.0f ? flMaxEyeDeflection : 0.866f; } // default to cos(30) if not set

	int linearboneindex;
	inline mstudiolinearbone_t *pLinearBones() const { return (linearboneindex) ? (mstudiolinearbone_t *)(((byte *)this) + linearboneindex) : NULL; }

	int sznameindex;
	inline char *pszName() { return (sznameindex) ? (char *)(((byte *)this) + sznameindex ) : NULL; }

	int m_nBoneFlexDriverCount;
	int m_nBoneFlexDriverIndex;
	inline mstudioboneflexdriver_t *pBoneFlexDriver( int i ) const { Assert( i >= 0 && i < m_nBoneFlexDriverCount ); return (mstudioboneflexdriver_t *)(((byte *)this) + m_nBoneFlexDriverIndex) + i; }

#ifdef PLATFORM_64BITS
	mutable serializedstudioptr_t< void	> virtualModel;
	mutable serializedstudioptr_t< void	> animblockModel;

	serializedstudioptr_t< void> pVertexBase;
	serializedstudioptr_t< void> pIndexBase;

	int reserved[56 - 4 * sizeof( serializedstudioptr_t< void > ) / sizeof( int ) ];
#else
	int reserved[56];
#endif
};

struct studiohdr_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					id;
	int					version;

	int					checksum;		// this has to be the same in the phy and vtx files to load!

	inline const char *	pszName( void ) const { if (studiohdr2index && pStudioHdr2()->pszName()) return pStudioHdr2()->pszName(); else return name; }
	char				name[64];
	int					length;


	Vector				eyeposition;	// ideal eye position

	Vector				illumposition;	// illumination center
	
	Vector				hull_min;		// ideal movement hull size
	Vector				hull_max;			

	Vector				view_bbmin;		// clipping bounding box
	Vector				view_bbmax;		

	int					flags;

	int					numbones;			// bones
	int					boneindex;
	inline mstudiobone_t *pBone( int i ) const { Assert( i >= 0 && i < numbones); return (mstudiobone_t *)(((byte *)this) + boneindex) + i; };

	int					numbonecontrollers;		// bone controllers
	int					bonecontrollerindex;
	inline mstudiobonecontroller_t *pBonecontroller( int i ) const { Assert( i >= 0 && i < numbonecontrollers); return (mstudiobonecontroller_t *)(((byte *)this) + bonecontrollerindex) + i; };

	int					numhitboxsets;
	int					hitboxsetindex;

	// Look up hitbox set by index
	mstudiohitboxset_t	*pHitboxSet( int i ) const 
	{ 
		Assert( i >= 0 && i < numhitboxsets); 
		return (mstudiohitboxset_t *)(((byte *)this) + hitboxsetindex ) + i; 
	};

	// Calls through to hitbox to determine size of specified set
	inline mstudiobbox_t *pHitbox( int i, int set ) const 
	{ 
		mstudiohitboxset_t const *s = pHitboxSet( set );
		if ( !s )
			return NULL;

		return s->pHitbox( i );
	};

	// Calls through to set to get hitbox count for set
	inline int			iHitboxCount( int set ) const
	{
		mstudiohitboxset_t const *s = pHitboxSet( set );
		if ( !s )
			return 0;

		return s->numhitboxes;
	};

	// file local animations? and sequences
//private:
	int					numlocalanim;			// animations/poses
	int					localanimindex;		// animation descriptions
  	inline mstudioanimdesc_t *pLocalAnimdesc( int i ) const { if (i < 0 || i >= numlocalanim) i = 0; return (mstudioanimdesc_t *)(((byte *)this) + localanimindex) + i; };

	int					numlocalseq;				// sequences
	int					localseqindex;


//private:
	mutable int			activitylistversion;	// initialization flag - have the sequences been indexed?
	mutable int			eventsindexed;
	
	// raw textures
	int					numtextures;
	int					textureindex;


	// raw textures search paths
	int					numcdtextures;
	int					cdtextureindex;

	// replaceable textures tables
	int					numskinref;
	int					numskinfamilies;
	int					skinindex;

	int					numbodyparts;		
	int					bodypartindex;
	inline mstudiobodyparts_t	*pBodypart( int i ) const { return (mstudiobodyparts_t *)(((byte *)this) + bodypartindex) + i; };

	// queryable attachable points
//private:
	int					numlocalattachments;
	int					localattachmentindex;

	// animation node to animation node transition graph
//private:
	int					numlocalnodes;
	int					localnodeindex;
	int					localnodenameindex;


	int					numflexdesc;
	int					flexdescindex;
	inline mstudioflexdesc_t *pFlexdesc( int i ) const { Assert( i >= 0 && i < numflexdesc); return (mstudioflexdesc_t *)(((byte *)this) + flexdescindex) + i; };

	int					numflexcontrollers;
	int					flexcontrollerindex;
	inline mstudioflexcontroller_t *pFlexcontroller( LocalFlexController_t i ) const { Assert( numflexcontrollers == 0 || ( i >= 0 && i < numflexcontrollers ) ); return (mstudioflexcontroller_t *)(((byte *)this) + flexcontrollerindex) + i; };

	int					numflexrules;
	int					flexruleindex;
	inline mstudioflexrule_t *pFlexRule( int i ) const { Assert( i >= 0 && i < numflexrules); return (mstudioflexrule_t *)(((byte *)this) + flexruleindex) + i; };

	int					numikchains;
	int					ikchainindex;
	inline mstudioikchain_t *pIKChain( int i ) const { Assert( i >= 0 && i < numikchains); return (mstudioikchain_t *)(((byte *)this) + ikchainindex) + i; };

	int					nummouths;
	int					mouthindex;

//private:
	int					numlocalposeparameters;
	int					localposeparamindex;

	int					surfacepropindex;
	inline char * const pszSurfaceProp( void ) const { return ((char *)this) + surfacepropindex; }

	// Key values
	int					keyvalueindex;
	int					keyvaluesize;

	int					numlocalikautoplaylocks;
	int					localikautoplaylockindex;

	int					CountAutoplaySequences() const;
	int					CopyAutoplaySequences( unsigned short *pOut, int outCount ) const;
	int					GetAutoplayList( unsigned short **pOut ) const;

	// The collision model mass that jay wanted
	float				mass;
	int					contents;

	// external animations, models, etc.
	int					numincludemodels;
	int					includemodelindex;

	// implementation specific back pointer to virtual data
#ifdef PLATFORM_64BITS
	// implementation specific back pointer to virtual data. Relocated to studiohdr2_t
	int					unused_virtualModel;
#else
	mutable void		*virtualModel;
#endif

	// for demand loaded animation blocks
	int					szanimblocknameindex;	
	int					numanimblocks;
	int					animblockindex;
#ifdef PLATFORM_64BITS
	// implementation specific back pointer to virtual data. Relocated to studiohdr2_t
	int					unused_animblockModel;
#else
	mutable void		*animblockModel;
#endif

	int					bonetablebynameindex;
	inline const byte	*GetBoneTableSortedByName() const { return (byte *)this + bonetablebynameindex; }

	// used by tools only that don't cache, but persist mdl's peer data
	// engine uses virtualModel to back link to cache pointers
#ifdef PLATFORM_64BITS
	// implementation specific back pointer to virtual data. Relocated to studiohdr2_t
	int					unused_pVertexBase;
	int					unused_pIndexBase;
#else
	mutable void		*pVertexBase;
	mutable void		*pIndexBase;
#endif

	// if STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT is set,
	// this value is used to calculate directional components of lighting 
	// on static props
	byte				constdirectionallightdot;

	// set during load of mdl data to track *desired* lod configuration (not actual)
	// the *actual* clamped root lod is found in studiohwdata
	// this is stored here as a global store to ensure the staged loading matches the rendering
	byte				rootLOD;
	
	// set in the mdl data to specify that lod configuration should only allow first numAllowRootLODs
	// to be set as root LOD:
	//	numAllowedRootLODs = 0	means no restriction, any lod can be set as root lod.
	//	numAllowedRootLODs = N	means that lod0 - lod(N-1) can be set as root lod, but not lodN or lower.
	byte				numAllowedRootLODs;

	byte				unused[1];

	int					unused4; // zero out if version < 47

	int					numflexcontrollerui;
	int					flexcontrolleruiindex;
	mstudioflexcontrollerui_t *pFlexControllerUI( int i ) const { Assert( i >= 0 && i < numflexcontrollerui); return (mstudioflexcontrollerui_t *)(((byte *)this) + flexcontrolleruiindex) + i; }

	float				flVertAnimFixedPointScale;
	inline float		VertAnimFixedPointScale() const { return ( flags & STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE ) ? flVertAnimFixedPointScale : 1.0f / 4096.0f; }

	int					unused3[1];

	// FIXME: Remove when we up the model version. Move all fields of studiohdr2_t into studiohdr_t.
	int					studiohdr2index;
	studiohdr2_t*		pStudioHdr2() const { return (studiohdr2_t *)( ( (byte *)this ) + studiohdr2index ); }

	// Src bone transforms are transformations that will convert .dmx or .smd-based animations into .mdl-based animations
	int					NumSrcBoneTransforms() const { return studiohdr2index ? pStudioHdr2()->numsrcbonetransform : 0; }

	inline int			IllumPositionAttachmentIndex() const { return studiohdr2index ? pStudioHdr2()->IllumPositionAttachmentIndex() : 0; }

	inline float		MaxEyeDeflection() const { return studiohdr2index ? pStudioHdr2()->MaxEyeDeflection() : 0.866f; } // default to cos(30) if not set

	inline mstudiolinearbone_t *pLinearBones() const { return studiohdr2index ? pStudioHdr2()->pLinearBones() : NULL; }

	inline int			BoneFlexDriverCount() const { return studiohdr2index ? pStudioHdr2()->m_nBoneFlexDriverCount : 0; }
	inline const mstudioboneflexdriver_t* BoneFlexDriver( int i ) const { Assert( i >= 0 && i < BoneFlexDriverCount() ); return studiohdr2index ? pStudioHdr2()->pBoneFlexDriver( i ) : NULL; }

#ifdef PLATFORM_64BITS

#else
	void* 				VirtualModel() const { return virtualModel; }
	void				SetVirtualModel( void* ptr ) { virtualModel = ptr; }

	void*				VertexBase() const { return pVertexBase; }
	void				SetVertexBase( void* _pVertexBase ) const { pVertexBase = _pVertexBase; }
	void*				IndexBase() const { return pIndexBase; }
	void				SetIndexBase( void* _pIndexBase ) const { pIndexBase = _pIndexBase; }
#endif

	// NOTE: No room to add stuff? Up the .mdl file format version 
	// [and move all fields in studiohdr2_t into studiohdr_t and kill studiohdr2_t],
	// or add your stuff to studiohdr2_t. See NumSrcBoneTransforms/SrcBoneTransform for the pattern to use.
	int					unused2[1];

	studiohdr_t() = default;

private:
	// No copy constructors allowed
	studiohdr_t(const studiohdr_t& vOther);

	friend struct virtualmodel_t;
};



//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

class IDataCache;
class IMDLCache;

class CStudioHdr
{
public:
	CStudioHdr( void );
	CStudioHdr( const studiohdr_t *pStudioHdr, IMDLCache *mdlcache = NULL );
	~CStudioHdr() { Term(); }

	void Init( const studiohdr_t *pStudioHdr, IMDLCache *mdlcache = NULL );
	void Term();

public:
	inline bool IsValid( void ) { return (m_pStudioHdr != NULL); };
	inline const studiohdr_t	*GetRenderHdr( void ) const { return m_pStudioHdr; };

private:
	mutable const studiohdr_t		*m_pStudioHdr;
	mutable virtualmodel_t	*m_pVModel;

	mutable CUtlVector< const studiohdr_t * > m_pStudioHdrCache;

	mutable int			m_nFrameUnlockCounter;
	int	*				m_pFrameUnlockCounter;
	CThreadFastMutex	m_FrameUnlockCounterMutex;

public:












	inline mstudioflexcontroller_t *pFlexcontroller( LocalFlexController_t i ) const { return m_pStudioHdr->pFlexcontroller( i ); };


	//inline const char	*name() const { return m_pStudioHdr->name; }; // deprecated -- remove after full xbox merge
	inline const char	*pszName() const { return m_pStudioHdr->pszName(); };







	inline int			flags() const { return m_pStudioHdr->flags; };


	inline float		mass() const { return m_pStudioHdr->mass; };
	inline int			contents() const { return m_pStudioHdr->contents; }


	








public:



public:

private:
	CUtlVector< int >  m_boneFlags;
	CUtlVector< int >  m_boneParent;

public:

	// This class maps an activity to sequences allowed for that activity, accelerating the resolution
	// of SelectWeightedSequence(), especially on PowerPC. Iterating through every sequence
	// attached to a model turned out to be a very destructive cache access pattern on 360.
	// 
	// I've encapsulated this behavior inside a nested class for organizational reasons; there is
	// no particular programmatic or efficiency benefit to it. It just makes clearer what particular
	// code in the otherwise very complicated StudioHdr class has to do with this particular
	// optimization, and it lets you collapse the whole definition down to a single line in Visual
	// Studio.
	class CActivityToSequenceMapping /* final */ 
	{
	public:
		// A tuple of a sequence and its corresponding weight. Lists of these correspond to activities.
		struct SequenceTuple
		{
			short		seqnum;
			short		weight; // the absolute value of the weight from the sequence header
			CUtlSymbol	*pActivityModifiers;		// list of activity modifier symbols
			int			iNumActivityModifiers;
		};

		// The type of the hash's stored data, a composite of both key and value
		// (because that's how CUtlHash works):
		// key: an int, the activity #
		// values: an index into the m_pSequenceTuples array, a count of the
		// total sequences present for an activity, and the sum of their
		// weights.
		// Note this struct is 128-bits wide, exactly coincident to a PowerPC 
		// cache line and VMX register. Please consider very carefully the
		// performance implications before adding any additional fields to this.
		// You could probably do away with totalWeight if you really had to.
		struct HashValueType
		{
			// KEY (hashed)
			int activityIdx; 

			// VALUE (not hashed)
			int startingIdx;
			int count;
			int totalWeight;

			HashValueType(int _actIdx, int _stIdx, int _ct, int _tW) : 
			activityIdx(_actIdx), startingIdx(_stIdx), count(_ct), totalWeight(_tW) {}

			// default constructor (ought not to be actually used)
			HashValueType() : activityIdx(-1), startingIdx(-1), count(-1), totalWeight(-1) 
				{ AssertMsg(false, "Don't use default HashValueType()!"); }


			class HashFuncs
			{
			public:
				// dummy constructor (gndn)
				HashFuncs( int ) {}

				// COMPARE
				// compare two entries for uniqueness. We should never have two different
				// entries for the same activity, so we only compare the activity index;
				// this allows us to use the utlhash as a dict by constructing dummy entries
				// as hash lookup keys.
				bool operator()( const HashValueType &lhs, const HashValueType &rhs ) const
				{
					return lhs.activityIdx == rhs.activityIdx;
				}

				// HASH
				// We only hash on the activity index; everything else is data.
				unsigned int operator()( const HashValueType &item ) const
				{
					return HashInt( item.activityIdx );
				}
			};
		};

		typedef CUtlHash<HashValueType, HashValueType::HashFuncs, HashValueType::HashFuncs> ActivityToValueIdxHash;

		// These must be here because IFM does not compile/link studio.cpp (?!?)

		// ctor
		CActivityToSequenceMapping( void ) 
			: m_pSequenceTuples(NULL), m_iSequenceTuplesCount(0), m_ActToSeqHash(8,0,0), m_expectedPStudioHdr(NULL), m_expectedVModel(NULL) 
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
			, m_bIsInitialized(false) 
#endif
		{};

		// dtor -- not virtual because this class has no inheritors
		~CActivityToSequenceMapping()
		{	
			if ( m_pSequenceTuples != NULL )
			{
				if ( m_pSequenceTuples->pActivityModifiers != NULL )
				{
					delete[] m_pSequenceTuples->pActivityModifiers;
				}
				delete[] m_pSequenceTuples;
			}
		}


		/// The number of sequences available for an activity.
		int NumSequencesForActivity( int forActivity );

#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
		inline bool IsInitialized( void ) { return m_bIsInitialized; }
#endif

	private:

		/// Allocate my internal array. (It is freed in the destructor.) Also,
		/// build the hash of activities to sequences and populate m_pSequenceTuples.
		void Initialize( CStudioHdr *pstudiohdr );

		/// Force Initialize() to occur again, even if it has already occured.
		void Reinitialize( CStudioHdr *pstudiohdr );

		/// A more efficient version of the old SelectWeightedSequence() function in animation.cpp. 
		int SelectWeightedSequence( CStudioHdr *pstudiohdr, int activity, int curSequence );

		// selects the sequence with the most matching modifiers
		int SelectWeightedSequenceFromModifiers( CStudioHdr *pstudiohdr, int activity, CUtlSymbol *pActivityModifiers, int iModifierCount );

		// Actually a big array, into which the hash values index.
		SequenceTuple *m_pSequenceTuples;
		unsigned int m_iSequenceTuplesCount; // (size of the whole array)
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
		bool m_bIsInitialized;
#endif

		// we don't store an outer pointer because we can't initialize it at construction time
		// (warning c4355) -- there are ways around this but it's easier to just pass in a 
		// pointer to the CStudioHdr when we need it, since this class isn't supposed to 
		// export its interface outside the studio header anyway.
		// CStudioHdr * const m_pOuter;

		ActivityToValueIdxHash m_ActToSeqHash;

		// we store these so we can know if the contents of the studiohdr have changed
		// from underneath our feet (this is an emergency data integrity check)
		const void *m_expectedPStudioHdr;
		const void *m_expectedVModel;


		friend class CStudioHdr;
	};

	CActivityToSequenceMapping m_ActivityToSequence;

	/// A more efficient version of the old SelectWeightedSequence() function in animation.cpp. 
	/// Returns -1 on failure to find a sequence
	inline int SelectWeightedSequence( int activity, int curSequence )
	{
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
		// We lazy-initialize the header on demand here, because CStudioHdr::Init() is
		// called from the constructor, at which time the this pointer is illegitimate.
		if ( !m_ActivityToSequence.IsInitialized() )
		{
			m_ActivityToSequence.Initialize(this);
		}
#endif
		return m_ActivityToSequence.SelectWeightedSequence( this, activity, curSequence );
	}

	inline int SelectWeightedSequenceFromModifiers( int activity, CUtlSymbol *pActivityModifiers, int iModifierCount )
	{
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
		// We lazy-initialize the header on demand here, because CStudioHdr::Init() is
		// called from the constructor, at which time the this pointer is illegitimate.
		if ( !m_ActivityToSequence.IsInitialized() )
		{
			m_ActivityToSequence.Initialize( this );
		}
#endif
		return m_ActivityToSequence.SelectWeightedSequenceFromModifiers( this, activity, pActivityModifiers, iModifierCount );
	}

	/// True iff there is at least one sequence for the given activity.
	inline bool HaveSequenceForActivity( int activity )	
	{
#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE
		if ( !m_ActivityToSequence.IsInitialized() )
		{
			m_ActivityToSequence.Initialize(this);
		}
#endif
		return (m_ActivityToSequence.NumSequencesForActivity( activity ) > 0);
	}


#ifdef STUDIO_ENABLE_PERF_COUNTERS
public:

	// timing info
	mutable	int			m_nPerfAnimatedBones;
	mutable	int			m_nPerfUsedBones;
	mutable	int			m_nPerfAnimationLayers;
#endif


};

/*
class CModelAccess
{
public:
	CModelAccess(CStudioHdr *pSemaphore)
	 : m_pStudioHdr(pSemaphore)
	{
		m_pStudioHdr->IncrementAccess();
	}
	
	~CModelAccess()
	{
		m_pStudioHdr->DecrementAccess();
	}
	
private:
	CStudioHdr *m_pStudioHdr;
};

#define ENABLE_MODEL_ACCESS( a ) \
	CModelAccess ModelAccess##__LINE__( a->m_pStudioHdr )
*/

//-----------------------------------------------------------------------------
// Purpose:
//-----------------------------------------------------------------------------

struct flexweight_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					key;
	float				weight;
	float				influence;
};

struct flexsetting_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					nameindex;

	inline char *pszName( void ) const
	{ 
		return (char *)(((byte *)this) + nameindex); 
	}

	// Leaving this for legacy support
	int					obsolete1;

	// Number of flex settings
	int					numsettings;
	int					index;
	
	// OBSOLETE:  
	int					obsolete2;

	// Index of start of contiguous array of flexweight_t structures
	int					settingindex;

};


struct flexsettinghdr_t
{
	DECLARE_BYTESWAP_DATADESC();
	int					id;
	int					version;

	inline const char * pszName( void ) const { return name; }
	char				name[64];
	int					length;

	int					numflexsettings;
	int					flexsettingindex;
	inline flexsetting_t *pSetting( int i ) const { return (flexsetting_t *)(((byte *)this) + flexsettingindex) + i; };
	int					nameindex;

	// look up flex settings by "index"
	int					numindexes;
	int					indexindex;


	// index names of "flexcontrollers"
	int					numkeys;
	int					keynameindex;

	int					keymappingindex;
	inline int			*pLocalToGlobal( int i ) const { return (int *)(((byte *)this) + keymappingindex) + i; };
};













	// get float
	// get Flexcontroller value
	// get flex weight




	// not implemented
	// not implemented
	// only used in token parsing

	// only used in token parsing


	// Fetch a value from a 2 Way slider for the 1st value RemapVal( 0.0, 0.5, 0.0, 1.0 )
	// Fetch a value from a 2 Way slider for the 2nd value RemapVal( 0.5, 1.0, 0.0, 1.0 )
	// Fetch a value from a 2 Way slider for the 2nd value RemapVal( 0.5, 1.0, 0.0, 1.0 )
	// Perform a combo operation (essentially multiply the last N values on the stack)
	// Performs a combination domination operation
	// 
	// 

// motion flags

	















	// controller that wraps shortest distance

// sequence and autolayer flags
		// ending frame should be the same as the starting frame
		// do not interpolate between previous animation and this one
		// this sequence "adds" to the base sequences, not slerp blends
		// temporary flag that forces the sequence to always play
		// 
		// this animation/sequence has no real animation data
//						0x0040
		// cycle index is taken from a pose parameter index
		// cycle index is taken from a real-time clock, not the animations cycle index
		// sequence has a local context sequence
		// don't show in default selection views
		// a forward declared sequence (empty)
		// Has been updated at runtime to activity index
		// Has been updated at runtime to event index
		// sequence blends in worldspace
// autolayer flags
//							0x0001
//							0x0002
//							0x0004
//							0x0008
		// 
//							0x0020
		// convert layer ramp in/out curve is a spline instead of linear
		// pre-bias the ramp curve to compense for a non-1 weight, assuming a second layer is also going to accumulate
//							0x0100
		// animation always blends at 1.0 (ignores weight)
//							0x0400
//							0x0800
		// layer is a local context sequence
//							0x2000
		// layer blends using a pose parameter instead of parent cycle






#endif // STUDIO_H
