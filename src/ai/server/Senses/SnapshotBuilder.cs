using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.Server.Senses;

/// <summary>A bot's own HUD (plan_hl2mp_ai §4.3): what a client knows of itself.</summary>
public interface IBotHud
{
    SelfState Read(EntityHandle bot);
}

/// <summary>
/// <see cref="IBotHud"/> over the engine and the arsenal, for the bot's own
/// player only: its health, armour, the weapon in its hands, that weapon's
/// clip and the reserve of its ammo, as its HUD shows them; then the game's
/// own rows (<see cref="IBotGameView.Hud"/>: a class, conditions, metal). The
/// weapon catalog and the arsenal are the core's, every game's weapons' rows
/// (TF2's register there too); what is the game's is asked of the game.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotHud)])]
public sealed class EngineBotHud(IEnginePlayers players, IEngineEntities entities, Arsenal arsenal, WeaponCatalog weapons, IBotGame game,
                                 IPlayerStances stances) : IBotHud
{
    public SelfState Read(EntityHandle bot)
    {
        Vector o = entities.GetOrigin(bot), v = entities.GetVelocity(bot), a = entities.GetEyeAngles(bot);
        bool alive = players.IsAlive(bot);
        bool observer = players.GetObserverMode(bot) != ObserverMode.None;
        int kind = -1, clip = 0, reserve = 0, alt = 0;
        foreach ((string className, int c, bool active) in arsenal.Holdings(bot))
        {
            if (!active)
                continue;
            kind = weapons.IndexOf(className);
            clip = c;
            if (weapons.For(className) is not { } type)
                continue;
            if (type.AmmoName.Length > 0 && arsenal.PoolOf(bot, type.AmmoName, out int pool))
                reserve = pool;
            if (type.SecondaryAmmoName.Length > 0 && arsenal.PoolOf(bot, type.SecondaryAmmoName, out int altPool))
                alt = altPool;
        }

        int grenades = weapons.For(game.GrenadeWeapon) is { } frag && arsenal.PoolOf(bot, frag.AmmoName, out int carried) ? carried : 0;
        SelfFlags flags = (alive ? SelfFlags.Alive : SelfFlags.None) | (observer ? SelfFlags.Observer : SelfFlags.None)
                          | BotBodyFlags.Of(stances, bot);
        var read = new SelfState(new AiVector(o.X, o.Y, o.Z), new AiVector(v.X, v.Y, v.Z), new AiAngles(a.X, a.Y, a.Z),
                                 entities.GetHealth(bot), entities.GetArmor(bot), kind, clip, reserve, flags, alt, grenades);
        return game.View.Hud(bot, in read);
    }
}

/// <summary>
/// What a bot's own body tells it, as its client predicts its own movement (plan_bots_teams_tf2 P6
/// step 6): whether it stands on the ground and whether it is ducked. Live, no bot's view said it
/// was grounded, so a TF2 Soldier never rocket-jumped.
/// </summary>
public static class BotBodyFlags
{
    public static SelfFlags Of(IPlayerStances stances, EntityHandle bot)
        => (stances.OnGround(bot) ? SelfFlags.Grounded : SelfFlags.None) | (stances.Ducked(bot) ? SelfFlags.Ducking : SelfFlags.None);
}

/// <summary>
/// Builds each bot's <see cref="PerceivedView"/> for a tick (plan_hl2mp_ai
/// §4.3): its own HUD, then exactly the actors its vision holds and sees this
/// tick, as a client draws them: where, how fast, the weapon model in their
/// hands and what their animation shows. Never another player's health, clip
/// or reserve (the IL census over its callees holds that); one of the two
/// types that read another actor (§4.4). Each view carries the team a client
/// renders the bot and each actor on (plan_bots_teams_tf2 B.2), then what the
/// game's client draws over that (<see cref="IBotGameView.Render"/>: a disguise,
/// a cloak; an actor it draws nothing of is not in the view), the things that
/// are not players the bot sees (<see cref="ThingSight"/>) and the objectives the
/// HUD shows (plan_bots_teams_tf2 C.3, C.6).
/// </summary>
[SideService(Side.Server)]
public sealed class SnapshotBuilder(IEnginePlayers players, WeaponCatalog weapons, IBotHud hud, IPlayerStances stances, IRenderedTeams teams,
                                    IBotGame game, ThingSight things, ShownActors? shown = null)
{
    /// <summary>Who each bot takes each sighted player for (a disguise's alias): the events and the views name the same actor.</summary>
    public ShownActors Shown { get; } = shown ?? new ShownActors(game);

    /// <summary>
    /// Adds a view for every bot in <paramref name="bots"/> to
    /// <paramref name="snapshot"/>; <paramref name="activityOf"/> answers what
    /// a client sees an actor's animation doing (the reload gesture, §4.5).
    /// </summary>
    /// <summary>The team a client renders <paramref name="player"/> on (plan_bots_teams_tf2 B.2): for the walk's give-way list.</summary>
    public int RenderedTeam(EntityHandle player) => teams.TeamOf(player);

    public void Build(AiSnapshot snapshot, ReadOnlySpan<SensedBot> bots, VisionPass vision, Func<int, ActorActivity> activityOf)
    {
        if (!bots.IsEmpty)
            things.Begin(snapshot.Tick);
        ObjectiveKnowledge? objectives = game.View.Objectives;
        foreach (SensedBot b in bots)
        {
            SelfState self = hud.Read(b.Player) with { Team = teams.TeamOf(b.Player) };
            // The bot's own reload, as its own client shows it (its view model
            // and HUD): the weapon tap's reload state of its own slot.
            if ((activityOf(b.Id.Slot) & ActorActivity.Reloading) != 0)
                self = self with { Flags = self.Flags | SelfFlags.Reloading };
            PerceivedView view = snapshot.Add(b.Id, self);
            view.SetObjectives(objectives);
            view.SetGameHud(game.View.HudOf(b.Player));
            if (vision.ReadOf(b.Id.Slot) is { } me)
                things.AddSeen(in b, me.Eye, view);
            if (vision.SightOf(b.Id.Slot) is not { } sight)
                continue;
            for (int t = 1; t < VisionPass.Slots; t++)
            {
                if (t == b.Id.Slot || !sight.Sees(t) || vision.ReadOf(t) is not { } them)
                    continue;
                int weapon = weapons.IndexOf(players.GetActiveWeapon(them.Player));
                ActorActivity activity = activityOf(t) | (stances.Ducked(them.Player) ? ActorActivity.Crouched : ActorActivity.None);
                // Every actor's target ID shows a name, its own unless the game draws a borrowed one (a disguise): the field
                // means the same for everyone, so its value never sets anyone apart (plan_bots_teams_tf2 C.3, the Spy's fact 2).
                ActorId told = Shown.HeldFor(b.Id.Slot, t);
                var facts = new ActorFacts(told.IsValid ? told : game.View.ShownAs(b.Player, them.Player, sight.TargetOf(t)), them.Origin, them.Velocity, weapon, activity, sight.FractionOf(t), teams.TeamOf(them.Player),
                                           NameSlot: t, Facing: them.View);
                if (game.View.Render(b.Player, them.Player, ref facts))
                    view.AddSighted(in facts);
            }
        }
    }
}
