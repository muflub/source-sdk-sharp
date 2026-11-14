using SourceSharp.Ai.TF2.Knowledge;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>The voice menu's lines a TF2 bot says (plan_bots_teams_tf2_part_g G.2.4).</summary>
public enum TFVoiceLine : byte
{
    None = 0,
    Medic,
    Thanks,
    Spy,
    SentryAhead,
}

/// <summary>
/// The voice menu, out (plan_bots_teams_tf2_part_g G.2.4): a bot says what a player would, with
/// the key a player would press. "Spy!" at an enemy its client draws as a Spy, "Sentry ahead!"
/// at an enemy sentry it had not seen before, "Medic!" when it is under half its health, and
/// "Thanks!" once a Medic beside it has healed it. Each is the mod's own <c>voicemenu</c> word, a
/// command row of the ability table (<see cref="TFAbilities.VoiceMedic"/> and the rest), sent as
/// the decide's one-shot: a decide carries one action, so the trigger is let go for that decide
/// (90 ms), and a reload, a weapon change or a rocket jump comes first.
///
/// <para>It does not spam: between any two lines it leaves its Teamwork's
/// <c>voiceEverySeconds</c> (a lone wolf says little), and the same line is not said again inside
/// <see cref="SameLineSeconds"/>. A line it could not say when its moment came is dropped after
/// <see cref="LineKeepsSeconds"/>, as a person lets it go.</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>Under this share of its health the bot calls for a Medic.</summary>
    public const float MedicCallShare = 0.5f;

    /// <summary>The same line is not said again inside this long.</summary>
    public const float SameLineSeconds = 30f;

    /// <summary>A line whose moment came while the bot could not speak is still said within this long, then dropped.</summary>
    public const float LineKeepsSeconds = 4f;

    /// <summary>
    /// Being healed: health gained a little at a time (at most <see cref="HealStepMost"/> a decide: a
    /// pack or a respawn gives it all at once) with a teammate Medic in sight within
    /// <see cref="HealerWithin"/>; thanks are due once that adds up to <see cref="ThanksAfterHealth"/>.
    /// </summary>
    public const int HealStepMost = 12, ThanksAfterHealth = 40;

    /// <summary>How near a Medic heals from: a medigun's beam holds to 1.2 times its 450 u reach.</summary>
    public const float HealerWithin = 450f * 1.2f;

    private const int VoiceLineCount = 5;
    private readonly int[] _voiceRow = new int[VoiceLineCount];
    private readonly int[] _saidCount = new int[VoiceLineCount];
    private readonly Tick[] _saidAt = new Tick[VoiceLineCount];
    private readonly bool[] _everSaid = new bool[VoiceLineCount];
    private readonly Tick[] _dueAt = new Tick[VoiceLineCount];
    private readonly bool[] _due = new bool[VoiceLineCount];
    private bool _voiceRowsRead, _spoke;
    private Tick _nextVoice;
    private int _healthBefore, _healed;

    /// <summary>How many times the bot has said <paramref name="line"/>, for the facts and the counters.</summary>
    public int Said(TFVoiceLine line) => _saidCount[(int)line];

    /// <summary>Voice lines said in all.</summary>
    public int VoiceLines { get; private set; }

    /// <summary>
    /// Says a line if one is due and the bot may speak: no other one-shot this decide, no rocket
    /// jump's sequence running, its last line long enough ago. The most pressing first: a Spy, a
    /// sentry, its own health, its thanks.
    /// </summary>
    private void Speak()
    {
        NoteLines();
        // A reload, a weapon change or a rocket jump is this decide's one-shot already; the trigger can be let go for a decide.
        if (_action.Kind is not (ActionKind.None or ActionKind.Fire) || Busy || (_spoke && _now < _nextVoice))
            return;
        TFVoiceLine line = Due(TFVoiceLine.Spy) ? TFVoiceLine.Spy
            : Due(TFVoiceLine.SentryAhead) ? TFVoiceLine.SentryAhead
            : Due(TFVoiceLine.Medic) ? TFVoiceLine.Medic
            : Due(TFVoiceLine.Thanks) ? TFVoiceLine.Thanks
            : TFVoiceLine.None;
        if (line == TFVoiceLine.None)
            return;
        int i = (int)line;
        if (_action.Kind == ActionKind.Fire)
        {
            // The shot this decide did not take is not counted.
            Shots--;
            FeetShots -= _look.Aim == AimPoint.Feet ? 1 : 0;
        }

        _action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, _voiceRow[i], ++_oneShot);
        (_due[i], _saidAt[i], _everSaid[i], _spoke) = (false, _now, true, true);
        _nextVoice = _now + (int)(_p.VoiceEverySeconds / _tick);
        _saidCount[i]++;
        VoiceLines++;
    }

    /// <summary>Whether <paramref name="line"/> waits to be said: its moment came within <see cref="LineKeepsSeconds"/>, the table has its row, and it was not said inside <see cref="SameLineSeconds"/>.</summary>
    private bool Due(TFVoiceLine line)
    {
        int i = (int)line;
        if (!_due[i])
            return false;
        if ((_now - _dueAt[i]) * _tick > LineKeepsSeconds)
            return _due[i] = false;
        return _voiceRow[i] >= 0 && !(_everSaid[i] && (_now - _saidAt[i]) * _tick < SameLineSeconds);
    }

    private void Call(TFVoiceLine line) => (_due[(int)line], _dueAt[(int)line]) = (true, _now);

    /// <summary>This decide's moments: what the bot has just seen or felt that a player would say something about.</summary>
    private void NoteLines()
    {
        if (!_voiceRowsRead)
        {
            _voiceRowsRead = true;
            _voiceRow[(int)TFVoiceLine.None] = -1;
            _voiceRow[(int)TFVoiceLine.Medic] = AbilityRow(TFAbilities.VoiceMedic);
            _voiceRow[(int)TFVoiceLine.Thanks] = AbilityRow(TFAbilities.VoiceThanks);
            _voiceRow[(int)TFVoiceLine.Spy] = AbilityRow(TFAbilities.VoiceSpy);
            _voiceRow[(int)TFVoiceLine.SentryAhead] = AbilityRow(TFAbilities.VoiceSentryAhead);
            _healthBefore = _self.Health;
        }

        int gained = _self.Health - _healthBefore;
        _healthBefore = _self.Health;
        if (_sentrySeenNew)
            Call(TFVoiceLine.SentryAhead);

        bool healer = false;
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            bool friend = TeamRule.Friends(_self.Team, f.Team);
            if (!friend && f.Class == (int)TFClass.Spy && TeamRule.IsGameTeam(f.Team))
                Call(TFVoiceLine.Spy);
            healer |= friend && f.Class == (int)TFClass.Medic && _self.Origin.DistanceTo(f.Origin) < HealerWithin;
        }

        if (HealthShare < MedicCallShare)
            Call(TFVoiceLine.Medic);

        // A hit ends a heal and with no Medic beside it nobody is owed; health all at once is a pack's, not a Medic's.
        if (gained < 0 || !healer)
            _healed = 0;
        else if (gained <= HealStepMost)
            _healed += gained;
        if (_healed >= ThanksAfterHealth)
        {
            _healed = 0;
            Call(TFVoiceLine.Thanks);
        }
    }
}
