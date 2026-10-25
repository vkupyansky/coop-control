using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using GTA;
using GTA.Math;
using GTA.Native;
using HarmonyLib;
using RageCoop.Client;
using RageCoop.Client.Scripting;
using RageCoopAdmin.Client;

internal static partial class Program
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static GTA.Player _gtaPlayer = null!;
    private static Ped _localPed = null!, _npcDriver = null!;
    private static Ped? _runtimeDriver;
    private static Vehicle _nativeVehicle = null!;
    private static int _nativePopulation = 5, _nativeModel = 701;
    private static bool _nativePersistent, _invalidDimensions;
    private static float _nativeSpeed;
    private static bool _modelCar = true, _modelBike, _modelQuad, _modelBicycle, _modelTrailer;
    private static IntPtr _nativeAddress = new IntPtr(8001);
    private static readonly Dictionary<MethodInfo, DynamicMethod> RuntimeStubs = new Dictionary<MethodInfo, DynamicMethod>();

    private static void CheckRealRuntimeClassification()
    {
        Type pool = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.EntityPool", true)!;
        var ids = (Dictionary<int, SyncedVehicle>)pool.GetField("VehiclesByID", Static)!.GetValue(null)!;
        var handles = (Dictionary<int, SyncedVehicle>)pool.GetField("VehiclesByHandle", Static)!.GetValue(null)!;
        var peds = (Dictionary<int, SyncedPed>)pool.GetField("PedsByHandle", Static)!.GetValue(null)!;
        var harmony = new Harmony("friendslegacy.check.ghost-runtime-boundary");
        MethodInfo[] targets = typeof(GhostTrafficRuntime).GetMethods(Instance | Static | BindingFlags.DeclaredOnly)
            .Where(method => new[] { "Capture", "TryRead", "TryRadius" }.Contains(method.Name))
            .Append(typeof(RuntimeEntityLookup).GetMethod(nameof(RuntimeEntityLookup.IsPlayerPed), Static)!)
            .Append(typeof(RuntimeEntityLookup).GetMethod(nameof(RuntimeEntityLookup.RemotePlayerPositions), Static)!)
            .Concat(typeof(GhostTrafficRuntime).GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(Instance | Static | BindingFlags.DeclaredOnly)).Where(method => method.Name.Contains("Capture")))
            .Concat(typeof(RuntimeEntityLookup).GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(Instance | Static | BindingFlags.DeclaredOnly)).Where(method => method.Name.Contains("RemotePlayerPositions"))).ToArray();
        var pedIds = (Dictionary<int, SyncedPed>)pool.GetField("PedsByID", Static)!.GetValue(null)!;
        _gtaPlayer = (GTA.Player)typeof(GTA.Player).GetConstructor(Instance, null, new[] { typeof(int) }, null)!.Invoke(new object[] { 0 });
        _localPed = NativeObject<Ped>(8001); _npcDriver = NativeObject<Ped>(8002); _runtimeDriver = _npcDriver;
        _nativeVehicle = NativeObject<Vehicle>(9001);
        SyncedVehicle vehicle = Synced<SyncedVehicle>(301, 10);
        typeof(SyncedVehicle).GetProperty(nameof(SyncedVehicle.MainVehicle))!.SetValue(vehicle, _nativeVehicle);
        SyncedPed npc = Synced<SyncedPed>(401, 10);
        typeof(SyncedPed).GetProperty(nameof(SyncedPed.MainPed))!.SetValue(npc, _npcDriver);
        ids.Add(vehicle.ID, vehicle); handles.Add(_nativeVehicle.Handle, vehicle); peds.Add(_npcDriver.Handle, npc);
        try
        {
            foreach (MethodInfo method in targets) harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Program), nameof(IsolateRuntime)));
            var runtime = new GhostTrafficRuntime();
            NativePositions[_nativeVehicle.Handle] = new Vector3(500, 0, 0);
            Assert(runtime.Capture(128).Length == 0, "Traffic far from every player is not captured.");
            SyncedPed farPlayer = Synced<SyncedPed>(20, 20);
            typeof(SyncedPed).GetProperty(nameof(SyncedPed.MainPed))!.SetValue(farPlayer, NativeObject<Ped>(8003));
            NativePositions[8003] = new Vector3(520, 0, 0);
            pedIds.Add(farPlayer.ID, farPlayer);
            try
            {
                Assert(runtime.Capture(128).Length == 1,
                    "This game also captures and reports its traffic near another player far away, which only this game can confirm.");
            }
            finally { pedIds.Remove(farPlayer.ID); NativePositions.Clear(); }
            GhostTrafficVehicle key = runtime.Capture(128).Single();
            Assert(runtime.TryRead(key, out GhostTrafficBody? body) && body != null && body.LocalOwner && body.AmbientOrigin && !body.Protected && !body.PlayerOccupant,
                "Actual runtime proves native ambient provenance only for a current locally owned nonpersistent NPC vehicle.");
            Assert(body!.Riders.Length == 0, "A car's occupants are inside its body and need no pairs of their own.");
            foreach (int population in new[] { 1, 2, 3, 4 })
            {
                _nativePopulation = population;
                Assert(runtime.TryRead(key, out body) && body != null && body.AmbientOrigin && body.Population == population,
                    "Every random population — permanent, parked, patrol and scenario — is road traffic, not only ambient and parked.");
            }
            _nativePopulation = 6;
            Assert(runtime.TryRead(key, out body) && body != null && !body.AmbientOrigin && !body.Protected,
                "A permanent (story) car is never classified as road traffic.");
            _nativePopulation = 7; _nativePersistent = true;
            Assert(runtime.TryRead(key, out body) && body != null && body.Protected && !body.AmbientOrigin,
                "Actual runtime revokes locally observed ambient traffic adopted by a persistent resource.");
            SetRawOwner(vehicle, 20);
            runtime = new GhostTrafficRuntime();
            Assert(runtime.TryRead(key, out body) && body != null && !body.LocalOwner && !body.AmbientOrigin && !body.Protected,
                "A remote mission-population replica cannot invent ambient provenance or block a server-proved owner transfer.");
            _nativeSpeed = 12f;
            Assert(runtime.TryRead(key, out body) && body != null && body.Speed == 12f,
                "Actual runtime reads a remote copy's speed, so a moving car with no player in it counts as traffic.");
            _nativeSpeed = 0f;
            SyncedPed remotePlayer = Synced<SyncedPed>(20, 20);
            typeof(SyncedPed).GetProperty(nameof(SyncedPed.MainPed))!.SetValue(remotePlayer, _npcDriver);
            peds[_npcDriver.Handle] = remotePlayer;
            Assert(runtime.TryRead(key, out body) && body != null && body.PlayerDriver && body.PlayerOccupant && body.Protected,
                "Actual engine IsPlayer identity protects a remote player's car despite native Ped.IsPlayer=false.");
            _modelCar = false; _modelBike = true;
            var roadBikeRuntime = new GhostTrafficRuntime();
            Assert(roadBikeRuntime.TryRead(key, out GhostTrafficBody? bikeBody) && bikeBody != null && bikeBody.PlayerOccupant && bikeBody.Protected &&
                bikeBody.PlayerDriver && bikeBody.Riders.SequenceEqual(new[] { _npcDriver.Handle }),
                "A player's motorcycle stays solid for others but drives through traffic itself, together with its rider.");
            _modelBike = false; _modelQuad = true;
            Assert(new GhostTrafficRuntime().TryRead(key, out _), "Quad bikes are classified as road traffic, rather than aircraft.");
            _modelQuad = false; _modelBicycle = true;
            Assert(new GhostTrafficRuntime().TryRead(key, out _), "Ambient bicycles remain eligible road bodies while player-occupied ones remain protected.");
            _modelBicycle = false; _modelTrailer = true;
            Assert(new GhostTrafficRuntime().TryRead(key, out _), "A traffic truck's trailer is an eligible road body.");
            _modelTrailer = false;
            var aircraftRuntime = new GhostTrafficRuntime();
            Assert(!aircraftRuntime.TryRead(key, out _) && aircraftRuntime.Capture(128).Length == 0,
                "Planes, helicopters, boats and trains cannot enter the pair candidate cache or receive road-mode suppression.");
            _modelCar = true;
            peds[_npcDriver.Handle] = npc;
            Assert(runtime.TryRead(key, out body) && body != null && body.Protected && !body.PlayerOccupant,
                "After a player leaves, the same vehicle remains protected rather than becoming ghost traffic again.");
            runtime = new GhostTrafficRuntime(); peds.Remove(_npcDriver.Handle);
            Assert(runtime.TryRead(key, out body) && body != null && body.Protected && !body.ClassificationKnown,
                "An unregistered occupant is excluded immediately, without a false permanent source classification.");
            int savedPopulation = _nativePopulation;
            _nativePopulation = 5;
            Assert(new GhostTrafficRuntime().TryRead(key, out body) && body != null && body.ClassificationKnown && !body.Protected && !body.PlayerOccupant,
                "A random NPC that RageCoop skipped over its ped limit is still an NPC, so its car stays traffic.");
            _nativePopulation = savedPopulation;
            peds[_npcDriver.Handle] = npc;
            _nativeAddress = new IntPtr(8002);
            Assert(!runtime.TryRead(key, out _), "Actual runtime rejects a reused handle at a different native address.");
            _nativeAddress = new IntPtr(8001); _nativeModel++;
            Assert(!runtime.TryRead(key, out _), "Actual runtime rejects a model replacement under a cached native handle.");
            _nativeModel--;
            SyncedVehicle replacement = Synced<SyncedVehicle>(vehicle.ID, 20);
            ids[vehicle.ID] = replacement;
            Assert(!runtime.TryRead(key, out _), "Actual entity-pool object replacement invalidates the cached vehicle even before handle tables catch up.");
            ids[vehicle.ID] = vehicle;
            typeof(SyncedVehicle).GetProperty(nameof(SyncedVehicle.MainVehicle))!.SetValue(vehicle, NativeObject<Vehicle>(9001));
            Assert(!runtime.TryRead(key, out _), "Replacing the native wrapper on one engine record invalidates the previous body identity.");
            typeof(SyncedVehicle).GetProperty(nameof(SyncedVehicle.MainVehicle))!.SetValue(vehicle, _nativeVehicle);
            _invalidDimensions = true; runtime = new GhostTrafficRuntime();
            Assert(!runtime.TryRead(key, out _), "Malformed model bounds never authorize collision suppression.");
        }
        finally
        {
            ids.Remove(vehicle.ID); handles.Remove(_nativeVehicle.Handle); peds.Remove(_npcDriver.Handle);
            foreach (MethodInfo method in targets) harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
            _invalidDimensions = false; _modelCar = true; _modelBike = _modelQuad = _modelBicycle = false;
        }
    }

    private static T NativeObject<T>(int handle) where T : Entity => (T)typeof(T).GetConstructor(Instance, null, new[] { typeof(int) }, null)!.Invoke(new object[] { handle });
    private static T Synced<T>(int id, int owner) where T : SyncedEntity
    {
        var value = (T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(SyncedEntity).GetProperty(nameof(SyncedEntity.ID))!.SetValue(value, id); SetRawOwner(value, owner); return value;
    }
    private static void SetRawOwner(SyncedEntity entity, int owner) => typeof(SyncedEntity).GetField("_ownerID", Instance)!.SetValue(entity, owner);

    private static IEnumerable<CodeInstruction> IsolateRuntime(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction source in instructions)
        {
            var code = new CodeInstruction(source);
            if (code.operand is MethodInfo method && method.DeclaringType == typeof(Model) && method.Name == nameof(Model.GetDimensions))
            { code.opcode = OpCodes.Call; code.operand = typeof(Program).GetMethod(nameof(NativeDimensions), Static)!; }
            else if (code.operand is MethodInfo native && IsRuntimeBoundary(native))
            { code.opcode = OpCodes.Call; code.operand = RuntimeStub(native); }
            yield return code;
        }
    }

    private static bool IsRuntimeBoundary(MethodInfo method)
    {
        Type? type = method.DeclaringType;
        if (type == typeof(Model) && new[] { "get_IsCar", "get_IsBike", "get_IsQuadBike", "get_IsBicycle", "get_IsTrailer" }.Contains(method.Name)) return true;
        if (type == typeof(API) || (type == typeof(SyncedEntity) && method.Name == "get_IsLocal")) return true;
        if (type?.Assembly != typeof(Entity).Assembly || type.IsValueType || type == typeof(PoolObject) || type == typeof(InputArgument)) return false;
        return type != typeof(Entity) || (method.Name != "op_Equality" && method.Name != "op_Inequality");
    }

    private static DynamicMethod RuntimeStub(MethodInfo method)
    {
        if (RuntimeStubs.TryGetValue(method, out DynamicMethod? cached)) return cached;
        Type[] arguments = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        if (!method.IsStatic) arguments = new[] { method.DeclaringType!.IsValueType ? method.DeclaringType.MakeByRefType() : method.DeclaringType }.Concat(arguments).ToArray();
        var stub = new DynamicMethod("GhostRuntime_" + RuntimeStubs.Count, method.ReturnType, arguments, typeof(Program), true);
        ILGenerator il = stub.GetILGenerator();
        string? field = method.Name switch
        {
            "get_Player" => nameof(_gtaPlayer),
            "get_Character" => nameof(_localPed),
            "get_PopulationType" => nameof(_nativePopulation),
            "get_IsPersistent" => nameof(_nativePersistent),
            "get_MemoryAddress" => nameof(_nativeAddress),
            "GetPedOnSeat" => nameof(_runtimeDriver),
            "get_IsCar" => nameof(_modelCar),
            "get_IsBike" => nameof(_modelBike),
            "get_IsQuadBike" => nameof(_modelQuad),
            "get_IsBicycle" => nameof(_modelBicycle),
            "get_IsTrailer" => nameof(_modelTrailer),
            "get_Speed" => nameof(_nativeSpeed),
            _ => null
        };
        string? helper = method.Name switch
        {
            "get_Model" => nameof(NativeModel),
            "get_Occupants" => nameof(NativeOccupants),
            "get_Position" => nameof(NativePosition),
            "get_IsLocal" => nameof(NativeLocalOwner),
            _ => null
        };
        if (field != null) il.Emit(OpCodes.Ldsfld, typeof(Program).GetField(field, Static)!);
        else if (helper != null)
        {
            if (method.Name is "get_IsLocal" or "get_Position") il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(Program).GetMethod(helper, Static)!);
        }
        else if (method.Name == "Exists" || method.Name == "get_IsOnServer") il.Emit(OpCodes.Ldc_I4_1);
        else if (method.Name == "get_LocalPlayerID") il.Emit(OpCodes.Ldc_I4, 10);
        else if (method.Name == "get_IsPlayer" || method.Name == "get_IsDead") il.Emit(OpCodes.Ldc_I4_0);
        else throw new InvalidOperationException("Unexpected GTA boundary: " + method);
        il.Emit(OpCodes.Ret); RuntimeStubs.Add(method, stub); return stub;
    }
    private static Model NativeModel() => new Model(_nativeModel);
    private static Ped[] NativeOccupants() => _runtimeDriver == null ? Array.Empty<Ped>() : new[] { _runtimeDriver };
    private static readonly Dictionary<int, Vector3> NativePositions = new Dictionary<int, Vector3>();
    private static Vector3 NativePosition(Entity entity) => NativePositions.TryGetValue(entity.Handle, out Vector3 position) ? position : Vector3.Zero;
    private static bool NativeLocalOwner(SyncedEntity entity) => entity.OwnerID == 10;
    private static void NativeDimensions(ref Model model, out Vector3 minimum, out Vector3 maximum)
    { minimum = new Vector3(-1, -2, -1); maximum = _invalidDimensions ? minimum : new Vector3(1, 2, 1); }
}
