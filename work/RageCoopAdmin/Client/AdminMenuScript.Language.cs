using System.Linq;
using LemonUI.Menus;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private void RefreshMenuLanguage()
    {
        if (_pool == null) return;
        bool time = _updatingTimeItems, wanted = _updatingWantedItem, settings = _updatingInterfaceSettings;
        _updatingTimeItems = _updatingWantedItem = _updatingInterfaceSettings = true;
        try
        {
            UiMenu.Refresh(_pool.OfType<NativeMenu>());
            foreach (NativeMenu menu in _pool.OfType<NativeMenu>()) FitMenuBanner(menu);
            _textInput?.RefreshLanguage();
        }
        finally
        {
            _updatingTimeItems = time;
            _updatingWantedItem = wanted;
            _updatingInterfaceSettings = settings;
        }
        RefreshMenuControlHints();
    }
}
