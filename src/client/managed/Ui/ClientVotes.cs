using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The client's side of voting: the ballot as the server reports it, for the panel;
/// how long this player must wait to call another; and the radial menus. One key
/// (<c>callvote</c>) does what fits: with a ballot open and this player yet to
/// vote it asks yes or no, else it offers a vote to call, with the wait when there is one.
/// </summary>
public sealed class ClientVotes(RadialMenus radial, IPlayerNames names, IEngineHud hud, IFwdEngineClient engine, IGameClock clock, IEngineEnvironment environment, ILog log)
    : IVoteSource, IVoteStateHook
{
    /// <summary>How long a refusal stays up.</summary>
    public const float RefusalSeconds = 4f;

    private const int SlotCeiling = 32;

    private VoteState _state;
    private float _stateAt;
    private bool _voted;
    private float _cooldownUntil;
    private VoteRefusal _refusal;
    private float _refusalUntil;

    private VotePhase Phase => (VotePhase)_state.Phase;

    public bool Showing => Phase != VotePhase.Idle;

    public bool Decided => Phase is VotePhase.Passed or VotePhase.Failed;

    public bool Passed => Phase == VotePhase.Passed;

    public string Target => NameOf(_state.Target);

    public string Caller => NameOf(_state.Caller);

    public int Yes => _state.Yes;

    public int No => _state.No;

    public int Voters => _state.Voters;

    public int SecondsLeft => (int)MathF.Ceiling(MathF.Max(0f, _state.SecondsLeft - (clock.Now - _stateAt)));

    public bool Voted => _voted;

    public string Key => hud.LookupBinding("callvote") is { Length: > 0 } key ? key.ToUpperInvariant() : "";

    public string? Refusal => clock.Now < _refusalUntil ? _refusal switch
    {
        VoteRefusal.InProgress => "#SS_UI_Vote_Refused_InProgress",
        VoteRefusal.Cooldown => "#SS_UI_Vote_Refused_Cooldown",
        VoteRefusal.BadTarget => "#SS_UI_Vote_Refused_BadTarget",
        VoteRefusal.Immune => "#SS_UI_Vote_Refused_Immune",
        VoteRefusal.Disabled => "#SS_UI_Vote_Refused_Disabled",
        _ => null,
    } : null;

    /// <summary>Seconds until this player may call another vote; 0 now.</summary>
    public float CooldownLeft => MathF.Max(0f, _cooldownUntil - clock.Now);

    public void OnVoteState(VoteState state)
    {
        log.Msg($"[SourceSharp/Vote] state: {(VotePhase)state.Phase}, {state.Yes} yes, {state.No} no of {state.Voters}\n");
        // A new ballot: nobody here has voted on it yet.
        if ((VotePhase)state.Phase == VotePhase.Voting && Phase != VotePhase.Voting)
            _voted = false;
        _state = state;
        _stateAt = clock.Now;
    }

    public void OnVoteCooldown(VoteCooldown cooldown)
    {
        log.Msg($"[SourceSharp/Vote] {(VoteRefusal)cooldown.Refusal}, {cooldown.Seconds:0}s until the next call\n");
        if (cooldown.Seconds > 0f)
            _cooldownUntil = clock.Now + cooldown.Seconds;
        _refusal = (VoteRefusal)cooldown.Refusal;
        _refusalUntil = _refusal == VoteRefusal.None ? 0f : clock.Now + RefusalSeconds;
        // The call went through, and a caller's call is their yes.
        if (_refusal == VoteRefusal.None)
            _voted = true;
    }

    /// <summary>The key: the ballot while one waits on this player, else a vote to call.</summary>
    public void Open()
    {
        // A vote is a level's: at the main menu there is nothing to call or answer.
        if (!engine.IsInGame())
            return;
        radial.Toggle(this, Phase == VotePhase.Voting && !_voted ? Ballot : Call, engine.IsInGame);
    }

    private RadialPage Ballot()
        => new("#SS_UI_Vote_Ballot",
        [
            new RadialEntry("#SS_UI_Vote_Yes", () => Cast(1), Detail: Target),
            new RadialEntry("#SS_UI_Vote_No", () => Cast(2), Detail: Target),
        ]);

    private RadialPage Call()
    {
        float wait = CooldownLeft;
        bool busy = Showing;
        return new RadialPage("#SS_UI_Vote_Call",
        [
            new RadialEntry("#SS_UI_Vote_KickPlayer", Open: KickWho, Enabled: wait <= 0f && !busy,
                            Detail: wait > 0f ? Wait(wait) : null),
        ]);
    }

    /// <summary>"Wait 2:14".</summary>
    public static string Wait(float seconds)
    {
        int s = (int)MathF.Ceiling(seconds);
        return $"Wait {s / 60}:{s % 60:00}";
    }

    private RadialPage KickWho()
    {
        var entries = new List<RadialEntry>();
        var globals = environment.Globals;
        int max = globals.IsAvailable && globals.MaxClients > 0 ? globals.MaxClients : SlotCeiling;
        int local = engine.LocalPlayerSlot();
        for (int slot = 1; slot <= max; slot++)
        {
            int target = slot;
            if (slot != local && names.TryGet(slot, out var info))
                entries.Add(new RadialEntry(info.Name, () => engine.SubmitConsoleLine($"{VoteVerbs.Call} kick {target}")));
        }
        if (entries.Count == 0)
            entries.Add(new RadialEntry("#SS_UI_Vote_NoOne", Enabled: false));
        return new RadialPage("#SS_UI_Vote_KickWho", entries);
    }

    private void Cast(int option)
    {
        _voted = true;
        engine.SubmitConsoleLine($"{VoteVerbs.Cast} {option}");
    }

    private string NameOf(int slot) => names.TryGet(slot, out var info) ? info.Name : "";
}

[ConCommand("callvote", Help = "The ballot when a vote waits on you, else a vote to call")]
public sealed class CallVoteCommand(ClientVotes votes) : IConCommand
{
    public void Execute(string args) => votes.Open();
}
