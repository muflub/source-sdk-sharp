//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// CBaseAnimating (baseanimating.cpp): what every entity with a studio model
/// answers, whatever it is. Props, items, projectiles, gibs, the player and
/// its view model all derive from it in stock, so a map can re-skin, re-body,
/// re-scale, re-model or ignite any of them by the same names.
///
/// <para>
/// Its keys act as they arrive, as stock's keyfields write the fields the
/// engine sends: the registry hands each key a set takes to
/// <see cref="KeyTaken"/>. Its context thinks are taken before the class's own
/// <see cref="ScriptedEntity.OnThink(string)"/>, so a class that overrides that
/// does not have to call down for its model scale to keep changing.
/// </para>
/// </summary>
public abstract class AnimatingEntity : ScriptedEntity
{
    /// <summary>CBaseAnimating's keyfields. See <see cref="AnimatingVars"/>.</summary>
    [ScriptVars] public AnimatingVars AnimatingVars = new();

    /// <summary>m_OnIgnite: it has been set alight.</summary>
    public readonly Output OnIgnite = new();

    private IServerSkins? _skins;
    private Flames? _flames;
    private IEnginePrecache? _precache;
    private IEngineEntityRender? _render;

    private IServerSkins Skins => _skins ?? throw UnboundArea(nameof(IServerSkins));
    private Flames Flames => _flames ?? throw UnboundArea(nameof(Server.Flames));
    private IEnginePrecache Precache => _precache ?? throw UnboundArea(nameof(IEnginePrecache));
    private IEngineEntityRender Render => _render ?? throw UnboundArea(nameof(IEngineEntityRender));

    private protected override void BindOwnAreas(IServiceLocator engine)
    {
        _skins = engine.Resolve(typeof(IServerSkins)) as IServerSkins;
        _flames = engine.Resolve(typeof(Flames)) as Flames;
        _precache = engine.Require<IEnginePrecache>();
        _render = engine.Require<IEngineEntityRender>();
    }

    private InvalidOperationException UnboundArea(string area)
        => new($"{GetType().Name} has no {area}: it was not built by a registry that holds one");

    /// <summary>m_flModelScale, and the change under way (stock's MODELSCALE data object).</summary>
    [SaveField] private float _modelScale = 1f;

    [SaveField] private float _scaleStart, _scaleGoal, _scaleStartTime, _scaleFinishTime;

    private const string ModelScaleContext = "UpdateModelScaleThink";

    /// <summary>The scale it is drawn at.</summary>
    public float ModelScale => _modelScale;

    internal override void KeyTaken(ScriptedVarSet set, string lowered)
    {
        if (!ReferenceEquals(set, AnimatingVars))
            return;

        switch (lowered)
        {
            case "skin": Skins.SetSkin(Handle, AnimatingVars.Skin); break;
            case "body": Skins.SetBody(Handle, AnimatingVars.Body); break;
            case "setbodygroup": Skins.SetBody(Handle, AnimatingVars.SetBodyGroup); break;
            case "modelscale": SetModelScale(AnimatingVars.ModelScale, 0f); break;
        }
    }

    // The integer inputs take the value's number and truncate it, as an
    // integer input's variant converts a float.

    /// <summary>DEFINE_INPUT( m_nSkin, "skin" ): the keyfield's own field.</summary>
    [ScriptedEvent("skin")]
    private void OnSkinInput(float skin)
    {
        AnimatingVars.Skin = (int)skin;
        Skins.SetSkin(Handle, AnimatingVars.Skin);
    }

    /// <summary>m_nSkin: the skin it is drawn with, as its key or input last set it.</summary>
    public int Skin => AnimatingVars.Skin;

    /// <summary>DEFINE_INPUT( m_nBody, "SetBodyGroup" ).</summary>
    [ScriptedEvent] private void OnSetBodyGroup(float body) => Skins.SetBody(Handle, (int)body);

    /// <summary>InputSetModelScale: the scale, and over how many seconds to reach it.</summary>
    [ScriptedEvent]
    private void OnSetModelScale(string value)
    {
        Vector scale = ParseVector(value);
        SetModelScale(scale.X, scale.Y);
    }

    /// <summary>InputSetModel (ScriptSetModel): a model that precaches, or nothing.</summary>
    [ScriptedEvent]
    private void OnSetModel(string model)
    {
        if (model.Length == 0 || Precache.PrecacheModel(model) == -1)
            return;

        SetModel(model);
    }

    /// <summary>InputIgnite: thirty seconds, as a level designer asks.</summary>
    [ScriptedEvent("Ignite")] private void OnIgniteInput() => Ignite(30f);

    [ScriptedEvent] private void OnIgniteLifetime(float lifetime) => IgniteLifetime(lifetime);

    [ScriptedEvent] private void OnIgniteNumHitboxFires(float count) => IgniteNumHitboxFires((int)count);

    [ScriptedEvent] private void OnIgniteHitboxFireScale(float scale) => IgniteHitboxFireScale(scale);

    /// <summary>CBaseAnimating::IgniteLifetime: alight if it is not, then burning for this long.</summary>
    public void IgniteLifetime(float lifetime)
    {
        if (Burning() is { } flame)
            flame.SetLifetime(lifetime);
    }

    /// <summary>CBaseAnimating::IgniteNumHitboxFires: alight if it is not, and its flame's hitbox fires.</summary>
    public void IgniteNumHitboxFires(int count)
    {
        if (Burning() is { } flame)
            flame.NumHitboxFires = count;
    }

    /// <summary>CBaseAnimating::IgniteHitboxFireScale: alight if it is not, and its flame's hitbox fire scale.</summary>
    public void IgniteHitboxFireScale(float scale)
    {
        if (Burning() is { } flame)
            flame.HitboxFireScale = scale;
    }

    /// <summary>Whether a flame is burning on it (FL_ONFIRE).</summary>
    public bool IsOnFire => Flames.On(Handle) is not null;

    /// <summary>The flame burning on it (its effect entity), or null.</summary>
    public SsEntityFlame? Flame => Flames.On(Handle);

    /// <summary>
    /// CBaseAnimating::Ignite: nothing if it is already burning; otherwise a
    /// flame for <paramref name="lifetime"/> seconds, and OnIgnite, which fires
    /// even when no flame could be made.
    /// </summary>
    public virtual void Ignite(float lifetime, float size = 0f)
    {
        if (IsOnFire)
            return;

        Flames.Ignite(Handle, lifetime, size);
        OnIgnite.Fire(Handle);
    }

    /// <summary>The flame on it, set alight first (stock's 30 s) when there is none.</summary>
    private SsEntityFlame? Burning()
    {
        if (!IsOnFire)
            Ignite(30f);

        return Flames.On(Handle);
    }

    /// <summary>
    /// CBaseAnimating::SetModelScale: at once, or over
    /// <paramref name="changeDuration"/> seconds, a step every tick.
    /// </summary>
    public void SetModelScale(float scale, float changeDuration)
    {
        if (changeDuration > 0f)
        {
            _scaleStart = _modelScale;
            _scaleGoal = scale;
            _scaleStartTime = GameTime;
            _scaleFinishTime = GameTime + changeDuration;
            SetContextThink(ModelScaleContext, 0f);
            return;
        }

        _scaleFinishTime = 0f;
        WriteModelScale(scale);
    }

    private protected override bool TakeBaseThink(string context)
    {
        if (context != ModelScaleContext)
            return false;

        UpdateModelScale();
        return true;
    }

    /// <summary>CBaseAnimating::UpdateModelScale: the lerp toward the goal, and the goal at the finish.</summary>
    private void UpdateModelScale()
    {
        if (_scaleFinishTime <= 0f)
            return;

        float now = GameTime;
        float frac = Math.Clamp((now - _scaleStartTime) / (_scaleFinishTime - _scaleStartTime), 0f, 1f);

        if (now >= _scaleFinishTime)
        {
            _scaleFinishTime = 0f;
            WriteModelScale(_scaleGoal);
        }
        else
        {
            WriteModelScale(_scaleStart + (_scaleGoal - _scaleStart) * frac);
        }

        if (frac < 1f)
            SetContextThink(ModelScaleContext, 0f);
    }

    private void WriteModelScale(float scale)
    {
        _modelScale = scale;
        Render.SetModelScale(Handle, scale);
    }
}

/// <summary>
/// The map keys every <see cref="AnimatingEntity"/> declares: CBaseAnimating's
/// keyfields that act on the entity.
/// </summary>
[ScriptVars]
public partial class AnimatingVars
{
    /// <summary>m_nSkin: which skin of its model it is drawn with.</summary>
    [VarName("skin")]
    public int Skin { get; set; }

    /// <summary>m_nBody: which bodygroup combination it is drawn with.</summary>
    [VarName("body")]
    public int Body { get; set; }

    /// <summary>The same field by the input's name, which is also a key.</summary>
    public int SetBodyGroup { get; set; }

    /// <summary>m_flModelScale.</summary>
    [VarName("modelscale")]
    public float ModelScale { get; set; } = 1f;
}
