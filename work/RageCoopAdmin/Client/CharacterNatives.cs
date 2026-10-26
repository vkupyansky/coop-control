using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OGCoop.Shared;
using GTA;
using GTA.Native;

namespace RageCoopAdmin.Client;

// GTA boundary for characters: putting a look on a ped, reading it back and
// the names GTA Online's shops give clothes. Game thread only.
internal static class CharacterNatives
{
    // Shop item records: the text label starts at field 9 (8-byte fields) and
    // holds up to 63 characters; the buffer is far larger than the record.
    private const int ShopRecordBytes = 512;
    private const int ShopLabelOffset = 9 * 8;
    private static readonly Regex Label = new Regex("^[A-Za-z0-9_]{1,63}$", RegexOptions.CultureInvariant);

    // Overlays that take hair colours and those that take make-up colours.
    private static readonly int[] HairColoured = { 1, CharacterCodec.Eyebrows, 10 };
    private static readonly int[] MakeupColoured = { 5, 8 };

    internal static void Apply(int ped, CharacterLook look)
    {
        if (!look.IsOnline) return;
        ApplyHead(ped, look.Model, look.Head);
        ApplyParts(ped, look);
        ApplyTattoos(ped, look.Tattoos);
    }

    // Story scripts clear a ped's decorations and put the story hero's tattoos
    // on (the respawn does): ours are put on again with the clothes.
    internal static void ApplyTattoos(int ped, IEnumerable<CharacterTattoo> tattoos)
    {
        Function.Call((Hash)0x0E5173C163976E38UL, ped); // CLEAR_PED_DECORATIONS
        foreach (CharacterTattoo tattoo in tattoos)
            Function.Call((Hash)0x5F5D1665E352A839UL, ped, tattoo.Collection, tattoo.Name); // ADD_PED_DECORATION_FROM_HASHES
    }

    // Clothes and accessories only: also puts a story hero's own clothes back.
    internal static void ApplyParts(int ped, CharacterLook look)
    {
        foreach (CharacterPart part in look.Clothes) SetComponent(ped, part.Slot, part.Drawable, part.Texture);
        foreach (int slot in CharacterCodec.PropSlots)
        {
            CharacterPart? prop = look.Props.FirstOrDefault(part => part.Slot == slot);
            SetProp(ped, slot, prop?.Drawable ?? -1, prop?.Texture ?? 0);
        }
    }

    // An Online body has no face until its parents are set; the blend comes
    // first, then the features, overlays, hair and eye colours on top of it.
    internal static void ApplyHead(int ped, int model, CharacterHead? head)
    {
        head ??= CharacterHead.Default(model);
        Function.Call(Hash.SET_PED_HEAD_BLEND_DATA, ped, head.Mother, head.Father, 0, head.Mother, head.Father, 0,
            head.Resemblance / 100f, head.SkinTone / 100f, 0f, false);
        for (int index = 0; index < CharacterCodec.FeatureCount; index++)
            Function.Call((Hash)0x71A5C1DBA060049EUL, ped, index, head.Feature(index) / (float)CharacterCodec.FeatureLimit); // SET_PED_MICRO_MORPH
        for (int slot = 0; slot < CharacterCodec.OverlayCount; slot++)
        {
            CharacterOverlay overlay = head.Overlay(slot);
            Function.Call((Hash)0x48F44967FA05CC1EUL, ped, slot, overlay.Style < 0 ? 255 : overlay.Style, overlay.Opacity / 100f); // SET_PED_HEAD_OVERLAY
            int tint = HairColoured.Contains(slot) ? 1 : MakeupColoured.Contains(slot) ? 2 : 0;
            if (tint != 0) Function.Call((Hash)0x497BF74A7B9CB952UL, ped, slot, tint, overlay.Color, overlay.Color); // SET_PED_HEAD_OVERLAY_TINT
        }
        Function.Call((Hash)0x4CFFC65454C93A49UL, ped, head.HairColor, head.HairHighlight); // SET_PED_HAIR_TINT
        Function.Call((Hash)0x50B56988B170AFDFUL, ped, head.EyeColor); // SET_HEAD_BLEND_EYE_COLOR
    }

    internal static bool TakesColour(int overlay) => HairColoured.Contains(overlay) || MakeupColoured.Contains(overlay);

    internal static int OverlayStyles(int overlay) => Function.Call<int>((Hash)0xCF1CE768BB43480EUL, overlay); // GET_PED_HEAD_OVERLAY_NUM

    internal static int HairTints() => Function.Call<int>((Hash)0xE5C0CF872C2AD150UL); // GET_NUM_PED_HAIR_TINTS

    internal static int MakeupTints() => Function.Call<int>((Hash)0xD1F7CA1535D22818UL); // GET_NUM_PED_MAKEUP_TINTS

    internal static int OverlayTints(int overlay) => HairColoured.Contains(overlay) ? HairTints() : MakeupTints();

    // A new Online body starts in GTA's default clothes and no accessories.
    internal static void Reset(int ped, int model, CharacterHead? head, IEnumerable<CharacterTattoo> tattoos)
    {
        Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped);
        Function.Call(Hash.CLEAR_ALL_PED_PROPS, ped, 1);
        ApplyHead(ped, model, head);
        ApplyTattoos(ped, tattoos);
    }

    internal static void Randomize(int ped, int model, CharacterHead? head)
    {
        Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped, 0);
        Function.Call(Hash.CLEAR_ALL_PED_PROPS, ped, 1);
        Function.Call(Hash.SET_PED_RANDOM_PROPS, ped);
        // The face comes from the parents; the head slot stays GTA's plain head.
        SetComponent(ped, 0, 0, 0);
        ApplyHead(ped, model, head);
    }

    internal static CharacterLook Read(int ped, int model)
    {
        var look = new CharacterLook { Model = model };
        for (int slot = 0; slot < CharacterCodec.ComponentCount; slot++)
            look.Clothes.Add(new CharacterPart { Slot = slot, Drawable = Drawable(ped, slot, false), Texture = Texture(ped, slot, false) });
        foreach (int slot in CharacterCodec.PropSlots)
        {
            int drawable = Drawable(ped, slot, true);
            if (drawable >= 0) look.Props.Add(new CharacterPart { Slot = slot, Drawable = drawable, Texture = Math.Max(0, Texture(ped, slot, true)) });
        }
        return look;
    }

    // GTA Online's shops put an item on together with the parts it needs (a
    // jacket forces its arms and undershirt); without them the body shows
    // holes. The shop data lists them as a shop item or a plain number for a
    // clothing slot. Returns how many parts were put on.
    internal static int WearForcedParts(int ped, int slot, int drawable, int texture)
    {
        int item = Function.Call<int>((Hash)0x0368B3A838070348UL, ped, slot, drawable, texture); // GET_HASH_NAME_FOR_COMPONENT
        if (item == 0) return 0;
        int count = Math.Min(16, Function.Call<int>((Hash)0xC6B9DB42C04DD8C3UL, item)); // GET_SHOP_PED_APPAREL_FORCED_COMPONENT_COUNT
        int worn = 0;
        for (int index = 0; index < count; index++)
        {
            var name = new OutputArgument();
            var value = new OutputArgument();
            var type = new OutputArgument();
            Function.Call((Hash)0x6C93ED8C2F74859BUL, item, index, name, value, type); // GET_FORCED_COMPONENT
            int forced = name.GetResult<int>(), part = type.GetResult<int>();
            if (part < 0 || part >= CharacterCodec.ComponentCount || part == slot) continue;
            int forcedDrawable = value.GetResult<int>(), forcedTexture = 0;
            if (forced != 0 && forced != ZeroHash && !ShopRecord(forced, false, out forcedDrawable, out forcedTexture)) continue;
            if (forcedDrawable < 0 || forcedDrawable > CharacterCodec.MaximumDrawable) continue;
            SetComponent(ped, part, forcedDrawable, Math.Max(0, forcedTexture));
            worn++;
        }
        return worn;
    }

    private const int ZeroHash = 0x6E3C5C6B; // joaat("0"): "no item"

    // A shop item's clothing number and colour (record fields 3 and 4).
    private static bool ShopRecord(int item, bool prop, out int drawable, out int texture)
    {
        IntPtr record = Marshal.AllocHGlobal(ShopRecordBytes);
        try
        {
            Marshal.Copy(new byte[ShopRecordBytes], 0, record, ShopRecordBytes);
            if (prop) Function.Call((Hash)0x5D5CAFF661DDF6FCUL, item, record); // GET_SHOP_PED_PROP
            else Function.Call((Hash)0x74C0E2A57EC66760UL, item, record); // GET_SHOP_PED_COMPONENT
            drawable = Marshal.ReadInt32(record, 3 * 8);
            texture = Marshal.ReadInt32(record, 4 * 8);
            return true;
        }
        finally { Marshal.FreeHGlobal(record); }
    }

    // Puts back every clothing slot (but `keep`) that differs from the look;
    // returns how many were changed.
    internal static int RestoreClothes(int ped, CharacterLook look, int keep)
    {
        int changed = 0;
        foreach (CharacterPart part in look.Clothes)
        {
            if (part.Slot == keep) continue;
            if (Drawable(ped, part.Slot, false) == part.Drawable && Texture(ped, part.Slot, false) == part.Texture) continue;
            SetComponent(ped, part.Slot, part.Drawable, part.Texture);
            changed++;
        }
        return changed;
    }

    internal static void SetComponent(int ped, int slot, int drawable, int texture) =>
        Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, slot, drawable, texture, 0);

    internal static void SetProp(int ped, int slot, int drawable, int texture)
    {
        if (drawable < 0) Function.Call(Hash.CLEAR_PED_PROP, ped, slot, 1);
        else Function.Call(Hash.SET_PED_PROP_INDEX, ped, slot, drawable, texture, false, 1);
    }

    internal static void Set(int ped, int slot, bool prop, int drawable, int texture)
    {
        if (prop) SetProp(ped, slot, drawable, texture);
        else SetComponent(ped, slot, drawable, texture);
    }

    internal static int Drawables(int ped, int slot, bool prop) => prop
        ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_DRAWABLE_VARIATIONS, ped, slot)
        : Function.Call<int>(Hash.GET_NUMBER_OF_PED_DRAWABLE_VARIATIONS, ped, slot);

    internal static int Textures(int ped, int slot, bool prop, int drawable) => drawable < 0 ? 0 : prop
        ? Function.Call<int>(Hash.GET_NUMBER_OF_PED_PROP_TEXTURE_VARIATIONS, ped, slot, drawable)
        : Function.Call<int>(Hash.GET_NUMBER_OF_PED_TEXTURE_VARIATIONS, ped, slot, drawable);

    internal static int Drawable(int ped, int slot, bool prop) => prop
        ? Function.Call<int>(Hash.GET_PED_PROP_INDEX, ped, slot, 1)
        : Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped, slot);

    internal static int Texture(int ped, int slot, bool prop) => prop
        ? Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, ped, slot)
        : Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped, slot);

    // The item's name in the game's language, as GTA Online's shops show it;
    // null for items the shops do not sell.
    internal static string? ShopName(int ped, int slot, bool prop, int drawable, int texture)
    {
        if (drawable < 0) return null;
        int hash = prop
            ? Function.Call<int>((Hash)0x5D6160275CAEC8DDUL, ped, slot, drawable, texture) // GET_HASH_NAME_FOR_PROP
            : Function.Call<int>((Hash)0x0368B3A838070348UL, ped, slot, drawable, texture); // GET_HASH_NAME_FOR_COMPONENT
        if (hash == 0) return null;
        IntPtr record = Marshal.AllocHGlobal(ShopRecordBytes);
        try
        {
            Marshal.Copy(new byte[ShopRecordBytes], 0, record, ShopRecordBytes);
            if (prop) Function.Call((Hash)0x5D5CAFF661DDF6FCUL, hash, record); // GET_SHOP_PED_PROP
            else Function.Call((Hash)0x74C0E2A57EC66760UL, hash, record); // GET_SHOP_PED_COMPONENT
            string label = Marshal.PtrToStringAnsi(IntPtr.Add(record, ShopLabelOffset)) ?? string.Empty;
            if (!Label.IsMatch(label) || !Function.Call<bool>(Hash.DOES_TEXT_LABEL_EXIST, label)) return null;
            string name = Game.GetLocalizedString(label);
            return string.IsNullOrWhiteSpace(name) || name == "NULL" ? null : name.Trim();
        }
        finally { Marshal.FreeHGlobal(record); }
    }
}

// The weapons, health and armour a ped carries: SET_PLAYER_MODEL makes a new
// ped and drops them, so they move to the new body.
internal sealed class CarriedGear
{
    private readonly List<(WeaponHash Hash, int Ammo, WeaponTint Tint, WeaponComponentHash[] Parts)> _weapons =
        new List<(WeaponHash, int, WeaponTint, WeaponComponentHash[])>();
    private WeaponHash _current = WeaponHash.Unarmed;
    private int _health;
    private int _armor;

    internal static CarriedGear Take(Ped ped)
    {
        var gear = new CarriedGear { _health = ped.Health, _armor = ped.Armor, _current = ped.Weapons.Current.Hash };
        foreach (WeaponHash hash in Weapon.GetAllWeaponHashesForHumanPeds())
        {
            if (hash == WeaponHash.Unarmed || !ped.Weapons.HasWeapon(hash)) continue;
            Weapon weapon = ped.Weapons[hash];
            var parts = new List<WeaponComponentHash>();
            foreach (WeaponComponent part in weapon.Components)
                if (part.ComponentHash != WeaponComponentHash.Invalid && part.Active) parts.Add(part.ComponentHash);
            gear._weapons.Add((hash, weapon.Ammo, weapon.Tint, parts.ToArray()));
        }
        return gear;
    }

    internal void GiveTo(Ped ped)
    {
        foreach ((WeaponHash hash, int ammo, WeaponTint tint, WeaponComponentHash[] parts) in _weapons)
        {
            Weapon weapon = ped.Weapons.Give(hash, 0, false, true);
            weapon.Ammo = ammo;
            weapon.Tint = tint;
            foreach (WeaponComponentHash part in parts) weapon.Components[part].Active = true;
        }
        if (_current != WeaponHash.Unarmed && ped.Weapons.HasWeapon(_current)) ped.Weapons.Select(_current, true);
        if (_health > 0) ped.Health = Math.Min(_health, ped.MaxHealth);
        ped.Armor = _armor;
    }
}
