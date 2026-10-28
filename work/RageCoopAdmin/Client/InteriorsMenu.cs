using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// Icons as in Online: all on the pause map, nearby ones on the minimap.
public sealed partial class AdminMenuScript
{
    private const long InteriorTickMilliseconds = 250;
    private const float DoorDrawDistance = 40f;
    private const float DoorHeightReach = 2.5f;
    private const float InsideLeaveDistance = 150f;
    private static readonly Color DoorColor = Palette.WithAlpha(Palette.Orange, 150);
    private readonly InteriorRuntime _interiors = new InteriorRuntime(OnlineInteriors.All);
    private InteriorPeople? _interiorPeople;
    private readonly DoorTrigger[] _doors = OnlineInteriors.Doors.Select(_ => new DoorTrigger()).ToArray();
    private readonly float?[] _doorGround = new float?[OnlineInteriors.Doors.Length];
    // The exit leads to the door the player came in by, or the nearest one after a teleport.
    private InteriorDoor? _insideDoor;
    private readonly DoorTrigger _exitDoor = new DoorTrigger();
    private Dictionary<string, NativeMenu>? _entranceMenus;
    private OnlineInterior? _inside;
    private float? _exitGround;
    private long _lastInteriorTick;
    // Buildings from GTA Online updates this game has, asked once on the server.
    private bool[]? _buildingAvailable;

    private void BuildInteriorsMenu(NativeMenu teleports)
    {
        if (_pool == null) return;
        _entranceMenus = new Dictionary<string, NativeMenu>();
        NativeMenu interiors = CreateMenu("OG Coop", "Интерьеры");
        UiMenu.Description(interiors, () => UiText.Get("Интерьеры GTA Online. У каждого есть вход с улицы — значок на карте."));
        teleports.AddSubMenu(interiors);
        _pool.Add(interiors);
        string? group = null;
        NativeMenu? groupMenu = null;
        foreach (InteriorBuilding building in OnlineInteriors.Buildings)
        {
            if (building.Group != group || groupMenu == null)
            {
                groupMenu = CreateMenu("OG Coop", building.Group);
                interiors.AddSubMenu(groupMenu);
                _pool.Add(groupMenu);
                group = building.Group;
            }

            foreach (OnlineInterior interior in building.Interiors)
            {
                groupMenu.Add(InteriorItem(interior));
            }

            if (building.Interiors.Length < 2) continue;
            // Walking into a shared lobby offers its apartments, as in Online.
            NativeMenu entrance = CreateMenu("OG Coop", building.Name);
            foreach (OnlineInterior interior in building.Interiors) entrance.Add(InteriorItem(interior));
            _pool.Add(entrance);
            _entranceMenus[building.Key] = entrance;
        }
    }

    private NativeItem InteriorItem(OnlineInterior interior)
    {
        NativeItem item = UiMenu.Item(() => InteriorTitle(interior), () => UiText.Get("Перенестись внутрь. Выход — у метки на месте появления."));
        item.Activated += (_, _) => EnterInterior(interior);
        return item;
    }

    private static string InteriorTitle(OnlineInterior interior) =>
        interior.Building.Interiors.Length == 1
            ? UiText.Get(interior.Building.Name)
            : UiText.Get(interior.Building.Name) + ": " + UiText.Get(interior.Name);

    private void EnterInterior(OnlineInterior interior, InteriorDoor? door = null)
    {
        if (!_specialPlaces.OnlineMapEnabled)
        {
            Notification.PostTicker(UiText.Get("~o~Интерьер:~s~ дождитесь загрузки карты."), false);
            return;
        }

        if (!IsAvailable(interior))
        {
            Notification.PostTicker(UiText.Get("~o~Интерьер:~s~ его нет в вашей версии игры."), false);
            return;
        }

        if (!CanTeleportToPlace(!interior.ByCar, out _)) return;
        _pool?.HideAll();
        long now = NowMilliseconds();
        // Rooms on the island exist only while the island is loaded.
        if (interior.Building.Place >= 0) _specialPlaces.Target(interior.Building.Place, now);
        _interiors.Target(interior.Index, now);
        _inside = interior;
        _insideDoor = door ?? NearestDoor(interior.Building, LocalPlayerPosition());
        _exitGround = null;
        _exitDoor.Disarm();
        BeginTeleport(interior.X, interior.Y, interior.Z, "интерьеру «" + interior.Name + "»", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
        if (_pendingTeleport != null)
        {
            _pendingTeleport.Interior = interior.Index;
            _pendingTeleport.FloorKnown = true;
        }
    }

    // arrived runs at the door once the exit succeeded.
    private bool ExitInterior(OnlineInterior interior, Action? arrived = null)
    {
        if (!CanTeleportToPlace(!interior.ByCar, out _)) return false;
        InteriorDoor door = _insideDoor is InteriorDoor used && used.Building == interior.Building ? used : interior.Building.Doors[0];
        int index = Array.IndexOf(OnlineInteriors.Doors, door);
        _inside = null;
        _insideDoor = null;
        _doors[index].Disarm();
        float ground = _doorGround[index] ?? door.Position.Z;
        BeginTeleport(door.Position.X, door.Position.Y, ground + 1f, "выходу", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
        if (_pendingTeleport == null) return false;
        _pendingTeleport.FloorKnown = true;
        _pendingTeleport.Arrived = arrived;
        return true;
    }

    internal static InteriorDoor NearestDoor(InteriorBuilding building, Vector3? from) =>
        from is Vector3 at ? building.Doors.OrderBy(door => door.Position.DistanceTo2D(at)).First() : building.Doors[0];

    private bool IsAvailable(InteriorBuilding building)
    {
        _buildingAvailable ??= OnlineInteriors.Buildings.Select(item => item.Dlc == null || InteriorNatives.IsDlcPresent(item.Dlc)).ToArray();
        return _buildingAvailable[Array.IndexOf(OnlineInteriors.Buildings, building)];
    }

    private bool IsAvailable(OnlineInterior interior) =>
        IsAvailable(interior.Building) && (interior.Dlc == null || InteriorNatives.IsDlcPresent(interior.Dlc));

    // The player got into our interior without a door: a teleport to a partner, a home spawn, walking in.
    private void NoticeInterior()
    {
        if (_inside != null || _pendingTeleport != null) return;
        int[] found = _interiors.Find(Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, Game.Player.Character.Handle));
        if (found.Length == 0) return;
        // A shared room exits to the building the player came in by.
        int index = found.Where(item => OnlineInteriors.All[item].Building == _insideDoor?.Building).DefaultIfEmpty(found[0]).First();
        _inside = OnlineInteriors.All[index];
        if (_insideDoor?.Building != _inside.Building) _insideDoor = _inside.Building.Doors[0];
        _exitGround = null;
        _exitDoor.Disarm();
    }

    // Markers and the people inside every frame, loading and icons every 250 ms.
    private void TickInteriors()
    {
        if (!_specialPlaces.OnlineMapEnabled) return;
        long now = NowMilliseconds();
        Vector3? local = LocalPlayerPosition();
        if (now - _lastInteriorTick >= InteriorTickMilliseconds)
        {
            _lastInteriorTick = now;
            _interiors.Tick(now, local);
            if (local.HasValue) NoticeInterior();
            TickHome(now);
        }

        _interiorPeople ??= new InteriorPeople(InteriorPedData.Sets, OnlineInteriors.All, message => Logger.Info(message),
            place => place == InteriorPeople.CayoBeachParty && _specialPlaces.IslandShown);
        _interiorPeople.Tick(now);

        if (local is not Vector3 at) return;
        Ped player = Game.Player.Character;
        bool idle = !player.IsDead && _pendingTeleport == null && _pool?.AreAnyVisible != true;
        bool free = idle && !player.IsInVehicle();
        for (int index = 0; index < OnlineInteriors.Doors.Length; index++)
        {
            InteriorDoor door = OnlineInteriors.Doors[index];
            Vector3 place = door.Position;
            float flat = new Vector2(at.X - place.X, at.Y - place.Y).Length();
            if (flat > DoorDrawDistance || !IsAvailable(door.Building))
            {
                _doors[index].Step(float.MaxValue);
                continue;
            }

            _doorGround[index] ??= Ground(place.X, place.Y, place.Z + 2f);
            float ground = _doorGround[index] ?? place.Z;
            DrawDoor(place.X, place.Y, ground);
            float distance = Math.Abs(at.Z - (ground + 1f)) <= DoorHeightReach ? flat : float.MaxValue;
            if (!free)
            {
                _doors[index].Disarm();
            }
            else if (_doors[index].Step(distance))
            {
                OnDoor(door);
            }
        }

        if (_inside is OnlineInterior inside)
        {
            if (at.DistanceTo(new Vector3(inside.X, inside.Y, inside.Z)) > InsideLeaveDistance)
            {
                _inside = null;
                return;
            }

            _exitGround ??= Ground(inside.X, inside.Y, inside.Z + 0.5f);
            float floor = _exitGround ?? inside.Z - 1f;
            // The test track is left by car, so the circle is wider.
            bool driving = inside.ByCar && player.IsInVehicle();
            DrawDoor(inside.X, inside.Y, floor, inside.ByCar ? DoorTrigger.CarScale : 1f);
            float exit = Math.Abs(at.Z - (floor + 1f)) <= DoorHeightReach
                ? new Vector2(at.X - inside.X, at.Y - inside.Y).Length()
                : float.MaxValue;
            if (!(free || (idle && driving)))
            {
                _exitDoor.Disarm();
            }
            else if (_exitDoor.Step(exit, driving ? DoorTrigger.CarScale : 1f))
            {
                ExitInterior(inside);
            }
        }
    }

    private void OnDoor(InteriorDoor door)
    {
        InteriorBuilding building = door.Building;
        if (building.Interiors.Length == 1 || _entranceMenus == null || !_entranceMenus.TryGetValue(building.Key, out NativeMenu? menu))
        {
            EnterInterior(building.Interiors[0], door);
            return;
        }

        _pool?.HideAll();
        menu.SelectedIndex = 0;
        menu.Visible = true;
    }

    private static void DrawDoor(float x, float y, float ground, float scale = 1f) =>
        World.DrawMarker(MarkerType.Cylinder, new Vector3(x, y, ground + 0.02f), Vector3.Zero, Vector3.Zero,
            new Vector3(1.4f * scale, 1.4f * scale, 0.6f), DoorColor);

    // First ground below a point just above the door or the floor: probing from
    // the sky would find a tower's roof.
    private static float? Ground(float x, float y, float from)
    {
        var z = new OutputArgument();
        return Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, x, y, from, z, false, false) ? z.GetResult<float>() : null;
    }

    private void ResetInteriors()
    {
        _interiorPeople?.Reset();
        _interiors.Reset();
        _inside = null;
        _insideDoor = null;
        _exitGround = null;
    }
}
