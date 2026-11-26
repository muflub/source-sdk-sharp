//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Server.Entities.Items;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// The crosshair's target ID, as a TF2 HUD shows it (plan_bots_teams_tf2 P7; the coordinator's ruling of 2026-11-20: exact
/// health is readable for the bot's heal target and for the player under its crosshair): the player the bot's view
/// points at and that player's health, as meters <see cref="Slot"/>, <see cref="Health"/>, <see cref="Most"/> and
/// <see cref="Enemy"/> (1 for a player not shown on the bot's team). Stock's rules, read off the client:
/// <list type="bullet">
/// <item>The target is the first player a line from the eye along the view meets (<c>C_TFPlayer::UpdateIDTarget</c>,
/// c_tf_player.cpp:7069-7118): <c>MASK_SOLID | CONTENTS_DEBRIS</c> from 10 u ahead of the eye, then the shot mask's
/// line when that one meets a player, as the medigun traces; a line that starts inside a teammate shows nobody.</item>
/// <item>Its health is shown (<c>CTargetID::IsValidIDTarget</c>, tf_hud_target_id.cpp:340-457) for a teammate always;
/// for an enemy drawn on the bot's team by a disguise, a Spy viewer, or a viewer whose kit has <c>see_enemy_health</c>
/// (the Solemn Vow), unless the target is cloaked.</item>
/// <item>An enemy Spy in disguise shows his disguise's health against the disguise class's most
/// (<c>CTargetID::UpdateID</c>, :818-826), never his own.</item>
/// </list>
/// Read for the classes that use it (<see cref="ReadsFor"/>): the Medic, for a teammate's exact health before it beams
/// him; the Spy, for an enemy's. Stock's retain cone (an enemy kept as the target a few degrees off the crosshair, :365-441)
/// is not ported: the target is only who the line meets.
/// <para>Registered plainly: a registered type joins every hook chain it implements (TFHudRegistrar adds the chain), and an
/// <c>As</c> naming <see cref="ITFHudMeters"/> would register the contract a second time beside <see cref="TFMedigunMeters"/>
/// (live, ctf_2fort: "ITFHudMeters is registered twice", the server did not start). Its own hook order keeps the chain's
/// orders unique.</para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFTargetIdMeters(IEngineEntities entities, IEnginePlayers players, IEngineEnvironment environment, ITraceQueries traces,
                                     ITFMapPlayers people, ITFActorFacts actors, TFClassTable classes, ITFOwnKit kits, ITFMedigunResists resists)
    : ITFHudMeters, IOrderedHook
{
    /// <summary>Every class's reader orders itself at 100 plus its class (coordinator notice 1, 2026-11-21): the Medic's.</summary>
    public int HookOrder => 100 + (int)TFClass.Medic;

    /// <summary>The meters' names: the target's player slot, its health shown, its most, and 1 when it is not drawn on the bot's team.</summary>
    public const string Slot = "target.slot", Health = "target.health", Most = "target.most", Enemy = "target.enemy";

    /// <summary>
    /// The Vaccinator's resist on a Medic's own charge meter (0 bullet, 1 blast, 2 fire; absent for another medigun): the Medic's one
    /// reader carries it, so the chain keeps one order per class (round 2).
    /// </summary>
    public const string Resist = "medic.resist";

    /// <summary>Stock's <c>MASK_SOLID | CONTENTS_DEBRIS</c> and <c>MASK_SHOT</c> (bspflags.h).</summary>
    public const uint IdMask = 0x0200400Bu | 0x04000000u, ShotMask = 0x46004003u;

    /// <summary>Stock's <c>MAX_TRACE_LENGTH</c> (worldsize.h), and the line's start ahead of the eye (10 u).</summary>
    public const float Reach = 56755.84f, StartAhead = 10f;

    /// <summary>The kit attribute that shows an enemy's health (the Solemn Vow: <c>see_enemy_health</c>).</summary>
    public const string SeeEnemyHealth = "see_enemy_health";

    /// <summary>
    /// Whether a bot of <paramref name="playerClass"/> reads the target ID: the Medic and the Spy, who play by it. A class whose lane
    /// wants it adds itself here (the read costs one or two lines a bot a sense tick).
    /// </summary>
    public static bool ReadsFor(TFClass playerClass) => playerClass is TFClass.Medic or TFClass.Spy;

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        if (playerClass == TFClass.Medic && resists.ResistType(bot.EntryIndex) is >= 0 and var resist)
            into.Add(new TFMeter(Resist, resist));
        if (!ReadsFor(playerClass) || !TryTarget(bot, out int index, out EntityHandle target))
            return;
        if (!Shown(bot, playerClass, index, target, out int health, out int most, out bool enemy))
            return;
        into.Add(new TFMeter(Slot, index));
        into.Add(new TFMeter(Health, health));
        into.Add(new TFMeter(Most, most));
        into.Add(new TFMeter(Enemy, enemy ? 1f : 0f));
    }

    /// <summary>The player the bot's view line meets first (stock's <c>UpdateIDTarget</c>), if any.</summary>
    private bool TryTarget(EntityHandle bot, out int index, out EntityHandle target)
    {
        (index, target) = (0, EntityHandle.None);
        Vector eye = entities.GetEyePosition(bot), ang = entities.GetEyeAngles(bot);
        float pitch = ang.X * (MathF.PI / 180f), yaw = ang.Y * (MathF.PI / 180f);
        var forward = new Vector(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        var start = new Vector(eye.X + forward.X * StartAhead, eye.Y + forward.Y * StartAhead, eye.Z + forward.Z * StartAhead);
        var end = new Vector(eye.X + forward.X * Reach, eye.Y + forward.Y * Reach, eye.Z + forward.Z * Reach);
        TraceAnswer t = traces.Line(start, end, IdMask, bot);
        int hit = PlayerIndexOf(t.Entity);
        if (hit == 0)
            return false;
        TraceAnswer shot = traces.Line(start, end, ShotMask, bot);
        int shotHit = PlayerIndexOf(shot.Entity);
        if (shot.Fraction < 1f && shotHit != 0 && (!t.StartSolid || shotHit != hit))
            (t, hit) = (shot, shotHit);
        // Starting inside a player shows nobody, unless an enemy: one may stand pressed against the bot.
        bool enemy = people.Team(players.GetHandle(hit)) != people.Team(bot);
        if (t.StartSolid && !enemy)
            return false;
        (index, target) = (hit, players.GetHandle(hit));
        return true;
    }

    /// <summary>The player slot of <paramref name="hit"/> when it is a live player, else 0.</summary>
    private int PlayerIndexOf(EntityHandle hit)
    {
        if (hit == EntityHandle.None)
            return 0;
        int i = hit.EntryIndex, max = environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0;
        return i >= 1 && i <= max && players.GetHandle(i) == hit && players.IsAlive(hit) ? i : 0;
    }

    /// <summary>Whether stock's target ID shows player <paramref name="index"/>'s health to the bot, and the health and most it shows.</summary>
    private bool Shown(EntityHandle bot, TFClass viewer, int index, EntityHandle target, out int health, out int most, out bool enemy)
    {
        (health, most) = (0, 0);
        int team = people.Team(bot);
        bool sameTeam = people.Team(target) == team;
        bool read = actors.TryRead(index, out TFActorFacts f);
        bool stealthed = read && (f.Cloaked || f.PercentInvisible > 0f);
        bool disguisedAsUs = read && f.Disguised && (int)f.DisguiseTeam == team;
        enemy = !sameTeam && !disguisedAsUs;
        if (!sameTeam && (stealthed || !(disguisedAsUs || viewer == TFClass.Spy || SeesEnemyHealth(bot.EntryIndex))))
            return false;
        if (!sameTeam && read && f.Disguised)
        {
            // A disguised enemy shows the disguise's health, against the disguise class's most.
            health = f.DisguiseHealth;
            most = classes.IsLoaded && f.DisguiseClass is > TFClass.Undefined and < TFClass.Civilian ? classes[f.DisguiseClass].MaxHealth : people.MaxHealth(target);
            return true;
        }

        (health, most) = (people.Health(target), people.MaxHealth(target));
        return true;
    }

    /// <summary>Whether the bot's own kit carries <see cref="SeeEnemyHealth"/>, as its item's tooltip says.</summary>
    private bool SeesEnemyHealth(int index)
    {
        foreach (TFOwnKitItem item in kits.KitOf(index))
        {
            foreach (TFOwnKitAttribute a in item.Attributes)
            {
                if (a.Class == SeeEnemyHealth && a.Value > 0f)
                    return true;
            }
        }

        return false;
    }
}
