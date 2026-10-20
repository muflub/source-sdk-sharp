//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>One key of a "break" block, in the order the text gives it.</summary>
public readonly record struct BreakKey(string Key, string Value);

/// <summary>
/// A break block resolved against the caller's two defaults: everything
/// <c>CBreakParser</c> (props_shared.cpp, ported in shell_propdata_system.cpp)
/// writes into a <c>breakmodel_t</c> that the piece output reads.
/// </summary>
public readonly record struct BreakModelFields(
    string ModelName, Vector Offset, Vector Velocity, float Health, float FadeTime,
    float FadeMinDist, float FadeMaxDist, float BurstScale, CollisionGroup CollisionGroup,
    bool IsRagdoll, bool IsMotionDisabled, int MultiplayerBreakMode);

/// <summary>
/// The "break" blocks of a model's collision keyvalue text, parsed in C#
/// (the user's call, 2026-09-29; plan_server_novalve.md).
///
/// <para>
/// <b>THE TEXT</b> is <see cref="IFwdModelInfo.CollisionKeyValueText"/>'s: the
/// .phy's keyvalues, blocks of <c>name { "key" "value" ... }</c> at the top
/// level. Every block is kept or skipped whole; only blocks named "break"
/// (compared case-insensitively, as <c>strcmpi</c> did) are returned.
/// </para>
///
/// <para>
/// <b>WHY THE KEYS ARE KEPT, NOT RESOLVED.</b> Two of a block's fields depend on
/// the caller: the burst scale and the collision group start from defaults the
/// caller passes, and <c>fadetime</c> sets the group to debris only when no
/// <c>debris</c> key came before it. So the parse keeps each block's keys in
/// order and <see cref="Resolve"/> replays them against the defaults, exactly as
/// <c>CBreakParser::SetDefaults</c> then <c>ParseKeyValue</c> per key did.
/// </para>
/// </summary>
public static class BreakModelText
{
    /// <summary>The block name the break list is built from.</summary>
    public const string BreakBlock = "break";

    // breakmodel_t::modelName is char[512].
    private const int ModelNameBytes = 512;

    /// <summary>
    /// Every block named <paramref name="blockName"/> (case-insensitive), each as
    /// its keys in text order. Empty for null or empty text.
    /// </summary>
    public static List<IReadOnlyList<BreakKey>> Blocks(string? text, string blockName = BreakBlock)
    {
        var blocks = new List<IReadOnlyList<BreakKey>>();

        if (string.IsNullOrEmpty(text))
            return blocks;

        var tokens = new Tokenizer(text);

        while (tokens.Next(out string name, out bool nameQuoted))
        {
            if (!nameQuoted && name == "}")
                continue;   // a stray close: nothing to attach it to

            // The block's opening brace. A name followed by anything else is not
            // a block; it is skipped as a lone token.
            if (!tokens.Next(out string open, out bool openQuoted) || openQuoted || open != "{")
                continue;

            bool keep = string.Equals(name, blockName, StringComparison.OrdinalIgnoreCase);
            var keys = keep ? new List<BreakKey>() : null;

            while (tokens.Next(out string key, out bool keyQuoted))
            {
                if (!keyQuoted && key == "}")
                    break;

                if (!keyQuoted && key == "{")
                {
                    tokens.SkipBlock();   // a nested block: not a key
                    continue;
                }

                if (!tokens.Next(out string value, out bool valueQuoted))
                    break;

                if (!valueQuoted && value == "{")
                {
                    tokens.SkipBlock();
                    continue;
                }

                keys?.Add(new BreakKey(key, value));
            }

            if (keys is not null)
                blocks.Add(keys);
        }

        return blocks;
    }

    /// <summary>
    /// One block's fields against the caller's defaults. Keys are compared
    /// case-insensitively; an unknown key is ignored; a repeated key's last
    /// value wins, except where stock's own rule reads an earlier one (the
    /// group: fadetime makes it debris only before any debris key).
    /// </summary>
    public static BreakModelFields Resolve(IReadOnlyList<BreakKey> keys,
                                           float defaultBurstScale, CollisionGroup defaultCollisionGroup)
    {
        // CBreakParser::SetDefaults.
        string modelName = string.Empty;
        Vector offset = Vector.Zero, velocity = Vector.Zero;
        float health = 1f, fadeTime = 20f, fadeMin = 0f, fadeMax = 0f;
        float burst = defaultBurstScale;
        CollisionGroup group = defaultCollisionGroup;
        bool ragdoll = false, motionDisabled = false, wroteGroup = false;
        int mpBreak = 0;   // MULTIPLAYER_BREAK_DEFAULT

        foreach (BreakKey kv in keys)
        {
            string key = kv.Key, value = kv.Value;

            if (Is(key, "model"))
                modelName = FixupModelName(value);
            else if (Is(key, "ragdoll"))
            {
                modelName = FixupModelName(value);
                ragdoll = true;
            }
            else if (Is(key, "motiondisabled"))
                motionDisabled = true;
            else if (Is(key, "offset"))
                offset = StringToVector(value);
            else if (Is(key, "health"))
                health = VariantParse.Atof(value);
            else if (Is(key, "fadetime"))
            {
                fadeTime = VariantParse.Atof(value);
                if (!wroteGroup)
                    group = CollisionGroup.Debris;
            }
            else if (Is(key, "fademindist"))
                fadeMin = VariantParse.Atof(value);
            else if (Is(key, "fademaxdist"))
                fadeMax = VariantParse.Atof(value);
            else if (Is(key, "debris"))
            {
                group = VariantParse.Atoi(value) > 0 ? CollisionGroup.Debris : CollisionGroup.Interactive;
                wroteGroup = true;
            }
            else if (Is(key, "burst"))
                burst = VariantParse.Atof(value);
            else if (Is(key, "multiplayer_break"))
            {
                // FStrEq: case-insensitive whole string.
                if (Is(value, "server"))
                    mpBreak = 1;   // MULTIPLAYER_BREAK_SERVERSIDE
                else if (Is(value, "client"))
                    mpBreak = 2;   // MULTIPLAYER_BREAK_CLIENTSIDE
            }
            else if (Is(key, "velocity"))
                velocity = StringToVector(value);

            // placementbone / placementattachment are parsed by stock into
            // breakmodel_t::placementName, which nothing in the piece output
            // reads: placement is the model's "placementOrigin" attachment.
        }

        return new BreakModelFields(modelName, offset, velocity, health, fadeTime, fadeMin, fadeMax,
                                    burst, group, ragdoll, motionDisabled, mpBreak);
    }

    /// <summary>
    /// FixupModelName: "models/" in front unless it is already there, ".mdl"
    /// behind unless it is already there (both compared case-insensitively),
    /// within breakmodel_t's 512-byte name.
    /// </summary>
    public static string FixupModelName(string value)
    {
        string name = value.Length > 1023 ? value.Substring(0, 1023) : value;   // tmp[1024]

        if (!name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            name = "models/" + name;

        if (name.Length > ModelNameBytes - 1)
            name = name.Substring(0, ModelNameBytes - 1);

        if (name.Length < 4 || !name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            int room = ModelNameBytes - 1 - name.Length;
            name += ".mdl".Substring(0, Math.Max(0, Math.Min(4, room)));
        }

        return name;
    }

    /// <summary>
    /// UTIL_StringToFloatArray( v, 3, text ): atof of each whitespace-separated
    /// token in turn, and every component the text runs out before is ZERO (the
    /// `for ( j++; j < count; j++ ) pVector[j] = 0;` after the loop). The text
    /// is first cut to 127 characters (tempString[128]).
    /// </summary>
    public static Vector StringToVector(string text)
    {
        if (text.Length > 127)
            text = text.Substring(0, 127);

        Span<float> v = stackalloc float[3];
        int at = 0;

        for (int j = 0; j < 3; j++)
        {
            v[j] = VariantParse.Atof(text.Substring(at));

            while (at < text.Length && text[at] <= ' ')
                at++;
            while (at < text.Length && text[at] > ' ')
                at++;

            if (at >= text.Length)
                break;   // the rest of v stays zero

            at++;   // past the separator, as pstr++ did
        }

        return new Vector(v[0], v[1], v[2]);
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Quoted strings (no escapes), braces as their own tokens, and bare words
    // ended by whitespace, a quote or a brace. EVERY TOKEN IS LOWERCASED (ASCII
    // only, as C's tolower): VPhysics' key parser hands its handler lowercased
    // text, found by the live oracle -- wood_crate001a.phy names
    // "wood_crate001a_Chunk01" and the native break list answered
    // "..._chunk01".
    private ref struct Tokenizer
    {
        private readonly ReadOnlySpan<char> _text;
        private int _at;

        public Tokenizer(string text)
        {
            _text = text;
            _at = 0;
        }

        public bool Next(out string token, out bool quoted)
        {
            token = string.Empty;
            quoted = false;

            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                _at++;

            if (_at >= _text.Length)
                return false;

            char c = _text[_at];

            if (c == '{' || c == '}')
            {
                _at++;
                token = c.ToString();
                return true;
            }

            if (c == '"')
            {
                int start = ++_at;
                while (_at < _text.Length && _text[_at] != '"')
                    _at++;
                token = Lower(_text.Slice(start, _at - start));
                if (_at < _text.Length)
                    _at++;
                quoted = true;
                return true;
            }

            int begin = _at;
            while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]) &&
                   _text[_at] != '"' && _text[_at] != '{' && _text[_at] != '}')
                _at++;
            token = Lower(_text.Slice(begin, _at - begin));
            return true;
        }

        private static string Lower(ReadOnlySpan<char> text)
        {
            Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                buffer[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
            }
            return buffer.ToString();
        }

        public void SkipBlock()
        {
            int depth = 1;
            while (depth > 0 && Next(out string t, out bool q))
            {
                if (!q && t == "{")
                    depth++;
                else if (!q && t == "}")
                    depth--;
            }
        }
    }
}
