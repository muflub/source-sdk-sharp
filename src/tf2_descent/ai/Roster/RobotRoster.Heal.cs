namespace TF2.Descent.Ai.Roster;

public sealed partial class RobotRoster
{
    /// <summary>
    /// Gives robot <paramref name="i"/> back up to <paramref name="amount"/> of life (a medic's
    /// heal, HO6), never past its most; a dead robot is not healed. Answers the life it gained.
    /// </summary>
    public double Heal(int i, double amount)
    {
        Row(i);
        if (_state[i] is RobotState.Dead or RobotState.Empty || !(amount > 0))
            return 0;
        double before = _life[i];
        _life[i] = Math.Min(_stats[i].Life, before + amount);
        return _life[i] - before;
    }
}
