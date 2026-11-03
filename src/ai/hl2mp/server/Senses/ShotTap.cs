using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// Each player's shots and the hits they cause into the recording, humans'
/// and bots' alike (plan_hl2mp_ai §3.4.4; user ruling 2026-11-02: "After my
/// recording set med bot to match my hit rate"): a shot is one <c>Fired</c>
/// event (a trigger pull, however many pellets); a hit names its victim (the
/// recorder anonymises a person), its damage, the weapon the attacker last
/// fired and the range at that moment, eye to body, from the vision pass's
/// last reads. Harm a player does itself is no hit, nor is a hit with no
/// player behind it. As Observed records: no brain is told. One check an
/// event while nothing records. <see cref="WeaponEventTap"/> and
/// <see cref="CombatTap"/>, which hold the hooks, hand it the events.
/// </summary>
[SideService(Side.Server)]
public sealed class ShotTap(Hl2mpAiSession ai, Hl2mpBots bots, VisionPass vision, IEngineEntities entities, IEngineFrame frame)
{
    private readonly int[] _lastWeapon = NoWeapons();

    private static int[] NoWeapons()
    {
        int[] w = new int[VisionPass.Slots];
        Array.Fill(w, -1);
        return w;
    }

    public void OnWeaponEvent(EntityHandle player, int weaponType, PlayerWeaponEvent e, int count)
    {
        if (e != PlayerWeaponEvent.Fired || !ai.Recording.Recording)
            return;
        int slot = entities.GetIndex(player);
        if (slot is < 1 or >= VisionPass.Slots)
            return;
        _lastWeapon[slot] = weaponType;
        ai.Recording.Observed(AiEvent.ShotFired(CommandTap.ActorOf(bots, slot, player), new Tick(frame.TickCount()), weaponType, count));
    }

    public void OnPlayerHurt(in PlayerHurt hurt)
    {
        if (!ai.Recording.Recording || hurt.Attacker == EntityHandle.None || hurt.Attacker == hurt.Player)
            return;
        int from = entities.GetIndex(hurt.Attacker), to = entities.GetIndex(hurt.Player);
        if (from is < 1 or >= VisionPass.Slots || to is < 1 or >= VisionPass.Slots)
            return;
        int range = vision.ReadOf(from) is { } a && vision.ReadOf(to) is { } v ? (int)MathF.Round(a.Eye.DistanceTo(VisionPass.Centre(v))) : -1;
        ai.Recording.Observed(AiEvent.HitDealt(CommandTap.ActorOf(bots, from, hurt.Attacker), new Tick(frame.TickCount()),
                                               CommandTap.ActorOf(bots, to, hurt.Player), hurt.Taken, _lastWeapon[from], range));
    }
}
