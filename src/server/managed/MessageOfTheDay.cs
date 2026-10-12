//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using System;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>The file the HTML message of the day is read from.</summary>
[ConVar("motdfile", "motd.txt", Help = "The MOTD file to load.")]
public sealed class MotdFileConVar : StringConVar
{
    protected override void OnChanged(string oldValue, string newValue)
        => MessageOfTheDay.ResetIfInvalid(this, newValue);
}

/// <summary>The file the text-only message of the day is read from.</summary>
[ConVar("motdfile_text", "motd_text.txt",
        Help = "The text-only MOTD file to use for clients that have disabled HTML MOTDs.")]
public sealed class MotdTextFileConVar : StringConVar
{
    protected override void OnChanged(string oldValue, string newValue)
        => MessageOfTheDay.ResetIfInvalid(this, newValue);
}

/// <summary>Where the message of the day is written: the InfoPanel string table.</summary>
public interface IInfoPanel
{
    /// <summary>
    /// Writes <paramref name="data"/> as <paramref name="name"/>'s user data.
    /// False when the table is not there or would not take it.
    /// </summary>
    bool Set(string name, ReadOnlySpan<byte> data);
}

/// <summary><see cref="IInfoPanel"/> over the engine's own InfoPanel table.</summary>
public sealed class InfoPanel : IInfoPanel
{
    /// <summary>The table's name, as the engine creates it (shell_stringtables.cpp).</summary>
    public const string TableName = "InfoPanel";

    private readonly INetworkStringTableDirectory _tables;

    public InfoPanel(INetworkStringTableDirectory tables) => _tables = tables;

    public bool Set(string name, ReadOnlySpan<byte> data)
        => _tables.Find(TableName) is { } table
           && table.AddString(name, data) != INetworkStringTableRef.InvalidStringIndex;
}

/// <summary>
/// The message of the day, read in C# and written to the InfoPanel string table
/// with the file as the string's user data -- stock's
/// CServerGameDLL::LoadMessageOfTheDay and LoadSpecificMOTDMsg
/// (gameinterface.cpp:2059-2110), which the native shell stopped running when
/// gameinterface.cpp left the build (c253ff5 retired its empty remains).
///
/// <para>
/// For each of the two convars: the file is looked for as <c>cfg/&lt;name&gt;</c>,
/// then as <c>&lt;name&gt;</c> at the root, and, when the convar still holds its
/// default, as the preferred name with <c>.txt</c> turned into
/// <c>_default.txt</c> -- which is what a stock install ships
/// (hl2mp/cfg/motd_default.txt). A file over 2048 bytes is refused. The bytes
/// go out exactly as read, with a terminating NUL, as stock's PutChar( 0 ).
/// </para>
/// </summary>
public sealed class MessageOfTheDay
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>The largest file stock accepts.</summary>
    public const int MaxBytes = 2048;

    private readonly MotdFileConVar _html;
    private readonly MotdTextFileConVar _text;
    private readonly IEngineFiles _files;
    private readonly IInfoPanel _panel;

    public MessageOfTheDay(ILog log, MotdFileConVar html, MotdTextFileConVar text, IEngineFiles files, IInfoPanel panel)
    {

        _log = log;
        _html = html;
        _text = text;
        _files = files;
        _panel = panel;
    }

    /// <summary>Both messages, in stock's order.</summary>
    public void Load()
    {
        LoadOne(_html.Value, _html.DefaultValue, "motd");
        LoadOne(_text.Value, _text.DefaultValue, "motd_text");
    }

    /// <summary>LoadSpecificMOTDMsg. Answers whether the table took it.</summary>
    internal bool LoadOne(string value, string defaultValue, string stringName)
    {
        string preferred = "cfg/" + value;
        string resolved = preferred;
        byte[]? bytes = _files.ReadGameFileBytes(resolved);

        // Not found? The root, which is where it used to go.
        if (bytes is null)
        {
            resolved = value;
            bytes = _files.ReadGameFileBytes(resolved);
        }

        // Still not? The shipped default, but only for the default name.
        if (bytes is null && string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase))
        {
            int dotTxt = preferred.IndexOf(".txt", StringComparison.OrdinalIgnoreCase);

            if (dotTxt >= 0)
            {
                resolved = preferred.Substring(0, dotTxt) + "_default.txt";
                bytes = _files.ReadGameFileBytes(resolved);
            }
        }

        if (bytes is null)
        {
            _log.Msg($"'{preferred}' not found; not loaded\n");
            return false;
        }

        if (bytes.Length > MaxBytes)
        {
            _log.Warning($"'{resolved}' is too big; not loaded\n");
            return false;
        }

        var data = new byte[bytes.Length + 1];
        bytes.CopyTo(data, 0);

        _log.Msg(string.Equals(preferred, resolved, StringComparison.OrdinalIgnoreCase)
            ? $"Set {stringName} from file '{resolved}'\n"
            : $"Set {stringName} from file '{resolved}'.  ('{preferred}' was not found.)\n");

        if (_panel.Set(stringName, data))
            return true;

        _log.Warning($"[SourceSharp] the InfoPanel table did not take '{stringName}'\n");
        return false;
    }

    /// <summary>
    /// Stock's IsValidPath: not empty, not absolute, and no "..", so a convar
    /// cannot point the loader outside the game's search paths.
    /// </summary>
    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // Q_IsAbsolutePath: a leading slash of either kind, or a drive letter.
        bool absolute = path[0] == '/' || path[0] == '\\' || (path.Length > 1 && path[1] == ':');

        return !absolute && path.IndexOf("..", StringComparison.Ordinal) < 0;
    }

    /// <summary>ValidateMOTDFilename: an invalid value goes back to the default.</summary>
    internal static void ResetIfInvalid(StringConVar convar, string value)
    {
        if (!IsValidPath(value))
            convar.SetValue(convar.DefaultValue);
    }
}
