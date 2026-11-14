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

    /// <summary>
    /// <c>RemoveAllCustomAttributes</c> (tf_player.cpp:22874-22882): every
    /// name the map holds leaves the list and the map is emptied — the spawn's
    /// and the death's sweep (:3906, :12925; <see cref="TFCustomAttributeLifetime"/>).
    /// </summary>
    public void RemoveAll()
    {
        foreach (string name in _expiries.Keys)
        {
            if (schema.Attribute(name) is { } definition)
                Manager()?.Remove(definition.Defindex);
        }

        _expiries.Clear();
    }

    /// <summary>
    /// The list moves with the player when a kit builds them a new manager
    /// (<see cref="Econ.TFLoadoutProvider.Weapons"/>): every name the map
    /// holds takes its value from <paramref name="from"/>'s own list onto
    /// <paramref name="to"/>'s. Stock has one manager for the player's life,
    /// so a regeneration never touches <c>m_AttributeList</c>; only
    /// <see cref="Remove"/>, the expiry and <see cref="RemoveAll"/> do.
    /// </summary>
    public void CarryTo(AttributeContainer from, AttributeContainer to)
    {
        foreach (string name in _expiries.Keys)
        {
            if (schema.Attribute(name) is not { } definition)
                continue;

            foreach (ref readonly var value in from.Attributes)
            {
                if (value.Definition.Defindex == definition.Defindex)
                {
                    to.Set(value);
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

/// <summary>
/// The runtime list's two sweeps, where stock runs
/// <c>RemoveAllCustomAttributes</c>: at the end of the spawn
/// (<c>CTFPlayer::Spawn</c>, tf_player.cpp:3906 — after <c>InitClass</c>
/// has handed out the kit and set the health, so the hook is the spawn's
/// <see cref="OnSpawned"/>, ordered after the kit's own arms) and at the
/// death (<c>Event_Killed</c>, :12925). A regeneration is neither: the
/// locker leaves the list alone, and the kit it re-runs carries the list
/// onto the manager it builds (<see cref="Econ.TFLoadoutProvider.Weapons"/>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFCustomAttributeLifetime(TFPlayerCarriers carriers)
    : IPlayerSpawnHook, IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.CustomAttributeLifetime;

    public void OnSpawning(EntityHandle player, float now) { }

    public void OnSpawned(EntityHandle player, float now)
        => carriers.Part<TFPlayerAttributes>(player.EntryIndex)?.RemoveAll();

    public void OnPlayerDied(PlayerDeath death)
        => carriers.Part<TFPlayerAttributes>(death.Player.EntryIndex)?.RemoveAll();
}

/// <summary>
/// Attributes a player holds that are nobody's item and are not the runtime
/// list's: a mod's standing bonuses (a character sheet's maximum health, its
/// move speed), which must be on the player whenever a hook walks them and
/// must not be re-added by hand after every kit.
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>): every source is asked each time
/// the player's attribute manager is built — the spawn's kit, a locker's
/// regeneration, a kit change (<see cref="Econ.TFLoadoutProvider.Weapons"/>)
/// — and each container answered provides to the new manager, after the
/// kit's own items, as an equipped item does (stock's
/// <c>CAttributeManager::ProvideTo</c>). The container stays the source's:
/// a value it sets or removes afterwards is what the next hook walk reads,
/// with no rebuild, and the source keeps answering the same container for as
/// long as the bonuses stand. A source with nothing for the player answers
/// null; one that throws is named on the log and has provided nothing.
/// </para>
///
/// <para>
/// Not the runtime list (<see cref="TFPlayerAttributes"/>): that one is
/// stock's <c>AddCustomAttribute</c>, timed, and swept at the spawn and the
/// death. What a source provides is there for as long as the source says so.
/// </para>
/// </summary>
public interface ITFPlayerAttributeSource
{
    /// <summary>
    /// The provider holding <paramref name="player"/>'s standing attributes,
    /// or null when this source has none for them.
    /// </summary>
    AttributeContainer? AttributesOf(EntityHandle player);
}
