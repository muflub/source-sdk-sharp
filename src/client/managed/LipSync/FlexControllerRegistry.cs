//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.LipSync;

/// <summary>
/// The global flex controller names (C_BaseFlex::AddGlobalFlexController): a
/// name's index is where it was first added, matched without regard to ASCII
/// case. Once the registry holds MAXSTUDIOFLEXCTRL * 4 names a new name is not
/// added and gets the index one past the end. Stock keeps one per process;
/// here the owner holds the instance.
/// </summary>
public sealed class FlexControllerRegistry
{
    /// <summary>MAXSTUDIOFLEXCTRL * 4 (studio.h).</summary>
    public const int Capacity = 96 * 4;

    private readonly List<string> _names = [];

    public int Count => _names.Count;

    public int Add(string name)
    {
        for (int i = 0; i < _names.Count; i++)
        {
            if (SameIgnoringAsciiCase(_names[i], name))
                return i;
        }
        int index = _names.Count;
        if (_names.Count < Capacity)
            _names.Add(name);
        return index;
    }

    public string Name(int index) => index >= 0 && index < _names.Count ? _names[index] : "";

    // Q_stricmp's rule: only A-Z fold.
    private static bool SameIgnoringAsciiCase(string a, string b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (Fold(a[i]) != Fold(b[i]))
                return false;
        }
        return true;
    }

    private static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
