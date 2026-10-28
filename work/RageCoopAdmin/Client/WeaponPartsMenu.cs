using System.Collections.Generic;
using GTA;
using LemonUI.Menus;

namespace RageCoopAdmin.Client;

// RageCoop already sends the components and tint of the weapon in hand to the
// other players.
public sealed partial class AdminMenuScript
{
    private NativeMenu? _weaponPartsMenu;
    private bool _refreshingWeaponParts;

    private void BuildWeaponPartsMenu()
    {
        if (_pool == null) return;
        _weaponPartsMenu = CreateMenu("OG Coop", "Обвесы");
        _pool.Add(_weaponPartsMenu);
    }

    private void OpenWeaponParts(WeaponEntry entry, Ped player, NativeMenu parent)
    {
        if (_weaponPartsMenu == null) return;
        Weapon weapon = player.Weapons[entry.Hash];
        _weaponPartsMenu.Clear();
        _weaponPartsMenu.Parent = parent;
        _weaponPartsMenu.Add(UiMenu.Item(() => entry.Name, () => UiText.Get("Обвесы и окраска оружия в руках видны другим игрокам."), configure: item => { item.Enabled = false; }));

        var parts = new List<(WeaponComponent Component, NativeCheckboxItem Item)>();
        foreach (WeaponComponent component in weapon.Components)
        {
            if (component.ComponentHash == WeaponComponentHash.Invalid) continue;
            string name = component.LocalizedName;
            if (string.IsNullOrWhiteSpace(name) || name == "NULL") name = component.DisplayName;
            if (string.IsNullOrWhiteSpace(name) || name == "NULL") continue;
            string title = name;
            NativeCheckboxItem item = UiMenu.Checkbox(() => title, () => UiText.Get("Поставить или снять. Детали одного места заменяют друг друга."), component.Active);
            item.CheckboxChanged += (_, _) =>
            {
                if (_refreshingWeaponParts) return;
                component.Active = item.Checked;
                RefreshWeaponParts(parts);
            };
            parts.Add((component, item));
            _weaponPartsMenu.Add(item);
        }

        if (weapon.TintCount > 1)
        {
            var colours = new string[weapon.TintCount];
            for (int index = 0; index < colours.Length; index++) colours[index] = (index + 1).ToString();
            NativeListItem<string> tint = UiMenu.List(() => UiText.Get("Окраска"), () => UiText.Get("Цвет оружия: стрелками ←/→."), () => colours,
                configure: item => { item.SelectedIndex = System.Math.Min(System.Math.Max((int)weapon.Tint, 0), colours.Length - 1); });
            tint.ItemChanged += (_, _) => weapon.Tint = (WeaponTint)tint.SelectedIndex;
            _weaponPartsMenu.Add(tint);
        }

        if (parts.Count == 0 && weapon.TintCount <= 1)
            _weaponPartsMenu.Add(UiMenu.Item(() => UiText.Get("У этого оружия нет обвесов"), () => UiText.Get("Выберите другую модель."), configure: item => { item.Enabled = false; }));

        parent.Visible = false;
        _weaponPartsMenu.SelectedIndex = _weaponPartsMenu.Items.Count > 1 ? 1 : 0;
        _weaponPartsMenu.Visible = true;
    }

    // A magazine, scope or finish replaces the one in the same place, so every
    // box shows what the weapon actually carries after a change.
    private void RefreshWeaponParts(List<(WeaponComponent Component, NativeCheckboxItem Item)> parts)
    {
        _refreshingWeaponParts = true;
        try
        {
            foreach ((WeaponComponent component, NativeCheckboxItem item) in parts) item.Checked = component.Active;
        }
        finally
        {
            _refreshingWeaponParts = false;
        }
    }
}
