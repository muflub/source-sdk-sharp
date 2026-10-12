//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_tracer: a bullet streak drawn between here and something named, on a
/// repeat.
///
/// <para>
/// The streak is a temp entity and has no object behind it. What is a decision
/// is WHERE THE FAR END IS and how often — and the far-end half is the whole
/// reason this class is more than two lines: stock resolves the named entity
/// once, remembers its ORIGIN rather than a handle, and never looks again. A
/// tracer aimed at something that moves keeps pointing where that thing was
/// when the map loaded.
/// </para>
///
/// <para>
/// That is kept. It is not obviously the better behaviour, but it is what every
/// map with one was built against, and a tracer that started tracking would
/// change those maps rather than fix them. Recorded in docs/divergences.md as
/// stock behaviour deliberately preserved.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvTracer)]
public sealed class EnvTracer : ScriptedEntity
{
    public EnvTracer(IEngineEffects effects)
    {
        Effects = effects;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;
    /// <summary>What a map may set on this entity. See <see cref="EnvTracerVars"/>.</summary>
    [ScriptVars] public EnvTracerVars Vars = new();

    /// <summary>
    /// Where the far end is, resolved once.
    ///
    /// <para>
    /// Null until the first think rather than filled at spawn, and that timing
    /// is the interesting part. Stock does this in Activate, which runs after
    /// every entity in the map exists; there is no Activate on this hierarchy,
    /// and OnSpawn runs while the map is still being parsed — so a tracer whose
    /// target appears LATER in the .bsp would find nothing and never start.
    /// The first think is the earliest moment that is certainly after the whole
    /// map has loaded.
    /// </para>
    /// </summary>
    private Vector? _end;

    /// <summary>Whether the target has been looked for yet. Distinct from having found it.</summary>
    private bool _resolved;

    public override void OnSpawn()
    {
        // Stock's, and it is a CLAMP rather than a default: a map that wrote 0
        // gets one second, not a streak every tick.
        if (Vars.Delay <= 0f)
            Vars.Delay = 1f;

        SetNextThink(Vars.Delay);
    }

    /// <summary>The wait between streaks, after the zero clamp. Test-visible.</summary>
    internal float Delay => Vars.Delay;

    public override void OnThink()
    {
        if (!_resolved)
        {
            _resolved = true;
            _end = Resolve();

            if (_end is null)
            {
                // Named and said once. Stock prints the same thing and then
                // stops thinking, which is what makes a typo in the target key
                // findable at all — the entity is otherwise silent and
                // invisible.
                Log.Warning($"env_tracer: unknown entity \"{Vars.Target}\"\n");
                StopThinking();
                return;
            }
        }

        if (_end is Vector end)
            Effects.TracerEffect(Origin, end);

        SetNextThink(Vars.Delay);
    }

    /// <summary>
    /// Where the named entity is, or null if there is no such entity.
    ///
    /// <para>
    /// By NAME through the engine rather than through the managed registry,
    /// because a tracer may point at either hierarchy: <c>info_target</c> is
    /// C#'s and a stock brush is not, and a mapper picking one of them did not
    /// mean anything by the choice.
    /// </para>
    /// </summary>
    private Vector? Resolve()
    {
        if (Vars.Target.Length == 0)
            return null;

        EntityHandle handle = Entities.FindByName(Vars.Target);
        return Entities.IsValid(handle) ? Entities.GetOrigin(handle) : null;
    }
}

/// <summary>
/// The map keys <see cref="EnvTracer"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvTracerVars
{
    /// <summary>Seconds between streaks. Zero means one, not none — see <see cref="OnSpawn"/>.</summary>
    public float Delay { get; set; }

    /// <summary>What it points at, by name.</summary>
    public string Target { get; set; } = string.Empty;
}
