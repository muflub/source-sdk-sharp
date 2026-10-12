using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_soundscape_report</c> — what the SERVER has decided each player is
/// hearing, in the same shape <c>cs_cl_soundscape_report</c> prints on the
/// client.
///
/// <para>
/// <b>It exists because one report cannot close plan_entity.md step 8e-sound's
/// second defect.</b> The client's report answers "which soundscape is playing
/// here", and a client that has latched onto a <c>playsoundscape</c> answers it
/// perfectly while the transport is still missing — which is exactly the state
/// step 7e measured and could not distinguish with the client's number alone.
/// The gate needs BOTH ends: the server's choice and the client's, compared,
/// so that "soundscapes change on entry" means the server decided and the
/// client followed rather than the client deciding for itself.
/// </para>
///
/// <para>
/// The soundscape ID is the one-based token <see cref="ISoundscapes.Register"/>
/// hands out and is what the wire carries as <c>entIndex</c>; the INDEX is the
/// position in the parsed <c>soundscapes_*.txt</c> list, which is the number
/// both sides name a soundscape by. Both are printed because a mismatch between
/// them is precisely how a wired transport can still play the wrong thing.
/// </para>
/// </summary>
[ConCommand("cs_sv_soundscape_report",
            Help = "plan_entity.md 8e-sound: which soundscape the SERVER has chosen for each " +
                   "player, by id, by index and by name. Compare with cs_cl_soundscape_report.")]
public sealed class SoundscapeReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public SoundscapeReportCommand(ILog log, ISoundscapes soundscapes, IEnginePlayers players, IEngineEntities entities)
    {
        _log = log;
        _soundscapes = soundscapes;
        _players = players;
        _entities = entities;
    }

    private readonly ISoundscapes _soundscapes;
    private readonly IEnginePlayers _players;
    private readonly IEngineEntities _entities;

    public void Execute(string args)
    {
        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            var player = _players.GetHandle(slot);

            if (!_entities.IsValid(player))
                continue;

            int id = _soundscapes.CurrentId(player);
            var chosen = Find(id);

            _log.Msg($"[SourceSharp] server soundscape report: registered " +
                    $"{_soundscapes.All.Count} player {slot} id {id} index " +
                    $"{(chosen?.SoundscapeIndex ?? -1)} [{(chosen?.SoundscapeName ?? "none")}]\n");
        }
    }

    private EnvSoundscape? Find(int id)
    {
        // By identity token rather than by list position: Register hands out
        // one-based tokens in spawn order, so the list index and the token
        // agree today -- and a report that assumed they always would is a
        // report that lies the day a soundscape fails to register.
        foreach (var soundscape in _soundscapes.All)
            if (soundscape.SoundscapeId == id)
                return soundscape;

        return null;
    }
}
