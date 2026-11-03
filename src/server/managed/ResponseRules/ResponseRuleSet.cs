//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The response rule engine: CResponseSystem
/// (game/server/AI_ResponseSystem.cpp) — the script loader
/// (<c>response</c>/<c>criterion</c>/<c>rule</c>/<c>enumeration</c> and
/// <c>#include</c>), the criterion matcher, the best-rule search and the
/// response group selection, arm for arm.
/// </summary>
public sealed class ResponseRuleSet
{
    private readonly IUniformRandom _random;

    private readonly Action<string> _warn;

    public ResponseRuleSet(IUniformRandom random, Action<string>? warning = null)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _warn = warning ?? ((_) => { });
    }

    // ------------------------------------------------------------------
    // tables
    // ------------------------------------------------------------------

    private sealed class Matcher
    {
        public bool Valid;
        public bool IsNumeric;
        public bool NotEqual;
        public bool UseMin;
        public bool MinEquals;
        public bool UseMax;
        public bool MaxEquals;
        public float MinVal;
        public float MaxVal;
        public string Token = string.Empty;
        public string RawToken = string.Empty;
    }

    private sealed class Criterion
    {
        public string Name = string.Empty;
        public string Value = string.Empty;
        public float Weight = Quantise(1f);
        public bool Required;
        public readonly List<int> SubCriteria = new();
        public readonly Matcher Matcher = new();

        public bool IsSubCriteriaType => SubCriteria.Count > 0;
    }

    private sealed class ResponseEntry
    {
        public ResponseKind Kind = ResponseKind.None;
        public string Value = string.Empty;
        public float Weight = Quantise(1f);
        public bool First;
        public bool Last;
        public int DepletionCount;
    }

    private sealed class ResponseGroup
    {
        public readonly List<ResponseEntry> Entries = new();
        public ResponseParams Params = new();
        public bool Enabled = true;
        /// <summary>Stock's byte: the sequential walk wraps at 256.</summary>
        public byte CurrentIndex;
        public int DepletionCount = 1;
        public bool DepleteBeforeRepeat = true;
        public bool HasFirst;
        public bool HasLast;
        public bool Sequential;
        public bool NoRepeat;

        public bool HasUndepletedChoices
        {
            get
            {
                if (!DepleteBeforeRepeat)
                {
                    return true;
                }

                foreach (ResponseEntry entry in Entries)
                {
                    if (entry.DepletionCount != DepletionCount)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void MarkResponseUsed(int index)
        {
            if (!DepleteBeforeRepeat || (uint)index >= (uint)Entries.Count)
            {
                return;
            }

            Entries[index].DepletionCount = DepletionCount;
        }

        public void ResetDepletionCount()
        {
            if (!DepleteBeforeRepeat)
            {
                return;
            }

            DepletionCount++;
        }

        /// <summary>The stock Reset: note it sets the count BACK to 1 after incrementing it.</summary>
        public void Reset()
        {
            ResetDepletionCount();
            Enabled = true;
            CurrentIndex = 0;
            DepletionCount = 1;

            foreach (ResponseEntry entry in Entries)
            {
                entry.DepletionCount = 0;
            }
        }

        public bool HasUndepletedFirst(out int index)
        {
            index = -1;
            if (!DepleteBeforeRepeat)
            {
                return false;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                ResponseEntry entry = Entries[i];
                if (entry.DepletionCount != DepletionCount && entry.First)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        public bool HasUndepletedLast(out int index)
        {
            index = -1;
            if (!DepleteBeforeRepeat)
            {
                return false;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                ResponseEntry entry = Entries[i];
                if (entry.DepletionCount != DepletionCount && entry.Last)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class Rule
    {
        public string Name = string.Empty;
        public readonly List<int> Criteria = new();
        public readonly List<int> Responses = new();
        public string? Context;
        public bool MatchOnce;
        public bool Enabled = true;
        public bool ApplyContextToWorld;
    }

    private readonly List<Rule> _rules = new();
    private readonly List<Criterion> _criteria = new();
    private readonly List<ResponseGroup> _groups = new();
    private readonly Dictionary<string, int> _groupIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _criterionIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _enumerations = new(StringComparer.OrdinalIgnoreCase);

    public int RuleCount => _rules.Count;

    public int CriterionCount => _criteria.Count;

    public int ResponseCount => _groups.Count;

    /// <summary>The root script of the last successful <see cref="Load"/>.</summary>
    public string? LoadedScript { get; private set; }

    /// <summary>The stock float16 weight and interval fields, held as floats.</summary>
    private static float Quantise(float value) => (float)(Half)value;

    private static ResponseInterval Quantise(Interval interval)
        => new() { Start = Quantise(interval.Start), Range = Quantise(interval.Range) };

    // ------------------------------------------------------------------
    // loading
    // ------------------------------------------------------------------

    /// <summary>
    /// LoadRuleSet: read one root script and follow its includes. The tables
    /// are cleared first — stock loads once per process and never exercises a
    /// second load; ours reloads per level, where accumulate would be a
    /// different engine.
    /// </summary>
    public bool Load(string script, IEngineFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Clear();

        byte[]? bytes = files.ReadGameFileBytes(script);
        if (bytes is null || bytes.Length == 0)
        {
            _warn($"CResponseSystem:  failed to load {script}");
            return false;
        }

        _files = files;
        try
        {
            // Stock parses raw bytes through char*; Latin-1 is the identity
            // map, so every byte reaches a token exactly as it did there.
            HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
            LoadFromBuffer(script, Encoding.Latin1.GetString(bytes), included);
        }
        finally
        {
            _files = null;
        }

        if (_scripts.Count != 0)
        {
            throw new InvalidOperationException("response script stack did not unwind");
        }

        LoadedScript = script;
        return true;
    }

    public void Clear()
    {
        _rules.Clear();
        _criteria.Clear();
        _groups.Clear();
        _groupIndex.Clear();
        _criterionIndex.Clear();
        _enumerations.Clear();
    }

    /// <summary>ResetResponseGroups: every group's depletion state, per level.</summary>
    public void Reset()
    {
        foreach (ResponseGroup group in _groups)
        {
            group.Reset();
        }
    }

    private sealed class Script
    {
        public string Name = string.Empty;
        public string Buffer = string.Empty;

        /// <summary>The stock currenttoken; −1 once the parse saw end of file.</summary>
        public int Pos;

        public int TokenCount;
    }

    private readonly List<Script> _scripts = new();

    private IEngineFiles? _files;

    private void LoadFromBuffer(string scriptName, string buffer, HashSet<string> included)
    {
        included.Add(scriptName);
        _scripts.Add(new Script { Name = scriptName, Buffer = buffer });

        while (true)
        {
            if (!ParseToken())
            {
                break;
            }

            if (TokenIs("#include"))
            {
                ParseInclude(included);
            }
            else if (TokenIs("response"))
            {
                ParseResponse();
            }
            else if (TokenIs("criterion") || TokenIs("criteria"))
            {
                ParseCriterion();
            }
            else if (TokenIs("rule"))
            {
                ParseRule();
            }
            else if (TokenIs("enumeration"))
            {
                ParseEnumeration();
            }
            else
            {
                // Stock Errors here and abandons the file; ours warns and does.
                Warn($"CResponseSystem::LoadFromBuffer:  Unknown entry type '{_token}', expecting " +
                    "'response', 'criterion', 'enumeration' or 'rules'");
                break;
            }
        }

        _scripts.RemoveAt(_scripts.Count - 1);
    }

    private void ParseInclude(HashSet<string> included)
    {
        ParseToken();
        string include = $"scripts/{_token}";

        // The stock pool is case-insensitive (stringpool.cpp StrLess), and a
        // failed read is not a member: the next reference tries again.
        if (included.Contains(include))
        {
            return;
        }

        byte[]? bytes = _files!.ReadGameFileBytes(include);
        if (bytes is null)
        {
            _warn($"Unable to load #included script {include}");
            return;
        }

        LoadFromBuffer(include, Encoding.Latin1.GetString(bytes), included);
    }

    // ------------------------------------------------------------------
    // the tokeniser: IVEngineServer::ParseFile's, break set {}()':
    // ------------------------------------------------------------------

    private string _token = string.Empty;

    private bool _unget;

    private static bool IsBreak(char c)
        => c == '{' || c == '}' || c == '(' || c == ')' || c == '\'' || c == ':';

    /// <summary>One engine COM_Parse over a remembered position; −1 means end of file.</summary>
    private static (string Token, int Pos) ParseFile(string data, int pos)
    {
        while (true)
        {
            // skipwhite
            while (pos < data.Length && data[pos] <= ' ')
            {
                pos++;
            }

            if (pos >= data.Length)
            {
                return (string.Empty, -1);
            }

            char c = data[pos];

            // a // comment runs to end of line, then look again
            if (c == '/' && pos + 1 < data.Length && data[pos + 1] == '/')
            {
                while (pos < data.Length && data[pos] != '\n')
                {
                    pos++;
                }

                continue;
            }

            if (c == '"')
            {
                pos++;
                int start = pos;
                while (pos < data.Length)
                {
                    char q = data[pos++];
                    if (q == '"' || q == (char)0)
                    {
                        return (data[start..(pos - 1)], pos);
                    }
                }

                return (data[start..], pos);
            }

            // a break character is a one-character token
            if (IsBreak(c))
            {
                return (c.ToString(), pos + 1);
            }

            int word = pos;
            pos++;
            while (pos < data.Length)
            {
                char w = data[pos];
                if (w <= ' ' || IsBreak(w))
                {
                    break;
                }

                pos++;
            }

            return (data[word..pos], pos);
        }
    }

    private bool ParseToken()
    {
        if (_unget)
        {
            _unget = false;
            return true;
        }

        if (_scripts.Count == 0)
        {
            return false;
        }

        Script script = _scripts[^1];
        script.TokenCount++;

        if (script.Pos < 0)
        {
            _token = string.Empty;
            return false;
        }

        (string token, int pos) = ParseFile(script.Buffer, script.Pos);
        _token = token;
        script.Pos = pos;
        return pos >= 0;
    }

    private void Unget() => _unget = true;

    /// <summary>
    /// TokenWaiting: is there another token on this line? Stock's
    /// <c>!isspace(p) || isalnum(p)</c> is, for every character, its own
    /// negation's complement — the test is "a non-space before the line ends";
    /// a comment ends the line.
    /// </summary>
    private bool TokenWaiting()
    {
        if (_scripts.Count == 0)
        {
            return false;
        }

        Script script = _scripts[^1];
        int pos = script.Pos;
        if (pos < 0)
        {
            return false;
        }

        string data = script.Buffer;
        while (pos < data.Length && data[pos] != '\n')
        {
            char c = data[pos];
            if (c == '/' && pos + 1 < data.Length && data[pos + 1] == '/')
            {
                return false;
            }

            if (!char.IsWhiteSpace(c))
            {
                return true;
            }

            pos++;
        }

        return false;
    }

    private void Warn(string message)
    {
        string script = _scripts.Count > 0 ? _scripts[^1].Name : string.Empty;
        int token = _scripts.Count > 0 ? _scripts[^1].TokenCount : -1;
        _warn($"{script}(token {token}) : {message}");
    }

    private bool TokenIs(string expected) => string.Equals(_token, expected, StringComparison.OrdinalIgnoreCase);

    private static bool TextIs(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // the parsers
    // ------------------------------------------------------------------

    private void ParseResponse()
    {
        ParseToken();
        string groupName = _token;

        ResponseGroup group = new();

        while (true)
        {
            if (!ParseToken())
            {
                break; // divergence: stock's braceless-group loop spins at end of file
            }

            if (IsRootCommand())
            {
                Unget();
                break;
            }

            if (TokenIs("{"))
            {
                while (true)
                {
                    if (!ParseToken())
                    {
                        break; // divergence as above
                    }

                    if (TokenIs("}"))
                    {
                        break;
                    }

                    if (TokenIs("permitrepeats"))
                    {
                        group.DepleteBeforeRepeat = false;
                        continue;
                    }

                    if (TokenIs("sequential"))
                    {
                        group.Sequential = true;
                        continue;
                    }

                    if (TokenIs("norepeat"))
                    {
                        group.NoRepeat = true;
                        continue;
                    }

                    ParseOneResponse(groupName, group);
                }

                break;
            }

            if (ParseGroupParam(group))
            {
                continue;
            }

            ParseOneResponse(groupName, group);
        }

        InsertGroup(groupName, group);
    }

    private void ParseOneResponse(string groupName, ResponseGroup group)
    {
        ResponseEntry entry = new();

        entry.Kind = ComputeResponseType(_token);
        if (entry.Kind == ResponseKind.None)
        {
            Warn($"response entry '{groupName}' with unknown response type '{_token}'");
            return;
        }

        ParseToken();
        entry.Value = _token;

        while (TokenWaiting())
        {
            ParseToken();

            if (TokenIs("weight"))
            {
                ParseToken();
                entry.Weight = Quantise(VariantParse.Atof(_token));
                continue;
            }

            if (TokenIs("displayfirst"))
            {
                entry.First = true;
                group.HasFirst = true;
                continue;
            }

            if (TokenIs("displaylast"))
            {
                entry.Last = true;
                group.HasLast = true;
                continue;
            }

            if (ParseGroupParam(group))
            {
                continue;
            }

            Warn($"response entry '{groupName}' with unknown command '{_token}'");
        }

        group.Entries.Add(entry);
    }

    /// <summary>
    /// The shared response-parameter chain. Both ParseResponse's bare words
    /// and ParseOneResponse's trailing words write the GROUP's params, as
    /// stock's <c>rp</c> pointer into the group does.
    /// </summary>
    private bool ParseGroupParam(ResponseGroup group)
    {
        switch (_token.ToLowerInvariant())
        {
            case "predelay":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.DelayBeforeSpeak;
                group.Params.PreDelay = Quantise(Interval.Parse(_token));
                return true;
            case "nodelay":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.DelayAfterSpeak;
                group.Params.Delay = default;
                return true;
            case "defaultdelay":
                group.Params.Flags |= ResponseParamFlags.DelayAfterSpeak;
                group.Params.Delay = Quantise(new Interval(AISDefMinDelay, AISDefMaxDelay - AISDefMinDelay));
                return true;
            case "delay":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.DelayAfterSpeak;
                group.Params.Delay = Quantise(Interval.Parse(_token));
                return true;
            case "speakonce":
                group.Params.Flags |= ResponseParamFlags.SpeakOnce;
                return true;
            case "noscene":
                group.Params.Flags |= ResponseParamFlags.DontUseScene;
                return true;
            case "stop_on_nonidle":
                group.Params.Flags |= ResponseParamFlags.StopOnNonIdle;
                return true;
            case "odds":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.Odds;
                group.Params.Odds = (short)Math.Clamp(VariantParse.Atoi(_token), 0, 100);
                return true;
            case "respeakdelay":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.RespeakDelay;
                group.Params.RespeakDelay = Quantise(Interval.Parse(_token));
                return true;
            case "weapondelay":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.WeaponDelay;
                group.Params.WeaponDelay = Quantise(Interval.Parse(_token));
                return true;
            case "soundlevel":
                ParseToken();
                group.Params.Flags |= ResponseParamFlags.SoundLevel;
                group.Params.SoundLevel = (byte)SoundLevelText.Parse(_token, message => Warn(message));
                return true;
            default:
                return false;
        }
    }

    /// <summary>ai_speech.h's default-delay constants, as the stock doubles narrow.</summary>
    private const float AISDefMinDelay = 2.8f;

    private const float AISDefMaxDelay = 3.2f;

    private static ResponseKind ComputeResponseType(string s)
    {
        if (TextIs(s, "scene"))
        {
            return ResponseKind.Scene;
        }

        if (TextIs(s, "sentence"))
        {
            return ResponseKind.Sentence;
        }

        if (TextIs(s, "speak"))
        {
            return ResponseKind.Speak;
        }

        if (TextIs(s, "response"))
        {
            return ResponseKind.Reference;
        }

        if (TextIs(s, "print"))
        {
            return ResponseKind.Print;
        }

        return ResponseKind.None;
    }

    private bool IsRootCommand()
        => TokenIs("#include") || TokenIs("response") || TokenIs("enumeration")
            || TokenIs("criteria") || TokenIs("criterion") || TokenIs("rule");

    private void ParseCriterion()
    {
        ParseToken();
        string name = _token;
        ParseOneCriterion(name);
    }

    private int ParseOneCriterion(string name)
    {
        Criterion criterion = new();
        bool gotBody = false;

        while (TokenWaiting() || !gotBody)
        {
            if (!ParseToken())
            {
                break; // divergence: stock spins here on a truncated criterion
            }

            if (IsRootCommand())
            {
                Unget();
                break;
            }

            if (TokenIs("{"))
            {
                gotBody = true;

                while (true)
                {
                    if (!ParseToken())
                    {
                        break; // divergence as above
                    }

                    if (TokenIs("}"))
                    {
                        break;
                    }

                    if (_criterionIndex.TryGetValue(_token, out int sub))
                    {
                        criterion.SubCriteria.Add(sub);
                    }
                    else
                    {
                        Warn($"Skipping unrecongized subcriterion '{_token}' in '{name}'");
                    }
                }

                continue;
            }

            if (TokenIs("required"))
            {
                criterion.Required = true;
            }
            else if (TokenIs("weight"))
            {
                ParseToken();
                criterion.Weight = Quantise(VariantParse.Atof(_token));
            }
            else
            {
                criterion.Name = _token;
                ParseToken();
                criterion.Value = _token;
                gotBody = true;
            }
        }

        if (!criterion.IsSubCriteriaType)
        {
            ComputeMatcher(criterion);
        }

        if (_criterionIndex.ContainsKey(name))
        {
            Warn($"Multiple definitions for criteria '{name}'");
            return -1;
        }

        int index = _criteria.Count;
        _criteria.Add(criterion);
        _criterionIndex.Add(name, index);
        return index;
    }

    private void ParseEnumeration()
    {
        ParseToken();
        string enumerationName = _token;

        ParseToken();
        if (!TokenIs("{"))
        {
            Warn($"Expecting '{{' in enumeration '{enumerationName}', got '{_token}'");
            return;
        }

        while (true)
        {
            if (!ParseToken())
            {
                break; // divergence: stock's unterminated-enumeration loop spins
            }

            if (TokenIs("}"))
            {
                break;
            }

            if (_token.Length <= 0)
            {
                Warn($"Expecting more tokens in enumeration '{enumerationName}'");
                break;
            }

            string key = _token;
            ParseToken();
            float value = VariantParse.Atof(_token);

            // stock builds "[name::key]" and lowercases it; the lookup below
            // is case-insensitive over the same space
            string full = $"[{enumerationName}::{key}]".ToLowerInvariant();
            _enumerations.TryAdd(full, value);
        }
    }

    private void ParseRule()
    {
        int instanced = _instancedCriteria;

        ParseToken();
        string ruleName = _token;

        ParseToken();
        if (!TokenIs("{"))
        {
            Warn($"Expecting '{{' in rule '{ruleName}', got '{_token}'");
            return;
        }

        Rule rule = new() { Name = ruleName };

        bool validRule = true;
        while (true)
        {
            if (!ParseToken())
            {
                break; // divergence: stock's unterminated-rule loop spins
            }

            if (TokenIs("}"))
            {
                break;
            }

            if (_token.Length <= 0)
            {
                Warn($"Expecting more tokens in rule '{ruleName}'");
                break;
            }

            if (TokenIs("matchonce"))
            {
                rule.MatchOnce = true;
                continue;
            }

            if (TokenIs("applyContextToWorld"))
            {
                rule.ApplyContextToWorld = true;
                continue;
            }

            if (TokenIs("applyContext"))
            {
                ParseToken();
                rule.Context = rule.Context is null ? _token : $"{rule.Context},{_token}";
                continue;
            }

            if (TokenIs("response"))
            {
                while (TokenWaiting())
                {
                    ParseToken();
                    if (_groupIndex.TryGetValue(_token, out int group))
                    {
                        rule.Responses.Add(group);
                    }
                    else
                    {
                        validRule = false;
                        Warn($"No such response '{_token}' for rule '{ruleName}'");
                    }
                }

                continue;
            }

            if (TokenIs("criteria") || TokenIs("criterion"))
            {
                while (TokenWaiting())
                {
                    ParseToken();
                    if (_criterionIndex.TryGetValue(_token, out int criterion))
                    {
                        rule.Criteria.Add(criterion);
                    }
                    else
                    {
                        validRule = false;
                        Warn($"No such criterion '{_token}' for rule '{ruleName}'");
                    }
                }

                continue;
            }

            // anything else is an inline criterion under a generated name
            string generated = $"[{ruleName}{++instanced:D3}]";
            _instancedCriteria = instanced;
            Unget();
            int index = ParseOneCriterion(generated);
            if (index >= 0)
            {
                rule.Criteria.Add(index);
            }
        }

        if (validRule)
        {
            _rules.Add(rule);
        }
        else
        {
            _warn($"Discarded rule {ruleName}");
        }
    }

    /// <summary>Stock's function-static instanced-criteria counter, per engine.</summary>
    private int _instancedCriteria;

    private void InsertGroup(string name, ResponseGroup group)
    {
        _groups.Add(group);
        _groupIndex.TryAdd(name, _groups.Count - 1);
    }

    // ------------------------------------------------------------------
    // matching
    // ------------------------------------------------------------------

    private float LookupEnumeration(string name, out bool found)
    {
        if (_enumerations.TryGetValue(name, out float value))
        {
            found = true;
            return value;
        }

        found = false;
        return 0f;
    }

    private void ResolveToken(Matcher matcher, string raw)
    {
        if (!raw.StartsWith('['))
        {
            matcher.Token = raw;
            return;
        }

        float value = LookupEnumeration(raw, out bool found);
        if (!found)
        {
            matcher.Token = raw;
            Warn($"No such enumeration '{raw}'");
            return;
        }

        // Q_snprintf(token, "%f", f): six fixed decimals
        matcher.Token = value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>AppearsToBeANumber: atof non-zero, or every character a zero.</summary>
    private static bool AppearsToBeANumber(string token)
    {
        if (VariantParse.Atof(token) != 0f)
        {
            return true;
        }

        foreach (char c in token)
        {
            if (c != '0')
            {
                return false;
            }
        }

        return true;
    }

    private void ComputeMatcher(Criterion criterion)
    {
        Matcher matcher = criterion.Matcher;

        string value = criterion.Value;

        bool gt = false;
        bool lt = false;
        bool eq = false;
        bool nt = false;

        var raw = new StringBuilder();
        string rawToken = string.Empty;
        string token = string.Empty;

        int position = 0;
        bool done = false;
        while (!done)
        {
            char c = position < value.Length ? value[position] : (char)0;
            switch (c)
            {
                case '>':
                    gt = true;
                    break;
                case '<':
                    lt = true;
                    break;
                case '=':
                    eq = true;
                    break;
                case '!':
                    nt = true;
                    break;
                case ',':
                case (char)0:
                    rawToken = raw.ToString();
                    raw.Clear();

                    ResolveToken(matcher, rawToken);
                    token = matcher.Token;

                    if (gt)
                    {
                        matcher.UseMin = true;
                        matcher.MinEquals = eq;
                        matcher.MinVal = VariantParse.Atof(token);
                        matcher.IsNumeric = true;
                    }
                    else if (lt)
                    {
                        matcher.UseMax = true;
                        matcher.MaxEquals = eq;
                        matcher.MaxVal = VariantParse.Atof(token);
                        matcher.IsNumeric = true;
                    }
                    else
                    {
                        matcher.NotEqual = nt;
                        matcher.IsNumeric = AppearsToBeANumber(token);
                    }

                    gt = lt = eq = nt = false;

                    if (position >= value.Length)
                    {
                        done = true;
                    }

                    break;
                default:
                    raw.Append(c);
                    break;
            }

            position++;
        }

        matcher.RawToken = rawToken;
        matcher.Valid = true;
    }

    private bool CompareUsingMatcher(string? setValue, Matcher matcher)
    {
        if (!matcher.Valid)
        {
            return false;
        }

        float v = VariantParse.Atof(setValue);
        if (setValue is not null && setValue.StartsWith('['))
        {
            v = LookupEnumeration(setValue, out _);
        }

        int minmaxcount = 0;

        if (matcher.UseMin)
        {
            if (matcher.MinEquals)
            {
                if (v < matcher.MinVal)
                {
                    return false;
                }
            }
            else
            {
                if (v <= matcher.MinVal)
                {
                    return false;
                }
            }

            minmaxcount++;
        }

        if (matcher.UseMax)
        {
            if (matcher.MaxEquals)
            {
                if (v > matcher.MaxVal)
                {
                    return false;
                }
            }
            else
            {
                if (v >= matcher.MaxVal)
                {
                    return false;
                }
            }

            minmaxcount++;
        }

        if (minmaxcount >= 1)
        {
            return true;
        }

        if (matcher.NotEqual)
        {
            if (matcher.IsNumeric)
            {
                if (v == VariantParse.Atof(matcher.Token))
                {
                    return false;
                }
            }
            else
            {
                if (string.Equals(setValue, matcher.Token, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        if (matcher.IsNumeric)
        {
            // an empty set value is a missing key: never matches "0"
            if (string.IsNullOrEmpty(setValue))
            {
                return false;
            }

            return v == VariantParse.Atof(matcher.Token);
        }

        return string.Equals(setValue, matcher.Token, StringComparison.OrdinalIgnoreCase);
    }

    private bool Compare(string? setValue, Criterion criterion)
        => CompareUsingMatcher(setValue, criterion.Matcher);

    private float ScoreCriteriaAgainstRuleCriteria(ResponseCriteriaSet set, int criterionIndex, out bool exclude)
    {
        Criterion criterion = _criteria[criterionIndex];

        if (criterion.IsSubCriteriaType)
        {
            float subScore = 0f;
            foreach (int sub in criterion.SubCriteria)
            {
                subScore += ScoreCriteriaAgainstRuleCriteria(set, sub, out bool _);
            }

            exclude = criterion.Required && subScore == 0f;
            return subScore * criterion.Weight;
        }

        exclude = false;

        float score = 0f;

        string actualValue = string.Empty;

        int found = set.Find(criterion.Name);
        if (found != -1)
        {
            actualValue = set.GetValue(found);
        }

        if (Compare(actualValue, criterion))
        {
            // the GetWeight of a missing index is 1.0, stock's reachable default
            score = set.GetWeight(found) * criterion.Weight;
        }
        else if (criterion.Required)
        {
            exclude = true;
        }

        return score;
    }

    private float ScoreCriteriaAgainstRule(ResponseCriteriaSet set, int ruleIndex)
    {
        Rule rule = _rules[ruleIndex];

        if (!rule.Enabled)
        {
            return 0f;
        }

        float score = 0f;

        foreach (int criterionIndex in rule.Criteria)
        {
            score += ScoreCriteriaAgainstRuleCriteria(set, criterionIndex, out bool exclude);

            if (exclude)
            {
                return 0f;
            }
        }

        return score;
    }

    // ------------------------------------------------------------------
    // selection
    // ------------------------------------------------------------------

    private int SelectWeightedResponseFromGroup(ResponseGroup group, IResponseFilter? filter)
    {
        int count = group.Entries.Count;
        if (count == 0)
        {
            return -1;
        }

        // fake-deplete the entries the filter rejects
        List<int> fakedDepletes = new();
        if (filter is not null && group.DepleteBeforeRepeat)
        {
            for (int i = 0; i < count; i++)
            {
                ResponseEntry entry = group.Entries[i];
                if (entry.DepletionCount != group.DepletionCount
                    && !filter.IsValidResponse(entry.Kind, entry.Value))
                {
                    fakedDepletes.Add(i);
                    group.MarkResponseUsed(i);
                }
            }
        }

        if (!group.HasUndepletedChoices)
        {
            group.ResetDepletionCount();

            if (filter is not null && group.DepleteBeforeRepeat)
            {
                fakedDepletes.Clear();
                for (int i = 0; i < count; i++)
                {
                    ResponseEntry entry = group.Entries[i];
                    if (!filter.IsValidResponse(entry.Kind, entry.Value))
                    {
                        fakedDepletes.Add(i);
                        group.MarkResponseUsed(i);
                    }
                }
            }

            if (!group.HasUndepletedChoices)
            {
                RevertFakeDepletes(group, fakedDepletes);

                // the group has looped all the way through
                if (group.NoRepeat)
                {
                    group.Enabled = false;
                }

                return -1;
            }
        }

        bool checkRepeats = group.DepleteBeforeRepeat;
        int depletionCount = group.DepletionCount;

        int slot = -1;

        if (checkRepeats)
        {
            // snag the first slot right away
            if (group.HasUndepletedFirst(out int first) && first != -1)
            {
                slot = first;
            }

            if (slot == -1 && group.HasUndepletedLast(out int last) && last != -1)
            {
                // the last slot only if no other undepleted entry remains
                int i;
                for (i = 0; i < count; i++)
                {
                    ResponseEntry entry = group.Entries[i];
                    if (entry.DepletionCount == depletionCount)
                    {
                        continue;
                    }

                    if (entry.Last)
                    {
                        continue;
                    }

                    break;
                }

                if (i >= count)
                {
                    slot = last;
                }
            }
        }

        if (slot == -1)
        {
            float totalWeight = 0f;

            for (int i = 0; i < count; i++)
            {
                ResponseEntry entry = group.Entries[i];
                if (checkRepeats && entry.DepletionCount == depletionCount)
                {
                    continue;
                }

                // the last entry is dealt with above
                if (checkRepeats && entry.Last)
                {
                    continue;
                }

                int prevSlot = slot;

                if (totalWeight == 0f)
                {
                    slot = i;
                }

                // always assume the very first slot will match
                totalWeight += entry.Weight;
                if (totalWeight == 0f || _random.RandomFloat(0, totalWeight) < entry.Weight)
                {
                    slot = i;
                }

                if (!checkRepeats && slot != prevSlot && filter is not null
                    && !filter.IsValidResponse(entry.Kind, entry.Value))
                {
                    slot = prevSlot;
                    totalWeight -= entry.Weight;
                }
            }
        }

        if (slot != -1)
        {
            group.MarkResponseUsed(slot);
        }

        RevertFakeDepletes(group, fakedDepletes);

        return slot;
    }

    private static void RevertFakeDepletes(ResponseGroup group, List<int> fakedDepletes)
    {
        foreach (int i in fakedDepletes)
        {
            group.Entries[i].DepletionCount = 0;
        }
    }

    /// <summary>
    /// The sequential walk both entry points share: advance, wrap unless
    /// norepeat, and take the first entry the filter accepts.
    /// </summary>
    private bool SelectSequential(ResponseGroup group, IResponseFilter? filter, out int index)
    {
        int count = group.Entries.Count;
        int initial = group.CurrentIndex;

        while (true)
        {
            int current = group.CurrentIndex;
            group.CurrentIndex = (byte)(current + 1);

            if (current >= count)
            {
                if (group.NoRepeat)
                {
                    group.Enabled = false;
                    index = -1;
                    return false;
                }

                current = 0;
                group.CurrentIndex = 0;
            }

            index = current;

            if (FilterAccepts(group.Entries[index], filter))
            {
                return true;
            }

            if (group.CurrentIndex == initial)
            {
                index = -1;
                return false;
            }
        }
    }

    private static bool FilterAccepts(ResponseEntry entry, IResponseFilter? filter)
        => filter is null || filter.IsValidResponse(entry.Kind, entry.Value);

    /// <summary>
    /// ResolveResponse: walk a reference chain to a concrete entry, honouring
    /// the named group's own sequential or weighted selection at each hop.
    /// </summary>
    private bool ResolveResponse(string name, IResponseFilter? filter, out ResponseEntry? entry, out ResponseGroup? group)
    {
        entry = null;
        group = null;

        if (!_groupIndex.TryGetValue(name, out int index))
        {
            return false;
        }

        ResponseGroup? current = _groups[index];
        if (!current.Enabled || current.Entries.Count == 0)
        {
            return false;
        }

        int entryIndex;
        if (current.Sequential)
        {
            if (!SelectSequential(current, filter, out entryIndex))
            {
                return false;
            }
        }
        else
        {
            entryIndex = SelectWeightedResponseFromGroup(current, filter);
            if (entryIndex < 0)
            {
                return false;
            }
        }

        ResponseEntry selected = current.Entries[entryIndex];
        if (selected.Kind == ResponseKind.Reference)
        {
            return ResolveResponse(selected.Value, filter, out entry, out group);
        }

        entry = selected;
        group = current;
        return true;
    }

    /// <summary>GetBestResponse: pick one of the rule's response groups at random, then select from it.</summary>
    private bool GetBestResponse(Rule rule, IResponseFilter? filter, out ResponseEntry? entry, out ResponseGroup? group)
    {
        entry = null;
        group = null;

        int count = rule.Responses.Count;
        if (count == 0)
        {
            return false;
        }

        int index = _random.RandomInt(0, count - 1);
        ResponseGroup chosen = _groups[rule.Responses[index]];

        if (!chosen.Enabled || chosen.Entries.Count == 0)
        {
            return false;
        }

        int entryIndex;
        if (chosen.Sequential)
        {
            if (!SelectSequential(chosen, filter, out entryIndex))
            {
                return false;
            }
        }
        else
        {
            entryIndex = SelectWeightedResponseFromGroup(chosen, filter);
            if (entryIndex < 0)
            {
                return false;
            }
        }

        ResponseEntry selected = chosen.Entries[entryIndex];
        if (selected.Kind == ResponseKind.Reference)
        {
            return ResolveResponse(selected.Value, filter, out entry, out group);
        }

        entry = selected;
        group = chosen;
        return true;
    }

    private int FindBestMatchingRule(ResponseCriteriaSet set)
    {
        // stock's floor: a zero-score rule never matches
        float bestScore = 0.001f;
        List<int> best = new();

        for (int i = 0; i < _rules.Count; i++)
        {
            float score = ScoreCriteriaAgainstRule(set, i);
            if (score < bestScore)
            {
                continue;
            }

            if (score != bestScore)
            {
                bestScore = score;
                best.Clear();
            }

            best.Add(i);
        }

        if (best.Count == 0)
        {
            return -1;
        }

        if (best.Count == 1)
        {
            return best[0];
        }

        return best[_random.RandomInt(0, best.Count - 1)];
    }

    /// <summary>
    /// FindBestResponse: TRUE whenever a rule matched, even when its response
     /// selection failed — the response is then kind None with an empty name,
     /// as stock's valid flag leaves it.
    /// </summary>
    public bool FindBestResponse(ResponseCriteriaSet set, out MatchedResponse response, IResponseFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(set);

        int bestRule = FindBestMatchingRule(set);

        ResponseKind kind = ResponseKind.None;
        string name = string.Empty;
        ResponseParams parameters = new();
        string ruleName = string.Empty;
        string? context = null;
        bool applyContextToWorld = false;
        bool valid = false;

        if (bestRule != -1)
        {
            Rule rule = _rules[bestRule];

            if (GetBestResponse(rule, filter, out ResponseEntry? entry, out ResponseGroup? group))
            {
                kind = entry!.Kind;
                name = Truncate(entry.Value, 63);
                parameters = group!.Params;
            }

            ruleName = Truncate(rule.Name, 63);

            if (rule.MatchOnce)
            {
                rule.Enabled = false;
            }

            context = rule.Context;
            applyContextToWorld = rule.ApplyContextToWorld;

            valid = true;
        }

        response = new MatchedResponse
        {
            Kind = kind,
            Name = name,
            Rule = ruleName,
            Params = parameters,
            Context = context,
            ApplyContextToWorld = applyContextToWorld,
            Criteria = set,
        };

        return valid;
    }

    /// <summary>AI_Response::Init's V_strcpy_safe into char[64].</summary>
    private static string Truncate(string text, int length)
        => text.Length <= length ? text : text[..length];
}
