using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private readonly Queue<WeaponHash> _pendingWeapons = new Queue<WeaponHash>();
    private readonly List<WeaponEntry> _weaponCatalog = new List<WeaponEntry>();
    private readonly HashSet<WeaponHash> _infiniteWeaponHashes = new HashSet<WeaponHash>();
    private NativeMenu? _weaponsMenu;
    private NativeItem? _weaponCatalogState;
    private NativeCheckboxItem? _infiniteAmmoItem;
    private NativeCheckboxItem? _infiniteClipItem;
    private Ped? _infiniteAmmoPed;
    private long _lastAmmoUpdate;

    private void BuildWeaponsMenu()
    {
        if (_pool == null || _mainMenu == null)
        {
            return;
        }

        _weaponsMenu = CreateMenu("Coop Control", "Оружие");
        _mainMenu.AddSubMenu(_weaponsMenu);
        _pool.Add(_weaponsMenu);
        var refill = UiMenu.Item(() => UiText.Get("Пополнить все патроны"), () => UiText.Get("Заполнить боезапас оружия, которое у вас уже есть."));
        refill.Activated += (_, _) => RefillAllAmmo();
        _weaponsMenu.Add(refill);
        _infiniteAmmoItem = UiMenu.Checkbox(() => UiText.Get("Бесконечные патроны"), () => UiText.Get("Боезапас не заканчивается. Перезарядка остаётся."));
        _infiniteAmmoItem.CheckboxChanged += (_, _) => RefreshInfiniteAmmo();
        _weaponsMenu.Add(_infiniteAmmoItem);
        _infiniteClipItem = UiMenu.Checkbox(() => UiText.Get("Без перезарядки"), () => UiText.Get("Оружие стреляет без смены магазина."));
        _infiniteClipItem.CheckboxChanged += (_, _) => ApplyInfiniteClip();
        _weaponsMenu.Add(_infiniteClipItem);
        _weaponCatalogState = UiMenu.Item(() => UiText.Get("Список оружия загружается…"), () => UiText.Get("Покажем только оружие из вашей версии игры."), configure: item => { item.Enabled = false; });
        _weaponsMenu.Add(_weaponCatalogState);
        BuildWeaponPartsMenu();

        foreach (WeaponHash hash in Weapon.GetAllWeaponHashesForHumanPeds().Distinct())
        {
            if (hash != WeaponHash.Unarmed)
            {
                _pendingWeapons.Enqueue(hash);
            }
        }
    }

    private void TickWeapons(long now)
    {
        for (int i = 0; i < 8 && _pendingWeapons.Count > 0; i++)
        {
            WeaponHash hash = _pendingWeapons.Dequeue();
            if (!Function.Call<bool>(Hash.IS_WEAPON_VALID, (uint)hash))
            {
                continue;
            }

            string name = Weapon.GetHumanNameFromHash(hash);
            WeaponGroup group = Function.Call<WeaponGroup>(Hash.GET_WEAPONTYPE_GROUP, (uint)hash);
            string? category = WeaponCategory(group);
            // Internal vehicle weapons and script-only entries do not belong in the player's inventory.
            if (category == null || string.IsNullOrWhiteSpace(name) || name == "NULL")
            {
                continue;
            }

            _weaponCatalog.Add(new WeaponEntry(hash, name, category));
        }

        if (_pendingWeapons.Count == 0 && _weaponCatalogState != null)
        {
            FinishWeaponsCatalog();
        }

        if ((_infiniteAmmoItem?.Checked == true || _infiniteClipItem?.Checked == true) &&
            (now - _lastAmmoUpdate) / (double)Stopwatch.Frequency >= 0.5)
        {
            _lastAmmoUpdate = now;
            RefreshInfiniteAmmo();
            ApplyInfiniteClip();
        }
    }

    private void FinishWeaponsCatalog()
    {
        if (_weaponsMenu == null || _pool == null || _weaponCatalogState == null)
        {
            return;
        }

        _weaponsMenu.Remove(_weaponCatalogState);
        _weaponCatalogState = null;
        foreach (IGrouping<string, WeaponEntry> category in _weaponCatalog.GroupBy(entry => entry.Category)
                     .OrderBy(group => Array.IndexOf(WeaponCategories, group.Key)))
        {
            NativeMenu menu = CreateMenu("Coop Control", category.Key);
            _weaponsMenu.AddSubMenu(menu);
            _pool.Add(menu);
            foreach (WeaponEntry entry in category.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = UiMenu.List(() => entry.Name, () => UiText.Get("Выберите действие стрелками ←/→ и подтвердите выбор."), () => (new[] { "Взять", "В руки", "Патроны", "Обвесы", "Убрать" }).Select(UiText.Get).ToArray());
                item.Activated += (_, _) =>
                {
                    ActOnWeapon(entry, item.SelectedIndex, menu);
                    item.SelectedIndex = 0;
                };
                menu.Add(item);
            }
        }

        if (_weaponCatalog.Count == 0)
        {
            _weaponsMenu.Add(UiMenu.Item(() => UiText.Get("Оружие пока недоступно"), () => UiText.Get("Игра не предоставила список доступных моделей."), configure: item => { item.Enabled = false; }));
        }
    }

    private static readonly string[] WeaponCategories =
    {
        "Ближний бой", "Пистолеты", "Пистолеты-пулемёты", "Дробовики", "Автоматы", "Пулемёты",
        "Снайперские винтовки", "Тяжёлое оружие", "Метательное оружие", "Снаряжение"
    };

    private static string? WeaponCategory(WeaponGroup group) => group switch
    {
        WeaponGroup.Melee => "Ближний бой",
        WeaponGroup.Pistol or WeaponGroup.Stungun => "Пистолеты",
        WeaponGroup.SMG => "Пистолеты-пулемёты",
        WeaponGroup.AssaultRifle => "Автоматы",
        WeaponGroup.MG => "Пулемёты",
        WeaponGroup.Shotgun => "Дробовики",
        WeaponGroup.Sniper => "Снайперские винтовки",
        WeaponGroup.Heavy => "Тяжёлое оружие",
        WeaponGroup.Thrown => "Метательное оружие",
        WeaponGroup.PetrolCan or WeaponGroup.FireExtinguisher or WeaponGroup.Parachute => "Снаряжение",
        _ => null
    };

    private void ActOnWeapon(WeaponEntry entry, int action, NativeMenu category)
    {
        Ped? player = AvailablePlayerForWeapons();
        if (player == null)
        {
            return;
        }

        if (!player.Weapons.IsWeaponValid(entry.Hash))
        {
            Notification.PostTicker(UiText.Get("~o~Оружие:~s~ эта модель сейчас недоступна."), false);
            return;
        }

        if (action == 0)
        {
            Weapon weapon = player.Weapons.Give(entry.Hash, 0, true, true);
            if (!weapon.IsPresent)
            {
                Notification.PostTicker(UiText.Format("~o~Оружие:~s~ не удалось выдать «{0}».", UiText.Get(entry.Name)), false);
                return;
            }

            weapon.Ammo = Math.Max(weapon.Ammo, weapon.MaxAmmo);
            weapon.AmmoInClip = weapon.MaxAmmoInClip;
            RefreshInfiniteAmmo();
            ApplyInfiniteClip();
            Notification.PostTicker(UiText.Format("~g~Оружие:~s~ «{0}» у вас.", UiText.Get(entry.Name)), false);
            return;
        }

        if (!player.Weapons.HasWeapon(entry.Hash))
        {
            Notification.PostTicker(UiText.Get("~o~Оружие:~s~ сначала возьмите эту модель."), false);
            return;
        }

        Weapon current = player.Weapons[entry.Hash];
        if (action == 1)
        {
            if (!player.Weapons.Select(entry.Hash))
            {
                Notification.PostTicker(UiText.Get("~o~Оружие:~s~ сейчас нельзя взять эту модель в руки."), false);
            }
        }
        else if (action == 2)
        {
            current.Ammo = current.MaxAmmo;
            current.AmmoInClip = current.MaxAmmoInClip;
            Notification.PostTicker(UiText.Get("~g~Оружие:~s~ боезапас пополнен."), false);
        }
        else if (action == 3)
        {
            OpenWeaponParts(entry, player, category);
        }
        else if (action == 4)
        {
            if (_infiniteWeaponHashes.Remove(entry.Hash))
            {
                current.InfiniteAmmo = false;
            }

            player.Weapons.Remove(entry.Hash);
            if (!player.Weapons.HasWeapon(entry.Hash))
            {
                Notification.PostTicker(UiText.Format("~g~Оружие:~s~ «{0}» убрано.", UiText.Get(entry.Name)), false);
            }
        }
    }

    private static Ped? AvailablePlayerForWeapons()
    {
        Ped player = Game.Player.Character;
        if (!API.IsOnServer || player == null || !player.Exists() || player.IsDead)
        {
            Notification.PostTicker(UiText.Get("~o~Оружие:~s~ дождитесь возрождения и повторите."), false);
            return null;
        }

        return player;
    }

    private static void RefillAllAmmo()
    {
        Ped? player = AvailablePlayerForWeapons();
        if (player == null)
        {
            return;
        }

        int count = 0;
        foreach (Weapon weapon in player.Weapons)
        {
            if (weapon.Hash == WeaponHash.Unarmed || weapon.MaxAmmo <= 1)
            {
                continue;
            }

            weapon.Ammo = weapon.MaxAmmo;
            weapon.AmmoInClip = weapon.MaxAmmoInClip;
            count++;
        }

        Notification.PostTicker(UiText.Get(count > 0
            ? "~g~Оружие:~s~ боезапас пополнен."
            : "~o~Оружие:~s~ сначала возьмите оружие с патронами."), false);
    }

    private void RefreshInfiniteAmmo()
    {
        Ped? player = Game.Player.Character;
        if (_infiniteAmmoPed != null && (player == null || !player.Exists() || _infiniteAmmoPed.Handle != player.Handle))
        {
            RestoreWeaponFlags();
        }

        if (player == null || !player.Exists() || player.IsDead)
        {
            RestoreWeaponFlags();
            return;
        }

        _infiniteAmmoPed = player;
        if (_infiniteAmmoItem?.Checked != true)
        {
            foreach (WeaponHash hash in _infiniteWeaponHashes)
            {
                Function.Call(Hash.SET_PED_INFINITE_AMMO, player.Handle, false, (uint)hash);
            }

            _infiniteWeaponHashes.Clear();
            return;
        }

        foreach (Weapon weapon in player.Weapons)
        {
            if (weapon.Hash != WeaponHash.Unarmed && _infiniteWeaponHashes.Add(weapon.Hash))
            {
                weapon.InfiniteAmmo = true;
            }
        }
    }

    private void ApplyInfiniteClip()
    {
        Ped player = Game.Player.Character;
        if (player != null && player.Exists())
        {
            if (_infiniteAmmoPed != null && _infiniteAmmoPed.Handle != player.Handle)
            {
                RestoreWeaponFlags();
            }

            _infiniteAmmoPed = player;
            Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, player.Handle, _infiniteClipItem?.Checked == true);
        }
    }

    private void CleanupWeapons()
    {
        RestoreWeaponFlags();
        if (_infiniteAmmoItem?.Checked == true) _infiniteAmmoItem.Checked = false;
        if (_infiniteClipItem?.Checked == true) _infiniteClipItem.Checked = false;
    }

    private void RestoreWeaponFlags()
    {
        if (_infiniteAmmoPed != null && _infiniteAmmoPed.Exists())
        {
            foreach (WeaponHash hash in _infiniteWeaponHashes)
            {
                Function.Call(Hash.SET_PED_INFINITE_AMMO, _infiniteAmmoPed.Handle, false, (uint)hash);
            }

            Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, _infiniteAmmoPed.Handle, false);
        }

        _infiniteWeaponHashes.Clear();
        _infiniteAmmoPed = null;
    }

    private readonly struct WeaponEntry
    {
        internal WeaponEntry(WeaponHash hash, string name, string category)
        {
            Hash = hash;
            _name = name;
            Category = category;
        }

        internal WeaponHash Hash { get; }
        private readonly string _name;
        internal string Name => UiText.IsEnglish ? GameCatalogNames.Weapon(Hash, _name) : _name;
        internal string Category { get; }
    }
}
