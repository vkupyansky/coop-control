using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoopAdmin.Server;
using OGCoop.Shared;
using GTA;
using HarmonyLib;
using RageCoop.Server;
using RageCoop.Server.Scripting;

internal static class Program
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<(Client Client, int Event, object[] Args)> Sent = new();

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.ghostregistry.transport");
        try
        {
            boundary.Patch(typeof(API).GetMethod(nameof(API.SendCustomEventQueued), Instance)!,
                prefix: new HarmonyMethod(typeof(Program), nameof(CaptureSend)));
            CheckOwnerAndSession();
            CheckTransferReuseAndPromotion();
            CheckBoundStopAndLateJoin();
            CheckWorldServerProtocol();
            Console.WriteLine("Actual server ghost registry and entity-table checks passed; transport was captured, no GTA collision simulation was performed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); Sent.Clear(); }
    }

    private static void CheckOwnerAndSession()
    {
        using var fixture = new Fixture();
        Client first = fixture.Client(11, 101), second = fixture.Client(22, 202);
        fixture.Vehicle(51, first);
        fixture.Report(first, 101, 1, new GhostTrafficClaim(51, 900, true));
        Assert(fixture.Entries(first).Single().Id == 51 && fixture.Entries(second).Single().OwnerId == 11,
            "A ready owner registers ambient provenance through the production handler and both clients receive one shared state.");
        long revision = fixture.Revision;
        fixture.Report(second, 202, 1, new GhostTrafficClaim(51, 900, false));
        Assert(fixture.Revision == revision && fixture.Entries(first).Length == 1,
            "Another connected player cannot protect or classify a vehicle owned by someone else.");
        fixture.ResetRate(first);
        fixture.Report(first, 999, 2, new GhostTrafficClaim(51, 900, false));
        fixture.Report(first, 101, 2, new GhostTrafficClaim(51, 900, false), epoch: new string('a', 32));
        fixture.Report(first, 101, 1, new GhostTrafficClaim(51, 900, false));
        Assert(fixture.Revision == revision && fixture.Entries(first).Length == 1,
            "A wrong session, old server epoch and replayed sequence cannot mutate accepted provenance.");
        Set(first, nameof(Client.IsReady), false);
        fixture.Report(first, 101, 2, new GhostTrafficClaim(51, 900, false));
        Set(first, nameof(Client.IsReady), true);
        Assert(fixture.Revision == revision, "An unready sender cannot mutate the registry.");
        fixture.Vehicle(52, first, 901);
        fixture.Report(first, 101, 2, new GhostTrafficClaim(52, 902, true));
        Assert(fixture.Revision == revision, "A claimed model that differs from the server's known model is rejected.");
        fixture.ResetRate(first);
        fixture.Report(first, 101, 3, new GhostTrafficClaim(52, 901, true));
        Assert(fixture.Entries(first).All(entry => entry.Id != 52),
            "Server-created vehicles with a known model never enter the eligible traffic snapshot.");
        fixture.Sessions[first] = 303;
        Call(fixture.Resource, "OnGhostTrafficWorldSession", first);
        fixture.Report(first, 101, 4, new GhostTrafficClaim(51, 900, false));
        Assert(fixture.Entries(first).Length == 1, "A previous resource session cannot affect the replacement session.");
        fixture.Report(first, 303, 1, new GhostTrafficClaim(51, 900, false));
        Assert(fixture.Entries(first).Length == 0,
            "The new resource session may start its own report sequence without inheriting the old sequence.");
    }

    private static void CheckTransferReuseAndPromotion()
    {
        using var fixture = new Fixture();
        Client first = fixture.Client(11, 101), second = fixture.Client(22, 202);
        ServerVehicle vehicle = fixture.Vehicle(61, first);
        fixture.Report(first, 101, 1, new GhostTrafficClaim(61, 910, true));
        Set(vehicle, nameof(ServerVehicle.Owner), second);
        fixture.Prune();
        Assert(fixture.Entries(first).Single().OwnerId == 22 && fixture.Entries(second).Single().OwnerId == 22,
            "Ownership transfer preserves exact-object ambient provenance and publishes its new physics owner to every player.");
        fixture.ResetRate(first);
        long revision = fixture.Revision;
        fixture.Report(first, 101, 2, new GhostTrafficClaim(61, 910, false));
        Assert(fixture.Revision == revision, "The prior physics owner loses report authority immediately after transfer.");
        Set(second.Player, nameof(ServerPed.LastVehicle), vehicle);
        fixture.Prune();
        Assert(fixture.Entries(first).Length == 0, "A vehicle used by a real player is permanently promoted out of ambient traffic.");
        Set(second.Player, nameof(ServerPed.LastVehicle), null);
        fixture.Report(second, 202, 1, new GhostTrafficClaim(61, 910, true));
        Assert(fixture.Entries(second).Length == 0, "A new owner's ambient flag cannot erase existing player-use protection.");
        fixture.Vehicle(61, second);
        fixture.Prune();
        Assert(fixture.Records.Count == 0 && fixture.Entries(first).Length == 0,
            "A reused network ID with a different ServerVehicle object inherits neither ambient provenance nor protection.");
        fixture.ResetRate(second);
        fixture.Report(second, 202, 2, new GhostTrafficClaim(61, 911, true));
        Assert(fixture.Entries(first).Single().ModelHash == 911, "A newly observed replacement vehicle needs its own owner report and model.");
        fixture.Vehicles.TryRemove(61, out _);
        fixture.Prune();
        Assert(fixture.Records.Count == 0 && fixture.Entries(first).Length == 0,
            "Removal from the actual server entity table removes the published eligibility.");
    }

    private static void CheckBoundStopAndLateJoin()
    {
        using var fixture = new Fixture();
        Client first = fixture.Client(11, 101);
        long sequence = 0;
        for (int start = 0; start < GhostTrafficProtocol.MaximumEntries; start += GhostTrafficProtocol.MaximumClaims)
        {
            var claims = new List<GhostTrafficClaim>();
            for (int offset = 0; offset < GhostTrafficProtocol.MaximumClaims; offset++)
            {
                int id = start + offset + 1;
                fixture.Vehicle(id, first);
                claims.Add(new GhostTrafficClaim(id, 920, true));
            }
            fixture.ResetRate(first);
            fixture.Report(first, 101, ++sequence, claims.ToArray());
        }
        fixture.Vehicle(2000, first);
        fixture.ResetRate(first);
        fixture.Report(first, 101, ++sequence, new GhostTrafficClaim(2000, 920, true));
        Assert(fixture.Records.Count == GhostTrafficProtocol.MaximumEntries &&
            fixture.Entries(first).Length == GhostTrafficProtocol.MaximumEntries,
            "Owner reports and retained provenance have a strict 1024-entry cap without evicting live protection.");
        Client late = fixture.Client(22, 202);
        Assert(fixture.Entries(late).Length == GhostTrafficProtocol.MaximumEntries && fixture.SessionInLastState(late) == 202,
            "A late player's registered world session receives the complete current bounded registry with its own session token.");
        fixture.SetEnabled(false);
        Assert(fixture.Entries(first).Length == 0 && fixture.Entries(late).Length == 0,
            "Disabling shared traffic ghost sends empty eligibility to all clients.");
        fixture.SetEnabled(true);
        Assert(fixture.Entries(late).Length == GhostTrafficProtocol.MaximumEntries,
            "Re-enabling the mode retains live-object provenance while regenerating shared eligibility.");
        Set(first, nameof(Client.IsReady), false);
        Call(fixture.Resource, "OnGhostTrafficPlayerDisconnected", first);
        Assert(fixture.Entries(late).Length == 0 && fixture.Records.Count == GhostTrafficProtocol.MaximumEntries,
            "Disconnect hides unavailable owners while preserving provenance for surviving objects awaiting owner transfer.");
        int before = Sent.Count;
        Call(fixture.Resource, "CleanupGhostTraffic");
        Call(fixture.Resource, "CleanupGhostTraffic");
        Set(first, nameof(Client.IsReady), true);
        fixture.Report(first, 101, ++sequence, new GhostTrafficClaim(1, 920, true));
        Call(fixture.Resource, "OnGhostTrafficWorldSession", late);
        Assert(fixture.Records.Count == 0 && Sent.Count == before,
            "Repeated cleanup clears retained references and stale captured handlers cannot send or repopulate after stop.");
    }

    private static void CheckWorldServerProtocol()
    {
        using var fixture = new Fixture();
        fixture.SetEnabled(false);
        Client first = fixture.Client(11, 101), second = fixture.Client(22, 202);
        fixture.Sessions.Remove(second);
        Call(fixture.Resource, "OnWorldStatePlayerReady", second);
        Sent.Clear();
        Call(fixture.Resource, "OnWorldStateFetch", new CustomEventReceivedArgs { Client = second, Args = new object[] { 202 } });
        int[] events = Sent.Where(value => ReferenceEquals(value.Client, second)).Select(value => value.Event).ToArray();
        Assert(events.SequenceEqual(new[] { 20774144, GhostTrafficProtocol.State }),
            "A production world fetch sends its correlated world identity before the late-join traffic registry on the same transport.");
        Assert(!((IDictionary)Field(fixture.Resource, "worldUnknownReady")).Contains(second),
            "A successful versioned resource handshake removes the unknown-client grace deadline.");
        SetField(fixture.Host, "_hostClient", first);
        long revision = fixture.WorldRevision;
        fixture.Observe(first, 101, revision, 8, 9, 10);
        Assert((int)fixture.WorldState[9] == 8 && (int)fixture.WorldState[10] == 9,
            "The current real host supplies the authoritative observed clock through the versioned production handler.");
        fixture.Observe(second, 202, revision, 17, 0, 0);
        Assert((int)fixture.WorldState[9] == 8, "A ready non-host cannot overwrite authoritative world observations.");
        Sent.Clear();
        fixture.Change(first, 101, 1, 3, 14, 50, true);
        object[] ack = fixture.LastWorld(first);
        Assert((int)ack[3] == 0 && (bool)ack[6] && (int)ack[9] == 8,
            "A freeze is broadcast to both ready players using the observed shared clock rather than the sender's local fallback.");
        Assert(Sent.Count(value => value.Event == 20774144 && (int)value.Args[3] == 0) == 2 &&
            Sent.Any(value => value.Event == 20774144 && (int)value.Args[3] == 1 && (bool)value.Args[4]),
            "The production change sends a correlated success and a shared state to every ready client.");
        fixture.Observe(first, 101, revision, 17, 0, 0);
        Assert((int)fixture.WorldState[9] == 8, "An old host observation cannot undo an accepted control revision.");
        fixture.Change(second, 202, 1, 1, 21, 25, false);
        Assert((bool)fixture.LastWorld(first)[6] && (int)fixture.LastWorld(first)[9] == 21 &&
            (int)fixture.LastWorld(second)[9] == 21,
            "A second player's later time selection replaces the shared value in server order while preserving the active freeze for all.");
        Client unknown = fixture.Client(33, 303);
        fixture.Sessions.Remove(unknown);
        Call(fixture.Resource, "OnWorldStatePlayerReady", unknown);
        Sent.Clear();
        fixture.ResetWorldRate(first);
        fixture.Change(first, 101, 2, 5, 0, 0, true);
        object[] refused = fixture.LastWorld(first);
        Assert((int)refused[3] == 2 && !(bool)refused[4] && !(bool)refused[8],
            "Enabling a shared mode is explicitly rejected while any ready player has no supported resource handshake.");
        Call(fixture.Resource, "OnWorldPlayerUpdate", null, first);
        Assert((bool)fixture.WorldState[6], "A normal late join retains active modes during its bounded handshake grace.");
        var unknownReady = (IDictionary)Field(fixture.Resource, "worldUnknownReady");
        unknownReady[unknown] = Environment.TickCount64 - 5001;
        Sent.Clear();
        Call(fixture.Resource, "OnWorldPlayerUpdate", null, first);
        object[] safe = fixture.LastWorld(first);
        Assert(!(bool)safe[6] && !(bool)safe[7] && !(bool)safe[8] &&
            Sent.Count(value => value.Event == 20774114) == 3,
            "An incompatible late join releases all shared modes after the grace period and informs all ready players without kicking them.");
        long afterSafety = fixture.WorldRevision;
        int count = Sent.Count;
        Call(fixture.Resource, "OnWorldPlayerUpdate", null, first);
        Assert(fixture.WorldRevision == afterSafety && Sent.Count == count,
            "An already disabled safety state does not produce repeated status or state spam.");
        fixture.Sessions[unknown] = 303;
        Call(fixture.Resource, "OnGhostTrafficWorldSession", unknown);
        fixture.ResetWorldRate(first);
        fixture.Change(first, 101, 3, 5, 0, 0, true);
        Assert((bool)fixture.WorldState[8], "After every player has the current resource, the same production request enables the mode for all.");
        Client replacement = fixture.Client(11, 404);
        long beforeStale = fixture.WorldRevision;
        Sent.Clear();
        fixture.ResetWorldRate(first);
        fixture.Change(first, 101, 4, 5, 0, 0, false);
        fixture.Observe(first, 101, beforeStale, 17, 0, 0);
        Call(fixture.Resource, "OnWorldStateFetch", new CustomEventReceivedArgs { Client = first, Args = new object[] { 505 } });
        Assert(fixture.WorldRevision == beforeStale && Sent.Count == 0 && !ReferenceEquals(first, replacement),
            "A stale ready Client object with a reused player ID cannot fetch, observe or change the current server world.");
        fixture.ResetWorldRate(replacement);
        fixture.Change(replacement, 404, 1, 5, 0, 0, false, epoch: new string('b', 32));
        Assert(fixture.WorldRevision == beforeStale && !(bool)fixture.LastWorld(replacement)[4],
            "The real production handler rejects an old resource epoch with a correlated current-state response.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly CoopAdminServer Resource = new();
        internal readonly Server Host;
        internal readonly ConcurrentDictionary<int, ServerVehicle> Vehicles;
        internal readonly Dictionary<Client, int> Sessions;
        internal readonly IDictionary Records;
        internal readonly string Epoch;

        internal Fixture()
        {
            // Construct the real API, dictionaries and entities without Start:
            // no listener, external accounts, game installation or working data.
            Host = new Server(new Settings { AnnounceSelf = false, AutoUpdate = false, UseP2P = false });
            Resource.API = Host.API;
            Vehicles = (ConcurrentDictionary<int, ServerVehicle>)Host.API.Entities.GetType().GetProperty("Vehicles", Instance)!.GetValue(Host.API.Entities)!;
            Sessions = (Dictionary<Client, int>)Field(Resource, "worldSessions");
            Records = (IDictionary)Field(Resource, "ghostTrafficRecords");
            SetField(Resource, "worldStateActive", true);
            Call(Resource, "RegisterGhostTrafficEvents");
            Epoch = (string)Field(Resource, "worldState").GetType().GetProperty("Epoch", Instance)!.GetValue(Field(Resource, "worldState"))!;
            SetEnabled(true);
        }

        private object CurrentWorldSnapshot => Field(Resource, "worldState").GetType().GetMethod("Snapshot", Instance)!
            .Invoke(Field(Resource, "worldState"), new object[] { Environment.TickCount64 })!;
        internal long WorldRevision => (long)CurrentWorldSnapshot.GetType().GetProperty("Revision", Instance)!.GetValue(CurrentWorldSnapshot)!;

        internal object[] WorldState
        {
            get
            {
                object state = CurrentWorldSnapshot;
                Type protocol = state.GetType().Assembly.GetType("OGCoop.Shared.WorldStateProtocol", true)!;
                Type change = state.GetType().Assembly.GetType("OGCoop.Shared.WorldStateChange", true)!;
                return (object[])protocol.GetMethod("Encode", Instance | BindingFlags.Static)!.Invoke(null, new object[] { state, 0, 0, true, Enum.ToObject(change, 0) })!;
            }
        }

        internal object[] LastWorld(Client client) => Sent.Last(value => ReferenceEquals(value.Client, client) && value.Event == 20774144).Args;

        internal void Change(Client client, int session, int requestId, int kind, int value1, int value2, bool enabled, string? epoch = null) =>
            Call(Resource, "OnWorldStateChange", new CustomEventReceivedArgs
            { Client = client, Args = new object[] { session, requestId, epoch ?? Epoch, kind, value1, value2, enabled } });

        internal void Observe(Client client, int session, long revision, int hour, int minute, int second) =>
            Call(Resource, "OnWorldHostObservation", new CustomEventReceivedArgs
            { Client = client, Args = new object[] { session, Epoch, revision, hour, minute, second, 0x36A83D84, 0x36A83D84, 0f, 2000 } });

        internal void ResetWorldRate(Client client)
        {
            ((IDictionary)Field(Resource, "lastWorldRequests")).Remove(client);
            ((IDictionary)Field(Resource, "lastWorldObservations")).Remove(client);
            ((IDictionary)Field(Resource, "lastWorldFetches")).Remove(client);
        }

        internal long Revision => (long)Field(Resource, "ghostTrafficRevision");

        internal Client Client(int id, int session)
        {
            var client = (Client)Activator.CreateInstance(typeof(Client), Instance, null, new object?[] { Host }, null)!;
            var ped = (ServerPed)Activator.CreateInstance(typeof(ServerPed), Instance, null, new object?[] { Host }, null)!;
            Set(ped, "ID", id); Set(ped, "Owner", client); Set(client, "Player", ped); Set(client, "IsReady", true);
            ((IDictionary)Field(Host, "ClientsByID"))[id] = client;
            Sessions[client] = session;
            Call(Resource, "OnGhostTrafficWorldSession", client);
            return client;
        }

        internal ServerVehicle Vehicle(int id, Client owner, int model = 0)
        {
            var vehicle = (ServerVehicle)RuntimeHelpers.GetUninitializedObject(typeof(ServerVehicle));
            Set(vehicle, "ID", id); Set(vehicle, "Owner", owner); Set(vehicle, "Model", new Model(model));
            Vehicles[id] = vehicle;
            return vehicle;
        }

        internal void Report(Client client, int session, long sequence, GhostTrafficClaim claim, string? epoch = null) =>
            Report(client, session, sequence, new[] { claim }, epoch);

        internal void Report(Client client, int session, long sequence, GhostTrafficClaim[] claims, string? epoch = null) =>
            Call(Resource, "OnGhostTrafficReport", new CustomEventReceivedArgs
            {
                Client = client,
                Args = GhostTrafficProtocol.EncodeReport(session, epoch ?? Epoch, sequence, claims)
            });

        internal void ResetRate(Client client)
        {
            var reporters = (IDictionary)Field(Resource, "ghostTrafficReporters");
            if (reporters.Contains(client)) SetField(reporters[client]!, "LastReport", 0L);
        }

        internal void Prune()
        {
            SetField(Resource, "lastGhostTrafficPrune", 0L);
            Call(Resource, "OnGhostTrafficPlayerUpdate", null, Host.API.GetAllClients().Values.First());
        }

        internal void SetEnabled(bool enabled)
        {
            object state = Field(Resource, "worldState");
            Type change = state.GetType().Assembly.GetType("OGCoop.Shared.WorldStateChange", true)!;
            string epoch = (string)state.GetType().GetProperty("Epoch", Instance)!.GetValue(state)!;
            object?[] args = { epoch, Enum.ToObject(change, 5), 0, 0, enabled, Environment.TickCount64, null };
            state.GetType().GetMethod("TryChange", Instance)!.Invoke(state, args);
            Call(Resource, "OnGhostTrafficWorldChanged");
        }

        internal GhostTrafficEntry[] Entries(Client client)
        {
            object[] args = Sent.Last(value => ReferenceEquals(value.Client, client) && value.Event == GhostTrafficProtocol.State).Args;
            if (!GhostTrafficProtocol.TryReadState(args, out _, out string epoch, out _, out GhostTrafficEntry[] entries) || epoch != Epoch)
                throw new InvalidOperationException("Server emitted invalid registry state.");
            return entries;
        }

        internal int SessionInLastState(Client client) =>
            (int)Sent.Last(value => ReferenceEquals(value.Client, client) && value.Event == GhostTrafficProtocol.State).Args[0];

        public void Dispose()
        {
            Call(Resource, "CleanupGhostTraffic");
            ((IDisposable)Field(Host, "_worker")).Dispose();
            foreach (string name in new[] { "_playerUpdateTimer", "_announceTimer", "_antiAssholesTimer", "_updateTimer" })
                ((IDisposable)Field(Host, name)).Dispose();
            Sent.Clear();
        }
    }

    private static object Field(object target, string name) => target.GetType().GetField(name, Instance)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
    private static void Set(object target, string name, object? value) => target.GetType().GetProperty(name, Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Instance)!.Invoke(target, args);
    private static bool CaptureSend(List<Client> __0, int __1, object[] __2)
    { foreach (Client client in __0) Sent.Add((client, __1, __2)); return false; }
    private static void Assert(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS " + label); }
}
