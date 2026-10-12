//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

//-----------------------------------------------------------------------------
// THE PLAYER'S OWN CONSOLE COMMANDS (plan_shim.md §5 3b, lane 3c-entityshell).
//
// Fifteen registrations and a ~120-line verb table lived in
// shell_standalone_player.cpp. They were native for one reason and it was a
// good one: two of the six things they do had no managed crossing. setang needs
// CPlayerState::fixangle, which is engine memory the engine reads back through
// GetPlayerState, and noclip needs a move-type write from BETWEEN frames rather
// than from inside the movement loop that owns the field. Both crossings exist
// now (PlayerSnapEyeAngles, PlayerSetMoveType), so the policy has no reason to
// be C++ any more: which damage type a suicide carries, whether a cheat is
// refused out loud, and what "setpos" does to velocity are decisions.
//
// THE SPELLINGS ARE NOT THIS PORT'S TO CHOOSE. A person types god, not ss_god;
// tools/ drives the game with setpos and setang (19 and 14 files); and
// stock's registrations of these names are #ifndef SOURCESHARP'd at their own
// sites precisely so these can hold them. A renamed console command is a
// deleted one from the point of view of anyone with it in a script.
//
// WHY THEY ARE ConCommands AND NOT ClientCommand BRANCHES. A registered name
// never reaches IServerGameClients::ClientCommand -- the engine dispatches it
// first -- which is what made stock's own handlers dead without being missing:
// they resolved a CBasePlayer, got NULL for a hierarchy player, and returned
// without a word. Registration is also the only path engine->ServerCommand
// reaches, and that is how every tool in this tree, and this mod's development
// API, drives the game.
//
// THE sv_cheats GATE IS PER COMMAND AND IS DevCommand's, which refuses OUT
// LOUD and in stock's own words. The native side spelt the same gate by hand
// in StandalonePlayer_ClientCommand, with the same message. Note which commands
// do NOT carry it: kill and explode, because stock's did not -- kill is an
// ordinary command in every Source game and explode is the same command with a
// different damage type. That was true of the native spellings too, which
// returned before reaching their cheat check.
//
// NO FCVAR_CHEAT on the registrations, for the reason DevCommands.cs states at
// length and the native file repeated: the engine's flag check is a second,
// path-dependent gate that refuses differently depending on where the word came
// from, and it refuses in SILENCE.
//-----------------------------------------------------------------------------

/// <summary>
/// What every command here needs beyond <see cref="DevContext"/>: the damage
/// path and the two player-movement crossings.
///
/// <para>
/// A base class rather than six copies of the same two fields, and it exists
/// mainly so that <see cref="Suicide"/> is written once. The difference between
/// <c>kill</c> and <c>explode</c> is one damage type, and two hand-written
/// copies of a suicide is how they drift into being two different deaths.
/// </para>
/// </summary>
public abstract class PlayerSelfCommand : DevCommand
{
    protected PlayerSelfCommand(DevContext context, IEngineDamage damage,
                                IEnginePlayerMovement movement)
        : base(context)
    {
        Damage = damage;
        Movement = movement;
    }

    protected IEngineDamage Damage { get; }
    protected IEnginePlayerMovement Movement { get; }

    /// <summary>
    /// Enough damage to end it, through the ordinary damage path.
    ///
    /// <para>
    /// THROUGH THE DAMAGE PATH AND NOT BY SETTING HEALTH TO ZERO, so that the
    /// managed rules see a suicide exactly as they see every other death — the
    /// death event, the score, the drop, the ragdoll. Stock's
    /// <c>CommitSuicide</c> did the same thing for the same reason.
    /// </para>
    ///
    /// <para>
    /// THE DAMAGE TYPE IS NOT COSMETIC. It is what the death rule reads to
    /// decide gibbing and the death effect, so a suicide that claimed to be
    /// generic would be a different death from the one that was asked for.
    /// <c>kill_helper</c>'s <c>bExplode</c> picks exactly this in stock.
    /// </para>
    ///
    /// <para>
    /// The attacker is nobody — <see cref="EntityHandle.None"/> — which is what
    /// the native spelling passed (a NULL <c>CTakeDamageInfo</c> attacker) and
    /// is what makes a suicide read as one rather than as a kill by the victim.
    /// </para>
    /// </summary>
    protected void Suicide(EntityHandle player, DamageType type)
        => Damage.Apply(player, EntityHandle.None, SuicideDamage, type);

    /// <summary>
    /// The number the native spelling used. Large enough that no armour, buff
    /// or rule leaves the player standing, which is what "kill" has to mean —
    /// a suicide that failed because of a damage reduction would be a command
    /// that silently did nothing, which is the whole class of bug this file's
    /// port exists to close.
    /// </summary>
    public const float SuicideDamage = 10000f;
}

//=============================================================================
// Dying on purpose. Neither is cheat-gated, because stock's were not.
//=============================================================================

/// <summary>
/// <c>kill</c> — generic damage, enough of it.
/// </summary>
[ConCommand("kill", Help = "Kills the player with generic damage")]
public sealed class KillCommand : PlayerSelfCommand
{
    public KillCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override bool RequiresCheats => false;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        Suicide(issuer, DamageType.Generic);

        // SAID. The native kill branch was silent, which is survivable only
        // because a dead player is visible — every other command in this set
        // names what it did, and this is not an exception worth having.
        Log.Msg("kill: the player was killed.\n");
    }
}

/// <summary>
/// <c>explode</c> — the same suicide with <see cref="DamageType.Blast"/> and
/// <see cref="DamageType.AlwaysGib"/> on it, which is the whole difference
/// between the two commands in stock as well.
/// </summary>
[ConCommand("explode", Help = "Kills the player with explosive damage")]
public sealed class ExplodeCommand : PlayerSelfCommand
{
    public ExplodeCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override bool RequiresCheats => false;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        Suicide(issuer, DamageType.Blast | DamageType.AlwaysGib);
        Log.Msg("explode: the player was killed by blast damage.\n");
    }
}

/// <summary>
/// <c>ss_kill</c> — the hierarchy-explicit spelling of <c>kill</c>.
///
/// <para>
/// KEPT, AND KEPT NATIVE-FREE. The <c>ss_*</c> names were the ones that worked
/// while the stock spellings were dead, so they are in people's fingers and in
/// this tree's own tooling — <c>tools/</c> references <c>ss_ground</c>. Sharing
/// the body rather than repeating it is what keeps the two spellings from
/// becoming two behaviours.
/// </para>
/// </summary>
[ConCommand("ss_kill", Help = "Kills the standalone player issuing it")]
public sealed class SsKillCommand : PlayerSelfCommand
{
    public SsKillCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override bool RequiresCheats => false;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        Suicide(issuer, DamageType.Generic);
        Log.Msg("ss_kill: the player was killed.\n");
    }
}

//=============================================================================
// Hurting without killing.
//=============================================================================

/// <summary>
/// <c>hurtme</c> — take a stated amount of damage.
///
/// <para>
/// <see cref="DamageType.PreventPhysicsForce"/>, as the native spelling passed:
/// the point is to lose health, and a shove from a damage force would move the
/// player away from whatever they were standing in front of to measure.
/// </para>
/// </summary>
[ConCommand("hurtme", Help = "Hurts the player.\n\tArguments: <health to lose> (must have sv_cheats)")]
public class HurtMeCommand : PlayerSelfCommand
{
    public HurtMeCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    /// <summary>Stock's default when the command is typed bare.</summary>
    public const float DefaultDamage = 10f;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        float amount = DefaultDamage;

        // A BAD NUMBER IS REFUSED, not read as zero. atof answered 0 for
        // anything unparseable, so `hurtme x` did nothing and said nothing —
        // indistinguishable from the command being dead, which for a long time
        // it was.
        if (argv.Length >= 1 &&
            !float.TryParse(argv[0], NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
        {
            Log.Msg($"{Verb}: '{argv[0]}' is not a number\n");
            return;
        }

        Damage.Apply(issuer, EntityHandle.None, amount, DamageType.PreventPhysicsForce);
        Log.Msg($"{Verb}: {amount:F0} damage\n");
    }
}

/// <summary><c>ss_hurtme</c> — the hierarchy-explicit spelling.</summary>
[ConCommand("ss_hurtme",
    Help = "Hurts the standalone player issuing it: ss_hurtme <damage>")]
public sealed class SsHurtMeCommand : HurtMeCommand
{
    public SsHurtMeCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }
}

//=============================================================================
// Being somewhere else.
//=============================================================================

/// <summary>
/// <c>setpos</c> — put the player at a stated world position.
///
/// <para>
/// <b>The velocity is cleared, and that is not tidiness.</b> A player moved
/// while falling arrives with the fall still on them and keeps going, which
/// makes the command read as "setpos put me somewhere near where I asked".
/// The native spelling cleared it for the same reason.
/// </para>
///
/// <para>
/// <c>setpos_exact</c> differs as stock's does: it TELEPORTS, moving the
/// player's physics shadow with them (see <see cref="SetPosExactCommand"/>).
/// </para>
/// </summary>
[ConCommand("setpos", Help = "Move player to specified origin (must have sv_cheats).")]
public class SetPosCommand : PlayerSelfCommand
{
    public SetPosCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 3)
        {
            Log.Msg($"usage: {Verb} x y z\n");
            return;
        }

        if (!TryVector(argv, 0, requireAll: true, out Vector to, out string offender))
        {
            Log.Msg($"{Verb}: '{offender}' is not a number\n");
            return;
        }

        Place(issuer, to);

        Log.Msg($"{Verb}: {to.X:F1} {to.Y:F1} {to.Z:F1}\n");
    }

    /// <summary>
    /// Stock's <c>setpos</c>: the origin alone (client.cpp:1280). The player's
    /// physics shadow is left where it was and catches up through the
    /// controller. The velocity is cleared (see the class).
    /// </summary>
    protected virtual void Place(EntityHandle issuer, Vector to)
    {
        Context.Entities.SetOrigin(issuer, to);
        Context.Entities.SetVelocity(issuer, Vector.Zero);
    }

    /// <summary>
    /// Three floats from <paramref name="argv"/> at <paramref name="from"/>, or
    /// the first word that is not one.
    ///
    /// <para>
    /// <see cref="CultureInfo.InvariantCulture"/> because a console is typed at
    /// in ASCII whatever the machine's locale is: a comma-decimal locale would
    /// otherwise read <c>1.5</c> as <c>15</c>.
    /// </para>
    ///
    /// <para>
    /// <paramref name="requireAll"/> is false for the angle commands, whose
    /// roll is optional in stock — a missing word there is 0 and a PRESENT bad
    /// word is still a refusal, which is the distinction the flag draws.
    /// </para>
    /// </summary>
    internal static bool TryVector(string[] argv, int from, bool requireAll,
                                   out Vector vector, out string offender)
    {
        vector = default;
        offender = string.Empty;

        Span<float> parsed = stackalloc float[3];

        for (int i = 0; i < 3; i++)
        {
            int at = from + i;

            if (at >= argv.Length || argv[at].Length == 0)
            {
                if (requireAll)
                {
                    offender = at < argv.Length ? argv[at] : string.Empty;
                    return false;
                }

                parsed[i] = 0f;
                continue;
            }

            if (!float.TryParse(argv[at], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out parsed[i]))
            {
                offender = argv[at];
                return false;
            }
        }

        vector = new Vector(parsed[0], parsed[1], parsed[2]);
        return true;
    }
}

/// <summary>
/// <c>setpos_exact</c>. Stock's is <c>pPlayer->Teleport( &amp;newpos, NULL, NULL )</c>
/// (client.cpp:1355): a teleport, which moves the player's physics shadow with
/// them, where <see cref="SetPosCommand"/> sets the origin alone and leaves the
/// shadow behind for a tick. The family's velocity rule is kept.
/// </summary>
[ConCommand("setpos_exact",
    Help = "Move player to an exact specified origin (must have sv_cheats).")]
public sealed class SetPosExactCommand : SetPosCommand
{
    public SetPosExactCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override void Place(EntityHandle issuer, Vector to)
        => Context.Entities.Teleport(issuer, to, angles: null, zeroVelocity: true);
}

/// <summary><c>ss_setpos</c> — the hierarchy-explicit spelling.</summary>
[ConCommand("ss_setpos",
    Help = "Moves the standalone player issuing it: ss_setpos <x> <y> <z>")]
public sealed class SsSetPosCommand : SetPosCommand
{
    public SsSetPosCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }
}

//=============================================================================
// Looking somewhere else.
//=============================================================================

/// <summary>
/// <c>setang</c> — snap the player's view to a stated pitch and yaw.
///
/// <para>
/// Through <see cref="IEnginePlayerMovement.SnapEyeAngles"/> rather than
/// through an entity-angle write, because a player's VIEW is
/// <c>CPlayerState::v_angle</c> and an entity's angles are where its model
/// points. The crossing sets <c>fixangle</c> with it, which is what tells the
/// client to adopt the value instead of predicting from its own — without that,
/// the next usercmd's stale viewangles undo the snap before anyone sees it.
/// </para>
///
/// <para>
/// Roll is optional, as it is in stock, and defaults to 0.
/// </para>
/// </summary>
[ConCommand("setang",
    Help = "Snap player eyes to specified pitch yaw <roll:optional> (must have sv_cheats).")]
public class SetAngCommand : PlayerSelfCommand
{
    public SetAngCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 2)
        {
            Log.Msg($"usage: {Verb} pitch yaw <roll optional>\n");
            return;
        }

        if (!SetPosCommand.TryVector(argv, 0, requireAll: false,
                                     out Vector angles, out string offender))
        {
            Log.Msg($"{Verb}: '{offender}' is not a number\n");
            return;
        }

        Movement.SnapEyeAngles(issuer, angles);

        Log.Msg($"{Verb}: {angles.X:F1} {angles.Y:F1} {angles.Z:F1}\n");
    }
}

/// <summary>
/// <c>setang_exact</c>. One rule with <see cref="SetAngCommand"/>: the snap is
/// already exact, because <c>fixangle</c> ABSOLUTE makes the client adopt
/// <c>v_angle</c> wholesale.
/// </summary>
[ConCommand("setang_exact",
    Help = "Snap player eyes and orientation to specified pitch yaw <roll:optional> (must have sv_cheats).")]
public sealed class SetAngExactCommand : SetAngCommand
{
    public SetAngExactCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }
}

//=============================================================================
// Not being solid.
//=============================================================================

/// <summary>
/// <c>noclip</c> — toggle between walking and flying through the world.
///
/// <para>
/// ONE CROSSING, NOT TWO. <see cref="IEnginePlayerMovement.SetMoveType"/>
/// answers the move type the player had before the write, so the toggle needs
/// no separate read — and a read and a write with a frame between them would be
/// a frame of movement decided by the state being toggled.
/// </para>
///
/// <para>
/// A player in any move type that is NOT noclip walks out of this, which is
/// what stock's toggle did: it compared against MOVETYPE_NOCLIP and put back
/// MOVETYPE_WALK, rather than remembering what the player had been in.
/// </para>
/// </summary>
[ConCommand("noclip", Help = "Toggle. Player becomes non-solid and flies. (must have sv_cheats)")]
public class NoClipCommand : PlayerSelfCommand
{
    public NoClipCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        MoveType was = Movement.SetMoveType(issuer, MoveType.NoClip);

        // ALREADY FLYING: the answer that came back says so, and the write just
        // made was a no-op. Put walking back.
        if (was == MoveType.NoClip)
        {
            Movement.SetMoveType(issuer, MoveType.Walk);
            Log.Msg("noclip OFF\n");
            return;
        }

        // NOT A PLAYER, and worth telling apart from the toggle. No player is
        // ever in MoveType.None, so this is the crossing saying it could not
        // resolve the handle rather than reporting a state.
        if (was == MoveType.None)
        {
            Log.Msg($"{Verb}: the issuer is not a hierarchy player, so nothing was done.\n");
            return;
        }

        Log.Msg("noclip ON\n");
    }
}

/// <summary><c>ss_noclip</c> — the hierarchy-explicit spelling.</summary>
[ConCommand("ss_noclip", Help = "Toggles noclip on the standalone player issuing it")]
public sealed class SsNoClipCommand : NoClipCommand
{
    public SsNoClipCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }
}

//=============================================================================
// Hiding from nobody.
//=============================================================================

/// <summary>
/// <c>notarget</c> — and the honest answer that it does nothing here.
///
/// <para>
/// STOCK'S RULE HAS NOTHING TO ACT ON IN THIS BUILD, and saying so is the whole
/// reason the command is registered at all. <c>notarget</c> sets FL_NOTARGET,
/// which is read by <c>CAI_BaseNPC</c>'s target selection — and this build
/// compiles no AI. A flag nothing reads is not a cheat; it is a lie with a
/// confirmation message.
/// </para>
///
/// <para>
/// So it answers rather than toggling a bit and reporting "notarget ON" over a
/// game where it means nothing. When AI arrives this becomes a rule beside god
/// and buddha in <see cref="DamageCheats"/>.
/// </para>
/// </summary>
[ConCommand("notarget", Help = "Toggle. Player becomes hidden to NPCs. (must have sv_cheats)")]
public sealed class NoTargetCommand : PlayerSelfCommand
{
    public NoTargetCommand(DevContext context, IEngineDamage damage, IEnginePlayerMovement movement)
        : base(context, damage, movement) { }

    protected override void Run(EntityHandle issuer, string[] argv)
        => Log.Msg("notarget: nothing on this server runs AI, so there is nobody to hide from.\n");
}

//=============================================================================
// The two damage-rule cheats, which already had a managed home.
//=============================================================================

/// <summary>
/// <c>god</c> — invulnerability, toggled out loud.
///
/// <para>
/// <b>A REGISTRATION, NOT A SECOND IMPLEMENTATION.</b>
/// <see cref="DamageCheats.ToggleGod"/> is the rule and has been since the
/// damage rules went managed; what was still native was the CON_COMMAND that
/// carried the word to it, because a registered name is the only way the server
/// console and <c>engine-&gt;ServerCommand</c> can reach anything.
/// <see cref="ServerModule.OnClientCommand"/> reaches the same method for a
/// word typed at a client console, and the two are alternative doors to one
/// room rather than two rooms.
/// </para>
///
/// <para>
/// NOT a <see cref="DevCommand"/>: the sv_cheats gate for these two is inside
/// <see cref="DamageCheats"/>, where stock's FCVAR_CHEAT was — on the TOGGLE.
/// Gating here as well would refuse twice and print twice.
/// </para>
/// </summary>
[ConCommand("god", Help = "Toggle. Player becomes invulnerable. (must have sv_cheats)")]
public sealed class GodCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly DamageCheats _cheats;
    private readonly IEnginePlayers _players;

    public GodCommand(ILog log, DamageCheats cheats, IEnginePlayers players)
    {

        _log = log;
        _cheats = cheats;
        _players = players;
    }

    public void Execute(string args)
    {
        EntityHandle issuer = _players.GetCommandClient();

        // SAID, not swallowed. "No player issued that" and "the player ignored
        // it" are the same nothing from a console, and telling them apart by
        // hand is what this whole family of bugs has cost.
        if (issuer == EntityHandle.None)
        {
            _log.Msg("god: no player issued that, so nothing was done.\n");
            return;
        }

        _cheats.ToggleGod(issuer);
    }
}

/// <summary>
/// <c>buddha</c> — take damage but do not die. <see cref="GodCommand"/>'s
/// reasoning applies unchanged.
/// </summary>
[ConCommand("buddha", Help = "Toggle. Player takes damage but won't die. (must have sv_cheats)")]
public sealed class BuddhaCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly DamageCheats _cheats;
    private readonly IEnginePlayers _players;

    public BuddhaCommand(ILog log, DamageCheats cheats, IEnginePlayers players)
    {

        _log = log;
        _cheats = cheats;
        _players = players;
    }

    public void Execute(string args)
    {
        EntityHandle issuer = _players.GetCommandClient();

        if (issuer == EntityHandle.None)
        {
            _log.Msg("buddha: no player issued that, so nothing was done.\n");
            return;
        }

        _cheats.ToggleBuddha(issuer);
    }
}
