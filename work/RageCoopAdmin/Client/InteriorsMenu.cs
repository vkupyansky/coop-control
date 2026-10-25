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

// GTA Online interiors as in Online: an icon at each street entrance (all on the
// pause map, nearby ones on the minimap), a door marker that takes a player on
// foot inside, an exit marker inside, and Teleports → Interiors.
public sealed partial class AdminMenuScript
{
    private const long InteriorTickMilliseconds = 250;
    private const float DoorDrawDistance = 40f;
    private const float DoorHeightReach = 2.5f;
    private const float InsideLeaveDistance = 150f;
    private static readonly Color DoorColor = Color.FromArgb(150, 240, 200, 80);
    private readonly InteriorRuntime _interiors = new InteriorRuntime(OnlineInteriors.All);
    private readonly DoorTrigger[] _doors = OnlineInteriors.Buildings.Select(_ => new DoorTrigger()).ToArray();
    private readonly float?[] _doorGround = new float?[OnlineInteriors.Buildings.Length];
    private readonly DoorTrigger _exitDoor = new DoorTrigger();
    private Dictionary<string, NativeMenu>? _entranceMenus;
    private OnlineInterior? _inside;
    private float? _exitGround;
    private long _lastInteriorTick;
    // Buildings from GTA Online updates this game has; asked once on the server.
    private bool[]? _buildingAvailable;

    private void BuildInteriorsMenu(NativeMenu teleports)
    {
        if (_pool == null) return;
        _entranceMenus = new Dictionary<string, NativeMenu>();
        NativeMenu interiors = CreateMenu("Coop Control", "Интерьеры");
        UiMenu.Description(interiors, () => UiText.Get("Интерьеры GTA Online. У каждого есть вход с улицы — значок на карте."));
        teleports.AddSubMenu(interiors);
        _pool.Add(interiors);
        string? group = null;
        NativeMenu? groupMenu = null;
        foreach (InteriorBuilding building in OnlineInteriors.Buildings)
        {
            if (building.Group != group || groupMenu == null)
            {
                groupMenu = CreateMenu("Coop Control", building.Group);
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
            NativeMenu entrance = CreateMenu("Coop Control", building.Name);
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

    private void EnterInterior(OnlineInterior interior)
    {
        if (!_specialPlaces.OnlineMapEnabled)
        {
            Notification.PostTicker(UiText.Get("~o~Интерьер:~s~ дождитесь загрузки карты."), false);
            return;
        }

        if (!IsAvailable(interior.Building))
        {
            Notification.PostTicker(UiText.Get("~o~Интерьер:~s~ его нет в вашей версии игры."), false);
            return;
        }

        if (!CanTeleportToPlace(true, out _)) return;
        _pool?.HideAll();
        _interiors.Target(interior.Index, NowMilliseconds());
        _inside = interior;
        _exitGround = null;
        _exitDoor.Disarm();
        BeginTeleport(interior.X, interior.Y, interior.Z, "интерьеру «" + interior.Name + "»", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
    }

    private void ExitInterior(OnlineInterior interior)
    {
        if (!CanTeleportToPlace(true, out _)) return;
        InteriorBuilding building = interior.Building;
        int index = Array.IndexOf(OnlineInteriors.Buildings, building);
        _inside = null;
        _doors[index].Disarm();
        float ground = _doorGround[index] ?? building.Z;
        BeginTeleport(building.X, building.Y, ground + 1f, "выходу", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
    }

    private bool IsAvailable(InteriorBuilding building)
    {
        _buildingAvailable ??= OnlineInteriors.Buildings.Select(item => item.Dlc == null || InteriorNatives.IsDlcPresent(item.Dlc)).ToArray();
        return _buildingAvailable[Array.IndexOf(OnlineInteriors.Buildings, building)];
    }

    // Every frame on the server: door and exit markers; loading, icons every 250 ms.
    private void TickInteriors()
    {
        if (!_specialPlaces.OnlineMapEnabled) return;
        long now = NowMilliseconds();
        Vector3? local = LocalPlayerPosition();
        if (now - _lastInteriorTick >= InteriorTickMilliseconds)
        {
            _lastInteriorTick = now;
            _interiors.Tick(now, local);
        }

        if (local is not Vector3 at) return;
        Ped player = Game.Player.Character;
        bool free = !player.IsDead && !player.IsInVehicle() && _pendingTeleport == null && _pool?.AreAnyVisible != true;
        for (int index = 0; index < OnlineInteriors.Buildings.Length; index++)
        {
            InteriorBuilding building = OnlineInteriors.Buildings[index];
            float flat = new Vector2(at.X - building.X, at.Y - building.Y).Length();
            if (flat > DoorDrawDistance || !IsAvailable(building))
            {
                _doors[index].Step(float.MaxValue);
                continue;
            }

            _doorGround[index] ??= Ground(building.X, building.Y, building.Z + 2f);
            float ground = _doorGround[index] ?? building.Z;
            DrawDoor(building.X, building.Y, ground);
            float distance = Math.Abs(at.Z - (ground + 1f)) <= DoorHeightReach ? flat : float.MaxValue;
            if (!free)
            {
                _doors[index].Disarm();
            }
            else if (_doors[index].Step(distance))
            {
                OnDoor(building);
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
            DrawDoor(inside.X, inside.Y, floor);
            float exit = Math.Abs(at.Z - (floor + 1f)) <= DoorHeightReach
                ? new Vector2(at.X - inside.X, at.Y - inside.Y).Length()
                : float.MaxValue;
            if (!free)
            {
                _exitDoor.Disarm();
            }
            else if (_exitDoor.Step(exit))
            {
                ExitInterior(inside);
            }
        }
    }

    private void OnDoor(InteriorBuilding building)
    {
        if (building.Interiors.Length == 1 || _entranceMenus == null || !_entranceMenus.TryGetValue(building.Key, out NativeMenu? menu))
        {
            EnterInterior(building.Interiors[0]);
            return;
        }

        _pool?.HideAll();
        menu.SelectedIndex = 0;
        menu.Visible = true;
    }

    private static void DrawDoor(float x, float y, float ground) =>
        World.DrawMarker(MarkerType.Cylinder, new Vector3(x, y, ground + 0.02f), Vector3.Zero, Vector3.Zero,
            new Vector3(1.4f, 1.4f, 0.6f), DoorColor);

    // First ground below a point just above the door or the floor: probing from
    // the sky would find a tower's roof.
    private static float? Ground(float x, float y, float from)
    {
        var z = new OutputArgument();
        return Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, x, y, from, z, false, false) ? z.GetResult<float>() : null;
    }

    private void ResetInteriors()
    {
        _interiors.Reset();
        _inside = null;
        _exitGround = null;
    }
}
