using System.Collections.Immutable;
using TF2.Server.Econ;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// One item of the bot's kit as it knows it (plan_bots_teams_tf2 P7, step 2): the slot, the defindex, the item class,
/// the weapon kind its HUD's list selects (-1 for a worn item: a shield, Gunboats) and the item's own attributes, the
/// classes that change how it is played. Read off the bot's own loadout (<see cref="ITFOwnKit"/>), as a player knows it.
/// </summary>
public sealed record TFKitItem(TFLoadoutSlot Slot, int Defindex, string ItemClass, int WeaponKind, ImmutableArray<TFOwnKitAttribute> Attributes)
{
    /// <summary>Whether the item carries an attribute of class <paramref name="attributeClass"/>.</summary>
    public bool Has(string attributeClass)
    {
        foreach (TFOwnKitAttribute a in Attributes)
        {
            if (a.Class == attributeClass)
                return true;
        }

        return false;
    }

    /// <summary>The value of the item's attribute of class <paramref name="attributeClass"/>, or <paramref name="none"/>.</summary>
    public float Attribute(string attributeClass, float none)
    {
        foreach (TFOwnKitAttribute a in Attributes)
        {
            if (a.Class == attributeClass)
                return a.Value;
        }

        return none;
    }

    /// <summary>Whether the item is wielded (it has a weapon kind the HUD's list selects), not worn.</summary>
    public bool Wielded => WeaponKind >= 0;
}

/// <summary>A meter the bot's HUD shows (a Medic's charge, a Soldier's rage), by a name its reader gives it: <c>medic.charge</c>.</summary>
public readonly record struct TFMeter(string Name, float Value);

/// <summary>Why the bot's HUD shows a teammate's health (the coordinator's ruling of 2026-11-20 on the Medic's A7).</summary>
[Flags]
public enum TFMateSeen : byte
{
    None = 0,

    /// <summary>The bot's beam is on them: the Medic's heal-target panel.</summary>
    HealTarget = 1 << 0,

    /// <summary>
    /// They call: to a Medic, a teammate at or under <see cref="TFHud.CallingShare"/> of their health within
    /// <see cref="TFHud.CallingReach"/>, through walls (stock's auto-callers, tf_weapon_medigun.cpp:2487-2562): a flag and
    /// a place, no number. A Spy disguised as the bot's team calls as a teammate does, as stock's HUD has it.
    /// </summary>
    Calling = 1 << 1,

    /// <summary>
    /// The bot's own respawn glow lasts (stock's <c>TF_COND_TEAM_GLOWS</c>, <c>tf_spawn_glows_duration</c> seconds from its
    /// respawn, tf_player.cpp:3636): its HUD outlines every living teammate through walls (tf_hud_spectator_extras.cpp:122-175,
    /// under <c>tf_spec_xray</c>): a place, no number (round 2's S11; recorded from format 11). Only a teammate of the bot's
    /// real team glows: stock force-transmits by real team, so an enemy Spy disguised as the bot's team is sent only in its
    /// PVS and never outlined map-wide.
    /// </summary>
    Glowing = 1 << 2,
}

/// <summary>
/// A teammate the bot's HUD shows, why, and where: the exact health only for the beam's patient (<see cref="TFMateSeen.HealTarget"/>);
/// a caller's or a glowing teammate's <see cref="Health"/> and <see cref="MaxHealth"/> are -1: the HUD shows only that they need
/// a heal, or where they are.
/// </summary>
public readonly record struct TFMateHealth(int Slot, int Health, int MaxHealth, TFMateSeen Why, AiVector At);

/// <summary>
/// What a TF2 bot's own HUD shows past its <see cref="SelfState"/> (plan_bots_teams_tf2 P7): its kit, its meters and
/// the teammates' health its HUD shows. Immutable: the binding makes a new one only when something on it changed, so
/// a decider may keep what it worked out from one (by reference) until it gets another. Not in ActorFacts, which stays
/// health-free: a teammate's health is here only for the reasons <see cref="TFMateSeen"/> names.
/// </summary>
public sealed class TFHud(ImmutableArray<TFKitItem> kit, ImmutableArray<TFMeter> meters, ImmutableArray<TFMateHealth> mates) : GameHud
{
    /// <summary>The auto-caller rule (stock's <c>hud_medicautocallers</c>, a Medic's setting a bot is ruled to have on): at or under 75 % within 1,000 u.</summary>
    public const float CallingShare = 0.75f, CallingReach = 1000f;

    /// <summary>The kit, weapons in slot order then the worn items.</summary>
    public ImmutableArray<TFKitItem> Kit { get; } = kit.IsDefault ? [] : kit;

    /// <summary>The meters, in the order their readers gave them.</summary>
    public ImmutableArray<TFMeter> Meters { get; } = meters.IsDefault ? [] : meters;

    /// <summary>The teammates whose health the HUD shows, by slot.</summary>
    public ImmutableArray<TFMateHealth> Mates { get; } = mates.IsDefault ? [] : mates;

    /// <summary>The wielded item in <paramref name="slot"/>, if the kit has one.</summary>
    public bool TryWielded(TFLoadoutSlot slot, out TFKitItem item)
    {
        foreach (TFKitItem k in Kit)
        {
            if (k.Slot == slot && k.Wielded)
            {
                item = k;
                return true;
            }
        }

        item = null!;
        return false;
    }

    /// <summary>The item, wielded or worn, in <paramref name="slot"/>, if the kit has one.</summary>
    public bool TryItem(TFLoadoutSlot slot, out TFKitItem item)
    {
        foreach (TFKitItem k in Kit)
        {
            if (k.Slot == slot)
            {
                item = k;
                return true;
            }
        }

        item = null!;
        return false;
    }

    /// <summary>The wielded item whose weapon kind is <paramref name="kind"/> (the one in hand, by the HUD's active weapon), if any.</summary>
    public bool TryKind(int kind, out TFKitItem item)
    {
        foreach (TFKitItem k in Kit)
        {
            if (k.WeaponKind == kind && k.Wielded)
            {
                item = k;
                return true;
            }
        }

        item = null!;
        return false;
    }

    /// <summary>The meter called <paramref name="name"/>, or <paramref name="none"/> when the HUD shows none.</summary>
    public float Meter(string name, float none = -1f)
    {
        foreach (TFMeter m in Meters)
        {
            if (m.Name == name)
                return m.Value;
        }

        return none;
    }

    /// <summary>The health the HUD shows of the teammate in <paramref name="slot"/>, if it shows any.</summary>
    public bool TryMate(int slot, out TFMateHealth mate)
    {
        foreach (TFMateHealth m in Mates)
        {
            if (m.Slot == slot)
            {
                mate = m;
                return true;
            }
        }

        mate = default;
        return false;
    }

    /// <summary>
    /// The bytes a recording keeps (format 10, review-p7f F1): the kit (slot, defindex, item class, kind, each attribute's
    /// class and value), the meters and the mates, all a decider reads, so a replay decides as the game did.
    /// </summary>
    public override byte[] Recorded()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((ushort)Kit.Length);
            foreach (TFKitItem k in Kit)
            {
                w.Write((byte)k.Slot);
                w.Write(k.Defindex);
                w.Write(k.ItemClass);
                w.Write(k.WeaponKind);
                w.Write((ushort)k.Attributes.Length);
                foreach (TFOwnKitAttribute a in k.Attributes)
                {
                    w.Write(a.Class);
                    w.Write(a.Value);
                }
            }

            w.Write((ushort)Meters.Length);
            foreach (TFMeter m in Meters)
            {
                w.Write(m.Name);
                w.Write(m.Value);
            }

            w.Write((ushort)Mates.Length);
            foreach (TFMateHealth m in Mates)
            {
                w.Write((byte)m.Slot);
                w.Write(m.Health);
                w.Write(m.MaxHealth);
                w.Write((byte)m.Why);
                w.Write(m.At.X);
                w.Write(m.At.Y);
                w.Write(m.At.Z);
            }
        }

        return ms.ToArray();
    }

    /// <summary>The HUD <paramref name="bytes"/> (<see cref="Recorded"/>'s) hold.</summary>
    public static TFHud FromRecorded(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes, writable: false));
        var kit = ImmutableArray.CreateBuilder<TFKitItem>();
        for (int i = r.ReadUInt16(); i > 0; i--)
        {
            var slot = (TFLoadoutSlot)r.ReadByte();
            int defindex = r.ReadInt32();
            string itemClass = r.ReadString();
            int kind = r.ReadInt32();
            var attributes = ImmutableArray.CreateBuilder<TFOwnKitAttribute>();
            for (int a = r.ReadUInt16(); a > 0; a--)
            {
                string cls = r.ReadString();
                attributes.Add(new TFOwnKitAttribute(cls, r.ReadSingle()));
            }

            kit.Add(new TFKitItem(slot, defindex, itemClass, kind, attributes.ToImmutable()));
        }

        var meters = ImmutableArray.CreateBuilder<TFMeter>();
        for (int i = r.ReadUInt16(); i > 0; i--)
        {
            string name = r.ReadString();
            meters.Add(new TFMeter(name, r.ReadSingle()));
        }

        var mates = ImmutableArray.CreateBuilder<TFMateHealth>();
        for (int i = r.ReadUInt16(); i > 0; i--)
        {
            int slot = r.ReadByte(), health = r.ReadInt32(), most = r.ReadInt32();
            var why = (TFMateSeen)r.ReadByte();
            float x = r.ReadSingle(), y = r.ReadSingle();
            mates.Add(new TFMateHealth(slot, health, most, why, new AiVector(x, y, r.ReadSingle())));
        }

        return new TFHud(kit.ToImmutable(), meters.ToImmutable(), mates.ToImmutable());
    }

    /// <summary>Whether this HUD shows what <paramref name="kit"/>, <paramref name="meters"/> and <paramref name="mates"/> say: then it is kept, not made again.</summary>
    public bool Shows(ReadOnlySpan<TFKitItem> kit, ReadOnlySpan<TFMeter> meters, ReadOnlySpan<TFMateHealth> mates)
    {
        if (kit.Length != Kit.Length || meters.Length != Meters.Length || mates.Length != Mates.Length)
            return false;
        for (int i = 0; i < kit.Length; i++)
        {
            if (!ReferenceEquals(kit[i], Kit[i]))
                return false;
        }

        return meters.SequenceEqual(Meters.AsSpan()) && mates.SequenceEqual(Mates.AsSpan());
    }
}
