//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The keyboard page's rows from the running mod's scripts/kb_act.lst, read off
/// the GAME paths (a VPK's counts), as stock's COptionsSubKeyboard reads them:
/// pairs of command and description, a <c>blank</c> command's description a
/// heading, a description starting <c>=</c> skipped. No file is no rows.
/// </summary>
public sealed class ModKeyActions(IOptionsFiles files) : IUiKeyActions
{
    public const string FileName = "scripts/kb_act.lst";

    public IReadOnlyList<UiKeyAction> Actions()
        => files.ReadAll(FileName) is { } data ? Parse(data) : [];

    public static IReadOnlyList<UiKeyAction> Parse(byte[] data)
    {
        var rows = new List<UiKeyAction>();
        string? section = null;
        int at = 0;
        while (Next(data, ref at, out string command) && Next(data, ref at, out string description))
        {
            if (description[0] == '=')
                continue;
            if (command.Equals("blank", StringComparison.OrdinalIgnoreCase))
                section = description;
            else
                rows.Add(new UiKeyAction(command, description, section));
        }
        return rows;
    }

    private static bool Next(byte[] data, ref int at, out string token)
    {
        at = MapEntityBlock.ParseToken(data, at, out token);
        if (at >= 0)
            return token.Length > 0;
        at = data.Length;
        return false;
    }
}
