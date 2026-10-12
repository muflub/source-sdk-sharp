//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// A sprite model's material file, read in C# (plan_client_novalve C9,
/// KeyValues K4). The engine has the client fill a CEngineSprite per sprite
/// model; native asks this which material the sprite names and what its
/// .vmt says, and builds one procedural material a render mode from the
/// answer (shell_sprite_leaf.cpp, IManagedMaterialFilesClient::SpriteMaterial).
///
/// <para>
/// The name rule is spritemodel.cpp's: the extension stripped, lower case,
/// forward slashes, under <c>materials/</c> unless the name is a UNC path
/// (<c>//server/...</c>), which is used as it is; the file is that plus
/// <c>.vmt</c>, read on the GAME path. A file that cannot be read is
/// stock's warning and no sprite.
/// </para>
/// </summary>
public sealed class SpriteMaterialFiles(ILog log, KeyValuesLoad load)
{
    /// <summary>The material's name and the .vmt's first root, or null (said) when the file cannot be read.</summary>
    public (string Material, KeyValuesNode Vmt)? Read(string spriteName)
    {
        string material = MaterialName(spriteName);
        string vmt = material + ".vmt";
        KeyValuesNode? root = load.LoadFromFile("vmt", vmt, "GAME");
        if (root == null)
        {
            log.Warning($"Unable to load sprite material {vmt}!\n");
            return null;
        }
        return (material, root);
    }

    /// <summary>"materials/&lt;name, no extension, lower case, forward slashes&gt;", or a UNC name as it is.</summary>
    public static string MaterialName(string spriteName)
    {
        var b = new StringBuilder(StripExtension(spriteName));
        for (int i = 0; i < b.Length; i++)
        {
            char c = b[i];
            if (c >= 'A' && c <= 'Z') b[i] = (char)(c + 32);
            else if (c == '\\') b[i] = '/';
        }
        string baseName = b.ToString();
        bool unc = baseName.Length >= 3 && baseName[0] == '/' && baseName[1] == '/' && baseName[2] != '/';
        return unc ? baseName : "materials/" + baseName;
    }

    // V_StripExtension: the last '.' after the last separator, not the first character.
    private static string StripExtension(string s)
    {
        for (int end = s.Length - 1; end > 0; end--)
        {
            char c = s[end];
            if (c == '/' || c == '\\') break;
            if (c == '.') return s[..end];
        }
        return s;
    }

    /// <summary>
    /// KeyValues' IsSteamDeck(false), which [$DECK] evaluates: -nogamepadui
    /// says no, -gamepadui yes, else the SteamDeck or SteamTenFoot variable.
    /// </summary>
    public static bool IsSteamDeck(IReadOnlyList<string> args)
    {
        if (LocalCommandLine.Has(args, "-nogamepadui")) return false;
        if (LocalCommandLine.Has(args, "-gamepadui")) return true;
        return Atoi(Environment.GetEnvironmentVariable("SteamDeck")) != 0
               || Atoi(Environment.GetEnvironmentVariable("SteamTenFoot")) != 0;
    }

    private static long Atoi(string? s) =>
        string.IsNullOrEmpty(s) ? 0 : (int)CNumbers.Strtol(Encoding.Latin1.GetBytes(s), out _);
}

/// <summary>
/// A KeyValues tree in one block for native to rebuild (shell_sprite_leaf.cpp
/// reads it): a NUL-terminated header string, then the tree in preorder, each
/// key as its type byte, its name NUL-terminated, its value (a string
/// NUL-terminated, an int32, a float32 or a uint64, little-endian, by type;
/// nothing for a block) and a uint32 child count.
/// </summary>
public static class KeyValuesBlock
{
    /// <summary>Writes the header and tree into payload: the bytes written, or -1 when they do not fit.</summary>
    public static int Write(Span<byte> payload, string header, KeyValuesNode root)
    {
        int at = 0;
        if (!Put(payload, ref at, Encoding.Latin1.GetBytes(header))) return -1;
        return Node(payload, ref at, root) ? at : -1;
    }

    private static bool Put(Span<byte> payload, ref int at, ReadOnlySpan<byte> cstring)
    {
        if (at + cstring.Length + 1 > payload.Length) return false;
        cstring.CopyTo(payload[at..]);
        at += cstring.Length;
        payload[at++] = 0;
        return true;
    }

    private static bool Node(Span<byte> payload, ref int at, KeyValuesNode kv)
    {
        if (at + 1 > payload.Length) return false;
        payload[at++] = (byte)kv.Type;
        if (!Put(payload, ref at, kv.Name)) return false;
        switch (kv.Type)
        {
            case KeyValuesType.String:
                if (!Put(payload, ref at, kv.StringValue)) return false;
                break;
            case KeyValuesType.Int:
                if (at + 4 > payload.Length) return false;
                BitConverter.TryWriteBytes(payload[at..], kv.IntValue);
                at += 4;
                break;
            case KeyValuesType.Float:
                if (at + 4 > payload.Length) return false;
                BitConverter.TryWriteBytes(payload[at..], kv.FloatValue);
                at += 4;
                break;
            case KeyValuesType.UInt64:
                if (at + 8 > payload.Length) return false;
                BitConverter.TryWriteBytes(payload[at..], kv.UInt64Value);
                at += 8;
                break;
        }
        int countAt = at;
        if (at + 4 > payload.Length) return false;
        at += 4;
        uint count = 0;
        foreach (KeyValuesNode child in kv.Children)
        {
            if (!Node(payload, ref at, child)) return false;
            count++;
        }
        BitConverter.TryWriteBytes(payload[countAt..], count);
        return true;
    }
}
