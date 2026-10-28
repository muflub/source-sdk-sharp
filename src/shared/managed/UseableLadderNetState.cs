//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a func_useableladder tells the client (stock's DT_FuncLadder): enough
/// for the client's prediction to run the same ladder code the server does.
///
/// <para>
/// The mount points are WORLD positions, settled at spawn by the server's
/// traces. Stock sends them relative to the entity's origin and adds the
/// origin back on each read, which only differs for a ladder that moves after
/// spawn; nothing in this build moves one.
/// </para>
///
/// <para>
/// <see cref="Disabled"/> is sent and stock's is not: stock's client always
/// thinks a ladder is enabled, so a disabled one mispredicts there. Recorded in
/// docs/divergences.md.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncUseableladder)]
public partial class UseableLadderNetState
{
    /// <summary>The upper mount point (<c>m_vecPlayerMountPositionTop</c>, made absolute).</summary>
    public partial Vector Top { get; set; }

    /// <summary>The lower mount point (<c>m_vecPlayerMountPositionBottom</c>, made absolute).</summary>
    public partial Vector Bottom { get; set; }

    /// <summary><c>m_bFakeLadder</c>: spawnflag 1, an auto-mount point and not a ladder.</summary>
    public partial bool Fake { get; set; }

    /// <summary><c>m_bDisabled</c>: StartDisabled, then the Enable and Disable inputs.</summary>
    public partial bool Disabled { get; set; }
}

/// <summary>
/// What an info_ladder_dismount tells the client: where it is. Stock's
/// DT_InfoLadderDismount is empty and the client reads the origin every entity
/// sends; it is its own field here so the client's prediction reads it from
/// managed state rather than through the shell.
/// </summary>
[StandaloneNetState(EntityNames.InfoLadderDismount)]
public partial class LadderDismountNetState
{
    /// <summary>The point, in world space.</summary>
    public partial Vector Origin { get; set; }
}
