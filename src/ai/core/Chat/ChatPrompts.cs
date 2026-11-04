using System.Globalization;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>What a bot's line is about.</summary>
public enum ChatTriggerKind
{
    Kill,
    Death,
    Suicide,
    Revenge,
    Streak,
    Leader,
    Pickup,
    Reply,
    Spot,
    Hot,
    Idle,
    Cold,

    /// <summary>A player's question, answered from facts (<see cref="ChatAnswerBook"/>).</summary>
    Answer,
}

/// <summary>A bot's self as chat speaks it: its name, the first name it answers to, and the tone of its most distinctive trait.</summary>
public sealed record ChatPersona(string Name, string FirstName, string Tone);

/// <summary>
/// One trigger. <paramref name="Other"/> is the other player, if any;
/// <paramref name="Short"/> asks for a few words (a reaction in a fight).
/// </summary>
public readonly record struct ChatTrigger(ChatTriggerKind Kind, string? Other = null, string? Weapon = null, int Count = 0, bool Short = false);

/// <summary>One line of a conversation a bot was in.</summary>
public readonly record struct ChatTurn(bool FromBot, string Text);

/// <summary>What a player just said to a bot, and what the two said before.</summary>
public sealed record ChatHeard(string Text, IReadOnlyList<ChatTurn> Earlier, bool Friendly = false);

/// <summary>
/// Everything a bot's line may be written from (plan_hl2mp_ai §3.6): its
/// persona, the scoreboard a player sees, and its own recent events, already
/// worded. The type has no position, no health and no other player's state: it
/// cannot carry what the bot does not know. A fact holds that by reflection.
/// </summary>
public sealed record ChatFacts(ChatPersona Persona, int Frags, int Deaths, int Rank, int Players, string Leader, int LeaderFrags, IReadOnlyList<string> Recent);

/// <summary>A prompt for the model.</summary>
public sealed record ChatPrompt(string System, string User, int MaxTokens)
{
    /// <summary>Both parts, for the filter's echo check.</summary>
    public string Whole => System + "\n" + User;
}

/// <summary>
/// Builds a line's prompt from <see cref="ChatFacts"/> (§3.6). Whatever a
/// player typed, and a player's name, is untrusted: <see cref="Untrusted"/>
/// caps it and takes out control characters and the delimiters, and a player's
/// text goes in only between <c>&lt;chat&gt;</c> tags, which the system text says
/// are data. A tiny model may follow it anyway: the output filter is the
/// protection, this only makes it rarer.
/// </summary>
public sealed class ChatPromptBuilder(ChatWords words)
{
    /// <summary>The most of a player's line that reaches the model.</summary>
    public const int MaxHeard = 100;

    /// <summary>The most of a player's name that reaches the model.</summary>
    public const int MaxName = 24;

    /// <summary>The words a line is asked to keep to, and a reaction in a fight.</summary>
    public const int Words = 12, ShortWords = 5;

    /// <summary>Tokens the model may spend: a little more than <see cref="Words"/> needs, so it stops by itself.</summary>
    public const int Tokens = 32;

    /// <summary>Tokens a rephrased answer may spend: the longest fact is near the line's limit.</summary>
    public const int AnswerTokens = 48;

    public ChatPrompt Build(ChatFacts facts, in ChatTrigger trigger, ChatHeard? heard = null, string avoid = "")
    {
        ArgumentNullException.ThrowIfNull(facts);
        string other = Untrusted(trigger.Other, MaxName);
        string system = Fill("system", ("first", Untrusted(facts.Persona.FirstName, MaxName)), ("tone", facts.Persona.Tone),
                             ("words", Num(trigger.Short ? ShortWords : Words)));
        if (avoid.Length > 0)
            system += " " + Fill("avoid", ("words", avoid));
        var user = new StringBuilder();
        user.Append(Fill("score", ("frags", Num(facts.Frags)), ("deaths", Num(facts.Deaths)), ("rank", Num(facts.Rank)), ("players", Num(facts.Players)),
                         ("leader", Untrusted(facts.Leader, MaxName)), ("leaderFrags", Num(facts.LeaderFrags)))).Append('\n');
        if (facts.Recent.Count > 0)
            user.Append(words.Prompts["recent"]).Append(' ').AppendJoin("; ", facts.Recent).Append(".\n");
        if (heard is { Earlier.Count: > 0 })
        {
            user.Append(Fill("earlier", ("other", other))).Append('\n');
            foreach (ChatTurn turn in heard.Earlier)
            {
                string text = Untrusted(turn.Text, MaxHeard);
                user.Append(turn.FromBot ? $"{words.Prompts["you"]}: {text}\n" : $"{other}: <chat>{text}</chat>\n");
            }
        }

        user.Append(trigger.Kind switch
        {
            ChatTriggerKind.Kill => Fill("kill", ("other", other), ("weapon", words.WeaponName(trigger.Weapon))),
            ChatTriggerKind.Death => Fill("death", ("other", other), ("weapon", words.WeaponName(trigger.Weapon))),
            ChatTriggerKind.Suicide => Fill("suicide"),
            ChatTriggerKind.Revenge => Fill("revenge", ("other", other)),
            ChatTriggerKind.Streak => Fill("streak", ("count", Num(trigger.Count))),
            ChatTriggerKind.Leader => Fill("leader"),
            ChatTriggerKind.Pickup => Fill("pickup", ("weapon", words.WeaponName(trigger.Weapon))),
            ChatTriggerKind.Spot => Fill("spot", ("other", other)),
            ChatTriggerKind.Hot => Fill("hot"),
            ChatTriggerKind.Idle => Fill("idle"),
            ChatTriggerKind.Cold => Fill("cold"),
            _ => Fill(heard is { Friendly: true } ? "replyFriendly" : "reply", ("other", other), ("text", Untrusted(heard?.Text, MaxHeard))),
        }).Append('\n').Append(words.Prompts["tail"]);
        return new ChatPrompt(system, user.ToString(), Tokens);
    }

    /// <summary>
    /// The prompt that asks a model to say a fact in a bot's voice (§3.6): the fact, what was asked
    /// between tags as ever, and an order to add nothing. <see cref="ChatAnswerCheck"/> is what holds it.
    /// </summary>
    public ChatPrompt BuildAnswer(ChatPersona persona, string other, string asked, string fact)
    {
        ArgumentNullException.ThrowIfNull(persona);
        string system = Fill("answerSystem", ("first", Untrusted(persona.FirstName, MaxName)), ("tone", persona.Tone));
        string user = Fill("answer", ("other", Untrusted(other, MaxName)), ("text", Untrusted(asked, MaxHeard)), ("fact", fact));
        return new ChatPrompt(system, user + "\n" + words.Prompts["tail"], AnswerTokens);
    }

    /// <summary>A bot's own remembered event, worded for its later prompts.</summary>
    public string Recent(ChatTriggerKind kind, string? other, string? weapon) => kind switch
    {
        ChatTriggerKind.Death => Fill("recentDeath", ("other", Untrusted(other, MaxName)), ("weapon", words.WeaponName(weapon))),
        ChatTriggerKind.Suicide => Fill("recentSuicide"),
        _ => Fill("recentKill", ("other", Untrusted(other, MaxName)), ("weapon", words.WeaponName(weapon))),
    };

    /// <summary>
    /// <paramref name="text"/> as data for a prompt: control and format
    /// characters out, the angle brackets that delimit it and the braces of a
    /// template out, spacing
    /// collapsed, at most <paramref name="cap"/> characters.
    /// </summary>
    public static string Untrusted(string? text, int cap)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var b = new StringBuilder(Math.Min(text.Length, cap));
        bool space = false;
        foreach (char c in text)
        {
            if (b.Length >= cap)
                break;
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse || c is '<' or '>' or '{' or '}')
            {
                space = c is '\n' or '\r' or '\t' || space;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && b.Length > 0)
                b.Append(' ');
            space = false;
            b.Append(c);
        }

        return b.ToString();
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private string Fill(string key, params ReadOnlySpan<(string Name, string Value)> values)
    {
        string text = words.Prompts[key];
        foreach ((string name, string value) in values)
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        return text;
    }
}
