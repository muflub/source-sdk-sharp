using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>The binding's numbers for the things TF2 draws that are not players (<see cref="ThingFacts.Type"/>).</summary>
public enum TFThingType
{
    None = 0,
    Rocket,
    Pipe,
    Sticky,
    Arrow,
    Flare,
    HealingBolt,
    Jar,
    Cleaver,
    Syringe,
    Sentry = 20,
    Dispenser,
    TeleporterEntrance,
    TeleporterExit,
}

/// <summary>
/// What a client sees of a player's body past his conditions (round 2's S4): how deep in water he stands (the engine's
/// <c>GetWaterLevel</c>, 0 out of it) and whether he stands on the ground. Both are what any client draws of him.
/// </summary>
public readonly record struct TFBody(int WaterLevel, bool OnGround)
{
    /// <summary>A body with no movement state to read: dry and on the ground, so nothing reads it wet or thrown.</summary>
    public static TFBody Unread => new(0, OnGround: true);

    /// <summary>The body a movement state's water level and flags (<c>FL_ONGROUND</c>) describe.</summary>
    public static TFBody Of(int waterLevel, uint flags) => new(waterLevel, (flags & (uint)GameMovement.FlOnGround) != 0);
}

/// <summary>A player's body as every client draws it (<see cref="TFBody"/>), for the view's wet and blast-airborne reads.</summary>
public interface ITFBodies
{
    /// <summary>The player's water and ground; <see cref="TFBody.Unread"/> for an entity with no movement state.</summary>
    TFBody BodyOf(EntityHandle player);
}

/// <summary>The server's bodies: the state the command loop published for the player (<see cref="SsPlayer.State"/>).</summary>
[SideService(Side.Server, As = [typeof(ITFBodies)])]
public sealed class TFBodies(IEntityDirectory directory) : ITFBodies
{
    public TFBody BodyOf(EntityHandle player)
        => directory.Get(player) is SsPlayer p ? TFBody.Of(p.State.WaterLevel, p.State.Flags) : TFBody.Unread;
}

/// <summary>
/// What a TF2 client draws past the engine (plan_bots_teams_tf2 C.3, P6 step 3), for the
/// generic senses (<see cref="IBotGameView"/>). Everything is what the mod sends every client:
/// <see cref="ITFActorFacts"/> per player, the projectiles' rows and the buildings'.
///
/// <para>
/// HONESTY ABOUT THE SPY, BOTH WAYS. To a viewer on the Spy's own team a disguised Spy is drawn as
/// what he is, a Spy (the paper mask): his own team and class, the disguise shown. To anyone else
/// he is the disguise: its team, its class, the borrowed name's slot and the disguise class's
/// primary in his hands, and no sign of a disguise. A cloak fades him by the client's invisibility
/// level and past <see cref="CloakHidden"/> he is not drawn at all (stock's 75 % in
/// <c>tf_bot_vision.cpp:258-302</c>'s vocabulary). The real team is read only to choose between
/// those two drawings; it never reaches a view of an enemy.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFBotGameView(ITFActorFacts actors, ITFPlayerData players, IEngineEntities entities, IEntityDirectory directory,
                                  TFDisguiseWeapons disguises, WeaponCatalog weapons, TFObjectiveFeed objectives,
                                  ITFRoundStateView rounds, TFPlayerMovementRestartFreezeConVar restartFreeze, TFHudReader hud,
                                  TFPlayerLives lives, TFAvoidTeammatesConVar avoidTeammates, ITFBodies bodies) : IBotGameView
{
    /// <summary>How invisible a cloaked Spy is when a client draws nothing of him.</summary>
    public const float CloakHidden = 0.75f;

    /// <summary>The least of a cloaked Spy a cue shows (flames, Jarate, a blink, blood): as much as a cloak just under hiding him.</summary>
    public const float CueShows = 1f - CloakHidden;

    /// <summary>How far a projectile's body reaches round its origin, and a building's (its middle is half that over its feet).</summary>
    public const float ProjectileReach = 8f, BuildingReach = 48f;

    public SelfState Hud(EntityHandle bot, in SelfState read)
    {
        int index = bot.EntryIndex;
        if (!actors.TryRead(index, out TFActorFacts own))
            return read;
        int metal = players.Networked(index) is { } state ? state.Ammo[(int)TFAmmo.Metal] : 0;
        return read with { Class = (int)own.Class, Conditions = Conditions(in own, disguiseShown: true, bodies.BodyOf(bot), own: true), Metal = metal,
                           Flags = Frozen(bot) ? read.Flags | SelfFlags.Frozen : read.Flags };
    }

    /// <summary>
    /// Whether <paramref name="bot"/>'s own HUD marks the player in <paramref name="slot"/> as dominating it
    /// (plan_bots_teams_tf2_part_g G.2.5): the row of its own networked state its client reads for the
    /// scoreboard's nemesis mark (<c>m_bPlayerDominatingMe</c>), and nobody else's.
    /// </summary>
    public bool DominatedBy(EntityHandle bot, int slot)
        => players.Networked(bot.EntryIndex) is { } own && own.IsPlayerDominatingMe(slot);

    /// <summary>
    /// The pre-round freeze, as the bot's client knows it: its HUD shows the round restarting (the
    /// round state every client is sent) and the replicated <c>tf_player_movement_restart_freeze</c>
    /// stands, the two things the movement's own speed walk asks (TFMovementRules.MaxSpeedScale).
    /// </summary>
    public bool Frozen(EntityHandle bot) => rounds.State == RoundState.Preround && restartFreeze.Value;

    /// <summary>The bot's kit, meters and the teammates' health its HUD shows (<see cref="TFHudReader"/>, plan_bots_teams_tf2 P7).</summary>
    public GameHud? HudOf(EntityHandle bot) => hud.Read(bot);

    public bool Render(EntityHandle viewer, EntityHandle them, ref ActorFacts facts)
    {
        if (!actors.TryRead(them.EntryIndex, out TFActorFacts drawn))
            return true;
        int viewerTeam = actors.TryRead(viewer.EntryIndex, out TFActorFacts own) ? (int)own.Team : 0;
        int dress = disguises.DressOf(them.EntryIndex) is { } row ? weapons.IndexOf(row) : -1;
        return Draw(viewerTeam, in drawn, dress, ref facts, bodies.BodyOf(them));
    }


    // Each player's disguise as an alias (the Spy's honesty fix, plan_bots_teams_tf2 C.3): the disguise it stands for and
    // its actor, by player index. Shared by every viewer the disguise fools (the review: per Spy and disguise, not per viewer).
    private readonly (ulong Key, ActorId Alias)[] _aliases = new (ulong, ActorId)[VisionPass.Slots];
    private int _aliasesMade;

    /// <summary>Aliases made, one a disguise: <c>ai.tf.aliases</c>, for the facts.</summary>
    public int AliasesMade => _aliasesMade;

    public ActorId ShownAs(EntityHandle viewer, EntityHandle them, ActorId real)
    {
        int index = them.EntryIndex;
        if ((uint)index >= (uint)_aliases.Length || !actors.TryRead(index, out TFActorFacts drawn))
            return real;
        if (!drawn.Disguised)
            _aliases[index] = default;
        int viewerTeam = actors.TryRead(viewer.EntryIndex, out TFActorFacts own) ? (int)own.Team : 0;
        if (!PassesAsMate(viewerTeam, in drawn))
            return real;
        ulong key = DisguiseKey(in drawn, lives.LifeOf(index));
        ref (ulong Key, ActorId Alias) row = ref _aliases[index];
        if (row.Key != key || !row.Alias.IsValid)
            row = (key, ActorId.Alias(AliasSlot(in drawn, index), ++_aliasesMade));
        return row.Alias;
    }

    /// <summary>
    /// TF2's collision (TFTeamCollide): players of two game teams collide and teammates pass through, by the real teams as the
    /// physics has them; with tf_avoidteammates 0 teammates collide too, so a touch tells nothing and none is solid news.
    /// </summary>
    public bool Solid(EntityHandle viewer, EntityHandle them)
        => avoidTeammates.Value && actors.TryRead(viewer.EntryIndex, out TFActorFacts own) && actors.TryRead(them.EntryIndex, out TFActorFacts other)
           && own.Team != other.Team && own.Team is TFTeam.Red or TFTeam.Blue && other.Team is TFTeam.Red or TFTeam.Blue;

    /// <summary>Whether a client of <paramref name="viewerTeam"/> draws <paramref name="them"/> as a disguise: anyone not on the Spy's team.</summary>
    public static bool Fools(int viewerTeam, in TFActorFacts them) => them.Disguised && !(viewerTeam != 0 && viewerTeam == (int)them.Team);

    /// <summary>
    /// Whether the disguise passes <paramref name="them"/> off as one of <paramref name="viewerTeam"/>'s own: an enemy dressed
    /// in the viewer's team, the case the alias is for (review F3). A Spy dressed in his own team (<c>disguise N -2</c>) is
    /// still drawn on his own, an enemy's team, and stays the real player, a target like any enemy.
    /// </summary>
    public static bool PassesAsMate(int viewerTeam, in TFActorFacts them)
        => them.Disguised && viewerTeam != 0 && viewerTeam != (int)them.Team && (int)them.DisguiseTeam == viewerTeam;

    /// <summary>The slot an alias is drawn in: the borrowed name's, whose scoreboard row the disguise claims; the Spy's own when it borrowed none.</summary>
    public static int AliasSlot(in TFActorFacts them, int own) => them.DisguiseTargetIndex > 0 ? them.DisguiseTargetIndex : own;

    /// <summary>
    /// One disguise of one player's life: its class, team and borrowed name, and the life it is worn in
    /// (<see cref="TFPlayerLives"/>, his spawns), so the same dress after a respawn is another alias (review F7).
    /// </summary>
    public static ulong DisguiseKey(in TFActorFacts them, int life)
        => ((ulong)(uint)life << 32) | ((ulong)(byte)them.DisguiseClass << 24) | ((ulong)(byte)them.DisguiseTeam << 16)
           | (ushort)them.DisguiseTargetIndex;

    /// <summary>
    /// What a client of <paramref name="viewerTeam"/> draws of a player whose mod facts are
    /// <paramref name="them"/>, over the engine's <paramref name="facts"/>; false when it draws
    /// nothing. <paramref name="dressWeapon"/> is the catalog kind of the weapon his disguise
    /// shows in his hands, -1 for none; <paramref name="body"/> his water and ground.
    /// </summary>
    public static bool Draw(int viewerTeam, in TFActorFacts them, int dressWeapon, ref ActorFacts facts, TFBody body)
    {
        // A cloak hides past 75 % unless something on the body shows it: flames, Jarate, a blink where it was struck, blood
        // (stock's tf_bot_vision.cpp:264-271 notices them whatever the cloak, as a player does).
        bool cue = them.Burning || them.Jarated || them.Blinking || them.Bleeding;
        if (them.PercentInvisible >= CloakHidden && !cue)
            return false;
        bool sameTeam = viewerTeam != 0 && viewerTeam == (int)them.Team;
        // A cue shows the body however deep the cloak: never less of it than CueShows (review-p7f F5: a fully cloaked burning Spy read 0).
        float shown = 1f - them.PercentInvisible;
        if (cue && them.Cloaked)
            shown = MathF.Max(shown, CueShows);
        facts = facts with
        {
            VisibleFraction = facts.VisibleFraction * shown,
            Conditions = Conditions(in them, disguiseShown: sameTeam, body),
        };
        if (Fools(viewerTeam, in them))
        {
            facts = facts with
            {
                Team = (int)them.DisguiseTeam,
                Class = (int)them.DisguiseClass,
                NameSlot = ShownName(in them, facts.NameSlot),
                WeaponSeen = dressWeapon >= 0 ? dressWeapon : facts.WeaponSeen,
            };
        }
        else
        {
            facts = facts with { Class = (int)them.Class };
        }

        return true;
    }

    /// <summary>
    /// The slot whose name an enemy's target ID shows over a disguised Spy (<c>tf_hud_target_id.cpp:764-793</c>): the
    /// borrowed player's, unless he borrowed none, when stock's target ID keeps the Spy's own name (<paramref name="own"/>,
    /// the slot the engine read gave him). Cloaked he keeps the borrowed one: stock's target ID shows a stealthed enemy no
    /// name at all, and his own here would mark him and name his real row (review F7).
    /// </summary>
    public static int ShownName(in TFActorFacts them, int own)
        => them.Disguised && them.DisguiseTargetIndex > 0 ? them.DisguiseTargetIndex : own;

    /// <summary>
    /// The conditions a client draws on a player; a disguise shows only to the Spy's own team. Round 2's S4 adds what the
    /// mini-crit and wet rules read, each as a client sees it:
    /// <list type="bullet">
    /// <item>Jarate's and Mad Milk's drips, drawn on him whatever he wears (tf_player_shared.cpp:3591, :3827);</item>
    /// <item>wet as stock's <c>crit_vs_wet_players</c> reads it (tf_gamerules.cpp:5991-6012): Jarate, milk or gas on him, or
    /// any water at his feet; its last prong, out of the water under 5 s, reads <c>m_flWaterExitTime</c>, which is the
    /// server's alone (tf_player.h:1441, in no send table) and which the port's rule also leaves out (TFDamageModifyRules);</item>
    /// <item>the mark for death over his head (c_tf_player.cpp:5181-5198): stock draws it for the silent mark too, and for
    /// neither on a cloaked or disguised player; his own (<paramref name="own"/>) is the HUD's icon by his health, shown
    /// whatever he is in (tf_hud_playerstatus.cpp:999-1001);</item>
    /// <item>in the air from a blast, stock's <c>InAirDueToExplosion</c> (tf_player.h:599, the port's TFDamageLayer): off the
    /// ground and out of the water with the blast-jump condition on, or on a Thermal Thruster;</item>
    /// <item>scoped: a zoomed Sniper, slowed, with the dot his rifle puts on what he aims at (stock's <c>ZoomIn</c> makes
    /// it, tf_weapon_sniperrifle.cpp:534-541, and every client draws it).</item>
    /// </list>
    /// </summary>
    public static ActorConditions Conditions(in TFActorFacts f, bool disguiseShown, TFBody body, bool own = false)
        => (f.Burning ? ActorConditions.Burning : 0) | (f.Stunned ? ActorConditions.Stunned : 0) | (f.Ubered ? ActorConditions.Invulnerable : 0)
           | (f.Disguised && disguiseShown ? ActorConditions.Disguised : 0) | (f.Cloaked ? ActorConditions.Cloaked : 0)
           | (f.Taunting ? ActorConditions.Taunting : 0)
           | (f.Jarated ? ActorConditions.Jarated : 0) | (f.Milked ? ActorConditions.Milked : 0)
           | (f.Jarated || f.Milked || f.Gassed || body.WaterLevel > 0 ? ActorConditions.Wet : 0)
           | (f.MarkedForDeath && (own || !(f.Cloaked || f.Disguised)) ? ActorConditions.MarkedForDeath : 0)
           | (BlastAirborne(in f, body) ? ActorConditions.BlastAirborne : 0) | (f.Zoomed ? ActorConditions.Zoomed : 0);

    /// <summary>Stock's <c>InAirDueToExplosion</c> (tf_player.h:599): thrown by a blast, off the ground and out of the water; or on a thruster.</summary>
    public static bool BlastAirborne(in TFActorFacts f, TFBody body)
        => (!body.OnGround && body.WaterLevel == 0 && f.BlastJumping) || f.Rocketpack;

    public void Things(List<ThingRead> into)
    {
        into.Clear();
        Projectiles(into, TFProjectileEntityNames.Rocket, TFThingType.Rocket);
        Projectiles(into, TFProjectileEntityNames.Pipebomb, TFThingType.Pipe);
        Projectiles(into, TFProjectileEntityNames.Stickybomb, TFThingType.Sticky);
        Projectiles(into, TFProjectileEntityNames.Arrow, TFThingType.Arrow);
        Projectiles(into, TFProjectileEntityNames.Flare, TFThingType.Flare);
        Projectiles(into, TFProjectileEntityNames.HealingBolt, TFThingType.HealingBolt);
        Projectiles(into, TFProjectileEntityNames.Jar, TFThingType.Jar);
        Projectiles(into, TFProjectileEntityNames.JarMilk, TFThingType.Jar);
        Projectiles(into, TFProjectileEntityNames.Cleaver, TFThingType.Cleaver);
        Projectiles(into, TFProjectileEntityNames.Syringe, TFThingType.Syringe);
        Buildings(into, TFObjectClassNames.SentryGun);
        Buildings(into, TFObjectClassNames.Dispenser);
        Buildings(into, TFObjectClassNames.Teleporter);
    }

    /// <summary>
    /// A projectile's team and owner as the server knows them: the team its networked row carries (the deflector's after a
    /// reflect), and the player who owns it now (stock's owner entity or thrower, re-pointed on a deflect:
    /// <see cref="ITFOwnedProjectile"/>). An arrow and a syringe carry no team on their row here, so they are drawn in
    /// their owner's (a client colours an arrow's trail by its shooter's team; docs/divergences.md). Who owns it reaches
    /// only that owner's own view (<see cref="RenderThing"/>): a player knows his own projectiles, not whose another is.
    /// </summary>
    private (int Team, int Owner) TeamOf(ScriptedEntity e)
    {
        int wire = e switch
        {
            TFRocket r => r.State.TeamNumber,
            TFPipebomb p => p.State.TeamNumber,
            TFStickybomb s => s.State.TeamNumber,
            TFFlare f => f.State.TeamNumber,
            TFHealingBolt b => b.State.TeamNumber,
            TFJar j => j.State.TeamNumber,
            TFCleaver c => c.State.TeamNumber,
            _ => 0,
        };
        return TeamOf(wire, e as ITFOwnedProjectile, owner => actors.TryRead(owner, out TFActorFacts f) ? (int)f.Team : 0);
    }

    /// <summary>
    /// A projectile's drawn team and owner from its row's <paramref name="wireTeam"/> and <paramref name="owned"/>'s owner:
    /// the row's team, else (only then is it asked) <paramref name="teamOf"/> the owner; the owner when it is a player's
    /// slot, else 0 (an ownerless arrow's owner index is the empty handle's, past every player slot).
    /// </summary>
    public static (int Team, int Owner) TeamOf(int wireTeam, ITFOwnedProjectile? owned, Func<int, int> teamOf)
    {
        int index = owned?.OwnerIndex ?? 0;
        int owner = index is > 0 and < SourceSharp.Abi.EngineLimits.MaxPlayerSlots ? index : 0;
        int team = wireTeam != 0 ? wireTeam : owner > 0 ? teamOf(owner) : 0;
        return (team, owner);
    }

    private void Projectiles(List<ThingRead> into, string className, TFThingType type)
    {
        foreach (ScriptedEntity e in directory.OfClass(className))
        {
            (int team, int owner) = TeamOf(e);
            Vector o = entities.GetOrigin(e.Handle), v = entities.GetVelocity(e.Handle);
            into.Add(new ThingRead(e.Handle, new ThingFacts((int)e.Handle.Value, ThingKind.Projectile, (int)type, new AiVector(o.X, o.Y, o.Z),
                                                            new AiVector(v.X, v.Y, v.Z), team, owner), ProjectileReach));
        }
    }

    /// <summary>A building as it stands: a blueprint being placed is its builder's alone, and one carried is in nobody's sight.</summary>
    private void Buildings(List<ThingRead> into, string className)
    {
        foreach (TFBaseObject b in directory.OfClass(className).OfType<TFBaseObject>())
        {
            if (b.IsPlacing || b.IsCarried)
                continue;
            Vector o = entities.GetOrigin(b.Handle);
            int builder = b.Builder == EntityHandle.None ? 0 : b.Builder.EntryIndex;
            into.Add(new ThingRead(b.Handle, new ThingFacts((int)b.Handle.Value, ThingKind.Building, (int)BuildingType(b.Type, b.Mode),
                                                            new AiVector(o.X, o.Y, o.Z), default, b.Team, builder, b.Level, b.Health, TFKnowledge.BuildingFlags(b.HasSapper, b is TFTeleporter { State: TFTeleporterState.Ready }, b.IsBuilding)), BuildingReach));
        }
    }

    /// <summary>A building's type, a teleporter's end by its mode (0 the entrance, 1 the exit).</summary>
    public static TFThingType BuildingType(TFObjectType type, int mode) => type switch
    {
        TFObjectType.Sentrygun => TFThingType.Sentry,
        TFObjectType.Dispenser => TFThingType.Dispenser,
        TFObjectType.Teleporter => mode == 0 ? TFThingType.TeleporterEntrance : TFThingType.TeleporterExit,
        _ => TFThingType.None,
    };

    public bool RenderThing(EntityHandle viewer, ref ThingFacts facts)
    {
        if (facts.Kind == ThingKind.Projectile)
        {
            // A player knows which projectiles are his own; whose another's is, he does not (review H1, 2026-11-21).
            if (facts.OwnerSlot != viewer.EntryIndex)
                facts = facts with { OwnerSlot = 0 };
            return true;
        }

        if (facts.Kind != ThingKind.Building)
            return true;
        bool seesHealth = actors.TryRead(viewer.EntryIndex, out TFActorFacts own) && ShowsBuildingHealth(in own, facts.Team);
        if (!seesHealth)
            facts = facts with { Health = -1 };
        return true;
    }

    /// <summary>
    /// Whether a viewer's target ID shows a building's health (<c>tf_hud_target_id.cpp:483</c>,
    /// :865-878): to the building's own team and to a Spy, and to nobody else.
    /// </summary>
    public static bool ShowsBuildingHealth(in TFActorFacts viewer, int buildingTeam)
        => (int)viewer.Team == buildingTeam || viewer.Class == TFClass.Spy;

    public ObjectiveKnowledge? Objectives => objectives.Current;

    /// <summary>TF2's feed is its notices' (<see cref="TFKillFeedTap"/>): a feign is in it, a silent kill not in the victim's team's.</summary>
    public bool OwnKillFeed => true;
}
