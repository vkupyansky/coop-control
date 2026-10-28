using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using OGCoop.Shared;
using GTA;
using GTA.Math;
using GTA.Native;
using RageCoop.Client;

namespace RageCoopAdmin.Client;

// Another player's game removes an NPC car or pedestrian it owns once it leaves
// that player's view. The server does not relay removals: the copy here just
// stops receiving updates and RageCoop deletes it after 10 s, often in front of
// this player. A copy near this player that went silent while its owner keeps
// sending passes to this game instead and lives on as its own traffic.
// Game thread only.
internal static class TrafficHandover
{
    internal const float Distance = 150f;
    internal const long SilentMilliseconds = 1500;
    internal const long OwnerActiveMilliseconds = 500;
    private const int ScanMilliseconds = 250;
    private const int MaximumAdopted = 1024;
    // GTA's "normal" driving style: obeys lights and avoids traffic.
    private const int NormalDriving = 786603;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type? Pool = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.EntityPool", false);
    private static readonly Dictionary<int, SyncedVehicle>? Vehicles = PoolField<Dictionary<int, SyncedVehicle>>("VehiclesByID");
    private static readonly Dictionary<int, SyncedVehicle>? VehicleHandles = PoolField<Dictionary<int, SyncedVehicle>>("VehiclesByHandle");
    private static readonly Dictionary<int, SyncedPed>? Peds = PoolField<Dictionary<int, SyncedPed>>("PedsByID");
    private static readonly Dictionary<int, SyncedPed>? PedHandles = PoolField<Dictionary<int, SyncedPed>>("PedsByHandle");
    private static readonly object? VehicleLock = PoolField<object>("VehiclesLock");
    private static readonly object? PedLock = PoolField<object>("PedsLock");
    private static readonly PropertyInfo? PedVehicle = typeof(SyncedPed).GetProperty("VehicleID", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly HashSet<(int Handle, IntPtr Address)> Adopted = new HashSet<(int, IntPtr)>();
    private static long _lastScan;

    internal static bool IsAvailable => Vehicles != null && VehicleHandles != null && Peds != null && PedHandles != null &&
        VehicleLock != null && PedLock != null && PedVehicle?.PropertyType == typeof(int);

    // Silence alone may be the owner's own pause or connection. Only a copy that
    // went silent while its owner keeps sending was removed by its owner.
    internal static bool ShouldAdopt(long silentMilliseconds, long ownerSilentMilliseconds, float distanceSquared) =>
        silentMilliseconds >= SilentMilliseconds && ownerSilentMilliseconds >= 0 &&
        ownerSilentMilliseconds <= OwnerActiveMilliseconds && Finite.Is(distanceSquared) && distanceSquared <= Distance * Distance;

    // A car taken over here stays road traffic for the ghost-traffic mode,
    // whatever population GTA reports for the former script-created copy.
    internal static bool WasAdopted(int handle, IntPtr address) => Adopted.Contains((handle, address));

    internal static void Reset()
    {
        Adopted.Clear();
        _lastScan = 0;
    }

    internal static void Tick(long now)
    {
        if (_lastScan != 0 && now - _lastScan < Stopwatch.Frequency * ScanMilliseconds / 1000) { return; }
        _lastScan = now;
        if (!IsAvailable) { return; }
        Vector3 local = LocalPosition();
        int ridingWith = PassengerPopulation.RidingWith;
        var peds = new List<SyncedPed>();
        var vehicles = new List<SyncedVehicle>();
        lock (PedLock!)
        {
            foreach (SyncedPed ped in Peds!.Values)
            {
                Ped? native = ped.MainPed;
                if (ped.IsLocal || ped.IsPlayer || native == null || !Exists(native) || !EngineTrafficPolicy.MayAdopt(ped.OwnerID, ridingWith)) { continue; }
                if (ShouldAdopt(Silence(ped), OwnerSilence(ped.OwnerID), PositionOf(native).DistanceToSquared(local))) { peds.Add(ped); }
            }
        }
        lock (VehicleLock!)
        {
            foreach (SyncedVehicle vehicle in Vehicles!.Values)
            {
                Vehicle? native = vehicle.MainVehicle;
                if (vehicle.IsLocal || native == null || !Exists(native) || !EngineTrafficPolicy.MayAdopt(vehicle.OwnerID, ridingWith)) { continue; }
                float distance = PositionOf(native).DistanceToSquared(local);
                if (!ShouldAdopt(Silence(vehicle), OwnerSilence(vehicle.OwnerID), distance)) { continue; }
                if (!HasPlayerInside(vehicle.ID) && !HasPlayerOccupant(native)) { vehicles.Add(vehicle); }
            }
        }
        // Drivers first: a car's driving order needs its driver to be this game's.
        foreach (SyncedPed ped in peds) { AdoptPed(ped); }
        foreach (SyncedVehicle vehicle in vehicles) { AdoptVehicle(vehicle); }
    }

    private static void AdoptPed(SyncedPed ped)
    {
        Ped native = ped.MainPed;
        lock (PedLock!)
        {
            if (!Peds!.TryGetValue(ped.ID, out SyncedPed? current) || !ReferenceEquals(current, ped)) { return; }
            Peds.Remove(ped.ID);
            if (PedHandles!.TryGetValue(native.Handle, out SyncedPed? mapped) && ReferenceEquals(mapped, ped)) { PedHandles.Remove(native.Handle); }
        }
        ReleasePed(native);
    }

    private static void AdoptVehicle(SyncedVehicle vehicle)
    {
        Vehicle native = vehicle.MainVehicle;
        lock (VehicleLock!)
        {
            if (!Vehicles!.TryGetValue(vehicle.ID, out SyncedVehicle? current) || !ReferenceEquals(current, vehicle)) { return; }
            Vehicles.Remove(vehicle.ID);
            if (VehicleHandles!.TryGetValue(native.Handle, out SyncedVehicle? mapped) && ReferenceEquals(mapped, vehicle)) { VehicleHandles.Remove(native.Handle); }
        }
        if (Adopted.Count >= MaximumAdopted) { Adopted.Clear(); }
        Adopted.Add((native.Handle, AddressOf(native)));
        ReleaseVehicle(native);
    }

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

    // -1 when the owner is unknown, which never authorizes a takeover.
    private static long OwnerSilence(int ownerId)
    {
        lock (PedLock!)
        {
            return Peds!.TryGetValue(ownerId, out SyncedPed? owner) && owner.IsPlayer ? Silence(owner) : -1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Silence(SyncedEntity entity) =>
        entity.LastSyncedStopWatch is { IsRunning: true } watch ? watch.ElapsedMilliseconds : -1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector3 LocalPosition() => Game.Player.Character.Position;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Exists(Entity entity) => entity.Exists();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector3 PositionOf(Entity entity) => entity.Position;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IntPtr AddressOf(Entity entity) => entity.MemoryAddress;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasPlayerOccupant(Vehicle vehicle)
    {
        foreach (Ped occupant in vehicle.Occupants)
        {
            if (RuntimeEntityLookup.IsPlayerPed(occupant, out _)) { return true; }
        }
        return false;
    }

    // RageCoop makes copies of NPCs into puppets. The NPC gets back its own
    // reactions and a task, then GTA manages and later removes it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleasePed(Ped ped)
    {
        ped.BlockPermanentEvents = false;
        foreach (PedConfigFlagToggles flag in new[]
        {
            PedConfigFlagToggles.BlockNonTemporaryEvents, PedConfigFlagToggles.DisableShockingEvents, PedConfigFlagToggles.DisablePanicInVehicle,
            PedConfigFlagToggles.DisableHurt, PedConfigFlagToggles.DisableExplosionReactions, PedConfigFlagToggles.IgnoreBeingOnFire,
            PedConfigFlagToggles.DisableEvasiveDives
        })
        {
            ped.SetConfigFlag(flag, false);
        }
        Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, StringHash.AtStringHash(ped.Gender == Gender.Female ? "CIVFEMALE" : "CIVMALE"));
        Vehicle? vehicle = ped.CurrentVehicle;
        if (vehicle == null)
        {
            Function.Call(Hash.TASK_WANDER_STANDARD, ped.Handle, 10f, 10);
        }
        else if (vehicle.GetPedOnSeat(VehicleSeat.Driver) == ped && RuntimeEntityLookup.GetVehicleByHandle(vehicle.Handle) is not { IsLocal: false })
        {
            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, ped.Handle, vehicle.Handle, 20f, NormalDriving);
        }
        ped.MarkAsNoLongerNeeded();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleaseVehicle(Vehicle vehicle)
    {
        Ped? driver = vehicle.GetPedOnSeat(VehicleSeat.Driver);
        if (driver != null && driver.Exists() && !RuntimeEntityLookup.IsPlayerPed(driver, out _) &&
            RuntimeEntityLookup.GetPedByHandle(driver.Handle) is not { IsLocal: false })
        {
            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, driver.Handle, vehicle.Handle, 20f, NormalDriving);
        }
        vehicle.MarkAsNoLongerNeeded();
    }

    private static T? PoolField<T>(string name) where T : class => Pool?.GetField(name, Static)?.GetValue(null) as T;
}
