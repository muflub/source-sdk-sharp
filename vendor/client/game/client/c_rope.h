//========= Copyright Valve Corporation, All rights reserved. ============//

//========= SourceSharp's edit of c_rope.h (vendor/README.md) =====//
// C_RopeKeyframe over CShellRenderable and the rope manager: moved out of the
// fork's edited game/client/c_rope.h (client.so on unmodified Valve headers,
// user 2026-10-25).
#pragma once

#include "client_entity.h"
#include "shell_renderable.h"
#include "rope_physics.h"
#include "materialsystem/imaterial.h"
#include "rope_shared.h"
#include "bitvec.h"

class KeyValues;
class C_BaseAnimating;

// A ROPE IS A RENDERABLE, NOT AN ENTITY.
// It was `: public C_BaseEntity`, and that single line was the largest entry in
// the whole client removal set: 160 of the 161 vtable-only symbol pairs the
// hierarchy relink owed were this object emitting C_RopeKeyframe's vtable --
// one undefined reference per inherited virtual, for a class whose own code
// called almost none of them.
// WHAT THE ENTITY WAS ACTUALLY PROVIDING, item by item, because "it was an
// entity" is not a list. Same accounting as tempent.h's, which moved
// C_LocalTempEntity onto this base first:
//   InitializeAsClientEntity()  an entity index, a row in the client entity
//                               list, and ClientLeafSystem()->AddRenderable().
//                               Only the last was ever read here: nothing looks
//                               a rope up by index or by handle. Create() calls
//                               AddToLeafSystem( RENDER_GROUP_OPAQUE_ENTITY ),
//                               which is the group InitializeAsClientEntity was
//                               being handed anyway.
//   SetNextClientThink()        a row in the client think list, which called
//                               ClientThink() once a frame. A renderable has no
//                               place in that list, so the walk is explicit:
//                               Rope_SimulateAll() below, over the same g_Ropes
//                               list this file has always kept, driven once a
//                               frame from the port's rope game system.
//   SetCollisionBounds()        the render bounds, and the side effect of
//                               telling the leaf system the object's extent
//                               moved. Two members and RenderableMoved() here;
//                               forgetting the side effect is a rope that draws
//                               only from the leaves it was first added to.
//   Release()                   destroyed a client-created entity outright.
//                               CShellRenderable::Release() does the same, and
//                               c_baseanimating.cpp:1357 calls it unchanged.
//   operator new                MEMSET THE BLOCK TO ZERO. The constructor sets
//                               seventeen members and leaves eleven -- m_Slack,
//                               m_RopeLength, m_Width, m_nSegments, the gust
//                               timers -- on the promise the block arrived
//                               zeroed. CShellRenderable carries the same
//                               allocator, which is why that promise still
//                               holds.
//   ShouldDraw / IsTransparent  pure on CDefaultClientRenderable; answered
//                               below exactly as before.
// AND WHAT IS DELIBERATELY GONE, rather than carried:
//   OnDataChanged / ReceiveMessage / m_iRopeMaterialModelIndex
//                               the NETWORK half. This class has had no recv
//                               table since step 8e-classes and no ServerClass
//                               named CRopeKeyframe is compiled at all, so
//                               neither method had a caller: measured at 14 of
//                               14 constructions from the client-only Create()
//                               path over a two-pass live gate. They are
//                               removed rather than left as unreachable
//                               entity-shaped code.
//   WorldSpaceCenter()          a C_BaseEntity virtual, overridden here to
//                               answer the origin. Its one reader was
//                               CalculateEndPointAttachment resolving an
//                               endpoint that was ITSELF a rope, which this
//                               build cannot produce: the managed ropes take
//                               positions and the model cables take the model.
//   GetAttachmentVelocity / GetAttachment( int, Vector& )
//                               C_BaseEntity virtuals with no caller in this
//                               build. The two IClientRenderable attachment
//                               forms -- which is how anything actually asks a
//                               renderable where its ends are -- stay.
//   m_bReadyToDraw              C_BaseEntity's flag. Nothing in this build
//                               calls SetReadyToDraw on anything, so the guard
//                               in DrawModel was reading a constant true.
// The endpoints stay EHANDLEs. A model cable hangs off a C_BaseAnimating's
// attachment points and has to follow them, so that endpoint has to be an
// entity for as long as c_baseanimating.cpp is in the build; it leaves with it.
// The MANAGED ropes hold no entity at either end -- see SetStartPos below.
class C_RopeKeyframe : public CShellRenderable
{
public:

	typedef CShellRenderable BaseClass;
	// SOURCESHARP: DECLARE_CLIENTCLASS() WAS HERE. C_RopeKeyframe is
	// CLIENT-ONLY in this build -- every one is made by Create() or
	// CreateFromKeyValues() and none by the network factory (14 of 14 over a
	// two-pass live gate). It had no client class then and it is not
	// networkable AT ALL now: a CShellRenderable answers NULL to
	// GetClientNetworkable(). The measurement, the nineteen props that went
	// with the recv table, and the check that the recv proxy's side effect is
	// covered elsewhere are all at the matching site in c_rope.cpp.

private:

	class CPhysicsDelegate : public CSimplePhysics::IHelper
	{
	public:
		virtual void	GetNodeForces( CSimplePhysics::CNode *pNodes, int iNode, Vector *pAccel );
		virtual void	ApplyConstraints( CSimplePhysics::CNode *pNodes, int nNodes );
	
		C_RopeKeyframe	*m_pKeyframe;
	};

	friend class CPhysicsDelegate;

public:

					C_RopeKeyframe();
					~C_RopeKeyframe();

	// Attach to things (you can also just lock the endpoints down yourself if you hook the physics).

	// READ-ONLY GEOMETRY, and it is INSTRUMENTATION -- named as debt per
	// LANE_BRIEF rather than left to ship unremarked.
	// It exists because the gate on this class is "a rope hangs between two
	// points and sags", and every counter-shaped instrument this port has
	// reached for has at some point kept counting for a broken build: a rope
	// whose render cache is filled every frame with a straight line between
	// two zeroes increments the draw counter exactly as a correct one does.
	// These four accessors are what let cs_cl_rope_spans report the SHAPE --
	// where the ends are, where the nodes actually are, and how far the middle
	// hangs below the chord -- so the verdict is geometry rather than a tally.
	// m_vPredicted rather than m_vPos, because m_vPredicted is what BuildRope
	// meshes: this reports the rope that is DRAWN, not the one that is
	// simulated a step ahead of it.
	// Removal owner: whoever deletes cs_cl_rope_spans. The two go together and
	// nothing else calls these.

	struct BuildRopeQueuedData_t
	{
		Vector	*m_pPredictedPositions;
		Vector	*m_pLightValues;
		int		m_iNodeCount;
		Vector	m_vColorMod;
		float	m_RopeLength;
		float	m_Slack;
	};

// IClientRenderable, which is what an entity was supplying.
public:

	virtual int		DrawModel( int flags ) OVERRIDE;
	virtual bool	ShouldDraw() OVERRIDE;

	// Ropes go in RENDER_GROUP_OPAQUE_ENTITY and the rope renderer does its own
	// blending, exactly as it did when C_BaseEntity::IsTransparent answered for
	// a modelless entity in the default render mode.
	virtual bool	IsTransparent() OVERRIDE { return false; }

	virtual void	GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE;

	// Specify ROPE_ATTACHMENT_START_POINT or ROPE_ATTACHMENT_END_POINT for the attachment.
	virtual	bool	GetAttachment( int number, Vector &origin, QAngle &angles ) OVERRIDE;
	virtual bool	GetAttachment( int number, matrix3x4_t &matrix ) OVERRIDE;

private:
	
private:

	// Track which links touched something last frame. Used to prevent wind from gusting on them.
	CBitVec<ROPE_MAX_SEGMENTS>		m_LinksTouchingSomething;
	int								m_nLinksTouchingSomething;
	bool							m_bApplyWind;
	int								m_fPrevLockedPoints;	// Which points are locked down.
	int								m_iForcePointMoveCounter;

	// Used to control resting state.
	bool			m_bPrevEndPointPos[2];
	Vector			m_vPrevEndPointPos[2];

	float			m_flCurScroll;		// for scrolling texture.
	float			m_flScrollSpeed;

	int				m_RopeFlags;			// Combo of ROPE_ flags.

	// The render bounds, in the local form GetRenderBounds answers. They were
	// SetCollisionBounds() on the entity, and UpdateBBox() still computes them
	// the same way.
	Vector			m_vRenderMins;
	Vector			m_vRenderMaxs;
		
	CRopePhysics<ROPE_MAX_SEGMENTS>	m_RopePhysics;
	Vector			m_LightValues[ROPE_MAX_SEGMENTS]; // light info when the rope is created.

	int				m_nSegments;		// Number of segments.
	
	EHANDLE			m_hStartPoint;		// StartPoint/EndPoint are entities
	EHANDLE			m_hEndPoint;
	short			m_iStartAttachment;	// StartAttachment/EndAttachment are attachment points.
	short			m_iEndAttachment;

	// ...or the endpoint is simply a place. See SetStartPos above. Both arrive
	// false/zero from the zeroing allocator, which is what "this end is an
	// entity, as stock" is spelled as.
	bool			m_bEndPointIsFixed[2];
	Vector			m_vFixedEndPointPos[2];

	unsigned char	m_Subdiv;			// Number of subdivions in between segments.

	int				m_RopeLength;		// Length of the rope, used for tension.
	int				m_Slack;			// Extra length the rope is given.
	float			m_TextureScale;		// pixels per inch
	
	int				m_fLockedPoints;	// Which points are locked down.

	float				m_Width;

	CPhysicsDelegate	m_PhysicsDelegate;

	IMaterial		*m_pMaterial;
	IMaterial		*m_pBackMaterial;			// Optional translucent background material for the rope to help reduce aliasing.

	int				m_TextureHeight;	// Texture height, for texture scale calculations.

	// Instantaneous force
	Vector			m_flImpulse;
	Vector			m_flPreviousImpulse;

	// Simulated wind gusts.
	float			m_flCurrentGustTimer;
	float			m_flCurrentGustLifetime;	// How long will the current gust last?

	float			m_flTimeToNextGust;			// When will the next wind gust be?
	Vector			m_vWindDir;					// What direction does the current gust go in?

	Vector			m_vColorMod;				// Color modulation on all verts?

	Vector			m_vCachedEndPointAttachmentPos[2];
	QAngle			m_vCachedEndPointAttachmentAngle[2];

	// In network table, can't bit-compress
	bool			m_bConstrainBetweenEndpoints;	// Simulated segment points won't stretch beyond the endpoints

	bool			m_bEndPointAttachmentPositionsDirty : 1;
	bool			m_bEndPointAttachmentAnglesDirty : 1;
	bool			m_bNewDataThisFrame : 1;			// Set to true in OnDataChanged so that we simulate that frame
	bool			m_bPhysicsInitted : 1;				// It waits until all required entities are 
	// present to start simulating and rendering.

	int				m_nRopeIndex;

	friend class CRopeManager;
};

//void Rope_ShowRSpeeds();

// Rope Manager
abstract_class IRopeManager
{
public:
	virtual						~IRopeManager() {}
	virtual void				ResetRenderCache( void ) = 0;
	virtual void				AddToRenderCache( C_RopeKeyframe *pRope ) = 0;
	virtual void				DrawRenderCache( bool bShadowDepth ) = 0;
	virtual void				OnRenderStart( void ) = 0;
	virtual void				SetHolidayLightMode( bool bHoliday ) = 0;
	virtual bool				IsHolidayLightMode( void ) = 0;
	virtual int					GetHolidayLightStyle( void ) = 0;
};

IRopeManager *RopeManager();
