//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The kind of a matched response; mirrors ResponseType_t
/// (game/server/AI_Criteria.h). Response is a reference to another
/// response group by name and is resolved during selection, never surfaced.
/// </summary>
public enum ResponseKind
{
    None = 0,
    Speak,
    Sentence,
    Scene,
    Reference,
    Print,
}

/// <summary>
/// The AI_ResponseParams RG_* bits (game/server/AI_Criteria.h).
/// </summary>
public static class ResponseParamFlags
{
    public const short DelayAfterSpeak = 1 << 0;
    public const short SpeakOnce = 1 << 1;
    public const short Odds = 1 << 2;
    public const short RespeakDelay = 1 << 3;
    public const short SoundLevel = 1 << 4;
    public const short DontUseScene = 1 << 5;
    public const short StopOnNonIdle = 1 << 6;
    public const short WeaponDelay = 1 << 7;
    public const short DelayBeforeSpeak = 1 << 8;
}

/// <summary>
/// A delay interval of a response group; the stock response_interval_t
/// float16 pair (game/server/AI_Criteria.h). Values are half-quantised at
/// parse, so half(float) round trips exactly through here.
/// </summary>
public readonly struct ResponseInterval
{
    public float Start { get; init; }

    public float Range { get; init; }

    public static readonly ResponseInterval Zero = default;
}

/// <summary>
/// The per-response-group playback parameters; AI_ResponseParams
/// (game/server/AI_Criteria.h). Defaults are the stock constructor's:
/// odds 100, sound level 0, every interval zero, no flags.
/// </summary>
public struct ResponseParams
{
    public short Flags;

    public ResponseInterval Delay;

    public ResponseInterval RespeakDelay;

    public ResponseInterval WeaponDelay;

    /// <summary>Stock default 100; the clamped atoi of the odds token.</summary>
    public short Odds = 100;

    public byte SoundLevel;

    public ResponseInterval PreDelay;

    public ResponseParams()
    {
    }

    public readonly bool Has(short flag) => (Flags & flag) != 0;
}

/// <summary>
/// The criteria set a caller offers to <see cref="IResponseRules"/>; the
/// AI_CriteriaSet port (game/server/AI_Criteria.h/.cpp). Append replaces an
/// existing case-insensitive match's value and weight, values are truncated
/// to 63 characters (the stock Q_strncpy into char[64]), lookups are
/// case-insensitive, and a missing criterion weighs 1.0.
/// </summary>
public sealed class ResponseCriteriaSet
{
    private struct Entry
    {
        public string Name;
        public string Value;
        public float Weight;
    }

    private readonly List<Entry> _entries = new();

    private readonly Dictionary<string, int> _lookup = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _entries.Count;

    /// <summary>The value truncated to 63 characters, as AI_CriteriaSet::AppendCriteria does.</summary>
    public void Append(string name, string value, float weight = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(name);
        value ??= string.Empty;
        if (value.Length > 63)
        {
            value = value[..63];
        }

        if (_lookup.TryGetValue(name, out int index))
        {
            Entry entry = _entries[index];
            entry.Value = value;
            entry.Weight = weight;
            _entries[index] = entry;
            return;
        }

        _lookup.Add(name, _entries.Count);
        _entries.Add(new Entry { Name = name, Value = value, Weight = weight });
    }

    /// <summary>The case-insensitive AI_CriteriaSet::FindCriterionIndex; -1 when absent.</summary>
    public int Find(string name)
        => _lookup.TryGetValue(name, out int index) ? index : -1;

    public string GetName(int index) => (uint)index < (uint)_entries.Count ? _entries[index].Name : string.Empty;

    /// <summary>AI_CriteriaSet::GetValue: the empty string when out of range.</summary>
    public string GetValue(int index) => (uint)index < (uint)_entries.Count ? _entries[index].Value : string.Empty;

    /// <summary>AI_CriteriaSet::GetWeight: 1.0 when out of range.</summary>
    public float GetWeight(int index) => (uint)index < (uint)_entries.Count ? _entries[index].Weight : 1.0f;
}

/// <summary>
/// The IResponseFilter a caller may hang on a search
/// (game/server/AI_ResponseSystem.h); the selection asks it whether a
/// response's value is currently speakable.
/// </summary>
public interface IResponseFilter
{
    bool IsValidResponse(ResponseKind kind, string value);
}

/// <summary>
/// What a successful search hands back; the AI_Response the caller consumes
/// (game/server/AI_Criteria.h). The name and rule are truncated to 63
/// characters as AI_Response::Init's V_strcpy_safe into char[64] does.
/// Criteria is the set the search ran against (the stock copy is elided:
/// the search is synchronous and the caller owns the set).
/// </summary>
public sealed class MatchedResponse
{
    public ResponseKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;

    public ResponseParams Params { get; init; }

    public string? Context { get; init; }

    public bool ApplyContextToWorld { get; init; }

    public ResponseCriteriaSet? Criteria { get; init; }
}

/// <summary>
/// The response rule search; CResponseSystem::FindBestResponse
/// (game/server/AI_ResponseSystem.cpp). Returns false with a
/// kind-None response when nothing matched; a matched rule whose response
/// selection failed still returns true with kind None, as the stock
/// valid-flag does.
/// </summary>
public interface IResponseRules
{
    bool FindBestResponse(ResponseCriteriaSet criteria, out MatchedResponse response, IResponseFilter? filter = null);
}
