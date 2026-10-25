using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.UI;
using LemonUI.Menus;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private readonly IReadOnlyList<AddonVehicleDefinition> _addonVehicles = AddonVehicleCatalog.LoadEmbedded();
    private NativeMenu? _addonVehiclesMenu;

    private void BuildAddonVehiclesMenu()
    {
        if (_vehicleMenu == null || _pool == null || _addonVehicles.Count == 0) { return; }
        _addonVehiclesMenu = CreateMenu("Coop Control", "Добавленные машины");
        _vehicleMenu.AddSubMenu(_addonVehiclesMenu);
        _pool.Add(_addonVehiclesMenu);
        foreach (AddonVehicleDefinition definition in _addonVehicles)
        {
            AddAddonVehicleItem(_addonVehiclesMenu, definition);
        }
    }

    private void AddAddonVehicleItem(NativeMenu menu, AddonVehicleDefinition definition)
    {
        int hash = AddonModelHash(definition);
        if (IsVehicleModelAvailable(hash))
        {
            AddVehicleItem(menu, new VehicleEntry(hash, definition.SpawnCode, definition.Name, -1));
            return;
        }

        // A missing DLC must remain visible with its real name, without requesting its model.
        // The selected card explains missing models without a long right label.
        var unavailable = UiMenu.VehicleItem(() => definition.Name, () => definition.CardDescription(installed: false),
            configure: item => { item.Enabled = false; });
        unavailable.Selected += (_, _) => ReleaseSpawnModelPreload();
        menu.Add(unavailable);
    }

    private AddonVehicleDefinition? FindAddonVehicle(int hash) =>
        _addonVehicles.FirstOrDefault(definition => AddonModelHash(definition) == hash);

    private static int AddonModelHash(AddonVehicleDefinition definition) =>
        unchecked((int)StringHash.AtStringHash(definition.SpawnCode.ToLowerInvariant()));

    private static bool IsVehicleModelAvailable(int hash)
    {
        Model model = new Model(hash);
        return model.IsInCdImage && model.IsVehicle;
    }

    private bool EnsureSpawnModelAvailable(int hash)
    {
        if (IsVehicleModelAvailable(hash)) { return true; }
        ReleaseSpawnModelPreload();
        Notification.PostTicker(FindAddonVehicle(hash) is AddonVehicleDefinition definition
            ? UiText.Format("~o~{0}:~s~ машина не установлена. Установите общий набор добавленных машин и перезапустите игру.", definition.Name)
            : UiText.Get("~o~Этой модели нет в вашей версии GTA."), false);
        return false;
    }

}
