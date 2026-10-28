//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =====================================================//
//
// THE ENGINE BLOCK: the fixed prefix at offset 0 of the blob every entity
// carries (plan.md §4, plan_entity.md §3.2).
//
// WHY IT EXISTS. Most of the ICollideable / IServerEntity / IClientRenderable
// surface is getters the engine calls synchronously and often -- the spatial
// partition asks GetCollisionOrigin and OBBMins per trace, the renderer asks
// GetRenderOrigin and IsTransparent per draw. Those calls cannot cross into
// managed code at that rate, and they are not native policy either. They are
// answered from memory managed code WRITES, and this is that memory. A shell
// getter becomes one load at a constant offset.
//
// THERE IS ONE DECLARATION OF THE BLOCK AND IT IS THE C# ONE.
// SourceSharp.Abi/EngineBlock.cs declares it; engine_block_generated.h, which
// this file includes, is BUILT from that declaration -- the structs and the
// static_asserts alike -- by EngineBlock.NativeHeaderText. This file is the
// header the game includes and the prose about what the block is FOR; it
// contributes nothing to the layout.
//
// It used to hold a second, hand-written EngineBlock_t that mirrored the C#
// one, defended by the generated asserts. Two declarations of one layout is
// plan_entity.md §7's named drift risk, and generating the struct removes the
// second declaration rather than defending it. What remains:
//
//   * The fingerprint the join handshake exchanges is computed from the C#
//     declaration and from nothing else, so no edit made on this side can move
//     it -- and there is now no edit to make on this side.
//   * EngineBlockTests compares the generated file, byte for byte, with what
//     the C# declaration produces, so hand-editing it turns the managed suite
//     red rather than quietly changing the layout under the game.
//   * engine_block_layout_test.cpp asserts every offset again from a gtest,
//     against HAND-WRITTEN literals rather than generated ones. That is the
//     one comparison in the chain whose two sides come from different authors,
//     and it is why the gtest matters MORE now than it did when the struct
//     beside it was typed by a person.
//
// THE FIELD NAMES ARE NOT HUNGARIAN, and that is deliberate. They are spelled
// exactly as the C# fields are, because the generated header names each one
// inside an offsetof(). Identical spelling is what makes the two sides
// mechanically comparable rather than comparable through a mapping table that
// would be a third thing to keep honest.
//
// BOTH TAILS ARE PRESENT ON BOTH SIDES. The block is ONE layout: the server
// sends its fingerprint at join and the client compares it with its own, so a
// server whose block had only a server tail could never agree with a client
// whose block had only a client tail, and the comparison would refuse every
// join it ever saw. The tails say who WRITES what, not who has it.
//
// WHERE THE PROSE WENT. The reasoning that used to sit beside each field --
// why Padding0 exists, why the reservations are uint64 and not bytes, why the
// client tail's pose is not the head's placement -- travels with the fields
// now. It is declared once, in [NativeDoc] attributes on the C# declaration,
// and emitted into engine_block_generated.h above the field it explains. Read
// that file at a debugger prompt exactly as you would have read this one.
//
//===========================================================================//

#pragma once

// EngineBlockHead_t, EngineBlockServerTail_t, EngineBlockClientTail_t and
// EngineBlock_t, generated from SourceSharp.Abi.EngineBlock, with one
// static_assert per field under them. It brings its own includes.
#include "engine_block_generated.h"
