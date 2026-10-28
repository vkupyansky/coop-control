using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA.Math;
using HarmonyLib;
using OGCoop.Shared;
using RageCoopAdmin.Client;

// Online interiors: the real catalog, loading rules and door triggers run here;
// only GTA's map-part and interior calls are replaced by recorded fakes.
internal static class Program
{
    private static readonly List<string> Calls = new List<string>();
    private static readonly HashSet<string> Requested = new HashSet<string>();
    private static readonly List<string> Removed = new List<string>();
    // Map parts the game's own map has on before anything asks for them.
    private static readonly HashSet<string> GameMap = new HashSet<string>();
    private static bool Ready;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.onlineinteriors");
        try
        {
            foreach (string name in new[] { "RequestIpl", "RemoveIpl", "IsIplActive" })
                boundary.Patch(typeof(SpecialPlaceNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            foreach (string name in new[] { "InteriorAt", "IsReady", "Activate", "Deactivate", "Tint", "Refresh", "Open", "Release" })
                boundary.Patch(typeof(InteriorNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            CheckCatalog();
            CheckEntrances();
            CheckNewPlaces();
            CheckLoading();
            CheckMansions();
            CheckGameMap();
            CheckDoors();
            CheckIconBudget();
            Console.WriteLine("Online interior checks passed: " + Passes + ". Catalog, loading and doors are the real code; GTA calls are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    // Story mode adds its own icons only below 150 on the map.
    private static void CheckIconBudget()
    {
        int ours = OnlineInteriors.Doors.Length + 15;
        Check(MapIconBudget.Allowed(100, false, ours) == 40,
            "with 100 of the game's icons only 40 of ours are shown, so the game can still add its shops and Los Santos Customs");
        Check(MapIconBudget.Allowed(150, false, ours) == 0 && MapIconBudget.Allowed(-5, false, 3) == 3,
            "at the game's limit none of ours are shown; a few fit in an empty map");
        Check(MapIconBudget.Allowed(130, true, ours) == ours, "on the open pause map all our icons are shown");
    }

    private static void CheckCatalog()
    {
        InteriorBuilding[] buildings = OnlineInteriors.Buildings;
        OnlineInterior[] all = OnlineInteriors.All;
        Check(buildings.Select(b => b.Key).Distinct().Count() == buildings.Length && all.Select(i => i.Key).Distinct().Count() == all.Length,
            "every building and interior has its own key");
        Check(all.Select((interior, index) => interior.Index == index && interior.Building.Interiors.Contains(interior)).All(ok => ok),
            "every interior knows its building and its place in the list");
        Check(buildings.All(b => OnlineInteriors.Icons.Contains(b.Sprite)) &&
            buildings.Where(b => b.Group == "Офисы").All(b => b.Sprite == 475) && buildings.Single(b => b.Key == "casino").Sprite == 679 &&
            buildings.Where(b => b.Key.StartsWith("clubhouse_", StringComparison.Ordinal)).All(b => b.Sprite == 492) &&
            buildings.Where(b => b.Group == "Агентства").All(b => b.Sprite == 826) && buildings.Where(b => b.Group == "Особняки").All(b => b.Sprite == 948) &&
            buildings.Single(b => b.Key == "bunker").Sprite == 557 && buildings.Single(b => b.Key == "nightclub").Sprite == 614,
            "icons are GTA Online's own: homes, offices, casino, clubhouses, businesses, agencies, mansions");
        Check(buildings.Where(b => b.Group == "Дома").All(b => b.Interiors.All(i => Flat(b, i) < 12f)) &&
            buildings.Where(b => b.Group is "Апартаменты" or "Офисы" or "Агентства").All(b => b.Interiors.Where(i => !i.Key.EndsWith("_garage", StringComparison.Ordinal)).All(i => Flat(b, i) < 40f)) &&
            buildings.Where(b => b.Group == "Офисы").All(b => b.Interiors.Single(i => i.Key.EndsWith("_garage", StringComparison.Ordinal)) is OnlineInterior garage && Flat(b, garage) < 100f),
            "each house, tower apartment and office lies inside the building whose door leads to it; a CEO garage lies under its office block");
        Check(all.All(i => i.Ipls.All(ipl => ipl.Length > 0) && i.Sets.All(set => set.Name.Length > 0)) &&
            buildings.Where(b => b.Group == "Офисы").All(b => b.Interiors[0].Ipls.Length == 1 && b.Interiors[0].Sets.Any(s => s.Name == "office_chairs")),
            "map parts and furnishing are named; CEO offices get one style and their chairs");
        Check(all.Length == 82 && buildings.Length == 56, "82 interiors in 56 buildings");
        string[] groups = buildings.Select(b => b.Group).Where((group, index) => index == 0 || buildings[index - 1].Group != group).ToArray();
        Check(groups.Length == groups.Distinct().Count(), "each menu group is one block in the catalog, so the menu has no second group of the same name");
        Check(buildings.Where(b => b.Group == "Офисы").All(b => b.Interiors.Length == 3 && b.Interiors[2].Key.EndsWith("_workshop", StringComparison.Ordinal) &&
                b.Interiors[2].Ipls.Single().EndsWith("_modgarage", StringComparison.Ordinal)) &&
            buildings.Where(b => b.Group == "Агентства").All(b => b.Interiors.Length == 2 && b.Interiors[1].Name == "Гараж агентства"),
            "each CEO office tower opens its office, garage and workshop; each agency its office and the agencies' shared garage");
        Check(buildings.Where(b => b.Group == "Склады").Select(b => b.Sprite).OrderBy(s => s).SequenceEqual(new[] { 473, 473, 473, 524 }) &&
            buildings.Where(b => b.Key.StartsWith("ceo_warehouse_", StringComparison.Ordinal)).Select(b => b.Interiors.Single().Name).SequenceEqual(new[] { "Малый склад", "Средний склад", "Большой склад" }) &&
            buildings.Single(b => b.Key == "vehicle_warehouse").Interiors.Length == 2 && buildings.Single(b => b.Key == "eclipse_garage").Sprite == 856,
            "warehouses: special cargo warehouses of three sizes and the vehicle warehouse with its workshop below; garages have the garage icon");
        Check(buildings.Where(b => b.Dlc != null).Select(b => b.Key).OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(new[] { "agents_factory", "car_wash", "helitours", "kortz_center", "mansion_richman", "mansion_tongva", "mansion_vinewood", "mckenzie_hangar", "smoke_on_the_water" }) &&
            buildings.Single(b => b.Key == "agents_factory").Dlc == "mp2024_02" && buildings.Single(b => b.Key == "smoke_on_the_water").Dlc == "mp2025_01" &&
            buildings.Single(b => b.Key == "kortz_center").Dlc == "mp2026_01" &&
            buildings.Where(b => b.Dlc != null).All(b => b.Dlc is "mp2024_02" or "mp2025_01" or "mp2025_02" or "mp2026_01"),
            "only the businesses and halls of the 2024-2026 updates, the car wash and the mansions need a newer GTA Online update and are hidden without it");
        Check(all.Where(i => i.Dlc != null).Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(new[] { "mansion_richman_workshop", "mansion_tongva_workshop", "mansion_vinewood_workshop" }) &&
            all.Where(i => i.Dlc != null).All(i => i.Dlc == "mp2026_01" && i.X == 2583.847f && i.Building.Group == "Особняки"),
            "each mansion opens its own house and the one Kortz workshop of the 2026 update");
        Check(buildings.Select(b => (Math.Round(b.X), Math.Round(b.Y))).Distinct().Count() == buildings.Length,
            "every building has its own street door");
        Check(all.All(i => i.Sets.All(s => s.Name is not ("Set" or "Clear" or "Load"))),
            "no helper names from the furnishing source slipped in as furnishing sets");
    }

    private static void CheckEntrances()
    {
        var expected = new Dictionary<string, int>
        {
            ["bunker"] = 11, ["hangar"] = 5, ["facility"] = 9, ["nightclub"] = 10, ["arcade"] = 6, ["auto_shop"] = 5,
            ["bail_office"] = 5, ["salvage_yard"] = 5, ["vehicle_warehouse"] = 10, ["ceo_warehouse_small"] = 6,
            ["ceo_warehouse_medium"] = 8, ["ceo_warehouse_large"] = 8, ["clubhouse_sandy"] = 6, ["clubhouse_paleto"] = 6,
            ["biker_meth"] = 4, ["biker_weed"] = 4, ["biker_coke"] = 4, ["biker_money"] = 4, ["biker_documents"] = 4,
            ["agents_factory"] = 2,
        };
        InteriorBuilding[] buildings = OnlineInteriors.Buildings;
        Check(buildings.All(b => b.Doors.Length == (expected.TryGetValue(b.Key, out int count) ? count : 1)) && OnlineInteriors.Doors.Length == 158,
            "businesses have all their Online doors: 11 bunkers, 5 hangars, 9 facilities, 10 nightclubs, 22 warehouses, 12 clubhouses, the factory's two and others; homes and offices one");
        Check(buildings.All(b => b.Doors[0].Position == new Vector3(b.X, b.Y, b.Z) && b.Doors.Select((door, index) => door.Number == index && door.Building == b).All(ok => ok)) &&
            OnlineInteriors.Doors.Select(door => door.Key).Distinct().Count() == OnlineInteriors.Doors.Length,
            "the first door is the building's own, every door has its own key and number");
        InteriorDoor[] doors = OnlineInteriors.Doors;
        float closest = doors.SelectMany((a, i) => doors.Skip(i + 1).Select(b => new Vector2(a.Position.X - b.Position.X, a.Position.Y - b.Position.Y).Length())).Min();
        Check(closest > 15f, "no two doors share a spot (closest " + closest.ToString("F0") + " m)");
        Check(buildings.Where(b => b.Doors.Length > 1).All(b => b.IconName.Length > 0 && !b.IconName.Contains(",")),
            "a building with doors in many districts is named without a district on the map");
        Check(buildings.Where(b => b.Key.StartsWith("clubhouse_", StringComparison.Ordinal)).Select(b => b.Name).OrderBy(n => n).SequenceEqual(new[] { "Клуб байкеров 1", "Клуб байкеров 2" }),
            "the two clubhouse layouts are told apart by number, as each has six doors");

        Calls.Clear(); Requested.Clear(); Removed.Clear(); Ready = false;
        var runtime = new InteriorRuntime(OnlineInteriors.All, dlc => false);
        OnlineInterior bunker = OnlineInteriors.All.Single(i => i.Key == "bunker_inside");
        InteriorDoor far = bunker.Building.Doors[10];
        runtime.Tick(0, far.Position + new Vector3(30f, 0f, 0f));
        Check(runtime.IsLoaded(bunker.Index) && Requested.Contains(bunker.Ipls[0]), "walking up to any of the eleven bunker doors loads the bunker");
        runtime.Tick(20_000, Far);
        Check(!runtime.IsLoaded(bunker.Index), "and leaving it unloads the bunker as before");

        OnlineInterior[] garages = OnlineInteriors.All.Where(i => i.Name == "Гараж агентства").ToArray();
        OnlineInterior hawick = garages.Single(i => i.Key == "agency_hawick_garage");
        OnlineInterior rockford = garages.Single(i => i.Key == "agency_rockford_garage");
        var inside = new Vector3(hawick.X, hawick.Y, hawick.Z);
        Ready = true;
        Calls.Clear();
        runtime.Target(hawick.Index, 30_000);
        runtime.Tick(30_000, inside);
        Check(runtime.IsLoaded(hawick.Index) && runtime.IsFurnished(hawick.Index) && garages.Where(i => i != hawick).All(i => !runtime.IsLoaded(i.Index)) &&
            runtime.OwnsRoom(hawick.Index) && !runtime.OwnsRoom(rockford.Index),
            "the agencies' shared garage is loaded through the agency the player came by, the others stay off");
        Calls.Clear();
        runtime.Target(rockford.Index, 31_000);
        runtime.Tick(31_000, inside);
        Check(runtime.IsLoaded(rockford.Index) && !runtime.IsLoaded(hawick.Index) &&
            !Calls.Contains("Deactivate(Entity_Set_Workshop_Lights)") && !Calls.Contains("Deactivate(entity_set_tints)") && !Calls.Any(c => c.StartsWith("Release", StringComparison.Ordinal)),
            "coming by another agency hands the garage over and keeps its furnishing");
        runtime.Tick(100_000, Far);
        Check(garages.All(i => !runtime.IsLoaded(i.Index)) && Calls.Contains("Deactivate(Entity_Set_Workshop_Lights)") && Calls.Any(c => c.StartsWith("Release", StringComparison.Ordinal)),
            "when nobody needs it, the garage unloads as any interior");

        OnlineInterior smoke = OnlineInteriors.All.Single(i => i.Key == "smoke_on_the_water_office");
        OnlineInterior heli = OnlineInteriors.All.Single(i => i.Key == "helitours_office");
        var office = new Vector3(smoke.X, smoke.Y, smoke.Z);
        runtime.Target(smoke.Index, 110_000);
        runtime.Tick(110_000, office);
        Calls.Clear();
        runtime.Target(heli.Index, 111_000);
        runtime.Tick(111_000, office);
        Check(runtime.IsFurnished(heli.Index) && !runtime.IsLoaded(smoke.Index) && Calls.Contains("Activate(set_heli)") &&
            Calls.Contains("Deactivate(set_smoke)") && Calls.Contains("Deactivate(set_smoke_tint)") && !Calls.Contains("Deactivate(set_heli)") &&
            !Calls.Any(c => c.StartsWith("Release", StringComparison.Ordinal)),
            "Smoke on the Water and Higgins Helitours share one office, which shows only the style of the business the player came to");
        runtime.Reset();
        Ready = false;
    }

    // The full map: the test track, the island mansion, homes and leaving a room without a door.
    private static void CheckNewPlaces()
    {
        OnlineInterior[] all = OnlineInteriors.All;
        InteriorBuilding meet = OnlineInteriors.Buildings.Single(b => b.Key == "car_meet");
        Check(meet.Interiors.Select(i => i.Key).SequenceEqual(new[] { "car_meet_inside", "car_meet_track" }) &&
            all.Where(i => i.ByCar).Select(i => i.Key).SequenceEqual(new[] { "car_meet_track" }) && meet.Interiors[1].Z - meet.Interiors[0].Z > 45f,
            "the LS Car Meet door opens the meet and its test track above it, the only room one drives into");
        Check(OnlineInteriors.Buildings.Where(b => b.Place >= 0).Select(b => b.Key).SequenceEqual(new[] { "el_rubio" }) &&
            OnlineInteriors.Buildings.Single(b => b.Key == "el_rubio").Place == SpecialPlaces.CayoPerico,
            "El Rubio's mansion is the one building on Cayo Perico, so the island loads to enter it");
        OnlineInterior[] homes = OnlineInteriors.Homes;
        Check(homes.Length == 22 && homes[0].Key == "casino_penthouse" && homes.All(i => HomeCodec.IsKey(i.Key) && i.Key.Length > 0) &&
            !homes.Any(i => i.Key.EndsWith("_workshop", StringComparison.Ordinal)) &&
            homes.Count(i => i.Building.Group == "Апартаменты") == 10 && homes.Count(i => i.Building.Group == "Дома") == 8 &&
            homes.Count(i => i.Building.Group == "Особняки") == 3,
            "a home is one of 22: the casino penthouse, 10 tower apartments, 8 houses and 3 mansions, each with a key the server accepts");

        Calls.Clear(); Requested.Clear(); Removed.Clear(); Ready = true;
        var runtime = new InteriorRuntime(OnlineInteriors.All, dlc => true);
        OnlineInterior workshop = all.Single(i => i.Key == "mansion_tongva_workshop");
        Check(runtime.Find(77).Length == 0 && runtime.Find(0).Length == 0, "no room is ours before it opens");
        runtime.Tick(1_000, new Vector3(workshop.X, workshop.Y, workshop.Z));
        int[] found = runtime.Find(77);
        // The Kortz Center halls are near and open too, and the stand-in game gives every room one id.
        Check(found.Where(index => all[index].Name == "Мастерская").Select(index => all[index].Key).SequenceEqual(new[] { "mansion_vinewood_workshop" }) &&
            runtime.Find(0).Length == 0,
            "standing in the Kortz workshop after a teleport to a partner, the room is found through its first mansion, so the exit is shown");
        InteriorBuilding kortz = OnlineInteriors.Buildings.Single(b => b.Key == "kortz_center");
        Check(kortz.Interiors.Select(i => i.Name).SequenceEqual(new[] { "Выставочный зал", "Хранилище", "Погрузка" }) && kortz.Group == "Ограбления" &&
            OnlineInteriors.Buildings.Single(b => b.Key == "arcade").Interiors.Select(i => i.Key).SequenceEqual(new[] { "arcade_inside", "arcade_basement" }),
            "the Kortz Center door opens the exhibition, the vault and the loading bay; the arcade door also its basement");
        runtime.Reset();
        Ready = false;
    }

    // In the hills above Vinewood, far from every door.
    private static readonly Vector3 Far = new Vector3(-1000f, 1800f, 300f);

    private static float Flat(InteriorBuilding building, OnlineInterior interior) =>
        new Vector2(building.X - interior.X, building.Y - interior.Y).Length();

    private static void CheckLoading()
    {
        Calls.Clear(); Requested.Clear(); Removed.Clear(); Ready = false;
        var runtime = new InteriorRuntime(OnlineInteriors.All, dlc => false);
        OnlineInterior penthouse = OnlineInteriors.All.Single(i => i.Key == "casino_penthouse");
        OnlineInterior office = OnlineInteriors.All.Single(i => i.Key == "maze_bank_office");
        string[] world = OnlineInteriors.All.Where(i => i.Building.Dlc == null).SelectMany(i => i.World).ToArray();
        var city = Far;
        Check(OnlineInteriors.Doors.All(d => new Vector2(d.Position.X - city.X, d.Position.Y - city.Y).Length() > InteriorRuntime.NearEntrance),
            "the test spot is far from every door");
        runtime.Tick(1_000, city);
        Check(Calls.Count == 0 && !runtime.HasState && Requested.SetEquals(world) &&
            new[] { "vw_casino_main", "gr_case10_bunkerclosed", "xm_hatches_terrain", "m24_1_bailoffice_vinewood", "xm3_warehouse_grnd" }.All(world.Contains),
            "far from every entrance no rooms load, but the outsides are on: the casino, the bunker hatches, the ground at the Doomsday hatches, bail offices");
        var door = new Vector3(penthouse.Building.X + 60f, penthouse.Building.Y, penthouse.Building.Z);
        runtime.Tick(2_000, door);
        Check(runtime.IsLoaded(penthouse.Index) && new[] { "vw_casino_penthouse", "h4_clubposter_palmstraxx", "h4_clubposter_moodymann", "h4_clubposter_keinemusik" }
            .All(Requested.Contains), "near the casino door the penthouse loads, and the Music Locker beside it");
        Check(!runtime.IsFurnished(penthouse.Index) && !Calls.Any(c => c.StartsWith("Activate", StringComparison.Ordinal) || c.StartsWith("Open", StringComparison.Ordinal)) &&
            !runtime.IsReady(penthouse.Index), "furnishing and opening wait until the interior exists; a teleport waits for it");
        Ready = true;
        Calls.Clear();
        runtime.Tick(2_250, door);
        Check(Calls.Contains("Open(77)") && Calls.IndexOf("Open(77)") < Calls.IndexOf("Activate(Set_Pent_Dealer)") && runtime.IsReady(penthouse.Index),
            "the interior story mode keeps switched off is opened and pinned before it is furnished");
        Check(runtime.IsFurnished(penthouse.Index) && Calls.Contains("Activate(Set_Pent_Dealer)") && Calls.Contains("Tint(Set_Pent_Tint_Shell, 1)") &&
            Calls.Last() == "Refresh", "once ready the penthouse gets its sets and colours and is refreshed");
        Calls.Clear();
        runtime.Tick(2_500, door);
        Check(!Calls.Any(c => c.StartsWith("Activate", StringComparison.Ordinal) || c == "Refresh" || c.StartsWith("Open", StringComparison.Ordinal)),
            "furnishing and opening are applied once");
        runtime.Tick(12_400, city);
        Check(runtime.IsLoaded(penthouse.Index), "leaving, the interior waits ten seconds");
        runtime.Tick(12_600, city);
        Check(!runtime.IsLoaded(penthouse.Index) && Calls.Contains("Deactivate(Set_Pent_Dealer)") && Calls.Contains("Release(77)") &&
            !Requested.Contains("vw_casino_penthouse") && Requested.Contains("vw_casino_main"),
            "then its rooms unload with their furnishing and are no longer held in memory; the casino building stays");
        runtime.Target(office.Index, 13_000);
        Check(runtime.IsLoaded(office.Index) && Requested.Contains("ex_dt1_11_office_03c"), "a teleport loads its interior from anywhere");
        var inside = new Vector3(office.X, office.Y, office.Z);
        runtime.Tick(13_500, inside);
        Check(runtime.IsFurnished(office.Index) && Calls.Contains("Activate(office_chairs)"), "inside the office tower the chairs are furnished");
        runtime.Tick(60_000, inside);
        Check(runtime.IsLoaded(office.Index), "the interior stays while the player is in it");
        runtime.Reset();
        Check(!runtime.HasState && Requested.SetEquals(world) && !Removed.Any(world.Contains),
            "a disconnect unloads every room and leaves the outsides, which the Online map needs");
    }

    // The 2025 mansions replace three hillsides: without their map parts the
    // Online map has holes there, seen from the roads of the hills.
    private static void CheckMansions()
    {
        Calls.Clear(); Requested.Clear(); Removed.Clear(); Ready = false;
        OnlineInterior[] mansions = OnlineInteriors.All.Where(i => i.Building.Group == "Особняки" && i.Key.EndsWith("_house", StringComparison.Ordinal)).ToArray();
        OnlineInterior vinewood = mansions.Single(i => i.Key == "mansion_vinewood_house");
        Check(mansions.Length == 3 && mansions.All(i => i.Generic != null && i.World.Length > 10 && i.Ipls.Length == 0) &&
            OnlineInteriors.All.Count(i => i.Generic != null) == 3,
            "each of the three mansions has its whole outside and hillside on the map, and a lighter building for afar");
        var runtime = new InteriorRuntime(OnlineInteriors.All, dlc => dlc == "mp2025_02");
        var city = Far;
        runtime.Tick(1_000, city);
        Check(mansions.All(i => i.World.All(Requested.Contains) && Requested.Contains(i.Generic!)) &&
            !Calls.Any(c => c.StartsWith("Open", StringComparison.Ordinal) || c.StartsWith("Activate", StringComparison.Ordinal)),
            "far from every mansion each one's hillside and building are on the map, seen as the lighter building; its rooms are not opened");
        var near = new Vector3(vinewood.X + 50f, vinewood.Y, vinewood.Z);
        runtime.Tick(2_000, near);
        Check(!Requested.Contains(vinewood.Generic!) && mansions.Where(i => i != vinewood).All(i => Requested.Contains(i.Generic!)),
            "close to a mansion its full building is shown, the others stay light");
        runtime.Tick(30_000, city);
        runtime.Reset();
        Check(mansions.All(i => i.World.All(Requested.Contains)) && !Removed.Any(name => mansions.Any(i => i.World.Contains(name))),
            "the mansions and their hillsides stay, also after leaving the server: the map has holes without them");
        Requested.Clear();
        var older = new InteriorRuntime(OnlineInteriors.All, dlc => false);
        older.Tick(50_000, city);
        older.Reset();
        Check(!mansions.Any(i => i.World.Any(Requested.Contains) || Requested.Contains(i.Generic!)),
            "a game without the mansions update keeps its own hills untouched");
    }

    // A map part that was on before the resource asked for it is the game's own.
    private static void CheckGameMap()
    {
        Calls.Clear(); Requested.Clear(); Removed.Clear(); Ready = false;
        OnlineInterior penthouse = OnlineInteriors.All.Single(i => i.Key == "casino_penthouse");
        GameMap.Add("vw_casino_penthouse");
        try
        {
            var runtime = new InteriorRuntime(OnlineInteriors.All, dlc => false);
            var door = new Vector3(penthouse.Building.X, penthouse.Building.Y, penthouse.Building.Z);
            runtime.Tick(1_000, door);
            runtime.Tick(20_000, new Vector3(0f, 0f, 30f));
            runtime.Reset();
            Check(!Removed.Contains("vw_casino_penthouse") && Removed.Contains("h4_clubposter_palmstraxx"),
                "a map part of the game's own map is never taken away; the parts this resource added are");
        }
        finally { GameMap.Clear(); }
    }

    private static void CheckDoors()
    {
        var door = new DoorTrigger();
        Check(!door.Step(0.5f) && !door.Step(0.4f), "arriving inside a door circle does not use it");
        Check(!door.Step(3f) && door.Step(1f) && !door.Step(0.8f), "walking out and back in uses the door once");
        Check(!door.Step(5f) && !door.Step(1.5f) && door.Step(1.1f), "the circle is 1.2 m wide");
        door.Step(10f);
        door.Disarm();
        Check(!door.Step(0.5f), "a disarmed door waits for the player to walk away first");
        Check(!door.Step(float.NaN) && door.Step(1f), "an unknown distance counts as far away");
        var exit = new DoorTrigger();
        Check(!exit.Step(10f, DoorTrigger.CarScale) && exit.Step(3.4f, DoorTrigger.CarScale) && !new DoorTrigger().Step(3.4f),
            "the test track's exit takes a car three times wider than a walker's circle");
    }

    private static bool RequestIpl(string name) { Requested.Add(name); return false; }
    private static bool RemoveIpl(string name) { Requested.Remove(name); Removed.Add(name); return false; }
    private static bool IsIplActive(string name, ref bool __result) { __result = Requested.Contains(name) || GameMap.Contains(name); return false; }
    private static bool InteriorAt(ref int __result) { __result = Ready ? 77 : 0; return false; }
    private static bool IsReady(ref bool __result) { __result = Ready; return false; }
    private static bool Activate(string set) { Calls.Add("Activate(" + set + ")"); return false; }
    private static bool Deactivate(string set) { Calls.Add("Deactivate(" + set + ")"); return false; }
    private static bool Tint(string set, int tint) { Calls.Add("Tint(" + set + ", " + tint + ")"); return false; }
    private static bool Refresh() { Calls.Add("Refresh"); return false; }
    private static bool Open(int interior) { Calls.Add("Open(" + interior + ")"); return false; }
    private static bool Release(int interior) { Calls.Add("Release(" + interior + ")"); return false; }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL " + message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
