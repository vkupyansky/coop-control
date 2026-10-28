using System;
using System.Collections.Generic;
using System.IO;
using OGCoop.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

public sealed partial class CoopAdminServer
{
    // The checked server queues CustomEvent, PedSync/OnPlayerUpdate and
    // disconnect callbacks on its one Worker. EngineServerGuard also queues
    // ready callbacks there. OnStart precedes listener start, and guarded unload
    // joins that Worker before OnStop. These session dictionaries therefore
    // share that serialized lifetime. Timer SendPlayerUpdate only sends roster.
    private readonly WorldStateAuthority worldState = new();
    private WorldModeStore? worldModes;
    private readonly Dictionary<Client, int> worldSessions = new();
    private readonly Dictionary<Client, long> lastWorldRequests = new();
    private readonly Dictionary<Client, long> lastWorldFetches = new();
    private readonly Dictionary<Client, int> lastWorldRequestIds = new();
    private readonly Dictionary<Client, long> lastWorldObservations = new();
    // Ready but no world request yet: 0 while its game has sent no update (window in the
    // background, no frames), otherwise the time of the first update.
    private readonly Dictionary<Client, long> worldUnknownReady = new();
    private bool worldStateActive;

    private bool HasWorldSession(Client client, int session, string epoch) =>
        worldStateActive && session > 0 && epoch == worldState.Epoch &&
        worldSessions.TryGetValue(client, out int current) && current == session;

    private void RegisterWorldStateEvents()
    {
        worldModes = new WorldModeStore(CurrentResource.DataFolder);
        try
        {
            bool? ghostTraffic = worldModes.LoadGhostTraffic();
            if (ghostTraffic == null) Logger.Warning("OG Coop: saved world modes are damaged; traffic pass-through starts off.");
            worldState.RestoreGhostTraffic(ghostTraffic == true);
            if (ghostTraffic == true) Logger.Info("OG Coop: traffic pass-through restored: on");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("OG Coop: saved world modes unreadable (" + ex.Message + "); traffic pass-through starts off.");
        }

        worldStateActive = true;
        API.RegisterCustomEventHandler(WorldStateProtocol.Request, OnWorldStateFetch);
        API.RegisterCustomEventHandler(WorldStateProtocol.Change, OnWorldStateChange);
        API.RegisterCustomEventHandler(WorldStateProtocol.Observe, OnWorldHostObservation);
    }

    private void CleanupWorldState()
    {
        worldStateActive = false;
        worldSessions.Clear();
        lastWorldRequests.Clear();
        lastWorldFetches.Clear();
        lastWorldRequestIds.Clear();
        lastWorldObservations.Clear();
        worldUnknownReady.Clear();
        worldState.Reset();
    }

    private void RemoveWorldStateClient(Client client)
    {
        worldSessions.Remove(client);
        lastWorldRequests.Remove(client);
        lastWorldFetches.Remove(client);
        lastWorldRequestIds.Remove(client);
        lastWorldObservations.Remove(client);
        worldUnknownReady.Remove(client);
    }

    private void OnWorldStateFetch(CustomEventReceivedArgs e)
    {
        if (!worldStateActive || !HasReadyPlayer(e) || !IsCurrentReadyClient(e.Client) || e.Args is not { Length: 1 } ||
            e.Args[0] is not int session || session <= 0 || !CanRun(e.Client, lastWorldFetches, 500)) return;
        if (!worldSessions.TryGetValue(e.Client, out int previous) || previous != session)
        {
            lastWorldRequestIds.Remove(e.Client);
            lastWorldRequests.Remove(e.Client);
        }
        worldSessions[e.Client] = session;
        worldUnknownReady.Remove(e.Client);
        SendWorldState(e.Client, session);
        OnGhostTrafficWorldSession(e.Client);
    }

    private void OnWorldStateChange(CustomEventReceivedArgs e)
    {
        if (!worldStateActive || !HasReadyPlayer(e) || !IsCurrentReadyClient(e.Client) || e.Args is not { Length: 7 } ||
            e.Args[0] is not int session || session <= 0 || e.Args[1] is not int requestId || requestId <= 0 ||
            e.Args[2] is not string epoch || !WorldStateProtocol.IsEpoch(epoch) ||
            e.Args[3] is not int kind || !WorldStateProtocol.IsChange(kind) ||
            e.Args[4] is not int value1 || e.Args[5] is not int value2 || e.Args[6] is not bool enabled ||
            !worldSessions.TryGetValue(e.Client, out int currentSession) || currentSession != session) return;
        WorldStateChange change = (WorldStateChange)kind;
        // Replays cannot mutate state a second time or consume a later request ID.
        if ((lastWorldRequestIds.TryGetValue(e.Client, out int previous) && requestId <= previous) ||
            !CanRun(e.Client, lastWorldRequests, 250))
        {
            SendWorldState(e.Client, session, requestId, false, change);
            return;
        }
        if (enabled && (change == WorldStateChange.FreezeTime || change == WorldStateChange.FreezeWeather ||
                change == WorldStateChange.GhostTraffic))
        {
            foreach (Client client in ReadyClients())
            {
                if (worldSessions.ContainsKey(client)) continue;
                SendWorldState(e.Client, session, requestId, false, change);
                SendStatus(e.Client, "Дождитесь загрузки обновлённого меню у всех игроков.");
                return;
            }
        }
        lastWorldRequestIds[e.Client] = requestId;
        bool accepted = worldState.TryChange(epoch, change, value1, value2, enabled,
            Environment.TickCount64, out WorldStateSnapshot state);
        // Send the correlated result before its broadcast. All events use the same
        // reliable ordered channel, including updates from concurrent players.
        API.SendCustomEventQueued(new List<Client> { e.Client }, WorldStateProtocol.State,
            WorldStateProtocol.Encode(state, session, requestId, accepted, change));
        if (accepted)
        {
            API.SendCustomEventQueued(ReadyClients(), WorldStateProtocol.State, WorldStateProtocol.Encode(state, 0, change: change));
            if (change == WorldStateChange.GhostTraffic)
            {
                OnGhostTrafficWorldChanged();
                SaveGhostTraffic(enabled);
            }
        }
    }

    // Only a player's own switch is saved. Modes released for an outdated
    // client keep the saved choice.
    private void SaveGhostTraffic(bool enabled)
    {
        try
        {
            worldModes?.SaveGhostTraffic(enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("OG Coop: traffic pass-through not saved (" + ex.Message + "); it stays " + (enabled ? "on" : "off") + " until the server restarts.");
        }
    }

    private void OnWorldHostObservation(CustomEventReceivedArgs e)
    {
        if (!worldStateActive || !HasReadyPlayer(e) || !IsCurrentReadyClient(e.Client) || !ReferenceEquals(e.Client, API.Host) ||
            e.Args is not { Length: 10 } || e.Args[0] is not int session || session <= 0 ||
            !worldSessions.TryGetValue(e.Client, out int currentSession) || currentSession != session ||
            e.Args[1] is not string epoch || epoch != worldState.Epoch || e.Args[2] is not long revision ||
            e.Args[3] is not int hour || e.Args[4] is not int minute || e.Args[5] is not int second ||
            !WorldStateProtocol.IsClock(hour, minute, second) ||
            e.Args[6] is not int weather1 || e.Args[7] is not int weather2 ||
            e.Args[8] is not float blend || !float.IsFinite(blend) || blend < 0 || blend > 1 ||
            e.Args[9] is not int clockRate || clockRate < 1 || clockRate > 600000 ||
            WorldStateProtocol.WeatherIndexForHash(weather1) < 0 || WorldStateProtocol.WeatherIndexForHash(weather2) < 0 ||
            revision != worldState.Snapshot(Environment.TickCount64).Revision ||
            !CanRun(e.Client, lastWorldObservations, 500)) return;
        int index = WorldStateProtocol.WeatherIndexForHash(blend >= 0.5f ? weather2 : weather1);
        worldState.ObserveHost(hour, minute, second, index, clockRate, Environment.TickCount64, revision);
        MaintainWorldCapabilities();
    }

    private void SendWorldState(Client client, int session, int requestId = 0, bool accepted = true,
        WorldStateChange change = WorldStateChange.Snapshot) =>
        API.SendCustomEventQueued(new List<Client> { client }, WorldStateProtocol.State,
            WorldStateProtocol.Encode(worldState.Snapshot(Environment.TickCount64), session, requestId, accepted, change));

    private void OnWorldStatePlayerReady(Client client)
    {
        if (!worldStateActive) return;
        // OnStart fetch supplies the resource-session token. A ready callback may
        // precede it, so only already registered clients can receive a target reply.
        if (worldSessions.TryGetValue(client, out int session)) SendWorldState(client, session);
        else worldUnknownReady[client] = 0;
    }

    private void OnWorldPlayerUpdate(object? sender, Client client)
    {
        // The 5 s for the world request count from its game's first frame: the resource sends it from a frame.
        if (worldUnknownReady.TryGetValue(client, out long since) && since == 0) worldUnknownReady[client] = Environment.TickCount64;
        MaintainWorldCapabilities();
    }

    private void MaintainWorldCapabilities()
    {
        if (!worldStateActive || worldUnknownReady.Count == 0) return;
        long now = Environment.TickCount64;
        bool incompatible = false;
        foreach (KeyValuePair<Client, long> pending in worldUnknownReady)
        {
            if (pending.Key.IsReady && !worldSessions.ContainsKey(pending.Key) && pending.Value != 0 && now - pending.Value >= 5000)
            {
                incompatible = true;
                break;
            }
        }
        if (!incompatible || !worldState.ReleaseModes(now, out WorldStateSnapshot state)) return;
        API.SendCustomEventQueued(ReadyClients(), WorldStateProtocol.State, WorldStateProtocol.Encode(state, 0));
        OnGhostTrafficWorldChanged();
        foreach (Client client in ReadyClients())
            SendStatus(client, "Общие режимы выключены: у одного из игроков не загрузилось обновлённое меню.");
    }
}
