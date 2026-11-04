namespace SourceSharp.Ai;

/// <summary>What a line is said for: the moment, who it may name, and whether the line it answers was friendly.</summary>
public readonly record struct ChatLineContext(ChatTriggerKind Kind, string? Other, string? Heard);

/// <summary>
/// The rules a line must meet for the moment it is about (plan_hl2mp_ai §3.6), beyond what
/// <see cref="ChatOutputFilter"/> asks of every line: it names only who the moment is about,
/// it has the fight the right way round, and a friendly line does not draw an insult. A tiny
/// model gets all three wrong often enough to read as confused ("nice one lodle" after killing
/// lodle, a kill reaction naming a third player), and the prompt alone did not cure it. Pure.
/// </summary>
public sealed class ChatLineRules(ChatWords words)
{
    /// <summary>Why <paramref name="line"/> does not fit <paramref name="context"/>, of everyone called <paramref name="known"/>; <see cref="ChatRefusal.None"/> when it does.</summary>
    public ChatRefusal Check(string line, in ChatLineContext context, IReadOnlySet<string> known)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(known);
        string lower = line.ToLowerInvariant();
        string[] said = ChatText.Words(lower);
        string? other = FirstWord(context.Other);
        string[] heard = context.Heard is { } h ? ChatText.Words(h.ToLowerInvariant()) : [];
        foreach (string word in said)
        {
            if (known.Contains(word) && word != other && !heard.Contains(word, StringComparer.Ordinal))
                return ChatRefusal.WrongPerson;
        }

        ChatTriggerKind k = context.Kind;
        bool won = k is ChatTriggerKind.Kill or ChatTriggerKind.Revenge or ChatTriggerKind.Streak or ChatTriggerKind.Spot or ChatTriggerKind.Leader;
        if (won && words.Line.WonSaysNot.Any(p => ChatText.HasPhrase(lower, p)))
            return ChatRefusal.WrongSide;
        if (k is ChatTriggerKind.Death or ChatTriggerKind.Suicide && words.Line.LostSaysNot.Any(p => ChatText.HasPhrase(lower, p)))
            return ChatRefusal.WrongSide;
        if (context.Heard is { } text && IsFriendly(text) && words.Line.Insults.Any(p => ChatText.HasPhrase(lower, p)))
            return ChatRefusal.Unfriendly;
        return ChatRefusal.None;
    }

    /// <summary>Whether a player's line is friendly: a kind word in it and no insult.</summary>
    public bool IsFriendly(string heard)
    {
        ArgumentNullException.ThrowIfNull(heard);
        string lower = heard.ToLowerInvariant();
        return words.Line.Friendly.Any(p => ChatText.HasPhrase(lower, p)) && !words.Line.Insults.Any(p => ChatText.HasPhrase(lower, p));
    }

    /// <summary>The first word of a name as chat speaks it, lower case; null for no name.</summary>
    public static string? FirstWord(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string[] w = ChatText.Words(name.ToLowerInvariant());
        return w.Length > 0 ? w[0] : null;
    }
}

/// <summary>
/// The last lines' openings, over the server (§3.6): a line that opens with the word one of the
/// last few did, or the two words one of the last dozen did, reads as the tic the model has
/// ("nice try", 13 of 44 lines in the game of 2026-11-03). The prompt names these words too.
/// </summary>
public sealed class ChatOpenings
{
    /// <summary>How many lines back a first word, and the first two words, may not come again.</summary>
    public const int FirstWordRing = 4, TwoWordRing = 12;

    private readonly List<string[]> _ring = new();

    public void Clear() => _ring.Clear();

    public bool Repeats(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] w = ChatText.Words(line.ToLowerInvariant());
        if (w.Length == 0)
            return false;
        for (int i = 0; i < _ring.Count; i++)
        {
            string[] was = _ring[_ring.Count - 1 - i];
            if (i < FirstWordRing && was[0] == w[0])
                return true;
            if (w.Length > 1 && was.Length > 1 && was[0] == w[0] && was[1] == w[1])
                return true;
        }

        return false;
    }

    public void Said(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] w = ChatText.Words(line.ToLowerInvariant());
        if (w.Length == 0)
            return;
        _ring.Add(w);
        if (_ring.Count > TwoWordRing)
            _ring.RemoveAt(0);
    }

    /// <summary>The words the next line should not open with, for its prompt: the ones it would be refused for.</summary>
    public string Avoid()
    {
        var names = new List<string>();
        for (int i = 0; i < Math.Min(FirstWordRing, _ring.Count); i++)
        {
            string first = _ring[_ring.Count - 1 - i][0];
            if (!names.Contains(first))
                names.Add(first);
        }

        return string.Join(", ", names);
    }
}

/// <summary>Plain text work chat's rules share: words and whole phrases, any punctuation.</summary>
public static class ChatText
{
    /// <summary>The words of already-lowered text: letters, digits and apostrophes.</summary>
    public static string[] Words(string lower)
    {
        ArgumentNullException.ThrowIfNull(lower);
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i <= lower.Length; i++)
        {
            bool letter = i < lower.Length && (char.IsLetterOrDigit(lower[i]) || lower[i] == '\'');
            if (letter && start < 0)
                start = i;
            else if (!letter && start >= 0)
            {
                words.Add(lower[start..i].Trim('\''));
                start = -1;
            }
        }

        words.RemoveAll(w => w.Length == 0);
        return [.. words];
    }

    /// <summary>Whether <paramref name="phrase"/> stands in <paramref name="lower"/> as whole words.</summary>
    public static bool HasPhrase(string lower, string phrase)
        => (" " + string.Join(' ', Words(lower)) + " ").Contains(" " + phrase + " ", StringComparison.Ordinal);
}
