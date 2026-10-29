using SourceSharp.Abi;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of the carried weapon (Phase 10).
///
/// <para>
/// What arrives is <see cref="WeaponNetState"/> — whose weapon this is, which
/// weapon it currently IS, and the firing state the local player predicts.
/// What this class does with it is presentation: the w_ model rides the
/// carrier's skeleton through the bonemerge follower, the same mechanism
/// cs_bonemerge proved live before any weapon existed to ship with it.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsWeapon)]
public sealed class ClientSsWeapon : ClientStandaloneEntity
{
    /// <summary>A weapon with no draw effects and no render policy to ask (a test's).</summary>
    public ClientSsWeapon(IEngineEntityRender render)
        : this(render, new NoWeaponDrawEffects(), new NoRenderPolicyWishes())
    {
    }

    [Inject]
    public ClientSsWeapon(IEngineEntityRender render, IWeaponDrawEffects effects, IRenderPolicyWishes policy)
        : base(render)
    {
        _effects = effects;
        _policy = policy;
    }

    private readonly IWeaponDrawEffects _effects;
    private readonly IRenderPolicyWishes _policy;

    // The type the two-pass wish was last made for, so it is asked once per change.
    private int _wishedFor = int.MinValue;

    public override string ClassName => EntityNames.SsWeapon;

    /// <summary>The same type the server writes through, decoded per update.</summary>
    [NetState] public WeaponNetState State = new();

    private int _mergedOnto = -1;

    /// <summary>
    /// How many updates have decoded into <see cref="State"/>. The replay's
    /// compare is gated on this moving: a fresh decode means the state holds
    /// the server's word and the acknowledged slot has something to be judged
    /// against (see ClientArsenal).
    /// </summary>
    public int Decodes { get; private set; }

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;

        // Which render group this weapon answers is its type's: stock's
        // stunstick and physcannon draw their glows in the translucent pass of
        // a two-pass model (GetRenderGroup).
        if (State.WeaponTypeId != _wishedFor)
        {
            _wishedFor = State.WeaponTypeId;
            _policy.WantTwoPass(Handle, _effects.WantsTwoPass(State.WeaponTypeId));
        }

        // The carrier is named by ENTINDEX — players hold 1..maxplayers on
        // both sides identically, which is what lets an entity reference cross
        // the wire without a serial-number agreement.
        int owner = State.OwnerIndex;

        if (owner > 0 && State.WeaponTypeId >= 0)
        {
            if (owner != _mergedOnto)
            {
                Render.SetBoneMergeSource(Handle, owner);
                _mergedOnto = owner;
            }
        }
        else if (_mergedOnto >= 0)
        {
            Render.SetBoneMergeSource(Handle, -1);
            _mergedOnto = -1;
        }
    }

    public override void OnRemoved()
    {
        _mergedOnto = -1;
        _policy.WantTwoPass(Handle, false);
        _wishedFor = int.MinValue;
    }

    /// <summary>The translucent pass of a two-pass weapon: its type's draw effects.</summary>
    public override int OnDrawExtras(int flags) => _effects.DrawWorldModel(this, flags);
}
