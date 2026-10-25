using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using GTA;
using GTA.Math;
using HarmonyLib;
using RageCoop.Client;
using RageCoopAdmin.Client;

internal static class Program
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string PatchId = "friendslegacy.ragecoop.enginetraffic";
    private static bool _modelValid;
    private static int _modelRequests;

    private static int Main()
    {
        try
        {
            CheckCullingPolicy();
            CheckInstalledInstructions();
            CheckCadence();
            try
            {
                Assert(EngineTrafficGuard.Install() && EngineTrafficGuard.Install(), "The verified installed engine accepts one idempotent traffic patch set.");
                CheckCreateGate();
                CheckPoolRecreation();
                CheckSkippedSend();
                CheckNearbyCopies();
                CheckHandover();
            }
            finally { EngineTrafficGuard.Uninstall(); }
            Assert(Harmony.GetPatchInfo(EngineTrafficGuard.FindDoSync()!)?.Owners.Contains(PatchId) != true &&
                Harmony.GetPatchInfo(EngineTrafficGuard.FindCreateVehicle()!)?.Owners.Contains(PatchId) != true,
                "Unloading removes the owned engine patches.");
            Console.WriteLine("Installed engine traffic/lifecycle checks passed; live GTA traffic was not simulated.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckCullingPolicy()
    {
        Assert(!EngineTrafficPolicy.CanCull(false, false, false, false, 100000f), "Traffic below the soft limit is retained.");
        Assert(!EngineTrafficPolicy.CanCull(true, true, false, false, 100000f), "Visible traffic ahead survives even above the soft limit.");
        Assert(!EngineTrafficPolicy.CanCull(true, false, false, false, 10000f), "Nearby traffic survives when the camera turns away.");
        Assert(!EngineTrafficPolicy.CanCull(true, false, true, false, 100000f), "Mission/persistent traffic survives.");
        Assert(!EngineTrafficPolicy.CanCull(true, false, false, true, 100000f), "Player-occupied traffic survives at any distance.");
        Assert(EngineTrafficPolicy.CanCull(true, false, false, false, 100000f), "The original population filter can still remove distant unseen ambient traffic above the cap.");
        Assert(EngineTrafficPolicy.TheirCopyWins(5, 9, true) && !EngineTrafficPolicy.TheirCopyWins(9, 5, true) &&
            !EngineTrafficPolicy.TheirCopyWins(5, 9, false) && !EngineTrafficPolicy.TheirCopyWins(0, 9, true) && !EngineTrafficPolicy.TheirCopyWins(5, 0, true),
            "Both games keep the same one shared copy: the lower player number's, and only when the other copy is replaceable.");
        Assert(EngineTrafficPolicy.IsReplaceable(false, EntityPopulationType.RandomParked) && EngineTrafficPolicy.IsReplaceable(false, EntityPopulationType.RandomAmbient) &&
            !EngineTrafficPolicy.IsReplaceable(true, EntityPopulationType.RandomParked) && !EngineTrafficPolicy.IsReplaceable(false, EntityPopulationType.Mission) &&
            !EngineTrafficPolicy.IsReplaceable(false, EntityPopulationType.Permanent),
            "Only GTA's own random vehicles are replaced; created, script and story vehicles stay with their game.");
        Assert(TrafficHandover.ShouldAdopt(1600, 100, 100f * 100f) && !TrafficHandover.ShouldAdopt(1000, 100, 100f * 100f) &&
            !TrafficHandover.ShouldAdopt(1600, 2000, 100f * 100f) && !TrafficHandover.ShouldAdopt(1600, -1, 100f * 100f) &&
            !TrafficHandover.ShouldAdopt(1600, 100, 151f * 151f) && !TrafficHandover.ShouldAdopt(1600, 100, float.NaN),
            "A copy near this player is taken over only after 1.5 s of silence while its owner keeps sending.");
        Assert(EngineTrafficPolicy.CreateNearbyCopy(149f * 149f) && !EngineTrafficPolicy.CreateNearbyCopy(151f * 151f) &&
            !EngineTrafficPolicy.CreateNearbyCopy(float.NaN), "Another player's cars within 150 m of this player are always copied; farther ones keep RageCoop's limit.");
        Assert(!EngineTrafficPolicy.CanCull(true, false, false, false, float.NaN) &&
            !EngineTrafficPolicy.CanCull(true, false, false, false, float.PositiveInfinity), "Invalid distance never authorizes a deletion.");
        Assert(EngineTrafficPolicy.SuppressSpawn(false, false, false, 100f * 100f, 50f * 50f),
            "A new car GTA put next to another player, out of this player's view, is removed before it is sent and pops in for him.");
        Assert(!EngineTrafficPolicy.SuppressSpawn(false, false, false, 60f * 60f, 50f * 50f),
            "Cars about as close to both players are kept, so traffic around players driving together is not thinned.");
        Assert(!EngineTrafficPolicy.SuppressSpawn(true, false, false, 100f * 100f, 50f * 50f), "A car this player sees is never removed.");
        Assert(!EngineTrafficPolicy.SuppressSpawn(false, false, false, 400f * 400f, 151f * 151f), "Cars farther than 150 m from the other player stay.");
        Assert(!EngineTrafficPolicy.SuppressSpawn(false, true, false, 100f * 100f, 50f * 50f) &&
            !EngineTrafficPolicy.SuppressSpawn(false, false, true, 100f * 100f, 50f * 50f), "Persistent and player-occupied cars are never removed.");
        Assert(!EngineTrafficPolicy.SuppressSpawn(false, false, false, float.NaN, 50f * 50f) &&
            !EngineTrafficPolicy.SuppressSpawn(false, false, false, 100f * 100f, float.PositiveInfinity),
            "Without another player or with an invalid distance nothing is removed.");
    }

    private static void CheckInstalledInstructions()
    {
        MethodInfo sync = EngineTrafficGuard.FindDoSync() ?? throw new InvalidOperationException("Missing installed DoSync.");
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(sync);
        Assert(EngineTrafficGuard.HasCullGates(original, sync), "Both installed soft-limit comparisons and their typed candidate locals match.");
        CodeInstruction[] rewritten = EngineTrafficGuard.ProtectTraffic(original, sync).ToArray();
        Assert(rewritten.Length == original.Count + 4 && rewritten.Count(code => code.operand is MethodInfo method &&
            method.DeclaringType == typeof(EngineTrafficGuard) && (method.Name == "CanCullPed" || method.Name == "CanCullVehicle")) == 2,
            "Exactly two protection predicates are added to DoSync.");
        Assert(rewritten.Count(code => code.operand is MethodInfo method && method.Name == "get_PopulationType") ==
            original.Count(code => code.operand is MethodInfo method && method.Name == "get_PopulationType"),
            "Original ambient population rules remain in the engine.");

        MethodInfo create = EngineTrafficGuard.FindCreateVehicle() ?? throw new InvalidOperationException("Missing installed CreateVehicle.");
        List<CodeInstruction> createOriginal = PatchProcessor.GetOriginalInstructions(create);
        Assert(EngineTrafficGuard.TryFindNearbyBlock(createOriginal, out int start, out int end), "The installed destructive two-meter replacement block matches exactly.");
        CodeInstruction[] createPatched = EngineTrafficGuard.RemoveNearbyReplacement(createOriginal).ToArray();
        Assert(createPatched.Length == createOriginal.Count - (end - start) &&
            !createPatched.Any(code => code.operand is MethodInfo method && (method.Name == "GetNearbyVehicles" || method.Name == "RemoveVehicle")),
            "Creating a remote car cannot query/delete another nearby car or remove its own incoming pool record.");
        Assert(createPatched.Count(code => code.operand is MethodInfo method && method.Name == "Delete") == 1,
            "Only replacement of this record's own prior vehicle remains.");
        bool rejected = false;
        try { EngineTrafficGuard.RemoveNearbyReplacement(new[] { new CodeInstruction(OpCodes.Ret) }).ToArray(); }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "An incompatible creation method is rejected before applying a guessed edit.");
    }

    private static void CheckCadence()
    {
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var schedule = new FullSnapshotSchedule();
            long lastOrdinary = -30;
            var fullTimes = new List<long>();
            for (int frame = 0; frame < fps * 4; frame++)
            {
                long now = frame * 1000L / fps;
                if (now - lastOrdinary < 30) { continue; }
                lastOrdinary = now;
                if (!schedule.IsDue(now, 500, false)) { continue; }
                schedule.Sent(now);
                fullTimes.Add(now);
            }
            Assert(fullTimes.Count >= 7 && fullTimes.Count <= 8 &&
                fullTimes.Zip(fullTimes.Skip(1), (first, second) => second - first).All(gap => gap >= 500 && gap <= 567),
                $"Full snapshots remain bounded and cannot starve at {fps} FPS despite skipped ordinary send frames.");
        }
        var requested = new FullSnapshotSchedule();
        requested.Sent(1000);
        Assert(!requested.IsDue(1100, 500, false) && requested.IsDue(1100, 500, true), "SendFullNextFrame can explicitly request an earlier complete snapshot.");
    }

    private static void CheckCreateGate()
    {
        SyncedVehicle incoming = VehicleRecord(870001, 71);
        Dictionary<int, SyncedVehicle> ids = PoolDictionary<SyncedVehicle>("VehiclesByID");
        const string modelPatchId = "friendslegacy.check.enginemodel";
        var modelPatch = new Harmony(modelPatchId);
        MethodInfo[] modelGetters = new[] { "IsInCdImage", "IsVehicle", "IsLoaded" }.Select(name =>
            typeof(Model).GetProperty(name, Instance)!.GetGetMethod()!).ToArray();
        MethodInfo request = typeof(Model).GetMethod("Request", Instance, null, Type.EmptyTypes, null)!;
        foreach (MethodInfo getter in modelGetters)
        {
            modelPatch.Patch(getter, prefix: new HarmonyMethod(typeof(Program), getter.Name == "get_IsLoaded" ? nameof(ModelLoaded) : nameof(ModelValid)));
        }
        modelPatch.Patch(request, prefix: new HarmonyMethod(typeof(Program), nameof(ModelRequested)));
        ids.Add(incoming.ID, incoming);
        try
        {
            Assert(!(bool)EngineTrafficGuard.FindCreateVehicle()!.Invoke(incoming, null)! && ReferenceEquals(ids[incoming.ID], incoming),
                "The patched real CreateVehicle returns safely before native calls until full model data arrives, keeping the incoming record.");
            typeof(SyncedEntity).GetProperty("LastFullSynced", Instance)!.SetValue(incoming, 1UL);
            typeof(SyncedEntity).GetProperty("Model", Instance)!.SetValue(incoming, new Model(1));
            Assert(!(bool)EngineTrafficGuard.FindCreateVehicle()!.Invoke(incoming, null)! && _modelRequests == 0,
                "The patched real CreateVehicle rejects an invalid full model before any deletion or request.");
            _modelValid = true;
            Assert(!(bool)EngineTrafficGuard.FindCreateVehicle()!.Invoke(incoming, null)! && _modelRequests == 1 && ReferenceEquals(ids[incoming.ID], incoming),
                "An unloaded valid model is requested without deleting the incoming record or native vehicles.");

            // A loaded copy would reach native creation; only the twin path may run here.
            _modelLoaded = true;
            // SHVDN's pool scan needs the game; the guard's own lookup boundary is replaced.
            modelPatch.Patch(typeof(EngineTrafficGuard).GetMethod("NearbyVehicles", Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(NearbyTwinVehicles)));
            modelPatch.Patch(typeof(EngineTrafficGuard).GetMethod("NearbyPeds", Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(NearbyTwinPeds)));
            modelPatch.Patch(typeof(EngineTrafficGuard).GetMethod("HasPlayerOccupant", Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(NoOccupants)));
            modelPatch.Patch(typeof(EngineTrafficGuard).GetMethod("PopulationOf", Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(ParkedPopulation)));
            modelPatch.Patch(typeof(EngineTrafficGuard).GetMethod("IsPersistent", Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(NotPersistent)));
            var duplicates = new List<string>();
            EngineTrafficGuard.Log = duplicates.Add;
            modelPatch.Patch(typeof(RuntimeEntityLookup).GetMethod(nameof(RuntimeEntityLookup.IsPlayerPed), Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(NotPlayer)));
            Assert(!(bool)EngineTrafficGuard.FindCreateVehicle()!.Invoke(incoming, null)! && ReferenceEquals(ids[incoming.ID], incoming) &&
                incoming.MainVehicle == null,
                "Another game's copy of a parked car or boat is not created on top of this game's own one; its record stays for later.");
            EngineTrafficGuard.FindCreateVehicle()!.Invoke(incoming, null);
            Assert(duplicates.Count == 1 && duplicates[0].Contains("duplicate vehicle") && duplicates[0].Contains("population=2") && duplicates[0].Contains("kept=this game's"),
                "Each duplicate is logged once with its model, GTA population and place, so a session shows what both games create twice.");
            var clerk = (SyncedPed)FormatterServices.GetUninitializedObject(typeof(SyncedPed));
            typeof(SyncedEntity).GetProperty("ID")!.SetValue(clerk, 870301);
            typeof(SyncedEntity).GetField("_ownerID", Instance)!.SetValue(clerk, 71);
            typeof(SyncedEntity).GetProperty("Model", Instance)!.SetValue(clerk, new Model(2));
            Assert(!(bool)EngineTrafficGuard.FindCreateCharacter()!.Invoke(clerk, null)! && clerk.MainPed == null,
                "Another game's copy of a shop clerk or scenario NPC is not created on top of this game's own one.");
            Assert(duplicates.Count == 2 && duplicates[1].Contains("duplicate NPC") && duplicates[1].Contains("kept=each player's own"),
                "Duplicate NPCs are logged as each player's own.");
        }
        finally
        {
            EngineTrafficGuard.Log = null;
            _modelLoaded = false;
            ids.Remove(incoming.ID);
            modelPatch.UnpatchAll(modelPatchId);
        }
    }

    private static bool _modelLoaded;
    private static bool NearbyTwinVehicles(ref Vehicle[] __result) { __result = new[] { NativeEntity<Vehicle>(902101) }; return false; }
    private static bool NearbyTwinPeds(ref Ped[] __result) { __result = new[] { NativeEntity<Ped>(902201) }; return false; }
    private static bool NoOccupants(ref bool __result) { __result = false; return false; }
    private static bool ParkedPopulation(ref EntityPopulationType __result) { __result = EntityPopulationType.RandomParked; return false; }
    private static bool NotPersistent(ref bool __result) { __result = false; return false; }
    private static bool NotPlayer(ref bool __result, ref bool known) { known = true; __result = false; return false; }

    private static bool ModelValid(ref bool __result) { __result = _modelValid; return false; }
    private static bool ModelLoaded(ref bool __result) { __result = _modelLoaded; return false; }
    private static bool ModelRequested() { _modelRequests++; return false; }

    private static void CheckPoolRecreation()
    {
        var record = VehicleRecord(870002, 71);
        var unrelated = VehicleRecord(870003, 72);
        Dictionary<int, SyncedVehicle> handles = PoolDictionary<SyncedVehicle>("VehiclesByHandle");
        Dictionary<int, SyncedVehicle> ids = PoolDictionary<SyncedVehicle>("VehiclesByID");
        const int oldHandle = 901001, newHandle = 901002, unrelatedHandle = 901003;
        handles.Add(oldHandle, record);
        handles.Add(unrelatedHandle, unrelated);
        typeof(SyncedVehicle).GetProperty("MainVehicle", Instance)!.SetValue(record, NativeEntity<Vehicle>(newHandle));
        try
        {
            EngineTrafficGuard.FindAdd(typeof(SyncedVehicle))!.Invoke(null, new object[] { record });
            Assert(!handles.ContainsKey(oldHandle) && ReferenceEquals(handles[newHandle], record) && ReferenceEquals(handles[unrelatedHandle], unrelated),
                "Actual EntityPool.Add remaps a recreated car and preserves unrelated handle mappings.");
            handles.Add(oldHandle, unrelated);
            EngineTrafficGuard.FindAdd(typeof(SyncedVehicle))!.Invoke(null, new object[] { record });
            Assert(ReferenceEquals(handles[oldHandle], unrelated), "Reusing the former handle for unrelated traffic remains valid.");
        }
        finally
        {
            handles.Remove(oldHandle);
            handles.Remove(newHandle);
            handles.Remove(unrelatedHandle);
            ids.Remove(record.ID);
        }

        var ped = (SyncedPed)FormatterServices.GetUninitializedObject(typeof(SyncedPed));
        typeof(SyncedEntity).GetProperty("ID")!.SetValue(ped, 870004);
        typeof(SyncedEntity).GetField("_ownerID", Instance)!.SetValue(ped, 71);
        Dictionary<int, SyncedPed> pedHandles = PoolDictionary<SyncedPed>("PedsByHandle");
        Dictionary<int, SyncedPed> pedIds = PoolDictionary<SyncedPed>("PedsByID");
        pedHandles.Add(oldHandle, ped);
        typeof(SyncedPed).GetProperty("MainPed", Instance)!.SetValue(ped, NativeEntity<Ped>(newHandle));
        try
        {
            EngineTrafficGuard.FindAdd(typeof(SyncedPed))!.Invoke(null, new object[] { ped });
            Assert(!pedHandles.ContainsKey(oldHandle) && ReferenceEquals(pedHandles[newHandle], ped), "Actual EntityPool.Add also remaps a recreated ped.");
        }
        finally
        {
            pedHandles.Remove(oldHandle);
            pedHandles.Remove(newHandle);
            pedIds.Remove(ped.ID);
        }
    }

    private static void CheckSkippedSend()
    {
        SyncedVehicle vehicle = VehicleRecord(870005, 71);
        Type network = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.Networking", true)!;
        FieldInfo interval = network.GetField("SyncInterval", Static)!;
        int previous = (int)interval.GetValue(null)!;
        typeof(SyncedEntity).GetProperty("LastSentStopWatch", Instance)!.SetValue(vehicle, Stopwatch.StartNew());
        vehicle.SendFullNextFrame = true;
        try
        {
            interval.SetValue(null, int.MaxValue);
            EngineTrafficGuard.FindSend(typeof(SyncedVehicle))!.Invoke(null, new object[] { vehicle, true });
            Assert(vehicle.SendFullNextFrame, "A skipped real SendVehicle preserves an explicit full-snapshot request and returns before native access.");
        }
        finally { interval.SetValue(null, previous); }
    }

    private static void CheckNearbyCopies()
    {
        Assembly client = typeof(SyncedVehicle).Assembly;
        Type main = client.GetType("RageCoop.Client.Main", true)!;
        FieldInfo positionField = main.GetField("PlayerPosition", Static)!, localField = main.GetField("LocalPlayerID", Static)!;
        object previousLocal = localField.GetValue(null)!;
        Dictionary<int, SyncedVehicle> vehicles = PoolDictionary<SyncedVehicle>("VehiclesByID");
        Dictionary<int, SyncedPed> peds = PoolDictionary<SyncedPed>("PedsByID");
        int[] vehicleIds = { 870103, 870104 }, pedIds = { 870203, 870204, 870205 };
        object Packet(string handler, int id, Vector3 position, byte speed = 0)
        {
            Type type = EngineTrafficGuard.FindReceive(handler)!.GetParameters()[0].ParameterType;
            object packet = Activator.CreateInstance(type, true)!;
            type.GetProperty("ID")!.SetValue(packet, id);
            type.GetProperty("OwnerID")!.SetValue(packet, 72);
            type.GetProperty("Position")!.SetValue(packet, position);
            if (handler == "PedSync") type.GetProperty("Speed")!.SetValue(packet, speed);
            return packet;
        }
        // RageCoop's handlers resolve the owner through its roster, which needs the
        // game; the installed prefix runs here against the real entity pool.
        void Prefix(string name, object packet) => typeof(EngineTrafficGuard).GetMethod(name, Static)!.Invoke(null, new[] { packet, (object)true });
        try
        {
            positionField.SetValue(null, Vector3.Zero);
            localField.SetValue(null, 1);
            Prefix("BeforeReceiveVehicle", Packet("VehicleSync", vehicleIds[0], new Vector3(1000, 0, 0)));
            Assert(!vehicles.ContainsKey(vehicleIds[0]), "Beyond 150 m another player's car keeps RageCoop's per-owner limit.");
            Prefix("BeforeReceiveVehicle", Packet("VehicleSync", vehicleIds[1], new Vector3(40, 0, 0)));
            Assert(vehicles.TryGetValue(vehicleIds[1], out SyncedVehicle? copy) && copy.ID == vehicleIds[1],
                "Another player's car near this player gets its real pool record before RageCoop's per-owner limit is consulted.");
            Prefix("BeforeReceivePed", Packet("PedSync", pedIds[0], new Vector3(30, 0, 0), speed: 1));
            Assert(!peds.ContainsKey(pedIds[0]), "Pedestrians near this player keep RageCoop's limit.");
            Prefix("BeforeReceivePed", Packet("PedSync", pedIds[1], new Vector3(1000, 0, 0), speed: 4));
            Assert(!peds.ContainsKey(pedIds[1]), "Far NPC drivers keep RageCoop's limit.");
            Prefix("BeforeReceivePed", Packet("PedSync", pedIds[2], new Vector3(30, 0, 0), speed: 4));
            Assert(peds.TryGetValue(pedIds[2], out SyncedPed? driver) && driver.ID == pedIds[2],
                "The NPC driving another player's car near this player gets its real pool record, so the car is not driverless.");
        }
        finally
        {
            localField.SetValue(null, previousLocal);
            foreach (int id in vehicleIds) vehicles.Remove(id);
            foreach (int id in pedIds) peds.Remove(id);
        }
    }

    private static readonly Dictionary<int, long> HandoverSilences = new Dictionary<int, long>();
    private static readonly Dictionary<int, Vector3> HandoverPositions = new Dictionary<int, Vector3>();
    private static readonly List<int> HandoverReleases = new List<int>();

    private static void CheckHandover()
    {
        Assembly client = typeof(SyncedVehicle).Assembly;
        FieldInfo localField = client.GetType("RageCoop.Client.Main", true)!.GetField("LocalPlayerID", Static)!;
        object previousLocal = localField.GetValue(null)!;
        Dictionary<int, SyncedVehicle> vehicles = PoolDictionary<SyncedVehicle>("VehiclesByID"), vehicleHandles = PoolDictionary<SyncedVehicle>("VehiclesByHandle");
        Dictionary<int, SyncedPed> peds = PoolDictionary<SyncedPed>("PedsByID"), pedHandles = PoolDictionary<SyncedPed>("PedsByHandle");
        const string patchId = "friendslegacy.check.handover";
        var patch = new Harmony(patchId);
        foreach (string name in new[] { "Silence", "LocalPosition", "Exists", "PositionOf", "AddressOf", "HasPlayerOccupant", "ReleasePed", "ReleaseVehicle" })
            patch.Patch(typeof(TrafficHandover).GetMethod(name, Static)!, prefix: new HarmonyMethod(typeof(Program), "Handover" + name));
        SyncedPed AddPed(int id, int owner, int handle, long silence, Vector3 at)
        {
            SyncedPed ped = PedRecord(id, owner);
            typeof(SyncedPed).GetProperty("MainPed")!.SetValue(ped, NativeEntity<Ped>(handle));
            peds[id] = ped; pedHandles[handle] = ped; HandoverSilences[id] = silence; HandoverPositions[handle] = at;
            return ped;
        }
        SyncedVehicle AddCar(int id, int handle, long silence, Vector3 at)
        {
            SyncedVehicle vehicle = VehicleRecord(id, 72);
            typeof(SyncedVehicle).GetProperty("MainVehicle")!.SetValue(vehicle, NativeEntity<Vehicle>(handle));
            vehicles[id] = vehicle; vehicleHandles[handle] = vehicle; HandoverSilences[id] = silence; HandoverPositions[handle] = at;
            return vehicle;
        }
        try
        {
            localField.SetValue(null, 1);
            TrafficHandover.Reset();
            SyncedPed owner = AddPed(72, 72, 903001, 100, new Vector3(500, 0, 0));
            AddPed(873001, 72, 903002, 2000, new Vector3(20, 0, 0));
            AddCar(873101, 903101, 2000, new Vector3(20, 0, 0));
            AddCar(873102, 903102, 2000, new Vector3(1000, 0, 0));
            AddCar(873103, 903103, 2000, new Vector3(30, 0, 0));
            typeof(SyncedPed).GetProperty("VehicleID", Instance)!.SetValue(owner, 873103);
            AddCar(873104, 903104, 400, new Vector3(25, 0, 0));
            TrafficHandover.Tick(Stopwatch.GetTimestamp());
            Assert(!peds.ContainsKey(873001) && !pedHandles.ContainsKey(903002) && !vehicles.ContainsKey(873101) && !vehicleHandles.ContainsKey(903101) &&
                HandoverReleases.OrderBy(handle => handle).SequenceEqual(new[] { 903002, 903101 }),
                "The real pool hands over a silent NPC and car near this player, without deleting them, while their owner keeps sending.");
            Assert(vehicles.ContainsKey(873102) && vehicles.ContainsKey(873103) && vehicles.ContainsKey(873104) && peds.ContainsKey(72),
                "Far copies, a car another player drives and a copy that is still updated stay with their owner.");
            Assert(TrafficHandover.WasAdopted(903101, new IntPtr(903101)) && !TrafficHandover.WasAdopted(903102, new IntPtr(903102)),
                "A taken-over car stays road traffic for the ghost-traffic mode.");
            HandoverReleases.Clear(); HandoverSilences[72] = 2000; HandoverSilences[873104] = 2000; TrafficHandover.Reset();
            TrafficHandover.Tick(Stopwatch.GetTimestamp());
            Assert(HandoverReleases.Count == 0 && vehicles.ContainsKey(873104),
                "When the owner itself has gone silent (paused or connection lost), nothing is taken over.");
        }
        finally
        {
            localField.SetValue(null, previousLocal);
            patch.UnpatchAll(patchId);
            foreach (int id in new[] { 72, 873001 }) peds.Remove(id);
            foreach (int handle in new[] { 903001, 903002 }) pedHandles.Remove(handle);
            foreach (int id in new[] { 873101, 873102, 873103, 873104 }) vehicles.Remove(id);
            foreach (int handle in new[] { 903101, 903102, 903103, 903104 }) vehicleHandles.Remove(handle);
            TrafficHandover.Reset();
        }
    }

    private static bool HandoverSilence(SyncedEntity entity, ref long __result) { __result = HandoverSilences.TryGetValue(entity.ID, out long value) ? value : -1; return false; }
    private static bool HandoverLocalPosition(ref Vector3 __result) { __result = Vector3.Zero; return false; }
    private static bool HandoverExists(ref bool __result) { __result = true; return false; }
    private static bool HandoverPositionOf(Entity entity, ref Vector3 __result) { __result = HandoverPositions.TryGetValue(entity.Handle, out Vector3 at) ? at : Vector3.Zero; return false; }
    private static bool HandoverAddressOf(Entity entity, ref IntPtr __result) { __result = new IntPtr(entity.Handle); return false; }
    private static bool HandoverHasPlayerOccupant(ref bool __result) { __result = false; return false; }
    private static bool HandoverReleasePed(Ped ped) { HandoverReleases.Add(ped.Handle); return false; }
    private static bool HandoverReleaseVehicle(Vehicle vehicle) { HandoverReleases.Add(vehicle.Handle); return false; }

    private static SyncedPed PedRecord(int id, int owner)
    {
        var ped = (SyncedPed)FormatterServices.GetUninitializedObject(typeof(SyncedPed));
        typeof(SyncedEntity).GetProperty("ID")!.SetValue(ped, id);
        typeof(SyncedEntity).GetField("_ownerID", Instance)!.SetValue(ped, owner);
        return ped;
    }

    private static SyncedVehicle VehicleRecord(int id, int owner)
    {
        var vehicle = (SyncedVehicle)FormatterServices.GetUninitializedObject(typeof(SyncedVehicle));
        typeof(SyncedEntity).GetProperty("ID")!.SetValue(vehicle, id);
        typeof(SyncedEntity).GetField("_ownerID", Instance)!.SetValue(vehicle, owner);
        return vehicle;
    }

    private static Dictionary<int, T> PoolDictionary<T>(string name) =>
        (Dictionary<int, T>)typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.EntityPool", true)!.GetField(name, Static)!.GetValue(null)!;

    private static T NativeEntity<T>(int handle) where T : Entity =>
        (T)typeof(T).GetConstructor(Instance, null, new[] { typeof(int) }, null)!.Invoke(new object[] { handle });

    private static void Assert(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        Console.WriteLine("PASS " + message);
    }
}
