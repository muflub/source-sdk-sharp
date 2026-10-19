//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>WaterLODMaterialProxy.cpp ("WaterLOD"): the view's cheap-water distances into the water material.</summary>
public sealed class WaterLodProxy : MaterialProxy
{
    private int _start = -1;
    private int _end = -1;

    public override bool Init(IProxyInit init)
    {
        _start = init.FindVar("$CHEAPWATERSTARTDISTANCE", false, out bool found);
        if (!found)
            return false;
        _end = init.FindVar("$CHEAPWATERENDDISTANCE", false, out found);
        return found;
    }

    public override void Bind(IProxyBind bind)
    {
        if (_start < 0 || _end < 0)
            return;
        bind.WaterLod(out float start, out float end);
        bind.SetFloat(_start, start);
        bind.SetFloat(_end, end);
    }
}

/// <summary>
/// WorldDimsProxy.cpp ("WorldDims"): the world's bounds into
/// <c>$world_mins</c>/<c>$world_maxs</c>. Init asks with no found flag, so the
/// two vars are whatever FindVar answers, found or not.
/// </summary>
public sealed class WorldDimsProxy : MaterialProxy
{
    private int _mins = -1;
    private int _maxs = -1;

    public override bool Init(IProxyInit init)
    {
        _mins = init.FindVar("$world_mins", false, out _);
        _maxs = init.FindVar("$world_maxs", false, out _);
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (_mins >= 0 && _maxs >= 0 && bind.WorldBounds(out Vector mins, out Vector maxs))
        {
            bind.SetVec(_mins, [mins.X, mins.Y, mins.Z], 3);
            bind.SetVec(_maxs, [maxs.X, maxs.Y, maxs.Z], 3);
        }
    }
}

/// <summary>dummyproxy.cpp ("Dummy"): says so at developer 1 and does nothing.</summary>
public sealed class DummyProxy : MaterialProxy
{
    private readonly ILog _log;
    private readonly Func<bool> _developer;

    /// <param name="developer">Whether <c>developer</c> is 1 or more (DevMsg( 1, ... ) prints then).</param>
    public DummyProxy(ILog log, Func<bool> developer)
    {
        _log = log;
        _developer = developer;
    }

    public override bool Init(IProxyInit init)
    {
        if (_developer())
            _log.Msg($"CDummyMaterialProxy::Init( material = \"{init.MaterialName}\" )\n");
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        if (_developer())
            _log.Msg($"CDummyMaterialProxy::OnBind( 0x{bind.Arg:x} )\n");
    }
}

/// <summary>
/// "Shield", "HeliBlade", "VortEmissive": HL2MP exposes these three (their
/// bodies live in HL2 entity TUs this mod does not compile), so materials that
/// name them get a proxy that binds nothing rather than a "not found" error.
/// Kept for interface parity with HL2MP (shell_materialproxies.cpp's history).
/// </summary>
public sealed class NullProxy : MaterialProxy
{
    public override bool Init(IProxyInit init) => true;

    public override void Bind(IProxyBind bind)
    {
    }
}

/// <summary>
/// "PlayerLogo" (proxyplayer.cpp:331-486, ported in shell_materialproxies.cpp
/// and now here): a spray decal's <c>$basetexture</c> becomes the spray of
/// the player the decal belongs to. The bind argument is that player's INDEX,
/// never a renderable. A spray that has not downloaded yet draws the default
/// and is looked up again on the next bind (stock cached the miss, so a spray
/// arriving mid-map never appeared until the map changed).
/// </summary>
public sealed class PlayerLogoProxy : MaterialProxy
{
    public const string DefaultDecal = "decals/YBlood1";

    /// <summary><c>TEXTURE_GROUP_DECAL</c>.</summary>
    public const string DecalGroup = "Decal textures";

    private readonly Dictionary<uint, int> _logos = new();
    private int _baseTexture = -1;
    private int _default = -1;

    /// <summary>How each bind ended (no spray, the player's texture, the default): the readout cs_cl_playerlogo printed.</summary>
    public int[] Outcomes { get; } = new int[3];

    public enum Outcome
    {
        NoCustomFile = 0,
        PlayerTexture = 1,
        DefaultTexture = 2,
    }

    /// <summary>A player index the engine can be asked about: 1..maxClients.</summary>
    public static bool IndexIsBindable(int player, int maxClients) => player > 0 && player <= maxClients;

    public static Outcome Classify(bool hasCustomFile, bool textureFound)
        => !hasCustomFile ? Outcome.NoCustomFile : textureFound ? Outcome.PlayerTexture : Outcome.DefaultTexture;

    /// <summary><c>"temp/" + Q_binarytohex( &amp;customFiles[0], 4 )</c>: the CRC's bytes in memory order, two lower-case hex digits each.</summary>
    public static string TextureName(uint crc)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, crc);
        return "temp/" + string.Concat(bytes.ToArray().Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    public override bool Init(IProxyInit init)
    {
        _baseTexture = init.FindVar("$basetexture", true, out bool found);
        if (!found)
            return false;

        _default = init.FindTexture(DefaultDecal, DecalGroup, true, out bool isError);
        if (_default < 0 || isError)
        {
            _default = -1;
            return false;
        }
        return true;
    }

    public override void Bind(IProxyBind bind)
    {
        int player = (int)bind.Arg;
        if (!IndexIsBindable(player, bind.MaxClients) || _baseTexture < 0)
            return;
        if (!bind.PlayerCustomFile(player, out uint crc))
            return;

        bool hasCustomFile = crc != 0;
        int texture = -1;
        if (hasCustomFile && !_logos.TryGetValue(crc, out texture))
        {
            texture = bind.FindTexture(TextureName(crc), DecalGroup, false);
            if (texture >= 0)
                _logos[crc] = texture;
        }

        Outcome outcome = Classify(hasCustomFile, texture >= 0);
        Outcomes[(int)outcome]++;
        if (outcome == Outcome.NoCustomFile)
            return;

        if (outcome == Outcome.PlayerTexture)
            bind.SetTexture(_baseTexture, texture);
        else if (_default >= 0)
            bind.SetTexture(_baseTexture, _default);
    }

    public override void SaveState(ReloadWriter writer)
    {
        writer.Write(_logos.Count);
        foreach ((uint crc, int texture) in _logos)
        {
            writer.Write(unchecked((int)crc));
            writer.Write(texture);
        }
        foreach (int n in Outcomes)
            writer.Write(n);
    }

    public override void RestoreState(ReloadReader reader)
    {
        _logos.Clear();
        int count = reader.ReadInt();
        for (int i = 0; i < count; i++)
        {
            uint crc = unchecked((uint)reader.ReadInt());
            _logos[crc] = reader.ReadInt();
        }
        for (int i = 0; i < Outcomes.Length; i++)
            Outcomes[i] = reader.ReadInt();
    }
}
