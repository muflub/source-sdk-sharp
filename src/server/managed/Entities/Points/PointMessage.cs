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
/// point_message: a line of text a mapper hangs in the world, readable when you
/// stand near it.
///
/// <para>
/// Server-only, and it has to be: every decision in it is about a player's
/// position relative to this entity, which is exactly what a server knows and a
/// client half would have to be told anyway. What crosses to the client is the
/// text, once, through the debug-overlay channel — a mapper's annotation
/// belongs to a place rather than to a screen, and there is no other way to put
/// text at a world position without building a font renderer.
/// </para>
///
/// <para>
/// The one real change is who it draws for. Stock asks <c>UTIL_GetLocalPlayer</c>
/// for the distance test, which returns NULL in any game with more than one
/// slot and asserts while doing it — so in a deathmatch mod stock's
/// point_message shows nothing to anybody. This one measures against the
/// NEAREST player, which is the same rule in a game that has more than one.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointMessage)]
public sealed class PointMessage : ScriptedEntity
{
    public PointMessage(IEngineConsole console, IEngineHud hud, IEnginePlayers players)
    {
        Console = console;
        Hud = hud;
        Players = players;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;

    /// <summary>drawing, and the local player it is drawn for.</summary>
    private readonly IEngineHud Hud;

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>Placed with the message already off. Stock's 1.</summary>
    private const int StartDisabledFlag = 1;

    /// <summary>What a map may set on this entity. See <see cref="PointMessageVars"/>.</summary>
    [ScriptVars] public PointMessageVars Vars = new();

    /// <summary>
    /// Set once from the start-disabled spawnflag in <see cref="OnSpawn"/>,
    /// which a hot reload does not run again -- so it is <c>[SaveField]</c>.
    /// Uncarried, the rebuilt instance read false and a start-enabled message
    /// never painted again (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _enabled;

    /// <summary>
    /// Stock's tenth of a second, and it is both the test rate and the lifetime
    /// handed to the overlay. Those have to match: an overlay outliving its
    /// think would stay on screen after the player walked away, and one shorter
    /// than the think would flicker.
    /// </summary>
    private const float Interval = 0.1f;

    public override void OnSpawn()
    {
        _enabled = (SpawnFlags & StartDisabledFlag) == 0;
        SetNextThink(Interval);
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => _enabled = true;

    [ScriptedEvent("Disable")]
    private void OnDisable() => _enabled = false;

    public override void OnThink()
    {
        SetNextThink(Interval);

        if (!_enabled || Vars.Message.Length == 0)
            return;

        // The developer gate is stock's and is read live rather than latched:
        // a mapper turns developer on to see their annotations and expects them
        // to appear without a map reload.
        if (Vars.DeveloperOnly && ParseInt(Console.FindConVarValue("developer") ?? "0") == 0)
            return;

        if (!AnyPlayerWithinRadius())
            return;

        // Line 0 and a lifetime of exactly one think. Stock passes a duration
        // of zero, which means one FRAME, and then relies on being redrawn from
        // the game frame every frame; this is redrawn ten times a second, so
        // the overlay has to last that long or it would be visible one frame in
        // seven.
        Hud.DrawWorldText(Origin, 0, Vars.Message, Interval);
    }

    /// <summary>
    /// Whether anybody is close enough to read it.
    ///
    /// <para>
    /// From the EYE position, as stock does — a message hung at head height in
    /// a corridor should appear when you can see it, not when your feet reach
    /// the same distance.
    /// </para>
    ///
    /// <para>
    /// Stock also skips a player carrying FL_NOTARGET, which this does not.
    /// That flag is set by the `notarget` cheat and read by AI, and adding an
    /// ABI slot so a cheat can hide a mapper's annotation from the person who
    /// typed it is the wrong trade. Recorded in docs/divergences.md.
    /// </para>
    /// </summary>
    private bool AnyPlayerWithinRadius()
    {
        float radiusSquared = (float)Vars.Radius * Vars.Radius;
        Vector origin = Origin;

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            var player = Players.GetHandle(slot);

            if (!Entities.IsValid(player))
                continue;

            Vector delta = Entities.GetEyePosition(player) - origin;
            float distanceSquared = (delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z);

            if (distanceSquared <= radiusSquared)
                return true;
        }

        return false;
    }
}

/// <summary>
/// The map keys <see cref="PointMessage"/> declares.
/// </summary>
[ScriptVars]
public partial class PointMessageVars
{
    public string Message { get; set; } = string.Empty;

    public int Radius { get; set; } = 128;

    public bool DeveloperOnly { get; set; }
}
