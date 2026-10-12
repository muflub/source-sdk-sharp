//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// How many pieces a map is willing to have flying at once when things break.
///
/// The same shape as game_ragdoll_manager and for the same reason: breaking a
/// crate is cheap and breaking a room full of them at once is not, so a map that
/// stages the second wants to say "keep twelve" rather than pay for whatever the
/// prop data asks for. It is a burst budget rather than a world budget — see
/// <see cref="Allows"/> — which is what makes it a limit on one explosion rather
/// than on the debris a fight leaves behind.
///
/// The DX8 key is that budget again for a machine below shader model 3, and the
/// rule choosing between the two is <see cref="GameRagdollManager.Budget"/>
/// rather than a second copy of it. Stock wrote that rule twice, once here and
/// once in ragdoll_manager.cpp, and two copies of a rule is how the two entities
/// come to disagree about it.
///
/// **Nothing reads the budget in this mod.** props_shared.cpp asks for a gib
/// manager through GetGibManager, which begins with `#ifndef HL2_EPISODIC return
/// NULL`, and this is not an episodic build — so stock's own limit has never
/// applied here either. The keys are carried, the arithmetic is carried, and the
/// entity is honest about counting nothing.
/// </summary>
[ScriptedEntity(EntityNames.GameGibManager)]
public sealed class GameGibManager : ScriptedEntity
{
    public GameGibManager(IEngineConsole console)
    {
        Console = console;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;
    /// <summary>What a map may set on this entity. See <see cref="GameGibManagerVars"/>.</summary>
    [ScriptVars] public GameGibManagerVars Vars = new();

    private int _dxLevel = DefaultDxLevel;

    // mat_dxlevel is a CLIENT convar; a dedicated server has never heard of it,
    // so the honest default is the capable path. See GameRagdollManager, which
    // makes the same assumption for the same reason.
    private const int DefaultDxLevel = 90;

    /// <summary>The budget in force, after the DX level has chosen between the two.</summary>
    public int CurrentMaxPieces { get; private set; } = -1;

    /// <summary>Whether the budget has been waived.</summary>
    public bool AllowNewGibs => Vars.AllowNewGibs;

    public override void OnSpawn()
    {
        // Read once, as stock does in Activate: the level cannot change without
        // the renderer restarting.
        _dxLevel = ParseInt(Console.FindConVarValue("mat_dxlevel") ?? string.Empty, DefaultDxLevel);

        Apply();
    }

    [ScriptedEvent("SetMaxPieces")]
    private void OnSetMaxPieces(float count)
    {
        Vars.MaxPieces = (int)count;
        Apply();
    }

    [ScriptedEvent("SetMaxPiecesDX8")]
    private void OnSetMaxPiecesDX8(float count)
    {
        Vars.MaxPiecesDX8 = (int)count;
        Apply();
    }

    private void Apply() => CurrentMaxPieces = GameRagdollManager.Budget(_dxLevel, Vars.MaxPieces, Vars.MaxPiecesDX8);

    /// <summary>Whether one more piece may be spawned. See <see cref="Allows"/>.</summary>
    public bool AllowsAnother(int livePieces, bool spawnedThisFrame)
        => Allows(Vars.AllowNewGibs, CurrentMaxPieces, livePieces, spawnedThisFrame);

    /// <summary>
    /// Whether one more piece may be spawned.
    ///
    /// The last condition is the whole entity, and it is easy to misread: the
    /// count only matters in a frame that has already produced a piece. A crate
    /// broken on its own is never refused however much debris is already lying
    /// around; a room of crates broken by one explosion is cut off at the
    /// budget. So this caps a burst, not the world — which is the right shape,
    /// because the cost being avoided is the spike rather than the total.
    ///
    /// Zero is a real answer and not "unset": it forbids pieces outright, which
    /// is how a map with a tight physics budget stops paying for debris nobody
    /// looks at. Negative is "no limit of mine".
    /// </summary>
    internal static bool Allows(bool allowNewGibs, int maxPieces, int livePieces, bool spawnedThisFrame)
    {
        if (allowNewGibs)
            return true;

        if (maxPieces < 0)
            return true;

        if (maxPieces == 0)
            return false;

        return !spawnedThisFrame || livePieces < maxPieces;
    }
}

/// <summary>
/// The map keys <see cref="GameGibManager"/> declares.
/// </summary>
[ScriptVars]
public partial class GameGibManagerVars
{
    /// <summary>Pieces to allow in one burst. Negative means "no limit of mine".</summary>
    public int MaxPieces { get; set; } = -1;

    /// <summary>The same budget for a machine below shader model 3.</summary>
    public int MaxPiecesDX8 { get; set; } = -1;

    /// <summary>
    /// Waives the budget entirely. A key rather than an input, and set on the
    /// entity that exists to impose the limit, which reads oddly until you see
    /// what it is for: a map keeps one manager and lets the difficult room turn
    /// the cap on by clearing this.
    /// </summary>
    public bool AllowNewGibs { get; set; }
}
