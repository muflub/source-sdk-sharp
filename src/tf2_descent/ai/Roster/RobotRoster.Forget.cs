namespace TF2.Descent.Ai.Roster;

public sealed partial class RobotRoster
{
    /// <summary>
    /// A hero is dead, so nothing on the roster keeps his seat (<paramref name="seat"/>):
    /// a row whose carried target is his forgets the target, where and when it last knew of him,
    /// and its alert (a robot lent a body later must not go to where he fell); a row whose top
    /// threat or taunt is his drops it; a pack whose last contact is his calms and forgets
    /// whose it was (the far simulation's converging pack walks home). Contacts that name nobody
    /// (a barrel, a door) are not his and are kept. Answers the rows and packs changed.
    /// </summary>
    public (int Rows, int Packs) ForgetSeat(int seat)
    {
        if (seat <= 0)
            return (0, 0);

        int rows = 0, packs = 0;
        for (int i = 0; i < Count; i++)
        {
            bool changed = false;
            if (_targetSeat[i] == seat)
            {
                _targetSeat[i] = CarriedMemory.None.TargetSeat;
                _lastKnown[i] = CarriedMemory.None.LastKnownPosition;
                _lastKnownTick[i] = CarriedMemory.None.LastKnownTick;
                _alert[i] = CarriedMemory.None.Alert;
                changed = true;
            }

            if (_threatSeat[i] == seat)
            {
                _threatSeat[i] = CarriedMemory.None.ThreatSeat;
                _threat[i] = CarriedMemory.None.Threat;
                changed = true;
            }

            if (_tauntSeat[i] == seat)
            {
                _tauntSeat[i] = -1;
                _tauntUntil[i] = 0;
                changed = true;
            }

            if (changed)
                rows++;
        }

        for (int p = 0; p < PackCount; p++)
        {
            if (_contactSeat[p] != seat)
                continue;
            _packAlert[p] = AlertLevel.Calm;
            _contactSeat[p] = -1;
            packs++;
        }

        return (rows, packs);
    }
}
