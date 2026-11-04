using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SourceSharp.Ai;

/// <summary>Why the output filter refused a line. <see cref="None"/> is a line that passed.</summary>
public enum ChatRefusal
{
    None,
    Empty,
    Command,
    ServerPrefix,
    Url,
    ColourCode,
    FormatCode,
    NotLatin,
    Banned,
    Chatter,
    EchoesPrompt,

    /// <summary>The line names the bot saying it ("yeah thanks ravi", said by Ravi).</summary>
    SelfAddress,

    /// <summary>The line opens as another said just now: the tic "nice try" (<see cref="ChatLineRules"/>).</summary>
    RepeatsOpening,

    /// <summary>The line names a player the moment is not about (<see cref="ChatLineRules"/>).</summary>
    WrongPerson,

    /// <summary>The line has the fight the wrong way round: praise for the one it killed, a gloat at its killer (<see cref="ChatLineRules"/>).</summary>
    WrongSide,

    /// <summary>An insult for a player's friendly line (<see cref="ChatLineRules"/>).</summary>
    Unfriendly,

    /// <summary>An answer that said something its facts do not (<c>ChatAnswerCheck</c>); the plain template goes out instead.</summary>
    Unverified,
}

/// <summary>A filtered line, or why there is none.</summary>
public readonly record struct ChatVerdict(string? Line, ChatRefusal Refusal);

/// <summary>
/// The real protection of bot chat (plan_hl2mp_ai §3.6): every line the model
/// writes passes here before anyone can see it, reply or not, because a tiny
/// model will sometimes follow what a player tells it. The line is cut to one
/// line under <see cref="MaxLine"/> with control characters gone; it is refused
/// if it could read as a console command, a server message, a URL, a colour or
/// format code, a word on the file's lists, a piece of the prompt, or a
/// model talking about itself. A refused line is dropped and counted, never
/// retried. Pure, and safe from any thread: the worker runs it.
/// </summary>
public sealed partial class ChatOutputFilter(ChatWords words)
{
    /// <summary>The longest line, below the say path's own 127 (CheckChatText).</summary>
    public const int MaxLine = 100;

    /// <summary>Words in a row shared with the prompt that make a line an echo.</summary>
    public const int EchoWords = 5;

    /// <summary>
    /// Checks <paramref name="raw"/>: <paramref name="prompt"/> is what the
    /// model was given, and <paramref name="selfName"/> a prefix of the bot's
    /// own first name the model may have added ("Bobby: hi" says "hi").
    /// </summary>
    public ChatVerdict Check(string? raw, string prompt = "", string selfName = "")
    {
        if (string.IsNullOrEmpty(raw))
            return Refused(ChatRefusal.Empty);

        // Colour codes are refused before the control characters go: stripped, the line would read as if nothing had been tried.
        if (HasColourCode(raw))
            return Refused(ChatRefusal.ColourCode);

        string line = Clean(FirstLine(raw));
        if (selfName.Length > 0 && line.StartsWith(selfName + ":", StringComparison.OrdinalIgnoreCase))
            line = Clean(line[(selfName.Length + 1)..]);
        if (!line.Any(char.IsLetter))
            return Refused(ChatRefusal.Empty);

        string lower = line.ToLowerInvariant();
        ChatRefusal refusal = Refusal(line, lower, prompt);
        if (refusal == ChatRefusal.None && selfName.Length > 0 && Words(lower).Contains(selfName.ToLowerInvariant(), StringComparer.Ordinal))
            refusal = ChatRefusal.SelfAddress;
        return refusal != ChatRefusal.None ? Refused(refusal) : new ChatVerdict(Capped(line), ChatRefusal.None);
    }

    private static ChatVerdict Refused(ChatRefusal why) => new(null, why);

    private ChatRefusal Refusal(string line, string lower, string prompt)
    {
        if (line[0] is '/' or '!' or '@' or '#' or '\\' or '~' or '+' or '-')
            return ChatRefusal.Command;
        if (IsCommand(lower))
            return ChatRefusal.Command;
        if (line[0] == '[' || NamePrefix().IsMatch(line) || ServerWord().IsMatch(lower))
            return ChatRefusal.ServerPrefix;
        if (Url().IsMatch(lower))
            return ChatRefusal.Url;
        if (FormatCode().IsMatch(line))
            return ChatRefusal.FormatCode;
        if (line.Any(c => char.IsLetter(c) && c > '\u024F'))
            return ChatRefusal.NotLatin;
        if (HasBannedWord(lower))
            return ChatRefusal.Banned;
        if (line[0] is '*' or '(' || words.Chatter.Any(c => (" " + lower).Contains(c, StringComparison.Ordinal)))
            return ChatRefusal.Chatter;
        return Echoes(lower, prompt) ? ChatRefusal.EchoesPrompt : ChatRefusal.None;
    }

    private static string FirstLine(string raw)
    {
        foreach (string part in raw.Split(['\r', '\n', '\u2028', '\u2029', '\u0085']))
        {
            if (!string.IsNullOrWhiteSpace(part))
                return part;
        }

        return "";
    }

    /// <summary>Source's chat colours (0x01-0x08), written raw, as an escape, or as the tag plugins spell them.</summary>
    private static bool HasColourCode(string raw)
    {
        foreach (char c in raw)
        {
            if (c is >= '\u0001' and <= '\u0008')
                return true;
        }

        return ColourEscape().IsMatch(raw);
    }

    /// <summary>Control, format and emoji characters gone, quotes and spacing tidied, the wrapping quotes off.</summary>
    private static string Clean(string s)
    {
        var b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsSurrogate(c))
                continue;
            switch (c)
            {
                case '\u2018' or '\u2019':
                    b.Append('\'');
                    continue;
                case '\u201C' or '\u201D':
                    b.Append('"');
                    continue;
                case '\u2013' or '\u2014':
                    b.Append('-');
                    continue;
                case '\u2026':
                    b.Append("...");
                    continue;
            }

            if (char.IsWhiteSpace(c))
            {
                b.Append(' ');
                continue;
            }

            UnicodeCategory cat = char.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherSymbol or UnicodeCategory.PrivateUse or UnicodeCategory.NonSpacingMark)
                continue;
            b.Append(c);
        }

        string t = Spaces().Replace(b.ToString(), " ").Trim();
        while (t.Length >= 2 && t[0] is '"' or '\'' or '`' && t[^1] == t[0])
            t = t[1..^1].Trim();
        return t.Trim('"', '`').Trim();
    }

    /// <summary>Whether the line could be a console command: a known first word, one that stands alone, a word only the console uses anywhere, a cvar name, a slash word or a separator.</summary>
    private bool IsCommand(string lower)
    {
        if (lower.Contains(';', StringComparison.Ordinal) || SlashWord().IsMatch(lower) || CvarName().IsMatch(lower))
            return true;
        int space = lower.IndexOf(' ', StringComparison.Ordinal);
        string first = space < 0 ? lower : lower[..space];
        if (words.Commands.Contains(first))
            return true;
        return words.LoneCommands.Contains(lower.TrimEnd('.', '!', '?', ' ')) || Words(lower).Any(words.ConsoleWords.Contains);
    }

    /// <summary>
    /// Whether the line holds a word on the list, however it is dressed: accents and the usual
    /// swaps ("sh1t", "b!tch", "phuck"), masked ("f*ck"), stretched ("fuuuck"), or broken up by
    /// spaces, dots or symbols ("f u c k", "f.u.c.k", "what the f uck").
    /// </summary>
    private bool HasBannedWord(string lower)
    {
        string[] tokens = Tokens(lower);
        if (words.BannedAnywhere.Any(w => string.Concat(tokens).Contains(w, StringComparison.Ordinal)))
            return true;
        if (tokens.Any(IsBanned))
            return true;

        // A word in pieces: a run of tokens with a piece in it that is no word of its own.
        for (int i = 0; i < tokens.Length; i++)
        {
            bool piece = false;
            string joined = tokens[i];
            piece |= IsPiece(tokens[i]);
            for (int j = i + 1; j < tokens.Length && j <= i + MaxPieces && joined.Length < MaxJoined; j++)
            {
                piece |= IsPiece(tokens[j]);
                joined += tokens[j];
                if ((piece || (j == i + 1 && joined.Length >= WholeWordsJoined)) && IsBanned(joined))
                    return true;
            }
        }

        return false;
    }

    /// <summary>How far apart a word's pieces may be counted, and how long a joined run gets.</summary>
    private const int MaxPieces = 6, MaxJoined = 16;

    /// <summary>Two whole words that make a long word on the list are that word ("ass hole", "bull shit"); a short one is chance.</summary>
    private const int WholeWordsJoined = 7;

    /// <summary>A short token that is not a word in its own right: only those make a word of what stands beside them, so "go ok" stays two words.</summary>
    private bool IsPiece(string token)
        => token.Length <= 2 && token.All(char.IsLetter) && !words.ShortWords.Contains(token);

    /// <summary>A word on the list, as written, stretched ("fuuuuck"), by its sound ("phuck"), or masked ("f*ck").</summary>
    private bool IsBanned(string word)
    {
        if (word.Contains('*', StringComparison.Ordinal))
            return words.Banned.Any(w => MaskFits(word, w));
        if (words.Banned.Contains(word) || words.Banned.Contains(Unstretched(word)))
            return true;
        return word.Contains("ph", StringComparison.Ordinal) && words.Banned.Contains(Unstretched(word.Replace("ph", "f", StringComparison.Ordinal)));
    }

    private static bool MaskFits(string masked, string word)
    {
        if (masked.Length != word.Length || masked[0] == '*')
            return false;
        for (int i = 0; i < word.Length; i++)
        {
            if (masked[i] != '*' && masked[i] != word[i])
                return false;
        }

        return true;
    }

    private static string Unstretched(string word)
    {
        var b = new StringBuilder(word.Length);
        foreach (char c in word)
        {
            if (b.Length == 0 || b[^1] != c)
                b.Append(c);
        }

        return b.ToString();
    }

    /// <summary>
    /// The line as bare tokens: accents off, then split at anything that is not a letter,
    /// a digit or a symbol standing for a letter. A number standing alone stays a number
    /// ("5 hit" is no "s hit"); inside a word its digits and symbols read as the letters
    /// they stand for. A mask's <c>*</c> stays for the mask's match.
    /// </summary>
    private static string[] Tokens(string lower)
    {
        var tokens = new List<string>();
        var b = new StringBuilder();
        string plain = NoAccents(lower);
        void End()
        {
            if (b.Length == 0)
                return;
            string raw = b.ToString();
            b.Clear();
            string token = raw.Any(char.IsLetter) || raw.Contains('*', StringComparison.Ordinal) ? Unleet(raw) : raw;
            token = token.Trim('*').Length == 0 ? "" : token;
            if (token.Length > 0)
                tokens.Add(token);
        }

        for (int i = 0; i < plain.Length; i++)
        {
            char c = plain[i];
            bool inside = i > 0 && i + 1 < plain.Length && char.IsLetterOrDigit(plain[i - 1]) && (char.IsLetterOrDigit(plain[i + 1]) || plain[i + 1] is '*' or '$' or '@');
            if (char.IsLetterOrDigit(c) || c == '\'' || (c is '*' && (inside || (i > 0 && plain[i - 1] == '*'))) || (c is '$' or '@') || (c is '!' or '|' or '+' && inside))
                b.Append(c);
            else
                End();
        }

        End();
        return [.. tokens];
    }

    private static string NoAccents(string s)
    {
        string d = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(d.Length);
        foreach (char c in d)
        {
            if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                b.Append(c);
        }

        return b.ToString();
    }

    /// <summary>The usual digit and symbol spellings as letters.</summary>
    private static string Unleet(string word)
    {
        var b = new StringBuilder(word.Length);
        foreach (char c in word)
        {
            b.Append(c switch
            {
                '0' => 'o',
                '1' or '!' or '|' => 'i',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' or '$' => 's',
                '7' or '+' => 't',
                '8' => 'b',
                _ => c,
            });
        }

        return b.ToString();
    }

    private static IEnumerable<string> Words(string s)
    {
        int start = -1;
        for (int i = 0; i <= s.Length; i++)
        {
            bool letter = i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '\'');
            if (letter && start < 0)
                start = i;
            else if (!letter && start >= 0)
            {
                yield return s[start..i];
                start = -1;
            }
        }
    }

    /// <summary>Whether <see cref="EchoWords"/> words in a row of the line are in the prompt.</summary>
    private static bool Echoes(string lower, string prompt)
    {
        if (prompt.Length == 0)
            return false;
        string[] line = [.. Words(lower)];
        if (line.Length < EchoWords)
            return false;
        string joined = " " + string.Join(' ', Words(prompt.ToLowerInvariant())) + " ";
        for (int i = 0; i + EchoWords <= line.Length; i++)
        {
            if (joined.Contains(" " + string.Join(' ', line, i, EchoWords) + " ", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Under <see cref="MaxLine"/>: the first sentence if that fits, else cut at the last space.</summary>
    private static string Capped(string line)
    {
        if (line.Length <= MaxLine)
            return line;
        for (int i = MaxLine - 1; i > 0; i--)
        {
            if (line[i] is '.' or '!' or '?')
                return line[..(i + 1)];
        }

        int space = line.LastIndexOf(' ', MaxLine);
        return (space > 0 ? line[..space] : line[..MaxLine]).TrimEnd(',', ' ', ';', ':', '-');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>A leading "name:" of a word or a few, how a chat line or server message reads in the chat box.</summary>
    [GeneratedRegex(@"^[^\s:]+( [^\s:]+){0,3}\s?:(\s|$)")]
    private static partial Regex NamePrefix();

    [GeneratedRegex(@"\[server\]|\[console\]|\[admin\]|\bconsole\s?:|\bserver\s?:|\badmin\s?:")]
    private static partial Regex ServerWord();

    [GeneratedRegex(@"://|\bwww\.|\b[\w-]+\.(com|net|org|gg|io|tv|ru|xyz|co|me|info|biz|ly|app|dev|uk|de)\b")]
    private static partial Regex Url();

    /// <summary>A printf spec or a localisation token (<c>#HL2MP_Chat_All</c>), which the chat box would expand; hashtags with them.</summary>
    [GeneratedRegex(@"%[a-zA-Z0-9#]|#\w")]
    private static partial Regex FormatCode();

    [GeneratedRegex(@"\\x0|\\u00|\\[0-9]|\^[0-9]|\{[a-zA-Z]+\}")]
    private static partial Regex ColourEscape();

    [GeneratedRegex(@"(^|\s)/\w")]
    private static partial Regex SlashWord();

    [GeneratedRegex(@"\b(sv|mp|cl|ent|net|host|mat|ai|r)_[a-z]\w*")]
    private static partial Regex CvarName();

}
