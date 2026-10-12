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
public sealed class MuzzleFlashCommand(ILog log, MuzzleFlashes flashes, IFxRender render, IClientStandalones registry) : IConCommand
{
    public void Execute(string args)
    {
        string[] a = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length < 3 || !int.TryParse(a[0], out int type) || !int.TryParse(a[1], out int attachment) || !int.TryParse(a[2], out int fp))
        {
            log.Msg("usage: cs_cl_te_muzzleflash type attachment firstperson\n");
            return;
        }
        if (registry.GetByIndex(render.LocalPlayer()) is not { } player)
        {
            log.Msg("cs_cl_te_muzzleflash: no local player\n");
            return;
        }
        long before = flashes.Drawn;
        flashes.MuzzleFlash(type, player.Handle.Value, attachment, fp != 0);
        log.Msg($"cs_cl_te_muzzleflash: type {type} on attachment {attachment}: {flashes.Drawn - before} variant(s) drawn\n");
    }
}
