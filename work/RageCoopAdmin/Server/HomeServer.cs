using System;
using System.Collections.Generic;
using System.IO;
using OGCoop.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

public sealed partial class CoopAdminServer
{
    private const long HomeSaveCooldownMs = 1000;
    private readonly object homeGate = new();
    private readonly Dictionary<Client, long> lastHomeSave = new();
    private HomeStore? homes;

    private void RegisterHomeEvents()
    {
        homes = new HomeStore(CurrentResource.DataFolder);
        API.RegisterCustomEventHandler(HomeProtocol.Request, OnHomeRequest);
        API.RegisterCustomEventHandler(HomeProtocol.Save, OnHomeSave);
    }

    private void OnHomeRequest(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 0 } || !CanRequestData(e.Client, HomeProtocol.Request)) return;
        lock (homeGate)
        {
            string home;
            try { home = homes!.Load(e.Client.Username); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // An unreadable file stays as it is. No answer goes out, so the menu never offers to overwrite it.
                Logger.Warning($"Home of {e.Client.Username} could not be loaded: {error.GetType().Name}");
                return;
            }
            API.SendCustomEventQueued(new List<Client> { e.Client }, HomeProtocol.State, home);
        }
    }

    private void OnHomeSave(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 1 } || e.Args[0] is not string key) return;
        lock (homeGate)
        {
            bool saved = HomeCodec.IsKey(key) && CanRun(e.Client, lastHomeSave, HomeSaveCooldownMs) && TrySaveHome(e.Client, key);
            API.SendCustomEventQueued(new List<Client> { e.Client }, HomeProtocol.Saved, saved);
        }
    }

    private void OnHomePlayerDisconnected(Client client)
    {
        lock (homeGate) lastHomeSave.Remove(client);
    }

    private bool TrySaveHome(Client client, string key)
    {
        try
        {
            homes!.Save(client.Username, key);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Warning($"Home of {client.Username} could not be saved: {error.GetType().Name}");
            return false;
        }
    }
}
