using System;
using OGCoop.Shared;
using GTA.UI;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private GarageClient? _garage;

    private void RegisterGarageEvents()
    {
        _eventSubscriptions.Register(GarageProtocol.State, e => QueueGarageEvent(e, state: true));
        _eventSubscriptions.Register(GarageProtocol.Result, e => QueueGarageEvent(e, state: false));
    }

    private void InitializeGarage()
    {
        _garage = new GarageClient((hash, args) => API.SendCustomEvent(hash, args));
        _garage.Changed += OnGarageChanged;
        _garage.Request();
        _answers.Await("garage", () => _garage?.Request(), () => _garage?.Current != null, NowMilliseconds());
    }

    private void QueueGarageEvent(CustomEventReceivedArgs e, bool state)
    {
        if (_stopping || e.Args == null) return;
        object[] args = (object[])e.Args.Clone();
        API.QueueAction(() =>
        {
            if (!_active || _stopping || _garage == null) return;
            if (!state)
            {
                _garage.ApplyResult(args);
                return;
            }
            _garage.ApplyState(args);
        });
    }

    private void OnGarageChanged()
    {
        GarageData garage = _garage!.Current!;
        _presets = garage.Vehicles;
        _presetsMenuDirty = true;
        ApplyFavorites(garage.Favorites);
    }

    private bool GarageReady()
    {
        if (_garage?.Current != null) return true;
        Notification.PostTicker(UiText.Get("~o~Гараж ещё загружается.~s~ Попробуйте через секунду."), false);
        return false;
    }

    private static void NotifyGarageResult(GarageResult result, string success) =>
        Notification.PostTicker(result switch
        {
            GarageResult.Saved => "~g~" + UiText.Get(success),
            GarageResult.Full => "~o~" + UiText.Get("Список заполнен. Удалите одну из 64 сохранённых машин."),
            GarageResult.DuplicateName => "~o~" + UiText.Get("Такое название уже есть. Выберите другое."),
            GarageResult.Missing => UiText.Get("~o~Список машин изменился.~s~ Выберите машину заново."),
            GarageResult.Invalid => UiText.Get("~o~Эту конфигурацию пока нельзя сохранить."),
            _ => UiText.Get("~o~Гараж на сервере недоступен.~s~ Данные не изменены.")
        }, false);
}
