using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The fire and reload tap (plan_hl2mp_ai §4.5): a bot sees another player's
/// shot or reload only while it holds sight of that player at that tick, as
/// a client sees the muzzle flash and the reload gesture of a player on its
/// screen; anything unseen is not an event (hearing is the other way in). The
/// tap also keeps what each player's animation shows, for the views.
/// </summary>
[SideService(Side.Server, As = [typeof(IPlayerWeaponEventHook)])]
public sealed class WeaponEventTap(Hl2mpSenses senses, VisionPass vision, Hl2mpBots bots, IEngineEntities entities, IEngineFrame frame, ShotTap shots, AimMeter aim)
    : IPlayerWeaponEventHook
{
    private readonly List<AiEvent> _events = new();
    private readonly List<(ActorId Bot, SightTracker Sight)> _watchers = new();

    public void OnWeaponEvent(EntityHandle player, int weaponType, PlayerWeaponEvent e, int count)
    {
        shots.OnWeaponEvent(player, weaponType, e, count);
        int slot = entities.GetIndex(player);
        if (slot is < 1 or >= VisionPass.Slots)
            return;

        aim.Weapon(player, weaponType, e);
        ActorActivity a = senses.ActivityOf(slot);
        senses.SetActivity(slot, e switch
        {
            PlayerWeaponEvent.ReloadStarted => a | ActorActivity.Reloading,
            PlayerWeaponEvent.ReloadFinished => a & ~ActorActivity.Reloading,
            PlayerWeaponEvent.Fired or PlayerWeaponEvent.AltFired => a | ActorActivity.Firing,
            _ => a,
        });

        _watchers.Clear();
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (vision.SightOf(b.Id.Slot) is { } sight)
                _watchers.Add((b.Id, sight));
        }

        _events.Clear();
        Perceive(slot, weaponType, e, count, _watchers, new Tick(frame.TickCount()), _events);
        foreach (AiEvent ev in _events)
            senses.Push(ev);
    }

    /// <summary>
    /// The events <paramref name="e"/> by the player in <paramref name="slot"/>
    /// gives each watcher that sees that player now; none for a watcher that
    /// does not, nor for the player itself.
    /// </summary>
    public static void Perceive(int slot, int weaponType, PlayerWeaponEvent e, int count, IReadOnlyList<(ActorId Bot, SightTracker Sight)> watchers,
                                Tick tick, List<AiEvent> into)
    {
        // What a watcher learns of a clip comes from Fired: a grenade or a ball takes nothing from it.
        if (e == PlayerWeaponEvent.AltFired)
            return;
        foreach ((ActorId bot, SightTracker sight) in watchers)
        {
            if (bot.Slot == slot || !sight.Sees(slot))
                continue;
            ActorId actor = sight.TargetOf(slot);
            into.Add(e switch
            {
                PlayerWeaponEvent.Fired => AiEvent.SeenFire(bot, tick, actor, weaponType, count),
                PlayerWeaponEvent.ReloadStarted => AiEvent.SeenReload(bot, tick, actor, ReloadPhase.Start, weaponType),
                PlayerWeaponEvent.ReloadShell => AiEvent.SeenReload(bot, tick, actor, ReloadPhase.Loop, weaponType, count),
                _ => AiEvent.SeenReload(bot, tick, actor, ReloadPhase.End, weaponType, count),
            });
        }
    }
}
