namespace SourceSharp.Ai;

/// <summary>
/// How much bots talk (§3.6). Plain numbers from the console settings, handed
/// over each tick; a fact sets the chances to 0 or 1.
/// </summary>
public sealed record ChatSettings
{
    /// <summary>The server's one unprompted line per this many seconds (<c>ai_chat_interval</c>).</summary>
    public float IntervalSeconds { get; init; } = 30f;

    /// <summary>A bot's silence after any line of its own that was not a reply to a player.</summary>
    public float BotCooldownSeconds { get; init; } = 60f;

    /// <summary>The server's one reply to a player per this many seconds; player-prompted, so far tighter than the interval.</summary>
    public float ReplyIntervalSeconds { get; init; } = 3f;

    /// <summary>The chance a bot answers a line in general chat that named nobody (<c>ai_chat_reply_chance</c>).</summary>
    public float ReplyChance { get; init; } = 0.1f;

    /// <summary>The chance a kill or a death moves a bot to say something.</summary>
    public float ReactChance { get; init; } = 0.4f;

    /// <summary>The chance a rare moment does: revenge, a streak, the lead, a pickup worth having.</summary>
    public float MomentChance { get; init; } = 0.9f;

    /// <summary>How long after its last hit, given or taken, a bot counts as in a fight.</summary>
    public float FightSeconds { get; init; } = 4f;

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
}

/// <summary>A bot's row on the scoreboard, and who leads.</summary>
public readonly record struct ChatScore(int Frags, int Deaths, int Rank, int Players, string Leader, int LeaderFrags);

/// <summary>A line a bot said, for the inspector.</summary>
public sealed record ChatLine(float Time, string Trigger, string Text);

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
    private readonly List<Wish> _wishes = new();
    private readonly Dictionary<long, Pending> _pending = new();
    private readonly List<Ready> _ready = new();
    private readonly AiRandom _random = new(seed);
    private float _nextLine;
    private float _nextReply;
    private long _nextJob = 1;
    private bool _sayingBotReply;
    private bool _saying;

    /// <summary>The settings in force; the binding replaces them when a console setting changes.</summary>
    public ChatSettings Settings { get; set; } = new();

    /// <summary>Where lines are asked for and how long they may take; the binding's.</summary>
    public ChatTarget Target { get; set; }

    public float TimeoutSeconds { get; set; } = 5f;

    /// <summary>The slots of the bots chat knows, for the binding to follow the roster.</summary>
    public IEnumerable<int> Slots => _bots.Keys;

    /// <summary>The bot in <paramref name="slot"/> joins, saying nothing yet.</summary>
    public void Join(int slot, ChatPersona persona) => _bots[slot] = new BotState(persona);

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
        _pending.Clear();
        _ready.Clear();
        _nextLine = 0f;
        _nextReply = 0f;
        PausedUntil = 0f;
    }

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
                Want(killerSlot, new ChatTrigger(ChatTriggerKind.Kill, victimName, weapon, Short: true), Settings.ReactChance, now);
            }
        }

        if (_bots.TryGetValue(victimSlot, out BotState? victim))
        {
            victim.Streak = 0;
            if (suicide)
            {
                victim.Remember(prompts.Recent(ChatTriggerKind.Suicide, null, null), Settings.RecentEvents);
                Want(victimSlot, new ChatTrigger(ChatTriggerKind.Suicide, Short: true), Settings.ReactChance, now);
            }
            else
            {
                victim.LastKiller = killerName;
                victim.Remember(prompts.Recent(ChatTriggerKind.Death, killerName, weapon), Settings.RecentEvents);
                Want(victimSlot, new ChatTrigger(ChatTriggerKind.Death, killerName, weapon, Short: true), Settings.ReactChance, now);
            }
        }
    }

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
        bool fromBot = _bots.ContainsKey(speakerSlot);
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
            if (open.Length == 0 || _random.NextFloat() >= Settings.ReplyChance)
                return;
            named.Add(open[_random.NextInt(open.Length)]);
        }

        foreach (int slot in named)
        {
            BotState b = _bots[slot];
            List<ChatTurn> turns = b.Conversation(speakerKey);
            var earlier = turns.ToArray();
            b.Hear(speakerKey, new ChatTurn(false, text), Settings.ConversationLines);
            var heard = new ChatHeard(text, earlier);
            _wishes.Add(new Wish(slot, new ChatTrigger(ChatTriggerKind.Reply, speakerName, Short: b.InFight(now, Settings)), heard, speakerKey, now, fromBot, IsReply: true));
        }
    }

    /// <summary>
    /// The main thread's tick: takes what the worker finished, hands it one
    /// wish that passes the limits, says what is due. Never waits.
    /// </summary>
    public void Tick(float now)
    {
        while (worker.TryTake(out ChatResult r))
            Finished(in r, now);
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

        if (!_pending.Remove(r.Id, out Pending p) || r.Line is null || !_bots.ContainsKey(p.Slot))
            return;
        float reaction = Settings.ReactionMinSeconds + _random.NextFloat() * (Settings.ReactionMaxSeconds - Settings.ReactionMinSeconds);
        float typed = p.Asked + reaction + r.Line.Length / Settings.CharsPerSecond;
        _ready.Add(new Ready(p.Slot, r.Line, MathF.Max(now, typed), p.Asked, p.Trigger, p.SpeakerKey, p.ToBot));
    }

    private void Choose(float now)
    {
        Wish? chosen = null;
        for (int i = _wishes.Count - 1; i >= 0; i--)
        {
            Wish w = _wishes[i];
            if (now - w.At > (w.IsReply ? Settings.ReplyWishSeconds : Settings.WishSeconds))
            {
                counters.Limited();
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

    /// <summary>The limits: a player's reply has its own short interval, everything else the server's and the bot's.</summary>
    private bool Allowed(Wish w, float now)
    {
        if (!_bots.TryGetValue(w.Slot, out BotState? b))
            return false;
        if (w.IsReply && !w.FromBot)
            return now >= _nextReply;
        if (now < _nextLine || now < b.CooldownUntil)
            return false;
        return w.Trigger.Short || !b.InFight(now, Settings);
    }

    private void Ask(Wish wish, BotState bot, float now)
    {
        ChatScore score = world.ScoreOf(wish.Slot);
        var facts = new ChatFacts(bot.Persona, score.Frags, score.Deaths, score.Rank, score.Players, score.Leader, score.LeaderFrags, bot.Recent);
        ChatPrompt prompt = prompts.Build(facts, wish.Trigger, wish.Heard);
        long id = _nextJob++;
        if (!worker.TryPost(new ChatJob(id, wish.Slot, prompt, Target, TimeoutSeconds, bot.Persona.FirstName)))
        {
            counters.Limited();
            return;
        }

        counters.Request();
        _pending[id] = new Pending(wish.Slot, wish.At, wish.Trigger.Kind, wish.SpeakerKey, wish.FromBot);
        if (wish.IsReply && !wish.FromBot)
        {
            _nextReply = now + Settings.ReplyIntervalSeconds;
            // A conversation holds the bots' other chatter back a while.
            _nextLine = MathF.Max(_nextLine, now + Settings.IntervalSeconds * 0.5f);
        }
        else
        {
            _nextLine = now + Settings.IntervalSeconds;
            bot.CooldownUntil = now + Settings.BotCooldownSeconds;
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

            bool reply = r.Kind == ChatTriggerKind.Reply;
            if (reply && r.SpeakerKey is { } key)
                bot.Hear(key, new ChatTurn(true, r.Line), Settings.ConversationLines);
            bot.Said(new ChatLine(now, r.Kind.ToString(), r.Line), Settings.LinesKept);
            counters.Say(reply);
            _saying = true;
            _sayingBotReply = reply && r.ToBot;
            try
            {
                world.Say(r.Slot, r.Line);
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
        if (_random.NextFloat() < chance)
            _wishes.Add(new Wish(slot, trigger, null, null, now, false, IsReply: false));
    }

    private sealed record Wish(int Slot, ChatTrigger Trigger, ChatHeard? Heard, string? SpeakerKey, float At, bool FromBot, bool IsReply);

    private readonly record struct Pending(int Slot, float Asked, ChatTriggerKind Trigger, string? SpeakerKey, bool ToBot);

    private readonly record struct Ready(int Slot, string Line, float At, float Asked, ChatTriggerKind Kind, string? SpeakerKey, bool ToBot);

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
