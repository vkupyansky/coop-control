using System;
using System.IO;
using System.Xml;
using CoopControl.Shared;
using GTA.UI;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

public sealed partial class AdminMenuScript
{
    private GarageClient? _garage;
    private bool _garageImportChecked;

    private void RegisterGarageEvents()
    {
        _eventSubscriptions.Register(GarageProtocol.State, e => QueueGarageEvent(e, state: true));
        _eventSubscriptions.Register(GarageProtocol.Result, e => QueueGarageEvent(e, state: false));
    }

    private void InitializeGarage()
    {
        _garage = new GarageClient((hash, args) => API.SendCustomEvent(hash, args));
        _garage.Changed += OnGarageChanged;
        _garageImportChecked = false;
        _garage.Request();
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
            if (_garage.ApplyState(args) && !_garageImportChecked)
            {
                _garageImportChecked = true;
                ImportLocalGarage();
            }
        });
    }

    private void ImportLocalGarage()
    {
        try
        {
            _garage!.ImportLocal(CurrentResource.DataFolder, complete => Notification.PostTicker(UiText.Get(complete
                ? "~g~Гараж этого компьютера перенесён на сервер."
                : "~o~Гараж этого компьютера перенесён не полностью.~s~ Файлы на компьютере оставлены."), false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or XmlException)
        {
            // The files stay untouched; the next connection tries again.
            Logger.Warning("Coop Control: local garage could not be moved to the server: " + ex.GetType().Name);
        }
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
