//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// An options script (stock's CDescription over CScriptObjects,
/// common/GameUI/scriptobject.cpp): "VERSION 1.0", "DESCRIPTION &lt;type&gt;",
/// then a brace of entries, each <c>"cvar" { "prompt" ["tooltip"] { TYPE ...
/// } { "default" } [SetInfo] }</c>. A second script read over the first
/// changes only the defaults of entries it names and adds the ones it does
/// not; an entry of an unknown type ends the read, keeping what came before
/// it, as stock's did. The types are stock's (BOOL, NUMBER, LIST, STRING,
/// OBSOLETE, SLIDER, CATEGORY) and BUTTON, whose default is a command.
/// </summary>
public sealed class OptionsScript(string descriptionType)
{
    private sealed class Entry
    {
        public string Name = "";
        public string Prompt = "";
        public string? Tooltip;
        public UiScriptKind Kind;
        public bool Obsolete;
        public float Min, Max;
        public List<UiScriptItem> Items = [];
        public string Value = "";
        public bool SetInfo;
    }

    private readonly List<Entry> _entries = [];

    /// <summary>The entries, obsolete ones left out, with their current values.</summary>
    public List<UiScriptEntry> Entries()
    {
        var list = new List<UiScriptEntry>(_entries.Count);
        foreach (var e in _entries)
            if (!e.Obsolete)
                list.Add(new UiScriptEntry(e.Name, e.Prompt, e.Tooltip, e.Kind, e.Min, e.Max, e.Items, e.Value, e.SetInfo));
        return list;
    }

    /// <summary>Stock's TransferCurrentValues: a convar's value, when it has one, becomes the entry's.</summary>
    public void TakeCurrentValues(Func<string, string?> convar)
    {
        foreach (var e in _entries)
            if (convar(e.Name) is { Length: > 0 } value)
                e.Value = value;
    }

    /// <summary>One script read over what is held; false when it stopped early or was not a script of this type.</summary>
    public bool Read(byte[] data)
    {
        int at = 0;
        if (!Next(data, ref at, out string t) || !t.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Next(data, ref at, out t) || ParseFloat(t) != 1f)
            return false;
        if (!Next(data, ref at, out t) || !t.Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Next(data, ref at, out t) || !t.Equals(descriptionType, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Next(data, ref at, out t) || t != "{")
            return false;
        while (true)
        {
            int start = at;
            if (!Next(data, ref at, out t))
                return false;
            if (t == "}")
                return true;
            at = start;
            var held = _entries.Find(e => e.Name.Equals(t, StringComparison.OrdinalIgnoreCase));
            var entry = held ?? new Entry();
            if (!ReadEntry(data, ref at, entry, held is null))
                return false;
            if (held is null)
                _entries.Add(entry);
        }
    }

    // CScriptObject::ReadFromBuffer. A held entry (isNew false) keeps its
    // prompt, type, range and choices and takes only the new default.
    private static bool ReadEntry(byte[] data, ref int at, Entry e, bool isNew)
    {
        if (!Next(data, ref at, out string t))
            return false;
        if (isNew)
            e.Name = t;
        if (!Next(data, ref at, out t) || t != "{")
            return false;
        if (!Next(data, ref at, out t))
            return false;
        if (isNew)
            e.Prompt = t;
        if (!Next(data, ref at, out t))
            return false;
        if (t != "{")
        {
            if (isNew)
                e.Tooltip = t;
            if (!Next(data, ref at, out t))
                return false;
        }
        if (t != "{" || !Next(data, ref at, out t))
            return false;
        if (!TypeOf(t, out UiScriptKind kind, out bool obsolete))
            return false;
        if (isNew)
        {
            e.Kind = kind;
            e.Obsolete = obsolete;
        }
        if (kind == UiScriptKind.Category)
            return Next(data, ref at, out t) && t == "}" && Next(data, ref at, out t) && t == "}";

        switch (kind)
        {
            case UiScriptKind.Number or UiScriptKind.Slider:
                if (!Next(data, ref at, out t))
                    return false;
                float min = ParseFloat(t);
                if (!Next(data, ref at, out t))
                    return false;
                if (isNew)
                {
                    e.Min = min;
                    e.Max = ParseFloat(t);
                }
                if (!Next(data, ref at, out t) || t != "}")
                    return false;
                break;
            case UiScriptKind.List:
                while (true)
                {
                    if (!Next(data, ref at, out t))
                        return false;
                    if (t == "}")
                        break;
                    string label = t;
                    if (!Next(data, ref at, out t))
                        return false;
                    if (isNew)
                        e.Items.Add(new UiScriptItem(label, t));
                }
                break;
            default:
                if (!Next(data, ref at, out t) || t != "}")
                    return false;
                break;
        }

        if (!Next(data, ref at, out t) || t != "{")
            return false;
        Next(data, ref at, out t);
        e.Value = kind is UiScriptKind.Number or UiScriptKind.Slider or UiScriptKind.Bool ? Trimmed(t) : t;
        if (!Next(data, ref at, out t) || t != "}")
            return false;
        if (!Next(data, ref at, out t))
            return false;
        if (t.Equals("SetInfo", StringComparison.OrdinalIgnoreCase))
        {
            e.SetInfo = true;
            if (!Next(data, ref at, out t))
                return false;
        }
        return t == "}";
    }

    private static bool TypeOf(string word, out UiScriptKind kind, out bool obsolete)
    {
        obsolete = false;
        switch (word.ToUpperInvariant())
        {
            case "BOOL": kind = UiScriptKind.Bool; return true;
            case "NUMBER": kind = UiScriptKind.Number; return true;
            case "LIST": kind = UiScriptKind.List; return true;
            case "STRING": kind = UiScriptKind.String; return true;
            case "OBSOLETE": kind = UiScriptKind.Bool; obsolete = true; return true;
            case "SLIDER": kind = UiScriptKind.Slider; return true;
            case "CATEGORY": kind = UiScriptKind.Category; return true;
            case "BUTTON": kind = UiScriptKind.Button; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>A value as the engine takes it (stock's WriteToConfig): a bool 1 or 0, a number within its range, a list's chosen value or 0.</summary>
    public static string ConfigValue(UiScriptEntry e, string value)
    {
        switch (e.Kind)
        {
            case UiScriptKind.Bool:
                return ParseFloat(value) != 0f ? "1" : "0";
            case UiScriptKind.Number or UiScriptKind.Slider:
                float v = ParseFloat(value);
                if (e.Min != -1f)
                    v = MathF.Max(v, e.Min);
                if (e.Max != -1f)
                    v = MathF.Min(v, e.Max);
                return v.ToString("0.000000", CultureInfo.InvariantCulture);
            case UiScriptKind.List:
                foreach (var item in e.Items)
                    if (item.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return item.Value;
                return "0";
            default:
                return value;
        }
    }

    /// <summary>The console line that sets an entry (setinfo for a SetInfo entry); null for a category or a button.</summary>
    public static string? ConfigLine(UiScriptEntry e, string value)
        => e.Kind is UiScriptKind.Category or UiScriptKind.Button
            ? null
            : (e.SetInfo ? "setinfo " : "") + e.Name + " \"" + Strip(ConfigValue(e, value)) + "\"";

    /// <summary>The script with these values as its defaults (stock's WriteToScriptFile), to be read back as cfg/user.scr.</summary>
    public static string Write(string descriptionType, IReadOnlyList<UiScriptEntry> entries, IReadOnlyList<string> values)
    {
        var s = new StringBuilder();
        s.Append("VERSION 1.0\r\n\r\nDESCRIPTION ").Append(descriptionType).Append("\r\n{\r\n");
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string v = values[i];
            s.Append("\t\"").Append(Strip(e.Name)).Append("\"\r\n\t{\r\n");
            s.Append("\t\t\"").Append(Strip(e.Prompt)).Append("\"\r\n");
            if (e.Tooltip is { Length: > 0 } tip)
                s.Append("\t\t\"").Append(Strip(tip)).Append("\"\r\n");
            switch (e.Kind)
            {
                case UiScriptKind.Bool:
                    s.Append("\t\t{ BOOL }\r\n\t\t{ \"").Append(ParseFloat(v) != 0f ? "1" : "0").Append("\" }\r\n");
                    break;
                case UiScriptKind.Number or UiScriptKind.Slider:
                    s.Append("\t\t{ ").Append(e.Kind == UiScriptKind.Number ? "NUMBER " : "SLIDER ").Append(Clean(e.Min))
                     .Append(' ').Append(Clean(e.Max)).Append(" }\r\n\t\t{ \"").Append(Clean(ParseFloat(v))).Append("\" }\r\n");
                    break;
                case UiScriptKind.String:
                    s.Append("\t\t{ STRING }\r\n\t\t{ \"").Append(Strip(v)).Append("\" }\r\n");
                    break;
                case UiScriptKind.List:
                    s.Append("\t\t{\r\n\t\t\tLIST\r\n");
                    foreach (var item in e.Items)
                        s.Append("\t\t\t\"").Append(Strip(item.Label)).Append("\" \"").Append(Strip(item.Value)).Append("\"\r\n");
                    s.Append("\t\t}\r\n\t\t{ \"").Append(Strip(v)).Append("\" }\r\n");
                    break;
                case UiScriptKind.Button:
                    s.Append("\t\t{ BUTTON }\r\n\t\t{ \"").Append(Strip(v)).Append("\" }\r\n");
                    break;
                case UiScriptKind.Category:
                    s.Append("\t\t{ CATEGORY }\r\n");
                    break;
            }
            if (e.SetInfo)
                s.Append("\t\tSetInfo\r\n");
            s.Append("\t}\r\n\r\n");
        }
        s.Append("}\r\n");
        return s.ToString();
    }

    private static bool Next(byte[] data, ref int at, out string token)
    {
        int next = MapEntityBlock.ParseToken(data, at, out token);
        if (next < 0)
        {
            at = data.Length;
            return false;
        }
        at = next;
        return token.Length > 0;
    }

    private static float ParseFloat(string text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    // StripFloatTrailingZeros / CleanFloat: a number with no trailing zeros or point.
    private static string Trimmed(string text)
        => text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;

    private static string Clean(float v) => Trimmed(v.ToString("0.######", CultureInfo.InvariantCulture));

    // UTIL_StripInvalidCharacters: no quote may break the line.
    private static string Strip(string text) => text.Replace("\"", "", StringComparison.Ordinal);
}

/// <summary>The files an options script lives in, whole: read over every search path, written to the mod's.</summary>
public interface IOptionsFiles
{
    byte[]? ReadAll(string fileName);

    void WriteAll(string fileName, byte[] bytes);
}

/// <summary><see cref="IOptionsFiles"/> over the engine's filesystem: read on GAME, written on MOD, as stock's open did.</summary>
public sealed class EngineOptionsFiles(IFwdBaseFileSystem files) : IOptionsFiles
{
    public byte[]? ReadAll(string fileName) => files.ReadAll(fileName, "GAME");

    public void WriteAll(string fileName, byte[] bytes)
    {
        nint handle = files.Open(fileName, "wb", "MOD");
        if (handle == 0)
            return;
        files.Write(bytes, handle);
        files.Close(handle);
    }
}

/// <summary>
/// An options script over the game's files and convars: its defaults file,
/// then the user's over it, then (when <paramref name="takeCurrentValues"/>)
/// each convar's value. Saved, every entry's line runs and the script is
/// written as the user's file. By default the advanced multiplayer entries
/// (cfg/user_default.scr, cfg/user.scr), which stock's dialog read the
/// convars into.
/// </summary>
public class OptionsScriptSource(IOptionsFiles files, IUiConVarStore convars, IUiCommandLine commands,
                                 string defaultFile = OptionsScriptSource.DefaultFile,
                                 string userFile = OptionsScriptSource.UserFile,
                                 string description = OptionsScriptSource.Description,
                                 bool takeCurrentValues = true)
    : IOptionsScriptSource
{
    public const string DefaultFile = "cfg/user_default.scr", UserFile = "cfg/user.scr", Description = "INFO_OPTIONS";

    public IReadOnlyList<UiScriptEntry> Load()
    {
        var script = new OptionsScript(description);
        if (files.ReadAll(defaultFile) is { } defaults)
            script.Read(defaults);
        if (files.ReadAll(userFile) is { } user)
            script.Read(user);
        if (takeCurrentValues)
            script.TakeCurrentValues(convars.Get);
        return script.Entries();
    }

    public void Save(IReadOnlyList<UiScriptEntry> entries, IReadOnlyList<string> values)
    {
        for (int i = 0; i < entries.Count; i++)
            if (OptionsScript.ConfigLine(entries[i], values[i]) is { } line)
                commands.Submit(line);
        files.WriteAll(userFile, Encoding.UTF8.GetBytes(OptionsScript.Write(description, entries, values)));
    }

    public void Press(UiScriptEntry entry)
    {
        if (entry.Kind == UiScriptKind.Button && entry.Value.Length > 0)
            commands.Submit(entry.Value);
    }
}

/// <summary>
/// The create-server game page's script: cfg/settings_default.scr, then
/// cfg/settings.scr (SERVER_OPTIONS). Stock's gameplay page read only the
/// two files, never the convars' values: the page starts from what was last
/// saved, not from the server last run.
/// </summary>
public sealed class ServerSettingsSource(IOptionsFiles files, IUiConVarStore convars, IUiCommandLine commands)
    : OptionsScriptSource(files, convars, commands, ServerDefaultFile, ServerUserFile, ServerDescription, takeCurrentValues: false),
      IServerSettingsSource
{
    public const string ServerDefaultFile = "cfg/settings_default.scr", ServerUserFile = "cfg/settings.scr",
                        ServerDescription = "SERVER_OPTIONS";
}
