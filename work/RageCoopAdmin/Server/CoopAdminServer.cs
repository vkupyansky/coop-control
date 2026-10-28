using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

public sealed partial class CoopAdminServer : ServerScript
{
    private const int SpawnVehicleEvent = 20774111;
    private const int RepairVehicleEvent = 20774113;
    private const int StatusEvent = 20774114;
    private const int ShareWaypointEvent = 20774118;
    private const int WaypointStateEvent = 20774119;
    private const int TeleportToPlayerEvent = 20774120;
    private const int TeleportToPlayerStateEvent = 20774121;
    private const int RecallCarEvent = 20774122;
    private const int RecallCarStateEvent = 20774123;
    private const int RepairStateEvent = 20774136;
    private const int VehicleSpawnResponseEvent = 20774137;

    private const long SpawnCooldownMs = 750;
    private const long RepairCooldownMs = 700;
    private const long WaypointCooldownMs = 1000;
    private const long TeleportCooldownMs = 3000;
    private const long RecallCooldownMs = 1000;
    private const float RepairMaxDistanceSquared = 35f * 35f;
    private const int MapCoordinateLimit = 1_000_000;

    private readonly Dictionary<Client, long> lastSpawn = new();
    private readonly Dictionary<Client, long> lastRepair = new();
    private readonly Dictionary<Client, long> lastWaypoint = new();
    private readonly Dictionary<Client, long> lastTeleport = new();
    private readonly Dictionary<Client, long> lastRecall = new();
    private readonly Dictionary<Client, ServerVehicle> spawnedVehicles = new();
    // Player data requests read files from disk and broadcast, so each kind runs at most once per pause.
    private readonly Dictionary<(Client Client, int Kind), long> lastDataRequests = new();
    internal const long DataRequestCooldownMs = 1000;
    internal const long GarageChangeCooldownMs = 100;
    private long repairRevision;
    private (int X, int Y)? sharedWaypoint;

    public override void OnStart()
    {
        OGCoop.Shared.EngineLoggerGuard.Install();
        try
        {
            OGCoop.Shared.EngineWorkerGuard.Install();
            OGCoop.Shared.EngineTransportGuard.Install();
            EngineServerGuard.Install();
            EngineRelayGuard.Install();
        }
        catch
        {
            EngineRelayGuard.Uninstall();
            EngineServerGuard.Uninstall();
            OGCoop.Shared.EngineTransportGuard.Uninstall();
            OGCoop.Shared.EngineWorkerGuard.Uninstall();
            OGCoop.Shared.EngineLoggerGuard.Uninstall();
            throw;
        }
        API.RegisterCustomEventHandler(SpawnVehicleEvent, OnSpawnVehicle);
        API.RegisterCustomEventHandler(RepairVehicleEvent, OnRepairVehicle);
        API.RegisterCustomEventHandler(ShareWaypointEvent, OnShareWaypoint);
        API.RegisterCustomEventHandler(TeleportToPlayerEvent, OnTeleportToPlayer);
        API.RegisterCustomEventHandler(RecallCarEvent, OnRecallCar);
        API.RegisterCustomEventHandler(RepairOwnerAckEvent, OnRepairOwnerAcknowledged);
        RegisterVehicleToolsEvents();
        RegisterWorldStateEvents();
        RegisterGhostTrafficEvents();
        RegisterGarageEvents();
        RegisterCharacterEvents();
        RegisterYachtEvents();
        RegisterHomeEvents();
        RegisterSpecialPlaceEvents();
        API.Events.OnPlayerReady += OnPlayerReady;
        API.Events.OnPlayerDisconnected += OnPlayerDisconnected;
        API.Events.OnPlayerUpdate += OnRepairPlayerUpdate;
        API.Events.OnPlayerUpdate += OnWorldPlayerUpdate;
        API.Events.OnPlayerUpdate += OnGhostTrafficPlayerUpdate;
        Logger.Info("OG Coop ready: shared world, vehicles and owner-confirmed repair");
        int port = ServerBanner.ReadPort(System.IO.Path.Combine(AppContext.BaseDirectory, "Settings.xml"));
        foreach (string line in ServerBanner.Lines(port, ServerBanner.LocalAddresses(), ServerBanner.Russian)) Logger.Info(line);
        StartUpdateCheck();
    }

    public override void OnStop()
    {
        EngineRelayGuard.Uninstall();
        API.Events.OnPlayerReady -= OnPlayerReady;
        API.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
        API.Events.OnPlayerUpdate -= OnRepairPlayerUpdate;
        API.Events.OnPlayerUpdate -= OnWorldPlayerUpdate;
        API.Events.OnPlayerUpdate -= OnGhostTrafficPlayerUpdate;
        EngineServerGuard.Uninstall();
        OGCoop.Shared.EngineTransportGuard.Uninstall();
        OGCoop.Shared.EngineWorkerGuard.Uninstall();
        OGCoop.Shared.EngineLoggerGuard.Uninstall();
        pendingRepairs.Clear();
        lastSpawn.Clear();
        lastRepair.Clear();
        lastWaypoint.Clear();
        lastTeleport.Clear();
        lastRecall.Clear();
        lastPlaceInvite.Clear();
        spawnedVehicles.Clear();
        sharedWaypoint = null;
        CleanupVehicleTools();
        CleanupWorldState();
        CleanupGhostTraffic();
    }

    private void OnSpawnVehicle(CustomEventReceivedArgs e)
    {
        if (!TryGetSpawnRequestToken(e, out int token))
        {
            return;
        }
        if (e.Args.Length != 2 || e.Args[1] is not int modelHash || modelHash == 0)
        {
            SendVehicleSpawnFailure(e.Client, token, "Неверная модель машины. Выберите её заново.");
            return;
        }

        if (!CanRun(e.Client, lastSpawn, SpawnCooldownMs))
        {
            SendVehicleSpawnFailure(e.Client, token, "Подождите секунду перед созданием следующей машины.");
            return;
        }

        ServerVehicle? vehicle = TryCreateVehicleFor(e.Client, modelHash, out string error);
        if (vehicle == null) { SendVehicleSpawnFailure(e.Client, token, error); return; }
        SendVehicleSpawnCreated(e.Client, token, vehicle, modelHash);
    }

    private bool TryGetSpawnRequestToken(CustomEventReceivedArgs e, out int token)
    {
        token = 0;
        if (e.Client == null || e.Args is not { Length: > 0 } || e.Args[0] is not int value || value == 0)
            return false;
        token = value;
        if (HasReadyPlayer(e)) { return true; }
        SendVehicleSpawnFailure(e.Client, token, "Дождитесь подключения к игре и повторите.");
        return false;
    }

    private void SendVehicleSpawnCreated(Client client, int token, ServerVehicle vehicle, int modelHash) =>
        API.SendCustomEventQueued(new List<Client> { client }, VehicleSpawnResponseEvent, token, vehicle.ID, modelHash);

    private void SendVehicleSpawnFailure(Client client, int token, string message) =>
        API.SendCustomEventQueued(new List<Client> { client }, VehicleSpawnResponseEvent, token, message);

    private ServerVehicle? TryCreateVehicleFor(Client client, int modelHash, out string error)
    {
        error = string.Empty;
        // The game client checked IsVehicle and IsInCdImage before this request.
        // The headless server cannot call GTA natives, and VehicleHash omits some DLC models.
        Vector3 playerPosition = client.Player.Position;
        float heading = client.Player.Rotation.Z;
        if (!IsValidWorldCoordinate(playerPosition.X) || !IsValidWorldCoordinate(playerPosition.Y) ||
            !IsValidWorldCoordinate(playerPosition.Z) || !float.IsFinite(heading))
        {
            error = "Не удалось определить место для машины. Повторите попытку.";
            return null;
        }

        heading %= 360f;
        double radians = heading * Math.PI / 180d;
        Vector3 spawnPosition = new(
            playerPosition.X - (float)Math.Sin(radians) * 5f,
            playerPosition.Y + (float)Math.Cos(radians) * 5f,
            playerPosition.Z);
        if (!IsValidWorldCoordinate(spawnPosition.X) || !IsValidWorldCoordinate(spawnPosition.Y))
        {
            error = "Машину нельзя создать за пределами карты.";
            return null;
        }

        ServerVehicle? vehicle = API.Entities.CreateVehicle(client, new Model(modelHash), spawnPosition, heading);
        if (vehicle is null)
        {
            error = "Не удалось создать эту модель машины. Повторите попытку.";
            return null;
        }

        if (spawnedVehicles.TryGetValue(client, out ServerVehicle? previous))
        {
            if (ReferenceEquals(API.Entities.GetVehicleByID(previous.ID), previous))
            {
                previous.Delete();
            }

            ForgetVehicleAppearance(previous);
        }

        spawnedVehicles[client] = vehicle;
        return vehicle;
    }

    private void OnRepairVehicle(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 2 } ||
            e.Args[0] is not int vehicleId || vehicleId == 0 ||
            e.Args[1] is not int reportedModelHash || reportedModelHash == 0)
        {
            return;
        }

        ServerVehicle? vehicle = API.Entities.GetVehicleByID(vehicleId);
        if (vehicle is null)
        {
            SendStatus(e.Client, "Машина больше не существует");
            return;
        }

        if (!(DistanceSquared(e.Client.Player.Position, vehicle.Position) <= RepairMaxDistanceSquared))
        {
            SendStatus(e.Client, "Подойдите ближе к машине");
            return;
        }

        int knownModelHash = vehicle.Model.Hash;
        if (knownModelHash != 0 && reportedModelHash != knownModelHash)
        {
            SendStatus(e.Client, "Машина изменилась. Повторите ремонт");
            return;
        }

        if (!CanRun(e.Client, lastRepair, RepairCooldownMs))
        {
            return;
        }

        Client? owner = vehicle.Owner;
        if (owner is null || !owner.IsReady)
        {
            SendStatus(e.Client, "Машина ещё загружается. Повторите ремонт через секунду");
            return;
        }

        PrunePendingRepairs();
        if (pendingRepairs.Count >= 128 && !pendingRepairs.ContainsKey(vehicle.ID))
        {
            SendStatus(e.Client, "Ремонт пока занят. Повторите через секунду");
            return;
        }

        long revision = System.Threading.Interlocked.Increment(ref repairRevision);
        int modelHash = knownModelHash != 0 ? knownModelHash : reportedModelHash;
        pendingRepairs[vehicle.ID] = new PendingRepair(vehicle, owner, e.Client, revision, modelHash,
            Environment.TickCount64);
        // Repair the simulation owner first, since a passenger's local repair is otherwise
        // overwritten by the owner's next ordinary vehicle snapshot.
        API.SendCustomEventQueued(new List<Client> { owner }, RepairOwnerRequestEvent,
            vehicle.ID, revision, modelHash);
    }

    private void OnShareWaypoint(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 2 } ||
            e.Args[0] is not int x || e.Args[1] is not int y)
        {
            return;
        }

        if (!IsMapCoordinate(x) || !IsMapCoordinate(y))
        {
            SendStatus(e.Client, "Метка за пределами карты");
            return;
        }

        if (!CanRun(e.Client, lastWaypoint, WaypointCooldownMs))
        {
            return;
        }

        sharedWaypoint = (x, y);
        API.SendCustomEventQueued(ReadyClients(), WaypointStateEvent, x, y);
    }

    private void OnTeleportToPlayer(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 2 } || e.Args[0] is not int targetId ||
            e.Args[1] is not int token || token == 0 || targetId == e.Client.Player.ID)
        {
            return;
        }

        if (!CanRun(e.Client, lastTeleport, TeleportCooldownMs))
        {
            return;
        }

        if (API.GetAllClients().TryGetValue(targetId, out Client? other) && other.IsReady && other.Player is not null)
        {
            Vector3 position = other.Player.Position;
            if (!TryEncodeCoordinate(position.X, out int x) ||
                !TryEncodeCoordinate(position.Y, out int y) ||
                !TryEncodeCoordinate(position.Z, out int z))
            {
                SendStatus(e.Client, "Местоположение напарника недоступно");
                return;
            }

            API.SendCustomEventQueued(new List<Client> { e.Client }, TeleportToPlayerStateEvent, token, x, y, z);
            return;
        }

        SendStatus(e.Client, "Игрок уже отключился. Обновите список напарников");
    }

    private void OnRecallCar(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 1 } || e.Args[0] is not int token || token == 0)
        {
            return;
        }

        if (!CanRun(e.Client, lastRecall, RecallCooldownMs))
        {
            SendRecallFailure(e.Client, token, "Подождите секунду и повторите попытку");
            return;
        }

        if (!spawnedVehicles.TryGetValue(e.Client, out ServerVehicle? vehicle))
        {
            SendRecallFailure(e.Client, token, "Сначала создайте машину в меню транспорта");
            return;
        }

        if (!ReferenceEquals(API.Entities.GetVehicleByID(vehicle.ID), vehicle))
        {
            spawnedVehicles.Remove(e.Client);
            SendRecallFailure(e.Client, token, "Машина больше не существует");
            return;
        }

        SendRecallState(e.Client, vehicle, token);
    }

    private void SendRecallState(Client client, ServerVehicle vehicle, int token)
    {
        Vector3 position = vehicle.Position;
        if (!TryEncodeCoordinate(position.X, out int x) ||
            !TryEncodeCoordinate(position.Y, out int y) ||
            !TryEncodeCoordinate(position.Z, out int z))
        {
            SendRecallFailure(client, token, "Местоположение машины недоступно");
            return;
        }

        API.SendCustomEventQueued(new List<Client> { client }, RecallCarStateEvent, token, vehicle.ID, x, y, z);
    }

    private void SendRecallFailure(Client client, int token, string message) =>
        API.SendCustomEventQueued(new List<Client> { client }, RecallCarStateEvent, token, message);

    private void OnPlayerReady(object? sender, Client client)
    {
        if (sharedWaypoint is (int x, int y))
        {
            API.SendCustomEventQueued(new List<Client> { client }, WaypointStateEvent, x, y);
        }
        OnVehicleToolsPlayerReady(client);
        OnWorldStatePlayerReady(client);
        NotifyOutdated(client);
    }

    private void OnPlayerDisconnected(object? sender, Client client)
    {
        lastSpawn.Remove(client);
        lastRepair.Remove(client);
        lastWaypoint.Remove(client);
        lastTeleport.Remove(client);
        lastRecall.Remove(client);
        lastPlaceInvite.Remove(client);
        ForgetDataRequests(client);
        RemovePendingRepairs(client);
        OnVehicleToolsPlayerDisconnected(client);
        RemoveWorldStateClient(client);
        OnGhostTrafficPlayerDisconnected(client);
        OnCharacterPlayerDisconnected(client);
        OnYachtPlayerDisconnected(client);
        OnHomePlayerDisconnected(client);
        if (spawnedVehicles.Remove(client, out ServerVehicle? vehicle))
        {
            if (DeleteOnLeave(client, vehicle, API.Entities.GetVehicleByID(vehicle.ID)))
            {
                vehicle.Delete();
            }

            ForgetVehicleAppearance(vehicle);
        }
    }

    // RageCoop removes the leaving player's own cars before this event. What stays in the
    // pool is a car a partner now drives, and it must not vanish under him.
    internal static bool DeleteOnLeave(Client leaving, ServerVehicle spawned, ServerVehicle? pooled) =>
        ReferenceEquals(pooled, spawned) && ReferenceEquals(spawned.Owner, leaving);

    private static bool HasReadyPlayer(CustomEventReceivedArgs e) =>
        e.Client is not null && e.Client.IsReady && e.Client.Player is not null;

    private static bool IsMapCoordinate(int coordinate) =>
        coordinate is >= -MapCoordinateLimit and <= MapCoordinateLimit;

    private static bool IsValidWorldCoordinate(float coordinate) =>
        float.IsFinite(coordinate) && coordinate is >= -10_000f and <= 10_000f;

    private static bool TryEncodeCoordinate(float coordinate, out int encoded)
    {
        encoded = 0;
        if (!IsValidWorldCoordinate(coordinate))
        {
            return false;
        }

        encoded = (int)Math.Round(coordinate * 100f);
        return true;
    }

    private static bool IsValidRequest(CustomEventReceivedArgs e, out int value)
    {
        value = 0;
        if (!HasReadyPlayer(e))
        {
            return false;
        }

        if (e.Args is not { Length: 1 } || e.Args[0] is not int argument)
        {
            return false;
        }

        value = argument;
        return true;
    }

    private bool IsCurrentReadyClient(Client? client) => IsCurrentReadyClient(client, API.GetAllClients());

    private static bool IsCurrentReadyClient(Client? client, Dictionary<int, Client> clients) => client != null && client.IsReady &&
        client.Player != null && clients.TryGetValue(client.Player.ID, out Client? current) &&
        ReferenceEquals(current, client);

    internal bool CanRequestData(Client client, int kind, long cooldownMs = DataRequestCooldownMs)
    {
        long now = Environment.TickCount64;
        if (lastDataRequests.TryGetValue((client, kind), out long last) && now - last < cooldownMs) return false;
        lastDataRequests[(client, kind)] = now;
        return true;
    }

    internal void ForgetDataRequests(Client client)
    {
        foreach ((Client, int) key in lastDataRequests.Keys.Where(key => ReferenceEquals(key.Client, client)).ToList())
            lastDataRequests.Remove(key);
    }

    private static bool CanRun(Client client, Dictionary<Client, long> requests, long cooldownMs)
    {
        long now = Environment.TickCount64;
        if (requests.TryGetValue(client, out long last) && now - last < cooldownMs)
        {
            return false;
        }

        requests[client] = now;
        return true;
    }

    private static float DistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private void SendStatus(Client client, string message)
    {
        API.SendCustomEventQueued(new List<Client> { client }, StatusEvent, message);
    }

    private List<Client> ReadyClients()
    {
        List<Client> clients = new();
        foreach (Client client in API.GetAllClients().Values)
        {
            if (client.IsReady)
            {
                clients.Add(client);
            }
        }

        return clients;
    }

}
