using SourceSharp.Shared;

namespace SourceSharp.MapGen;

/// <summary>
/// One station's plot of floor, and the only way its contents get added.
///
/// Everything a station places goes through here so three things are true
/// without any station having to remember them: names are unique and prefixed,
/// positions are relative to the plot rather than absolute, and the reset relay
/// is wired up whether or not the station uses it.
/// </summary>
public sealed class Site
{
    // ------------------------------------------------------------------
    // Materials.
    //
    // Stock HL2 content, and every one of them checked against
    // docs/hl2-materials.tsv rather than picked by eye. That file is the
    // catalogue of every material this mod can reach at runtime — 5,204 of
    // them, out of the four VPKs mod_sharp/gameinfo.txt mounts — with the
    // facts that decide whether a name can be used at all. Regenerate it with
    // `python3 tools/matcat.py`.
    //
    // FOUR RULES DECIDED THIS PALETTE, and MaterialCatalogueTests enforces
    // each of them against the catalogue, so a future substitution cannot
    // quietly break one:
    //
    // 1. `use` must be `brush`. A VertexLitGeneric material shades per-vertex
    //    from a model's lighting origin; on a world face there is neither a
    //    lighting origin nor a lightmap, and the face draws flat and usually
    //    black. That failure looks like broken lighting, not a wrong material,
    //    which is exactly why it is worth a column and a test.
    //
    // 2. `tex` must be `ok` — every .vtf the .vmt names has to exist in the
    //    mounted packs. A name that resolves to nothing is a purple
    //    checkerboard in a running game and nothing earlier says so.
    //
    // 3. No $envmap on anything structural. This map has no env_cubemap in it,
    //    and a cubemapped surface with no cubemap built draws the default
    //    black reflection over itself. Glass and water are the two exceptions
    //    below, each for a reason written beside it.
    //
    // 4. A different $surfaceprop per surface a player can stand on, and
    //    exactly ONE of them metal. See Metal.
    //
    // Spelled in lower case because that is how the catalogue spells them and
    // a name is meant to be copied out of it verbatim. vbsp does not care; the
    // TOOLS/ constants are left in upper case rather than churning them.
    // ------------------------------------------------------------------

    /// <summary>
    /// The hall's own floor: one slab under the whole grid, and the largest
    /// surface in the map by a long way.
    ///
    /// Plain, matte and light on purpose. Everything the map is FOR — props
    /// settling, bullet decals, debris, ragdolls — has to read against this,
    /// so a busy or dark floor costs more than it looks like it does.
    /// </summary>
    public const string Floor   = "concrete/concretefloor001a";

    /// <summary>
    /// The hall's ceiling bands. Different from <see cref="Floor"/> only so
    /// that up and down are told apart at a glance in a screenshot, which they
    /// were not when both were the same measurement grid.
    /// </summary>
    public const string Overhead = "plaster/plasterceiling003a";

    /// <summary>
    /// The hall's four walls: the shell, and nothing a station places.
    ///
    /// Split from <see cref="Wall"/> because the two used to be the same
    /// material and a func_door standing against a wall was invisible until it
    /// moved. The shell is quiet so that the contents are not.
    /// </summary>
    public const string Hall    = "plaster/plasterwall021a";

    /// <summary>
    /// What stations build with: pedestals, blockers, movers, platforms,
    /// doors, breakable panels.
    ///
    /// Wood, and chosen for being unlike every other surface in the map rather
    /// than for realism — a wooden lift is odd, and a lift you can tell apart
    /// from the floor it rises out of is worth more in a test map. It also
    /// gives a third walkable game material (W) for the platforms and lift
    /// tops, and wood impacts under gunfire that are not the floor's.
    /// </summary>
    public const string Wall    = "wood/woodwall014a";

    public const string Trigger = "TOOLS/TOOLSTRIGGER";

    /// <summary>
    /// Each station's 512-unit plot of ground, laid over <see cref="Floor"/>.
    ///
    /// Tile, so that walking from the hall onto a station changes the footstep
    /// sound as well as the picture — game material T against the floor's C.
    /// </summary>
    public const string Pad     = "tile/tilefloor001a";

    /// <summary>
    /// The button face. A control panel, so it reads as pressable.
    ///
    /// The obvious HL2 choices — props/doorbutton002a, props/elevatorbutton003b
    /// — are all $surfaceprop "metalpanel", which is game material 'M'. See
    /// <see cref="Metal"/> for why exactly one surface in this map may be that.
    /// This is the only material in the mounted content that draws a control
    /// panel and is not metal: it is $surfaceprop "computer", game material
    /// 'P'. HL1-era art, shipped in hl2_misc, and distinct from everything
    /// around it — which for a button is the point.
    /// </summary>
    public const string Switch  = "halflife/lab1_cmpm5";

    /// <summary>
    /// The plate you stand on in front of each pedestal. Deliberately the
    /// loudest thing in the room.
    ///
    /// <para>
    /// Carpet: unlike anything else here to look at, and audibly different
    /// underfoot, so arriving at a station is announced twice. The station
    /// markers were added because the first version of this map was
    /// unfindable — the button was grey on a grey plinth — and this is the
    /// same problem solved with a material instead of a light.
    /// </para>
    ///
    /// <para>
    /// props/hazardstrip001a is the obvious choice for "loudest", and it is
    /// wrong: $surfaceprop "metalpanel". A plate at EVERY station made of
    /// game material 'M' would fire the surfaces station's
    /// env_player_surface_trigger from the other end of the hall. See
    /// <see cref="Metal"/>.
    /// </para>
    /// </summary>
    public const string Accent  = "props/carpetfloor007a";

    /// <summary>
    /// The one metal surface in the map, and the only material here chosen for
    /// what it is made of rather than how it looks.
    ///
    /// <para>
    /// env_player_surface_trigger watches the player's ground material
    /// GLOBALLY: CEnvPlayerSurfaceTrigger::SetPlayerSurface is called from
    /// game movement and told to every trigger in the map, so a trigger
    /// looking for 'M' fires wherever in the hall the player steps onto metal
    /// — not only inside its own plot. The map used to be entirely
    /// $surfaceprop "concrete", so that station fired nothing at all; the fix
    /// was one metal plate, and the fix stops working the moment there is a
    /// second one somewhere a player can stand.
    /// </para>
    ///
    /// <para>
    /// So the rule the rest of this palette is built around: every walkable
    /// surface has a different $surfaceprop, and exactly one of them —
    /// this — has a metal game material. concrete (C) for the hall floor,
    /// tile (T) for the station pads, wood (W) for platforms and lifts, carpet
    /// (D) for the accent plates, metal (M) here and nowhere else.
    /// A floor texture rather than the wall texture that used to be here,
    /// because it is a plate on the ground and because metalwall058a carries
    /// $envmap, which this map has no cubemap for.
    /// </para>
    /// </summary>
    public const string Metal   = "metal/metalfloor007a";
    public const string NoDraw  = "TOOLS/TOOLSNODRAW";

    /// <summary>
    /// The window the breakables station shatters, and the ONE material in the
    /// game that can show it cracking.
    ///
    /// <para>
    /// func_breakable_surf draws crack sprites by swapping a detail texture into
    /// the variant a material names in $crackmaterial, through a proxy that only
    /// such a material asks for. Without the key the panes still go — the holes
    /// are real and the support arithmetic still runs — but their edges are
    /// clean rather than jagged, which made the one station whose purpose is
    /// showing glass shatter unable to show it. Stock behaves identically; it is
    /// a property of the material, not of the entity.
    /// </para>
    ///
    /// <para>
    /// glasswindowbreak070a is the only .vmt in hl2_misc that declares one, out
    /// of 18,574 entries — found by walking the VPK directory rather than by
    /// guessing at names, after glasswindow018a (which has a _cracked TEXTURE it
    /// never names) and glasswindow070b (not a material at all) both failed.
    /// The catalogue now records that as a `crackmaterial` flag, so the claim
    /// is greppable rather than remembered: `awk -F'\t' '$8 ~ /crackmaterial/'`
    /// over docs/hl2-materials.tsv still returns this one row, and
    /// MaterialCatalogueTests asserts it.
    /// </para>
    ///
    /// <para>
    /// The one drawn material here that carries $envmap, against the rule at
    /// the top of this file. Unavoidable: it is the only material that can
    /// crack, and the station exists to show cracking. A pane of glass is also
    /// the one surface where a black reflection reads as glass rather than as
    /// a fault.
    /// </para>
    /// </summary>
    public const string Glass   = "glass/glasswindowbreak070a";

    private readonly VmfMap _map;
    private readonly Dictionary<string, VmfEntity> _named = [];
    private int _serial;

    public Site(VmfMap map, string slug, Point at)
    {
        _map = map;
        Slug = slug;
        At = at;
    }

    /// <summary>Where the plot is: the centre of its floor.</summary>
    public Point At { get; }

    public string Slug { get; }

    /// <summary>A name inside this station, prefixed so two stations cannot collide.</summary>
    public string Name(string suffix) => $"{Slug}_{suffix}";

    /// <summary>The relay every reset button pulls. Named so stations can wire to it.</summary>
    public string ResetName => Name("reset");

    /// <summary>Adds a point entity, positioned relative to the plot.</summary>
    public VmfEntity Ent(string className, string suffix, Point offset, params string[] keyValues)
    {
        var entity = new VmfEntity { ClassName = className };

        if (suffix.Length > 0)
            entity.Set("targetname", Name(suffix));

        entity.Set("origin", (At + offset).ToString());

        Apply(entity, keyValues);
        Remember(entity);

        return entity;
    }

    /// <summary>
    /// The brush classes that are meaningless without an origin, and so are
    /// given the centre of their own box when the station does not say.
    ///
    /// <para>
    /// vbsp only offsets a brush model's planes when the entity carries an
    /// <c>origin</c> key, and in Hammer that key is written for you from a
    /// tools/toolsorigin brush. A generated map has no Hammer, so a brush entity
    /// without the key has its origin at the WORLD origin however far away its
    /// geometry is. Most brushes do not care -- a door describes its travel as
    /// an offset and a trigger only needs its shape -- but these three kinds do,
    /// and they fail silently: anything that ROTATES sweeps its geometry around
    /// the middle of the map rather than around itself, a func_conveyor looks
    /// for riders in a box around the world origin and finds none, and a
    /// func_tracktrain puts its origin on a node and parks its brush an entire
    /// map-width away.
    /// </para>
    ///
    /// <para>
    /// Defaulted rather than required, because "you forgot an origin" is not a
    /// thing the generator can notice after the fact and the .vmf looks correct
    /// either way -- the failure is only visible in a running game. A station
    /// that wants a hinge at an edge, or a train drawn on its first node, writes
    /// <c>origin</c> itself and that value wins: <see cref="Apply"/> runs after
    /// this and overwrites.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> NeedsOrigin =
    [
        EntityNames.FuncRotating,
        EntityNames.FuncDoorRotating,
        EntityNames.FuncRotButton,
        EntityNames.MomentaryRotButton,
        EntityNames.FuncPlatrot,
        EntityNames.FuncTrackchange,
        EntityNames.FuncTrackautochange,
        EntityNames.FuncConveyor,
        EntityNames.FuncTrain,
        EntityNames.FuncTracktrain,
    ];

    /// <summary>Adds a brush entity: a box, relative to the plot, with its own solid.</summary>
    public VmfEntity Brush(string className, string suffix, Point mins, Point maxs,
                           string material, params string[] keyValues)
    {
        var entity = new VmfEntity { ClassName = className };

        if (suffix.Length > 0)
            entity.Set("targetname", Name(suffix));

        entity.Solids.Add(VmfMap.Box(At + mins, At + maxs, material));

        // Before Apply, so a station that writes its own origin still wins.
        if (NeedsOrigin.Contains(className))
            entity.Set("origin", (At + Middle(mins, maxs)).ToString());

        Apply(entity, keyValues);
        Remember(entity);

        return entity;
    }

    private static Point Middle(Point mins, Point maxs)
        => new((mins.X + maxs.X) * 0.5f, (mins.Y + maxs.Y) * 0.5f, (mins.Z + maxs.Z) * 0.5f);

    /// <summary>
    /// Adds a brush entity drawn on ONE face, the rest nodraw.
    ///
    /// func_breakable_surf is why this exists: vbsp derives its pane grid from a
    /// single drawn quad and rejects anything else.
    /// </summary>
    public VmfEntity Pane(string className, string suffix, Point mins, Point maxs,
                          string material, string face, params string[] keyValues)
    {
        var entity = new VmfEntity { ClassName = className };

        if (suffix.Length > 0)
            entity.Set("targetname", Name(suffix));

        Point lo = At + mins, hi = At + maxs;

        entity.Solids.Add(VmfMap.Pane(lo, hi, material, face));

        // The four corners of the drawn quad, in world space.
        //
        // These come from HAMMER, not from vbsp: tying a brush to
        // func_breakable_surf runs a helper that works them out and writes them
        // as keyvalues. A generated map has no Hammer to do that, so it has to
        // compute them itself — and without them the entity spawns with a
        // zero-sized surface, divides zero by zero working out its pane width,
        // and networks the NaN.
        foreach (var (key, corner) in Corners(lo, hi, face))
            entity.Set(key, corner.ToString());

        Apply(entity, keyValues);
        Remember(entity);

        return entity;
    }

    /// <summary>
    /// The four corners of a box's named face, as func_breakable_surf wants
    /// them: lower-left, upper-left, lower-right, upper-right.
    ///
    /// "Left" and "right" are from the point of view of somebody looking AT the
    /// face, which is the only reading that makes a consistent quad — the
    /// entity derives its width and height vectors from these, and a set that
    /// crosses over gives it a surface with a negative dimension.
    /// </summary>
    private static IEnumerable<(string Key, Point Corner)> Corners(Point lo, Point hi, string face)
    {
        (Point ll, Point ul, Point lr, Point ur) = face switch
        {
            // Looking along +Y at a face whose normal is -Y: +X is to the right.
            "-y" => (new Point(lo.X, lo.Y, lo.Z), new Point(lo.X, lo.Y, hi.Z),
                     new Point(hi.X, lo.Y, lo.Z), new Point(hi.X, lo.Y, hi.Z)),

            "+y" => (new Point(hi.X, hi.Y, lo.Z), new Point(hi.X, hi.Y, hi.Z),
                     new Point(lo.X, hi.Y, lo.Z), new Point(lo.X, hi.Y, hi.Z)),

            "-x" => (new Point(lo.X, hi.Y, lo.Z), new Point(lo.X, hi.Y, hi.Z),
                     new Point(lo.X, lo.Y, lo.Z), new Point(lo.X, lo.Y, hi.Z)),

            "+x" => (new Point(hi.X, lo.Y, lo.Z), new Point(hi.X, lo.Y, hi.Z),
                     new Point(hi.X, hi.Y, lo.Z), new Point(hi.X, hi.Y, hi.Z)),

            _ => throw new ArgumentException($"'{face}' is not a vertical face; a breakable "
                                             + "surface needs one", nameof(face)),
        };

        yield return ("lowerleft", ll);
        yield return ("upperleft", ul);
        yield return ("lowerright", lr);
        yield return ("upperright", ur);
    }

    /// <summary>Adds world geometry -- scenery, not an entity.</summary>
    public void Solid(Point mins, Point maxs, string material, string? top = null)
        => _map.WorldSolids.Add(VmfMap.Box(At + mins, At + maxs, material, top));

    /// <summary>
    /// Marks somewhere the player is meant to act: a beam pointing straight down
    /// at it, with a halo where it lands.
    ///
    /// point_spotlight rather than a sprite because env_sprite is not
    /// registered in this build, and rather than a light because the hall is
    /// already bright enough -- spawnflag 2 is "no dynamic light", so this adds
    /// a visible beam and contributes nothing to the lighting.
    ///
    /// Interaction points are otherwise invisible by construction: a trigger has
    /// no surface, and a button is a small brush among a lot of similar ones.
    /// </summary>
    public VmfEntity Marker(string suffix, Point above, string colour, float length = 120f)
        => Ent("point_spotlight", suffix, above + new Point(0f, 0f, length),
               "angles", "90 0 0",
               "SpotlightLength", Vmf(length),
               "SpotlightWidth", "14",
               "rendercolor", colour,
               "renderamt", "180",
               "HDRColorScale", "0.7",
               "IgnoreSolid", "1",
               // 1 = on at spawn, 2 = contribute no dynamic light.
               "spawnflags", "3");

    private static string Vmf(float f)
        => f.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An anonymous name, for the entities a station needs several of.</summary>
    public string Anon(string stem) => Name($"{stem}{++_serial}");

    /// <summary>
    /// Wires the station's reset relay to one of its own entities.
    ///
    /// Stations reset through the relay rather than through the button, so a
    /// station never has to know how the reset was asked for -- and anything
    /// else that wants to reset one has a name to fire.
    /// </summary>
    public void OnReset(string suffix, string input, string parameter = "", float delay = 0f)
        => Find(ResetName).Wire("OnTrigger", Name(suffix), input, parameter, delay);

    /// <summary>Wires one of the station's entities to something else.</summary>
    public void Wire(string fromSuffix, string output, string target, string input,
                     string parameter = "", float delay = 0f)
        => Find(Name(fromSuffix)).Wire(output, target, input, parameter, delay);

    private void Remember(VmfEntity entity)
    {
        _map.Entities.Add(entity);

        string name = entity.Name;

        if (name.Length == 0)
            return;

        // Two entities sharing a name is legal in Source and occasionally
        // wanted, but in this map it is always a mistake -- every reset is
        // wired by name, and a duplicate would silently fire both.
        if (!_named.TryAdd(name, entity))
            throw new InvalidOperationException($"station {Slug} declares '{name}' twice");
    }

    private VmfEntity Find(string name)
        => _named.TryGetValue(name, out var entity)
            ? entity
            : throw new InvalidOperationException($"station {Slug} has no entity named '{name}'");

    private static void Apply(VmfEntity entity, string[] keyValues)
    {
        if (keyValues.Length % 2 != 0)
            throw new ArgumentException("keyvalues come in pairs", nameof(keyValues));

        for (int i = 0; i < keyValues.Length; i += 2)
            entity.Set(keyValues[i], keyValues[i + 1]);
    }
}
