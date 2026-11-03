//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>
/// The player's own runtime attribute list — the server side of
/// <c>CTFPlayer::AddCustomAttribute</c>/<c>RemoveCustomAttribute</c>
/// (server/tf/tf_player.cpp:22813-22842) and the expiry clock of
/// <c>CTFPlayer::UpdateCustomAttributes</c> (:22848-22869, ticked from
/// PreThink :1374). The value lands on <c>m_AttributeList</c>, the player
/// manager's own list, as <c>CTFPlayerShared::ApplyAttributeToPlayer</c>
/// resolves the name through the schema's attribute definitions
/// (tf_player_shared.cpp:9324-9333) and answers, like stock, to nothing for a
/// name no definition carries; <see cref="Remove"/> is stock's guard that only
/// a name the expiry map holds (<c>m_mapCustomAttributes</c>, a
/// <c>CUtlMap&lt;CUtlString, float&gt;</c>, tf_player.h:1551) reaches the list.
/// The map holds the absolute expire time (duration &gt; 0) or stock's
/// duration itself for a permanent one, and <see cref="Tick"/> drops an
/// expired name the way stock's restart-on-removal loop does, one per pass.
///
/// <para>
/// What stock wraps around the same two calls that this build has no half of:
/// <c>TeamFortress_SetSpeed</c> and
/// <c>GetAttributeManager()->OnAttributeValuesChanged()</c> — our maximum
/// speed is recomputed per call, so there is no cached value to refresh
/// so there is no cached value to refresh, and the manager's walk reads the
/// list as it stands. A runtime value is the 32-bit float only, as
/// <c>SetRuntimeAttributeValue</c> writes it (econ_item_view.cpp:1754-1780);
/// the string-typed attributes keep whatever Text their absence implies.
/// </para>
/// </summary>
public sealed class TFPlayerAttributes(TFPlayerCarriers carriers, TFPlayerItems items, ItemSchema schema, IGameClock clock)
    : IPlayerComponent
{
    /// <summary><c>m_mapCustomAttributes</c>: name to absolute expire time, 0 or less for never.</summary>
    private readonly Dictionary<string, float> _expiries = new(StringComparer.Ordinal);

    private int _index = -1;

    /// <summary>After the respawn override (40); the expiry is a housekeeping thought.</summary>
    public int Order => 45;

    /// <summary>
    /// <c>AddCustomAttribute</c>: the value stomps any previous one under the
    /// same name's definition (a re-add replaces the list entry in place,
    /// <c>CAttributeList::SetRuntimeAttributeValue</c>) and the expiry is
    /// stock's stomp too (:22823). No definition, no entry, as
    /// <c>ApplyAttributeToPlayer</c>'s early return.
    /// </summary>
    public void Add(string attributeName, float value, float duration = -1f)
    {
        if (schema.Attribute(attributeName) is not { } definition)
            return;

        _expiries[attributeName] = duration > 0 ? clock.Now + duration : duration;
        if (Manager() is { } manager)
            manager.Set(AttributeValue.OfFloat(definition, value));
    }

    /// <summary>
    /// <c>RemoveCustomAttribute</c>: the list entry goes and the name leaves
    /// the expiry map; a name the map never held is stock's
    /// <c>InvalidIndex</c> early return, and the manager untouched.
    /// </summary>
    public void Remove(string attributeName)
    {
        if (!_expiries.ContainsKey(attributeName))
            return;

        if (schema.Attribute(attributeName) is { } definition)
            Manager()?.Remove(definition.Defindex);
        _expiries.Remove(attributeName);
    }

    /// <summary>
    /// <c>UpdateCustomAttributes</c>: every name whose time has passed
    /// (<c>now &gt; expire</c>, stock's strict comparison) loses its list
    /// entry, one per pass the way stock's restart loop rounds.
    /// </summary>
    public void Tick(float now)
    {
        bool again = true;
        while (again)
        {
            again = false;
            foreach ((string name, float expire) in _expiries)
            {
                if (expire > 0 && now > expire)
                {
                    Expire(name);
                    again = true;
                    break;
                }
            }
        }
    }

    private void Expire(string name)
    {
        if (schema.Attribute(name) is { } definition)
            Manager()?.Remove(definition.Defindex);
        _expiries.Remove(name);
    }

    public void Attach(EntityHandle player)
    {
        _expiries.Clear();
        _index = player.EntryIndex;
        carriers.EnterPart(_index, this);
    }

    public void Detach()
    {
        _expiries.Clear();
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
    }

    /// <summary>The player's attribute manager, whose own list the values land on.</summary>
    private AttributeContainer? Manager() => items.ManagerOf(_index) as AttributeContainer;
}
