using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OGCoop.Shared;
using GTA.Math;

namespace RageCoopAdmin.Client;

// All calls, including network snapshot application, belong to the GTA script
// thread. No per-pair messages or permanent entity collision flags are written.
internal sealed class GhostTrafficController
{
    internal const int MaximumBodies = 128;
    internal const int MaximumPairs = 256;
    private const float PairDistance = 70f;
    private const int ScanMilliseconds = 100;
    // Server confirmation takes a report and a broadcast. Traffic another
    // player's game creates next to this player needs passing at once.
    private const int ProvisionalMilliseconds = 3000;
    // Only an NPC in the owner's game drives a car with no player in it.
    private const float DrivenSpeed = 3f;
    private readonly IGhostTrafficRuntime _runtime;
    private readonly Action<object[]> _sendReport;
    private readonly Action<string>? _log;
    private readonly HashSet<long> _reportedSolid = new HashSet<long>();
    private readonly Func<long> _clock;
    private readonly long _frequency;
    private readonly Dictionary<int, GhostTrafficEntry> _registry = new Dictionary<int, GhostTrafficEntry>();
    private readonly Dictionary<long, Pair> _active = new Dictionary<long, Pair>();
    private readonly Dictionary<long, long> _firstSeen = new Dictionary<long, long>();
    private GhostTrafficVehicle[] _vehicles = Array.Empty<GhostTrafficVehicle>();
    private GhostTrafficVehicle[] _nearPlayers = Array.Empty<GhostTrafficVehicle>();
    private GhostTrafficClaim[] _lastClaims = Array.Empty<GhostTrafficClaim>();
    private string _epoch = string.Empty;
    private int _session;
    private long _revision = -1, _sequence, _lastScan, _lastReport;
    private bool _enabled, _scanDue = true, _reportDue = true;

    internal GhostTrafficController(Action<object[]> sendReport, Action<string> log)
        : this(new GhostTrafficRuntime(), sendReport, Stopwatch.GetTimestamp, Stopwatch.Frequency, log) { }

    internal GhostTrafficController(IGhostTrafficRuntime runtime, Action<object[]> sendReport, Func<long> clock, long frequency, Action<string>? log = null)
    {
        _runtime = runtime; _sendReport = sendReport; _clock = clock; _frequency = frequency; _log = log;
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
    }

    internal bool HasPendingPairs => _active.Count != 0;

    internal void Configure(bool enabled, string epoch, int session)
    {
        if (_epoch != epoch || _session != session)
        {
            _registry.Clear(); _revision = -1; _sequence = 0;
            _epoch = epoch; _session = session;
            ForgetVehicles(); _lastClaims = Array.Empty<GhostTrafficClaim>(); _scanDue = _reportDue = true;
            _firstSeen.Clear();
        }
        if (_enabled != enabled) _scanDue = _reportDue = true;
        bool was = _enabled;
        _enabled = enabled && session > 0 && epoch.Length == 32;
        if (was != _enabled) _log?.Invoke("OG Coop: traffic pass-through " + (_enabled ? "on" : "off"));
        if (!_enabled) { ForgetVehicles(); _firstSeen.Clear(); }
    }

    internal bool ApplyRegistry(object[] args)
    {
        if (!GhostTrafficProtocol.TryReadState(args, out int session, out string epoch, out long revision, out GhostTrafficEntry[] entries) ||
            session != _session || epoch != _epoch || revision <= _revision) return false;
        _registry.Clear();
        foreach (GhostTrafficEntry entry in entries) _registry.Add(entry.Id, entry);
        _revision = revision;
        return true;
    }

    internal void Tick()
    {
        if (!_enabled && _active.Count == 0) return;
        long now = _clock();
        bool scan = _enabled && (_scanDue || Elapsed(now, _lastScan, ScanMilliseconds));
        if (scan)
        {
            _vehicles = _runtime.Capture(MaximumBodies).Take(MaximumBodies).ToArray();
            _lastScan = now; _scanDue = false;
            var seen = new HashSet<long>(_vehicles.Select(Identity));
            foreach (long gone in _firstSeen.Keys.Where(key => !seen.Contains(key)).ToArray()) _firstSeen.Remove(gone);
            foreach (long key in seen) if (!_firstSeen.ContainsKey(key)) _firstSeen.Add(key, now);
        }

        var bodies = new Dictionary<int, GhostTrafficBody>();
        var attempted = new HashSet<int>();
        GhostTrafficBody? Read(GhostTrafficVehicle key)
        {
            if (bodies.TryGetValue(key.Id, out GhostTrafficBody? present))
                return ReferenceEquals(present.Vehicle, key) ? present : null;
            if (!attempted.Add(key.Id) || !_runtime.TryRead(key, out GhostTrafficBody? current) || current == null) return null;
            bodies.Add(key.Id, current);
            return current;
        }
        if (_enabled)
        {
            // Each read costs about twenty game calls. Between scans only cars
            // near a player driver can pair with it or touch it.
            foreach (GhostTrafficVehicle vehicle in scan ? _vehicles : _nearPlayers) Read(vehicle);
        }
        if (scan)
        {
            _nearPlayers = NearPlayers(bodies.Values);
            GhostTrafficClaim[] claims = bodies.Values.Where(body => body.LocalOwner && body.ClassificationKnown && (body.AmbientOrigin || body.Protected))
                    .OrderBy(body => body.Vehicle.Id).Take(GhostTrafficProtocol.MaximumClaims)
                    .Select(body => new GhostTrafficClaim(body.Vehicle.Id, body.Vehicle.ModelHash,
                        body.AmbientOrigin && !body.Protected && !body.PlayerOccupant)).ToArray();
            if (_reportDue || Elapsed(now, _lastReport, 1000) || (!SameClaims(claims, _lastClaims) && Elapsed(now, _lastReport, 250)))
            {
                if (_sequence < long.MaxValue)
                    _sendReport(GhostTrafficProtocol.EncodeReport(_session, _epoch, ++_sequence, claims));
                _lastClaims = claims;
                _lastReport = now; _reportDue = false;
            }
        }

        var wanted = new Dictionary<long, Pair>();
        var choices = new List<(float Distance, GhostTrafficBody Player, GhostTrafficBody Traffic)>();
        if (_enabled)
        {
            GhostTrafficBody[] players = bodies.Values.Where(body => body.PlayerDriver && body.AliveDriver).ToArray();
            GhostTrafficBody[] traffic = bodies.Values.Where(body => IsConfirmedTraffic(body) || IsProvisionalTraffic(body, now) || IsRemoteNpcTraffic(body)).ToArray();
            foreach (GhostTrafficBody player in players)
                foreach (GhostTrafficBody ambient in traffic)
                {
                    float distance = player.Position.DistanceToSquared(ambient.Position);
                    if (player.Vehicle.Id != ambient.Vehicle.Id && distance <= PairDistance * PairDistance)
                        choices.Add((distance, player, ambient));
                }
        }

        // Turning off while intersecting must not re-enable contact inside a
        // car. Continue only existing pairs until conservative body bounds clear.
        foreach (KeyValuePair<long, Pair> item in _active)
        {
            Pair pair = item.Value;
            GhostTrafficBody? player = Read(pair.Player), traffic = Read(pair.Traffic);
            if (player != null && traffic != null &&
                !traffic.PlayerOccupant && !traffic.Protected &&
                (!_enabled || pair.Provisional || (_registry.TryGetValue(traffic.Vehicle.Id, out GhostTrafficEntry? proof) && proof.ModelHash == traffic.Vehicle.ModelHash)) &&
                Intersecting(player, traffic))
                wanted[item.Key] = pair;
        }

        foreach (var choice in choices.OrderBy(value => value.Distance))
        {
            if (wanted.Count >= MaximumPairs) break;
            long key = PairKey(choice.Player.Vehicle.Id, choice.Traffic.Vehicle.Id);
            bool provisional = !IsConfirmedTraffic(choice.Traffic) ||
                (wanted.TryGetValue(key, out Pair? existing) && existing.Provisional);
            wanted[key] = new Pair(choice.Player.Vehicle, choice.Traffic.Vehicle, provisional);
        }

        _active.Clear();
        int cameraVehicle = wanted.Count == 0 ? 0 : _runtime.CameraVehicleHandle();
        var cameraTraffic = new Dictionary<int, (float Distance, GhostTrafficVehicle Vehicle)>();
        var applied = new List<(long Key, Pair Pair, GhostTrafficBody Player, GhostTrafficBody Traffic)>();
        foreach (KeyValuePair<long, Pair> item in wanted)
        {
            // Bodies are read again each frame, never reused across ticks. GTA
            // natives run on this non-yielding script thread, so all pairs can
            // share that identity/occupancy read without repeating native scans.
            GhostTrafficBody? player = Read(item.Value.Player), traffic = Read(item.Value.Traffic);
            if (player == null || traffic == null ||
                traffic.PlayerOccupant || traffic.Protected) continue;
            applied.Add((item.Key, item.Value, player, traffic));
        }

        // A traffic car keeps one no-collision partner, the last one named: this
        // game's own player goes last, so a car between two players lets him through.
        foreach (var (key, pair, player, traffic) in applied.OrderBy(item => item.Player.LocalOwner))
        {
            _runtime.SuppressThisFrame(player, traffic);
            _active[key] = pair;
            if (cameraVehicle != 0 && pair.Player.Handle == cameraVehicle)
                cameraTraffic[pair.Traffic.Id] = (player.Position.DistanceToSquared(traffic.Position), pair.Traffic);
        }
        // The gameplay camera still probes passable traffic and pulls in. Nearest
        // last, so it wins even if the engine keeps only one ignored entity.
        foreach (var item in cameraTraffic.Values.OrderByDescending(value => value.Distance).ThenByDescending(value => value.Vehicle.Id))
            _runtime.IgnoreCameraCollisionThisFrame(item.Vehicle);
        if (_enabled && _log != null) ReportSolidContacts(bodies.Values);
        if (!_enabled) ForgetVehicles();
    }

    // The caller keeps ticking to drain intersecting pairs on ordinary off or
    // disconnect. Final resource unload uses Clear: no callback or native state
    // is retained, and no arbitrary vehicle teleport is performed.
    internal void Stop()
    {
        _enabled = false; _registry.Clear(); _session = 0; _epoch = string.Empty;
        ForgetVehicles(); _lastClaims = Array.Empty<GhostTrafficClaim>(); _firstSeen.Clear();
    }
    internal void Clear()
    {
        Stop(); _active.Clear();
        _revision = -1; _sequence = 0; _lastClaims = Array.Empty<GhostTrafficClaim>(); _scanDue = _reportDue = true;
        _reportedSolid.Clear();
        _runtime.Clear();
    }

    private void ForgetVehicles()
    {
        _vehicles = Array.Empty<GhostTrafficVehicle>();
        _nearPlayers = Array.Empty<GhostTrafficVehicle>();
    }

    private static GhostTrafficVehicle[] NearPlayers(IEnumerable<GhostTrafficBody> bodies)
    {
        GhostTrafficBody[] all = bodies.ToArray();
        GhostTrafficBody[] players = all.Where(body => body.PlayerDriver && body.AliveDriver).ToArray();
        return all.Where(body => players.Any(player => player.Position.DistanceToSquared(body.Position) <= PairDistance * PairDistance))
            .Select(body => body.Vehicle).ToArray();
    }

    private bool IsConfirmedTraffic(GhostTrafficBody body) => !body.PlayerOccupant && !body.Protected &&
        _registry.TryGetValue(body.Vehicle.Id, out GhostTrafficEntry? entry) && entry.ModelHash == body.Vehicle.ModelHash &&
        entry.OwnerId == body.OwnerId;

    // Only newly appeared traffic qualifies: a car this game classifies as
    // ambient, or another game's replica driven by a known non-player driver.
    // Player-used, unknown or created cars never qualify, later passing needs
    // the server registry.
    private bool IsProvisionalTraffic(GhostTrafficBody body, long now) =>
        !body.PlayerOccupant && !body.Protected && body.ClassificationKnown &&
        (body.LocalOwner ? body.AmbientOrigin : body.AliveDriver) &&
        _firstSeen.TryGetValue(Identity(body.Vehicle), out long first) && !Elapsed(now, first, ProvisionalMilliseconds);

    // Another game's car with an NPC at the wheel, or moving with no player in
    // it, is that game's traffic. It passes without waiting for its owner's
    // report, which a far or older owner may never send. Parked and created
    // cars stand still and keep waiting for the server.
    private static bool IsRemoteNpcTraffic(GhostTrafficBody body) =>
        !body.LocalOwner && !body.PlayerOccupant && !body.Protected && body.ClassificationKnown &&
        (body.AliveDriver || body.Speed >= DrivenSpeed);

    // A car the local driver is about to touch but cannot pass is logged once,
    // with the reason, so a reported collision can be explained afterwards.
    private void ReportSolidContacts(IEnumerable<GhostTrafficBody> bodies)
    {
        GhostTrafficBody[] all = bodies.ToArray();
        GhostTrafficBody? local = all.FirstOrDefault(body => body.LocalOwner && body.PlayerDriver);
        if (local == null) return;
        foreach (GhostTrafficBody body in all)
        {
            if (body.Vehicle.Id == local.Vehicle.Id || body.PlayerDriver || !Intersecting(local, body) ||
                _active.ContainsKey(PairKey(local.Vehicle.Id, body.Vehicle.Id))) continue;
            if (_reportedSolid.Count >= 256) _reportedSolid.Clear();
            if (!_reportedSolid.Add(Identity(body.Vehicle))) continue;
            string reason = body.PlayerOccupant ? "player inside" : !body.ClassificationKnown ? "unknown occupant" :
                body.Protected ? "player-used or persistent" : IsConfirmedTraffic(body) || IsRemoteNpcTraffic(body) ? "passable but not paired" :
                body.LocalOwner ? (body.AmbientOrigin ? "awaiting server registry" : "not road traffic") :
                "remote, parked or created, not confirmed";
            _log!("OG Coop: solid traffic touched: id=" + body.Vehicle.Id + "; model=" + body.Vehicle.ModelHash +
                "; owner=" + (body.LocalOwner ? "local" : "remote") + "; population=" + body.Population + "; reason=" + reason);
        }
    }

    private static long Identity(GhostTrafficVehicle vehicle) => PairKey(vehicle.Id, vehicle.Handle);

    private static bool Intersecting(GhostTrafficBody first, GhostTrafficBody second)
    {
        float radius = first.Radius + second.Radius + 0.5f;
        return first.Position.DistanceToSquared(second.Position) <= radius * radius;
    }

    private bool Elapsed(long now, long previous, int milliseconds) => now - previous >= _frequency * milliseconds / 1000;
    private static bool SameClaims(GhostTrafficClaim[] first, GhostTrafficClaim[] second) => first.Length == second.Length &&
        first.Select((claim, index) => claim.Id == second[index].Id && claim.ModelHash == second[index].ModelHash && claim.Ambient == second[index].Ambient).All(value => value);
    private static long PairKey(int player, int traffic) => ((long)(uint)player << 32) | (uint)traffic;
    private sealed class Pair
    {
        internal Pair(GhostTrafficVehicle player, GhostTrafficVehicle traffic, bool provisional = false)
        { Player = player; Traffic = traffic; Provisional = provisional; }
        internal GhostTrafficVehicle Player { get; }
        internal GhostTrafficVehicle Traffic { get; }
        // Kept until the bodies separate, so an expired window never pushes a
        // player out from inside a car.
        internal bool Provisional { get; }
    }
}

internal sealed class GhostTrafficVehicle
{
    internal GhostTrafficVehicle(int id, int modelHash, int handle, IntPtr address, object identity, object native)
    { Id = id; ModelHash = modelHash; Handle = handle; Address = address; Identity = identity; Native = native; }
    internal int Id { get; }
    internal int ModelHash { get; }
    internal int Handle { get; }
    internal IntPtr Address { get; }
    internal object Identity { get; }
    internal object Native { get; }
}

internal sealed class GhostTrafficBody
{
    internal GhostTrafficBody(GhostTrafficVehicle vehicle, Vector3 position, float radius, int ownerId, bool localOwner,
        bool ambientOrigin, bool protectedVehicle, bool playerOccupant, bool playerDriver, bool aliveDriver, bool classificationKnown = true,
        int population = 0, float speed = 0f, int[]? riders = null)
    {
        Population = population;
        Speed = speed;
        Riders = riders ?? Array.Empty<int>();
        Vehicle = vehicle; Position = position; Radius = radius; OwnerId = ownerId; LocalOwner = localOwner;
        AmbientOrigin = ambientOrigin; Protected = protectedVehicle; PlayerOccupant = playerOccupant;
        PlayerDriver = playerDriver; AliveDriver = aliveDriver;
        ClassificationKnown = classificationKnown;
    }
    internal GhostTrafficVehicle Vehicle { get; }
    internal Vector3 Position { get; }
    internal float Radius { get; }
    internal int OwnerId { get; }
    internal bool LocalOwner { get; }
    internal bool AmbientOrigin { get; }
    internal bool Protected { get; }
    internal bool PlayerOccupant { get; }
    internal bool PlayerDriver { get; }
    internal bool AliveDriver { get; }
    internal bool ClassificationKnown { get; }
    internal int Population { get; }
    internal float Speed { get; }
    // Handles of occupants outside the body: motorcycle, quad and bicycle riders.
    internal int[] Riders { get; }
}

internal interface IGhostTrafficRuntime
{
    GhostTrafficVehicle[] Capture(int maximum);
    bool TryRead(GhostTrafficVehicle vehicle, out GhostTrafficBody? body);
    void SuppressThisFrame(GhostTrafficBody player, GhostTrafficBody traffic);
    int CameraVehicleHandle();
    void IgnoreCameraCollisionThisFrame(GhostTrafficVehicle traffic);
    void Clear();
}
