//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>KeyValues' data types, as <c>KeyValues::types_t</c> numbers them.</summary>
public enum ProxyKeyType
{
    None = 0,
    String = 1,
    Int = 2,
    Float = 3,
    Ptr = 4,
    WString = 5,
    Color = 6,
    UInt64 = 7,
}

/// <summary>
/// One key of a proxy's KeyValues block, with <c>tier1/KeyValues.cpp</c>'s
/// accessors' answers. Native hands each key RAW (the string of a STRING key,
/// the number of an INT or FLOAT one, plus what <c>sscanf "%f"</c>, <c>atof</c>
/// and <c>atoi</c> read from a string) because <see cref="GetString"/> is not a
/// read: on an INT or FLOAT key it formats the number (<c>"%d"</c>,
/// <c>"%f"</c>) and stores it back as a STRING, so every later read of that key
/// sees the text. This replays that.
/// </summary>
public sealed class ProxyKey
{
    public ProxyKey(string name, ProxyKeyType type, string text, float number, int integer,
                    int scanCount, float scanValue, float atof, int atoi)
    {
        Name = name;
        Type = type;
        Text = text;
        Number = number;
        Integer = integer;
        ScanCount = scanCount;
        ScanValue = scanValue;
        Atof = atof;
        Atoi = atoi;
    }

    public string Name { get; }

    public ProxyKeyType Type { get; private set; }

    /// <summary>A STRING key's text.</summary>
    public string Text { get; private set; }

    /// <summary>A FLOAT key's value.</summary>
    public float Number { get; }

    /// <summary>An INT key's value.</summary>
    public int Integer { get; }

    /// <summary>A STRING key's <c>sscanf( text, "%f" )</c>: how many it read, and what.</summary>
    public int ScanCount { get; private set; }

    public float ScanValue { get; private set; }

    /// <summary>A STRING key's <c>(float)atof( text )</c> and <c>atoi( text )</c>.</summary>
    public float Atof { get; private set; }

    public int Atoi { get; private set; }

    /// <summary>
    /// <c>KeyValues::GetString()</c>: a STRING's text; an INT or FLOAT is
    /// formatted and becomes a STRING; anything else answers the default.
    /// </summary>
    public string GetString(string defaultValue = "")
    {
        switch (Type)
        {
            case ProxyKeyType.String:
                return Text;
            case ProxyKeyType.Float:
                BecomeString(((double)Number).ToString("F6", CultureInfo.InvariantCulture));
                return Text;
            case ProxyKeyType.Int:
                BecomeString(Integer.ToString(CultureInfo.InvariantCulture));
                return Text;
            default:
                return defaultValue;
        }
    }

    /// <summary><c>KeyValues::GetFloat()</c>.</summary>
    public float GetFloat() => Type switch
    {
        ProxyKeyType.String => Atof,
        ProxyKeyType.Float => Number,
        ProxyKeyType.Int => Integer,
        _ => 0f,
    };

    /// <summary><c>KeyValues::GetInt()</c>.</summary>
    public int GetInt() => Type switch
    {
        ProxyKeyType.String => Atoi,
        ProxyKeyType.Float => (int)Number,
        _ => Integer,
    };

    // The text GetString wrote is a plain decimal ("%f" or "%d"), the one
    // shape a C parse and .NET's agree on: float.Parse rounds once to float as
    // strtof does, double.Parse then a cast is atof's double-then-float.
    private void BecomeString(string text)
    {
        Type = ProxyKeyType.String;
        Text = text;
        ScanCount = 1;
        ScanValue = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        Atof = (float)double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        Atoi = int.TryParse(text.Split('.')[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int i) ? i : 0;
    }
}

/// <summary>
/// A proxy's KeyValues block: the keys native handed over, looked up the way
/// KeyValues' symbol table looks them up (case-insensitively), with its
/// by-name accessors' defaults.
/// </summary>
public sealed class ProxyKeyValues
{
    private readonly List<ProxyKey> _keys;

    public ProxyKeyValues(IEnumerable<ProxyKey> keys) => _keys = keys.ToList();

    public IReadOnlyList<ProxyKey> Keys => _keys;

    /// <summary><c>KeyValues::FindKey( name )</c>.</summary>
    public ProxyKey? Find(string name)
    {
        foreach (ProxyKey key in _keys)
        {
            if (string.Equals(key.Name, name, StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    /// <summary><c>KeyValues::GetString( name, "" )</c>.</summary>
    public string GetString(string name) => Find(name)?.GetString() ?? string.Empty;

    /// <summary><c>KeyValues::GetFloat( name, def )</c>.</summary>
    public float GetFloat(string name, float defaultValue = 0f) => Find(name) is { } key ? key.GetFloat() : defaultValue;

    /// <summary><c>KeyValues::GetInt( name, def )</c>.</summary>
    public int GetInt(string name, int defaultValue = 0) => Find(name) is { } key ? key.GetInt() : defaultValue;
}
