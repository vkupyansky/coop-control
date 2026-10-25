using System;
using System.Collections.Generic;
using System.Linq;
using CoopControl.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

public sealed partial class CoopAdminServer
{
    private readonly object ghostTrafficGate = new();
    private readonly Dictionary<int, GhostTrafficRecord> ghostTrafficRecords = new();
    private readonly Dictionary<Client, GhostTrafficReporter> ghostTrafficReporters = new();
    private bool ghostTrafficActive;
    private long ghostTrafficRevision = 1;
    private long lastGhostTrafficPrune;

    private void RegisterGhostTrafficEvents()
    {
        lock (ghostTrafficGate) ghostTrafficActive = true;
        API.RegisterCustomEventHandler(GhostTrafficProtocol.Report, OnGhostTrafficReport);
    }

    private void CleanupGhostTraffic()
    {
        lock (ghostTrafficGate)
        {
            ghostTrafficActive = false;
            ghostTrafficRecords.Clear();
            ghostTrafficReporters.Clear();
            ghostTrafficRevision = 1;
            lastGhostTrafficPrune = 0;
        }
    }

    private void OnGhostTrafficWorldSession(Client client)
    {
        lock (ghostTrafficGate)
        {
            if (!ghostTrafficActive || !IsCurrentReadyClient(client) ||
                !worldSessions.TryGetValue(client, out int session) || session <= 0) return;
            if (!ghostTrafficReporters.TryGetValue(client, out GhostTrafficReporter? reporter) || reporter.Session != session)
                ghostTrafficReporters[client] = new GhostTrafficReporter(session);
            PruneGhostTraffic();
            SendGhostTrafficState(client, session);
        }
    }

    private void OnGhostTrafficWorldChanged()
    {
        lock (ghostTrafficGate)
        {
            if (!ghostTrafficActive) return;
            PruneGhostTraffic();
            ghostTrafficRevision = checked(ghostTrafficRevision + 1);
            BroadcastGhostTrafficState();
        }
    }

    private void OnGhostTrafficPlayerUpdate(object? sender, Client client)
    {
        lock (ghostTrafficGate)
        {
            if (!ghostTrafficActive) return;
            long now = Environment.TickCount64;
            if (now - lastGhostTrafficPrune < 1000) return;
            lastGhostTrafficPrune = now;
            if (PruneGhostTraffic()) PublishGhostTrafficChange();
        }
    }

    private void OnGhostTrafficPlayerDisconnected(Client client)
    {
        lock (ghostTrafficGate)
        {
            if (!ghostTrafficActive) return;
            ghostTrafficReporters.Remove(client);
            // An owner can change without destroying the vehicle. Keep its exact
            // object provenance, but stop advertising an unavailable authority.
            if (PruneGhostTraffic()) PublishGhostTrafficChange();
        }
    }

    private void OnGhostTrafficReport(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args == null ||
            !GhostTrafficProtocol.TryReadReport(e.Args, out int session, out string epoch,
                out long sequence, out GhostTrafficClaim[] claims)) return;
        lock (ghostTrafficGate)
        {
            if (!ghostTrafficActive || !IsCurrentReadyClient(e.Client) ||
                !HasWorldSession(e.Client, session, epoch) ||
                !ghostTrafficReporters.TryGetValue(e.Client, out GhostTrafficReporter? reporter) ||
                reporter.Session != session || sequence <= reporter.Sequence) return;
            long now = Environment.TickCount64;
            if (reporter.HasReport && now - reporter.LastReport < 500) return;
            reporter.Sequence = sequence;
            reporter.LastReport = now;
            reporter.HasReport = true;
            bool changed = PruneGhostTraffic();
            HashSet<ServerVehicle> protectedVehicles = ProtectedGhostTrafficVehicles(API.GetAllClients().Values);
            foreach (GhostTrafficClaim claim in claims)
            {
                ServerVehicle? vehicle = API.Entities.GetVehicleByID(claim.Id);
                if (vehicle == null || !ReferenceEquals(vehicle.Owner, e.Client)) continue;
                int knownModel = vehicle.Model.Hash;
                if (knownModel != 0 && knownModel != claim.ModelHash) continue;
                if (ghostTrafficRecords.TryGetValue(claim.Id, out GhostTrafficRecord? record))
                {
                    if (!ReferenceEquals(record.Vehicle, vehicle) || record.ModelHash != claim.ModelHash) continue;
                    // A player-used/created car never becomes ambient again just
                    // because another owner later reports a local population flag.
                    if (!claim.Ambient && record.Ambient)
                    {
                        record.Ambient = false;
                        changed = true;
                    }
                    continue;
                }
                if (ghostTrafficRecords.Count >= GhostTrafficProtocol.MaximumEntries) continue;
                // In the supported server, client-discovered traffic has Model=0;
                // CreateVehicle assigns Model. This excludes server-created cars.
                bool ambient = claim.Ambient && knownModel == 0 && !protectedVehicles.Contains(vehicle);
                ghostTrafficRecords.Add(claim.Id, new GhostTrafficRecord(vehicle, claim.ModelHash, ambient));
                changed = true;
            }
            if (changed) PublishGhostTrafficChange();
        }
    }

    private bool PruneGhostTraffic()
    {
        bool changed = false;
        Dictionary<int, Client> clients = API.GetAllClients();
        HashSet<ServerVehicle> protectedVehicles = ProtectedGhostTrafficVehicles(clients.Values);
        foreach (KeyValuePair<int, GhostTrafficRecord> pair in ghostTrafficRecords.ToArray())
        {
            GhostTrafficRecord record = pair.Value;
            ServerVehicle? current = API.Entities.GetVehicleByID(pair.Key);
            if (current == null || !ReferenceEquals(current, record.Vehicle) ||
                (current.Model.Hash != 0 && current.Model.Hash != record.ModelHash))
            {
                ghostTrafficRecords.Remove(pair.Key);
                changed = true;
                continue;
            }
            if (record.Ambient && (current.Model.Hash != 0 || protectedVehicles.Contains(current)))
            {
                record.Ambient = false;
                changed = true;
            }
            int ownerId = IsCurrentReadyClient(current.Owner, clients) ? current.Owner.Player.ID : 0;
            if (record.OwnerId != ownerId)
            {
                record.OwnerId = ownerId;
                changed = true;
            }
        }
        return changed;
    }

    private HashSet<ServerVehicle> ProtectedGhostTrafficVehicles(IEnumerable<Client> clients)
    {
        var protectedVehicles = new HashSet<ServerVehicle>(spawnedVehicles.Values);
        foreach (Client client in clients)
            if (client.Player?.LastVehicle is ServerVehicle used) protectedVehicles.Add(used);
        return protectedVehicles;
    }

    private void PublishGhostTrafficChange()
    {
        ghostTrafficRevision = checked(ghostTrafficRevision + 1);
        BroadcastGhostTrafficState();
    }

    private void BroadcastGhostTrafficState()
    {
        foreach (Client client in ReadyClients())
            if (worldSessions.TryGetValue(client, out int session)) SendGhostTrafficState(client, session);
    }

    private void SendGhostTrafficState(Client client, int session)
    {
        GhostTrafficEntry[] entries = worldState.GhostTrafficEnabled
            ? ghostTrafficRecords.Values.Where(record => record.Ambient && record.OwnerId != 0)
                .Select(record => new GhostTrafficEntry(record.Vehicle.ID, record.ModelHash, record.OwnerId)).ToArray()
            : Array.Empty<GhostTrafficEntry>();
        API.SendCustomEventQueued(new List<Client> { client }, GhostTrafficProtocol.State,
            GhostTrafficProtocol.EncodeState(session, worldState.Epoch, ghostTrafficRevision, entries));
    }

    private sealed class GhostTrafficReporter
    {
        internal GhostTrafficReporter(int session) { Session = session; }
        internal readonly int Session;
        internal long Sequence;
        internal long LastReport;
        internal bool HasReport;
    }

    private sealed class GhostTrafficRecord
    {
        internal GhostTrafficRecord(ServerVehicle vehicle, int modelHash, bool ambient)
        { Vehicle = vehicle; ModelHash = modelHash; Ambient = ambient; OwnerId = vehicle.Owner.Player.ID; }
        internal readonly ServerVehicle Vehicle;
        internal readonly int ModelHash;
        internal bool Ambient;
        internal int OwnerId;
    }
}
