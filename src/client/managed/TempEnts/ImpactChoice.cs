using System.Collections.Immutable;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// What native does about one impact. Mirrors <c>SourceSharpImpactAction_t</c>
/// in <c>game/client/impact_effects.h</c>;
/// <c>ImpactChoiceTests.TheManagedActionsAreTheOnesTheHeaderDeclares</c>
/// compares the two off disk rather than trusting them to stay in step.
/// </summary>
public enum ImpactAction
{
    /// <summary>Stock's fall-through. A real answer, and the common one.</summary>
    Nothing = 0,

    /// <summary><c>FX_DebrisFlecks( origin, &amp;tr, material, scale, noFlecks )</c>.</summary>
    DebrisFlecks = 1,

    /// <summary><c>FX_DustImpact( origin, &amp;tr, scale )</c>.</summary>
    Dust = 2,

    /// <summary><c>FX_AntlionImpact( origin, &amp;tr )</c>.</summary>
    Antlion = 3,

    /// <summary>The jittered reflect vector, then <c>FX_MetalSpark</c>.</summary>
    MetalSpark = 4,

    /// <summary><c>g_pEffects-&gt;Sparks</c>, one unit off the surface normal.</summary>
    Sparks = 5,

    /// <summary><c>DispatchParticleEffect</c> with the name this choice carries.</summary>
    Particle = 6,

    /// <summary><c>CNewParticleEffect::Create</c> with the name this choice carries.</summary>
    NewParticle = 7,
}

/// <summary>What the choice is made from. <c>SourceSharpImpactChoiceIn_t</c>.</summary>
public enum ImpactChoiceIn
{
    /// <summary>The <c>CHAR_TEX_*</c> the trace's surface carries.</summary>
    Material = 0,

    /// <summary>The impact's scale, as the caller passed it.</summary>
    Scale = 1,

    /// <summary>The caller's <c>FLAGS_CUSTIOM_EFFECTS_*</c> word.</summary>
    Flags = 2,

    /// <summary><c>r_drawflecks</c>.</summary>
    DrawFlecks = 3,

    /// <summary><c>cl_new_impact_effects</c>.</summary>
    NewEffects = 4,

    /// <summary>The impact's <c>DMG_*</c> word, for the decal.</summary>
    DamageType = 5,
}

/// <summary>The answer. <c>SourceSharpImpactChoiceOut_t</c>.</summary>
public enum ImpactChoiceOut
{
    /// <summary>An <see cref="ImpactAction"/>.</summary>
    Action = 0,

    /// <summary>The scale to pass the emitter.</summary>
    Scale = 1,

    /// <summary>1 when flecks are suppressed.</summary>
    NoFlecks = 2,
}

/// <summary>One impact's choice, as managed code answers it.</summary>
/// <param name="Action">What native does.</param>
/// <param name="Scale">The scale the emitter gets.</param>
/// <param name="NoFlecks">Whether flecks are suppressed.</param>
/// <param name="ParticleName">
/// The particle system the two <c>*Particle</c> actions name, or "" for the
/// six that name none.
/// </param>
/// <param name="DecalName">The decal name, before the engine translates it.</param>
public readonly record struct ImpactChoice(
    ImpactAction Action,
    int Scale,
    bool NoFlecks,
    string ParticleName,
    string DecalName);

/// <summary>
/// WHICH EFFECT FAMILY AN IMPACT IS, AND WHICH DECAL IT LEAVES.
///
/// <para>
/// <see cref="ImpactEffectPolicy"/> answers what one family is made OF — the
/// counts, the ranges, the colour ramps. This answers which family it IS, and
/// it is the decision that used to sit above that one in C++:
/// <c>PerformCustomEffects</c>'s <c>iMaterial == CHAR_TEX_*</c> chain,
/// <c>PerformNewCustomEffects</c>'s twenty-six-row surface-to-particle-system
/// table, the fleck suppression both of those recomputed, and
/// <c>GetImpactDecal</c>'s decal name.
/// </para>
///
/// <para>
/// <b>Two tables, and the game runs the first.</b> <c>cl_new_impact_effects</c>
/// defaults to 0, so the chain is what every shot in this build takes; the
/// table is the path a player who turned that on gets. Both are here because
/// both are choices about what a surface IS, and leaving one behind would have
/// left the knob switching between a managed answer and a C++ one.
/// </para>
///
/// <para>
/// <b>The chain and the table do not agree, and that is stock's.</b> Dirt and
/// sand call <c>FX_DustImpact</c> in the chain and name "impact_dirt" in the
/// table; metal and vent get a jittered reflect and a spark in the chain and
/// name "impact_metal" in the table; glass has a table entry and NO chain arm
/// at all, so shooting glass with the new effects off draws no custom effect —
/// <c>FX_GlassImpact</c> is reached from the "GlassImpact" client effect, not
/// from here. Every one of those is reproduced rather than reconciled: this
/// move is a change of language and not of behaviour.
/// </para>
///
/// <para>
/// <b>The table's index is bounds-checked here and was not in C++.</b> Stock
/// indexes <c>s_pImpactEffect[ iMaterial - 'A' ]</c> with no test, on a
/// twenty-six entry array, from a material byte that arrives over the wire. A
/// surfaceprop naming anything outside 'A'..'Z' read past the array and handed
/// <c>CNewParticleEffect::Create</c> whatever pointer was there. Here that is
/// <see cref="ImpactAction.Nothing"/>, and
/// <c>ImpactChoiceTests.AMaterialOutsideTheTableIsRefusedRatherThanRead</c>
/// pins it.
/// </para>
/// </summary>
public static class ImpactChoicePolicy
{
    /// <summary>How many ints the caller sends. <c>SS_CHOICE_IN_COUNT</c>.</summary>
    public const int InCount = 6;

    /// <summary>How many ints come back. <c>SS_CHOICE_OUT_COUNT</c>.</summary>
    public const int OutCount = 3;

    /// <summary>The longest name either buffer takes. <c>SS_IMPACT_NAME_MAX</c>.</summary>
    public const int NameMax = 64;

    /// <summary>
    /// <c>FLAGS_CUSTIOM_EFFECTS_NOFLECKS</c> (<c>game/client/fx_impact.h</c>).
    /// Stock's spelling of the constant is stock's; it is not a typo made here.
    /// </summary>
    public const int FlagsNoFlecks = 0x1;

    /// <summary>
    /// The decal every impact leaves on this hierarchy.
    ///
    /// <para>
    /// <c>GetImpactDecal</c>'s entity argument exists so a class can override
    /// <c>DamageDecal</c> — glass, flesh, antlion — and that virtual is
    /// <c>C_BaseEntity</c>'s, which this build has no instances of. So there is
    /// no table to consult and this IS the answer, not a placeholder for a
    /// missing one: the engine's <c>TranslateDecalForGameMaterial</c> then
    /// picks the final decal from the surfaceprop the shot actually hit.
    /// </para>
    /// </summary>
    public const string DefaultDecal = "Impact.Concrete";

    // The CHAR_TEX_* surface types (game/shared/decals.h), spelled as the ASCII
    // letters the surface property system writes them as — the same decision
    // ImpactEffectPolicy's three make, and for the same reason: a number here
    // would be a second place for 'W' to be written down wrong.
    private const int Antlion = 'A';
    private const int BloodyFlesh = 'B';
    private const int Concrete = 'C';
    private const int Dirt = 'D';
    private const int Metal = 'M';
    private const int Sand = 'N';
    private const int Computer = 'P';
    private const int Tile = 'T';
    private const int Vent = 'V';
    private const int Wood = 'W';
    private const int Glass = 'Y';
    private const int WarpShield = 'Z';

    /// <summary>The first and last material the table has a row for.</summary>
    private const int TableFirst = 'A';
    private const int TableLast = 'Z';

    /// <summary>
    /// <c>s_pImpactEffect[26]</c>, verbatim — one row per <c>CHAR_TEX_*</c> from
    /// 'A' to 'Z', a particle system name and the name to use instead when
    /// flecks are suppressed. An empty string is stock's <c>NULL</c>: that
    /// material names no system and nothing is drawn for it.
    /// </summary>
    private static readonly ImmutableArray<(string Name, string NoFlecks)> Table =
    [
        ("impact_antlion", ""),                            // A CHAR_TEX_ANTLION
        ("", ""),                                          // B CHAR_TEX_BLOODYFLESH
        ("impact_concrete", "impact_concrete_noflecks"),    // C CHAR_TEX_CONCRETE
        ("impact_dirt", ""),                               // D CHAR_TEX_DIRT
        ("", ""),                                          // E CHAR_TEX_EGGSHELL
        ("", ""),                                          // F CHAR_TEX_FLESH
        ("", ""),                                          // G CHAR_TEX_GRATE
        ("", ""),                                          // H CHAR_TEX_ALIENFLESH
        ("", ""),                                          // I CHAR_TEX_CLIP
        ("", ""),                                          // J unused
        ("", ""),                                          // K unused
        ("", ""),                                          // L CHAR_TEX_PLASTIC
        ("impact_metal", ""),                              // M CHAR_TEX_METAL
        ("impact_dirt", ""),                               // N CHAR_TEX_SAND
        ("", ""),                                          // O CHAR_TEX_FOLIAGE
        ("impact_computer", ""),                           // P CHAR_TEX_COMPUTER
        ("", ""),                                          // Q unused
        ("", ""),                                          // R unused
        ("", ""),                                          // S CHAR_TEX_SLOSH
        ("impact_concrete", "impact_concrete_noflecks"),    // T CHAR_TEX_TILE
        ("", ""),                                          // U unused
        ("impact_metal", ""),                              // V CHAR_TEX_VENT
        ("impact_wood", "impact_wood_noflecks"),            // W CHAR_TEX_WOOD
        ("", ""),                                          // X unused
        ("impact_glass", ""),                              // Y CHAR_TEX_GLASS
        ("warp_shield_impact", ""),                        // Z CHAR_TEX_WARPSHIELD
    ];

    /// <summary>
    /// The choice, from the six inputs native holds.
    /// </summary>
    /// <param name="material">The <c>CHAR_TEX_*</c> the surface carries.</param>
    /// <param name="scale">The impact's scale.</param>
    /// <param name="flags">The caller's <c>FLAGS_CUSTIOM_EFFECTS_*</c> word.</param>
    /// <param name="drawFlecks"><c>r_drawflecks</c>.</param>
    /// <param name="newEffects"><c>cl_new_impact_effects</c>.</param>
    /// <param name="damageType">The impact's <c>DMG_*</c> word.</param>
    public static ImpactChoice Choose(int material, int scale, int flags,
                                      bool drawFlecks, bool newEffects, int damageType)
    {
        // Stock's, computed once here where it was computed twice there:
        //     bool bNoFlecks = !r_drawflecks.GetBool();
        //     if ( !bNoFlecks ) bNoFlecks = ( nFlags & FLAGS_CUSTIOM_EFFECTS_NOFLECKS ) != 0;
        // -- which reads as "flecks are off if the convar says so, and if it
        // does not, then if the caller says so".
        bool noFlecks = !drawFlecks || (flags & FlagsNoFlecks) != 0;

        var (action, particleName) = newEffects
            ? FromTable(material, noFlecks)
            : FromChain(material);

        return new ImpactChoice(action, scale, noFlecks, particleName, DefaultDecal);
    }

    /// <summary>
    /// <c>PerformNewCustomEffects</c>'s table lookup — the
    /// <c>cl_new_impact_effects 1</c> path.
    /// </summary>
    private static (ImpactAction, string) FromTable(int material, bool noFlecks)
    {
        if (material < TableFirst || material > TableLast)
            return (ImpactAction.Nothing, "");

        var row = Table[material - TableFirst];

        // Stock: the no-flecks name is used only if the row HAS one. A row with
        // a name and no no-flecks name draws its flecked system either way.
        string name = noFlecks && row.NoFlecks.Length > 0 ? row.NoFlecks : row.Name;

        return name.Length == 0
            ? (ImpactAction.Nothing, "")
            : (ImpactAction.NewParticle, name);
    }

    /// <summary>
    /// <c>PerformCustomEffects</c>'s <c>iMaterial == CHAR_TEX_*</c> chain — the
    /// path every shot in this build takes.
    /// </summary>
    private static (ImpactAction, string) FromChain(int material) => material switch
    {
        // Cement and wood have dust and flecks. Three materials, one arm:
        // stock spells concrete/tile and wood as two branches with identical
        // bodies, and the emitter reads the material itself.
        Concrete or Tile or Wood => (ImpactAction.DebrisFlecks, ""),

        Dirt or Sand => (ImpactAction.Dust, ""),

        Antlion => (ImpactAction.Antlion, ""),

        Metal or Vent => (ImpactAction.MetalSpark, ""),

        Computer => (ImpactAction.Sparks, ""),

        // The one chain arm that names a particle system rather than calling an
        // emitter, and it names the same one its table row does.
        WarpShield => (ImpactAction.Particle, "warp_shield_impact"),

        // Everything else, glass and bloody flesh included. Stock's chain has
        // no arm for either: the glass burst is reached from the "GlassImpact"
        // client effect and the blood from the blood effect, neither of which
        // comes through here.
        _ => (ImpactAction.Nothing, ""),
    };

    /// <summary>
    /// The flat form the ABI slot speaks: six ints in, three ints out, and the
    /// two names written separately by the caller.
    /// </summary>
    /// <returns>
    /// The number of ints written, or −1 when <paramref name="input"/> is short
    /// or <paramref name="output"/> cannot hold the answer. −1 is a REFUSAL and
    /// native does what stock's fall-through does, which is nothing.
    /// </returns>
    public static int Choose(ReadOnlySpan<int> input, Span<int> output, out ImpactChoice choice)
    {
        choice = default;

        if (input.Length < InCount || output.Length < OutCount)
            return -1;

        choice = Choose(input[(int)ImpactChoiceIn.Material],
                        input[(int)ImpactChoiceIn.Scale],
                        input[(int)ImpactChoiceIn.Flags],
                        input[(int)ImpactChoiceIn.DrawFlecks] != 0,
                        input[(int)ImpactChoiceIn.NewEffects] != 0,
                        input[(int)ImpactChoiceIn.DamageType]);

        output[(int)ImpactChoiceOut.Action] = (int)choice.Action;
        output[(int)ImpactChoiceOut.Scale] = choice.Scale;
        output[(int)ImpactChoiceOut.NoFlecks] = choice.NoFlecks ? 1 : 0;

        return OutCount;
    }
}
