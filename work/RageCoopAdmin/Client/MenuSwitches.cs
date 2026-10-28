using System.Collections.Generic;
using System.Linq;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// The Character and Weapons switches (invincibility, no wanted level, abilities,
// ammunition) are remembered on this computer with the other settings and
// turned on again at the next join. Time, weather and traffic pass-through
// are shared world settings and are not part of this.
public sealed partial class AdminMenuScript
{
    private Dictionary<string, bool>? _unsavedSwitches;
    private bool _restoringSwitches;

    private (string Key, NativeCheckboxItem? Item)[] MenuSwitches() => new[]
    {
        ("NoWanted", _noWantedItem), ("Invincible", _invincibleItem), ("Seatbelt", _seatbeltItem), ("SuperJump", _superJumpItem),
        ("Stamina", _staminaItem), ("Underwater", _underwaterItem), ("InfiniteAmmo", _infiniteAmmoItem), ("NoReload", _infiniteClipItem),
    };

    private void BindMenuSwitches()
    {
        foreach ((string key, NativeCheckboxItem? item) in MenuSwitches())
        {
            if (item == null) continue;
            NativeCheckboxItem box = item;
            box.CheckboxChanged += (_, _) => RememberSwitch(key, box.Checked);
        }
    }

    // Leaving the server turns the switches off without forgetting the choice.
    private void RememberSwitch(string key, bool on)
    {
        if (_restoringSwitches || _stopping || !API.IsOnServer) return;
        PersistInterfaceSettings(value => SetSwitch(value, key, on));
    }

    private void RestoreMenuSwitches()
    {
        _restoringSwitches = true;
        try
        {
            foreach ((string key, NativeCheckboxItem? item) in MenuSwitches())
                if (item != null && !item.Checked && _interfaceSettings.Switches.Contains(key)) item.Checked = true;
        }
        finally { _restoringSwitches = false; }
    }

    private static void SetSwitch(InterfaceSettings settings, string key, bool on)
    {
        settings.Switches.RemoveAll(name => name == key);
        if (on) settings.Switches.Add(key);
    }

    // Switches changed since the last successful write, carried into the
    // next write like the other unsaved settings.
    private void NoteSwitchChanges(IEnumerable<string> before)
    {
        var previous = new HashSet<string>(before);
        var now = new HashSet<string>(_interfaceSettings.Switches);
        foreach (string key in previous.Union(now).Where(key => previous.Contains(key) != now.Contains(key)))
            (_unsavedSwitches ??= new Dictionary<string, bool>())[key] = now.Contains(key);
    }

    private void ApplyUnsavedSwitches(InterfaceSettings value)
    {
        if (_unsavedSwitches == null) return;
        foreach (KeyValuePair<string, bool> change in _unsavedSwitches) SetSwitch(value, change.Key, change.Value);
    }
}
