namespace TF2.Descent.Rules.Sheets;

/// <summary>
/// The most XP one kill of a robot template could pay any hero (plan_descent_logic RU4,
/// closing RU13's open bound): what <see cref="CheckpointRules"/> holds a checkpoint's claimed
/// experience to. 0 for a name that is no template.
/// </summary>
public interface IKillXpBound
{
    long MostXpPerKill(string template);
}
