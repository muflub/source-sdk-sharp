namespace SourceSharp.Ai;

/// <summary>
/// How much bots talk (§3.6). Plain numbers from the console settings, handed
/// over each tick; a fact sets the chances to 0 or 1.
/// </summary>
public sealed record ChatSettings
{
    /// <summary>The server's one unprompted line per this many seconds (<c>ai_chat_interval</c>).</summary>
    public float IntervalSeconds { get; init; } = 10f;

    /// <summary>A bot's silence after any line of its own that was not a reply to a player.</summary>
    public float BotCooldownSeconds { get; init; } = 30f;

    /// <summary>The server's one reply to a player per this many seconds; player-prompted, so far tighter than the interval.</summary>
    public float ReplyIntervalSeconds { get; init; } = 3f;

    /// <summary>The chance a bot answers a player's line in general chat that named nobody (<c>ai_chat_reply_chance</c>).</summary>
    public float ReplyChance { get; init; } = 0.5f;

    /// <summary>The same for a bot's line, as a share of that: bots talk to the player first.</summary>
    public float BotReplyShare { get; init; } = 0.2f;

    /// <summary>The chance a kill or a death moves a bot to say something.</summary>
    public float ReactChance { get; init; } = 0.4f;

    /// <summary>The chance a rare moment does: revenge, a streak, the lead, a pickup worth having.</summary>
    public float MomentChance { get; init; } = 0.9f;

    /// <summary>How long after its last hit, given or taken, a bot counts as in a fight.</summary>
    public float FightSeconds { get; init; } = 4f;

    /// <summary>Whether the limits follow the number of bots (<see cref="IntervalFor"/>, <see cref="CooldownFor"/>, <see cref="ReactFor"/>); a fact holds them still.</summary>
    public bool Scaled { get; init; } = true;

    /// <summary>A player's line holds the bots' chatter back this long, so it is not buried as it arrives (<c>hud_saytext_time</c> keeps it 12 s).</summary>
    public float HumanHoldSeconds { get; init; } = 4f;

    /// <summary>After this long with no line from any bot, one says something anyway, so a small game is not silent; 0 for never.</summary>
    public float IdleSeconds { get; init; } = 25f;

    /// <summary>
    /// The server's interval for this many bots: 0.7 of the setting plus 0.03 a bot, so 0.76 of it
    /// with 2 (a small game has few moments and can have them all), 1.06 with 12, and 1.3 from 20
    /// (a big one has far more than anyone can read). Measured live at 12 bots: a line per 13 s
    /// at 1.2 of the setting, so a little under it to land inside the 8 to 12 s asked for.
    /// </summary>
    public float IntervalFor(int bots) => Scaled ? IntervalSeconds * Math.Clamp(0.7f + 0.03f * bots, 0.7f, 1.3f) : IntervalSeconds;

    /// <summary>A bot's cooldown for this many bots: long enough that most of the others get a turn, never past the setting, never under the interval.</summary>
    public float CooldownFor(int bots)
    {
        if (!Scaled)
            return BotCooldownSeconds;
        float interval = IntervalFor(bots);
        return MathF.Max(interval, MathF.Min(BotCooldownSeconds, interval * bots * 0.6f));
    }

    /// <summary>The chance a kill or a death moves a bot, for this many bots: twice the setting up to 3 bots, falling to the setting at 6 and past it.</summary>
    public float ReactFor(int bots) => Scaled ? ReactChance * Math.Clamp(6f / MathF.Max(bots, 1), 1f, 2f) : ReactChance;

    /// <summary>The server's wait after a line that was refused, not said: it spends none of the interval.</summary>
    public float RefusedGapSeconds { get; init; } = 1f;

    /// <summary>How long a wish waits for the limits before it is forgotten: the moment has passed.</summary>
    public float WishSeconds { get; init; } = 3f;

    /// <summary>The same for a player's line, which is worth waiting a little longer for.</summary>
    public float ReplyWishSeconds { get; init; } = 8f;

    /// <summary>How long a line may wait from the moment it is about: past this it would make no sense.</summary>
    public float StaleSeconds { get; init; } = 15f;

    /// <summary>The look-up and reaction before a bot starts typing.</summary>
    public float ReactionMinSeconds { get; init; } = 0.6f;

    public float ReactionMaxSeconds { get; init; } = 1.6f;

    /// <summary>How fast it types.</summary>
    public float CharsPerSecond { get; init; } = 8f;

    /// <summary>The first of a streak that is worth a word, and every multiple of it.</summary>
    public int StreakStep { get; init; } = 3;

    /// <summary>The fewest kills that make leading worth a brag.</summary>
    public int LeaderMinFrags { get; init; } = 3;

    /// <summary>The last lines of a conversation a bot keeps per player, and the last events it keeps.</summary>
    public int ConversationLines { get; init; } = 6;

    public int RecentEvents { get; init; } = 4;

    /// <summary>How long a question waits for a bot out of a fight before one in a fight gives it a short answer.</summary>
    public float AnswerWaitSeconds { get; init; } = 2.5f;

    /// <summary>How long a model may take over an answer before the plain line goes out.</summary>
    public float AnswerTimeoutSeconds { get; init; } = 1.5f;

    /// <summary>The most time an answer spends being typed: someone is waiting for it.</summary>
    public float AnswerTypingSeconds { get; init; } = 3f;

    /// <summary>One answer per this long to one player: the bypass of the server's interval is for answers, not for a flood of questions.</summary>
    public float AnswerIntervalSeconds { get; init; } = 2f;

    /// <summary>The lines a bot's route shows.</summary>
    public int LinesKept { get; init; } = 8;
}

/// <summary>What bot chat needs of the game (§3.6): what a bot may write from, and its say path.</summary>
public interface IChatWorld
{
    /// <summary>The scoreboard row of the bot in <paramref name="slot"/> and its leader, as any player's HUD shows it.</summary>
    ChatScore ScoreOf(int slot);

    /// <summary>Says <paramref name="line"/> as the bot in <paramref name="slot"/>, through the path a player's chat takes.</summary>
    void Say(int slot, string line);

    /// <summary>Where the player in <paramref name="asker"/> stands, only if the bot in <paramref name="slot"/> can see them now.</summary>
    AiVector? AskerSeenBy(int slot, int asker);

    /// <summary>Where the player in <paramref name="asker"/> stands, for the marker on their own screen: never for a line.</summary>
    AiVector? AskerAt(int asker);

    /// <summary>Shows <paramref name="mark"/> to the player in <paramref name="asker"/> alone.</summary>
    void Mark(int asker, in ChatMark mark);
}

/// <summary>A bot's row on the scoreboard, and who leads.</summary>
public readonly record struct ChatScore(int Frags, int Deaths, int Rank, int Players, string Leader, int LeaderFrags);

/// <summary>A line a bot said, for the inspector.</summary>
public sealed record ChatLine(float Time, string Trigger, string Text);

/// <summary>What the filter refused and why, as the model wrote it: for the dev route, never for a player.</summary>
public sealed record ChatRefused(float Time, int Slot, string Reason, string Raw);

/// <summary>
/// Decides who says what and when (plan_hl2mp_ai §3.6): the game's events go
/// in as they happen, on the main thread, and the only things that come out
/// are lines to say. Nothing here reaches a brain, a decision or a command,
/// and it draws from its own random stream, so a bot's play is the same with
/// chat on or off. The limits: one unprompted line per
/// <see cref="ChatSettings.IntervalSeconds"/> over the server, a cooldown per
/// bot, no chatter in a fight (a kill or a death is a short reaction), a
/// typing delay before a line appears. A player who names a bot is answered by
/// that bot, or by the first two named; a line naming nobody is answered by at
/// most one bot, sometimes; a line from a bot is answered once at most, and
/// that answer by nobody.
/// </summary>
public sealed class ChatDirector(ChatWords words, ChatPromptBuilder prompts, ChatWorker worker, ChatCounters counters, IChatWorld world, ulong seed)
{
    /// <summary>A line names at most this many bots to answer it.</summary>
    public const int MaxAddressed = 2;

    private readonly Dictionary<int, BotState> _bots = new();
    private readonly List<ChatRefused> _refused = new();
    private readonly List<Wish> _wishes = new();
    private readonly List<Question> _questions = new();
    private readonly Dictionary<int, float> _nextAnswer = new();
    private readonly ChatOutputFilter _filter = new(words);
    private readonly Dictionary<long, Pending> _pending = new();
    private readonly List<Ready> _ready = new();
    private readonly AiRandom _random = new(seed);
    private readonly ChatLineRules _rules = new(words);
    private readonly ChatOpenings _openings = new();
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private float _nextLine;
    private float _nextReply;
    private float _lastLine = float.NegativeInfinity;
    private long _nextJob = 1;
    private bool _sayingBotReply;
    private bool _saying;

    /// <summary>What questions are answered from, or null for none: the binding's, once the level has a map and a help file.</summary>
    public ChatAnswerBook? Answers { get; set; }

    /// <summary>The settings in force; the binding replaces them when a console setting changes.</summary>
    public ChatSettings Settings { get; set; } = new();

    /// <summary>Where lines are asked for and how long they may take; the binding's.</summary>
    public ChatTarget Target { get; set; }

    public float TimeoutSeconds { get; set; } = 5f;

    /// <summary>The slots of the bots chat knows, for the binding to follow the roster.</summary>
    public IEnumerable<int> Slots => _bots.Keys;

    /// <summary>Follows the console's endpoint: a new one is asked at once, whatever the old one's outage.</summary>
    public void Aim(ChatTarget target)
    {
        if (target == Target)
            return;
        Target = target;
        PausedUntil = 0f;
        LastFailure = ChatFailure.None;
    }

    /// <summary>The bot in <paramref name="slot"/> joins, saying nothing yet.</summary>
    public void Join(int slot, ChatPersona persona)
    {
        _bots[slot] = new BotState(persona);
        Know(persona.FirstName);
    }

    /// <summary>Someone called <paramref name="name"/> is about: a line naming them when the moment is not about them is confused.</summary>
    private void Know(string? name)
    {
        if (ChatLineRules.FirstWord(name) is { } first && !words.NotNames.Contains(first))
            _names.Add(first);
    }

    /// <summary>The bot in <paramref name="slot"/> left: what it remembered, wanted and was about to say goes with it.</summary>
    public void Leave(int slot)
    {
        _bots.Remove(slot);
        _wishes.RemoveAll(w => w.Slot == slot);
        _ready.RemoveAll(r => r.Slot == slot);
        foreach (long id in _pending.Where(p => p.Value.Slot == slot).Select(p => p.Key).ToArray())
            _pending.Remove(id);
    }

    /// <summary>A map change: nobody remembers anything and the limits start over.</summary>
    public void Reset()
    {
        _bots.Clear();
        _wishes.Clear();
        _questions.Clear();
        _nextAnswer.Clear();
        _pending.Clear();
        _ready.Clear();
        _nextLine = 0f;
        _nextReply = 0f;
        _lastLine = float.NegativeInfinity;
        PausedUntil = 0f;
        _openings.Clear();
        _names.Clear();
    }

    /// <summary>The filter's last refusals, newest last (the route shows them).</summary>
    public IReadOnlyList<ChatRefused> Refused => _refused;

    /// <summary>The last lines the bot in <paramref name="slot"/> said.</summary>
    public IReadOnlyList<ChatLine> LinesOf(int slot) => _bots.TryGetValue(slot, out BotState? b) ? b.Lines : [];

    /// <summary>How many conversations the bot in <paramref name="slot"/> remembers (a fact's read).</summary>
    public int ConversationsOf(int slot) => _bots.TryGetValue(slot, out BotState? b) ? b.Conversations.Count : 0;

    /// <summary>A hit, given or taken: the bot in <paramref name="slot"/> is in a fight.</summary>
    public void Fighting(int slot, float now)
    {
        if (_bots.TryGetValue(slot, out BotState? b))
            b.LastFight = now;
    }

    /// <summary>
    /// A death on the kill feed. Slots are 0 for nobody; only a bot's own part
    /// in it reaches that bot's memory and its wish to say something.
    /// </summary>
    public void Death(int victimSlot, string victimName, int killerSlot, string killerName, string weapon, float now)
    {
        Know(victimName);
        Know(killerName);
        bool suicide = killerSlot == 0 || killerSlot == victimSlot;
        if (!suicide && _bots.TryGetValue(killerSlot, out BotState? killer))
        {
            killer.Streak++;
            killer.Remember(prompts.Recent(ChatTriggerKind.Kill, victimName, weapon), Settings.RecentEvents);
            bool revenge = string.Equals(killer.LastKiller, victimName, StringComparison.Ordinal);
            if (revenge)
            {
                killer.LastKiller = null;
                Want(killerSlot, new ChatTrigger(ChatTriggerKind.Revenge, victimName, weapon, Short: true), Settings.MomentChance, now);
            }
            else if (killer.Streak % Settings.StreakStep == 0)
            {
                Want(killerSlot, new ChatTrigger(ChatTriggerKind.Streak, victimName, weapon, killer.Streak, Short: true), Settings.MomentChance, now);
            }
            else
            {
                Want(killerSlot, new ChatTrigger(ChatTriggerKind.Kill, victimName, weapon, Short: true), Settings.ReactFor(_bots.Count), now);
            }
        }

        if (_bots.TryGetValue(victimSlot, out BotState? victim))
        {
            victim.Streak = 0;
            if (suicide)
            {
                victim.Remember(prompts.Recent(ChatTriggerKind.Suicide, null, null), Settings.RecentEvents);
                Want(victimSlot, new ChatTrigger(ChatTriggerKind.Suicide, Short: true), Settings.ReactFor(_bots.Count), now);
            }
            else
            {
                victim.LastKiller = killerName;
                victim.Remember(prompts.Recent(ChatTriggerKind.Death, killerName, weapon), Settings.RecentEvents);
                Want(victimSlot, new ChatTrigger(ChatTriggerKind.Death, killerName, weapon, Short: true), Settings.ReactFor(_bots.Count), now);
            }
        }
    }

    /// <summary>The bot in <paramref name="slot"/> found <paramref name="other"/> at the corner it expected them at (§5.5).</summary>
    public void CaughtAtSpot(int slot, string other, float now)
        => Want(slot, new ChatTrigger(ChatTriggerKind.Spot, other, Short: true), Settings.MomentChance, now);

    /// <summary>The bot in <paramref name="slot"/> is suddenly playing above itself (§5.5): it can tell.</summary>
    public void RunningHot(int slot, float now)
        => Want(slot, new ChatTrigger(ChatTriggerKind.Hot, Short: true), Settings.MomentChance, now);

    /// <summary>The bot in <paramref name="slot"/> has suddenly gone cold (§5.5): it can tell that too.</summary>
    public void RunningCold(int slot, float now)
        => Want(slot, new ChatTrigger(ChatTriggerKind.Cold, Short: true), Settings.MomentChance, now);

    /// <summary>The bot in <paramref name="slot"/> picked up <paramref name="weapon"/>; only the ones worth having are worth a word.</summary>
    public void PickedUp(int slot, string weapon, float now)
    {
        if (words.Braggable.Contains(weapon))
            Want(slot, new ChatTrigger(ChatTriggerKind.Pickup, Weapon: weapon), Settings.MomentChance, now);
    }

    /// <summary>The bot in <paramref name="slot"/>'s rank, as it reads the scoreboard: taking the lead is worth a word, once per time it takes it.</summary>
    public void Ranked(int slot, int rank, int frags, float now)
    {
        if (!_bots.TryGetValue(slot, out BotState? b))
            return;
        bool leads = rank == 1 && frags >= Settings.LeaderMinFrags;
        if (leads && !b.Leading)
            Want(slot, new ChatTrigger(ChatTriggerKind.Leader), Settings.MomentChance, now);
        b.Leading = leads;
    }

    /// <summary>
    /// A line said. <paramref name="speakerKey"/> identifies the speaker in a
    /// bot's memory, <paramref name="hearers"/> are the bots that would see
    /// the line in their chat, as a player in their place would. A line that
    /// starts like a command is nobody's conversation; a bot's reply to a bot
    /// (the line being said by <see cref="Deliver"/>) ends the exchange.
    /// </summary>
    public void Heard(int speakerSlot, string speakerKey, string speakerName, string text, IReadOnlyList<int> hearers, float now)
    {
        if (text.Length == 0 || text[0] is '/' or '!' || (_saying && _sayingBotReply))
            return;
        Know(speakerName);
        bool fromBot = _bots.ContainsKey(speakerSlot);
        if (!fromBot)
            _nextLine = MathF.Max(_nextLine, now + Settings.HumanHoldSeconds);
        if (!fromBot && Hear(speakerSlot, speakerKey, speakerName, text, hearers, now))
            return;
        var named = new List<int>();
        foreach (int slot in hearers)
        {
            if (slot != speakerSlot && named.Count < MaxAddressed && _bots.TryGetValue(slot, out BotState? b)
                && ChatAddress.Names(text, b.Persona, words.NotNames))
                named.Add(slot);
        }

        if (named.Count == 0)
        {
            int[] open = [.. hearers.Where(s => s != speakerSlot && _bots.ContainsKey(s))];
            if (open.Length == 0 || _random.NextFloat() >= (fromBot ? Settings.ReplyChance * Settings.BotReplyShare : Settings.ReplyChance))
                return;
            named.Add(open[_random.NextInt(open.Length)]);
        }

        foreach (int slot in named)
        {
            BotState b = _bots[slot];
            List<ChatTurn> turns = b.Conversation(speakerKey);
            var earlier = turns.ToArray();
            b.Hear(speakerKey, new ChatTurn(false, text), Settings.ConversationLines);
            var heard = new ChatHeard(text, earlier, _rules.IsFriendly(text));
            _wishes.Add(new Wish(slot, new ChatTrigger(ChatTriggerKind.Reply, speakerName, Short: b.InFight(now, Settings)), heard, speakerKey, now, fromBot, IsReply: true));
        }
    }

    /// <summary>A player's question goes to one bot, whoever is free when it can be answered; true when the line was one.</summary>
    private bool Hear(int asker, string speakerKey, string speakerName, string text, IReadOnlyList<int> hearers, float now)
    {
        if (Answers is not { } book)
            return false;
        int[] open = [.. hearers.Where(s => s != asker && _bots.ContainsKey(s))];
        int[] named = [.. open.Where(s => ChatAddress.Names(text, _bots[s].Persona, words.NotNames))];
        if (open.Length == 0 || book.Read(text, named.Length > 0, _names) is not { } topic)
            return false;
        counters.Question();
        _questions.Add(new Question(asker, speakerKey, speakerName, text, topic, open, named.Length > 0 ? named[0] : 0, now));
        return true;
    }

    /// <summary>The next question that can be answered: asked of whoever it named, or the bot free longest, a bot in a fight only once it has waited.</summary>
    private void ChooseAnswer(float now)
    {
        for (int i = 0; i < _questions.Count; i++)
        {
            Question q = _questions[i];
            if (now - q.At > Settings.ReplyWishSeconds)
            {
                counters.AnswerDropped();
                _questions.RemoveAt(i--);
                continue;
            }

            if (_nextAnswer.GetValueOrDefault(q.Asker) > now)
                continue;
            int slot = Answerer(q, now, out bool fighting);
            if (slot == 0)
                continue;
            _questions.RemoveAt(i);
            _nextAnswer[q.Asker] = now + Settings.AnswerIntervalSeconds;
            AskAnswer(q, slot, fighting, now);
            return;
        }
    }

    private int Answerer(Question q, float now, out bool fighting)
    {
        fighting = false;
        if (q.Named != 0 && _bots.TryGetValue(q.Named, out BotState? named))
        {
            fighting = named.InFight(now, Settings);
            return q.Named;
        }

        int best = 0;
        float bestSpoke = float.MaxValue;
        foreach (int s in q.Open)
        {
            if (!_bots.TryGetValue(s, out BotState? b) || b.InFight(now, Settings) || _pending.Values.Any(p => p.Slot == s))
                continue;
            float spoke = b.Lines.Count > 0 ? b.Lines[^1].Time : float.NegativeInfinity;
            if (spoke < bestSpoke)
            {
                bestSpoke = spoke;
                best = s;
            }
        }

        if (best != 0 || now - q.At < Settings.AnswerWaitSeconds)
            return best;

        // All of them are busy, and someone is waiting: whoever has fought least lately, in a few words.
        float oldest = float.MaxValue;
        foreach (int s in q.Open)
        {
            if (_bots.TryGetValue(s, out BotState? b) && b.LastFight < oldest)
            {
                oldest = b.LastFight;
                best = s;
            }
        }

        fighting = best != 0;
        return best;
    }

    /// <summary>
    /// Answers: a fact with a check goes to the model to be said in the bot's voice, and comes back
    /// checked or as the plain line; a shrug, an answer with nothing to check, a bot in a fight and a
    /// model that is not there all go out as the plain line, with no model asked.
    /// </summary>
    private void AskAnswer(Question q, int slot, bool fighting, float now)
    {
        BotState bot = _bots[slot];
        int draw = _random.NextInt(int.MaxValue);
        var asked = new ChatAsked(q.Text, world.ScoreOf(slot), bot.LastKiller, world.AskerSeenBy(slot, q.Asker), world.AskerAt(q.Asker), draw);
        ChatAnswer answer = Answers!.Answer(q.Topic, asked);
        string line = fighting && answer.ShortLine.Length > 0 ? answer.ShortLine : answer.Check is null ? answer.Line : Answers.Voiced(answer.Line, draw);
        if (answer.Kind is ChatTopicKind.Unknown or ChatTopicKind.People)
            counters.Shrug();
        bool toModel = answer.Check is { } && !fighting && now >= PausedUntil;
        long id = _nextJob++;
        if (toModel)
        {
            ChatPrompt prompt = prompts.BuildAnswer(bot.Persona, q.SpeakerName, q.Text, answer.Line);
            toModel = worker.TryPost(new ChatJob(id, slot, prompt, Target, MathF.Min(TimeoutSeconds, Settings.AnswerTimeoutSeconds), bot.Persona.FirstName, answer.Check, line));
            if (toModel)
                counters.Request();
        }

        var pending = new Pending(slot, q.At, ChatTriggerKind.Answer, q.SpeakerKey, false, q.SpeakerName, q.Text, q.Asker, answer.Mark);
        if (toModel)
        {
            _pending[id] = pending;
            return;
        }

        if (answer.Check is { })
            counters.AnswerTemplate();
        ChatVerdict plain = _filter.Check(line, "", bot.Persona.FirstName);
        if (plain.Line is null)
        {
            counters.Refuse(plain.Refusal);
            KeepRefusal(now, slot, plain.Refusal, line);
            return;
        }

        Queue(pending, plain.Line, now);
    }

    /// <summary>A line on its way: it appears when it has had time to be typed.</summary>
    private void Queue(in Pending p, string line, float now)
    {
        float reaction = Settings.ReactionMinSeconds + _random.NextFloat() * (Settings.ReactionMaxSeconds - Settings.ReactionMinSeconds);
        float typing = line.Length / Settings.CharsPerSecond;
        if (p.Trigger == ChatTriggerKind.Answer)
            typing = MathF.Min(typing, Settings.AnswerTypingSeconds);
        _ready.Add(new Ready(p.Slot, line, MathF.Max(now, p.Asked + reaction + typing), p.Asked, p.Trigger, p.SpeakerKey, p.ToBot, p.Asker, p.Mark));
    }

    /// <summary>
    /// The main thread's tick: takes what the worker finished, hands it one
    /// wish that passes the limits, says what is due. Never waits.
    /// </summary>
    public void Tick(float now)
    {
        while (worker.TryTake(out ChatResult r))
            Finished(in r, now);
        Idle(now);
        ChooseAnswer(now);
        Choose(now);
        Deliver(now);
    }

    /// <summary>How the last answer ended (the binding's one console line reads it); a cancel says nothing about the server.</summary>
    public ChatFailure LastFailure { get; private set; }

    public string LastFailureDetail { get; private set; } = "";

    /// <summary>No line is asked for before this time: the binding's pause while the server is down.</summary>
    public float PausedUntil { get; set; }

    private void Finished(in ChatResult r, float now)
    {
        if (r.Failure != ChatFailure.Cancelled)
        {
            LastFailure = r.Failure;
            LastFailureDetail = r.Detail;
        }

        if (r.Refusal != ChatRefusal.None)
            KeepRefusal(now, r.Slot, r.Refusal, r.Detail);

        if (!_pending.Remove(r.Id, out Pending p))
            return;
        if (r.Line is null)
        {
            GiveBack(p, now);
            return;
        }

        if (!_bots.ContainsKey(p.Slot))
            return;
        if (p.Trigger != ChatTriggerKind.Answer)
        {
            ChatRefusal unfit = _rules.Check(r.Line, new ChatLineContext(p.Trigger, p.Other, p.Heard), _names);
            if (unfit == ChatRefusal.None && p.Trigger != ChatTriggerKind.Reply && _openings.Repeats(r.Line))
                unfit = ChatRefusal.RepeatsOpening;
            if (unfit != ChatRefusal.None)
            {
                counters.Refuse(unfit);
                KeepRefusal(now, p.Slot, unfit, r.Line);
                GiveBack(p, now);
                return;
            }
        }

        _openings.Said(r.Line);
        Queue(p, r.Line, now);
    }

    /// <summary>Nothing said for a while: one bot out of a fight says something, so a small game is not silent.</summary>
    private void Idle(float now)
    {
        if (float.IsNegativeInfinity(_lastLine))
            _lastLine = now;
        if (Settings.IdleSeconds <= 0f || now - _lastLine < Settings.IdleSeconds || _bots.Count == 0)
            return;
        _lastLine = now;
        int[] free = [.. _bots.Where(b => !b.Value.InFight(now, Settings)).Select(b => b.Key).Order()];
        if (free.Length > 0)
            Want(free[_random.NextInt(free.Length)], new ChatTrigger(ChatTriggerKind.Idle), 1f, now);
    }

    private void KeepRefusal(float now, int slot, ChatRefusal why, string raw)
    {
        _refused.Add(new ChatRefused(now, slot, why.ToString(), raw));
        if (_refused.Count > Settings.LinesKept)
            _refused.RemoveAt(0);
    }

    /// <summary>A line that was never said spends none of the limits: the next moment may have them, a breath later.</summary>
    private void GiveBack(in Pending p, float now)
    {
        if (p.Trigger is ChatTriggerKind.Reply or ChatTriggerKind.Answer)
            return;
        _nextLine = MathF.Min(_nextLine, now + Settings.RefusedGapSeconds);
        if (_bots.TryGetValue(p.Slot, out BotState? bot))
            bot.CooldownUntil = MathF.Min(bot.CooldownUntil, now + Settings.RefusedGapSeconds);
    }

    private void Choose(float now)
    {
        Wish? chosen = null;
        for (int i = _wishes.Count - 1; i >= 0; i--)
        {
            Wish w = _wishes[i];
            if (now - w.At > (w.IsReply ? Settings.ReplyWishSeconds : Settings.WishSeconds))
            {
                counters.Limited(now < PausedUntil ? ChatLimit.Paused : Blocker(w, now) ?? ChatLimit.Queue);
                _wishes.RemoveAt(i);
                continue;
            }

            if (now < PausedUntil || !Allowed(w, now))
                continue;
            if (chosen is null || (w.IsReply && !chosen.IsReply))
                chosen = w;
        }

        if (chosen is not { } wish || !_bots.TryGetValue(wish.Slot, out BotState? bot))
            return;
        _wishes.Remove(wish);
        Ask(wish, bot, now);
    }

    /// <summary>
    /// The limits: a reply has its own short interval (the line it answers
    /// has just used the server's), a reply to a bot the bot's cooldown too;
    /// everything else the server's and the bot's.
    /// </summary>
    private bool Allowed(Wish w, float now) => Blocker(w, now) is null;

    /// <summary>The limit that holds <paramref name="w"/> back now, or null when none does.</summary>
    private ChatLimit? Blocker(Wish w, float now)
    {
        if (!_bots.TryGetValue(w.Slot, out BotState? b))
            return ChatLimit.Gone;
        if (w.IsReply)
        {
            if (now < _nextReply)
                return ChatLimit.Reply;
            return w.FromBot && now < b.CooldownUntil ? ChatLimit.Cooldown : null;
        }

        if (now < _nextLine)
            return ChatLimit.Interval;
        if (now < b.CooldownUntil)
            return ChatLimit.Cooldown;
        return w.Trigger.Short || !b.InFight(now, Settings) ? null : ChatLimit.Fight;
    }

    private void Ask(Wish wish, BotState bot, float now)
    {
        ChatScore score = world.ScoreOf(wish.Slot);
        var facts = new ChatFacts(bot.Persona, score.Frags, score.Deaths, score.Rank, score.Players, score.Leader, score.LeaderFrags, bot.Recent);
        ChatPrompt prompt = prompts.Build(facts, wish.Trigger, wish.Heard, _openings.Avoid());
        long id = _nextJob++;
        if (!worker.TryPost(new ChatJob(id, wish.Slot, prompt, Target, TimeoutSeconds, bot.Persona.FirstName)))
        {
            counters.Limited(ChatLimit.Queue);
            return;
        }

        counters.Request();
        _pending[id] = new Pending(wish.Slot, wish.At, wish.Trigger.Kind, wish.SpeakerKey, wish.FromBot, wish.Trigger.Other, wish.Heard?.Text);
        if (wish.IsReply)
        {
            _nextReply = now + Settings.ReplyIntervalSeconds;
            // A conversation holds the bots' other chatter back a while.
            _nextLine = MathF.Max(_nextLine, now + Settings.IntervalFor(_bots.Count) * 0.5f);
            if (wish.FromBot)
                bot.CooldownUntil = now + Settings.CooldownFor(_bots.Count);
        }
        else
        {
            _nextLine = now + Settings.IntervalFor(_bots.Count);
            bot.CooldownUntil = now + Settings.CooldownFor(_bots.Count);
        }
    }

    private void Deliver(float now)
    {
        for (int i = _ready.Count - 1; i >= 0; i--)
        {
            Ready r = _ready[i];
            if (now < r.At)
                continue;
            _ready.RemoveAt(i);
            if (now - r.Asked > Settings.StaleSeconds || !_bots.TryGetValue(r.Slot, out BotState? bot))
            {
                counters.Drop();
                continue;
            }

            bool answer = r.Kind == ChatTriggerKind.Answer;
            bool reply = r.Kind == ChatTriggerKind.Reply || answer;
            if (reply && r.SpeakerKey is { } key)
                bot.Hear(key, new ChatTurn(true, r.Line), Settings.ConversationLines);
            bot.Said(new ChatLine(now, r.Kind.ToString(), r.Line), Settings.LinesKept);
            counters.Say(reply);
            _lastLine = now;
            _saying = true;
            _sayingBotReply = answer || (reply && r.ToBot);
            try
            {
                world.Say(r.Slot, r.Line);
                if (r.Mark is { } mark)
                {
                    counters.Mark();
                    world.Mark(r.Asker, mark);
                }
            }
            finally
            {
                _saying = false;
                _sayingBotReply = false;
            }
        }
    }

    private void Want(int slot, in ChatTrigger trigger, float chance, float now)
    {
        bool wanted = _random.NextFloat() < chance;
        counters.Trigger(wanted);
        if (wanted)
            _wishes.Add(new Wish(slot, trigger, null, null, now, false, IsReply: false));
    }

    private sealed record Wish(int Slot, ChatTrigger Trigger, ChatHeard? Heard, string? SpeakerKey, float At, bool FromBot, bool IsReply);

    private sealed record Question(int Asker, string SpeakerKey, string SpeakerName, string Text, ChatTopic Topic, int[] Open, int Named, float At);

    private readonly record struct Pending(int Slot, float Asked, ChatTriggerKind Trigger, string? SpeakerKey, bool ToBot, string? Other, string? Heard, int Asker = 0, ChatMark? Mark = null);

    private readonly record struct Ready(int Slot, string Line, float At, float Asked, ChatTriggerKind Kind, string? SpeakerKey, bool ToBot, int Asker = 0, ChatMark? Mark = null);

    private sealed class BotState(ChatPersona persona)
    {
        private readonly List<string> _recent = new();
        private readonly List<ChatLine> _lines = new();

        public ChatPersona Persona { get; } = persona;

        public Dictionary<string, List<ChatTurn>> Conversations { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Recent => _recent;

        public IReadOnlyList<ChatLine> Lines => _lines;

        public int Streak { get; set; }

        public string? LastKiller { get; set; }

        public bool Leading { get; set; }

        public float LastFight { get; set; } = float.NegativeInfinity;

        public float CooldownUntil { get; set; }

        public bool InFight(float now, ChatSettings s) => now - LastFight < s.FightSeconds;

        public void Remember(string text, int keep)
        {
            _recent.Add(text);
            if (_recent.Count > keep)
                _recent.RemoveAt(0);
        }

        public List<ChatTurn> Conversation(string key)
        {
            if (!Conversations.TryGetValue(key, out List<ChatTurn>? turns))
                Conversations[key] = turns = new List<ChatTurn>();
            return turns;
        }

        public void Hear(string key, ChatTurn turn, int keep)
        {
            List<ChatTurn> turns = Conversation(key);
            turns.Add(turn);
            if (turns.Count > keep)
                turns.RemoveAt(0);
        }

        public void Said(ChatLine line, int keep)
        {
            _lines.Add(line);
            if (_lines.Count > keep)
                _lines.RemoveAt(0);
        }
    }
}

/// <summary>Whether a line names a bot: its first name, or a part of its trait name, whole words, any case.</summary>
public static class ChatAddress
{
    public static bool Names(string text, ChatPersona persona, IReadOnlySet<string> notNames)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(notNames);
        string[] said = text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim(' ', ',', '.', '!', '?', ':', ';', '"', '\'', '(', ')')).ToArray();
        foreach (string part in persona.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length >= 2 && !notNames.Contains(part) && said.Contains(part, StringComparer.Ordinal))
                return true;
        }

        return false;
    }
}
