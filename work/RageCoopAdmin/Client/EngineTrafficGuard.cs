using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using GTA;
using GTA.Math;
using HarmonyLib;
using RageCoop.Client;

namespace RageCoopAdmin.Client;

// Correct the installed engine's destructive traffic filter and lifecycle gates.
// Keep discovery, interpolation, population rules and packet formats in RageCoop.
internal static class EngineTrafficGuard
{
    private const string PatchId = "friendslegacy.ragecoop.enginetraffic";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type? Pool = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.EntityPool", false);
    private static readonly Type? Network = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.Networking", false);
    private static readonly Dictionary<int, SyncedVehicle>? VehicleHandles = PoolField<Dictionary<int, SyncedVehicle>>("VehiclesByHandle");
    private static readonly Dictionary<int, SyncedPed>? PedHandles = PoolField<Dictionary<int, SyncedPed>>("PedsByHandle");
    private static readonly object? VehicleLock = PoolField<object>("VehiclesLock");
    private static readonly object? PedLock = PoolField<object>("PedsLock");
    private static readonly Func<SyncedEntity, Model>? ReadModel = BindEntityGetter<Model>("Model");
    private static readonly Func<SyncedEntity, Stopwatch>? ReadLastSent = BindEntityGetter<Stopwatch>("LastSentStopWatch");
    private static readonly Func<SyncedEntity, Vector3>? ReadPosition = BindEntityGetter<Vector3>("Position");
    private static readonly PropertyInfo? PedVehicle = typeof(SyncedPed).GetProperty("VehicleID", Instance);
    private static readonly MethodInfo? RemoveVehicleRecord = Pool?.GetMethod("RemoveVehicle", Static, null, new[] { typeof(int), typeof(string) }, null);
    private static readonly MethodInfo? RemovePedRecord = Pool?.GetMethod("RemovePed", Static, null, new[] { typeof(int), typeof(string) }, null);
    private static readonly Dictionary<int, SyncedPed>? Peds = PoolField<Dictionary<int, SyncedPed>>("PedsByID");
    private static readonly HashSet<int> DuplicateVehicles = new HashSet<int>(), DuplicatePeds = new HashSet<int>();
    private static readonly List<Vehicle> LosingVehicles = new List<Vehicle>();
    private static readonly HashSet<long> LoggedDuplicates = new HashSet<long>();
    private static readonly FieldInfo? LocalPlayer = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.Main", false)?.GetField("LocalPlayerID", Static);
    internal static Action<string>? Log;
    private static readonly FieldInfo? SyncInterval = Network?.GetField("SyncInterval", Static);
    private static readonly FieldInfo? PlayerPosition = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.Main", false)?.GetField("PlayerPosition", Static);
    private static readonly MethodInfo? AddVehicle = Pool?.GetNestedType("ThreadSafe")?.GetMethod("Add", Static, null, new[] { typeof(SyncedVehicle) }, null);
    private static readonly MethodInfo? AddPed = Pool?.GetNestedType("ThreadSafe")?.GetMethod("Add", Static, null, new[] { typeof(SyncedPed) }, null);
    private static readonly ConstructorInfo? NewVehicle = typeof(SyncedVehicle).GetConstructor(Instance, null, new[] { typeof(int) }, null);
    private static readonly ConstructorInfo? NewPed = typeof(SyncedPed).GetConstructor(Instance, null, new[] { typeof(int) }, null);
    private static PacketReader? _vehiclePacket, _pedPacket;
    private static ConditionalWeakTable<SyncedEntity, FullSnapshotSchedule> _fullSchedules = new ConditionalWeakTable<SyncedEntity, FullSnapshotSchedule>();
    private static readonly List<MethodInfo> Targets = new List<MethodInfo>();
    private static Harmony? _harmony;

    internal static MethodInfo? FindDoSync() => Pool?.GetMethod("DoSync", Static, null, Type.EmptyTypes, null);
    internal static MethodInfo? FindCreateVehicle() => typeof(SyncedVehicle).GetMethod("CreateVehicle", Instance, null, Type.EmptyTypes, null);
    internal static MethodInfo? FindCreateCharacter() => typeof(SyncedPed).GetMethod("CreateCharacter", Instance, null, Type.EmptyTypes, null);
    internal static MethodInfo? FindAdd(Type entityType) => Pool?.GetMethod("Add", Static, null, new[] { entityType }, null);
    internal static MethodInfo? FindReceive(string name) => Network?.GetMethods(Static | BindingFlags.DeclaredOnly)
        .SingleOrDefault(method => method.Name == name && method.ReturnType == typeof(void) && method.GetParameters().Length == 1);

    internal static MethodInfo? FindSend(Type entityType) => Network?.GetMethod(entityType == typeof(SyncedPed) ? "SendPed" : "SendVehicle",
        Static, null, new[] { entityType, typeof(bool) }, null);

    internal static bool Install()
    {
        if (_harmony != null) { return true; }
        MethodInfo? sync = FindDoSync(), create = FindCreateVehicle(), createPed = FindCreateCharacter();
        MethodInfo? addPed = FindAdd(typeof(SyncedPed)), addVehicle = FindAdd(typeof(SyncedVehicle));
        MethodInfo? sendPed = FindSend(typeof(SyncedPed)), sendVehicle = FindSend(typeof(SyncedVehicle));
        MethodInfo? receivePed = FindReceive("PedSync"), receiveVehicle = FindReceive("VehicleSync");
        PacketReader? pedPacket = PacketReader.Create(receivePed, speed: true), vehiclePacket = PacketReader.Create(receiveVehicle, speed: false);
        if (sync == null || create?.ReturnType != typeof(bool) || addPed == null || addVehicle == null ||
            sendPed == null || sendVehicle == null || VehicleHandles == null || PedHandles == null || !RuntimeEntityLookup.CanListPlayers ||
            VehicleLock == null || PedLock == null || ReadModel == null || ReadLastSent == null || SyncInterval?.FieldType != typeof(int) ||
            !HasCullGates(PatchProcessor.GetOriginalInstructions(sync), sync) ||
            !TryFindNearbyBlock(PatchProcessor.GetOriginalInstructions(create), out _, out _) ||
            !HasSendGate(sendPed) || !HasSendGate(sendVehicle) || pedPacket == null || vehiclePacket == null ||
            PlayerPosition?.FieldType != typeof(Vector3) || AddVehicle == null || AddPed == null || NewVehicle == null || NewPed == null ||
            createPed?.ReturnType != typeof(bool) || ReadPosition == null || PedVehicle?.PropertyType != typeof(int) || Peds == null ||
            RemoveVehicleRecord == null || RemovePedRecord == null || LocalPlayer?.FieldType != typeof(int))
        {
            return false;
        }

        var harmony = new Harmony(PatchId);
        try
        {
            Patch(harmony, sync, postfix: nameof(RemoveDuplicates), transpiler: nameof(ProtectTraffic));
            Patch(harmony, create, prefix: nameof(BeforeCreateVehicle), transpiler: nameof(RemoveNearbyReplacement));
            Patch(harmony, createPed!, prefix: nameof(BeforeCreateCharacter));
            Patch(harmony, addPed, prefix: nameof(BeforeAddPed));
            Patch(harmony, addVehicle, prefix: nameof(BeforeAddVehicle));
            Patch(harmony, sendPed, prefix: nameof(BeforeSendPed), postfix: nameof(AfterSendPed));
            Patch(harmony, sendVehicle, prefix: nameof(BeforeSendVehicle), postfix: nameof(AfterSendVehicle));
            _pedPacket = pedPacket; _vehiclePacket = vehiclePacket;
            Patch(harmony, receivePed!, prefix: nameof(BeforeReceivePed));
            Patch(harmony, receiveVehicle!, prefix: nameof(BeforeReceiveVehicle));
        }
        catch
        {
            foreach (MethodInfo target in Targets) { harmony.Unpatch(target, HarmonyPatchType.All, PatchId); }
            Targets.Clear();
            throw;
        }
        _harmony = harmony;
        return true;
    }

    internal static void Uninstall()
    {
        if (_harmony != null)
        {
            foreach (MethodInfo target in Targets) { _harmony.Unpatch(target, HarmonyPatchType.All, PatchId); }
        }
        Targets.Clear();
        _harmony = null;
        _pedPacket = _vehiclePacket = null;
        DuplicateVehicles.Clear();
        DuplicatePeds.Clear();
        LosingVehicles.Clear();
        LoggedDuplicates.Clear();
        Log = null;
        _fullSchedules = new ConditionalWeakTable<SyncedEntity, FullSnapshotSchedule>();
    }

    private static void Patch(Harmony harmony, MethodInfo target, string? prefix = null, string? postfix = null, string? transpiler = null)
    {
        Targets.Add(target);
        HarmonyMethod? Method(string? name) => name == null ? null : new HarmonyMethod(typeof(EngineTrafficGuard), name);
        harmony.Patch(target, prefix: Method(prefix), postfix: Method(postfix), transpiler: Method(transpiler));
    }

    internal static bool HasCullGates(IList<CodeInstruction> instructions, MethodBase target) =>
        FindCullGate(instructions, target, "WorldPedSoftLimit", typeof(Ped), out _) >= 0 &&
        FindCullGate(instructions, target, "WorldVehicleSoftLimit", typeof(Vehicle), out _) >= 0 &&
        FindPedList(instructions) >= 0;

    // DoSync один раз кладёт World.GetAllPeds() в EntityPool.allPeds и ищет в нём новых NPC для отправки
    internal static int FindPedList(IList<CodeInstruction> instructions)
    {
        int[] matches = Enumerable.Range(0, Math.Max(0, instructions.Count - 1)).Where(index =>
            instructions[index].operand is MethodInfo method && method.DeclaringType == typeof(World) && method.Name == "GetAllPeds" &&
            instructions[index + 1].opcode == OpCodes.Stsfld && instructions[index + 1].operand is FieldInfo field &&
            field.DeclaringType == Pool && field.Name == "allPeds").ToArray();
        return matches.Length == 1 ? matches[0] : -1;
    }

    private static int FindCullGate(IList<CodeInstruction> instructions, MethodBase target, string setting, Type candidateType, out int candidate)
    {
        candidate = -1;
        if (instructions.Count < 2) { return -1; }
        int[] matches = Enumerable.Range(0, instructions.Count - 1).Where(index =>
            instructions[index].operand is MethodInfo getter && getter.DeclaringType?.FullName == "RageCoop.Client.Settings" &&
            getter.Name == "get_" + setting && instructions[index + 1].opcode == OpCodes.Cgt).ToArray();
        if (matches.Length != 1) { return -1; }
        int comparison = matches[0] + 1;
        for (int index = comparison + 1; index < Math.Min(comparison + 12, instructions.Count); index++)
        {
            if (instructions[index].operand is not MethodInfo getter || getter.DeclaringType != typeof(Entity) || getter.Name != "get_PopulationType") { continue; }
            if (!instructions[index - 1].IsLdloc()) { return -1; }
            candidate = instructions[index - 1].LocalIndex();
            IList<LocalVariableInfo>? locals = target.GetMethodBody()?.LocalVariables;
            return locals != null && candidate >= 0 && candidate < locals.Count && locals[candidate].LocalType == candidateType ? comparison : -1;
        }
        return -1;
    }

    internal static IEnumerable<CodeInstruction> ProtectTraffic(IEnumerable<CodeInstruction> original, MethodBase __originalMethod)
    {
        List<CodeInstruction> instructions = original.Select(code => new CodeInstruction(code)).ToList();
        int ped = FindCullGate(instructions, __originalMethod, "WorldPedSoftLimit", typeof(Ped), out int pedLocal);
        int vehicle = FindCullGate(instructions, __originalMethod, "WorldVehicleSoftLimit", typeof(Vehicle), out int vehicleLocal);
        int list = FindPedList(instructions);
        if (ped < 0 || vehicle < 0 || list < 0) { throw new InvalidOperationException("Unsupported RageCoop traffic culling gates."); }
        foreach (var gate in new[] { (Index: ped, Local: pedLocal, Method: nameof(CanCullPed)), (Index: vehicle, Local: vehicleLocal, Method: nameof(CanCullVehicle)) }
            .OrderByDescending(gate => gate.Index))
        {
            instructions.InsertRange(gate.Index + 1, new[] { CodeInstruction.LoadLocal(gate.Local), CodeInstruction.Call(typeof(EngineTrafficGuard), gate.Method) });
        }
        // список стоит раньше обеих проверок, вставки его не сдвинули
        if (list > Math.Min(ped, vehicle)) { throw new InvalidOperationException("Unsupported RageCoop sync order."); }
        instructions.Insert(list + 1, CodeInstruction.Call(typeof(LocalPeds), nameof(LocalPeds.Without)));
        return instructions;
    }

    // Both gates run once for every entity GTA has just created, under the
    // pool monitor, before RageCoop tracks and sends it.
    private static bool CanCullPed(bool overLimit, Ped ped)
    {
        if (!ped.Exists()) { return false; }
        RemoveRemotePedTwins(ped);
        return overLimit && EngineTrafficPolicy.CanCull(true, ped.IsOnScreen, ped.IsPersistent, IsPlayer(ped), NearestPlayerDistanceSquared(ped.Position));
    }

    // Runs once for each car GTA has just created, before RageCoop sends it.
    // The engine's own population check still limits deletion to ambient and
    // parked traffic.
    private static bool CanCullVehicle(bool overLimit, Vehicle vehicle)
    {
        if (!vehicle.Exists()) { return false; }
        RemoveRemoteVehicleTwins(vehicle);
        Vector3 position = vehicle.Position;
        float remote = NearestRemotePlayerDistanceSquared(position);
        if (!overLimit && !EngineTrafficPolicy.MayPopIn(remote)) { return false; }
        float local = position.DistanceToSquared(Game.Player.Character.Position);
        bool onScreen = vehicle.IsOnScreen, persistent = vehicle.IsPersistent;
        bool playerOccupant = vehicle.Occupants.Any(IsPlayer);
        return EngineTrafficPolicy.SuppressSpawn(onScreen, persistent, playerOccupant, local, remote) ||
            EngineTrafficPolicy.CanCull(overLimit, onScreen, persistent, playerOccupant, Math.Min(local, remote));
    }

    private static bool IsPlayer(Ped ped) => RuntimeEntityLookup.IsPlayerPed(ped, out _);

    private static float NearestPlayerDistanceSquared(Vector3 position) =>
        Math.Min(position.DistanceToSquared(Game.Player.Character.Position), NearestRemotePlayerDistanceSquared(position));

    private static float NearestRemotePlayerDistanceSquared(Vector3 position)
    {
        float distance = float.PositiveInfinity;
        foreach (Vector3 player in RuntimeEntityLookup.RemotePlayerPositions())
        {
            distance = Math.Min(distance, position.DistanceToSquared(player));
        }
        return distance;
    }

    // Network thread, after EngineNetworkGuard took the pool monitors. A record
    // that already exists keeps RageCoop's own path; a missing one near this
    // player is added so the original skips its per-owner limit.
    private static void BeforeReceiveVehicle(object __0, bool __runOriginal)
    {
        PacketReader? packet = _vehiclePacket;
        if (!__runOriginal || packet == null) { return; }
        int id = packet.Id(__0);
        if (id == 0 || RuntimeEntityLookup.GetVehicleByID(id) != null || !IsNearLocalPlayer(packet.Position(__0))) { return; }
        AddVehicle!.Invoke(null, new[] { NewVehicle!.Invoke(new object[] { id }) });
    }

    private static void BeforeReceivePed(object __0, bool __runOriginal)
    {
        PacketReader? packet = _pedPacket;
        if (!__runOriginal || packet == null) { return; }
        int id = packet.Id(__0), owner = packet.Owner(__0);
        if (!EngineTrafficPolicy.CopyNearbyPed(packet.Speed!(__0), owner, PassengerPopulation.RidingWith)) { return; }
        if (id == 0 || id == owner || RuntimeEntityLookup.GetPedByID(id) != null || !IsNearLocalPlayer(packet.Position(__0))) { return; }
        AddPed!.Invoke(null, new[] { NewPed!.Invoke(new object[] { id }) });
    }

    private static bool IsNearLocalPlayer(Vector3 position) =>
        EngineTrafficPolicy.CreateNearbyCopy(position.DistanceToSquared((Vector3)PlayerPosition!.GetValue(null)!));

    private sealed class PacketReader
    {
        internal Func<object, int> Id = null!, Owner = null!;
        internal Func<object, Vector3> Position = null!;
        internal Func<object, byte>? Speed;

        internal static PacketReader? Create(MethodInfo? receiver, bool speed)
        {
            Type? type = receiver?.GetParameters().Single().ParameterType;
            if (type == null) { return null; }
            Func<object, T>? Read<T>(string name)
            {
                PropertyInfo? property = type.GetProperty(name, Instance);
                if (property?.PropertyType != typeof(T) || property.GetGetMethod() == null) { return null; }
                ParameterExpression packet = Expression.Parameter(typeof(object), "packet");
                return Expression.Lambda<Func<object, T>>(Expression.Property(Expression.Convert(packet, type), property), packet).Compile();
            }
            Func<object, int>? id = Read<int>("ID"), owner = Read<int>("OwnerID");
            Func<object, Vector3>? position = Read<Vector3>("Position");
            Func<object, byte>? moving = speed ? Read<byte>("Speed") : null;
            if (id == null || owner == null || position == null || (speed && moving == null)) { return null; }
            return new PacketReader { Id = id, Owner = owner, Position = position, Speed = moving };
        }
    }

    private static bool BeforeCreateVehicle(SyncedVehicle __instance, ref bool __result)
    {
        __result = false;
        if (__instance.LastFullSynced == 0) { return false; }
        Model model = ReadModel!(__instance);
        if (!model.IsInCdImage || !model.IsVehicle) { return false; }
        if (!model.IsLoaded)
        {
            model.Request();
            return false;
        }
        if (HasPlayerInside(__instance.ID)) { return true; }
        Vector3 position = ReadPosition!(__instance);
        Vehicle? twin = LocalVehicleTwin(model, position);
        if (twin == null) { return true; }
        EntityPopulationType population = PopulationOf(twin);
        // One shared copy: the lower-numbered player's. Both games apply the same rule.
        if (EngineTrafficPolicy.TheirCopyWins(__instance.OwnerID, LocalPlayerId(), EngineTrafficPolicy.IsReplaceable(IsPersistent(twin), population)))
        {
            ReplaceLocalVehicle(twin);
            LogDuplicate(__instance.ID, "vehicle", model.Hash, population, position, "other player's");
            return true;
        }
        LogDuplicate(__instance.ID, "vehicle", model.Hash, population, position, "this game's");
        return false;
    }

    // NPCs stay one per game: a shop's own script owns its clerk.
    private static bool BeforeCreateCharacter(SyncedPed __instance, ref bool __result)
    {
        if (__instance.IsPlayer || (__instance.MainPed != null && __instance.MainPed.Exists())) { return true; }
        Model model = ReadModel!(__instance);
        if (!model.IsLoaded) { return true; }
        Vector3 position = ReadPosition!(__instance);
        Ped? twin = LocalPedTwin(model, position);
        if (twin == null) { return true; }
        LogDuplicate(PedKey(__instance.ID), "NPC", model.Hash, PopulationOf(twin), position, "each player's own");
        __result = false;
        return false;
    }

    private static Vehicle? LocalVehicleTwin(Model model, Vector3 position)
    {
        foreach (Vehicle twin in NearbyVehicles(position, model))
        {
            SyncedVehicle? record = RuntimeEntityLookup.GetVehicleByHandle(twin.Handle);
            if ((record == null || record.IsLocal) && !HasPlayerOccupant(twin)) { return twin; }
        }
        return null;
    }

    private static Ped? LocalPedTwin(Model model, Vector3 position)
    {
        foreach (Ped twin in NearbyPeds(position, model))
        {
            SyncedPed? record = RuntimeEntityLookup.GetPedByHandle(twin.Handle);
            if ((record == null || record.IsLocal) && !IsPlayer(twin)) { return twin; }
        }
        return null;
    }

    // Runs inside the sync pass: deleting this game's own copy also removes it
    // from RageCoop and tells the server; the pass works on a snapshot.
    private static void ReplaceLocalVehicle(Vehicle twin)
    {
        SyncedVehicle? record = RuntimeEntityLookup.GetVehicleByHandle(twin.Handle);
        if (record is { IsLocal: true }) { RemoveVehicleRecord!.Invoke(null, new object[] { record.ID, "Duplicate" }); }
        else if (record == null) { twin.Delete(); }
    }

    // A twin met when GTA has just created this game's copy. The loser is
    // removed after the sync pass, which must not meet its deleted object as a
    // new one. A removed copy of the other game's is recreated by its next
    // packet, and the creation gate then applies the same rule.
    private static void RemoveRemoteVehicleTwins(Vehicle vehicle)
    {
        foreach (Vehicle twin in NearbyVehicles(vehicle.Position, vehicle.Model))
        {
            if (twin.Handle == vehicle.Handle) { continue; }
            SyncedVehicle? record = RuntimeEntityLookup.GetVehicleByHandle(twin.Handle);
            if (record == null || record.IsLocal || HasPlayerInside(record.ID) || HasPlayerOccupant(twin)) { continue; }
            EntityPopulationType population = PopulationOf(vehicle);
            if (EngineTrafficPolicy.TheirCopyWins(record.OwnerID, LocalPlayerId(), EngineTrafficPolicy.IsReplaceable(IsPersistent(vehicle), population)))
            {
                LosingVehicles.Add(vehicle);
                LogDuplicate(record.ID, "vehicle", vehicle.Model.Hash, population, vehicle.Position, "other player's");
                return;
            }
            DuplicateVehicles.Add(record.ID);
            LogDuplicate(record.ID, "vehicle", vehicle.Model.Hash, population, vehicle.Position, "this game's");
        }
    }

    private static void RemoveRemotePedTwins(Ped ped)
    {
        foreach (Ped twin in NearbyPeds(ped.Position, ped.Model))
        {
            if (twin.Handle == ped.Handle) { continue; }
            SyncedPed? record = RuntimeEntityLookup.GetPedByHandle(twin.Handle);
            if (record == null || record.IsLocal || record.IsPlayer) { continue; }
            DuplicatePeds.Add(record.ID);
            LogDuplicate(PedKey(record.ID), "NPC", ped.Model.Hash, PopulationOf(ped), ped.Position, "each player's own");
        }
    }

    private static void RemoveDuplicates()
    {
        if (DuplicateVehicles.Count != 0 || LosingVehicles.Count != 0)
        {
            lock (VehicleLock!)
            {
                foreach (int id in DuplicateVehicles)
                    if (RuntimeEntityLookup.GetVehicleByID(id) is { IsLocal: false }) { RemoveVehicleRecord!.Invoke(null, new object[] { id, "Duplicate" }); }
                foreach (Vehicle vehicle in LosingVehicles)
                    if (vehicle.Exists()) { ReplaceLocalVehicle(vehicle); }
            }
            DuplicateVehicles.Clear();
            LosingVehicles.Clear();
        }
        if (DuplicatePeds.Count != 0)
        {
            lock (PedLock!)
            {
                foreach (int id in DuplicatePeds)
                    if (RuntimeEntityLookup.GetPedByID(id) is { IsLocal: false, IsPlayer: false }) { RemovePedRecord!.Invoke(null, new object[] { id, "Duplicate" }); }
            }
            DuplicatePeds.Clear();
        }
    }

    // Each duplicate is reported once, so a session shows what both games place
    // at the same points.
    private static void LogDuplicate(long key, string kind, int model, EntityPopulationType population, Vector3 at, string kept)
    {
        Action<string>? log = Log;
        if (log == null) { return; }
        if (LoggedDuplicates.Count >= 1024) { LoggedDuplicates.Clear(); }
        if (!LoggedDuplicates.Add(key)) { return; }
        log("OG Coop: duplicate " + kind + ": model=" + model.ToString("X8") + "; population=" + (int)population +
            "; at=" + at.X.ToString("F0") + "," + at.Y.ToString("F0") + "," + at.Z.ToString("F0") + "; kept=" + kept);
    }

    private static long PedKey(int id) => (1L << 32) | (uint)id;
    private static int LocalPlayerId() => (int)LocalPlayer!.GetValue(null)!;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static EntityPopulationType PopulationOf(Entity entity) => entity.PopulationType;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsPersistent(Entity entity) => entity.IsPersistent;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vehicle[] NearbyVehicles(Vector3 position, Model model) =>
        World.GetNearbyVehicles(position, EngineTrafficPolicy.VehicleTwinDistance, model);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Ped[] NearbyPeds(Vector3 position, Model model) =>
        World.GetNearbyPeds(position, EngineTrafficPolicy.PedTwinDistance, model);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasPlayerOccupant(Vehicle vehicle) => vehicle.Occupants.Any(IsPlayer);

    private static bool HasPlayerInside(int vehicleId)
    {
        lock (PedLock!)
        {
            foreach (SyncedPed ped in Peds!.Values)
            {
                if (ped.IsPlayer && (int)PedVehicle!.GetValue(ped)! == vehicleId) { return true; }
            }
        }
        return false;
    }

    internal static bool TryFindNearbyBlock(IList<CodeInstruction> instructions, out int start, out int end)
    {
        start = end = -1;
        if (instructions.Count < 50 || instructions[0].opcode != OpCodes.Nop || instructions[1].opcode != OpCodes.Ldarg_0 ||
            instructions[2].operand is not MethodInfo position || position.DeclaringType != typeof(SyncedEntity) || position.Name != "get_Position" ||
            instructions[3].opcode != OpCodes.Ldc_R4 || !Equals(instructions[3].operand, 2f) ||
            instructions[5].operand is not MethodInfo nearby || nearby.DeclaringType != typeof(World) || nearby.Name != "GetNearbyVehicles")
        {
            return false;
        }
        for (int index = 6; index + 1 < instructions.Count; index++)
        {
            if (instructions[index].operand is not MethodInfo getter || getter.DeclaringType != typeof(SyncedVehicle) ||
                getter.Name != "get_MainVehicle" || instructions[index + 1].opcode != OpCodes.Dup) { continue; }
            start = 1;
            end = index - 1;
            CodeInstruction[] block = instructions.Skip(start).Take(end - start).ToArray();
            return end - start == 42 && instructions[end].opcode == OpCodes.Ldarg_0 && block.All(code => code.blocks.Count == 0) &&
                block.Count(code => code.operand is MethodInfo method && method.DeclaringType == Pool && method.Name == "RemoveVehicle") == 1 &&
                block.Count(code => code.operand is MethodInfo method && method.DeclaringType == typeof(PoolObject) && method.Name == "Delete") == 1;
        }
        return false;
    }

    internal static IEnumerable<CodeInstruction> RemoveNearbyReplacement(IEnumerable<CodeInstruction> original)
    {
        List<CodeInstruction> instructions = original.Select(code => new CodeInstruction(code)).ToList();
        if (!TryFindNearbyBlock(instructions, out int start, out int end))
        {
            throw new InvalidOperationException("Unsupported RageCoop nearby vehicle replacement block.");
        }
        instructions.RemoveRange(start, end - start);
        return instructions;
    }

    private static void BeforeAddPed(SyncedPed __0)
    {
        lock (PedLock!) { RemoveStaleHandles(PedHandles!, __0, __0.MainPed?.Handle); }
    }

    private static void BeforeAddVehicle(SyncedVehicle __0)
    {
        lock (VehicleLock!) { RemoveStaleHandles(VehicleHandles!, __0, __0.MainVehicle?.Handle); }
    }

    internal static void RemoveStaleHandles<T>(Dictionary<int, T> handles, T entity, int? currentHandle) where T : class
    {
        int[] stale = handles.Where(pair => ReferenceEquals(pair.Value, entity) && pair.Key != currentHandle).Select(pair => pair.Key).ToArray();
        foreach (int handle in stale) { handles.Remove(handle); }
    }

    private static bool HasSendGate(MethodInfo target)
    {
        List<CodeInstruction> instructions = PatchProcessor.GetOriginalInstructions(target);
        int gate = instructions.FindIndex(code => code.operand is MethodInfo method && method.DeclaringType == typeof(Stopwatch) && method.Name == "get_ElapsedMilliseconds");
        return gate >= 0 && gate + 1 < instructions.Count && instructions[gate + 1].LoadsField(SyncInterval) &&
            instructions.Count(code => code.operand is MethodInfo method && method.DeclaringType == typeof(Stopwatch) && method.Name == "Restart") == 1 &&
            instructions.Count(code => code.operand is MethodInfo method && method.DeclaringType == Network && method.Name == "SendSync") == 1;
    }

    private static void BeforeSendPed(SyncedPed __0, ref bool __1, out SendAttempt __state) => PrepareSend(__0, ref __1, out __state);
    private static void BeforeSendVehicle(SyncedVehicle __0, ref bool __1, out SendAttempt __state) => PrepareSend(__0, ref __1, out __state);
    private static void AfterSendPed(SyncedPed __0, SendAttempt __state) => FinishSend(__0, __state);
    private static void AfterSendVehicle(SyncedVehicle __0, SendAttempt __state) => FinishSend(__0, __state);

    private static void PrepareSend(SyncedEntity entity, ref bool full, out SendAttempt attempt)
    {
        attempt = default;
        full = false;
        if (ReadLastSent!(entity).ElapsedMilliseconds < (int)SyncInterval!.GetValue(null)!) { return; }
        FullSnapshotSchedule schedule = _fullSchedules.GetValue(entity, _ => new FullSnapshotSchedule());
        bool requested = entity.SendFullNextFrame;
        full = schedule.IsDue(Stopwatch.GetTimestamp(), Stopwatch.Frequency / 2, requested);
        attempt = new SendAttempt { Schedule = schedule, Full = full, Requested = requested };
    }

    private static void FinishSend(SyncedEntity entity, SendAttempt attempt)
    {
        if (!attempt.Full) { return; }
        attempt.Schedule!.Sent(Stopwatch.GetTimestamp());
        if (attempt.Requested) { entity.SendFullNextFrame = false; }
    }

    private struct SendAttempt
    {
        internal FullSnapshotSchedule? Schedule;
        internal bool Full;
        internal bool Requested;
    }

    private static T? PoolField<T>(string name) where T : class => Pool?.GetField(name, Static)?.GetValue(null) as T;

    private static Func<SyncedEntity, T>? BindEntityGetter<T>(string name)
    {
        MethodInfo? getter = typeof(SyncedEntity).GetProperty(name, Instance)?.GetGetMethod(true);
        return getter?.ReturnType == typeof(T) ? (Func<SyncedEntity, T>)getter.CreateDelegate(typeof(Func<SyncedEntity, T>)) : null;
    }
}
