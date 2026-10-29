//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.WeaponEffects;

/// <summary>
/// Whether the camera is behind the local player: <c>input-&gt;CAM_IsThirdPerson()</c>,
/// which is <c>C_BasePlayer::ShouldDrawLocalPlayer</c>'s answer.
/// </summary>
public interface IThirdPersonCamera
{
    bool IsThirdPerson { get; }
}

/// <summary><see cref="IThirdPersonCamera"/> over the client's input, whose camera it is.</summary>
public sealed class InputThirdPersonCamera(Input.ClientInput input) : IThirdPersonCamera
{
    public bool IsThirdPerson => input.CamIsThirdPerson();
}

/// <summary>
/// One carried weapon at one of stock's draw points: the weapon, the local
/// viewmodel when the weapon is drawn through it, and the clock.
/// </summary>
public readonly record struct WeaponDrawContext(ClientSsWeapon Weapon, ClientSsViewModel? ViewModel, bool FirstPerson,
                                                float Now, float FrameTime = 0f, WeaponNetState? Predicted = null)
{
    /// <summary>The carrier's entindex.</summary>
    public int Owner => Weapon.State.OwnerIndex;

    /// <summary>
    /// The state the weapon's client effects read: its prediction when there
    /// is one (<see cref="IWeaponDrawStates"/>), else what was decoded.
    /// </summary>
    public WeaponNetState State => Predicted ?? Weapon.State;
}

/// <summary>
/// The state a carried weapon is drawn from. Stock's client effects read the
/// weapon's own fields, which for the local player's gun are its prediction:
/// a change the replay makes shows the frame it is made, not when the server's
/// word of it arrives.
/// </summary>
public interface IWeaponDrawStates
{
    /// <summary>
    /// <paramref name="weapon"/>'s predicted state, or null when nothing
    /// predicts it (somebody else's gun, or prediction behind the decode).
    /// </summary>
    WeaponNetState? PredictedStateOf(ClientSsWeapon weapon);
}

/// <summary>
/// The client half of one stock weapon's draw effects: what its
/// <c>ViewModelDrawn</c>, its <c>DrawModel</c> arm for the translucent pass
/// and its <c>ClientThink</c> do.
/// </summary>
public interface IWeaponDrawEffect
{
    /// <summary>The weapon classname this draws for (stock's class).</summary>
    string WeaponName { get; }

    /// <summary>
    /// Whether the carried world model answers <c>RENDER_GROUP_TWOPASS</c>, so
    /// that <see cref="DrawModel"/> is asked in the translucent pass.
    /// </summary>
    bool TwoPass { get; }

    /// <summary>Once a frame for every carried weapon of this kind: stock's <c>ClientThink</c>.</summary>
    void Think(in WeaponDrawContext context);

    /// <summary>After the local viewmodel's model, in the viewmodel pass: stock's <c>ViewModelDrawn</c>.</summary>
    void ViewModelDrawn(in WeaponDrawContext context);

    /// <summary>
    /// The carried world model's translucent pass: stock's <c>DrawModel</c> arm
    /// for <c>STUDIO_TRANSPARENCY</c>. What DrawModel answers, or -1 to let the
    /// model draw.
    /// </summary>
    int DrawModel(in WeaponDrawContext context, int flags);

    /// <summary>The weapon stopped being this kind, or went: let go of what it held.</summary>
    void Forget(EntityHandle weapon);
}

/// <summary>What the carried weapon and the viewmodel ask of the draw effects.</summary>
public interface IWeaponDrawEffects
{
    /// <summary>The local viewmodel has drawn: its weapon's <c>ViewModelDrawn</c>.</summary>
    void ViewModelDrawn(ClientSsViewModel viewModel);

    /// <summary>The carried weapon's translucent pass; -1 lets the model draw.</summary>
    int DrawWorldModel(ClientSsWeapon weapon, int flags);

    /// <summary>Whether a weapon of this type answers the two-pass group.</summary>
    bool WantsTwoPass(int weaponTypeId);
}

/// <summary>No draw effects: a weapon or viewmodel built without the game's.</summary>
public sealed class NoWeaponDrawEffects : IWeaponDrawEffects
{
    public void ViewModelDrawn(ClientSsViewModel viewModel) { }

    public int DrawWorldModel(ClientSsWeapon weapon, int flags) => -1;

    public bool WantsTwoPass(int weaponTypeId) => false;
}

/// <summary>
/// The weapons' draw effects, by weapon: the carried weapon's type picks the
/// <see cref="IWeaponDrawEffect"/>, the draw point picks the call.
///
/// <para>
/// <c>ShouldDrawUsingViewModel</c> is the context's <see cref="WeaponDrawContext.FirstPerson"/>:
/// carried by the local player with the camera at the eye.
/// </para>
/// </summary>
public sealed class WeaponDrawEffects : IWeaponDrawEffects
{
    private readonly IClientStandalones _standalones;
    private readonly IEngineHud _screen;
    private readonly IThirdPersonCamera _camera;
    private readonly IGameClock _clock;
    private readonly WeaponCatalog _weapons;
    private readonly IWeaponDrawStates? _states;
    private readonly Dictionary<string, IWeaponDrawEffect> _byName = new(StringComparer.Ordinal);

    // The kind each carried weapon was last framed as, so a change of kind (a
    // switch: this build's carried weapon is one entity per player) or a
    // removal lets the old kind's effect go.
    private readonly Dictionary<EntityHandle, IWeaponDrawEffect> _framed = [];
    private readonly List<EntityHandle> _gone = [];
    private readonly HashSet<EntityHandle> _seen = [];

    // gpGlobals->frametime as the last frame had it: 0 while paused, when
    // stock's thinks and draws make no new beams.
    private float _frameTime;

    public WeaponDrawEffects(IClientStandalones standalones, IEngineHud screen, IThirdPersonCamera camera,
                             IGameClock clock, WeaponCatalog weapons, IEnumerable<IWeaponDrawEffect> effects,
                             IWeaponDrawStates? states = null)
    {
        _states = states;
        _standalones = standalones;
        _screen = screen;
        _camera = camera;
        _clock = clock;
        _weapons = weapons;
        foreach (IWeaponDrawEffect effect in effects)
            _byName[effect.WeaponName] = effect;
    }

    private IWeaponDrawEffect? For(int weaponTypeId)
        => _weapons.ByIndex(weaponTypeId) is { } type && _byName.TryGetValue(type.Name, out IWeaponDrawEffect? effect)
            ? effect
            : null;

    public bool WantsTwoPass(int weaponTypeId) => For(weaponTypeId)?.TwoPass ?? false;

    /// <summary>
    /// <c>ShouldDrawUsingViewModel</c>: carried by the local player, with the
    /// camera at the eye.
    /// </summary>
    private bool FirstPerson(ClientSsWeapon weapon)
    {
        int local = _screen.LocalPlayerIndex;
        return local > 0 && weapon.State.OwnerIndex == local && !_camera.IsThirdPerson;
    }

    private ClientSsViewModel? LocalViewModel(int owner)
    {
        foreach (ClientStandaloneEntity entity in _standalones.OfClass(EntityNames.PredictedViewModel))
        {
            if (entity is ClientSsViewModel { IsLocal: true } viewModel && viewModel.State.OwnerIndex == owner)
                return viewModel;
        }

        return null;
    }

    private WeaponDrawContext Context(ClientSsWeapon weapon)
    {
        bool firstPerson = FirstPerson(weapon);
        return new WeaponDrawContext(weapon, firstPerson ? LocalViewModel(weapon.State.OwnerIndex) : null, firstPerson,
                                     _clock.Now, _frameTime, _states?.PredictedStateOf(weapon));
    }

    public void ViewModelDrawn(ClientSsViewModel viewModel)
    {
        if (!viewModel.IsLocal)
            return;

        foreach (ClientStandaloneEntity entity in _standalones.OfClass(EntityNames.SsWeapon))
        {
            if (entity is not ClientSsWeapon weapon || weapon.State.OwnerIndex != viewModel.State.OwnerIndex)
                continue;

            For(weapon.State.WeaponTypeId)?.ViewModelDrawn(
                new WeaponDrawContext(weapon, viewModel, FirstPerson: true, _clock.Now, _frameTime,
                                      _states?.PredictedStateOf(weapon)));
            return;
        }
    }

    public int DrawWorldModel(ClientSsWeapon weapon, int flags)
        => For(weapon.State.WeaponTypeId) is { } effect ? effect.DrawModel(Context(weapon), flags) : -1;

    /// <summary>
    /// Once a frame: every carried weapon's <c>ClientThink</c>, and the effects
    /// of a weapon that changed kind or went let go.
    /// </summary>
    public void Frame(float frameTime)
    {
        _frameTime = frameTime;
        _seen.Clear();

        foreach (ClientStandaloneEntity entity in _standalones.OfClass(EntityNames.SsWeapon))
        {
            if (entity is not ClientSsWeapon weapon)
                continue;

            _seen.Add(weapon.Handle);
            IWeaponDrawEffect? effect = weapon.State.OwnerIndex > 0 ? For(weapon.State.WeaponTypeId) : null;

            if (_framed.TryGetValue(weapon.Handle, out IWeaponDrawEffect? was) && was != effect)
            {
                was.Forget(weapon.Handle);
                _framed.Remove(weapon.Handle);
            }

            if (effect is null)
                continue;

            _framed[weapon.Handle] = effect;
            effect.Think(Context(weapon));
        }

        _gone.Clear();
        foreach (EntityHandle framed in _framed.Keys)
        {
            if (!_seen.Contains(framed))
                _gone.Add(framed);
        }

        foreach (EntityHandle gone in _gone)
        {
            _framed[gone].Forget(gone);
            _framed.Remove(gone);
        }
    }
}
