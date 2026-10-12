//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// c_te_legacytempents.cpp's <c>CTempEnts::AttachTentToPlayer</c>: a model or
/// an animating sprite riding a player's head until it dies, high priority, at
/// full alpha and without the distance fade. What KillAttachedTents (and
/// CTEKillPlayerAttachments) takes away again. Replayed against the recorded
/// attaches (TempEntAttachTests).
/// </summary>
public sealed class TempEntAttachments(ILog log, ClientTempEntPool pool, IEnginePrecache models,
                                       IEngineEffects effects, IGameClock clock)
{
    // kRenderFxNoDissipation.
    private const int RenderFxNoDissipation = 14;

    /// <summary>
    /// The attach. <paramref name="maxClients"/> is the engine's
    /// <c>gpGlobals-&gt;maxClients</c>. The slot the tempent took, or -1.
    /// </summary>
    public int AttachTentToPlayer(int client, int modelIndex, float zoffset, float life, int maxClients)
    {
        if (client <= 0 || client > maxClients)
        {
            log.Warning("Bad client in AttachTentToPlayer()!\n");
            return -1;
        }

        if (!effects.ClientEntityOrigin(client, out Vector origin))
        {
            log.Warning($"Couldn't get IClientEntity for {client}\n");
            return -1;
        }

        ModelType type = models.ModelTypeOf(modelIndex);
        if (type == ModelType.Bad)
        {
            log.Warning($"No model {modelIndex}!\n");
            return -1;
        }

        var position = new Vector(origin.X, origin.Y, origin.Z + zoffset);

        var spawn = TempEntSpawn.Prepared(clock.Now);
        spawn.RenderMode = (int)RenderMode.Normal;
        spawn.ColorA = 255;
        spawn.RenderAmt = 255;
        spawn.RenderFx = RenderFxNoDissipation;
        spawn.ClientIndex = client;
        spawn.TentOffset = new Vector(0, 0, zoffset);
        spawn.Die = clock.Now + life;
        spawn.Flags |= (int)(TempEntFlags.PlyrAttachment | TempEntFlags.Persist);

        if (type == ModelType.Sprite)
        {
            int frameCount = models.ModelFrameCount(modelIndex);
            spawn.FrameMax = frameCount - 1;
            spawn.Flags |= (int)(TempEntFlags.SprAnimate | TempEntFlags.SprAnimateLoop);
            spawn.FrameRate = 10;
        }
        else
        {
            spawn.FrameMax = 0;
        }

        spawn.Frame = 0;

        int slot = pool.Spawn(position, modelIndex, TempEntPriority.High, in spawn);
        if (slot < 0)
            log.Warning("No temp ent.\n");
        return slot;
    }
}
