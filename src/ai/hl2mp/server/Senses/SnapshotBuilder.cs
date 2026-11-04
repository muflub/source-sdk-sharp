using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>A bot's own HUD (plan_hl2mp_ai §4.3): what a client knows of itself.</summary>
public interface IBotHud
{
    SelfState Read(EntityHandle bot);
}

/// <summary>
/// <see cref="IBotHud"/> over the engine and the arsenal, for the bot's own
/// player only: its health, armour, the weapon in its hands, that weapon's
/// clip and the reserve of its ammo, as its HUD shows them.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotHud)])]
public sealed class EngineBotHud(IEnginePlayers players, IEngineEntities entities, Arsenal arsenal, WeaponCatalog weapons) : IBotHud
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

        int grenades = weapons.For(EntityNames.WeaponFrag) is { } frag && arsenal.PoolOf(bot, frag.AmmoName, out int carried) ? carried : 0;
        SelfFlags flags = (alive ? SelfFlags.Alive : SelfFlags.None) | (observer ? SelfFlags.Observer : SelfFlags.None);
        return new SelfState(new AiVector(o.X, o.Y, o.Z), new AiVector(v.X, v.Y, v.Z), new AiAngles(a.X, a.Y, a.Z),
                             entities.GetHealth(bot), entities.GetArmor(bot), kind, clip, reserve, flags, alt, grenades);
    }
}

/// <summary>
/// Builds each bot's <see cref="PerceivedView"/> for a tick (plan_hl2mp_ai
/// §4.3): its own HUD, then exactly the actors its vision holds and sees this
/// tick, as a client draws them: where, how fast, the weapon model in their
/// hands and what their animation shows. Never another player's health, clip
/// or reserve (the IL census over its callees holds that); one of the two
/// types that read another actor (§4.4).
/// </summary>
[SideService(Side.Server)]
public sealed class SnapshotBuilder(IEnginePlayers players, WeaponCatalog weapons, IBotHud hud, IPlayerStances stances)
{
    /// <summary>
    /// Adds a view for every bot in <paramref name="bots"/> to
    /// <paramref name="snapshot"/>; <paramref name="activityOf"/> answers what
    /// a client sees an actor's animation doing (the reload gesture, §4.5).
    /// </summary>
    public void Build(AiSnapshot snapshot, ReadOnlySpan<SensedBot> bots, VisionPass vision, Func<int, ActorActivity> activityOf)
    {
        foreach (SensedBot b in bots)
        {
            SelfState self = hud.Read(b.Player);
            // The bot's own reload, as its own client shows it (its view model
            // and HUD): the weapon tap's reload state of its own slot.
            if ((activityOf(b.Id.Slot) & ActorActivity.Reloading) != 0)
                self = self with { Flags = self.Flags | SelfFlags.Reloading };
            PerceivedView view = snapshot.Add(b.Id, self);
            if (vision.SightOf(b.Id.Slot) is not { } sight)
                continue;
            for (int t = 1; t < VisionPass.Slots; t++)
            {
                if (t == b.Id.Slot || !sight.Sees(t) || vision.ReadOf(t) is not { } them)
                    continue;
                int weapon = weapons.IndexOf(players.GetActiveWeapon(them.Player));
                ActorActivity activity = activityOf(t) | (stances.Ducked(them.Player) ? ActorActivity.Crouched : ActorActivity.None);
                view.AddSighted(new ActorFacts(sight.TargetOf(t), them.Origin, them.Velocity, weapon, activity, sight.FractionOf(t)));
            }
        }
    }
}
