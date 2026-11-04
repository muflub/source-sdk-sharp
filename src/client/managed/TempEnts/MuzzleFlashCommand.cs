using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>cs_cl_te_muzzleflash type attachment firstperson</c>: the temp entities'
/// muzzle flash on the local player's attachment, for a live check of the
/// flashes and their emitters: every variant on demand, the NPC ones included,
/// which no player weapon's animation event fires.
/// </summary>
[ConCommand("cs_cl_te_muzzleflash", Flags = ConVarFlags.Cheat,
            Help = "cs_cl_te_muzzleflash type attachment firstperson. The temp entities' muzzle flash on the local player")]
public sealed class MuzzleFlashCommand(ILog log, MuzzleFlashes flashes, IFxRender render, IClientStandalones registry)
{
    public void Execute(int type, int attachment, int firstPerson)
    {
        if (registry.GetByIndex(render.LocalPlayer()) is not { } player)
        {
            log.Msg("cs_cl_te_muzzleflash: no local player\n");
            return;
        }
        long before = flashes.Drawn;
        flashes.MuzzleFlash(type, player.Handle.Value, attachment, firstPerson != 0);
        log.Msg($"cs_cl_te_muzzleflash: type {type} on attachment {attachment}: {flashes.Drawn - before} variant(s) drawn\n");
    }
}
