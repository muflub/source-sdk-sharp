using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The aim meter in the game (plan_hl2mp_ai §12.3): feeds each bot's
/// <see cref="AimStats"/> from the bot's own command and the vision pass's
/// reads, once a tick after the commands ran, and from the taps' hits and shots.
/// What it reads of another player is the debugger's (<c>/ai/aim</c>), never a
/// bot's; no brain sees it.
/// </summary>
[SideService(Side.Server)]
public sealed class AimMeter(Hl2mpBots bots, VisionPass vision, Hl2mpAiSession ai, IEngineFrame frame, IEngineEntities entities, IEnginePlayers players,
                             WeaponCatalog weapons) : IFrameLifecycle
{
    private readonly Dictionary<ActorId, AimStats> _stats = new();
    private readonly Dictionary<int, int> _tough = new();
    private readonly Dictionary<int, float> _lastYaw = new();

    /// <summary>After the bots' commands (<c>ModStages.First + 500</c>).</summary>
    public int FrameOrder => ModStages.First + 510;

    /// <summary>The stats of <paramref name="bot"/>, made when first asked.</summary>
    public AimStats Of(ActorId bot)
    {
        if (!_stats.TryGetValue(bot, out AimStats? s))
            _stats[bot] = s = new AimStats(VisionPass.Slots);
        return s;
    }

    /// <summary>Starts every bot's count afresh.</summary>
    public void Reset()
    {
        foreach (AimStats s in _stats.Values)
            s.Reset();
    }

    /// <summary>
    /// Keeps the player in <paramref name="slot"/> at <paramref name="health"/>
    /// every tick so a measured target outlives its measurement; 0 stops. A hit
    /// still lands and is still told (<c>ai_aim_tough</c>).
    /// </summary>
    public void Tough(int slot, int health)
    {
        if (health > 0)
            _tough[slot] = health;
        else
            _tough.Remove(slot);
    }

    public void OnPreThink(float frameTime)
    {
        foreach ((int slot, int health) in _tough)
        {
            EntityHandle p = players.GetHandle(slot);
            if (p != EntityHandle.None && players.IsAlive(p) && !Spectators.Is(players, p))
                entities.SetHealth(p, health);
        }

        int tick = frame.TickCount();
        foreach (Hl2mpBot bot in bots.Bots)
        {
            if (vision.ReadOf(bot.Id.Slot) is not { } me || vision.SightOf(bot.Id.Slot) is not { } sight
                || bots.Applier.StateOf(bot.Id.Slot) is not { } state)
                continue;
            AimStats stats = Of(bot.Id);
            int chosen = -1;
            string leaf = "";
            if (ai.Session.LastDoing(bot.Id.Slot) is { } doing)
            {
                chosen = doing.Target.IsValid ? doing.Target.Slot : -1;
                leaf = doing.Leaf;
            }

            float yawNow = state.Motion.View.Yaw;
            stats.Command(tick, state.LastMove.Side, _lastYaw.TryGetValue(bot.Id.Slot, out float yawThen) ? HumanMotion.AngleDelta(yawThen, yawNow) : 0f, leaf);
            _lastYaw[bot.Id.Slot] = yawNow;
            AiVector forward = VisionMath.Forward(state.Motion.View);
            bool attack = (state.LastButtons & (int)InputButtons.Attack) != 0;
            bool close = false;
            for (int t = 1; t < VisionPass.Slots; t++)
            {
                if (t == bot.Id.Slot || vision.ReadOf(t) is not { } them)
                    continue;
                AiVector up = them.Eye - them.Origin;
                AiVector chest = them.Origin + up * 0.75f;
                AiVector to = chest - me.Eye;
                float cos = Math.Clamp(forward.Dot(to.Normalised()), -1f, 1f);
                float half = VisionPass.HalfWidth;
                bool onBody = AimStats.RayHitsBox(me.Eye, forward, them.Origin + new AiVector(-half, -half, 0f), them.Origin + new AiVector(half, half, up.Z + 8f));
                close |= sight.Sees(t) && to.Length <= AimStats.CloseRange;
                stats.Observe(t, tick, new AimSample(sight.StageOf(t) == SightStage.Clear, sight.Sees(t), t == chosen,
                                                     MathF.Acos(cos) * (180f / MathF.PI), onBody, attack, leaf, to.Length,
                                                     AimCone.Holds(AimCone.AngleToBody(state.Motion.View, me.Eye, them.Origin, them.Eye), to.Length)));
            }

            if (close)
                stats.Close(leaf, attack);
        }
    }

    /// <summary>A player's weapon event, from the fire tap: a bot's shot counts.</summary>
    public void Weapon(EntityHandle player, int weaponType, PlayerWeaponEvent e)
    {
        if (e != PlayerWeaponEvent.Fired || !BotOf(player, out ActorId bot))
            return;
        Of(bot).Shot(weaponType, frame.TickCount());
    }

    /// <summary>A hit on a player, from the damage tap: a bot's counts.</summary>
    public void Hurt(in PlayerHurt hurt)
    {
        if (hurt.Attacker == EntityHandle.None || !BotOf(hurt.Attacker, out ActorId bot))
            return;
        int victim = entities.GetIndex(hurt.Player);
        if (victim is >= 1 and < VisionPass.Slots)
            Of(bot).Hit(victim, hurt.Taken, frame.TickCount());
    }

    private bool BotOf(EntityHandle player, out ActorId bot)
    {
        int slot = entities.GetIndex(player);
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (b.Id.Slot == slot)
            {
                bot = b.Id;
                return true;
            }
        }

        bot = ActorId.None;
        return false;
    }

    /// <summary><c>/ai/aim</c>: every bot's account of each player it saw, shot at or hit.</summary>
    public string Json()
    {
        IEnumerable<object> list = bots.Bots.Select(b =>
        {
            AimStats s = Of(b.Id);
            var rows = new List<object>();
            for (int t = 0; t < s.Slots; t++)
            {
                AimStats.Row r = s.Of(t);
                if (r.Engagements == 0 && r.Shots == 0 && r.Hits == 0)
                    continue;
                rows.Add(new
                {
                    slot = t,
                    human = vision.ActorIn(t).IsValid && bots.Bots.All(o => o.Id.Slot != t),
                    engagements = r.Engagements,
                    clear_ticks = r.ClearTicks,
                    in_sight_ticks = r.InSightTicks,
                    firing_ticks = r.FiringTicks,
                    on_body_ticks = r.OnBodyTicks,
                    error_mean = r.ErrorMean,
                    error_spread = r.ErrorSpread,
                    shots = r.Shots,
                    hits = r.Hits,
                    damage = r.Damage,
                    first_shot_ticks = r.FirstShotTicks,
                    acquire_ticks = r.AcquireTicks,
                    leaves = r.Leaves,
                    shots_by_order = r.ShotsByOrder,
                    hits_by_order = r.HitsByOrder,
                    by_weapon = r.ByWeapon.Select(kv => new
                    {
                        weapon = weapons.ByIndex(kv.Key.Weapon)?.Name ?? "",
                        band = AimBands.Name(kv.Key.Band),
                        shots = kv.Value.Shots,
                        hits = kv.Value.Hits,
                    }),
                });
            }

            return (object)new { slot = b.Id.Slot, name = b.Name, profile = b.Skills.Name, view_resets = bots.Applier.StateOf(b.Id.Slot)?.ViewResets ?? 0,
                                    commands = s.Commands, yaw_reversals = s.YawReversals, side_interval_ticks = s.SideIntervalTicks, side_flips_by_leaf = s.SideFlipsByLeaf,
                                    close_by_leaf = s.CloseByLeaf.Select(kv => new { leaf = kv.Key, ticks = kv.Value.Ticks, firing = kv.Value.Firing }), targets = rows };
        });
        return AiJson.Write(new { tick = frame.TickCount(), bots = list });
    }
}
