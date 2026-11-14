using SourceSharp.Ai;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Roster;

public sealed partial class RobotRoster
{
    //-------------------------------------------------------------------------
    // For the far simulation and the embodiment rules (this assembly)
    //-------------------------------------------------------------------------

    internal ref AiVector PositionRef(int i) => ref _position[i];
    internal ref int RoomRef(int i) => ref _room[i];
    internal ref float YawRef(int i) => ref _yaw[i];
    internal ref int PatrolMarkerRef(int i) => ref _patrolMarker[i];
    internal ref long DwellUntilRef(int i) => ref _dwellUntil[i];
    internal void SetActivity(int i, FarActivity activity) => _activity[i] = activity;
    internal void MarkNear(int i, long now) => _lastNear[i] = now;
    internal void MarkRoomNear(int room, long now) => _roomLastNear[room] = now;
    internal RobotState StateAt(int i) => _state[i];
    internal int PackAt(int i) => _pack[i];
    internal bool BossAt(int i) => _boss[i];
    internal bool ChampionAt(int i) => !_affixes[i].IsEmpty;
    internal double SpeedAt(int i) => _stats[i].Speed;

    internal void Hear(int i, AiVector at, long now, int seat)
    {
        _alert[i] = AlertLevel.Alerted;
        _lastKnown[i] = at;
        _lastKnownTick[i] = now;
        if (seat >= 0) _targetSeat[i] = seat;
    }

    //-------------------------------------------------------------------------
    // The bytes the determinism facts hash
    //-------------------------------------------------------------------------

    /// <summary>Writes every field of every row, pack and room, in a fixed order, so two rosters can be compared by their bytes.</summary>
    public void WriteState(BinaryWriter w)
    {
        w.Write(Count);
        w.Write(PackCount);
        for (int i = 0; i < Count; i++)
        {
            w.Write(_serial[i]);
            w.Write(_template[i]);
            w.Write(_seed[i]);
            w.Write(_pack[i]);
            w.Write(_level[i]);
            w.Write(_scale[i]);
            WriteStats(w, _stats[i]);
            w.Write(_temperament[i].Style);
            foreach (int l in _temperament[i].Levels) w.Write(l);
            w.Write(_affixes[i].Length);
            foreach (var a in _affixes[i]) w.Write(a);
            w.Write(_boss[i]);
            w.Write(_homeRoom[i]);
            w.Write(_homeMarker[i]);
            w.Write((byte)_state[i]);
            w.Write(_room[i]);
            Write(w, _position[i]);
            w.Write(_yaw[i]);
            w.Write(_life[i]);
            w.Write(_statuses[i].Active.Length);
            foreach (var s in _statuses[i].Active)
            {
                w.Write(s.Id);
                w.Write(s.Stacks);
                w.Write(s.EndTick);
                w.Write(s.NextDamageTick);
            }
            w.Write(_statusDue[i]);
            w.Write(_speedFactor[i]);
            w.Write(_stunned[i]);
            w.Write(_clip[i]);
            w.Write(_body[i]);
            w.Write(_bodySerial[i]);
            w.Write(_targetSeat[i]);
            Write(w, _lastKnown[i]);
            w.Write(_lastKnownTick[i]);
            w.Write((byte)_alert[i]);
            w.Write(_threatSeat[i]);
            w.Write(_threat[i]);
            w.Write(_patrolMarker[i]);
            w.Write(_dwellUntil[i]);
            w.Write((byte)_activity[i]);
            w.Write(_lastNear[i]);
        }
        for (int p = 0; p < PackCount; p++)
        {
            w.Write(_packRoom[p]);
            w.Write(_packFirst[p]);
            w.Write(_packSize[p]);
            w.Write(_packSeed[p]);
            w.Write((byte)_packAlert[p]);
            w.Write(_contactRoom[p]);
            Write(w, _contact[p]);
            w.Write(_contactTick[p]);
            w.Write(_contactSeat[p]);
        }
        w.Write(_roomAwake.Length);
        for (int r = 0; r < _roomAwake.Length; r++)
        {
            w.Write(_roomAwake[r]);
            w.Write(_roomLastNear[r]);
        }
    }

    private static void Write(BinaryWriter w, AiVector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static void WriteStats(BinaryWriter w, RobotStats s)
    {
        w.Write(s.Scale);
        w.Write(s.Level);
        w.Write(s.Budget);
        w.Write(s.Life);
        w.Write(s.Damage);
        w.Write(s.Speed);
        w.Write(s.Reach);
        w.Write(s.KnockbackResistPct);
        w.Write(s.Xp);
        w.Write(s.LootQuality);
        foreach (var (type, value) in s.Resist)
        {
            w.Write(type);
            w.Write(value);
        }
        w.Write(s.Immunity ?? "");
    }
}
