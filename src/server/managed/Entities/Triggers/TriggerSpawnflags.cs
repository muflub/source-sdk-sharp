//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

// The map trigger volumes, in C#.
//
// A trigger is a shape carved out of the map that reports being stood in. The
// shape is not ours to choose — the map supplies it as a brush model — so all
// these classes decide is what entering, staying and leaving mean.
//
// The C++ versions in triggers.cpp are still compiled, because CBaseTrigger is
// a base class across the SDK and the file cannot leave the build. What has
// gone is their LINK_ENTITY_TO_CLASS, guarded by #ifndef SOURCESHARP — the
// conflict was never the classes existing, only two registrations claiming one
// name. That makes this port per-class rather than per-file: what is not here
// is still served by C++, and nothing regresses.

/// <summary>
/// What a trigger volume notices: CBaseTrigger's spawnflags, shared by every
/// trigger_ entity in the port.
///
/// <para>
/// This one really is a shared domain rather than a collection of similar
/// things. Every trigger volume reads these same five bits off its own
/// spawnflags word to answer the same question -- which kinds of thing count as
/// touching me -- and a volume's answer is expressed as a subset of them. That
/// is why <see cref="TriggerVolume.KindFlags"/> can be a value of this type and
/// each volume can narrow it.
/// </para>
///
/// <para>
/// What used to live alongside these and does NOT belong to them:
/// trigger_push's "push once" and trigger_teleport's "preserve angles". Those
/// are one entity's bits each, filed here because they were too small to sit
/// anywhere else, and they now sit on their entities. Keeping them here made
/// the shared set look like a grab bag and offered every trigger a bit that
/// means nothing to it -- trigger_teleport's 32 is not in this set at all, and
/// trigger_push's 128 is one place above EverythingElse.
/// </para>
/// </summary>
[Flags]
public enum TriggerSpawnflags
{
    /// <summary>No kind named. Read as "anybody" -- see TriggerVolume.KindWanted.</summary>
    None           = 0b0000_0000,

    Clients        = 0b0000_0001,
    NPCs           = 0b0000_0010,
    PushablesToo   = 0b0000_0100,
    PhysicsObjects = 0b0000_1000,
    EverythingElse = 0b0100_0000,
}
