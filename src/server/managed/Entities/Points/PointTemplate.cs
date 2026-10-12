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
/// point_template: remembers a group of entities a mapper drew, so that copies
/// of them can be put into the world later.
///
/// <para>
/// This class was the standing answer to "why is that one still C++". The
/// argument ran: a template is map-parse machinery, it reaches into the entity
/// parser during level load and stashes the keyvalue TEXT of the entities it
/// names, and there is no moment at which managed code could do that. Every
/// word of that is still true, and none of it was ever about the ENTITY.
/// </para>
///
/// <para>
/// What a template DECIDES is here: which names it claims, whether it consumes
/// them, whether the copies get unique names so their entity I/O wires up to
/// each other, and — the part that is the whole point of a template — where
/// each copy goes. That last one is arithmetic over a stored transform, and it
/// is <see cref="Transform"/> now, held to the mathlib it replaced by the
/// golden table in <c>sourcesharp/test/template_transform_cases.txt</c>.
/// </para>
///
/// <para>
/// What is left native is the residue and is genuinely irreducible: the map
/// parse that produces the keyvalue text, and the parser that turns text back
/// into an entity in hierarchy order. Those are the TWO remaining
/// <c>IEngineScripted.Template*</c> calls, and each one carries positions and
/// text rather than entities.
/// </para>
///
/// <para>
/// <b>It was four</b> (plan_server_all.md Wave 1, docs/plan-audit.md D11). The
/// STORE — the text, the entity-I/O rewriting over it, the instance counter and
/// the deferred precache list — was the other side of two of them, and it is
/// <see cref="TemplateStore"/> now. Reconnecting a group and scheduling its
/// precache are one managed object calling another.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointTemplate)]
public sealed class PointTemplate : ScriptedEntity
{
    public PointTemplate(IEngineScripted scripted, TemplateStore templates)
    {
        Scripted = scripted;
        Templates = templates;
    }

    /// <summary>The map parser and the ordered spawn — what is left native.</summary>
    private readonly IEngineScripted Scripted;

    /// <summary>
    /// The store: the keyvalue text of every template this has claimed, the
    /// entity-I/O rewriting over it, and the deferred precache list.
    ///
    /// <para>
    /// It was on the other side of <see cref="Scripted"/> until Wave 1
    /// (docs/plan-audit.md D11). Injected rather than resolved, so a test can
    /// hand this class a store of its own and read what ended up in it.
    /// </para>
    /// </summary>
    private readonly TemplateStore Templates;

    /// <summary>Stock's cap, and the number of keys the FGD offers.</summary>
    public const int MaxTemplates = 16;

    /// <summary>
    /// point_template's two spawnflags. Both are the mapper turning something
    /// OFF, which is why both read inverted at their call sites — stock spells
    /// them as the negative and existing maps are set against that spelling.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>What a template whose mapper ticked nothing reads as.</summary>
        None = 0,

        /// <summary>Leave the source entities in the world instead of eating them.</summary>
        KeepSources = 0b01,

        /// <summary>
        /// Give every copy the source's own name rather than a unique one.
        ///
        /// A mapper may only ask for this having promised that one instance
        /// exists at a time: without the suffix, two instances of a group share
        /// targetnames, and an output inside one of them fires at both.
        /// </summary>
        PreserveNames = 0b10,
    }

    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>
    /// Whether copies get unique names. Stock's <c>AllowNameFixup</c>, and it
    /// decides three separate things: whether the group's internal entity I/O is
    /// rewritten at load, which copy of the map data is precached, and which is
    /// parsed at spawn. All three have to agree or a group is precached as one
    /// thing and spawned as another.
    /// </summary>
    public bool AllowNameFixup => !Flags.HasFlag(SpawnFlag.PreserveNames);

    /// <summary>
    /// Whether the entities this template is made of are removed from the map.
    ///
    /// The default, and the right one: a template SOURCE is a drawing of what to
    /// spawn, not a thing that should be standing there as well.
    /// </summary>
    public bool RemovesItsSources => !Flags.HasFlag(SpawnFlag.KeepSources);

    /// <summary>Fired by ForceSpawn once a copy of the group is in the world.</summary>
    public readonly Output OnEntitySpawned = new("OnEntitySpawned");

    /// <summary>The "Template01".."Template16" keys, in slot order.</summary>
    private readonly string?[] _sourceNames = new string?[MaxTemplates];

    /// <summary>
    /// What this template holds: the store's index for each source, and where
    /// that source stood RELATIVE TO THIS ENTITY.
    ///
    /// Relative and not absolute, and that is the whole design. An instance is
    /// placed somewhere else entirely, and the group has to arrive with its own
    /// shape intact — the crate still on top of the barrel, both still facing
    /// the way they were drawn.
    /// </summary>
    private readonly List<int> _indices = new();
    private readonly List<Transform> _stored = new();

    /// <summary>How many templates this holds. Zero until the map pass runs.</summary>
    public int TemplateCount => _indices.Count;

    /// <summary>
    /// The sixteen "Template01".."Template16" keys. Sixteen [ScriptVars] keys
    /// would say the same thing far less clearly, and the SLOT is what the order
    /// of capture depends on — a group is claimed key by key, and stock's
    /// warning about a key that named nothing is per key.
    /// </summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (key.Length == 10 && key.StartsWith("template", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(8), out int slot)
            && slot >= 1 && slot <= MaxTemplates)
        {
            _sourceNames[slot - 1] = value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Claims the entities this template names, and turns them into templates.
    ///
    /// <para>
    /// This runs INSIDE the map parse, in the window between the map's entities
    /// being read and being spawned — the map loader spawns point_templates
    /// there deliberately, exactly as stock did, because a source has to be
    /// claimed before it spawns and before another template claims it. Outside
    /// that window <see cref="IEngineScripted.TemplateCapture"/> answers
    /// nothing, which is what a template created by <c>ent_create</c> sees.
    /// </para>
    ///
    /// <para>
    /// The two calls at the end are not optional extras. Without the reconnect,
    /// a group whose entities are wired to each other spawns copies still wired
    /// to the ORIGINALS — which have just been eaten — so the copies do nothing.
    /// Without the precache, the first instance loads its models and sounds
    /// mid-game.
    /// </para>
    /// </summary>
    public override void OnSpawn()
    {
        // Through the injected entity area rather than the base class's Origin,
        // which reads the process-wide Engine. Both answer the same thing in the
        // game; only one of them can be stubbed, and a test standing on the
        // other is told (0,0,0) whether or not this line was ever reached.
        Vector origin = Entities.GetOrigin(Handle);
        QAngle angles = QAngle.FromVector(Entities.GetAngles(Handle));

        for (int slot = 0; slot < MaxTemplates; slot++)
        {
            string? name = _sourceNames[slot];
            if (string.IsNullOrEmpty(name))
                continue;

            // THROUGH THE STORE AND NOT STRAIGHT AT THE ENGINE -- plan_entity.md
            // step 2d. The window a template claims in is MapSpawner's now,
            // because MapSpawner is what holds the map text; TemplateStore.Capture
            // asks it when a map is being spawned and falls through to the engine
            // when one is not, which is the ent_create path and is unchanged.
            var found = Templates.Capture(Scripted, name, RemovesItsSources);

            if (found.Count == 0)
            {
                // Stock's mapmaker warning, and it earns its place: a template
                // that named nothing is silent at runtime and shows up as a
                // spawner that does nothing, hours later.
                Log.Warning($"[SourceSharp] point_template '{Name}': nothing in the map is " +
                            $"named '{name}'\n");
                continue;
            }

            foreach (var source in found)
            {
                _indices.Add(source.Index);
                _stored.Add(Transform.Relative(origin, angles, source.Origin, source.Angles));
            }
        }

        if (_indices.Count == 0)
            return;

        Templates.ReconnectIoForGroup(_indices, AllowNameFixup);
        Templates.SchedulePrecache(_indices, AllowNameFixup);
    }

    /// <summary>
    /// Puts a copy of everything this template holds into the world, at a place
    /// and a facing.
    ///
    /// <para>
    /// The group keeps its shape: each source is placed by composing its stored
    /// relative transform with the one asked for, so instancing a template at
    /// its own position and angles puts every source back exactly where it was
    /// drawn.
    /// </para>
    /// </summary>
    /// <returns>
    /// What was created, from either entity hierarchy. Empty when this template
    /// holds nothing, or when a template failed to parse — which is stock's
    /// all-or-nothing answer rather than a half-spawned group.
    /// </returns>
    public List<EntityHandle> CreateInstance(Vector origin, QAngle angles)
    {
        if (_indices.Count == 0)
        {
            Log.Warning($"[SourceSharp] point_template '{Name}': asked for an instance and it " +
                        "holds no templates\n");
            return new List<EntityHandle>();
        }

        // A NEW INSTANCE NUMBER FIRST, and it is what makes the entity I/O
        // inside this copy of the group point at THIS copy rather than at the
        // previous one's. Stock burned it inside CreateInstance for the same
        // reason; the counter moved here with the store.
        Templates.StartUniqueInstance();

        var spawns = new List<KeyedSpawn>(_indices.Count);

        for (int i = 0; i < _indices.Count; i++)
        {
            var (where, facing) = _stored[i].Placed(origin, angles);

            // PARSED HERE, not across the boundary (plan_server_novalve.md P5):
            // the chosen copy of the block, read with MapEntityBlock -- every
            // pair in block order, classname included, which is what
            // CEntityMapData's walk handed the entity when native parsed it.
            var block = new MapEntityBlock(Templates.InstanceMapData(_indices[i], AllowNameFixup), Log,
                                           Templates.StringSize(_indices[i]));

            if (!block.ExtractValue("classname", out string className) || className.Length == 0)
            {
                // Stock's all-or-nothing answer: one unreadable template, no group.
                Log.Warning($"[SourceSharp] point_template '{Name}': template {_indices[i]} has no " +
                            "classname; nothing was spawned\n");
                return new List<EntityHandle>();
            }

            var keys = new List<KeyValuePair<string, string>>();
            if (block.GetFirstKey(out string key, out string value))
            {
                do
                {
                    keys.Add(new(key, value));
                }
                while (block.GetNextKey(out key, out value));
            }

            spawns.Add(new KeyedSpawn(className, keys, where, facing));
        }

        return Scripted.TemplateSpawnKeyed(spawns);
    }

    /// <summary>
    /// Spawns a copy of the group where this entity stands.
    ///
    /// <para>
    /// The output fires only when something was actually made, which is stock's
    /// behaviour: a map wiring OnEntitySpawned to a counter should not see the
    /// count rise for a template that spawned nothing.
    /// </para>
    /// </summary>
    [ScriptedEvent("ForceSpawn")]
    private void OnForceSpawn()
    {
        var made = CreateInstance(Entities.GetOrigin(Handle),
                                  QAngle.FromVector(Entities.GetAngles(Handle)));

        if (made.Count == 0)
            return;

        OnEntitySpawned.Fire();
    }
}
