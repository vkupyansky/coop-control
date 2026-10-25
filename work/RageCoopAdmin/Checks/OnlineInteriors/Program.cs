using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GTA.Math;
using HarmonyLib;
using RageCoopAdmin.Client;

// Online interiors: the real catalog, loading rules and door triggers run here;
// only GTA's map-part and interior calls are replaced by recorded fakes.
internal static class Program
{
    private static readonly List<string> Calls = new List<string>();
    private static readonly HashSet<string> Requested = new HashSet<string>();
    private static bool Ready;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.onlineinteriors");
        try
        {
            foreach (string name in new[] { "RequestIpl", "RemoveIpl" })
                boundary.Patch(typeof(SpecialPlaceNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            foreach (string name in new[] { "InteriorAt", "IsReady", "Activate", "Deactivate", "Tint", "Refresh" })
                boundary.Patch(typeof(InteriorNatives).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!,
                    prefix: new HarmonyMethod(typeof(Program), name));
            CheckCatalog();
            CheckLoading();
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
        int ours = OnlineInteriors.Buildings.Length + 15;
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
        Check(all.Length == 61 && buildings.Length == 50, "three groups: 61 interiors behind 50 doors");
        Check(buildings.Where(b => b.Group == "Склады").Select(b => b.Sprite).OrderBy(s => s).SequenceEqual(new[] { 473, 473, 473, 524 }) &&
            buildings.Where(b => b.Key.StartsWith("ceo_warehouse_", StringComparison.Ordinal)).Select(b => b.Interiors.Single().Name).SequenceEqual(new[] { "Малый склад", "Средний склад", "Большой склад" }) &&
            buildings.Single(b => b.Key == "vehicle_warehouse").Interiors.Length == 2 && buildings.Single(b => b.Key == "eclipse_garage").Sprite == 856,
            "warehouses: one special cargo warehouse of each size and the vehicle warehouse with its workshop below; garages have the garage icon");
        Check(buildings.Where(b => b.Dlc != null).Select(b => b.Key).OrderBy(k => k).SequenceEqual(new[] { "car_wash", "mansion_richman", "mansion_tongva", "mansion_vinewood" }) &&
            buildings.Where(b => b.Dlc != null).All(b => b.Dlc is "mp2025_01" or "mp2025_02"),
            "only the car wash and the mansions need a newer GTA Online update and are hidden without it");
        Check(buildings.Select(b => (Math.Round(b.X), Math.Round(b.Y))).Distinct().Count() == buildings.Length,
            "every building has its own street door");
        Check(all.All(i => i.Sets.All(s => s.Name is not ("Set" or "Clear" or "Load"))),
            "no helper names from the furnishing source slipped in as furnishing sets");
    }

    private static float Flat(InteriorBuilding building, OnlineInterior interior) =>
        new Vector2(building.X - interior.X, building.Y - interior.Y).Length();

    private static void CheckLoading()
    {
        Calls.Clear(); Requested.Clear(); Ready = false;
        var runtime = new InteriorRuntime(OnlineInteriors.All);
        OnlineInterior penthouse = OnlineInteriors.All.Single(i => i.Key == "casino_penthouse");
        OnlineInterior office = OnlineInteriors.All.Single(i => i.Key == "maze_bank_office");
        var city = new Vector3(0f, 0f, 30f);
        runtime.Tick(1_000, city);
        Check(Calls.Count == 0 && !runtime.HasState, "far from every entrance nothing is loaded");
        var door = new Vector3(penthouse.Building.X + 60f, penthouse.Building.Y, penthouse.Building.Z);
        runtime.Tick(2_000, door);
        Check(runtime.IsLoaded(penthouse.Index) && Requested.SetEquals(new[] { "vw_casino_penthouse", "vw_casino_main", "hei_dlc_windows_casino",
            "hei_dlc_casino_aircon", "vw_dlc_casino_door", "hei_dlc_casino_door",
            "h4_clubposter_palmstraxx", "h4_clubposter_moodymann", "h4_clubposter_keinemusik" }),
            "near the casino door its floor, penthouse and building parts load, and the Music Locker beside it");
        Check(!runtime.IsFurnished(penthouse.Index) && !Calls.Any(c => c.StartsWith("Activate", StringComparison.Ordinal)),
            "furnishing waits until the interior has streamed in");
        Ready = true;
        Calls.Clear();
        runtime.Tick(2_250, door);
        Check(runtime.IsFurnished(penthouse.Index) && Calls.Contains("Activate(Set_Pent_Dealer)") && Calls.Contains("Tint(Set_Pent_Tint_Shell, 1)") &&
            Calls.Last() == "Refresh", "once ready the penthouse gets its sets and colours and is refreshed");
        Calls.Clear();
        runtime.Tick(2_500, door);
        Check(!Calls.Any(c => c.StartsWith("Activate", StringComparison.Ordinal) || c == "Refresh"), "furnishing is applied once");
        runtime.Tick(12_400, city);
        Check(runtime.IsLoaded(penthouse.Index), "leaving, the interior waits ten seconds");
        runtime.Tick(12_600, city);
        Check(!runtime.IsLoaded(penthouse.Index) && Calls.Contains("Deactivate(Set_Pent_Dealer)") && !Requested.Contains("vw_casino_penthouse") &&
            !Requested.Contains("vw_casino_main"), "then it unloads with its furnishing and map parts");
        runtime.Target(office.Index, 13_000);
        Check(runtime.IsLoaded(office.Index) && Requested.Contains("ex_dt1_11_office_03c"), "a teleport loads its interior from anywhere");
        var inside = new Vector3(office.X, office.Y, office.Z);
        runtime.Tick(13_500, inside);
        Check(runtime.IsFurnished(office.Index) && Calls.Contains("Activate(office_chairs)"), "inside the office tower the chairs are furnished");
        runtime.Tick(60_000, inside);
        Check(runtime.IsLoaded(office.Index), "the interior stays while the player is in it");
        runtime.Reset();
        Check(!runtime.HasState && Requested.Count == 0, "a disconnect unloads every interior");
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
    }

    private static bool RequestIpl(string name) { Requested.Add(name); return false; }
    private static bool RemoveIpl(string name) { Requested.Remove(name); return false; }
    private static bool InteriorAt(ref int __result) { __result = Ready ? 77 : 0; return false; }
    private static bool IsReady(ref bool __result) { __result = Ready; return false; }
    private static bool Activate(string set) { Calls.Add("Activate(" + set + ")"); return false; }
    private static bool Deactivate(string set) { Calls.Add("Deactivate(" + set + ")"); return false; }
    private static bool Tint(string set, int tint) { Calls.Add("Tint(" + set + ", " + tint + ")"); return false; }
    private static bool Refresh() { Calls.Add("Refresh"); return false; }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL " + message);
        Passes++;
        Console.WriteLine("PASS " + message);
    }
}
