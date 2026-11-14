using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Skills;

/// <summary>
/// The skill verbs a hero's client sends (plan_descent_logic HE6):
/// <list type="bullet">
/// <item><c>tf2_descent_cast &lt;slot 1-6&gt; &lt;1|0&gt;</c>: the slot's key went
/// down (1) or came up (0). The client's key actions send both edges, so a
/// charge is held between them.</item>
/// <item><c>tf2_descent_learn &lt;skill&gt;</c>: buy a rank with a skill point.</item>
/// <item><c>tf2_descent_assign &lt;slot 1-6&gt; &lt;skill|none&gt;</c>: put a learned cast skill in a slot; a refusal is told to the hero.</item>
/// </list>
/// Each is claimed for a hero only; anything else passes on.
/// </summary>
[SideService(Side.Server)]
public sealed class SkillClientCommands(SkillRuntime runtime, Heroes heroes, IEngineEntities entities, ISkillChannel channel)
    : IClientCommandHook, IOrderedHook
{
    public const string Cast = "tf2_descent_cast";
    public const string Learn = "tf2_descent_learn";
    public const string AssignVerb = "tf2_descent_assign";

    /// <summary>In TF2 Descent's skills block.</summary>
    public const int Order = ModStages.First + 640;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command is not (Cast or Learn or AssignVerb))
            return false;
        int index = entities.GetIndex(player);
        if (heroes.Of(index) is null)
            return false;

        switch (command)
        {
            case Cast when args.Length >= 2 && Int(args[0]) is int slot && Int(args[1]) is int edge:
                if (edge != 0)
                    runtime.Press(index, slot);
                else
                    runtime.Release(index, slot);
                return true;
            case Learn when args.Length >= 1:
                runtime.Buy(index, args[0]);
                return true;
            case AssignVerb when args.Length >= 2 && Int(args[0]) is int slot:
                // The buy tells its own refusal; an assign's is told here, so the screen's
                // action shows the server's reason (plan_descent_logic UI3).
                if (runtime.Assign(index, slot, args[1]) is { } refused)
                    channel.Tell(index, $"Cannot assign slot {slot}: {refused}");
                return true;
        }

        return true;
    }

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
}

/// <summary><c>tf2_descent_skill_buy &lt;player&gt; &lt;skill&gt;</c>: one rank, as the client's <c>tf2_descent_learn</c> (sv_cheats).</summary>
[ConCommand("tf2_descent_skill_buy", Help = "tf2_descent_skill_buy <player slot> <skill id>: buy one rank with a skill point (sv_cheats).")]
public sealed class SkillBuyCommand(SvCheatsConVar cheats, Heroes heroes, SkillRuntime runtime, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string skill)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_skill_buy", slot) is null)
            return;
        var result = runtime.Buy(slot, skill);
        log.Msg($"[TF2Descent/Skills] {slot} tf2_descent_skill_buy {skill}: {(result is { Ok: true } ? "bought" : "refused " + result?.Refusal)}\n");
    }
}

/// <summary><c>tf2_descent_skill_slot &lt;player&gt; &lt;slot&gt; &lt;skill|none&gt;</c>: as the client's <c>tf2_descent_assign</c> (sv_cheats).</summary>
[ConCommand("tf2_descent_skill_slot", Help = "tf2_descent_skill_slot <player slot> <skill slot 1-6> <skill id|none>: assign a skill slot (sv_cheats).")]
public sealed class SkillSlotCommand(SvCheatsConVar cheats, Heroes heroes, SkillRuntime runtime, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(1, SkillBar.Slots)] int index, string skill)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_skill_slot", slot) is null)
            return;
        if (runtime.Assign(slot, index, skill) is { } refused)
            log.Warning($"[TF2Descent/Skills] {slot} tf2_descent_skill_slot {index} {skill}: {refused}\n");
    }
}

/// <summary>
/// <c>tf2_descent_cast_as &lt;player&gt; &lt;slot&gt; &lt;1|0&gt;</c>: the client's
/// <c>tf2_descent_cast</c> for that player, from the server console (sv_cheats):
/// the live proof's way to press and release a key a headless client cannot.
/// </summary>
[ConCommand("tf2_descent_cast_as", Help = "tf2_descent_cast_as <player slot> <skill slot 1-6> <1 press|0 release>: a skill key's edge for the player (sv_cheats).")]
public sealed class SkillCastAsCommand(SvCheatsConVar cheats, Heroes heroes, SkillRuntime runtime, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(1, SkillBar.Slots)] int index, [ArgRange(0, 1)] int edge)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_cast_as", slot) is null)
            return;
        var use = edge != 0 ? runtime.Press(slot, index) : runtime.Release(slot, index);
        log.Msg($"[TF2Descent/Skills] {slot} tf2_descent_cast_as {index} {edge}: {use.Kind}{(use.Refusal is { } r ? " " + r : "")}\n");
    }
}

/// <summary><c>tf2_descent_skills &lt;player&gt;</c>: the hero's ranks, slots, cooldowns, toggles and skill stats (sv_cheats).</summary>
[ConCommand("tf2_descent_skills", Help = "tf2_descent_skills <player slot>: print the hero's skill ranks, slots, cooldowns and toggles (sv_cheats).")]
public sealed class SkillsCommand(SvCheatsConVar cheats, Heroes heroes, SkillCasters casters, SkillSlotStore slots,
                                  SkillBuffs buffs, SkillAttributes attributes, ISkillWorld world, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_skills", slot) is not { } hero)
            return;

        var c = CultureInfo.InvariantCulture;
        var sheet = hero.Sheet!;
        var stats = hero.Stats!;
        var caster = casters.Of(slot);
        string ranks = string.Join(" ", sheet.SkillRanks.Where(r => r.Value > 0).Select(r => $"{r.Key}:{r.Value}"));
        string bar = string.Join(" ", slots.Of(slot).Select((s, i) =>
        {
            int ready = s is not null && caster is not null && caster.ReadyTick.TryGetValue(s, out int t) && t > world.Tick ? t - world.Tick : 0;
            return $"{i + 1}={s ?? "-"}{(ready > 0 ? $"({ready}t)" : "")}";
        }));
        string held = string.Join(", ", buffs.Of(slot).Select(b => $"{b.SourceId} to tick {b.EndTick}"));
        var tf = attributes.Of(slot);
        string skillStats = string.Join(" ", new[] { "damage_all_pct", "damage_weapon_pct", "self_blast_damage_pct", "jump_force_pct",
                                                     "spread_pct", "projectile_speed_pct" }
            .Select(s => string.Create(c, $"{s} {stats.Total(s):0.##}")));
        log.Msg(string.Create(c,
            $"[TF2Descent/Skills] {slot} ranks [{ranks}] points {sheet.SkillPoints} | slots {bar} | " +
            $"toggles [{string.Join(",", caster?.State.Toggles ?? [])}] charging {caster?.Charging?.SkillId ?? "-"} " +
            $"blast jumping {caster?.BlastJumping} family {caster?.Family ?? "-"} | buffs [{held}] | {skillStats} | " +
            $"TF rocket_jump_dmg_reduction {tf.SelfBlastDamage:0.###} mult_dmgself_push_force {tf.SelfBlastPush:0.###} " +
            $"crit_while_airborne {(tf.CritWhileAirborne ? 1 : 0)} | tick {world.Tick} mana {hero.Mana:0.##}\n"));
    }
}

/// <summary>
/// <c>tf2_descent_skill_respec &lt;player&gt;</c>: every skill rank and attribute
/// point back, through the rules' respec and the hero's audited apply; the
/// price is not charged (sv_cheats).
/// </summary>
[ConCommand("tf2_descent_skill_respec", Help = "tf2_descent_skill_respec <player slot>: respec the hero by the rules, uncharged (sv_cheats).")]
public sealed class SkillRespecCommand(SvCheatsConVar cheats, Heroes heroes, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        if (new HeroCommandGate(cheats, heroes, log).Hero("tf2_descent_skill_respec", slot) is not { } hero)
            return;
        var audit = hero.Apply(sheet => hero.Rules.Heroes.Respec(sheet).Sheet);
        log.Msg($"[TF2Descent/Skills] {slot} respec: {(audit.Ok ? "applied" : "refused " + audit.Rule)}, skill points {hero.Sheet!.SkillPoints}\n");
    }
}
