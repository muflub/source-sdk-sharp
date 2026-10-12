//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — ONE MODEL, ONE CROSSING ============================//
//
// Everything managed code has ever asked a model about, packed once and handed
// over once: the sequence table (label, activity name, flags, and every event
// on it), the attachment names, and the bone count. Each sequence's default-pose
// duration left the blob at v3 (2026-10-12): managed computes it from its own
// studio header (StudioSequenceTiming), so studio_sequence_timing.cpp could go.
//
// WHY IT IS ONE CROSSING AND NOT SEVEN ENTRY POINTS. Before this, managed code
// asked per call and per event: SourceSharp_EntityLookupSequence,
// ...LookupSequenceByActivity, ...SequenceDuration, ...SequenceEventCount,
// ...SequenceEvent (once PER EVENT), ...StandaloneAttachmentCount and
// ...StandaloneAttachmentName (once PER ATTACHMENT)
// — seven slots, each answering one scalar about a file that does not change
// while the level is loaded. They left the ABI at version 248.
// ...StandaloneBoneCount was counted as an eighth and did NOT go: its
// pOutCached half is per-ENTITY rather than per-model. Reading one weapon's fire animation cost
// 1 + 1 + (E+1) calls; reading one prop's attachments cost 2 + A. This answers
// the whole model once and managed caches it for the level.
//
// AND IT IS WHY THE TWO SIDES CAN STOP DISAGREEING. The client's
// SourceSharp_EntityLookupSequence called ::LookupSequence (game/shared/
// animation.cpp), which on a label miss falls through to an ACTIVITY lookup
// resolved by SelectWeightedSequence — a weighted RANDOM pick. The server's
// called BoneLibrary_LookupSequence, which refuses that fallback. So the same
// ACT_ name answered a random index on the client and -1 on the server, and a
// sequence index is PREDICTED state. With the table crossing instead of the
// answer, the RULE is one managed implementation both sides run
// (SourceSharp.Abi/ModelDescriptor.cs), which is the only shape that can be
// the same on both sides by construction rather than by review.
//
// REASON LETTER: none, and that is the point. Nothing here decides anything —
// it is the marshaller for a file format, exactly as sequence_events.h said of
// itself. The decisions (which sequence an activity resolves to, which name
// matches, whether a duration is usable) are in C#.
//
// THE BUFFER PROTOCOL is the tree's usual one: call with nBytes 0 (or too
// small) to be told the size, then call again. The answer is always the number
// of bytes the blob needs, and the buffer is written only when it fits whole —
// a half-written blob would decode as a differently-shaped model rather than
// as a failure.
//
// ENDIANNESS AND ALIGNMENT: little-endian, packed, no padding. Both ends of
// this crossing are the same process on the same architecture; the format is
// fixed here rather than inherited from a struct layout so that a compiler
// changing its padding cannot change what managed reads.
//
//=============================================================================//
#ifndef SOURCESHARP_MODEL_DESCRIPTOR_H
#define SOURCESHARP_MODEL_DESCRIPTOR_H
#ifdef _WIN32
#pragma once
#endif

#include "tier0/platform.h"

//-----------------------------------------------------------------------------
// THE FORMAT, in one place so the C# decoder can be read against it.
//
//   int32   kModelDescriptorVersion      (the first word, so a stale decoder
//                                         fails loudly rather than silently)
//   int32   nBoneCount
//   int32   nAttachmentCount
//     nAttachmentCount x {
//       string  name                      in model order
//       float   localX, localY, localZ    the attachment's local origin: column
//                                         3 of pAttachment(i).local, relative to
//                                         its bone (v2; break-piece placement
//                                         reads "placementOrigin" from it)
//     }
//   int32   nSequenceCount
//     nSequenceCount x {
//       string  label                     pszLabel()
//       string  activityName              pszActivityName(), "" when untagged
//       int32   flags                     seqdesc.flags
//       int32   nEventCount
//         nEventCount x {
//           float  cycle
//           int32  id                     seqdesc.pEvent(i)->event
//           int32  type                   seqdesc.pEvent(i)->type
//           string name                   "" unless AE_TYPE_NEWEVENTSYSTEM
//           string options
//         }
//     }
//
// `string` is int32 length in bytes followed by that many bytes, NOT
// nul-terminated. A length of 0 is an empty string and is not the same as
// absent; nothing in this format is absent.
//-----------------------------------------------------------------------------
enum { kModelDescriptorVersion = 3 };

//-----------------------------------------------------------------------------
// Pack the model at nModelIndex into pBuffer.
//
// Returns the number of bytes the blob needs — ALWAYS, whether or not it was
// written. Zero means the index names no model, or the model has no studio
// data (a sprite, a brush model): managed reads that as "this model answers
// nothing", which is what stock's per-call entry points also answered for it.
//
// pBuffer may be NULL when nBytes is 0, which is how a caller asks for the size.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ModelDescriptor( int32_t nModelIndex, char *pBuffer, int32_t nBytes );

#endif // SOURCESHARP_MODEL_DESCRIPTOR_H
