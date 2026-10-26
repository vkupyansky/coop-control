using System;
using System.Collections.Generic;
using OGCoop.Shared;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private readonly List<NativeItem> _worldActions = new List<NativeItem>();
    private WorldStateController? _worldState;
    private GhostTrafficController? _ghostTraffic;
    private NativeCheckboxItem? _freezeTimeItem, _freezeWeatherItem, _ghostTrafficItem;
    private bool _refreshingWorldControls, _worldConnected;
    private volatile int _worldGeneration;
    private int _worldTimeHour, _worldTimeMinute;

    private void RegisterWorldEvents()
    {
        _eventSubscriptions.Register(WorldStateProtocol.State, e => QueueWorldEvent(e, false));
        _eventSubscriptions.Register(GhostTrafficProtocol.State, e => QueueWorldEvent(e, true));
    }

    private void QueueWorldEvent(CustomEventReceivedArgs e, bool traffic)
    {
        if (_stopping || e.Args == null || e.Args.Length != (traffic ? 4 : 13) ||
            (traffic && (e.Args[3] is not byte[] payload || payload.Length > GhostTrafficProtocol.MaximumEntries * 12))) return;
        int generation = _worldGeneration;
        object[] args = (object[])e.Args.Clone();
        // Byte payloads are owned by the network event until this callback ends.
        for (int i = 0; i < args.Length; i++)
            if (args[i] is byte[] bytes) args[i] = (byte[])bytes.Clone();
        API.QueueAction(() =>
        {
            if (!_active || _stopping || !_worldConnected || generation != _worldGeneration) return;
            if (traffic) _ghostTraffic?.ApplyRegistry(args);
            else _worldState?.Receive(args);
        });
    }

    private void InitializeWorldControls()
    {
        _ghostTraffic = new GhostTrafficController(args => API.SendCustomEvent(GhostTrafficProtocol.Report, args), message => Logger.Info(message));
        _worldState = new WorldStateController((hash, args) => API.SendCustomEvent(hash, args));
        _worldState.Changed += OnWorldChanged;
        _worldState.PendingExpired += OnWorldRequestExpired;
        RefreshWorldControls();
    }

    private void BuildWorldControls(NativeMenu transport, NativeMenu weather, NativeMenu time)
    {
        _freezeWeatherItem = AddSharedToggle(weather, "Зафиксировать погоду",
            "Удерживать выбранную погоду у всех игроков. Выключите, чтобы погода снова менялась.", WorldStateChange.FreezeWeather);
        _freezeTimeItem = AddSharedToggle(time, "Зафиксировать время",
            "Остановить часы у всех игроков. Выключите, чтобы время снова шло.", WorldStateChange.FreezeTime);
        _ghostTrafficItem = AddSharedToggle(transport, "Проезд сквозь трафик",
            "Для всех игроков за рулём: проезжать сквозь машины NPC. Столкновения с игроками, пешеходами и окружением сохраняются.", WorldStateChange.GhostTraffic);
    }

    private NativeCheckboxItem AddSharedToggle(NativeMenu menu, string title, string description, WorldStateChange change)
    {
        var item = UiMenu.Checkbox(() => UiText.Get(title), () => UiText.Get(description), configure: item => { item.Enabled = false; });
        item.CheckboxChanged += (_, _) =>
        {
            if (_refreshingWorldControls) return;
            bool desired = item.Checked;
            // LemonUI flips before the event. Restore confirmed state while the
            // server decides, rather than displaying unconfirmed success.
            RefreshWorldControls();
            WorldStateSnapshot? state = _worldState?.Snapshot;
            int value1 = change == WorldStateChange.FreezeTime ? GTA.Chrono.GameClock.Hour :
                change == WorldStateChange.FreezeWeather ? state?.WeatherIndex ?? -1 : 0;
            int value2 = change == WorldStateChange.FreezeTime ? GTA.Chrono.GameClock.Minute : 0;
            RequestWorldChange(change, value1, value2, desired);
        };
        menu.Add(item);
        _worldActions.Add(item);
        return item;
    }

    private void TickWorldControls()
    {
        bool connected = API.IsOnServer;
        if (connected && !_worldConnected && _worldState != null)
        {
            _worldConnected = true;
            _worldGeneration++;
            try
            {
                if (!GhostTrafficRuntime.IsAvailable)
                    throw new InvalidOperationException("Unsupported RageCoop traffic lookup contract.");
                _worldState.Start();
            }
            catch (Exception ex)
            {
                _worldState.Stop();
                Logger.Warning("OG Coop: shared world controls unavailable: " + ex.GetType().Name);
                Notification.PostTicker(UiText.Get("~o~Мир:~s~ общие настройки недоступны для установленной версии клиента."), false);
            }
            RefreshWorldControls();
        }
        else if (!connected && _worldConnected)
        {
            _worldConnected = false;
            _worldGeneration++;
            _worldState?.Stop();
            _ghostTraffic?.Stop();
            TrafficHandover.Reset();
            PassengerPopulation.Reset();
            _timeSelection.Cancel();
            RefreshWorldControls();
        }
        _worldState?.Tick();
        if (_worldConnected)
        {
            PassengerPopulation.Tick();
            TrafficHandover.Tick(System.Diagnostics.Stopwatch.GetTimestamp());
        }
        // Ordinary off/disconnect drains intersecting pairs without moving cars.
        _ghostTraffic?.Tick();
    }

    private int RequestWorldChange(WorldStateChange change, int value1, int value2, bool enabled)
    {
        int request = _worldState?.Request(change, value1, value2, enabled) ?? 0;
        RefreshWorldControls();
        if (request == 0)
            Notification.PostTicker(UiText.Get("~o~Мир:~s~ дождитесь подтверждения состояния сервера и повторите выбор."), false);
        return request;
    }

    private void OnWorldChanged(WorldStateSnapshot state, int request, bool accepted, WorldStateChange change)
    {
        _ghostTraffic?.Configure(state.GhostTrafficEnabled, state.Epoch, _worldState!.SessionToken);
        if (request > 0 && change == WorldStateChange.SetTime)
        {
            if (accepted) _timeSelection.Confirm(_worldTimeHour, _worldTimeMinute);
            else _timeSelection.Cancel();
            RefreshTimeSelection();
        }
        RefreshWorldControls();
        if (request <= 0) return;
        if (!accepted)
        {
            Notification.PostTicker(UiText.Get("~o~Мир:~s~ изменение не применено. Дождитесь загрузки меню у всех игроков и повторите выбор."), false);
            return;
        }
        string message = change switch
        {
            WorldStateChange.SetTime => UiText.Get("Время: ") + FormatTime(_worldTimeHour, _worldTimeMinute),
            WorldStateChange.SetWeather => UiText.Get("Погода: ") + UiText.Get(WorldOptions.WeatherNames[state.WeatherIndex]),
            WorldStateChange.FreezeTime => UiText.Get(state.TimeFrozen ? "Время зафиксировано для всех." : "Часы снова идут у всех."),
            WorldStateChange.FreezeWeather => UiText.Get(state.WeatherFrozen ? "Погода зафиксирована для всех." : "Погода снова меняется у всех."),
            WorldStateChange.GhostTraffic => UiText.Get(state.GhostTrafficEnabled ? "Проезд сквозь трафик включён для всех." : "Проезд сквозь трафик выключен для всех."),
            _ => string.Empty
        };
        if (message.Length != 0) Notification.PostTicker(UiText.Get("~o~Мир:~s~ ") + message, false);
    }

    private void OnWorldRequestExpired()
    {
        _timeSelection.Cancel();
        RefreshTimeSelection();
        RefreshWorldControls();
        Notification.PostTicker(UiText.Get("~o~Мир:~s~ сервер не подтвердил изменение. Повторите выбор."), false);
    }

    private void RefreshWorldControls()
    {
        WorldStateSnapshot? state = _worldState?.Snapshot;
        bool ready = _worldConnected && state != null && _worldState?.PendingRequestId == 0;
        _refreshingWorldControls = true;
        try
        {
            if (_freezeTimeItem != null) _freezeTimeItem.Checked = state?.TimeFrozen == true;
            if (_freezeWeatherItem != null) _freezeWeatherItem.Checked = state?.WeatherFrozen == true;
            if (_ghostTrafficItem != null) _ghostTrafficItem.Checked = state?.GhostTrafficEnabled == true;
            foreach (NativeItem item in _worldActions) item.Enabled = ready;
            for (int i = 0; i < _weatherItems.Count; i++)
                UiMenu.AltTitle(_weatherItems[i], () => state?.WeatherIndex == i ? UiText.Get("Сейчас") : string.Empty);
        }
        finally { _refreshingWorldControls = false; }
    }

    private void CleanupWorldControls()
    {
        _worldGeneration++;
        _worldConnected = false;
        if (_worldState != null)
        {
            _worldState.Changed -= OnWorldChanged;
            _worldState.PendingExpired -= OnWorldRequestExpired;
            try { _worldState.Stop(); }
            catch (Exception ex) { Logger.Warning("OG Coop: world cleanup failed: " + ex.GetType().Name); }
            _worldState = null;
        }
        try { _ghostTraffic?.Clear(); }
        catch (Exception ex) { Logger.Warning("OG Coop: traffic cleanup failed: " + ex.GetType().Name); }
        _ghostTraffic = null;
        TrafficHandover.Reset();
        PassengerPopulation.Reset();
        _worldActions.Clear();
    }
}
