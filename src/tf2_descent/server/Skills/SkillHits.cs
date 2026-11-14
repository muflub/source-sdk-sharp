using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Shared;
using TF2.Server.Combat;
using TF2.Shared;

namespace TF2.Descent.Server.Skills;

/// <summary>
/// How a skill's damage is told apart from a weapon's (R17): every hit a
/// skill deals is dealt under a weapon name of its own,
/// <c>descent_skill_&lt;id&gt;</c>, which no item carries, so TF2 reads no
/// carried weapon's attributes for it (a launcher's <c>mult_dmg</c> never
/// raises a Patriot Missile), and a damage modifier can ask
/// <see cref="ISkillHits.Current"/> whether the hit in flight is a skill's.
/// </summary>
public interface ISkillHits
{
    /// <summary>The skill the hit being dealt now is from, or null for any other hit (or none).</summary>
    string? Current();
}

/// <summary>The skill weapon names and the hit in flight, over TF2's damage scopes.</summary>
[SideService(Side.Server, As = [typeof(ISkillHits)])]
public sealed class SkillHits(TFDamageScopes scopes) : ISkillHits
{
    /// <summary>The prefix of every skill's weapon name.</summary>
    public const string Prefix = "descent_skill_";

    public static string WeaponNameOf(string skillId) => Prefix + skillId;

    /// <summary>The skill a weapon name is the skill weapon of, or null.</summary>
    public static string? SkillOf(string? weaponName) =>
        weaponName is { Length: > 0 } w && w.StartsWith(Prefix, StringComparison.Ordinal) ? w[Prefix.Length..] : null;

    public string? Current() => scopes.TryCurrent(out _, out _, out string weaponName) ? SkillOf(weaponName) : null;
}

/// <summary>
/// The rules' condition words on a HERO (a player) as TF conditions (E9: TF
/// conditions stay on heroes; robots take <see cref="IRobotStatusSink"/>).
/// A word with no TF condition here is not bound on a player, by name:
/// <c>crit_immune</c>, <c>no_ammo</c>, <c>no_reload</c>, <c>no_fall_damage</c>,
/// <c>air_control</c> (the parachute's own), <c>knock_up</c>, <c>emp_stun</c>,
/// <c>stun</c>, <c>slowed</c>, <c>burn</c> (each needs a TF arm with
/// parameters, an attacker or an attribute rather than a condition), and the
/// passive and cleanse words, which are not conditions to add.
/// </summary>
public static class SkillConditions
{
    /// <summary>The TF condition a word puts on a player, or null.</summary>
    public static TFCond? OnPlayer(string condition) => condition switch
    {
        "crit_boost" => TFCond.Critboosted,
        "mini_crit_marked" => TFCond.Markedfordeath,
        "invulnerable" => TFCond.InvulnerableUserBuff,
        "slow_fall" => TFCond.ParachuteDeployed,
        _ => null,
    };

    /// <summary>The TF condition a cleanse word takes off a player, or null.</summary>
    public static TFCond? Cleanses(string condition) => condition switch
    {
        "cleanse_burn" => TFCond.Burning,
        "cleanse_bleed" => TFCond.Bleeding,
        _ => null,
    };
}

/// <summary>What the runtime tells a hero's own client: a notice, and the skill bar.</summary>
public interface ISkillChannel
{
    void Tell(int playerIndex, string text);

    void Bar(int playerIndex, SkillBar bar);
}

/// <summary>The channel over TF2 Descent's notice and skill-bar messages.</summary>
[SideService(Side.Server, As = [typeof(ISkillChannel)])]
public sealed class SkillChannel(ClientMessages messages, IUserMessageSender sender) : ISkillChannel
{
    public void Tell(int playerIndex, string text) => messages.Notice(playerIndex, text);

    public void Bar(int playerIndex, SkillBar bar) => sender.Send(bar, playerIndex);
}
