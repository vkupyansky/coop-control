using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RageCoop.Client;

namespace RageCoopAdmin.Client;

// The installed RageCoop publishes only local spawn events. Its entity pool also
// contains remote objects, but is internal: bind its exact supported signatures once.
internal static class RuntimeEntityLookup
{
    private static readonly Func<int, SyncedVehicle?>? VehicleById;
    private static readonly Func<int, SyncedVehicle?>? VehicleByHandle;
    private static readonly Func<int, SyncedPed?>? PedById;
    private static readonly Func<int, SyncedPed?>? PedByHandle;
    private static readonly Func<int, Player?>? PlayerById;
    private static readonly Func<List<int>>? VehicleIds;
    private static readonly Dictionary<int, SyncedPed>? Peds;
    private static readonly object? VehicleLock;
    private static readonly object? PedLock;

    static RuntimeEntityLookup()
    {
        Type? pool = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.EntityPool", throwOnError: false);
        if (pool == null) { return; }
        VehicleById = Bind<SyncedVehicle>(pool, "GetVehicleByID");
        VehicleByHandle = Bind<SyncedVehicle>(pool, "GetVehicleByHandle");
        PedById = Bind<SyncedPed>(pool, "GetPedByID");
        PedByHandle = Bind<SyncedPed>(pool, "GetPedByHandle");
        Type? players = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.PlayerList", throwOnError: false);
        if (players != null) { PlayerById = Bind<Player>(players, "GetPlayer"); }
        MethodInfo? vehicleIds = pool.GetMethod("GetVehicleIDs", BindingFlags.Public | BindingFlags.Static,
            null, Type.EmptyTypes, null);
        if (vehicleIds?.ReturnType == typeof(List<int>))
        {
            VehicleIds = (Func<List<int>>)vehicleIds.CreateDelegate(typeof(Func<List<int>>));
        }
        VehicleLock = pool.GetField("VehiclesLock", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        PedLock = pool.GetField("PedsLock", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Peds = pool.GetField("PedsByID", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Dictionary<int, SyncedPed>;
    }

    internal static bool IsAvailable => VehicleById != null && VehicleByHandle != null && PedById != null && VehicleLock != null && PedLock != null;

    internal static bool CanListVehicles => VehicleIds != null && VehicleById != null && VehicleLock != null;

    internal static bool CanListPlayers => Peds != null && PedLock != null;

    internal static bool CanIdentifyPlayerPed => PedById != null && PedByHandle != null && PlayerById != null && PedLock != null;

    internal static SyncedVehicle[] GetLocalVehicles()
    {
        if (VehicleIds == null || VehicleById == null || VehicleLock == null) { return Array.Empty<SyncedVehicle>(); }
        lock (VehicleLock)
        {
            return VehicleIds().Select(id => VehicleById(id))
                .Where(vehicle => vehicle != null && vehicle.IsLocal).Cast<SyncedVehicle>().ToArray();
        }
    }

    // Other players' characters as this game shows them, without this player.
    internal static GTA.Math.Vector3[] RemotePlayerPositions()
    {
        if (Peds == null || PedLock == null) { return Array.Empty<GTA.Math.Vector3>(); }
        lock (PedLock)
        {
            return Peds.Values.Where(ped => ped.IsPlayer && !ped.IsLocal && ped.MainPed != null && ped.MainPed.Exists())
                .Select(ped => ped.MainPed.Position).ToArray();
        }
    }

    // Other players who are alive, for a respawn beside a partner.
    internal static GTA.Math.Vector3[] LivingRemotePlayerPositions()
    {
        if (Peds == null || PedLock == null) { return Array.Empty<GTA.Math.Vector3>(); }
        lock (PedLock)
        {
            return Peds.Values.Where(ped => ped.IsPlayer && !ped.IsLocal && ped.MainPed != null && ped.MainPed.Exists() && !ped.MainPed.IsDead)
                .Select(ped => ped.MainPed.Position).ToArray();
        }
    }

    internal static SyncedVehicle? GetVehicleByID(int id)
    {
        if (VehicleById == null || VehicleLock == null) { return null; }
        lock (VehicleLock) { return VehicleById(id); }
    }

    internal static SyncedVehicle? GetVehicleByHandle(int handle)
    {
        if (VehicleByHandle == null || VehicleLock == null) { return null; }
        lock (VehicleLock) { return VehicleByHandle(handle); }
    }

    internal static bool IsCurrentVehicle(SyncedVehicle vehicle) =>
        vehicle.ID != 0 && ReferenceEquals(GetVehicleByID(vehicle.ID), vehicle);

    // Known means the local character, a current pool mapping or GTA's own
    // random population: RageCoop skips some random NPCs over its ped limit,
    // while other players' characters are script-created here. Any other
    // unmapped ped is not proof of an NPC.
    internal static bool IsPlayerPed(GTA.Ped ped, out bool known)
    {
        if (ped.IsPlayer || ped.Handle == GTA.Game.Player.Character.Handle) { known = true; return true; }
        known = false;
        if (PedByHandle == null || PedLock == null) { return false; }
        lock (PedLock)
        {
            SyncedPed? mapped = PedByHandle(ped.Handle);
            known = mapped != null && mapped.MainPed?.Handle == ped.Handle;
            if (known) { return mapped!.IsPlayer; }
        }
        GTA.EntityPopulationType population = ped.PopulationType;
        known = population >= GTA.EntityPopulationType.RandomPermanent && population <= GTA.EntityPopulationType.RandomAmbient;
        return false;
    }

    internal static SyncedPed? GetPedByHandle(int handle)
    {
        if (PedByHandle == null || PedLock == null) { return null; }
        lock (PedLock) { return PedByHandle(handle); }
    }

    internal static SyncedPed? GetPedByID(int id)
    {
        if (PedById == null || PedLock == null) { return null; }
        lock (PedLock) { return PedById(id); }
    }

    internal static SyncedPed? GetPlayerPed(int id)
    {
        if (PedById == null || PedLock == null) { return null; }
        lock (PedLock)
        {
            SyncedPed? ped = PedById(id);
            return ped != null && ped.ID == id && ped.IsPlayer ? ped : null;
        }
    }

    internal static bool IsCurrentPlayerPed(int handle)
    {
        if (handle == 0 || PedByHandle == null || PedById == null || PlayerById == null || PedLock == null) { return false; }
        lock (PedLock)
        {
            SyncedPed? mapped = PedByHandle(handle);
            if (mapped == null || !mapped.IsPlayer) { return false; }
            SyncedPed? current = PedById(mapped.ID);
            if (!ReferenceEquals(current, mapped) || current == null || !current.IsPlayer || current.MainPed?.Handle != handle) { return false; }
            try
            {
                // The int overload reads one connected player; unlike the
                // SyncedPed overload it never changes Player.Character.
                Player? player = PlayerById(current.ID);
                if (player == null || player.ID != current.ID) { return false; }
                SyncedPed? character = player.Character;
                // Only a valid replacement character disproves this pool
                // identity. A pending or corrupt link must not enable carjack.
                return character == null || !character.IsPlayer || character.ID != current.ID || ReferenceEquals(character, current);
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                                       (ex is ArgumentException && ex is not ArgumentNullException))
            {
                // The roster has no monitor shared with its listener. An
                // uncertain read cannot authorize ejecting a confirmed player.
                return true;
            }
        }
    }

    private static Func<int, T?>? Bind<T>(Type type, string name) where T : class
    {
        MethodInfo? method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
        if (method == null || method.ReturnType != typeof(T)) { return null; }
        return (Func<int, T?>)method.CreateDelegate(typeof(Func<int, T?>));
    }
}
