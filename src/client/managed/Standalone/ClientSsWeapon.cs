using SourceSharp.Abi;
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
    public ClientSsWeapon(IEngineEntityRender render)
        : base(render)
    {
    }

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
    }
}
