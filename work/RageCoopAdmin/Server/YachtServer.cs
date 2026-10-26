using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OGCoop.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

// Yachts are kept per RageCoop name (YachtStore). Every player on the server
// sees the yachts of everyone on it; a berth holds one yacht, so a player who
// joins with a berth already taken has theirs moved to the next free one.
public sealed partial class CoopAdminServer
{
    private const long YachtSaveCooldownMs = 1000;
    private readonly object yachtGate = new();
    private readonly Dictionary<Client, long> lastYachtSave = new();
    // The yachts on the map: those of the players on the server.
    private readonly Dictionary<Client, YachtLook> shownYachts = new();
    private YachtStore? yachts;

    private void RegisterYachtEvents()
    {
        yachts = new YachtStore(CurrentResource.DataFolder);
        API.RegisterCustomEventHandler(YachtProtocol.Request, OnYachtRequest);
        API.RegisterCustomEventHandler(YachtProtocol.Save, OnYachtSave);
    }

    private void OnYachtRequest(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 0 }) return;
        lock (yachtGate)
        {
            // An unreadable file gets no answer, so the game does not offer a
            // new yacht over it.
            if (!TryLoadYacht(e.Client, out YachtLook? own)) return;
            shownYachts.Remove(e.Client);
            if (own != null && BerthTaken(own.Berth, e.Client))
            {
                int free = YachtCodec.FreeBerth(own.Berth + 1, berth => BerthTaken(berth, e.Client));
                if (free < 0) own = null;
                else
                {
                    own.Berth = free;
                    if (!TrySaveYacht(e.Client, own)) own = null;
                }
            }
            var requester = new List<Client> { e.Client };
            API.SendCustomEventQueued(requester, YachtProtocol.State, own == null ? string.Empty : YachtCodec.Encode(own));
            foreach (KeyValuePair<Client, YachtLook> shown in shownYachts.Where(item => IsReady(item.Key)))
                API.SendCustomEventQueued(requester, YachtProtocol.Look, shown.Key.Username, YachtCodec.Encode(shown.Value));
            if (own != null) Show(e.Client, own);
        }
    }

    private void OnYachtSave(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 1 } || e.Args[0] is not string xml) return;
        lock (yachtGate)
        {
            YachtLook look;
            try { look = YachtCodec.Decode(xml); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            {
                SendYachtSaved(e.Client, false);
                return;
            }
            bool saved = !BerthTaken(look.Berth, e.Client) && CanRun(e.Client, lastYachtSave, YachtSaveCooldownMs) && TrySaveYacht(e.Client, look);
            SendYachtSaved(e.Client, saved);
            if (saved) Show(e.Client, look);
        }
    }

    private void OnYachtPlayerDisconnected(Client client)
    {
        lock (yachtGate)
        {
            lastYachtSave.Remove(client);
            if (!shownYachts.Remove(client)) return;
            List<Client> others = ReadyClients().Where(other => !ReferenceEquals(other, client)).ToList();
            if (others.Count > 0) API.SendCustomEventQueued(others, YachtProtocol.Look, client.Username, string.Empty);
        }
    }

    private void Show(Client owner, YachtLook look)
    {
        shownYachts[owner] = YachtCodec.Copy(look);
        List<Client> others = ReadyClients().Where(client => !ReferenceEquals(client, owner)).ToList();
        if (others.Count > 0) API.SendCustomEventQueued(others, YachtProtocol.Look, owner.Username, YachtCodec.Encode(look));
    }

    private bool BerthTaken(int berth, Client owner) =>
        shownYachts.Any(item => !ReferenceEquals(item.Key, owner) && item.Value.Berth == berth && IsReady(item.Key));

    private bool IsReady(Client client) => ReadyClients().Any(ready => ReferenceEquals(ready, client));

    private bool TryLoadYacht(Client client, out YachtLook? look)
    {
        try
        {
            look = yachts!.Load(client.Username);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            // An unreadable file is kept untouched rather than replaced.
            Logger.Warning($"Yacht of {client.Username} could not be loaded: {error.GetType().Name}");
            look = null;
            return false;
        }
    }

    private bool TrySaveYacht(Client client, YachtLook look)
    {
        try
        {
            yachts!.Save(client.Username, look);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Warning($"Yacht of {client.Username} could not be saved: {error.GetType().Name}");
            return false;
        }
    }

    private void SendYachtSaved(Client client, bool saved) =>
        API.SendCustomEventQueued(new List<Client> { client }, YachtProtocol.Saved, saved);
}
