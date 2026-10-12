//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>What one demonstration argument is, and therefore how it is written.</summary>
public enum TeArgKind
{
    /// <summary>A literal integer, at the field's declared storage width.</summary>
    Int,

    /// <summary>A literal float.</summary>
    Float,

    /// <summary>Three floats, optionally offset from where the effect is fired.</summary>
    Vector,

    /// <summary>One element of the inventory's single array field.</summary>
    Element,

    /// <summary>A model index, resolved by name at fire time.</summary>
    Model,

    /// <summary>A decal index, resolved by name at fire time.</summary>
    Decal,

    /// <summary>An <c>EffectDispatch</c> string table index, resolved by name at fire time.</summary>
    EffectName,

    /// <summary>An EHandle nothing owns — <c>INVALID_NETWORKED_EHANDLE_VALUE</c> on the wire.</summary>
    NoOwner,

    /// <summary>
    /// A surface-property index, written as <c>value + 1</c>.
    ///
    /// <para>
    /// ITS OWN KIND BECAUSE THE ARITHMETIC IS THE PROXY. Stock declares
    /// <c>m_nSurfaceProp</c> with <c>SendProxy_ShortAddOne</c>
    /// (<c>sendproxy.cpp:46</c>), which adds one so that -1 — "no surface" —
    /// survives an UNSIGNED eight-bit prop as a zero. A send proxy never appears
    /// in the table the client pairs against (plan 2B), so the +1 moved to the
    /// send side; <c>TempEnts.DispatchEffect</c> applies it in a line of code and
    /// this book applies it in a kind. Written as a plain
    /// <see cref="Int"/> of 0 the value would be right and the REASON would be
    /// gone, and the next person to want a real surface property would write it
    /// off by one.
    /// </para>
    /// </summary>
    SurfaceProp,
}

/// <summary>
/// One field of one demonstration, as DATA rather than as a line of code.
///
/// <para>
/// The alternative — a lambda per class calling <see cref="TempEntWriter"/> —
/// cannot be checked without an engine, and the thing most worth checking here
/// is that every name in the book is a name the declaration actually has. A
/// misspelled wire name warns at fire time and writes nothing, which on screen
/// is an effect that happens somewhere else or not at all; as data it is a
/// dictionary lookup a <c>[Fact]</c> can do with no game running, which is what
/// <c>SandboxTeTests</c> does.
/// </para>
/// </summary>
public sealed record TeArg(TeArgKind Kind, string Field)
{
    /// <summary><see cref="TeArgKind.Int"/>'s value.</summary>
    public int Number { get; init; }

    /// <summary><see cref="TeArgKind.Float"/>'s value.</summary>
    public float Value { get; init; }

    /// <summary><see cref="TeArgKind.Vector"/>'s and <see cref="TeArgKind.Element"/>'s value.</summary>
    public Vector Offset { get; init; }

    /// <summary>
    /// Whether <see cref="Offset"/> is measured from where the effect is fired.
    ///
    /// <para>
    /// TRUE FOR EVERY POSITION AND FALSE FOR EVERY DIRECTION, which is the whole
    /// of why it is a flag rather than a convention. An <c>ss_te_tester</c> fires
    /// at its own origin (plan 9.1), so a position written absolutely would put
    /// forty-three effects in one heap wherever the book happened to say —
    /// and a direction written relatively would point at the map's origin
    /// instead of upwards.
    /// </para>
    /// </summary>
    public bool Relative { get; init; }

    /// <summary><see cref="TeArgKind.Element"/>'s index into the array.</summary>
    public int Index { get; init; }

    /// <summary>The model, decal or effect name, for the three kinds that resolve one.</summary>
    public string Name { get; init; } = "";

    public static TeArg Int(string field, int value)
        => new(TeArgKind.Int, field) { Number = value };

    public static TeArg Float(string field, float value)
        => new(TeArgKind.Float, field) { Value = value };

    /// <summary>A position, relative to where the effect is fired.</summary>
    public static TeArg At(string field, float x, float y, float z)
        => new(TeArgKind.Vector, field) { Offset = new Vector(x, y, z), Relative = true };

    /// <summary>A direction, a set of angles or a size — a vector that is not a place.</summary>
    public static TeArg Dir(string field, float x, float y, float z)
        => new(TeArgKind.Vector, field) { Offset = new Vector(x, y, z), Relative = false };

    /// <summary>One element of an array field, as a position.</summary>
    public static TeArg Element(string field, int index, float x, float y, float z)
        => new(TeArgKind.Element, field) { Index = index, Offset = new Vector(x, y, z), Relative = true };

    public static TeArg Model(string field, string model)
        => new(TeArgKind.Model, field) { Name = model };

    public static TeArg Decal(string field, string decal)
        => new(TeArgKind.Decal, field) { Name = decal };

    public static TeArg Effect(string field, string effect)
        => new(TeArgKind.EffectName, field) { Name = effect };

    public static TeArg NoOwner(string field)
        => new(TeArgKind.NoOwner, field);

    /// <summary>A surface-property index. The +1 is applied when it is written.</summary>
    public static TeArg SurfaceProp(string field, int value)
        => new(TeArgKind.SurfaceProp, field) { Number = value };
}

/// <summary>
/// One temp entity, and what makes it look like something.
/// </summary>
/// <param name="ClassName">
/// What the two sides pair on, and what an <c>ss_te_tester</c>'s <c>te</c>
/// keyvalue names: <c>"CTESparks"</c>.
/// </param>
/// <param name="Slug">
/// A short name for the same thing, used for the station's entity names and as
/// <c>cs_te_fire</c>'s argument. Derived from nothing — written down — because
/// stripping <c>CTE</c> would give <c>bspdecal</c> and <c>bsp_decal</c>
/// depending on who did it, and this is the name a person types.
/// </param>
/// <param name="What">One line, for the console and for the station's board.</param>
/// <param name="Args">Every field this demonstration writes.</param>
public sealed record TeDemo(string ClassName, string Slug, string What, TeArg[] Args);

/// <summary>
/// One demonstration of every temp entity, and the one place that knows what a
/// plausible one looks like.
///
/// <para>
/// WHY THERE IS A BOOK AT ALL. A temp entity has no classname and no factory, so
/// nothing in the sandbox's coverage machinery can see one and nothing in the
/// build could demonstrate that the forty-three ported effects still fire. plan
/// 9.1's answer is an ordinary <c>[ScriptedEntity]</c> that fires one — which
/// needs to know, per class, which fields make that class look like anything.
/// That knowledge is here, once, and <see cref="TeTester"/>, <c>cs_te_fire</c>
/// and <c>SandboxTeTests</c> all read it. Two copies of it would be two answers
/// to "what does a CTEExplosion look like", and the map and the gate would drift.
/// </para>
///
/// <para>
/// THE VALUES ARE THE SUPERSEDED COMMANDS', CARRIED OVER RATHER THAN INVENTED.
/// Each lane wrote a scaffolding command with its own chosen arguments and a
/// note beside them saying what the choice was for — a non-zero pitch because
/// <c>SendPropAngle</c> anglemods and a plain float would not, six DISTINCT
/// colour bytes because six equal ones pass whatever order the props arrive in,
/// <c>exponent 5</c> because it is the one signed prop and small enough that an
/// unsigned declaration would have looked correct. Those commands go with this
/// pass (plan 9.1 supersedes them); the reasoning does not, so it is here.
/// </para>
///
/// <para>
/// ONE BUG IS DELIBERATELY NOT INHERITED. <c>cs_sv_te_fire_f muzzleflash</c>
/// printed <c>type 0</c> while passing <c>MUZZLEFLASH_TYPE_DEFAULT</c>, which is
/// <c>0x00000001</c> (<c>shareddefs.h:335</c>). The number below is 1 and the
/// text that prints comes from the same table, so the two cannot disagree again.
/// </para>
///
/// <para>
/// EVERY DEMONSTRATION GOES THROUGH <see cref="TempEntWriter"/> AND
/// <c>TempEntPlayback</c> — the managed send path — rather than through
/// <c>ITempEnts</c> where a method happens to exist or through native
/// <c>te-&gt;</c> where one does not. That is the only route all forty-three
/// share: <c>ITempEnts</c> has twenty-one methods, and the beams, sprites and
/// most of the gore reach the wire through native forwards. A book that used
/// three routes would be measuring three things and reporting one number. The
/// native chain keeps its own provers — the live callers, the gtests, and
/// <c>cs_sv_te_dispatch</c>, which is kept for a reason this cannot cover.
/// </para>
/// </summary>
public static class TeDemos
{
    //-------------------------------------------------------------------------
    // The models. Every one is precached for every level already, so resolving
    // one by name at fire time answers the index the string table holds rather
    // than claiming a new one -- which is what SourceSharp_BubbleEffect and
    // cs_sv_te_fizz already do (shell_worldquery.cpp:2033).
    //
    // Six come from W_Precache (world_adapter.cpp:105) and three from
    // TempEnts.Precache, and the difference matters: the second list is the one
    // that MOVED to managed code when the te_*.cpp files were evicted, so a zero
    // index for one of those three is the precache having lapsed rather than a
    // detail of the demonstration. Fire() says exactly that when it sees one.
    //-------------------------------------------------------------------------

    /// <summary>g_pModelNameLaser. What every stock beam caller draws with.</summary>
    public const string Laser = "sprites/laserbeam.vmt";

    /// <summary>CTESprite::Precache's own model, and TempEnts.Precache's first line.</summary>
    public const string Smoke = "sprites/gunsmoke.vmt";

    /// <summary>CTEFizz::Precache's own model. Also g_sModelIndexBubbles.</summary>
    public const string Bubble = "sprites/bubble.vmt";

    /// <summary>CTEBreakModel's and CTEPhysicsProp's shared precache.</summary>
    public const string Gibs = "models/gibs/hgibs.mdl";

    /// <summary>g_sModelIndexBloodSpray.</summary>
    public const string BloodSpray = "sprites/bloodspray.vmt";

    /// <summary>g_sModelIndexBloodDrop.</summary>
    public const string BloodDrop = "sprites/blood.vmt";

    /// <summary>
    /// The decal the six decal tables stamp.
    ///
    /// <para>
    /// A SIGN rather than a bullet hole, and that is not decoration: the point
    /// of firing a decal in a demonstration map is being able to see WHERE it
    /// landed, and a scorch mark the size of a coin on a tiled floor is
    /// indistinguishable from nothing having happened. It is one of the two the
    /// sandbox already ships through <c>infodecal</c>, so the map precaches it
    /// whether or not anything fires.
    /// </para>
    /// </summary>
    public const string Mark = "decals/decalsigndanger001a";

    /// <summary>
    /// The effect name <c>CTEEffectDispatch</c>'s demonstration dispatches.
    ///
    /// <para>
    /// <c>"watersplash"</c> because it is one of the four this build's own
    /// callers use and one that a client effect actually claims —
    /// <c>DECLARE_CLIENT_EFFECT("watersplash")</c> is compiled here.
    /// <c>"Impact"</c> would have been the more obvious pick and was BEHAVIOUR
    /// LEDGER ROW C7: it was declared only in <c>tf/</c>, <c>hl2/</c> and
    /// <c>sdk/</c>, so it never played in this build, and a station
    /// demonstrating it would have demonstrated a tracked gap while looking
    /// like a broken port. Lane <c>fx-impact</c> has since closed that row —
    /// <c>shell_impact_effects.cpp</c> registers the name — so the choice is no
    /// longer forced. It is left alone because <c>"watersplash"</c> is what the
    /// station's demo assets and its gate are both written against, and
    /// changing the name to take the credit would be a change to a working
    /// station for no gain.
    /// </para>
    /// </summary>
    public const string DispatchName = "watersplash";

    /// <summary>
    /// <c>MUZZLEFLASH_TYPE_DEFAULT</c> — <c>0x00000001</c>, and NOT zero.
    /// <c>shareddefs.h:335</c>. See the class remarks.
    /// </summary>
    public const int MuzzleFlashDefault = 1;

    /// <summary>
    /// <c>CHAR_TEX_CONCRETE</c>. A visible value rather than 0, so a wrong width
    /// arrives as a different number on the client instead of as a zero that
    /// could have come from anywhere.
    /// </summary>
    private const int Concrete = 'C';

    /// <summary>
    /// The entity a beam or a decal is attached to.
    ///
    /// <para>
    /// 0 IS THE WORLD and it is what every decal here uses: a decal on
    /// brushwork needs no entity, and an index naming an entity that is not
    /// there is a decal that goes nowhere.
    /// </para>
    /// </summary>
    private const int World = 0;

    /// <summary>
    /// The entity the entity-attached beams hang off: slot 1, the first player.
    ///
    /// <para>
    /// It is the only edict a headless one-client gate can name and be sure
    /// exists, and it is what <c>cs_sv_te_beam</c> used for the same reason.
    /// A beam whose ends resolve to nothing draws nothing, which from the
    /// console is indistinguishable from a table that did not pair — so the
    /// four entity-attached beams point at something that is certainly there.
    /// </para>
    /// </summary>
    private const int FirstPlayer = 1;

    /// <summary>
    /// The whole book: one demonstration per registered temp entity.
    ///
    /// <para>
    /// THERE IS NO EXEMPTION LIST (plan 9.2). Firing IS the whole of what a temp
    /// entity does, so a class that cannot be fired from a station has already
    /// failed the port, and there is nothing an exemption could excuse.
    /// <c>SandboxTeTests</c> asserts this list against the declarations rather
    /// than trusting it: a class added to <c>SourceSharp.Shared/TempEnts/</c>
    /// with no row here fails the build.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TeDemo> All { get; } =
    [
        // ---------------------------------------------------------------
        // The three base tables that claim a ServerClass of their own.
        //
        // They are here for the same reason every other row is: they
        // register, they have a handle, and TempEntPlayback will send one.
        // Nothing in stock CREATES a CTEBaseBeam or a CTEParticleSystem --
        // they exist to be nested -- so these are the only things in the
        // build that ever put one on the wire, and a base table whose own
        // encode is wrong would otherwise be visible only through its seven
        // derivatives all being wrong together.
        //
        // CBaseTempEntity is NOT here and cannot be: RegistersClass = false
        // while stock still registers that name (plan 2E), so it has no
        // handle, and its blob is zero bytes -- Engine.TempEntPlayback
        // refuses an empty one. SandboxTeTests derives the same rule from
        // the declaration rather than repeating this paragraph.
        // ---------------------------------------------------------------
        new("CTEParticleSystem", "particlesystem",
            "the base every particle temp entity nests: an origin, and nothing else",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
            ]),

        // AND THIS ONE THE SERVER CANNOT SEND TODAY, which the sandbox pass found
        // by being the first thing in the build ever to try.
        //
        // The declaration claims a ServerClass (RegistersClass defaults true) and
        // the CLIENT gives it one -- handle 6, c_te_basebeam.cpp evicted -- but
        // te_basebeam.cpp is still compiled on the SERVER, so native refuses the
        // managed ServerClass at registration ("the native build still registers
        // one of that name") and SourceSharp_TempEntPlayback then refuses the
        // send, once per fire, naming the class. The table is built either way,
        // which is why the seven derived beams pair and work.
        //
        // KEPT, NOT EXEMPTED -- and the disagreement it was kept for is CLOSED.
        // The note here used to read "THE SERVER CANNOT SEND ONE while
        // te_basebeam.cpp is native: native refuses the ServerClass and says so
        // per fire", which is what the sandbox pass measured. Lane SRV evicted
        // te_basebeam.cpp (docs/te-ledger.md's roadmap, step 3), the managed
        // CTEBaseBeam ServerClass registers, and a live join now hands all 43 to
        // native with zero refusals -- with nothing changed in TeBaseBeam.cs,
        // exactly as this comment predicted.
        //
        // What remains is CLIENT-side and is stock's own behaviour rather than a
        // gap: C_TEBaseBeam::PostDataUpdate is Assert( 0 ), because a base is
        // never sent by anything but this command. So it arrives, decodes, and
        // is counted as dropped with one line naming it -- which is that assert
        // in a build where asserts are compiled out.
        new("CTEBaseBeam", "basebeam",
            "the base every beam nests. Nothing but this command ever sends one, so the "
            + "client has no player for it and says so: stock's C_TEBaseBeam::PostDataUpdate "
            + "is Assert( 0 )",
            BaseBeam(255, 255, 255)),

        new("CTEMetalSparks", "metalsparks",
            "a round off metal, and the base CTEArmorRicochet nests",
            [
                TeArg.At("Position", 0f, 0f, 24f),
                TeArg.Dir("Direction", 0f, 0f, 1f),
            ]),

        // ---------------------------------------------------------------
        // Lane A -- the beams. All seven on DT_BaseBeam, so all seven write
        // the base's fifteen props first and their own after. The field names
        // below are TeBaseBeam's members, which is what the wire carries since
        // lane RA dropped the eight declarations' WireNames.
        // ---------------------------------------------------------------
        // BOTH ENDS ZERO, which is the form that always DREW -- and it is
        // cs_sv_te_beam's own choice for this one, kept for the reason it made
        // it. An ent-point beam whose indices are both zero is two POINTS and
        // reaches no entity; while row C8 was open, giving it a real index met
        // the refusal the four below met. The row is closed and a real index
        // works now, but this demonstration keeps the shape it was chosen for.
        new("CTEBeamEntPoint", "beamentpoint",
            "a beam from an entity to a point, with both ends at zero — the form that draws",
            [
                .. BaseBeam(0, 63, 127),
                TeArg.Int("StartEntity", World),
                TeArg.Int("EndEntity", World),
                TeArg.At("StartPoint", 0f, 0f, 16f),
                TeArg.At("EndPoint", 0f, 0f, 112f),
            ]),

        // THE FOUR ENTITY-ATTACHED BEAMS WERE BEHAVIOUR LEDGER ROW C8, AND THEY
        // STILL FIRE AT A REAL INDEX ON PURPOSE.
        //
        // Beam_t::entity[] is re-read every frame and used to be dereferenced
        // through CBaseHandle::Get(), an unfiltered reinterpret of the handle
        // table -- so SourceSharp_BeamCreate* refused a managed entity and said
        // so once per entity index. Entity 1 in this build is ss_player, which
        // IS managed, so all four of these arrived, decoded, and were refused.
        //
        // Lane C8 closed the row by resolving the endpoint through
        // cl_entitylist, which answers for both hierarchies. These four now
        // DRAW, at a managed entity, and the index stays real for the reason it
        // was chosen: an index that resolved to nothing would draw nothing while
        // looking like a beam that simply missed, and that is as unreadable now
        // that the capability works as it was while it did not.
        new("CTEBeamEnts", "beaments",
            "a beam between two entities, and it follows them. Row C8 closed: it draws at a MANAGED entity",
            [
                .. BaseBeam(127, 63, 0),
                TeArg.Int("StartEntity", FirstPlayer),
                TeArg.Int("EndEntity", World),
            ]),

        new("CTEBeamFollow", "beamfollow",
            "a beam trailing an entity. Row C8 closed: entity 1 is ss_player, managed, and it now has a subject",
            [
                .. BaseBeam(255, 255, 255),
                TeArg.Int("EntIndex", FirstPlayer),
            ]),

        new("CTEBeamLaser", "beamlaser",
            "a laser between two entities. Row C8 closed: it draws at a MANAGED entity",
            [
                .. BaseBeam(127, 63, 0),
                TeArg.Int("StartEntity", FirstPlayer),
                TeArg.Int("EndEntity", World),
            ]),

        new("CTEBeamPoints", "beampoints",
            "a beam between two points",
            [
                .. BaseBeam(0, 63, 127),
                TeArg.At("StartPoint", 0f, 0f, 16f),
                TeArg.At("EndPoint", 0f, 0f, 144f),
            ]),

        // The two that use MaxEdict where the five above use a literal 24
        // (plan Part 6, lane A: "do not unify them"). The demonstration is
        // the same shape; the WIDTH is the difference, and it is in the
        // declaration where it belongs.
        new("CTEBeamRing", "beamring",
            "a ring between two entities. Row C8 closed: it draws at a MANAGED entity",
            [
                .. BaseBeam(255, 255, 0),
                TeArg.Int("StartEntity", FirstPlayer),
                TeArg.Int("EndEntity", World),
            ]),

        new("CTEBeamRingPoint", "beamringpoint",
            "a ring expanding around a point",
            [
                .. BaseBeam(255, 255, 0),
                TeArg.At("Center", 0f, 0f, 16f),
                TeArg.Float("StartRadius", 16f),
                TeArg.Float("EndRadius", 192f),
            ]),

        // ---------------------------------------------------------------
        // Lane B -- the decals. Every one of them fires AT THE PAD, which is
        // the tester's own origin: a decal painted in mid-air lands on
        // nothing and reads exactly like a decal that never arrived.
        // ---------------------------------------------------------------
        new("CTEBSPDecal", "bspdecal",
            "a decal sprayed onto brushwork",
            [
                TeArg.At("Origin", 0f, 0f, 0f),
                TeArg.Int("Entity", World),
                TeArg.Decal("Index", Mark),
            ]),

        new("CTEDecal", "decal",
            "the bullet-hole decal: the client re-traces the segment to find the surface",
            [
                TeArg.At("Origin", 0f, 0f, 0f),

                // ABOVE the point and ending AT it. A start below the floor
                // finds the wrong side of it (c_te_decal.cpp:123).
                TeArg.At("Start", 0f, 0f, 64f),
                TeArg.Int("Entity", World),
                TeArg.Int("Hitbox", 0),
                TeArg.Decal("Index", Mark),
            ]),

        new("CTEWorldDecal", "worlddecal",
            "a decal on world brushwork: no entity, no trace, no hitbox",
            [
                TeArg.At("Origin", 0f, 0f, 0f),
                TeArg.Decal("Index", Mark),
            ]),

        new("CTEPlayerDecal", "playerdecal",
            "a player's spray logo. The player is a SLOT, not an edict",
            [
                TeArg.At("Origin", 0f, 0f, 0f),
                TeArg.Int("Entity", World),
                TeArg.Int("Player", FirstPlayer),
            ]),

        new("CTEProjectedDecal", "projecteddecal",
            "a decal thrown along a facing. The inventory's one qangles prop",
            [
                TeArg.At("Origin", 0f, 0f, 64f),

                // Straight down, which is CTEProjectedDecal's own default
                // facing rather than the zeroed one its constructor leaves.
                TeArg.Dir("Rotation", 90f, 0f, 0f),
                TeArg.Float("Distance", 64f),
                TeArg.Decal("Index", Mark),
            ]),

        new("CTEFootprintDecal", "footprintdecal",
            "a footprint. The direction is NORMALISED, or the print stretches",
            [
                TeArg.At("Origin", 0f, 0f, 0f),
                TeArg.Dir("Direction", 0f, 1f, 0f),
                TeArg.Int("Entity", World),
                TeArg.Decal("Index", Mark),
                TeArg.Int("MaterialType", Concrete),
            ]),

        // ---------------------------------------------------------------
        // Lane C -- sprites and light.
        // ---------------------------------------------------------------
        new("CTESprite", "sprite",
            "one sprite, at a point",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Model("ModelIndex", Smoke),
                TeArg.Float("Scale", 2f),
                TeArg.Int("Brightness", 255),
            ]),

        new("CTEGlowSprite", "glowsprite",
            "a sprite that fades. Life comes BEFORE size in the call and after it on the wire",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Model("ModelIndex", Smoke),
                TeArg.Float("Scale", 1.5f),
                TeArg.Float("Life", 2f),
                TeArg.Int("Brightness", 200),
            ]),

        new("CTESpriteSpray", "spritespray",
            "a spray of sprites along a direction",
            [
                TeArg.At("Origin", 0f, 0f, 24f),
                TeArg.Dir("Direction", 0f, 0f, 1f),
                TeArg.Model("ModelIndex", Smoke),
                TeArg.Float("Noise", 0.8f),
                TeArg.Int("Speed", 30),
                TeArg.Int("Count", 5),
            ]),

        new("CTESmoke", "smoke",
            "an animating puff of smoke",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Model("ModelIndex", Smoke),
                TeArg.Float("Scale", 5f),
                TeArg.Int("FrameRate", 12),
            ]),

        new("CTEDynamicLight", "dynamiclight",
            "a light for a moment. exponent is the one SIGNED 8-bit int in the inventory",
            [
                TeArg.At("Origin", 0f, 0f, 48f),
                TeArg.Int("R", 255),
                TeArg.Int("G", 255),
                TeArg.Int("B", 63),

                // 5, which is small and positive -- exactly why an unsigned
                // declaration of this prop would have looked correct.
                TeArg.Int("Exponent", 5),
                TeArg.Float("Radius", 200f),
                TeArg.Float("Time", 2f),
                TeArg.Float("Decay", 0f),
            ]),

        new("CTEShowLine", "showline",
            "a debug line from the origin to a second point",
            [
                TeArg.At("Origin", 0f, 0f, 16f),
                TeArg.At("End", 0f, 0f, 144f),
            ]),

        // ---------------------------------------------------------------
        // Lane D -- gore and fluid.
        // ---------------------------------------------------------------
        new("CTEBloodSprite", "bloodsprite",
            "a blood splash: two models, one spray and one drop",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Dir("Direction", 0f, 0f, 1f),
                TeArg.Int("ColorR", 247),
                TeArg.Int("ColorG", 0),
                TeArg.Int("ColorB", 0),
                TeArg.Int("ColorA", 255),
                TeArg.Model("SprayModel", BloodSpray),
                TeArg.Model("DropModel", BloodDrop),
                TeArg.Int("Size", 16),
            ]),

        new("CTEBloodStream", "bloodstream",
            "a jet of blood. Its direction is an 11-bit vector over [-10,10], not a COORD one",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Dir("Direction", 0f, 0f, 1f),
                TeArg.Int("ColorR", 247),
                TeArg.Int("ColorG", 0),
                TeArg.Int("ColorB", 0),
                TeArg.Int("ColorA", 255),
                TeArg.Int("Amount", 100),
            ]),

        new("CTEArmorRicochet", "armorricochet",
            "a round off armour. Declares NO props of its own: all of it is DT_TEMetalSparks",
            [
                TeArg.At("Position", 0f, 0f, 24f),
                TeArg.Dir("Direction", 0f, 0f, 1f),
            ]),

        new("CTEEnergySplash", "energysplash",
            "a round off an energy shield",
            [
                TeArg.At("Position", 0f, 0f, 24f),
                TeArg.Dir("Direction", 0f, 0f, 1f),

                // One bit on the wire and one byte in the blob. Explosive,
                // because the client branches on it and a zero would have
                // played the smaller version and still looked like a pass.
                TeArg.Int("Explosive", 1),
            ]),

        new("CTEBubbles", "bubbles",
            "bubbles rising through a box of water",
            [
                TeArg.At("Mins", -32f, -32f, 0f),
                TeArg.At("Maxs", 32f, 32f, 128f),
                TeArg.Model("ModelIndex", Bubble),
                TeArg.Float("Height", 128f),
                TeArg.Int("Count", 50),
                TeArg.Float("Speed", 2f),
            ]),

        new("CTEBubbleTrail", "bubbletrail",
            "a trail of bubbles under a water line",
            [
                TeArg.At("Mins", -32f, -32f, 0f),
                TeArg.At("Maxs", 32f, 32f, 128f),
                TeArg.Model("ModelIndex", Bubble),
                TeArg.Float("WaterZ", 0f),
                TeArg.Int("Count", 20),
                TeArg.Float("Speed", 8f),
            ]),

        // BEHAVIOUR LEDGER ROW S1/C6, and the demonstration says so on the
        // station board rather than looking broken. The server half is real
        // and this sends it; c_te_fizz.cpp resolves m_nEntity through
        // cl_entitylist->GetEnt, which answers NULL for every entity on the
        // parallel hierarchy, so what arrives is dropped. Tracked, not silent.
        new("CTEFizz", "fizz",
            "bubbles fizzing off a brush. LEDGER ROW C6: it arrives and the client drops it",
            [
                TeArg.Int("Entity", World),
                TeArg.Model("ModelIndex", Bubble),
                TeArg.Int("Density", 200),

                // SIGNED sixteen bits. Negative on purpose: an unsigned
                // declaration would carry 100 perfectly and this not at all.
                TeArg.Int("Current", -100),
            ]),

        // ---------------------------------------------------------------
        // Lane E -- physical debris.
        // ---------------------------------------------------------------
        new("CTEBreakModel", "breakmodel",
            "a model smashed into gibs",
            [
                TeArg.At("Origin", 0f, 0f, 32f),

                // A NON-ZERO PITCH, deliberately: SendPropAngle anglemods each
                // component into its declared [0,360] on the way out, and a
                // plain float prop would put this outside its own range.
                TeArg.Dir("Rotation", 15f, 90f, 0f),
                TeArg.Dir("Size", 16f, 16f, 16f),
                TeArg.Dir("Velocity", 0f, 0f, 64f),
                TeArg.Model("ModelIndex", Gibs),
                TeArg.Int("Randomization", 100),
                TeArg.Int("Count", 10),
                TeArg.Float("Time", 5f),

                // BREAK_GLASS.
                TeArg.Int("Flags", 1),
            ]),

        new("CTEPhysicsProp", "physicsprop",
            "a prop handed to the client to simulate. m_nSkin is the inventory's AnimationSkin, signed",
            [
                TeArg.At("Origin", 0f, 0f, 48f),
                TeArg.Dir("Rotation", 15f, 90f, 0f),
                TeArg.Dir("Velocity", 0f, 0f, 64f),
                TeArg.Model("ModelIndex", Gibs),

                // 3 -- small and positive, which is exactly why an unsigned
                // m_nSkin would have looked correct here.
                TeArg.Int("Skin", 3),
                TeArg.Int("Flags", 1),
                TeArg.Int("Effects", 0),
            ]),

        new("CTEExplosion", "explosion",
            "the fireball",
            [
                TeArg.At("Origin", 0f, 0f, 48f),
                TeArg.Model("ModelIndex", Smoke),
                TeArg.Float("Scale", 3f),
                TeArg.Int("FrameRate", 15),

                // TE_EXPLFLAG_NONE.
                TeArg.Int("Flags", 0),
                TeArg.Dir("Normal", 0f, 0f, 1f),
                TeArg.Int("MaterialType", Concrete),
                TeArg.Int("Radius", 200),
                TeArg.Int("Magnitude", 100),
            ]),

        new("CTEShatterSurface", "shattersurface",
            "glass coming apart. SIX distinct colour bytes, because six equal ones prove no order",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Dir("Angles", 15f, 90f, 0f),
                TeArg.Dir("Force", 0f, 0f, 40f),
                TeArg.At("ForcePos", 0f, 0f, 32f),
                TeArg.Float("Width", 32f),
                TeArg.Float("Height", 48f),
                TeArg.Float("ShardSize", 4f),

                // SHATTERSURFACE_GLASS.
                TeArg.Int("SurfaceType", 0),
                TeArg.Int("FrontColorR", 10),
                TeArg.Int("FrontColorG", 20),
                TeArg.Int("FrontColorB", 30),
                TeArg.Int("BackColorR", 40),
                TeArg.Int("BackColorG", 50),
                TeArg.Int("BackColorB", 60),
            ]),

        new("CTELargeFunnel", "largefunnel",
            "a cone of sprites pouring into a point. Reversed is an int, not a bool: the prop is two bits",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Model("ModelIndex", Smoke),
                TeArg.Int("Reversed", 1),
            ]),

        new("CTEKillPlayerAttachments", "killplayerattachments",
            "drops everything stuck to a player. The player is a SLOT, five bits",
            [
                TeArg.Int("Player", FirstPlayer),
            ]),

        // ---------------------------------------------------------------
        // Lane F -- the odd ones and the restored three.
        // ---------------------------------------------------------------
        new("CTEBeamSpline", "beamspline",
            "the inventory's ONE array: six of sixteen points written, ten sent as zero",
            [
                TeArg.Int("Points", 6),
                TeArg.Element("Spline", 0, 0f, 0f, 16f),
                TeArg.Element("Spline", 1, 32f, 0f, 48f),
                TeArg.Element("Spline", 2, 64f, 0f, 16f),
                TeArg.Element("Spline", 3, 96f, 0f, 48f),
                TeArg.Element("Spline", 4, 128f, 0f, 16f),
                TeArg.Element("Spline", 5, 160f, 0f, 48f),
            ]),

        new("CTEClientProjectile", "clientprojectile",
            "the inventory's ONE EHandle, and it is sent as INVALID: a projectile nobody owns",
            [
                TeArg.At("Origin", 0f, 0f, 32f),
                TeArg.Dir("Velocity", 0f, 0f, 512f),
                TeArg.Model("ModelIndex", Gibs),
                TeArg.Int("LifeTime", 5),
                TeArg.NoOwner("Owner"),
            ]),

        new("CTEMuzzleFlash", "muzzleflash",
            "the flash at the end of a barrel",
            [
                TeArg.At("Origin", 0f, 0f, 40f),

                // A NEGATIVE PITCH on purpose: SendPropVector keeps it and
                // SendPropQAngles would not. -45 arriving as 315 is what
                // declaring this table's angles the other way looks like.
                TeArg.Dir("Angles", -45f, 90f, 0f),
                TeArg.Float("Scale", 2f),
                TeArg.Int("Type", MuzzleFlashDefault),
            ]),

        // BEHAVIOUR LEDGER ROW S2, restored. te_impact.cpp is excluded from
        // this build and ITempEntsSystem has no Impact member at all, so
        // this row -- and ITempEnts.Impact -- is the only way one is sent.
        new("CTEImpact", "impact",
            "a generic impact. LEDGER ROW S2: nothing else in this build can send one",
            [
                TeArg.At("Origin", 0f, 0f, 8f),
                TeArg.Dir("Normal", 0f, 0f, 1f),
                TeArg.Int("Type", Concrete),
            ]),

        // BEHAVIOUR LEDGER ROW S3, restored UNCONDITIONALLY: stock's
        // ITempEntsSystem::GaussExplosion has a body only under HL2_DLL,
        // which this build does not define.
        new("CTEGaussExplosion", "gaussexplosion",
            "the gauss discharge. LEDGER ROW S3: stock's is #ifdef HL2_DLL and this one is not",
            [
                TeArg.At("Origin", 0f, 0f, 32f),

                // Type 1 rather than 0: FX_GaussExplosion switches on it, so
                // a two-bit prop arriving as zero would still have played
                // something and looked like a success.
                TeArg.Int("Type", 1),
                TeArg.Dir("Direction", 0f, 0f, 1f),
            ]),

        // BEHAVIOUR LEDGER ROW S6, and the one row that was never a gap.
        new("CTEDust", "dust",
            "the puff a physics object throws up. Its direction is FOUR bits per component over [-1,1]",
            [
                TeArg.At("Origin", 0f, 0f, 16f),
                TeArg.Float("Size", 16f),
                TeArg.Float("Speed", 32f),

                // A UNIT vector, which is what TE_Dust asserts: four bits per
                // component over an explicit [-1,1] clamps a longer one, so it
                // would arrive pointing somewhere else rather than be refused.
                TeArg.Dir("Direction", 0f, 0f, 1f),
            ]),

        // ---------------------------------------------------------------
        // Lane S4 -- the busiest temp entity in the build, and the only one
        // whose payload is a nested table. Written through the same dotted
        // wire names TempEnts.DispatchEffect uses.
        // ---------------------------------------------------------------
        // THE NESTED TABLE'S PROPS ARE ADDRESSED UNPREFIXED, and that is not an
        // oversight: DT_EffectData is nested at m_EffectData, and the registry
        // flattens the nested table's own props into this class's field map
        // under their own names -- exactly as TempEnts.DispatchEffect writes
        // them. m_CustomColors and m_ControlPoint1 keep their dots because
        // DT_EffectData itself declares them that way, one level further down.
        // Writing "m_EffectData.m_vOrigin" here was the first thing this pass
        // got wrong, and it was silent: TempEntWriter warns and writes nothing,
        // so the effect dispatched at the world origin.
        new("CTEEffectDispatch", "effectdispatch",
            "one temp entity standing for fifty-two client effects, dispatched by NAME",
            [
                TeArg.At("Origin", 0f, 0f, 16f),
                TeArg.At("Start", 0f, 0f, 80f),
                TeArg.Dir("Angles", 0f, 0f, 0f),
                TeArg.Dir("Normal", 0f, 0f, 1f),
                TeArg.Int("Flags", 0),
                TeArg.Float("Magnitude", 2f),
                TeArg.Float("Scale", 1f),
                TeArg.Int("AttachmentIndex", 0),

                // -1 IS "no surface property", and it is the value
                // SendProxy_ShortAddOne existed for -- so it is written through
                // the kind that carries the proxy's arithmetic rather than as a
                // literal 0 that would be right for the wrong reason.
                TeArg.SurfaceProp("SurfaceProp", -1),
                TeArg.Effect("EffectName", DispatchName),
                TeArg.Int("Material", 0),
                TeArg.Int("DamageType", 0),
                TeArg.Int("HitBox", 0),
                TeArg.Int("EntIndex", World),
                TeArg.Int("Color", 0),
                TeArg.Float("Radius", 16f),
                TeArg.Int("CustomColors", 0),
                TeArg.Int("ControlPoint1", 0),
            ]),

        new("CTESparks", "sparks",
            "a shower of sparks. The first temp entity this port put on the wire",
            [
                TeArg.At("Origin", 0f, 0f, 24f),
                TeArg.Int("Magnitude", 4),
                TeArg.Int("TrailLength", 2),
                TeArg.Dir("Direction", 0f, 0f, 1f),
            ]),
    ];

    /// <summary>
    /// <c>DT_BaseBeam</c>'s fifteen props, which seven derived tables and the
    /// base itself all write before anything of their own.
    ///
    /// <para>
    /// A FUNCTION AND NOT A COPIED BLOCK, because the ordering is the wire:
    /// <c>Speed</c> comes BEFORE <c>Red Green Blue Alpha</c>, which is not the
    /// member order in <c>te_basebeam.h</c> (plan Part 6). Eight transcriptions
    /// of that would be eight chances to sort it "properly".
    /// </para>
    ///
    /// <para>
    /// THE NAMES ARE <see cref="TeBaseBeam"/>'S MEMBERS, not stock's
    /// <c>m_nSpeed</c> / <c>r</c> / <c>g</c> / <c>b</c> / <c>a</c>: the eight
    /// beam declarations dropped their <c>WireName</c>s, so the wire name IS
    /// the member name (<c>docs/te-declaration-conversion.md</c>). A name this
    /// book gets wrong is a field left at zero and a warning at fire time, which
    /// is why <c>SandboxTeTests</c> checks every one of them against the
    /// declaration with no game running.
    /// </para>
    /// </summary>
    private static TeArg[] BaseBeam(int r, int g, int b) =>
    [
        TeArg.Model("ModelIndex", Laser),

        // No halo. 0 is what every stock beam caller passes and what
        // cs_sv_te_beam used; a halo index naming nothing draws nothing.
        TeArg.Int("HaloIndex", 0),
        TeArg.Int("StartFrame", 0),
        TeArg.Int("FrameRate", 10),
        TeArg.Float("Life", 2f),
        TeArg.Float("Width", 1f),
        TeArg.Float("EndWidth", 1f),
        TeArg.Int("FadeLength", 0),
        TeArg.Float("Amplitude", 1f),
        TeArg.Int("Speed", 0),
        // ColorR..ColorA, because DT_BaseBeam's colour is one Color32 member and
        // TeClassBuilder expands it into four eight-bit props named for it.
        TeArg.Int("ColorR", r),
        TeArg.Int("ColorG", g),
        TeArg.Int("ColorB", b),
        TeArg.Int("ColorA", 150),
        TeArg.Int("Flags", 1),
    ];

    /// <summary>A demonstration by its class name, or null.</summary>
    public static TeDemo? ByClass(string className)
        => All.FirstOrDefault(d => string.Equals(d.ClassName, className, StringComparison.OrdinalIgnoreCase));

    /// <summary>A demonstration by its short name, or null.</summary>
    public static TeDemo? BySlug(string slug)
        => All.FirstOrDefault(d => string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));

    //-------------------------------------------------------------------------
    // WHERE EACH ONE GOES.
    //
    // Forty-three effects fired at one point is a heap, which is the state plan
    // 9.1 names and rejects: "44 of them across one plot is 44 distinguishable
    // effects in one screenshot rather than a pile at the world origin". So the
    // book carries the layout too, and the sandbox generator and cs_te_fire read
    // the same one -- the map and the command put the same effect in the same
    // place, and a screenshot of one can be compared with the other.
    //
    // Sized to a station's pad: 512 units square, minus the pedestal at the
    // front (y from -300 to -190) that every station gets from Furniture().
    //-------------------------------------------------------------------------

    /// <summary>How many across. Seven by seven holds forty-nine.</summary>
    public const int Columns = 7;

    /// <summary>Units between neighbours.</summary>
    public const float Pitch = 64f;

    /// <summary>
    /// Pushed towards the back of the plot, clear of the accent plate and the
    /// pedestal the station's furniture puts at the front.
    /// </summary>
    public const float Forward = 40f;

    /// <summary>Where the n-th demonstration fires, relative to the plot's centre.</summary>
    public static Vector Offset(int index)
    {
        (float x, float y, float z) = Plot(index);

        return new Vector(x, y, z);
    }

    /// <summary>
    /// The same place, as three plain floats.
    ///
    /// <para>
    /// IT EXISTS FOR THE MAP GENERATOR, and the reason is a rule
    /// <c>LoadedAssemblyTests</c> enforces rather than a style preference.
    /// Discovery scans every loaded assembly that references
    /// <c>SourceSharp.Abi</c>, on the reasoning that an assembly with no ABI
    /// reference cannot be carrying a declaration — and <c>SourceSharp.MapGen</c>
    /// is deliberately on the other side of that line, so that a build tool is
    /// never enrolled as a mod assembly and never shifts the order the layout
    /// fingerprint and the user message indices are derived from. A single
    /// <c>var offset = TeDemos.Offset(i)</c> in <c>Sandbox.cs</c> puts an
    /// <c>SourceSharp.Abi.Vector</c> in MapGen's metadata and crosses it. It did;
    /// the test caught it.
    /// </para>
    /// </summary>
    public static (float X, float Y, float Z) Plot(int index)
    {
        int column = index % Columns;
        int row = index / Columns;

        return (
            (column - ((Columns - 1) * 0.5f)) * Pitch,
            ((row * Pitch) - ((Rows - 1) * Pitch * 0.5f)) + Forward,

            // The pad's top surface. A decal fired at the surface lands on it;
            // one fired in mid-air lands on nothing and reads exactly like a
            // decal that never arrived.
            8f);
    }

    /// <summary>How many rows the book actually needs. Derived, so a forty-fourth fits.</summary>
    public static int Rows => (All.Count + Columns - 1) / Columns;
}
