using System;
using System.Linq;
using OGCoop.Shared;
using GTA.Math;

namespace RageCoopAdmin.Client;

internal enum SpecialPlaceKind
{
    // Cayo Perico: the game's own island switch, roads, water and map.
    Island,
    // North Yankton: map parts plus its own roads and minimap.
    Prologue,
    // Map parts available in story mode.
    Ipl,
    // Map parts registered only with the Online map group.
    OnlineIpl
}

internal sealed class SpecialPlace
{
    internal SpecialPlace(int id, string key, string name, string description, SpecialPlaceKind kind,
        float x, float y, float z, float centerX, float centerY, float radius, bool onFootOnly, params string[] ipls)
    {
        Id = id;
        Key = key;
        Icon = key switch
        {
            "cayo" => 798, // radar_heist_island
            "carrier_east" or "carrier_zancudo" => 890, // radar_aircraft_carrier
            "yacht_bluffs" or "yacht_paleto" or "yacht_port" or "yacht_chumash" or "yacht_north"
                or "yacht_west_sea" or "yacht_south_sea" or "yacht_east" => 455, // radar_yacht
            "cargo_ship" => 911, // radar_cargo_ship
            "airstrip" => 903, // radar_field_hangar
            "red_carpet" => 135, // radar_cinema
            _ => 0 // North Yankton: reached only by teleport, so no map icon
        };
        Name = name;
        Description = description;
        Kind = kind;
        X = x;
        Y = y;
        Z = z;
        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
        OnFootOnly = onFootOnly;
        Ipls = ipls;
    }

    internal int Id { get; }
    internal string Key { get; }
    internal string Name { get; }
    internal string Description { get; }
    internal SpecialPlaceKind Kind { get; }
    internal float X { get; }
    internal float Y { get; }
    internal float Z { get; }
    internal float CenterX { get; }
    internal float CenterY { get; }
    internal float Radius { get; }
    internal bool OnFootOnly { get; }
    internal string[] Ipls { get; }
    // Map icon (blip sprite) shown while the place is loaded (the island: always), 0 for none.
    internal int Icon { get; }

    // Everything stays loaded with the usual map, except two places. North Yankton
    // hangs above the same sea as Cayo Perico and the eastern carrier, so it is
    // loaded only by teleporting there and replaces both while someone is in it.
    internal bool ExplicitOnly => Kind == SpecialPlaceKind.Prologue;
    // The game's island switch hides distant Los Santos (CFX developers: it "will
    // disable LS"). With the island always on, the city went blank in fast flight.
    // So the island loads only when the player is near it or teleports there.
    internal bool LoadsNearby => Kind == SpecialPlaceKind.Island;

    internal bool Contains(Vector3 position, float margin)
    {
        float dx = position.X - CenterX;
        float dy = position.Y - CenterY;
        float reach = Radius + margin;
        return Finite.Is(dx) && Finite.Is(dy) && dx * dx + dy * dy <= reach * reach;
    }
}

// Hidden places of the game world, outdoors only. Positions: map-part extents
// from DurtyFree's gta-v-data-dumps (ipls.json) and Bob74's bob74_ipl (MIT);
// Cayo Perico arrival from RageCoop Race's "Welcome To Cayo Perico" track.
internal static class SpecialPlaces
{
    internal const int CayoPerico = 0;
    internal const int NorthYankton = 1;
    internal const int EastCarrier = 2;
    internal const int YachtEast = 14;

    // The island itself, without the surrounding sea: its roads, water and map.
    internal const float IslandCenterX = 4940f;
    internal const float IslandCenterY = -5130f;

    internal static readonly SpecialPlace[] All =
    {
        new SpecialPlace(CayoPerico, "cayo", "Кайо-Перико", "Остров из ограбления GTA Online: дороги, пляжи, особняк. Можно на машине.",
            SpecialPlaceKind.Island, 4971.345f, -5697.125f, 18.887f, 4700f, -5145f, 2000f, false),
        new SpecialPlace(NorthYankton, "yankton", "Северный Янктон", "Заснеженный городок далеко на востоке. Карта маленькая, по краям невидимая стена. Можно на машине.",
            SpecialPlaceKind.Prologue, 3217.697f, -4834.826f, 111.815f, 4136f, -4953f, 2200f, false,
            "prologue01", "prologue01c", "prologue01d", "prologue01e", "prologue01f", "prologue01g", "prologue01h",
            "prologue01i", "prologue01j", "prologue01k", "prologue01z", "prologue02", "prologue03", "prologue03b",
            "prologue04", "prologue04b", "prologue05", "prologue05b", "prologue06", "prologue06b", "prologue_occl",
            "prologue06_int", "prologuerd", "prologuerdb", "prologue_DistantLights", "prologue_LODLights",
            "DES_ProTree_start", "prologue_m2_door", "prologue03_grv_cov"),
        new SpecialPlace(EastCarrier, "carrier_east", "Авианосец на востоке", "Палуба авианосца далеко в море к востоку от города. Пешком.",
            SpecialPlaceKind.OnlineIpl, 3082.312f, -4717.119f, 15.262f, 3051f, -4668f, 600f, true,
            "hei_carrier", "hei_carrier_int1", "hei_carrier_int2", "hei_carrier_int3", "hei_carrier_int4",
            "hei_carrier_int5", "hei_carrier_int6", "hei_carrier_lodlights"),
        new SpecialPlace(3, "carrier_zancudo", "Авианосец у Занкудо", "Палуба авианосца у западного побережья, рядом с Форт-Занкудо. Пешком.",
            SpecialPlaceKind.OnlineIpl, -3208.03f, 3954.54f, 14.0f, -3234f, 3879f, 600f, true,
            "m24_1_carrier", "m24_1_carrier_int1", "m24_1_carrier_int2", "m24_1_carrier_int3", "m24_1_carrier_int4",
            "m24_1_carrier_int5", "m24_1_carrier_int6", "m24_1_carrier_ladders"),
        new SpecialPlace(4, "yacht_bluffs", "Яхта у Пасифик-Блафс", "Большая яхта у западного побережья. Пешком.",
            SpecialPlaceKind.OnlineIpl, -2043.974f, -1031.582f, 11.981f, -2073f, -1024f, 400f, true,
            "hei_yacht_heist", "hei_yacht_heist_bar", "hei_yacht_heist_bar_lod", "hei_yacht_heist_bedrm",
            "hei_yacht_heist_bedrm_lod", "hei_yacht_heist_bridge", "hei_yacht_heist_bridge_lod", "hei_yacht_heist_enginrm",
            "hei_yacht_heist_enginrm_lod", "hei_yacht_heist_lod", "hei_yacht_heist_lounge", "hei_yacht_heist_lounge_lod",
            "hei_yacht_heist_slod", "hei_yacht_heist_lodlights"),
        new SpecialPlace(5, "yacht_paleto", "Яхта у Палето-Бэй", "Яхта у северного побережья. Пешком.",
            SpecialPlaceKind.OnlineIpl, -1363.724f, 6734.108f, 2.446f, -1418f, 6750f, 400f, true,
            "gr_heist_yacht2", "gr_heist_yacht2_bar", "gr_heist_yacht2_bar_lod", "gr_heist_yacht2_bedrm",
            "gr_heist_yacht2_bedrm_lod", "gr_heist_yacht2_bridge", "gr_heist_yacht2_bridge_lod", "gr_heist_yacht2_enginrm",
            "gr_heist_yacht2_enginrm_lod", "gr_heist_yacht2_lod", "gr_heist_yacht2_lounge", "gr_heist_yacht2_lounge_lod",
            "gr_heist_yacht2_slod"),
        new SpecialPlace(6, "cargo_ship", "Грузовое судно в море", "Большой грузовой корабль к югу от порта. Пешком.",
            SpecialPlaceKind.OnlineIpl, -344.435f, -4062.832f, 17.0f, -363f, -4091f, 500f, true,
            "m23_2_cargoship", "m23_2_cargoship_bridge"),
        new SpecialPlace(7, "airstrip", "Аэродром в холмах", "Скрытая взлётная полоса в холмах Тонгва. Можно на машине.",
            SpecialPlaceKind.OnlineIpl, -2106.98f, 1468.31f, 282.0f, -2025f, 1470f, 600f, false,
            "m24_2_airstrip"),
        new SpecialPlace(8, "red_carpet", "Красная дорожка", "Ковровая дорожка премьеры у кинотеатра в Вайнвуде. Пешком.",
            SpecialPlaceKind.Ipl, 294.7f, 189.6f, 104.3f, 295f, 190f, 300f, true,
            "redcarpet"),
        // Yachts with interiors from GTA Online updates (Cayo Perico parties, The Contract,
        // the summer update). Arrival is the hull centre, the teleport finds the deck below.
        new SpecialPlace(9, "yacht_port", "Яхта у порта", "Яхта с интерьером в море к югу от порта Лос-Сантоса. Пешком.",
            SpecialPlaceKind.OnlineIpl, 50.6f, -3294.4f, 9.0f, 50.6f, -3296f, 300f, true,
            "h4_islandx_yacht_01", "h4_islandx_yacht_01_int"),
        new SpecialPlace(10, "yacht_chumash", "Яхта у Чумаша", "Яхта с интерьером у западного побережья, напротив Чумаша. Пешком.",
            SpecialPlaceKind.OnlineIpl, -3538.6f, 738.5f, 9.0f, -3540f, 738.5f, 300f, true,
            "h4_islandx_yacht_02", "h4_islandx_yacht_02_int"),
        new SpecialPlace(11, "yacht_north", "Яхта на севере", "Яхта с интерьером у северо-западного берега, за Палето-Форест. Пешком.",
            SpecialPlaceKind.OnlineIpl, -1749.0f, 5330.9f, 9.0f, -1751f, 5331f, 300f, true,
            "h4_islandx_yacht_03", "h4_islandx_yacht_03_int"),
        new SpecialPlace(12, "yacht_west_sea", "Яхта в море на западе", "Яхта с интерьером в открытом море к западу от Пасифик-Блафс. Пешком.",
            SpecialPlaceKind.OnlineIpl, -3263.0f, -1584.7f, 9.0f, -3265f, -1584f, 300f, true,
            "sf_yacht_01", "sf_yacht_01_int"),
        new SpecialPlace(13, "yacht_south_sea", "Яхта в море на юге", "Яхта с интерьером в открытом море далеко к югу от города. Пешком.",
            SpecialPlaceKind.OnlineIpl, -848.3f, -4819.6f, 9.0f, -846f, -4818f, 300f, true,
            "sf_yacht_02", "sf_yacht_02_int"),
        new SpecialPlace(YachtEast, "yacht_east", "Яхта у восточного авианосца", "Яхта с интерьером в море рядом с восточным авианосцем. Пешком.",
            SpecialPlaceKind.OnlineIpl, 3616.9f, -4781.0f, 9.0f, 3619f, -4781f, 300f, true,
            "sum_lost_yacht", "sum_lost_yacht_int")
    };

    // North Yankton overlaps Cayo Perico, the eastern carrier and the yacht beside it in the same sea.
    internal static bool Conflict(int first, int second) =>
        (first == NorthYankton && SharesYanktonSea(second)) || (second == NorthYankton && SharesYanktonSea(first));

    private static bool SharesYanktonSea(int place) => place == CayoPerico || place == EastCarrier || place == YachtEast;
}

// Places stay loaded with the usual map while connected, Cayo Perico only near
// it and North Yankton only after teleporting there. Each game loads places
// itself. Game thread only.
internal sealed class SpecialPlaceRuntime
{
    internal const float ExitMargin = 200f;
    internal const long EmptyMilliseconds = 10_000;
    internal const long ConflictMilliseconds = 3_000;
    internal const long TargetMilliseconds = 30_000;
    // Island roads, water and map follow the player over the island itself.
    internal const float IslandShowRadius = 1500f;
    internal const float IslandHideRadius = 1800f;
    // The island's roads replace the city's (no traffic there). Unloading them
    // near the island may crash the game, so they go only this far away.
    internal const float IslandRoadsRadius = 2500f;
    // The island loads this far beyond its area (2.6 km from the area centre,
    // about 1.5 km from its shores) and unloads a second after leaving the wider
    // margin: while it is on, Los Santos stays hidden, so a jet flying back from
    // it (a kilometre in 15 s) must find the city loading well before it arrives.
    internal const float IslandLoadMargin = 600f;
    internal const float IslandKeepMargin = 900f;
    internal const long IslandLeaveMilliseconds = 1_000;
    // The game needs a moment to plan a route after the pause map closes.
    internal const long RouteCheckMilliseconds = 2_000;

    private readonly SpecialPlace[] _places;
    private readonly bool[] _active;
    private readonly long[] _lastWanted;
    private readonly Action<string>? _log;
    private int _target = -1;
    private long _targetUntil;
    private bool _onlineMap;
    private bool _islandShown;
    private bool _prologueShown;
    private bool _islandRoads;
    private bool _settled;
    private bool _onlineProbed;
    // A waypoint over the island: when its route is checked for the log.
    private long _routeCheckAt;
    private bool _routeChecked;

    internal SpecialPlaceRuntime(SpecialPlace[] places, Action<string>? log = null)
    {
        _places = places;
        _active = new bool[places.Length];
        _lastWanted = new long[places.Length];
        _log = log;
    }

    internal bool IsActive(int id) => _active[id];

    // Inside a place away from the usual streets: island, Yankton, ships,
    // airstrip. The red carpet stands in the city.
    internal bool IsInsideSeparatePlace(Vector3 position) =>
        _places.Any(place => place.Kind != SpecialPlaceKind.Ipl && _active[place.Id] && place.Contains(position, 0f));

    // The Online map stays on until GTA restarts (see SpecialPlaceNatives).
    internal bool OnlineMapEnabled => _onlineMap;
    internal bool IslandShown => _islandShown;

    // Online places wait for the switch to the Online map. It reloads the world
    // around the player (models vanish for seconds), so the script performs it
    // behind a faded screen and then calls EnterOnlineMap.
    internal bool NeedsOnlineMap { get; private set; }

    internal bool MapReady => _onlineProbed && !NeedsOnlineMap;
    internal bool IslandRoads => _islandRoads;

    internal bool HasState => _target >= 0 || _islandShown || _prologueShown || _islandRoads || _active.Any(active => active);

    // The island has no count of present map parts and reports -1.
    internal int LoadedParts(int id)
    {
        SpecialPlace place = _places[id];
        if (place.Kind == SpecialPlaceKind.Island || place.Ipls.Length == 0) return -1;
        return place.Ipls.Count(SpecialPlaceNatives.IsIplActive);
    }

    // A teleport (own or accepted invitation) to this place: load it now. The
    // chosen place wins over a conflicting one for the teleport's duration.
    internal void Target(int id, long now)
    {
        _target = id;
        _targetUntil = now + TargetMilliseconds;
        _lastWanted[id] = now;
        // An Online place waits for the map switch behind the loading screen. In view it showed
        // the world reloading, and on a slow PC SHVDN aborted RageCoop.
        if (!_active[id] && (_onlineMap || _places[id].Kind != SpecialPlaceKind.OnlineIpl)) Activate(id);
    }

    internal void CancelTarget(int id)
    {
        if (_target == id) _target = -1;
    }

    internal void EnterOnlineMap()
    {
        if (_onlineMap) return;
        SpecialPlaceNatives.EnterOnlineMap();
        _onlineMap = true;
        NeedsOnlineMap = false;
        _log?.Invoke("OG Coop: special places switched to the Online map.");
    }

    internal void Tick(long now, Vector3? local)
    {
        if (!_settled && local.HasValue) Settle(local.Value);
        if (_target >= 0 && now > _targetUntil) _target = -1;
        if (!_onlineProbed) ProbeOnlineMap();
        var raw = new bool[_places.Length];
        for (int i = 0; i < _places.Length; i++)
        {
            SpecialPlace place = _places[i];
            raw[i] = _target == i || Wanted(i, local);
        }

        for (int i = 0; i < _places.Length; i++)
        {
            bool blocked = false;
            for (int other = 0; other < _places.Length; other++)
            {
                if (other == i || !SpecialPlaces.Conflict(i, other)) continue;
                // The chosen place wins, otherwise the one already loaded keeps it.
                if (_target == other || (_target != i && _active[other] && raw[other])) blocked = true;
            }

            if (raw[i] && !blocked)
            {
                _lastWanted[i] = now;
                if (!_active[i] && (_onlineMap || _places[i].Kind != SpecialPlaceKind.OnlineIpl)) Activate(i);
            }
            else if (_active[i] && now - _lastWanted[i] >= (blocked ? ConflictMilliseconds :
                         _places[i].LoadsNearby ? IslandLeaveMilliseconds : EmptyMilliseconds))
            {
                Deactivate(i);
            }
        }

        float island = local.HasValue ? IslandDistance(local.Value) : float.MaxValue;
        ShowIsland(_active[SpecialPlaces.CayoPerico] && island <= (_islandShown ? IslandHideRadius : IslandShowRadius));
        if (_islandRoads && !_islandShown && island > IslandRoadsRadius)
        {
            SpecialPlaceNatives.SetIslandRoads(false);
            _islandRoads = false;
        }

        ShowPrologue(_active[SpecialPlaces.NorthYankton] && local.HasValue &&
            _places[SpecialPlaces.NorthYankton].Contains(local.Value, _prologueShown ? ExitMargin : 0f));
    }

    private bool Wanted(int id, Vector3? local)
    {
        SpecialPlace place = _places[id];
        if (place.ExplicitOnly) return _active[id] && local.HasValue && place.Contains(local.Value, ExitMargin);
        if (place.LoadsNearby) return local.HasValue && place.Contains(local.Value, _active[id] ? IslandKeepMargin : IslandLoadMargin);
        return true;
    }

    // The island minimap is drawn like an interior and must be set every frame.
    // GTA draws no GPS route on an interior minimap, so while a waypoint is set
    // the minimap stays ordinary. The pause map always shows the island.
    internal void Frame(long now)
    {
        if (!_islandShown) return;
        bool waypoint = SpecialPlaceNatives.IsWaypointActive();
        bool paused = SpecialPlaceNatives.IsPauseMenuActive();
        if (!waypoint || paused) SpecialPlaceNatives.DrawIslandRadarThisFrame();
        if (!waypoint)
        {
            _routeCheckAt = 0;
            _routeChecked = false;
            return;
        }

        if (paused || _routeChecked) return;
        if (_routeCheckAt == 0)
        {
            _routeCheckAt = now + RouteCheckMilliseconds;
            return;
        }

        if (now < _routeCheckAt) return;
        _routeChecked = true;
        _log?.Invoke(SpecialPlaceNatives.IsRouteFound()
            ? "OG Coop: island waypoint: GPS route " + SpecialPlaceNatives.RouteLength() + " m."
            : "OG Coop: island waypoint: the game found no GPS route" + (_islandRoads ? "." : "; island roads off."));
    }

    // Disconnect or resource stop.
    internal void Reset(Vector3? local)
    {
        _target = -1;
        for (int i = 0; i < _places.Length; i++)
        {
            if (_active[i]) Deactivate(i);
        }

        ShowIsland(false);
        if (_islandRoads && (!local.HasValue || IslandDistance(local.Value) > IslandRoadsRadius))
        {
            SpecialPlaceNatives.SetIslandRoads(false);
            _islandRoads = false;
        }

        ShowPrologue(false);
    }

    // A previous session in this GTA process may already have switched to the
    // Online map: then its parts load at once and no switch is needed.
    private void ProbeOnlineMap()
    {
        _onlineProbed = true;
        if (_onlineMap) return;
        SpecialPlace? online = _places.FirstOrDefault(place => place.Kind == SpecialPlaceKind.OnlineIpl);
        if (online == null) return;
        string probe = online.Ipls[0];
        SpecialPlaceNatives.RequestIpl(probe);
        if (SpecialPlaceNatives.IsIplActive(probe))
        {
            _onlineMap = true;
            return;
        }

        NeedsOnlineMap = true;
    }

    // A previous session (a crash, a disconnect near the island) may have left
    // the island's roads, water or map on: the first position away resets them.
    private void Settle(Vector3 local)
    {
        _settled = true;
        if (IslandDistance(local) <= IslandRoadsRadius)
        {
            _islandRoads = true;
            return;
        }

        SpecialPlaceNatives.SetIslandRoads(false);
        if (SpecialPlaceNatives.IsIslandWater()) SpecialPlaceNatives.SetIslandWater(false);
        SpecialPlaceNatives.SetIslandMap(false);
    }

    private static float IslandDistance(Vector3 position)
    {
        float dx = position.X - SpecialPlaces.IslandCenterX;
        float dy = position.Y - SpecialPlaces.IslandCenterY;
        return Finite.Is(dx) && Finite.Is(dy) ? (float)Math.Sqrt(dx * dx + dy * dy) : float.MaxValue;
    }

    private void Activate(int id)
    {
        SpecialPlace place = _places[id];
        _active[id] = true;
        switch (place.Kind)
        {
            case SpecialPlaceKind.Island:
                SpecialPlaceNatives.SetIslandEnabled(true);
                break;
            case SpecialPlaceKind.Prologue:
                RequestAll(place);
                SpecialPlaceNatives.SetPrologueRoads(true);
                break;
            case SpecialPlaceKind.OnlineIpl:
                RequestAll(place);
                break;
            default:
                RequestAll(place);
                break;
        }

        int loaded = LoadedParts(id);
        _log?.Invoke("OG Coop: special place " + place.Key + " loaded" +
            (loaded < 0 ? "." : ": " + loaded + "/" + place.Ipls.Length + " map parts" + (_onlineMap ? " (Online map)." : ".")));
    }

    private void Deactivate(int id)
    {
        SpecialPlace place = _places[id];
        _active[id] = false;
        if (_target == id) _target = -1;
        switch (place.Kind)
        {
            case SpecialPlaceKind.Island:
                SpecialPlaceNatives.SetIslandEnabled(false);
                break;
            case SpecialPlaceKind.Prologue:
                foreach (string ipl in place.Ipls) SpecialPlaceNatives.RemoveIpl(ipl);
                SpecialPlaceNatives.SetPrologueRoads(false);
                break;
            default:
                foreach (string ipl in place.Ipls) SpecialPlaceNatives.RemoveIpl(ipl);
                break;
        }

        _log?.Invoke("OG Coop: special place " + place.Key + " unloaded.");
    }

    private static void RequestAll(SpecialPlace place)
    {
        foreach (string ipl in place.Ipls) SpecialPlaceNatives.RequestIpl(ipl);
    }

    // Over the island: its water, map and roads. Roads are switched off
    // separately, only far from the island.
    private void ShowIsland(bool show)
    {
        if (show == _islandShown) return;
        _islandShown = show;
        _routeCheckAt = 0;
        _routeChecked = false;
        SpecialPlaceNatives.SetIslandWater(show);
        SpecialPlaceNatives.SetIslandMap(show);
        if (show && !_islandRoads)
        {
            SpecialPlaceNatives.SetIslandRoads(true);
            _islandRoads = true;
        }
    }

    private void ShowPrologue(bool show)
    {
        if (show == _prologueShown) return;
        _prologueShown = show;
        SpecialPlaceNatives.SetPrologueMap(show);
    }
}
