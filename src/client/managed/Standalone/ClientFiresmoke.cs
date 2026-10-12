using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of ss_firesmoke, and the shortest entity in this directory.
///
/// <para>
/// That is not an omission — it is what stock's <c>C_FireSmoke</c> had already
/// become. <c>Simulate</c>, <c>Update</c>, <c>UpdateScale</c>,
/// <c>UpdateFlames</c>, <c>UpdateAnimation</c>, <c>UpdateEffects</c>,
/// <c>AddFlames</c>, <c>SpawnSmoke</c> and <c>FindClipPlane</c> are all empty
/// bodies in the file this replaces, left behind when the flame moved from
/// hand-built sprites to a particle system. What remains is <c>Start()</c>:
/// choose one of eight systems from a size and a flag, and create it.
/// </para>
///
/// <para>
/// So the whole class is: when the flame arrives, start the right system; when
/// it goes, stop it. The choosing is <see cref="FireParticleNames"/>, which is
/// shared and tested; the starting is one ABI call.
/// </para>
///
/// <para>
/// AND IT IS NOT OPTIONAL, short as it is. A class with networked state gets a
/// table on a side only if that side has an entity class to host it, so without
/// this file the server publishes a SendTable the client cannot match and the
/// join stops with "No matching RecvTable for SendTable 'DT_ss_firesmoke'"
/// rather than with a missing flame.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsFiresmoke)]
public sealed class ClientFiresmoke : ClientStandaloneEntity
{
    public ClientFiresmoke(IEngineEntityRender render, IEngineEffects effects)
        : base(render)
    {
        Effects = effects;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    public override string ClassName => EntityNames.SsFiresmoke;

    /// <summary>
    /// The same declaration the server writes through. Decoded by the registry
    /// before <see cref="OnDataChanged"/> is called.
    /// </summary>
    [NetState] public FireSmokeNetState State = new();

    /// <summary>
    /// Which system is burning, so a second update does not start a second one.
    ///
    /// <para>
    /// Empty until one has been started. Stock had no equivalent because
    /// <c>Start()</c> was called from exactly one place —
    /// <c>OnDataChanged(DATA_UPDATE_CREATED)</c> — and could not be reached
    /// twice. That is no longer true here: an entity re-entering the PVS is
    /// created again, and a flame whose size changed enough to cross a bucket
    /// boundary should change system rather than gain one.
    /// </para>
    /// </summary>
    private string _running = "";

    /// <summary>
    /// Starts, or changes, the particle system this flame is made of.
    ///
    /// <para>
    /// On every update rather than only the first, because the size and the
    /// smoke flag are both things the fire can change while it burns, and
    /// because there is nothing else for this class to do.
    /// </para>
    ///
    /// <para>
    /// Nothing here reads the entity's POSITION. Native drives the system's
    /// control point from the entity every frame, which is the only correct
    /// answer for the same reason the sprites' is: what this side holds is the
    /// last snapshot, and an entity that only moved delivers no update at all.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        string wanted = FireParticleNames.ForSize(State.FlameSize, State.Smoke);

        if (wanted == _running)
            return;

        if (!Effects.ParticleSystemStart(Handle, wanted))
        {
            // A name no .pcf defines. Said once per flame rather than per
            // update, which is what leaving _running empty would have cost.
            Log.Warning($"[SourceSharp] ss_firesmoke: no particle system named '{wanted}'\n");
            _running = wanted;
            return;
        }

        // Said once per flame, and worth the line: whether the client started
        // the system the server's size asked for is otherwise unobservable from
        // outside, and "no warning" is not the same evidence as "this one".
        // It is also the only place the size-to-bucket decision becomes visible
        // in a running game.
        Log.Msg($"[SourceSharp/Client] ss_firesmoke: {wanted} for a flame of {State.FlameSize} units\n");

        _running = wanted;
    }

    /// <summary>
    /// The flame goes out with its entity.
    ///
    /// The particles already in the air finish rather than blinking away, which
    /// is a second or two of fire burning out and is what stock's
    /// <c>~C_FireSmoke</c> asks for with its own three arguments.
    /// </summary>
    public override void OnRemoved()
    {
        Effects.ParticleSystemStop(Handle);
        _running = "";
    }
}
