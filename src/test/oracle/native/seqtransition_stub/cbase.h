//===== SourceSharp — the sequence transitioner oracle's cbase.h ==============//
// sequence_Transitioner.cpp and its header need CStudioHdr (public/studio.h),
// CUtlVector and, with CLIENT_DLL, CAnimationLayer: the client's
// C_AnimationLayer and its GetFadeout (game/client/animationlayer.h, pristine,
// copied beside the subject). Nothing else of the game is compiled.
//
// animationlayer.h opens its class with ALLOW_DATATABLES_PRIVATE_ACCESS(),
// which public/client_class.h defines (with all of the receive tables); the
// same one-line friend declaration is restated here instead.
//=============================================================================//
#pragma once
#define ALLOW_DATATABLES_PRIVATE_ACCESS() \
	template <typename T>                 \
	friend int ClientClassInit( T * );
#include "studio.h"
#include "utlvector.h"
#include "animationlayer.h"
