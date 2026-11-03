//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace TF2.Shared.Econ;

/// <summary><c>attributeprovidertypes_t</c> (attribute_manager.h:22-26).</summary>
public enum AttributeProviderType
{
    Generic,
    Weapon,
}

/// <summary>
/// One entity's attribute manager as the hooks walk it: what
/// <c>CAttributeManager</c> and its two containers hold. Its own attributes
/// (<c>CAttributeContainer</c>'s item, <c>CAttributeContainerPlayer</c>'s
/// player list), the entities that provide to it (<c>m_Providers</c>), and the
/// owner it asks last (<c>IHasAttributes::GetAttributeOwner</c>).
///
/// <para>
/// Spans and an indexed list, so a walk allocates nothing.
/// </para>
/// </summary>
public interface IAttributeProvider
{
    /// <summary><c>GetProviderType</c>: a weapon never provides to its owner's other weapons.</summary>
    AttributeProviderType ProviderType { get; }

    /// <summary>
    /// The attributes this entity holds itself (an item's <c>CAttributeList</c>,
    /// the player's <c>m_AttributeList</c>), in their order. They shadow a
    /// static attribute of the same definition.
    /// </summary>
    ReadOnlySpan<AttributeValue> Attributes { get; }

    /// <summary>
    /// The item definition's static attributes, walked after <see cref="Attributes"/>
    /// and skipping any definition that list holds (<c>CEconItemView::IterateAttributes</c>,
    /// econ_item_view.cpp:523-640). Empty for an entity with no item.
    /// </summary>
    ReadOnlySpan<AttributeValue> StaticAttributes { get; }

    /// <summary><c>m_Providers</c>, in order; a null entry is a provider that is gone.</summary>
    IReadOnlyList<IAttributeProvider?> Providers { get; }

    /// <summary><c>GetAttributeOwner</c>: the entity asked after the providers (a weapon's carrier); null for none.</summary>
    IAttributeProvider? Owner { get; }
}

/// <summary>
/// A plain <see cref="IAttributeProvider"/>: <c>CAttributeManager</c>'s provider
/// bookkeeping (<c>ProvideTo</c>, <c>StopProvidingTo</c>, attribute_manager.cpp:180-249)
/// over an optional item definition and a list of the entity's own attributes.
/// </summary>
public sealed class AttributeContainer(ItemDefinition? item = null, AttributeProviderType providerType = AttributeProviderType.Generic)
    : IAttributeProvider
{
    private readonly List<AttributeValue> _attributes = [];
    private readonly List<IAttributeProvider?> _providers = [];
    private readonly List<AttributeContainer> _receivers = [];

    public ItemDefinition? Item { get; } = item;

    public AttributeProviderType ProviderType { get; } = providerType;

    public ReadOnlySpan<AttributeValue> Attributes => CollectionsMarshal.AsSpan(_attributes);

    public ReadOnlySpan<AttributeValue> StaticAttributes => Item is null ? [] : Item.StaticAttributes;

    public IReadOnlyList<IAttributeProvider?> Providers => _providers;

    /// <summary>Who this one provides to (<c>m_Receivers</c>).</summary>
    public IReadOnlyList<AttributeContainer> Receivers => _receivers;

    public IAttributeProvider? Owner { get; set; }

    /// <summary>
    /// Sets one of this entity's own attributes: replaces the value of a
    /// definition already in the list, else appends (<c>CAttributeList::SetRuntimeAttributeValue</c>).
    /// </summary>
    public void Set(AttributeValue value)
    {
        for (int i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Definition.Defindex == value.Definition.Defindex)
            {
                _attributes[i] = value;
                return;
            }
        }
        _attributes.Add(value);
    }

    /// <summary>
    /// <c>CAttributeList::RemoveAttribute</c> (econ_item_view.cpp:1829-1841):
    /// the first attribute of the definition goes; false when the list holds none.
    /// </summary>
    public bool Remove(int defindex)
    {
        for (int i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Definition.Defindex == defindex)
            {
                _attributes.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary><c>ProvideTo</c>: <paramref name="receiver"/> is provided to by this one, at the end of its list.</summary>
    public void ProvideTo(AttributeContainer receiver)
    {
        if (receiver._providers.Contains(this))
            return;
        receiver._providers.Add(this);
        _receivers.Add(receiver);
    }

    /// <summary>
    /// <c>StopProvidingTo</c>: <c>RemoveProvider</c>'s <c>FindAndFastRemove</c>,
    /// which moves the receiver's LAST provider into the freed place, so the
    /// order the rest are applied in changes as stock's does.
    /// </summary>
    public void StopProvidingTo(AttributeContainer receiver)
    {
        if (FastRemove(receiver._providers, this))
            FastRemove(_receivers, receiver);
    }

    private static bool FastRemove<T>(List<T> list, T item) where T : class?
    {
        int i = list.IndexOf(item);
        if (i < 0)
            return false;
        list[i] = list[^1];
        list.RemoveAt(list.Count - 1);
        return true;
    }
}

/// <summary>
/// <c>CALL_ATTRIB_HOOK_FLOAT/INT/STRING</c> (attribute_manager.h:40-55): a value
/// passed through every attribute of a hook class that the entity, the entities
/// providing to it and its owner hold.
///
/// <para>
/// <b>Order</b> (stage1 Corrections 5): the entity's own attributes first
/// (<c>CAttributeContainer::ApplyAttributeFloat</c>, attribute_manager.cpp:718-735),
/// then each provider in <c>m_Providers</c>' order, each walked the same way,
/// then the owner (<c>CAttributeManager::ApplyAttributeFloat</c>, :442-497).
/// Each attribute is applied as it is met (<c>ApplyAttribute</c>, :582-627):
/// percentage and inverted percentage multiply, additive, additive percentage
/// and particle index add, the killstreak and lookup formats overwrite, <c>or</c>
/// ors the integers; any other format leaves the value. There is no
/// add-then-multiply pass: two providers compose in the order they are met.
/// </para>
///
/// <para>
/// A provider that is the initiator is skipped, a weapon never applies to a
/// weapon its owner carries, and a manager already on the walk answers the
/// value unchanged (<c>m_bPreventLoopback</c>): the walk's own stack is that
/// flag. Stock's per-manager result cache (<c>ApplyAttributeFloatWrapper</c>)
/// is not kept: it answers what the walk answers.
/// </para>
///
/// <para>
/// One per side, not thread-safe (the side's game thread owns it); a walk
/// allocates nothing.
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class AttributeHooks
{
    private readonly List<IAttributeProvider> _walking = new(16);

    /// <summary><c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( entity, value, hook )</c>.</summary>
    public float Float(float value, string? attributeClass, IAttributeProvider? entity)
    {
        if (string.IsNullOrEmpty(attributeClass) || entity is null)
            return value;
        return ApplyFloat(value, entity, entity, attributeClass);
    }

    /// <summary>
    /// <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c>: the float walk over the value as a
    /// float, rounded back as <c>RoundFloatToInt</c> rounds (<c>AttributeConvertFromFloat&lt;int&gt;</c>,
    /// attribute_manager.cpp:102-105).
    /// </summary>
    public int Int(int value, string? attributeClass, IAttributeProvider? entity) =>
        RoundFloatToInt(Float(value, attributeClass, entity));

    /// <summary>
    /// <c>CALL_ATTRIB_HOOK_STRING_ON_OTHER</c>: the first string attribute of the
    /// class an entity holds replaces the value (a second is ignored), then the
    /// providers' and the owner's in the walk's order, so the last one met wins
    /// (<c>CEconItemAttributeIterator_ApplyAttributeString</c>, :784-840).
    /// </summary>
    public string? String(string? value, string? attributeClass, IAttributeProvider? entity)
    {
        if (string.IsNullOrEmpty(attributeClass) || entity is null)
            return value;
        return ApplyString(value, entity, entity, attributeClass);
    }

    /// <summary>
    /// <c>RoundFloatToInt</c> (mathlib.h:1175): <c>cvtss2si</c> under the default
    /// rounding mode, nearest with ties to even; NaN and out-of-range answer
    /// the integer indefinite, <c>int.MinValue</c>.
    /// </summary>
    public static int RoundFloatToInt(float f)
    {
        float r = MathF.Round(f, MidpointRounding.ToEven);
        return float.IsNaN(r) || r >= 2147483648f || r < -2147483648f ? int.MinValue : (int)r;
    }

    /// <summary><c>ApplyAttribute</c> (attribute_manager.cpp:582-627) for one attribute's value.</summary>
    public static float Apply(AttributeDescriptionFormat format, float value, float modifier)
    {
        switch (format)
        {
            case AttributeDescriptionFormat.Percentage:
            case AttributeDescriptionFormat.InvertedPercentage:
                return value * modifier;

            case AttributeDescriptionFormat.Additive:
            case AttributeDescriptionFormat.AdditivePercentage:
            case AttributeDescriptionFormat.ParticleIndex:
                return value + modifier;

            case AttributeDescriptionFormat.KillstreakIdleEffectIndex:
            case AttributeDescriptionFormat.KillstreakEffectIndex:
            case AttributeDescriptionFormat.FromLookupTable:
                return modifier;

            case AttributeDescriptionFormat.Or:
                return (int)value | (int)modifier;

            default:
                // Dates and unknown formats: stock asserts and leaves the value.
                return value;
        }
    }

    private bool Walking(IAttributeProvider manager)
    {
        foreach (var m in _walking)
        {
            if (ReferenceEquals(m, manager))
                return true;
        }
        return false;
    }

    private static bool Hooks(in AttributeValue attribute, string hook) =>
        attribute.Definition.ValueType == AttributeValueType.Default
        && string.Equals(attribute.Definition.AttributeClass, hook, StringComparison.OrdinalIgnoreCase);

    private static bool Shadowed(ReadOnlySpan<AttributeValue> own, int defindex)
    {
        foreach (ref readonly var a in own)
        {
            if (a.Definition.Defindex == defindex)
                return true;
        }
        return false;
    }

    private static bool Skipped(IAttributeProvider? provider, IAttributeProvider initiator) =>
        provider is null
        || ReferenceEquals(provider, initiator)
        || (provider.ProviderType == AttributeProviderType.Weapon && initiator.ProviderType == AttributeProviderType.Weapon);

    private float ApplyFloat(float value, IAttributeProvider manager, IAttributeProvider initiator, string hook)
    {
        if (Walking(manager))
            return value;

        _walking.Add(manager);
        try
        {
            var own = manager.Attributes;
            foreach (ref readonly var a in own)
            {
                if (Hooks(a, hook))
                    value = Apply(a.Definition.DescriptionFormat, value, a.AsFloat);
            }
            foreach (ref readonly var a in manager.StaticAttributes)
            {
                if (Hooks(a, hook) && !Shadowed(own, a.Definition.Defindex))
                    value = Apply(a.Definition.DescriptionFormat, value, a.AsFloat);
            }

            var providers = manager.Providers;
            for (int i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                if (!Skipped(provider, initiator))
                    value = ApplyFloat(value, provider!, initiator, hook);
            }

            if (manager.Owner is { } owner)
                value = ApplyFloat(value, owner, initiator, hook);

            return value;
        }
        finally
        {
            _walking.RemoveAt(_walking.Count - 1);
        }
    }

    private static bool HooksString(in AttributeValue attribute, string hook) =>
        attribute.Definition.ValueType == AttributeValueType.String
        && string.Equals(attribute.Definition.AttributeClass, hook, StringComparison.OrdinalIgnoreCase);

    private string? ApplyString(string? value, IAttributeProvider manager, IAttributeProvider initiator, string hook)
    {
        if (Walking(manager))
            return value;

        _walking.Add(manager);
        try
        {
            bool found = false;
            var own = manager.Attributes;
            foreach (ref readonly var a in own)
            {
                if (!found && HooksString(a, hook))
                {
                    value = a.Text;
                    found = true;
                }
            }
            foreach (ref readonly var a in manager.StaticAttributes)
            {
                if (!found && HooksString(a, hook) && !Shadowed(own, a.Definition.Defindex))
                {
                    value = a.Text;
                    found = true;
                }
            }

            var providers = manager.Providers;
            for (int i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                if (!Skipped(provider, initiator))
                    value = ApplyString(value, provider!, initiator, hook);
            }

            if (manager.Owner is { } owner)
                value = ApplyString(value, owner, initiator, hook);

            return value;
        }
        finally
        {
            _walking.RemoveAt(_walking.Count - 1);
        }
    }
}
