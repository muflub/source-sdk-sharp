using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Skills;

/// <summary>What a key's press or release came to.</summary>
public enum SkillUseKind
{
    /// <summary>Nothing: an empty slot, a release with nothing held, no hero.</summary>
    Nothing,
    /// <summary>The cast went off (its effects fired, its mana spent).</summary>
    Cast,
    /// <summary>A toggle switched off.</summary>
    ToggledOff,
    /// <summary>A charge began; the release fires it.</summary>
    Charging,
    /// <summary>A charge was let go before its first stage: nothing fired, nothing spent.</summary>
    Early,
    /// <summary>A charge held past its misfire: spent, nothing fired.</summary>
    Misfire,
    /// <summary>The rules refused the cast (<see cref="SkillUse.Refusal"/>).</summary>
    Refused,
    /// <summary>The hero is down (WO6): he casts nothing until he is up.</summary>
    Downed,
}

/// <summary>One press or release: what happened, which skill, the refusal, the stages released at, the mana spent.</summary>
public readonly record struct SkillUse(SkillUseKind Kind, string? SkillId = null, CastRefusal? Refusal = null, int Stages = 0,
                                       double ManaSpent = 0);

/// <summary>
/// THE SKILLS RUNTIME (plan_descent_logic HE6): the rules' casting carried out
/// in the game, per hero.
///
/// <list type="bullet">
/// <item>A key's PRESS (of a hero alive and playing) casts its slot's skill through <see cref="CastRules.Cast"/>
/// with the hero's mana and the caster's state, spends the mana through the
/// hero component, starts the cooldown in server ticks and fires each effect
/// through its binder. A charge skill's press only begins the charge; its
/// RELEASE casts at the stage reached (none reached: nothing, nothing spent),
/// and a charge held past its <c>misfireAfter</c> misfires.</item>
/// <item>A refusal is told to the hero once a second for each skill and
/// reason, and logged every time.</item>
/// <item>Each TICK: toggles drain (<see cref="CastRules.Drain"/>, off at zero),
/// timed buffs end, scheduled parts (a shell's delay, a volley) go off, the
/// weapon family and the BlastJumping state are read (a landing fires
/// <c>blast_jump_landing</c>), the held triggers' stats are counted again,
/// the TF attributes follow the stats, and the owner's skill bar is sent
/// when it changed.</item>
/// </list>
///
/// Gear's bonus ranks are not wired (<see cref="SkillBonuses.None"/>): the
/// equipment package hands them over when it has them.
/// </summary>
[SideService(Side.Server)]
public sealed class SkillRuntime(TF2DescentRules rules, Heroes heroes, SkillCasters casters, SkillSlotStore slots,
                                 SkillBuffs buffs, SkillBinders binders, SkillAttributes attributes, ISkillChannel channel,
                                 ILog log)
{
    /// <summary>A refusal is told again after this long.</summary>
    public const double NoticeSeconds = 1.0;

    private ISkillWorld World => binders.World;

    private int Now => World.Tick;

    private double Seconds(int tick) => tick * (double)World.TickInterval;

    //-------------------------------------------------------------------------
    // The hero's life
    //-------------------------------------------------------------------------

    public void Attach(int playerIndex, EntityHandle player)
    {
        var caster = casters.Enter(playerIndex, player);
        caster.LastHurtTick = Now;
    }

    public void Detach(int playerIndex)
    {
        if (casters.Of(playerIndex) is { } caster)
            casters.Leave(playerIndex, caster);
        buffs.Clear(playerIndex);
        attributes.Forget(playerIndex);
    }

    //-------------------------------------------------------------------------
    // Keys
    //-------------------------------------------------------------------------

    /// <summary>The key of <paramref name="slot"/> went down.</summary>
    public SkillUse Press(int playerIndex, int slot)
    {
        if (Ready(playerIndex) is not ({ } caster, { } hero, { } sheet, { } stats))
            return default;
        if (slots.In(playerIndex, slot) is not { } id || !World.IsAlive(caster.Player))
            return default;
        if (hero.Downed)
        {
            log.Msg(Line(playerIndex, $"cast {id} refused: the hero is down"));
            return new SkillUse(SkillUseKind.Downed, id);
        }
        if (rules.Skills.Find(id) is not { } def)
            return Refuse(caster, id, CastRefusal.UnknownSkill);

        Sync(caster, hero);
        bool toggleOn = def.Kind == SkillKind.Toggle && caster.State.Toggles.Contains(id);
        if (!toggleOn && def.Effect.Any(e => e.Primitive == Primitive.Charge))
        {
            var probe = rules.Casting.Cast(sheet, stats, SkillBonuses.None, caster.State, id, stages: 0);
            if (!probe.Ok)
                return Refuse(caster, id, probe.Refusal!.Value);
            caster.Charging = new SkillCharge(slot, id, Now);
            log.Msg(Line(playerIndex, $"{id} charging from tick {Now}"));
            SendBar(caster);
            return new SkillUse(SkillUseKind.Charging, id);
        }

        return Cast(caster, hero, def, stages: null, misfire: false);
    }

    /// <summary>The key of <paramref name="slot"/> came up: a charge held there is released.</summary>
    public SkillUse Release(int playerIndex, int slot)
    {
        if (Ready(playerIndex) is not ({ } caster, { } hero, { } sheet, { } stats))
            return default;
        if (caster.Charging is not { } charge || charge.Slot != slot)
            return default;

        caster.Charging = null;
        if (hero.Downed)
        {
            log.Msg(Line(playerIndex, $"{charge.SkillId} released while down: nothing fired"));
            SendBar(caster);
            return new SkillUse(SkillUseKind.Downed, charge.SkillId);
        }
        var def = rules.Skills.Skill(charge.SkillId);
        var resolved = rules.Skills.Resolve(sheet, stats, SkillBonuses.None, def.Id);
        int reached = StagesReached(resolved, Now - charge.StartTick, binders.Ticks);
        if (reached == 0)
        {
            log.Msg(Line(playerIndex, $"{def.Id} released at tick {Now} before its first stage: nothing fired"));
            SendBar(caster);
            return new SkillUse(SkillUseKind.Early, def.Id);
        }

        Sync(caster, hero);
        return Cast(caster, hero, def, reached, misfire: false);
    }

    /// <summary>
    /// The stages a charge held <paramref name="heldTicks"/> has readied: stage
    /// k of n once the key has been down for the ticks of <c>k x time / n</c>
    /// seconds (<paramref name="ticks"/> rounds them as cooldowns are), at
    /// most <c>stages</c>. Counted in ticks, so a hold of exactly the charge's
    /// time readies its last stage whatever the tick interval's rounding.
    /// </summary>
    public static int StagesReached(ResolvedSkill skill, int heldTicks, Func<double, int> ticks)
    {
        if (skill.Effects.FirstOrDefault(e => e.Primitive == Primitive.Charge) is not { } charge)
            return 0;
        int stages = (int)charge["stages"];
        double time = charge["time"];
        int reached = 0;
        while (reached < stages && heldTicks >= ticks(time * (reached + 1) / stages))
            reached++;
        return reached;
    }

    //-------------------------------------------------------------------------
    // Buying and slots
    //-------------------------------------------------------------------------

    /// <summary>
    /// One rank of <paramref name="skillId"/> bought with a point, through
    /// <see cref="SkillRules.Buy"/> and the hero component's audited
    /// <see cref="HeroComponent.Apply"/>; a refusal is told to the hero.
    /// Null when the slot holds no hero with a sheet.
    /// </summary>
    public BuyResult? Buy(int playerIndex, string skillId)
    {
        if (heroes.Of(playerIndex) is not { Sheet: { } sheet } hero)
            return null;

        var result = rules.Skills.Buy(sheet, skillId);
        if (!result.Ok)
        {
            channel.Tell(playerIndex, $"Cannot learn {NameOf(skillId)}: {result.Detail}");
            log.Msg(Line(playerIndex, $"buy {skillId} refused: {result.Refusal} ({result.Detail})"));
            return result;
        }

        var audit = hero.Apply(s => rules.Skills.Buy(s, skillId).Sheet);
        int rank = SkillRules.RealRank(hero.Sheet!, skillId);
        log.Msg(Line(playerIndex, $"bought {skillId} rank {rank}, skill points {hero.Sheet!.SkillPoints}, " +
                                  $"audit {(audit.Ok ? "ok" : audit.Rule)}"));
        return audit.Ok ? result : new BuyResult(sheet, null, audit.Rule);
    }

    /// <summary>
    /// Puts <paramref name="skillId"/> in <paramref name="slot"/> (<c>none</c>
    /// empties it): a cast skill the hero has a real rank in. The refusal's
    /// words, or null when assigned.
    /// </summary>
    public string? Assign(int playerIndex, int slot, string skillId)
    {
        if (slot is < 1 or > SkillBar.Slots)
            return $"slot {slot} is not 1 to {SkillBar.Slots}";
        if (heroes.Of(playerIndex) is not { Sheet: { } sheet })
            return "no hero";

        AuditResult set;
        if (skillId is "none" or "")
        {
            set = slots.Set(playerIndex, slot, null);
        }
        else
        {
            if (rules.Skills.Find(skillId) is not { } def)
                return $"'{skillId}' is not a skill";
            if (!def.IsCast)
                return $"{def.Name} is an upgrade: nothing to cast";
            if (SkillRules.RealRank(sheet, skillId) <= 0)
                return $"{def.Name} is not learned";
            set = slots.Set(playerIndex, slot, skillId);
        }
        if (!set.Ok)
            return $"the sheet refused it: {set.Rule}";

        log.Msg(Line(playerIndex, $"slot {slot} holds {slots.In(playerIndex, slot) ?? "nothing"}"));
        if (casters.Of(playerIndex) is { } caster)
            SendBar(caster);
        return null;
    }

    //-------------------------------------------------------------------------
    // Triggers
    //-------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="trigger"/> happened to the hero (<see cref="Primitives.Triggers"/>):
    /// each held skill's effects on it fire. A <c>triggered</c> effect grants
    /// its stats for its duration, heals, refunds mana and puts its conditions
    /// on the hero (target self) or the one hit (<paramref name="victim"/>);
    /// any other primitive on a trigger goes off through its binder at the
    /// hero. A family-scoped effect fires only with that family in the hands.
    /// </summary>
    public void Trigger(int playerIndex, string trigger, EntityHandle victim = default)
    {
        if (Ready(playerIndex) is not ({ } caster, { } hero, { } sheet, { } stats))
            return;

        foreach (var (id, real) in sheet.SkillRanks)
        {
            if (real <= 0 || rules.Skills.Find(id) is not { } def || !rules.Skills.OpenTo(sheet.ClassId, def)
                || !def.Effect.Any(e => e.Trigger == trigger))
                continue;

            var resolved = rules.Skills.Resolve(sheet, stats, SkillBonuses.None, id);
            var cast = new SkillCast(playerIndex, hero.Player, def, resolved, 0, Now, []);
            for (int i = 0; i < resolved.Effects.Count; i++)
            {
                var effect = resolved.Effects[i];
                if (effect.Trigger != trigger || (effect.Family is { } family && caster.Family != family))
                    continue;
                if (!UnderCap(caster, $"{id}#{i}", effect))
                    continue;

                if (effect.Primitive == Primitive.Triggered)
                    Triggered(caster, hero, cast, effect, i, victim);
                else
                    binders.Of(effect.Primitive)?.Fire(caster, cast, effect);
                log.Msg(Line(playerIndex, $"{id} triggered on {trigger}"));
            }
        }
    }

    private void Triggered(SkillCaster caster, HeroComponent hero, SkillCast cast, ResolvedEffect effect, int index,
                           EntityHandle victim)
    {
        double duration = effect["duration"];
        if (effect.Stats.Count > 0 && duration > 0)
        {
            buffs.Add(caster.PlayerIndex, new HeroBuff($"{cast.SkillId}#{index}", caster.PlayerIndex,
                                                      SkillRules.AsBuff(cast.SkillId, effect), Now + binders.Ticks(duration)));
            hero.Recompute();
        }

        double heal = effect["heal"] + effect["pctMaxLife"] / 100 * World.MaxHealth(hero.Player);
        if (heal > 0)
            World.Heal(hero.Player, (float)heal);

        if (effect["manaRefund"] > 0)
        {
            double before = hero.Mana;
            hero.SetMana(hero.Mana + effect["manaRefund"]);
            log.Msg(Line(caster.PlayerIndex, string.Create(CultureInfo.InvariantCulture,
                $"{cast.SkillId} refunds mana {effect["manaRefund"]:0.##}: {before:0.##} -> {hero.Mana:0.##}")));
        }

        if (effect.Target == "self")
        {
            foreach (string condition in effect.Conditions)
                if (SkillConditions.OnPlayer(condition) is { } cond)
                    World.AddCondition(hero.Player, cond, (float)duration, hero.Player);
        }
        else if (victim != EntityHandle.None && effect.Conditions.Count > 0)
        {
            binders.Afflict(cast, effect, victim);
        }
    }

    /// <summary>A triggered effect's <c>perSecondCap</c>: at most that many firings in any second; true when this one may fire.</summary>
    private bool UnderCap(SkillCaster caster, string key, ResolvedEffect effect)
    {
        if (!effect.Params.TryGetValue("perSecondCap", out var cap) || cap <= 0)
            return true;
        if (!caster.TriggerTicks.TryGetValue(key, out var ticks))
            caster.TriggerTicks[key] = ticks = [];
        int window = binders.Ticks(1);
        ticks.RemoveAll(t => Now - t >= window);
        if (ticks.Count >= (int)cap)
            return false;
        ticks.Add(Now);
        return true;
    }

    /// <summary>
    /// The hero's BlastJumping state, as TF2's condition goes on or off: the
    /// held <c>blast_jumping</c> stats follow at once (Death From Above), and
    /// the end is a landing, which fires <c>blast_jump_landing</c>. An edge
    /// the state already has does nothing.
    /// </summary>
    public void SetBlastJumping(int playerIndex, bool on)
    {
        if (Ready(playerIndex) is not ({ } caster, { } hero, { } sheet, _) || caster.BlastJumping == on)
            return;

        caster.BlastJumping = on;
        log.Msg(Line(playerIndex, on ? $"blast jumping (tick {Now})" : $"landed from a blast jump (tick {Now})"));
        caster.HeldKey = HeldKey(caster, hero, sheet);
        caster.HeldStats = Held(caster, hero, sheet);
        hero.Recompute();
        if (!on)
            Trigger(playerIndex, "blast_jump_landing");
    }

    /// <summary>A hit the hero took: the rested trigger starts again.</summary>
    public void Hurt(int playerIndex)
    {
        if (casters.Of(playerIndex) is { } caster)
            caster.LastHurtTick = Now;
    }

    //-------------------------------------------------------------------------
    // Ticks
    //-------------------------------------------------------------------------

    public void Tick(int playerIndex)
    {
        if (Ready(playerIndex) is not ({ } caster, { } hero, { } sheet, { } stats))
            return;

        int now = Now;
        bool recompute = false;

        string? family = FamilyOf(World.ActiveItemClass(caster.Player));
        if (!caster.FamilySeen || family != caster.Family)
        {
            caster.Family = family;
            caster.FamilySeen = true;
            recompute = true;
            log.Msg(Line(playerIndex, $"weapon family {family ?? "none"}"));
        }

        // The condition hook tells each edge as TF2 makes it (SetBlastJumping);
        // the read here only catches an edge no hook told.
        SetBlastJumping(playerIndex, World.BlastJumping(caster.Player));

        if (buffs.Expire(playerIndex, now))
            recompute = true;
        foreach (var ended in caster.OrderEndTick.Where(o => o.Value <= now).Select(o => o.Key).ToList())
            caster.OrderEndTick.Remove(ended);

        var due = caster.Pending.Where(p => p.Tick <= now).ToList();
        caster.Pending.RemoveAll(p => p.Tick <= now);
        foreach (var p in due)
            binders.Of(p.Effect.Primitive)?.Due(caster, p);

        if (caster.Charging is { } held && !World.IsAlive(caster.Player))
        {
            caster.Charging = null;
            log.Msg(Line(playerIndex, $"{held.SkillId}'s charge lost: the hero is not alive"));
        }

        if (caster.Charging is { } charge && Misfired(sheet, stats, charge, now))
        {
            caster.Charging = null;
            Sync(caster, hero);
            Cast(caster, hero, rules.Skills.Skill(charge.SkillId), stages: null, misfire: true);
        }

        if (caster.State.Toggles.Count > 0)
            recompute |= Drain(caster, hero, sheet, stats);

        DashLanding(caster);

        string key = HeldKey(caster, hero, sheet);
        if (key != caster.HeldKey)
        {
            caster.HeldKey = key;
            caster.HeldStats = Held(caster, hero, sheet);
            recompute = true;
        }

        if (recompute)
            hero.Recompute();

        Follow(caster, hero);
        SendBar(caster);
    }

    /// <summary>The family of an item class, from the rules' one lookup (class words and all: <c>tf_weapon_shotgun_soldier</c> is a shotgun).</summary>
    public string? FamilyOf(string? itemClass) =>
        !string.IsNullOrEmpty(itemClass) && rules.Content.TryGetFamilyOfItemClass(itemClass, out var family) ? family.Id : null;

    private bool Misfired(HeroSheet sheet, HeroStats stats, SkillCharge charge, int now)
    {
        var resolved = rules.Skills.Resolve(sheet, stats, SkillBonuses.None, charge.SkillId);
        if (resolved.Effects.FirstOrDefault(e => e.Primitive == Primitive.Charge) is not { } effect)
            return false;
        double after = effect["misfireAfter"];
        return after > 0 && Seconds(now - charge.StartTick) > effect["time"] + after;
    }

    /// <summary>One tick of every toggle on; true when one switched off (out of mana).</summary>
    private bool Drain(SkillCaster caster, HeroComponent hero, HeroSheet sheet, HeroStats stats)
    {
        Sync(caster, hero);
        var before = caster.State;
        var after = rules.Casting.Drain(sheet, stats, SkillBonuses.None, before, World.TickInterval);
        double spent = before.Mana - after.Mana;
        if (spent > 0 && !hero.TrySpendMana(spent))
            hero.SetMana(0);
        caster.State = after;
        if (after.Toggles.Count == before.Toggles.Count)
            return false;

        foreach (string off in before.Toggles.Except(after.Toggles))
        {
            channel.Tell(caster.PlayerIndex, $"{NameOf(off)} is off: out of mana");
            log.Msg(Line(caster.PlayerIndex, $"{off} switched off: out of mana"));
        }

        return true;
    }

    /// <summary>A dash's landing: once the hero left the ground and stands again, <c>dash_landing</c> fires for the dash's own skill.</summary>
    private void DashLanding(SkillCaster caster)
    {
        if (caster.Dashing is not { } dash)
            return;
        bool airborne = World.Airborne(caster.Player);
        if (airborne)
        {
            caster.DashLeftGround = true;
            return;
        }

        if (!caster.DashLeftGround && Now - dash.Tick < binders.Ticks(0.5))
            return;

        caster.Dashing = null;
        foreach (var effect in dash.Skill.Effects.Where(e => e.Trigger == "dash_landing"))
            binders.Of(effect.Primitive)?.Fire(caster, dash with { Tick = Now }, effect);
    }

    /// <summary>The held state the held triggers read: BlastJumping, tenths of life missing, rested, toggles on.</summary>
    private string HeldKey(SkillCaster caster, HeroComponent hero, HeroSheet sheet)
    {
        int max = World.MaxHealth(hero.Player);
        int missing = max > 0 ? Math.Clamp((max - World.Health(hero.Player)) * 10 / max, 0, 10) : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"{caster.BlastJumping}|{missing}|{RestedSeconds(caster):0}|{string.Join(',', caster.State.Toggles)}|{sheet.SkillRanks.Count}");
    }

    private double RestedSeconds(SkillCaster caster) => Math.Min(60, Seconds(Now - caster.LastHurtTick));

    /// <summary>
    /// What the held triggers and the toggles grant now: <c>blast_jumping</c>'s
    /// stats while BlastJumping (Death From Above), <c>per_10pct_health_missing</c>'s
    /// times each tenth of life missing, <c>no_damage_taken</c>'s once
    /// <c>delay</c> seconds passed unhurt, a toggle's while it is on.
    /// </summary>
    private List<Contribution> Held(SkillCaster caster, HeroComponent hero, HeroSheet sheet)
    {
        var list = new List<Contribution>();
        var bare = rules.Heroes.Stats(sheet, []);
        int max = World.MaxHealth(hero.Player);
        int tenths = max > 0 ? Math.Clamp((max - World.Health(hero.Player)) * 10 / max, 0, 10) : 0;
        foreach (var (id, real) in sheet.SkillRanks)
        {
            if (real <= 0 || rules.Skills.Find(id) is not { } def || !rules.Skills.OpenTo(sheet.ClassId, def))
                continue;
            bool on = caster.State.Toggles.Contains(id);
            if (!on && !def.Effect.Any(e => e.Trigger is "blast_jumping" or "per_10pct_health_missing" or "no_damage_taken"))
                continue;

            var resolved = rules.Skills.Resolve(sheet, bare, SkillBonuses.None, id);
            foreach (var effect in resolved.Effects)
            {
                int times = effect.Trigger switch
                {
                    null when effect.Primitive == Primitive.Toggle && on => 1,
                    "blast_jumping" when caster.BlastJumping => 1,
                    "per_10pct_health_missing" => tenths,
                    "no_damage_taken" when RestedSeconds(caster) >= effect["delay"] => 1,
                    _ => 0,
                };
                if (times == 0)
                    continue;
                foreach (var c in SkillRules.AsBuff(id, effect))
                    list.Add(c with { Amount = c.Amount * times });
                if (effect.Trigger == "blast_jumping")
                    log.Msg(Line(caster.PlayerIndex, $"{id} holds while blast jumping: " +
                                                     string.Join(", ", effect.Stats.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Stat} {s.Amount:+0.##;-0.##}")))));
            }
        }

        return list;
    }

    /// <summary>The TF attributes the hero's skill stats hold, set when they change.</summary>
    private void Follow(SkillCaster caster, HeroComponent hero)
    {
        if (hero.Stats is not { } stats || hero.Sheet is not { } sheet)
            return;

        bool airCrit = false;
        foreach (var (id, real) in sheet.SkillRanks)
            if (real > 0 && rules.Skills.Find(id) is { } def && rules.Skills.OpenTo(sheet.ClassId, def))
                airCrit |= def.Effect.Any(e => e.Trigger == "on_melee_hit_airborne" && e.Conditions.Contains("melee_crit")
                                               && (e.Family is null || e.Family == caster.Family));

        attributes.Set(caster.PlayerIndex, new SkillTFAttributes(
            (float)Math.Max(0, 1 + stats.Total("self_blast_damage_pct") / 100),
            (float)Math.Max(0, 1 + stats.Total("jump_force_pct") / 100),
            airCrit));
    }

    //-------------------------------------------------------------------------
    // Casting
    //-------------------------------------------------------------------------

    private SkillUse Cast(SkillCaster caster, HeroComponent hero, SkillDef def, int? stages, bool misfire)
    {
        var sheet = hero.Sheet!;
        var stats = hero.Stats!;
        int now = Now;
        var result = rules.Casting.Cast(sheet, stats, SkillBonuses.None, caster.State, def.Id, stages);
        if (!result.Ok)
            return Refuse(caster, def.Id, result.Refusal!.Value);

        double before = hero.Mana;
        if (result.ManaSpent > 0 && !hero.TrySpendMana(result.ManaSpent))
            return Refuse(caster, def.Id, CastRefusal.NoMana);

        caster.State = result.State;
        // A cast is an act: it ends a respawn's grace (WO6's downed state reads the count).
        hero.Acted();
        if (result.Skill is not { } skill)
        {
            log.Msg(Line(caster.PlayerIndex, $"{def.Id} switched off"));
            SendBar(caster);
            return new SkillUse(SkillUseKind.ToggledOff, def.Id);
        }

        // The cooldown in whole server ticks; the rules' clock is the tick in seconds, so it holds exactly as long.
        int cooldown = binders.Ticks(skill.Cooldown);
        if (cooldown > 0)
        {
            caster.ReadyTick[def.Id] = now + cooldown;
            caster.State = caster.State with { ReadyAt = caster.State.ReadyAt.SetItem(def.Id, Seconds(now + cooldown)) };
        }

        foreach (string ended in result.OrdersEnded)
        {
            caster.OrderEndTick.Remove(ended);
            foreach (int index in buffs.End(ended))
                heroes.Of(index)?.Recompute();
        }

        foreach (string begun in result.OrdersBegun)
            caster.OrderEndTick[begun] = binders.Ticks(caster.State.Orders[begun] - Seconds(now)) + now;

        int used = stages ?? (int)skill.Effects.Where(e => e.Primitive == Primitive.Charge).Select(e => e["stages"]).DefaultIfEmpty(0).Max();
        log.Msg(Line(caster.PlayerIndex, string.Create(CultureInfo.InvariantCulture,
            $"cast {def.Id} rank {skill.Rank}{(used > 0 ? $" stage {used}" : "")}: mana {result.ManaSpent:0.##} ({before:0.##} -> {hero.Mana:0.##}), " +
            $"cooldown {cooldown} ticks{(cooldown > 0 ? $" to tick {now + cooldown}" : "")}{(misfire ? ", MISFIRE: nothing fired" : "")}")));

        if (misfire)
        {
            channel.Tell(caster.PlayerIndex, $"{def.Name} misfired");
            SendBar(caster);
            return new SkillUse(SkillUseKind.Misfire, def.Id, Stages: used, ManaSpent: result.ManaSpent);
        }

        Fire(caster, hero, def, skill, used);
        foreach (string order in result.OrdersBegun.Where(o => o != def.Id))
            FireOrder(caster, hero, order);

        if (def.Kind == SkillKind.Toggle)
            hero.Recompute();
        SendBar(caster);
        return new SkillUse(SkillUseKind.Cast, def.Id, Stages: used, ManaSpent: result.ManaSpent);
    }

    /// <summary>The cast's effects through their binders: the ones fired by a trigger, the passives, the charge and the toggle excepted; a debuff with no radius beside a damaging effect rides its hits.</summary>
    private void Fire(SkillCaster caster, HeroComponent hero, SkillDef def, ResolvedSkill skill, int stages)
    {
        bool hits = skill.Effects.Any(e => e.Trigger is null && e.Primitive is Primitive.Projectile or Primitive.Hitscan);
        var onHit = skill.Effects.Where(e => e.Trigger is null && e.Primitive == Primitive.Debuff && hits && e["radius"] <= 0).ToList();
        var cast = new SkillCast(caster.PlayerIndex, hero.Player, def, skill, stages, Now, onHit);
        caster.LastCast[def.Id] = cast;

        foreach (var effect in skill.Effects)
        {
            if (effect.Trigger is not null || onHit.Contains(effect)
                || effect.Primitive is Primitive.Passive or Primitive.Charge or Primitive.Toggle or Primitive.Triggered)
                continue;
            if (binders.Of(effect.Primitive) is { } binder)
                binder.Fire(caster, cast, effect);
            else
                log.Msg(Line(caster.PlayerIndex, $"{def.Id}: {effect.Primitive} is not bound"));
        }
    }

    /// <summary>An Order a cast issued beside its own (Supreme Commander's <c>issue_all_orders</c>): its auras go up at the hero's rank.</summary>
    private void FireOrder(SkillCaster caster, HeroComponent hero, string order)
    {
        var def = rules.Skills.Skill(order);
        var skill = rules.Skills.Resolve(hero.Sheet!, hero.Stats!, SkillBonuses.None, order);
        Fire(caster, hero, def, skill, 0);
    }

    private SkillUse Refuse(SkillCaster caster, string skillId, CastRefusal why)
    {
        log.Msg(Line(caster.PlayerIndex, $"cast {skillId} refused: {why}"));
        string key = skillId + "|" + why;
        if (!caster.Told.TryGetValue(key, out int told) || Now - told >= binders.Ticks(NoticeSeconds))
        {
            caster.Told[key] = Now;
            channel.Tell(caster.PlayerIndex, $"{NameOf(skillId)}: {Words(why)}");
        }

        return new SkillUse(SkillUseKind.Refused, skillId, why);
    }

    /// <summary>A refusal as the player is told it.</summary>
    public static string Words(CastRefusal why) => why switch
    {
        CastRefusal.NoMana => "not enough mana",
        CastRefusal.OnCooldown => "not ready",
        CastRefusal.NotLearned => "not learned",
        CastRefusal.NotCast => "an upgrade: nothing to cast",
        CastRefusal.NeedsBlastJumping => "only while blast jumping",
        CastRefusal.Inactive => "inactive",
        _ => "unknown skill",
    };

    //-------------------------------------------------------------------------

    private (SkillCaster Caster, HeroComponent Hero, HeroSheet Sheet, HeroStats Stats)? Ready(int playerIndex) =>
        casters.Of(playerIndex) is { } caster && heroes.Of(playerIndex) is { Sheet: { } sheet, Stats: { } stats } hero
            ? (caster, hero, sheet, stats)
            : null;

    /// <summary>The rules' state as of now: the hero's mana, the clock, BlastJumping.</summary>
    private void Sync(SkillCaster caster, HeroComponent hero) =>
        caster.State = caster.State with { Mana = hero.Mana, Now = Seconds(Now), BlastJumping = caster.BlastJumping };

    private string NameOf(string skillId) => rules.Skills.Find(skillId)?.Name ?? skillId;

    /// <summary>The owner's bar, sent when it differs from the last one sent.</summary>
    private void SendBar(SkillCaster caster)
    {
        int now = Now;
        var held = slots.Of(caster.PlayerIndex);
        var ready = new int[SkillBar.Slots];
        int toggled = 0;
        for (int i = 0; i < SkillBar.Slots; i++)
        {
            if (held[i] is not { } id)
                continue;
            ready[i] = caster.ReadyTick.TryGetValue(id, out int t) && t > now ? t : 0;
            if (caster.State.Toggles.Contains(id))
                toggled |= 1 << i;
        }

        var bar = new SkillBar
        {
            Skills = string.Join(',', held.Select(s => s ?? "")),
            ReadyTicks = ready,
            ChargeSlot = (byte)(caster.Charging?.Slot ?? 0),
            ChargeStartTick = caster.Charging?.StartTick ?? 0,
            ToggledMask = (byte)toggled,
            ChargeTicks = ChargeTicksOf(caster),
        };
        string key = $"{bar.Skills}|{string.Join(',', ready)}|{bar.ChargeSlot}|{bar.ChargeStartTick}|{bar.ToggledMask}|{bar.ChargeTicks}";
        if (key == caster.SentBar)
            return;
        caster.SentBar = key;
        channel.Bar(caster.PlayerIndex, bar);
    }

    /// <summary>
    /// The server ticks the charge held by <paramref name="caster"/> takes to its last
    /// stage: the resolved charge's time, in ticks as <see cref="StagesReached"/> counts
    /// them (so the client's meter fills exactly when the last stage is reached); 0 with
    /// no charge.
    /// </summary>
    public int ChargeTicksOf(SkillCaster caster)
    {
        if (caster.Charging is not { } charge || heroes.Of(caster.PlayerIndex) is not { Sheet: { } sheet, Stats: { } stats })
            return 0;
        var resolved = rules.Skills.Resolve(sheet, stats, SkillBonuses.None, charge.SkillId);
        return resolved.Effects.FirstOrDefault(e => e.Primitive == Primitive.Charge) is { } effect
            ? binders.Ticks(effect["time"])
            : 0;
    }

    private static string Line(int playerIndex, string what) =>
        string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Skills] {playerIndex} {what}\n");
}
