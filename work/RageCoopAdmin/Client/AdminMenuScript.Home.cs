using System;
using System.Linq;
using GTA;
using GTA.UI;
using LemonUI.Menus;
using OGCoop.Shared;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// своё жильё в меню Телепорты, Моё жильё: апартаменты, дома, особняки и пентхаус казино
// сервер хранит выбор по имени, после входа на сервер игрок появляется дома, машина из дома ждёт у входа
public sealed partial class AdminMenuScript
{
    // сколько после входа ждать карту Online, возрождение и редактор персонажа
    private const long HomeArrivalLimit = 120_000;
    private const long HomeSaveRetry = 5_000;
    private NativeMenu? _homeMenu;
    private NativeItem? _homeGo;
    private NativeListItem<string>? _homeChoice;
    // жильё с сервера: null до ответа, пустая строка без жилья
    private string? _home;
    private string? _homeSaving;
    private long _homeSentAt;
    private bool _homeFirstState;
    // с какого времени ждём появления дома после входа, -1 не ждём
    private long _homeArrivalSince = -1;

    private void RegisterHomeEvents()
    {
        _eventSubscriptions.Register(HomeProtocol.State, e => QueueHomeEvent(e, state: true));
        _eventSubscriptions.Register(HomeProtocol.Saved, e => QueueHomeEvent(e, state: false));
    }

    private void InitializeHome()
    {
        _home = null;
        _homeSaving = null;
        _homeFirstState = true;
        _homeArrivalSince = -1;
        API.SendCustomEvent(HomeProtocol.Request, Array.Empty<object>());
    }

    private void QueueHomeEvent(CustomEventReceivedArgs e, bool state)
    {
        if (_stopping || e.Args == null) return;
        object[] args = (object[])e.Args.Clone();
        API.QueueAction(() =>
        {
            if (!_active || _stopping) return;
            if (state) ApplyHomeState(args);
            else if (args is { Length: 1 } && args[0] is bool saved) FinishHomeSave(saved);
        });
    }

    private void ApplyHomeState(object[] args)
    {
        if (args is not { Length: 1 } || args[0] is not string key || !HomeCodec.IsKey(key)) return;
        _home = key;
        if (_homeFirstState && HomeInterior(key) != null) _homeArrivalSince = NowMilliseconds();
        _homeFirstState = false;
        RefreshHomeMenu();
    }

    private void FinishHomeSave(bool saved)
    {
        string? key = _homeSaving;
        _homeSaving = null;
        if (key == null) return;
        if (saved)
        {
            _home = key;
            Notification.PostTicker(HomeInterior(key) is OnlineInterior home
                ? UiText.Format("~g~Жильё:~s~ теперь ваш дом «{0}».", InteriorTitle(home))
                : UiText.Get("~g~Жильё:~s~ вы без жилья."), false);
        }
        else Notification.PostTicker(UiText.Get("~o~Жильё:~s~ сервер не сохранил выбор. Повторите."), false);
        RefreshHomeMenu();
    }

    private void BuildHomeMenu(NativeMenu teleports)
    {
        if (_pool == null) return;
        _homeMenu = CreateMenu("OG Coop", "Моё жильё");
        UiMenu.Description(_homeMenu, () => UiText.Get("Квартира, дом или особняк. После входа на сервер вы появляетесь дома, машина из дома ждёт у входа."));
        teleports.AddSubMenu(_homeMenu);
        _pool.Add(_homeMenu);
        _homeMenu.Shown += (_, _) => RefreshHomeMenu();

        _homeGo = UiMenu.Item(() => UiText.Get("Домой"), () => UiText.Get("Перенестись в своё жильё."));
        _homeGo.Activated += (_, _) => GoHome();
        _homeMenu.Add(_homeGo);
        _homeChoice = UiMenu.List(() => UiText.Get("Жильё"), () => UiText.Get("Выберите ←/→ и нажмите Enter, чтобы сделать своим."),
            () => new[] { UiText.Get("Нет") }.Concat(OnlineInteriors.Homes.Select(InteriorTitle)).ToArray());
        _homeChoice.Activated += (_, _) =>
            SaveHome(_homeChoice.SelectedIndex <= 0 ? string.Empty : OnlineInteriors.Homes[_homeChoice.SelectedIndex - 1].Key);
        _homeMenu.Add(_homeChoice);
    }

    private void RefreshHomeMenu()
    {
        if (_homeMenu == null || _homeGo == null || _homeChoice == null) return;
        _homeGo.Enabled = HomeInterior(_home) != null;
        _homeChoice.Enabled = _home != null;
        OnlineInterior? chosen = HomeInterior(_homeSaving ?? _home);
        _homeChoice.SelectedIndex = chosen == null ? 0 : Array.IndexOf(OnlineInteriors.Homes, chosen) + 1;
    }

    private void SaveHome(string key)
    {
        if (_home == null)
        {
            Notification.PostTicker(UiText.Get("~o~Жильё:~s~ ещё загружается. Попробуйте через секунду."), false);
            return;
        }

        if (key == _home && _homeSaving == null) return;
        long now = NowMilliseconds();
        if (_homeSaving != null && now - _homeSentAt < HomeSaveRetry) return;
        _homeSaving = key;
        _homeSentAt = now;
        API.SendCustomEvent(HomeProtocol.Save, key);
    }

    private void GoHome()
    {
        if (HomeInterior(_home) is not OnlineInterior home)
        {
            Notification.PostTicker(UiText.Get("~o~Жильё:~s~ сначала выберите его ниже."), false);
            return;
        }

        EnterInterior(home);
    }

    private static OnlineInterior? HomeInterior(string? key) =>
        string.IsNullOrEmpty(key) ? null : OnlineInteriors.Homes.FirstOrDefault(interior => interior.Key == key);

    // после входа на сервер: домой, когда игрок на ногах, карта Online загружена и ничего другого не идёт
    private void TickHome(long now)
    {
        if (_homeArrivalSince < 0) return;
        OnlineInterior? home = HomeInterior(_home);
        if (home == null || now - _homeArrivalSince > HomeArrivalLimit)
        {
            _homeArrivalSince = -1;
            return;
        }

        Ped player = Game.Player.Character;
        if (player == null || !player.Exists() || player.IsDead || player.IsInVehicle() || _creator != null ||
            _pendingTeleport != null || _pendingRecall != null || _mapLoad != null || _pool?.AreAnyVisible == true) return;
        _homeArrivalSince = -1;
        if (_inside != home) EnterInterior(home);
    }

    // из дома машина не появляется в комнате: игрок выходит к двери, машина приходит туда
    private bool SpawnOutsideHome(int modelHash, string? encodedAppearance)
    {
        if (_inside is not OnlineInterior room || Array.IndexOf(OnlineInteriors.Homes, room) < 0 || Game.Player.Character.IsInVehicle()) return false;
        if (ExitInterior(room, () => RequestVehicleSpawn(modelHash, encodedAppearance)))
            Notification.PostTicker(UiText.Get("~o~Машина:~s~ выходим к двери, она появится там."), false);
        return true;
    }

    private void ResetHome()
    {
        _homeSaving = null;
        _homeArrivalSince = -1;
    }
}
