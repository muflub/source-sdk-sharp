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
                                  ITFRoundStateView rounds, TFPlayerMovementRestartFreezeConVar restartFreeze, TFHudReader hud) : IBotGameView
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
        return read with { Class = (int)own.Class, Conditions = Conditions(in own, disguiseShown: true), Metal = metal,
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
        return Draw(viewerTeam, in drawn, dress, ref facts);
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
        if (!Fools(viewerTeam, in drawn))
            return real;
        ulong key = DisguiseKey(in drawn, real);
        ref (ulong Key, ActorId Alias) row = ref _aliases[index];
        if (row.Key != key || !row.Alias.IsValid)
            row = (key, ActorId.Alias(AliasSlot(in drawn, index), ++_aliasesMade));
        return row.Alias;
    }

    /// <summary>Whether a client of <paramref name="viewerTeam"/> takes <paramref name="them"/> for someone else: a disguise, to anyone not on the Spy's team.</summary>
    public static bool Fools(int viewerTeam, in TFActorFacts them) => them.Disguised && !(viewerTeam != 0 && viewerTeam == (int)them.Team);

    /// <summary>The slot an alias is drawn in: the borrowed name's, whose scoreboard row the disguise claims; the Spy's own when it borrowed none.</summary>
    public static int AliasSlot(in TFActorFacts them, int own) => them.DisguiseTargetIndex > 0 ? them.DisguiseTargetIndex : own;

    /// <summary>One disguise of one player: its class, team and borrowed name, and the player's life (his actor's serial).</summary>
    public static ulong DisguiseKey(in TFActorFacts them, ActorId real)
        => ((ulong)(uint)real.Serial << 32) | ((ulong)(byte)them.DisguiseClass << 24) | ((ulong)(byte)them.DisguiseTeam << 16)
           | (ushort)them.DisguiseTargetIndex;

    /// <summary>
    /// What a client of <paramref name="viewerTeam"/> draws of a player whose mod facts are
    /// <paramref name="them"/>, over the engine's <paramref name="facts"/>; false when it draws
    /// nothing. <paramref name="dressWeapon"/> is the catalog kind of the weapon his disguise
    /// shows in his hands, -1 for none.
    /// </summary>
    public static bool Draw(int viewerTeam, in TFActorFacts them, int dressWeapon, ref ActorFacts facts)
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
            Conditions = Conditions(in them, disguiseShown: sameTeam),
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
    /// borrowed player's, unless he has none or is cloaked, when stock's target ID keeps the Spy's own name
    /// (<paramref name="own"/>, the slot the engine read gave him).
    /// </summary>
    public static int ShownName(in TFActorFacts them, int own)
        => them.Disguised && !them.Cloaked && them.DisguiseTargetIndex > 0 ? them.DisguiseTargetIndex : own;

    /// <summary>The conditions a client draws on a player; a disguise shows only to the Spy's own team.</summary>
    public static ActorConditions Conditions(in TFActorFacts f, bool disguiseShown)
        => (f.Burning ? ActorConditions.Burning : 0) | (f.Stunned ? ActorConditions.Stunned : 0) | (f.Ubered ? ActorConditions.Invulnerable : 0)
           | (f.Disguised && disguiseShown ? ActorConditions.Disguised : 0) | (f.Cloaked ? ActorConditions.Cloaked : 0)
           | (f.Taunting ? ActorConditions.Taunting : 0);

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
    /// A projectile's team and owner as its networked row carries them (every client's trail reads
    /// them); an arrow and a syringe carry no row here, so their team reads 0 (docs/divergences.md).
    /// </summary>
    private static (int Team, int Owner) TeamOf(ScriptedEntity e) => e switch
    {
        TFRocket r => (r.State.TeamNumber, r.State.OwnerIndex),
        TFPipebomb p => (p.State.TeamNumber, 0),
        TFStickybomb s => (s.State.TeamNumber, 0),
        TFFlare f => (f.State.TeamNumber, 0),
        TFHealingBolt b => (b.State.TeamNumber, 0),
        TFJar j => (j.State.TeamNumber, 0),
        TFCleaver c => (c.State.TeamNumber, 0),
        _ => (0, 0),
    };

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
                                                            new AiVector(o.X, o.Y, o.Z), default, b.Team, builder, b.Level, b.Health), BuildingReach));
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
