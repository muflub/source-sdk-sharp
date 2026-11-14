//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Client.Player;

/// <summary>
/// The client's <see cref="ITFWeaponOwners"/>, for the local player's
/// predicted weapons: the class and the crit multiplier from the carrier
/// (the predicted working copy for the local player, the decoded state for
/// anyone else), and the conditions as a <see cref="TFConditions"/> bound to
/// the WORKING COPY's five words — so a weapon's <c>TF_COND_AIMING</c> on a
/// replayed command is in the copy the movement reads and the ring saves,
/// and the next frame's reset to the server's words is what the set reads
/// after it. Only the local player is predicted: anyone else has no
/// conditions to write.
///
/// <para>
/// <see cref="WinsCrits"/> is the round-win arm of
/// <c>CalcIsAttackCritical</c>: stock's team_win block (tf_weaponbase.cpp:1563)
/// crits the winners outright, not only through the 16 s
/// <c>TF_COND_CRITBOOSTED_BONUS_TIME</c> grant, so the prediction reads the
/// resource's <c>RoundState</c> and <c>WinningTeam</c> words the server writes
/// every think — the same read the respawn countdown and the HUD make.
/// </para>
///
/// <para>
/// <see cref="ItemOf"/> answers the item behind a predicted weapon from the
/// definition index the carrier carries for that weapon row — what stock
/// networks on each weapon's own item view (<c>DT_ScriptCreatedItem</c>'s
/// <c>m_iItemDefinitionIndex</c>, econ_item_view.cpp:182), kept here as one
/// array on the TF carrier (see <see cref="TFPlayerNetState.WeaponItemDefIndex"/>):
/// the client looks the defindex up in its own parsed schema
/// (<c>CEconItemView::GetItemDefinition</c>, econ_item_view.h:194, whose
/// <c>GetStaticData</c> looks the index up in the schema at
/// econ_item_view.cpp:435) and hands
/// back the definition's static attributes, so the predicted hooks walk the
/// same table the server's walk does. It reads the DECODED state, as stock
/// reads the server's item view: the defindex is the loadout's word, not a
/// prediction (CritMult's rule). What the definition does not hold — an
/// item's own values, the runtime list, a standing source's — rides the
/// carrier's attribute lists (<see cref="TFAttributeWire"/>), stock's
/// networked <c>m_AttributeManager</c> (tf_player.h:647, sent at
/// tf_player.cpp:831) and item views: with them, <see cref="ItemOf"/> and
/// <see cref="ManagerOf"/> answer the server's walk for any player.
/// </para>
///
/// <para>
/// <see cref="ITFWeaponOwners.EyesUnderwater"/> and <see cref="ITFWeaponOwners.OffGround"/>
/// read the standalones' player state — the networked <c>WaterLevel</c> and
/// flags word the local player's prediction
/// moves, the same read the underwater ambience makes. Only the local player
/// is predicted, so the flamethrower's refusals are theirs to read; anyone
/// else's shell runs on the server.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFWeaponOwners)])]
public sealed class TFClientWeaponOwners(ITFPlayerData data, IClientStandalones standalones,
                                         TFClientPlayerResources resources,
                                         ItemSchema itemSchema, WeaponCatalog catalog,
                                         ILocalArsenal arsenal, IEngineHud hud)
    : ITFWeaponOwners
{
    /// <summary>WL_Eyes (<c>GameMovement.WlEyes</c>, which a client class cannot name).</summary>
    private const int WaterLevelEyes = 3;

    /// <summary>FL_ONGROUND (<c>public/const.h</c>, <c>GameMovement.FlOnGround</c>).</summary>
    private const uint FlOnGround = 1u << 0;

    /// <summary>FL_DUCKING (<c>public/const.h</c>, <c>GameMovement.FlDucking</c>).</summary>
    private const uint FlDucking = 1u << 1;

    private TFConditions? _conditions;
    private TFPlayerNetState? _bound;

    /// <summary>The bare chain, for the manager's own attribute reads (the
    /// provide gate) when the composition hands none.</summary>
    private readonly AttributeHooks _hooks = new();

    /// <summary>The player's manager, cached against the carrier, the defindex
    /// words and the attribute lists' serial it was built from; with the
    /// lists, the kit's items by weapon row (null while the lists are empty).</summary>
    private readonly Dictionary<int, (TFPlayerNetState Bound, int[] Rows, int Serial, AttributeContainer Manager,
                                      Dictionary<int, AttributeContainer>? Items)> _managers = [];

    public TFClass ClassOf(int playerIndex) => State(playerIndex)?.Class() ?? TFClass.Undefined;

    public TFConditions? ConditionsOf(int playerIndex)
    {
        if (data.Predicted(playerIndex) is not { } working)
            return null;

        if (!ReferenceEquals(working, _bound))
        {
            _bound = working;
            _conditions = new TFConditions(authoritative: false, new TFCarrierCondWords(working));
        }

        return _conditions;
    }

    public float CritMult(int playerIndex) => State(playerIndex)?.CritMultiplier() ?? 1f;

    // Sent, not predicted (the arrival's word): the replay reads the working
    // copy the snapshot decoded, so the refusal matches the server's frame.
    public float NoAttackExpire(int playerIndex) => State(playerIndex)?.NoAttackExpire ?? 0f;

    // Sent, not predicted (the arrival's word): the death hook's count, as the
    // replay's charge rate reads the snapshot's.
    public int DecapitationsOf(int playerIndex) => State(playerIndex)?.Decapitations ?? 0;

    public bool WinsCrits(int playerIndex)
        => resources.Live is { } live
           && playerIndex is > 0 and < TFPlayerResourceNetState.Slots
           && live.State.RoundState == (byte)RoundState.TeamWin
           && live.State.WinningTeam == live.State.Team[playerIndex];

    /// <summary>
    /// The item behind <paramref name="weaponName"/> for <paramref name="playerIndex"/>:
    /// with the attribute lists, the item the server's walk holds — its own
    /// values, owned by the player's manager (<see cref="TFAttributeWire.Read"/>);
    /// without them, the definition's statics alone.
    /// </summary>
    public IAttributeProvider? ItemOf(int playerIndex, string weaponName)
    {
        if (Cached(playerIndex) is { Items: { } items }
            && catalog.IndexOf(weaponName) is int row and >= 0
            && items.TryGetValue(row, out var item))
            return item;

        return ItemOf(itemSchema, catalog, data.Networked(playerIndex), weaponName);
    }

    /// <summary>
    /// The item walk itself, static so the replay's press reads the same table
    /// this does: the row the carrier's defindex array names, looked up in the
    /// parsed schema. No carrier — or a row and defindex that name nothing —
    /// is no item.
    /// </summary>
    internal static IAttributeProvider? ItemOf(ItemSchema itemSchema, WeaponCatalog catalog,
                                               TFPlayerNetState? state, string weaponName)
    {
        if (state is null)
            return null;

        int row = catalog.IndexOf(weaponName);
        if (row < 0 || row >= state.WeaponItemDefIndex.Length)
            return null;

        // The wire keeps defindex + 1 (the array's untouched zero is no item
        // and the schema's own item 0, the bat, stays nameable).
        if (itemSchema.Item(state.WeaponItemDefIndex[row] - 1) is not { } definition)
            return null;

        // Stock's client-side item view (CEconItemView over the server's
        // defindex); Owner stays null — the player's own manager is
        // ManagerOf's deferred story.
        return new AttributeContainer(definition, AttributeProviderType.Weapon);
    }

    /// <summary>
    /// The player's own attribute manager (stock's <c>m_AttributeManager</c>,
    /// tf_player.h:647, sent at tf_player.cpp:831): every item the carrier's
    /// rows hold behind them — ItemOf's defindex word — provides to it, each
    /// answering the manager as its owner, the shape the server's kit gives its
    /// players (TFLoadoutProvider.Weapons). Cached per player against the
    /// carrier it was built from and the defindex words behind it: a new kit
    /// writes new rows and rebuilds it, the same moment the server's rebuilds
    /// its own, so a hook walked on the player answers alike on both sides
    /// (the AirJump parachute ask, the blast-jump scale, the medigun's ring).
    /// </summary>
    public IAttributeProvider? ManagerOf(int playerIndex) => Cached(playerIndex)?.Manager;

    /// <summary>
    /// The player's manager and items, rebuilt once when the carrier, its
    /// defindex words or its attribute lists' serial moved. With lists, the
    /// server's walk read back (<see cref="TFAttributeWire.Read"/>), so every
    /// client walks any player's items alike (user ruling L11); without them,
    /// <see cref="BuildManager"/>.
    /// </summary>
    private (TFPlayerNetState Bound, int[] Rows, int Serial, AttributeContainer Manager,
             Dictionary<int, AttributeContainer>? Items)? Cached(int playerIndex)
    {
        if (itemSchema is null || catalog is null || data.Networked(playerIndex) is not { } state)
            return null;

        var rows = state.WeaponItemDefIndex;
        if (_managers.TryGetValue(playerIndex, out var cache) && Holds(cache, state, rows))
            return cache;

        Dictionary<int, AttributeContainer>? items = null;
        AttributeContainer manager;
        if (state.AttributeProviderCount > 0)
        {
            items = [];
            manager = TFAttributeWire.Read(state, itemSchema, items) ?? new AttributeContainer();
        }
        else
        {
            manager = BuildManager(playerIndex, state);
        }

        var held = new int[rows.Length];
        for (int i = 0; i < held.Length; i++)
            held[i] = rows[i];

        var built = (state, held, state.AttributeSerial, manager, items);
        _managers[playerIndex] = built;
        return built;
    }

    /// <summary>Whether the cache still stands on the carrier it was built from,
    /// with the defindex words and the lists' serial that built it.</summary>
    private static bool Holds((TFPlayerNetState Bound, int[] Rows, int Serial, AttributeContainer Manager,
                               Dictionary<int, AttributeContainer>? Items) cache,
                              TFPlayerNetState state, NetArray<int> rows)
    {
        if (!ReferenceEquals(cache.Bound, state) || cache.Rows.Length != rows.Length
            || cache.Serial != state.AttributeSerial)
            return false;

        for (int i = 0; i < cache.Rows.Length; i++)
        {
            if (cache.Rows[i] != rows[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// The manager itself, shaped as the server's kit builds it: each item
    /// gated by its own <c>provide_on_active</c> mode before it provides
    /// (stock's ReapplyProvision, tf_weaponbase.cpp:5709-5740), every item
    /// then answering the manager as its owner. This side names the held
    /// weapon through the arsenal; nobody else's hand is known here, and a
    /// player with no arsenal reads as stock's no-active answer — mode 1
    /// items stay out until their weapon is held.
    /// </summary>
    private AttributeContainer BuildManager(int playerIndex, TFPlayerNetState state)
    {
        var manager = new AttributeContainer();
        string active = hud is not null && arsenal is not null && playerIndex == hud.LocalPlayerIndex
                      && arsenal.TryGetActive(out string heldWeapon, out _, out _)
            ? heldWeapon
            : string.Empty;

        var rows = state.WeaponItemDefIndex;
        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row] == 0 || catalog!.ByIndex(row) is not { } type)
                continue;

            // The item itself, ItemOf's builder: the manager's provider answers
            // exactly what the held-weapon read answers.
            if (ItemOf(itemSchema!, catalog, state, type.Name) is not AttributeContainer item)
                continue;

            if (_hooks.Int(0, "provide_on_active", item) != 1 || catalog.IndexOf(active) == row)
                item.ProvideTo(manager);

            item.Owner = manager;
        }

        return manager;
    }

    /// <summary>
    /// The local player's active weapon, as the arsenal holds it; the client
    /// predicts nobody else's hand, so anyone else is stock's no-weapon arm.
    /// </summary>
    public IAttributeProvider? ActiveItemOf(int playerIndex)
    {
        if (playerIndex != hud.LocalPlayerIndex)
            return null;

        return arsenal.TryGetActive(out string name, out _, out _)
            ? ItemOf(playerIndex, name)
            : null;
    }

    public bool OffGround(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player
           && (player.State.Flags & FlOnGround) == 0;

    public bool EyesUnderwater(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player
           && player.State.WaterLevel == WaterLevelEyes;

    public bool DuckingOf(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player
           && (player.State.Flags & FlDucking) != 0;

    /// <summary>
    /// The drawn bow holds the jump, read off the arsenal's state by the name
    /// <see cref="ILocalArsenal.TryGetActive"/> found — the local player's own
    /// prediction, whose feet the client's replay moves. Nobody else's jump is
    /// predicted here, and a state the arsenal cannot serve is no refusal.
    /// </summary>
    public bool OwnerCanJump(int playerIndex)
        => playerIndex != hud.LocalPlayerIndex
           || !arsenal.TryGetActive(out string name, out _, out _)
           || TFWeaponNames.IdOf(name) != TFWeaponId.CompoundBow
           || arsenal.ActiveWeaponState is not { } state
           || !TFBowBehaviour.Drawn(state);

    /// <summary>
    /// The pomson's ring hit on the predicted copy (<c>tf_weaponbase.cpp:5568</c>,
    /// :5592): the local medic'sÜberCharge rides the held medigun's charge word,
    /// the spy's cloak the carrier's hundred-point meter. Nobody else's meters
    /// are predicted, so nobody else is drained here — the server's decoded
    /// words carry theirs.
    /// </summary>
    public bool DrainOnHitMeters(int playerIndex, float medicFraction, float spyFraction)
    {
        bool drained = false;

        if (medicFraction > 0f && ClassOf(playerIndex) == TFClass.Medic
            && arsenal.TryGetActive(out _, out _, out _)
            && arsenal.ActiveWeaponState is { } weapon)
        {
            weapon.BehaviourMeter = Math.Max(0f, weapon.BehaviourMeter - medicFraction);
            drained = true;
        }

        if (spyFraction > 0f && ClassOf(playerIndex) == TFClass.Spy
            && data.Predicted(playerIndex) is { } state)
        {
            state.CloakMeter = Math.Max(0f, state.CloakMeter - spyFraction * 100f);
            drained = true;
        }

        return drained;
    }

    private TFPlayerNetState? State(int playerIndex) => data.Predicted(playerIndex) ?? data.Networked(playerIndex);
}
