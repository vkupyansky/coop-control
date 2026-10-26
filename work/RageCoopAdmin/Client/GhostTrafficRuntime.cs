using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GTA;
using GTA.Math;
using GTA.Native;
using RageCoop.Client;
using RageCoop.Client.Scripting;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

internal sealed class GhostTrafficRuntime : IGhostTrafficRuntime
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type? Pool = typeof(SyncedVehicle).Assembly.GetType("RageCoop.Client.EntityPool", false);
    private static readonly object? VehicleLock = Pool?.GetField("VehiclesLock", Static)?.GetValue(null);
    private static readonly Dictionary<int, SyncedVehicle>? VehicleIds = Pool?.GetField("VehiclesByID", Static)?.GetValue(null) as Dictionary<int, SyncedVehicle>;
    private ConditionalWeakTable<SyncedVehicle, Provenance> _provenance = new ConditionalWeakTable<SyncedVehicle, Provenance>();
    private readonly Dictionary<int, float> _radii = new Dictionary<int, float>();

    internal static bool IsAvailable => RuntimeEntityLookup.IsAvailable && RuntimeEntityLookup.CanIdentifyPlayerPed &&
        VehicleLock != null && VehicleIds != null;

    public GhostTrafficVehicle[] Capture(int maximum)
    {
        if (!IsAvailable || maximum <= 0 || !API.IsOnServer) return Array.Empty<GhostTrafficVehicle>();
        Ped local = Game.Player.Character;
        if (local == null || !local.Exists()) return Array.Empty<GhostTrafficVehicle>();
        // Each game reports the traffic it owns near any player, not only near
        // its own: another player's game keeps cars near him that only it can
        // confirm, and its physics must not push them into him.
        Vector3[] players = RuntimeEntityLookup.RemotePlayerPositions().Where(Finite.Is).Prepend(local.Position).ToArray();
        SyncedVehicle[] records;
        lock (VehicleLock!) { records = VehicleIds!.Values.Take(2048).ToArray(); }
        var nearby = new List<(SyncedVehicle Record, Vehicle Native, float Distance, bool Player)>();
        foreach (SyncedVehicle record in records)
        {
            Vehicle? vehicle = record.MainVehicle;
            if (record.ID == 0 || record.OwnerID == 0 || vehicle == null || !vehicle.Exists() || !RuntimeEntityLookup.IsCurrentVehicle(record)) continue;
            Vector3 position = vehicle.Position;
            float distance = players.Min(player => player.DistanceToSquared(position));
            if (!Finite.Is(distance) || distance > 200f * 200f) continue;
            if (!TryRadius(vehicle.Model, out _)) continue;
            Ped? driver = vehicle.GetPedOnSeat(VehicleSeat.Driver);
            bool player = driver != null && driver.Exists() && RuntimeEntityLookup.IsPlayerPed(driver, out _);
            nearby.Add((record, vehicle, distance, player));
        }
        return nearby.OrderByDescending(item => item.Player).ThenBy(item => item.Distance).Take(Math.Min(maximum, GhostTrafficController.MaximumBodies))
            .Select(item => new GhostTrafficVehicle(item.Record.ID, item.Native.Model.Hash, item.Native.Handle,
                item.Native.MemoryAddress, item.Record, item.Native)).ToArray();
    }

    public bool TryRead(GhostTrafficVehicle key, out GhostTrafficBody? body)
    {
        body = null;
        if (!IsAvailable || key.Identity is not SyncedVehicle record || key.Native is not Vehicle vehicle ||
            record.ID != key.Id || record.OwnerID == 0 || !ReferenceEquals(RuntimeEntityLookup.GetVehicleByID(key.Id), record) ||
            !ReferenceEquals(RuntimeEntityLookup.GetVehicleByHandle(key.Handle), record) || !ReferenceEquals(record.MainVehicle, vehicle) ||
            !vehicle.Exists() || vehicle.Handle != key.Handle || key.Address == IntPtr.Zero || vehicle.MemoryAddress != key.Address ||
            vehicle.Model.Hash != key.ModelHash) return false;
        Vector3 position = vehicle.Position;
        if (!Finite.Is(position) || !TryRadius(vehicle.Model, out float radius)) return false;

        Provenance origin = _provenance.GetValue(record, _ => new Provenance());
        if (!ReferenceEquals(origin.Native, vehicle) || origin.Address != key.Address)
        {
            origin.Native = vehicle; origin.Address = key.Address; origin.AmbientSeen = false;
        }
        bool occupied = false, unknown = false;
        // Riders of motorcycles, quads and bicycles sit outside the vehicle's
        // body and collide on their own; car occupants are inside it.
        Model model = vehicle.Model;
        bool exposed = model.IsBike || model.IsQuadBike || model.IsBicycle;
        var riders = new List<int>();
        foreach (Ped occupant in vehicle.Occupants)
        {
            if (!occupant.Exists()) { unknown = true; continue; }
            occupied |= RuntimeEntityLookup.IsPlayerPed(occupant, out bool known);
            unknown |= !known;
            if (exposed) riders.Add(occupant.Handle);
        }
        Ped? driver = vehicle.GetPedOnSeat(VehicleSeat.Driver);
        bool playerDriver = driver != null && driver.Exists() && RuntimeEntityLookup.IsPlayerPed(driver, out _);
        if (occupied) origin.PlayerUsed = true;
        bool localOwner = record.IsLocal && API.LocalPlayerID != 0;
        EntityPopulationType population = localOwner ? vehicle.PopulationType : EntityPopulationType.Unknown;
        // Every random population is road traffic: ambient, parked, patrol
        // (police/emergency) and scenario cars. Permanent and mission cars are not.
        // A car taken over from another player's game is this game's traffic too.
        bool ambient = localOwner && !vehicle.IsPersistent &&
            (population >= EntityPopulationType.RandomPermanent && population <= EntityPopulationType.RandomAmbient ||
             TrafficHandover.WasAdopted(key.Handle, key.Address));
        if (ambient) origin.AmbientSeen = true;
        // Remote replicas are script-created and cannot prove their original
        // population locally. Only their owner's server-validated registry may.
        bool protectedVehicle = origin.PlayerUsed || unknown || (localOwner && origin.AmbientSeen && vehicle.IsPersistent);
        body = new GhostTrafficBody(key, position, radius, record.OwnerID, localOwner, ambient,
            protectedVehicle, occupied, playerDriver, driver != null && driver.Exists() && !driver.IsDead, !unknown, (int)population,
            localOwner ? 0f : vehicle.Speed, riders.ToArray());
        return true;
    }

    public void SuppressThisFrame(GhostTrafficBody player, GhostTrafficBody traffic)
    {
        // An entity keeps a single no-collision partner: every call replaces the
        // previous one. Each traffic part therefore names the player's vehicle,
        // which keeps every nearby car passable; the player's side names this
        // car and ends with the last pair of the frame. Calling all rider pairs
        // left a traffic car naming the player's rider, so a motorcycle passed
        // only the last car of the frame. Always current-frame.
        foreach (int part in player.Riders.Prepend(player.Vehicle.Handle))
            Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, part, traffic.Vehicle.Handle, true);
        foreach (int part in traffic.Riders.Append(traffic.Vehicle.Handle))
            Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, part, player.Vehicle.Handle, true);
    }

    // The gameplay camera follows the vehicle the local player is in, as
    // driver or passenger.
    public int CameraVehicleHandle()
    {
        Ped local = Game.Player.Character;
        if (local == null || !local.Exists()) return 0;
        Vehicle? vehicle = local.CurrentVehicle;
        return vehicle != null && vehicle.Exists() ? vehicle.Handle : 0;
    }

    public void IgnoreCameraCollisionThisFrame(GhostTrafficVehicle traffic) =>
        Function.Call(Hash.SET_GAMEPLAY_CAM_IGNORE_ENTITY_COLLISION_THIS_UPDATE, traffic.Handle);

    public void Clear()
    {
        _provenance = new ConditionalWeakTable<SyncedVehicle, Provenance>();
        _radii.Clear();
    }

    private bool TryRadius(Model model, out float radius)
    {
        if (_radii.TryGetValue(model.Hash, out radius)) return radius > 0;
        // Trailers belong to the traffic truck that tows them.
        if (!(model.IsCar || model.IsBike || model.IsQuadBike || model.IsBicycle || model.IsTrailer))
        {
            radius = 0;
            CacheRadius(model.Hash, radius);
            return false;
        }
        model.GetDimensions(out Vector3 minimum, out Vector3 maximum);
        Vector3 size = new Vector3(Math.Max(Math.Abs(minimum.X), Math.Abs(maximum.X)),
            Math.Max(Math.Abs(minimum.Y), Math.Abs(maximum.Y)), Math.Max(Math.Abs(minimum.Z), Math.Abs(maximum.Z)));
        radius = size.Length();
        if (!Finite.Is(radius) || radius <= 0 || radius > 100f || minimum.X >= maximum.X || minimum.Y >= maximum.Y || minimum.Z >= maximum.Z) return false;
        CacheRadius(model.Hash, radius);
        return true;
    }

    private void CacheRadius(int hash, float radius)
    {
        if (_radii.Count >= 128) _radii.Clear();
        _radii.Add(hash, radius);
    }

    private sealed class Provenance
    {
        internal bool PlayerUsed;
        internal bool AmbientSeen;
        internal object? Native;
        internal IntPtr Address;
    }
}
