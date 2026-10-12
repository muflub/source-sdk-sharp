//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server-browser tags this game's convars put in <c>sv_tags</c> —
/// plan_server_novalve.md P3, the last <c>KeyValues</c> server.so built.
///
/// <para>
/// <b>A TAG IS DECLARED ON ITS CONVAR:</b>
/// <c>[ConVar("sv_gravity", "800", Tag = "gravity")]</c>. While the convar's
/// value differs from its default (compared as text) the tag is in
/// <c>sv_tags</c>; at the default it is not. That is the rule the engine
/// applied to the (convar, tag) list it used to ask the game for through
/// <c>IServerGameTags::GetTaggedConVarList</c> — a <c>KeyValues</c> tree the
/// game built and the engine walked and freed, which needed tier1's
/// <c>KeyValues</c> and vstdlib's symbol table in server.so. That list is empty
/// now (stock <c>CGameRules</c>' own answer), so the engine's pass only ever
/// touches its own tags and never one of these.
/// </para>
///
/// <para>
/// <b>PUSHED ON CHANGE.</b> <see cref="ConVarRegistry.TaggedChanged"/> fires
/// from the engine's change callback, and every tag is re-derived then, as the
/// engine re-derived its list on a change. Construction applies them once,
/// because a convar can come up off its default (config.cfg) without a change
/// ever being announced. One divergence: an operator who deletes one of these
/// tags from <c>sv_tags</c> by hand keeps it deleted until its convar next
/// changes, where the engine would have put it back on any notify change.
/// </para>
///
/// <para>
/// Stock also listed <c>sv_alltalk</c>. This mod declares no such convar, and
/// the engine skipped a name it could not find, so it never tagged anything.
/// </para>
/// </summary>
public sealed class ServerTags
{
    /// <summary>The engine's cap on the tag string, terminator included.</summary>
    public const int MaxTagStringLength = 128;

    private readonly IEngineConsole _console;
    private readonly ConVarRegistry _convars;

    public ServerTags(IEngineConsole console, ConVarRegistry convars)
    {
        _console = console;
        _convars = convars;
        _convars.TaggedChanged += OnTaggedChanged;
        Apply();
    }

    /// <summary>Stops listening. The registry and this die together; this is for tests and reloads.</summary>
    public void Detach() => _convars.TaggedChanged -= OnTaggedChanged;

    private void OnTaggedChanged(ConVarBase changed) => Apply();

    /// <summary>
    /// Re-derives every declared tag into <c>sv_tags</c>. Writes only when the
    /// string changes, and does not read <c>sv_tags</c> when nothing is tagged.
    /// </summary>
    public void Apply()
    {
        var tagged = _convars.All.Where(c => c.Tag is not null).ToList();
        if (tagged.Count == 0)
            return;

        string? before = _console.FindConVarValue("sv_tags");
        if (before is null)
            return;

        string after = Derive(before, tagged);
        if (!string.Equals(after, before, StringComparison.Ordinal))
            _console.SetConVarValueByName("sv_tags", after);
    }

    /// <summary>The tag string after each tagged convar's rule is applied, in declaration order.</summary>
    public static string Derive(string tags, IEnumerable<ConVarBase> declared)
    {
        foreach (var convar in declared)
        {
            if (convar.Tag is not { } tag)
                continue;

            tags = string.Equals(convar.RawValue, convar.DefaultValue, StringComparison.Ordinal)
                ? Remove(tags, tag)
                : Add(tags, tag);
        }
        return tags;
    }

    /// <summary>
    /// Puts <paramref name="tag"/> at the FRONT, followed by a comma and the
    /// old string, unless a tag equal to it ignoring case is already there.
    /// </summary>
    public static string Add(string tags, string tag)
    {
        if (Split(tags).Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            return tags;

        return Cap(tag + "," + tags);
    }

    /// <summary>
    /// Every other tag, each followed by a comma, in order; the string is left
    /// alone when <paramref name="tag"/> (ignoring case) is not in it.
    /// </summary>
    public static string Remove(string tags, string tag)
    {
        if (tags.Length == 0)
            return tags;

        var kept = new StringBuilder();
        bool found = false;
        foreach (string t in Split(tags))
        {
            if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                found = true;
            else
                kept.Append(t).Append(',');
        }

        return found ? Cap(kept.ToString()) : tags;
    }

    // The engine's split drops empty pieces.
    private static string[] Split(string tags)
        => tags.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static string Cap(string s)
        => s.Length < MaxTagStringLength ? s : s.Substring(0, MaxTagStringLength - 1);
}
