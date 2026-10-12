using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// What a fired animation event actually DOES on this client: a light at the
/// muzzle, a noise from the soundscript.
///
/// <para>
/// The consumer <c>cs_animcheck</c> has been waiting for. Phase 5 could read a
/// model's event table and say what it carried; it could not make any of it
/// happen. The fire animation played, <c>v_smg1</c>'s <c>AE_MUZZLEFLASH</c>
/// sat at cycle 0, and the screen was as dark on the frame of the shot as on
/// the frame before it.
/// </para>
///
/// <para>
/// <b>A dynamic light AND the muzzle-flash temp entity.</b> The light was
/// first, when the temp entities' flash had no managed path; since lane
/// c5-particles the event also sends the particle flash, as stock's
/// C_BaseAnimating did (<c>DispatchMuzzleEffect</c> for the named events,
/// <c>FireObsoleteEvent</c> for the numbered ones), through
/// <see cref="TempEnts.ILegacyMuzzleFlashes"/>. Without it a player's shots
/// lit the room and drew no flash.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 3 throughout, client presentation for one viewer.
/// Everything here is a photon or a sound wave; nothing is a decision anybody
/// else has to agree with. The events with server consequences never reach
/// this class — <see cref="AnimationEventCatalog.IsClientEvent"/> drops them
/// upstream.
/// </para>
/// </summary>
public sealed class AnimationEventEffects : IAnimationEventSink
{
    /// <summary>
    /// Where the muzzle-flash lights' pool keys start.
    ///
    /// <para>
    /// A band of its own, above any entity index, because
    /// <c>light_dynamic</c> keys its pool slot by entity index and a muzzle
    /// flash borrowing that number would put a lamp out for a tenth of a
    /// second every time somebody fired near it. Keyed at all — rather than
    /// key 0, which allocates a fresh slot each time — so that an automatic
    /// weapon MOVES its flash rather than filling the pool with a burst's
    /// worth of them.
    /// </para>
    /// </summary>
    public const int MuzzleLightKeyBase = 0x0010_0000;

    /// <summary>
    /// How long a muzzle flash lights for.
    ///
    /// <para>
    /// Stock's dlight muzzle flashes die within a couple of frames; longer and
    /// a burst reads as a torch rather than as a series of shots.
    /// </para>
    /// </summary>
    public const float MuzzleLightSeconds = 0.06f;

    /// <summary>How far a muzzle flash throws light.</summary>
    public const float MuzzleLightRadius = 256f;

    private readonly IEngineEntityRender _render;
    private readonly IEngineVisibility _visibility;
    private readonly IEngineSound _sound;
    private readonly IEngineEntities _entities;
    private readonly TempEnts.ILegacyMuzzleFlashes _flashes;
    private readonly IViewEntity _view;
    private readonly TempEnts.ILegacyBrass _brass;
    private readonly Interop.IFxRender _fx;

    public AnimationEventEffects(IEngineEntityRender render, IEngineVisibility visibility,
                                 IEngineSound sound, IEngineEntities entities, TempEnts.ILegacyMuzzleFlashes flashes,
                                 IViewEntity view, TempEnts.ILegacyBrass brass, Interop.IFxRender fx)
    {
        _brass = brass;
        _fx = fx;
        _render = render;
        _visibility = visibility;
        _sound = sound;
        _entities = entities;
        _flashes = flashes;
        _view = view;
    }

    /// <summary>Every event this class has been handed, by kind. For the report.</summary>
    public int Lit { get; private set; }

    /// <inheritdoc cref="Lit"/>
    public int Sounded { get; private set; }

    /// <summary>
    /// Events understood but not acted on, and events not understood at all.
    ///
    /// <para>
    /// Counted rather than ignored, because "nothing happened" and "nothing
    /// was asked for" are the two things a report about effects most needs to
    /// tell apart, and a silent drop makes them identical.
    /// </para>
    /// </summary>
    public int Unhandled { get; private set; }

    /// <summary>The last thing that happened, for the report to print.</summary>
    public string LastLine { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public void Fire(in AnimationEventFired fired)
    {
        AnimationEventAction action = AnimationEventActions.Decide(fired.Carried);

        switch (action.Kind)
        {
            case AnimationEventKind.MuzzleFlash:
                MuzzleFlash(fired, action);
                break;

            case AnimationEventKind.Sound:
                _sound.ClientEmitSound(fired.Entity, action.Payload);
                Sounded++;
                LastLine = $"{fired.ClassName} seq {fired.Sequence} " +
                           $"{fired.Carried.Describe()} -> sound '{action.Payload}'";
                break;

            case AnimationEventKind.EjectBrass:
                EjectBrass(fired, action);
                break;

            default:
                Unhandled++;
                LastLine = $"{fired.ClassName} seq {fired.Sequence} " +
                           $"{fired.Carried.Describe()} -> {action.Kind} (nothing does this yet)";
                break;
        }
    }

    /// <summary>
    /// <c>CL_EVENT_EJECTBRASS1</c>, as stock's C_BaseAnimating played it: a
    /// shell from attachment 2 (the decoder's index), at that attachment's
    /// place and angles, the entity's own angles as the gun's,
    /// <c>atoi( options )</c> its type, when the view is within 256 units of
    /// the entity. The temp entities' EjectBrass (managed) does the rest.
    /// </summary>
    private void EjectBrass(in AnimationEventFired fired, in AnimationEventAction action)
    {
        Vector here = _entities.GetOrigin(fired.Entity);
        Vector eye = _fx.MainViewOrigin();
        float dx = eye.X - here.X, dy = eye.Y - here.Y, dz = eye.Z - here.Z;
        if (!(dx * dx + dy * dy + dz * dz < 256 * 256) ||
            !_render.GetAttachment(fired.Entity, action.AttachmentIndex, out Vector origin, out Vector angles))
        {
            Unhandled++;
            LastLine = $"{fired.ClassName} seq {fired.Sequence} {fired.Carried.Describe()} -> no brass (out of range or no attachment)";
            return;
        }

        Vector gun = _entities.GetAngles(fired.Entity);
        _brass.EjectBrass(origin, new QAngle(angles.X, angles.Y, angles.Z), new QAngle(gun.X, gun.Y, gun.Z),
                          AnimationEventActions.Atoi(action.Payload));
        Ejected++;
        LastLine = $"{fired.ClassName} seq {fired.Sequence} {fired.Carried.Describe()} -> brass {action.Payload} " +
                   $"from attachment {action.AttachmentIndex} ({origin.X:0.0} {origin.Y:0.0} {origin.Z:0.0})";
    }

    /// <summary>Shells thrown by the brass event, for the report.</summary>
    public int Ejected { get; private set; }

    /// <summary>Forget the counters. The level is going.</summary>
    public void Reset()
    {
        Lit = 0;
        Sounded = 0;
        Ejected = 0;
        Unhandled = 0;
        LastLine = string.Empty;
    }

    private void MuzzleFlash(in AnimationEventFired fired, in AnimationEventAction action)
    {
        // By name when the event named one, by index when it numbered one. The
        // two kinds of event say it in different ways and neither can be
        // guessed from the other.
        int attachment = action.AttachmentName.Length > 0
            ? _render.LookupAttachment(fired.Entity, action.AttachmentName)
            : action.AttachmentIndex;

        if (attachment <= 0 ||
            !_render.GetAttachment(fired.Entity, attachment, out Vector origin, out _))
        {
            // The model does not have the attachment its own event asked for.
            // Refused rather than lit at the entity's origin: a flash at the
            // origin of a view model is a flash inside the player's head, and
            // it would look like the attachment working badly rather than like
            // it being absent.
            Unhandled++;
            LastLine = $"{fired.ClassName} seq {fired.Sequence} " +
                       $"{fired.Carried.Describe()} -> no attachment " +
                       $"'{(action.AttachmentName.Length > 0 ? action.AttachmentName : action.AttachmentIndex.ToString())}'";
            return;
        }

        var light = new DynamicLight
        {
            Key = MuzzleLightKeyBase + _entities.GetIndex(fired.Entity),
            IsElight = 0,
            OriginX = origin.X,
            OriginY = origin.Y,
            OriginZ = origin.Z,
            Radius = MuzzleLightRadius,

            // The warm white of burning propellant, with the exponent that
            // lets a dlight be brighter than white — which is what makes a
            // muzzle flash read as a flash rather than as a lamp.
            Red = 255,
            Green = 192,
            Blue = 64,
            Exponent = 5,

            Style = 0,
            Flags = (int)DynamicLightFlags.None,
            LiveFor = MuzzleLightSeconds,
        };

        _visibility.DynamicLightUpdate(light);

        // And the particle flash, as stock's C_BaseAnimating sent it to the temp
        // entities: a named event (DispatchMuzzleEffect) with its type, the
        // entity's own handle and the attachment it named; a numbered one
        // (FireObsoleteEvent) as the world-space flash, atoi( options ) its
        // type, on the view entity.
        if (action.AttachmentName.Length > 0)
            _flashes.MuzzleFlash(AnimationEventActions.MuzzleFlashType(action.Payload), fired.Entity.Value, attachment, action.FirstPerson);
        else
            _flashes.MuzzleFlashAt(AnimationEventActions.Atoi(action.Payload), _view.ViewEntity(), action.FirstPerson);

        Lit++;
        LastLine = $"{fired.ClassName} seq {fired.Sequence} {fired.Carried.Describe()} -> " +
                   $"muzzle light '{action.Payload}' at attachment {attachment} " +
                   $"({origin.X:0.0} {origin.Y:0.0} {origin.Z:0.0})";
    }
}

/// <summary>
/// <c>render-&gt;GetViewEntity()</c>'s handle: whose eyes the view is from,
/// which stock's numbered muzzle-flash events hand the temp entities.
/// </summary>
public interface IViewEntity
{
    uint ViewEntity();
}

/// <summary>
/// The local player as the view entity. The engine's client view entity is not
/// read on this ABI; it is the local player unless a point_viewcontrol has the
/// view, and then a numbered flash lands on the player rather than the camera.
/// </summary>
public sealed class LocalViewEntity(Interop.IFxRender render, IClientStandalones registry) : IViewEntity
{
    public uint ViewEntity() => registry.GetByIndex(render.LocalPlayer())?.Handle.Value ?? EntityHandle.None.Value;
}

/// <summary>
/// The console door onto the dispatcher: how many events have fired, and what
/// the last one did.
///
/// <para>
/// Its companion <c>cs_animcheck</c> says what a model CARRIES. This one says
/// what has actually HAPPENED, which is the question that separates "the model
/// has a muzzle flash" from "the muzzle flashed" — the exact gap this piece of
/// work closed, and the one a report about the table alone cannot see.
/// </para>
///
/// <para>
/// It is a counter and not the oracle. A count is a log line, and this project
/// has twice been told by a log line that something was working while the
/// screen disagreed. The oracle for a muzzle flash is a screenshot.
/// </para>
/// </summary>
[ConCommand("cs_animevents",
            Help = "cs_animevents [classname-filter]. How many animation events have fired on " +
                   "this client and what the last one did; with a filter, the count PER ENTITY.")]
public sealed class AnimEventsCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly AnimationEventPump _pump;
    private readonly AnimationEventEffects _effects;
    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public AnimEventsCommand(ILog log, AnimationEventPump pump, AnimationEventEffects effects,
                             IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _pump = pump;
        _effects = effects;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string filter = args.Trim();

        if (filter.Length > 0)
        {
            PerEntity(filter);
            return;
        }

        _log.Msg($"[SourceSharp/Client] cs_animevents: {_pump.WatchedCount} entity/entities watched, " +
                $"{_pump.FiredCount} client event(s) fired\n");
        _log.Msg($"[SourceSharp/Client] cs_animevents: {_effects.Lit} muzzle light(s), " +
                $"{_effects.Sounded} sound(s), {_effects.Ejected} shell(s), {_effects.Unhandled} unhandled\n");

        _log.Msg(_effects.LastLine.Length > 0
            ? $"[SourceSharp/Client] cs_animevents: last {_effects.LastLine}\n"
            : "[SourceSharp/Client] cs_animevents: nothing has fired yet\n");
    }

    /// <summary>
    /// One line per matching entity: its index, its sequence, its cycle, and
    /// how many events have fired ON IT.
    ///
    /// <para>
    /// <b>Per entity, because the client total cannot answer "once".</b> The
    /// total moves for every viewmodel in the map — a player standing still
    /// with an idling weapon adds to it — so a check watching the total for
    /// growth is watching noise. Step 6d's live gate reads these rows.
    /// </para>
    ///
    /// <para>
    /// The SEQUENCE and the CYCLE are printed beside the count and not because
    /// anything needs them here: a count that did not move is the same line for
    /// an event that correctly fired once and for an animation that never
    /// started, and the pair beside it is what separates those.
    /// </para>
    /// </summary>
    private void PerEntity(string filter)
    {
        int looked = 0;
        int matched = 0;

        foreach (var entity in _standalones.Live())
        {
            looked++;

            if (filter != "*" &&
                !entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            matched++;

            _render.SequenceState(entity.Handle, out int sequence, out float cycle,
                                  out float duration, out bool looping, out int starts);

            _log.Msg($"[SourceSharp/Client] cs_animevents: entity {entity.Handle.Value & 0x7FFu} " +
                    $"'{entity.ClassName}' seq {sequence} cycle {cycle:0.0000} " +
                    $"dur {duration:0.0000} loop {(looping ? 1 : 0)} starts {starts} " +
                    $"fired {_pump.FiredOn(entity.Handle)}\n");
        }

        _log.Msg($"[SourceSharp/Client] cs_animevents: {matched} of {looked} live entit(ies) " +
                $"matched '{filter}'\n");
    }
}
