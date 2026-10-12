//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// What c_te_legacytempents.cpp's CTempEnts and C_LocalTempEntity asked, now
/// that the TU has left client.so: native's thunk (shell_legacy_tempents_client.cpp)
/// carries each call here. The render choices, the muzzle flashes
/// and the attach are their own classes, each replayed against stock;
/// what is decided here is CTempEnts::EjectBrass's gate and model and the
/// level's model list (LevelInit).
/// </summary>
public sealed class ClientTempEntDecisions(TempEntRender render, TempEntBreakGroups groups, MuzzleFlashes muzzle,
                                           TempEntAttachments attach, ClientTempEntPool pool,
                                           TempEntSpawns spawns, IFxTempEntModels models, IEngineConsole console)
    : IClientTempEntHost, ITempModelSpawner, ILegacyBrass
{
    /// <summary>CTempEnts::LevelInit's models, in its order: the muzzle sprites, then the three shells.</summary>
    public static readonly IReadOnlyList<string> LevelModels =
    [
        "sprites/ar2_muzzle1.vmt", "sprites/muzzleflash4.vmt", "sprites/muzzleflash4.vmt",
        "sprites/ar2_muzzle1b.vmt", "sprites/ar2_muzzle2b.vmt", "sprites/ar2_muzzle3b.vmt", "sprites/ar2_muzzle4b.vmt",
        "effects/combinemuzzle1.vmt", "effects/combinemuzzle2.vmt",
        "models/weapons/shell.mdl", "models/weapons/rifleshell.mdl", "models/weapons/shotgun_shell.mdl",
    ];

    /// <summary>m_pShells[]: the pistol's, the rifle's, the shotgun's.</summary>
    public static readonly IReadOnlyList<string> Shells = ["models/weapons/shell.mdl", "models/weapons/rifleshell.mdl", "models/weapons/shotgun_shell.mdl"];

    /// <summary>Brass ejected since the load (cs_cl_fx_report).</summary>
    public long Ejected { get; private set; }

    public bool IsTransparent(bool translucentModel, int renderMode) => TempEntRender.IsTransparent(translucentModel, renderMode);

    public int RenderGroup(int renderMode, int blend, bool translucentModel, bool twoPass)
        => TempEntRender.RenderGroup(renderMode, blend, translucentModel, twoPass);

    public void Blend(ReadOnlySpan<float> f, Span<int> output)
    {
        var (blend, alpha) = render.ComputeFxBlend(new TempEntBlendInput((int)f[0], (int)f[1], (int)f[2], (int)f[3], f[4],
            new Vector(f[5], f[6], f[7]), new Vector(f[8], f[9], f[10]), new Vector(f[11], f[12], f[13])));
        output[0] = blend;
        output[1] = alpha;
    }

    public int OccludedAlpha(int flags, int alpha, int renderAmt, in Vector normal, in Vector origin, in Vector mainViewOrigin)
        => TempEntRender.OccludedAlpha(flags, alpha, renderAmt, normal, origin, mainViewOrigin);

    public int LightingOrigin(int slot) => slot < 0 ? -1 : (int)groups.LightingOriginOf(slot);

    public void MuzzleFlash(int type, uint handle, int attachment, bool firstPerson, bool worldPath)
    {
        if (worldPath)
            muzzle.MuzzleFlashAt(type, handle, firstPerson);
        else
            muzzle.MuzzleFlash(type, handle, attachment, firstPerson);
    }

    public void EjectBrass(in Vector origin, in QAngle angles, in QAngle gunAngles, int type)
    {
        // Whether or not to eject brass from weapons: ConVar::GetBool, the value's whole part.
        string? eject = console.FindConVarValue("cl_ejectbrass");
        if (eject is not null && float.TryParse(eject, System.Globalization.NumberStyles.Float,
                                                System.Globalization.CultureInfo.InvariantCulture, out float f) && (int)f == 0)
            return;

        if (type < 0 || type >= Shells.Count)
            return;

        int slot = pool.ReserveNamed(origin, Shells[type], TempEntPriority.Low, models);
        if (slot < 0)
            return;

        Ejected++;
        spawns.EjectBrass(slot, type, new Vector(angles.Pitch, angles.Yaw, angles.Roll),
                          new Vector(gunAngles.Pitch, gunAngles.Yaw, gunAngles.Roll));
    }

    /// <summary>
    /// <c>CTempEnts::RocketFlare</c>: <c>sprites/animglow01.vmt</c>, loaded by
    /// name as stock loads it, then <see cref="TempEntSpawns.RocketFlareInto"/>.
    /// The slot, or -1.
    /// </summary>
    public int RocketFlare(in Vector pos)
    {
        int frameCount = models.FrameCount(TempEntSpawns.RocketFlareModel);
        if (frameCount < 0)
            return -1;

        int slot = pool.ReserveNamed(pos, TempEntSpawns.RocketFlareModel, TempEntPriority.Low, models);
        if (slot < 0)
            return -1;

        spawns.RocketFlareInto(slot, frameCount);
        return slot;
    }

    /// <summary>
    /// <c>CTempEnts::SpawnTempModel</c>, for a model named rather than handed
    /// over as a <c>model_t *</c>: loaded as stock's callers load it
    /// (<c>engine-&gt;LoadModel</c>), then
    /// <see cref="TempEntSpawns.SpawnTempModelInto"/>. The slot, or -1.
    /// </summary>
    public int SpawnTempModel(string model, in Vector origin, in Vector angles, in Vector velocity,
                              float life, int flags)
    {
        int slot = pool.ReserveNamed(origin, model, TempEntPriority.Low, models);
        if (slot < 0)
            return -1;

        spawns.SpawnTempModelInto(slot, angles, velocity, life, flags);
        return slot;
    }

    public int Attach(int client, int modelIndex, float zoffset, float life, int maxClients)
        => attach.AttachTentToPlayer(client, modelIndex, zoffset, life, maxClients);

    public void LevelInit()
    {
        foreach (string model in LevelModels)
            models.LoadModel(model);
    }
}

/// <summary><c>CTempEnts::SpawnTempModel</c> for a named model, as a client effect asks it.</summary>
public interface ITempModelSpawner
{
    int SpawnTempModel(string model, in Vector origin, in Vector angles, in Vector velocity, float life, int flags);
}
