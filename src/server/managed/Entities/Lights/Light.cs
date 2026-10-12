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
/// light, light_spot and light_glspot: a switchable light.
///
/// <para>
/// Almost all of a light happens before the game starts. vbsp reads the entity
/// out of the map, vrad works out what it does to every surface, and the result
/// is baked into the lightmap; the entity that survives into the running game
/// holds a STYLE NUMBER and nothing else. So this class is small on purpose,
/// and the small thing it does is the only thing there is.
/// </para>
///
/// <para>
/// The rule that makes the runtime entity matter at all: <b>a named light is a
/// switched light</b>. vbsp gives every light with a targetname one of styles
/// 32 to 62 and bakes its contribution into a separate lightmap layer; the
/// engine then scales that layer by whatever the style's animation string says.
/// A light with no name has no style, no layer and nothing to switch, which is
/// why stock deletes it at spawn and so does this. There are 31 such styles, and
/// vbsp stops the compile with an error past that — worth knowing before adding
/// named lights to a map.
/// </para>
///
/// <para>
/// Three classnames share the implementation because stock shares one C++ class
/// between them: <c>light_spot</c> and <c>light_glspot</c> differ from
/// <c>light</c> only in what vrad does with their cone keys, and vrad has
/// already finished by the time any of this runs.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.Light)]
[ScriptedEntity(EntityNames.LightSpot)]
[ScriptedEntity(EntityNames.LightGlspot)]
public class Light : ScriptedEntity
{
    public Light(IEngineVisibility visibility)
    {
        Visibility = visibility;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    protected readonly IEngineVisibility Visibility;
    /// <summary>Turned off at map load, and stays that way until something says otherwise.</summary>
    private const int SfStartOff = 1;

    /// <summary>
    /// The first style vbsp hands out to a switchable light. Below this are the
    /// engine's own preset animations, which belong to the world rather than to
    /// any entity — writing one would change every surface using it.
    /// </summary>
    private const int FirstSwitchableStyle = 32;

    /// <summary>What a map may set on this entity. See <see cref="LightVars"/>.</summary>
    [ScriptVars] public LightVars Vars = new();

    /// <summary>Whether the light is currently lit. Starts from the spawnflag.</summary>
    private bool _on;

    /// <summary>The fade in progress, as the two letters it runs between.</summary>
    private char _currentFade;
    private char _targetFade;
    private bool _fading;

    /// <summary>
    /// Set when the light deleted itself for having no name. Decided in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>: one mark here carries the light_environment that
    /// derives from this class too, and uncarried the rebuilt instance would
    /// answer <see cref="IsInert"/> false for a light that is gone
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _inert;

    /// <summary>Whether this light deleted itself at spawn. For tests.</summary>
    public bool IsInert => _inert;

    /// <summary>
    /// Whether these spawnflags say the light starts dark. A static form
    /// because <see cref="ScriptedEntity.SpawnFlags"/> asks the engine, and the
    /// decision is worth being able to check without one.
    /// </summary>
    public static bool StartsOff(int spawnFlags) => (spawnFlags & SfStartOff) != 0;

    /// <summary>Whether the light is lit, for tests and for debug-api.</summary>
    public bool IsOn => _on;

    /// <summary>The style slot this entity drives, for tests and for debug-api.</summary>
    public int Style => Vars.Style;

    /// <summary>Whether a FadeToPattern is still walking. For tests.</summary>
    public bool FadeInProgress => _fading;

    /// <summary>The letter the fade is currently showing. For tests.</summary>
    public char CurrentFadeLetter => _currentFade;

    /// <summary>
    /// An unnamed light is deleted, and that is not a shortcut: it has no
    /// switchable style, so there is no layer of the lightmap it could scale and
    /// nothing any input could do to it. Keeping it would cost an edict per
    /// light — 57 of them in dm_lockdown alone.
    /// </summary>
    public override void OnSpawn()
    {
        if (Vars.TargetName.Length == 0 && Name.Length == 0)
        {
            Entities.Remove(Handle);
            _inert = true;
            return;
        }

        if (Vars.Style < FirstSwitchableStyle)
            return;

        if (Vars.Pattern.Length == 0 && Vars.DefaultStyle > 0)
            Vars.Pattern = LightStyles.Default(Vars.DefaultStyle);

        _on = !StartsOff(SpawnFlags);

        Visibility.SetLightStyle(Vars.Style, _on ? Pattern : LightStyles.Off);
    }

    /// <summary>
    /// "_light" is vrad's, and claiming it here is how <c>light_environment</c>
    /// stays quiet about a key nothing at runtime can use. Declared on the base
    /// rather than the subclass because <c>light</c> carries one too.
    /// </summary>
    public override bool OnKeyValue(string key, string value)
        => key.Equals("_light", StringComparison.OrdinalIgnoreCase);

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn() => TurnOn();

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff() => TurnOff();

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (_on) TurnOff(); else TurnOn();
    }

    /// <summary>
    /// A new pattern, and the light comes on whether or not it was on. Stock
    /// does the same, and the reason is worth keeping: a pattern is a statement
    /// about how the light behaves while lit, so setting one on a dark light and
    /// leaving it dark would look like the input did nothing.
    /// </summary>
    [ScriptedEvent("SetPattern")]
    private void OnSetPattern(string pattern)
    {
        Vars.Pattern = pattern;
        _fading = false;
        _on = true;
        Visibility.SetLightStyle(Vars.Style, Pattern);
    }

    /// <summary>
    /// Walks the light from the first letter of its current pattern to the first
    /// letter of the new one, a step every tenth of a second, and only then
    /// switches to the new pattern outright.
    ///
    /// <para>
    /// It is a fade between two BRIGHTNESSES rather than between two animations,
    /// which is all a single letter can express — the rest of the target pattern
    /// does not begin until the arrival. Stock is the same and the effect is a
    /// dimmer rather than a crossfade.
    /// </para>
    /// </summary>
    [ScriptedEvent("FadeToPattern")]
    private void OnFadeToPattern(string pattern)
    {
        if (pattern.Length == 0)
            return;

        _currentFade = Pattern[0];
        _targetFade  = pattern[0];
        Vars.Pattern     = pattern;
        _fading      = true;
        _on          = true;

        SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (!_fading)
            return;

        if (_currentFade < _targetFade) _currentFade++;
        else if (_currentFade > _targetFade) _currentFade--;

        if (_currentFade == _targetFade)
        {
            _fading = false;
            Visibility.SetLightStyle(Vars.Style, Pattern);
            return;
        }

        Visibility.SetLightStyle(Vars.Style, _currentFade.ToString());
        SetNextThink(0.1f);
    }

    /// <summary>
    /// What to write when the light is on: the mapper's pattern, or steady
    /// normal brightness when there is none.
    /// </summary>
    private string Pattern => Vars.Pattern.Length > 0 ? Vars.Pattern : LightStyles.Normal;

    private void TurnOn()
    {
        _on = true;
        _fading = false;
        Visibility.SetLightStyle(Vars.Style, Pattern);
    }

    private void TurnOff()
    {
        _on = false;
        _fading = false;
        Visibility.SetLightStyle(Vars.Style, LightStyles.Off);
    }
}

/// <summary>
/// light_environment: the sun.
///
/// <para>
/// Every value on it — the angle, the colour, the ambient term, the brightness —
/// is read by vrad and burned into the lightmap, and none of it is legible to
/// anything at runtime. What is left is a <see cref="Light"/> exactly, which is
/// what stock's CEnvLight is too: it derives from CLight and overrides nothing
/// but a key it discards.
/// </para>
///
/// <para>
/// So this is a separate class only to own a separate classname. It is not a
/// stub — the switchable-light behaviour it inherits is real, and a mapper who
/// names a light_environment gets a style number from vbsp the same as any
/// other light.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.LightEnvironment)]
public sealed class LightEnvironment : Light
{
    public LightEnvironment(IEngineVisibility visibility)
        : base(visibility)
    {
    }

}

/// <summary>
/// The map keys <see cref="Light"/> declares.
/// </summary>
[ScriptVars]
public partial class LightVars
{
    /// <summary>Which of the engine's 64 lightstyle slots this entity drives.</summary>
    public int Style { get; set; }

    /// <summary>
    /// One of the engine's preset animations to copy, for a mapper who wants
    /// "candle" without writing out the letters. Only consulted when no explicit
    /// pattern was given.
    /// </summary>
    public int DefaultStyle { get; set; }

    /// <summary>
    /// The animation string itself: one letter per tenth of a second, 'a' black
    /// through 'm' normal, repeating.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// The mapper's pitch. Claimed and ignored, which is deliberate rather than
    /// an omission — see the divergence note in docs/divergences.md.
    /// </summary>
    public float Pitch { get; set; }

    /// <summary>
    /// The name, as the MAP wrote it.
    ///
    /// <para>
    /// Declared as a field even though <see cref="ScriptedEntity.Name"/> would
    /// answer the same thing, because the two are not available at the same
    /// moment for the same reasons: the engine's answer needs a live entity, and
    /// this decision is what a light IS rather than something it does, so it
    /// should be legible from the map data alone. Both sides of the shell see
    /// every key, so the engine still has the name as well.
    /// </para>
    /// </summary>
    public string TargetName { get; set; } = string.Empty;
}
