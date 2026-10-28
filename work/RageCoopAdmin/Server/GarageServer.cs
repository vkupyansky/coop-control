using System;
using System.Collections.Generic;
using System.IO;
using OGCoop.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

public sealed partial class CoopAdminServer
{
    private readonly object garageGate = new();
    private GarageStore? garages;

    private void RegisterGarageEvents()
    {
        garages = new GarageStore(CurrentResource.DataFolder);
        API.RegisterCustomEventHandler(GarageProtocol.Request, OnGarageRequest);
        API.RegisterCustomEventHandler(GarageProtocol.Change, OnGarageChange);
    }

    private void OnGarageRequest(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 0 } || !CanRequestData(e.Client, GarageProtocol.Request)) return;
        lock (garageGate)
        {
            if (TryLoadGarage(e.Client, out GarageData? garage)) SendGarage(e.Client, garage);
        }
    }

    private void OnGarageChange(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args == null) return;
        lock (garageGate)
        {
            if (e.Args.Length < 1 || e.Args[0] is not int token) return;
            // Every change reads, writes with fsync and sends the whole garage.
            if (!CanRequestData(e.Client, GarageProtocol.Change, GarageChangeCooldownMs))
            {
                SendGarageResult(e.Client, token, GarageResult.Unavailable);
                return;
            }
            if (!TryLoadGarage(e.Client, out GarageData? garage))
            {
                SendGarageResult(e.Client, token, GarageResult.Unavailable);
                return;
            }
            if (!GarageCodec.TryApply(garage, e.Args, out token, out GarageResult result)) return;
            if (result == GarageResult.Saved && !TrySaveGarage(e.Client, garage)) result = GarageResult.Unavailable;
            SendGarageResult(e.Client, token, result);
            if (result == GarageResult.Saved) SendGarage(e.Client, garage);
        }
    }

    private bool TryLoadGarage(Client client, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GarageData? garage)
    {
        try
        {
            garage = garages!.Load(client.Username);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            // An unreadable file is kept untouched rather than replaced by an empty garage.
            Logger.Warning($"Garage of {client.Username} could not be loaded: {error.GetType().Name}");
            garage = null;
            return false;
        }
    }

    private bool TrySaveGarage(Client client, GarageData garage)
    {
        try
        {
            garages!.Save(client.Username, garage);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Warning($"Garage of {client.Username} could not be saved: {error.GetType().Name}");
            return false;
        }
    }

    private void SendGarage(Client client, GarageData garage) =>
        API.SendCustomEventQueued(new List<Client> { client }, GarageProtocol.State, GarageCodec.Encode(garage));

    private void SendGarageResult(Client client, int token, GarageResult result) =>
        API.SendCustomEventQueued(new List<Client> { client }, GarageProtocol.Result, token, (int)result);
}
